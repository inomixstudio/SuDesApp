using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace SuDesApp.Utilities
{
    /// <summary>
    /// Menjalankan installer pembaruan TANPA satu pun pilihan untuk diklik: Inno
    /// Setup dipanggil dalam mode senyap (<c>/SILENT</c>) sehingga hanya jendela
    /// kemajuan pemasangan yang terlihat, lalu aplikasi dibuka kembali.
    ///
    /// Installer tidak bisa mengganti berkas program selama aplikasi berjalan
    /// (mutex <c>SuDesApp_SingletonMutex</c> masih dipegang dan DLL sedang
    /// dipakai). Karena itu pemasangan dijadwalkan ke proses PowerShell pembantu
    /// TERSEMBUNYI yang menunggu proses ini benar-benar berakhir lebih dulu.
    ///
    /// Kelas ini sengaja tidak menyentuh WPF: penutupan jendela dan penghentian
    /// layanan tetap dikerjakan sisi aplikasi, sedangkan urutan menunggu → pasang
    /// → buka lagi dimiliki satu tempat ini.
    /// </summary>
    public static class PemasangPembaruan
    {
        /// <summary>
        /// Argumen Inno Setup: tanpa wizard dan tanpa kotak pesan, tetapi jendela
        /// kemajuan pemasangan tetap tampil — persis "loading penginstalan" tanpa
        /// pertanyaan apa pun. <c>/CLOSEAPPLICATIONS</c> menutup sisa proses lama
        /// yang masih memegang berkas (bila ada), <c>/NORESTART</c> mencegah Inno
        /// memulai ulang Windows sendiri.
        /// </summary>
        public const string ArgumenSenyap = "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS";

        /// <summary>Argumen yang sama, terpisah per item untuk PowerShell.</summary>
        private static readonly string[] Argumen = ToArray(ArgumenSenyap);

        /// <summary>Batas tunggu proses lama berakhir sebelum pemasangan tetap dijalankan (detik).</summary>
        private const int BatasTungguDetik = 180;

        /// <summary>
        /// Susun skrip pembantu: tunggu proses aplikasi berakhir (dengan batas waktu),
        /// jalankan installer senyap sampai selesai, lalu buka aplikasi lagi.
        /// Internal agar urutannya dapat diuji tanpa memunculkan proses nyata.
        /// </summary>
        internal static string SusunSkrip(
            int idProses, string jalurInstaller, string? jalurAplikasi, int batasTungguDetik)
        {
            var skrip = new StringBuilder();
            skrip.Append($"Wait-Process -Id {idProses} -Timeout {batasTungguDetik} -ErrorAction SilentlyContinue; ");
            skrip.Append($"Start-Process -FilePath {Kutip(jalurInstaller)}");
            skrip.Append(" -ArgumentList ");
            skrip.Append(string.Join(",", Array.ConvertAll(Argumen, Kutip)));
            skrip.Append(" -Wait");

            if (!string.IsNullOrWhiteSpace(jalurAplikasi))
            {
                skrip.Append($"; Start-Process -FilePath {Kutip(jalurAplikasi!)}");
            }

            return skrip.ToString();
        }

        /// <summary>
        /// Susun argumen baris perintah PowerShell (skrip disandikan Base64 agar aman
        /// dari spasi dan karakter khusus pada jalur berkas).
        /// </summary>
        internal static string SusunArgumen(string skrip)
        {
            var terkode = Convert.ToBase64String(Encoding.Unicode.GetBytes(skrip));
            return $"-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {terkode}";
        }

        /// <summary>
        /// Jadwalkan pemasangan installer setelah proses ini berakhir. Best effort:
        /// <c>false</c> berarti penjadwalan gagal (mis. installer hilang) dan
        /// pemanggil JANGAN menutup aplikasi.
        /// </summary>
        /// <param name="jalurInstaller">Berkas installer yang sudah diunduh &amp; diverifikasi.</param>
        /// <param name="jalurAplikasi">
        /// Exe yang dibuka kembali setelah pemasangan; null berarti aplikasi tidak
        /// dibuka lagi (mis. pengguna memang hanya ingin menutup aplikasi).
        /// </param>
        public static bool JalankanSetelahKeluar(string jalurInstaller, string? jalurAplikasi = null)
        {
            if (string.IsNullOrWhiteSpace(jalurInstaller))
            {
                return false;
            }

            try
            {
                if (!File.Exists(jalurInstaller))
                {
                    return false;
                }

                string? exe = !string.IsNullOrWhiteSpace(jalurAplikasi) && File.Exists(jalurAplikasi)
                    ? jalurAplikasi
                    : null;

                var skrip = SusunSkrip(Environment.ProcessId, jalurInstaller, exe, BatasTungguDetik);

                using var proses = Process.Start(new ProcessStartInfo
                {
                    FileName = PowerShellWindows.JalurExecutable(),
                    Arguments = SusunArgumen(skrip),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });

                return proses != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Kutip literal PowerShell (tanda kutip tunggal digandakan).</summary>
        private static string Kutip(string teks) => "'" + teks.Replace("'", "''") + "'";

        private static string[] ToArray(string argumen) =>
            argumen.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}
