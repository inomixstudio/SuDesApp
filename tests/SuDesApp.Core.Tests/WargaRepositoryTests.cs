using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;
using Xunit;

namespace SuDesApp.Core.Tests
{
    /// <summary>
    /// Uji halaman Data Warga: migrasi skema database lama, paging/filter,
    /// mutasi, Kartu Keluarga, dan rekapitulasi. Semua memakai SQLite in-memory
    /// supaya tidak menyentuh berkas database pengguna.
    /// </summary>
    public sealed class WargaRepositoryTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly WargaRepository _repo;

        public WargaRepositoryTests()
        {
            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();

            // Skema dasar persis seperti yang dipakai CoreTestFixture.
            _connection.ExecuteScript(
                CoreTestFixture.ReadProjectFile("desa.db.sql"));
            _connection.ExecuteScript(
                "ALTER TABLE Surat ADD COLUMN Status TEXT NOT NULL DEFAULT 'Draft';" +
                "ALTER TABLE Surat ADD COLUMN KodeJenis TEXT NULL;" +
                "ALTER TABLE Surat ADD COLUMN AdditionalData TEXT NULL;" +
                "ALTER TABLE Surat ADD COLUMN CreatedAt TEXT NULL;" +
                "ALTER TABLE Surat ADD COLUMN UpdatedAt TEXT NULL;" +
                "ALTER TABLE JenisSurat ADD COLUMN Deskripsi TEXT NULL;" +
                "ALTER TABLE SKU ADD COLUMN LokasiUsaha TEXT NULL;");

            // desa.db.sql menyertakan satu baris warga contoh untuk pemasangan
            // baru; uji di sini menghitung baris sendiri jadi dibersihkan dulu.
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
        }

        public void Dispose() => _connection.Dispose();

        // ---------- Migrasi ----------

