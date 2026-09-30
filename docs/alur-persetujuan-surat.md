# Alur Persetujuan Surat (Opsional)

Tahap 4. Inti janjinya: **kebiasaan operator tidak berubah**. Surat tetap dibuat,
disimpan, dan dicetak langsung seperti sebelumnya. Alur persetujuan hanya berjalan
bila operator **secara sadar** memilihnya per surat.

| Hal | Ketentuan |
| --- | --- |
| Bawaan surat baru | Tanpa alur (`StatusPersetujuan` kosong) — perilaku persis seperti versi sebelumnya |
| Pemicu | Menu klik kanan **🖋️ Persetujuan Surat** pada daftar Register Surat |
| Siapa yang boleh | Peran dengan izin `TandaTanganSurat` (Sekdes, Kades, Administrator) |
| Penghalang | Tidak ada — tidak ada tombol wajib, tidak ada verifikasi sebelum terbit |
| Nomor & kode verifikasi | Tidak pernah ditahan oleh alur; tetap dibuat seperti biasa |
| Berkas scan | `Templates\SuratScan\{Surat_<id>_<nomor>}.pdf/.jpg/.png` |

## Alur

```
(kosong) ──ajukan──► DIAJUKAN ──verifikasi──► DIVERIFIKASI ──tanda tangan──► TERBIT
                        │                        │
                        └────────tolak───────────┴──────────► DITOLAK
```

1. **Ajukan Verifikasi** — surat berstatus **Aktif** masuk ke alur; dicatat sebagai
   "Menunggu verifikasi".
2. **Verifikasi Surat…** — pemeriksa mengisi catatan (boleh kosong); status jadi
   "Menunggu tanda tangan".
3. **Tanda Tangan** — ditandatangani; nama akun + waktu tercatat, dan surat wajib
   punya kode verifikasi (disusulkan otomatis bila belum ada).
4. **Tolak Surat…** — alasan **wajib diisi**; surat jadi DITOLAK. Setelah diperbaiki
   boleh diajukan ulang (catatan lama dibersihkan).
5. **Batalkan Pengajuan** — jalan keluar: surat kembali menjadi surat biasa
   tanpa alur. Hanya boleh sebelum terbit/ditolak — jejak yang sudah selesai
   tidak bisa dihapus.

Setiap langkah tercatat di **Riwayat Aktivitas** (aksi, nomor surat, akun pelaku).

## Label cetak

Label dicetak di kepala surat (`SuratRenderer.LabelStatusSurat`) dan hanya
berlaku untuk surat yang memakai alur / pernah dicetak:

| Label | Kapan |
| --- | --- |
| *(tanpa label)* | Surat tanpa alur, cetakan pertama — **inilah perilaku lama** |
| `DRAF — MENUNGGU VERIFIKASI` | Status DIAJUKAN |
| `SUDAH DIVERIFIKASI — MENUNGGU TANDA TANGAN` | Status DIVERIFIKASI |
| `DITOLAK — TIDAK SAH, JANGAN DIPAKAI` | Status DITOLAK (merah, mengalahkan label lain) |
| `SALINAN — CETAKAN KE-n` | Cetakan ke-n ke atas, surat sudah terbit/tanpa alur |

Jumlah cetakan dicatat setiap kali surat terkirim ke printer (tombol cetak
di Register dan tombol **Cetak** di pratinjau). Pencatatan gagal → pencetakan
tetap berjalan; hanya label berikutnya yang tidak diperbarui.

## Berkas scan

**📎 Lampirkan Scan Surat…** menyalin PDF/gambar hasil scan surat bertanda
tangan ke folder `Templates\SuratScan` (ekstensi: `.pdf`, `.jpg`, `.jpeg`,
`.png` — berkas lain ditolak). Kolom `FileScanSurat` menyimpan **nama berkas
saja**, bukan jalur; pembacaan lewat `IPersetujuanSuratService.ResolveScanFullPath`
yang menolak jalur absolut atau berisi `..`. **🗑️ Lepas Scan Surat** menghapus
catatan beserta berkasnya. Penanda 📎 tampil pada kolom *Persetujuan* di daftar.

## Pemantauan dari sistem luar (API)

Status persetujuan bisa dipantau lewat API Desa tanpa membuka aplikasi
(lihat `docs/api-desa.md`):

- `GET /statistik` memuat `suratPerStatusPersetujuan` — jumlah surat per status
  (`DIAJUKAN`, `DIVERIFIKASI`, `TERBIT`, `DITOLAK`, `TANPA_ALUR`); kunci selalu
  terisi penuh walau nol.
- `GET /verifikasi/{kode}` memuat `statusPersetujuan` per surat; untuk surat dalam
  alur nilainya sama dengan label yang tercetak di kertas surat.
- `GET /rekap-bulanan?tahun=YYYY` — rekap 12 bulan per status persetujuan,
  untuk tren setahun penuh.

## Data

Kolom baru pada tabel `Surat` (dipasang oleh `SuratRepository.EnsureSuratSchemaAsync`
untuk database lama): `StatusPersetujuan`, `VerifikasiOleh`, `VerifikasiPada`,
`DitandatanganiOleh`, `DitandatanganiPada`, `CatatanPersetujuan`, `FileScanSurat`,
`JumlahCetak`.

- Model & aturan: `SuDesApp.Core/Data/Models/PersetujuanSuratData.cs`
- Layanan: `SuDesApp.Core/Services/PersetujuanSuratService.cs` (`IPersetujuanSuratService`)
- Penyimpanan kolom: `UpdateSuratPersetujuan`, `UpdateSuratScan`, `CatatCetakSurat`
  di `SuDesApp.Core/Data/Queries/SuratQueries.sql`
- Uji: `tests/SuDesApp.Core.Tests/PersetujuanSuratServiceTests.cs`
