# Spesifikasi Kebutuhan Perangkat Lunak (SKPL) — Platform Rantai Pasok Pangan V3

Dokumen ini mengikuti format materi kuliah (bagian 3.1 dan 3.2) dan diperluas untuk V3. Semua status di bawah merujuk bukti
nyata di `docs/V3-PENGUJIAN.md` dan `docs/bukti/`. Status yang belum punya bukti ditulis **belum diuji**.

## 1. Latar belakang masalah
V3 berfokus pada nilai grosir dalam agregasi dan distribusi, cara warung membeli sesuai modal dan kebutuhan, konsistensi stok dan pembayaran saat transaksi berulang atau bersamaan, penanganan produk berisiko kedaluwarsa, serta pengukuran food waste dari data transaksi. Perhitungan deterministik dan rekomendasi dapat dijelaskan lewat rumus serta asumsi.

## 2. Aktor dan hak akses
| Aktor | Role akun | Terikat usaha | Ringkasan hak akses |
|---|---|---|---|
| Grosir | `GROSSIER` | ya | Katalog dan stok, menjual ke warung, paket anti-mubazir, data penjualan warung (dengan izin) |
| Warung | `RETAIL` | ya | Memesan dari grosir, POS, melayani pesanan pembeli, promo |
| Peternakan | `LIVESTOCK` | ya | Katalog, rencana produksi ternak, pencatatan hasil, dan manajemen stok |
| Pembeli akhir | `CONSUMER` | **tidak** | Katalog eceran warung, pesanan, pembayaran demo, riwayat; tidak melihat modal/stok internal/laporan |
| Admin | `ADMIN` | tidak | Verifikasi dan aktivasi usaha/akun, pantau pesanan, audit log, ringkasan + integritas stok; bukan pemilik transaksi bisnis |

Role akun (hak akses), peran usaha di rantai pasok (`MarketRole`: Retail, Wholesale, Livestock), dan jenis bisnis/template (GROCERY, CLOTHING, …) adalah tiga hal terpisah. Pendaftaran publik dapat membuat akun warung, grosir, peternakan, atau pembeli akhir, tetapi tidak `ADMIN` (diuji: E4).

## 3. Kebutuhan sistem
### 3.1 Kebutuhan Sistem Fungsional
Tabel berikut mempertahankan rumusan dari materi kuliah (nomor 1–19) dan menambahkan kebutuhan V3 (nomor 20–27).

