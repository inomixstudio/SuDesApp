using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using SuDesApp.Data.Models;
using SuDesApp.Services;
using SuDesApp.Wpf.Controls;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.ViewModels;

namespace SuDesApp.Wpf.Views
{
    /// <summary>
    /// Dialog bertema untuk berkas Word yang berisi lebih dari satu bentuk surat:
    /// setiap halaman/bagian ditampilkan sebagai satu kartu yang bisa dicentang dan
    /// dinamai, lalu bagian terpilih disimpan menjadi template surat tersendiri.
    ///
    /// Berkas yang hanya memuat satu surat tidak memakai dialog ini — pengguna
    /// langsung dibawa ke wizard template dengan hasil pembacaan berkas.
    /// </summary>
    public partial class WordImporWindow : Window
    {
        private readonly WordImporViewModel _viewModel;
        private readonly List<string> _namaTerpakai;

        private readonly Func<TemplateSuratKustom, Task<PratinjauTemplateSurat?>>? _pratinjau;

        public WordImporWindow(
            WordSuratImpor hasil,
            IEnumerable<string>? namaTerpakai,
            Func<TemplateSuratKustom, Task<PratinjauTemplateSurat?>>? pratinjau = null)
        {
            InitializeComponent();

            ChromeWindowBehavior.Attach(this);

            _namaTerpakai = (namaTerpakai ?? Enumerable.Empty<string>())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .ToList();
            _pratinjau = pratinjau;

            _viewModel = new WordImporViewModel(hasil, _namaTerpakai, pratinjau);
            DataContext = _viewModel;
        }

        /// <summary>
        /// Tampilkan dialog pemilihan bagian. Mengembalikan daftar template terpilih
        /// (null bila pengguna membatalkan). Owner otomatis jendela aktif agar selalu
        /// modal di depan aplikasi.
        /// </summary>
        public static List<(string Nama, TemplateSuratKustom Template)>? Show(
            WordSuratImpor hasil,
            IEnumerable<string>? namaTerpakai,
            Func<TemplateSuratKustom, Task<PratinjauTemplateSurat?>>? pratinjau = null)
        {
            var owner = Application.Current?.Windows.OfType<Window>()
                            .FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;

            var dialog = new WordImporWindow(hasil, namaTerpakai, pratinjau);
            if (owner != null && owner != dialog)
            {
                dialog.Owner = owner;
            }

            return dialog.ShowDialog() == true
                ? dialog._viewModel.Terpilih(dialog._namaTerpakai)
                : null;
        }

        private void PilihSemua_Click(object sender, RoutedEventArgs e) => _viewModel.BalikPilihSemua();

        private void Batal_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void Buat_Click(object sender, RoutedEventArgs e) => DialogResult = true;

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
