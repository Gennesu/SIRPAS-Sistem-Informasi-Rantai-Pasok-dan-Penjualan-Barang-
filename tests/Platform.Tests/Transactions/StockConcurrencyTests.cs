using Microsoft.EntityFrameworkCore;
using Platform.Tests.TestSupport;
using Platform.Web.Modules.BusinessProfile.Domain;
using Platform.Web.Modules.Consumers.Contracts;
using Platform.Web.Modules.Consumers.Domain;
using Platform.Web.Modules.Delivery.Contracts;
using Platform.Web.Modules.Orders.Contracts;
using Platform.Web.Modules.Orders.Domain;
using Platform.Web.Modules.Sales.Contracts;
using Platform.Web.Modules.Stock.Contracts;
using Platform.Web.Modules.Stock.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Tests.Transactions;

/// <summary>
/// Kelompok B: stok dan konkurensi. Semua tes yang berlabel "serentak" menjalankan beberapa DbContext/koneksi SQLite
/// sekaligus terhadap file database yang sama. Hasil diperiksa lewat invarian database (stok, reservasi, movement), bukan status HTTP.
/// </summary>
public sealed class StockConcurrencyTests : IDisposable
{
    private readonly V3World _w = new();

    public void Dispose() => _w.Dispose();

    private async Task AssertConsistentAsync()
    {
        var violations = await _w.Open(_w.Admin()).Integrity.CheckAsync();
        Assert.True(violations.Count == 0, "Pelanggaran integritas: " + string.Join("; ", violations.Select(v => $"{v.Rule} {v.Detail}")));
    }

    private static PlaceConsumerOrderRequest Pickup(V3World.Actor shop, int productId, int qty, string key) =>
        new(shop.BusinessId!.Value, FulfillmentType.Pickup, PaymentMethod.CashOnPickup, null, null,
            new[] { new ConsumerLineInput(productId, qty) }, key);

    private async Task<Inventory> InventoryOf(V3World.Actor owner, int productId) =>
        await _w.Open(_w.Admin()).Db.Inventories.AsNoTracking().SingleAsync(i => i.BusinessId == owner.BusinessId && i.ProductId == productId);

    // ------------------------------------------------------------------ B1

    [Fact]
    public async Task B1_Stok_10_dua_pembeli_serentak_meminta_7_hanya_satu_berhasil_dan_stok_tidak_minus()
    {
        var shop = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung Rebutan");
        var buyerA = await _w.RegisterConsumerAsync("Ani");
        var buyerB = await _w.RegisterConsumerAsync("Budi");

        for (var trial = 0; trial < 12; trial++)
        {
            var productId = await _w.CreateRetailProductAsync(shop, "Susu " + trial, 10, 5_000m);

            var results = await V3World.RaceAsync(
                () => _w.Open(buyerA).ConsumerOrders.PlaceAsync(Pickup(shop, productId, 7, $"a-{trial}")),
                () => _w.Open(buyerB).ConsumerOrders.PlaceAsync(Pickup(shop, productId, 7, $"b-{trial}")));

            Assert.Equal(1, results.Count(r => r.Succeeded));
            var loser = results.Single(r => !r.Succeeded);
            Assert.Equal(ErrorKind.Conflict, loser.Kind);
            Assert.Contains("tidak cukup", loser.Error!);

            var inv = await InventoryOf(shop, productId);
            Assert.Equal(10, inv.QtyBase);
            Assert.Equal(7, inv.ReservedBase);       // hanya satu pemenang yang menahan 7
            Assert.True(inv.QtyBase - inv.ReservedBase >= 0);
        }

        await AssertConsistentAsync();
    }

