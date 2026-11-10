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
        // Jalur 1: pembaruan kecil (tambalan)
        // =====================================================================

        private async Task TerapkanTambalanAsync()
        {
            if (!BisaTambalan || _updateInfo == null || _tambalan == null || _rencana == null) return;

            var ringkasanPerbaikan = Perbaikan.Count == 0
                ? "Tidak ada rincian perbaikan."
                : string.Join("\n", Perbaikan.Take(12).Select(p => "• " + p));

            // Rincian ditampilkan di kartu keputusan dalam halaman — bukan dialog popup.
            var rincian =
                $"Pembaruan kecil {_tambalan.Versi} siap dipasang.\n\n" +
                $"Yang diperbaiki/ditambah:\n{ringkasanPerbaikan}\n\n" +
                $"{RincianPembaruanText}\n" +
                "Aplikasi akan ditutup sebentar, berkas diganti, lalu dibuka kembali otomatis. " +
                "Data surat, database, dan pengaturan tidak diubah.";

            bool lanjut = await TanyaAsync(
                "Pembaruan Kecil",
                rincian,
                $"Pasang pembaruan kecil {_tambalan.Versi} sekarang?",
                "Pasang Sekarang",
                "Nanti");

            if (!lanjut)
            {
                TampilkanPesan(    JenisPesanInline.Info,
                    "Pembaruan tidak dipasang",
                    $"Pembaruan kecil {_tambalan.Versi} masih tersedia. Tekan tombol Perbarui Sekarang kapan saja " +
                    "bila sudah siap memasangnya — tidak ada yang berubah pada aplikasi Anda.",
                    "hasil-pasang");
                return;
            }

            IsDownloading = true;
            DownloadPercent = 0;
            StatusText = "Mengunduh paket pembaruan kecil...";

            var progress = new Progress<int>(persen =>
            {
                DownloadPercent = persen;
                StatusText = $"Mengunduh paket pembaruan kecil: {persen}%";
            });

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            _ctsOperasi = cts;

            try
            {
                // Unduhan paket tambalan juga diulang sendiri bila koneksi terputus
                // (setiap percobaan mengunduh ulang paketnya, jadi berkas tidak bercampur).
                var pengulang = BuatPengulangUnduhan();
                var laporanPercobaan = new Progress<PercobaanUnduhanUlang>(LaporkanPercobaanUlang);

                TambalanSiap siap = null!;
                bool tuntas = await pengulang.JalankanAsync(
                    async ct => siap = await _patchService.UnduhDanSiapkanAsync(
                        _updateInfo, _tambalan, _rencana, progress, ct),
                    laporanPercobaan, cts.Token);

                if (!tuntas)
                {
                    throw GagalSetelahPercobaan(pengulang);
                }

                DownloadPercent = 100;
                StatusText = $"Menutup aplikasi untuk memasang {_tambalan.Versi}...";

                // Aplikasi harus benar-benar tertutup sebelum berkasnya diganti —
                // skrip penerap menunggu proses ini berakhir, lalu membuka aplikasi lagi.
                _patchService.JalankanPenerapan(siap);

                await Task.Delay(400);
                Application.Current?.Shutdown();
            }
            catch (OperationCanceledException)
            {
                StatusText = "Pembaruan dibatalkan";
                TampilkanPesan(    JenisPesanInline.Peringatan,
                    "Pemasangan dibatalkan",
                    "Pembaruan kecil tidak jadi dipasang dan aplikasi tetap memakai versi sekarang. " +
                    "Data surat dan pengaturan tidak berubah.",
                    "hasil-pasang");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memasang pembaruan kecil");
                StatusText = "Gagal memasang pembaruan kecil";

                // Gagal sebelum skrip penerap jalan — skrip hanya mencatat bila sudah
                // dieksekusi, jadi kegagalan unduhan/verifikasi dicatat di sini.
                CatatRiwayatGagal(_tambalan.Versi, "Tambalan", ex.Message);

                // Benar-benar gagal (bukan batal pengguna): beri tahu lewat lonceng,
                // karena halaman bisa saja sudah ditinggalkan saat percobaan ulang berjalan.
                KabariUnduhanGagal(
                    "Paket pembaruan kecil", _tambalan.Versi, ex.Message,
                    _pengulangUnduhan?.PercobaanDipakai ?? 0);

                TampilkanPesan(    JenisPesanInline.Galat,
                    "Gagal memasang pembaruan kecil",
                    $"{ex.Message}\nSilakan coba lagi, atau pasang lewat installer penuh (tombol Unduh Installer). " +
                    "Aplikasi tetap berjalan normal dengan versi sekarang.",
                    "hasil-pasang");
            }
            finally
            {
                if (ReferenceEquals(_ctsOperasi, cts))
                {
                    _ctsOperasi = null;
                }

                IsDownloading = false;
                DownloadPercent = 0;
            }
        }


    }
}
