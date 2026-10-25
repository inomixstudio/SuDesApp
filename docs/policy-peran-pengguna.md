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
| Akun pusat penyedia (`adminpusat`) | **Tidak pernah** masuk tabel `Pengguna` → tidak muncul di Kelola Pengguna. Kata sandi **tidak ditanam di kode maupun `Login.dat`**: diprovisikan per mesin lewat `SUDES_PUSAT_SANDI` atau berkas `Pusat.kunci`. Kunci menu rahasia **Pusat Administrasi**: menu itu tidak ditampilkan untuk sesi akun lain | Aktif |
| Login Google | **Dihapus dari layar login (2026-10-13)** — akun Google bukan jalan masuk aplikasi. Koneksi akun Google kini hanya untuk pencadangan ke Google Drive dan disambungkan SETELAH masuk, dari halaman "Login dengan Google" | Dihapus |
| Pemeriksaan izin | Ditegakkan di halaman + lapis data (bukan lewat penyembunyian menu): `SessionContext.Wajib` di layanan simpan surat, impor warga, verifikasi/tanda tangan, dan kelola pengguna (aktif setelah login; jalur sistem — API desa, pekerja latar — membuka blok lewat `SesiSistem()` per-aliran) | Aktif |
| Sesi kedaluwarsa otomatis | Layar kunci idle: setelah diam (bawaan 15 menit, bisa diatur 1–120 menit di Pengaturan Aplikasi), jendela ditutup layar kunci; dibuka lewat isi ulang kata sandi akun aktif. Percobaan salah memakai aturan kunci 3× gagal yang sama | Aktif |
| Enkripsi database | SQLCipher (bundle e_sqlcipher); kunci acak 256 bit disimpan DPAPI CurrentUser (`%LOCALAPPDATA%\SuDesApp\DesaKunciDb.bin`). Saat start: database plaintext lama dicadangkan ke `desa-plaintext-<tanggal>.bak` lalu dikonversi otomatis (sqlcipher_export); database baru terenkripsi sejak byte pertama. Koneksi pengujian in-memory sengaja tanpa kunci | Aktif |

---

## 2. Peran dan Izin

Izin diturunkan dari peran lewat satu peta di kode
(`Data/Models/PenggunaData.cs` → `HakAkses`). Perlu ditulis apa adanya, karena
kalimat lama di dokumen ini mengklaim menu yang tidak diizinkan “tidak
ditampilkan sama sekali di sidebar” — padahal tidak ada penapisan peran di
sidebar: **semua menu tampil untuk semua peran**. Yang menegakkan izin adalah
halaman dan lapis data saat aksinya benar-benar dijalankan (`SessionContext.Wajib`
pada layanan simpan surat, impor warga, verifikasi/tanda tangan, dan kelola
pengguna). Konsekuensinya: menu yang kelihatannya terbuka TIDAK berarti izin,
dan satu-satunya menu yang tidak dibangun sama sekali untuk peran tertentu
adalah **Pusat Administrasi** — hanya untuk sesi akun pusat, dan tindakan
membukanya memeriksa ulang syarat itu.

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
- **Pusat Administrasi** sengaja TIDAK berbasis peran: peran menentukan menu
  desa, sedangkan menu ini kuncinya adalah akun pusat di `Login.dat`
  (bagian 3.2) — isinya pengelolaan langganan seluruh desa, bukan urusan
  pemerintahan desa. Karena itu ia tidak muncul di tabel izin di atas.

---

## 3. Berkas Akun Lokal (Login.dat)

Berkas `Login.dat` menyimpan akun yang sengaja hidup **di luar database desa**
— akun lama, dan akun pusat penyedia. Isinya dienkripsi **DPAPI
(`DataProtectionScope.CurrentUser`)**: berkas yang tersalin ke komputer atau
akun Windows lain tidak bisa dibaca (kata sandi di dalamnya sendiri disimpan
sebagai SHA256 bergaram, bukan teks biasa — tetapi bukan PBKDF2 seperti tabel
`Pengguna`). Pemilik bentuk berkas dan enkripsinya: `BerkasLogin`
(`SuDesApp.Wpf/Utilities/BerkasLogin.cs`).

