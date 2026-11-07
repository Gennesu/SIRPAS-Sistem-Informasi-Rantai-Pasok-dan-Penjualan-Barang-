using Microsoft.EntityFrameworkCore;
using Platform.Web.Data;
using Platform.Web.Modules.Audit.Contracts;
using Platform.Web.Modules.BusinessProfile.Domain;
using Platform.Web.Modules.Consumers.Contracts;
using Platform.Web.Modules.Consumers.Domain;
using Platform.Web.Modules.Delivery.Contracts;
using Platform.Web.Modules.Orders.Contracts;
using Platform.Web.Modules.Orders.Domain;
using Platform.Web.Modules.Sales.Contracts;
using Platform.Web.Modules.Stock.Contracts;
using Platform.Web.Modules.Stock.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Consumers.Services;

/// <inheritdoc />
public sealed class ConsumerOrderService : IConsumerOrderService
{
    private const int MaxLines = 40;
    private const int MaxQty = 100_000;

    private readonly AppDbContext _db;
    private readonly IBusinessContext _context;
    private readonly IUnitOfWork _uow;
    private readonly IStockReservationService _reservations;
    private readonly IInventoryService _inventory;
    private readonly ISalesService _sales;
    private readonly IDeliveryService _delivery;
    private readonly IAuditLogService _audit;
    private readonly TimeProvider _time;

    public ConsumerOrderService(
        AppDbContext db,
        IBusinessContext context,
        IUnitOfWork uow,
        IStockReservationService reservations,
        IInventoryService inventory,
        ISalesService sales,
        IDeliveryService delivery,
        IAuditLogService audit,
        TimeProvider time)
    {
        _db = db;
        _context = context;
        _uow = uow;
        _reservations = reservations;
        _inventory = inventory;
        _sales = sales;
        _delivery = delivery;
        _audit = audit;
        _time = time;
    }

    private sealed record PlannedItem(
        int ProductId, string ProductName, string BaseUnitName, int QtyBase, decimal UnitPrice, decimal RegularUnitPrice,
        bool IsPromo, int PromoPercent, DateOnly? ExpiryDate, int? BatchId, string? ImagePath = null);

    private sealed record Plan(
        Business Seller, FulfillmentType Fulfillment, string? Address, decimal DeliveryFee, double? RoadKm, IReadOnlyList<PlannedItem> Items)
    {
        public decimal Subtotal => Items.Sum(i => Money.Round(i.UnitPrice * i.QtyBase));

        public decimal Savings => Items.Sum(i => Money.Round((i.RegularUnitPrice - i.UnitPrice) * i.QtyBase));
    }

    // ------------------------------------------------------------------ rencana & pembuatan

    public async Task<Result<ConsumerOrderPreviewDto>> PreviewAsync(PlaceConsumerOrderRequest request, CancellationToken ct = default)
    {
        var plan = await PlanAsync(request, ct);
        if (!plan.Succeeded)
        {
            return Result<ConsumerOrderPreviewDto>.Fail(plan.Kind, plan.Error ?? "Pesanan tidak valid.");
        }

        var p = plan.Value!;
        return Result<ConsumerOrderPreviewDto>.Ok(new ConsumerOrderPreviewDto(
            p.Seller.Id, p.Seller.Name, p.Fulfillment, p.Address, p.Subtotal, p.DeliveryFee, p.Subtotal + p.DeliveryFee, p.Savings,
            p.Items.Select(i => new ConsumerOrderItemDto(
                i.ProductName, i.BaseUnitName, i.QtyBase, i.UnitPrice, i.RegularUnitPrice, i.IsPromo, i.PromoPercent, i.ExpiryDate,
                Money.Round(i.UnitPrice * i.QtyBase), i.ImagePath)).ToList()));
    }

