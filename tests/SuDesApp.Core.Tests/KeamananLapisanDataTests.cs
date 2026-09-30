using System;
using System.Linq;
using System.Threading.Tasks;
using SuDesApp.Api;
using SuDesApp.Utilities;
using Xunit;

namespace SuDesApp.Core.Tests
{
    /// <summary>
    /// Uji guard izin lapisan data (SessionContext.Wajib) dan kunci API per
    /// cakupan: default nonaktif (jalur lama utuh), SesiSistem per-aliran
    /// (AsyncLocal), kompatibilitas kunci lama, dan pengecekan cakupan.
    ///
    /// Tes yang menyentuh berkas kunci API wajib memasang
    /// <c>ApiKunci.LokasiOverride</c> — tanpa itu, DPAPI CurrentUser akan
    /// membaca/menimpa berkas ApiKunci.bin profil user yang asli.
    ///
    /// Koleksi "API" WAJIB: static <c>ApiKunci.LokasiOverride</c> juga ditulis
    /// ApiListenerEndToEndTests/ApiServiceTests, dan xunit menjalankan kelas di
    /// luar koleksi yang sama secara paralel — tanpa atribut ini dua kelas
    /// saling menimpa override sehingga listener kadang membaca kunci dari
    /// folder tes yang salah (401 intermiten).
    /// </summary>
    [Collection("API")]
    public sealed class KeamananLapisanDataTests
    {
        // =====================================================================
        // Guard izin lapisan data
        // =====================================================================

        [Fact]
        public void Wajib_NonaktifBawaan_JalurLamaTetapJalan()
        {
            Assert.False(SessionContext.PenegakanAktif);
            // Bila penegakan mati, peran APA PUN lolos — inilah yang menjaga
            // seluruh 402 tes lama dan jalur sistem tetap berjalan.
            SessionContext.Set("operator-kecil", "manual", "OPERATOR", "Op Kecil");
            var izin = SuDesApp.Data.Models.IzinAplikasi.KelolaPengguna;
            Assert.Null(Record.Exception(() => SessionContext.Wajib(izin)));
        }

        [Fact]
        public void Wajib_Aktif_PeranTerbatasDitolak_AdministratorLolos()
        {
            SessionContext.SetPenegakanAktif(true);
            try
            {
                SessionContext.Set("op1", "manual", "OPERATOR", "Operator 1");
                var ex = Record.Exception(() =>
                    SessionContext.Wajib(SuDesApp.Data.Models.IzinAplikasi.KelolaPengguna));
                Assert.IsType<IzinDitolakException>(ex);
                Assert.Equal(
                    SuDesApp.Data.Models.IzinAplikasi.KelolaPengguna,
                    ((IzinDitolakException)ex!).Izin);

                SessionContext.Set("admin1", "manual", "ADMINISTRATOR", "Admin 1");
                Assert.Null(Record.Exception(() =>
                    SessionContext.Wajib(SuDesApp.Data.Models.IzinAplikasi.KelolaPengguna)));
            }
            finally
            {
                SessionContext.SetPenegakanAktif(false);
                SessionContext.Set("admin", "manual");
            }
        }

        [Fact]
        public async Task SesiSistem_MelewatiGuard_HanyaPadaAliranItu()
        {
            SessionContext.SetPenegakanAktif(true);
            try
            {
                SessionContext.Set("op1", "manual", "OPERATOR", "Operator 1");
                var izin = SuDesApp.Data.Models.IzinAplikasi.KelolaPengguna;

                // Di dalam blok: lewat. Di luar blok: tetap ditolak.
                using (SessionContext.SesiSistem())
                {
                    Assert.Null(Record.Exception(() => SessionContext.Wajib(izin)));
                }
                Assert.IsType<IzinDitolakException>(
                    Record.Exception(() => SessionContext.Wajib(izin)));

                // Sesi sistem tidak bocor ke pekerjaan latar yang berjalan bersamaan
                // (AsyncLocal per-aliran, bukan sakelar global).
                var exLatar = await Record.ExceptionAsync(() =>
                    Task.Run(() => SessionContext.Wajib(izin)));
                Assert.IsType<IzinDitolakException>(exLatar);
            }
            finally
            {
                SessionContext.SetPenegakanAktif(false);
                SessionContext.Set("admin", "manual");
            }
        }

