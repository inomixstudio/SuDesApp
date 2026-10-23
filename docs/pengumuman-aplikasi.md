# Pengumuman Aplikasi — SuDesApp

Fitur **pengumuman broadcast**: pemberitahuan yang Anda terbitkan untuk semua
instalasi desa muncul sebagai **popup di layar pengguna** (plus entri lonceng
notifikasi) tanpa perlu memperbarui aplikasi.

## Cara menulis pengumuman

**Paling mudah: halaman `/admin`.** Buka `https://<server-Anda>/admin`, masuk dengan ID dan token operator berizin `announcements.manage`, lalu gunakan kartu **Terbitkan pengumuman ke seluruh desa** untuk mengisi judul, isi, jenis (Info/Peringatan), dan opsional jendela Mulai &
Berhenti, tekan **Terbitkan**. Daftar di bawahnya memuat yang pernah diterbitkan
dengan tombol Hapus (endpoint `GET/POST/DELETE /admin/pengumuman` — semuanya
dilindungi operator dengan izin `announcements.manage`, pembatas laju `admin`).

**Atau lewat SQL Editor** (role pemilik tabel):

## Aluran datanya

```
Anda (operator)                       Server SuDesApp                  Aplikasi desa (WPF)
──────────────                        ───────────────                  ───────────────────
Tabel `pengumuman` di Supabase  ─────► GET /pengumuman (publik)  ─────► menagih tiap 60 detik
(atau INSERT lewat SQL Editor)         menyaring aktif+jendela          ↳ sejak dibuka + setelah 15 dt
                                        (LayananPengumuman)              ↳ yang belum pernah tampil
                                                                          di perangkat → popup + lonceng
                                                                          ↳ id ditandai "terlihat"
```

## 1. Tabel di Supabase (`schema.sql`)

Tabel `pengumuman` sudah ada di `SuDesApp.Server/schema.sql`. Jalankan isi
berkas itu di **Supabase Dashboard → SQL Editor** (aman dijalankan berulang).
Kolomnya:

| Kolom | Wajib | Arti |
|---|---|---|
| `id` | otomatis | UUID (bawaan `gen_random_uuid()`); menjadi kunci de-duplikasi popup. |
| `judul` | ya | Judul singkat di popup & lonceng. |
| `isi` | tidak | Isi pesan (boleh multi-baris). |
| `jenis` | tidak | `info` (bawaan) atau `peringatan` — baris popup pegang aksen ⚠ dan masuk lonceng sebagai Warning. |
| `aktif` | ya, bawaan true | Matikan (`false`) saja menutup pengumuman tanpa menghapus barisnya. |
| `mulai` | tidak | Tampilkan mulai waktu ini (NULL = sekarang). |
| `sampai` | tidak | Berhenti tampil setelah ini (NULL = sampai dimatikan). |
| `dibuat` | otomatis | Waktu terbit — memengaruhi urutan tampil. |

Cara menulis pengumuman (SQL Editor):

```sql
-- Pengumuman biasa, tampil mulai sekarang, sampai dimatikan
INSERT INTO pengumuman (judul, isi) VALUES
('Jendela layanan WhatsApp', 'Layanan WA tutup 22:00–05:00 WIB malam ini.');

-- Peringatan dengan jendela tampil 3 hari
INSERT INTO pengumuman (judul, isi, jenis, mulai, sampai) VALUES
('Wajib perbarui ke 10.3', 'Nomor surat ganda diperbaiki di 10.3.', 'peringatan',
 now(), now() + interval '3 days');
```

Waktu memakai UTC (`timestamptz`); popup menampilkannya dalam waktu lokal.

**Mematikan pengumuman.** `UPDATE pengumuman SET aktif=false` menyembunyikannya
dari `GET /pengumuman` (server menyaring kolomnya), tetapi barisnya tetap ada
dan muncul sebagai **Nonaktif** di daftar `/admin` — untuk dimatikan permanen
atau dihapus. Endpoint publik membaca maksimal 50 baris **terbaru**, jadi
pengumuman lama yang masih `aktif` tetap terkirim selama masuk jendela itu.

