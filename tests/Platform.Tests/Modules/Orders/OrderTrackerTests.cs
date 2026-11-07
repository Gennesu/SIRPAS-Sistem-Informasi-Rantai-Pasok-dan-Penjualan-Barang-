using Platform.Web.Modules.Delivery.Contracts;
using Platform.Web.Modules.Orders.Contracts;
using Platform.Web.Modules.Orders.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Tests.Modules.Orders;

public sealed class OrderTrackerTests
{
    private static readonly DateTime T0 = new(2026, 10, 1, 3, 0, 0, DateTimeKind.Utc);

    private static OrderEventDto E(OrderStatus status, int minutes, string? note = null) => new(status, T0.AddMinutes(minutes), note);

    [Fact]
    public void Pengiriman_punya_enam_fase_dan_fase_berjalan_ditandai()
    {
        var steps = OrderTracker.Build(
            OrderStatus.Preparing, FulfillmentType.Delivery,
            new[] { E(OrderStatus.Pending, 0), E(OrderStatus.Accepted, 10), E(OrderStatus.Preparing, 30) });

        Assert.Equal(6, steps.Count);
        Assert.Equal(new[] { "Pesanan dibuat", "Diterima grosir", "Disiapkan", "Siap dikirim", "Dalam pengiriman", "Selesai" }, steps.Select(s => s.Title).ToArray());
        Assert.Equal(new[] { TrackingState.Done, TrackingState.Done, TrackingState.Current, TrackingState.Upcoming, TrackingState.Upcoming, TrackingState.Upcoming },
            steps.Select(s => s.State).ToArray());
        Assert.Equal(T0.AddMinutes(30), steps[2].At);
        Assert.Null(steps[3].At);
    }

    [Fact]
    public void Ambil_sendiri_memakai_judul_fase_yang_sesuai()
    {
        var steps = OrderTracker.Build(OrderStatus.Ready, FulfillmentType.Pickup, new[] { E(OrderStatus.Pending, 0) });

        Assert.Equal("Siap diambil", steps[3].Title);
        Assert.Equal("Sudah diambil", steps[4].Title);
        Assert.Equal(TrackingState.Current, steps[3].State);
    }

    [Fact]
    public void Pesanan_selesai_semua_fasenya_selesai()
    {
        var steps = OrderTracker.Build(OrderStatus.Completed, FulfillmentType.Delivery, Array.Empty<OrderEventDto>());

        Assert.True(steps.All(s => s.State == TrackingState.Done));
    }

    [Fact]
    public void Pesanan_pembeli_akhir_diterima_warung_bukan_grosir()
    {
        var steps = OrderTracker.Build(
            OrderStatus.Pending, FulfillmentType.Pickup, new[] { E(OrderStatus.Pending, 0) }, TrackingParty.Consumer);

        Assert.Equal(new[] { "Pesanan dibuat", "Diterima warung", "Disiapkan", "Siap diambil", "Sudah diambil", "Selesai" },
            steps.Select(s => s.Title).ToArray());
        foreach (var step in steps)
        {
            Assert.DoesNotContain("grosir", (step.Title + " " + step.Description).ToLowerInvariant());
        }
    }

    [Fact]
    public void Pengiriman_ke_pembeli_akhir_menyebut_alamat_pembeli_dan_penolakan_oleh_warung()
    {
        var delivery = OrderTracker.Build(OrderStatus.Delivered, FulfillmentType.Delivery, Array.Empty<OrderEventDto>(), TrackingParty.Consumer);
        Assert.Contains("alamat Anda", delivery.Single(s => s.Title == "Dalam pengiriman").Description);

        var rejected = OrderTracker.Build(OrderStatus.Rejected, FulfillmentType.Pickup, new[] { E(OrderStatus.Pending, 0) }, TrackingParty.Consumer);
        Assert.Contains(rejected, s => s.Title == "Ditolak warung");
        Assert.Equal("Menunggu warung menerima pesanan", OrderTracker.WaitingFor(OrderStatus.Pending, FulfillmentType.Pickup, TrackingParty.Consumer));
    }

    [Fact]
    public void Alur_antar_usaha_tetap_menyebut_grosir()
    {
        var steps = OrderTracker.Build(OrderStatus.Pending, FulfillmentType.Pickup, new[] { E(OrderStatus.Pending, 0) });

        Assert.Equal("Diterima grosir", steps[1].Title);
        Assert.Equal("Menunggu grosir menerima pesanan", OrderTracker.WaitingFor(OrderStatus.Pending, FulfillmentType.Pickup));
    }

    [Fact]
    public void Pesanan_ditolak_berhenti_di_fase_terakhir_dan_menampilkan_alasan()
    {
        var steps = OrderTracker.Build(
            OrderStatus.Rejected, FulfillmentType.Delivery,
            new[] { E(OrderStatus.Pending, 0), E(OrderStatus.Rejected, 5, "Stok kosong") });

        Assert.Equal(2, steps.Count);
        Assert.Equal(TrackingState.Done, steps[0].State);
        Assert.Equal(TrackingState.Failed, steps[1].State);
        Assert.Contains("Stok kosong", steps[1].Description);
    }

    [Fact]
    public void Dibatalkan_setelah_diterima_menampilkan_fase_yang_sempat_dilewati()
    {
        var steps = OrderTracker.Build(
            OrderStatus.Cancelled, FulfillmentType.Pickup,
            new[] { E(OrderStatus.Pending, 0), E(OrderStatus.Accepted, 3), E(OrderStatus.Cancelled, 9, "Barang rusak") });

        Assert.Equal(3, steps.Count);
        Assert.Equal("Dibatalkan", steps[2].Title);
    }

    [Fact]
    public void Posisi_truk_diam_di_grosir_sebelum_berangkat_dan_di_tujuan_setelah_selesai()
    {
        var now = T0.AddMinutes(100);

        Assert.Equal(0.0, OrderTracker.TruckProgress(OrderStatus.Preparing, null, 30, now));
        Assert.Equal(0.0, OrderTracker.TruckProgress(OrderStatus.Ready, null, 30, now));
        Assert.Equal(1.0, OrderTracker.TruckProgress(OrderStatus.Completed, T0, 30, now));
    }

    [Fact]
    public void Posisi_truk_bergerak_menurut_waktu_tempuh_dan_berhenti_di_95_persen()
    {
        // Berangkat 15 menit lalu, perjalanan 30 menit: separuh jalan.
        Assert.Equal(0.5, OrderTracker.TruckProgress(OrderStatus.Delivered, T0, 30, T0.AddMinutes(15)), 3);
        // Sudah lewat estimasi tetapi belum dikonfirmasi: berhenti di 95%.
        Assert.Equal(0.95, OrderTracker.TruckProgress(OrderStatus.Delivered, T0, 30, T0.AddMinutes(90)), 3);
        // Baru berangkat: tidak nol agar truk terlihat meninggalkan toko.
        Assert.Equal(0.05, OrderTracker.TruckProgress(OrderStatus.Delivered, T0, 30, T0), 3);
    }

    [Fact]
    public void Penantian_menyebut_pihak_yang_harus_bertindak()
    {
        Assert.Contains("grosir", OrderTracker.WaitingFor(OrderStatus.Pending, FulfillmentType.Delivery));
        Assert.Contains("warung", OrderTracker.WaitingFor(OrderStatus.Delivered, FulfillmentType.Delivery));
        Assert.Equal("Pesanan selesai", OrderTracker.WaitingFor(OrderStatus.Completed, FulfillmentType.Pickup));
    }
}
