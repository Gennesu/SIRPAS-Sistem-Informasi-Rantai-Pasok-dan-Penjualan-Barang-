# Platform Rantai Pasok Pangan V3 — Grosir · Warung · Pembeli

Aplikasi web lokal untuk marketplace **grosir → warung → pembeli akhir**, **stok dengan reservasi dan batch (FEFO)**, **checkout dan pembayaran demo yang tahan klik ganda**, **kasir (POS)**, **promo anti-mubazir**, serta laporan keuangan dan food waste.
Dibangun di atas V2 (ASP.NET Core MVC + EF Core + MySQL sebagai database utama, modular monolith); fitur V2 tetap berfungsi.

> V3 bukan sekadar jual-beli: fokusnya pada **konsistensi data** (stok tidak minus, pembayaran/penerimaan tidak tercatat dua kali,
> pesanan tidak ganda) dan pada alur transaksi yang konsisten.

## Yang baru di V3
| Area | Isi |
|---|---|
| **Aktor baru** | **Pembeli akhir** (role `CONSUMER`: akun individu **tanpa BusinessId**, katalog warung, keranjang→konfirmasi→pesanan, promo layak konsumsi). Usaha grosir, warung, dan peternakan tetap tersedia. |
| **Ketahanan transaksi** | Unit kerja (satu transaksi) untuk setiap operasi stok/pesanan/pembayaran; **reservasi stok atomik saat pesanan dibuat**; kunci idempotensi + unique index; concurrency token + CHECK database; transisi status dibaca ulang di dalam transaksi. |
| **Pembayaran demo** | Bayar saat ambil / saat diterima / **simulasi lokal** (diberi label jelas, bukan uang nyata). Database menjamin **paling banyak satu pembayaran sukses per pesanan**. |
| **Peta Leaflet + OpenStreetMap** | Peta memakai tile OpenStreetMap (`Map:OnlineTiles=true`, bawaan) di atas kisi skematis lokal, jadi tanpa internet pin dan jarak tetap terbaca. Tidak ada OSRM/Nominatim/CDN/font eksternal; rute, jarak, dan ongkir selalu dihitung lokal. |
| **Database** | MySQL/MariaDB; **restart tidak menghapus data**; seeder idempoten; penjaga skema menolak database dengan skema yang tidak cocok; reset hanya lewat konfigurasi eksplisit. SQLite dan PostgreSQL tetap dapat dipilih sebagai provider alternatif. |
| **Food waste** | Halaman *Dampak food waste*: terjual, tersalurkan (promo/paket), terbuang, kerugian — dari catatan stok dan transaksi nyata. |

## Pembaruan V3.6 — angka terhubung, tanpa angka pajangan
- **Modul Chain (peternakan)** tidak memakai JSON statis (`ChainData.cs` dihapus). Halaman **Stok**, **Produksi**, dan **Ringkasan** dihitung dari buku besar stok (`InventoryMovement`).
- **Alur:** catat hasil produksi/panen (menu *Produksi*) -> jenis pergerakan baru `ProductionIn` -> stok bertambah -> terlihat di Stok, Produksi, dan Ringkasan. Barang rusak/susut dicatat dari form yang sama dan mengurangi stok.
- Halaman yang belum punya sumber data nyata (Kandang/Lahan, Keuangan, Laporan) tampil **kosong dengan penjelasan**, bukan angka contoh.
- Landing tidak lagi menampilkan kartu contoh berangka.

## Pembaruan terbaru (V3.1)
| Area | Perubahan |
|---|---|
| **Peta** | Tile OpenStreetMap tersedia di halaman peta warung dan grosir; warung mencari grosir dan grosir melihat persebaran warung. Daftar, jarak, pencarian, dan filter radius tetap tersedia. |
| **Pesanan pembeli** | Pelacakan *Pesanan saya* kini menyebut **warung** sebagai penerima dan penjual ("Diterima warung"), bukan grosir. Warung dapat membuat promo stok hampir kedaluwarsa untuk pembeli langsung dari halaman Kedaluwarsa. |
| **Tema** | Pembeli memakai tema **biru**; akun usaha dan admin tetap hijau (satu baris di `_Layout.cshtml`). |
| **Sidebar** | Posisi gulir menu tersimpan saat berpindah halaman (tidak lagi lompat ke atas) untuk semua peran. |
| **Gambar produk** | Pemilik masing-masing mengunggah gambar produknya sendiri (JPG/PNG/WebP/GIF, maks. 2 MB, isi berkas diperiksa) di form produk. Gambar tampil di katalog, marketplace, kasir, promo, dan detail pesanan; tanpa gambar tampil kotak berinisial. Data demo memakai ilustrasi bawaan di `wwwroot/img/products`. |