    [Fact]
    public async Task B1b_StockOut_langsung_serentak_2x7_dari_stok_10_tidak_kehilangan_update_seperti_V2()
    {
        var shop = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung Koreksi");
        for (var trial = 0; trial < 12; trial++)
        {
            var productId = await _w.CreateRetailProductAsync(shop, "Mie " + trial, 10, 3_000m);
            var results = await V3World.RaceAsync(
                () => _w.Open(shop).Inventory.StockOutAsync(new StockOutRequest(productId, 7, MovementType.CorrectionOut)),
                () => _w.Open(shop).Inventory.StockOutAsync(new StockOutRequest(productId, 7, MovementType.CorrectionOut)));

            Assert.Equal(1, results.Count(r => r.Succeeded)); // V2: 20 dari 20 percobaan KEDUANYA sukses (stok berakhir 3, padahal 14 keluar)
            var inv = await InventoryOf(shop, productId);
            Assert.Equal(3, inv.QtyBase);
            var movements = await _w.Open(_w.Admin()).Db.InventoryMovements.AsNoTracking()
                .Where(m => m.ProductId == productId).SumAsync(m => m.QtyBaseSigned);
            Assert.Equal(inv.QtyBase, movements);
        }

        await AssertConsistentAsync();
    }

    // ------------------------------------------------------------------ B2

    [Fact]
    public async Task B2_Dua_kasir_serentak_menjual_stok_yang_sama_hanya_satu_transaksi_berhasil()
    {
        var shop = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung Kasir");
        for (var trial = 0; trial < 12; trial++)
        {
            var productId = await _w.CreateRetailProductAsync(shop, "Kopi " + trial, 10, 4_000m);
            var results = await V3World.RaceAsync(
                () => _w.Open(shop).Sales.ConfirmAsync(new ConfirmSaleRequest(new[] { new SaleLineInput(productId, 7) }, "CASH", Guid.NewGuid().ToString("N"), null)),
                () => _w.Open(shop).Sales.ConfirmAsync(new ConfirmSaleRequest(new[] { new SaleLineInput(productId, 7) }, "CASH", Guid.NewGuid().ToString("N"), null)));

            Assert.Equal(1, results.Count(r => r.Succeeded));
            Assert.Equal(3, (await InventoryOf(shop, productId)).QtyBase);
            var sold = await _w.Open(_w.Admin()).Db.SaleItems.AsNoTracking().Where(i => i.ProductId == productId).SumAsync(i => i.QtyBase);
            Assert.Equal(7, sold); // penjualan tercatat sama dengan stok yang keluar
        }

        await AssertConsistentAsync();
    }

    [Fact]
    public async Task B2b_Kunci_kasir_yang_sama_dikirim_serentak_menghasilkan_satu_struk_yang_sama_bukan_error()
    {
        var shop = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung Struk");
        var productId = await _w.CreateRetailProductAsync(shop, "Teh", 10, 4_000m);
        var key = Guid.NewGuid().ToString("N");

        var results = await V3World.RaceAsync(
            () => _w.Open(shop).Sales.ConfirmAsync(new ConfirmSaleRequest(new[] { new SaleLineInput(productId, 2) }, "CASH", key, null)),
            () => _w.Open(shop).Sales.ConfirmAsync(new ConfirmSaleRequest(new[] { new SaleLineInput(productId, 2) }, "CASH", key, null)),
            () => _w.Open(shop).Sales.ConfirmAsync(new ConfirmSaleRequest(new[] { new SaleLineInput(productId, 2) }, "CASH", key, null)));

        Assert.All(results, r => Assert.True(r.Succeeded, r.Error)); // V2: salah satu menerima pesan error
        Assert.Single(results.Select(r => r.Value!.Number).Distinct());
        Assert.Equal(1, await _w.Open(_w.Admin()).Db.Sales.CountAsync());
        Assert.Equal(8, (await InventoryOf(shop, productId)).QtyBase);
    }

    // ------------------------------------------------------------------ B3

