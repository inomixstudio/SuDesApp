using System;
using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Services;
using Xunit;

namespace SuDesApp.Tests.Licensing
{
    /// <summary>
    /// Aturan alur uji coba 30 hari: instalasi baru terkunci sampai trial
    /// dimulai, trial membuka menu vital sementara, dan setelah trial habis
    /// menu vital terkunci lagi sampai lisensi dibayar.
    ///
    /// Yang diuji adalah KEPUTUSAN (Core), bukan tampilan: popup dan gerbang
    /// navigasi hanya membaca hasil <see cref="PenjagaLisensi.HitungKeadaan"/>
    /// dan <see cref="PenjagaLisensi.ApakahMenuVital"/>, jadi bila aturan ini
    /// benar, perilaku aplikasi mengikutinya.
    /// </summary>
    [Collection("LisensiBerbagiBerkas")]
    public sealed class PenjagaLisensiTests : IDisposable
    {
        private readonly string _folder;

        public PenjagaLisensiTests()
        {
            _folder = Path.Combine(Path.GetTempPath(),
                "sudesapp-trial-" + Guid.NewGuid().ToString("N"));
            TrialLokal.FolderOverride = _folder;
        }

        public void Dispose()
        {
            TrialLokal.FolderOverride = null;
            try
            {
                if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
            }
            catch
            {
                // Folder sementara yang gagal dihapus tidak menggagalkan uji.
            }
        }

        [Fact]
        public void InstalasiBaru_BelumTrial_Terkunci()
        {
            var keadaan = PenjagaLisensi.HitungKeadaan(
                statusAktif: false, trialAktif: false, trialPernahMulai: false,
                punyaKodeAktivasi: false, kunciTanpaLangganan: false);

            Assert.Equal(KeadaanLisensi.Demo, keadaan);
            Assert.True(PenjagaLisensi.HitungTerkunci(keadaan));
            Assert.Equal("Demo", PenjagaLisensi.LabelKeadaan(keadaan));
        }

        [Fact]
        public void TrialBerjalan_MenuVitalTerbuka()
        {
            var keadaan = PenjagaLisensi.HitungKeadaan(
                statusAktif: false, trialAktif: true, trialPernahMulai: true,
                punyaKodeAktivasi: false, kunciTanpaLangganan: false);

            Assert.Equal(KeadaanLisensi.Trial, keadaan);
            Assert.False(PenjagaLisensi.HitungTerkunci(keadaan));
            Assert.Equal("Trial", PenjagaLisensi.LabelKeadaan(keadaan));
        }

        [Fact]
        public void TrialHabis_TerkunciWalauTidakPernahBayar()
        {
            var keadaan = PenjagaLisensi.HitungKeadaan(
                statusAktif: false, trialAktif: false, trialPernahMulai: true,
                punyaKodeAktivasi: false, kunciTanpaLangganan: false);

            Assert.Equal(KeadaanLisensi.BacaSaja, keadaan);
            Assert.True(PenjagaLisensi.HitungTerkunci(keadaan));
            Assert.Equal("Baca-saja", PenjagaLisensi.LabelKeadaan(keadaan));
        }

        [Fact]
        public void LanggananAktif_MenangAtasSisaTrial()
        {
            var keadaan = PenjagaLisensi.HitungKeadaan(
                statusAktif: true, trialAktif: true, trialPernahMulai: true,
                punyaKodeAktivasi: true, kunciTanpaLangganan: true);

            Assert.Equal(KeadaanLisensi.Berlangganan, keadaan);
            Assert.False(PenjagaLisensi.HitungTerkunci(keadaan));
            Assert.Equal("Full Service", PenjagaLisensi.LabelKeadaan(keadaan));
        }

