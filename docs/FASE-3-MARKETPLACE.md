# Tahap Marketplace: Delivery/Pickup, Marketplace, Order, Map, Admin

Melengkapi alur utama platform: warung mencari grosir terverifikasi, memesan, grosir memproses, barang diterima warung
(stok masuk otomatis), plus peta lokasi dan halaman admin.

## 1. Modul baru
| Modul | Isi |
|---|---|
| Delivery | `DeliveryOption` per grosir: pengiriman/ambil sendiri, tarif tetap, minimum pesanan, keterangan |
| Marketplace | Cari grosir terverifikasi (kata kunci, kategori, jarak Haversine, cara pengambilan), katalog + harga + stok publik |
| Orders | `Order` + `OrderItem`, state machine, reservasi stok grosir, penerimaan warung (pembelian + stok + ongkir) |
| Map | Peta Leaflet + OpenStreetMap, titik grosir + lokasi usaha sendiri, pemilih lokasi di Profil/Registrasi |
| Admin | Dashboard, pengguna, verifikasi/aktif usaha, pesanan, log aktivitas, kategori |

## 2. Perubahan database (dibuat otomatis oleh `EnsureCreated`)
Tabel baru: `DeliveryOptions`, `Orders`, `OrderItems`. Kolom baru: `Products.SourceProductId` (tanpa foreign key).
Jenis pergerakan stok baru: `OrderOut` (grosir menerima pesanan) dan `OrderReturnIn` (pesanan batal).

## 3. Alur pesanan
```
Warung: pilih grosir → isi jumlah per kemasan → pilih Kirim/Ambil sendiri → Buat pesanan      [PENDING]
Grosir: Terima (stok grosir dikurangi) ───────────────────────────────────────────────────→ [ACCEPTED]
        Mulai siapkan → Siap → Tandai dikirim / diambil ────────────────────────→ [PREPARING → READY → DELIVERED | PICKED_UP]
Warung: Konfirmasi barang diterima ───────────────────────────────────────────────────────→ [COMPLETED]
        (pembelian dicatat, stok warung bertambah, modal rata-rata diperbarui, ongkir → pengeluaran Transportasi)
Cabang: Grosir Tolak (PENDING, wajib alasan) → REJECTED · Warung Batal (PENDING) → CANCELLED · Grosir Batal (ACCEPTED, stok kembali) → CANCELLED
```
Aturan penting:
- **Server menghitung ulang** harga, stok, minimum pesanan, dan ongkir dari katalog grosir (tidak percaya input browser).
- Pesanan menyimpan **snapshot** nama, kemasan, isi, harga: perubahan harga grosir tidak mengubah pesanan berjalan.
- Setiap perubahan stok terjadi di **tenant pelakunya**: grosir mengurangi/mengembalikan stok sendiri, warung menambah stok sendiri. Tidak ada lintas-tenant tulis.
- Perpindahan status dijaga `OrderStateMachine` (murni, dapat diuji) dan pelaku yang berwenang (pembeli/penjual).
- Saat diterima grosir, stok dicek ulang: dua pesanan yang gabungannya melebihi stok → pesanan kedua gagal diterima.
- Produk warung dicocokkan dengan produk grosir: `SourceProductId` → SKU → nama; bila belum ada, dibuat otomatis (satuan dasar dan kemasan disalin; **harga jual tetap ditentukan warung**).
- Semua langkah penerimaan dalam satu transaksi: gagal di mana pun = status pesanan dan stok tidak berubah.

## 4. Keamanan dan isolasi
- Pesanan dimiliki dua usaha: filter query global `ITwoPartyOwned` (pembeli, penjual, atau admin). Usaha lain mendapat 404.
- Satu-satunya pembacaan lintas usaha adalah katalog publik marketplace dan info pengiriman (`IgnoreQueryFilters`, hanya di `MarketplaceService` dan `DeliveryService.GetPublicAsync`).
- Grosir tampil di Marketplace/Peta hanya bila **VERIFIED + aktif**; hanya produk berharga grosir.
- **Sesi dicek ulang setiap request** (`CookieSessionValidator` + `ILoginGate`): pengguna atau usaha yang dinonaktifkan admin langsung dikeluarkan dan tidak bisa masuk lagi. Ini menutup batasan A-16.
- Admin tidak dapat menonaktifkan dirinya sendiri; aksi admin tercatat di log aktivitas.

## 5. Data dummy tambahan
Grosir mengatur pengiriman (Sumber Rejeki: kirim Rp15.000 + ambil sendiri, minimum Rp100.000; Makmur Jaya: ambil sendiri saja).
Stok awal Warung Maju kini berasal dari **pesanan marketplace sungguhan** (diterima, disiapkan, diambil, dikonfirmasi, 8 hari lalu);
grosir menaikkan harga minyak lalu warung memesan lagi dengan ongkir (modal rata-rata terlihat di Stok). Pesanan yang masih berjalan:
satu **menunggu grosir** (Teh Botol) dan satu **sudah diambil, menunggu konfirmasi warung** (Gula dari Makmur Jaya).
Warung Berkah sengaja menunggu verifikasi.

