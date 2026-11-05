using System;
using System.IO;
using System.Windows;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Views;

namespace SuDesApp.Wpf
{
    public partial class App
    {
        /// <summary>
        /// Dipanggil ketika database aktif tidak bisa dibuka saat startup — misalnya
        /// database terenkripsi versi lama yang berkas kuncinya tidak ada di komputer
        /// ini, atau berkas yang rusak. Pengguna ditawari memilih berkas database
        /// lain (hasil pemulihan cadangan, database beserta kuncinya, atau database
        /// desa lain); pilihan disimpan lalu aplikasi dijadwalkan dibuka ulang,
        /// sehingga seluruh koneksi memakai berkas itu.
        ///
        /// Berkas yang gagal dibuka tidak pernah diubah, ditimpa, atau diganti
        /// otomatis — tanpa kunci aslinya data memang tidak bisa dibaca, dan menebak
        /// hanya berisiko merusak data desa.
        /// </summary>
        /// <returns>
        /// True bila aplikasi sudah dijadwalkan dibuka ulang dengan berkas pilihan
        /// (pemanggil tidak boleh menutup aplikasi lagi); false bila pengguna memilih
        /// keluar, sehingga pemanggil yang menutup aplikasi.
        /// </returns>
        private bool TawarkanPemulihanDatabase(Exception galat)
        {
            var pertanyaan =
                "Berkas database yang dipakai sekarang tidak bisa dibuka.\n\n" +
                galat.Message + "\n\n" +
                "Tidak ada data yang diubah. Aplikasi bisa memakai berkas database lain: " +
                "hasil pemulihan cadangan, database beserta berkas kuncinya, atau database " +
                "desa lain yang masih sehat.\n\n" +
                "Pilih berkas database lain sekarang?";

            while (true)
            {
                if (TanyaYaTidak(pertanyaan) != true) return false;

                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Pilih berkas database desa",
                    Filter = "Berkas database (*.db)|*.db|Semua berkas (*.*)|*.*",
                    CheckFileExists = true,
                    Multiselect = false
                };

                if (dialog.ShowDialog() != true) return false;

                var jalur = Path.GetFullPath(dialog.FileName);
                var status = BerkasDatabase.Periksa(jalur);

                if (status == StatusBerkasDatabase.Siap || status == StatusBerkasDatabase.Terenkripsi)
                {
                    AppPreferenceStore.SetJalurDatabase(jalur);

                    (_serviceProvider?.GetService(typeof(ILogger<App>)) as ILogger<App>)
                        ?.LogInformation(
                            "Database tidak bisa dibuka; pengguna memilih berkas lain: {Jalur} ({Bentuk})",
                            jalur,
                            status == StatusBerkasDatabase.Terenkripsi ? "terenkripsi" : "tanpa enkripsi");

                    MulaiUlangAplikasi.PasangPenanda();
                    if (!MulaiUlangAplikasi.JadwalkanProsesBaru())
                    {
                        MulaiUlangAplikasi.BersihkanPenanda();
                        TampilkanPesanDatabase(
                            "Lokasi database sudah tersimpan, tetapi aplikasi tidak bisa " +
                            "dijadwalkan dibuka ulang otomatis. Tutup jendela ini lalu jalankan " +
                            "SuDesApp kembali — database pilihan akan dipakai.\n\n" +
                            $"Database: {jalur}",
                            AppMessageIcon.Info);
                        return false;
                    }

                    Shutdown();
                    return true;
                }

                // Berkas yang dipilih belum bisa dipakai: jelaskan alasannya, lalu
                // tawarkan pilihan lagi tanpa menutup aplikasi lebih dulu.
                TampilkanPesanDatabase(PesanBerkasTidakBisaDipakai(status, jalur), AppMessageIcon.Warning);
                pertanyaan = "Pilih berkas database lain?";
            }
        }

        private static string PesanBerkasTidakBisaDipakai(StatusBerkasDatabase status, string jalur) =>
            status switch
            {
                StatusBerkasDatabase.BerkasLamaTerkunci =>
                    "Berkas itu adalah database terenkripsi dari versi SuDesApp lama, tetapi " +
                    "berkas kuncinya tidak ada atau tidak cocok di komputer ini.\n\n" +
                    $"Database: {jalur}\n" +
                    $"Berkas kunci yang dicari: {BerkasDatabase.LokasiBerkasKunci}\n\n" +
                    "Salin DesaKunciDb.bin dari komputer asal database (folder " +
                    "%LOCALAPPDATA%\\SuDesApp), atau pilih hasil pemulihan cadangan yang " +
                    "tidak terenkripsi.",

                StatusBerkasDatabase.TidakTerbaca =>
                    "Berkas SQLite itu tidak dapat dibaca dan kemungkinan rusak atau tidak " +
                    "lengkap.\n\n" +
                    $"Database: {jalur}\n\n" +
                    "Pilih berkas lain atau hasil pemulihan cadangan.",

                StatusBerkasDatabase.Terkunci =>
                    "Berkas database itu sedang dipakai proses lain atau aksesnya ditolak, " +
                    "sehingga belum bisa diperiksa.\n\n" +
                    $"Database: {jalur}\n\n" +
                    "Tutup aplikasi lain yang memakai berkas itu, lalu pilih kembali. " +
                    "Berkas ini bukan database terenkripsi yang kehilangan kunci.",

                _ =>
                    "Berkas database itu belum ada atau bukan berkas database yang sah.\n\n" +
                    $"Database: {jalur}"
            };

        private static bool? TanyaYaTidak(string pesan)
        {
            try
            {
                return MessageDialogWindow.Show(
                    "Database", pesan,
                    AppMessageButton.YesNo, AppMessageIcon.Error);
            }
            catch
            {
                // Tema/dialog belum siap pada fase startup: MessageBox bawaan tetap
                // memberi pilihan yang sama.
                return MessageBox.Show(pesan, "Database",
                    MessageBoxButton.YesNo, MessageBoxImage.Error) == MessageBoxResult.Yes;
            }
        }

        private static void TampilkanPesanDatabase(string pesan, AppMessageIcon ikon)
        {
            try
            {
                MessageDialogWindow.Show("Database", pesan, AppMessageButton.Ok, ikon);
            }
            catch
            {
                MessageBox.Show(pesan, "Database", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
