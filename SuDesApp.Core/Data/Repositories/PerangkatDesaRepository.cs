using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;

namespace SuDesApp.Data.Repositories
{
    public interface IPerangkatDesaRepository
    {
        Task EnsureTableAsync(CancellationToken cancellationToken = default);
        Task<PerangkatDesa?> GetAsync(int id, CancellationToken cancellationToken = default);
        Task<List<PerangkatDesa>> GetAllAsync(CancellationToken cancellationToken = default);

        /// <summary>Daftar perangkat desa sesuai filter, urut jabatan lalu nama.</summary>
        Task<List<PerangkatDesa>> SearchAsync(PerangkatDesaFilter filter, CancellationToken cancellationToken = default);

        Task<int> InsertAsync(PerangkatDesa data, CancellationToken cancellationToken = default);
        Task UpdateAsync(PerangkatDesa data, CancellationToken cancellationToken = default);
        Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);

        /// <summary>Nilai dusun, RT, dan RW yang pernah dipakai, untuk mengisi filter.</summary>
        Task<IReadOnlyList<string>> GetDaftarNilaiWilayahAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// True bila jabatan yang sama pada wilayah yang sama masih dipegang
        /// orang lain. Hanya baris yang masih memegang jabatan (AKTIF atau
        /// MENUNGGU SK) yang dihitung: baris SELESAI atau BERHENTi adalah riwayat
        /// orang sebelumnya, bukan penghalang, supaya pergantian Kepala Desa,
        /// Ketua RT, atau Ketua BPD tetap bisa dicatat.
        /// </summary>
        Task<bool> JabatanSudahDipakaiAsync(
            string jabatan, string? dusun, string? rt, string? rw,
            int? selainId = null, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Penyimpanan data perangkat desa (Kepala Desa sampai Ketua RT/RW,
    /// Linmas, Posyandu, PKK, dan BPD).
    /// </summary>
    public class PerangkatDesaRepository : IPerangkatDesaRepository
    {
        private const string KolomSelect = @"
            ID, Nama, Jabatan, NIP, NIK, JenisKelamin, TempatLahir, TanggalLahir,
            Pendidikan, Alamat, Dusun, RT, RW, NomorHP, WhatsApp, Unit,
            NomorSK, TanggalSK, BerkasSK, MasaJabatanMulai, MasaJabatanSelesai, Status, Catatan,
            DibuatOleh, DiperbaruiOleh, CreatedAt, UpdatedAt";

        private readonly SqliteConnection _connection;
        private readonly ILogger<PerangkatDesaRepository> _logger;

        public PerangkatDesaRepository(
            SqliteConnection connection,
            ILogger<PerangkatDesaRepository> logger)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Membuat tabel bila belum ada. DDL-nya sengaja sama dengan yang ada di
        /// <c>DatabaseInitializer</c> supaya pengujian bisa menyiapkan tabelnya
        /// sendiri tanpa menjalankan seluruh migrasi.
        /// </summary>
        public async Task EnsureTableAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await BukaAsync(cancellationToken).ConfigureAwait(false);

                await _connection.ExecuteAsync(new CommandDefinition(Ddl.CreateTable + Ddl.Index, cancellationToken: cancellationToken))
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyiapkan tabel PerangkatDesa");
                throw new DataAccessException("Gagal menginisialisasi tabel PerangkatDesa", ex);
            }
        }

        public async Task<PerangkatDesa?> GetAsync(int id, CancellationToken cancellationToken = default)
        {
            try
            {
                await BukaAsync(cancellationToken).ConfigureAwait(false);
                return await _connection.QuerySingleOrDefaultAsync<PerangkatDesa>(new CommandDefinition(
                    $"SELECT {KolomSelect} FROM PerangkatDesa WHERE ID = @ID",
                    new { ID = id }, cancellationToken: cancellationToken)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membaca perangkat desa #{ID}", id);
                throw new DataAccessException("Gagal membaca data perangkat desa", ex);
            }
        }

        public async Task<List<PerangkatDesa>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await BukaAsync(cancellationToken).ConfigureAwait(false);
                var rows = await _connection.QueryAsync<PerangkatDesa>(new CommandDefinition(
                    $"SELECT {KolomSelect} FROM PerangkatDesa ORDER BY Jabatan, Nama",
                    cancellationToken: cancellationToken)).ConfigureAwait(false);
                return rows.AsList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membaca daftar perangkat desa");
                throw new DataAccessException("Gagal membaca daftar perangkat desa", ex);
            }
        }