Setelah didekripsi, isinya satu akun per baris:

```
username:garam:hash
username:garam:hash:pusat
```

Baris tanpa bidang keempat adalah akun biasa; bidang keempat `pusat` menandai
akun pusat penyedia. Berkas versi lama yang hanya memuat satu baris
`username:garam:hash` **tetap terbaca** tanpa perlu diubah.

### 3.1 Akun desa lama (perilaku tidak berubah)

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

### 3.2 Akun pusat penyedia (menu rahasia)

Akun pusat (`adminpusat`, lihat `AkunPusat`) berbeda dalam tiga hal dari akun
desa:

1. **Tidak masuk tabel `Pengguna`, tidak pernah.** Login-nya sengaja TIDAK
   lewat jalur migrasi: kata sandi provisioning diverifikasi langsung lalu sesi
   dibuka. Akibatnya ia tidak muncul di halaman Kelola Pengguna, tidak bisa
   diubah/dihapus dari aplikasi desa, dan tidak ikut tercadang bersama data desa.
2. **Tidak ada kata sandi bawaan.** Sandi akun pusat diprovisikan di mesin yang
   membutuhkannya lewat environment variable `SUDES_PUSAT_SANDI` atau berkas
   `Pusat.kunci` di samping executable. Tanpa provisioning, akun pusat tidak
   bisa dipakai (gagal-tertutup). Sandi tidak pernah ditulis ke kode sumber,
   `Login.dat`, maupun log; akun pusat lama yang masih memakai sandi bawaan
   terdahulu dihapus otomatis saat aplikasi dibuka.
3. **Satu-satunya pembuka menu Pusat Administrasi.** Menu itu tidak ditambahkan
   ke sidebar untuk sesi lain — dan bukan hanya disamarkan: tindakan membukanya
   juga menolak sesi non-pusat. Penentunya bukan nama pengguna semata, melainkan
   penanda sesi (`SessionContext.SesiPusat`) yang hanya dinyalakan jalur login
   akun pusat, sehingga akun database yang kebetulan bernama sama tetap tidak
   membukanya. Yang dilindungi akun ini adalah **keterlihatan menu**, bukan
   halaman operatornya: halaman pusat administrasi meminta ID dan token operator
   server (`Lisensi:AdminOperators`) dengan izin terbatas, yang tidak pernah
   disimpan di aplikasi desa.

---

## 4. Jalan Masuk Aplikasi (sejak 2026-10-13)

Layar login hanya punya **satu** jalur masuk: **akun aplikasi** (username dan
kata sandi; pemasangan baru mendapat kata sandi administrator acak yang
hanya ditampilkan sekali, dan akun lama `Login.dat` tetap berfungsi seperti
dijelaskan pada bagian 3). Sebelumnya layar login juga
menawarkan **Login Google** — otomatis bila ada token tersimpan, atau lewat
tombol "Masuk dengan Google" — yang langsung memberi **sesi Administrator
penuh tanpa kata sandi aplikasi**. Jalur itu **sudah dibongkar** karena
melewati peran, penguncian akun, dan pencatatan identitas sesi.

Alasan memilih akun aplikasi, bukan Google:

1. Aplikasi desa harus bisa dibuka **tanpa internet**; login Google perlu
   jaringan untuk memverifikasi token.
2. Pengguna desa tidak selalu punya akun Google.
3. Satu akun Google yang tersambung di komputer itu tidak boleh berarti
   "siapa pun yang menghidupkan komputer ini menjadi Administrator".

Koneksi akun Google **tetap ada dan tidak terpengaruh**: halaman **"Login dengan
Google"** di menu samping (dibuka setelah pengguna masuk) menyambungkan akun
Google untuk **pencadangan database ke Google Drive**. Kanal Google Formulir/
Sheet dan WhatsApp sudah dihapus, jadi izin OAuth yang diminta hanya Drive.
Kredensial OAuth tetap dibutuhkan paket rilis (lihat
`PANDUAN-GITHUB-ACTIONS.md`), hanya saja bukan untuk masuk ke aplikasi.