## Persyaratan dan cara menjalankan
1. Pasang **.NET 10 SDK** (https://dotnet.microsoft.com/download). Jika tidak ingin memasangnya secara global, pasang lokal di folder `.dotnet-sdk` lewat PowerShell dari folder project:
   ```powershell
   $installer = Join-Path $env:TEMP 'dotnet-install.ps1'
   Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile $installer
   & $installer -Channel 10.0 -InstallDir (Join-Path (Get-Location) '.dotnet-sdk') -Architecture x64
   ```
   Unduhan SDK dan restore NuGet memerlukan internet; aplikasi berjalan lokal setelah dependensi terpasang.
2. Cara singkat dari folder ini:
   ```bash
   python main.py
   ```
   Peluncur ini memakai pustaka standar Python saja. SDK .NET 10 dapat dipasang di folder `.dotnet-sdk` (tanpa `.venv`); paket NuGet disimpan di `.nuget/packages`.
   Saat pertama dijalankan, `dotnet run` memulihkan paket yang tercantum di `src/Platform.Web/Platform.Web.csproj`.
3. Atau jalankan langsung dengan .NET:
   ```bash
   dotnet run --project src/Platform.Web
   ```
4. Buat database `sirpas` di MySQL 8.0.16+ atau MariaDB (misalnya di phpMyAdmin XAMPP):
   ```sql
   CREATE DATABASE sirpas CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
   ```
   Lalu simpan connection string secara lokal:
   ```powershell
   dotnet user-secrets set "ConnectionStrings:Default" "Server=localhost;Port=3306;Database=sirpas;User ID=root;Password=PASSWORD_MYSQL;" --project src/Platform.Web
   ```
   Pastikan database `sirpas` sudah dibuat dan akun MySQL tersebut punya izin membuat/mengubah tabel. Jangan masukkan password ke Git.
5. Jalankan aplikasi dan buka **http://localhost:5080**. Halaman login punya tombol **masuk-cepat** untuk semua akun demo.

Untuk membuka aplikasi dari HP di jaringan Wi-Fi yang sama, jalankan `python main.py --lan`. Peluncur akan menampilkan alamat `localhost` untuk komputer dan alamat IP Wi-Fi/LAN yang bisa dibuka dari browser HP. Jika beberapa alamat IP ditampilkan, gunakan alamat jaringan Wi-Fi komputer.

Saat pertama dijalankan, aplikasi membuat tabel MySQL dan mengisi data dummy sekali. Menjalankan ulang aplikasi **mempertahankan** semua data dan transaksi. File SQLite lama di `src/Platform.Web/App_Data/platform-demo-v3.db` tidak dihapus, tetapi datanya juga **tidak otomatis disalin** ke MySQL.

### Akun demo (password semua akun: `Demo12345`)
| Peran | Email | Keterangan |
|---|---|---|
| Grosir | `grosir@demo.test` | Grosir Sumber Rejeki; katalog grosir tersedia untuk dipesan warung |
| Warung | `warung@demo.test` | **Warung Maju**: promo Teh Botol −30% dan Kopi −15%, pesanan pembeli masuk |
| Warung | `warung3@demo.test`, `warung5@demo.test` | Toko Sejahtera dan Warung Pak Dedi (akun demo tambahan) |
| Pembeli akhir | `pembeli@demo.test`, `pembeli2@demo.test` | Budi Pembeli dan Siti Konsumen (tanpa usaha) |
| Admin | `admin@demo.test` | Verifikasi usaha, pesanan, log, ringkasan platform + indikator integritas stok |
| (V2) | `grosir2@`, `warung2@`, `warung4@`, `warung6@demo.test` | Skenario V2 tetap ada |

Skrip demonstrasi lengkap (pemilihan jalur, pembayaran simulasi dan pencegahan ganda, dua pesanan berebut stok): **`docs/V3-DEMO.md`**.

## Konfigurasi
| Kunci | Bawaan | Arti |
|---|---|---|
| `ConnectionStrings:Default` | Kosong di `appsettings.json` | Isi dengan connection string MySQL lewat user-secrets atau `ConnectionStrings__Default`; jangan simpan kredensial di Git |
| `Database:Provider` | `MySql` | Provider EF Core 10 untuk MySQL dan MariaDB; MariaDB 10.4 (bawaan sejumlah versi XAMPP) belum tercantum dalam versi yang diuji provider. `Sqlite` dan `Postgres` tersedia sebagai alternatif |
| `Database:Mode` | `EnsureCreated` | Skema dibuat saat pertama; `EnsureCreated` tidak memperbarui skema lama, karena itu ada penjaga skema (lihat `docs/V3-ARSITEKTUR.md`) |
| `Demo:SeedDummyData` | `true` | Isi data demo sekali (idempoten) |
| `Demo:ResetDatabaseOnStart` | **`false`** | **PERINGATAN**: `true` menghapus SELURUH data pada database aktif saat start. Aktifkan hanya sadar, matikan lagi setelahnya. Bisa lewat variabel lingkungan `Demo__ResetDatabaseOnStart=true` |
| `Map:OnlineTiles` | `true` | Memuat tile OpenStreetMap (hanya tampilan; kisi lokal jadi cadangan bila internet putus). `false` = selalu kisi lokal tanpa permintaan internet. Rute, jarak, ongkir selalu dihitung lokal |
| `Demo:ShowDeveloperErrors` | `false` | Halaman galat pengembang (stack trace) hanya bila `true` di Development; bawaan pengguna tidak melihat detail internal |

## Pengujian
```bash
dotnet test                       # suite xUnit, SQLite file/in-memory, tanpa layanan eksternal
python3 tests/http/smoke_v3.py    # pemeriksaan HTTP nyata (aplikasi harus berjalan di http://127.0.0.1:5088; lihat docstring)
python3 tests/http/crash_recovery.py --load <PID> ...   # uji pemulihan crash (SIGKILL di tengah beban), lalu --verify
```
Hasil aktual, lingkungan uji, dan kegagalan yang ditemukan: **`docs/V3-PENGUJIAN.md`** dan folder `docs/bukti/`. Berkas di `docs/bukti/` adalah snapshot historis dari versi sebelumnya, bukan gambaran fitur pada kode saat ini.
**Catatan jujur:** hasil pada dokumen dihasilkan di Ubuntu 24.04 dengan .NET SDK 10.0.112 memakai runner uji mini karena paket NuGet
(xunit) tidak dapat diunduh di lingkungan itu. Kode tes sama persis dengan yang ada di `tests/Platform.Tests`; jalankan `dotnet test`
di komputer Anda dan catat hasilnya sendiri sebelum mengklaimnya pada laporan.

## Dokumentasi
| Dokumen | Isi |
|---|---|
| `docs/V3-SKPL.md` | Analisis kebutuhan (format kuliah): §3.1 kebutuhan fungsional, §3.2 non-fungsional, aktor, asumsi, kriteria penerimaan, keterlacakan ke tes |
| `docs/V3-KUALITAS-ISO25010.md` | Evaluasi 8 karakteristik ISO/IEC 25010: persyaratan terukur, metode, hasil aktual, status |
| `docs/V3-ARSITEKTUR.md` | Modular monolith, ketergantungan modul, batas transaksi, concurrency, reservasi, idempotensi |
| `docs/V3-ERD.md`, `docs/V3-USE-CASE.md`, `docs/V3-SEQUENCE.md` | ERD, aktor dan matriks akses, diagram urutan (checkout, reservasi, pembayaran demo, penerimaan) |
| `docs/V3-PENGUJIAN.md` | Kasus uji, hasil aktual, lingkungan, temuan V2 (sebelum) vs V3 (sesudah), cacat tersisa |
| `docs/V3-BATASAN.md` | Yang masih simulasi dan belum layak produksi |
| `docs/V3-DEMO.md` | Langkah presentasi |
| `docs/FASE-*.md` | Dokumen V2 (tetap berlaku untuk fitur V2) |

## Struktur
```
src/Platform.Web/
  Modules/<Modul>/{Contracts,Domain,Data,Services,Controllers,ViewModels,Views}
    V2: Auth, BusinessProfile, Template, Catalog, Stock, Purchasing, Sales, Accounting, Reporting, Delivery, Marketplace, Orders,
        Map, Rescue, Insights, Notifications, Admin, Audit
    V3 baru: Consumers (pembeli, katalog, pesanan, pembayaran demo, promo),
             Stock (+reservasi, integritas), Rescue (+dampak food waste)
  SharedKernel/   UnitOfWork, IVersioned, konteks pengguna, Result, WIB, Rupiah
  Data/           AppDbContext, DbSeeder (penjaga skema), seeder demo V2+V3, EfUnitOfWork
tests/Platform.Tests/   unit + integrasi + konkurensi (SQLite file)   tests/http/   skrip uji HTTP   tests/baseline-v2/   probe cacat V2
docs/             dokumen V3 + V2;  docs/bukti/   keluaran uji aktual
```
Antarmodul hanya berkomunikasi lewat `Contracts`; batas ini diperiksa otomatis (`ArchitectureTests`).
Lisensi aset (Leaflet, font, ikon): `src/Platform.Web/wwwroot/lib/LICENSES.md`.
