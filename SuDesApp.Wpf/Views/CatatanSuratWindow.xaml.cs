using System.Linq;
using System.Windows;
using System.Windows.Input;
using SuDesApp.Wpf.Controls;

namespace SuDesApp.Wpf.Views
{
    /// <summary>
    /// Dialog kecil untuk menulis satu catatan bebas (mis. catatan pemeriksaan atau
    /// alasan penolakan surat) tanpa meninggalkan halaman register. Bertema sama
    /// dengan <see cref="MessageDialogWindow"/> supaya warna mengikuti tema aktif.
    ///
    /// Dipakai lewat <see cref="Tanya"/>; hasil null berarti pengguna membatalkan.
    /// </summary>
    public partial class CatatanSuratWindow : Window
    {
        private bool _catatanWajib;

        /// <summary>Catatan yang diisi pengguna (null bila dialog dibatalkan).</summary>
        public string? Hasil { get; private set; }

        public CatatanSuratWindow()
        {
            InitializeComponent();

            ChromeWindowBehavior.Attach(this);

            BtnBatal.Click += (_, _) => { Hasil = null; DialogResult = false; };
            BtnSimpan.Click += (_, _) => Simpan();
        }

        /// <summary>
        /// Tanyakan satu catatan kepada pengguna. Mengembalikan null bila dialog
        /// dibatalkan/ditutup; teks (boleh kosong bila <paramref name="wajib"/> false)
        /// bila disimpan.
        /// </summary>
        public static string? Tanya(string judul, string pertanyaan, string? teksAwal = null, bool wajib = false)
        {
            var owner = Application.Current?.Windows.OfType<Window>()
                            .FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;

            var dlg = new CatatanSuratWindow();
            dlg.Title = string.IsNullOrWhiteSpace(judul) ? "Catatan Surat" : judul;
            dlg._catatanWajib = wajib;

            if (owner != null && owner != dlg)
            {
                dlg.Owner = owner;
            }

            dlg.PromptText.Text = pertanyaan;
            dlg.CatatanBox.Text = teksAwal ?? string.Empty;

            if (wajib)
            {
                dlg.GalatText.Text = "Catatan wajib diisi supaya alasannya jelas bagi pembuat surat.";
            }

            dlg.Loaded += (_, _) =>
            {
                dlg.CatatanBox.Focus();
                dlg.CatatanBox.CaretIndex = dlg.CatatanBox.Text.Length;
            };

            return dlg.ShowDialog() == true ? dlg.Hasil : null;
        }

        private void Simpan()
        {
            var teks = (CatatanBox.Text ?? string.Empty).Trim();
            if (_catatanWajib && teks.Length == 0)
            {
                GalatText.Visibility = Visibility.Visible;
                CatatanBox.Focus();
                return;
            }

            Hasil = teks;
            DialogResult = true;
        }

        private void CaptionBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }
    }
}
