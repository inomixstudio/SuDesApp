using System;
using System.Collections.Generic;
using System.Linq;
using SuDesApp.Data.Models;
using SuDesApp.Services;
using Xunit;

namespace SuDesApp.Core.Tests
{
    /// <summary>
    /// Uji pemeriksaan keutuhan penomoran untuk tutup buku tahunan. Fokusnya
    /// dua hal: deret wajib mulai lagi dari 001 setiap ganti tahun, dan
    /// penyimpangan (nomor ganda, nomor hilang, tahun yang tidak cocok) harus
    /// ketahuan, bukan lolos diam-diam.
    /// </summary>
    public sealed class VerifikasiPenomoranServiceTests
    {
        private static BarisNomorTerbit Baris(
            string nomor, string tanggal = "2026-03-04", string jenis = "SKD") =>
            new() { NomorSurat = nomor, TanggalSurat = tanggal, NamaJenis = jenis };

        private static HasilVerifikasiNomor Periksa(
            int tahun, params BarisNomorTerbit[] baris) =>
            VerifikasiPenomoranService.Analisis(tahun, baris, new[] { 2026, 2025 });

        // ---------- uraian nomor ----------

        [Theory]
        [InlineData("470/015/Ds/2026", "470", 15, 2026)]
        [InlineData("474.3/006/Ds/2026", "474.3", 6, 2026)]
        [InlineData("474.2/004/Ds/2026", "474.2", 4, 2026)]
        [InlineData("130/003/Ds/2026", "130", 3, 2026)]
        [InlineData("TMPL/012/Ds/2026", "TMPL", 12, 2026)]
        public void UraiNomor_MengenaliSeluruhAwalan(string nomor, string awalan, int urut, int tahun)
        {
            var urai = VerifikasiPenomoranService.UraiNomor(nomor);

            Assert.NotNull(urai);
            Assert.Equal(awalan, urai!.Awalan);
            Assert.Equal(urut, urai.Nomor);
            Assert.Equal(tahun, urai.Tahun);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("470/015")]          // kurang dari tiga segmen
        [InlineData("470/abc/Ds/2026")]  // urut bukan angka
        [InlineData("/015/Ds/2026")]     // awalan kosong
        public void UraiNomor_MenolakNomorTidakTerbaca(string nomor)
        {
            Assert.Null(VerifikasiPenomoranService.UraiNomor(nomor));
        }

        // ---------- deret dalam satu tahun ----------

        [Fact]
        public void DeretTurun_SatuDeretBersama_UrutBerurutan_TanpaMasalah()
        {
            var hasil = Periksa(2026,
                Baris("470/001/Ds/2026", "2026-01-02", "SKD"),
                Baris("470/002/Ds/2026", "2026-01-03", "SKD"),
                Baris("470/003/Ds/2026", "2026-01-04", "KEMATIAN"),
                Baris("470/004/Ds/2026", "2026-01-05", "DOM_WRG"));

            DeretNomor deret = Assert.Single(hasil.Deret);
            Assert.Equal("470", deret.Awalan);
            Assert.Equal(4, deret.Jumlah);
            Assert.Equal(1, deret.NomorPertama);
            Assert.Equal(4, deret.NomorTerakhir);
            Assert.Empty(deret.NomorHilang);
            Assert.True(deret.MulaiDariSatu);
            Assert.False(hasil.AdaMasalahBlocking);
            Assert.Contains("SKD", deret.NamaJenis);
            Assert.Contains("KEMATIAN", deret.NamaJenis);
        }

        [Fact]
        public void DeretTerpisah_TiapAwalanDihitungSendiri()        {
            // Sesuai acuan: 470 satu deret bersama, NTCR 474.3 deret sendiri,
            // Numpang Nikah 474.2 deret sendiri, Rekening Koran 130 deret sendiri.
            var hasil = Periksa(2026,
                Baris("470/001/Ds/2026", "2026-02-01", "SKD"),
                Baris("470/002/Ds/2026", "2026-02-02", "SKD"),
                Baris("474.3/001/Ds/2026", "2026-02-03", "NTCR_N1"),
                Baris("474.2/001/Ds/2026", "2026-02-04", "NTCR_N8"),
                Baris("130/001/Ds/2026", "2026-02-05", "REKKOR"));

            // 5 baris, tapi hanya 4 awalan: 470 dipakai dua kali karena
            // semua surat SKD/NTCR/Rekening dalam satu deret bersama.
            Assert.Equal(4, hasil.Deret.Count);
            Assert.All(hasil.Deret, d =>
            {
                Assert.Equal(1, d.NomorPertama);
                Assert.True(d.MulaiDariSatu);
            });
            Assert.Equal(5, hasil.JumlahSeluruhSurat);
        }