> **Sejak versi 2.6.0:** layar login kini memakai `PenggunaService.MasukAsync`
> (PBKDF2, kunci 3× gagal, peran dari database). Akun administrator awal
> disisipkan otomatis ke tabel `Pengguna` saat pemasangan baru dengan kata
> sandi acak per instalasi (ditampilkan sekali di layar login), sehingga
> langsung muncul di halaman Kelola Pengguna dan bisa diganti kata sandinya.
> Sandi bawaan lama `admin` yang sudah terbit di repo publik dirotasi otomatis
> saat dipakai. Instalasi lama
> yang masih memakai `Login.dat` dimigrasi otomatis: saat login pertama kali
> berhasil via `Login.dat`, akun dipindahkan ke tabel `Pengguna` dan kata sandi
> selanjutnya diverifikasi melalui database.

---

## 5. Penegakan Izin di Lapis Layanan

Pemeriksaan izin tidak lagi hanya di menu dan halaman: layanan Core memanggil
`SessionContext.Wajib(izin)` pada aksi yang mengubah data (mis.
`PenggunaService.TambahAsync`, `UbahAsync`, `ResetKataSandiAsync`, `HapusAsync`
menuntut `KelolaPengguna`). Bila peran sesi tidak memegang izin itu,
`IzinDitolakException` dilempar — satu jalur yang sama dipakai desktop maupun
backend `SuDesApp.Api`.

### 5.1 Desktop (WPF)

Sesi tetap berupa nilai global seperti sebelumnya: satu aplikasi, satu
pengguna. `PenegakanAktif` dinyalakan setelah login berhasil.

### 5.2 Backend `SuDesApp.Api`

Satu proses Api melayani banyak pengguna sekaligus, jadi identitas **tidak**
ditulis ke properti global. Middleware memanggil
`SessionContext.MulaiSesiPermintaan(...)` untuk setiap permintaan ber-Bearer
token; nilai itu hidup di `AsyncLocal` (per-aliran async), sehingga peran
Administrator milik satu permintaan tidak terbaca permintaan lain — sudah diuji
lewat 40 permintaan paralel dengan dua peran berbeda.

| Keadaan permintaan | Hasil |
|---|---|
| Tanpa Bearer token | **401** — endpoint memeriksa sesi lebih dulu |
| Token sah, peran memegang izin | Aksi dijalankan (**200**) |
| Token sah, peran tidak memegang izin | **403** + `izin` yang ditolak |
| Tanpa token pada kanal kunci API (mis. `POST /permintaan`) | Penegakan dilewati, setara `SesiSistem` — kanal otomatis bukan pengguna |
| Tanpa token pada formulir warga (`POST /permintaan-publik`) | Diterima sebagai kanal publik, dibatasi 5 kiriman / 10 menit per alamat IP |

Endpoint yang memakai aturan ini: `GET/POST/PUT/DELETE /sudes/api/v1/pengguna`
serta `POST /sudes/api/v1/pengguna/{id}/kata-sandi` (izin `KelolaPengguna`,
hanya Administrator). Bila peran atau status aktif sebuah akun diubah, akun
dihapus, atau kata sandinya direset — semua token milik akun itu langsung
diputus, jadi wewenang dan kata sandi baru berlaku tanpa menunggu sesi 12 jam
habis.

### 5.3 Yang Masih Terbuka

Kanal otomatis di desktop (pekerja latar), kanal kunci API, dan formulir publik
warga sengaja melewati pemeriksaan peran karena bukan pengguna aplikasi.
Formulir publik tetap dibatasi laju per alamat IP dan tidak bisa membaca
antrean: `GET /sudes/api/v1/permintaan` menuntut sesi operator, karena isinya
nama, NIK, dan nomor HP warga. Endpoint tulis lain di `SuDesApp.Api` (surat,
warga, perangkat) belum dibuat; saat dibuat, pola yang sama dipakai: periksa
sesi → jalankan layanan Core → biarkan `Wajib` menilai, dan petakan
`IzinDitolakException` menjadi 403.
