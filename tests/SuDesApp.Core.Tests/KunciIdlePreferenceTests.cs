using System;
using System.IO;
using SuDesApp.Utilities;
using Xunit;

namespace SuDesApp.Core.Tests
{
    /// <summary>
    /// Uji preferensi kunci otomatis saat idle (layar kunci): sakelar aktif
    /// dan batas waktu diam dalam menit.
    ///
    /// Berkas preferensi ditunjuk lewat <c>AppPreferenceStore.LokasiOverride</c>
    /// seperti ApiServiceTests/ApiListenerEndToEndTests, sehingga berkas
    /// preferensi profil user asli di %LOCALAPPDATA% tidak pernah tersentuh.
    /// Koleksi "API" dipakai bersama karena LokasiOverride statis (proses satu)
    /// dan oleh karena itu tidak boleh berjalan berbarengan dengan kelas itu.
    /// </summary>
    [Collection("API")]
    public sealed class KunciIdlePreferenceTests : IDisposable
    {
        private readonly string _jalur;

        public KunciIdlePreferenceTests()
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
        public void KunciIdle_SakelarBawaan_AktifDanBisaDimatikan()
        {
            // Bawaan aplikasi: kunci idle aktif begitu fitur ini dirilis.
            Assert.True(AppPreferenceStore.IsKunciIdleAktif());

            AppPreferenceStore.SetKunciIdleAktif(false);
            Assert.False(AppPreferenceStore.IsKunciIdleAktif());

            AppPreferenceStore.SetKunciIdleAktif(true);
            Assert.True(AppPreferenceStore.IsKunciIdleAktif());
        }

        [Fact]
        public void KunciIdle_WaktuBawaan_15Menit()
        {
            Assert.Equal(15, AppPreferenceStore.GetKunciIdleMenit());
        }

        [Fact]
        public void KunciIdle_RentangSah_TersimpanTepat()
        {
            AppPreferenceStore.SetKunciIdleMenit(1);
            Assert.Equal(1, AppPreferenceStore.GetKunciIdleMenit());

            AppPreferenceStore.SetKunciIdleMenit(120);
            Assert.Equal(120, AppPreferenceStore.GetKunciIdleMenit());

            AppPreferenceStore.SetKunciIdleMenit(45);
            Assert.Equal(45, AppPreferenceStore.GetKunciIdleMenit());
        }

        [Fact]
        public void KunciIdle_NilaiDiLuarRentang_JatuhKeBawaan()
        {
            // 0 menit akan mengunci aplikasi seketika — tidak boleh sah.
            AppPreferenceStore.SetKunciIdleMenit(0);
            Assert.Equal(15, AppPreferenceStore.GetKunciIdleMenit());

            AppPreferenceStore.SetKunciIdleMenit(9999);
            Assert.Equal(15, AppPreferenceStore.GetKunciIdleMenit());

            AppPreferenceStore.SetKunciIdleMenit(-5);
            Assert.Equal(15, AppPreferenceStore.GetKunciIdleMenit());
        }

        [Fact]
        public void KunciIdle_PreferensiBerkasRusak_JatuhKeBawaan()
        {
            // Berkas berisi nilai yang tidak sah: angka gagal parse → default;
            // boolean memakai aturan store (nilai selain 1/true = mati, sehingga
            // berkas korup tidak pernah mengunci aplikasi diam-diam).
            Directory.CreateDirectory(Path.GetDirectoryName(_jalur)!);
            File.WriteAllText(_jalur, "kunciIdleMenit=abc\nkunciIdleAktif=maybe\n");

            Assert.Equal(15, AppPreferenceStore.GetKunciIdleMenit());
            Assert.False(AppPreferenceStore.IsKunciIdleAktif());
        }
    }
}
