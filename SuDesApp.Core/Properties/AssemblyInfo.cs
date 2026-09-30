using System.Runtime.CompilerServices;

// Uji unit memanggil sebagian helper internal (mis. JudulTampil dan IsiPlaceholder
// pada SkPerangkatGenerator, atau Analisis pada VerifikasiPenomoranService) supaya
// aturan bentuk SK dan penomoran bisa diuji tanpa menyiapkan SQLite atau menulis
// PDF. Helper tetap internal supaya tidak menjadi bagian API publik.
[assembly: InternalsVisibleTo("SuDesApp.Core.Tests")]
