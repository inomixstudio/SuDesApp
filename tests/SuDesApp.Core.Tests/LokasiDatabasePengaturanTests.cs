using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Configuration;
using SuDesApp.Utilities;
using Xunit;

namespace SuDesApp.Core.Tests
{
    /// <summary>
    /// Lokasi berkas database desa yang dipilih pengguna (Pengaturan Aplikasi →
    /// Database Desa): nilai preferensinya dan penerapannya oleh <see cref="AppConfig"/>
    /// saat aplikasi dibuka.
    ///
    /// Berkas preferensi ditunjuk lewat <c>AppPreferenceStore.LokasiOverride</c>
    /// seperti KunciIdlePreferenceTests/ApiServiceTests, sehingga berkas preferensi
    /// profil user asli di %LOCALAPPDATA% tidak pernah tersentuh. Koleksi "API"
    /// dipakai bersama karena LokasiOverride statis (satu proses).
    /// </summary>
    [Collection("API")]
    public sealed class LokasiDatabasePengaturanTests : IDisposable
    {
        private readonly string _folder;
        private readonly string _jalurPreferensi;

        public LokasiDatabasePengaturanTests()
        {
            _folder = Path.Combine(Path.GetTempPath(), "uji-lokasi-db-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);

            _jalurPreferensi = Path.Combine(_folder, "login_prefs.json");
            AppPreferenceStore.LokasiOverride = () => _jalurPreferensi;
        }

        public void Dispose()
        {
            AppPreferenceStore.LokasiOverride = null;
            try { Directory.Delete(_folder, recursive: true); }
            catch { /* sementara — biarkan OS membersihkan */ }
        }

        private static AppConfig BuatConfig(string connectionString) =>
            new(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AppConfig:databaseConnectionString"] = connectionString
                })
                .Build());

        [Fact]
        public void Preferensi_KosongAtauDikosongkan_KembaliKeNull()
        {
            // Belum pernah diatur.
            Assert.Null(AppPreferenceStore.GetJalurDatabase());

            AppPreferenceStore.SetJalurDatabase(Path.Combine(_folder, "desa-desa.db"));
            Assert.Equal(Path.Combine(_folder, "desa-desa.db"), AppPreferenceStore.GetJalurDatabase());

            // Dikosongkan = kembali memakai lokasi bawaan (bukan tersimpan sebagai spasi).
            AppPreferenceStore.SetJalurDatabase(null);
            Assert.Null(AppPreferenceStore.GetJalurDatabase());

            AppPreferenceStore.SetJalurDatabase("   ");
            Assert.Null(AppPreferenceStore.GetJalurDatabase());
        }

        [Fact]
        public void AppConfig_TanpaPilihan_MemakaiLokasiBawaan()
        {
            var bawaan = Path.Combine(_folder, "desa-bawaan.db");

            var config = BuatConfig($"Data Source={bawaan}");

            Assert.False(config.DatabaseKustom);
            Assert.Equal(bawaan, config.DatabasePath);
            Assert.Equal(bawaan, config.DatabasePathBawaan);
            Assert.Equal($"Data Source={bawaan}", config.DatabaseConnectionString);
        }

        [Fact]
        public void AppConfig_AdaPilihan_MemakaiLokasiPenggunaDanMembuatFoldernya()
        {
            var bawaan = Path.Combine(_folder, "desa-bawaan.db");
            var folderData = Path.Combine(_folder, "data-desa", "arsip");
            var pilihan = Path.Combine(folderData, "desa.db");

            AppPreferenceStore.SetJalurDatabase(pilihan);
            var config = BuatConfig($"Data Source={bawaan}");

            Assert.True(config.DatabaseKustom);
            Assert.Equal(pilihan, config.DatabasePath);
            Assert.Equal(bawaan, config.DatabasePathBawaan); // asal-usul tetap terlacak
            Assert.Equal($"Data Source={pilihan}", config.DatabaseConnectionString);

            // Folder tujuan disiapkan lebih dahulu supaya berkas baru bisa dibuat di sana.
            Assert.True(Directory.Exists(folderData));
        }

        [Fact]
        public void AppConfig_PilihanSamaDenganBawaan_BukanPilihanKustom()
        {
            var bawaan = Path.Combine(_folder, "desa-bawaan.db");

            AppPreferenceStore.SetJalurDatabase(bawaan);
            var config = BuatConfig($"Data Source={bawaan}");

            // Menunjuk berkas yang sama bukan pemindahan lokasi — jangan ditandai
            // kustom supaya pesan “memakai lokasi bawaan” tetap benar.
            Assert.False(config.DatabaseKustom);
            Assert.Equal(bawaan, config.DatabasePath);
        }

        [Fact]
        public void AppConfig_KoneksiMemori_TidakPernahDitimpaPilihanPengguna()
        {
            AppPreferenceStore.SetJalurDatabase(Path.Combine(_folder, "desa-pilihan.db"));

            var config = BuatConfig("Data Source=:memory:");

            // Sumber non-berkas tidak punya “lokasi” untuk dipindahkan: pengujian
            // dan database sementara harus tetap memakai :memory: apa pun preferensinya.
            Assert.False(config.DatabaseKustom);
            Assert.Contains(":memory:", config.DatabaseConnectionString);
        }

        [Fact]
        public void AppConfig_FolderTujuanGagalDibuat_KembaliKeBawaan()
        {
            var bawaan = Path.Combine(_folder, "desa-bawaan.db");

            // Kasus nyata: lokasi pilihan menunjuk "folder" yang ternyata sebuah berkas,
            // jadi direktorinya tidak mungkin dibuat. Aplikasi harus tetap memakai
            // lokasi bawaan dan JANGAN menandai lokasi itu sebagai pilihan yang berlaku —
            // tanpa itu aplikasi gagal membuka database setiap kali dibuka.
            var berkasPenghalang = Path.Combine(_folder, "blokir");
            File.WriteAllText(berkasPenghalang, "bukan folder");

            AppPreferenceStore.SetJalurDatabase(Path.Combine(berkasPenghalang, "desa.db"));
            var config = BuatConfig($"Data Source={bawaan}");

            Assert.False(config.DatabaseKustom);
            Assert.Equal(bawaan, config.DatabasePath);
            Assert.Equal($"Data Source={bawaan}", config.DatabaseConnectionString);
        }

        [Fact]
        public void AppConfig_PilihanTidakSah_KembaliKeBawaanTanpaGagal()
        {
            var bawaan = Path.Combine(_folder, "desa-bawaan.db");

            // Karakter NUL tidak sah untuk jalur berkas — preferensi rusak seperti itu
            // tidak boleh menggagalkan startup aplikasi.
            AppPreferenceStore.SetJalurDatabase("desa\u0000rusak.db");
            var config = BuatConfig($"Data Source={bawaan}");

            Assert.False(config.DatabaseKustom);
            Assert.Equal(bawaan, config.DatabasePath);
        }
    }
}
