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
