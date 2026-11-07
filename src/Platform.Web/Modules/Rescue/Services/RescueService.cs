using Microsoft.EntityFrameworkCore;
using Platform.Web.Data;
using Platform.Web.Modules.Audit.Contracts;
using Platform.Web.Modules.BusinessProfile.Domain;
using Platform.Web.Modules.Delivery.Contracts;
using Platform.Web.Modules.Insights.Contracts;
using Platform.Web.Modules.Orders.Domain;
using Platform.Web.Modules.Rescue.Contracts;
using Platform.Web.Modules.Rescue.Domain;
using Platform.Web.Modules.Stock.Contracts;
using Platform.Web.Modules.Stock.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Rescue.Services;

public sealed class RescueService : IRescueService
{
    private const int MaxItems = 10;
    private const int MaxTargets = 50;

    // Pesanan yang sudah diterima grosir (stok sudah keluar) dianggap "terjual".
    private static readonly OrderStatus[] SoldStatuses =
    {
        OrderStatus.Accepted, OrderStatus.Preparing, OrderStatus.Ready,
        OrderStatus.Delivered, OrderStatus.PickedUp, OrderStatus.Completed
    };

    private readonly AppDbContext _db;
    private readonly IBusinessContext _context;
    private readonly IExpiryService _expiry;
    private readonly ISellThroughService _sellThrough;
    private readonly IDeliveryService _delivery;
    private readonly IAuditLogService _audit;
    private readonly TimeProvider _time;

    public RescueService(
        AppDbContext db,
        IBusinessContext context,
        IExpiryService expiry,
        ISellThroughService sellThrough,
        IDeliveryService delivery,
        IAuditLogService audit,
        TimeProvider time)
    {
        _db = db;
        _context = context;
        _expiry = expiry;
        _sellThrough = sellThrough;
        _delivery = delivery;
        _audit = audit;
        _time = time;
    }

    private DateOnly Today => WibTime.TodayWib(_time);
    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    private bool TryGetSeller(out int businessId)
    {
        businessId = _context.BusinessId ?? 0;
        return _context.IsAuthenticated && _context.Role == RoleNames.Grossier && _context.BusinessId is not null;
    }

    private bool TryGetBuyer(out int businessId)
    {
        businessId = _context.BusinessId ?? 0;
        return _context.IsAuthenticated && _context.Role == RoleNames.Retail && _context.BusinessId is not null;
    }

    // ================================================================== grosir: buat paket