        public async Task<List<PerangkatDesa>> SearchAsync(
            PerangkatDesaFilter filter, CancellationToken cancellationToken = default)
        {
            var syarat = new List<string>();
            var param = new DynamicParameters();

            if (!string.IsNullOrWhiteSpace(filter.Cari))
            {
                string cari = $"%{filter.Cari.Trim()}%";
                syarat.Add(@"(Nama LIKE @Cari OR Jabatan LIKE @Cari OR IFNULL(NIP,'') LIKE @Cari
                            OR IFNULL(NIK,'') LIKE @Cari OR IFNULL(NomorHP,'') LIKE @Cari
                            OR IFNULL(WhatsApp,'') LIKE @Cari OR IFNULL(NomorSK,'') LIKE @Cari
                            OR IFNULL(Alamat,'') LIKE @Cari OR IFNULL(Dusun,'') LIKE @Cari
                            OR IFNULL(Unit,'') LIKE @Cari)");
                param.Add("Cari", cari);
            }

            if (!string.IsNullOrWhiteSpace(filter.Jabatan))
            {
                param.Add("Jabatan", filter.Jabatan.Trim());
                syarat.Add("Jabatan = @Jabatan");
            }

            if (!string.IsNullOrWhiteSpace(filter.Status))
            {
                param.Add("Status", filter.Status.Trim());
                syarat.Add("Status = @Status");
            }

            if (!string.IsNullOrWhiteSpace(filter.Wilayah))
            {
                param.Add("Wilayah", filter.Wilayah.Trim());
                syarat.Add("(IFNULL(Dusun,'') = @Wilayah OR IFNULL(RT,'') = @Wilayah OR IFNULL(RW,'') = @Wilayah)");
            }

            if (!string.IsNullOrWhiteSpace(filter.Kelompok))
            {
                // Kelompok dihitung dari jabatan (tidak ada kolomnya di database),
                // jadi daftar jabatannya lebih dulu diterjemahkan ke satu syarat IN.
                var jabatanKelompok = JabatanPerangkat.DaftarKelompok(filter.Kelompok);
                if (jabatanKelompok.Count == 0)
                {
                    // Nama kelompok yang tidak dikenal: kembalikan daftar kosong,
                    // bukan seluruh baris — filter yang bocor lebih berbahaya
                    // daripada daftar yang kosong.
                    syarat.Add("0 = 1");
                }
                else
                {
                    param.Add("KelompokJabatan", jabatanKelompok);
                    syarat.Add("Jabatan IN @KelompokJabatan");
                }
            }

            string where = syarat.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", syarat);

            try
            {
                await BukaAsync(cancellationToken).ConfigureAwait(false);
                var rows = await _connection.QueryAsync<PerangkatDesa>(new CommandDefinition(
                    $"SELECT {KolomSelect} FROM PerangkatDesa{where} ORDER BY Jabatan, Nama",
                    param, cancellationToken: cancellationToken)).ConfigureAwait(false);
                return rows.AsList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyaring perangkat desa");
                throw new DataAccessException("Gagal menyaring data perangkat desa", ex);
            }
        }

