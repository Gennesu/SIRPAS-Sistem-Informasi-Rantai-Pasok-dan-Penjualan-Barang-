using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Web.Modules.Catalog.Contracts;
using Platform.Web.Modules.Insights.Contracts;
using Platform.Web.Modules.Rescue.Contracts;
using Platform.Web.Modules.Rescue.ViewModels;
using Platform.Web.Modules.Stock.Contracts;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Rescue.Controllers;

/// <summary>
/// Dashboard food waste. Grosir: batch berisiko, rekomendasi warung, paket, penawaran. Warung: stok sendiri yang mendekati kadaluarsa
/// (plus promo untuk pembeli).
/// </summary>
[Authorize(Roles = RoleNames.Retail + "," + RoleNames.Grossier)]
public class RescueController : Controller
{
    private readonly IRescueService _rescue;
    private readonly IExpiryService _expiry;
    private readonly ISellThroughService _sellThrough;
    private readonly IProductService _products;
    private readonly TimeProvider _time;

    public RescueController(
        IRescueService rescue, IExpiryService expiry, ISellThroughService sellThrough, IProductService products, TimeProvider time)
    {
        _rescue = rescue;
        _expiry = expiry;
        _sellThrough = sellThrough;
        _products = products;
        _time = time;
    }

    /// <summary>Dampak food waste: terjual, tersalurkan (promo/paket), terbuang, dan kerugiannya, dari data transaksi nyata.</summary>
    public async Task<IActionResult> Impact([FromServices] IFoodWasteService foodWaste, int days = 30, CancellationToken ct = default)
    {
        days = days is 7 or 30 or 90 ? days : 30;
        var to = WibTime.TodayWib(TimeProvider.System);
        ViewData["Days"] = days;
        return View(await foodWaste.GetSummaryAsync(to.AddDays(-(days - 1)), to, ct));
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (User.IsInRole(RoleNames.Grossier))
        {
            return View(await _rescue.GetDashboardAsync(ct));
        }

        var batches = await _expiry.GetBatchesAsync(null, true, null, ct);
        var offers = await _rescue.ListReceivedAsync(false, ct);
        var deals = (await _rescue.ListPublicAsync(null, ct)).Take(3).ToList();
        return View("RetailIndex", new RetailRescueViewModel(await _expiry.GetSummaryAsync(ct), batches, offers, deals));
    }

    // ------------------------------------------------------------------ grosir: paket & penawaran

    [Authorize(Roles = RoleNames.Grossier)]
    public async Task<IActionResult> Bundles(string? tab, CancellationToken ct)
    {
        tab = tab is "offers" or "history" ? tab : "active";
        var bundles = await _rescue.ListMineAsync(tab == "history", ct);
        if (tab == "history")
        {
            bundles = bundles.Where(b => b.State is BundleState.Expired or BundleState.Ended).ToList();
        }

        return View(new BundlesViewModel(bundles, await _rescue.ListSentAsync(ct), tab));
    }

    [Authorize(Roles = RoleNames.Grossier)]
    [HttpGet]
    public async Task<IActionResult> Compose(int? batchId, [FromQuery(Name = "to")] int[]? to, CancellationToken ct)
    {
        var model = await BuildComposeAsync(new ComposeInput(), batchId, to, ct);
        return View(model);
    }

    [Authorize(Roles = RoleNames.Grossier)]
    [HttpPost]
    public async Task<IActionResult> Compose(ComposeInput input, CancellationToken ct)
    {
        var items = input.Items.Where(i => i.QtyBase > 0).Select(i => new RescueItemInput(i.BatchId, i.QtyBase)).ToList();
        var targets = input.Targets.Where(t => t.Selected).Select(t => new OfferTarget(t.BusinessId, t.SuggestedBundles)).ToList();

        if (ModelState.IsValid)
        {
            var result = await _rescue.CreateBundleAsync(
                new CreateBundleRequest(
                    input.Title, input.Description, items, input.BundlePrice, input.BundlePrice is null ? input.DiscountPercent : null,
                    input.ValidUntil, targets, input.Message, input.PrivateToTargets && targets.Count > 0),
                ct);
            if (result.Succeeded)
            {
                TempData["Success"] = targets.Count > 0
                    ? $"Paket dibuat dan penawaran terkirim ke {targets.Count} warung."
                    : "Paket dibuat dan tayang di marketplace.";
                return RedirectToAction(nameof(Bundles), new { tab = targets.Count > 0 ? "offers" : "active" });
            }

            ModelState.AddModelError(string.Empty, result.Error ?? "Paket gagal dibuat.");
        }

        var focus = items.FirstOrDefault()?.BatchId;
        return View(await BuildComposeAsync(input, focus, targets.Select(t => t.BusinessId).ToArray(), ct));
    }

