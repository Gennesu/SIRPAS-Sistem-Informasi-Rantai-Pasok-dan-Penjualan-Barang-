namespace Platform.Web.SharedKernel;

/// <summary>Data untuk partial _ProductThumb: gambar produk, atau kotak berinisial bila belum ada gambar.</summary>
public sealed record ProductThumbModel(string? ImagePath, string? Name, int Size = 44);
