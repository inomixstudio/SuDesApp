using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OfficeOpenXml;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Services;
using SuDesApp.Utilities;
using Xunit;

namespace SuDesApp.Core.Tests
{
    /// <summary>
    /// Uji impor warga massal: pembacaan berkas .xlsx/.csv, pemetaan judul
    /// kolom, validasi NIK (16 digit dan duplikat), serta efeknya ke database.
    /// Semua memakai SQLite in-memory dan berkas sementara, jadi berkas
    /// database pengguna tidak pernah tersentuh.
    /// </summary>
    public sealed class ImporWargaServiceTests : IDisposable
    {
        private const string NikA = "3204010101800001";
        private const string NikB = "3204010101850002";

        private readonly SqliteConnection _connection;
        private readonly WargaRepository _repo;
        private readonly ImporWargaService _svc;
        private readonly List<string> _berkasSementara = new();

        public ImporWargaServiceTests()
        {
            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();

            _connection.ExecuteScript(CoreTestFixture.ReadProjectFile("desa.db.sql"));
            _connection.ExecuteScript(
                "ALTER TABLE Surat ADD COLUMN Status TEXT NOT NULL DEFAULT 'Draft';" +
                "ALTER TABLE Surat ADD COLUMN KodeJenis TEXT NULL;" +
                "ALTER TABLE Surat ADD COLUMN AdditionalData TEXT NULL;" +
                "ALTER TABLE Surat ADD COLUMN CreatedAt TEXT NULL;" +
                "ALTER TABLE Surat ADD COLUMN UpdatedAt TEXT NULL;" +
                "ALTER TABLE JenisSurat ADD COLUMN Deskripsi TEXT NULL;" +
                "ALTER TABLE SKU ADD COLUMN LokasiUsaha TEXT NULL;");

            // desa.db.sql menyertakan satu warga contoh untuk pemasangan baru;
            // pengujian di sini menghitung sendiri barisnya.
            _connection.ExecuteNonQuery("DELETE FROM Warga;");

            _repo = new WargaRepository(
                _connection,
                new MemoryCacheService(
                    new MemoryCache(new MemoryCacheOptions()),
                    NullLogger<MemoryCacheService>.Instance),
                new AppConfig(new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["AppConfig:databaseConnectionString"] = "Data Source=:memory:"
                    })
                    .Build()),
                NullLogger<WargaRepository>.Instance);

