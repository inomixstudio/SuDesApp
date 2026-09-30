# Audit: Halaman Laporan Penduduk

Catatan internal (akar repo, **bukan** `docs/`) — dijalankan 29 September 2026 atas
permintaan pemelihara. Cakupan: `LaporanPenduduk.cs` (model + builder),
`LaporanPendudukService.cs` (PDF/Excel), `LaporanPendudukGenerator.cs` (QuestPDF),
`LaporanViewModel.cs` + `LaporanView.xaml` (layar), `WargaRepository.GetStatistikWargaAsync`
(sumber angka), dan `LaporanPendudukTests.cs` (587 baris uji yang sudah ada).

Halaman ini adalah keluaran rekapitulasi resmi yang dikirim ke dinas, jadi auditnya
berfokus pada satu pertanyaan: **apakah angka yang dikirim benar** — bukan sekadar apakah
halamannya jalan.

## Ringkasan

| # | Temuan | Tingkat | Status |
|---|--------|---------|--------|
| P1 | Komposisi jenis kelamin memakai angka seluruh warga tetapi dibagi jumlah penduduk — L + P bisa melebihi baris "Penduduk" dan persentasenya meleset | Tinggi | BERES |
| P2 | "Hapus semua" pada pemilih isi laporan justru mencetak **laporan lengkap** (kosong diperlakukan sama dengan null), dan kalau itu diteruskan, generator bahkan melempar galat indeks kosong | Tinggi | BERES |
| P3 | Penduduk dengan jenis kelamin kosong/tidak dikenal hilang diam-diam dari rekap (tidak dihitung L maupun P) | Sedang | BERES |
| P4 | Usia dihitung dengan `strftime('now')` SQLite — laporan yang dicetak pada tanggal lain dari tanggal laporannya menghitung kelompok usia pada hari berjalan | Sedang | dicatat (perilaku diterima) |
| P5 | Properti mati: `LaporanPendudukBuilder.AngkaPersen` dan `LaporanTabelPilihan.DipakaiDan` tidak pernah dipakai | Rendah | BERES |
| P6 | `RenderPdfAsync`/`RenderExcelAsync`/`BuatPdfAsync`/`BuatExcelAsync` dideklarasikan `async` tanpa `await` | Rendah | dicatat (kosmetik) |
| P7 | `GetStatistikWargaAsync` menjalankan ±10 kueri berurutan per penyusunan; halaman memanggilnya ulang pada Segarkan, Pratinjau, dan Simpan | Rendah | dicatat (kinerja) |
| P8 | Satu `LaporanPendudukService` terdaftar scoped dan dipakai bersama; seluruh angka layar/PDF/Excel berasal dari satu `LaporanPendudukData` | — | sudah benar (dipertahankan) |

---

## 1. Komposisi jenis kelamin salah dasar perhitungan (P1, P3)

**Temuan.** `WargaRepository.GetStatistikWargaAsync` menghitung `LakiLaki`/`Perempuan`
dengan `GROUP BY JenisKelamin` **tanpa filter status warga** — termasuk warga pindah dan
meninggal. `LaporanPendudukBuilder.TabelRingkasan` lalu mencetaknya sebagai
`BarisPersen("Laki-laki", r.LakiLaki, penduduk)` di mana `penduduk = TotalAktif + TotalBaru`.

Akibatnya pada desa dengan 70 penduduk dan 100 warga pindah/meninggal, baris L + P
menjumlah 170 orang "dari 70 penduduk" — dan persentase L/P dihitung terhadap pembagi yang
tidak pernah menjadi penyebut yang benar. Tabel ini dikirim ke dinas.

**Perbaikan.**

- `WargaStatistikRingkasan` ditambah tiga angka: `LakiLakiPenduduk`,
  `PerempuanPenduduk`, dan `PendudukJenisKelaminTidakDiketahui`
  (`WargaKeluarga.cs:186-207`). Angka `LakiLaki`/`Perempuan` lama tetap ada karena dipakai
  halaman Data Warga dan API Ringkasan (yang memang menjumlah seluruh warga).
