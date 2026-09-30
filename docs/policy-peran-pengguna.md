# Kebijakan Pengguna dan Peran — SuDesApp

> Status dokumen: **berlaku sebagai default implementasi** (sesuai kode saat
> ini). Akun disimpan di tabel `Pengguna` (SQLite), kata sandi di-hash
> PBKDF2-SHA256, dan setiap perubahan akun tercatat di Riwayat Aktivitas.

---

## 1. Ringkasan

| Topik | Kebijakan | Status |
|---|---|---|
| Penyimpanan akun | Tabel `Pengguna` di database desa | Aktif |
| Hash kata sandi | PBKDF2-SHA256, 120.000 putaran, salt 16 byte per akun | Aktif |
| Minimal panjang kata sandi | 8 karakter untuk akun baru, reset, dan ubah sendiri | Aktif |
| Kunci percobaan gagal | 3 kali gagal → akun terkunci 5 menit (tersimpan di database) | Aktif |
| Peran | Administrator, Sekdes, Operator, Kades, Auditor | Aktif |
| Akun lama (`Login.dat`) | Dimigrasikan otomatis ke database saat login pertama | Aktif |
| Login Google | Sesi Administrator penuh (tidak memakai kata sandi aplikasi) | Aktif |
| Pemeriksaan izin | Ditegakkan di menu + halaman + lapis data: `SessionContext.Wajib` di layanan simpan surat, impor warga, verifikasi/tanda tangan, dan kelola pengguna (aktif setelah login; jalur sistem — API desa, proses WhatsApp — membuka blok lewat `SesiSistem()` per-aliran) | Aktif |
| Sesi kedaluwarsa otomatis | Layar kunci idle: setelah diam (bawaan 15 menit, bisa diatur 1–120 menit di Pengaturan Aplikasi), jendela ditutup layar kunci; dibuka lewat isi ulang kata sandi akun aktif (akun Google: verifikasi akun Google tersambung). Percobaan salah memakai aturan kunci 3× gagal yang sama | Aktif |
| Enkripsi database | SQLCipher (bundle e_sqlcipher); kunci acak 256 bit disimpan DPAPI CurrentUser (`%LOCALAPPDATA%\SuDesApp\DesaKunciDb.bin`). Saat start: database plaintext lama dicadangkan ke `desa-plaintext-<tanggal>.bak` lalu dikonversi otomatis (sqlcipher_export); database baru terenkripsi sejak byte pertama. Koneksi pengujian in-memory sengaja tanpa kunci | Aktif |

---

## 2. Peran dan Izin

Izin diturunkan dari peran lewat satu peta di kode
(`Data/Models/PenggunaData.cs` → `HakAkses`). Menu yang tidak diizinkan tidak
ditampilkan sama sekali di sidebar.

| Izin | Administrator | Sekdes | Operator | Kades | Auditor |
|---|:--:|:--:|:--:|:--:|:--:|
| Buat/ubah surat (`BuatSurat`) | ✔ | ✔ | ✔ | – | – |
| Tanda tangan/verifikasi surat (`TandaTanganSurat`) | ✔ | ✔ | – | ✔ | – |
| Kelola data warga (`KelolaWarga`) | ✔ | ✔ | ✔ | – | – |
| Kelola perangkat desa (`KelolaPerangkat`) | ✔ | ✔ | ✔ | – | – |
| Laporan (`Laporan`) | ✔ | ✔ | ✔ | ✔ | ✔ |
| Layanan online (`LayananOnline`) | ✔ | ✔ | ✔ | – | – |
| Verifikasi keaslian surat (`VerifikasiSurat`) | ✔ | ✔ | ✔ | ✔ | ✔ |
| Riwayat aktivitas (`RiwayatAktivitas`) | ✔ | ✔ | ✔ | ✔ | ✔ |
| Pengaturan aplikasi (`PengaturanAplikasi`) | ✔ | ✔ | – | – | – |
| Cadangan/impor database (`Cadangan`) | ✔ | ✔ | – | – | – |
| Kelola pengguna (`KelolaPengguna`) | ✔ | – | – | – | – |

Catatan:

- **Administrator** adalah peran pengelola. Akun Administrator aktif terakhir
  tidak bisa diturunkan perannya, dinonaktifkan, atau dihapus — supaya desa
  tidak terkunci dari pengelolaan aplikasinya sendiri.
- **Kades** dan **Auditor** tidak bisa membuat surat. Keduanya tetap bisa
  memeriksa register, laporan, verifikasi keaslian, dan riwayat aktivitas.
- **Kelola Pengguna** hanya untuk Administrator. Halaman yang sama memuat tombol
  **Ubah Kata Sandi Saya** di header yang selalu tersedia untuk pemilik akun, jadi
  setiap peran tetap bisa mengganti kata sandinya sendiri tanpa menu terpisah.
  Halaman itu juga memuat kartu **Akun Saya** (nama, peran, dan status akun yang
  sedang dipakai) yang tampil untuk semua peran — termasuk yang tidak berhak
  mengelola akun — karena itu satu-satunya tempat pemakai melihat identitasnya
  sendiri secara utuh, bukan sekadar potongan namanya di bilah status.

---

## 3. Perjalanan Akun Lama (Login.dat)

Aplikasi versi lama menyimpan satu akun di berkas `Login.dat` (DPAPI,
`username:salt:hash`). Aturan peralihannya:

1. Bila tabel `Pengguna` masih kosong, login memakai berkas lama seperti biasa.
2. Setelah login lama berhasil, akun itu **langsung dibuat di database** sebagai
   Administrator dengan kata sandi yang baru saja diketik (migrasi ini boleh
   memakai kata sandi lebih pendek dari 8 karakter supaya pemilik desa tidak
   terkunci dari aplikasinya sendiri).
3. Login berikutnya memakai database: peran, penguncian, dan Riwayat Aktivitas
   berlaku penuh. Kata sandi tetap seperti yang diketik saat migrasi
   (perhatikan huruf besar/kecil).
4. Bila migrasi gagal (mis. nama akun tidak memenuhi aturan), login lama tetap
   dipakai dan kegagalan dicatat di log aplikasi.

Berkas `Login.dat` **tidak dihapus** oleh migrasi; ia hanya berhenti dipakai
untuk verifikasi setelah database berisi akun.

---

## 4. Yang Belum Ditegakkan di Lapis Data

Pemeriksaan izin saat ini ada di lapis tampilan (menu dan halaman). Artinya,
kode internal Core (mis. layanan WhatsApp otomatis yang berjalan di latar)
tetap dapat menulis surat tanpa memeriksa peran — memang disengaja, karena
kanal otomatis bukan pengguna. Bila nanti aplikasi dibuka lewat jaringan
(API/layanan daring), pemeriksaan izin perlu dinaikkan ke lapis layanan;
kebutuhan itu dicatat sebagai **[TENTUKAN]** di dokumen `api-desa.md`.
