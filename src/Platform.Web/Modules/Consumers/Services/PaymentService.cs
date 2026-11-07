using Microsoft.EntityFrameworkCore;
using Platform.Web.Data;
using Platform.Web.Modules.Audit.Contracts;
using Platform.Web.Modules.Consumers.Contracts;
using Platform.Web.Modules.Consumers.Domain;
using Platform.Web.Modules.Orders.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Consumers.Services;

/// <summary>
/// Buku pembayaran demo. Semua perubahan status pembayaran lewat sini agar aturannya satu:
/// Pending -> Success | Failed | Cancelled | Refunded, dan paling banyak satu Success per pesanan
/// (selain dijaga di sini, dijamin unique index berfilter di database).
/// TIDAK ADA uang nyata: tidak ada penyedia pembayaran, tidak ada data kartu/rekening.
/// </summary>
internal static class PaymentLedger
{
    public static Payment Open(AppDbContext db, ConsumerOrder order, DateTime now)
    {
        var payment = new Payment
        {
            Reference = "PAY-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), // sementara; diganti PAY-<id> setelah tersimpan
            ConsumerOrderId = order.Id,
            ConsumerUserId = order.ConsumerUserId,
            SellerBusinessId = order.SellerBusinessId,
            Method = order.PaymentMethod,
            Status = PaymentStatus.Pending,
            Amount = order.TotalAmount,
            CreatedAt = now
        };
        db.Payments.Add(payment);
        return payment;
    }

    public static void FinalizeReference(Payment payment) => payment.Reference = $"PAY-{payment.Id:D6}";

    /// <summary>Pembayaran tunai terkumpul saat barang diambil/diterima: satu catatan Pending menjadi Success.</summary>
    public static void SettleCash(ConsumerOrder order, IReadOnlyList<Payment> payments, DateTime now)
    {
        if (order.PaymentStatus == OrderPaymentStatus.Paid || payments.Any(p => p.Status == PaymentStatus.Success))
        {
            return; // sudah lunas: tidak dicatat dua kali
        }

        var pending = payments.LastOrDefault(p => p.Status == PaymentStatus.Pending);
        if (pending is null)
        {
            return;
        }

        pending.Status = PaymentStatus.Success;
        pending.CompletedAt = now;
        order.PaymentStatus = OrderPaymentStatus.Paid;
        order.PaidAt = now;
    }

    /// <summary>Pesanan dibatalkan/ditolak: yang masih Pending dibatalkan, yang sudah Success dikembalikan (simulasi).</summary>
    public static void CancelOrRefund(ConsumerOrder order, IReadOnlyList<Payment> payments, DateTime now)
    {
        var refunded = false;
        foreach (var payment in payments)
        {
            if (payment.Status == PaymentStatus.Pending)
            {
                payment.Status = PaymentStatus.Cancelled;
                payment.CompletedAt = now;
            }
            else if (payment.Status == PaymentStatus.Success)
            {
                payment.Status = PaymentStatus.Refunded;
                payment.CompletedAt = now;
                refunded = true;
            }
        }

        order.PaymentStatus = refunded || order.PaymentStatus == OrderPaymentStatus.Paid
            ? OrderPaymentStatus.Refunded
            : OrderPaymentStatus.Cancelled;
    }

    public static PaymentDto ToDto(Payment p) =>
        new(p.Id, p.Reference, p.Method, p.Status, p.Amount, p.CreatedAt, p.CompletedAt, p.FailureReason);
}

/// <inheritdoc />
public sealed class PaymentService : IPaymentService
{
    private readonly AppDbContext _db;
    private readonly IBusinessContext _context;
    private readonly IAuditLogService _audit;
    private readonly TimeProvider _time;
    private readonly IUnitOfWork _uow;

    public PaymentService(AppDbContext db, IBusinessContext context, IAuditLogService audit, TimeProvider time, IUnitOfWork uow)
    {
        _db = db;
        _context = context;
        _audit = audit;
        _time = time;
        _uow = uow;
    }

