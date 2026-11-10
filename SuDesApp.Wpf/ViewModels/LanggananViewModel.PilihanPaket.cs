using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using SuDesApp.ControlSurat;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class LanggananViewModel : ObservableObject
    {
        // =======================================================================
        // Pilihan paket (bulanan / 6 bulan / tahunan).
        // =======================================================================

        private string _paketTerpilih = "bulanan";

        /// <summary>
        /// Kartu paket di halaman. Daftar kodenya tetap (lihat
        /// <see cref="KatalogLangganan.Bawaan"/>) supaya kartu selalu ada isi,
        /// dan ANGKA harga serta masanya diisi dari katalog server begitu server
        /// terjangkau — sebelum itu kartu menampilkan keterangan tanpa rupiah,
        /// persis seperti perilaku lama.
        /// </summary>
        public IReadOnlyList<PaketOpsi> PilihanPaket { get; }

        /// <summary>Paket terpilih (diobserver lewat <see cref="PaketTerpilihOpsi"/>).</summary>
        public PaketOpsi? PaketTerpilihOpsi
        {
            get => PilihanPaket.FirstOrDefault(p => p.Kode == _paketTerpilih) ?? PilihanPaket[0];
            set
            {
                if (value == null) return;
                _paketTerpilih = value.Kode;
                OnPropertyChanged(nameof(PaketTerpilihOpsi));
                SegarkanQris();
            }
        }

        /// <summary>
        /// Paket yang akan dibeli: "bulanan", "enambulan", atau "tahunan".
        /// Nilainya ikut ke server saat membuat tagihan; harga TIDAK dibaca dari
        /// sini — server yang menentukan, supaya angka yang tampil dan yang
        /// ditagih tidak bisa berbeda.
        /// </summary>
        public string PaketTerpilih => _paketTerpilih;


    }
}