        [Fact]
        public void SesiSistem_Bersarang_PulihKeKeadaanLuar()
        {
            using (SessionContext.SesiSistem())
            using (SessionContext.SesiSistem())
            {
                // dua blok bersarang — keduanya menandai aliran sebagai sesi sistem
            }
            // setelah kedua Dispose: AsyncLocal kembali ke null (bukan true menempel)
            SessionContext.SetPenegakanAktif(true);
            try
            {
                SessionContext.Set("op1", "manual", "OPERATOR", "Operator 1");
                Assert.IsType<IzinDitolakException>(Record.Exception(() =>
                    SessionContext.Wajib(SuDesApp.Data.Models.IzinAplikasi.KelolaPengguna)));
            }
            finally
            {
                SessionContext.SetPenegakanAktif(false);
                SessionContext.Set("admin", "manual");
            }
        }

        // =====================================================================
        // Kunci API per cakupan
        // =====================================================================

        [Fact]
        public void CariCocok_KunciUtamaDanTambahan_SemuaDipertimbangkan()
        {
            var tersimpan = new[]
            {
                new ApiKunciEntry { Nilai = "kunci-utama", Cakupan = ApiCakupan.Penuh },
                new ApiKunciEntry { Nilai = "kunci-agregat", Cakupan = ApiCakupan.Agregat, Nama = "Dashboard" },
            };

            Assert.NotNull(ApiAutentikasi.CariCocok("kunci-utama", tersimpan));
            Assert.NotNull(ApiAutentikasi.CariCocok("kunci-agregat", tersimpan));
            Assert.Null(ApiAutentikasi.CariCocok("kunci-palsu", tersimpan));
            Assert.Null(ApiAutentikasi.CariCocok(null, tersimpan));
            Assert.Null(ApiAutentikasi.CariCocok("kunci-utama", Array.Empty<ApiKunciEntry>()));
        }

        [Fact]
        public void CakupanCukup_MatriksEndpointVsKunci()
        {
            var endpointAgregat = new ApiEndpoint { Kategori = ApiKategori.Agregat };
            var endpointPerangkat = new ApiEndpoint { Kategori = ApiKategori.Perangkat };
            var endpointPermintaan = new ApiEndpoint { Kategori = ApiKategori.Permintaan };

            // Penuh membuka semua.
            Assert.True(ApiAutentikasi.CakupanCukup(ApiCakupan.Penuh, endpointAgregat));
            Assert.True(ApiAutentikasi.CakupanCukup(ApiCakupan.Penuh, endpointPerangkat));
            Assert.True(ApiAutentikasi.CakupanCukup(ApiCakupan.Penuh, endpointPermintaan));

            // Agregat: baca-saja agregat + perangkat; TIDAK permintaan.
            Assert.True(ApiAutentikasi.CakupanCukup(ApiCakupan.Agregat, endpointAgregat));
            Assert.True(ApiAutentikasi.CakupanCukup(ApiCakupan.Agregat, endpointPerangkat));
            Assert.False(ApiAutentikasi.CakupanCukup(ApiCakupan.Agregat, endpointPermintaan));

            // Permintaan: hanya alur permintaan.
            Assert.False(ApiAutentikasi.CakupanCukup(ApiCakupan.Permintaan, endpointAgregat));
            Assert.False(ApiAutentikasi.CakupanCukup(ApiCakupan.Permintaan, endpointPerangkat));
            Assert.True(ApiAutentikasi.CakupanCukup(ApiCakupan.Permintaan, endpointPermintaan));
        }

