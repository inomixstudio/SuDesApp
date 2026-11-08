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
    public partial class App
    {
        /// <summary>
        /// Pemberitahuan lisensi setiap aplikasi dijalankan, sesuai alur uji coba otomatis:
        ///
        /// • Instalasi baru: trial 30 hari SUDAH aktif otomatis (via SegarkanDariCache)
        ///   — tanpa form/popup. Semua fitur berjalan normal.
        /// • Trial habis / lisensi belum aktif (BacaSaja): popup peringatan mengarahkan
        ///   ke menu AKTIVASI LISENSI atau hubungi developer (WhatsApp). Cetak PDF/Excel
        ///   (kecuali Daftar Hadir) dan sinkron Android terkunci.
        ///
        /// Dipanggil SETELAH jendela utama tampil dan dijalankan lewat Dispatcher
        /// supaya popup tidak menghalangi penggambaran awal.
        /// </summary>
        private void TampilkanPemberitahuanLisensi(Window owner)
        {
            SuDesApp.Services.PenjagaLisensi penjaga;
            try
            {
                penjaga = _serviceProvider!.GetRequiredService<SuDesApp.Services.PenjagaLisensi>();
                penjaga.SegarkanDariCache();
            }
            catch (Exception ex)
            {
                try
                {
                    _serviceProvider!.GetRequiredService<ILogger<App>>()
                        .LogWarning(ex, "Gagal menyiapkan pemberitahuan lisensi saat startup");
                }
                catch { /* logger tidak tersedia — jangan menutupi kegagalan asli */ }
                return;
            }

            owner.Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    switch (penjaga.Keadaan)
                    {
                        case SuDesApp.Services.KeadaanLisensi.Demo:
                            {
                                // Seharusnya tidak tercapai: SegarkanDariCache sudah
                                // mengaktifkan trial otomatis. Bila masih Demo
                                // (mis. berkas trial gagal ditulis), aktifkan diam-diam
                                // tanpa form — jangan bertanya ke pengguna.
                                penjaga.MulaiTrialLokal();
                                // Tidak ada popup: semua fitur langsung normal.
                                break;
                            }

                        case SuDesApp.Services.KeadaanLisensi.BacaSaja:
                            {
                                // Trial habis (tanpa kode aktivasi) dibedakan dari
                                // langganan berbayar yang berakhir supaya pesannya tepat.
                                bool trialHabis = penjaga.TrialPernahDimulai && !penjaga.PunyaKodeAktivasi;
                                string judul = trialHabis ? "Masa uji coba berakhir" : "Lisensi belum aktif";
                                string pesan = trialHabis
                                    ? SuDesApp.Services.PenjagaLisensi.PesanTrialHabis
                                    : SuDesApp.Services.PenjagaLisensi.PesanAksesDikunci;

                                // Popup pemberitahuan memakai jendela bergaya splash.
                                // Arahkan ke menu AKTIVASI LISENSI atau hubungi developer
                                // (tombol WhatsApp di halaman itu) — sesuai permintaan.
                                bool bukaLangganan = SuDesApp.Wpf.Views.TrialPemberitahuanWindow.Pilih(
                                    judul,
                                    pesan + "\n\nBuka menu AKTIVASI LISENSI sekarang? Di sana juga ada tombol hubungi developer via WhatsApp.",
                                    "Buka Aktivasi", "Nanti Saja");

                                if (bukaLangganan && owner.DataContext is SuDesApp.Wpf.ViewModels.MainWindowViewModel vm)
                                {
                                    await vm.ShowLanggananPublicAsync();
                                }
                                break;
                            }
                    }
                }
                catch (Exception ex)
                {
                    try
                    {
                        _serviceProvider!.GetRequiredService<ILogger<App>>()
                            .LogWarning(ex, "Gagal menampilkan pemberitahuan lisensi");
                    }
                    catch { /* logger tidak tersedia — dialog sudah gagal, jangan lempar lagi */ }
                }
            }));
        }

        /// <summary>
        /// Kirim notifikasi lonceng dari thread mana pun. Diam saja bila aplikasi
        /// sedang menutup atau layanannya belum tersedia — penutupan tidak boleh
        /// digagalkan oleh pemberitahuan.
        /// </summary>
        private void KirimNotifikasiLonceng(Action<NotificationService> kirim)
        {
            try
            {
                var layanan = _serviceProvider?.GetService<NotificationService>();
                if (layanan is null) return;

                App.Current?.Dispatcher.InvokeAsync(() => kirim(layanan));
            }
            catch { /* aplikasi sedang menutup */ }
        }
    }
}