        [Fact]
        public void KodeAktivasiTanpaPembayaran_TetapTerkunci()
        {
            // Kode aktivasi saja bukan bukti bayar: status tetap harus datang
            // dari token bertanda tangan server. Tanpa status aktif → terkunci,
            // dan popup yang muncul adalah "trial habis/belum aktif", bukan
            // ajakan trial lagi.
            var keadaan = PenjagaLisensi.HitungKeadaan(
                statusAktif: false, trialAktif: false, trialPernahMulai: false,
                punyaKodeAktivasi: true, kunciTanpaLangganan: false);

            Assert.Equal(KeadaanLisensi.BacaSaja, keadaan);
            Assert.True(PenjagaLisensi.HitungTerkunci(keadaan));
        }

        [Theory]
        [InlineData("warga")]
        [InlineData("perangkat-desa")]
        [InlineData("apbdes")]
        [InlineData("daftar-hadir")]
        [InlineData("rekening-koran")]
        [InlineData("template-surat")]
        [InlineData("formulir")]
        [InlineData("pengaturan-surat")]
        [InlineData("surat:SKTM")]
        [InlineData("keputusan:PERDES")]
        [InlineData("template-pdf:DOMISILI")]
        [InlineData("agenda:RAPAT")]
        public void RutePembuatanDanCetak_AdalahMenuVital(string route)
        {
            Assert.True(PenjagaLisensi.ApakahMenuVital(route));
        }

        [Theory]
        [InlineData("beranda")]
        [InlineData("register-surat")]
        [InlineData("riwayat")]
        [InlineData("langganan")]
        [InlineData("cadangkan-pulihkan")]
        [InlineData("pengaturan-aplikasi")]
        [InlineData("")]
        [InlineData(null)]
        public void RuteBacaDanPenyelamatanData_TetapTerbuka(string? route)
        {
            Assert.False(PenjagaLisensi.ApakahMenuVital(route));
        }

        [Fact]
        public void TrialLokal_BerjalanSesuaiDurasi_DanTidakBisaDiulang()
        {
            var trial = new TrialLokal();

            Assert.False(trial.SudahMulai);
            Assert.False(trial.Aktif());

            Assert.True(trial.MulaiSekarang());

            var mulai = Assert.IsType<DateTime>(trial.MulaiUtc);
            Assert.True(trial.Aktif(mulai));
            Assert.True(trial.Aktif(mulai.AddDays(TrialLokal.DURASI_HARI - 1)));
            Assert.False(trial.Aktif(mulai.AddDays(TrialLokal.DURASI_HARI)));
            Assert.Equal(TrialLokal.DURASI_HARI, trial.SisaHari(mulai));
            Assert.Equal(0, trial.SisaHari(mulai.AddDays(TrialLokal.DURASI_HARI + 1)));

            // Trial tidak bisa direset dengan memanggil ulang.
            Assert.False(trial.MulaiSekarang());
        }

        [Fact]
        public void TrialLokal_TersimpanDiDisk_SetelahObjekBaru()
        {
            Assert.True(new TrialLokal().MulaiSekarang());

            var lain = new TrialLokal();
            Assert.True(lain.SudahMulai);
            Assert.NotNull(lain.MulaiUtc);
            Assert.False(lain.MulaiSekarang());
        }

        [Fact]
        public void Penjaga_InstalasiBaru_TerkunciLaluTrialMembukaMenuVital()
        {
            // Tanpa kode aktivasi dan tanpa status: keadaan sebenarnya yang
            // dibaca aplikasi pada instalasi pertama.
            var lisensi = new LisensiManager(NullLogger<LisensiManager>.Instance)
            {
                KodeAktivasi = null
            };
            var penjaga = new PenjagaLisensi(lisensi, new TrialLokal(),
                NullLogger<PenjagaLisensi>.Instance);

            penjaga.Terapkan(null);

            Assert.Equal(KeadaanLisensi.Demo, penjaga.Keadaan);
            Assert.True(penjaga.Terkunci);
            Assert.True(penjaga.BacaSaja);

            bool berubah = false;
            penjaga.Berubah += () => berubah = true;

            Assert.True(penjaga.MulaiTrialLokal());
            Assert.Equal(KeadaanLisensi.Trial, penjaga.Keadaan);
            Assert.False(penjaga.Terkunci);
            Assert.Equal(TrialLokal.DURASI_HARI, penjaga.SisaHariTrial);
            Assert.NotNull(penjaga.TrialBerakhirUtc);
            Assert.True(berubah);
        }

