using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SuDesApp;
using SuDesApp.Configuration;
using SuDesApp.ControlSurat;
using Microsoft.Data.Sqlite;
using SuDesApp.Data.Handlers;
using SuDesApp.Data.Models;
using SuDesApp.Data.Queries;
using SuDesApp.Data.Repositories;
using SuDesApp.GeneratorPdf;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Input;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.ViewModels;
using SuDesApp.Wpf.Views;

namespace SuDesApp.Wpf
{
    public partial class App : Application
    {
        /// <summary>
        /// Nama mutex satu-instance. Nilainya HARUS sama dengan <c>AppMutex</c> di
        /// <c>installer/SuDesApp.iss</c>: installer memakainya untuk mengetahui
        /// bahwa aplikasi sedang berjalan sebelum menimpa berkasnya. Tanpa mutex
        /// yang benar-benar dibuat aplikasi, pemeriksaan itu tidak pernah melihat
        /// apa pun dan upgrade bisa berjalan saat desa.db masih terkunci.
        /// </summary>
        internal const string NamaMutexInstansi = "SuDesApp_SingletonMutex";

        private Mutex? _mutexInstansi;
        private bool _mutexDimiliki;

        private IServiceProvider? _serviceProvider;

        /// <summary>Akses penyedia layanan global (dipakai kode non-DI seperti PdfPreviewView).</summary>
        public IServiceProvider ServiceProvider => _serviceProvider
            ?? throw new InvalidOperationException("ServiceProvider belum diinisialisasi.");

        /// <summary>
        /// Akses cepat layanan cetak PDF dari kode non-DI (mis. PdfPreviewView):
        /// dialog cetak WebView2 melalui library SudesPdf.
        /// </summary>
        public static Services.PdfPrintService PrintService =>
            (App.Current as App)?.ServiceProvider.GetRequiredService<Services.PdfPrintService>()
            ?? throw new InvalidOperationException("ServiceProvider belum diinisialisasi.");
    }
}