        [Fact]
        public void CakupanCukup_KategoriLain_TidakTercakupKunciSempit()
        {
            // Kategori Verifikasi (endpoint publik) tidak perlu kunci; bila
            // dicek pun, kunci sempit tidak mengakuinya.
            var endpointVerifikasi = new ApiEndpoint { Kategori = ApiKategori.Verifikasi };
            Assert.False(ApiAutentikasi.CakupanCukup(ApiCakupan.Agregat, endpointVerifikasi));
            Assert.False(ApiAutentikasi.CakupanCukup(ApiCakupan.Permintaan, endpointVerifikasi));
            Assert.True(ApiAutentikasi.CakupanCukup(ApiCakupan.Penuh, endpointVerifikasi));
        }

        [Fact]
        public void MuatSemua_BerkasLama_TeksKunciTunggal_DibacaSebagaiUtama()
        {
            var jalur = JalurSementara();
            ApiKunci.LokasiOverride = () => jalur;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(jalur)!);
                File.WriteAllBytes(jalur,
                    System.Security.Cryptography.ProtectedData.Protect(
                        System.Text.Encoding.UTF8.GetBytes("kunci-lama-polos"),
                        null, System.Security.Cryptography.DataProtectionScope.CurrentUser));

                var semua = ApiKunci.MuatSemua();
                var entry = Assert.Single(semua);
                Assert.Equal("kunci-lama-polos", entry.Nilai);
                Assert.Equal(ApiCakupan.Penuh, entry.Cakupan);
            }
            finally
            {
                ApiKunci.LokasiOverride = null;
                Directory.Delete(Path.GetDirectoryName(jalur)!, recursive: true);
            }
        }

        [Fact]
        public void Simpan_BuatTambahan_Hapus_PutaranPenuh()
        {
            var jalur = JalurSementara();
            ApiKunci.LokasiOverride = () => jalur;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(jalur)!);
                File.WriteAllBytes(jalur,
                    System.Security.Cryptography.ProtectedData.Protect(
                        System.Text.Encoding.UTF8.GetBytes("kunci-utama-lama"),
                        null, System.Security.Cryptography.DataProtectionScope.CurrentUser));

                // Kunci tambahan agregat.
                var tambahan = ApiKunci.BuatTambahan(ApiCakupan.Agregat, "Dashboard Kecamatan");
                Assert.NotEqual("kunci-utama-lama", tambahan.Nilai);
                Assert.Equal(ApiCakupan.Agregat, tambahan.Cakupan);

                var semua = ApiKunci.MuatSemua();
                Assert.Equal(2, semua.Count);
                Assert.Equal(ApiCakupan.Penuh, semua[0].Cakupan);
                Assert.Contains(semua, k => k.Cakupan == ApiCakupan.Agregat && k.Nama == "Dashboard Kecamatan");

                // Regenerasi kunci UTAMA tidak mengusik kunci tambahan.
                ApiKunci.Simpan("kunci-utama-baru");
                semua = ApiKunci.MuatSemua();
                Assert.Equal(2, semua.Count);
                Assert.Equal("kunci-utama-baru", semua.First(k => k.Cakupan == ApiCakupan.Penuh).Nilai);
                Assert.Contains(semua, k => k.Cakupan == ApiCakupan.Agregat);

                // Hapus kunci tambahan berdasar nilai.
                Assert.True(ApiKunci.HapusTambahan(tambahan.Nilai));
                Assert.False(ApiKunci.HapusTambahan(tambahan.Nilai));
                Assert.Single(ApiKunci.MuatSemua());
            }
            finally
            {
                ApiKunci.LokasiOverride = null;
                Directory.Delete(Path.GetDirectoryName(jalur)!, recursive: true);
            }
        }

        [Fact]
        public void BuatTambahan_CakupanPenuh_Ditolak()
        {
            Assert.Throws<ArgumentException>(() => ApiKunci.BuatTambahan(ApiCakupan.Penuh, "x"));
        }

        private static string JalurSementara() => System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "sudes-test",
            Guid.NewGuid().ToString("N"), "ApiKunci.bin");
    }
}
