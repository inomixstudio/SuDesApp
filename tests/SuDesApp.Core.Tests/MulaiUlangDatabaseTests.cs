using System;
using System.Collections.Generic;
using System.IO;
using SuDesApp.Utilities;
using Xunit;

namespace SuDesApp.Core.Tests
{
    /// <summary>
    /// Tombol "Simpan &amp; Mulai Ulang Sekarang" (Pengaturan Aplikasi → Database Desa):
    /// setelah pilihan lokasi database disimpan, aplikasi ditutup rapi lalu dibuka
    /// kembali supaya lokasi baru langsung dipakai. Uji di sini bagian yang bisa
    /// diuji tanpa UI: penanda "lahir dari mulai ulang" di preferensi, dan
    /// penjadwalan proses baru yang menunggu proses lama selesai.
    ///
    /// Berkas preferensi ditunjuk lewat <c>AppPreferenceStore.LokasiOverride</c>
    /// (koleksi "API" dipakai bersama karena override-nya statis, satu proses).
    /// </summary>
    [Collection("API")]
    public sealed class MulaiUlangDatabaseTests : IDisposable
    {
        private readonly string _folder;
        private readonly string _jalurPreferensi;

        public MulaiUlangDatabaseTests()
        {
            _folder = Path.Combine(Path.GetTempPath(), "uji-mulai-ulang-" + Guid.NewGuid().ToString("N"));
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

        [Fact]
        public void Penanda_TurunNaikDiPreferensi()
        {
            Assert.False(MulaiUlangAplikasi.AdanyaPenandaMulaiUlang);

            MulaiUlangAplikasi.PasangPenanda();
            Assert.True(MulaiUlangAplikasi.AdanyaPenandaMulaiUlang);

            MulaiUlangAplikasi.BersihkanPenanda();
            Assert.False(MulaiUlangAplikasi.AdanyaPenandaMulaiUlang);
        }

        [Fact]
        public void Penanda_BerkasPreferensiTidakAda_TetapFalse()
        {
            // Preferensi kosong (pemasangan baru): tidak boleh lempar, cukup false.
            Assert.False(MulaiUlangAplikasi.AdanyaPenandaMulaiUlang);
        }

        [Fact]
        public void ArgumenPenjadwal_MemuatSkripTerkodeDenganBenderaAman()
        {
            var argumen = MulaiUlangAplikasi.SusunArgumenPenjadwal("Write-Host uji");

            // PowerShell tersembunyi, tanpa profil, tanpa interaksi — dan skripnya
            // disandikan Base64 (bukan teks polos) agar aman dari spasi/kutip.
            Assert.Contains("-NoProfile", argumen);
            Assert.Contains("-NonInteractive", argumen);
            Assert.Contains("-WindowStyle Hidden", argumen);
            Assert.Contains("-EncodedCommand ", argumen);

            var terkode = argumen[(argumen.IndexOf("-EncodedCommand ", StringComparison.Ordinal)
                + "-EncodedCommand ".Length)..];
            Assert.Equal("Write-Host uji",
                System.Text.Encoding.Unicode.GetString(Convert.FromBase64String(terkode)));
        }

        [Fact]
        public void JalurProsesSaatIni_BerisiDotnetTestHost()
        {
            var jalur = MulaiUlangAplikasi.JalurProsesSaatIni();
            Assert.False(string.IsNullOrWhiteSpace(jalur));
            Assert.True(File.Exists(jalur));
            // Di runner test, "exe" yang berjalan adalah host uji — bukan aplikasi.
            Assert.Contains("testhost", jalur!, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SkripPenjadwal_TungguProsesLamaLaluJalankanUlang()
        {
            var skrip = MulaiUlangAplikasi.SusunSkripPenjadwal(1234, "D:\\Aplikasi\\SuDesApp.exe", 120);

            // Tunggu proses lama dengan batas waktu (diam bila sudah mati duluan),
            // lalu jalankan exe yang sama.
            Assert.Contains("Wait-Process -Id 1234 -Timeout 120", skrip);
            Assert.Contains("Start-Process -FilePath 'D:\\Aplikasi\\SuDesApp.exe'", skrip);
        }

        [Fact]
        public void SkripPenjadwal_JalurBerisiKutipTetapUtuh()
        {
            var skrip = MulaiUlangAplikasi.SusunSkripPenjadwal(1, "D:\\Dat'a Desa\\app.exe", 60);
            Assert.Contains("'D:\\Dat''a Desa\\app.exe'", skrip);
        }

        [Fact]
        public void Penanda_TahanNilaiSetelahBanyakTulisUlang()
        {
            // Preferensi ditulis baris-per-baris; pastikan penanda tidak rusak
            // bila berkasnya sudah berisi banyak kunci lain (mis. pilihan database).
            AppPreferenceStore.SetJalurDatabase("D:\\Data Desa\\desa.db");
            for (int i = 0; i < 5; i++)
            {
                MulaiUlangAplikasi.PasangPenanda();
                MulaiUlangAplikasi.BersihkanPenanda();
            }
            MulaiUlangAplikasi.PasangPenanda();

            Assert.True(MulaiUlangAplikasi.AdanyaPenandaMulaiUlang);
            Assert.Equal("D:\\Data Desa\\desa.db", AppPreferenceStore.GetJalurDatabase());
        }
    }
}
