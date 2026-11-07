# Tahap Demo: Katalog, Stok, Pembelian, POS, Penjualan, Keuangan, Dashboard + Data Dummy

Cakupan: Fase 2–5 pada rencana MVP, disederhanakan untuk **demo kuliah** (SQLite, data dummy, tanpa migration).
Marketplace, Order, Map, Delivery/Pickup, dan Admin dikerjakan pada tahap berikutnya: lihat `FASE-3-MARKETPLACE.md`.

## 1. Modul baru
| Modul | Isi |
|---|---|
| Template | `TemplateCatalog` (GROCERY, CLOTHING, ELECTRONICS, OTHER): modul aktif, satuan dasar, field tambahan. Menu dan form dirender dari template |
| Catalog | Produk, kategori, satuan dasar + kemasan (`ProductUnit`), validasi, SKU unik per usaha, nonaktifkan (tidak dihapus) |
| Stock (modul Inventory) | `Inventory` + `InventoryMovement`: stok masuk/keluar, **moving average cost**, koreksi, harga jual, stok minimum |
| Purchasing | Pembelian (satuan kemasan → satuan dasar) + stok masuk, satu transaksi |
| Sales (POS) | Keranjang manual, konfirmasi atomik, struk, anti-duplikat, "Penjualan Hari Ini" + filter periode |
| Accounting | Pengeluaran operasional |
| Reporting | Query baca: laporan penjualan, ringkasan keuangan, dashboard |

