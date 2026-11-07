using Microsoft.EntityFrameworkCore;
using Platform.Web.Data;
using Platform.Web.Modules.Audit.Contracts;
using Platform.Web.Modules.Catalog.Contracts;
using Platform.Web.Modules.Catalog.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Catalog.Services;

public sealed class ProductService : IProductService
{
    private const string NotFoundMessage = "Produk tidak ditemukan.";
    private const decimal MaxMoney = 1_000_000_000_000m;
    private const int MaxExtraUnits = 5;

    private readonly AppDbContext _db;
    private readonly IBusinessContext _context;
    private readonly IAuditLogService _audit;
    private readonly TimeProvider _time;
    private readonly ILogger<ProductService> _logger;

    public ProductService(
        AppDbContext db,
        IBusinessContext context,
        IAuditLogService audit,
        TimeProvider time,
        ILogger<ProductService> logger)
    {
        _db = db;
        _context = context;
        _audit = audit;
        _time = time;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(CancellationToken ct = default)
    {
        return await _db.Categories
            .AsNoTracking()
            .OrderBy(c => c.Id)
            .Select(c => new CategoryDto(c.Id, c.Name))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ProductDto>> ListAsync(string? search, bool includeInactive, CancellationToken ct = default)
    {
        var products = await _db.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Units)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

        var term = search?.Trim();
        return products
            .Where(p => includeInactive || p.IsActive)
            .Where(p => string.IsNullOrEmpty(term)
                        || p.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                        || (p.Sku?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))
            .Select(ToDto)
            .ToList();
    }

    public async Task<Result<ProductDto>> GetAsync(int id, CancellationToken ct = default)
    {
        var product = await _db.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Units)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        return product is null
            ? Result<ProductDto>.Fail(ErrorKind.NotFound, NotFoundMessage)
            : Result<ProductDto>.Ok(ToDto(product));
    }

    public async Task<Result<ProductDto>> CreateAsync(SaveProductRequest request, CancellationToken ct = default)
    {
        var businessId = _context.BusinessId;
        if (!_context.IsAuthenticated || _context.IsAdmin || businessId is null)
        {
            return Result<ProductDto>.Fail(ErrorKind.Forbidden, "Hanya akun usaha yang dapat mengelola produk.");
        }

        var n = Normalize(request);
        var error = await ValidateAsync(n, request.CategoryId, 0, ct);
        if (error is not null)
        {
            return Result<ProductDto>.Fail(error.Value.Kind, error.Value.Message);
        }

        if (request.MoqBase is < 1 or > 1_000_000)
        {
            return Result<ProductDto>.Fail(ErrorKind.Validation, "Minimum pesanan (MOQ) harus 1–1.000.000 satuan dasar.");
        }

        var product = new Product
        {
            MoqBase = request.MoqBase,
            BusinessId = businessId.Value,
            Name = n.Name,
            Sku = n.Sku,
            CategoryId = request.CategoryId,
            Description = n.Description,
            BaseUnitName = n.BaseUnit,
            IsActive = true,
            CreatedAt = _time.GetUtcNow().UtcDateTime,
            SourceProductId = request.SourceProductId,
            ImagePath = n.ImagePath
        };
        product.Units.Add(new ProductUnit
        {
            BusinessId = businessId.Value,
            UnitName = n.BaseUnit,
            ConversionToBase = 1,
            IsBase = true,
            WholesalePrice = n.BaseWholesalePrice
        });
        foreach (var extra in n.Extras)
        {
            product.Units.Add(new ProductUnit
            {
                BusinessId = businessId.Value,
                UnitName = extra.UnitName,
                ConversionToBase = extra.ConversionToBase,
                IsBase = false,
                WholesalePrice = extra.WholesalePrice
            });
        }

        _db.Products.Add(product);

        try
        {
            await _db.SaveChangesAsync(ct);
            _audit.Record("PRODUCT_CREATED", "Product", product.Id.ToString(), n.Name);
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Gagal menyimpan produk baru.");
            return Result<ProductDto>.Fail(ErrorKind.Unexpected, "Produk tidak dapat disimpan saat ini. Silakan coba lagi.");
        }

        return await GetAsync(product.Id, ct);
    }