## 6. Asumsi baru (tambahkan ke bagian 0 dokumen analisis)
| ID | Asumsi |
|---|---|
| A-27 | Satu pesanan = satu grosir; tidak ada keranjang tersimpan (pilih jumlah di halaman katalog grosir) |
| A-28 | Stok grosir dikurangi saat pesanan **diterima**; pembeli hanya dapat membatalkan saat PENDING; grosir dapat membatalkan saat ACCEPTED (stok kembali) |
| A-29 | Hanya akun warung yang memesan; grosir tidak memesan ke grosir lain |
| A-30 | Pembayaran tidak dilacak platform (dilakukan di luar sistem); tidak ada entity Payment pada demo |
| A-31 | Ongkir tarif tetap per grosir; dicatat sebagai pengeluaran Transportasi saat COMPLETED |
| A-32 | Peta memakai Leaflet + OpenStreetMap dari CDN (butuh internet); daftar grosir tetap tampil tanpa internet. Tanpa geocoding alamat: pilih titik di peta atau isi koordinat |
| A-33 | Notifikasi belum ada; perubahan status dilihat di halaman Pesanan |
| A-34 | Belum ada retur, komplain, atau penerimaan sebagian |
| A-35 | Satuan dasar juga dapat dipesan bila grosir memberi harga per satuan dasar |

## 7. Dokumen analisis yang harus diperbarui
| Bagian | Perubahan |
|---|---|
| 0 Asumsi | Tambah A-27 s.d. A-35; A-16 tidak berlaku lagi (sesi dicek tiap request) |
| 5–6 Modul/dependensi | Tambah Delivery, Marketplace, Orders, Map, Admin. Panah: Marketplace → Delivery, BusinessProfile · Orders → Marketplace, Delivery, Catalog, Stock, Purchasing, Accounting · Map → Marketplace · Admin → membaca semua modul, mengubah lewat layanan modul pemilik. Catatan: arah Marketplace/Order berbalik dari rencana awal (Order membaca katalog Marketplace; form pesan berada di modul Orders) |
| 7 Arsitektur | Filter dua pihak (`ITwoPartyOwned`), pembacaan publik lintas usaha, `ILoginGate` + validasi sesi |
| 8–10 ERD/entity | Tambah `DeliveryOption` (1:1 Business), `Order`, `OrderItem`; `StoreLocation` dilebur ke `Business.Latitude/Longitude`; `Product.SourceProductId`; `Payment` ditunda |
| 11–13, 17 Flow | Sesuai bagian 3 dokumen ini |
| 24 Testing | Lihat bagian 8 |

## 8. Test dan verifikasi
Seluruh proyek: **59 metode / 66 kasus uji** (xUnit, SQLite in-memory). Tambahan tahap ini: skenario spesifikasi lengkap (grosir → pesan → terima → stok warung → jual 5 Item),
filter marketplace, validasi pesanan (minimum, stok, cara pengambilan), tolak/batal, aturan pihak yang berwenang, dua pesanan melebihi stok, isolasi pesanan,
ongkir → pengeluaran, penghubungan produk lewat SKU, aturan admin, `OrderStateMachine`, Haversine.

Verifikasi saat pengembangan (lingkungan tanpa akses NuGet):
- Seluruh kode dikompilasi dengan .NET 10 SDK; EF Core/xUnit diganti stub.
- 66 kasus uji **dijalankan** terhadap EF Core palsu berbasis memori (filter tenant, transaksi + rollback): lulus semua.
- Aplikasi web **dijalankan sungguhan** (Kestrel, routing, otorisasi, antiforgery, Razor, JSON) dan diuji lewat HTTP: **143 pemeriksaan** lulus; log server tanpa exception.
- Seeder dummy (termasuk alur pesanan) dijalankan sampai selesai dan angkanya diperiksa.

Belum terverifikasi (butuh EF Core/SQLite asli dan browser): terjemahan LINQ ke SQL, pemetaan kolom, validasi model EF saat start, tampilan peta Leaflet.
Jalankan `dotnet run` dan `dotnet test`; bila ada error, tempel ke chat.

## 9. Acceptance criteria (centang setelah dicoba di komputer Anda)
- [ ] Marketplace: grosir terverifikasi tampil terurut jarak; filter jarak/kategori/pengiriman/kata kunci benar
- [ ] Order: pesan 2 Box → grosir terima (stok −80) → siapkan → diambil → warung konfirmasi (stok warung +80, modal Rp3.000)
- [ ] Order: tolak wajib alasan; grosir batal setelah diterima mengembalikan stok; aksi oleh pihak yang salah ditolak
- [ ] Order: dikirim → ongkir menjadi pengeluaran Transportasi; harga/stok/minimum dihitung ulang di server
- [ ] Delivery: grosir mengatur pengiriman/ambil sendiri; warung tidak bisa memilih cara yang tidak disediakan
- [ ] Map: peta menampilkan grosir dan lokasi sendiri; klik peta di Profil mengisi koordinat
- [ ] Admin: verifikasi usaha; nonaktifkan pengguna/usaha (sesi langsung keluar); log aktivitas; kategori; pesanan seluruh platform
- [ ] Isolasi: usaha lain tidak dapat melihat atau memproses pesanan orang lain