## 2. Isolasi & keamanan

- Endpoint `GET /pengumuman` **publik tanpa kunci** (seperti kode verifikasi
  surat) dan isinya dapat dibaca siapa pun: sengaja — hanya judul/isi, tidak
  ada data desa.
- Kebijakan RLS-nya (schema.sql): `SELECT` terbuka untuk semua role, sedangkan
  tulis/mengubah/hapus hanya untuk role `sudes_server` (dan operator lewat role
  pemilik tabel di SQL Editor). Kunci `anon` Supabase **tidak bisa** menulis.
  Yang menegakkan ini dua hal sekaligus: `ALTER TABLE pengumuman ENABLE ROW
  LEVEL SECURITY` (kebijakan tidak berlaku tanpa ini) dan pencabutan hak tulis
  `anon`/`authenticated` di `schema.sql`. Keduanya dijaga uji
  `SkemaPostgresTests`.
- Tabel ini tidak punya `tenant_id`: broadcast, bukan data desa, dan tidak ikut
  sistem isolasi lintas-tenant. Kegagalannya juga tidak menggagalkan server
  (tabel belum ada → `GET /pengumuman` jawab `[]`).

## 3. Siklus di aplikasi desa

| Tahap | Berkas |
|---|---|
| Penagihan 60 detik + implementasi (mulai 15 detik setelah jendela tampil) | `SuDesApp.Core/Services/PengumumanKlien.cs` + `SuDesApp.Wpf/Services/PengumumanLatar.cs` |
| Parser & penyaring (pengumuman yang belum pernah tampil) | `PengumumanKlien.cs` (`PengumumanPemilih`) |
| Pratinjau "sudah tampil di perangkat ini" (id dipisah `;`, maks 100) | `SuDesApp.Core/Utilities/PengumumanTerlihatStore.cs` (kunci pref `pengumumanTerlihat`, di AppPreferenceStore) |
| Popup kanan-bawah (oproer 25 detik) | `SuDesApp.Wpf/Views/PengumumanPopupWindow.xaml(.cs)` |
| Lonceng | `NotificationService`.Info/Warning |

Perilaku yang dijanjikan:

- **Popup muncul sekali per pengumuman per perangkat** — setelah pengumuman
  ditampilkan id-nya ditandai "terlihat", jadi buka-tutup aplikasi tidak
  mengulangnya. Pengumuman baru dengan id lain selalu muncul.
- **Tidak deletak modal**: popup otomatis menutup dalam 25 detik, atau bisa
  ditutup dengan tombol ✕; isinya tetap terbaca di lonceng.
- **Offline aman**: server tak terjangkau → penagihan diam, alias tidak ada
  popup; tidak ada retry yang membanjiri log (lic kegagalan dibicarakan sekali).

## 4. Uji

`SuDesApp.Tests/PengumumanTests.cs` — 11 uji: logika jendela waktu dan kolom
`aktif` di server, validasi formulir `/admin` (judul, panjang, jendela, jenis),
parser klien (array, baris rusak, offline, alamat kosong), logika dedupe, dan
batas jumlah id tersimpan.

## Batas yang jujur

- "Realtime" = dekat: jeda terpanjang ≈ 60 detik (polling), bukan push
  WebSocket. Kalau suatu saat perlu push, langkahnya menambah koneksi SSE di
  server; skema data dan aturan de-duplikasi tidak perlu berubah.
- Popup tidak menghitung pengumuman yang "mulai" di masa depan — server yang
  menyaringnya, supaya klien dan server tidak pernah menyimpang.
- Mematikan atau menghapus pengumuman menghentikan penayangan **berikutnya**,
  tetapi tidak menarik popup maupun entri lonceng yang sudah tampil di
  perangkat desa.
