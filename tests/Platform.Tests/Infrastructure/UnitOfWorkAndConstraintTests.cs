using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Platform.Tests.TestSupport;
using Platform.Web.Data;
using Platform.Web.Modules.BusinessProfile.Domain;
using Platform.Web.Modules.Stock.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Tests.Infrastructure;

/// <summary>Unit of work, concurrency token, dan constraint database (lapisan pengaman terakhir).</summary>
public sealed class UnitOfWorkAndConstraintTests : IDisposable
{
    private readonly V3World _w = new();

    public void Dispose() => _w.Dispose();

    [Fact]
    public async Task UoW_commit_pada_sukses_rollback_pada_hasil_gagal_dan_bergabung_pada_pemanggilan_bersarang()
    {
        var shop = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung UoW");
        var pid = await _w.CreateRetailProductAsync(shop, "Teh", 10, 3_000m);
        var s = _w.Open(shop);

        // sukses -> tersimpan
        var ok = await s.Uow.ExecuteAsync<int>(async ct =>
        {
            Assert.True((await s.Inventory.StockOutAsync(new Platform.Web.Modules.Stock.Contracts.StockOutRequest(pid, 2, MovementType.CorrectionOut), ct)).Succeeded);
            return Result<int>.Ok(1);
        });
        Assert.True(ok.Succeeded);
        Assert.Equal(8, (await _w.Open(_w.Admin()).Db.Inventories.AsNoTracking().SingleAsync()).QtyBase);

        // bersarang: bagian dalam berhasil, bagian luar gagal -> SEMUA dibatalkan (tidak ada transaksi bersarang)
        var nested = await s.Uow.ExecuteAsync<int>(async ct =>
        {
            Assert.True(s.Uow.InTransaction);
            Assert.True((await s.Inventory.StockOutAsync(new Platform.Web.Modules.Stock.Contracts.StockOutRequest(pid, 3, MovementType.CorrectionOut), ct)).Succeeded);
            return Result<int>.Fail(ErrorKind.Conflict, "luar gagal");
        });
        Assert.False(nested.Succeeded);
        Assert.False(s.Uow.InTransaction);
        Assert.Equal(8, (await _w.Open(_w.Admin()).Db.Inventories.AsNoTracking().SingleAsync()).QtyBase);
    }

    [Fact]
    public async Task UoW_kunci_tulis_yang_ditahan_lama_menghasilkan_Conflict_yang_jelas_bukan_exception_mentah()
    {
        var path = Path.Combine(Path.GetTempPath(), "uow-lock-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={path};Default Timeout=1").Options;
            using (var init = new AppDbContext(options, new FakeBusinessContext()))
            {
                init.Database.EnsureCreated();
            }

            var ctx = new FakeBusinessContext();
            ctx.AsAdmin();
            using var holder = new ModuleServices(options, ctx, TimeProvider.System);
            using var waiter = new ModuleServices(options, ctx, TimeProvider.System);

            await using var tx = await holder.Db.Database.BeginTransactionAsync(); // penulis lain menahan kunci tulis
            var blocked = await waiter.Uow.ExecuteAsync<int>(ct => Task.FromResult(Result<int>.Ok(1)));
            Assert.False(blocked.Succeeded);
            Assert.Equal(ErrorKind.Conflict, blocked.Kind);
            Assert.Contains("tidak ada perubahan yang tersimpan", blocked.Error!, StringComparison.OrdinalIgnoreCase);

            await tx.RollbackAsync(); // setelah dilepas, unit kerja berjalan normal
            Assert.True((await waiter.Uow.ExecuteAsync<int>(ct => Task.FromResult(Result<int>.Ok(7)))).Succeeded);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); File.Delete(path + "-wal"); File.Delete(path + "-shm"); } catch { /* abaikan */ }
        }
    }

    [Fact]
    public async Task Concurrency_token_menolak_pembaruan_dengan_data_usang_secara_eksplisit()
    {
        var shop = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung Token");
        await _w.CreateRetailProductAsync(shop, "Gula", 10, 14_000m);

        using var a = _w.Open(_w.Admin());
        using var b = _w.Open(_w.Admin());
        var invA = await a.Db.Inventories.SingleAsync();
        var invB = await b.Db.Inventories.SingleAsync();
        Assert.Equal(invA.Version, invB.Version);

        invA.SellingPriceBase = 15_000m;
        await a.Db.SaveChangesAsync();
        Assert.Equal(invB.Version + 1, invA.Version); // Version naik otomatis

        invB.SellingPriceBase = 16_000m;               // b masih memegang versi lama
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => b.Db.SaveChangesAsync());
        Assert.Equal(15_000m, (await _w.Open(_w.Admin()).Db.Inventories.AsNoTracking().SingleAsync()).SellingPriceBase); // tidak tertimpa diam-diam
    }

    [Fact]
    public async Task Constraint_database_menolak_stok_negatif_dan_reservasi_berlebih_walau_aplikasi_salah()
    {
        var shop = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung Constraint");
        await _w.CreateRetailProductAsync(shop, "Susu", 10, 8_000m, expiry: _w.Today.AddDays(30));
        using var s = _w.Open(_w.Admin());

        var ex1 = await Assert.ThrowsAsync<SqliteException>(() => s.Db.Database.ExecuteSqlRawAsync("UPDATE Inventories SET QtyBase = -1"));
        Assert.Contains("CHECK", ex1.Message, StringComparison.OrdinalIgnoreCase);
        var ex2 = await Assert.ThrowsAsync<SqliteException>(() => s.Db.Database.ExecuteSqlRawAsync("UPDATE Inventories SET ReservedBase = QtyBase + 1"));
        Assert.Contains("CHECK", ex2.Message, StringComparison.OrdinalIgnoreCase);
        var ex3 = await Assert.ThrowsAsync<SqliteException>(() => s.Db.Database.ExecuteSqlRawAsync("UPDATE StockBatches SET QtyRemainingBase = -5"));
        Assert.Contains("CHECK", ex3.Message, StringComparison.OrdinalIgnoreCase);
        var ex4 = await Assert.ThrowsAsync<SqliteException>(() => s.Db.Database.ExecuteSqlRawAsync("UPDATE StockBatches SET ReservedBase = QtyRemainingBase + 1"));
        Assert.Contains("CHECK", ex4.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(10, (await s.Db.Inventories.AsNoTracking().SingleAsync()).QtyBase);
    }
}
