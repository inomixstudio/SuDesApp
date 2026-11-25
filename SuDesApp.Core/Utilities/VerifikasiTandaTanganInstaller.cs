using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace SuDesApp.Utilities
{
    /// <summary>
    /// Memastikan installer pembaruan ditandatangani Authenticode dan tanda tangannya
    /// valid (rantai sertifikat tepercaya, isi berkas tidak berubah). Dipanggil sebelum
    /// installer dijalankan, karena verifikasi SHA-256 saja tidak cukup: bila digest
    /// tidak tersedia atau berasal dari sumber yang sama dengan berkasnya, hash tidak
    /// membuktikan asal rilis.
    /// </summary>
    public static class VerifikasiTandaTanganInstaller
    {
        private const int BatasWaktuMs = 60_000;

        public static bool Valid(string berkas, out string keterangan)
        {
            keterangan = string.Empty;
            if (string.IsNullOrWhiteSpace(berkas) || !File.Exists(berkas))
            {
                keterangan = "berkas installer tidak ditemukan";
                return false;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = PowerShellWindows.JalurExecutable(),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                psi.ArgumentList.Add("-NoProfile");
                psi.ArgumentList.Add("-NonInteractive");
                psi.ArgumentList.Add("-Command");
                // Path lewat environment variable: tidak ada risiko kutipan/injeksi.
                psi.ArgumentList.Add("(Get-AuthenticodeSignature -LiteralPath $env:SUDES_BERKAS_VERIFIKASI).Status");
                psi.Environment["SUDES_BERKAS_VERIFIKASI"] = berkas;

                using var proses = Process.Start(psi);
                if (proses == null)
                {
                    keterangan = "gagal menjalankan pemeriksa tanda tangan";
                    return false;
                }

                Task<string> keluaran = proses.StandardOutput.ReadToEndAsync();
                Task galat = proses.StandardError.ReadToEndAsync();
                if (!proses.WaitForExit(BatasWaktuMs))
                {
                    try
                    {
                        proses.Kill(entireProcessTree: true);
                    }
                    catch (InvalidOperationException)
                    {
                        // Proses sudah selesai sendiri; galat Kill tidak mengubah hasil.
                    }
                    keterangan = "pemeriksaan tanda tangan melewati batas waktu";
                    return false;
                }

                proses.WaitForExit();
                Task.WaitAll(keluaran, galat);
                string status = keluaran.Result.Trim();
                keterangan = string.IsNullOrEmpty(status) ? "status tanda tangan kosong" : status;
                return string.Equals(status, "Valid", StringComparison.Ordinal);
            }
            catch (Exception ex)
            {
                keterangan = ex.Message;
                return false;
            }
        }
    }
}
