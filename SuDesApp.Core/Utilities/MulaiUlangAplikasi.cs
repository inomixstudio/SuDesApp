using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace SuDesApp.Utilities
{
    /// <summary>
    /// Pendukung tombol "Simpan &amp; Mulai Ulang Sekarang" (Pengaturan Aplikasi →
    /// Database Desa): setelah lokasi database baru disimpan di preferensi,
    /// aplikasi perlu ditutup dan dibuka kembali supaya seluruh sambungan database
    /// lama tertutup rapi sebelum lokasi baru dipakai.
    ///
    /// Kelas ini sengaja tidak menyentuh WPF (pustaka Core dipakai tanpa UI):
    /// tugasnya hanya (1) penanda "proses ini lahir dari mulai ulang" di preferensi,
    /// dan (2) menjadwalkan proses baru yang menunggu proses lama benar-benar
    /// selesai sebelum menjalankan exe yang sama. Penutupan jendelanya sendiri
    /// dikerjakan sisi WPF lewat <c>MainWindow.KeluarUntukMulaiUlang()</c>.
    ///
    /// Menunggu proses lama dulu itu penting: tanpa itu, proses baru bisa membuka
    /// database ketika proses lama masih menulis (backup saat keluar di OnExit),
    /// sehingga inisialisasi database bisa menabrak kunci berkas.
    /// </summary>
    public static class MulaiUlangAplikasi
    {
        /// <summary>Kunci preferensi penanda mulai ulang ("1" = proses baru hasil mulai ulang).</summary>
        private const string KeyMulaiUlang = "mulaiUlangAktif";

        /// <summary>Batas tunggu proses lama selesai sebelum proses baru tetap dijalankan (detik).</summary>
        private const int BatasTungguDetik = 120;

        /// <summary>Benar bila proses ini dibuka kembali oleh tombol mulai ulang.</summary>
        public static bool AdanyaPenandaMulaiUlang =>
            AppPreferenceStore.GetBool(KeyMulaiUlang, false);

        /// <summary>Pasang penanda sebelum proses lama ditutup.</summary>
        public static void PasangPenanda() => AppPreferenceStore.SetBool(KeyMulaiUlang, true);

        /// <summary>Hapus penanda — dipanggil proses baru setelah jendela utama siap.</summary>
        public static void BersihkanPenanda() => AppPreferenceStore.SetBool(KeyMulaiUlang, false);

        /// <summary>Jalur exe proses saat ini; null bila tidak dapat ditentukan.</summary>
        public static string? JalurProsesSaatIni()
        {
            try
            {
                var jalur = Environment.ProcessPath;
                return string.IsNullOrWhiteSpace(jalur) || !File.Exists(jalur) ? null : jalur;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Susun skrip pembantu: tunggu proses lama benar-benar selesai (dengan batas
        /// waktu — proses lama tidak pernah digagalkan penjadwal ini), lalu jalankan
        /// exe yang sama. Internal agar dapat diuji tanpa memunculkan proses nyata.
        /// </summary>
        internal static string SusunSkripPenjadwal(int idProses, string jalurExe, int batasTungguDetik)
        {
            // Kutip tunggal digandakan sesuai aturan literal PowerShell, supaya
            // jalur berisi tanda kutip tetap utuh.
            return $"Wait-Process -Id {idProses} -Timeout {batasTungguDetik} -ErrorAction SilentlyContinue; " +
                   $"Start-Process -FilePath '{jalurExe.Replace("'", "''")}'";
        }

        /// <summary>
        /// Susun argumen baris perintah PowerShell (skrip disandikan Base64 agar
        /// aman dari masalah spasi/karakter khusus). Internal agar dapat diuji
        /// tanpa memunculkan proses nyata.
        /// </summary>
        internal static string SusunArgumenPenjadwal(string skrip)
        {
            var terkode = Convert.ToBase64String(Encoding.Unicode.GetBytes(skrip));
            return $"-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {terkode}";
        }

        /// <summary>
        /// Jadwalkan pembukaan kembali exe yang sama SETELAH proses saat ini selesai.
        /// Dikerjakan proses pembantu terpisah (PowerShell tersembunyi) sebab proses
        /// ini sendiri tidak bisa menunggu dirinya mati. Best effort: false berarti
        /// penjadwalan gagal dan pemanggil JANGAN menutup aplikasi.
        /// </summary>
        public static bool JadwalkanProsesBaru()
        {
            try
            {
                var jalur = JalurProsesSaatIni();
                if (jalur == null) return false;

                var skrip = SusunSkripPenjadwal(Environment.ProcessId, jalur, BatasTungguDetik);

                using var proses = Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = SusunArgumenPenjadwal(skrip),
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                return proses != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
