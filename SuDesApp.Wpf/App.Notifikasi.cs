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
        /// Pemberitahuan lisensi setiap aplikasi dijalankan, sesuai alur uji coba:
        ///
        /// • Instalasi baru (Demo): popup meminta memulai trial 30 hari. Menu vital
        ///   tetap terkunci bila pengguna menolak.
        /// • Trial habis / lisensi belum aktif (BacaSaja): popup mengingatkan bahwa
        ///   masa uji coba berakhir dan mengarahkan ke halaman pembayaran; menu
        ///   vital tetap terkunci sampai pembayaran selesai.
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
                                // Popup pemberitahuan memakai jendela bergaya splash
                                // (ukuran & kepala yang sama) — bukan dialog pesan umum,
                                // supaya transisi splash → pemberitahuan trial terasa
                                // satu layar yang berlanjut.
                                bool mulai = SuDesApp.Wpf.Views.TrialPemberitahuanWindow.Pilih(
                                    "Mulai masa uji coba 30 hari",
                                    SuDesApp.Services.PenjagaLisensi.PesanMulaiTrial,
                                    "Mulai Trial", "Nanti Saja");

                                if (!mulai)
                                {
                                    // Menolak trial: menu vital tetap terkunci, dan
                                    // pengguna diberi tahu jalan keluarnya.
                                    SuDesApp.Wpf.Views.TrialPemberitahuanWindow.Pesan(
                                        "Menu vital terkunci",
                                        SuDesApp.Services.PenjagaLisensi.PesanBelumTrial,
                                        nadaPeringatan: true);
                                    break;
                                }

                                // Trial server didahulukan bila kode aktivasi sudah ada
                                // dan server terjangkau; trial lokal hanya cadangan offline.
                                var percobaan = await penjaga.MulaiPercobaanAsync();
                                var (judulPercobaan, pesanPercobaan) =
                                    SuDesApp.Services.PenjagaLisensi.JelaskanPercobaan(percobaan);

                                if (percobaan.BerlakuSampai is DateTime sampaiPercobaan)
                                {
                                    pesanPercobaan += $" Berlaku sampai {sampaiPercobaan.ToLocalTime():dd MMM yyyy}.";
                                }

                                bool ditolak = percobaan.Asal
                                    is SuDesApp.Services.AsalPercobaan.DitolakServer
                                    or SuDesApp.Services.AsalPercobaan.SudahPernahTrial;

                                SuDesApp.Wpf.Views.TrialPemberitahuanWindow.Pesan(
                                    judulPercobaan,
                                    pesanPercobaan,
                                    nadaPeringatan: ditolak);
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

                                // Popup pemberitahuan memakai jendela bergaya splash
                                // yang sama (lihat cabang Demo di atas).
                                bool bukaLangganan = SuDesApp.Wpf.Views.TrialPemberitahuanWindow.Pilih(
                                    judul,
                                    pesan + "\n\nBuka halaman Langganan sekarang untuk menyelesaikan pembayaran?",
                                    "Buka Langganan", "Nanti Saja");

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

                App.Current?.Dispatcher.Invoke(() => kirim(layanan));
            }
            catch { /* aplikasi sedang menutup */ }
        }
    }
}
