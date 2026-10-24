using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Controls;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Utilities;

namespace SuDesApp.Wpf.Views
{
    /// <summary>
    /// Jendela baca satu dokumen hukum dari dialog persetujuan — dokumen penuh
    /// dirender di dalam aplikasi, tanpa membuka aplikasi asosiasi Windows yang
    /// belum tentu ada di komputer desa.
    /// </summary>
    public partial class PersetujuanBacaWindow : Window
    {
        public PersetujuanBacaWindow(StatusPersetujuan status)
        {
            InitializeComponent();

            ChromeWindowBehavior.Attach(this);

            Title = status.Dokumen.Judul;
            JudulText.Text = status.Dokumen.Judul;
            VersiText.Text = status.VersiTerpasang is null
                ? "Versi dokumen belum terbaca."
                : $"{status.VersiTerpasangTampil} · {status.TanggalBerlakuTampil}";

            if (!status.BisaDibaca)
            {
                GalatText.Text = "Berkas dokumen tidak ditemukan di folder aplikasi.";
                GalatText.Visibility = Visibility.Visible;
                return;
            }

            try
            {
                Pembaca.Document = MarkdownKeFlowDocument.Susun(
                    MarkdownDokumen.Uraikan(File.ReadAllText(status.JalurBerkas!)));
            }
            catch (Exception ex)
            {
                GalatText.Text = "Dokumen tidak bisa dibaca: " + ex.Message;
                GalatText.Visibility = Visibility.Visible;
            }
        }

        private void Tutup_Click(object sender, RoutedEventArgs e) => Close();

        /// <summary>Seret area judul untuk memindah jendela.</summary>
        private void CaptionBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try { DragMove(); } catch { /* jendela sedang dimodali */ }
            }
        }
    }
}
