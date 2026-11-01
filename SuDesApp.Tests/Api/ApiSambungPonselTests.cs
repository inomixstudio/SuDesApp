using SuDesApp.Mobile;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace SuDesApp.Tests.Api
{
    /// <summary>
    /// Uji sambungan ponsel ke desktop: klien <see cref="ApiDesktop"/> dan
    /// <see cref="LayananSuratPonsel"/> dijalankan sungguhan terhadap listener
    /// desktop di localhost. Yang dijaga adalah jahitan yang tidak terlihat dari
    /// kode masing-masing sisi: nama header, bentuk amplop JSON, dan urutan
    /// masuk → tarik pengaturan → keluar.
    /// </summary>
    /// <remarks>
    /// Satu koleksi dengan <see cref="ApiListenerSesiTests"/>: keduanya
    /// mengalihkan lokasi kunci API dan preferensi yang bersifat statis proses,
    /// jadi tidak boleh berjalan bersamaan.
    /// </remarks>
    [Collection("ApiListener")]
    public sealed class ApiSambungPonselTests : IDisposable
    {
        private readonly ApiUjiLingkungan _lingkungan = new();
        private readonly string _folderPonsel;

        public ApiSambungPonselTests()
        {
            _folderPonsel = Path.Combine(_lingkungan.Folder, "ponsel");
        }

        public void Dispose() => _lingkungan.Dispose();

        /// <summary>Layanan baru dari berkas yang sama: meniru aplikasi dibuka ulang.</summary>
        private LayananSuratPonsel LayananBaru() => new(new PenyimpananSurat(_folderPonsel));

        [Fact]
        public async Task Ponsel_masuk_lalu_memakai_pengaturan_desktop()
        {
            var layanan = LayananBaru();
            Assert.False(layanan.TerhubungDesktop);

            var sambungan = await layanan.SambungkanAsync(
                _lingkungan.Alamat,
                ApiUjiLingkungan.Kunci,
                ApiUjiLingkungan.Username,
                ApiUjiLingkungan.KataSandi);

            Assert.True(layanan.TerhubungDesktop);
            Assert.Equal("Budi Santoso", sambungan.Nama);
            Assert.Equal("Administrator", sambungan.Peran);
            Assert.True(sambungan.Berakhir > DateTime.Now);
            Assert.Equal(ApiUjiLingkungan.Kunci, sambungan.KunciApi);

            Assert.Equal("Sukamaju", layanan.Profil.NamaDesa);
            Assert.Equal("Cikarang", layanan.Profil.Kecamatan);
            Assert.Equal("Bekasi", layanan.Profil.Kabupaten);
            Assert.Equal("H. Ahmad Fauzi", layanan.Profil.NamaKepalaDesa);
            Assert.True(layanan.Profil.Lengkap, "Kop surat harus lengkap setelah menarik pengaturan desktop.");
        }

        [Fact]
        public async Task Sambungan_bertahan_setelah_aplikasi_dibuka_ulang()
        {
            var layanan = LayananBaru();
            await layanan.SambungkanAsync(
                _lingkungan.Alamat,
                ApiUjiLingkungan.Kunci,
                ApiUjiLingkungan.Username,
                ApiUjiLingkungan.KataSandi);

            var setelahBukaUlang = LayananBaru();

            Assert.True(setelahBukaUlang.TerhubungDesktop);
            Assert.Equal("Sukamaju", setelahBukaUlang.Profil.NamaDesa);

            // Tarikan ulang memakai token tersimpan, bukan kata sandi.
            var diperbarui = await setelahBukaUlang.SegarkanPengaturanAsync();
            Assert.True(diperbarui.PengaturanDiambil >= setelahBukaUlang.Sambungan!.WaktuMasuk);
        }

        [Fact]
        public async Task Sandi_salah_ditolak_tanpa_meninggalkan_sambungan()
        {
            var layanan = LayananBaru();

            var galat = await Assert.ThrowsAsync<ApiDesktopException>(() => layanan.SambungkanAsync(
                _lingkungan.Alamat,
                ApiUjiLingkungan.Kunci,
                ApiUjiLingkungan.Username,
                "sandi-salah"));

            Assert.Equal("sesi_gagal", galat.Kode);
            Assert.False(layanan.TerhubungDesktop);
            Assert.Empty(layanan.Profil.NamaDesa);
        }

        [Fact]
        public async Task Kunci_api_salah_ditolak_dengan_pesan_kunci()
        {
            var layanan = LayananBaru();

            var galat = await Assert.ThrowsAsync<ApiDesktopException>(() => layanan.SambungkanAsync(
                _lingkungan.Alamat,
                "kunci-yang-bukan",
                ApiUjiLingkungan.Username,
                ApiUjiLingkungan.KataSandi));

            Assert.Equal("api_key_salah", galat.Kode);
            Assert.False(layanan.TerhubungDesktop);
        }

        [Fact]
        public async Task Putus_mencabut_token_sampai_ke_desktop()
        {
            var layanan = LayananBaru();
            var sambungan = await layanan.SambungkanAsync(
                _lingkungan.Alamat,
                ApiUjiLingkungan.Kunci,
                ApiUjiLingkungan.Username,
                ApiUjiLingkungan.KataSandi);

            await layanan.PutuskanAsync();

            Assert.False(layanan.TerhubungDesktop);
            Assert.False(LayananBaru().TerhubungDesktop, "Sambungan yang diputus tidak boleh hidup lagi setelah aplikasi dibuka ulang.");

            // Token lama harus benar-benar mati di desktop, bukan hanya dilupakan ponsel.
            using var jawaban = await _lingkungan.DenganTokenAsync(
                HttpMethod.Get, "/sudes/api/v1/pengaturan", sambungan.Token);

            Assert.Equal(HttpStatusCode.Unauthorized, jawaban.StatusCode);
        }
    }
}