| No. | Kebutuhan Sistem Fungsional | Tujuan dan Kegunaan |
|---|---|---|
| 1 | Registrasi dan login | Memungkinkan pemilik warung dan agen membuat akun serta masuk ke sistem sesuai hak aksesnya. *(V3: juga usaha peternakan dan pembeli akhir.)* |
| 2 | Pengelolaan profil usaha | Memungkinkan pengguna mengelola informasi usaha, seperti nama, alamat, kontak, dan jenis usaha. |
| 3 | Katalog produk | Membantu agen mengelola produk, kategori, harga, satuan, kemasan, dan informasi stok. *(V3: + MOQ per produk.)* |
| 4 | Marketplace grosir | Memudahkan warung mencari grosir terverifikasi berdasarkan produk, kategori, lokasi, jarak, dan pilihan pengiriman. |
| 5 | Pemesanan barang | Memungkinkan warung memesan produk dari grosir melalui aplikasi dan memantau status pesanannya. |
| 6 | Pengelolaan stok | Membantu warung dan agen mencatat stok masuk, stok keluar, koreksi stok, serta riwayat perubahan persediaan. |
| 7 | Pencatatan pembelian | Membantu mencatat pembelian barang dan memperbarui stok secara otomatis berdasarkan transaksi yang disimpan. |
| 8 | Kasir digital (POS) | Memudahkan warung mencatat penjualan kepada konsumen, menghitung total transaksi, dan menghasilkan struk. |
| 9 | Pengelolaan keuangan | Membantu mencatat biaya operasional dan menghitung omzet, HPP, laba kotor, serta hasil bersih berdasarkan data transaksi. |
| 10 | Laporan usaha | Menampilkan laporan penjualan, omzet, persediaan, dan ringkasan keuangan berdasarkan periode tertentu. |
| 11 | Pengiriman dan pengambilan barang | Memungkinkan grosir menentukan pilihan pengiriman atau pengambilan sendiri beserta tarif dan jangkauan layanan. |
| 12 | Peta lokasi usaha | Membantu warung menemukan lokasi grosir dan membantu grosir melihat persebaran lokasi warung. *(V3: mode lokal tanpa tile/routing eksternal.)* |
| 13 | Pengelolaan kedaluwarsa | Membantu memantau tanggal kedaluwarsa barang dan mengutamakan pengeluaran barang yang paling dekat kedaluwarsa melalui metode FEFO. *(V3: + nomor lot dan tanggal produksi; batch kedaluwarsa tidak dapat dijual.)* |
| 14 | Paket dan penawaran diskon | Membantu grosir menawarkan produk atau paket diskon, khususnya barang yang mendekati kedaluwarsa. |
| 15 | Ringkasan penjualan warung | Membantu grosir melihat jumlah produk yang terjual pada warung pelanggan yang memenuhi ketentuan akses dan memberikan izin berbagi data. |
| 16 | Pelacakan pesanan | Memungkinkan pengguna melihat tahapan pemrosesan pesanan dan perkiraan waktu pengiriman. *(Estimasi simulasi, bukan GPS.)* |
| 17 | Notifikasi | Memberikan informasi mengenai aktivitas seperti pesanan, penawaran, stok menipis, dan produk yang mendekati kedaluwarsa. *(Notifikasi dalam aplikasi; tanpa email/WhatsApp API.)* |
| 18 | Struk dan nota | Memungkinkan pengguna mencetak struk penjualan dan nota pesanan untuk dokumentasi transaksi. *(Dialog print peramban.)* |
| 19 | Administrasi sistem | Memungkinkan admin mengelola pengguna, memverifikasi usaha, mengelola kategori, memantau pesanan, dan melihat log aktivitas. *(V3: indikator integritas stok.)* |
| 20 | Akun dan pesanan pembeli akhir | Pembeli individu (tanpa BusinessId) mencari warung, melihat harga/ketersediaan/kedaluwarsa, mengonfirmasi dan membuat pesanan (ambil sendiri/antar), melihat status dan riwayat. |
| 21 | Pencatatan hasil peternakan | Memungkinkan usaha peternakan merencanakan produksi, mencatat hasil, dan memperbarui stok. |
| 22 | Reservasi stok dan konkurensi | Stok dicadangkan atomik saat pesanan dibuat; permintaan serentak atas stok terbatas tidak pernah menghasilkan stok minus atau reservasi berlebih. |
| 23 | Idempotensi operasi kritis | Checkout, pembuatan pesanan, pembayaran, penerimaan barang, pembatalan, pembelian, POS, dan pembuangan batch tidak menghasilkan efek ganda saat diulang. |
| 24 | Pembayaran demo | Bayar saat ambil/diterima atau simulasi lokal berlabel jelas; status pembayaran terpisah dari status pesanan; paling banyak satu pembayaran sukses per pesanan. |
| 25 | Promo pembeli untuk stok hampir kedaluwarsa | Warung memberi diskon pada batch yang mendekati kedaluwarsa; terjual lebih dulu (FEFO); tanggal kedaluwarsa ditampilkan. |
| 26 | Dampak food waste | Ringkasan terjual, tersalurkan, terbuang, dan kerugian dari catatan nyata. |
| 27 | Mode lokal tanpa internet dan data persisten | Seluruh alur inti berjalan di localhost tanpa layanan eksternal; restart tidak menghapus data; seeder idempoten; reset hanya lewat konfigurasi eksplisit. |

#### Pemetaan kode FR-01…FR-24 (master prompt V3) ke nomor di atas
FR-01→1,19 · FR-02→1,2,20 · FR-03→2,19,21 · FR-04→3,5,21 · FR-05→5 · FR-06→4,5,21 · FR-07→20 · FR-08→22 · FR-09→23 · FR-10→5,22,23 · FR-11→6,24 ·
FR-12→25 · FR-13→26 · FR-14→25 · FR-15→24 · FR-16→13,21 · FR-17→13 · FR-18→22 · FR-19→14,27 · FR-20→28 · FR-21→9,10 · FR-22→19 · FR-23→29 · FR-24→10,21,28.