        [Fact]
        public void DeretTidakMulaiDariSatu_Dilaporkan()
        {
            var hasil = Periksa(2026,
                Baris("470/007/Ds/2026", "2026-04-01", "SKD"),
                Baris("470/008/Ds/2026", "2026-04-02", "SKD"));

            DeretNomor deret = Assert.Single(hasil.Deret);
            Assert.Equal(7, deret.NomorPertama);
            Assert.False(deret.MulaiDariSatu);
            Assert.True(hasil.AdaDeretTidakMulaiDariSatu);
            Assert.Contains("bukan 001", deret.Ringkasan);
        }

        [Fact]
        public void NomorHilang_Dilaporkan_SebaagaiPeringatan_Saja()
        {
            var hasil = Periksa(2026,
                Baris("470/001/Ds/2026", "2026-05-01"),
                Baris("470/004/Ds/2026", "2026-05-04"));

            DeretNomor deret = Assert.Single(hasil.Deret);
            Assert.Equal(new[] { 2, 3 }, deret.NomorHilang);
            Assert.True(deret.AdaNomorHilang);
            Assert.False(deret.AdaMasalahBlocking);
            Assert.False(hasil.AdaMasalahBlocking);
            Assert.Contains("peringatan", hasil.Ringkasan);
        }

        [Fact]
        public void NomorGanda_MenutupBuku()
        {
            // Kolom NomorSurat UNIQUE jadi ini harus mustahil lewat UI, tapi
            // kalau muncul berarti ada jalur lain yang menulis register.
            var hasil = Periksa(2026,
                Baris("470/001/Ds/2026", "2026-06-01"),
                Baris("470/001/Ds/2026", "2026-06-02"),
                Baris("470/002/Ds/2026", "2026-06-03"));

            DeretNomor deret = Assert.Single(hasil.Deret);
            Assert.Equal(new[] { 1 }, deret.NomorGanda);
            Assert.Equal(3, deret.Jumlah);
            Assert.True(hasil.AdaMasalahBlocking);
            Assert.True(hasil.AdaNomorGanda);
        }

        [Fact]
        public void NomorTidakTerbaca_MenutupBuku()
        {
            var hasil = Periksa(2026,
                Baris("470/001/Ds/2026", "2026-07-01"),
                Baris("470/abc/Ds/2026", "2026-07-02"));

            Assert.True(hasil.AdaNomorTidakTerbaca);
            Assert.True(hasil.AdaMasalahBlocking);

            DeretNomor deret = hasil.Deret.First(d => d.AdaNomorGanda == false && d.NomorTidakTerbaca.Count > 0);
            Assert.Contains("470/abc/Ds/2026", deret.NomorTidakTerbaca);
        }

        [Fact]
        public void NomorKosong_DihitungSebagaiTidakTerbaca()
        {
            var hasil = Periksa(2026,
                Baris("   ", "2026-08-01"),
                Baris("470/001/Ds/2026", "2026-08-02"));

            Assert.True(hasil.AdaNomorTidakTerbaca);
            DeretNomor deret = hasil.Deret.First(d => d.NomorTidakTerbaca.Count > 0);
            Assert.Contains("(kosong)", deret.NomorTidakTerbaca);
        }

        // ---------- konsistensi tahun ----------

