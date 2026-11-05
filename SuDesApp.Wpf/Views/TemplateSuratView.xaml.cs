using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using SuDesApp.Data.Models;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.ViewModels;

namespace SuDesApp.Wpf.Views
{
    /// <summary>
    /// Halaman daftar Template Surat (surat buatan pengguna).
    ///
    /// Interaksi per baris — tombol cepat Isi/Lihat/Ubah, klik ganda, dan tombol
    /// Enter/F2 — ditangani di sini karena baris daftar adalah model data biasa
    /// (<see cref="TemplateSuratKustom"/>), bukan ViewModel halaman. Semua
    /// pekerjaan tetap dijalankan ViewModel, termasuk galat dan navigasinya.
    /// </summary>
    public partial class TemplateSuratView : UserControl
    {
        public TemplateSuratView()
        {
            InitializeComponent();
        }

        private TemplateSuratViewModel? Halaman => DataContext as TemplateSuratViewModel;

        private static TemplateSuratKustom? BarisDari(object sender) =>
            (sender as FrameworkElement)?.DataContext as TemplateSuratKustom;

        private void IsiBaris_Click(object sender, RoutedEventArgs e)
        {
            if (Halaman is { } vm)
            {
                PenangananGalatUi.Jalankan(() => vm.BukaIsiAsync(BarisDari(sender)), "Isi template");
            }
        }

        private void PratinjauBaris_Click(object sender, RoutedEventArgs e)
        {
            if (Halaman is { } vm)
            {
                PenangananGalatUi.Jalankan(
                    () => vm.BukaPratinjauAsync(BarisDari(sender)), "Pratinjau template");
            }
        }

        private void UbahBaris_Click(object sender, RoutedEventArgs e)
        {
            if (Halaman is { } vm)
            {
                PenangananGalatUi.Jalankan(() => vm.BukaUbahAsync(BarisDari(sender)), "Ubah template");
            }
        }

        /// <summary>Klik ganda pada baris langsung membuka formulir pengisian.</summary>
        private void DaftarTemplate_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Klik ganda pada tombol cepat tidak boleh ikut mengisi surat: tombol
            // sudah menangani aksinya sendiri.
            if (CariInduk<ButtonBase>(e.OriginalSource as DependencyObject) != null) return;

            if (Halaman is { } vm)
            {
                PenangananGalatUi.Jalankan(vm.IsiTerpilihAsync, "Isi template terpilih");
            }
        }

        /// <summary>Enter = isi surat, F2 = ubah template, dari baris terpilih.</summary>
        private void DaftarTemplate_KeyDown(object sender, KeyEventArgs e)
        {
            if (Halaman is not { } vm) return;

            if (e.Key == Key.Enter)
            {
                // Enter saat fokus berada di tombol cepat berarti menekan tombol
                // itu — jangan sekaligus mengisi surat dari baris terpilih.
                if (CariInduk<ButtonBase>(e.OriginalSource as DependencyObject) != null) return;

                e.Handled = true;
                PenangananGalatUi.Jalankan(vm.IsiTerpilihAsync, "Isi template terpilih");
            }
            else if (e.Key == Key.F2)
            {
                e.Handled = true;
                PenangananGalatUi.Jalankan(() => vm.BukaUbahAsync(vm.Terpilih), "Ubah template");
            }
        }

        /// <summary>Pintasan halaman: Ctrl+F memfokuskan kotak pencarian.</summary>
        private void Halaman_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                KotakCari.Focus();
                KotakCari.SelectAll();
                e.Handled = true;
            }
        }

        /// <summary>Escape di kotak pencarian mengosongkan kata kunci.</summary>
        private void KotakCari_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && Halaman is { } vm)
            {
                vm.Pencarian = string.Empty;
                e.Handled = true;
            }
        }

        private static T? CariInduk<T>(DependencyObject? current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T match) return match;
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
            return null;
        }
    }
}
