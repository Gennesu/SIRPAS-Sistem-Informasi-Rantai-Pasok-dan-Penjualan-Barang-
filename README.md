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
- **Database:** SQLite digunakan otomatis untuk pengembangan lokal. MySQL/MariaDB menjadi pilihan untuk hosting; PostgreSQL juga tersedia.
- **Peta:** Leaflet dan OpenStreetMap. Tile peta memerlukan internet; tampilan kisi lokal menjadi cadangan.
- **Arsitektur:** modular monolith; modul berkomunikasi melalui kontrak bersama.

## Menjalankan di komputer lokal

### Persyaratan

- .NET 10 SDK.
- Python 3 hanya diperlukan jika ingin memakai peluncur `main.py`.

### Jalankan aplikasi

Dari folder utama repository:

```powershell
python main.py
atau
dotnet run --project src/Platform.Web
```

`main.py` menjalankan aplikasi di **http://localhost:5080**. Jika port itu sedang dipakai, peluncur memilih port kosong berikutnya dan menampilkan alamat yang benar. Pada pemakaian pertama, aplikasi membuat skema database dan mengisi data demo. Untuk membuka aplikasi dari perangkat lain di Wi-Fi yang sama, jalankan `python main.py --lan`. Jika ingin menjalankan tanpa Python, `dotnet run --project src/Platform.Web` juga memakai SQLite melalui profil Development.

Lingkungan Development otomatis memakai SQLite pada `src/Platform.Web/platform-demo-v3.db`; Anda tidak perlu memasang atau menyalakan server database. Data demo diaktifkan secara bawaan dan data lokal tetap ada saat aplikasi dijalankan ulang. Database SQLite lokal terpisah dari database hosting; data lama tidak otomatis dipindahkan.

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
