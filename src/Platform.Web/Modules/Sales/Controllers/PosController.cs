using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Web.Modules.Sales.Contracts;
using Platform.Web.Modules.Stock.Contracts;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Sales.Controllers;

public sealed class CheckoutLine
{
    public int ProductId { get; set; }
    public int QtyBase { get; set; }
}

public sealed class CheckoutBody
{
    public List<CheckoutLine> Lines { get; set; } = new();
    public string PaymentMethod { get; set; } = "CASH";
    public string? IdempotencyKey { get; set; }
    public decimal? PaidAmount { get; set; }
}

[Authorize(Roles = RoleNames.Retail)]
public class PosController : Controller
{
    private readonly IInventoryService _inventory;
    private readonly ISalesService _sales;

    public PosController(IInventoryService inventory, ISalesService sales)
    {
        _inventory = inventory;
        _sales = sales;
    }

    public IActionResult Index() => View();

    // Pencarian produk untuk POS (JSON). Hanya produk dengan stok > 0 dan harga jual terisi.
    [HttpGet("pos/products")]
    public async Task<IActionResult> Products(string? q, CancellationToken ct)
    {
        var items = await _inventory.SearchSellableAsync(q, 30, ct);
        return Json(items.Select(p => new
        {
            id = p.ProductId,
            name = p.Name,
            sku = p.Sku,
            unit = p.BaseUnitName,
            stock = p.QtyBase,
            price = p.SellingPriceBase,
            image = p.ImagePath
        }));
    }

    // Konfirmasi penjualan (JSON). Validasi dan atomisitas dilakukan di SalesService.
    [HttpPost("pos/checkout")]
    public async Task<IActionResult> Checkout([FromBody] CheckoutBody body, CancellationToken ct)
    {
        var lines = body.Lines.Select(l => new SaleLineInput(l.ProductId, l.QtyBase)).ToList();
        var result = await _sales.ConfirmAsync(new ConfirmSaleRequest(lines, body.PaymentMethod, body.IdempotencyKey, body.PaidAmount), ct);

        if (!result.Succeeded)
        {
            return BadRequest(new { ok = false, error = result.Error });
        }

        var receipt = result.Value!;
        return Json(new { ok = true, id = receipt.Id, number = receipt.Number, total = receipt.Total, items = receipt.TotalItems, paid = receipt.Paid, change = receipt.Change });
    }
}