    public async Task<Result<int>> CreateBundleAsync(CreateBundleRequest request, CancellationToken ct = default)
    {
        if (!TryGetSeller(out var me))
        {
            return Result<int>.Fail(ErrorKind.Forbidden, "Hanya akun grosir yang dapat membuat paket anti-mubazir.");
        }

        var items = request.Items ?? Array.Empty<RescueItemInput>();
        if (items.Count == 0)
        {
            return Result<int>.Fail(ErrorKind.Validation, "Pilih minimal satu barang untuk paket.");
        }

        if (items.Count > MaxItems)
        {
            return Result<int>.Fail(ErrorKind.Validation, $"Satu paket maksimal berisi {MaxItems} barang.");
        }

        if (items.GroupBy(i => i.BatchId).Any(g => g.Count() > 1))
        {
            return Result<int>.Fail(ErrorKind.Validation, "Satu batch hanya boleh dipilih sekali dalam satu paket.");
        }

        if (items.Any(i => i.QtyBase is <= 0 or > 100_000))
        {
            return Result<int>.Fail(ErrorKind.Validation, "Jumlah per paket harus 1–100.000.");
        }

        var title = Clean(request.Title);
        if (title is { Length: < 3 or > 100 })
        {
            return Result<int>.Fail(ErrorKind.Validation, "Nama paket 3–100 karakter.");
        }

        var description = Clean(request.Description);
        if (description is { Length: > 300 })
        {
            return Result<int>.Fail(ErrorKind.Validation, "Deskripsi maksimal 300 karakter.");
        }

        var message = Clean(request.OfferMessage);
        if (message is { Length: > 200 })
        {
            return Result<int>.Fail(ErrorKind.Validation, "Pesan penawaran maksimal 200 karakter.");
        }

        var today = Today;
        var batches = (await _expiry.GetBatchesAsync(null, true, null, ct)).ToDictionary(b => b.BatchId);
        var lines = new List<RescueBundleItem>();
        foreach (var input in items)
        {
            if (!batches.TryGetValue(input.BatchId, out var batch))
            {
                return Result<int>.Fail(ErrorKind.NotFound, "Batch stok tidak ditemukan.");
            }

            if (batch.DaysLeft < 0)
            {
                return Result<int>.Fail(ErrorKind.Conflict, $"{batch.ProductName} sudah kadaluarsa dan tidak dapat dijual.");
            }

            if (batch.FreeBase < input.QtyBase)
            {
                return Result<int>.Fail(
                    ErrorKind.Conflict,
                    $"Stok {batch.ProductName} yang kadaluarsa {IdFormat.Date(batch.ExpiryDate)} hanya {batch.FreeBase} {batch.BaseUnitName} yang belum dicadangkan.");
            }

            if (batch.NormalPriceBase is not > 0)
            {
                return Result<int>.Fail(
                    ErrorKind.Validation, $"{batch.ProductName} belum punya harga grosir. Isi harga grosir di menu Produk lebih dulu.");
            }

            lines.Add(new RescueBundleItem
            {
                BusinessId = me,
                ProductId = batch.ProductId,
                BatchId = batch.BatchId,
                ProductName = batch.ProductName,
                BaseUnitName = batch.BaseUnitName,
                QtyBase = input.QtyBase,
                ExpiryDate = batch.ExpiryDate,
                NormalUnitPriceBase = batch.NormalPriceBase.Value
            });
        }

        var normal = Money.Round(lines.Sum(l => l.NormalUnitPriceBase * l.QtyBase));
        var earliest = lines.Min(l => l.ExpiryDate);
        var earliestDays = ExpiryBands.DaysLeft(earliest, today);

        decimal price;
        if (request.BundlePrice is decimal fixedPrice)
        {
            if (fixedPrice <= 0 || fixedPrice > normal)
            {
                return Result<int>.Fail(
                    ErrorKind.Validation, $"Harga paket harus lebih dari 0 dan tidak melebihi harga normal ({Rupiah.Format(normal)}).");
            }

            price = Money.Round(fixedPrice);
        }
        else
        {
            var percent = request.DiscountPercent ?? ExpiryBands.SuggestedDiscountPercent(earliestDays);
            if (percent is < 1 or > 90)
            {
                return Result<int>.Fail(ErrorKind.Validation, "Diskon harus 1–90%.");
            }

            price = Math.Min(normal, Math.Max(100m, Math.Round(normal * (100 - percent) / 100m / 100m, 0, MidpointRounding.AwayFromZero) * 100m));
        }

        var discount = normal <= 0 ? 0 : (int)Math.Round((1 - price / normal) * 100);

        var validUntil = request.ValidUntil ?? (earliest < today.AddDays(7) ? earliest : today.AddDays(7));
        if (validUntil < today)
        {
            return Result<int>.Fail(ErrorKind.Validation, "Tanggal berlaku tidak boleh di masa lalu.");
        }

        if (validUntil > earliest)
        {
            return Result<int>.Fail(
                ErrorKind.Validation,
                $"Paket tidak boleh berlaku melewati tanggal kadaluarsa barang terdekat ({IdFormat.Date(earliest)}).");
        }

        // Penerima penawaran (opsional): warung terverifikasi dan aktif.
        var targets = (request.OfferTargets ?? Array.Empty<OfferTarget>())
            .GroupBy(t => t.BusinessId)
            .Select(g => g.First())
            .ToList();
        if (targets.Count > MaxTargets)
        {
            return Result<int>.Fail(ErrorKind.Validation, $"Maksimal {MaxTargets} warung per penawaran.");
        }

        var buyers = new List<Business>();
        if (targets.Count > 0)
        {
            var ids = targets.Select(t => t.BusinessId).ToList();
            buyers = await _db.Businesses.AsNoTracking()
                .Where(b => ids.Contains(b.Id) && b.MarketRole == MarketRole.Retail
                            && b.VerificationStatus == VerificationStatus.Verified && b.IsActive)
                .ToListAsync(ct);
            if (buyers.Count != ids.Count)
            {
                return Result<int>.Fail(ErrorKind.Validation, "Ada warung tujuan yang tidak valid atau belum terverifikasi.");
            }
        }

        title ??= lines.Count == 1
            ? $"{lines[0].ProductName}, kadaluarsa {IdFormat.DateShort(earliest)}"
            : $"Paket {lines.Count} produk, kadaluarsa mulai {IdFormat.DateShort(earliest)}";
        if (title.Length > 100)
        {
            title = title[..100];
        }

        var now = Now;
        var bundle = new RescueBundle
        {
            BusinessId = me,
            Title = title,
            Description = description,
            Price = price,
            NormalPrice = normal,
            DiscountPercent = discount,
            ValidUntil = validUntil,
            IsPrivate = targets.Count > 0 && request.PrivateToTargets,
            CreatedAt = now
        };
        foreach (var line in lines)
        {
            bundle.Items.Add(line);
        }

        var seller = await _db.Businesses.AsNoTracking().FirstAsync(b => b.Id == me, ct);
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            _db.RescueBundles.Add(bundle);
            await _db.SaveChangesAsync(ct);

            foreach (var buyer in buyers)
            {
                var suggested = Math.Clamp(targets.First(t => t.BusinessId == buyer.Id).SuggestedBundles, 1, 10_000);
                _db.RescueOffers.Add(new RescueOffer
                {
                    BundleId = bundle.Id,
                    SellerBusinessId = me,
                    BuyerBusinessId = buyer.Id,
                    SellerName = seller.Name,
                    BuyerName = buyer.Name,
                    BundleTitle = bundle.Title,
                    Message = message,
                    SuggestedBundles = suggested,
                    Status = OfferStatus.Sent,
                    CreatedAt = now,
                    ExpiresAt = WibTime.WibToUtc(validUntil, 23, 59)
                });
            }

            _audit.Record("BUNDLE_CREATED", "RescueBundle", bundle.Id.ToString(), $"{bundle.Title}; harga={price}; diskon={discount}%; penawaran={buyers.Count}");
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            return Result<int>.Fail(ErrorKind.Unexpected, "Paket tidak dapat disimpan saat ini. Silakan coba lagi.");
        }

