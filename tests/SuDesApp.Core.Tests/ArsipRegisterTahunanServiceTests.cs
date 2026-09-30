using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using OfficeOpenXml;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Services;
using Xunit;

namespace SuDesApp.Core.Tests
{
    /// <summary>
    /// Uji arsip register tahunan. Yang dijaga dua hal: register memuat status
    /// buku dan deret (itu yang tidak ada di register lama), dan berkas Excel
    /// maupun PDF benar-benar terbit — arsip yang gagal dicetak tanpa suara
    /// adalah kegagalan yang baru ketahuan saat bukti sudah tidak lengkap.
    /// </summary>
    public sealed class ArsipRegisterTahunanServiceTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ArsipRegisterTahunanService _svc;

        public ArsipRegisterTahunanServiceTests()
        {
            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();
            _connection.ExecuteScript(@"
                CREATE TABLE Surat (
                    ID_Surat INTEGER PRIMARY KEY AUTOINCREMENT,
                    ID_Jenis INTEGER NOT NULL,
                    ID_Warga INTEGER,
                    NomorSurat TEXT NOT NULL UNIQUE,
                    TanggalSurat DATE NOT NULL,
                    Keterangan TEXT,
                    Keperluan TEXT
                );
                CREATE TABLE JenisSurat (
                    ID_Jenis INTEGER PRIMARY KEY AUTOINCREMENT,
                    KodeJenis TEXT,
                    NamaJenis TEXT,
                    Deskripsi TEXT,
                    IsActive INTEGER DEFAULT 1
                );
                CREATE TABLE Warga (
                    ID_Warga INTEGER PRIMARY KEY AUTOINCREMENT,
                    NIK TEXT,
                    Nama TEXT
                );
                CREATE TABLE TutupBukuTahun (
                    ID           INTEGER PRIMARY KEY AUTOINCREMENT,
                    Tahun        INTEGER NOT NULL UNIQUE,
                    Status       TEXT    NOT NULL DEFAULT 'TERBUKA'
                                CHECK (Status IN ('TERBUKA', 'TERTUTUP')),
                    TanggalTutup TEXT,
                    JumlahSurat  INTEGER NOT NULL DEFAULT 0,
                    JumlahDeret  INTEGER NOT NULL DEFAULT 0,
                    Snapshot     TEXT,
                    Catatan      TEXT,
                    DitutupOleh  TEXT,
                    DitutupPada  TEXT,
                    DibukaOleh   TEXT,
                    DibukaPada   TEXT,
                    CreatedAt    DATETIME DEFAULT CURRENT_TIMESTAMP,
                    UpdatedAt    DATETIME DEFAULT CURRENT_TIMESTAMP
                );
                INSERT INTO JenisSurat (ID_Jenis, KodeJenis, NamaJenis) VALUES (1, 'SKD', 'SK Domisili');
                INSERT INTO Warga (ID_Warga, NIK, Nama) VALUES (1, '3204010101800001', 'Budi Santoso');
                INSERT INTO Surat (ID_Jenis, ID_Warga, NomorSurat, TanggalSurat, Keterangan, Keperluan) VALUES
                    (1, 1, '470/001/Ds/2024', '2024-02-01', 'catatan 1', 'keperluan 1'),
                    (1, 1, '470/002/Ds/2024', '2024-03-01', NULL, NULL),
                    (1, NULL, '470/003/Ds/2024', '2024-04-01', 'tanpa warga', 'tanpa warga');");

            var jenis = new JenisSuratForTest(_connection);
            _svc = new ArsipRegisterTahunanService(
                jenis,
                new TutupBukuTahunRepository(_connection, NullLogger<TutupBukuTahunRepository>.Instance),
                new VerifikasiPenomoranService(jenis),
                new DesaPalsu(),
                NullLogger<ArsipRegisterTahunanService>.Instance,
                () => new DateTime(2025, 2, 3, 9, 0, 0));
        }

        public void Dispose() => _connection.Dispose();

        /// <summary>
        /// Query verifikasi memakai UnitOfWork dan cache, jadi untuk arsip ini
        /// cukup repository kecil yang menjalankan SQL yang sama langsung ke
        /// koneksi yang sama.
        /// </summary>
        private sealed class JenisSuratForTest : IJenisSuratRepository
        {
            private readonly SqliteConnection _c;
            public JenisSuratForTest(SqliteConnection c) => _c = c;

            public Task<List<BarisNomorTerbit>> GetNomorTerbitAsync(int? tahun = null) =>
                Task.FromResult(_c.Query<BarisNomorTerbit>(@"
                    SELECT s.ID_Surat, s.NomorSurat, s.TanggalSurat, js.NamaJenis
                      FROM Surat s
                      LEFT JOIN JenisSurat js ON s.ID_Jenis = js.ID_Jenis
                     WHERE (@Tahun IS NULL OR CAST(strftime('%Y', s.TanggalSurat) AS INTEGER) = @Tahun)
                     ORDER BY s.TanggalSurat, s.ID_Surat", new { Tahun = tahun }).ToList());

            public Task<List<BarisRegisterSurat>> GetRegisterSuratAsync(int tahun) =>
                Task.FromResult(_c.Query<BarisRegisterSurat>(@"
                    SELECT s.ID_Surat, s.NomorSurat, s.TanggalSurat, s.Keperluan, s.Keterangan,
                           js.NamaJenis, w.Nama AS NamaWarga, w.NIK AS NikWarga
                      FROM Surat s
                      LEFT JOIN JenisSurat js ON s.ID_Jenis = js.ID_Jenis
                      LEFT JOIN Warga w       ON s.ID_Warga = w.ID_Warga
                     WHERE CAST(strftime('%Y', s.TanggalSurat) AS INTEGER) = @Tahun
                     ORDER BY s.NomorSurat, s.ID_Surat", new { Tahun = tahun }).ToList());

            public Task<List<string>> GetAvailableYearsAsync() =>
                Task.FromResult(_c.Query<string>(
                    "SELECT DISTINCT strftime('%Y', TanggalSurat) FROM Surat ORDER BY 1 DESC").ToList());

            public Task InitializeJenisSuratDataAsync() => Task.CompletedTask;
            public Task<JenisSuratKelas> GetJenisSuratByNamaAsync(string namaJenis) => Task.FromResult(new JenisSuratKelas());
            public Task<JenisSuratKelas> GetByNamaAsync(string namaJenis) => Task.FromResult(new JenisSuratKelas());
            public Task<int> GetIdJenisSuratByNamaAsync(string namaJenis) => Task.FromResult(1);
            public Task<string> GenerateNomorSuratAsync(string kodeJenis) => Task.FromResult(string.Empty);
            public Task<bool> IsNomorSuratExistsAsync(string nomorSurat) => Task.FromResult(false);
            public Task<List<JenisSuratKelas>> GetAllJenisSuratAsync() => Task.FromResult(new List<JenisSuratKelas>());
            public Task<Dictionary<string, string>> GetJenisSuratDisplayNamesAsync() => Task.FromResult(new Dictionary<string, string>());
            public Task<List<string>> GetJenisSuratKeteranganDesa() => Task.FromResult(new List<string>());
            public Task<int?> GetLastSuratIdByTypeAsync(string templateName) => Task.FromResult<int?>(null);
            public Task<bool> HasExistingNomorSuratAsync(string namaJenis) => Task.FromResult(false);
            public Task<HashSet<string>> GetSharedNumberingGroupAsync() => Task.FromResult(new HashSet<string>());
            public Task RefreshConfigurationAsync() => Task.CompletedTask;
            public void TandaiRegisterBerubah() { /* fake: tanpa cache */ }
        }

        private sealed class DesaPalsu : IDesaRepository
        {
            public Task<DesaData> GetInfoDesaAsync(CancellationToken ct = default) =>
                Task.FromResult(new DesaData { NamaDesa = "Desa Uji" });

            public Task<DesaData> GetInfoDesaFromCacheAsync() => Task.FromResult(new DesaData { NamaDesa = "Desa Uji" });

            public Task InitializeAsync() => Task.CompletedTask;
            public Task UpdateInfoDesaAsync(DesaData data) => Task.CompletedTask;
            public Task SaveInfoDesaAsync(DesaData data) => Task.CompletedTask;
            public Task InvalidateCacheAsync() => Task.CompletedTask;
        }

        // ---------- menyusun data ----------

        [Fact]
        public async Task Susun_MengambilStatusBuku_DanRegister()
        {
            await TutupBukuAsync(tahun: 2024);

            var data = await _svc.SusunAsync(2024);

            Assert.Equal(2024, data.Tahun);
            Assert.Equal("Desa Uji", data.NamaDesa);
            Assert.Equal(StatusTutupBuku.Tertutup, data.StatusBuku.Status);
            Assert.Equal(3, data.JumlahSurat);
            Assert.Equal(3, data.Baris.Count);
            Assert.Equal("470", Assert.Single(data.Verifikasi.Deret).Awalan);
        }

        [Fact]
        public async Task Susun_TahunKosong_TetapBisaDicetak()
        {
            var data = await _svc.SusunAsync(2030);

            Assert.Empty(data.Baris);
            Assert.Equal(StatusTutupBuku.Terbuka, data.StatusBuku.Status);
        }

        [Fact]
        public async Task Susun_SuratTanpaWarga_TidakDilewati()
        {
            var data = await _svc.SusunAsync(2024);

            Assert.Contains(data.Baris, b => b.NomorSurat == "470/003/Ds/2024");
            Assert.Contains(data.Baris, b => b.NamaWarga is null);
        }

        [Fact]
        public async Task Susun_TahunTidakWajar_Ditolak()
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _svc.SusunAsync(12));
        }

        private async Task TutupBukuAsync(int tahun)
        {
            await new TutupBukuTahunRepository(_connection, NullLogger<TutupBukuTahunRepository>.Instance)
                .UpsertAsync(new TutupBukuTahun
                {
                    Tahun = tahun,
                    Status = StatusTutupBuku.Tertutup,
                    TanggalTutup = "2025-01-31 10:00:00",
                    JumlahSurat = 3,
                    JumlahDeret = 1,
                    DitutupOleh = "Operator",
                    Catatan = "Tahun reguler"
                });
        }

        // ---------- nama berkas ----------

        [Theory]
        [InlineData("xlsx", "Register Surat Desa 2024.xlsx")]
        [InlineData(".pdf", "Register Surat Desa 2024.pdf")]
        public void NamaBerkas_Baku_DanMenerimaTitik(string ekstensi, string harapan)
        {
            Assert.Equal(harapan, _svc.NamaBerkas(2024, ekstensi));
        }

        // ---------- Excel ----------

        [Fact]
        public void Excel_Terbit_DenganTigaSheet()
        {
            byte[] berkas = ArsipRegisterTahunanService.RenderExcel(DataContoh());

            Assert.NotEmpty(berkas);

            using var package = new ExcelPackage(new MemoryStream(berkas));
            var nama = package.Workbook.Worksheets.Select(w => w.Name).ToArray();

            Assert.Equal(new[] { "Ringkasan", "Register", "Deret Nomor" }, nama);
        }

        [Fact]
        public void Excel_MenulisStatusDanDeret()
        {
            byte[] berkas = ArsipRegisterTahunanService.RenderExcel(DataContoh());

            using var package = new ExcelPackage(new MemoryStream(berkas));

            string ringkasan = package.Workbook.Worksheets["Ringkasan"].Cells[1, 1].Text;
            Assert.Equal("ARSIP REGISTER SURAT DESA", ringkasan);

            Assert.Equal("470/001/Ds/2024", package.Workbook.Worksheets["Register"].Cells[2, 2].Text);
            Assert.Equal("Budi Santoso", package.Workbook.Worksheets["Register"].Cells[2, 5].Text);

            // Tanggal ISO 2024-02-01 dicetak mengikuti kebiasaan Indonesia.
            Assert.Equal("01-02-2024", package.Workbook.Worksheets["Register"].Cells[2, 3].Text);

            Assert.Equal("470", package.Workbook.Worksheets["Deret Nomor"].Cells[2, 1].Text);
            Assert.Equal(3, Convert.ToInt32(package.Workbook.Worksheets["Deret Nomor"].Cells[2, 3].Value));
        }

        [Fact]
        public void Excel_RegisterKosong_TidakThrow()
        {
            var data = DataContoh();
            data = new RegisterTahunanData
            {
                Tahun = data.Tahun,
                NamaDesa = data.NamaDesa,
                StatusBuku = data.StatusBuku,
                Verifikasi = data.Verifikasi,
                Baris = Array.Empty<BarisRegisterSurat>(),
                DicetakPada = data.DicetakPada
            };

            byte[] berkas = ArsipRegisterTahunanService.RenderExcel(data);

            using var package = new ExcelPackage(new MemoryStream(berkas));
            var register = package.Workbook.Worksheets["Register"];

            // Hanya baris judul yang terisi; tidak ada baris surat tambahan.
            Assert.Equal(1, register.Dimension.End.Row);
            Assert.Equal("Nomor Surat", register.Cells[1, 2].Text);
        }

        // ---------- PDF ----------

        [Fact]
        public void Pdf_Terbit_DenganHeaderYangBenar()
        {
            byte[] berkas = ArsipRegisterTahunanService.RenderPdf(DataContoh());

            Assert.True(berkas.Length > 1000);

            // PDF selalu diawali tanda %PDF-; kalau gagal dirender, yang keluar
            // biasanya array kosong atau teks error.
            Assert.Equal("%PDF", Encoding.ASCII.GetString(berkas, 0, 4));
        }

        [Fact]
        public void Pdf_RegisterKosong_TidakThrow()
        {
            var data = DataContoh();
            var kosong = new RegisterTahunanData
            {
                Tahun = data.Tahun,
                NamaDesa = data.NamaDesa,
                StatusBuku = data.StatusBuku,
                Verifikasi = data.Verifikasi,
                Baris = Array.Empty<BarisRegisterSurat>(),
                DicetakPada = data.DicetakPada
            };

            byte[] berkas = ArsipRegisterTahunanService.RenderPdf(kosong);

            Assert.Equal("%PDF", Encoding.ASCII.GetString(berkas, 0, 4));
        }

        [Fact]
        public async Task Pdf_DanExcel_LewatService_Terbit()
        {
            await TutupBukuAsync(2024);

            byte[] pdf = await _svc.BuatPdfAsync(2024);
            byte[] excel = await _svc.BuatExcelAsync(2024);

            Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
            Assert.NotEmpty(excel);
        }

        private static RegisterTahunanData DataContoh() => new()
        {
            Tahun = 2024,
            NamaDesa = "Desa Uji",
            StatusBuku = new TutupBukuTahun
            {
                Tahun = 2024,
                Status = StatusTutupBuku.Tertutup,
                TanggalTutup = "2025-01-31 10:00:00",
                DitutupOleh = "Operator",
                Catatan = "Tahun reguler"
            },
            Verifikasi = new HasilVerifikasiNomor
            {
                Tahun = 2024,
                Deret = new[]
                {
                    new DeretNomor
                    {
                        Awalan = "470",
                        Tahun = 2024,
                        Jumlah = 3,
                        NomorPertama = 1,
                        NomorTerakhir = 3,
                        NamaJenis = new[] { "SK Domisili" }
                    }
                },
                NomorTahunTidakCocok = Array.Empty<string>(),
                TahunTersedia = new[] { 2024 }
            },
            Baris = new[]
            {
                new BarisRegisterSurat
                {
                    ID_Surat = 1,
                    NomorSurat = "470/001/Ds/2024",
                    TanggalSurat = "2024-02-01",
                    NamaJenis = "SK Domisili",
                    NamaWarga = "Budi Santoso",
                    NikWarga = "3204010101800001",
                    Keperluan = "keperluan 1",
                    Keterangan = "catatan 1"
                }
            },
            DicetakPada = new DateTime(2025, 2, 3, 9, 0, 0)
        };
    }
}
