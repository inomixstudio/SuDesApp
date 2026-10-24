using System.ComponentModel;
using System.Windows.Controls;
using SuDesApp.Wpf.ViewModels;

namespace SuDesApp.Wpf.Views
{
    /// <summary>
    /// Halaman Kelola Pengguna. Seluruh aturan akun (kata sandi, peran,
    /// perlindungan Administrator terakhir) ada di
    /// <see cref="SuDesApp.Services.IPenggunaService"/>.
    ///
    /// Panel "Ubah Kata Sandi Saya" (pengganti menu Ubah Kata Sandi lama) memakai
    /// PasswordBox yang tidak mendukung binding TwoWay (alasan keamanan WPF),
    /// jadi nilainya disinkronkan ke ViewModel secara manual di sini.
    /// </summary>
    public partial class KelolaPenggunaView : UserControl
    {
        private KelolaPenggunaViewModel? _viewModel;
        private bool _sedangMenyinkronkan;

        public KelolaPenggunaView()
        {
            InitializeComponent();

            SandiLamaBox.PasswordChanged += (_, _) =>
            {
                if (!_sedangMenyinkronkan && _viewModel != null) _viewModel.SandiLama = SandiLamaBox.Password;
            };
            SandiBaruBox.PasswordChanged += (_, _) =>
            {
                if (!_sedangMenyinkronkan && _viewModel != null) _viewModel.SandiBaru = SandiBaruBox.Password;
            };
            KonfirmasiSandiBox.PasswordChanged += (_, _) =>
            {
                if (!_sedangMenyinkronkan && _viewModel != null) _viewModel.KonfirmasiSandi = KonfirmasiSandiBox.Password;
            };

            DataContextChanged += (_, e) => PasangViewModel(e.NewValue as KelolaPenggunaViewModel);
            Unloaded += (_, _) => PasangViewModel(null);
        }

        private void PasangViewModel(KelolaPenggunaViewModel? viewModel)
        {
            if (ReferenceEquals(_viewModel, viewModel)) return;

            if (_viewModel != null) _viewModel.PropertyChanged -= SaatViewModelBerubah;
            _viewModel = viewModel;
            if (_viewModel != null) _viewModel.PropertyChanged += SaatViewModelBerubah;
        }

        /// <summary>
        /// PasswordBox hanya mengalirkan nilai ke ViewModel, tidak sebaliknya.
        /// Karena itu pengosongan yang dilakukan ViewModel (sandi berhasil diubah,
        /// panel dibuka, atau panel ditutup) harus dicerminkan balik ke kotak
        /// isian — tanpa ini sandi lama tetap tertinggal di layar.
        /// </summary>
        private void SaatViewModelBerubah(object? sender, PropertyChangedEventArgs e)
        {
            if (_viewModel == null) return;

            switch (e.PropertyName)
            {
                case nameof(KelolaPenggunaViewModel.SandiLama):
                    Kosongkan(SandiLamaBox, _viewModel.SandiLama);
                    break;
                case nameof(KelolaPenggunaViewModel.SandiBaru):
                    Kosongkan(SandiBaruBox, _viewModel.SandiBaru);
                    break;
                case nameof(KelolaPenggunaViewModel.KonfirmasiSandi):
                    Kosongkan(KonfirmasiSandiBox, _viewModel.KonfirmasiSandi);
                    break;
                case nameof(KelolaPenggunaViewModel.IsPanelSandiTerbuka):
                    if (!_viewModel.IsPanelSandiTerbuka)
                    {
                        Kosongkan(SandiLamaBox, string.Empty);
                        Kosongkan(SandiBaruBox, string.Empty);
                        Kosongkan(KonfirmasiSandiBox, string.Empty);
                    }
                    break;
            }
        }

        private void Kosongkan(PasswordBox kotak, string? nilai)
        {
            if (!string.IsNullOrEmpty(nilai)) return;   // ViewModel masih berisi nilai — jangan sentuh isian pengguna
            if (kotak.Password.Length == 0) return;

            _sedangMenyinkronkan = true;
            try { kotak.Password = string.Empty; }
            finally { _sedangMenyinkronkan = false; }
        }
    }
}
