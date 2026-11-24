using System;
using System.IO;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.ViewModels;
using Xunit;

namespace SuDesApp.Tests.UI
{
    /// <summary>
    /// Pengaturan bahasa tampilan: kode yang disimpan di preferensi pengguna,
    /// pemetaan teks yang ditampilkan, dan baris pilihan di halaman pengaturan.
    /// Keduanya menyentuh penunjuk folder statis proses, jadi dijalankan berurutan.
    /// </summary>
    [Collection("PreferensiBersama")]
    public class BahasaPengaturanTests : IDisposable
    {
        private readonly string _folder = Path.Combine(
            Path.GetTempPath(), "sudes-bahasa-" + Guid.NewGuid().ToString("N"));

        public BahasaPengaturanTests()
        {
            Directory.CreateDirectory(_folder);
            AppPreferenceStore.LokasiOverride = () => Path.Combine(_folder, "prefs.json");
            Lokalisasi.SetCurrent(AppPreferenceStore.GetBahasa());
        }

        public void Dispose()
        {
            AppPreferenceStore.LokasiOverride = null;
            Lokalisasi.SetCurrent("id");

            try { Directory.Delete(_folder, recursive: true); }
            catch { /* sisa berkas uji */ }
        }

        [Fact]
        public void BahasaBawaanIndonesiaDanSimpananBertahan()
        {
            Assert.Equal("id", AppPreferenceStore.GetBahasa());

            AppPreferenceStore.SetBahasa("en");
            Assert.Equal("en", AppPreferenceStore.GetBahasa());
        }

        [Fact]
        public void BahasaTidakDikenalJatuhKeIndonesia()
        {
            AppPreferenceStore.SetBahasa("xx");
            Assert.Equal("id", AppPreferenceStore.GetBahasa());
        }

        [Fact]
        public void TeksBarisBahasaMengikutiBahasaAktif()
        {
            Lokalisasi.SetCurrent("id");
            Assert.Equal("Bahasa", Lokalisasi.Get("Settings.Bahasa.Judul"));
            Assert.Equal("Versi Aplikasi", Lokalisasi.Get("Settings.Versi.Judul"));

            Lokalisasi.SetCurrent("en");
            Assert.Equal("Language", Lokalisasi.Get("Settings.Bahasa.Judul"));
            Assert.Equal("Application Version", Lokalisasi.Get("Settings.Versi.Judul"));
        }

        [Fact]
        public void BarisBahasaMenyalakanPerubahanDanPilihannyaLengkap()
        {
            Lokalisasi.SetCurrent("id");
            string? berubah = null;
            var baris = new SettingSelectionRowVM(
                "Settings.Bahasa.Judul", "Settings.Bahasa.Keterangan", "id")
            {
                Diubah = nilai => berubah = nilai
            };

            Assert.Equal("Bahasa", baris.Judul);
            Assert.Contains(baris.Pilihan, p => p.Kode == "id" && p.Teks == "Indonesia");
            Assert.Contains(baris.Pilihan, p => p.Kode == "en" && p.Teks == "English");

            baris.Nilai = "en";
            Assert.Equal("en", berubah);

            // Pilihan tetap memakai dua bahasa saat bahasa Inggris dipilih.
            Assert.Contains(baris.Pilihan, p => p.Kode == "en");
        }
    }
}