Folder modul Inventory bernama `Stock` karena nama `Inventory` bentrok dengan entity `Inventory` (aturan nama C#).

## 2. Tabel baru
`Categories` (seed), `Products`, `ProductUnits`, `Inventories`, `InventoryMovements`, `Purchases`, `PurchaseItems`, `Sales`, `SaleItems`, `Expenses`.
Semua tabel bisnis memuat `BusinessId` dan terkena filter tenant otomatis. Harga, isi kemasan, dan modal disimpan sebagai **snapshot** pada item transaksi.

## 3. Aturan yang diterapkan
- Kemasan disimpan relatif ke satuan dasar: 1 Dus = 160 Item langsung. Stok selalu dalam satuan dasar.
- Pembelian 2 Box × 40 = 80 Item; modal Rp120.000 / 40 = Rp3.000 per Item.
- Moving average hanya saat stok **masuk**: (80 × 3.000 + 40 × 3.125) / 120 = 3.041,6667. Stok keluar memakai average cost saat itu sebagai COGS.
- Stok **tidak pernah negatif**: penjualan/koreksi yang melebihi stok ditolak sebelum data berubah.
- Penjualan atomik: Sale + SaleItem + movement + update stok dalam satu transaksi; gagal di langkah mana pun = semua dibatalkan.
- `IdempotencyKey` mencegah transaksi ganda saat tombol diklik dua kali.
- Omzet ≠ keuntungan: kotor = omzet − COGS; bersih = kotor − biaya operasional. Pembelian barang bukan biaya periode (masuk persediaan).
- Waktu disimpan UTC; tampilan dan filter periode memakai WIB (UTC+7).

## 4. Data dummy
Dibuat lewat layanan modul yang sama (bukan insert langsung), sehingga stok, modal, dan laporan konsisten.
4 usaha, 5 akun (lihat README), 7 produk grosir dengan kemasan, stok warung + riwayat 7 hari (±45 transaksi), 4 pengeluaran.
**Hari ini sengaja kosong** agar skenario demo langsung menghasilkan angka bersih (omzet Rp17.500, untung Rp2.500).
Mi Instan di Warung Maju sengaja berstok 0 untuk skenario beli 2 Box lalu jual 5 Item.

## 5. Asumsi baru (tambahkan ke bagian 0 dokumen analisis)
| ID | Asumsi |
|---|---|
| A-18 | Demo memakai SQLite + `EnsureCreated` (tanpa migration); PostgreSQL tetap didukung lewat `Database:Provider` |
| A-19 | Mode Development menghapus dan membuat ulang database setiap start, lalu mengisi data dummy |
| A-20 | WIB memakai offset tetap UTC+7 (tanpa database zona waktu) |
| A-21 | POS menjual dalam satuan dasar; harga jual ditentukan per satuan dasar |
| A-22 | Produk tidak dihapus, hanya dinonaktifkan |
| A-23 | Template berupa kode (`TemplateCatalog`), bukan tabel database |
| A-24 | Stok negatif selalu ditolak (opsi per usaha belum dibuat) |
| A-25 | Varian produk (baju) belum diimplementasi; hanya template yang menyiapkannya |
| A-26 | Tanpa concurrency token: dua kasir menjual produk yang sama pada saat bersamaan tidak ditangani |

## 6. Dokumen analisis yang harus diperbarui
| Bagian | Perubahan |
|---|---|
| 0 Asumsi | Tambah A-18 s.d. A-26 |
| 5–6 Modul/dependensi | Tambah modul Template dan Reporting; panah: Stock → Catalog, Purchasing → Stock, Sales → Stock, Reporting membaca Sales/Expense/Stock |
| 7 Arsitektur | Folder modul Inventory = `Stock`; basis data demo SQLite |
| 8–10 ERD/entity | Tambah entity di bagian 2; `ProductPackaging` dilebur ke `ProductUnit`; `ProductVariant` belum ada; `Payment`, `Orders`, `DeliveryOption`, `StoreLocation` belum ada |
| 14–18 Flow | Sesuai bagian 3 dokumen ini |
| 24 Testing | Lihat bagian 7 |

## 7. Test dan verifikasi
Test otomatis tahap ini (xUnit, SQLite in-memory): skenario end-to-end Mi Instan, stok tidak negatif, modal rata-rata, anti-duplikat,
isolasi tenant, hitungan kemasan, zona waktu WIB, format Rupiah. Jumlah keseluruhan proyek ada di `FASE-3-MARKETPLACE.md`.

Verifikasi yang sudah dilakukan saat pengembangan (di lingkungan tanpa akses NuGet):
- Seluruh kode (termasuk view Razor dan test) **dikompilasi** dengan .NET 10 SDK; paket EF Core/xUnit diganti stub.
- 37 kasus uji **dijalankan** terhadap EF Core palsu berbasis memori (filter tenant, transaksi + rollback): lulus semua.
- Seeder data dummy dijalankan sampai selesai; angkanya diperiksa.
- Aplikasi web **dijalankan sungguhan** dan diuji lewat HTTP: 76 pemeriksaan (login, role, antiforgery, kasir JSON, filter periode, registrasi, template): lulus semua.

Belum terverifikasi (butuh EF Core/SQLite asli di komputer Anda): terjemahan LINQ ke SQL, pemetaan kolom, validasi model EF saat start.
Jalankan `dotnet run` dan `dotnet test`; bila ada error, tempel ke chat.

## 8. Acceptance criteria (centang setelah dicoba di komputer Anda)
- [ ] Katalog: tambah/ubah/nonaktifkan produk dengan kemasan; SKU duplikat ditolak
- [ ] Stok: pembelian 2 Box → 80 Item, modal Rp3.000; riwayat pergerakan lengkap
- [ ] POS: jual 5 Item → stok 75; stok tidak cukup ditolak; klik ganda tidak menggandakan
- [ ] Penjualan Hari Ini: filter hari ini/kemarin/minggu/bulan/rentang; omzet, modal, untung benar
- [ ] Keuangan: omzet, HPP, untung kotor, biaya operasional, hasil bersih
- [ ] Dashboard: kartu, grafik 7 hari, stok menipis
- [ ] RBAC dan isolasi: warung lain/grosir tidak bisa membuka data yang bukan miliknya
