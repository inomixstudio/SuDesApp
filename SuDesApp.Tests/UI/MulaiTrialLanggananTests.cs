using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.ViewModels;
using Xunit;

namespace SuDesApp.Tests.UI
{
    /// <summary>
    /// Tombol **Mulai Trial 30 Hari** di halaman Langganan tidak boleh menuntut
    /// kode aktivasi: popup saat aplikasi dibuka bisa, halaman ini juga harus
    /// bisa. Yang diuji adalah tombolnya sendiri (perintah + tampilan), bukan
    /// hanya aturan di Core — jalur inilah yang diklik pengguna.
    ///
    /// Server dibuat "mati" (HttpClient selalu gagal) supaya terbukti tombolnya
    /// tetap bekerja offline lewat trial lokal.
    /// </summary>
    [Collection("LisensiBerbagiBerkas")]
    public sealed class MulaiTrialLanggananTests : IDisposable
    {
        private readonly string _folder;

        public MulaiTrialLanggananTests()
        {
            _folder = Path.Combine(Path.GetTempPath(),
                "sudesapp-langganan-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);

            TrialLokal.FolderOverride = _folder;
            LisensiManager.FolderCacheOverride = _folder;
            AppPreferenceStore.LokasiOverride = () => Path.Combine(_folder, "prefs.json");
        }

        public void Dispose()
        {
            TrialLokal.FolderOverride = null;
            LisensiManager.FolderCacheOverride = null;
            AppPreferenceStore.LokasiOverride = null;

            try
            {
                if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
            }
            catch
            {
                // Folder sementara yang gagal dihapus tidak menggagalkan uji.
            }
        }

        /// <summary>Server yang tidak pernah menjawab — keadaan "offline".</summary>
        private sealed class ServerMati : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
                => throw new HttpRequestException("server tidak terjangkau (uji)");
        }

        [Fact]
        public async Task Tombol_trial_halaman_langganan_bekerja_tanpa_kode()
        {
            var lisensi = new LisensiManager(
                NullLogger<LisensiManager>.Instance, new HttpClient(new ServerMati()))
            {
                UrlServer = "http://127.0.0.1:59999",
                KodeAktivasi = null
            };
            var klien = new LanggananKlien(
                lisensi, new HttpClient(new ServerMati()), NullLogger<LanggananKlien>.Instance);
            var penjaga = new PenjagaLisensi(
                lisensi, new TrialLokal(), NullLogger<PenjagaLisensi>.Instance, klien);

            var vm = new LanggananViewModel(
                NullLogger<LanggananViewModel>.Instance, lisensi, penjaga, klien,
                new SambunganServerLisensi());

            // Keadaan awal instalasi baru: tombol tampil, kode kosong.
            Assert.True(vm.TampilTombolTrial);
            Assert.True(string.IsNullOrEmpty(vm.KodeAktivasi));
            Assert.True(penjaga.Terkunci);

            // Menekan tombol — persis yang dilakukan pengguna.
            vm.MulaiTrialCommand.Execute(null);

            // Perintahnya async-void (pola ICommand); tunggu sampai keadaannya
            // berubah, bukan menebak lama waktunya.
            var batas = DateTime.UtcNow.AddSeconds(5);
            while (vm.TampilTombolTrial && DateTime.UtcNow < batas)
            {
                await Task.Delay(25);
            }

            Assert.False(vm.TampilTombolTrial);

            // Mode trial aktif = tombol "Bayar / Berlangganan" langsung tampil di
            // kartu status (jalan keluar ke Full Service tanpa mencari di kartu paket).
            Assert.True(vm.SedangTrial);
            Assert.Equal(KeadaanLisensi.Trial, penjaga.Keadaan);
            Assert.False(penjaga.Terkunci);
            Assert.True(new TrialLokal().SudahMulai);
            Assert.Equal("Trial 30 hari aktif", vm.LanggananStatus);
            Assert.StartsWith("Trial", vm.KeadaanLabel, StringComparison.Ordinal);
        }

        [Fact]
        public void Tombol_qris_membuka_panel_bahkan_kode_kosong()
        {
            // Laporan pengguna: menekan "Tampilkan QRIS" hanya memunculkan
            // "Isi kode aktivasi terlebih dahulu" tanpa sistem pun QR-nya.
            // QR adalah isi tombolnya — panel harus terbuka, dan pengingat
            // kode boleh berbunyi di kalimat pengarah yang sama.
            var lisensi = new LisensiManager(
                NullLogger<LisensiManager>.Instance, new HttpClient(new ServerMati()))
            {
                UrlServer = "http://127.0.0.1:59999",
                KodeAktivasi = null
            };
            var klien = new LanggananKlien(
                lisensi, new HttpClient(new ServerMati()), NullLogger<LanggananKlien>.Instance);
            var penjaga = new PenjagaLisensi(
                lisensi, new TrialLokal(), NullLogger<PenjagaLisensi>.Instance, klien);

            var vm = new LanggananViewModel(
                NullLogger<LanggananViewModel>.Instance, lisensi, penjaga, klien,
                new SambunganServerLisensi());

            Assert.True(string.IsNullOrEmpty(vm.KodeAktivasi));
            Assert.False(vm.TampilQris);

            vm.TampilkanQrisCommand.Execute(null);

            // QR/panel terbuka — bukan hanya banner penolakan.
            Assert.True(vm.TampilQris);

            // Pengingat kode tetap berbunyi, dan petunjuk scan tidak hilang
            // karena ditimpa pesan kedua.
            Assert.StartsWith("Isi kode aktivasi", vm.LanggananInfo, StringComparison.Ordinal);
            Assert.Contains("Scan QRIS", vm.LanggananInfo, StringComparison.Ordinal);
        }
    }
}
