using SuDesApp.Data.Models;
using Xunit;

namespace SuDesApp.Core.Tests
{
    /// <summary>
    /// Penjaga panel "SK per Jabatan" pada halaman Data Perangkat Desa:
    /// permintaan pemelihara (30 September 2026) adalah akses SK dipisah per
    /// JABATAN — Kepala Desa, Sekretaris Desa, Kaur Keuangan, Kaur Perencanaan,
    /// Kaur Tata Usaha dan Umum, Kasi Pelayanan, Kasi Pemerintahan, Kasi
    /// Kesejahteraan, Operator Desa, Staf, dan Jabatan Lainnya — dengan tombol
    /// aksi per jabatan yang sekaligus membuka SK siap cetak/pratinjau.
    /// Pengecualian: SK Kepala Desa diterbitkan Bupati (SKD), jadi kartunya
    /// membuka mode arsip berkas PDF, bukan penerbitan dokumen.
    /// </summary>
    public sealed class PerangkatSkPerJabatanTests
    {
        private static PerangkatDesa Orang(string jabatan, string status = StatusPerangkat.Aktif, string? berkas = null) =>
            new()
            {
                Nama = "Orang " + jabatan,
                Jabatan = jabatan,
                Status = status,
                BerkasSK = berkas
            };

        // ---------- susunan panel ----------

        [Fact]
        public void Panel_MenyusunSebelasJabatanSesuaiPermintaanPemelihara()
        {
            var kartu = SkJabatanAksiKatalog.Susun(Array.Empty<PerangkatDesa>());

            var diharapkan = new[]
            {
                JabatanPerangkat.KepalaDesa,
                JabatanPerangkat.SekretarisDesa,
                JabatanPerangkat.KaurKeuangan,
                JabatanPerangkat.KaurPerencanaan,
                JabatanPerangkat.KaurTataUsaha,
                JabatanPerangkat.KasiPelayanan,
                JabatanPerangkat.KasiPemerintahan,
                JabatanPerangkat.KasiKesejahteraan,
                JabatanPerangkat.OperatorDesa,
                JabatanPerangkat.Staf,
                JabatanPerangkat.Lainnya
            };

            Assert.Equal(diharapkan, kartu.Select(k => k.NamaJabatan).ToList());
            Assert.All(kartu, k => Assert.True(JabatanPerangkat.Valid(k.NamaJabatan)));
        }

        [Fact]
        public void Panel_KartuDibuatJugaBilaBelumAdaOrangnya()
        {
            // Pemasangan baru: operator tetap harus bisa membuka panel SK per jabatan.
            var kartu = SkJabatanAksiKatalog.Susun(Array.Empty<PerangkatDesa>());

            Assert.Equal(11, kartu.Count);
            Assert.All(kartu, k =>
            {
                Assert.Equal(0, k.JumlahOrang);
                Assert.Equal("Belum ada orang", k.Ringkas);
            });
        }

        [Fact]
        public void Panel_MenghitungOrangStatusDanArsipDariDataPerangkat()
        {
            var kartu = SkJabatanAksiKatalog.Susun(new[]
            {
                Orang(JabatanPerangkat.SekretarisDesa),
                Orang(JabatanPerangkat.SekretarisDesa, StatusPerangkat.MenungguSK),
                Orang(JabatanPerangkat.SekretarisDesa, StatusPerangkat.Selesai),
                Orang(JabatanPerangkat.KaurKeuangan),
                Orang(JabatanPerangkat.Staf, StatusPerangkat.Berhenti),
                // Di luar panel: kelompok lain tidak masuk kartu per jabatan ini.
                Orang(JabatanPerangkat.Linmas),
                Orang(JabatanPerangkat.KetuaBPD)
            });

            var sekretaris = kartu.Single(k => k.NamaJabatan == JabatanPerangkat.SekretarisDesa);
            Assert.Equal(3, sekretaris.JumlahOrang);
            Assert.Equal(1, sekretaris.JumlahAktif);
            Assert.Equal(1, sekretaris.JumlahMenungguSk);
            Assert.Equal("3 orang — 1 aktif, 1 menunggu SK, 1 lainnya", sekretaris.Ringkas);

            var kaur = kartu.Single(k => k.NamaJabatan == JabatanPerangkat.KaurKeuangan);
            Assert.Equal(1, kaur.JumlahOrang);
            Assert.Equal("1 orang — 1 aktif", kaur.Ringkas);

            var staf = kartu.Single(k => k.NamaJabatan == JabatanPerangkat.Staf);
            Assert.Equal(1, staf.JumlahOrang);
            Assert.Equal(0, staf.JumlahAktif);
            Assert.Equal("1 orang — 1 lainnya", staf.Ringkas);

            // Linmas dan BPD tidak termasuk panel jabatan struktural.
            Assert.DoesNotContain(kartu, k => k.Kelompok == "LINMAS" || k.Kelompok == "BPD");
        }

        // ---------- sumber SK per jabatan ----------