        return Result<int>.Ok(bundle.Id);
    }

    public async Task<IReadOnlyList<BundleDto>> ListMineAsync(bool includeClosed, CancellationToken ct = default)
    {
        if (!TryGetSeller(out _))
        {
            return Array.Empty<BundleDto>();
        }

        var bundles = await _db.RescueBundles.AsNoTracking().Include(b => b.Items)
            .OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.Id)
            .ToListAsync(ct);
        var dtos = await ToDtosAsync(bundles, withSold: true, sellers: null, ct);
        return includeClosed
            ? dtos
            : dtos.Where(d => d.State is BundleState.Active or BundleState.SoldOut).ToList();
    }

    public async Task<Result> EndBundleAsync(int bundleId, CancellationToken ct = default)
    {
        if (!TryGetSeller(out _))
        {
            return Result.Fail(ErrorKind.Forbidden, "Hanya akun grosir yang dapat menghentikan paket.");
        }

        var bundle = await _db.RescueBundles.FirstOrDefaultAsync(b => b.Id == bundleId, ct);
        if (bundle is null)
        {
            return Result.Fail(ErrorKind.NotFound, "Paket tidak ditemukan.");
        }

        if (bundle.IsEnded)
        {
            return Result.Ok();
        }

        var now = Now;
        bundle.IsEnded = true;
        bundle.EndedAt = now;
        _db.ForgetTracked<RescueOffer>(o => o.BundleId == bundleId);
        foreach (var offer in await _db.RescueOffers.Where(o => o.BundleId == bundleId && o.Status == OfferStatus.Sent).ToListAsync(ct))
        {
            offer.Status = OfferStatus.Withdrawn;
            offer.RespondedAt = now;
        }

        _audit.Record("BUNDLE_ENDED", "RescueBundle", bundle.Id.ToString(), bundle.Title);
        await _db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<IReadOnlyList<OfferDto>> ListSentAsync(CancellationToken ct = default)
    {
        if (!TryGetSeller(out var me))
        {
            return Array.Empty<OfferDto>();
        }

        var now = Now;
        var offers = await _db.RescueOffers.AsNoTracking()
            .Where(o => o.SellerBusinessId == me)
            .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .Take(200)
            .ToListAsync(ct);

        var bundleIds = offers.Select(o => o.BundleId).Distinct().ToList();
        var bundles = await _db.RescueBundles.AsNoTracking().Include(b => b.Items).Where(b => bundleIds.Contains(b.Id)).ToListAsync(ct);
        var dtos = (await ToDtosAsync(bundles, withSold: false, sellers: null, ct)).ToDictionary(d => d.Id);

        return offers
            .Select(o => ToOfferDto(o, now, dtos.TryGetValue(o.BundleId, out var bundle) ? bundle : null))
            .ToList();
    }

    public async Task<Result> WithdrawOfferAsync(int offerId, CancellationToken ct = default)
    {
        if (!TryGetSeller(out var me))
        {
            return Result.Fail(ErrorKind.Forbidden, "Hanya akun grosir yang dapat menarik penawaran.");
        }

        _db.ForgetTracked<RescueOffer>(o => o.Id == offerId);
        var offer = await _db.RescueOffers.FirstOrDefaultAsync(o => o.Id == offerId && o.SellerBusinessId == me, ct);
        if (offer is null)
        {
            return Result.Fail(ErrorKind.NotFound, "Penawaran tidak ditemukan.");
        }

        if (offer.Status != OfferStatus.Sent)
        {
            return Result.Fail(ErrorKind.Conflict, "Penawaran ini sudah dijawab dan tidak dapat ditarik.");
        }

        offer.Status = OfferStatus.Withdrawn;
        offer.RespondedAt = Now;
        _audit.Record("OFFER_WITHDRAWN", "RescueOffer", offer.Id.ToString(), offer.BuyerName);
        await _db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    // ================================================================== grosir: dashboard

    public async Task<RescueDashboardDto> GetDashboardAsync(CancellationToken ct = default)
    {
        var emptyBand = new ExpiryBandTotal(0, 0, 0m);
        var empty = new RescueDashboardDto(
            new ExpirySummaryDto(emptyBand, emptyBand, emptyBand, emptyBand, emptyBand, 0),
            Array.Empty<RescueCandidateDto>(), Array.Empty<BatchDto>(),
            new RescueStatsDto(0, 0, 0, 0, 0m, 0m, 0, 0m), 0);
        if (!TryGetSeller(out var me))
        {
            return empty;
        }

        var now = Now;
        var since = now.AddDays(-30);
        var summary = await _expiry.GetSummaryAsync(ct);
        var batches = await _expiry.GetBatchesAsync(30, true, null, ct);
        var demand = (await _sellThrough.GetDemandAsync(30, null, ct)).ToDictionary(d => d.ProductId);
        var hidden = await _sellThrough.CountHiddenCustomersAsync(ct);

        // Paket aktif dan penawaran terbuka, dipetakan per batch.
        var mine = await ListMineAsync(false, ct);
        var activeBundles = mine.Where(b => b.State == BundleState.Active).ToList();
        var bundleByBatch = new Dictionary<int, int>();
        foreach (var bundle in activeBundles)
        {
            foreach (var item in bundle.Items)
            {
                bundleByBatch.TryAdd(item.BatchId, bundle.Id);
            }
        }

        var openOffers = await _db.RescueOffers.AsNoTracking()
            .Where(o => o.SellerBusinessId == me && o.Status == OfferStatus.Sent && o.ExpiresAt > now)
            .ToListAsync(ct);
        var buyersByBatch = new Dictionary<int, HashSet<int>>();
        foreach (var offer in openOffers)
        {
            var bundle = mine.FirstOrDefault(b => b.Id == offer.BundleId);
            if (bundle is null)
            {
                continue;
            }

            foreach (var item in bundle.Items)
            {
                if (!buyersByBatch.TryGetValue(item.BatchId, out var set))
                {
                    buyersByBatch[item.BatchId] = set = new HashSet<int>();
                }

                set.Add(offer.BuyerBusinessId);
            }
        }

        var candidates = new List<RescueCandidateDto>();
        foreach (var batch in batches.Where(b => b.DaysLeft >= 0))
        {
            var discount = ExpiryBands.SuggestedDiscountPercent(batch.DaysLeft);
            decimal? unitPrice = batch.NormalPriceBase is decimal normal ? Money.RoundCost(normal * (100 - discount) / 100m) : null;

            var targets = new List<RescueTargetDto>();
            if (demand.TryGetValue(batch.ProductId, out var productDemand))
            {
                var sellWindow = Math.Max(batch.DaysLeft - 3, 1); // sisakan 3 hari agar warung masih sempat menjual
                foreach (var w in productDemand.Warungs.Where(x => x.SoldQtyBase > 0).OrderByDescending(x => x.AvgPerDay).Take(4))
                {
                    var canSell = (int)Math.Floor(w.AvgPerDay * sellWindow);
                    var suggested = Math.Min(batch.QtyBase, Math.Max(canSell - w.StockAtWarungBase, 0));
                    if (suggested < 1)
                    {
                        continue;
                    }

                    targets.Add(new RescueTargetDto(
                        w.BusinessId, w.Name, w.Address, w.DistanceKm, w.SoldQtyBase, w.AvgPerDay,
                        w.StockAtWarungBase, w.DaysOfCover, suggested,
                        buyersByBatch.TryGetValue(batch.BatchId, out var set) && set.Contains(w.BusinessId)));
                }
            }

            candidates.Add(new RescueCandidateDto(
                batch, discount, unitPrice,
                bundleByBatch.TryGetValue(batch.BatchId, out var bundleId) ? bundleId : null,
                targets));
        }

        // Statistik 30 hari: barang yang terselamatkan lewat paket vs yang terbuang karena kadaluarsa.
        var soldOrders = await _db.Orders.AsNoTracking().Include(o => o.Items)
            .Where(o => o.SellerBusinessId == me && o.CreatedAt >= since && SoldStatuses.Contains(o.Status))
            .ToListAsync(ct);
        var rescueItems = soldOrders.SelectMany(o => o.Items).Where(i => i.BundleId != null).ToList();

        var written = await _db.InventoryMovements.AsNoTracking()
            .Where(m => m.Type == MovementType.ExpiredOut && m.CreatedAt >= since)
            .ToListAsync(ct);

        var stats = new RescueStatsDto(
            activeBundles.Count,
            openOffers.Count,
            await _db.RescueOffers.AsNoTracking().CountAsync(o => o.SellerBusinessId == me && o.Status == OfferStatus.Accepted && o.RespondedAt >= since, ct),
            rescueItems.Sum(i => i.QtyBase),
            rescueItems.Sum(i => i.Subtotal),
            rescueItems.Sum(i => (i.NormalSubtotal ?? i.Subtotal) - i.Subtotal),
            written.Sum(m => -m.QtyBaseSigned),
            Money.Round(written.Sum(m => -m.QtyBaseSigned * m.UnitCost)));

        return new RescueDashboardDto(
            summary,
            candidates.OrderBy(c => c.Batch.DaysLeft).ThenByDescending(c => c.Batch.ValueAtCost).ToList(),
            batches.Where(b => b.Band == ExpiryBand.Expired).ToList(),
            stats,
            hidden);
    }

    // ================================================================== warung: promo & penawaran

    public async Task<IReadOnlyList<BundleDto>> ListPublicAsync(int? sellerBusinessId = null, CancellationToken ct = default)
    {
        if (!TryGetBuyer(out _))
        {
            return Array.Empty<BundleDto>();
        }

        var today = Today;
        var sellerQuery = _db.Businesses.AsNoTracking()
            .Where(b => b.MarketRole == MarketRole.Wholesale && b.VerificationStatus == VerificationStatus.Verified && b.IsActive);
        if (sellerBusinessId is int only)
        {
            sellerQuery = sellerQuery.Where(b => b.Id == only);
        }

        var sellerIds = await sellerQuery.Select(b => b.Id).ToListAsync(ct);
        if (sellerIds.Count == 0)
        {
            return Array.Empty<BundleDto>();
        }

        // Pembacaan lintas usaha yang disengaja: promo publik memang untuk dilihat semua warung.
        var bundles = await _db.RescueBundles.IgnoreQueryFilters().AsNoTracking().Include(b => b.Items)
            .Where(b => !b.IsEnded && !b.IsPrivate && b.ValidUntil >= today && sellerIds.Contains(b.BusinessId))
            .ToListAsync(ct);

        var sellers = await LoadSellersAsync(sellerIds, ct);
        var dtos = await ToDtosAsync(bundles, withSold: false, sellers, ct);
        return dtos
            .Where(d => d.State == BundleState.Active)
            .OrderBy(d => d.EarliestExpiryDays)
            .ThenByDescending(d => d.DiscountPercent)
            .ToList();
    }

    public async Task<Result<BundleDto>> GetForBuyerAsync(int bundleId, CancellationToken ct = default)
    {
        if (!TryGetBuyer(out var me))
        {
            return Result<BundleDto>.Fail(ErrorKind.NotFound, "Paket tidak ditemukan.");
        }

        var bundle = await _db.RescueBundles.IgnoreQueryFilters().AsNoTracking().Include(b => b.Items)
            .FirstOrDefaultAsync(b => b.Id == bundleId, ct);
        if (bundle is null)
        {
            return Result<BundleDto>.Fail(ErrorKind.NotFound, "Paket tidak ditemukan.");
        }

        var sellerOk = await _db.Businesses.AsNoTracking().AnyAsync(
            b => b.Id == bundle.BusinessId && b.MarketRole == MarketRole.Wholesale
                 && b.VerificationStatus == VerificationStatus.Verified && b.IsActive, ct);
        if (!sellerOk)
        {
            return Result<BundleDto>.Fail(ErrorKind.NotFound, "Paket tidak ditemukan.");
        }

        // Paket privat hanya terlihat oleh warung yang menerima penawarannya (filter dua pihak: hanya penawaran milik sendiri).
        if (bundle.IsPrivate && !await _db.RescueOffers.AsNoTracking().AnyAsync(o => o.BundleId == bundleId && o.BuyerBusinessId == me, ct))
        {
            return Result<BundleDto>.Fail(ErrorKind.NotFound, "Paket tidak ditemukan.");
        }

        var sellers = await LoadSellersAsync(new[] { bundle.BusinessId }, ct);
        var dto = (await ToDtosAsync(new[] { bundle }, withSold: false, sellers, ct)).Single();
        return Result<BundleDto>.Ok(dto);
    }

    public async Task<IReadOnlyList<OfferDto>> ListReceivedAsync(bool includeClosed, CancellationToken ct = default)
    {
        if (!TryGetBuyer(out var me))
        {
            return Array.Empty<OfferDto>();
        }

        var now = Now;
        var offers = await _db.RescueOffers.AsNoTracking()
            .Where(o => o.BuyerBusinessId == me)
            .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .Take(100)
            .ToListAsync(ct);

        var bundleIds = offers.Select(o => o.BundleId).Distinct().ToList();
        var bundles = await _db.RescueBundles.IgnoreQueryFilters().AsNoTracking().Include(b => b.Items)
            .Where(b => bundleIds.Contains(b.Id))
            .ToListAsync(ct);
        var sellers = await LoadSellersAsync(bundles.Select(b => b.BusinessId).Distinct().ToList(), ct);
        var dtos = (await ToDtosAsync(bundles, withSold: false, sellers, ct)).ToDictionary(d => d.Id);

        var result = offers
            .Select(o => ToOfferDto(o, now, dtos.TryGetValue(o.BundleId, out var bundle) ? bundle : null))
            .ToList();

        return includeClosed ? result : result.Where(o => o.State == OfferState.Sent).ToList();
    }

    public async Task<Result> DeclineOfferAsync(int offerId, CancellationToken ct = default)
    {
        if (!TryGetBuyer(out var me))
        {
            return Result.Fail(ErrorKind.Forbidden, "Hanya akun warung yang dapat menjawab penawaran.");
        }

        _db.ForgetTracked<RescueOffer>(o => o.Id == offerId);
        var offer = await _db.RescueOffers.FirstOrDefaultAsync(o => o.Id == offerId && o.BuyerBusinessId == me, ct);
        if (offer is null)
        {
            return Result.Fail(ErrorKind.NotFound, "Penawaran tidak ditemukan.");
        }

        if (offer.Status != OfferStatus.Sent)
        {
            return Result.Fail(ErrorKind.Conflict, "Penawaran ini sudah dijawab.");
        }

        offer.Status = OfferStatus.Declined;
        offer.RespondedAt = Now;
        _audit.Record("OFFER_DECLINED", "RescueOffer", offer.Id.ToString(), offer.BundleTitle);
        await _db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<int> CountOpenOffersAsync(CancellationToken ct = default)
    {
        if (!TryGetBuyer(out var me))
        {
            return 0;
        }

        var now = Now;
        return await _db.RescueOffers.AsNoTracking()
            .CountAsync(o => o.BuyerBusinessId == me && o.Status == OfferStatus.Sent && o.ExpiresAt > now, ct);
    }

    public async Task<IReadOnlyDictionary<int, (int Deals, int BestDiscountPercent)>> GetDealSummaryAsync(CancellationToken ct = default)
    {
        var result = new Dictionary<int, (int Deals, int BestDiscountPercent)>();
        foreach (var group in (await ListPublicAsync(null, ct)).Where(d => d.Seller is not null).GroupBy(d => d.Seller!.BusinessId))
        {
            result[group.Key] = (group.Count(), group.Max(d => d.DiscountPercent));
        }

        return result;
    }

    // ================================================================== dipakai OrderService

    public async Task<Result<BundleOrderInfo>> ResolveForOrderAsync(int bundleId, int qty, int? offerId, CancellationToken ct = default)
    {
        if (!TryGetBuyer(out var me))
        {
            return Result<BundleOrderInfo>.Fail(ErrorKind.Forbidden, "Hanya akun warung yang dapat memesan paket.");
        }

        if (qty is <= 0 or > 10_000)
        {
            return Result<BundleOrderInfo>.Fail(ErrorKind.Validation, "Jumlah paket harus 1–10.000.");
        }

        var found = await GetForBuyerAsync(bundleId, ct);
        if (!found.Succeeded)
        {
            return Result<BundleOrderInfo>.Fail(ErrorKind.NotFound, "Paket tidak ditemukan atau tidak tersedia untuk Anda.");
        }

        var bundle = found.Value!;
        if (bundle.State != BundleState.Active)
        {
            return Result<BundleOrderInfo>.Fail(ErrorKind.Conflict, $"Paket \"{bundle.Title}\" sudah tidak tersedia ({RescueLabels.Bundle(bundle.State).ToLowerInvariant()}).");
        }

        if (qty > bundle.Available)
        {
            return Result<BundleOrderInfo>.Fail(ErrorKind.Conflict, $"Stok paket \"{bundle.Title}\" tidak cukup (tersedia {bundle.Available} paket).");
        }

        if (offerId is int oid)
        {
            var offer = await _db.RescueOffers.AsNoTracking().FirstOrDefaultAsync(o => o.Id == oid && o.BuyerBusinessId == me, ct);
            if (offer is null || offer.BundleId != bundleId || offer.Status != OfferStatus.Sent || offer.ExpiresAt < Now)
            {
                return Result<BundleOrderInfo>.Fail(ErrorKind.Conflict, "Penawaran ini sudah tidak berlaku.");
            }
        }

        var productIds = bundle.Items.Select(i => i.ProductId).ToList();
        var products = await _db.Products.IgnoreQueryFilters().AsNoTracking().Include(p => p.Units)
            .Where(p => productIds.Contains(p.Id) && p.BusinessId == bundle.Seller!.BusinessId && p.IsActive)
            .ToListAsync(ct);

        var total = Money.Round(bundle.Price * qty);
        var normalTotal = Money.Round(bundle.NormalPrice * qty);
        var weights = bundle.Items.Select(i => i.NormalUnitPriceBase * i.QtyBase).ToList();
        var weightSum = weights.Sum();

        var lines = new List<BundleOrderLine>();
        var remaining = total;
        var remainingNormal = normalTotal;
        for (var index = 0; index < bundle.Items.Count; index++)
        {
            var item = bundle.Items[index];
            var product = products.FirstOrDefault(p => p.Id == item.ProductId);
            if (product is null)
            {
                return Result<BundleOrderInfo>.Fail(ErrorKind.Conflict, $"{item.ProductName} sudah tidak dijual oleh grosir.");
            }

            var last = index == bundle.Items.Count - 1;
            var share = weightSum <= 0 ? 1m / bundle.Items.Count : weights[index] / weightSum;
            var subtotal = last ? remaining : Money.Round(total * share);
            var normalSubtotal = last ? remainingNormal : Money.Round(normalTotal * share);
            remaining -= subtotal;
            remainingNormal -= normalSubtotal;

            var qtyBase = item.QtyBase * qty;
            lines.Add(new BundleOrderLine(
                product.Id, product.Name, product.Sku, product.CategoryId, product.BaseUnitName,
                string.Join("|", product.Units.OrderBy(u => u.ConversionToBase).Select(u => $"{u.UnitName}:{u.ConversionToBase}")),
                item.BatchId, item.ExpiryDate, qtyBase,
                Money.RoundCost(subtotal / qtyBase), subtotal, normalSubtotal));
        }

        return Result<BundleOrderInfo>.Ok(new BundleOrderInfo(bundle.Id, bundle.Title, bundle.Seller!.BusinessId, qty, total, lines));
    }

    public async Task<Result> MarkOfferAcceptedAsync(int offerId, int orderId, CancellationToken ct = default)
    {
        if (!TryGetBuyer(out var me))
        {
            return Result.Fail(ErrorKind.Forbidden, "Hanya akun warung yang dapat menerima penawaran.");
        }

        _db.ForgetTracked<RescueOffer>(o => o.Id == offerId);
        var offer = await _db.RescueOffers.FirstOrDefaultAsync(o => o.Id == offerId && o.BuyerBusinessId == me, ct);
        if (offer is null || offer.Status != OfferStatus.Sent)
        {
            return Result.Fail(ErrorKind.Conflict, "Penawaran ini sudah tidak berlaku.");
        }

        offer.Status = OfferStatus.Accepted;
        offer.RespondedAt = Now;
        offer.OrderId = orderId;
        _audit.Record("OFFER_ACCEPTED", "RescueOffer", offer.Id.ToString(), $"order={orderId}");
        await _db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    // ================================================================== helper

    private OfferDto ToOfferDto(RescueOffer o, DateTime now, BundleDto? bundle) => new(
        o.Id, o.BundleId, o.BundleTitle, o.SellerBusinessId, o.SellerName, o.BuyerBusinessId, o.BuyerName,
        o.Message, o.SuggestedBundles, StateOf(o, now), o.CreatedAt, o.ExpiresAt, o.RespondedAt, o.OrderId, bundle);

    private static OfferState StateOf(RescueOffer o, DateTime now) => o.Status switch
    {
        OfferStatus.Accepted => OfferState.Accepted,
        OfferStatus.Declined => OfferState.Declined,
        OfferStatus.Withdrawn => OfferState.Withdrawn,
        _ => o.ExpiresAt < now ? OfferState.Expired : OfferState.Sent
    };

    private async Task<Dictionary<int, DealSellerDto>> LoadSellersAsync(IReadOnlyCollection<int> sellerIds, CancellationToken ct)
    {
        var origin = _context.BusinessId is int mine
            ? await _db.Businesses.AsNoTracking().FirstOrDefaultAsync(b => b.Id == mine, ct)
            : null;
        var businesses = await _db.Businesses.AsNoTracking().Where(b => sellerIds.Contains(b.Id)).ToListAsync(ct);
        var delivery = await _delivery.GetPublicAsync(sellerIds, ct);
        var quotes = await _delivery.QuoteForBuyerAsync(sellerIds, ct);

        var result = new Dictionary<int, DealSellerDto>();
        foreach (var b in businesses)
        {
            double? distance = origin?.Latitude is double la && origin.Longitude is double lo && b.Latitude is double bla && b.Longitude is double blo
                ? Math.Round(Geo.DistanceKm(la, lo, bla, blo), 2)
                : null;
            var option = delivery.TryGetValue(b.Id, out var d) ? d : null;
            var quote = quotes.GetValueOrDefault(b.Id);
            var canDeliver = (option?.OffersDelivery ?? false) && (quote?.Available ?? true);
            result[b.Id] = new DealSellerDto(
                b.Id, b.Name, b.Address, b.Latitude, b.Longitude, distance,
                canDeliver, option?.OffersPickup ?? true, quote?.Fee ?? option?.DeliveryFee ?? 0m,
                quote?.RoadKm, quote?.Reason, option?.MinOrderAmount ?? 0m);
        }

        return result;
    }

    private async Task<List<BundleDto>> ToDtosAsync(
        IReadOnlyList<RescueBundle> bundles, bool withSold, IReadOnlyDictionary<int, DealSellerDto>? sellers, CancellationToken ct)
    {
        if (bundles.Count == 0)
        {
            return new List<BundleDto>();
        }

        var today = Today;
        var batchIds = bundles.SelectMany(b => b.Items).Select(i => i.BatchId).Distinct().ToList();
        var batches = await _db.StockBatches.IgnoreQueryFilters().AsNoTracking()
            .Where(b => batchIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, ct);
        var sold = withSold ? await SoldCountsAsync(ct) : new Dictionary<int, int>();

        var result = new List<BundleDto>();
        foreach (var bundle in bundles)
        {
            var items = bundle.Items
                .OrderBy(i => i.ExpiryDate).ThenBy(i => i.Id)
                .Select(i => new BundleItemDto(
                    i.ProductId, i.ProductName, i.BaseUnitName, i.QtyBase, i.ExpiryDate,
                    ExpiryBands.DaysLeft(i.ExpiryDate, today), i.NormalUnitPriceBase, i.BatchId,
                    batches.TryGetValue(i.BatchId, out var batch) ? Math.Max(0, batch.QtyRemainingBase - batch.ReservedBase) : 0))
                .ToList();

            var available = items.Count == 0 ? 0 : items.Min(i => i.QtyBase <= 0 ? 0 : i.BatchRemainingBase / i.QtyBase);
            var earliest = items.Count == 0 ? bundle.ValidUntil : items.Min(i => i.ExpiryDate);
            var state = bundle.IsEnded ? BundleState.Ended
                : (bundle.ValidUntil < today || items.Any(i => i.DaysLeft < 0)) ? BundleState.Expired
                : available <= 0 ? BundleState.SoldOut
                : BundleState.Active;

            result.Add(new BundleDto(
                bundle.Id, bundle.Title, bundle.Description, bundle.Price, bundle.NormalPrice, bundle.DiscountPercent,
                bundle.ValidUntil, earliest, ExpiryBands.DaysLeft(earliest, today), available,
                sold.TryGetValue(bundle.Id, out var s) ? s : 0, bundle.IsPrivate, state, bundle.CreatedAt, items,
                sellers is not null && sellers.TryGetValue(bundle.BusinessId, out var seller) ? seller : null));
        }

        return result;
    }

    // Jumlah paket terjual per paket (hanya pesanan milik penjual yang sedang login karena filter dua pihak).
    private async Task<Dictionary<int, int>> SoldCountsAsync(CancellationToken ct)
    {
        var orders = await _db.Orders.AsNoTracking().Include(o => o.Items)
            .Where(o => SoldStatuses.Contains(o.Status) && o.Items.Any(i => i.BundleId != null))
            .ToListAsync(ct);

        var result = new Dictionary<int, int>();
        foreach (var order in orders)
        {
            foreach (var group in order.Items.Where(i => i.BundleId != null).GroupBy(i => i.BundleId!.Value))
            {
                result[group.Key] = (result.TryGetValue(group.Key, out var current) ? current : 0) + (group.First().BundleQty ?? 0);
            }
        }

        return result;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
