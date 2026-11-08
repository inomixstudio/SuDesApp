# SuDesApp 2.11.0 — 8 November 2026

Pembaruan keamanan dan ketahanan yang besar: verifikasi sertifikat Supabase diaktifkan kembali, jalur HTTP polos IDM ditutup, kredensial perangkat Android disimpan terenkripsi, sinkronisasi dibuat idempoten, dan kunci lisensi mendapat identitas (`kid`) untuk rotasi yang aman. Tidak ada perubahan format data: `desa.db`, `Templates`, dan arsip surat lama dipakai apa adanya.

## Keamanan

- **Koneksi Supabase kini memverifikasi sertifikat.** `Trust Server Certificate` dan `Include Error Detail` dihapus dari string koneksi; `SSL Mode=Require` murni. Man-in-the-middle pada pooler kini terdeteksi.
- **Data IDM tidak lagi diambil lewat HTTP.** Cadangan sertifikat kedaluwarsa ke HTTP polos dihapus — aplikasi gagal-tertutup dan menampilkan galat, bukan angka tanpa enkripsi yang bisa dimodifikasi jaringan.
- **Secret OAuth Google tidak lagi tertanam di assembly rilis.** Berkas `*.lokal.cs` dikecualikan dari build `Release`; rilis memakai kredensial DPAPI perangkat atau diisi teknisi lewat Pengaturan.
- **Backend `SuDesApp.Api` diperketat:** default ikat loopback `127.0.0.1:5188`, rate limit global 120/menit, penanganan galat terpusat (`ProblemDetails`), dan jejak audit akses data pribadi (`/warga`, `/surat`) di log.
- **Token sesi `SuDesApp.Api` disimpan sebagai digest SHA-256**, bukan teks polos.
- **Kunci lisensi mendapat `kid`:** token ditandatangani baru berformat `v2.<kid>.<payload>.<signature>`; klien tetap menerima format lama `v1` selama masa transisi. Di `Production`, kunci yang hilang tidak lagi diganti diam-diam — server menolak start.
- **Kode aktivasi disamarkan di log** (hanya 4 karakter terakhir) dan input `KodeAktivasi`/`IdPerangkat` dibatasi 128 karakter.

## Android

- **Kredensial perangkat terenkripsi:** kata sandi dan token kini disimpan dengan `EncryptedSharedPreferences` (kunci di Android Keystore), dengan migrasi sekali dari storage plaintext lama.
- **URL QR divalidasi:** hanya `https` yang berakhir `.supabase.co` (atau sama dengan bawaan APK) diterima; QR `http://`/domain asing ditolak.
- **Keluar perangkat ikut mengakhiri sesi di server** (`auth/v1/logout`) sehingga refresh token dicabut.
- **Retry terjadwal:** `WorkManager` mengirim surat tertunda saat jaringan tersedia (15 menit, kendaraan: terkoneksi), bukan hanya saat menu dibuka.
- **Surat tertunda rusak tidak lagi dihapus diam-diam** — dipindah ke `antrean-tertunda-gagal/` untuk diperiksa.
- **Idempotensi sinkron:** setiap surat mendapat `client_uuid` sekali; `antrean_surat` mendapat unique index `(tenant_id, client_uuid)` (migrasi `20261111000000`), sehingga retry setelah timeout tidak lagi menduplikasi surat di register.
- `minify` + `shrinkResources` diaktifkan untuk build release; `FileProvider` dipersempit ke `Download/SuDesApp/`; string hint pengisian manual yang tak terpakai dihapus.

## Desktop (WPF & Core)