        public async Task<int> InsertAsync(PerangkatDesa data, CancellationToken cancellationToken = default)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));

            const string sql = @"
                INSERT INTO PerangkatDesa
                    (Nama, Jabatan, NIP, NIK, JenisKelamin, TempatLahir, TanggalLahir,
                     Pendidikan, Alamat, Dusun, RT, RW, NomorHP, WhatsApp, Unit,
                     NomorSK, TanggalSK, BerkasSK, MasaJabatanMulai, MasaJabatanSelesai, Status, Catatan,
                     DibuatOleh, DiperbaruiOleh, CreatedAt, UpdatedAt)
                VALUES
                    (@Nama, @Jabatan, @NIP, @NIK, @JenisKelamin, @TempatLahir, @TanggalLahir,
                     @Pendidikan, @Alamat, @Dusun, @RT, @RW, @NomorHP, @WhatsApp, @Unit,
                     @NomorSK, @TanggalSK, @BerkasSK, @MasaJabatanMulai, @MasaJabatanSelesai, @Status, @Catatan,
                     @DibuatOleh, @DiperbaruiOleh, @CreatedAt, @UpdatedAt);
                SELECT last_insert_rowid();";

            try
            {
                await BukaAsync(cancellationToken).ConfigureAwait(false);
                return await _connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    sql, data, cancellationToken: cancellationToken)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyimpan perangkat desa {Nama}", data.Nama);
                throw new DataAccessException("Gagal menyimpan data perangkat desa", ex);
            }
        }

        public async Task UpdateAsync(PerangkatDesa data, CancellationToken cancellationToken = default)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (data.ID <= 0) throw new ArgumentException("ID perangkat desa belum terisi.", nameof(data));

            const string sql = @"
                UPDATE PerangkatDesa
                   SET Nama = @Nama, Jabatan = @Jabatan, NIP = @NIP, NIK = @NIK,
                       JenisKelamin = @JenisKelamin, TempatLahir = @TempatLahir, TanggalLahir = @TanggalLahir,
                       Pendidikan = @Pendidikan, Alamat = @Alamat, Dusun = @Dusun, RT = @RT, RW = @RW,
                       NomorHP = @NomorHP, WhatsApp = @WhatsApp, Unit = @Unit,
                       NomorSK = @NomorSK, TanggalSK = @TanggalSK, BerkasSK = @BerkasSK,
                       MasaJabatanMulai = @MasaJabatanMulai, MasaJabatanSelesai = @MasaJabatanSelesai,
                       Status = @Status, Catatan = @Catatan,
                       DiperbaruiOleh = @DiperbaruiOleh, UpdatedAt = @UpdatedAt
                 WHERE ID = @ID";

            try
            {
                await BukaAsync(cancellationToken).ConfigureAwait(false);
                await _connection.ExecuteAsync(new CommandDefinition(
                    sql, data, cancellationToken: cancellationToken)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memperbarui perangkat desa #{ID}", data.ID);
                throw new DataAccessException("Gagal memperbarui data perangkat desa", ex);
            }
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
        {
            try
            {
                await BukaAsync(cancellationToken).ConfigureAwait(false);
                int affected = await _connection.ExecuteAsync(new CommandDefinition(
                    "DELETE FROM PerangkatDesa WHERE ID = @ID", new { ID = id },
                    cancellationToken: cancellationToken)).ConfigureAwait(false);
                return affected > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menghapus perangkat desa #{ID}", id);
                throw new DataAccessException("Gagal menghapus data perangkat desa", ex);
            }
        }

        public async Task<IReadOnlyList<string>> GetDaftarNilaiWilayahAsync(
            CancellationToken cancellationToken = default)
        {
            try
            {
                await BukaAsync(cancellationToken).ConfigureAwait(false);

                async Task<List<string>> Ambil(string kolom)
                {
                    var rows = await _connection.QueryAsync<string>(new CommandDefinition(
                        $@"SELECT DISTINCT {kolom} FROM PerangkatDesa
                           WHERE {kolom} IS NOT NULL AND TRIM({kolom}) <> ''
                           ORDER BY {kolom}",
                        cancellationToken: cancellationToken)).ConfigureAwait(false);
                    return rows.ToList();
                }

                var daftar = new List<string>();
                daftar.AddRange(await Ambil("Dusun").ConfigureAwait(false));
                daftar.AddRange(await Ambil("RT").ConfigureAwait(false));
                daftar.AddRange(await Ambil("RW").ConfigureAwait(false));
                return daftar;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengambil daftar nilai wilayah");
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// True bila jabatan pada wilayah itu masih dipegang orang lain. Hanya
        /// status yang masih memegang jabatan (AKTIF atau MENUNGGU SK) yang
        /// dihitung: baris SELESAI atau BERHENTI adalah riwayat orang
        /// sebelumnya, sehingga pergantian Kepala Desa, Ketua RT, atau Ketua BPD
        /// tetap bisa dicatat tanpa harus menghapus riwayat.
        /// </summary>
        public async Task<bool> JabatanSudahDipakaiAsync(
            string jabatan, string? dusun, string? rt, string? rw,
            int? selainId = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(jabatan)) return false;

            // Nama placeholder dibuat dari daftar status, supaya tidak ada
            // tanda kutip yang harus ditempelkan di dalam teks SQL dan tidak
            // ada asumsi jumlah status yang bersifat tetap.
            var parameter = new DynamicParameters();
            var namaStatus = new List<string>(StatusPerangkat.MemegangJabatan.Count);
            for (int i = 0; i < StatusPerangkat.MemegangJabatan.Count; i++)
            {
                string placeholder = "@Status" + i;
                namaStatus.Add(placeholder);
                parameter.Add(placeholder, StatusPerangkat.MemegangJabatan[i]);
            }

            parameter.Add("Jabatan", jabatan.Trim().ToUpperInvariant());
            parameter.Add("Dusun", (dusun ?? string.Empty).Trim());
            parameter.Add("RT", (rt ?? string.Empty).Trim());
            parameter.Add("RW", (rw ?? string.Empty).Trim());
            parameter.Add("SelainId", selainId);

            string sql = @"
                SELECT COUNT(1) FROM PerangkatDesa
                 WHERE Jabatan = @Jabatan
                   AND TRIM(COALESCE(Dusun, '')) = @Dusun
                   AND TRIM(COALESCE(RT, '')) = @RT
                   AND TRIM(COALESCE(RW, '')) = @RW
                   AND Status IN (" + string.Join(", ", namaStatus) + @")
                   AND (@SelainId IS NULL OR ID <> @SelainId)";

            try
            {
                await BukaAsync(cancellationToken).ConfigureAwait(false);
                int jumlah = await _connection.ExecuteScalarAsync<int>(new CommandDefinition(
                    sql, parameter, cancellationToken: cancellationToken)).ConfigureAwait(false);

                return jumlah > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memeriksa jabatan ganda {Jabatan}", jabatan);
                throw new DataAccessException("Gagal memeriksa jabatan yang sama", ex);
            }
        }

        private async Task BukaAsync(CancellationToken cancellationToken)
        {
            if (_connection.State != ConnectionState.Open)
                await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Rapikan nilai kolom Jabatan dari nama lama ke nama resmi terbaru
        /// (lihat <see cref="JabatanPerangkat.NamaLama"/>), supaya baris data lama
        /// tampil, terfilter, dan terkelompokkan di kelompok yang benar tanpa perlu
        /// dibuka-ulang lewat form. Dipanggil DatabaseInitializer setiap aplikasi
        /// dibuka; pernyataan UPDATE tanpa pencocokan berarti baris tak berubah.
        /// </summary>
        public static async Task PindahkanJabatanLamaAsync(SqliteConnection connection)
        {
            foreach (var pasangan in JabatanPerangkat.NamaLama)
            {
                await connection.ExecuteAsync(
                    "UPDATE PerangkatDesa SET Jabatan = @Baru WHERE UPPER(TRIM(Jabatan)) = @Lama",
                    new { Baru = pasangan.Value, Lama = pasangan.Key }).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Tambahkan kolom yang belum ada pada tabel <c>PerangkatDesa</c> milik
        /// database lama, lalu pastikan indeksnya ada. Tabel yang belum pernah
        /// dibuat ikut dibuat lewat <see cref="Ddl.CreateTable"/>, jadi pemanggil
        /// cukup memanggil satu metode ini tanpa memeriksa tabel lebih dulu.
        /// </summary>
        public static async Task EnsurePerangkatDesaAsync(SqliteConnection connection, CancellationToken ct = default)
        {
            await connection.ExecuteAsync(
                new CommandDefinition(Ddl.CreateTable, cancellationToken: ct)).ConfigureAwait(false);

            // Database lama bisa dibuat dari versi skema yang lebih awal, sehingga
            // tidak hanya kolom terbaru (BerkasSK) yang mungkin hilang. Diperiksa
            // SELURUH kolom: satu kolom yang hilang membuat setiap kueri gagal
            // ("no such column") dan halaman Data Perangkat tampil kosong.
            var kolomAda = new HashSet<string>(
                await connection.QueryAsync<string>(new CommandDefinition(
                    "SELECT name FROM pragma_table_info('PerangkatDesa');",
                    cancellationToken: ct)).ConfigureAwait(false),
                StringComparer.OrdinalIgnoreCase);

            foreach (var kolom in Ddl.KolomTambahan)
            {
                if (kolomAda.Contains(kolom.Nama)) continue;

                await connection.ExecuteAsync(new CommandDefinition(
                    $"ALTER TABLE PerangkatDesa ADD COLUMN {kolom.Ddl};",
                    cancellationToken: ct)).ConfigureAwait(false);
            }

            await connection.ExecuteAsync(
                new CommandDefinition(Ddl.Index, cancellationToken: ct)).ConfigureAwait(false);

            await PindahkanJabatanLamaAsync(connection).ConfigureAwait(false);
        }

        /// <summary>DDL tabel, dipakai initializer maupun repository.</summary>
        public static class Ddl
        {
            public const string CreateTable = @"
                CREATE TABLE IF NOT EXISTS PerangkatDesa (
                    ID INTEGER PRIMARY KEY AUTOINCREMENT,
                    Nama TEXT NOT NULL,
                    Jabatan TEXT NOT NULL,
                    NIP TEXT,
                    NIK TEXT,
                    JenisKelamin TEXT,
                    TempatLahir TEXT,
                    TanggalLahir DATETIME,
                    Pendidikan TEXT,
                    Alamat TEXT,
                    Dusun TEXT,
                    RT TEXT,
                    RW TEXT,
                    NomorHP TEXT,
                    WhatsApp TEXT,
                    Unit TEXT,
                    NomorSK TEXT,
                    TanggalSK DATETIME,
                    BerkasSK TEXT,
                    MasaJabatanMulai DATETIME,
                    MasaJabatanSelesai DATETIME,
                    Status TEXT NOT NULL DEFAULT 'AKTIF',
                    Catatan TEXT,
                    DibuatOleh TEXT,
                    DiperbaruiOleh TEXT,
                    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                );";

            public const string Index = @"
                CREATE INDEX IF NOT EXISTS IX_PerangkatDesa_Jabatan ON PerangkatDesa(Jabatan);
                CREATE INDEX IF NOT EXISTS IX_PerangkatDesa_Status ON PerangkatDesa(Status);
                CREATE INDEX IF NOT EXISTS IX_PerangkatDesa_Wilayah ON PerangkatDesa(Dusun, RT, RW);";

            /// <summary>
            /// Seluruh kolom tabel beserta DDL-nya, dipakai migrasi database lama.
            /// Daftar ini harus sejalan dengan <see cref="CreateTable"/>.
            ///
            /// Sengaja tanpa DEFAULT non-konstan (mis. CURRENT_TIMESTAMP): SQLite
            /// menolak ALTER TABLE ADD COLUMN dengan default seperti itu, sehingga
            /// kolom waktu ditambahkan sebagai NULL — baris lama tetap terbaca dan
            /// kolom CreatedAt/UpdatedAt terisi otomatis untuk baris baru.
            /// </summary>
            public static readonly (string Nama, string Ddl)[] KolomTambahan =
            {
                ("NIP", "NIP TEXT NULL"),
                ("NIK", "NIK TEXT NULL"),
                ("JenisKelamin", "JenisKelamin TEXT NULL"),
                ("TempatLahir", "TempatLahir TEXT NULL"),
                ("TanggalLahir", "TanggalLahir DATETIME NULL"),
                ("Pendidikan", "Pendidikan TEXT NULL"),
                ("Alamat", "Alamat TEXT NULL"),
                ("Dusun", "Dusun TEXT NULL"),
                ("RT", "RT TEXT NULL"),
                ("RW", "RW TEXT NULL"),
                ("NomorHP", "NomorHP TEXT NULL"),
                ("WhatsApp", "WhatsApp TEXT NULL"),
                ("Unit", "Unit TEXT NULL"),
                ("NomorSK", "NomorSK TEXT NULL"),
                ("TanggalSK", "TanggalSK DATETIME NULL"),
                ("BerkasSK", "BerkasSK TEXT NULL"),
                ("MasaJabatanMulai", "MasaJabatanMulai DATETIME NULL"),
                ("MasaJabatanSelesai", "MasaJabatanSelesai DATETIME NULL"),
                ("Status", "Status TEXT NOT NULL DEFAULT 'AKTIF'"),
                ("Catatan", "Catatan TEXT NULL"),
                ("DibuatOleh", "DibuatOleh TEXT NULL"),
                ("DiperbaruiOleh", "DiperbaruiOleh TEXT NULL"),
                ("CreatedAt", "CreatedAt DATETIME NULL"),
                ("UpdatedAt", "UpdatedAt DATETIME NULL"),
            };
        }
    }
}