    [Fact]
    public async Task B3_Dua_warung_serentak_memesan_stok_grosir_terbatas_hanya_satu_pesanan_terbentuk()
    {
        var wholesaler = await _w.RegisterBusinessAsync(MarketRole.Wholesale, "Grosir Terbatas", -6.915, 107.605);
        await _w.SetDeliveryAsync(wholesaler);
        var retailerA = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung A");
        var retailerB = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung B", -6.92, 107.62);

        for (var trial = 0; trial < 10; trial++)
        {
            var productId = await _w.CreateStockedProductAsync(wholesaler, "Gula " + trial, 10); // 10 Dus = 100 Pcs
            CreateOrderRequest Req(string key) => new(
                wholesaler.BusinessId!.Value, FulfillmentType.Pickup, null, null, new[] { new OrderLineInput(productId, "Dus", 7) }, null, null, key);

            var results = await V3World.RaceAsync(
                () => _w.Open(retailerA).Orders.CreateAsync(Req($"a-{trial}")),
                () => _w.Open(retailerB).Orders.CreateAsync(Req($"b-{trial}")));

            Assert.Equal(1, results.Count(r => r.Succeeded));
            Assert.Contains("tidak cukup", results.Single(r => !r.Succeeded).Error!);
            var inv = await InventoryOf(wholesaler, productId);
            Assert.Equal(100, inv.QtyBase);
            Assert.Equal(70, inv.ReservedBase);
        }

        await AssertConsistentAsync();
    }

    // ------------------------------------------------------------------ B4 (kombinasi jalur: pembeli akhir vs kasir)

    [Fact]
    public async Task B4_Pesanan_pembeli_dan_kasir_serentak_berebut_stok_eceran_yang_sama_hanya_satu_menang()
    {
        var shop = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung Campur");
        var buyer = await _w.RegisterConsumerAsync();
        for (var trial = 0; trial < 12; trial++)
        {
            var productId = await _w.CreateRetailProductAsync(shop, "Roti " + trial, 10, 6_000m);
            var results = await V3World.RaceAsync(
                async () => (await _w.Open(buyer).ConsumerOrders.PlaceAsync(Pickup(shop, productId, 7, $"c-{trial}"))).Succeeded,
                async () => (await _w.Open(shop).Sales.ConfirmAsync(new ConfirmSaleRequest(
                    new[] { new SaleLineInput(productId, 7) }, "CASH", Guid.NewGuid().ToString("N"), null))).Succeeded);

            Assert.Equal(1, results.Count(ok => ok));
            var inv = await InventoryOf(shop, productId);
            Assert.True(inv.QtyBase - inv.ReservedBase >= 0);
            // Hanya dua hasil yang sah: kasir menang (7 keluar fisik, tidak ada reservasi) ATAU pembeli menang (stok fisik utuh, 7 ditahan).
            Assert.True((inv.QtyBase, inv.ReservedBase) is (3, 0) or (10, 7), $"Hasil tidak sah: stok={inv.QtyBase}, ditahan={inv.ReservedBase}");
        }

        await AssertConsistentAsync();
    }

    // ------------------------------------------------------------------ B5

    private async Task<(V3World.Actor Wholesaler, V3World.Actor Retailer, int OrderId, int ProductId)> DeliveredB2BOrderAsync()
    {
        var wholesaler = await _w.RegisterBusinessAsync(MarketRole.Wholesale, "Grosir Terima", -6.915, 107.605);
        await _w.SetDeliveryAsync(wholesaler, delivery: true, fee: 15_000m);
        var retailer = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung Terima");
        var expiry = _w.Today.AddDays(60);
        var productId = await _w.CreateStockedProductAsync(wholesaler, "Sabun", 20, expiry: expiry);
        var orderId = (await _w.Open(retailer).Orders.CreateAsync(new CreateOrderRequest(
            wholesaler.BusinessId!.Value, FulfillmentType.Delivery, null, "Jl. Warung No. 9, Bandung",
            new[] { new OrderLineInput(productId, "Dus", 2) }, null, null, "kunci-terima"))).Value;
        foreach (var action in new[] { OrderAction.Accept, OrderAction.Prepare, OrderAction.MarkReady, OrderAction.HandOver })
        {
            var result = await _w.Open(wholesaler).Orders.ActAsync(orderId, action, null);
            Assert.True(result.Succeeded, result.Error);
        }

        return (wholesaler, retailer, orderId, productId);
    }

