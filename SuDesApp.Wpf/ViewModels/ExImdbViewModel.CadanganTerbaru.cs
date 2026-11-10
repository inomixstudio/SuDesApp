using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class ExImdbViewModel : ObservableObject
    {
        // =====================================================================
        // Cadangan terbaru di folder database
        // =====================================================================

        /// <summary>
        /// Cadangan .db terbaru di folder database (selain database aktif).
        /// Dipakai agar pemulihan bisa dimulai dari daftar ini tanpa membuka
        /// dialog berkas — termasuk cadangan otomatis sebelum impor.
        /// </summary>
        public ObservableCollection<CadanganTampil> CadanganTerbaru { get; } = new();

        public bool AdaCadanganTerbaru => CadanganTerbaru.Count > 0;

        public bool TidakAdaCadangan => CadanganTerbaru.Count == 0;

        /// <summary>Jumlah cadangan yang tampil — dipakai petak KPI dan label bagian.</summary>
        public string JumlahCadangan => CadanganTerbaru.Count == 0
            ? "Belum ada"
            : CadanganTerbaru.Count + " berkas";

        /// <summary>Waktu cadangan terbaru, atau ajakan membuat cadangan pertama.</summary>
        public string CadanganTerakhir => CadanganTerbaru.Count == 0
            ? "Belum pernah"
            : CadanganTerbaru[0].Diubah;

        public string KeteranganCadangan => CadanganTerbaru.Count == 0
            ? "Belum ada berkas cadangan di folder database. Buat cadangan pertama lewat bagian Ekspor."
            : $"{CadanganTerbaru.Count} cadangan terbaru di folder database, terbaru lebih dulu. "
              + "Klik Pulihkan pada salah satu kartu untuk memakai berkas itu.";

        public ICommand MuatCadanganCommand { get; }
        public ICommand PulihkanCadanganCommand { get; }

        /// <summary>Buat cadangan tanpa dialog, langsung di folder database.</summary>
        public ICommand BuatCadanganCepatCommand { get; }

        public ICommand SalinLokasiDatabaseCommand { get; }

        /// <summary>Baca ulang info database sekaligus pindai ulang berkas cadangan.</summary>
        private void SegarkanData()
        {
            MuatInfoDatabase();
            MuatCadanganTerbaru();
            StatusText = "Informasi database dan daftar cadangan disegarkan.";
        }

        /// <summary>Pindai ulang folder database dan segarkan daftar cadangan.</summary>
        public void MuatCadanganTerbaru()
        {
            CadanganTerbaru.Clear();

            try
            {
                var aktif = _service.GetDatabasePath();
                var folder = Path.GetDirectoryName(aktif) ?? string.Empty;

                foreach (var cadangan in CadanganDatabase.Pindai(folder, aktif))
                {
                    CadanganTerbaru.Add(new CadanganTampil(
                        cadangan.Path,
                        cadangan.Nama,
                        FormulirTemplateUkuran(cadangan.UkuranByte),
                        cadangan.Diubah.ToString("dd MMM yyyy HH:mm"),
                        cadangan.SebelumImpor ? "Otomatis sebelum impor" : "Cadangan ekspor",
                        cadangan.SebelumImpor ? "\U0001F6DF" : "\U0001F5C4"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Daftar cadangan tidak dapat dipindai");
            }

            OnPropertyChanged(nameof(AdaCadanganTerbaru));
            OnPropertyChanged(nameof(TidakAdaCadangan));
            OnPropertyChanged(nameof(JumlahCadangan));
            OnPropertyChanged(nameof(CadanganTerakhir));
            OnPropertyChanged(nameof(KeteranganCadangan));
            (BuatCadanganCepatCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }

        /// <summary>
        /// Mulai pemulihan dari salah satu kartu cadangan: isi jalur impor,
        /// pindah ke bagian Impor, lalu buka kartu konfirmasi. Berkas yang
        /// sudah hilang tidak pernah masuk ke jalur impor.
        /// </summary>
        private void PulihkanCadangan(CadanganTampil? cadangan)
        {
            if (cadangan is null || IsImporting || IsExporting)
            {
                return;
            }

            if (!File.Exists(cadangan.Path))
            {
                TampilkanPesan("Berkas cadangan tidak ditemukan",
                    "Berkas " + cadangan.Nama + " sudah tidak ada di folder database.", berhasil: false,
                    "Daftar cadangan sudah disegarkan — pilih berkas lain atau telusuri lewat tombol Pilih Berkas.");
                MuatCadanganTerbaru();
                return;
            }

            ImportPath = cadangan.Path;
            AdaPesan = false;

            var bagianImpor = Bagian.FirstOrDefault(b => b.Kunci == "Impor");
            if (bagianImpor != null)
            {
                PilihBagian(bagianImpor);
            }

            AdaKonfirmasiImpor = true;
            StatusText = "Menunggu konfirmasi impor: " + cadangan.Nama;
        }

    }
}