    [Authorize(Roles = RoleNames.Grossier)]
    [HttpPost]
    public async Task<IActionResult> EndBundle(int id, CancellationToken ct)
    {
        var result = await _rescue.EndBundleAsync(id, ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Paket dihentikan. Penawaran yang terbuka ditarik." : result.Error;
        return RedirectToAction(nameof(Bundles));
    }

    [Authorize(Roles = RoleNames.Grossier)]
    [HttpPost]
    public async Task<IActionResult> WithdrawOffer(int id, CancellationToken ct)
    {
        var result = await _rescue.WithdrawOfferAsync(id, ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Penawaran ditarik." : result.Error;
        return RedirectToAction(nameof(Bundles), new { tab = "offers" });
    }

    /// <summary>Membuang sisa batch (kadaluarsa) dari stok. Dipakai grosir maupun warung.</summary>
    [HttpPost]
    public async Task<IActionResult> WriteOff(int batchId, CancellationToken ct)
    {
        var result = await _expiry.WriteOffAsync(batchId, null, ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Batch dikeluarkan dari stok dan tercatat sebagai barang kadaluarsa." : result.Error;
        return RedirectToAction(nameof(Index));
    }

    // ------------------------------------------------------------------ composer: data bantu

    private async Task<ComposeViewModel> BuildComposeAsync(ComposeInput input, int? focusBatchId, int[]? preselected, CancellationToken ct)
    {
        var today = WibTime.TodayWib(_time);
        var batches = (await _expiry.GetBatchesAsync(null, false, null, ct))
            .Where(b => b.NormalPriceBase is > 0)
            .OrderBy(b => b.DaysLeft)
            .ToList();

        var productUnits = (await _products.ListAsync(null, false, ct)).ToDictionary(p => p.Id, p => p.Units);
        var options = new List<ComposeBatchOption>();
        foreach (var batch in batches)
        {
            var packs = productUnits.TryGetValue(batch.ProductId, out var units)
                ? units.Where(u => !u.IsBase && u.ConversionToBase > 1 && u.ConversionToBase <= batch.QtyBase).OrderByDescending(u => u.ConversionToBase).ToList()
                : new List<ProductUnitDto>();
            var pack = packs.FirstOrDefault();
            var defaultQty = pack?.ConversionToBase ?? Math.Min(batch.QtyBase, 1);
            var hint = pack is null ? $"per {batch.BaseUnitName}" : $"1 {pack.UnitName} = {pack.ConversionToBase} {batch.BaseUnitName}";
            options.Add(new ComposeBatchOption(batch, defaultQty, hint, batch.NormalPriceBase!.Value));
        }

        var focus = focusBatchId is int id ? options.FirstOrDefault(o => o.Batch.BatchId == id) : null;

        // Isian awal (bukan saat gagal kirim): batch fokus ikut dengan jumlah satu kemasan.
        if (input.Items.Count == 0)
        {
            input.Items = options.Select(o => new ComposeItemInput { BatchId = o.Batch.BatchId, QtyBase = o.Batch.BatchId == focus?.Batch.BatchId ? o.DefaultQty : 0 }).ToList();
        }
        else
        {
            // Pastikan semua batch punya baris (urutan sama dengan tampilan).
            var existing = input.Items.ToDictionary(i => i.BatchId, i => i.QtyBase);
            input.Items = options.Select(o => new ComposeItemInput { BatchId = o.Batch.BatchId, QtyBase = existing.GetValueOrDefault(o.Batch.BatchId) }).ToList();
        }

        if (input.DiscountPercent is null && input.BundlePrice is null)
        {
            input.DiscountPercent = ExpiryBands.SuggestedDiscountPercent(focus?.Batch.DaysLeft ?? 14);
        }

        if (input.ValidUntil is null)
        {
            var earliest = focus is null ? today.AddDays(7) : focus.Batch.ExpiryDate;
            input.ValidUntil = earliest < today.AddDays(7) ? earliest : today.AddDays(7);
        }

        // Daftar warung untuk batch fokus: urut menurut kecepatan jual, dengan saran jumlah.
        var rows = new List<ComposeTargetRow>();
        if (focus is not null)
        {
            var demand = (await _sellThrough.GetDemandAsync(30, focus.Batch.ProductId, ct)).FirstOrDefault();
            if (demand is not null)
            {
                var window = Math.Max(focus.Batch.DaysLeft - 3, 1);
                foreach (var w in demand.Warungs.Where(x => x.SoldQtyBase > 0).OrderByDescending(x => x.AvgPerDay))
                {
                    var canSell = (int)Math.Floor(w.AvgPerDay * window);
                    var qty = Math.Min(focus.Batch.QtyBase, Math.Max(canSell - w.StockAtWarungBase, 0));
                    var bundles = Math.Max(1, (int)Math.Round(qty / (double)Math.Max(focus.DefaultQty, 1)));
                    rows.Add(new ComposeTargetRow(w.BusinessId, w.Name, w.DistanceKm, w.SoldQtyBase, w.AvgPerDay, w.StockAtWarungBase, qty, bundles));
                }
            }
        }

        var chosen = new HashSet<int>(preselected ?? Array.Empty<int>());
        if (input.Targets.Count == 0)
        {
            input.Targets = rows.Select(r => new ComposeTargetInput { BusinessId = r.BusinessId, Selected = chosen.Contains(r.BusinessId), SuggestedBundles = r.SuggestedBundles }).ToList();
        }
        else
        {
            var existing = input.Targets.ToDictionary(t => t.BusinessId);
            input.Targets = rows.Select(r => existing.TryGetValue(r.BusinessId, out var t) ? t : new ComposeTargetInput { BusinessId = r.BusinessId, SuggestedBundles = r.SuggestedBundles }).ToList();
        }

        return new ComposeViewModel
        {
            Input = input,
            Batches = options,
            Targets = rows,
            FocusBatchId = focus?.Batch.BatchId,
            FocusProduct = focus?.Batch.ProductName,
            HiddenCustomers = await _sellThrough.CountHiddenCustomersAsync(ct),
            Today = today
        };
    }
}
