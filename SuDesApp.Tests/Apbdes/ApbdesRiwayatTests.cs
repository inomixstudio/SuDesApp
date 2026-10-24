using SuDesApp.Services;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.ViewModels;
using System.Linq;
using Xunit;

namespace SuDesApp.Tests.Apbdes
{
    /// <summary>
    /// Logika panel Riwayat APBDES: perbandingan tahun dari arsip berdamping dengan
    /// angka publikasi pemerintah yang diimpor — termasuk berkas publikasi yang
    /// memuat beberapa desa sekaligus.
    /// </summary>
    public class ApbdesRiwayatTests
    {
        private static ApbdesStore.Dokumen Dok(int tahun, string jenis, params (long ang, long real)[] pos)
        {
            var dok = new ApbdesStore.Dokumen { Tahun = tahun, Jenis = jenis };
            foreach (var p in pos)
            {
                dok.Pos.Add(new ApbdesStore.Pos { Kode = "5", Uraian = "x", Anggaran = p.ang, Realisasi = p.real });
            }
            return dok;
        }

        [Fact]
        public void HitungRiwayat_MemilihPerubahanTerbaruSebagaiAcuan()
        {
            var ubah1 = Dok(2024, "APBDes Perubahan", (150, 90));
            ubah1.PerubahanKe = 1;
            var ubah2 = Dok(2024, "APBDes Perubahan", (170, 120));
            ubah2.PerubahanKe = 2;
            var semua = new List<ApbdesStore.Dokumen>
            {
                Dok(2024, "APBDes Awal", (100, 80)),
                ubah1,
                ubah2,
                Dok(2024, ApbdesStore.JenisPublikasi, (175, 125)),
            };

            var riwayat = ApbdesViewModel.HitungRiwayat(semua);

            var baris = Assert.Single(riwayat);
            Assert.Equal(2024, baris.Tahun);
            Assert.Equal("APBDes Perubahan ke-2", baris.AcuanLabel);
            Assert.Equal(170, baris.Anggaran);
            Assert.Equal(120, baris.Realisasi);
            Assert.True(baris.AdaArsip);
            Assert.True(baris.AdaPublikasi);
            Assert.Equal(175, baris.PublikasiAnggaran);
            Assert.Equal(125, baris.PublikasiRealisasi);
            Assert.Equal(-5, baris.Anggaran - baris.PublikasiAnggaran);
        }

        [Fact]
        public void HitungRiwayat_TahunTanpaPublikasi_TetapTampil()
        {
            var semua = new List<ApbdesStore.Dokumen>
            {
                Dok(2023, "APBDes Awal", (100, 0)),
            };

            var riwayat = ApbdesViewModel.HitungRiwayat(semua);

            var baris = Assert.Single(riwayat);
            Assert.True(baris.AdaArsip);
            Assert.False(baris.AdaPublikasi);
            Assert.Equal(0, baris.PublikasiAnggaran);
        }

        [Fact]
        public void HitungRiwayat_ArsipKosong_HanyaPublikasi()
        {
            var semua = new List<ApbdesStore.Dokumen>
            {
                Dok(2022, ApbdesStore.JenisPublikasi, (90, 70)),
            };

            var riwayat = ApbdesViewModel.HitungRiwayat(semua);

            var baris = Assert.Single(riwayat);
            Assert.False(baris.AdaArsip);
            Assert.True(baris.AdaPublikasi);
        }

