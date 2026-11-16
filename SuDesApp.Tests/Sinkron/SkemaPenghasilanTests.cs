using System.Text.Json;
using SuDesApp.Sinkron;
using Xunit;

namespace SuDesApp.Tests.Sinkron
{
    /// <summary>
    /// Keterangan Penghasilan (PENGHASILAN, kode 472) kini jenis surat biasa di
    /// desktop form; Android mengirimkannya lewat antrean yang sama. Uji ini
    /// memastikan skema PENGHASILAN benar memetakan penghasilan.* dan data diri
    /// warga, sehingga surat tidak diimpor sebagai jenis tanpa data.
    /// </summary>
    public class SkemaPenghasilanTests
    {
        private static SkemaFormulir Skema() => SkemaFormulir.MuatDariJson(
            File.ReadAllText(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "Configuration", "SkemaFormulir.json")));

        [Fact]
        public void Skema_penghasilan_tersedia_dan_tiga_field_intinya_wajib()
        {
            var skema = Skema();
            var penghasilan = skema.Cari("PENGHASILAN");
            Assert.NotNull(penghasilan);
            Assert.Contains(penghasilan!.Fields, f => f.Key == "pekerjaanUsaha" && f.Required);
            Assert.Contains(penghasilan.Fields, f => f.Key == "penghasilanPerBulan" && f.Required);
            Assert.Contains(penghasilan.Fields, f => f.Key == "jumlahTanggungan" && !f.Required);
        }

        [Fact]
        public void Bangun_memetakan_penghasilan_dan_data_diri_warga()
        {
            var skema = Skema();
            var fields = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["nomorSurat"] = "",
                ["nik"] = "3203999988887777",
                ["nama"] = "Dr. H. Ahmad Suryana, S.H.",
                ["warga.tempatLahir"] = "Karawang",
                ["warga.tanggalLahir"] = "1990-05-02",
                ["jenisKelamin"] = "Laki-laki",
                ["pekerjaan"] = "Petani",
                ["warga.pekerjaan"] = "Petani",
                ["pekerjaanUsaha"] = "Usaha peternakan ayam",
                ["penghasilanPerBulan"] = "Rp2.500.000",
                ["jumlahTanggungan"] = "3 anak",
            });
            using var doc = JsonDocument.Parse(fields);

            var surat = skema.Bangun("PENGHASILAN", doc.RootElement, "472/001/Ds/2026", null, out var diterapkan);

            Assert.True(diterapkan > 0);
            Assert.Equal("PENGHASILAN", surat.NamaJenis);
            Assert.Equal("Dr. H. Ahmad Suryana, S.H.", surat.Warga?.Nama);
            Assert.Equal("Petani", surat.Warga?.Pekerjaan);
            Assert.NotNull(surat.Penghasilan);
            Assert.Equal("Usaha peternakan ayam", surat.Penghasilan!.PekerjaanUsaha);
            Assert.Equal("Rp2.500.000", surat.Penghasilan.PenghasilanPerBulan);
            Assert.Equal("3 anak", surat.Penghasilan.JumlahTanggungan);
        }
    }
}
