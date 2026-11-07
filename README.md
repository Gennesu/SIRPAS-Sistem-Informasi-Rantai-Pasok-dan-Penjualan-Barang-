# SIRPAS — Sistem Informasi Rantai Pasok dan Penjualan Barang

> Proyek Kompetisi Mahasiswa Muhammadiyah (KMM) AST-PTMA.

SIRPAS adalah aplikasi web untuk membantu distribusi produk dari **peternak ke agen, agen ke warung, lalu warung ke pembeli**. Aplikasi menyediakan pengelolaan produk dan stok, pemesanan, pencatatan penjualan, serta pemantauan status pesanan dalam satu sistem.

## Aktor dan alur utama

| Aktor | Fitur utama |
|---|---|
| Peternak | Mengelola produk dan stok, mencatat produksi, menjual produk kepada agen, dan memantau penerimaan pesanan. |
| Agen | Mengelola katalog dan stok, memesan atau menerima produk dari peternak, lalu memasok warung. Di kode, peran agen menggunakan nama internal `GROSSIER`/grosir. |
| Warung | Membeli dari agen, mengelola stok dan kedaluwarsa, mencatat penjualan melalui kasir, serta menerima pesanan pembeli. |
| Pembeli | Melihat produk dan promo warung, membuat pesanan, memilih ambil sendiri atau pengantaran, dan memantau status pesanan. |
| Admin | Memverifikasi usaha, mengelola pengguna dan kategori, serta memantau aktivitas platform. |

## Fitur

- Registrasi, login, profil usaha, dan pembatasan akses berdasarkan peran.
- Katalog produk, kategori, harga, satuan/kemasan, stok, dan gambar produk.
- Pencarian usaha dan produk, peta lokasi, serta alur pemesanan antaraktor.
- Pencatatan stok dan produksi peternakan, termasuk penjualan peternak ke agen.
- Kasir warung, pencatatan transaksi, laporan usaha, dan pemantauan kedaluwarsa.
- Promo warung untuk pembeli dan fitur pengurangan potensi barang terbuang.
- Notifikasi di dalam aplikasi. Sistem tidak mengirim notifikasi melalui email atau WhatsApp.
- Pembayaran pada aplikasi pembeli adalah simulasi/demo, bukan pemrosesan uang sungguhan.

## Teknologi

- **Backend dan web:** C#, ASP.NET Core MVC, Razor, .NET 10.
- **Akses data:** Entity Framework Core 10.
- **Database utama:** MySQL atau MariaDB. SQLite dan PostgreSQL juga tersedia sebagai provider alternatif.
- **Peta:** Leaflet dan OpenStreetMap. Tile peta memerlukan internet; tampilan kisi lokal menjadi cadangan.
- **Arsitektur:** modular monolith; modul berkomunikasi melalui kontrak bersama.

## Menjalankan di komputer lokal

### Persyaratan

- .NET 10 SDK.
- MySQL atau MariaDB aktif dan dapat diakses aplikasi.
- Python 3 hanya diperlukan jika ingin memakai peluncur `main.py`.

### 1. Buat database

Buat database kosong bernama `sirpas` melalui phpMyAdmin atau klien MySQL:

```sql
CREATE DATABASE sirpas CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
```

Atur connection string di **User Secrets** agar kata sandi tidak tersimpan di Git. Contoh berikut memakai XAMPP dengan port MariaDB **3307**:

```powershell
dotnet user-secrets set "ConnectionStrings:Default" 'Server=127.0.0.1;Port=3307;Database=sirpas;User ID=root;Password=;' --project src/Platform.Web
```

Sesuaikan port, nama pengguna, dan kata sandi jika konfigurasi MySQL/MariaDB Anda berbeda. Jika menggunakan port standar, biasanya port-nya `3306`. MariaDB 10.4 yang disertakan sejumlah versi XAMPP belum tercantum sebagai versi yang diuji oleh provider database proyek ini; gunakan versi MySQL/MariaDB yang kompatibel jika koneksi atau pembuatan skema gagal.

### 2. Jalankan aplikasi

Dari folder utama repository:

```powershell
dotnet run --project src/Platform.Web
```

Buka **http://localhost:5080**. Pada pemakaian pertama, aplikasi membuat skema database dan mengisi data demo. Alternatifnya, jalankan `python main.py`; untuk membuka aplikasi dari perangkat lain di Wi-Fi yang sama, jalankan `python main.py --lan`.

Data demo diaktifkan secara bawaan. Aplikasi mempertahankan data ketika dijalankan ulang. Data SQLite lama, jika ada, tidak otomatis dipindahkan ke MySQL/MariaDB.

## Akun demo utama

Semua akun demo menggunakan kata sandi **`Demo12345`** selama data demo aktif.

