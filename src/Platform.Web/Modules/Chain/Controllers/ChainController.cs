using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Web.Modules.Accounting.Contracts;
using Platform.Web.Modules.Accounting.ViewModels;
using Platform.Web.Modules.BusinessProfile.Contracts;
using Platform.Web.Modules.Catalog.Contracts;
using Platform.Web.Modules.Orders.Contracts;
using Platform.Web.Modules.Orders.Domain;
using Platform.Web.Modules.Stock.Contracts;
using Platform.Web.Modules.Stock.Domain;
using Platform.Web.Modules.Chain.ViewModels;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Chain.Controllers;

/// <summary>Satu halaman struktur aktor (judul, ringkasan angka, tabel, catatan).</summary>
public sealed class ChainPage
{
    public string Title { get; set; } = string.Empty;
    public string? Sub { get; set; }
    public List<string[]>? K { get; set; }
    public List<string>? H { get; set; }
    public List<string[]>? R { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// Halaman peternakan. Angka dihitung dari buku besar stok (InventoryMovement) lewat IInventoryService.
/// Alurnya: catat produksi/panen (CatatProduksi) -> stok bertambah -> terlihat di Stok dan Dashboard.
/// </summary>
[Authorize(Roles = RoleNames.Livestock)]
public class ChainController : Controller
{
    private const string ProductionReason = "Hasil produksi";

    private readonly IInventoryService _inventory;
    private readonly IProductService _products;
    private readonly IOrderService _orders;
    private readonly IBusinessService _businesses;
    private readonly IExpenseService _expenses;
    private readonly TimeProvider _time;

    public ChainController(
        IInventoryService inventory,
        IProductService products,
        IOrderService orders,
        IBusinessService businesses,
        IExpenseService expenses,
        TimeProvider time)
    {
        _inventory = inventory;
        _products = products;
        _orders = orders;
        _businesses = businesses;
        _expenses = expenses;
        _time = time;
    }

    public Task<IActionResult> Dashboard(CancellationToken ct) => Render("dashboard", ct);
    public Task<IActionResult> Produksi(CancellationToken ct) => Render("produksi", ct);
    public Task<IActionResult> Stok(CancellationToken ct) => Render("stok", ct);
    public async Task<IActionResult> Keuangan(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var today = WibTime.TodayWib(_time);
        var start = from ?? new DateOnly(today.Year, today.Month, 1);
        var end = to ?? today;
        if (end < start) (start, end) = (end, start);

        var orders = await _orders.ListAsync(null, 1000, ct);
        DateOnly OrderDate(OrderSummaryDto o) => DateOnly.FromDateTime(WibTime.ToWib(o.CompletedAt ?? o.CreatedAt));
        var completed = orders.Where(o => o.Status == OrderStatus.Completed && OrderDate(o) >= start && OrderDate(o) <= end).ToList();
        var pending = orders.Where(o => (o.Status is OrderStatus.Delivered or OrderStatus.PickedUp)
                && DateOnly.FromDateTime(WibTime.ToWib(o.CreatedAt)) >= start
                && DateOnly.FromDateTime(WibTime.ToWib(o.CreatedAt)) <= end)
            .ToList();
        var expenseRows = await _expenses.ListAsync(start, end, ct);
        var movements = await _inventory.GetMovementsAsync(null, 1000, ct);
        var stock = await _inventory.GetStockAsync(null, ct);
        var productUnits = stock.ToDictionary(s => s.ProductName, s => s.BaseUnitName);
        var productionLabel = MovementTypes.Label(MovementType.ProductionIn);
        var damagedLabel = MovementTypes.Label(MovementType.DamagedOut);
        var inRangeMovements = movements.Where(m =>
        {
            var date = DateOnly.FromDateTime(WibTime.ToWib(m.At));
            return date >= start && date <= end;
        }).ToList();
        var production = inRangeMovements
            .Where(m => m.TypeLabel == productionLabel || (m.QtyBaseSigned > 0 && (m.Reason ?? string.Empty).StartsWith(ProductionReason, StringComparison.OrdinalIgnoreCase)) || m.TypeLabel == damagedLabel)
            .GroupBy(m => m.ProductName)
            .Select(g => new ProductionReportRow(
                g.Key,
                productUnits.GetValueOrDefault(g.Key, "satuan"),
                g.Where(m => m.QtyBaseSigned > 0 && (m.TypeLabel == productionLabel || (m.Reason ?? string.Empty).StartsWith(ProductionReason, StringComparison.OrdinalIgnoreCase))).Sum(m => m.QtyBaseSigned),
                g.Where(m => m.TypeLabel == damagedLabel).Sum(m => Math.Abs(m.QtyBaseSigned))))
            .OrderBy(r => r.ProductName)
            .ToList();
        var salesByAgent = orders
            .Where(o => DateOnly.FromDateTime(WibTime.ToWib(o.CompletedAt ?? o.CreatedAt)) >= start
                && DateOnly.FromDateTime(WibTime.ToWib(o.CompletedAt ?? o.CreatedAt)) <= end)
            .GroupBy(o => o.BuyerName)
            .Select(g => new AgentSalesReportRow(
                g.Key,
                g.Count(o => o.Status == OrderStatus.Completed),
                g.Where(o => o.Status == OrderStatus.Completed).Sum(o => o.TotalAmount),
                g.Where(o => o.Status is (OrderStatus.Delivered or OrderStatus.PickedUp)).Sum(o => o.TotalAmount)))
            .OrderByDescending(r => r.Revenue)
            .ToList();

        var revenue = completed.Sum(o => o.TotalAmount);
        return View("Finance", new ChainFinanceViewModel
        {
            From = start,
            To = end,
            Revenue = revenue,
            CostOfGoods = completed.Sum(o => o.CostOfGoods),
            Expenses = expenseRows.Sum(e => e.Amount),
            PendingSales = pending.Sum(o => o.TotalAmount),
            InventoryValue = stock.Sum(s => s.QtyBase * s.AvgCostBase),
            SalesByAgent = salesByAgent,
            Production = production,
            ExpenseRows = expenseRows,
            ExpenseCategories = ExpenseCategories.All,
            HasProductionCosts = revenue == 0 || completed.All(o => o.TotalAmount == 0 || o.CostOfGoods > 0)
        });
    }

    public async Task<IActionResult> Sales(CancellationToken ct)
    {
        var agents = await _businesses.ListVerifiedAgentsAsync(ct);
        var products = (await _products.ListAsync(null, false, ct)).Where(p => p.IsActive).ToList();
        var stock = (await _inventory.GetStockAsync(null, ct)).ToDictionary(s => s.ProductId);
        var options = products.Select(p =>
        {
            stock.TryGetValue(p.Id, out var item);
            return new LivestockSaleProductOption(
                p.Id,
                p.Name,
                p.BaseUnitName,
                item?.AvailableBase ?? 0,
                p.Units.FirstOrDefault(u => u.IsBase && u.ConversionToBase == 1)?.WholesalePrice);
        }).ToList();

        return View(new LivestockSalesViewModel
        {
            Agents = agents.Select(a => new VerifiedAgentOption(a.Id, a.Name)).ToList(),
            Products = options,
            Sales = await _orders.ListAsync(null, 100, ct)
        });
    }

    [HttpPost]
    public async Task<IActionResult> CreateSale(LivestockSaleInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Periksa kembali agen, jumlah produk, dan catatan.";
            return RedirectToAction(nameof(Sales));
        }

        var lines = input.Lines.Where(l => l.ProductId > 0 && l.QtyBase > 0)
            .Select(l => new LivestockSaleLineInput(l.ProductId, l.QtyBase)).ToList();
        var result = await _orders.CreateLivestockSaleAsync(
            new CreateLivestockSaleRequest(input.AgentBusinessId, input.Note, lines, input.IdempotencyKey), ct);
        if (!result.Succeeded)
        {
            TempData["Error"] = result.Error ?? "Penjualan gagal dicatat.";
            return RedirectToAction(nameof(Sales));
        }

        TempData["Success"] = "Penjualan tercatat. Stok peternakan berkurang; agen perlu mengonfirmasi penerimaan agar stok agen bertambah.";
        return RedirectToAction(nameof(Sales));
    }

    [HttpPost]
    public async Task<IActionResult> AddExpense(ExpenseInput input, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Periksa kembali kategori dan jumlah pengeluaran.";
            return RedirectToAction(nameof(Keuangan), new { from, to });
        }

        var result = await _expenses.AddAsync(
            new AddExpenseRequest(input.Category, input.Amount, input.Date ?? WibTime.TodayWib(_time), input.Note), ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? "Pengeluaran tersimpan."
            : result.Error ?? "Pengeluaran gagal dicatat.";
        return RedirectToAction(nameof(Keuangan), new { from, to });
    }

    [HttpPost]
    public async Task<IActionResult> DeleteExpense(int id, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var result = await _expenses.DeleteAsync(id, ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? "Catatan pengeluaran dihapus."
            : result.Error ?? "Catatan pengeluaran gagal dihapus.";
        return RedirectToAction(nameof(Keuangan), new { from, to });
    }

    // Pencatatan manual hasil produk; jumlah tersimpan melalui buku besar stok.
    public async Task<IActionResult> Rencana(CancellationToken ct)
    {
        ViewData["Title"] = "Input produk manual";
        ViewData["ProductionProducts"] = (await _products.ListAsync(null, false, ct)).Where(p => p.IsActive).ToList();
        return View();
    }

    /// <summary>Catat hasil produksi/panen (stok bertambah) atau barang rusak/susut (stok berkurang) lewat buku besar stok.</summary>
    [HttpPost]
    public async Task<IActionResult> CatatProduksi(
        int productId, int qty, decimal? cost, string? kind, DateOnly? expiry, string? lot, string? note, string? opKey, string? returnTo, CancellationToken ct)
    {
        var returnAction = string.Equals(returnTo, "Rencana", StringComparison.OrdinalIgnoreCase)
            ? nameof(Rencana)
            : nameof(Produksi);

        if (productId <= 0 || qty is < 1 or > 1_000_000)
        {
            TempData["Error"] = "Pilih produk dan isi jumlah antara 1 dan 1.000.000.";
            return RedirectToAction(returnAction);
        }

        var damaged = kind == "rusak";
        var detail = string.IsNullOrWhiteSpace(note) ? string.Empty : ": " + note.Trim();
        var result = await _inventory.AdjustAsync(new AdjustStockRequest(
            productId,
            damaged ? MovementType.DamagedOut : MovementType.ProductionIn,
            qty,
            damaged ? null : cost,
            (damaged ? "Barang rusak" : ProductionReason) + detail,
            damaged ? null : expiry,
            null,
            string.IsNullOrWhiteSpace(opKey) ? null : opKey,
            lot,
            damaged ? null : WibTime.TodayWib(_time)), ct);

        if (result.Succeeded)
        {
            TempData["Success"] = damaged ? "Barang rusak dicatat; stok berkurang." : "Hasil produksi dicatat; stok bertambah.";
        }
        else
        {
            TempData["Error"] = result.Error ?? "Terjadi kesalahan.";
        }

        return RedirectToAction(returnAction);
    }

    [HttpPost]
    public async Task<IActionResult> TambahStok(
        int productId, int qty, decimal? cost, DateOnly? expiry, string? lot, string? note, string? opKey, CancellationToken ct)
    {
        if (productId <= 0 || qty is < 1 or > 1_000_000)
        {
            TempData["Error"] = "Pilih produk dan isi jumlah antara 1 dan 1.000.000.";
            return RedirectToAction(nameof(Stok));
        }

        var detail = string.IsNullOrWhiteSpace(note) ? string.Empty : ": " + note.Trim();
        var result = await _inventory.AdjustAsync(new AdjustStockRequest(
            productId,
            MovementType.ManualIn,
            qty,
            cost,
            "Tambah stok manual" + detail,
            expiry,
            null,
            string.IsNullOrWhiteSpace(opKey) ? null : opKey,
            lot), ct);

        if (result.Succeeded)
        {
            TempData["Success"] = "Stok produk berhasil ditambahkan.";
        }
        else
        {
            TempData["Error"] = result.Error ?? "Stok produk gagal ditambahkan.";
        }

        return RedirectToAction(nameof(Stok));
    }

    private async Task<IActionResult> Render(string key, CancellationToken ct)
    {
        var page = await BuildAsync(key, ct);
        ViewData["Title"] = page.Title;
        ViewData["Art"] = "kandang";

        if (key is "produksi" or "stok")
        {
            ViewData["ProductionProducts"] = (await _products.ListAsync(null, false, ct)).Where(p => p.IsActive).ToList();
        }

        return View("Page", page);
    }

    private async Task<ChainPage> BuildAsync(string key, CancellationToken ct)
    {
        if (key is not ("dashboard" or "stok" or "produksi"))
        {
            return EmptyPage(key);
        }

        var stock = await _inventory.GetStockAsync(null, ct);
        var moves = await _inventory.GetMovementsAsync(null, 1000, ct);
        var units = stock.GroupBy(s => s.ProductName).ToDictionary(g => g.Key, g => g.First().BaseUnitName);
        var since = _time.GetUtcNow().UtcDateTime.AddDays(-30);
        var recent = moves.Where(m => m.At >= since).ToList();

        var productionLabel = MovementTypes.Label(MovementType.ProductionIn);
        var damagedLabel = MovementTypes.Label(MovementType.DamagedOut);

        // Lot lama yang dicatat "Tambah manual" dengan alasan "Hasil produksi" tetap dihitung sebagai produksi.
        bool IsProduction(MovementDto m) => m.QtyBaseSigned > 0
            && (m.TypeLabel == productionLabel || (m.Reason ?? string.Empty).StartsWith(ProductionReason, StringComparison.OrdinalIgnoreCase));
        bool IsDamaged(MovementDto m) => m.TypeLabel == damagedLabel;
        string Num(int n) => Rupiah.Number(n, 0);
        string Qty(MovementDto m) =>
            (m.QtyBaseSigned > 0 ? "+" : "−") + Num(Math.Abs(m.QtyBaseSigned)) + " " + units.GetValueOrDefault(m.ProductName, string.Empty);

        switch (key)
        {
            case "stok":
                return new ChainPage
                {
                    Title = "Manajemen Stok",
                    Sub = "Stok = jumlah seluruh pergerakan: produksi, pembelian, pesanan, penjualan, dan koreksi. Tidak ada angka yang diisi manual.",
                    K = new List<string[]>
                    {
                        new[] { "Jenis barang", stock.Count.ToString() },
                        new[] { "Perlu tindakan", stock.Count(s => s.Status == StockStatus.Low).ToString() },
                        new[] { "Habis", stock.Count(s => s.Status == StockStatus.Out).ToString() }
                    },
                    H = new List<string> { "Item", "Stok", "Masuk 30 hari", "Keluar 30 hari", "Minimum", "Status" },
                    R = stock.Select(s => new[]
                    {
                        s.ProductName,
                        Num(s.QtyBase) + " " + s.BaseUnitName,
                        "+" + Num(recent.Where(m => m.ProductName == s.ProductName && m.QtyBaseSigned > 0).Sum(m => m.QtyBaseSigned)),
                        "−" + Num(recent.Where(m => m.ProductName == s.ProductName && m.QtyBaseSigned < 0).Sum(m => -m.QtyBaseSigned)),
                        s.MinStockBase > 0 ? Num(s.MinStockBase) : "–",
                        s.Status == StockStatus.Out ? "Habis" : s.Status == StockStatus.Low ? "Perlu tindakan" : "Aman"
                    }).ToList(),
                    Note = stock.Count == 0
                        ? "Belum ada stok. Tambahkan produk di Produk & foto, lalu catat hasil produksi di menu Produksi."
                        : "Minimum stok diatur dari menu Stok pada akun usaha; selisih masuk dan keluar dihitung dari 30 hari terakhir."
                };

            case "produksi":
            {
                var produced = moves.Where(IsProduction).ToList();
                return new ChainPage
                {
                    Title = "Produksi & Hasil",
                    Sub = "Setiap catatan hasil produksi atau panen langsung menambah stok dan terlihat di Stok dan Dashboard.",
                    K = new List<string[]>
                    {
                        new[] { "Catatan produksi (30 hari)", recent.Count(IsProduction).ToString() },
                        new[] { "Produk dihasilkan", produced.Select(m => m.ProductName).Distinct().Count().ToString() },
                        new[] { "Catatan rusak (30 hari)", recent.Count(IsDamaged).ToString() },
                        new[] { "Produksi terakhir", produced.Count > 0 ? WibTime.FormatDateTime(produced.Max(m => m.At)) : "–" }
                    },
                    H = new List<string> { "Waktu", "Produk", "Jenis", "Jumlah", "Keterangan" },
                    R = moves.Where(m => IsProduction(m) || IsDamaged(m)).Take(60)
                        .Select(m => new[] { WibTime.FormatDateTime(m.At), m.ProductName, IsDamaged(m) ? "Rusak" : "Produksi", Qty(m), m.Reason ?? string.Empty })
                        .ToList(),
                    Note = produced.Count == 0 ? "Belum ada hasil produksi tercatat. Isi form di atas untuk mulai." : null
                };
            }

            case "dashboard":
                return new ChainPage
                {
                    Title = "Ringkasan",
                    Sub = "Dihitung langsung dari stok dan pergerakannya.",
                    K = new List<string[]>
                    {
                        new[] { "Jenis produk", stock.Count.ToString() },
                        new[] { "Catatan produksi (30 hari)", recent.Count(IsProduction).ToString() },
                        new[] { "Stok perlu tindakan", stock.Count(s => s.Status != StockStatus.Ok).ToString() }
                    },
                    H = new List<string> { "Aktivitas terbaru", "Waktu", "Jenis" },
                    R = moves.Take(8).Select(m => new[] { m.ProductName + " " + Qty(m), WibTime.FormatDateTime(m.At), m.TypeLabel }).ToList(),
                    Note = moves.Count == 0 ? "Belum ada aktivitas. Mulai dari menu Produksi: catat hasil produksi atau panen." : null
                };

            default:
                return EmptyPage(key);
        }
    }

    // Halaman yang belum punya sumber data nyata: tampil kosong dengan penjelasan, tanpa angka contoh.
    private static ChainPage EmptyPage(string key)
    {
        var (title, sub) = key switch
        {
            "keuangan" => ("Keuangan & Laporan", "Pendapatan, biaya, dan laba."),
            _ => ("Segera tersedia", string.Empty)
        };

        return new ChainPage
        {
            Title = title,
            Sub = sub,
            Note = "Belum ada data. Halaman ini hanya menampilkan angka dari catatan nyata, dan pencatatan untuk bagian ini belum terhubung ke stok dan produksi."
        };
    }
}
