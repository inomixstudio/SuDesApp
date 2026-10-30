# Feature navigation architecture

`BuatSuratCatalog` owns the built-in “Buat Surat Baru” choices and display-name
aliases. `FeatureNavigationViewModel` renders the catalog labels and routes
canonical template identifiers. Beranda shortcuts resolve through the same
catalog. `MainWindowViewModel` owns application-level access checks, scoped
form creation, and navigation lifecycle; the letter form ViewModels own form
state and data collection.

`MainWindowViewModel` is a partial class split by concern so the shell does not
grow back into one file:

- `MainWindowViewModel.cs` — shared services/state, constructor wiring, `Dispose`;
- `MainWindowViewModel.Navigasi.cs` — routes, page construction and caching,
  sidebar/title state, license gate for vital menus;
- `MainWindowViewModel.Perintah.cs` — command surface and `ExitRequested`;
- `MainWindowViewModel.Status.cs` — status bar, license chip, village info,
  update badges, and notification-history messages.

## Page state cards (empty / failed load)

A page whose content is loaded asynchronously must never end up as a blank
content area when that load fails. The state lives in the page ViewModel and is
rendered by one shared control, `Controls/EmptyStateView` — icon chip → title →
cause → action slot → hint:

| Property | Meaning |
| --- | --- |
| `TampilkanKartuKeadaan` | no content **and** not loading |
| `JudulKeadaan` | distinguishes "not loaded yet" from "load failed" |
| `SebabKeadaan` | the failure text from the service, or the waiting sentence |
| `PetunjukKeadaan` | what to check next / where the page's data comes from |

`IdmViewModel`, `LaporanViewModel`, `KelolaPenggunaViewModel` and
`GoogleDriveViewModel` follow this pattern. A failed load is explained by the
card, with a retry command in the action slot — not only by a modal dialog that
covers the same empty page and then disappears without a trace.

## PDF preview: one path, one owner

Every "generate → temp file → preview" flow shares a single writer,
`Services/PratinjauPdf`: it owns the folder (TempPDF from `AppConfig`, else a
fallback temp folder), the safe file name, and the collision suffix, and it
returns the full path `SudesPdf` needs. ViewModels only produce PDF bytes —
writing them under their own names made previews pile up outside TempPDF.

Letter geometry and base text style live in `TataLetakHalamanSurat`; the kop
preview prints a mini page through the real engine (`KopSuratCetak`) instead of
drawing a second layout in XAML. `Views/PdfPreviewView` owns zoom, page layout
and the per-page bitmap budget; on zoom it resizes the existing cards and keeps
the old bitmap until the new render arrives, so the panel never blanks.

## IDM page: one snapshot, one selection

The IDM page loads several years from the center at once and lets the user pick
which year's indicators are on screen. State is split by owner:

- `IdmSeriTahun` (`SuDesApp.Core/Services/IdmSeriTahun.cs`) owns the loaded year
  series: one entry per year, newest first, `Cari(tahun)` for lookup,
  `Terbaru` for the latest *published* year, and `RingkasanTahun()` for the
  "how many years were loaded" sentence. Duplicate-year suppression lives here
  only; the load loop, the page, and the tests all read these rules.
- `IdmViewModel.DataIdm` (private record) owns one successful load: village
  code, load time, HTTP-fallback flag, and the series. It is replaced only when
  data is installed (fresh from the center or from the backup file).
- `IdmViewModel._tahunDipilih` owns the year whose detail is showing (`0` =
  none). `Hasil` derives from it; there is no stored “current year”.

Data flows one way: client → `PasangData` (build the snapshot, select the newest
year) → derived view. Selecting a year moves `_tahunDipilih` and rebuilds only
the pieces that depend on it (`Indikator`, `Riwayat`); the snapshot is untouched,
so captions about the latest published year and the PDF cannot drift from the
chips. `DeretTren` and the Indeks Desa 2025 card depend on the snapshot, not the
selection.