| Peran | Email | Nama/tujuan |
|---|---|---|
| Admin | `admin@demo.test` | Administrasi platform |
| Peternak | `ternak@demo.test` | Ayam Petelur Cisarua |
| Peternak | `ternak2@demo.test` | Kandang Sejahtera Cicalengka |
| Peternak | `ternak3@demo.test` | Peternakan Ciparay |
| Agen | `grosir@demo.test` | Grosir Sumber Rejeki |
| Agen | `grosir2@demo.test` | Grosir Makmur Jaya |
| Warung | `warung@demo.test` | Warung Maju |
| Warung | `warung2@demo.test` | Warung Berkah |
| Warung | `warung3@demo.test` | Toko Sejahtera |
| Warung | `warung4@demo.test` | Kedai Bu Rina |
| Warung | `warung5@demo.test` | Warung Pak Dedi |
| Warung | `warung6@demo.test` | Toko Ujung Berung |
| Pembeli | `pembeli@demo.test` | Budi Pembeli |
| Pembeli | `pembeli2@demo.test` | Siti Konsumen |

> Akun dan kata sandi demo hanya untuk demonstrasi lokal. Ganti kredensial sebelum aplikasi dipakai di lingkungan publik.

## Konfigurasi penting

| Pengaturan | Nilai bawaan | Keterangan |
|---|---|---|
| `ConnectionStrings:Default` | Kosong | Connection string database. Gunakan User Secrets saat lokal atau `ConnectionStrings__Default` sebagai variabel lingkungan di hosting. |
| `Database:Provider` | `MySql` | Pilihan provider: `MySql`, `Sqlite`, atau `Postgres`. |
| `Database:Mode` | `EnsureCreated` | Membuat skema bila database masih kosong. `EnsureCreated` bukan mekanisme migrasi untuk memperbarui skema lama. |
| `Demo:SeedDummyData` | `true` | Mengisi data demo saat database baru disiapkan. |
| `Demo:ResetDatabaseOnStart` | `false` | Jika diaktifkan, seluruh database aktif dihapus saat aplikasi mulai. Jangan aktifkan pada database yang berisi data penting. |
| `Map:OnlineTiles` | `true` | Mengaktifkan tile OpenStreetMap; `false` memakai tampilan peta lokal. |
| `Seed:AdminEmail`, `Seed:AdminPassword` | Kosong | Kredensial untuk membuat akun admin awal. Atur secara rahasia bila menyiapkan lingkungan tanpa akun demo. |

## Publish dan hosting

Hosting harus mendukung **ASP.NET Core/.NET 10** dan menyediakan database MySQL/MariaDB yang dapat diakses dari aplikasi web. Connection string lokal XAMPP tidak dapat dipakai oleh server hosting; gunakan host, port, database, dan kredensial yang diberikan penyedia hosting.

Buat paket publish:

```powershell
dotnet publish src/Platform.Web/Platform.Web.csproj -c Release -o .\artifacts\publish
```

Unggah **isi** folder `artifacts/publish` ke direktori aplikasi sesuai petunjuk hosting. Atur variabel lingkungan `ConnectionStrings__Default` di panel hosting. Untuk lingkungan publik, nonaktifkan data demo dengan `Demo__SeedDummyData=false` dan siapkan admin melalui `Seed__AdminEmail` serta `Seed__AdminPassword`. Pastikan `Demo__ResetDatabaseOnStart` tidak diaktifkan. Database produksi harus dibuat dan dicadangkan melalui layanan database hosting.

## Pengujian

Jalankan tes otomatis dengan:

```powershell
dotnet test
```

Skrip pemeriksaan HTTP dan pemulihan crash tersedia di `tests/http/`. Beberapa skrip memerlukan aplikasi yang berjalan dan opsi/alamat tertentu; lihat komentar di dalam skrip sebelum menjalankannya.

## Struktur repository

```text
src/Platform.Web/       Aplikasi ASP.NET Core MVC
  Modules/              Modul auth, usaha, katalog, stok, pesanan, penjualan, peternakan, dan lainnya
  Data/                 DbContext dan seeder database/data demo
  wwwroot/              CSS, JavaScript, dan aset statis
tests/                  Tes otomatis dan skrip pemeriksaan HTTP
docs/                   SKPL, dokumentasi tahap, dan bukti pengujian
main.py                 Peluncur lokal opsional
```

## Dokumentasi

- [SKPL dan kebutuhan sistem](docs/V3-SKPL.md)
- [Dokumentasi Tahap 1](docs/FASE-1.md)
- [Demo Tahap 2](docs/FASE-2-DEMO.md)
- [Marketplace Tahap 3](docs/FASE-3-MARKETPLACE.md)
- [Food waste Tahap 4](docs/FASE-4-FOOD-WASTE.md)
- [Bukti pengujian](docs/bukti/)