- **Penomoran surat diserialkan** (`SemaphoreSlim` statis) — tabrakan nomor antar jendela/impor sinkron dijamin bersih luar; `UNIQUE(NomorSurat)` tetap jaring terakhir.
- **`QueryProvider` lebih tahan:** komentar `--` tidak lagi memotong literal string yang mengandung `--`, validasi case-insensitive dan menerima placeholder `{WhereClause}`, definisi ganda (`InsertSurat`) dicatat warning.
- **`FileService.SanitizePath`** menolak traversal (`..`) dan memastikan hasil relatif tetap di dalam `BaseDirectory`.
- **`AsyncRelayCommand<T>`** tidak lagi melempar `InvalidCastException` untuk parameter null/salah tipe.
- **`PenggunaRepository`** tidak lagi memblokir di konstruktor (`GetAwaiter().GetResult()` dihapus); tabel dipastikan malas sebelum kueri pertama.
- **`DetakSambunganServer.Berhenti()`** tidak lagi membuang CTS selagi loop berjalan; dispose ditunda sampai loop berhenti.
- **`ApiListener`** dibatasi 32 konkurensi; kelebihan menjawab 503 `Retry-After`.
- **Parser impor dibatasi:** entri zip XLSX > 50 MB ditolak; PDF > 100 MB ditolak (anti zip-bomb/OOM).
- **`GoogleDriveService`** menjalankan alur OAuth tanpa menangkap konteks sinkronisasi UI.
- **`SinkronSuratService`** menandai sinkron dalam transaksi (log+hapus atomik) dan menarik antrean dengan filter status + `LIMIT 100`.

## Operasional

- SDK CI/release disamakan ke `11.0.100-rc.3`; `Dockerfile` dirapikan dan healthcheck diganti probe TCP portable (bukan `/dev/tcp` dash).
- CI kini memakai **Gitleaks**, pindai secret lebih tajam, `dependabot.yml` (NuGet/Actions/Gradle mingguan), dan job uji unit Android (`testDebugUnitTest`) pertama.
- Wizard installer kini berbahasa **Indonesia** (lama: Inggris saja). Ditambah `docs/cadangan-dan-pemulihan.md` untuk petugas.
- Semua `uses:` Actions di-pin ke SHA penuh (`checkout`, `setup-dotnet`, `setup-java`, `setup-gradle`).

## Tampilan & Performa (Ronde Lanjutan)

- Token radius modern di `ThemeStyles.xaml` (`Kecil 4 / Kontrol 6 / Kartu 8 / Header 10 / Dialog 14 / Penuh 999`) + style `SkeletonBar` tunggal tinggi 6; kartu statistik Beranda kini basis `KpiCardStyle` global.
- **Virtualisasi list utama**: Warga & Register memakai `VirtualizingStackPanel` (`Standard`) sehingga hanya baris terlihat yang direalisasi.
- **PerangkatDesa lazy**: hanya tabel aktif yang dibangun tiap muat (bukan 12 tabel); PDF dan SK dibuat di `Task.Run` agar UI tidak membeku; debounce pratinjau kop 120 → 350 ms.
- Kartu galat offline IDM diuji (`IdmKartuKeadaanTests`, 2 test) — halaman selalu menampilkan kartu saat gagal/offline, bukan layar kosong.
- `PerangkatDesaViewModel` dipecah menjadi `partial` (`EksporSk`, `Sk`) tanpa mengubah perilaku; pengecualian nullable Core diturunkan 12 → 4 kode.
- Komentar virtualisasi yang usang di `ThemeStyles.xaml` diperbaiki.

## Verifikasi

- `dotnet build SuDesApp.slnx` hijau, 0 peringatan, 0 galat.
- `dotnet test SuDesApp.Tests` — 424/424 lolos.
- Uji unit Android pertama (`AlamatSusunTest`) ditambahkan.

## Cara memasang

| Versi terpasang | Yang dipakai | Unduhan |
|---|---|---|
| 2.10.5 | satu pembaruan kecil (tambalan) | hanya berkas yang berubah |
| di bawah 2.10.5 | installer penuh 2.11.0 | ± 100 MB |

Aplikasi memilih jalur ini sendiri: tambalan hanya dipasang bila versi terpasang tercantum di `dariVersi` pada `patch.json`, sedangkan versi lain diarahkan ke installer penuh. Server lisensi perlu migrasi Supabase `20261111000000_client_uuid_idempotensi.sql` diterapkan sekali di SQL Editor sebelum perangkat Android baru dipakai bersamaan; aplikasi lama tetap berfungsi tanpa kolom itu (kolom nullable, unique index ber-partial).

Data desa (`desa.db`, `Templates`, hasil PDF) tidak ditimpa saat pemasangan versi baru.

## Tautan

- Semua rilis: <https://github.com/inomixstudio/SuDesApp/releases>
- Masalah dan permintaan fitur: <https://github.com/inomixstudio/SuDesApp/issues>