    public async Task<Result<ProductDto>> UpdateAsync(int id, SaveProductRequest request, CancellationToken ct = default)
    {
        var businessId = _context.BusinessId;
        if (!_context.IsAuthenticated || _context.IsAdmin || businessId is null)
        {
            return Result<ProductDto>.Fail(ErrorKind.Forbidden, "Hanya akun usaha yang dapat mengelola produk.");
        }

        var product = await _db.Products.Include(p => p.Units).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null)
        {
            return Result<ProductDto>.Fail(ErrorKind.NotFound, NotFoundMessage);
        }

        var n = Normalize(request);
        var error = await ValidateAsync(n, request.CategoryId, id, ct);
        if (error is not null)
        {
            return Result<ProductDto>.Fail(error.Value.Kind, error.Value.Message);
        }

        product.Name = n.Name;
        product.Sku = n.Sku;
        product.CategoryId = request.CategoryId;
        product.Description = n.Description;
        product.BaseUnitName = n.BaseUnit;
        if (request.MoqBase is < 1 or > 1_000_000)
        {
            return Result<ProductDto>.Fail(ErrorKind.Validation, "Minimum pesanan (MOQ) harus 1–1.000.000 satuan dasar.");
        }

        product.MoqBase = request.MoqBase;

        // Gambar: hapus bila diminta, ganti bila ada alamat baru, selain itu biarkan seperti semula.
        if (request.ClearImage)
        {
            product.ImagePath = null;
        }
        else if (n.ImagePath is not null)
        {
            product.ImagePath = n.ImagePath;
        }

        var baseUnit = product.Units.FirstOrDefault(u => u.IsBase);
        if (baseUnit is null)
        {
            baseUnit = new ProductUnit { BusinessId = businessId.Value, ConversionToBase = 1, IsBase = true };
            product.Units.Add(baseUnit);
        }

        baseUnit.UnitName = n.BaseUnit;
        baseUnit.WholesalePrice = n.BaseWholesalePrice;

        // Samakan kemasan tambahan: ubah yang ada, hapus yang tidak diminta, tambah yang baru.
        var wanted = n.Extras.ToDictionary(u => u.UnitName, StringComparer.OrdinalIgnoreCase);
        foreach (var existing in product.Units.Where(u => !u.IsBase).ToList())
        {
            if (wanted.TryGetValue(existing.UnitName, out var change))
            {
                existing.ConversionToBase = change.ConversionToBase;
                existing.WholesalePrice = change.WholesalePrice;
                wanted.Remove(existing.UnitName);
            }
            else
            {
                product.Units.Remove(existing);
                _db.ProductUnits.Remove(existing);
            }
        }

        foreach (var added in wanted.Values)
        {
            product.Units.Add(new ProductUnit
            {
                BusinessId = businessId.Value,
                UnitName = added.UnitName,
                ConversionToBase = added.ConversionToBase,
                IsBase = false,
                WholesalePrice = added.WholesalePrice
            });
        }

