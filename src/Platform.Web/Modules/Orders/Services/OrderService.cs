using Microsoft.EntityFrameworkCore;
using Platform.Web.Data;
using Platform.Web.Modules.Accounting.Contracts;
using Platform.Web.Modules.Audit.Contracts;
using Platform.Web.Modules.BusinessProfile.Contracts;
using Platform.Web.Modules.BusinessProfile.Domain;
using Platform.Web.Modules.Catalog.Contracts;
using Platform.Web.Modules.Delivery.Contracts;
using Platform.Web.Modules.Marketplace.Contracts;
using Platform.Web.Modules.Orders.Contracts;
using Platform.Web.Modules.Orders.Domain;
using Platform.Web.Modules.Purchasing.Contracts;
using Platform.Web.Modules.Rescue.Contracts;
using Platform.Web.Modules.Stock.Contracts;
using Platform.Web.Modules.Stock.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Orders.Services;

public sealed class OrderService : IOrderService
{
    private const int MaxLines = 40;
    private const decimal MaxMoney = 1_000_000_000_000m;

    private readonly AppDbContext _db;
    private readonly IBusinessContext _context;
    private readonly IMarketplaceService _marketplace;
    private readonly IDeliveryService _delivery;
    private readonly IRescueService _rescue;
    private readonly IBusinessService _business;
    private readonly IProductService _products;
    private readonly IInventoryService _inventory;
    private readonly IPurchasingService _purchasing;
    private readonly IExpenseService _expenses;
    private readonly IAuditLogService _audit;
    private readonly TimeProvider _time;
    private readonly ILogger<OrderService> _logger;
    private readonly IUnitOfWork _uow;
    private readonly IStockReservationService _reservations;

    public OrderService(
        AppDbContext db,
        IBusinessContext context,
        IMarketplaceService marketplace,
        IDeliveryService delivery,
        IRescueService rescue,
        IBusinessService business,
        IProductService products,
        IInventoryService inventory,
        IPurchasingService purchasing,
        IExpenseService expenses,
        IAuditLogService audit,
        TimeProvider time,
        ILogger<OrderService> logger,
        IUnitOfWork uow,
        IStockReservationService reservations)
    {
        _db = db;
        _context = context;
        _marketplace = marketplace;
        _delivery = delivery;
        _rescue = rescue;
        _business = business;
        _products = products;
        _inventory = inventory;
        _purchasing = purchasing;
        _expenses = expenses;
        _audit = audit;
        _time = time;
        _logger = logger;
        _uow = uow;
        _reservations = reservations;
    }

    // ------------------------------------------------------------------ buat pesanan

    public async Task<Result<int>> CreateAsync(CreateOrderRequest request, CancellationToken ct = default)
    {
        var buyerId = _context.BusinessId;
        if (!_context.IsAuthenticated || _context.Role is not (RoleNames.Retail or RoleNames.Grossier) || buyerId is null)
        {
            return Failure(ErrorKind.Forbidden, "Hanya akun warung atau grosir yang dapat memesan.");
        }

        if (request.SellerBusinessId == buyerId)
        {
            return Failure(ErrorKind.Validation, "Tidak dapat memesan ke usaha sendiri.");
        }

        if (!Enum.IsDefined(request.Fulfillment))
        {
            return Failure(ErrorKind.Validation, "Cara pengambilan tidak valid.");
        }

        var requested = request.Lines ?? Array.Empty<OrderLineInput>();
        var bundleRequests = (request.BundleLines ?? Array.Empty<BundleLineInput>())
            .GroupBy(b => b.BundleId)
            .Select(g => new BundleLineInput(g.Key, g.Sum(x => x.Qty)))
            .ToList();
        if (requested.Count == 0 && bundleRequests.Count == 0)
        {
            return Failure(ErrorKind.Validation, "Pilih minimal satu barang atau paket.");
        }

        if (bundleRequests.Count > 10)
        {
            return Failure(ErrorKind.Validation, "Maksimal 10 paket per pesanan.");
        }

        if (request.OfferId is not null && bundleRequests.Count != 1)
        {
            return Failure(ErrorKind.Validation, "Penawaran hanya dapat dipakai untuk satu paket.");
        }

        if (requested.Count > MaxLines)
        {
            return Failure(ErrorKind.Validation, $"Maksimal {MaxLines} baris per pesanan.");
        }

        if (requested.Any(l => l.QtyPack <= 0 || l.QtyPack > 1_000_000))
        {
            return Failure(ErrorKind.Validation, "Jumlah harus 1–1.000.000.");
        }

        var note = Clean(request.Note);
        if (note is { Length: > 200 })
        {
            return Failure(ErrorKind.Validation, "Catatan maksimal 200 karakter.");
        }

        var key = CleanKey(request.IdempotencyKey);

        // Seluruh pembuatan pesanan (cek idempotensi, harga, ongkir, reservasi stok, simpan) = SATU unit kerja.
        return await _uow.ExecuteAsync<int>(async token =>
        {
            if (key is not null)
            {
                var existing = await _db.Orders.AsNoTracking()
                    .Where(o => o.BuyerBusinessId == buyerId && o.IdempotencyKey == key)
                    .Select(o => (int?)o.Id)
                    .FirstOrDefaultAsync(token);
                if (existing is int orderId)
                {
                    return Result<int>.Ok(orderId); // permintaan yang sama dikirim ulang: kembalikan pesanan yang sudah ada
                }
            }

            var wholesaler = await _marketplace.GetWholesalerAsync(request.SellerBusinessId, token);
            if (!wholesaler.Succeeded)
            {
                return Failure(ErrorKind.NotFound, "Penjual tidak ditemukan, belum terverifikasi, atau tidak melayani akun Anda.");
            }

            var buyer = await _business.GetProfileAsync(buyerId.Value, token);
            if (!buyer.Succeeded)
            {
                return Failure(ErrorKind.NotFound, "Usaha Anda tidak ditemukan.");
            }

            return await BuildAndSaveAsync(
                buyerId.Value, buyer.Value!.Name, buyer.Value.Address, wholesaler.Value!, request.Fulfillment,
                requested, bundleRequests, request.OfferId, request.DeliveryAddress, note, key,
                token);
        }, ct);
    }

