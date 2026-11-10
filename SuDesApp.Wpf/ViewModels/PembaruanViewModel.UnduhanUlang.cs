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
        // Unduhan yang melanjutkan sendiri saat koneksi terputus
        // =====================================================================

        /// <summary>
        /// Siapkan pelaksana coba ulang: unduhan yang putus di tengah jalan diulang
        /// otomatis dengan jeda bertambah (3 dtk, 6 dtk, 12 dtk, 24 dtk, 48 dtk — lalu
        /// batas tertinggi 1 menit), sampai enam percobaan. Jeda & Batal pengguna tidak
        /// ikut diulang karena bukan gangguan koneksi.
        /// </summary>
        private UnduhanUlangOtomatis BuatPengulangUnduhan()
        {
            _pengulangUnduhan = new UnduhanUlangOtomatis(KebijakanUlang, _logger);
            return _pengulangUnduhan;
        }

        /// <summary>
        /// Kabarkan percobaan ulang di halaman: statusbar menyebut sisa jedanya dan
        /// sebuah kartu menjelaskan bahwa bagian yang sudah terunduh tidak diulang.
        /// </summary>
        private void LaporkanPercobaanUlang(PercobaanUnduhanUlang percobaan)
        {
            StatusText = percobaan.Teks;

            if (percobaan.Menyerah)
            {
                // Kartu kegagalannya ditampilkan penangan galat supaya hanya ada satu kartu.
                return;
            }

            var sisa = CatatanUnduhan.Length == 0 ? string.Empty : $" ({CatatanUnduhan})";
            TampilkanPesan(JenisPesanInline.Peringatan,
                "Koneksi terputus — unduhan dilanjutkan sendiri",
                $"{percobaan.Teks}.\nUnduhan melanjutkan dari bagian yang sudah tersimpan{sisa} — " +
                "tidak diulang dari awal. Halaman boleh ditinggalkan; unduhan tetap berjalan dan hasilnya " +
                "diberitahukan lewat lonceng notifikasi.",
                "coba-ulang-unduhan");
        }

        /// <summary>
        /// Lonceng: unduhan pembaruan selesai (disebut berapa kali koneksi terputus bila ada).
        /// </summary>
        private void KabariUnduhanSelesai(string jenis, string versi, string lokasi, int percobaanUlang)
        {
            try
            {
                _notifications?.Success(
                    "Unduhan pembaruan selesai",
                    $"{jenis} pembaruan {versi} berhasil diunduh" +
                    (percobaanUlang > 0
                        ? $" setelah {percobaanUlang} percobaan ulang karena koneksi terputus."
                        : ".") +
                    $"\nTersimpan di: {lokasi}",
                    "pembaruan");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Notifikasi unduhan selesai tidak bisa dikirim.");
            }
        }

        /// <summary>
        /// Lonceng: unduhan benar-benar gagal (sudah dicoba berulang). Menyebut sisa
        /// unduhan yang masih tersimpan supaya pengguna tahu melanjutkan nanti tidak
        /// mengunduh dari awal.
        /// </summary>
        private void KabariUnduhanGagal(
            string jenis, string versi, string alasan, int percobaanDipakai, UnduhanBerkasBerlanjut? sisa = null)
        {
            try
            {
                var keteranganSisa = sisa is { AdaSisa: true }
                    ? $"\nBagian yang sudah terunduh ({sisa.Persen}% • {sisa.Ringkasan}) tetap tersimpan, " +
                      "jadi melanjutkan nanti tidak mengulang dari awal."
                    : string.Empty;

                _notifications?.Error(
                    "Unduhan pembaruan gagal",
                    $"{jenis} pembaruan {versi} tidak bisa diunduh setelah {Math.Max(1, percobaanDipakai)} percobaan.\n" +
                    $"Penyebab: {alasan}" + keteranganSisa +
                    "\nPeriksa sambungan internet, lalu buka halaman Pembaruan untuk mencoba lagi.",
                    "pembaruan");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Notifikasi unduhan gagal tidak bisa dikirim.");
            }
        }

        /// <summary>
        /// Galat akhir unduhan untuk dilaporkan: menyebut berapa percobaan yang sudah
        /// dicoba bila memang lebih dari satu — kalau percobaan pertama langsung gagal
        /// karena sebab yang pasti, pesan aslinya lebih jelas bagi pengguna.
        /// </summary>
        private static IOException GagalSetelahPercobaan(UnduhanUlangOtomatis pengulang)
        {
            var asli = pengulang.GalatTerakhir;

            return pengulang.PercobaanDipakai <= 1 || asli == null
                ? new IOException(asli?.Message ?? "Unduhan gagal.", asli)
                : new IOException(
                    $"Unduhan tetap gagal setelah {pengulang.PercobaanDipakai} percobaan: {asli.Message}", asli);
        }

        /// <summary>Perbarui persentase & keterangan byte di halaman.</summary>
        private void LaporkanKemajuan(int persen, string? teksUkuran = null)
        {
            DownloadPercent = persen;
            if (!string.IsNullOrEmpty(teksUkuran))
            {
                CatatanUnduhan = teksUkuran!;
            }
        }


    }
}
