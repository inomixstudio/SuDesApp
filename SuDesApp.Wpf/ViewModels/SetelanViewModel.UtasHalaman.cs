using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Microsoft.Extensions.Logging;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.GeneratorPdf;
using SuDesApp.Utilities;
using SuDesApp.Wilayah;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class SetelanViewModel : ObservableObject
    {
        // =================================================================
        // Utas halaman. Isian dan koleksi di halaman ini dimiliki utas UI; hanya
        // pekerjaan murni (baca database, cocokkan ±90 ribu baris wilayah) yang
        // boleh berjalan di utas lain. Setiap perpindahan kembali ke utas
        // halaman lewat dua metode di bawah, jadi tidak ada tempat lain yang
        // diam-diam mengubah keadaan terikat dari utas kolam.
        // =================================================================

        /// <summary>
        /// Jalankan perubahan keadaan terikat XAML di utas halaman. Bila sudah
        /// berada di utas itu (keadaan normal di aplikasi), aksinya dijalankan
        /// langsung tanpa perpindahan.
        /// </summary>
        private Task JalankanDiUtasHalamanAsync(Action aksi)
        {
            var dispatcher = _dispatcherHalaman ?? System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
            {
                aksi();
                return Task.CompletedTask;
            }

            return dispatcher.InvokeAsync(aksi).Task;
        }

        /// <inheritdoc cref="JalankanDiUtasHalamanAsync(Action)"/>
        private Task JalankanDiUtasHalamanAsync(Func<Task> aksi)
        {
            var dispatcher = _dispatcherHalaman ?? System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
            {
                return aksi();
            }

            return dispatcher.InvokeAsync(aksi).Task.Unwrap();
        }


    }
}
