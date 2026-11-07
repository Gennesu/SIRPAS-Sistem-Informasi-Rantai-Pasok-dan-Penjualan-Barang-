namespace Platform.Web.SharedKernel;

/// <summary>Nama elemen input untuk pemilih lokasi: latitude, longitude, dan (opsional) kolom alamat yang boleh diisi otomatis.</summary>
public sealed record LocationPickerModel(string LatField, string LonField, string? AddressField = null);