        [Fact]
        public void SkKepalaDesaDariBupati_SedangkanPerangkatLainDariKepalaDesa()
        {
            Assert.Equal(SumberSkPerangkat.Bupati,
                SkJabatanAksiKatalog.SumberJabatan(JabatanPerangkat.KepalaDesa));

            foreach (var jabatan in new[]
                     {
                         JabatanPerangkat.SekretarisDesa,
                         JabatanPerangkat.KaurKeuangan,
                         JabatanPerangkat.KaurTataUsaha,
                         JabatanPerangkat.KasiPelayanan,
                         JabatanPerangkat.OperatorDesa,
                         JabatanPerangkat.Staf,
                         JabatanPerangkat.Kadus,
                         JabatanPerangkat.Lainnya
                     })
            {
                Assert.Equal(SumberSkPerangkat.KepalaDesa,
                    SkJabatanAksiKatalog.SumberJabatan(jabatan));
            }
        }

        [Fact]
        public void LabelAksi_MembedakanModeArsipCetakDanTanpaTemplate()
        {
            var denganTemplate = SkJabatanAksiKatalog.Susun(Array.Empty<PerangkatDesa>())
                .Single(k => k.NamaJabatan == JabatanPerangkat.SekretarisDesa);
            Assert.True(denganTemplate.PunyaTemplate);
            Assert.Equal("Buat SK & Pratinjau", denganTemplate.LabelAksi);

            var kepalaDesa = SkJabatanAksiKatalog.Susun(Array.Empty<PerangkatDesa>())
                .Single(k => k.NamaJabatan == JabatanPerangkat.KepalaDesa);
            Assert.True(kepalaDesa.DariBupati);
            Assert.Equal("Arsipkan SK Bupati", kepalaDesa.LabelAksi);
            Assert.Contains("Bupati", kepalaDesa.Keterangan);

            var lainnya = SkJabatanAksiKatalog.Susun(Array.Empty<PerangkatDesa>())
                .Single(k => k.NamaJabatan == JabatanPerangkat.Lainnya);
            Assert.False(lainnya.PunyaTemplate);
            Assert.Contains("Tanpa template SK bawaan", lainnya.LabelAksi);
            Assert.Contains("belum punya template", lainnya.Keterangan);
        }

        [Fact]
        public void KartuBupatiMenandaiArsipYangSudahAda()
        {
            var kartu = SkJabatanAksiKatalog.Susun(new[]
            {
                Orang(JabatanPerangkat.KepalaDesa, berkas: "SKD-123.pdf")
            }).Single(k => k.NamaJabatan == JabatanPerangkat.KepalaDesa);

            Assert.True(kartu.AdaArsipBupati);
            Assert.Contains("tersedia", kartu.LabelAksi);
        }

        [Fact]
        public void NamaTampil_KapitalTiapKata()
        {
            var kartu = SkJabatanAksiKatalog.Susun(Array.Empty<PerangkatDesa>());

            Assert.Equal("Kaur Tata Usaha Dan Umum",
                kartu.Single(k => k.NamaJabatan == JabatanPerangkat.KaurTataUsaha).NamaTampil);
            Assert.Equal("Kasi Pelayanan",
                kartu.Single(k => k.NamaJabatan == JabatanPerangkat.KasiPelayanan).NamaTampil);
            Assert.Equal("Lainnya",
                kartu.Single(k => k.NamaJabatan == JabatanPerangkat.Lainnya).NamaTampil);
        }

        // ---------- pengikatan halaman (pelajaran C12: binding salah tak terdeteksi build) ----------

        [Fact]
        public void HalamanPerangkat_MengikatPanelDanAksiYangAdaDiViewModel()
        {
            var view = CoreTestFixture.ReadProjectFile("SuDesApp.Wpf/Views/PerangkatDesaView.xaml");
            var vm = CoreTestFixture.ReadProjectFile("SuDesApp.Wpf/ViewModels/PerangkatDesaViewModel.cs");

            foreach (var penanda in new[]
                     {
                         "KartuJabatanAksi",
                         "BukaPanelJabatanCommand",
                         "AdanyaPanelJabatan",
                         "TampilkanSemuaCommand",
                         "SkDiterbitkanBupati"
                     })
            {
                Assert.Contains(penanda, view);
                Assert.Contains(penanda, vm);
            }

            // Panel tersusun dari data terbaru setiap daftar dimuat.
            Assert.Contains("SusunPanelJabatan", vm);
        }

        [Fact]
        public void HalamanPerangkat_PanelHanyaTampilBilaDaftarTakDifilterKelompok()
        {
            var vm = CoreTestFixture.ReadProjectFile("SuDesApp.Wpf/ViewModels/PerangkatDesaViewModel.cs");

            // SusunPanelJabatan wajib menyembunyikan panel bila daftar sedang
            // difilter satu kelompok: kartu kelompok lain berhitung nol dan akan
            // menyesatkan bila tetap tampil.
            var mulai = vm.IndexOf("private void SusunPanelJabatan()", StringComparison.Ordinal);
            Assert.True(mulai >= 0, "SusunPanelJabatan tidak ditemukan.");

            var blok = vm[mulai..];
            var akhir = blok.IndexOf("/// <summary>", StringComparison.Ordinal);
            blok = akhir > 0 ? blok[..akhir] : blok;

            Assert.Contains("AdanyaPanelJabatan", blok);
            Assert.Contains("FilterKelompok", blok);
        }
    }
}