        _audit.Record("PRODUCT_UPDATED", "Product", product.Id.ToString(), n.Name);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Gagal memperbarui produk {ProductId}.", id);
            return Result<ProductDto>.Fail(ErrorKind.Unexpected, "Produk tidak dapat disimpan saat ini. Silakan coba lagi.");
        }

        return await GetAsync(id, ct);
    }

    public async Task<Result> SetActiveAsync(int id, bool isActive, CancellationToken ct = default)
    {
        if (!_context.IsAuthenticated || _context.IsAdmin || _context.BusinessId is null)
        {
            return Result.Fail(ErrorKind.Forbidden, "Hanya akun usaha yang dapat mengelola produk.");
        }

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null)
        {
            return Result.Fail(ErrorKind.NotFound, NotFoundMessage);
        }

        product.IsActive = isActive;
        _audit.Record(isActive ? "PRODUCT_ACTIVATED" : "PRODUCT_DEACTIVATED", "Product", product.Id.ToString(), product.Name);
        await _db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result<int>> FindOrCreateFromSourceAsync(SourceProductSnapshot source, CancellationToken ct = default)
    {
        if (!_context.IsAuthenticated || _context.IsAdmin || _context.BusinessId is null)
        {
            return Result<int>.Fail(ErrorKind.Forbidden, "Hanya akun usaha yang dapat mengelola produk.");
        }

        var products = await _db.Products.ToListAsync(ct);
        Product? match = products.FirstOrDefault(p => p.SourceProductId == source.SourceProductId);
        if (match is null && source.Sku is not null)
        {
            match = products.FirstOrDefault(p => string.Equals(p.Sku, source.Sku, StringComparison.OrdinalIgnoreCase));
        }

        match ??= products.FirstOrDefault(p => string.Equals(p.Name, source.Name, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            if (match.SourceProductId is null)
            {
                match.SourceProductId = source.SourceProductId;
                await _db.SaveChangesAsync(ct);
            }

            return Result<int>.Ok(match.Id);
        }

        var created = await CreateAsync(
            new SaveProductRequest(
                source.Name, source.Sku, source.CategoryId, null, source.BaseUnitName, null, source.Units, source.SourceProductId),
            ct);

        return created.Succeeded
            ? Result<int>.Ok(created.Value!.Id)
            : Result<int>.Fail(created.Kind, created.Error ?? "Produk gagal dibuat.");
    }

    public async Task<Result> AddCategoryAsync(string name, CancellationToken ct = default)
    {
        if (!_context.IsAuthenticated || !_context.IsAdmin)
        {
            return Result.Fail(ErrorKind.Forbidden, "Hanya admin yang dapat mengelola kategori.");
        }

        var clean = (name ?? string.Empty).Trim();
        if (clean.Length is < 2 or > 60)
        {
            return Result.Fail(ErrorKind.Validation, "Nama kategori harus 2–60 karakter.");
        }

        var existing = await _db.Categories.AsNoTracking().ToListAsync(ct);
        if (existing.Any(c => string.Equals(c.Name, clean, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Fail(ErrorKind.Conflict, "Nama kategori sudah ada.");
        }

        // Id kategori diatur manual (seed memakai ValueGeneratedNever).
        var id = existing.Count == 0 ? 1 : existing.Max(c => c.Id) + 1;
        _db.Categories.Add(new Category { Id = id, Name = clean });
        _audit.Record("CATEGORY_ADDED", "Category", id.ToString(), clean);
        await _db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result> RenameCategoryAsync(int id, string name, CancellationToken ct = default)
    {
        if (!_context.IsAuthenticated || !_context.IsAdmin)
        {
            return Result.Fail(ErrorKind.Forbidden, "Hanya admin yang dapat mengelola kategori.");
        }

        var clean = (name ?? string.Empty).Trim();
        if (clean.Length is < 2 or > 60)
        {
            return Result.Fail(ErrorKind.Validation, "Nama kategori harus 2–60 karakter.");
        }

        var all = await _db.Categories.ToListAsync(ct);
        var category = all.FirstOrDefault(c => c.Id == id);
        if (category is null)
        {
            return Result.Fail(ErrorKind.NotFound, "Kategori tidak ditemukan.");
        }

        if (all.Any(c => c.Id != id && string.Equals(c.Name, clean, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Fail(ErrorKind.Conflict, "Nama kategori sudah ada.");
        }

        category.Name = clean;
        _audit.Record("CATEGORY_RENAMED", "Category", id.ToString(), clean);
        await _db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    // ------------------------------------------------------------------ helper

    private sealed record Normalized(
        string Name,
        string? Sku,
        string? Description,
        string BaseUnit,
        decimal? BaseWholesalePrice,
        List<UnitInput> Extras,
        string? ImagePath);

    private static Normalized Normalize(SaveProductRequest r) => new(
        (r.Name ?? string.Empty).Trim(),
        string.IsNullOrWhiteSpace(r.Sku) ? null : r.Sku.Trim().ToUpperInvariant(),
        string.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim(),
        (r.BaseUnitName ?? string.Empty).Trim(),
        r.BaseWholesalePrice,
        (r.ExtraUnits ?? Array.Empty<UnitInput>())
            .Select(u => new UnitInput((u.UnitName ?? string.Empty).Trim(), u.ConversionToBase, u.WholesalePrice))
            .ToList(),
        string.IsNullOrWhiteSpace(r.ImagePath) ? null : r.ImagePath.Trim());

    private async Task<(ErrorKind Kind, string Message)?> ValidateAsync(
        Normalized n, int categoryId, int excludeProductId, CancellationToken ct)
    {
        if (n.Name.Length is < 2 or > 120)
        {
            return (ErrorKind.Validation, "Nama produk harus 2–120 karakter.");
        }

        if (n.Sku is { Length: > 40 })
        {
            return (ErrorKind.Validation, "SKU maksimal 40 karakter.");
        }

        if (n.Description is { Length: > 300 })
        {
            return (ErrorKind.Validation, "Deskripsi maksimal 300 karakter.");
        }

        if (n.BaseUnit.Length is < 1 or > 20)
        {
            return (ErrorKind.Validation, "Satuan dasar harus 1–20 karakter.");
        }

        if (n.BaseWholesalePrice is < 0 or > MaxMoney)
        {
            return (ErrorKind.Validation, "Harga grosir tidak valid.");
        }

        if (n.Extras.Count > MaxExtraUnits)
        {
            return (ErrorKind.Validation, $"Kemasan tambahan maksimal {MaxExtraUnits}.");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { n.BaseUnit };
        foreach (var extra in n.Extras)
        {
            if (extra.UnitName.Length is < 1 or > 20)
            {
                return (ErrorKind.Validation, "Nama kemasan harus 1–20 karakter.");
            }

            if (extra.ConversionToBase is < 2 or > 100_000)
            {
                return (ErrorKind.Validation, $"Isi kemasan {extra.UnitName} harus bilangan bulat 2–100000.");
            }

            if (extra.WholesalePrice is < 0 or > MaxMoney)
            {
                return (ErrorKind.Validation, $"Harga grosir {extra.UnitName} tidak valid.");
            }

            if (!names.Add(extra.UnitName))
            {
                return (ErrorKind.Validation, $"Nama satuan \"{extra.UnitName}\" dipakai lebih dari sekali.");
            }
        }

        if (!await _db.Categories.AnyAsync(c => c.Id == categoryId, ct))
        {
            return (ErrorKind.Validation, "Kategori tidak valid.");
        }

        if (n.Sku is not null && await _db.Products.AnyAsync(p => p.Sku == n.Sku && p.Id != excludeProductId, ct))
        {
            return (ErrorKind.Conflict, "SKU sudah dipakai produk lain.");
        }

        if (!ProductImagePaths.IsValid(n.ImagePath))
        {
            return (ErrorKind.Validation, "Alamat gambar produk tidak valid.");
        }

        return null;
    }

    private static ProductDto ToDto(Product p) => new(
        p.Id,
        p.Name,
        p.Sku,
        p.CategoryId,
        p.Category.Name,
        p.Description,
        p.BaseUnitName,
        p.IsActive,
        p.Units
            .OrderBy(u => u.ConversionToBase)
            .Select(u => new ProductUnitDto(u.UnitName, u.ConversionToBase, u.IsBase, u.WholesalePrice))
            .ToList(),
        p.SourceProductId,
        p.MoqBase,
        p.ImagePath);
}