- Kueri jenis kelamin di `WargaRepository.cs:1218` kini mengelompokkan sekaligus menurut
  `MasihTinggal` (`COALESCE(NULLIF(StatusWarga,''), @Aktif) IN (@Aktif, @Baru)`) — satu
  kueri, bukan dua — dan mengisi kedua pasang angka. Baris jenis kelamin yang tidak dikenal
  tetap terhitung ke `PendudukJenisKelaminTidakDiketahui` bila masih tinggal.
- Tabel Ringkasan (builder), sheet "Ringkasan" Excel, dan kartu KPI layar semuanya kini
  memakai angka penduduk. Penduduk yang jenis kelaminnya belum diisi tampil sebagai baris
  **"Jenis kelamin belum diisi"** sendiri (layar/PDF/Excel), sehingga L + P + belum diisi =
  jumlah penduduk — selisih data tidak pernah hilang diam-diam. Barisnya hanya muncul bila
  angkanya lebih dari nol, jadi laporan desa yang datanya lengkap tidak bertambah baris.

**Uji:** `Susun_KomposisiJenisKelaminDariPenduduk_BukanSeluruhWarga`,
`Susun_JenisKelaminBelumDiisi_MunculSebagaiBarisSendiri`,
`Susun_JenisKelaminSemuaTerisi_TidakAdaBarisTidakDiketahui`, plus perbaikan asersi Excel
(`RenderExcel_DenganData_…` kini juga membandingkan sel L/P penduduk).

## 2. "Hapus semua" mencetak laporan lengkap (P2)

**Temuan.** Tiga pemanggilan `_laporan.SusunAsync(…, TabelDipilih.Count > 0 ? TabelDipilih : null)`
memperlakukan daftar kosong sama dengan null. Di sisi lain
`LaporanPendudukBuilder.Susun` hanya mengganti `UrutanTabel` bila `tabelTermasuk is { Count: > 0 }`,
sehingga null = semua tabel. Jadi tombol **"Hapus semua"** menghasilkan laporan **penuh** —
kebalikan dari yang diminta operator, tanpa satu pun peringatan.

Dan kalau perilaku itu suatu saat dibetulkan dengan meneruskan daftar kosong apa adanya,
`LaporanPendudukDocument.Compose` membaca `tabel[0]` tanpa pemeriksaan → `IndexOutOfRangeException`
pada halaman pertama. Dua cacat saling menutupi.

**Perbaikan.**

- Kontrak dibuat eksplisit: **null = semua tabel, daftar kosong = tanpa tabel**.
  `LaporanViewModel` kini meneruskan `TabelDipilih` apa adanya di ketiga tempat, dan builder
  menerima daftar kosong (`LaporanPenduduk.cs`, komentar kontrak di kedua sisi).
- Generator menangani laporan tanpa tabel: satu halaman ringkasan (kartu angka) bertanda
  tangan, bukan galat (`LaporanPendudukGenerator.cs`, `ComposeHalaman` kini menerima
  `LaporanTabel?`).
- `SusunTampilan` layar sudah aman untuk `TabelTerisi` kosong; status halaman menampilkan
  "0 tabel".

**Uji:** `Susun_DaftarTabelKosong_BedaArtinyaDenganNull` (builder) dan
`RenderPdf_SemuaTabelDimatikan_TetapTerbitRingkasanBertandaTangan` (generator — PDF sah,
tepat satu halaman).

## 3. Kelompok usia memakai tanggal berjalan, bukan tanggal laporan (P4)

`WargaRepository.cs:1289` menghitung usia dengan
`strftime('%Y','now') - strftime('%Y',TanggalLahir) …`. `LaporanPendudukBuilder` menerima
`saatCetak` dan mencetaknya sebagai "Per 31 Desember 2026", tetapi angka kelompok usianya
dihitung pada **hari berjalan** saat kueri dieksekusi. Dampaknya muncul hanya pada
laporan yang tanggal cetaknya bukan hari ini (backdate), jadi tidak terdeteksi oleh pemakaian
normal. Memperbaikinya berarti menghitung usia di C# dari `TanggalCetak` untuk seluruh
warga (atau kueri SQLite per tanggal acuan) — perubahan yang menyentuh jalur statistik
utama, jadi **tidak dikerjakan di audit ini**; dicatat sebagai ututan yang benar. Kartu
statistik dan seluruh tabel lain tidak terdampak karena tidak bergantung usia.

