using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Services;
using Xunit;

namespace SuDesApp.Core.Tests
{
    /// <summary>
    /// Uji tutup buku tahunan. Yang dijaga: menutup tahun tidak menghapus
    /// apa pun, selalu bisa dibalik, dan menolak menutup register yang
    /// penomorannya bermasalah kecuali operator memaksa dengan alasan.
    /// </summary>
    public sealed class TutupBukuTahunServiceTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly TutupBukuTahunRepository _repo;
        private readonly FakeVerifikasi _verifikasi = new();

        public TutupBukuTahunServiceTests()
        {
            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();
            _connection.ExecuteScript(@"
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
                CREATE INDEX IX_TutupBukuTahun_Status ON TutupBukuTahun(Status);");

            _repo = new TutupBukuTahunRepository(_connection, NullLogger<TutupBukuTahunRepository>.Instance);
        }

        public void Dispose() => _connection.Dispose();

        private TutupBukuTahunService CreateService(DateTime? sekarang = null) =>
            new(_repo, _verifikasi, NullLogger<TutupBukuTahunService>.Instance,
                sekarang.HasValue ? () => sekarang.Value : null);

        // ---------- helper verifikasi palsu ----------

        private sealed class FakeVerifikasi : IVerifikasiPenomoranService
        {
            public HasilVerifikasiNomor Hasil { get; set; } = Sepi();
            public List<int> Ditanya { get; } = new();

            public Task<List<int>> AmbilTahunTersediaAsync() =>
                Task.FromResult(new List<int> { 2025, 2026 });

            public Task<HasilVerifikasiNomor> PeriksaAsync(int tahun)
            {
                Ditanya.Add(tahun);
                return Task.FromResult(Hasil);
            }

            public static HasilVerifikasiNomor Sepi(int tahun = 2025) => new()
            {
                Tahun = tahun,
                Deret = Array.Empty<DeretNomor>(),
                NomorTahunTidakCocok = Array.Empty<string>(),
                TahunTersedia = new[] { 2025, 2026 }
            };

            public static HasilVerifikasiNomor Rapi(int tahun = 2025, int jumlah = 12, string awalan = "470")
            {
                var deret = new List<DeretNomor>
                {
                    new()
                    {
                        Awalan = awalan,
                        Tahun = tahun,
                        Jumlah = jumlah,
                        NomorPertama = 1,
                        NomorTerakhir = jumlah,
                        NamaJenis = new[] { "SKD" }
                    }
                };
                return new HasilVerifikasiNomor
                {
                    Tahun = tahun,
                    Deret = deret,
                    NomorTahunTidakCocok = Array.Empty<string>(),
                    TahunTersedia = new[] { 2025, 2026 }
                };
            }
        }

        // ---------- status ----------

        [Fact]
        public async Task TahunBelumPernahDisentuh_BerstatusTerbuka()
        {
            var status = await CreateService().AmbilStatusAsync(2025);

            Assert.Equal(2025, status.Tahun);
            Assert.Equal(StatusTutupBuku.Terbuka, status.Status);
            Assert.False(status.Tertutup);
        }

        // ---------- menutup ----------

        [Fact]
        public async Task Tutup_MenyimpanStatus_DanSnapshot()
        {
            _verifikasi.Hasil = FakeVerifikasi.Rapi(2025, jumlah: 12);
            var svc = CreateService(new DateTime(2026, 1, 15, 10, 30, 0));

            var hasil = await svc.TutupAsync(2025, "Tahun 2025 reguler", "Operator");

            Assert.True(hasil.Berhasil);
            Assert.Equal(AlasanTolakTutupBuku.TidakAda, hasil.Alasan);
            Assert.Equal(StatusTutupBuku.Tertutup, hasil.Data!.Status);
            Assert.Equal(12, hasil.Data.JumlahSurat);
            Assert.Equal(1, hasil.Data.JumlahDeret);
            Assert.Equal("2026-01-15 10:30:00", hasil.Data.TanggalTutup);
            Assert.Equal("Operator", hasil.Data.DitutupOleh);
            Assert.Equal("Tahun 2025 reguler", hasil.Data.Catatan);

            // Snapshot harus bisa dibaca kembali dan mewakili deret saat tutup.
            var snapshot = JsonSerializer.Deserialize<SnapshotTutupBuku>(hasil.Data.Snapshot!);
            Assert.NotNull(snapshot);
            Assert.Equal(2025, snapshot!.Tahun);
            Assert.Equal(12, snapshot.JumlahSurat);
            Assert.False(snapshot.Dipaksa);
            Assert.Equal("470", Assert.Single(snapshot.Deret).Awalan);
        }

        [Fact]
        public async Task Tutup_TidakMenghapusSurat_LaluDibuka_ReturnsTrue()
        {
            _verifikasi.Hasil = FakeVerifikasi.Rapi(2025, 3);
            var svc = CreateService();

            await svc.TutupAsync(2025, null, null);
            Assert.True((await svc.AmbilStatusAsync(2025)).Tertutup);

            await svc.BukaAsync(2025, "Ada koreksi", "Operator");

            var setelah = await svc.AmbilStatusAsync(2025);
            Assert.False(setelah.Tertutup);
            Assert.Null(setelah.Snapshot);
            Assert.Null(setelah.TanggalTutup);
            Assert.Equal("Operator", setelah.DibukaOleh);
            Assert.Contains("Ada koreksi", setelah.Catatan);
        }

        [Fact]
        public async Task Tutup_TidakMenumpukBaris_SaatDitutup_Ulang()
        {
            _verifikasi.Hasil = FakeVerifikasi.Rapi(2025, 5);
            var svc = CreateService();

            await svc.TutupAsync(2025, "pertama", "A");
            await svc.BukaAsync(2025, "dibuka lagi", "A");
            await svc.TutupAsync(2025, "kedua", "B");

            var semua = await svc.AmbilSemuaAsync();
            TutupBukuTahun satu = Assert.Single(semua);
            Assert.Equal("kedua", satu.Catatan);
            Assert.Equal("B", satu.DitutupOleh);
        }

        // ---------- guard ----------

        [Fact]
        public async Task Tutup_Ditolak_JikaTahunMendatang()
        {
            _verifikasi.Hasil = FakeVerifikasi.Rapi(2030);
            var svc = CreateService(new DateTime(2026, 1, 15, 0, 0, 0));

            var hasil = await svc.TutupAsync(2030, null, null);

            Assert.False(hasil.Berhasil);
            Assert.Equal(AlasanTolakTutupBuku.TahunMendatang, hasil.Alasan);
            Assert.Empty(await svc.AmbilSemuaAsync());
        }

        [Fact]
        public async Task Tutup_Ditolak_JikaTahunTidakWajar()
        {
            var hasil = await CreateService().TutupAsync(12, null, null);

            Assert.False(hasil.Berhasil);
            Assert.Equal(AlasanTolakTutupBuku.TahunTidakWajar, hasil.Alasan);
        }

        [Fact]
        public async Task Tutup_Ditolak_JikaSudahTertutup()
        {
            _verifikasi.Hasil = FakeVerifikasi.Rapi(2025, 4);
            var svc = CreateService();

            await svc.TutupAsync(2025, null, null);
            var kedua = await svc.TutupAsync(2025, null, null);

            Assert.False(kedua.Berhasil);
            Assert.Equal(AlasanTolakTutupBuku.SudahTertutup, kedua.Alasan);
        }

        [Fact]
        public async Task Tutup_Ditolak_JikaTidakAdaSurat()
        {
            _verifikasi.Hasil = FakeVerifikasi.Sepi(2025);
            var hasil = await CreateService().TutupAsync(2025, null, null);

            Assert.False(hasil.Berhasil);
            Assert.Equal(AlasanTolakTutupBuku.TidakAdaSurat, hasil.Alasan);
        }

        [Fact]
        public async Task Tutup_Ditolak_JikaAdaMasalahBlocking_TanpaPaksa()
        {
            _verifikasi.Hasil = new HasilVerifikasiNomor
            {
                Tahun = 2025,
                Deret = new[]
                {
                    new DeretNomor
                    {
                        Awalan = "470",
                        Tahun = 2025,
                        Jumlah = 3,
                        NomorPertama = 1,
                        NomorTerakhir = 3,
                        NomorGanda = new[] { 2 },
                        NamaJenis = new[] { "SKD" }
                    }
                },
                NomorTahunTidakCocok = new[] { "470/002/Ds/2024" },
                TahunTersedia = new[] { 2025 }
            };

            var hasil = await CreateService().TutupAsync(2025, null, null, paksa: false);

            Assert.False(hasil.Berhasil);
            Assert.Equal(AlasanTolakTutupBuku.BlockingTanpaPaksa, hasil.Alasan);
            Assert.Contains("470/002/Ds/2024", hasil.Pesan);
            Assert.Empty(await _repo.GetAllAsync());
        }

        [Fact]
        public async Task Tutup_BolehDipaksa_TetapiPaksaTercatatDiSnapshot()
        {
            _verifikasi.Hasil = new HasilVerifikasiNomor
            {
                Tahun = 2025,
                Deret = new[]
                {
                    new DeretNomor
                    {
                        Awalan = "470",
                        Tahun = 2025,
                        Jumlah = 3,
                        NomorPertama = 1,
                        NomorTerakhir = 3,
                        NomorGanda = new[] { 2 }
                    }
                },
                NomorTahunTidakCocok = Array.Empty<string>(),
                TahunTersedia = new[] { 2025 }
            };

            var hasil = await CreateService().TutupAsync(2025, "Nomor 002 dobel, sudah dikonfirmasi", "Operator", paksa: true);

            Assert.True(hasil.Berhasil);
            var snapshot = JsonSerializer.Deserialize<SnapshotTutupBuku>(hasil.Data!.Snapshot!);
            Assert.True(snapshot!.Dipaksa);
            Assert.Equal("Nomor 002 dobel, sudah dikonfirmasi", snapshot.Catatan);
        }

        [Fact]
        public async Task Buka_Ditolak_JikaBukuBelumTertutup()
        {
            var hasil = await CreateService().BukaAsync(2025, null, null);

            Assert.False(hasil.Berhasil);
            Assert.Equal(AlasanTolakTutupBuku.BelumTertutup, hasil.Alasan);
        }

        // ---------- peringatan non-blocking ----------

        [Fact]
        public async Task NomorHilang_TidakMemblokirTutup_OnlyPeringatan()
        {
            _verifikasi.Hasil = new HasilVerifikasiNomor
            {
                Tahun = 2025,
                Deret = new[]
                {
                    new DeretNomor
                    {
                        Awalan = "470",
                        Tahun = 2025,
                        Jumlah = 3,
                        NomorPertama = 1,
                        NomorTerakhir = 5,
                        NomorHilang = new[] { 2, 3 }
                    }
                },
                NomorTahunTidakCocok = Array.Empty<string>(),
                TahunTersedia = new[] { 2025 }
            };

            var hasil = await CreateService().TutupAsync(2025, null, null);

            Assert.True(hasil.Berhasil);
            Assert.Single(hasil.Peringatan);
            Assert.Contains("002, 003", hasil.Peringatan[0]);
        }

        [Fact]
        public void PeringatanRingkas_TidakMembanjiriNomorHilang()
        {
            var hasil = new HasilVerifikasiNomor
            {
                Tahun = 2025,
                Deret = new[]
                {
                    new DeretNomor
                    {
                        Awalan = "470",
                        Tahun = 2025,
                        Jumlah = 2,
                        NomorPertama = 1,
                        NomorTerakhir = 20,
                        NomorHilang = Enumerable.Range(2, 18).ToList()
                    }
                },
                NomorTahunTidakCocok = Array.Empty<string>(),
                TahunTersedia = new[] { 2025 }
            };

            string pesan = Assert.Single(hasil.Peringatan());
            Assert.Contains("002, 003, 004", pesan);
            Assert.Contains("(+15)", pesan);
        }

        // ---------- repository ----------

        [Fact]
        public async Task Repository_Upsert_Menimpa_SatuBarisPerTahun()
        {
            await _repo.UpsertAsync(new TutupBukuTahun
            {
                Tahun = 2025,
                Status = StatusTutupBuku.Tertutup,
                JumlahSurat = 7,
                DitutupOleh = "A"
            });

            await _repo.UpsertAsync(new TutupBukuTahun
            {
                Tahun = 2025,
                Status = StatusTutupBuku.Tertutup,
                JumlahSurat = 9,
                DitutupOleh = "B"
            });

            TutupBukuTahun data = Assert.Single(await _repo.GetAllAsync());
            Assert.Equal(9, data.JumlahSurat);
            Assert.Equal("B", data.DitutupOleh);
            Assert.NotNull(data.CreatedAt);
        }

        [Fact]
        public async Task Repository_MenolakStatusDiLuarYangDiizinkan()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => _repo.UpsertAsync(new TutupBukuTahun
            {
                Tahun = 2025,
                Status = "MENGHAPUS"
            }));
        }

        [Fact]
        public async Task Repository_AmbilSemua_UrutTahunTurun()
        {
            foreach (int tahun in new[] { 2024, 2026, 2025 })
            {
                await _repo.UpsertAsync(new TutupBukuTahun { Tahun = tahun });
            }

            var semua = await _repo.GetAllAsync();

            Assert.Equal(new[] { 2026, 2025, 2024 }, semua.Select(t => t.Tahun).ToArray());
        }

        [Fact]
        public async Task Repository_TabelDibuatOtomatis_JikaBelumAda()
        {
            var koneksiBaru = new SqliteConnection("Data Source=:memory:");
            koneksiBaru.Open();
            try
            {
                var repoBaru = new TutupBukuTahunRepository(
                    koneksiBaru, NullLogger<TutupBukuTahunRepository>.Instance);

                await repoBaru.EnsureTableAsync();
                await repoBaru.UpsertAsync(new TutupBukuTahun
                {
                    Tahun = 2026,
                    Status = StatusTutupBuku.Tertutup
                });

                Assert.NotNull(await repoBaru.GetAsync(2026));
            }
            finally
            {
                koneksiBaru.Dispose();
            }
        }

        [Fact]
        public async Task Tutup_LaluBuka_Berulang_TetapSatuBarisPerTahun()
        {
            _verifikasi.Hasil = FakeVerifikasi.Rapi(2025, 6);
            var svc = CreateService();

            for (int i = 0; i < 3; i++)
            {
                Assert.True((await svc.TutupAsync(2025, $"putaran {i}", "Op")).Berhasil);
                Assert.True((await svc.BukaAsync(2025, $"buka {i}", "Op")).Berhasil);
            }

            var status = await svc.AmbilStatusAsync(2025);
            Assert.Equal(StatusTutupBuku.Terbuka, status.Status);
            Assert.Equal(6, status.JumlahSurat);
            Assert.Single(await svc.AmbilSemuaAsync());
        }
    }
}