    public async Task<Result<PaymentDto>> PayDemoAsync(
        int consumerOrderId, bool simulateFailure, string? idempotencyKey, CancellationToken ct = default)
    {
        if (!_context.IsAuthenticated || _context.Role != RoleNames.Consumer || _context.UserId is not int userId)
        {
            return Result<PaymentDto>.Fail(ErrorKind.Forbidden, "Hanya pembeli pemilik pesanan yang dapat membayar.");
        }

        var key = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
        if (key is { Length: > 64 })
        {
            key = key[..64];
        }

        return await _uow.ExecuteAsync<PaymentDto>(async token =>
        {
            var order = await _db.ConsumerOrders.FirstOrDefaultAsync(o => o.Id == consumerOrderId, token);
            if (order is null || order.ConsumerUserId != userId)
            {
                return Result<PaymentDto>.Fail(ErrorKind.NotFound, "Pesanan tidak ditemukan.");
            }

            var payments = await _db.Payments.Where(p => p.ConsumerOrderId == consumerOrderId).OrderBy(p => p.Id).ToListAsync(token);

            // 1) Operasi yang sama dikirim ulang (klik ganda, refresh, retry setelah respons hilang): kembalikan hasil sebelumnya.
            if (key is not null && payments.FirstOrDefault(p => p.IdempotencyKey == key) is { } same)
            {
                return Result<PaymentDto>.Ok(PaymentLedger.ToDto(same));
            }

            // 2) Pesanan sudah lunas (mis. dibayar dari tab lain dengan kunci berbeda): tidak ada pembayaran sukses kedua.
            if (payments.FirstOrDefault(p => p.Status == PaymentStatus.Success) is { } paid)
            {
                return Result<PaymentDto>.Ok(PaymentLedger.ToDto(paid));
            }

            if (order.PaymentMethod != PaymentMethod.DemoSimulation)
            {
                return Result<PaymentDto>.Fail(
                    ErrorKind.Validation, "Pesanan ini memakai pembayaran tunai; pembayaran dicatat saat barang diambil/diterima.");
            }

            if (order.Status is OrderStatus.Rejected or OrderStatus.Cancelled)
            {
                return Result<PaymentDto>.Fail(ErrorKind.Conflict, "Pesanan sudah dibatalkan atau ditolak; pembayaran tidak dapat dilakukan.");
            }

            if (order.Status == OrderStatus.Completed)
            {
                return Result<PaymentDto>.Fail(ErrorKind.Conflict, "Pesanan sudah selesai.");
            }

            var now = _time.GetUtcNow().UtcDateTime;
            var row = payments.LastOrDefault(p => p.Status == PaymentStatus.Pending);
            if (row is null)
            {
                // Percobaan baru setelah percobaan sebelumnya gagal.
                row = PaymentLedger.Open(_db, order, now);
                await _db.SaveChangesAsync(token);
                PaymentLedger.FinalizeReference(row);
            }

            row.IdempotencyKey = key;
            row.CompletedAt = now;
            order.UpdatedAt = now;
            if (simulateFailure)
            {
                row.Status = PaymentStatus.Failed;
                row.FailureReason = "Simulasi: pembayaran ditolak";
                order.PaymentStatus = OrderPaymentStatus.Failed;
            }
            else
            {
                row.Status = PaymentStatus.Success;
                order.PaymentStatus = OrderPaymentStatus.Paid;
                order.PaidAt = now;
            }

            _audit.Record(
                simulateFailure ? "PAYMENT_DEMO_FAILED" : "PAYMENT_DEMO_SUCCESS", "Payment", row.Id.ToString(),
                $"{row.Reference}; pesanan={order.Number}; jumlah={row.Amount}");
            await _db.SaveChangesAsync(token);
            return Result<PaymentDto>.Ok(PaymentLedger.ToDto(row));
        }, ct);
    }
}