Catatan kecil pada tabel yang sama: `UsiaTidakDiketahui` pada ringkasan tidak pernah diisi
repository (baris "(tidak diketahui)" kelompok usia tetap muncul lewat dictionary, jadi
laporannya benar) — properti ini kandidat pembersihan berikutnya.

## 4. Temuan lain

| Kode | Catatan |
|------|---------|
| P5 | `AngkaPersen` (sisa kolom gabungan "1.234 (12,3 %)") dan `DipakaiDan` (kriteria ManyToOne untuk dialog yang tidak pernah dibangun) tidak punya pemanggil — keduanya dihapus agar tidak menyesatkan pembaca berikutnya. |
| P6 | Metode render dideklarasikan `async` tanpa `await` (hanya `return penampung.ToArray()`); berfungsi, tetapi membangun mesin status sia-sia. Kosmetik — dibiarkan agar tanda tangan antarmuka tidak berubah. |
| P7 | Satu penyusunan = ±10 kueri (COUNT, GROUP BY per kolom, pendidikan, KK, kepala KK, usia). Halaman menyusun ulang pada Segarkan, Pratinjau, **dan** Simpan — untuk database desa ribuan baris ini cepat, jadi diterima; bila nanti terasa lambat, satu-satunya jalur yang jelas adalah menembolok hasil penyusunan per kombinasi filter. |
| P8 | Prinsip "satu `LaporanPendudukData` untuk layar/PDF/Excel" sudah benar dan dijaga uji (`RenderExcel_DenganData_…` membandingkan sel workbook dengan tabel builder). Perubahan P1 dipastikan diterapkan **ke ketiga keluaran sekaligus**. |

Pemeriksaan lain yang **lolos** tanpa perubahan: pemetaan pendidikan ke jenjang BPS
(sudah teruji teori 12 kasus + integrasi), urutan RT numerik ("RT 10" > "RT 2"), nama sheet
Excel aman 31 karakter + unik, laporan kosong tetap terbit satu halaman penjelasan, dummy
NIK instansi/kematian dikecualikan, dan pemisahan halaman per tabel (uji jumlah halaman).

## Verifikasi

| Pemeriksaan | Hasil |
|-------------|-------|
| `dotnet build SuDesApp.Wpf` (Debug & Release) | 0 error, 0 warning |
| `dotnet test tests/SuDesApp.Core.Tests` (Debug & Release) | **471/471** hijau (466 → 471: +4 penjaga baru, +1 penyesuaian asersi Excel) |
| `Tools/check-resources.ps1` | LOLOS |
| `Tools/check-kontras-wcag.ps1` | LOLOS |

## Berkas yang berubah

- `SuDesApp.Core/Data/Models/WargaKeluarga.cs` — tiga angka komposisi penduduk baru.
- `SuDesApp.Core/Data/Repositories/WargaRepository.cs` — kueri jenis kelamin sekaligus
  menurut status tinggal; isi kedua pasang angka.
- `SuDesApp.Core/Data/Models/LaporanPenduduk.cs` — tabel Ringkasan memakai angka penduduk +
  baris "Jenis kelamin belum diisi"; kontrak null vs daftar kosong; `AngkaPersen` dihapus.
- `SuDesApp.Core/Services/LaporanPendudukService.cs` — sheet Ringkasan Excel selaras.
- `SuDesApp.Core/GeneratorPdf/LaporanPendudukGenerator.cs` — laporan tanpa tabel tercetak
  sebagai ringkasan bertanda tangan, bukan galat.
- `SuDesApp.Wpf/ViewModels/LaporanViewModel.cs` — daftar kosong diteruskan apa adanya; kartu
  KPI L/P memakai angka penduduk; `DipakaiDan` dihapus.
- `tests/SuDesApp.Core.Tests/LaporanPendudukTests.cs` — 4 uji penjaga baru, asersi uji lama
  disesuaikan dengan kontrak baru.
