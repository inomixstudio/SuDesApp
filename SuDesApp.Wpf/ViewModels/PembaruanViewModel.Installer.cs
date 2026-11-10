using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Utilities;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class PembaruanViewModel : ObservableObject
{
        // =====================================================================
        // Jalur 2: installer penuh
        // =====================================================================

        private async Task UnduhInstallerAsync()
        {
            if (_updateInfo == null || string.IsNullOrWhiteSpace(_updateInfo.DownloadUrl))
            {
                TampilkanPesan(    JenisPesanInline.Peringatan,
                    "Tautan unduhan belum tersedia",
                    "Informasi rilis ini belum memuat tautan unduhan installer. Coba periksa pembaruan lagi " +
                    "beberapa saat lagi, atau pasang pembaruan kecil bila tersedia.",
                    "hasil-pasang");
                return;
            }

            // Melanjutkan unduhan yang sudah dijeda: keputusan mengunduh sudah dijawab
            // sebelumnya, jadi tidak perlu kartu keputusan lagi.
            bool melanjutkan = _unduhanInstaller?.AdaSisa == true;

            if (!melanjutkan)
            {
                var ringkasanPerbaikan = Perbaikan.Count == 0
                    ? "Tidak ada rincian perbaikan."
                    : string.Join("\n", Perbaikan.Take(12).Select(p => "• " + p));

                // Keputusan ditanyakan lewat kartu di dalam halaman — bukan dialog popup.
                var rincian = $"Pembaruan {_updateInfo.Version} memakai installer penuh.\n\n" +
                              $"Yang diperbaiki/ditambah:\n{ringkasanPerbaikan}\n\n" +
                              (string.IsNullOrWhiteSpace(AlasanInstallerText) ? string.Empty : AlasanInstallerText + "\n") +
                              "Installer akan diunduh lalu dijalankan. Unduhan bisa dijeda dan dilanjutkan kapan saja.";

                bool lanjut = await TanyaAsync(
                    "Pembaruan Installer Penuh",
                    rincian,
                    $"Unduh installer pembaruan {_updateInfo.Version} sekarang?",
                    "Unduh Installer",
                    "Nanti");

                if (!lanjut)
                {
                    TampilkanPesan(    JenisPesanInline.Info,
                        "Unduhan installer tidak dijalankan",
                        $"Installer pembaruan {_updateInfo.Version} belum diunduh. Tekan Unduh Installer kapan saja " +
                        "bila sudah siap — tidak ada yang berubah pada aplikasi Anda.",
                        "hasil-installer");
                    return;
                }
            }

            IsDownloading = true;
            UnduhanDijeda = false;
            if (_unduhanInstaller != null) DownloadPercent = _unduhanInstaller.Persen;
            CatatanUnduhan = _unduhanInstaller?.Ringkasan ?? string.Empty;
            StatusText = melanjutkan
                ? $"Melanjutkan unduhan installer dari {CatatanUnduhan}..."
                : "Mempersiapkan download installer...";

            var progress = new Progress<KemajuanUnduhan>(k =>
            {
                LaporkanKemajuan(k.Persen, k.Teks);
                StatusText = (melanjutkan ? "Melanjutkan unduhan installer: " : "Download installer: ") + k.Teks;
            });

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            _ctsOperasi = cts;

            try
            {
                string downloadPath;

                // Koneksi putus bukan lagi kegagalan akhir: unduhan diulang sendiri di
                // latar belakang dengan jeda yang bertambah, dan setiap percobaan
                // melanjutkan dari bagian yang sudah tersimpan (tidak mengulang dari awal).
                var pengulang = BuatPengulangUnduhan();
                var laporanPercobaan = new Progress<PercobaanUnduhanUlang>(LaporkanPercobaanUlang);

                if (_unduhanInstaller != null)
                {
                    // Berkas sementara dipakai lagi: yang sudah terunduh tidak diulang.
                    var unduhan = _unduhanInstaller;
                    bool tuntas = await pengulang.JalankanAsync(
                        ct => unduhan.UnduhAsync(progress, ct), laporanPercobaan, cts.Token);

                    if (!tuntas)
                    {
                        // Sisa unduhan tetap tersimpan (bisa dilanjutkan kapan saja);
                        // penangan galat di bawah yang mencatat riwayat & kartu halaman.
                        throw GagalSetelahPercobaan(pengulang);
                    }

                    downloadPath = unduhan.Tujuan;
                    _unduhanInstaller = null;
                }
                else
                {
                    // Sumber tanpa dukungan lanjutan (mis. Google Drive): jalur lama.
                    var progressPersen = new Progress<int>(persen => LaporkanKemajuan(persen));
                    string hasil = string.Empty;
                    bool tuntas = await pengulang.JalankanAsync(
                        async ct => { hasil = await _updateService.DownloadUpdateAsync(_updateInfo, progressPersen, ct); },
                        laporanPercobaan, cts.Token);

                    if (!tuntas)
                    {
                        throw GagalSetelahPercobaan(pengulang);
                    }

                    downloadPath = hasil;
                }

                StatusText = $"Installer tersimpan di: {downloadPath}";
                DownloadPercent = 100;

                // Selesai (walau sempat terputus berkali-kali): beri tahu lewat lonceng
                // supaya pengguna tahu walau sudah berpindah halaman.
                KabariUnduhanSelesai(
                    "Installer", _updateInfo.Version ?? "-", downloadPath, pengulang.PercobaanUlang);

                // Unduhan selesai: jalur berkasnya ditampilkan di halaman (bukan popup)
                // supaya bisa dibaca/disalin kapan saja sebelum memutuskan menjalankannya.
                var jalankan = await TanyaAsync(
                    "Unduhan installer selesai",
                    $"Installer tersimpan di:\n{downloadPath}\n" +
                    "Jalankan installer sekarang untuk menuntaskan pemasangan, atau nanti " +
                    "(berkasnya tetap ada dan bisa dijalankan sendiri kapan saja).",
                    $"Jalankan installer {_updateInfo.Version} sekarang?",
                    "Jalankan Installer",
                    "Nanti",
                    kunci: "hasil-installer");

                if (!jalankan)
                {
                    // Berkasnya tetap ada: jalur simpan ditampilkan di halaman supaya
                    // bisa dijalankan sendiri nanti tanpa perlu popup apa pun.
                    TampilkanPesan(    JenisPesanInline.Info,
                        "Installer siap dijalankan kapan saja",
                        $"Installer pembaruan {_updateInfo.Version} tersimpan di:\n{downloadPath}",
                        "hasil-installer");
                }
                else
                {
                    // Catat di riwayat: installer dijalankan pengguna. Hasil akhirnya
                    // tergantung installer; versi terpasang akan berubah bila selesai.
                    RiwayatPembaruanStore.Tambah(new EntriRiwayatPembaruan
                    {
                        Versi = _updateInfo.Version ?? "-",
                        Jenis = "Installer",
                        JumlahBerkas = 0,
                        Berhasil = true,
                        Pesan = "Installer penuh dijalankan dari halaman Pembaruan.",
                        DariVersi = GetCurrentVersion().ToString(),
                        Waktu = DateTime.Now
                    });

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = downloadPath,
                        UseShellExecute = true
                    });
                    Application.Current?.Shutdown();
                }
            }
            catch (OperationCanceledException) when (UnduhanDijeda)
            {
                // Jeda — bukan batal: berkas sementara tetap ada dan unduhan bisa
                // diteruskan dari titik ini, bahkan setelah aplikasi dibuka lagi.
                StatusText = $"Unduhan dijeda di {DownloadPercent}% — tekan Lanjutkan untuk meneruskan.";
                TampilkanPesan(    JenisPesanInline.Peringatan,
                    "Unduhan installer dijeda",
                    $"Installer pembaruan {_updateInfo.Version} baru terunduh sebagian" +
                    (CatatanUnduhan.Length == 0 ? "." : $" ({CatatanUnduhan}).") +
                    "\nTekan Lanjutkan Unduhan untuk meneruskannya — bagian yang sudah terunduh tidak diulang, " +
                    "dan sisanya tetap tersimpan walau aplikasi ditutup.",
                    "hasil-installer");
            }
            catch (OperationCanceledException)
            {
                StatusText = "Download dibatalkan";
                TampilkanPesan(    JenisPesanInline.Peringatan,
                    "Unduhan dibatalkan",
                    "Installer tidak jadi diunduh. Aplikasi tetap memakai versi sekarang; unduhan bisa dimulai lagi kapan saja.",
                    "hasil-installer");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal download installer");
                StatusText = "Unduhan installer gagal — lihat keterangan di halaman.";
                CatatRiwayatGagal(_updateInfo.Version ?? "-", "Installer", "Unduhan installer gagal: " + ex.Message);

                // Benar-benar gagal (sudah dicoba berulang, bukan batal pengguna):
                // beri tahu lewat lonceng beserta sisa unduhan yang masih tersimpan.
                KabariUnduhanGagal(
                    "Installer", _updateInfo.Version ?? "-", ex.Message,
                    _pengulangUnduhan?.PercobaanDipakai ?? 0,
                    _unduhanInstaller?.AdaSisa == true ? _unduhanInstaller : null);
                TampilkanPesan(    JenisPesanInline.Galat,
                    "Installer gagal diunduh",
                    $"{ex.Message}\nPeriksa koneksi internet lalu coba lagi; kegagalan ini juga tercatat pada Riwayat Pembaruan di bawah.",
                    "hasil-installer");
            }
            finally
            {
                if (ReferenceEquals(_ctsOperasi, cts))
                {
                    _ctsOperasi = null;
                }

                IsDownloading = false;

                // Saat dijeda persentasenya dipertahankan supaya pengguna melihat
                // sampai mana unduhan berhenti.
                if (!UnduhanDijeda)
                {
                    DownloadPercent = 0;
                }

                OnPropertyChanged(nameof(AdaUnduhanTersisa));
                OnPropertyChanged(nameof(InfoSisaUnduhan));
                OnPropertyChanged(nameof(TeksTombolInstaller));
                OnPropertyChanged(nameof(JedaLanjutTersedia));
                RaiseCommandStates();
            }
        }

        /// <summary>
        /// Batalkan kegiatan yang sedang berjalan. Untuk unduhan installer, sisa
        /// unduhan yang tersimpan juga dibuang — berbeda dengan Jeda yang menyimpannya
        /// supaya bisa dilanjutkan.
        /// </summary>
        private void Batal()
        {
            BuangSisaUnduhan();
            // Hanya kegiatan yang sedang berjalan yang dihentikan — bukan seluruh
            // ViewModel — supaya mencoba lagi setelah Batal tetap bisa berhasil.
            _ctsOperasi?.Cancel();
            StatusText = "Membatalkan...";
        }

        /// <summary>Buang berkas sisa unduhan installer (dipakai tombol Batal).</summary>
        private void BuangSisaUnduhan()
        {
            if (_unduhanInstaller == null)
            {
                return;
            }

            _unduhanInstaller.Bersihkan();
            _unduhanInstaller = null;
            UnduhanDijeda = false;
            CatatanUnduhan = string.Empty;
        }

        public void Dispose()
        {
            _ctsOperasi?.Cancel();
            _ctsOperasi?.Dispose();
            _cts.Cancel();
            _cts.Dispose();
        }

    }
}
