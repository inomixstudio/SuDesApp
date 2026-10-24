using System;
using System.Windows;
using System.Windows.Input;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Controls;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.ViewModels;

namespace SuDesApp.Wpf.Views
{
    /// <summary>
    /// Dialog persetujuan Kebijakan Privasi dan Syarat &amp; Ketentuan.
    ///
    /// Hasil dialog hanya <see cref="Disetujui"/> = true bila persetujuan
    /// benar-benar TERSIMPAN (sudah diverifikasi layanan). Menutup dialog atau
    /// memilih Keluar berarti pengguna tidak menyetujui — pemanggil (App)
    /// menghentikan aplikasi, sesuai bunyi Syarat dan Ketentuan.
    /// </summary>
    public partial class PersetujuanDokumenWindow : Window
    {
        public PersetujuanDokumenViewModel ViewModel { get; }

        /// <summary>True bila pengguna menyetujui dan catatannya berhasil disimpan.</summary>
        public bool Disetujui { get; private set; }

        public PersetujuanDokumenWindow()
            : this(null)
        {
        }

        public PersetujuanDokumenWindow(PersetujuanDokumenViewModel? viewModel)
        {
            InitializeComponent();

            ChromeWindowBehavior.Attach(this);

            ViewModel = viewModel ?? new PersetujuanDokumenViewModel();
            DataContext = ViewModel;
        }

        private void Setuju_Click(object sender, RoutedEventArgs e)
        {
            // Tombol sudah nonaktif tanpa centang; pemeriksaan ini menjaga agar
            // Enter yang ditekan terlalu cepat tidak pernah mencatat persetujuan
            // yang belum dinyatakan.
            if (!ViewModel.Terima)
            {
                return;
            }

            var hasil = ViewModel.Catat(DateTime.Now);
            if (!hasil.Berhasil)
            {
                // Jangan tutup dialog: pengguna harus tahu persetujuannya belum
                // tersimpan, dan bisa mencoba lagi setelah ruang disk dibebaskan.
                System.Media.SystemSounds.Exclamation.Play();
                return;
            }

            Disetujui = true;
            DialogResult = true;
        }

        private void Keluar_Click(object sender, RoutedEventArgs e)
        {
            Disetujui = false;
            DialogResult = false;
        }

        private void Baca_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: StatusPersetujuan status })
            {
                return;
            }

            if (!status.BisaDibaca)
            {
                MessageDialogWindow.Show(
                    "Dokumen tidak ditemukan",
                    $"Berkas {status.Dokumen.Judul} tidak ditemukan di folder aplikasi. "
                    + "Dokumen ikut terpasang bersama aplikasi; pasang ulang bila berkasnya hilang.",
                    AppMessageButton.Ok, AppMessageIcon.Warning);
                return;
            }

            var pembaca = new PersetujuanBacaWindow(status) { Owner = this };
            pembaca.ShowDialog();
        }

        /// <summary>Seret area judul untuk memindah dialog.</summary>
        private void CaptionBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try { DragMove(); } catch { /* jendela sedang dimodali */ }
            }
        }
    }
}