    public async Task<Result<int>> CreateLivestockSaleAsync(CreateLivestockSaleRequest request, CancellationToken ct = default)
    {
        var sellerId = _context.BusinessId;
        if (!_context.IsAuthenticated || _context.Role != RoleNames.Livestock || sellerId is null)
        {
            return Failure(ErrorKind.Forbidden, "Hanya akun peternakan yang dapat mencatat penjualan ke agen.");
        }

        if (request.AgentBusinessId <= 0 || request.AgentBusinessId == sellerId.Value)
        {
            return Failure(ErrorKind.Validation, "Pilih agen yang valid.");
        }

        var submitted = request.Lines ?? Array.Empty<LivestockSaleLineInput>();
        if (submitted.Count > MaxLines || submitted.Any(l => l.QtyBase > 1_000_000))
        {
            return Failure(ErrorKind.Validation, $"Maksimal {MaxLines} baris dan jumlah maksimal 1.000.000 per produk.");
        }

        var requested = submitted
            .Where(l => l.ProductId > 0 && l.QtyBase > 0)
            .GroupBy(l => l.ProductId)
            .Select(g => new LivestockSaleLineInput(g.Key, g.Sum(x => x.QtyBase)))
            .ToList();
        if (requested.Count == 0)
        {
            return Failure(ErrorKind.Validation, "Isi jumlah minimal satu produk.");
        }

        if (requested.Any(l => l.QtyBase > 1_000_000))
        {
            return Failure(ErrorKind.Validation, $"Maksimal {MaxLines} produk dan jumlah maksimal 1.000.000 per produk.");
        }

        var note = Clean(request.Note);
        if (note is { Length: > 200 })
        {
            return Failure(ErrorKind.Validation, "Catatan maksimal 200 karakter.");
        }

        var key = CleanKey(request.IdempotencyKey);
        return await _uow.ExecuteAsync<int>(async token =>
        {
            if (key is not null)
            {
                var existing = await _db.Orders.AsNoTracking()
                    .Where(o => o.BuyerBusinessId == request.AgentBusinessId
                        && o.SellerBusinessId == sellerId.Value
                        && o.IdempotencyKey == key)
                    .Select(o => (int?)o.Id)
                    .FirstOrDefaultAsync(token);
                if (existing is int existingId)
                {
                    return Result<int>.Ok(existingId);
                }
            }

            var agent = await _db.Businesses.AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == request.AgentBusinessId
                    && b.MarketRole == MarketRole.Wholesale
                    && b.VerificationStatus == VerificationStatus.Verified
                    && b.IsActive, token);
            if (agent is null)
            {
                return Failure(ErrorKind.NotFound, "Agen tidak ditemukan, belum terverifikasi, atau tidak aktif.");
            }

            var farmResult = await _business.GetProfileAsync(sellerId.Value, token);
            if (!farmResult.Succeeded || farmResult.Value is null)
            {
                return Failure(ErrorKind.NotFound, "Profil peternakan tidak ditemukan.");
            }

            var catalog = (await _products.ListAsync(null, false, token)).ToDictionary(p => p.Id);
            var stock = (await _inventory.GetStockAsync(null, token)).ToDictionary(s => s.ProductId);
            var items = new List<OrderItem>();
            var subtotal = 0m;
            foreach (var line in requested)
            {
                if (!catalog.TryGetValue(line.ProductId, out var product) || !product.IsActive)
                {
                    return Failure(ErrorKind.NotFound, "Salah satu produk tidak ditemukan atau sudah dinonaktifkan.");
                }

                var baseUnit = product.Units.FirstOrDefault(u => u.IsBase && u.ConversionToBase == 1);
                if (baseUnit?.WholesalePrice is not decimal unitPrice || unitPrice <= 0)
                {
                    return Failure(ErrorKind.Validation, $"Harga jual satuan dasar untuk {product.Name} belum diatur.");
                }

                if (!stock.TryGetValue(line.ProductId, out var inventory) || inventory.AvailableBase < line.QtyBase)
                {
                    return Failure(ErrorKind.Conflict, $"Stok tersedia {product.Name} tidak mencukupi.");
                }

                var lineTotal = Money.Round(unitPrice * line.QtyBase);
                subtotal += lineTotal;
                if (lineTotal > MaxMoney || subtotal > MaxMoney)
                {
                    return Failure(ErrorKind.Validation, "Total penjualan melebihi batas pencatatan.");
                }
                items.Add(new OrderItem
                {
                    ProductId = product.Id,
                    ProductName = product.Name,
                    ProductSku = product.Sku,
                    CategoryId = product.CategoryId,
                    BaseUnitName = product.BaseUnitName,
                    UnitsSnapshot = string.Join("|", product.Units.Select(u => $"{u.UnitName}:{u.ConversionToBase}")),
                    UnitName = product.BaseUnitName,
                    QtyPack = line.QtyBase,
                    ConversionSnapshot = 1,
                    UnitPrice = unitPrice,
                    QtyBase = line.QtyBase,
                    Subtotal = lineTotal
                });
            }

            var now = _time.GetUtcNow().UtcDateTime;
            var order = new Order
            {
                Number = "TMP",
                BuyerBusinessId = agent.Id,
                SellerBusinessId = sellerId.Value,
                BuyerName = agent.Name,
                SellerName = farmResult.Value.Name,
                Status = OrderStatus.PickedUp,
                Fulfillment = FulfillmentType.Pickup,
                Subtotal = subtotal,
                TotalAmount = subtotal,
                Note = note,
                IdempotencyKey = key,
                CreatedAt = now,
                UpdatedAt = now
            };
            foreach (var item in items)
            {
                item.BuyerBusinessId = agent.Id;
                item.SellerBusinessId = sellerId.Value;
                order.Items.Add(item);
            }

            foreach (var status in new[] { OrderStatus.Pending, OrderStatus.Accepted, OrderStatus.Preparing, OrderStatus.Ready, OrderStatus.PickedUp })
            {
                order.Events.Add(new OrderEvent
                {
                    BuyerBusinessId = agent.Id,
                    SellerBusinessId = sellerId.Value,
                    Status = status,
                    At = now,
                    Note = status == OrderStatus.PickedUp ? "Peternakan mencatat barang telah diserahkan kepada agen." : null
                });
            }

            _db.Orders.Add(order);
            await _db.SaveChangesAsync(token);
            order.Number = $"O{order.Id:D6}";

            foreach (var item in order.Items)
            {
                var removed = await _inventory.StockOutAsync(new StockOutRequest(
                    item.ProductId, item.QtyBase, MovementType.LivestockSaleOut, "Order", order.Id,
                    $"Penjualan ke agen {agent.Name} ({order.Number})"), token);
                if (!removed.Succeeded)
                {
                    return Failure(removed.Kind, removed.Error ?? "Stok gagal dikurangi.");
                }

                item.SellerUnitCost = removed.Value!.UnitCostBase;
                item.ExpirySnapshot = FormatPortions(removed.Value.Allocations);
            }

            _audit.Record("LIVESTOCK_SALE_CREATED", "Order", order.Id.ToString(),
                $"{order.Number}; ke agen {agent.Name}; total={order.TotalAmount}", null, sellerId.Value);
            await _db.SaveChangesAsync(token);
            return Result<int>.Ok(order.Id);
        }, ct);
    }

    // Inti pembuatan pesanan: harga/ongkir/MOQ dihitung ulang di server, stok DICADANGKAN atomik, lalu pesanan disimpan.
    // Dipanggil di dalam unit kerja.
    private async Task<Result<int>> BuildAndSaveAsync(
        int buyerId, string buyerName, string buyerAddress, WholesalerDetailDto seller, FulfillmentType fulfillment,
        IReadOnlyList<OrderLineInput> requested, IReadOnlyList<BundleLineInput> bundleRequests, int? offerId,
        string? requestedAddress, string? note, string? key, CancellationToken ct)
    {
        var card = seller.Card;

        if (fulfillment == FulfillmentType.Delivery && !card.OffersDelivery)
        {
            return Failure(ErrorKind.Validation, "Penjual ini tidak menyediakan pengiriman.");
        }

        // Ongkir selalu dihitung di server dari jarak penjual-pembeli (bukan dari input browser).
        var quote = (await _delivery.QuoteForBuyerAsync(new[] { card.BusinessId }, ct)).GetValueOrDefault(card.BusinessId);
        if (fulfillment == FulfillmentType.Delivery && quote is { Available: false })
        {
            return Failure(ErrorKind.Validation, quote.Reason ?? "Pengiriman tidak tersedia untuk lokasi Anda.");
        }

        if (fulfillment == FulfillmentType.Pickup && !card.OffersPickup)
        {
            return Failure(ErrorKind.Validation, "Penjual ini tidak menyediakan ambil sendiri.");
        }

        // Baris dengan produk dan kemasan sama digabung.
        var merged = requested
            .GroupBy(l => (l.ProductId, Unit: (l.UnitName ?? string.Empty).Trim().ToLowerInvariant()))
            .Select(g => new OrderLineInput(g.Key.ProductId, (g.First().UnitName ?? string.Empty).Trim(), g.Sum(x => x.QtyPack)))
            .ToList();

        var items = new List<OrderItem>();
        var baseNeeded = new Dictionary<int, int>();
        var subtotal = 0m;

        foreach (var line in merged)
        {
            var product = seller.Products.FirstOrDefault(p => p.ProductId == line.ProductId);
            if (product is null)
            {
                return Failure(ErrorKind.NotFound, "Produk tidak tersedia di penjual ini.");
            }

            var unit = product.Units.FirstOrDefault(u => string.Equals(u.UnitName, line.UnitName, StringComparison.OrdinalIgnoreCase));
            if (unit is null)
            {
                return Failure(ErrorKind.Validation, $"Kemasan \"{line.UnitName}\" tidak tersedia untuk {product.Name}.");
            }

            int qtyBase;
            try
            {
                qtyBase = PackagingMath.ToBase(line.QtyPack, unit.ConversionToBase);
            }
            catch (OverflowException)
            {
                return Failure(ErrorKind.Validation, "Jumlah terlalu besar.");
            }

            baseNeeded[product.ProductId] = (baseNeeded.TryGetValue(product.ProductId, out var current) ? current : 0) + qtyBase;

            var lineSubtotal = Money.Round(unit.Price * line.QtyPack);
            subtotal += lineSubtotal;
            items.Add(new OrderItem
            {
                ProductId = product.ProductId,
                ProductName = product.Name,
                ProductSku = product.Sku,
                CategoryId = product.CategoryId,
                BaseUnitName = product.BaseUnitName,
                UnitsSnapshot = product.UnitsText,
                UnitName = unit.UnitName,
                QtyPack = line.QtyPack,
                ConversionSnapshot = unit.ConversionToBase,
                UnitPrice = unit.Price,
                QtyBase = qtyBase,
                Subtotal = lineSubtotal
            });
        }

        // Minimum order quantity (MOQ) per produk, dalam satuan dasar.
        foreach (var (productId, needed) in baseNeeded)
        {
            var product = seller.Products.First(p => p.ProductId == productId);
            if (needed < product.MoqBase)
            {
                return Failure(
                    ErrorKind.Validation,
                    $"Minimal pesanan {product.Name} di {card.Name} adalah {product.MoqBase} {product.BaseUnitName} (MOQ). Anda memesan {needed}.");
            }
        }

        // Paket anti-mubazir: harga dan isi dihitung ulang dari paket di server, batch kadaluarsa ditunjuk eksplisit.
        var bundleTotal = 0m;
        foreach (var bundleRequest in bundleRequests)
        {
            var resolved = await _rescue.ResolveForOrderAsync(bundleRequest.BundleId, bundleRequest.Qty, offerId, ct);
            if (!resolved.Succeeded)
            {
                return Failure(resolved.Kind, resolved.Error ?? "Paket tidak dapat dipesan.");
            }

            var info = resolved.Value!;
            if (info.SellerBusinessId != card.BusinessId)
            {
                return Failure(ErrorKind.Validation, "Paket ini bukan milik penjual yang dipilih.");
            }

            foreach (var line in info.Lines)
            {
                items.Add(new OrderItem
                {
                    ProductId = line.ProductId,
                    ProductName = line.ProductName,
                    ProductSku = line.Sku,
                    CategoryId = line.CategoryId,
                    BaseUnitName = line.BaseUnitName,
                    UnitsSnapshot = line.UnitsText,
                    UnitName = line.BaseUnitName,
                    QtyPack = line.QtyBase,
                    ConversionSnapshot = 1,
                    UnitPrice = line.UnitPriceBase,
                    QtyBase = line.QtyBase,
                    Subtotal = line.Subtotal,
                    NormalSubtotal = line.NormalSubtotal,
                    BundleId = info.BundleId,
                    BundleName = info.Title,
                    BundleQty = info.Qty,
                    SourceBatchId = line.BatchId
                });
            }

            bundleTotal += info.Total;
        }

        // Pemeriksaan awal yang ramah (pesan jelas). Penahanan stok yang sebenarnya terjadi atomik di ReserveAsync di bawah.
        foreach (var (productId, needed) in baseNeeded)
        {
            var product = seller.Products.First(p => p.ProductId == productId);
            if (needed > product.StockBase)
            {
                return Failure(ErrorKind.Conflict, $"Stok {product.Name} di {card.Name} tidak cukup (tersedia {product.StockText}).");
            }
        }

        // Minimal nilai pesanan berlaku untuk barang biasa; pesanan yang hanya berisi paket anti-mubazir tidak dikenai minimal
        // supaya stok yang mendekati kadaluarsa mudah terjual.
        if (requested.Count > 0 && subtotal < card.MinOrderAmount)
        {
            return Failure(ErrorKind.Validation, $"Minimal pesanan di {card.Name} adalah {Rupiah.Format(card.MinOrderAmount)}.");
        }

        string? address = null;
        if (fulfillment == FulfillmentType.Delivery)
        {
            address = Clean(requestedAddress) ?? buyerAddress;
            if (address.Length is < 5 or > 300)
            {
                return Failure(ErrorKind.Validation, "Alamat pengiriman harus 5–300 karakter.");
            }
        }

        subtotal += bundleTotal;
        var fee = fulfillment == FulfillmentType.Delivery ? (quote?.Fee ?? card.DeliveryFee) : 0m;
        var now = _time.GetUtcNow().UtcDateTime;
        var order = new Order
        {
            Number = "TMP",
            BuyerBusinessId = buyerId,
            SellerBusinessId = card.BusinessId,
            BuyerName = buyerName,
            SellerName = card.Name,
            Status = OrderStatus.Pending,
            Fulfillment = fulfillment,
            Subtotal = subtotal,
            DeliveryFee = fee,
            DeliveryDistanceKm = fulfillment == FulfillmentType.Delivery ? quote?.RoadKm : null,
            TotalAmount = subtotal + fee,
            DeliveryAddress = address,
            Note = note,
            IdempotencyKey = key,
            CreatedAt = now,
            UpdatedAt = now
        };
        foreach (var item in items)
        {
            item.BuyerBusinessId = buyerId;
            item.SellerBusinessId = card.BusinessId;
            order.Items.Add(item);
        }

        order.Events.Add(new OrderEvent
        {
            BuyerBusinessId = buyerId,
            SellerBusinessId = card.BusinessId,
            Status = OrderStatus.Pending,
            At = now
        });

        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct); // id pesanan dan id baris tersedia untuk reservasi
        order.Number = $"O{order.Id:D6}";

        // Reservasi atomik: stok yang ditahan tidak bisa dipesan pembeli lain. Gagal = seluruh unit kerja dibatalkan.
        var lines = order.Items.Select(i => new ReserveLine(i.Id, i.ProductId, i.QtyBase, i.SourceBatchId)).ToList();
        var reserved = await _reservations.ReserveAsync(card.BusinessId, ReservationRefTypes.Order, order.Id, lines, ct);
        if (!reserved.Succeeded)
        {
            return Failure(reserved.Kind, reserved.Error ?? "Stok tidak dapat dicadangkan.");
        }

        _audit.Record("ORDER_CREATED", "Order", order.Id.ToString(), $"{order.Number}; ke {card.Name}; total={order.TotalAmount}", null, buyerId);
        await _db.SaveChangesAsync(ct);

        if (offerId is int offer)
        {
            await _rescue.MarkOfferAcceptedAsync(offer, order.Id, ct);
        }

        return Result<int>.Ok(order.Id);
    }

    // ------------------------------------------------------------------ baca

    public async Task<IReadOnlyList<OrderSummaryDto>> ListAsync(
        OrderStatus? status, int take = 100, CancellationToken ct = default)
    {
        var businessId = _context.BusinessId;
        IQueryable<Order> query = _db.Orders.AsNoTracking().Include(o => o.Items);

        if (!_context.IsAdmin)
        {
            if (_context.Role == RoleNames.Livestock)
            {
                query = query.Where(o => o.SellerBusinessId == businessId);
            }
            else if (_context.Role == RoleNames.Grossier)
            {
                query = query.Where(o => o.SellerBusinessId == businessId
                    || (o.BuyerBusinessId == businessId && _db.Businesses.Any(b => b.Id == o.SellerBusinessId && b.MarketRole == MarketRole.Livestock)));
            }
            else
            {
                query = query.Where(o => o.BuyerBusinessId == businessId);
            }
        }

        if (status is not null)
        {
            query = query.Where(o => o.Status == status);
        }

        var livestockSellerIds = await _db.Businesses.AsNoTracking()
            .Where(b => b.MarketRole == MarketRole.Livestock)
            .Select(b => b.Id)
            .ToListAsync(ct);
        var rows = await query.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id).Take(take).ToListAsync(ct);
        return rows
            .Select(o => new OrderSummaryDto(
                o.Id, o.Number, o.BuyerName, o.SellerName, o.Status, o.Fulfillment, o.TotalAmount,
                ItemsText(o), o.CreatedAt, o.Items.Any(i => i.BundleId != null),
                o.Items.Sum(i => i.SellerUnitCost * i.QtyBase), livestockSellerIds.Contains(o.SellerBusinessId), o.CompletedAt))
            .ToList();
    }

    public async Task<Result<OrderDetailDto>> GetAsync(int id, CancellationToken ct = default)
    {
        var order = await _db.Orders.AsNoTracking().Include(o => o.Items).Include(o => o.Events).FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
        {
            return Result<OrderDetailDto>.Fail(ErrorKind.NotFound, "Pesanan tidak ditemukan.");
        }

        var actions = new List<OrderAction>();
        var actor = _context.BusinessId is int businessId ? ActorOf(order, businessId) : null;
        if (actor is not null && !_context.IsAdmin)
        {
            foreach (var action in Enum.GetValues<OrderAction>())
            {
                if (OrderStateMachine.TryNext(order.Status, action, order.Fulfillment, out _, out var required) && required == actor)
                {
                    actions.Add(action);
                }
            }
        }

        var events = order.Events
            .OrderBy(e => e.At).ThenBy(e => e.Id)
            .Select(e => new OrderEventDto(e.Status, e.At, e.Note))
            .ToList();
        var parties = await _db.Businesses.AsNoTracking()
            .Where(b => b.Id == order.SellerBusinessId || b.Id == order.BuyerBusinessId)
            .ToListAsync(ct);
        var sellerBusiness = parties.FirstOrDefault(b => b.Id == order.SellerBusinessId);
        var buyerBusiness = parties.FirstOrDefault(b => b.Id == order.BuyerBusinessId);
        var isLivestockSale = sellerBusiness?.MarketRole == MarketRole.Livestock;
        var party = isLivestockSale ? TrackingParty.LivestockSale
            : order.Fulfillment == FulfillmentType.Delivery && buyerBusiness is null ? TrackingParty.Consumer : TrackingParty.Wholesale;
        var steps = OrderTracker.Build(order.Status, order.Fulfillment, events, party);

        double? roadKm = order.DeliveryDistanceKm;
        if (roadKm is null && sellerBusiness?.Latitude is double sla && sellerBusiness.Longitude is double slo
            && buyerBusiness?.Latitude is double bla && buyerBusiness.Longitude is double blo)
        {
            roadKm = Geo.EstimateRoadKm(Geo.DistanceKm(sla, slo, bla, blo));
        }

        int? travelMinutes = roadKm is double km ? Geo.EstimateTravelMinutes(km) : null;
        DateTime? shippedAt = events.Where(e => e.Status == OrderStatus.Delivered).Select(e => (DateTime?)e.At).LastOrDefault();
        var counterpart = actor == OrderActor.Seller ? buyerBusiness : sellerBusiness;
        var tracking = new OrderTrackingInfo(
            counterpart?.Name ?? string.Empty,
            counterpart?.Phone,
            order.SellerName,
            sellerBusiness?.Address ?? string.Empty,
            order.BuyerName,
            order.DeliveryAddress ?? buyerBusiness?.Address ?? string.Empty,
            sellerBusiness?.Latitude, sellerBusiness?.Longitude,
            buyerBusiness?.Latitude, buyerBusiness?.Longitude,
            roadKm, travelMinutes, shippedAt,
            shippedAt is DateTime sent && travelMinutes is int minutes ? sent.AddMinutes(minutes) : null,
            OrderTracker.WaitingFor(order.Status, order.Fulfillment, party),
            sellerBusiness?.Phone,
            buyerBusiness?.Phone);

        return Result<OrderDetailDto>.Ok(new OrderDetailDto(
            order.Id, order.Number, order.BuyerName, order.SellerName, order.Status, order.Fulfillment,
            order.Subtotal, order.DeliveryFee, order.TotalAmount, order.DeliveryAddress, order.Note, order.RejectReason,
            order.CreatedAt, order.UpdatedAt,
            order.Items
                .Select(i => new OrderItemDto(
                    i.ProductName, i.UnitName, i.QtyPack, i.ConversionSnapshot, i.UnitPrice, i.QtyBase, i.Subtotal,
                    i.BundleName, ExpiryText(i.ExpirySnapshot), i.NormalSubtotal))
                .ToList(),
            actions,
            steps,
            events,
            tracking,
            order.Items.Where(i => i.NormalSubtotal != null).Sum(i => i.NormalSubtotal!.Value - i.Subtotal),
            order.DeliveryDistanceKm,
            actor == OrderActor.Seller,
            isLivestockSale));
    }

    // ------------------------------------------------------------------ ubah status

    public async Task<Result> ActAsync(int orderId, OrderAction action, string? reason, CancellationToken ct = default)
    {
        var businessId = _context.BusinessId;
        if (!_context.IsAuthenticated || _context.IsAdmin || businessId is null)
        {
            return Result.Fail(ErrorKind.Forbidden, "Hanya akun usaha yang dapat memproses pesanan.");
        }

        var cleanReason = Clean(reason);
        var needsReason = action is OrderAction.Reject or OrderAction.SellerCancel;
        if (needsReason && (cleanReason is null || cleanReason.Length < 3))
        {
            return Result.Fail(ErrorKind.Validation, "Alasan wajib diisi (minimal 3 karakter).");
        }

        if (cleanReason is { Length: > 200 })
        {
            return Result.Fail(ErrorKind.Validation, "Alasan maksimal 200 karakter.");
        }

        // Pesanan DIBACA ULANG di dalam unit kerja (setelah kunci tulis diambil), bukan sebelumnya: dua permintaan serentak
        // untuk pesanan yang sama diproses bergantian, dan yang kedua melihat status terbaru dari yang pertama.
        return await _uow.ExecuteAsync(async token =>
        {
            var order = await _db.Orders.Include(o => o.Items).Include(o => o.Events).FirstOrDefaultAsync(o => o.Id == orderId, token);
            var actor = order is null ? null : ActorOf(order, businessId.Value);
            if (order is null || actor is null)
            {
                return Result.Fail(ErrorKind.NotFound, "Pesanan tidak ditemukan.");
            }

            // Pengulangan aksi yang sama oleh pihak yang berwenang (klik ganda, refresh, retry): status sudah di tujuan
            // -> sukses tanpa efek apa pun lagi (tidak ada stok, pembayaran, atau event ganda).
            if (OrderStateMachine.ActorFor(action) == actor && order.Status == OrderStateMachine.TargetOf(action, order.Fulfillment))
            {
                return Result.Ok();
            }

            if (!OrderStateMachine.TryNext(order.Status, action, order.Fulfillment, out var next, out var required))
            {
                return Result.Fail(ErrorKind.Conflict, "Aksi ini tidak dapat dilakukan pada status pesanan saat ini.");
            }

            if (required != actor)
            {
                return Result.Fail(ErrorKind.Forbidden, "Anda tidak berwenang melakukan aksi ini pada pesanan ini.");
            }

            var previous = order.Status;
            var effect = action switch
            {
                OrderAction.Accept => await ConsumeReservationAsync(order, token),
                OrderAction.Reject or OrderAction.BuyerCancel =>
                    await _reservations.ReleaseAsync(order.SellerBusinessId, ReservationRefTypes.Order, order.Id, token),
                OrderAction.SellerCancel => await ReturnStockAsync(order, token),
                OrderAction.ConfirmReceived => await ReceiveAsync(order, token),
                _ => Result.Ok()
            };

            if (!effect.Succeeded)
            {
                return effect; // unit kerja dibatalkan: tidak ada perubahan status maupun stok
            }

            var now = _time.GetUtcNow().UtcDateTime;
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

            order.Events.Add(new OrderEvent
            {
                OrderId = order.Id,
                BuyerBusinessId = order.BuyerBusinessId,
                SellerBusinessId = order.SellerBusinessId,
                Status = next,
                At = now,
                Note = cleanReason
            });

            _audit.Record(
                "ORDER_" + action.ToString().ToUpperInvariant(), "Order", order.Id.ToString(),
                $"{order.Number}: {previous} -> {next}", null, businessId);
            await _db.SaveChangesAsync(token);
            return Result.Ok();
        }, ct);
    }

    // Penjual menerima pesanan: reservasi (penahanan) menjadi pengeluaran stok fisik; modal dan batch asal dibekukan di baris pesanan.
    private async Task<Result> ConsumeReservationAsync(Order order, CancellationToken ct)
    {
        var consumed = await _reservations.ConsumeAsync(order.SellerBusinessId, ReservationRefTypes.Order, order.Id, order.Number, ct);
        if (!consumed.Succeeded)
        {
            return Result.Fail(consumed.Kind, consumed.Error ?? "Stok gagal dikeluarkan.");
        }

        var byItem = consumed.Value!.ToDictionary(c => c.RefItemId);
        foreach (var item in order.Items)
        {
            if (!byItem.TryGetValue(item.Id, out var info))
            {
                return Result.Fail(ErrorKind.Conflict, "Reservasi stok pesanan tidak lengkap. Hubungi admin.");
            }

            item.SellerUnitCost = info.UnitCost;
            item.ExpirySnapshot = FormatPortions(info.Allocations);
        }

        return Result.Ok();
    }

    // Penjual membatalkan pesanan yang sudah diterima: stok dikembalikan pada modal yang sama (tepat sekali: status pesanan menjaganya).
    private async Task<Result> ReturnStockAsync(Order order, CancellationToken ct)
    {
        foreach (var item in order.Items)
        {
            var result = await _inventory.StockInAsync(
                new StockInRequest(
                    item.ProductId, item.QtyBase, item.SellerUnitCost, MovementType.OrderReturnIn, "Order", order.Id, $"Batal {order.Number}",
                    ParsePortions(item.ExpirySnapshot)),
                ct);
            if (!result.Succeeded)
            {
                return Result.Fail(result.Kind, result.Error ?? "Stok gagal dikembalikan.");
            }
        }

        return Result.Ok();
    }

    // Warung mengonfirmasi barang diterima: produk dicocokkan/dibuat, pembelian dicatat, stok bertambah,
    // ongkir menjadi pengeluaran transportasi.
    private async Task<Result> ReceiveAsync(Order order, CancellationToken ct)
    {
        var lines = new List<OrderPurchaseLine>();
        foreach (var item in order.Items)
        {
            var extras = ParseUnits(item.UnitsSnapshot)
                .Where(u => u.Conversion > 1)
                .Select(u => new UnitInput(u.Name, u.Conversion, null))
                .ToList();

            var product = await _products.FindOrCreateFromSourceAsync(
                new SourceProductSnapshot(item.ProductId, item.ProductName, item.ProductSku, item.CategoryId, item.BaseUnitName, extras), ct);
            if (!product.Succeeded)
            {
                return Result.Fail(product.Kind, product.Error ?? "Produk gagal disiapkan.");
            }

            lines.Add(new OrderPurchaseLine(
                product.Value, item.ProductName, item.UnitName, item.QtyPack, item.ConversionSnapshot, item.UnitPrice,
                ParsePortions(item.ExpirySnapshot)));
        }

        var purchase = await _purchasing.RecordFromOrderAsync(order.Id, order.SellerName, lines, ct);
        if (!purchase.Succeeded)
        {
            return Result.Fail(purchase.Kind, purchase.Error ?? "Pembelian gagal dicatat.");
        }

        if (order.Fulfillment == FulfillmentType.Delivery && order.DeliveryFee > 0)
        {
            var expense = await _expenses.AddAsync(
                new AddExpenseRequest("Transportasi", order.DeliveryFee, WibTime.TodayWib(_time), $"Ongkir pesanan {order.Number}"), ct);
            if (!expense.Succeeded)
            {
                return Result.Fail(expense.Kind, expense.Error ?? "Ongkir gagal dicatat.");
            }
        }

        return Result.Ok();
    }

    // ------------------------------------------------------------------ helper

    private static OrderActor? ActorOf(Order order, int businessId)
    {
        if (order.SellerBusinessId == businessId)
        {
            return OrderActor.Seller;
        }

        return order.BuyerBusinessId == businessId ? OrderActor.Buyer : null;
    }

    private static IEnumerable<(string Name, int Conversion)> ParseUnits(string snapshot)
    {
        foreach (var part in snapshot.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = part.Split(':');
            if (pieces.Length == 2 && int.TryParse(pieces[1], out var conversion))
            {
                yield return (pieces[0], conversion);
            }
        }
    }

    private static string ItemsText(Order order)
    {
        var parts = order.Items
            .Where(i => i.BundleId is null)
            .Select(i => $"{i.ProductName} {i.QtyPack} {i.UnitName}")
            .ToList();
        parts.AddRange(order.Items
            .Where(i => i.BundleId is not null)
            .GroupBy(i => i.BundleId)
            .Select(g => $"Paket {g.First().BundleName} x{g.First().BundleQty}"));
        return string.Join(", ", parts);
    }

    // "yyyy-MM-dd:jumlah|yyyy-MM-dd:jumlah" (satuan dasar), dari alokasi batch saat grosir menerima pesanan.
    private static string? FormatPortions(IReadOnlyList<ReservedAllocation>? allocations)
    {
        var dated = allocations?.Where(a => a.ExpiryDate is not null).ToList();
        if (dated is null || dated.Count == 0)
        {
            return null;
        }

        var text = string.Join("|", dated
            .GroupBy(a => a.ExpiryDate!.Value)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Key:yyyy-MM-dd}:{g.Sum(a => a.QtyBase)}"));
        return text.Length <= 400 ? text : text[..text.LastIndexOf('|', 399)];
    }

    private static string? FormatPortions(IReadOnlyList<BatchAllocation>? allocations)
    {
        var dated = allocations?.ToList();
        if (dated is null || dated.Count == 0)
        {
            return null;
        }

        var text = string.Join("|", dated
            .GroupBy(a => a.ExpiryDate)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Key:yyyy-MM-dd}:{g.Sum(a => a.QtyBase)}"));
        return text.Length <= 400 ? text : text[..text.LastIndexOf('|', 399)];
    }

    private static IReadOnlyList<BatchPortion>? ParsePortions(string? snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot))
        {
            return null;
        }

        var result = new List<BatchPortion>();
        foreach (var part in snapshot.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = part.Split(':');
            if (pieces.Length == 2
                && DateOnly.TryParseExact(pieces[0], "yyyy-MM-dd", out var date)
                && int.TryParse(pieces[1], out var qty) && qty > 0)
            {
                result.Add(new BatchPortion(date, qty));
            }
        }

        return result.Count == 0 ? null : result;
    }

    private static string? ExpiryText(string? snapshot)
    {
        var portions = ParsePortions(snapshot);
        if (portions is null)
        {
            return null;
        }

        return "Kadaluarsa " + string.Join(", ", portions.Select(p => $"{IdFormat.Date(p.ExpiryDate)} ({p.QtyBase})"));
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? CleanKey(string? key)
    {
        var clean = Clean(key);
        return clean is { Length: > 64 } ? clean[..64] : clean;
    }

    private static Result<int> Failure(ErrorKind kind, string message) => Result<int>.Fail(kind, message);
}
