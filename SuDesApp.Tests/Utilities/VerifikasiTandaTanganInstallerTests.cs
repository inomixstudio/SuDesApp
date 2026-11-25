using System.IO;
using SuDesApp.Utilities;
using Xunit;

namespace SuDesApp.Tests.Utilities
{
    /// <summary>
    /// Installer pembaruan tanpa tanda tangan Authenticode harus ditolak sebelum
    /// dijalankan, meskipun hash-nya cocok. Uji ini memakai berkas biasa (tidak
    /// ditandatangani) sebagai contoh yang harus ditolak.
    /// </summary>
    public class VerifikasiTandaTanganInstallerTests
    {
        [Fact]
        public void BerkasTanpaTandaTangan_DitolakDanTidakLolos()
        {
            string folder = Path.Combine(Path.GetTempPath(), "sudes-verif-" + Path.GetRandomFileName());
            Directory.CreateDirectory(folder);
            try
            {
                string berkas = Path.Combine(folder, "SuDesApp_Setup.exe");
                File.WriteAllBytes(berkas, new byte[] { 0x4D, 0x5A, 0x00, 0x00 });

                bool valid = VerifikasiTandaTanganInstaller.Valid(berkas, out string keterangan);

                Assert.False(valid);
                Assert.False(string.IsNullOrWhiteSpace(keterangan));
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void BerkasTidakAda_DitolakTanpaMemanggilPowerShell()
        {
            string berkas = Path.Combine(Path.GetTempPath(), "tidak-ada-" + Path.GetRandomFileName() + ".exe");

            bool valid = VerifikasiTandaTanganInstaller.Valid(berkas, out string keterangan);

            Assert.False(valid);
            Assert.Contains("tidak ditemukan", keterangan);
        }
    }
}