    [Fact]
    public async Task B5_Penerimaan_barang_diulang_atau_serentak_hanya_menambah_stok_sekali()
    {
        var (_, retailer, orderId, _) = await DeliveredB2BOrderAsync();

        var results = await V3World.RaceAsync(
            () => _w.Open(retailer).Orders.ActAsync(orderId, OrderAction.ConfirmReceived, null),
            () => _w.Open(retailer).Orders.ActAsync(orderId, OrderAction.ConfirmReceived, null),
            () => _w.Open(retailer).Orders.ActAsync(orderId, OrderAction.ConfirmReceived, null));
        var again = await _w.Open(retailer).Orders.ActAsync(orderId, OrderAction.ConfirmReceived, null); // dan sekali lagi berurutan

        Assert.All(results, r => Assert.True(r.Succeeded, r.Error)); // pengulangan = sukses tanpa efek (V2: stok menjadi 2x lipat)
        Assert.True(again.Succeeded);

        var db = _w.Open(_w.Admin()).Db;
        Assert.Equal(1, await db.Purchases.CountAsync(p => p.OrderId == orderId));
        var buyerInventory = await db.Inventories.AsNoTracking().SingleAsync(i => i.BusinessId == retailer.BusinessId);
        Assert.Equal(20, buyerInventory.QtyBase); // 2 Dus x 10 Pcs, bukan 40
        Assert.Equal(1, await db.InventoryMovements.CountAsync(m => m.BusinessId == retailer.BusinessId && m.Type == MovementType.PurchaseIn));
        Assert.Equal(1, await db.Expenses.CountAsync(e => e.BusinessId == retailer.BusinessId)); // ongkir dicatat sekali
        Assert.Equal(1, await db.OrderEvents.CountAsync(e => e.OrderId == orderId && e.Status == OrderStatus.Completed));
        await AssertConsistentAsync();
    }

    // ------------------------------------------------------------------ B6

    [Fact]
    public async Task B6_Dua_permintaan_pembatalan_serentak_melepas_reservasi_tepat_sekali()
    {
        var shop = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung Batal");
        var buyer = await _w.RegisterConsumerAsync();
        var productId = await _w.CreateRetailProductAsync(shop, "Biskuit", 10, 7_000m);
        var keep = (await _w.Open(buyer).ConsumerOrders.PlaceAsync(Pickup(shop, productId, 4, "keep"))).Value;
        var cancel = (await _w.Open(buyer).ConsumerOrders.PlaceAsync(Pickup(shop, productId, 3, "cancel"))).Value;
        Assert.Equal(7, (await InventoryOf(shop, productId)).ReservedBase);

        var results = await V3World.RaceAsync(
            () => _w.Open(buyer).ConsumerOrders.ActAsync(cancel, OrderAction.BuyerCancel, null),
            () => _w.Open(buyer).ConsumerOrders.ActAsync(cancel, OrderAction.BuyerCancel, null),
            () => _w.Open(buyer).ConsumerOrders.ActAsync(cancel, OrderAction.BuyerCancel, null));

        Assert.All(results, r => Assert.True(r.Succeeded, r.Error));
        var inv = await InventoryOf(shop, productId);
        Assert.Equal(4, inv.ReservedBase); // tersisa reservasi pesanan yang tidak dibatalkan: dilepas sekali (7-3), bukan tiga kali
        Assert.Equal(10, inv.QtyBase);
        var db = _w.Open(_w.Admin()).Db;
        Assert.Equal(1, await db.StockReservations.CountAsync(r => r.RefId == cancel && r.Status == ReservationStatus.Released));
        Assert.Equal(1, await db.ConsumerOrderEvents.CountAsync(e => e.ConsumerOrderId == cancel && e.Status == OrderStatus.Cancelled));
        Assert.Equal(OrderStatus.Pending, (await db.ConsumerOrders.SingleAsync(o => o.Id == keep)).Status);
        await AssertConsistentAsync();
    }

    // ------------------------------------------------------------------ B7

