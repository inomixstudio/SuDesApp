using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using System.Data;
using System.Text;
using System.Threading.Tasks;

namespace SuDesApp.Configuration
{
    public partial class DatabaseInitializer : IDatabaseInitializer
    {

        // INITIALIZE NEW FEATURES
        private async Task InitializeNewFeaturesAsync(SqliteConnection connection)
        {
            _logger.LogInformation("Initializing new features...");

            // 1. Create audit log table jika belum ada
            await CreateAuditLogTableAsync(connection);

            // 1a. Tabel Perangkat Desa (dibuat + migrasi nama jabatan lama)
            await EnsurePerangkatDesaTableAsync(connection);

            // 1b. Tabel status tutup buku tahunan (fitur baru; tidak ada di desa.db.sql versi awal)
            await EnsureTutupBukuTahunTableAsync(connection);

            // 1c. Tabel bukti langganan (kode aktivasi terhubung ke database desa)
            await EnsureLisensiAktivasiTableAsync(connection);

            // 2. Create views jika belum ada
            await CreateViewsAsync(connection);

            // 3. Create triggers jika belum ada
            await CreateTriggersAsync(connection);

            // 4. Create additional indexes
            await CreateAdditionalIndexesAsync(connection);

            // 5. Insert default data for new features
            await InsertDefaultDataAsync(connection);

            // 6. Buang sisa data Model N7 (sudah tidak dipakai aplikasi)
            await HapusSisaDataN7Async(connection);

            // 7. Kosongkan Pejabat Kecamatan yang masih berisi contoh bawaan
            await BersihkanPejabatKecamatanContohAsync(connection);

            // 🧹 8. Buang contoh warga bawaan (desa.db.sql tak lagi menyuntiknya)
            await BersihkanWargaContohAsync(connection);

            _logger.LogInformation("New features initialized successfully");
        }

