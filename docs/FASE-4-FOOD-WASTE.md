# Fase 4 — Anti Food Waste, ongkir jarak, pelacakan, struk

Dokumen rancangan untuk fitur yang ditambahkan di atas fase 1–3. Semua perhitungan penting dilakukan **di server**
(browser hanya menampilkan pratinjau) dan ditutup test di `tests/Platform.Tests`.

## 1. Batch kadaluarsa dan FEFO (modul Stock)
* `StockBatch` = satu lot stok dengan `ExpiryDate`. Jumlah semua batch produk selalu ≤ `Inventory.QtyBase`; selisihnya adalah stok "belum bertanggal".
* **FEFO**: setiap barang keluar (kasir, pesanan grosir, koreksi) mengambil batch yang paling dekat kadaluarsa lebih dulu, lalu sisanya dari stok belum bertanggal.
  `StockOutRequest.BatchId` memaksa pengambilan dari batch tertentu (dipakai paket dan pembuangan batch).
* Stok masuk membawa `BatchPortion` (tanggal + jumlah); tanggal sama digabung, modal dihitung rata-rata berbobot.
* `IExpiryService`: daftar batch, ringkasan per pita umur (Lewat / 0–7 / 8–14 / 15–30 / Aman), menandai stok lama, membuang batch kadaluarsa (tercatat sebagai pergerakan *Kadaluarsa*).
* Tanggal kadaluarsa **ikut berpindah** dari grosir ke warung: saat pesanan diterima grosir, alokasi batch disimpan di `OrderItem.ExpirySnapshot`;
  bila pesanan dibatalkan, batch asal dipulihkan; saat warung konfirmasi, batch yang sama dibuat di stok warung.

## 2. Paket (bundle) dan penawaran (modul Rescue)
* `RescueBundle` + `RescueBundleItem`: beberapa batch dijual bersama dengan harga miring. Harga normal = harga grosir termurah per satuan dasar. Paket tidak boleh berlaku melewati kadaluarsa barang terdekat.
* Ketersediaan paket = `min(sisa batch / jumlah per paket)`, dihitung langsung dari stok (tidak ada penghitung ganda).
* Pesanan paket: harga dan isi **dihitung ulang di server**, batch ditunjuk eksplisit (bukan FEFO biasa), subtotal dibagi proporsional ke tiap baris, dan **tidak dikenai minimal pesanan** agar stok mudah terjual.
* `RescueOffer`: penawaran grosir → satu warung (privat atau tayang juga di marketplace), berstatus Terkirim / Diambil / Ditolak / Ditarik, kedaluwarsa otomatis menurut `ValidUntil`.
* **Rekomendasi** (dashboard grosir): untuk tiap batch ≤ 30 hari, warung diurutkan menurut kecepatan jual 30 hari. Jumlah disarankan
  `= min(sisa batch, kecepatan_per_hari × (sisa_hari − 3) − stok_warung)`; warung yang stoknya sudah cukup tidak direkomendasikan.
  Saran diskon: ≤3 hari 50%, ≤7 hari 40%, ≤14 hari 30%, ≤30 hari 20%, selebihnya 10%.

## 3. Data penjualan warung (modul Insights) dan privasi
Satu-satunya tempat data penjualan warung dibaca lintas usaha (`ISellThroughService`), dengan batasan:
1. hanya untuk grosir yang sedang login; 2. hanya warung yang pernah memesan ke grosir itu (pesanan diterima atau lebih lanjut);
3. hanya warung yang mengaktifkan **"Bagikan ringkasan penjualan"** (Profil, bawaan: aktif); 4. hanya angka agregat jumlah terjual per produk grosir
(pencocokan lewat `SourceProductId`, lalu SKU, lalu nama) — tanpa struk, harga jual, atau keuntungan.

## 4. Ongkir berdasarkan jarak (modul Delivery)
`ongkir = tarif_dasar + max(0, jarak_jalan − jarak_termasuk) × tarif_per_km`, dibulatkan ke atas per Rp500; `jarak_jalan = garis_lurus × 1,3`.
`MaxDistanceKm` membatasi jangkauan (di luar itu hanya ambil sendiri). Tarif per km 0 = tarif tetap (kompatibel dengan data lama).
Dihitung oleh `DeliveryFeeCalculator` (murni, diuji) dan dipakai marketplace, peta, paket, serta saat pesanan dibuat (jarak disimpan di `Order.DeliveryDistanceKm`).

## 5. Pelacakan pesanan
* `OrderEvent` mencatat setiap perubahan status. `OrderTracker` menyusun fase (dibuat → diterima → disiapkan → siap → dikirim/diambil → selesai; atau berhenti dengan alasan bila ditolak/dibatalkan).
* **Posisi truk diperkirakan**, bukan GPS: diam di toko sebelum berangkat; setelah *Dikirim* bergerak sesuai `waktu_tempuh = jarak/25 km/jam + 10 menit`,
  berhenti di 95% sampai warung mengonfirmasi. Peta menggambar rute jalan dari OSRM (gratis) dengan cadangan garis melengkung.
* Halaman memeriksa status tiap 20 detik dan memuat ulang bila berubah; lonceng notifikasi memeriksa tiap 45 detik. Tanpa WebSocket/layanan push.
* Naik ke *real-time* penuh nanti: ganti polling dengan SignalR dan kirim koordinat kurir dari aplikasi kurir; antarmuka `OrderTrackingInfo` sudah memuat koordinat kedua titik.

## 6. Notifikasi
`INotificationService` menghitung dari data terkini (pesanan menunggu, penawaran baru, stok menipis, batch lewat/segera kadaluarsa, antrean verifikasi admin). Tanpa tabel tambahan.

## 7. Struk dan nota
* Kasir menerima **uang diterima** (tunai); kembalian dihitung server. Non-tunai dianggap pas.
* Struk thermal 58/80 mm (`@page` menyesuaikan ukuran kertas), kode batang Code 39, tombol cetak, **kirim WhatsApp** (teks struk lebar 32 karakter), salin teks.
* Nota pesanan A5 dengan data dua pihak, rincian paket, penghematan, ongkir, dan kolom tanda tangan.

## 8. Batasan yang disengaja (untuk tahap berikutnya)
* Pembayaran online tidak ada (pembayaran diatur antara warung dan grosir).
* Pelacakan bukan GPS sungguhan; rute memakai layanan demo OSRM.
* Migration EF belum dibuat (mode `EnsureCreated` untuk demo). Untuk produksi: `dotnet ef migrations add FoodWaste` lalu `Database:Mode = Migrate`.
* Halaman admin, produk, pembelian, riwayat penjualan, dan keuangan sudah memakai gaya baru lewat sistem desain, tetapi belum dirombak tata letaknya satu per satu.
