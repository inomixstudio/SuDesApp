using System;
using System.IO;
using SuDesApp.Utilities;
using Xunit;

namespace SuDesApp.Core.Tests
{
    /// <summary>
    /// Uji preferensi mode edit awalan nomor surat: apakah halaman Pengaturan
    /// Aplikasi dibuka kembali dalam mode edit (kotak isian awalan tampil) atau
    /// mode tampilan biasa.
    ///
    /// Berkas preferensi ditunjuk lewat <c>AppPreferenceStore.LokasiOverride</c>
    /// seperti KunciIdlePreferenceTests, sehingga berkas preferensi profil user
    /// asli di %LOCALAPPDATA% tidak pernah tersentuh. Koleksi "API" dipakai
    /// bersama karena LokasiOverride statis (satu proses).
    /// </summary>
    [Collection("API")]
    public sealed class ModeEditAwalanPreferenceTests : IDisposable
    {
        private readonly string _jalur;

        public ModeEditAwalanPreferenceTests()
        {
            _jalur = Path.Combine(
                Path.GetTempPath(), "sudes-test",
                Guid.NewGuid().ToString("N"), "login_prefs.json");
            AppPreferenceStore.LokasiOverride = () => _jalur;
        }

        public void Dispose()
        {
            AppPreferenceStore.LokasiOverride = null;
            try
            {
                var dir = Path.GetDirectoryName(_jalur);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // Bersih-bersih berkas sementara non-kritis.
            }
        }

        [Fact]
        public void ModeEditAwalan_Bawaan_Mati()
        {
            // Pemasangan baru membuka halaman Pengaturan dalam mode tampilan:
            // tabel nomor hanya dibaca sampai pengguna sendiri meminta mengedit.
            Assert.False(AppPreferenceStore.IsModeEditAwalanAktif());
        }

        [Fact]
        public void ModeEditAwalan_DinyalakanDanDimatikan_TerjagaAntarBacaan()
        {
            AppPreferenceStore.SetModeEditAwalan(true);
            Assert.True(AppPreferenceStore.IsModeEditAwalanAktif());

            // Dibaca lagi dari berkas (bukan dari sisa memori) supaya kunjungan
            // berikutnya ke halaman Pengaturan benar-benar mengikuti nilai ini.
            Assert.True(File.Exists(_jalur));
            Assert.True(AppPreferenceStore.IsModeEditAwalanAktif());

            AppPreferenceStore.SetModeEditAwalan(false);
            Assert.False(AppPreferenceStore.IsModeEditAwalanAktif());
        }

        [Fact]
        public void ModeEditAwalan_BerkasRusak_JatuhKeModeTampilan()
        {
            // Nilai tak sah (mis. berkas tersunting tangan) tidak boleh membuat
            // aplikasi diam-diam terbuka dalam mode edit.
            Directory.CreateDirectory(Path.GetDirectoryName(_jalur)!);
            File.WriteAllText(_jalur, "modeEditAwalan=ya\n");

            Assert.False(AppPreferenceStore.IsModeEditAwalanAktif());
        }
    }
}
