using Platform.Web.Modules.Catalog.Contracts;

namespace Platform.Tests.Modules.Catalog;

public class PackagingMathTests
{
    private static readonly ProductUnitDto[] Units =
    {
        new("Item", 1, true, null),
        new("Box", 40, false, null),
        new("Dus", 160, false, null)
    };

    [Fact]
    public void Dua_Box_isi_40_menjadi_80_Item()
    {
        Assert.Equal(80, PackagingMath.ToBase(2, 40));
    }

    [Fact]
    public void Harga_120000_per_Box_isi_40_menjadi_3000_per_Item()
    {
        Assert.Equal(3_000m, PackagingMath.CostPerBase(120_000m, 40));
    }

    [Fact]
    public void Rincian_stok_memakai_kemasan_terbesar_lebih_dulu()
    {
        Assert.Equal("2 Box + 5 Item", PackagingMath.Breakdown(85, Units));
        Assert.Equal("1 Dus", PackagingMath.Breakdown(160, Units));
        Assert.Equal("1 Dus + 1 Box + 3 Item", PackagingMath.Breakdown(203, Units));
        Assert.Equal("0 Item", PackagingMath.Breakdown(0, Units));
        Assert.Equal("7 Item", PackagingMath.Breakdown(7, Units));
    }
}