            _svc = new ImporWargaService(_repo, NullLogger<ImporWargaService>.Instance);
        }

        public void Dispose()
        {
            _connection.Dispose();
            foreach (string f in _berkasSementara)
            {
                try { File.Delete(f); } catch { /* sementara */ }
            }
        }

        // ---------- helper ----------

        private static readonly string[] HeaderEkspor =
        {
            "No", "NIK", "Nama", "No Kartu Keluarga", "Jenis Kelamin", "Tempat Lahir",
            "Tanggal Lahir", "Agama", "Golongan Darah", "Status Perkawinan", "Pekerjaan",
            "Pendidikan", "Nama Ayah", "Nama Ibu", "Nomor HP", "RT", "RW", "Dusun",
            "Alamat (jalan/kampung)", "Desa", "Kecamatan", "Kabupaten", "Status Warga",
            "Tanggal Status", "Keterangan", "Jumlah Surat"
        };

        /// <summary>Baris versi hasil ekspor: semua kolom terisi sesuai urutan header.</summary>
        private static string BarisEkspor(
            string nik, string nama, string tanggalLahir = "17/08/1990",
            string jk = "L", string goldah = "O", string status = "AKTIF") =>
            string.Join(",", new[]
            {
                "1", nik,Csv(nama), "3204010101800001", jk, "Bandung", tanggalLahir, "Islam",
                goldah, "Kawin", "Petani", "SMA", "Ayah", "Ibu", "08123456789", "01", "02",
                "Dusun I", "Kp. Uji No. 1", "Desa Uji", "Kec. Uji", "Kab. Uji", status,
                "", "Catatan", "2"
            });

        private static string Csv(string nilai) => "\"" + nilai.Replace("\"", "\"\"") + "\"";

        private string TulisCsv(string isi, string ekstensi = ".csv")
        {
            string jalur = Path.Combine(
                Path.GetTempPath(), "uji-impor-" + Guid.NewGuid().ToString("N") + ekstensi);
            File.WriteAllText(jalur, isi, new UTF8Encoding(true));
            _berkasSementara.Add(jalur);
            return jalur;
        }

        private string TulisCsvEkspor(params string[] baris) =>
            TulisCsv(string.Join("\r\n", new[] { string.Join(",", HeaderEkspor) }.Concat(baris)) + "\r\n");

        // ---------- pembacaan berkas ----------

        [Fact]
        public async Task Validasi_CsvEkspor_SemuaKolomTerdeteksi_SemuaBarisLolos()
        {
            string jalur = TulisCsvEkspor(
                BarisEkspor(NikA, "Budi Santoso"),
                BarisEkspor(NikB, "Siti Aminah", "02/05/1985", "P", "AB"));

            var hasil = await _svc.ValidasiAsync(jalur);

            Assert.False(hasil.AdaKesalahanBerkas, hasil.KesalahanBerkas);
            Assert.Empty(hasil.KolomTidakDikenali);
            Assert.Equal(2, hasil.JumlahBaris);
            Assert.Equal(0, hasil.JumlahGagal);
            Assert.Equal(2, hasil.JumlahAkanDisimpan);
            Assert.All(hasil.Baris, b => Assert.Equal(TindakanImpor.Simpan, b.Tindakan));

            BarisImporWarga budi = hasil.Baris[0];
            Assert.Equal("Budi Santoso", budi.Data!.Nama);
            Assert.Equal("Laki-laki", budi.Data.JenisKelamin);
            Assert.Equal("O", budi.Data.GolonganDarah);
            Assert.Equal("1990-08-17", budi.Data.TanggalLahir);
            Assert.Equal("3204010101800001", budi.Data.NoKK);
            Assert.Equal("01", budi.Data.RT);
            Assert.Equal("Dusun I", budi.Data.Dusun);
            Assert.Equal("Kp. Uji No. 1", budi.Data.AlamatDetail);
            Assert.Equal(StatusWargaTipe.Aktif, budi.Data.StatusWarga);
            Assert.Equal("WNI", budi.Data.Kewarganegaraan);

            Assert.Equal("1985-05-02", hasil.Baris[1].Data!.TanggalLahir);
            Assert.Equal("Perempuan", hasil.Baris[1].Data!.JenisKelamin);
            Assert.Equal("AB", hasil.Baris[1].Data!.GolonganDarah);
        }

        [Fact]
        public async Task Validasi_TanggalLahirTidakDikenal_BarisDitolak()
        {
            string jalur = TulisCsvEkspor(BarisEkspor(NikA, "Budi", "bukan tanggal"));

            var hasil = await _svc.ValidasiAsync(jalur);

            Assert.Equal(1, hasil.JumlahGagal);
            Assert.Contains("tidak dikenali", hasil.Baris[0].Kesalahan[0]);
            Assert.Null(hasil.Baris[0].Data);
        }

        [Fact]
        public async Task Validasi_CsvSemicolon_DanNamaBerKoma_TerbacaUtuh()
        {
            // Excel Indonesia menulis CSV dengan ';'; nama juga boleh memuat koma.
            string isi = "NIK;Nama;Tanggal Lahir;Tempat Lahir;Jenis Kelamin;Agama;" +
                         "Status Perkawinan;Desa;Kecamatan;Kabupaten\r\n" +
                         $"{NikA};\"Budi, SANTOSO\";17/08/1990;Bandung;L;Islam;Kawin;Desa Uji;Kec. Uji;Kab. Uji\r\n";
            string jalur = TulisCsv(isi);

            var hasil = await _svc.ValidasiAsync(jalur);

            Assert.False(hasil.AdaKesalahanBerkas, hasil.KesalahanBerkas);
            Assert.Equal(1, hasil.JumlahAkanDisimpan);
            Assert.Equal("Budi, SANTOSO", hasil.Baris[0].Data!.Nama);
            Assert.Equal("1990-08-17", hasil.Baris[0].Data!.TanggalLahir);
            Assert.Equal("Laki-laki", hasil.Baris[0].Data!.JenisKelamin);
        }

        [Fact]
        public async Task Validasi_KolomWajibKosong_Ditolak_SertaDisebutkan()
        {
            // Kolom sengaja sedikit: importer harus menolak dan menyebutkan
            // kolom yang kosong, bukan gagal diam-diam saat menyimpan.
            string isi = "NIK,Nama,Tanggal Lahir\r\n" + $"{NikA},Budi,17/08/1990\r\n";
            string jalur = TulisCsv(isi);

            var hasil = await _svc.ValidasiAsync(jalur);

            Assert.Equal(1, hasil.JumlahGagal);
            Assert.Equal(0, hasil.JumlahBisaDisimpan);
            Assert.Contains(hasil.Baris[0].Kesalahan, k => k.Contains("Desa"));
            Assert.Contains(hasil.Baris[0].Kesalahan, k => k.Contains("Tempat lahir"));

            // Menyimpan hasil pemeriksaan ini tidak boleh sampai ke database.
            Assert.Equal(0, await _svc.JalankanAsync(hasil));
        }

        [Fact]
        public void AlasanTampil_MenulisSatuAlasanPerBaris_AtauTandaStrip()
        {
            var bersih = new BarisImporWarga();
            Assert.False(bersih.AdaKesalahan);
            Assert.Equal("-", bersih.AlasanTampil);

            var bermasalah = new BarisImporWarga();
            bermasalah.Kesalahan.Add("NIK kosong.");
            bermasalah.Kesalahan.Add("Nama kosong.");

            Assert.True(bermasalah.AdaKesalahan);
            Assert.Equal(
                "NIK kosong." + Environment.NewLine + "Nama kosong.",
                bermasalah.AlasanTampil);
        }

        [Fact]
        public async Task Validasi_FormatTidakDidukung_KembalikanPesanYangJelas()
        {
            string jalur = TulisCsv("NIK,Nama\r\n", ".txt");

            var hasil = await _svc.ValidasiAsync(jalur);

            Assert.True(hasil.AdaKesalahanBerkas);
            Assert.Contains(".xlsx", hasil.KesalahanBerkas!);
            Assert.Empty(hasil.Baris);
        }

        [Fact]
        public async Task Validasi_TanpaKolomNik_KembalikanPesanYangJelas()
        {
            string jalur = TulisCsv("Nama,Desa\r\nBudi,Desa Uji\r\n");

            var hasil = await _svc.ValidasiAsync(jalur);

            Assert.True(hasil.AdaKesalahanBerkas);
            Assert.Contains("NIK", hasil.KesalahanBerkas!);
        }

        [Fact]
        public async Task Validasi_BerkasTidakAda_LemparFileNotFound()
        {
            await Assert.ThrowsAsync<FileNotFoundException>(
                () => _svc.ValidasiAsync(Path.Combine(Path.GetTempPath(), "tidak-ada-aja.xlsx")));
        }

        [Fact]
        public async Task Validasi_Xlsx_UrutanKolomBebas_DanTanggalExcelDibaca()
        {
            ExcelPackage.License.SetNonCommercialPersonal("ARIE INO");

            string jalur = Path.Combine(
                Path.GetTempPath(), "uji-impor-" + Guid.NewGuid().ToString("N") + ".xlsx");
            _berkasSementara.Add(jalur);

            using (var package = new ExcelPackage())
            {
                var ws = package.Workbook.Worksheets.Add("Data Warga");
                // Urutan sengaja dibalik dan tidak sama dengan hasil ekspor.
                ws.Cells[1, 1].Value = "Nama";
                ws.Cells[1, 2].Value = "Tgl Lahir";
                ws.Cells[1, 3].Value = "NIK";
                ws.Cells[1, 4].Value = "Jenis Kelamin";
                ws.Cells[1, 5].Value = "CatatanZg";
                ws.Cells[1, 6].Value = "Desa";
                ws.Cells[1, 7].Value = "Kecamatan";
                ws.Cells[1, 8].Value = "Kabupaten";
                ws.Cells[1, 9].Value = "Tempat Lahir";
                ws.Cells[1, 10].Value = "Agama";
                ws.Cells[1, 11].Value = "Status Perkawinan";

                ws.Cells[2, 1].Value = "Siti Aminah";
                ws.Cells[2, 2].Value = new DateTime(1985, 5, 2);   // sel bertanggal asli
                ws.Cells[2, 3].Value = NikB;
                ws.Cells[2, 4].Value = "PEREMPUAN";
                ws.Cells[2, 5].Value = "diimpor";
                ws.Cells[2, 6].Value = "Desa Uji";
                ws.Cells[2, 7].Value = "Kec. Uji";
                ws.Cells[2, 8].Value = "Kab. Uji";
                ws.Cells[2, 9].Value = "Bandung";
                ws.Cells[2, 10].Value = "Islam";
                ws.Cells[2, 11].Value = "Kawin";

                package.SaveAs(new FileInfo(jalur));
            }

            var hasil = await _svc.ValidasiAsync(jalur);

            Assert.False(hasil.AdaKesalahanBerkas, hasil.KesalahanBerkas);
            Assert.Contains("CatatanZg", hasil.KolomTidakDikenali);
            Assert.Equal(1, hasil.JumlahAkanDisimpan);

            WargaData data = hasil.Baris[0].Data!;
            Assert.Equal("Siti Aminah", data.Nama);
            Assert.Equal("1985-05-02", data.TanggalLahir);
            Assert.Equal("Perempuan", data.JenisKelamin);
        }

        [Fact]
        public async Task Validasi_Xlsx_AngkaKecilTakDianggapTanggal()
        {
            // RT/RW sering diketik sebagai angka. Kalau angka sekecil itu
            // dibaca sebagai nomor seri tanggal, RT 01 berubah jadi tahun 1899.
            ExcelPackage.License.SetNonCommercialPersonal("ARIE INO");

            string jalur = Path.Combine(
                Path.GetTempPath(), "uji-impor-" + Guid.NewGuid().ToString("N") + ".xlsx");
            _berkasSementara.Add(jalur);

            using (var package = new ExcelPackage())
            {
                var ws = package.Workbook.Worksheets.Add("Warga");
                ws.Cells[1, 1].Value = "NIK";
                ws.Cells[1, 2].Value = "Nama";
                ws.Cells[1, 3].Value = "RT";
                ws.Cells[1, 4].Value = "RW";
                ws.Cells[1, 5].Value = "Tanggal Lahir";
                ws.Cells[1, 6].Value = "Tempat Lahir";
                ws.Cells[1, 7].Value = "Jenis Kelamin";
                ws.Cells[1, 8].Value = "Agama";
                ws.Cells[1, 9].Value = "Status Perkawinan";
                ws.Cells[1, 10].Value = "Desa";
                ws.Cells[1, 11].Value = "Kecamatan";
                ws.Cells[1, 12].Value = "Kabupaten";

                ws.Cells[2, 1].Value = double.Parse(NikA);   // NIK bertipe angka
                ws.Cells[2, 2].Value = "Budi Santoso";
                ws.Cells[2, 3].Value = 1d;                    // RT 01
                ws.Cells[2, 4].Value = 2d;                    // RW 02
                ws.Cells[2, 5].Value = 31169d;                // 02/05/1985
                ws.Cells[2, 6].Value = "Bandung";
                ws.Cells[2, 7].Value = "L";
                ws.Cells[2, 8].Value = "Islam";
                ws.Cells[2, 9].Value = "Kawin";
                ws.Cells[2, 10].Value = "Desa Uji";
                ws.Cells[2, 11].Value = "Kec. Uji";
                ws.Cells[2, 12].Value = "Kab. Uji";

                package.SaveAs(new FileInfo(jalur));
            }

            var hasil = await _svc.ValidasiAsync(jalur);

            Assert.False(hasil.AdaKesalahanBerkas, hasil.KesalahanBerkas);
            Assert.True(hasil.JumlahAkanDisimpan == 1, RingkasanGalat(hasil));

            WargaData data = hasil.Baris[0].Data!;
            Assert.Equal(NikA, data.NIK);
            Assert.Equal("1", data.RT);
            Assert.Equal("2", data.RW);
            Assert.Equal("1985-05-02", data.TanggalLahir);
        }

        /// <summary>Pesan kegagalan yang bisa langsung dibaca saat uji gagal.</summary>
        private static string RingkasanGalat(HasilImporWarga h) =>
            $"gagal={h.JumlahGagal} baris={h.JumlahBaris} " +
            $"[{string.Join(" || ", h.Baris.Select(b => b.NomorBaris + ": " + string.Join(" + ", b.Kesalahan)))}] " +
            $"galatBerkas={h.KesalahanBerkas}";

        // ---------- validasi NIK ----------

        [Theory]
        [InlineData("12345", "16 digit")]
        [InlineData("32040101018000012345", "16 digit")]
        [InlineData("32040101018000A1", "hanya boleh berisi angka")]
        [InlineData("9999999999999999", "instansi atau kematian")]
        [InlineData("0000000000000000", "instansi atau kematian")]
        public async Task Validasi_NikTidakValid_Ditolak_DenganAlasanJelas(string nik, string alasan)
        {
            string jalur = TulisCsvEkspor(BarisEkspor(nik, "Budi Santoso"));

            var hasil = await _svc.ValidasiAsync(jalur);

            Assert.Equal(1, hasil.JumlahGagal);
            Assert.Contains(hasil.Baris[0].Kesalahan, k => k.Contains(alasan));
            Assert.Equal(0, hasil.JumlahBisaDisimpan);
        }

        [Fact]
        public async Task Validasi_NikDuplikatDiBerkas_HanyaBarisPertamaDisimpan()
        {
            string jalur = TulisCsvEkspor(
                BarisEkspor(NikA, "Budi Santoso"),
                BarisEkspor(NikA, "Budi Santoso Ganda"));

            var hasil = await _svc.ValidasiAsync(jalur);

            Assert.Equal(1, hasil.JumlahDuplikatDiBerkas);
            Assert.Equal(1, hasil.JumlahAkanDisimpan);
            Assert.Equal(TindakanImpor.Simpan, hasil.Baris[0].Tindakan);
            Assert.Contains(hasil.Baris[1].Kesalahan, k => k.Contains("baris 2"));
        }

        [Fact]
        public async Task Validasi_NikSudahDiDatabase_Dilewati_SecaraBawaan()
        {
            await Seed(NikA, "Budi Lama");
            string jalur = TulisCsvEkspor(BarisEkspor(NikA, "Budi Baru"));

            var hasil = await _svc.ValidasiAsync(jalur);

            Assert.Equal(1, hasil.JumlahDuplikatDiDatabase);
            Assert.Equal(0, hasil.JumlahBisaDisimpan);
            Assert.Equal(TindakanImpor.Lewati, hasil.Baris[0].Tindakan);
            Assert.Contains(hasil.Baris[0].Kesalahan, k => k.Contains("perbarui"));
        }

        [Fact]
        public async Task Validasi_NikSudahDiDatabase_Diperbarui_SaatModePerbarui()
        {
            await Seed(NikA, "Budi Lama");
            string jalur = TulisCsvEkspor(BarisEkspor(NikA, "Budi Baru"));

            var hasil = await _svc.ValidasiAsync(jalur, perbaruiYangSudahAda: true);

            Assert.Equal(0, hasil.JumlahGagal);
            Assert.Equal(1, hasil.JumlahAkanDiperbarui);
            Assert.Equal(0, hasil.JumlahAkanDisimpan);
        }

        [Fact]
        public async Task Validasi_NamaKosong_Ditolak()
        {
            string jalur = TulisCsvEkspor(BarisEkspor(NikA, ""));

            var hasil = await _svc.ValidasiAsync(jalur);

            Assert.Equal(1, hasil.JumlahGagal);
            Assert.Contains(hasil.Baris[0].Kesalahan, k => k.Contains("Nama"));
        }

        [Fact]
        public async Task Validasi_KolomTerlaluPanjang_Ditolak_SebutNamanya()
        {
            string jalur = TulisCsvEkspor(BarisEkspor(NikA, new string('X', 120)));

            var hasil = await _svc.ValidasiAsync(jalur);

            Assert.Equal(1, hasil.JumlahGagal);
            Assert.Contains(hasil.Baris[0].Kesalahan, k => k.Contains("Nama") && k.Contains("100"));
        }

        [Fact]
        public async Task Validasi_StatusDanJenisKelaminTakDikenal_Ditolak()
        {
            string isi = "NIK,Nama,Jenis Kelamin,Status Warga\r\n" +
                         $"{NikA},Budi,XYZ,SEPATU\r\n";
            string jalur = TulisCsv(isi);

            var hasil = await _svc.ValidasiAsync(jalur);

            Assert.Equal(1, hasil.JumlahGagal);
            Assert.Contains(hasil.Baris[0].Kesalahan, k => k.Contains("Jenis kelamin"));
            Assert.Contains(hasil.Baris[0].Kesalahan, k => k.Contains("Status warga"));
        }

        // ---------- penyimpanan ----------

        [Fact]
        public async Task JalankanAsync_HanyaMenyimpanBarisYangLolos()
        {
            string jalur = TulisCsvEkspor(
                BarisEkspor(NikA, "Budi Santoso"),
                BarisEkspor("NIK-KECIL", "Tidak Sah"),
                BarisEkspor(NikB, "Siti Aminah"));

            var hasil = await _svc.ValidasiAsync(jalur);
            int tersimpan = await _svc.JalankanAsync(hasil);

            Assert.Equal(2, tersimpan);
            Assert.Equal(2, JumlahWargaDiDatabase());
            Assert.Null(await _repo.GetWargaByNikAsync("NIK-KECIL"));
            Assert.NotNull(await _repo.GetWargaByNikAsync(NikA));
            Assert.NotNull(await _repo.GetWargaByNikAsync(NikB));
        }

        [Fact]
        public async Task JalankanAsync_MemperbaruiWargaLama_SaatModePerbarui()
        {
            await Seed(NikA, "Budi Lama");
            string jalur = TulisCsvEkspor(BarisEkspor(NikA, "Budi Baru"));

            var hasil = await _svc.ValidasiAsync(jalur, perbaruiYangSudahAda: true);
            int tersimpan = await _svc.JalankanAsync(hasil);

            Assert.Equal(1, tersimpan);
            Assert.Equal(1, JumlahWargaDiDatabase());

            var warga = await _repo.GetWargaByNikAsync(NikA);
            Assert.Equal("Budi Baru", warga!.Nama);
        }

        [Fact]
        public async Task JalankanAsync_BerkasTidakValid_TidakMenyimpanApaPun()
        {
            string jalur = TulisCsvEkspor(BarisEkspor("123", "Budi"));

            var hasil = await _svc.ValidasiAsync(jalur);
            int tersimpan = await _svc.JalankanAsync(hasil);

            Assert.Equal(0, tersimpan);
            Assert.Equal(0, JumlahWargaDiDatabase());
        }

        // ---------- helper internal ----------

        [Fact]
        public void PemisahBaris_KutipGandaDanBarisBaru_Dipertahankan()
        {
            var hasil = ImporWargaService.PemisahBaris(
                "a,b,c\r\n\"satu, dua\",\"diasaid \"\"halo\"\"\",\r\n", ',');

            Assert.Equal(2, hasil.Count);
            Assert.Equal(new[] { "a", "b", "c" }, hasil[0]);
            Assert.Equal(new[] { "satu, dua", "diasaid \"halo\"", "" }, hasil[1]);
        }

        [Theory]
        [InlineData("No Kartu Keluarga", "nokartukeluarga")]
        [InlineData("Alamat (jalan/kampung)", "alamatjalankampung")]
        [InlineData("  NIK  ", "nik")]
        public void BersihkanJudul_MembuangSpasiDanTandaBaca(string masuk, string keluar)
        {
            Assert.Equal(keluar, ImporWargaService.BersihkanJudul(masuk));
        }

        [Theory]
        [InlineData("A", "A")]
        [InlineData("Golongan Darah B", "B")]
        [InlineData("o", "O")]
        [InlineData("AB+", "AB+")]
        [InlineData("Z", null)]
        [InlineData("", null)]
        public void NormalisasiGolonganDarah_MenyeragamkanKataPengantar(string masuk, string? keluar)
        {
            Assert.Equal(keluar, ImporWargaService.NormalisasiGolonganDarah(masuk));
        }

        [Theory]
        [InlineData("17/08/1990", "1990-08-17")]
        [InlineData("17-08-1990", "1990-08-17")]
        [InlineData("1990-08-17", "1990-08-17")]
        [InlineData("17.08.1990", "1990-08-17")]
        public void CobaParseTanggal_MenerimaFormatLokal(string masuk, string keluar)
        {
            Assert.True(ImporWargaService.CobaParseTanggal(masuk, out var tanggal));
            Assert.Equal(keluar, tanggal!.Value.ToString("yyyy-MM-dd"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("bukan tanggal")]
        [InlineData("32/13/1990")]
        public void CobaParseTanggal_MenolakIsiTidakTerbaca(string masuk)
        {
            Assert.False(ImporWargaService.CobaParseTanggal(masuk, out var tanggal));
            Assert.Null(tanggal);
        }

        // ---------- utilitas ----------

        private async Task Seed(string nik, string nama) =>
            await _repo.AddOrUpdateWargaAsync(new WargaData
            {
                NIK = nik,
                Nama = nama,
                TempatLahir = "Bandung",
                TanggalLahir = "1990-08-17",
                JenisKelamin = "Laki-laki",
                Agama = "Islam",
                StatusPerkawinan = "Kawin",
                Desa = "Desa Uji",
                Kecamatan = "Kec. Uji",
                Kabupaten = "Kab. Uji",
                StatusWarga = StatusWargaTipe.Aktif
            });

        private int JumlahWargaDiDatabase() =>
            Convert.ToInt32(_connection.Scalar("SELECT COUNT(*) FROM Warga"));
    }
}