#### Keterlacakan kebutuhan fungsional → implementasi → bukti
| No. | Modul V3 utama | Bukti uji (nama kelas/tes) | Status |
|---|---|---|---|
| 1 | Auth, BusinessProfile, Consumers | `AuthServiceTests`, `BusinessServiceTests`, `V3AuthorizationTests.E4`, HTTP §7 | Terpenuhi |
| 2 | BusinessProfile, Consumers | `BusinessServiceTests`, `V3AuthorizationTests.E1` | Terpenuhi |
| 3 | Catalog (+MOQ) | `ProductOwnershipAndImageTests`, `V3SupplyChainFlowTests.D3` | Terpenuhi untuk katalog dan gambar; MOQ belum diuji khusus |
| 4 | Marketplace | `MarketplaceFlowTests` | Terpenuhi |
| 5 | Orders | `MarketplaceFlowTests`, `V3SupplyChainFlowTests.D3`, `IdempotencyAndPaymentTests.A1b` | Terpenuhi |
| 6 | Stock | `StockConcurrencyTests` (B1…B11), `ExpiryServiceTests` dan `ScenarioTests.Stok_tidak_boleh_negatif…` (V2) | Terpenuhi |
| 7 | Purchasing | `FinancialConsistencyTests.C3,C7,C8`, `ScenarioTests.Pembelian_kedua_dengan_harga_baru…` (V2) | Terpenuhi |
| 8 | Sales (POS) | `SalesReceiptTests`, `ScenarioTests.Skenario_beli_2_Box_lalu_jual_5_Item…` (V2), `StockConcurrencyTests.B2,B2b`, `FinancialConsistencyTests.C4` | Terpenuhi |
| 9–10 | Accounting, Reporting | `FinancialConsistencyTests.C4,C6`, tes V2 laporan | Terpenuhi (keuangan); halaman laporan V2 tidak diubah |
| 11 | Delivery | `DeliveryAndMapTests` (V2), HTTP §1–9 | Terpenuhi |
| 12 | Map | `OfflineAssetsTests`, HTTP §9 (halaman peta 200, mode lokal) | Terpenuhi untuk server/HTML; **perilaku JavaScript peta belum diuji di peramban** |
| 13 | Stock/Expiry | `ExpiryServiceTests` (V2), `D7`, `D8`, `D8b`, `B10` | Terpenuhi |
| 14 | Rescue | `RescueFlowTests` (V2) | Terpenuhi (V2, tidak diubah semantiknya) |
| 15 | Insights | `RescueFlowTests.Data_penjualan_hanya_untuk_grosir…`, `…Warung_yang_tidak_membagikan_data…` (V2) | Terpenuhi (V2) |
| 16 | Orders (tracker) | `OrderTrackerTests`, `DeliveryAndMapTests.Pelacakan_…` (V2) | Terpenuhi (estimasi simulasi) |
| 17 | Notifications | `DeliveryAndMapTests.Notifikasi_dihitung_dari_pesanan_penawaran_dan_kadaluarsa_terkini` (V2, lulus); halaman dirender pada HTTP §9 | Terpenuhi (V2); tidak ada notifikasi khusus pembeli di V3 |
| 18 | Sales/Orders (struk, nota) | `SalesReceiptTests` (V2) | Terpenuhi (V2); dialog print peramban **belum diuji** |
| 19 | Admin | `D` (`Admin_melihat_ringkasan…`), HTTP §7/§9 | Terpenuhi |
| 20 | Consumers | `D5`, `D7`, `D8`, `IdempotencyAndPaymentTests`, HTTP §1–4 | Terpenuhi |
| 21 | Chain, Catalog, Stock | Halaman produksi, perencanaan, dan stok | Belum ada skenario otomatis khusus | Belum diuji |
| 22 | Stock reservasi | `StockConcurrencyTests`, HTTP §4 | Terpenuhi |
| 23 | UnitOfWork, entity berkunci | `IdempotencyAndPaymentTests` (11), `StockConcurrencyTests.B5,B6`, `FinancialConsistencyTests.C8` | Terpenuhi |
| 24 | Consumers (Payment) | `IdempotencyAndPaymentTests.A2…A8`, `C6`, HTTP §2 | Terpenuhi |
| 27 | Consumers (promo) | `D7`, `D10`, `C1` | Terpenuhi |
| 28 | Rescue (FoodWaste) | `D10` | Terpenuhi |
| 29 | DbSeeder | `DatabaseLifecycleTests` (4), `bukti-restart.txt`, `bukti-pemulihan-crash.txt`, `OfflineAssetsTests` | Terpenuhi |

### 3.2 Kebutuhan Sistem Non-Fungsional
Tabel mengikuti format materi kuliah; kolom terakhir menambahkan **target terukur** dan status. Rincian per kriteria ada di `docs/V3-KUALITAS-ISO25010.md`.