    [Fact]
    public async Task B7_Pesanan_yang_sudah_dikirim_tidak_dapat_dibatalkan_lewat_jalur_pembatalan_sebelum_pengiriman()
    {
        var (wholesaler, retailer, orderId, productId) = await DeliveredB2BOrderAsync();
        var before = await InventoryOf(wholesaler, productId);

        var buyerCancel = await _w.Open(retailer).Orders.ActAsync(orderId, OrderAction.BuyerCancel, null);
        var sellerCancel = await _w.Open(wholesaler).Orders.ActAsync(orderId, OrderAction.SellerCancel, "Salah kirim");
        Assert.Equal(ErrorKind.Conflict, buyerCancel.Kind);
        Assert.Equal(ErrorKind.Conflict, sellerCancel.Kind);

        var after = await InventoryOf(wholesaler, productId);
        Assert.Equal(before.QtyBase, after.QtyBase);
        Assert.Equal(before.ReservedBase, after.ReservedBase);
        Assert.Equal(OrderStatus.Delivered, (await _w.Open(_w.Admin()).Db.Orders.SingleAsync(o => o.Id == orderId)).Status);

        // Hal yang sama untuk pesanan pembeli akhir yang sudah diserahkan.
        var shop = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung Serah");
        var buyer = await _w.RegisterConsumerAsync();
        var pid = await _w.CreateRetailProductAsync(shop, "Keripik", 10, 8_000m);
        var corder = (await _w.Open(buyer).ConsumerOrders.PlaceAsync(Pickup(shop, pid, 2, "serah"))).Value;
        foreach (var action in new[] { OrderAction.Accept, OrderAction.Prepare, OrderAction.MarkReady, OrderAction.HandOver })
        {
            Assert.True((await _w.Open(shop).ConsumerOrders.ActAsync(corder, action, null)).Succeeded);
        }

        Assert.Equal(ErrorKind.Conflict, (await _w.Open(buyer).ConsumerOrders.ActAsync(corder, OrderAction.BuyerCancel, null)).Kind);
        Assert.Equal(ErrorKind.Conflict, (await _w.Open(shop).ConsumerOrders.ActAsync(corder, OrderAction.SellerCancel, "batal")).Kind);
        Assert.Equal(8, (await InventoryOf(shop, pid)).QtyBase);
        await AssertConsistentAsync();
    }

    // ------------------------------------------------------------------ B8

    [Fact]
    public async Task B8_Exception_setelah_perubahan_stok_dan_sebelum_commit_membatalkan_semuanya()
    {
        var shop = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung Gagal");
        var productId = await _w.CreateRetailProductAsync(shop, "Selai", 10, 9_000m);
        var s = _w.Open(shop);

        // (1) pekerjaan mengurangi stok + menambah stok lain lalu GAGAL (hasil gagal) -> rollback penuh
        var failed = await s.Uow.ExecuteAsync<int>(async ct =>
        {
            var out1 = await s.Inventory.StockOutAsync(new StockOutRequest(productId, 6, MovementType.CorrectionOut), ct);
            Assert.True(out1.Succeeded);
            return Result<int>.Fail(ErrorKind.Conflict, "gagal sebelum commit");
        });
        Assert.False(failed.Succeeded);
        Assert.Equal(10, (await InventoryOf(shop, productId)).QtyBase);

        // (2) exception tak terduga di tengah unit kerja -> rollback penuh, tidak ada movement yatim
        var s2 = _w.Open(shop);
        await Assert.ThrowsAsync<ApplicationException>(() => s2.Uow.ExecuteAsync<int>(async ct =>
        {
            var removed = await s2.Inventory.StockOutAsync(new StockOutRequest(productId, 6, MovementType.CorrectionOut), ct);
            Assert.True(removed.Succeeded, removed.Error);
            throw new ApplicationException("kegagalan simulasi");
        }));
        Assert.Equal(10, (await InventoryOf(shop, productId)).QtyBase);
        var db = _w.Open(_w.Admin()).Db;
        Assert.Equal(0, await db.InventoryMovements.CountAsync(m => m.ProductId == productId && m.Type == MovementType.CorrectionOut));

        // (3) pesanan dengan dua baris: baris kedua kekurangan stok -> tidak ada pesanan dan tidak ada reservasi baris pertama
        var productB = await _w.CreateRetailProductAsync(shop, "Madu", 2, 20_000m);
        var buyer = await _w.RegisterConsumerAsync();
        var result = await _w.Open(buyer).ConsumerOrders.PlaceAsync(new PlaceConsumerOrderRequest(
            shop.BusinessId!.Value, FulfillmentType.Pickup, PaymentMethod.CashOnPickup, null, null,
            new[] { new ConsumerLineInput(productId, 3), new ConsumerLineInput(productB, 5) }, "dua-baris"));
        Assert.False(result.Succeeded);
        db = _w.Open(_w.Admin()).Db;
        Assert.Equal(0, await db.ConsumerOrders.CountAsync());
        Assert.Equal(0, await db.StockReservations.CountAsync());
        Assert.Equal(0, (await InventoryOf(shop, productId)).ReservedBase);
        await AssertConsistentAsync();
    }