        [Fact]
        public async Task Migrasi_TabelWargaLama_DitambahSemuaKolomBaru()
        {
            // Ganti tabel hasil desa.db.sql dengan bentuk "database lama" milik
            // pengguna yang sudah terlanjur memakai aplikasi versi sebelumnya.
            _connection.ExecuteNonQuery("DROP TABLE Warga;");
            _connection.ExecuteNonQuery(@"
                CREATE TABLE Warga (
                    ID_Warga INTEGER PRIMARY KEY AUTOINCREMENT,
                    NIK TEXT NOT NULL UNIQUE,
                    Nama TEXT NOT NULL,
                    TempatLahir TEXT,
                    TanggalLahir TEXT,
                    JenisKelamin TEXT,
                    Agama TEXT,
                    StatusPerkawinan TEXT,
                    Pekerjaan TEXT,
                    Alamat TEXT,
                    Pendidikan TEXT,
                    Kewarganegaraan TEXT
                );
                INSERT INTO Warga (NIK, Nama, TempatLahir, TanggalLahir, JenisKelamin, Agama,
                                   StatusPerkawinan, Pekerjaan, Alamat, Pendidikan, Kewarganegaraan)
                VALUES ('3204010101800009', 'Warga Lama', 'Bandung', '1975-05-05', 'Laki-laki',
                        'Islam', 'Kawin', 'Petani', 'Kp. Lama', 'SMA', 'WNI');");

            await _repo.InitializeWargaTableAsync();

            var kolom = KolomTabel(_connection, "Warga");
            foreach (var wajib in new[]
                     {
                         "NoKK", "RT", "RW", "NomorHP", "NamaAyah", "NamaIbu", "GolonganDarah",
                         "AlamatDetail", "StatusWarga", "TanggalStatus", "KeteranganWarga",
                         "Dusun", "Desa", "Kecamatan", "Kabupaten", "CreatedAt", "UpdatedAt"
                     })
            {
                Assert.Contains(wajib, kolom);
            }

            // Data lama harus tetap ada dan dibaca sebagai AKTIF.
            var hasil = await _repo.GetWargaPageAsync(new WargaFilter());
            Assert.Single(hasil.Items);
            Assert.Equal(StatusWargaTipe.Aktif, hasil.Items[0].StatusWarga);
        }

        [Fact]
        public async Task Migrasi_DijalankanDuaKali_TetapAman()
        {
            await _repo.InitializeWargaTableAsync();
            await _repo.InitializeWargaTableAsync();

            var hasil = await _repo.GetWargaPageAsync(new WargaFilter());
            Assert.NotNull(hasil);
        }

        [Fact]
        public async Task Migrasi_TabelKartuKeluarga_Dibuat()
        {
            await _repo.InitializeWargaTableAsync();

            using var command = _connection.CreateCommand();
            command.CommandText =
                "SELECT name FROM sqlite_master WHERE type='table' AND name='KartuKeluarga'";
            Assert.Equal("KartuKeluarga", command.ExecuteScalar());
        }

        // ---------- Dummy & paging ----------

        [Fact]
        public async Task Daftar_BarisDummyInstansiDanKematian_TidakMuncul()
        {
            await _repo.InitializeWargaTableAsync();
            await _repo.AddOrUpdateWargaAndGetIdAsync(Warga("3204010101800001", "Warga Asli"));

            // Baris dummy dibuat alur pembuatan surat (bukan lewat API warga),
            // jadi disisipkan langsung supaya bisa diuji penapisannya.
            _connection.ExecuteNonQuery(@"
                INSERT INTO Warga (NIK, Nama, TempatLahir, TanggalLahir, JenisKelamin, Agama,
                                   StatusPerkawinan, Desa, Kecamatan, Kabupaten, Kewarganegaraan, StatusWarga)
                VALUES ('9999999999999999', 'Instansi', 'Bandung', '1980-01-01', 'L', 'Islam',
                        'Belum Kawin', 'Desa Uji', 'Kec. Uji', 'Kab. Uji', 'WNI', 'AKTIF'),
                       ('0000000000000000', 'Kematian', 'Bandung', '1980-01-01', 'L', 'Islam',
                        'Belum Kawin', 'Desa Uji', 'Kec. Uji', 'Kab. Uji', 'WNI', 'MENINGGAL');");

            var hasil = await _repo.GetWargaPageAsync(new WargaFilter());

            Assert.Single(hasil.Items);
            Assert.Equal("Warga Asli", hasil.Items[0].Nama);
        }

        [Fact]
        public async Task Paging_HalamanKedua_TidakBerulangDanTotalBenar()
        {
            await _repo.InitializeWargaTableAsync();
            for (int i = 1; i <= 25; i++)
            {
                await _repo.AddOrUpdateWargaAndGetIdAsync(
                    Warga($"32040101018000{i:D2}", $"Warga {i:D2}", rt: "001"));
            }

            var halaman1 = await _repo.GetWargaPageAsync(new WargaFilter { Halaman = 1, UkuranHalaman = 10 });
            var halaman2 = await _repo.GetWargaPageAsync(new WargaFilter { Halaman = 2, UkuranHalaman = 10 });
            var halaman3 = await _repo.GetWargaPageAsync(new WargaFilter { Halaman = 3, UkuranHalaman = 10 });

            Assert.Equal(25, halaman1.Total);
            Assert.Equal(3, halaman1.TotalHalaman);
            Assert.Equal(10, halaman1.Items.Count);
            Assert.Equal(10, halaman2.Items.Count);
            Assert.Equal(5, halaman3.Items.Count);

            var nikMuncul = halaman1.Items.Concat(halaman2.Items).Concat(halaman3.Items)
                .Select(w => w.NIK).ToList();
            Assert.Equal(25, nikMuncul.Distinct().Count());
        }

        [Fact]
        public async Task Paging_HalamanMelebihiBatas_DikembalikanKeHalamanTerakhir()
        {
            await _repo.InitializeWargaTableAsync();
            await _repo.AddOrUpdateWargaAndGetIdAsync(Warga("3204010101800001", "Warga Asli"));

            // Halaman 9 dari total 1 halaman: repository tidak boleh error atau
            // menampilkan tabel kosong — operator harus tetap melihat datanya.
            var hasil = await _repo.GetWargaPageAsync(new WargaFilter { Halaman = 9, UkuranHalaman = 10 });

            Assert.Single(hasil.Items);
            Assert.Equal(1, hasil.Total);
            Assert.Equal(1, hasil.Halaman);
        }

        // ---------- Filter ----------

        [Fact]
        public async Task Filter_RT_HanyaMengambilWargaRTtersebut()
        {
            await _repo.InitializeWargaTableAsync();
            await _repo.AddOrUpdateWargaAndGetIdAsync(Warga("3204010101800001", "Warga RT 001", rt: "001"));
            await _repo.AddOrUpdateWargaAndGetIdAsync(Warga("3204010101800002", "Warga RT 002", rt: "002"));

            var hasil = await _repo.GetWargaPageAsync(new WargaFilter { RT = "001" });

            Assert.Single(hasil.Items);
            Assert.Equal("Warga RT 001", hasil.Items[0].Nama);
        }

        [Fact]
        public async Task Filter_Dusun_TidakPekaBesarKecil()
        {
            await _repo.InitializeWargaTableAsync();
            await _repo.AddOrUpdateWargaAndGetIdAsync(
                Warga("3204010101800001", "Warga Dusun", dusun: "Dusun I"));

            // Dropdown wilayah menyimpan dusun dalam huruf besar sebagai kunci.
            var hasil = await _repo.GetWargaPageAsync(new WargaFilter { Dusun = "DUSUN I" });

            Assert.Single(hasil.Items);
        }

        [Fact]
        public async Task Filter_Cari_MencakupNamaNikDanAlamat()
        {
            await _repo.InitializeWargaTableAsync();
            await _repo.AddOrUpdateWargaAndGetIdAsync(
                Warga("3204010101800001", "Budi Santoso", rt: "003", alamat: "Kp. Melati"));
            await _repo.AddOrUpdateWargaAndGetIdAsync(
                Warga("3204010101800002", "Warga Kedua", rt: "004"));

            Assert.Single((await _repo.GetWargaPageAsync(new WargaFilter { Cari = "Santoso" })).Items);
            Assert.Single((await _repo.GetWargaPageAsync(new WargaFilter { Cari = "1800001" })).Items);
            Assert.Single((await _repo.GetWargaPageAsync(new WargaFilter { Cari = "Melati" })).Items);
            // RT harus ikut tercari: operator mengetik "003" untuk menemukan alamat.
            Assert.Single((await _repo.GetWargaPageAsync(new WargaFilter { Cari = "003" })).Items);
        }

        [Fact]
        public async Task Filter_Status_MemisahkanAKTIFDanMeninggal()
        {
            await _repo.InitializeWargaTableAsync();
            await _repo.AddOrUpdateWargaAndGetIdAsync(Warga("3204010101800001", "Masih tinggal"));
            var idMeninggal = await _repo.AddOrUpdateWargaAndGetIdAsync(
                Warga("3204010101800002", "Sudah meninggal"));
            await _repo.UbahStatusWargaAsync(idMeninggal, StatusWargaTipe.Meninggal, "2026-01-01", "Sakit");

            var aktif = await _repo.GetWargaPageAsync(new WargaFilter { Status = StatusWargaTipe.Aktif });
            var meninggal = await _repo.GetWargaPageAsync(new WargaFilter { Status = StatusWargaTipe.Meninggal });

            Assert.Single(aktif.Items);
            Assert.Equal("Masih tinggal", aktif.Items[0].Nama);
            Assert.Single(meninggal.Items);
            Assert.Equal("Sudah meninggal", meninggal.Items[0].Nama);
        }

        // ---------- Mutasi ----------

        [Fact]
        public async Task Mutasi_StatusTanggalDanKeteranganTersimpan()
        {
            await _repo.InitializeWargaTableAsync();
            var id = await _repo.AddOrUpdateWargaAndGetIdAsync(Warga("3204010101800001", "Budi"));

            await _repo.UbahStatusWargaAsync(id, StatusWargaTipe.Pindah, "2026-03-04", "Pindah ke Bandung");

            var warga = await _repo.GetWargaByIdAsync(id);
            Assert.NotNull(warga);
            Assert.Equal(StatusWargaTipe.Pindah, warga!.StatusWarga);
            Assert.Equal("2026-03-04", warga.TanggalStatus);
            Assert.Equal("Pindah ke Bandung", warga.KeteranganWarga);
        }

        [Fact]
        public async Task Mutasi_StatusTidakDikenal_Ditolak()
        {
            await _repo.InitializeWargaTableAsync();
            var id = await _repo.AddOrUpdateWargaAndGetIdAsync(Warga("3204010101800001", "Budi"));

            await Assert.ThrowsAnyAsync<Exception>(() =>
                _repo.UbahStatusWargaAsync(id, "HILANG", "2026-01-01", null));

            var warga = await _repo.GetWargaByIdAsync(id);
            Assert.Equal(StatusWargaTipe.Aktif, warga!.StatusWarga);
        }

        [Fact]
        public async Task Simpan_WargaBaru_StatusDanTanggalStatusIkutTersimpan()
        {
            await _repo.InitializeWargaTableAsync();

            await _repo.AddOrUpdateWargaAndGetIdAsync(new WargaData
            {
                NIK = "3204010101800001",
                Nama = "Warga Baru",
                TempatLahir = "Bandung",
                TanggalLahir = "1990-02-03",
                JenisKelamin = "L",
                Agama = "Islam",
                StatusPerkawinan = "Belum Kawin",
                StatusWarga = StatusWargaTipe.Baru,
                TanggalStatus = "2026-02-03",
                Desa = "Desa Uji",
                Kecamatan = "Kec. Uji",
                Kabupaten = "Kab. Uji"
            });

            var warga = await _repo.GetWargaByNikAsync("3204010101800001");
            Assert.NotNull(warga);
            Assert.Equal(StatusWargaTipe.Baru, warga!.StatusWarga);
            Assert.Equal("2026-02-03", warga.TanggalStatus);
        }

        // ---------- Kartu Keluarga ----------

        [Fact]
        public async Task KartuKeluarga_DibuatDanDapatDibaca()
        {
            await _repo.InitializeWargaTableAsync();
            const string noKK = "3204010101110001";
            await _repo.AddOrUpdateWargaAndGetIdAsync(Warga("3204010101800001", "Budi", noKK: noKK, rt: "001", rw: "002"));

            await _repo.SaveKartuKeluargaAsync(new KartuKeluargaData
            {
                NoKK = noKK,
                NamaKepalaKeluarga = "Budi",
                RT = "001",
                RW = "002",
                Dusun = "Dusun I",
                Desa = "Desa Uji",
                Kecamatan = "Kec. Uji",
                Kabupaten = "Kab. Uji"
            });

            var kk = await _repo.GetKartuKeluargaAsync(noKK);

            Assert.NotNull(kk);
            Assert.Equal("Budi", kk!.NamaKepalaKeluarga);
            Assert.Equal("001", kk.RT);
            Assert.Equal("002", kk.RW);
            Assert.Equal(1, kk.JumlahAnggota);
        }

        [Fact]
        public async Task KartuKeluarga_Kosong_TidakAda()
        {
            await _repo.InitializeWargaTableAsync();

            Assert.Null(await _repo.GetKartuKeluargaAsync("3204010101110001"));
        }

        [Fact]
        public async Task AnggotaKeluarga_UrutBerdasarkanNama()
        {
            await _repo.InitializeWargaTableAsync();
            const string noKK = "3204010101110001";
            await _repo.AddOrUpdateWargaAndGetIdAsync(Warga("3204010101800003", "Zainal", noKK: noKK));
            await _repo.AddOrUpdateWargaAndGetIdAsync(Warga("3204010101800001", "Andi", noKK: noKK));
            await _repo.AddOrUpdateWargaAndGetIdAsync(Warga("3204010101800002", "Budi", noKK: noKK));
            await _repo.AddOrUpdateWargaAndGetIdAsync(Warga("3204010101800004", "Lain KK", noKK: "3204010101110002"));

            var anggota = await _repo.GetAnggotaKeluargaAsync(noKK);

            Assert.Equal(new[] { "Andi", "Budi", "Zainal" }, anggota.Select(a => a.Nama).ToArray());
        }

        // ---------- Rekapitulasi ----------

        [Fact]
        public async Task Statistik_MenghitungPerJenisKelaminDanStatus()
        {
            await _repo.InitializeWargaTableAsync();
            await _repo.AddOrUpdateWargaAndGetIdAsync(Warga("3204010101800001", "Budi", jk: "L"));
            await _repo.AddOrUpdateWargaAndGetIdAsync(Warga("3204010101800002", "Siti", jk: "P"));
            var idMeninggal = await _repo.AddOrUpdateWargaAndGetIdAsync(Warga("3204010101800003", "Tua", jk: "L"));
            await _repo.UbahStatusWargaAsync(idMeninggal, StatusWargaTipe.Meninggal, "2026-01-01", null);

            var s = await _repo.GetStatistikWargaAsync();

            Assert.Equal(3, s.TotalSeluruh);
            Assert.Equal(2, s.TotalAktif);
            Assert.Equal(1, s.TotalMeninggal);
            Assert.Equal(0, s.TotalPindah);
            Assert.Equal(2, s.LakiLaki);
            Assert.Equal(1, s.Perempuan);
        }

        [Fact]
        public async Task Statistik_KelompokUsia_TidakSemuaTidakDiketahui()
        {
            await _repo.InitializeWargaTableAsync();
            // Seluruhnya format ISO yang bisa dibaca SQLite.
            await _repo.AddOrUpdateWargaAndGetIdAsync(
                Warga("3204010101800001", "Bayi", tanggalLahir: DateTime.Today.AddYears(-2).ToString("yyyy-MM-dd")));
            await _repo.AddOrUpdateWargaAndGetIdAsync(
                Warga("3204010101800002", "Remaja", tanggalLahir: DateTime.Today.AddYears(-19).ToString("yyyy-MM-dd")));
            await _repo.AddOrUpdateWargaAndGetIdAsync(
                Warga("3204010101800003", "Dewasa",
                    tanggalLahir: DateTime.Today.AddYears(-35).ToString("yyyy-MM-dd")));

            var s = await _repo.GetStatistikWargaAsync();

            var tidakDiketahui = s.PerKelompokUsia.FirstOrDefault(b => b.Kunci == "(tidak diketahui)");
            Assert.NotNull(tidakDiketahui);
            Assert.Equal(0, tidakDiketahui!.Jumlah);
            Assert.Equal(0, s.UsiaTidakDiketahui);
            Assert.Equal(3, s.PerKelompokUsia.Sum(b => b.Jumlah));
        }

        [Fact]
        public async Task Statistik_PerRt_Grouping_Benar()
        {
            await _repo.InitializeWargaTableAsync();
            await _repo.AddOrUpdateWargaAndGetIdAsync(Warga("3204010101800001", "Andi", rt: "001"));
            await _repo.AddOrUpdateWargaAndGetIdAsync(Warga("3204010101800002", "Budi", rt: "001"));
            await _repo.AddOrUpdateWargaAndGetIdAsync(Warga("3204010101800003", "Candra", rt: "002"));

            var s = await _repo.GetStatistikWargaAsync();

            var rt1 = s.PerRt.FirstOrDefault(b => b.Kunci == "001");
            var rt2 = s.PerRt.FirstOrDefault(b => b.Kunci == "002");
            Assert.NotNull(rt1);
            Assert.NotNull(rt2);
            Assert.Equal(2, rt1!.Jumlah);
            Assert.Equal(1, rt2!.Jumlah);
        }

        // ---------- Ekspor ----------

        [Fact]
        public async Task Ekspor_MenghormatiFilterDanMengabaikanDummy()
        {
            await _repo.InitializeWargaTableAsync();
            await _repo.AddOrUpdateWargaAndGetIdAsync(Warga("3204010101800001", "RT 001", rt: "001"));
            await _repo.AddOrUpdateWargaAndGetIdAsync(Warga("3204010101800002", "RT 002", rt: "002"));

            // Baris dummy dibuat alur pembuatan surat, bukan lewat API warga.
            _connection.ExecuteNonQuery(@"
                INSERT INTO Warga (NIK, Nama, TempatLahir, TanggalLahir, JenisKelamin, Agama,
                                   StatusPerkawinan, RT, Desa, Kecamatan, Kabupaten, Kewarganegaraan, StatusWarga)
                VALUES ('9999999999999999', 'Instansi', 'Bandung', '1980-01-01', 'L', 'Islam',
                        'Belum Kawin', '001', 'Desa Uji', 'Kec. Uji', 'Kab. Uji', 'WNI', 'AKTIF');");

            var semua = await _repo.GetWargaForExportAsync(new WargaFilter());
            var filtered = await _repo.GetWargaForExportAsync(new WargaFilter { RT = "001" });

            Assert.Equal(2, semua.Count);
            Assert.DoesNotContain(semua, w => w.NIK == "9999999999999999");
            Assert.Single(filtered);
            Assert.Equal("RT 001", filtered[0].Nama);
        }

        // ---------- Bantuan ----------

        private static WargaData Warga(
            string nik, string nama,
            string? jk = "L", string? rt = null, string? rw = null,
            string? dusun = null, string? alamat = null, string? noKK = null,
            string? tanggalLahir = null) => new()
        {
            NIK = nik,
            Nama = nama,
            TempatLahir = "Bandung",
            TanggalLahir = tanggalLahir ?? "1990-01-01",
            JenisKelamin = jk,
            Agama = "Islam",
            StatusPerkawinan = "Belum Kawin",
            RT = rt,
            RW = rw,
            Dusun = dusun,
            AlamatDetail = alamat,
            NoKK = noKK,
            Desa = "Desa Uji",
            Kecamatan = "Kec. Uji",
            Kabupaten = "Kab. Uji",
            Kewarganegaraan = "WNI"
        };

        private static List<string> KolomTabel(SqliteConnection connection, string tabel)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info({tabel});";
            using var reader = command.ExecuteReader();
            var kolom = new List<string>();
            while (reader.Read()) kolom.Add(reader.GetString(1));
            return kolom;
        }
    }
}