        [Fact]
        public void RangkaiTahunan_BerkasDuaDesa_DipilihDesaTerbanyak()
        {
            var hasil = new HasilBacaPublikasi();
            hasil.Baris.Add(new BarisPublikasi { Tahun = 2024, NamaDesa = "Desa Lain", AnggaranPendapatan = 1 });
            hasil.Baris.Add(new BarisPublikasi { Tahun = 2024, NamaDesa = "Desa Saya", AnggaranBelanja = 7 });
            hasil.Baris.Add(new BarisPublikasi { Tahun = 2024, NamaDesa = "Desa Saya", RealisasiBelanja = 5 });
            hasil.Baris.Add(new BarisPublikasi { Tahun = 2025, NamaDesa = "Desa Saya", DanaDesa = 500 });

            var tahunan = ApbdesViewModel.RangkaiTahunan(hasil, null, out var catatan);

            Assert.True(catatan.Count > 0);
            Assert.Equal(2, tahunan.Count); // 2024 dan 2025 turun

            var r2024 = tahunan.Single(r => r.Tahun == 2024);
            Assert.Equal("Desa Saya", r2024.NamaDesa);
            Assert.Equal(7, r2024.AngBelanja);
            Assert.Equal(5, r2024.RealBelanja);
            Assert.Null(r2024.AngPendapatan);
            Assert.False(r2024.AngPendapatan.HasValue);

            var r2025 = tahunan.Single(r => r.Tahun == 2025);
            Assert.Equal(500, r2025.DanaDesa);
        }

        [Fact]
        public void RangkaiTahunan_BerkasDuaDesa_PilihanPengaturanSuratMenang()
        {
            var hasil = new HasilBacaPublikasi();
            hasil.Baris.Add(new BarisPublikasi { Tahun = 2024, NamaDesa = "Desa Lain", AnggaranPendapatan = 1 });
            hasil.Baris.Add(new BarisPublikasi { Tahun = 2024, NamaDesa = "Desa Saya", AnggaranBelanja = 7 });

            var tahunan = ApbdesViewModel.RangkaiTahunan(hasil, "Desa lain", out var catatan);

            // Pilihan Pengaturan Surat dipakai meski desa itu daripada berisi
            // lebih sedikit baris; catatan tetap muncul supaya pengguna tahu.
            var r2024 = Assert.Single(tahunan);
            Assert.Equal("Desa lain", r2024.NamaDesa);
            Assert.Equal(1, r2024.AngPendapatan);
            Assert.True(catatan.Count > 0);
        }

        [Fact]
        public void Reader_BacaAngka_MengenalFormatUmumPublikasi()
        {
            Assert.Equal(1234567, ApbdesPublikasiReader.BacaAngka("Rp1.234.567"));
            Assert.Equal(1234567, ApbdesPublikasiReader.BacaAngka("1,234,567")); // gaya AS
            Assert.Equal(1250001, ApbdesPublikasiReader.BacaAngka("1.250.000,50")); // desimal dibulatkan ke atas
            Assert.Equal(-4000, ApbdesPublikasiReader.BacaAngka("(4.000)"));   // kurung = negatif
            Assert.Null(ApbdesPublikasiReader.BacaAngka("-"));
            Assert.Null(ApbdesPublikasiReader.BacaAngka("Tidak ada"));
        }

        [Fact]
        public void Reader_CsvAngkaRibuDanDesimalTerbaca()
        {
            var jalur = Path.Combine(Path.GetTempPath(), "publikasi-uji-" + Guid.NewGuid().ToString("N") + ".csv");
            try
            {
                File.WriteAllLines(jalur, new[]
                {
                    "tahun;nama desa;anggaran pendapatan;realisasi pendapatan;anggaran belanja;dana desa",
                    "2024;Desa Uji;Rp1.250.000,50;1.000.000;1.234.567;-",
                });

                var hasil = ApbdesPublikasiReader.Baca(jalur);

                var baris = Assert.Single(hasil.Baris);
                Assert.Equal(2024, baris.Tahun);
                Assert.Equal("Desa Uji", baris.NamaDesa);
                Assert.Equal(1250001, baris.AnggaranPendapatan); // dibulatkan ke rupiah
                Assert.Equal(1000000, baris.RealisasiPendapatan);
                Assert.Equal(1234567, baris.AnggaranBelanja);
                Assert.Null(baris.DanaDesa);                     // "-" bukan angka
            }
            finally
            {
                File.Delete(jalur);
            }
        }
    }
}
