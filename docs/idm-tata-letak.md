# Tata letak halaman IDM

Halaman **Indeks Desa & IDM** (`SuDesApp.Wpf/Views/IdmView.xaml`) disusun
sebagai satu alur baca: tiga kartu ringkasan di atas, rincian indikator penuh
selebar halaman di bawahnya.

```
DataPageFrame
├─ HeaderContent ........ judul + chip ikon
└─ PageContent
   ├─ ToolbarCard ....... muat ulang, situs pusat, dokumentasi, cetak/PDF
   └─ ScrollViewer ...... gulir vertikal seluruh isi halaman
      └─ StackPanel
         ├─ PanelKartuResponsif
         │  ├─ StatusDesaCard ....... identitas, skor IDM, status, IKS/IKE/IKL,
         │  │                         target, riwayat tahun, sumber
         │  ├─ TrenCard ............ legenda + GrafikTrenIdm
         │  └─ IndeksDesaCard ...... total/nilai/status 2025, enam dimensi,
         │                            sub-dimensi, kabar resmi, rujukan resmi
         └─ RincianCard ........... tabel indikator, lebar penuh
```

## Pemilik keadaan

| Bagian | Pemilik |
| --- | --- |
| Angka, tahun, status, daftar indikator, pesan muat | `IdmViewModel` |
| Jumlah kolom, jarak antar kartu, tinggi baris | `Controls/PanelKartuResponsif` |
| Susunan kartu dan isi tiap kartu | `Views/IdmView.xaml` |

`IdmView.xaml.cs` sengaja kosong: tidak ada lagi perpindahan
`Grid.Row`/`Grid.Column` dari code-behind, sehingga keadaan tata letak hanya
dimiliki satu tempat.

## Aturan panel

`PanelKartuResponsif` menata anak **sesuai urutan** dan memilih jumlah kolom
dari lebar yang tersedia:

- **≥ 900 px** — tiga kolom sejajar; semua kartu di baris yang sama dibuat
  setinggi kartu tertinggi.
- **620–899 px** — dua kolom; kartu terakhir turun dan melebar selebar baris.
- **< 620 px** — satu kolom, kartu ditumpuk berurutan.

Jarak antar kartu (12 px) dan ambang kolom adalah properti panel, bukan angka
yang disalin ke tiap kartu. Untuk menambah atau mengganti urutan kartu, cukup
ubah urutan anak di XAML — jangan menulis `Grid.Row`/`Grid.Column` atau
menyembunyikan kartu untuk mengakali tata letak.

## Batas gulir

- Halaman menggulir vertikal sebagai satu kesatuan (`HalamanScrollViewer`).
- Tabel rincian tetap menggulir **mendatar** sendiri (`RincianScrollViewer`)
  karena tujuh kolomnya lebih lebar daripada jendela sempit; barisnya tidak
  dipotong, hanya digeser.
