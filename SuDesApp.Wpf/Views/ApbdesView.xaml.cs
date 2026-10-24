using System.Windows;
using System.Windows.Controls;

namespace SuDesApp.Wpf.Views
{
    /// <summary>
    /// Halaman APBDES. Seluruh keadaan halaman — termasuk daftar tahun anggaran —
    /// diurus <see cref="ViewModels.ApbdesViewModel"/> dan pengikatan datanya;
    /// di belakang layar hanya ada satu pemicu: menarik anggaran publikasi pusat
    /// (Prodeskel) sekali setiap kali halaman dibuka.
    /// </summary>
    public partial class ApbdesView : UserControl
    {
        private bool _sudahTarikPusat;

        public ApbdesView()
        {
            InitializeComponent();
            Loaded += TarikPusatSaatDibuka;
        }

        /// <summary>
        /// Sekali per pembukaan halaman: tarik anggaran yang sudah dipublikasikan
        /// pemerintah pusat, sehingga panel Riwayat terisi tanpa perlu menekan
        /// tombol. Gagal jaringan tidak memblokir halaman — alasannya tampil di
        /// <c>StatusPusat</c> dan tombol "Muat dari Pusat" tetap bisa dicoba lagi.
        /// </summary>
        private async void TarikPusatSaatDibuka(object sender, RoutedEventArgs e)
        {
            if (_sudahTarikPusat) return;
            _sudahTarikPusat = true;

            if (DataContext is ViewModels.ApbdesViewModel vm)
            {
                await vm.MuatDariPusatAsync();
            }
        }
    }
}