| No. | Kebutuhan Non-Fungsional | Penjelasan | Kebutuhan pada Aplikasi | Target terukur V3 → hasil → status |
|---|---|---|---|---|
| 1 | Kesesuaian fungsional (Functional Suitability) | Fungsi dan hasil sesuai kebutuhan | Perhitungan total pesanan dan laporan keuangan harus benar | Total/omzet/HPP/laba kotor = hitungan manual pada data uji (`C1`,`C4`, planner 653.000) → 100% cocok → **Terpenuhi** |
| 2 | Efisiensi kinerja (Performance Efficiency) | Waktu respons dan penggunaan sumber daya | Halaman Katalog merespons maksimal 3 detik pada kondisi uji yang ditentukan | Kondisi uji: 300 produk/usaha, SQLite lokal. Layanan maks 34 ms; HTTP maks 111 ms → **Terpenuhi** pada kondisi itu (1 vCPU, Linux) |
| 3 | Kompatibilitas (Compatibility) | Aplikasi dapat digunakan pada berbagai perangkat | Aplikasi bisa digunakan melalui HP dan laptop | Struktur responsif ada (viewport + 36 aturan `@media`, tabel→kartu); **uji di HP/laptop/peramban nyata: belum diuji** |
| 4 | Usabilitas (Usability) | Mudah dipelajari dan digunakan | Dashboard usaha dan pembeli mudah dipahami | Halaman tiap peran tersedia dan terender; antarmuka berbahasa Indonesia; tugas pesan barang dari dashboard pembeli = 4 halaman. **Uji dengan pengguna nyata: belum diuji** |
| 5 | Keandalan (Reliability) | Tetap konsisten saat operasi normal maupun menghadapi kegagalan | Pembayaran tidak tercatat dua kali dan stok tidak minus | 0 pelanggaran invarian pada seluruh tes konkurensi, 20 operasi serentak, dan uji crash `kill -9` → **Terpenuhi** |
| 6 | Keamanan (Security) | Melindungi akses dan data | Warung A tidak bisa mengubah stok atau melihat data privat warung B | Isolasi tenant pada semua tabel bertenant + permintaan dipalsukan ditolak (`E1`,`E2`, HTTP §8) → **Terpenuhi**; uji penetrasi/ZAP: belum diuji |
| 7 | Kemudahan Pemeliharaan (Maintainability) | Mudah diperiksa, diuji, dan diubah | Perubahan modul transaksi tidak merusak POS | POS tidak bergantung pada Consumers (`ArchitectureTests`) → **Terpenuhi**; cakupan baris: belum diukur |
| 8 | Portabilitas (Portability) | Mudah dipasang atau dipindahkan ke lingkungan lain | Proyek dapat dijalankan di komputer lain yang memenuhi persyaratan .NET | Jalan di Ubuntu 24.04 + .NET SDK 10.0.112 dari folder kerja lain, tanpa jalur absolut → **Terpenuhi pada Linux**; Windows/macOS dan `dotnet restore` nyata: belum diuji di lingkungan pengembang saya |

#### Pemetaan NFR-01…NFR-17 (master prompt) ke baris di atas
NFR-01,02→6 · NFR-03,04,05→5 · NFR-06→5,6 · NFR-07→7 · NFR-08,09→8,5 (offline) · NFR-10→4 · NFR-11→2 · NFR-12→5 · NFR-13→1 · NFR-14→1 · NFR-15→7 · NFR-16→6 · NFR-17→5.

## 4. Asumsi dan batasan
1. Pembayaran **seluruhnya simulasi lokal**; tidak ada uang nyata, gateway, atau data kartu.
2. Satu proses aplikasi dan satu file SQLite (penulis tunggal). Penskalaan ke banyak server tidak dirancang (lihat `V3-BATASAN.md`).
3. Waktu pemenuhan, ongkir, dan estimasi rute adalah **estimasi lokal** (jarak garis lurus × faktor jalan), bukan routing nyata/GPS.
4. Laju penjualan = rata-rata 30 hari dari transaksi nyata; bila belum ada riwayat, laju "belum diketahui" dan jumlah minimum dipakai.
5. Data dummy bersifat fiktif.

## 5. Kriteria penerimaan sistem
Dianggap siap demo bila: semua peran menjalankan fiturnya sesuai hak akses; grosir→warung→pembeli berjalan;
pembayaran demo tidak tercatat dua kali; operasi berulang tidak menggandakan efek; permintaan serentak tidak menghasilkan stok minus;
data bertahan setelah restart; semua alur inti jalan offline; seluruh tes wajib dijalankan dengan hasil jujur.
Hasil pemeriksaan terhadap kriteria ini: `docs/V3-PENGUJIAN.md` bagian 6.