        [Fact]
        public void TahunNomor_Beda_DenganTanggalSurat_MenutupBuku()
        {
            // Nomor 2025 tetapi suratnya bertanggal 2026: deret tahun 2025 akan
            // terlewati saat menghitung, jadi harus ketahuan.
            var hasil = Periksa(2026,
                Baris("470/001/Ds/2025", "2026-01-05"),
                Baris("470/002/Ds/2026", "2026-01-06"));

            Assert.True(hasil.AdaTahunTidakCocok);
            Assert.True(hasil.AdaMasalahBlocking);
            Assert.Contains(hasil.NomorTahunTidakCocok, k => k.Contains("470/001/Ds/2025"));
        }

        [Fact]
        public void SuratTahunLain_TidakDihitung_SebagaiTahunIni()
        {
            // Nomor urut 2026 muncul di tengah deret 2025; ini hasil normal
            // saat deret 2026 sudah berjalan lalu dihitung ulang untuk 2025.
            var hasil = Periksa(2025,
                Baris("470/001/Ds/2025", "2025-12-30"),
                Baris("470/002/Ds/2026", "2026-01-02"));

            Assert.True(hasil.AdaTahunTidakCocok);
            Assert.Equal(1, hasil.JumlahSeluruhSurat);
            Assert.Equal(1, Assert.Single(hasil.Deret).Jumlah);
        }

        [Fact]
        public void TanggalFormatIndonesia_TetapTerbaca()
        {
            var hasil = Periksa(2026,
                Baris("470/001/Ds/2026", "04-03-2026"),
                Baris("470/002/Ds/2026", "17/08/2026"));

            Assert.False(hasil.AdaTahunTidakCocok);
            Assert.Equal(2, hasil.JumlahSeluruhSurat);
        }

        // ---------- reset lintas tahun (inti item 16) ----------

        [Fact]
        public void DeretBaru_SetiapTahun_MulaiDariSatu_Lagi()
        {
            var tahun2025 = Periksa(2025,
                Baris("470/001/Ds/2025", "2025-01-02"),
                Baris("470/002/Ds/2025", "2025-01-03"),
                Baris("470/003/Ds/2025", "2025-12-30"));

            var tahun2026 = Periksa(2026,
                Baris("470/001/Ds/2026", "2026-01-02"),
                Baris("470/002/Ds/2026", "2026-01-03"));

            // Ini yang berlaku sekarang: penerbitan selalu memfilter tahun,
            // sehingga 2026 mulai lagi dari 001 meski 2025 sudah sampai 003.
            Assert.True(tahun2025.Deret[0].MulaiDariSatu);
            Assert.True(tahun2026.Deret[0].MulaiDariSatu);
            Assert.Equal(1, tahun2026.Deret[0].NomorPertama);
            Assert.False(tahun2025.AdaMasalahBlocking);
            Assert.False(tahun2026.AdaMasalahBlocking);
        }

        [Fact]
        public void TahunKosong_TidakDisebutSebagaiMasalah()
        {
            var hasil = Periksa(2026);

            Assert.Empty(hasil.Deret);
            Assert.Equal(0, hasil.JumlahSeluruhSurat);
            Assert.False(hasil.AdaMasalahBlocking);
            Assert.Contains("belum ada surat", hasil.Ringkasan);
        }

        [Fact]
        public void UrutanDeret_DitampilkanTerurut()        {
            var hasil = Periksa(2026,
                Baris("TMPL/001/Ds/2026", "2026-09-01", "TEMPLATE"),
                Baris("130/001/Ds/2026", "2026-09-02", "REKKOR"),
                Baris("470/001/Ds/2026", "2026-09-03", "SKD"),
                Baris("474.3/001/Ds/2026", "2026-09-04", "NTCR_N1"));

            Assert.Equal(
                new[] { "130", "470", "474.3", "TMPL" },
                hasil.Deret.Select(d => d.Awalan).ToArray());
        }

        [Fact]
        public void Ringkasan_MenyebutJumlahSurat()
        {
            var hasil = Periksa(2026,
                Baris("470/001/Ds/2026", "2026-10-01"),
                Baris("470/002/Ds/2026", "2026-10-02"),
                Baris("474.3/001/Ds/2026", "2026-10-03", "NTCR_N1"));

            Assert.Contains("2026: 3 surat", hasil.Ringkasan);
            Assert.Contains("2 deret", hasil.Ringkasan);
            Assert.Contains("rapi", hasil.Ringkasan);
        }
    }
}
