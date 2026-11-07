# Fase 1 — Authentication, User, Business, Role

> **Status awal:** ditulis tanpa .NET SDK. Sudah dikompilasi dan diuji pada tahap demo (lihat catatan di bawah).

> **Pembaruan (tahap demo):** Fase 1 kini ikut dikompilasi dan diuji (lihat `FASE-2-DEMO.md`, bagian Verifikasi).
> Cara menjalankan terbaru ada di `README.md`: mode demo memakai SQLite + `EnsureCreated` + data dummy, tanpa migration dan user-secrets.
> Langkah migration/PostgreSQL pada dokumen ini hanya relevan bila `Database:Mode = Migrate`.

## 1. Tujuan
Fondasi sistem: registrasi usaha (warung/grosir), login berbasis cookie, RBAC 3 role, profil usaha, audit log, dan isolasi data per `BusinessId`.

## 2. Modul yang dikerjakan
| Modul | Isi |
|---|---|
| Auth | `IAuthService`, `ISignInService`, login/logout, hash password, RBAC |
| BusinessProfile | Registrasi atomik (Business + akun pemilik), profil usaha, `BusinessType` |
| Audit (hook) | `IAuditLogService.Record(...)`, tabel `AuditLogs` |
| SharedKernel | `IBusinessContext`, `IBusinessOwned`, `Result`, tenant query filter di `AppDbContext` |

## 3. Dependency
`BusinessProfile → Auth → SharedKernel`; `Auth`, `BusinessProfile` → `Audit`. `AppUser` tidak punya referensi ke `Business` (FK dikonfigurasi dari sisi BusinessProfile).

## 4. Perubahan database
Migration awal **`InitialFoundation`** (dibuat dengan `dotnet ef migrations add`, lihat README).

| Tabel | Catatan |
|---|---|
| `Roles` | Seed: ADMIN, GROSSIER, RETAIL. Unik `Name` |
| `Users` | Unik `Email` (huruf kecil). FK `RoleId`, FK `BusinessId` (nullable; null = ADMIN) |
| `BusinessTypes` | Seed: GROCERY, CLOTHING, ELECTRONICS, OTHER. Unik `Code` |
| `Businesses` | `MarketRole` & `VerificationStatus` disimpan sebagai teks. FK `BusinessTypeId` |
| `AuditLogs` | Tanpa FK (catatan tetap ada walau data rujukan berubah). Indeks (BusinessId, CreatedAt), UserId |

Alasan: fondasi identitas + tenant harus ada sebelum modul bisnis mana pun. Tabel terdampak: semua baru (belum ada data lama).

## 5. Endpoint / UI
| Route | Akses | Fungsi |
|---|---|---|
| `GET/POST /register` | anonim | Registrasi usaha + akun pemilik |
| `GET/POST /Account/Login`, `POST /Account/Logout` | anonim / login | Masuk / keluar |
| `GET /business/profile/{id?}` | login | Lihat profil (pemilik atau admin; selain itu 404) |
| `POST /business/profile/{id}` | pemilik | Ubah profil |
| `GET /Home/*Dashboard` | per role | Placeholder dashboard |
| `GET /status/{code}` | anonim | Halaman 403/404/500 tanpa detail teknis |

## 6. Test (belum dijalankan)
23 metode / 25 kasus, SQLite in-memory:
- `AuthServiceTests` (9 metode): hash password, email duplikat, validasi password/email, aturan admin vs usaha, login sukses + audit, pesan gagal login seragam, akun nonaktif.
- `BusinessServiceTests` (11): registrasi warung/grosir, **rollback** saat email duplikat, validasi jenis bisnis/telepon/peran, baca/ubah profil, **isolasi tenant** (usaha lain → NotFound), admin boleh baca.
- `TenantFilterTests` (3): warung hanya melihat datanya, admin melihat semua, anonim tidak melihat apa pun.

## 7. Acceptance criteria Fase 1
**Auth**
- [ ] Register dan login berfungsi (uji manual 1–3 di README)
- [ ] Password tersimpan sebagai hash (`CreateUser_menyimpan_hash_bukan_password_asli`)
- [ ] Halaman dibatasi per role (uji manual 5)
- [ ] Isolasi tenant teruji (`GetProfile_usaha_lain_dijawab_NotFound`, `TenantFilterTests`)

**BusinessProfile**
- [ ] Registrasi membuat Business + akun dalam satu transaksi (`Register_email_duplikat_membatalkan_pembuatan_usaha`)
- [ ] Registrasi publik tidak bisa menghasilkan ADMIN
- [ ] Profil hanya bisa diubah pemilik (`UpdateProfile_usaha_lain_ditolak_dan_data_tidak_berubah`)

**Audit**
- [ ] Login, registrasi, dan perubahan profil tercatat di `AuditLogs`

Centang hanya setelah `dotnet test` hijau dan uji manual lulus.

## 8. Asumsi baru (tambahkan ke bagian 0 dokumen analisis)
| ID | Asumsi |
|---|---|
| A-13 | Koordinat usaha opsional, input manual; pemilihan lewat peta di Fase 7 |
| A-14 | Registrasi publik hanya RETAIL/GROSSIER; ADMIN hanya dari seed (user-secrets) |
| A-15 | Password 8–100 karakter. Belum ada lockout dan reset password |
| A-16 | Cookie login 8 jam (sliding), tidak dicek ulang ke DB tiap request: user yang dinonaktifkan tetap bisa masuk sampai cookie habis. Validasi ulang ditambahkan di Fase 8 |
| A-17 | Angka dari form memakai culture invariant (titik desimal) |

## 9. Dokumen yang harus diperbarui
| Bagian dokumen analisis | Perubahan |
|---|---|
| 0 Asumsi | Tambah A-13 s.d. A-17 |
| 7 Arsitektur | Struktur modul menambah `Data/` (konfigurasi EF) dan `ViewModels/`; modul bernama `BusinessProfile` |
| 9–10 Entity | `Business.Status` dipecah menjadi `VerificationStatus` + `IsActive`; `BusinessType` tabel referensi seed; `AuditLog` tanpa FK |
| 24 Testing | Test Fase 1 memakai SQLite in-memory; integration test PostgreSQL mulai Fase 3 |
| 25 Acceptance | Gunakan checklist bagian 7 di atas |

## 10. Dampak ke modul berikutnya
- **Fase 2 (Katalog):** entity pertama yang `IBusinessOwned` (Product, ProductUnit) otomatis terkena tenant filter; ambil `BusinessId` dari `IBusinessContext`, jangan dari input. Template menambah relasi `BusinessType → BusinessTemplate`.
- **Fase 6 (Marketplace):** tampilkan hanya `Verified` + `IsActive`; `Business` memang tidak dikenai tenant filter.
- **Fase 7 (Map):** mengisi `Latitude`/`Longitude` lewat Leaflet; kolom sudah tersedia.
- **Fase 8 (Admin):** memakai `VerificationStatus`/`IsActive`; tambahkan validasi ulang cookie (A-16).
- **Pelaporan (Fase 5):** waktu disimpan UTC; konversi ke Asia/Jakarta saat menampilkan "Penjualan Hari Ini".