    [Fact]
    public async Task B8b_Kegagalan_di_tengah_reservasi_setelah_baris_pertama_berhasil_tidak_meninggalkan_reservasi_yatim()
    {
        var shop = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung Tengah");
        var p1 = await _w.CreateRetailProductAsync(shop, "Produk A", 5, 5_000m);
        var p2 = await _w.CreateRetailProductAsync(shop, "Produk B", 5, 5_000m);
        var s = _w.Open(shop);

        var result = await s.Uow.ExecuteAsync<IReadOnlyList<ReservedAllocation>>(async ct =>
        {
            var ok = await s.Reservations.ReserveAsync(shop.BusinessId!.Value, ReservationRefTypes.Order, 1, new[] { new ReserveLine(1, p1, 3) }, ct);
            Assert.True(ok.Succeeded);
            // baris kedua gagal (stok tidak cukup) dan unit kerja membatalkan reservasi baris pertama
            var bad = await s.Reservations.ReserveAsync(shop.BusinessId!.Value, ReservationRefTypes.Order, 1, new[] { new ReserveLine(2, p2, 99) }, ct);
            return bad;
        });

        Assert.False(result.Succeeded);
        var db = _w.Open(_w.Admin()).Db;
        Assert.Equal(0, await db.StockReservations.CountAsync());
        Assert.Equal(0, (await db.Inventories.AsNoTracking().SingleAsync(i => i.ProductId == p1)).ReservedBase);
    }

    // ------------------------------------------------------------------ B9

    [Fact]
    public async Task B9_Alokasi_batch_untuk_dua_pesanan_tidak_melebihi_jumlah_batch()
    {
        var shop = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung Batch");
        var buyerA = await _w.RegisterConsumerAsync("Pembeli A");
        var buyerB = await _w.RegisterConsumerAsync("Pembeli B");
        for (var trial = 0; trial < 10; trial++)
        {
            var productId = await _w.CreateRetailProductAsync(shop, "Yoghurt " + trial, 10, 7_000m, expiry: _w.Today.AddDays(30));

            var results = await V3World.RaceAsync(
                () => _w.Open(buyerA).ConsumerOrders.PlaceAsync(Pickup(shop, productId, 7, $"a-{trial}")),
                () => _w.Open(buyerB).ConsumerOrders.PlaceAsync(Pickup(shop, productId, 7, $"b-{trial}")));

            Assert.Equal(1, results.Count(r => r.Succeeded));
            var batch = await _w.Open(_w.Admin()).Db.StockBatches.AsNoTracking().SingleAsync(b => b.ProductId == productId);
            Assert.Equal(10, batch.QtyRemainingBase);
            Assert.Equal(7, batch.ReservedBase);
            Assert.True(batch.ReservedBase <= batch.QtyRemainingBase);
        }

        await AssertConsistentAsync();
    }

    // ------------------------------------------------------------------ B10

