using System.Globalization;
using Platform.Web.Modules.Catalog.Contracts;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Catalog.Services;

/// <summary>
/// Menyimpan gambar produk unggahan pemilik usaha ke wwwroot/uploads/products/{BusinessId}/. Ukuran dibatasi 2 MB, isi berkas
/// diperiksa (tanda tangan JPG/PNG/WebP/GIF harus cocok dengan ekstensi), dan nama berkas di disk selalu acak.
/// </summary>
public sealed class ProductImageStore : IProductImageStore
{
    public const long MaxBytes = 2 * 1024 * 1024;

    private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };

    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ProductImageStore> _logger;

    public ProductImageStore(IWebHostEnvironment env, ILogger<ProductImageStore> logger)
    {
        _env = env;
        _logger = logger;
    }

    private string UploadRoot =>
        Path.GetFullPath(Path.Combine(_env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot"), "uploads", "products"));

    public async Task<Result<string>> SaveAsync(IFormFile file, int businessId, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
        {
            return Result<string>.Fail(ErrorKind.Validation, "Berkas gambar kosong.");
        }

        if (file.Length > MaxBytes)
        {
            return Result<string>.Fail(ErrorKind.Validation, "Ukuran gambar maksimal 2 MB.");
        }

        var extension = Path.GetExtension(file.FileName ?? string.Empty).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
        {
            return Result<string>.Fail(ErrorKind.Validation, "Format gambar harus JPG, PNG, WebP, atau GIF.");
        }

        var header = new byte[12];
        var read = 0;
        await using (var probe = file.OpenReadStream())
        {
            while (read < header.Length)
            {
                var n = await probe.ReadAsync(header.AsMemory(read, header.Length - read), ct);
                if (n == 0)
                {
                    break;
                }

                read += n;
            }
        }

        if (!LooksLikeImage(header, read, extension))
        {
            return Result<string>.Fail(ErrorKind.Validation, "Isi berkas bukan gambar yang valid untuk format " + extension.TrimStart('.').ToUpperInvariant() + ".");
        }

        var owner = businessId.ToString(CultureInfo.InvariantCulture);
        var name = Guid.NewGuid().ToString("N") + extension;
        try
        {
            var folder = Path.Combine(UploadRoot, owner);
            Directory.CreateDirectory(folder);
            await using var target = File.Create(Path.Combine(folder, name));
            await file.CopyToAsync(target, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Gagal menyimpan gambar produk usaha {BusinessId}.", businessId);
            return Result<string>.Fail(ErrorKind.Unexpected, "Gambar tidak dapat disimpan saat ini. Silakan coba lagi.");
        }

        return Result<string>.Ok($"{ProductImagePaths.UploadPrefix}{owner}/{name}");
    }

    public void Delete(string? imagePath)
    {
        if (string.IsNullOrEmpty(imagePath)
            || !imagePath.StartsWith(ProductImagePaths.UploadPrefix, StringComparison.Ordinal)
            || !ProductImagePaths.IsValid(imagePath))
        {
            return; // gambar demo bawaan (/img/products/...) tidak pernah dihapus
        }

        try
        {
            var root = UploadRoot;
            var relative = imagePath[ProductImagePaths.UploadPrefix.Length..].Replace('/', Path.DirectorySeparatorChar);
            var full = Path.GetFullPath(Path.Combine(root, relative));
            if (full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) && File.Exists(full))
            {
                File.Delete(full);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Gambar lama tidak dapat dihapus: {Path}", imagePath);
        }
    }

    // Isi berkas harus cocok dengan ekstensinya (bukan sekadar nama berkas yang diganti).
    private static bool LooksLikeImage(byte[] h, int length, string extension)
    {
        if (length < 12)
        {
            return false;
        }

        var jpeg = h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF;
        var png = h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47;
        var gif = h[0] == 0x47 && h[1] == 0x49 && h[2] == 0x46 && h[3] == 0x38;
        var webp = h[0] == 0x52 && h[1] == 0x49 && h[2] == 0x46 && h[3] == 0x46 && h[8] == 0x57 && h[9] == 0x45 && h[10] == 0x42 && h[11] == 0x50;

        return extension switch
        {
            ".jpg" or ".jpeg" => jpeg,
            ".png" => png,
            ".gif" => gif,
            ".webp" => webp,
            _ => false
        };
    }
}