    public async Task<Result<int>> PlaceAsync(PlaceConsumerOrderRequest request, CancellationToken ct = default)
    {
        if (!_context.IsAuthenticated || _context.Role != RoleNames.Consumer || _context.UserId is not int userId)
        {
            return Result<int>.Fail(ErrorKind.Forbidden, "Hanya akun pembeli yang dapat membuat pesanan pembeli.");
        }

        var key = string.IsNullOrWhiteSpace(request.IdempotencyKey) ? null : request.IdempotencyKey.Trim();
        if (key is { Length: > 64 })
        {
            key = key[..64];
        }

        // Satu unit kerja: cek kunci operasi -> hitung harga di server -> simpan pesanan -> reservasi stok atomik -> catatan pembayaran.
        return await _uow.ExecuteAsync<int>(async token =>
        {
            if (key is not null)
            {
                var existing = await _db.ConsumerOrders.AsNoTracking()
                    .Where(o => o.ConsumerUserId == userId && o.IdempotencyKey == key)
                    .Select(o => (int?)o.Id)
                    .FirstOrDefaultAsync(token);
                if (existing is int orderId)
                {
                    return Result<int>.Ok(orderId); // checkout yang sama dikirim ulang: kembalikan pesanan yang sama
                }
            }

            var planResult = await PlanAsync(request, token);
            if (!planResult.Succeeded)
            {
                return Result<int>.Fail(planResult.Kind, planResult.Error ?? "Pesanan tidak valid.");
            }

            var plan = planResult.Value!;
            var user = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == userId, token);
            var profile = await _db.ConsumerProfiles.AsNoTracking().FirstOrDefaultAsync(token);
            var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
            if (note is { Length: > 200 })
            {
                return Result<int>.Fail(ErrorKind.Validation, "Catatan maksimal 200 karakter.");
            }

            var now = _time.GetUtcNow().UtcDateTime;
            var order = new ConsumerOrder
            {
                Number = "TMP",
                ConsumerUserId = userId,
                ConsumerName = user.FullName,
                ConsumerPhone = profile?.Phone,
                SellerBusinessId = plan.Seller.Id,
                SellerName = plan.Seller.Name,
                Status = OrderStatus.Pending,
                Fulfillment = plan.Fulfillment,
                Subtotal = plan.Subtotal,
                DeliveryFee = plan.DeliveryFee,
                DeliveryDistanceKm = plan.RoadKm,
                TotalAmount = plan.Subtotal + plan.DeliveryFee,
                DeliveryAddress = plan.Address,
                Note = note,
                PaymentMethod = request.PaymentMethod,
                PaymentStatus = request.PaymentMethod == PaymentMethod.DemoSimulation ? OrderPaymentStatus.Pending : OrderPaymentStatus.Unpaid,
                IdempotencyKey = key,
                CreatedAt = now,
                UpdatedAt = now
            };

            foreach (var item in plan.Items)
            {
                order.Items.Add(new ConsumerOrderItem
                {
                    ConsumerUserId = userId,
                    SellerBusinessId = plan.Seller.Id,
                    ProductId = item.ProductId,
                    ProductName = item.ProductName,
                    BaseUnitName = item.BaseUnitName,
                    QtyBase = item.QtyBase,
                    UnitPrice = item.UnitPrice,
                    RegularUnitPrice = item.RegularUnitPrice,
                    IsPromo = item.IsPromo,
                    PromoPercent = item.PromoPercent,
                    ExpiryDate = item.ExpiryDate,
                    Subtotal = Money.Round(item.UnitPrice * item.QtyBase)
                });
            }

            order.Events.Add(new ConsumerOrderEvent
            {
                ConsumerUserId = userId, SellerBusinessId = plan.Seller.Id, Status = OrderStatus.Pending, At = now
            });

            _db.ConsumerOrders.Add(order);
            await _db.SaveChangesAsync(token); // id pesanan dan id baris tersedia untuk reservasi
            order.Number = $"C{order.Id:D6}";

            // Reservasi atomik atas stok warung: pembeli lain tidak dapat memesan bagian yang sudah ditahan.
            // Baris promo menunjuk batch tertentu; sisanya mengikuti FEFO.
            var lines = order.Items
                .Zip(plan.Items, (entity, planned) => new ReserveLine(entity.Id, entity.ProductId, entity.QtyBase, planned.BatchId))
                .ToList();
            var reserved = await _reservations.ReserveAsync(plan.Seller.Id, ReservationRefTypes.ConsumerOrder, order.Id, lines, token);
            if (!reserved.Succeeded)
            {
                return Result<int>.Fail(reserved.Kind, reserved.Error ?? "Stok tidak dapat dicadangkan.");
            }

            var payment = PaymentLedger.Open(_db, order, now);
            await _db.SaveChangesAsync(token);
            PaymentLedger.FinalizeReference(payment);

            _audit.Record(
                "CONSUMER_ORDER_CREATED", "ConsumerOrder", order.Id.ToString(),
                $"{order.Number}; ke {order.SellerName}; total={order.TotalAmount}; bayar={order.PaymentMethod}", userId, null);
            await _db.SaveChangesAsync(token);
            return Result<int>.Ok(order.Id);
        }, ct);
    }

    // Seluruh perhitungan harga, promo, dan ongkir dilakukan di server dari data warung; input browser hanya produk dan jumlah.
    private async Task<Result<Plan>> PlanAsync(PlaceConsumerOrderRequest request, CancellationToken ct)
    {
        if (!_context.IsAuthenticated || _context.Role != RoleNames.Consumer)
        {
            return Result<Plan>.Fail(ErrorKind.Forbidden, "Hanya akun pembeli yang dapat membuat pesanan pembeli.");
        }

        if (!Enum.IsDefined(request.Fulfillment) || !Enum.IsDefined(request.PaymentMethod))
        {
            return Result<Plan>.Fail(ErrorKind.Validation, "Cara pengambilan atau pembayaran tidak valid.");
        }

        if (request.PaymentMethod == PaymentMethod.CashOnPickup && request.Fulfillment != FulfillmentType.Pickup)
        {
            return Result<Plan>.Fail(ErrorKind.Validation, "Bayar saat ambil hanya untuk pesanan ambil sendiri.");
        }

        if (request.PaymentMethod == PaymentMethod.CashOnDelivery && request.Fulfillment != FulfillmentType.Delivery)
        {
            return Result<Plan>.Fail(ErrorKind.Validation, "Bayar saat diterima hanya untuk pesanan antar.");
        }

        var requested = request.Lines ?? Array.Empty<ConsumerLineInput>();
        if (requested.Count == 0)
        {
            return Result<Plan>.Fail(ErrorKind.Validation, "Pilih minimal satu barang.");
        }

        if (requested.Count > MaxLines)
        {
            return Result<Plan>.Fail(ErrorKind.Validation, $"Maksimal {MaxLines} jenis barang per pesanan.");
        }

        if (requested.Any(l => l.QtyBase is <= 0 or > MaxQty))
        {
            return Result<Plan>.Fail(ErrorKind.Validation, "Jumlah harus 1–100.000.");
        }

        var seller = await _db.Businesses.AsNoTracking().FirstOrDefaultAsync(
            b => b.Id == request.SellerBusinessId && b.MarketRole == MarketRole.Retail
                 && b.VerificationStatus == VerificationStatus.Verified && b.IsActive, ct);
        if (seller is null)
        {
            return Result<Plan>.Fail(ErrorKind.NotFound, "Warung tidak ditemukan atau belum terverifikasi.");
        }

        var option = (await _delivery.GetPublicAsync(new[] { seller.Id }, ct))[seller.Id];
        var profile = await _db.ConsumerProfiles.AsNoTracking().FirstOrDefaultAsync(ct);

        decimal fee = 0m;
        double? roadKm = null;
        string? address = null;
        if (request.Fulfillment == FulfillmentType.Delivery)
        {
            if (!option.OffersDelivery)
            {
                return Result<Plan>.Fail(ErrorKind.Validation, "Warung ini tidak menyediakan pengantaran.");
            }

            double? straight = profile is { Latitude: double la, Longitude: double lo } && seller.Latitude is double sla && seller.Longitude is double slo
                ? Geo.DistanceKm(la, lo, sla, slo)
                : null;
            var quote = DeliveryFeeCalculator.Quote(option, straight);
            if (!quote.Available)
            {
                return Result<Plan>.Fail(ErrorKind.Validation, quote.Reason ?? "Pengantaran tidak tersedia untuk lokasi Anda.");
            }

            fee = quote.Fee;
            roadKm = quote.RoadKm;
            address = string.IsNullOrWhiteSpace(request.DeliveryAddress) ? profile?.Address : request.DeliveryAddress.Trim();
            if (address is null || address.Length is < 5 or > 300)
            {
                return Result<Plan>.Fail(ErrorKind.Validation, "Alamat pengantaran harus 5–300 karakter.");
            }
        }
        else if (!option.OffersPickup)
        {
            return Result<Plan>.Fail(ErrorKind.Validation, "Warung ini tidak menyediakan ambil sendiri.");
        }

        var today = WibTime.TodayWib(_time);
        var merged = requested.GroupBy(l => l.ProductId).Select(g => new ConsumerLineInput(g.Key, g.Sum(x => x.QtyBase))).ToList();
        if (merged.Any(l => l.QtyBase > MaxQty))
        {
            return Result<Plan>.Fail(ErrorKind.Validation, "Jumlah terlalu besar.");
        }

        var productIds = merged.Select(m => m.ProductId).ToList();
        var inventories = await _db.Inventories.IgnoreQueryFilters().AsNoTracking().Include(i => i.Product)
            .Where(i => i.BusinessId == seller.Id && productIds.Contains(i.ProductId))
            .ToListAsync(ct);
        var batches = await _db.StockBatches.IgnoreQueryFilters().AsNoTracking()
            .Where(b => b.BusinessId == seller.Id && productIds.Contains(b.ProductId) && b.QtyRemainingBase > 0)
            .OrderBy(b => b.ExpiryDate).ThenBy(b => b.Id)
            .ToListAsync(ct);
        var promos = await _db.ConsumerPromos.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.BusinessId == seller.Id && p.IsActive && productIds.Contains(p.ProductId))
            .ToListAsync(ct);

        var items = new List<PlannedItem>();
        foreach (var line in merged)
        {
            var inv = inventories.FirstOrDefault(i => i.ProductId == line.ProductId);
            if (inv is null || !inv.Product.IsActive || inv.SellingPriceBase <= 0)
            {
                return Result<Plan>.Fail(ErrorKind.NotFound, "Produk tidak tersedia di warung ini.");
            }

            var productBatches = batches.Where(b => b.ProductId == line.ProductId).ToList();
            var expiredFree = productBatches.Where(b => b.ExpiryDate < today).Sum(b => b.QtyRemainingBase - b.ReservedBase);
            var sellable = Math.Max(0, inv.QtyBase - inv.ReservedBase - expiredFree);
            if (line.QtyBase > sellable)
            {
                return Result<Plan>.Fail(
                    ErrorKind.Conflict, $"Stok {inv.Product.Name} tidak cukup (tersedia {sellable} {inv.Product.BaseUnitName}).");
            }

            var remaining = line.QtyBase;
            var promo = promos.FirstOrDefault(p => p.ProductId == line.ProductId);
            if (promo is not null)
            {
                var limit = today.AddDays(promo.WithinDays);
                var promoPrice = ConsumerPricing.PromoPrice(inv.SellingPriceBase, promo.DiscountPercent);
                foreach (var batch in productBatches.Where(b => b.ExpiryDate >= today && b.ExpiryDate <= limit))
                {
                    var free = batch.QtyRemainingBase - batch.ReservedBase;
                    if (free <= 0 || remaining <= 0)
                    {
                        continue;
                    }

                    var take = Math.Min(free, remaining);
                    items.Add(new PlannedItem(
                        line.ProductId, inv.Product.Name, inv.Product.BaseUnitName, take, promoPrice, inv.SellingPriceBase,
                        true, promo.DiscountPercent, batch.ExpiryDate, batch.Id, inv.Product.ImagePath));
                    remaining -= take;
                }
            }

            if (remaining > 0)
            {
                items.Add(new PlannedItem(
                    line.ProductId, inv.Product.Name, inv.Product.BaseUnitName, remaining, inv.SellingPriceBase, inv.SellingPriceBase,
                    false, 0, null, null, inv.Product.ImagePath));
            }
        }

        return Result<Plan>.Ok(new Plan(seller, request.Fulfillment, address, fee, roadKm, items));
    }

    // ------------------------------------------------------------------ baca

    public async Task<IReadOnlyList<ConsumerOrderSummaryDto>> ListAsync(OrderStatus? status, int take = 100, CancellationToken ct = default)
    {
        if (!_context.IsAuthenticated)
        {
            return Array.Empty<ConsumerOrderSummaryDto>();
        }

        IQueryable<ConsumerOrder> query = _db.ConsumerOrders.AsNoTracking().Include(o => o.Items);
        var consumerSide = _context.Role == RoleNames.Consumer;
        if (!_context.IsAdmin)
        {
            if (consumerSide)
            {
                query = query.Where(o => o.ConsumerUserId == _context.UserId);
            }
            else if (_context.Role == RoleNames.Retail)
            {
                query = query.Where(o => o.SellerBusinessId == _context.BusinessId);
            }
            else
            {
                return Array.Empty<ConsumerOrderSummaryDto>();
            }
        }

        if (status is not null)
        {
            query = query.Where(o => o.Status == status);
        }

        var orders = await query.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id).Take(Math.Clamp(take, 1, 500)).ToListAsync(ct);
        return orders
            .Select(o => new ConsumerOrderSummaryDto(
                o.Id, o.Number, consumerSide ? o.SellerName : o.ConsumerName, o.Status, o.PaymentStatus, o.PaymentMethod, o.Fulfillment,
                o.TotalAmount,
                string.Join(", ", o.Items.GroupBy(i => i.ProductName).Take(3).Select(g => $"{g.Sum(i => i.QtyBase)} {g.First().BaseUnitName} {g.Key}"))
                + (o.Items.Select(i => i.ProductName).Distinct().Count() > 3 ? ", ..." : string.Empty),
                o.CreatedAt))
            .ToList();
    }

    public async Task<Result<ConsumerOrderDetailDto>> GetAsync(int id, CancellationToken ct = default)
    {
        var order = await _db.ConsumerOrders.AsNoTracking().Include(o => o.Items).Include(o => o.Events).FirstOrDefaultAsync(o => o.Id == id, ct);
        var actor = order is null ? null : ActorOf(order);
        if (order is null || (actor is null && !_context.IsAdmin))
        {
            return Result<ConsumerOrderDetailDto>.Fail(ErrorKind.NotFound, "Pesanan tidak ditemukan.");
        }

        var seller = await _db.Businesses.AsNoTracking().FirstOrDefaultAsync(b => b.Id == order.SellerBusinessId, ct);
        var payments = await _db.Payments.AsNoTracking().Where(p => p.ConsumerOrderId == id).OrderBy(p => p.Id).ToListAsync(ct);
        var events = order.Events.OrderBy(e => e.At).ThenBy(e => e.Id).Select(e => new OrderEventDto(e.Status, e.At, e.Note)).ToList();

        var actions = new List<OrderAction>();
        if (actor is not null)
        {
            foreach (var action in Enum.GetValues<OrderAction>())
            {
                if (OrderStateMachine.TryNext(order.Status, action, order.Fulfillment, out _, out var required)
                    && required == actor
                    && !(action == OrderAction.HandOver && NeedsPaymentFirst(order)))
                {
                    actions.Add(action);
                }
            }
        }

        var canPay = actor == OrderActor.Buyer
                     && order.PaymentMethod == PaymentMethod.DemoSimulation
                     && order.PaymentStatus is OrderPaymentStatus.Pending or OrderPaymentStatus.Failed
                     && order.Status is not (OrderStatus.Rejected or OrderStatus.Cancelled or OrderStatus.Completed);

        // Gambar produk milik warung penjual: dibaca lintas usaha secara sengaja (hanya alamat gambar, yang memang publik).
        var productIds = order.Items.Select(i => i.ProductId).Distinct().ToList();
        var images = await _db.Products.IgnoreQueryFilters().AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.ImagePath })
            .ToDictionaryAsync(p => p.Id, p => p.ImagePath, ct);
        var items = order.Items
            .OrderBy(i => i.Id)
            .Select(i => new ConsumerOrderItemDto(
                i.ProductName, i.BaseUnitName, i.QtyBase, i.UnitPrice, i.RegularUnitPrice, i.IsPromo, i.PromoPercent, i.ExpiryDate, i.Subtotal,
                images.TryGetValue(i.ProductId, out var img) ? img : null))
            .ToList();

        return Result<ConsumerOrderDetailDto>.Ok(new ConsumerOrderDetailDto(
            order.Id, order.Number, order.ConsumerName, order.ConsumerPhone, order.SellerName, seller?.Address ?? string.Empty, seller?.Phone,
            order.Status, order.PaymentStatus, order.PaymentMethod, order.Fulfillment, order.Subtotal, order.DeliveryFee, order.TotalAmount,
            order.DeliveryAddress, order.Note, order.RejectReason, order.CreatedAt, order.CompletedAt, items, events,
            payments.Select(PaymentLedger.ToDto).ToList(), actions, OrderTracker.Build(order.Status, order.Fulfillment, events, TrackingParty.Consumer), canPay,
            order.Items.Sum(i => Money.Round((i.RegularUnitPrice - i.UnitPrice) * i.QtyBase))));
    }

    // ------------------------------------------------------------------ aksi status

    public async Task<Result> ActAsync(int id, OrderAction action, string? reason, CancellationToken ct = default)
    {
        if (!_context.IsAuthenticated || _context.IsAdmin || _context.Role is not (RoleNames.Consumer or RoleNames.Retail))
        {
            return Result.Fail(ErrorKind.Forbidden, "Hanya pembeli atau warung penjual yang dapat memproses pesanan ini.");
        }

        var cleanReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        var needsReason = action is OrderAction.Reject or OrderAction.SellerCancel;
        if (needsReason && (cleanReason is null || cleanReason.Length < 3))
        {
            return Result.Fail(ErrorKind.Validation, "Alasan wajib diisi (minimal 3 karakter).");
        }

        if (cleanReason is { Length: > 200 })
        {
            return Result.Fail(ErrorKind.Validation, "Alasan maksimal 200 karakter.");
        }

        // Pesanan dibaca ulang DI DALAM unit kerja: dua aksi serentak diproses bergantian dan yang kedua melihat hasil yang pertama.
        return await _uow.ExecuteAsync(async token =>
        {
            var order = await _db.ConsumerOrders.Include(o => o.Items).Include(o => o.Events).FirstOrDefaultAsync(o => o.Id == id, token);
            var actor = order is null ? null : ActorOf(order);
            if (order is null || actor is null)
            {
                return Result.Fail(ErrorKind.NotFound, "Pesanan tidak ditemukan.");
            }

            if (OrderStateMachine.ActorFor(action) == actor && order.Status == OrderStateMachine.TargetOf(action, order.Fulfillment))
            {
                return Result.Ok(); // pengulangan aksi yang sama: tanpa efek stok, pembayaran, atau event ganda
            }

            if (!OrderStateMachine.TryNext(order.Status, action, order.Fulfillment, out var next, out var required))
            {
                return Result.Fail(ErrorKind.Conflict, "Aksi ini tidak dapat dilakukan pada status pesanan saat ini.");
            }

            if (required != actor)
            {
                return Result.Fail(ErrorKind.Forbidden, "Anda tidak berwenang melakukan aksi ini pada pesanan ini.");
            }

            if (action == OrderAction.HandOver && NeedsPaymentFirst(order))
            {
                return Result.Fail(ErrorKind.Conflict, "Pembayaran simulasi belum lunas. Tunggu pembeli menyelesaikan pembayaran.");
            }

            var now = _time.GetUtcNow().UtcDateTime;
            var payments = await _db.Payments.Where(p => p.ConsumerOrderId == order.Id).OrderBy(p => p.Id).ToListAsync(token);

            switch (action)
            {
                case OrderAction.Accept:
                {
                    var consumed = await _reservations.ConsumeAsync(
                        order.SellerBusinessId, ReservationRefTypes.ConsumerOrder, order.Id, order.Number, token);
                    if (!consumed.Succeeded)
                    {
                        return Result.Fail(consumed.Kind, consumed.Error ?? "Stok gagal dikeluarkan.");
                    }

                    // Modal dan batch asal dibekukan per baris (satu baris pesanan = satu RefItemId).
                    var byItem = consumed.Value!.ToDictionary(c => c.RefItemId);
                    foreach (var item in order.Items)
                    {
                        if (!byItem.TryGetValue(item.Id, out var info))
                        {
                            return Result.Fail(ErrorKind.Conflict, "Reservasi stok pesanan tidak lengkap. Hubungi admin.");
                        }

                        item.UnitCost = info.UnitCost;
                        item.ExpirySnapshot = ExpirySnapshotFormat.Format(info.Allocations);
                    }

                    break;
                }

                case OrderAction.Reject or OrderAction.BuyerCancel:
                {
                    var released = await _reservations.ReleaseAsync(order.SellerBusinessId, ReservationRefTypes.ConsumerOrder, order.Id, token);
                    if (!released.Succeeded)
                    {
                        return released;
                    }

                    PaymentLedger.CancelOrRefund(order, payments, now);
                    break;
                }

                case OrderAction.SellerCancel:
                {
                    // Stok yang sudah keluar saat diterima dikembalikan pada modal dan batch yang sama (tepat sekali: status menjaganya).
                    foreach (var item in order.Items)
                    {
                        var back = await _inventory.StockInAsync(
                            new StockInRequest(
                                item.ProductId, item.QtyBase, item.UnitCost, MovementType.OrderReturnIn, "ConsumerOrder", order.Id,
                                $"Batal {order.Number}", ExpirySnapshotFormat.Parse(item.ExpirySnapshot)),
                            token);
                        if (!back.Succeeded)
                        {
                            return Result.Fail(back.Kind, back.Error ?? "Stok gagal dikembalikan.");
                        }
                    }

                    PaymentLedger.CancelOrRefund(order, payments, now);
                    break;
                }

                case OrderAction.ConfirmReceived:
                {
                    // Pembayaran tunai terkumpul saat barang diterima; penjualan dicatat ke pembukuan warung TEPAT SATU KALI.
                    PaymentLedger.SettleCash(order, payments, now);
                    var sale = await _sales.RecordConsumerOrderSaleAsync(
                        order.SellerBusinessId, order.Id, order.Number, now,
                        order.PaymentMethod == PaymentMethod.DemoSimulation ? "DEMO" : "CASH",
                        order.Items.Select(i => new ConsumerSaleLine(i.ProductId, i.ProductName, i.QtyBase, i.UnitPrice, i.UnitCost)).ToList(),
                        token);
                    if (!sale.Succeeded)
                    {
                        return Result.Fail(sale.Kind, sale.Error ?? "Penjualan gagal dicatat.");
                    }

                    break;
                }
            }

            order.Status = next;
            order.UpdatedAt = now;
            if (next == OrderStatus.Completed)
            {
                order.CompletedAt = now;
            }

            if (needsReason)
            {
                order.RejectReason = cleanReason;
            }

            order.Events.Add(new ConsumerOrderEvent
            {
                ConsumerOrderId = order.Id,
                ConsumerUserId = order.ConsumerUserId,
                SellerBusinessId = order.SellerBusinessId,
                Status = next,
                At = now,
                Note = cleanReason
            });

            _audit.Record(
                "CONSUMER_ORDER_" + action.ToString().ToUpperInvariant(), "ConsumerOrder", order.Id.ToString(),
                $"{order.Number}: -> {next}");
            await _db.SaveChangesAsync(token);
            return Result.Ok();
        }, ct);
    }

    // ------------------------------------------------------------------ helper

    private static bool NeedsPaymentFirst(ConsumerOrder order) =>
        order.PaymentMethod == PaymentMethod.DemoSimulation && order.PaymentStatus != OrderPaymentStatus.Paid;

    private OrderActor? ActorOf(ConsumerOrder order)
    {
        if (_context.Role == RoleNames.Consumer && _context.UserId == order.ConsumerUserId)
        {
            return OrderActor.Buyer;
        }

        if (_context.Role == RoleNames.Retail && _context.BusinessId == order.SellerBusinessId)
        {
            return OrderActor.Seller;
        }

        return null;
    }
}
