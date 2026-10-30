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