        // BERSIHKAN PEJABAT KECAMATAN CONTOH
        /// <summary>
        /// Kolom Pejabat Kecamatan (Nama Camat, NIP Camat, Golongan) tidak lagi diisi
        /// contoh bawaan karena tidak semua surat memakainya. Database lama yang masih
        /// menyimpan nilai contoh dibersihkan di sini — hanya bila nilainya PERSIS
        /// contoh bawaan, jadi data camat yang benar-benar diisi pengguna tidak tersentuh.
        ///
        /// Aman dijalankan berulang: setelah bersih, tidak ada baris yang cocok lagi.
        /// </summary>
        private async Task BersihkanPejabatKecamatanContohAsync(SqliteConnection connection)
        {
            try
            {
                if (!await TableExistsAsync(connection, "InfoDesa"))
                {
                    return;
                }

                int terhapus = await connection.ExecuteAsync(
                    @"UPDATE InfoDesa
                         SET NamaCamat = CASE WHEN TRIM(COALESCE(NamaCamat, '')) IN ('Nama Camat', 'Default Camat') THEN '' ELSE NamaCamat END,
                             NipCamat  = CASE WHEN TRIM(COALESCE(NipCamat, '')) IN ('000000000000000000', '0') THEN '' ELSE NipCamat END,
                             GolCamat  = CASE WHEN TRIM(COALESCE(GolCamat, '')) IN ('IV/a', 'IV/a ') THEN '' ELSE GolCamat END
                       WHERE TRIM(COALESCE(NamaCamat, '')) IN ('Nama Camat', 'Default Camat')
                          OR TRIM(COALESCE(NipCamat, '')) IN ('000000000000000000', '0')
                          OR TRIM(COALESCE(GolCamat, '')) IN ('IV/a', 'IV/a ')");

                if (terhapus > 0)
                {
                    _logger.LogInformation(
                        "Pejabat Kecamatan contoh dikosongkan pada {Jumlah} baris InfoDesa.", terhapus);
                }
            }
            catch (Exception ex)
            {
                // Pembersihan data opsional: jangan gagalkan startup karenanya.
                _logger.LogWarning(ex, "Gagal membersihkan Pejabat Kecamatan contoh (tidak fatal).");
            }
        }

        // 🧹 BERSIHKAN CONTOH WARGA
        /// <summary>
        /// desa.db.sql versi lama menyuntik satu warga contoh ("NAMA ORANG TUA",
        /// NIK 1234567890123456) setiap database dibuat — instalasi baru jadi sudah
        /// terisi contoh. Penyemaian itu dihapus supaya data warga mulai kosong;
        /// database lama dibersihkan di sini — hanya bila NIK DAN namanya persis
        /// contoh bawaan, jadi data warga buatan pengguna tidak tersentuh.
        ///
        /// Aman dijalankan berulang: setelah bersih, tidak ada baris yang cocok
        /// lagi. Bila baris itu sudah dirujuk surat (FK RESTRICT), pembersihan
        /// dilewati tanpa menggagalkan startup.
        /// </summary>
        private async Task BersihkanWargaContohAsync(SqliteConnection connection)
        {
            try
            {
                if (!await TableExistsAsync(connection, "Warga"))
                {
                    return;
                }

                int terhapus = await connection.ExecuteAsync(
                    @"DELETE FROM Warga
                       WHERE NIK = '1234567890123456'
                         AND Nama = 'NAMA ORANG TUA'");

                if (terhapus > 0)
                {
                    _logger.LogInformation(
                        "Warga contoh bawaan dihapus dari tabel Warga: {Jumlah} baris.", terhapus);
                }
            }
            catch (Exception ex)
            {
                // Pembersihan data opsional: jangan gagalkan startup karenanya.
                _logger.LogWarning(ex, "Gagal membersihkan warga contoh (tidak fatal).");
            }
        }

        // BUANG SISA DATA MODEL N7
        /// <summary>
        /// Model N7 (penolakan kehendak nikah/rujuk) sudah tidak ada di aplikasi:
        /// blanko itu diterbitkan KUA, bukan kantor desa. Migrasi ini membuang sisa
        /// datanya pada database lama — surat beserta detail NTCR-nya dan baris jenis
        /// suratnya — supaya register, penomoran, dan pemilihan jenis surat bersih.
        ///
        /// Aman untuk dijalankan berulang: bila tidak ada data N7, tidak terjadi apa-apa.
        /// Sebelum menghapus, berkas database dicadangkan ke folder "Backup" di samping
        /// database, sehingga surat lama masih bisa dikembalikan bila diperlukan.
        /// </summary>
        private async Task HapusSisaDataN7Async(SqliteConnection connection)
        {
            const string jenisN7 = "NTCR_N7";

            try
            {
                // Tabel Surat tidak menyimpan NamaJenis; jenis surat dibaca lewat
                // relasi ke JenisSurat (s.ID_Jenis = j.ID_Jenis).
                const string hitungSuratN7 = @"
                    SELECT COUNT(*) FROM Surat s
                    INNER JOIN JenisSurat j ON j.ID_Jenis = s.ID_Jenis
                    WHERE j.NamaJenis = @Jenis COLLATE NOCASE";

                int suratN7 = await TableExistsAsync(connection, "Surat") && await TableExistsAsync(connection, "JenisSurat")
                    ? await connection.ExecuteScalarAsync<int>(hitungSuratN7, new { Jenis = jenisN7 })
                    : 0;

                int jenisN7Tersisa = await TableExistsAsync(connection, "JenisSurat")
                    ? await connection.ExecuteScalarAsync<int>(
                        "SELECT COUNT(*) FROM JenisSurat WHERE NamaJenis = @Jenis COLLATE NOCASE", new { Jenis = jenisN7 })
                    : 0;

                if (suratN7 == 0 && jenisN7Tersisa == 0)
                {
                    return;
                }

                _logger.LogWarning(
                    "Migrasi N7: ditemukan {Surat} surat, {Jenis} jenis surat. " +
                    "Model N7 sudah tidak dipakai — data lama akan dibuang setelah dicadangkan.",
                    suratN7, jenisN7Tersisa);

                await CadangkanSebelumHapusN7Async();

                if (suratN7 > 0)
                {
                    if (await TableExistsAsync(connection, "NTCR"))
                    {
                        await connection.ExecuteAsync(@"
                            DELETE FROM NTCR WHERE ID_Surat IN (
                                SELECT s.ID_Surat FROM Surat s
                                INNER JOIN JenisSurat j ON j.ID_Jenis = s.ID_Jenis
                                WHERE j.NamaJenis = @Jenis COLLATE NOCASE)",
                            new { Jenis = jenisN7 });
                    }

                    // Hapus suratnya selagi baris jenis suratnya masih ada (sumber nama jenis).
                    await connection.ExecuteAsync(@"
                        DELETE FROM Surat WHERE ID_Jenis IN (
                            SELECT ID_Jenis FROM JenisSurat WHERE NamaJenis = @Jenis COLLATE NOCASE)",
                        new { Jenis = jenisN7 });
                }

                if (jenisN7Tersisa > 0)
                {
                    await connection.ExecuteAsync(
                        "DELETE FROM JenisSurat WHERE NamaJenis = @Jenis COLLATE NOCASE", new { Jenis = jenisN7 });
                }

                _logger.LogInformation("Migrasi N7 selesai: sisa data Model N7 dihapus.");
            }
            catch (Exception ex)
            {
                // Jangan gagalkan startup hanya karena pembersihan data opsional ini.
                _logger.LogWarning(ex, "Gagal membersihkan sisa data Model N7 (tidak fatal).");
            }
        }

        /// <summary>
        /// Cadangkan berkas database sebelum data N7 dibuang: "&lt;database&gt;.sebelum-hapus-N7-
        /// &lt;stempel waktu&gt;.bak" di folder "Backup" sebelah berkas database.
        /// Bila pencadangan gagal, penghapusan dibatalkan demi keamanan data.
        /// </summary>
        private async Task CadangkanSebelumHapusN7Async()
        {
            var dbPath = new SqliteConnectionStringBuilder(_config.DatabaseConnectionString).DataSource;
            var dbDir = Path.GetDirectoryName(dbPath) ?? AppDomain.CurrentDomain.BaseDirectory;
            var nama = Path.GetFileNameWithoutExtension(dbPath);

            var backupPath = Path.Combine(dbDir, "Backup",
                $"{nama}.sebelum-hapus-N7-{DateTime.Now:yyyyMMdd-HHmmss}.bak");

            try
            {
                await CreateBackupAsync(backupPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pencadangan sebelum hapus data N7 gagal.");
                throw new InvalidOperationException(
                    "Data N7 tidak dihapus karena pencadangan database gagal.", ex);
            }
        }

        // CREATE AUDIT LOG TABLE
        private async Task CreateAuditLogTableAsync(SqliteConnection connection)
        {
            var sql = @"
                CREATE TABLE IF NOT EXISTS AuditLog (
                    ID_Log INTEGER PRIMARY KEY AUTOINCREMENT,
                    TableName TEXT NOT NULL,
                    RecordID INTEGER NOT NULL,
                    Action TEXT NOT NULL CHECK (Action IN ('INSERT', 'UPDATE', 'DELETE')),
                    OldValues TEXT,
                    NewValues TEXT,
                    UserID TEXT,
                    Timestamp DATETIME DEFAULT CURRENT_TIMESTAMP
                );";

            await connection.ExecuteAsync(sql);
            _logger.LogInformation("Tabel AuditLog dimigrasikan.");
        }

        /// <summary>
        /// Pastikan tabel <c>PerangkatDesa</c> ada, kolom <c>BerkasSK</c> sudah
        /// ada, indeksnya terbentuk, dan nama jabatan lama sudah dipetakan ke nama
        /// resmi terbaru. Satu metode di repository yang menangani semuanya supaya
        /// langkah-langkah ini tidak tercecer di banyak tempat.
        /// </summary>
        private async Task EnsurePerangkatDesaTableAsync(SqliteConnection connection)
        {
            try
            {
                await PerangkatDesaRepository.EnsurePerangkatDesaAsync(connection);
                _logger.LogInformation("Tabel PerangkatDesa siap dipakai.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyiapkan tabel PerangkatDesa");
                throw;
            }
        }

        /// <summary>
        /// Tabel status tutup buku tahunan. Skemanya hidup di repository sebagai
        /// satu-satunya sumber; tanpa langkah ini database lama tidak pernah
        /// memiliki tabelnya (desa.db.sql versi awal tidak memuatnya) sehingga
        /// halaman Tutup Buku Tahunan gagal dengan "no such table".
        /// </summary>
        private async Task EnsureTutupBukuTahunTableAsync(SqliteConnection connection)
        {
            try
            {
                await TutupBukuTahunRepository.EnsureAsync(connection);
                _logger.LogInformation("Tabel TutupBukuTahun siap dipakai.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyiapkan tabel TutupBukuTahun");
                throw;
            }
        }

        /// <summary>
        /// Tabel bukti langganan. Menyimpan token bertanda tangan hasil
        /// pengecekan server ke dalam database desa, supaya kode aktivasi
        /// terhubung ke data yang sama dan ikut terbawa saat aplikasi
        /// mencadangkan datanya. Tanpa langkah ini, penyimpanan bukti
        /// langganan gagal dengan "no such table" dan aplikasi kembali
        /// ke cache berkas saja.
        /// </summary>
        private async Task EnsureLisensiAktivasiTableAsync(SqliteConnection connection)
        {
            try
            {
                await LisensiAktivasiRepository.EnsureAsync(connection);
                _logger.LogInformation("Tabel LisensiAktivasi siap dipakai.");
            }
            catch (Exception ex)
            {
                // Kegagalan menyiapkan tabel lisensi tidak boleh menggagalkan
                // start aplikasi: cache berkas lisensi-cache.json masih
                // tersedia sebagai sumber offline, jadi desa yang sudah
                // langganan tidak terkunci hanya karena tabel ini belum ada.
                _logger.LogError(ex, "Gagal menyiapkan tabel LisensiAktivasi");
            }
        }

    }
}
