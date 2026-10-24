# Feature navigation architecture

`BuatSuratCatalog` owns the built-in “Buat Surat Baru” choices and display-name
aliases. `FeatureNavigationViewModel` renders the catalog labels and routes
canonical template identifiers. Beranda shortcuts resolve through the same
catalog. `MainWindowViewModel` owns application-level access checks, scoped
form creation, and navigation lifecycle; the letter form ViewModels own form
state and data collection.
