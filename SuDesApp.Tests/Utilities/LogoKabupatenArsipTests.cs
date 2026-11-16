using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using SuDesApp.Utilities;
using Xunit;

namespace SuDesApp.Tests.Utilities
{
    /// <summary>
    /// Kumpulan uji yang menyentuh keadaan STATIS LogoKabupaten (FolderOverride,
    /// JalurArsipOverride, FolderCacheOverride). Uji cetak contoh membaca jalur
    /// lambang lewat static yang sama lewat PengaturanCetak.JalurLogoEfektif,
    /// jadi kedua kelas WAJIB berjalan bergantian — bukan paralel, kalau tidak
    /// uji cetak bisa membaca jalur cache yang baru saja dibuang kelas ini.
    /// </summary>
    [CollectionDefinition("LambangKabupaten")]
    public sealed class KoleksiLambangKabupaten { }

    /// <summary>
    /// Sumber lambang kabupaten kini tiga tingkat: folder pengguna > folder repo >
    /// arsip terbuntel LambangKabupaten.zip (±20 MB, pengganti 514 berkas mentah
    /// ±214 MB). Uji ini memastikan pencarian dari arsip mengembalikan berkas cache
    /// yang benar-benar ada, folder pengguna tetap menang, dan kode yang tidak ada
    /// tetap null — bukan galat.
    /// </summary>
    [Collection("LambangKabupaten")]
    public class LogoKabupatenArsipTests : IDisposable
    {
        private readonly string _folderSementara;
        private readonly string _folderPengguna;
        private readonly string _folderCache;

        public LogoKabupatenArsipTests()
        {
            _folderSementara = Path.Combine(Path.GetTempPath(), "uji-lambang-" + Guid.NewGuid().ToString("N"));
            _folderPengguna = Path.Combine(_folderSementara, "pengguna");
            _folderCache = Path.Combine(_folderSementara, "cache");
            Directory.CreateDirectory(_folderPengguna);
            LogoKabupaten.FolderOverride = _folderPengguna;
            LogoKabupaten.FolderCacheOverride = _folderCache;
        }

        public void Dispose()
        {
            LogoKabupaten.FolderOverride = null;
            LogoKabupaten.JalurArsipOverride = null;
            LogoKabupaten.FolderCacheOverride = null;
            try { Directory.Delete(_folderSementara, recursive: true); } catch (IOException) { /* folder uji di temp — biarkan */ }
        }

        private string BuatArsip(string namaBerkas, byte[] isi)
        {
            string jalur = Path.Combine(_folderSementara!, namaBerkas);
            using var arsip = new ZipArchive(File.Create(jalur), ZipArchiveMode.Create);
            var masuk = arsip.CreateEntry("32.15.png");
            using var penulis = masuk.Open();
            penulis.Write(isi, 0, isi.Length);
            return jalur;
        }

        [Fact]
        public void Cari_DariArsip_MenghasilkanBerkasCacheYangAda()
        {
            byte[] png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };
            string jalurArsip = BuatArsip("lambang.zip", png);
            LogoKabupaten.JalurArsipOverride = jalurArsip;

            string? jalur = LogoKabupaten.Cari("32.15");

            Assert.NotNull(jalur);
            Assert.True(File.Exists(jalur));
            Assert.Equal(png, File.ReadAllBytes(jalur!));
            // Permintaan kedua memakai cache yang sama, bukan berkas baru.
            Assert.Equal(jalur, LogoKabupaten.Cari("32.15"));
        }

        [Fact]
        public void Cari_KodeTidakAdaDiArsip_MengembalikanNull()
        {
            byte[] png = { 0x89, 0x50, 0x4E, 0x47 };
            LogoKabupaten.JalurArsipOverride = BuatArsip("lambang.zip", png);

            Assert.Null(LogoKabupaten.Cari("99.99"));
        }

        [Fact]
        public void Cari_BerkasPengguna_MenangAtasArsip()
        {
            byte[] dariArsip = { 0x89, 0x50, 0x4E, 0x47, 1 };
            LogoKabupaten.JalurArsipOverride = BuatArsip("lambang.zip", dariArsip);

            byte[] milikPengguna = { 0xFF, 0xD8, 0xFF, 0xE0 };
            string jalurPengguna = Path.Combine(_folderPengguna, "32.15.png");
            File.WriteAllBytes(jalurPengguna, milikPengguna);

            Assert.Equal(jalurPengguna, LogoKabupaten.Cari("32.15"));
        }

        [Fact]
        public void SemuaTersedia_MenghitungArsipTanpaMengekstrakSemua()
        {
            byte[] png = { 0x89, 0x50, 0x4E, 0x47 };
            LogoKabupaten.JalurArsipOverride = BuatArsip("lambang.zip", png);

            var semua = LogoKabupaten.SemuaTersedia();

            Assert.True(semua.ContainsKey("32.15"));
            Assert.False(File.Exists(semua["32.15"]), "daftar tidak boleh mengekstrak seluruh arsip");
        }

        [Fact]
        public void Cari_ArsipRusak_MengembalikanNull_BukanGalat()
        {
            string jalurRusak = Path.Combine(_folderSementara, "rusak.zip");
            File.WriteAllBytes(jalurRusak, new byte[] { 1, 2, 3, 4 });
            LogoKabupaten.JalurArsipOverride = jalurRusak;

            Assert.Null(LogoKabupaten.Cari("32.15"));
            Assert.Empty(LogoKabupaten.SemuaTersedia());
        }
    }
}
