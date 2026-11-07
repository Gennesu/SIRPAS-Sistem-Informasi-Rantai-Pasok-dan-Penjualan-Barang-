using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MySqlConnector;
using Platform.Web.SharedKernel;

namespace Platform.Web.Data;

/// <summary>Implementasi IUnitOfWork di atas AppDbContext (lihat IUnitOfWork untuk aturan lengkap).</summary>
public sealed class EfUnitOfWork : IUnitOfWork
{
    private const int MaxAttempts = 4;

    private readonly AppDbContext _db;
    private readonly ILogger<EfUnitOfWork> _logger;

    public EfUnitOfWork(AppDbContext db, ILogger<EfUnitOfWork> logger)
    {
        _db = db;
        _logger = logger;
    }

    public bool InTransaction => _db.Database.CurrentTransaction is not null;

    public async Task<Result<T>> ExecuteAsync<T>(Func<CancellationToken, Task<Result<T>>> work, CancellationToken ct = default)
    {
        if (InTransaction)
        {
            return await work(ct); // bergabung ke transaksi pemanggil terluar
        }

        Result<T>? captured = null;
        var outcome = await RunAsync(
            async token =>
            {
                captured = await work(token);
                return captured.Succeeded;
            },
            ct);

        if (outcome.Failure is not null)
        {
            return Result<T>.Fail(outcome.Failure.Kind, outcome.Failure.Error);
        }

        return captured ?? Result<T>.Fail(ErrorKind.Unexpected, "Operasi gagal.");
    }

    public async Task<Result> ExecuteAsync(Func<CancellationToken, Task<Result>> work, CancellationToken ct = default)
    {
        if (InTransaction)
        {
            return await work(ct);
        }

        Result? captured = null;
        var outcome = await RunAsync(
            async token =>
            {
                captured = await work(token);
                return captured.Succeeded;
            },
            ct);

        if (outcome.Failure is not null)
        {
            return Result.Fail(outcome.Failure.Kind, outcome.Failure.Error);
        }

        return captured ?? Result.Fail(ErrorKind.Unexpected, "Operasi gagal.");
    }

    private sealed record Failure(ErrorKind Kind, string Error);

    private sealed record Outcome(Failure? Failure);

    private async Task<Outcome> RunAsync(Func<CancellationToken, Task<bool>> work, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            // Mulai dari keadaan bersih: semua pembacaan di dalam unit kerja diambil langsung dari database.
            if (!_db.ChangeTracker.HasChanges())
            {
                _db.ChangeTracker.Clear();
            }

            IDbContextTransaction? transaction = null;
            try
            {
                // BEGIN termasuk di dalam try agar kegagalan transaksi dari provider database dapat diulang seperti benturan lainnya.
                transaction = await _db.Database.BeginTransactionAsync(ct);

                var succeeded = await work(ct);
                if (!succeeded)
                {
                    await transaction.RollbackAsync(ct);
                    _db.ChangeTracker.Clear();
                    return new Outcome(null);
                }

                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return new Outcome(null);
            }
            catch (OperationCanceledException)
            {
                await SafeRollbackAsync(transaction);
                _db.ChangeTracker.Clear();
                throw;
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                await SafeRollbackAsync(transaction);
                _db.ChangeTracker.Clear();

                if (attempt >= MaxAttempts)
                {
                    _logger.LogWarning(ex, "Unit kerja gagal setelah {Attempts} percobaan (benturan data).", attempt);
                    return new Outcome(new Failure(
                        ErrorKind.Conflict,
                        "Data sedang diubah pengguna lain. Tidak ada perubahan yang tersimpan; silakan coba lagi."));
                }

                await Task.Delay(TimeSpan.FromMilliseconds(15 * attempt + Random.Shared.Next(0, 25)), ct);
            }
            catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
            {
                await SafeRollbackAsync(transaction);
                _db.ChangeTracker.Clear();
                _logger.LogError(ex, "Unit kerja dibatalkan karena kesalahan database.");
                return new Outcome(new Failure(
                    ErrorKind.Unexpected,
                    "Perubahan tidak dapat disimpan saat ini. Tidak ada data yang berubah; silakan coba lagi."));
            }
            catch
            {
                // Exception tak terduga: pastikan rollback penuh dan keadaan bersih, lalu teruskan (tidak ditelan).
                await SafeRollbackAsync(transaction);
                _db.ChangeTracker.Clear();
                throw;
            }
            finally
            {
                if (transaction is not null)
                {
                    await transaction.DisposeAsync();
                }
            }
        }
    }

    private static async Task SafeRollbackAsync(IDbContextTransaction? transaction)
    {
        if (transaction is null)
        {
            return;
        }

        try
        {
            await transaction.RollbackAsync();
        }
        catch
        {
            // transaksi sudah berakhir: tidak ada yang perlu dibatalkan
        }
    }

    // Benturan yang aman diulang: concurrency token, SQLite sibuk/terkunci, MySQL lock/deadlock/unique key, PostgreSQL serialization/deadlock/unique key, atau unique key yang
    // baru saja diisi permintaan lain (percobaan berikutnya menemukan barisnya dan memutar ulang hasilnya).
    internal static bool IsTransient(Exception ex)
    {
        if (ex is DbUpdateConcurrencyException)
        {
            return true;
        }

        for (Exception? inner = ex; inner is not null; inner = inner.InnerException)
        {
            switch (inner)
            {
                case SqliteException { SqliteErrorCode: 5 or 6 }:                  // SQLITE_BUSY / SQLITE_LOCKED
                case SqliteException { SqliteExtendedErrorCode: 2067 or 1555 }:    // UNIQUE / PRIMARY KEY violation
                case MySqlException { Number: 1062 or 1205 or 1213 }:               // duplicate key / lock timeout / deadlock
                case Npgsql.PostgresException { SqlState: "40001" or "40P01" or "23505" }:
                    return true;
            }
        }

        return false;
    }
}