    [Fact]
    public async Task B10_Reservasi_dibatalkan_memulihkan_stok_tersedia_tepat_sekali_termasuk_pembatalan_setelah_diterima()
    {
        var shop = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung Pulih");
        var buyer = await _w.RegisterConsumerAsync();
        var productId = await _w.CreateRetailProductAsync(shop, "Cokelat", 10, 12_000m, expiry: _w.Today.AddDays(90));

        // (a) pembatalan oleh pembeli saat menunggu
        var first = (await _w.Open(buyer).ConsumerOrders.PlaceAsync(Pickup(shop, productId, 6, "p-1"))).Value;
        var inv = await InventoryOf(shop, productId);
        Assert.Equal((10, 6), (inv.QtyBase, inv.ReservedBase));
        Assert.True((await _w.Open(buyer).ConsumerOrders.ActAsync(first, OrderAction.BuyerCancel, null)).Succeeded);
        Assert.True((await _w.Open(buyer).ConsumerOrders.ActAsync(first, OrderAction.BuyerCancel, null)).Succeeded); // ulang
        inv = await InventoryOf(shop, productId);
        Assert.Equal((10, 0), (inv.QtyBase, inv.ReservedBase));

        // (b) penjual menerima lalu membatalkan: stok keluar saat diterima dan KEMBALI sekali saat dibatalkan
        var second = (await _w.Open(buyer).ConsumerOrders.PlaceAsync(Pickup(shop, productId, 6, "p-2"))).Value;
        Assert.True((await _w.Open(shop).ConsumerOrders.ActAsync(second, OrderAction.Accept, null)).Succeeded);
        inv = await InventoryOf(shop, productId);
        Assert.Equal((4, 0), (inv.QtyBase, inv.ReservedBase));
        Assert.True((await _w.Open(shop).ConsumerOrders.ActAsync(second, OrderAction.SellerCancel, "Barang rusak")).Succeeded);
        Assert.True((await _w.Open(shop).ConsumerOrders.ActAsync(second, OrderAction.SellerCancel, "Barang rusak")).Succeeded); // ulang
        inv = await InventoryOf(shop, productId);
        Assert.Equal((10, 0), (inv.QtyBase, inv.ReservedBase));

        var db = _w.Open(_w.Admin()).Db;
        Assert.Equal(1, await db.InventoryMovements.CountAsync(m => m.ProductId == productId && m.Type == MovementType.OrderReturnIn));
        var batch = await db.StockBatches.AsNoTracking().SingleAsync(b => b.ProductId == productId);
        Assert.Equal((10, 0), (batch.QtyRemainingBase, batch.ReservedBase)); // batch ikut pulih dengan kadaluarsa yang sama
        await AssertConsistentAsync();
    }

    // ------------------------------------------------------------------ B11 (uji beban kecil)

    [Fact]
    public async Task B11_Dua_puluh_pesanan_serentak_untuk_stok_10_tepat_sepuluh_berhasil_dan_selesai_dalam_batas_wajar()
    {
        var shop = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung Beban");
        var productId = await _w.CreateRetailProductAsync(shop, "Air Galon", 10, 20_000m);
        var buyers = new List<V3World.Actor>();
        for (var i = 0; i < 5; i++)
        {
            buyers.Add(await _w.RegisterConsumerAsync("Pembeli Beban " + i));
        }

        var work = Enumerable.Range(0, 20)
            .Select(i => (Func<Task<Result<int>>>)(() => _w.Open(buyers[i % 5]).ConsumerOrders.PlaceAsync(Pickup(shop, productId, 1, "beban-" + i))))
            .ToArray();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var results = await V3World.RaceAsync(work);
        watch.Stop();
        Console.WriteLine($"[BEBAN] 20 pesanan serentak (stok 10): {results.Count(r => r.Succeeded)} berhasil, {results.Count(r => !r.Succeeded)} ditolak, waktu total {watch.ElapsedMilliseconds} ms");

        Assert.Equal(10, results.Count(r => r.Succeeded));
        Assert.All(results.Where(r => !r.Succeeded), r => Assert.Equal(ErrorKind.Conflict, r.Kind));
        var inv = await InventoryOf(shop, productId);
        Assert.Equal((10, 10), (inv.QtyBase, inv.ReservedBase));
        Assert.True(watch.ElapsedMilliseconds < 10_000, $"20 operasi serentak memakan {watch.ElapsedMilliseconds} ms");
        await AssertConsistentAsync();
    }
}
