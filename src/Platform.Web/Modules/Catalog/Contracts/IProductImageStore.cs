using Microsoft.AspNetCore.Http;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Catalog.Contracts;

/// <summary>Alamat gambar produk yang boleh disimpan di database.</summary>
public static class ProductImagePaths
{
    /// <summary>Gambar unggahan pemilik usaha (dikelola IProductImageStore).</summary>
    public const string UploadPrefix = "/uploads/products/";

    /// <summary>Gambar demo bawaan yang dikirim bersama aplikasi (tidak pernah dihapus).</summary>
    public const string DemoPrefix = "/img/products/";

    /// <summary>Null/kosong = tanpa gambar. Selain itu harus di bawah folder unggahan atau folder gambar demo, tanpa "..", dan hanya huruf/angka/"/-_.".</summary>
    public static bool IsValid(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return true;
        }

        if (path.Length > 260 || path.Contains(".."))
        {
            return false;
        }

        if (!path.StartsWith(UploadPrefix, StringComparison.Ordinal) && !path.StartsWith(DemoPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        return path.All(c => char.IsAsciiLetterOrDigit(c) || c is '/' or '-' or '_' or '.');
    }
}

/// <summary>Menyimpan gambar produk unggahan pemilik usaha. Setiap usaha hanya mengunggah gambar produknya sendiri.</summary>
public interface IProductImageStore
{
    /// <summary>Memeriksa (ukuran, format, isi berkas) lalu menyimpan gambar. Mengembalikan alamat publik, mis. /uploads/products/12/abc.jpg.</summary>
    Task<Result<string>> SaveAsync(IFormFile file, int businessId, CancellationToken ct = default);

    /// <summary>Menghapus berkas unggahan dari disk. Alamat di luar folder unggahan (gambar demo) diabaikan.</summary>
    void Delete(string? imagePath);
}