        [Fact]
        public void Penjaga_TrialTidakBisaDimulaiDuaKali()
        {
            var lisensi = new LisensiManager(NullLogger<LisensiManager>.Instance)
            {
                KodeAktivasi = null
            };
            var penjaga = new PenjagaLisensi(lisensi, new TrialLokal(),
                NullLogger<PenjagaLisensi>.Instance);

            Assert.True(penjaga.MulaiTrialLokal());
            Assert.False(penjaga.MulaiTrialLokal());
            Assert.Equal(KeadaanLisensi.Trial, penjaga.Keadaan);
        }

        [Fact]
        public void Penjaga_TrialKedaluwarsa_TerkunciDanTidakBisaDiulang()
        {
            // Instalasi yang trial-nya sudah lewat 30 hari: berkas trial sengaja
            // dimundurkan, persis keadaan "trial habis tanpa pembayaran".
            Directory.CreateDirectory(_folder);
            File.WriteAllText(
                Path.Combine(_folder, "trial-lokal.json"),
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    MulaiUtc = DateTime.UtcNow.AddDays(-(TrialLokal.DURASI_HARI + 1))
                }));

            var lisensi = new LisensiManager(NullLogger<LisensiManager>.Instance)
            {
                KodeAktivasi = null
            };
            var penjaga = new PenjagaLisensi(lisensi, new TrialLokal(),
                NullLogger<PenjagaLisensi>.Instance);

            penjaga.Terapkan(null);

            Assert.True(penjaga.TrialPernahDimulai);
            Assert.False(penjaga.TrialAktif);
            Assert.Equal(0, penjaga.SisaHariTrial);
            Assert.Equal(KeadaanLisensi.BacaSaja, penjaga.Keadaan);
            Assert.True(penjaga.Terkunci);

            // Trial tidak bisa diulang untuk membuka kunci lagi.
            Assert.False(penjaga.MulaiTrialLokal());
            Assert.Equal(KeadaanLisensi.BacaSaja, penjaga.Keadaan);
            Assert.True(penjaga.Terkunci);
        }

        [Theory]
        [InlineData(KeadaanLisensi.Berlangganan, "trial", KeadaanLisensi.Trial)]
        [InlineData(KeadaanLisensi.Berlangganan, "TRIAL", KeadaanLisensi.Trial)]
        [InlineData(KeadaanLisensi.Berlangganan, "tahunan", KeadaanLisensi.Berlangganan)]
        [InlineData(KeadaanLisensi.Berlangganan, null, KeadaanLisensi.Berlangganan)]
        [InlineData(KeadaanLisensi.Demo, "trial", KeadaanLisensi.Demo)]
        [InlineData(KeadaanLisensi.BacaSaja, "trial", KeadaanLisensi.BacaSaja)]
        public void KeadaanTampil_TrialServer_TetapDibacaTrial(
            KeadaanLisensi keadaan, string? paket, KeadaanLisensi harapan)
        {
            // Token trial server sah sebagai langganan, tetapi desa belum membayar:
            // lencana "Full Service" akan menyesatkan.
            Assert.Equal(harapan, PenjagaLisensi.KeadaanTampil(keadaan, paket));
        }

        [Fact]
        public void ChipStatus_MenyebutSisaHariSelamaAda()
        {
            Assert.Equal("Trial • sisa 9 hari",
                PenjagaLisensi.LabelChip(KeadaanLisensi.Trial, 9));
            Assert.Equal("Baca-saja",
                PenjagaLisensi.LabelChip(KeadaanLisensi.BacaSaja, 0));
        }
    }
}
