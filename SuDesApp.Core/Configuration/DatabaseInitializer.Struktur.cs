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

        // CREATE VIEWS
        private async Task CreateViewsAsync(SqliteConnection connection)
        {
            var views = new[]
            {
                @"CREATE VIEW IF NOT EXISTS v_SuratLengkap AS
                SELECT 
                    s.ID_Surat,
                    s.NomorSurat,
                    s.TanggalSurat,
                    js.NamaJenis,
                    js.KodeJenis,
                    w.Nama as NamaWarga,
                    w.NIK,
                    s.Keterangan,
                    s.Keperluan,
                    s.Status,
                    s.CreatedAt
                FROM Surat s
                LEFT JOIN JenisSurat js ON s.ID_Jenis = js.ID_Jenis
                LEFT JOIN Warga w ON s.ID_Warga = w.ID_Warga;",

                @"CREATE VIEW IF NOT EXISTS v_WargaAktif AS
                SELECT 
                    w.*,
                    COUNT(s.ID_Surat) as JumlahSurat
                FROM Warga w
                LEFT JOIN Surat s ON w.ID_Warga = s.ID_Warga
                GROUP BY w.ID_Warga;"
            };

            foreach (var viewSql in views)
            {
                try
                {
                    await connection.ExecuteAsync(viewSql);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to create view: {ViewSql}", viewSql.Substring(0, Math.Min(50, viewSql.Length)));
                }
            }
        }

        // CREATE TRIGGERS
        private async Task CreateTriggersAsync(SqliteConnection connection)
        {
            var triggers = new[]
            {
                @"CREATE TRIGGER IF NOT EXISTS tr_warga_updated 
                    AFTER UPDATE ON Warga
                    FOR EACH ROW
                BEGIN
                    UPDATE Warga SET UpdatedAt = CURRENT_TIMESTAMP WHERE ID_Warga = NEW.ID_Warga;
                END;",

                @"CREATE TRIGGER IF NOT EXISTS tr_surat_updated 
                    AFTER UPDATE ON Surat
                    FOR EACH ROW
                BEGIN
                    UPDATE Surat SET UpdatedAt = CURRENT_TIMESTAMP WHERE ID_Surat = NEW.ID_Surat;
                END;"
            };

            foreach (var triggerSql in triggers)
            {
                try
                {
                    await connection.ExecuteAsync(triggerSql);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to create trigger: {TriggerSql}", triggerSql.Substring(0, Math.Min(50, triggerSql.Length)));
                }
            }
        }

        // CREATE ADDITIONAL INDEXES
        private async Task CreateAdditionalIndexesAsync(SqliteConnection connection)
        {
            var indexes = new[]
            {
                "CREATE INDEX IF NOT EXISTS idx_warga_nik ON Warga(NIK);",
                "CREATE INDEX IF NOT EXISTS idx_warga_nama ON Warga(Nama);",
                "CREATE INDEX IF NOT EXISTS idx_surat_status ON Surat(Status);",
                "CREATE INDEX IF NOT EXISTS idx_surat_createdat ON Surat(CreatedAt);",
                "CREATE INDEX IF NOT EXISTS idx_auditlog_table_record ON AuditLog(TableName, RecordID);",
                "CREATE INDEX IF NOT EXISTS idx_auditlog_timestamp ON AuditLog(Timestamp);"
            };

            foreach (var indexSql in indexes)
            {
                try
                {
                    await connection.ExecuteAsync(indexSql);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to create index: {IndexSql}", indexSql);
                }
            }
        }

        // INSERT DEFAULT DATA
        private async Task InsertDefaultDataAsync(SqliteConnection connection)
        {
            // Insert IJIN_TINGGAL jenis surat jika belum ada
            var ijinTinggalSql = @"
                INSERT OR IGNORE INTO JenisSurat (ID_Jenis, NamaJenis, KodeJenis, Deskripsi) 
                VALUES (13, 'IJIN_TINGGAL', 'IJT', 'Surat Ijin Tinggal');";

            try
            {
                await connection.ExecuteAsync(ijinTinggalSql);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to insert default IJIN_TINGGAL data");
            }

            // Insert jenis surat REKENING_KORAN (permohonan print out rekening koran
            // ke bank) jika belum ada. Surat ini surat keluar desa dan ikut penomoran
            // bersama seperti SKD, jadi barisnya wajib ada agar nomornya bisa
            // dihasilkan dan surat tercatat di register.
            var rekeningKoranSql = @"
                INSERT OR IGNORE INTO JenisSurat (ID_Jenis, NamaJenis, KodeJenis, Deskripsi) 
                VALUES (21, 'REKENING_KORAN', 'REKKOR', 'Permohonan Print Out Rekening Koran');";

            try
            {
                await connection.ExecuteAsync(rekeningKoranSql);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to insert default REKENING_KORAN data");
            }

            // Insert jenis surat NTCR yang dibuat kantor desa (blanko N1-N6 + surat
            // numpang nikah N8) jika belum ada. Model N7 (penolakan kehendak
            // nikah/rujuk) diterbitkan KUA, jadi tidak ikut disemai; sisa data N7 pada
            // database lama dibersihkan oleh HapusSisaDataN7Async.
            var ntcrSql = @"
                INSERT OR IGNORE INTO JenisSurat (ID_Jenis, NamaJenis, KodeJenis, Deskripsi) 
                VALUES (14, 'NTCR_N1', 'N1T', 'Surat Pengantar Nikah (NTCR N1)');
                INSERT OR IGNORE INTO JenisSurat (ID_Jenis, NamaJenis, KodeJenis, Deskripsi) 
                VALUES (15, 'NTCR_N2', 'N2T', 'Permohonan Kehendak Nikah (NTCR N2)');
                INSERT OR IGNORE INTO JenisSurat (ID_Jenis, NamaJenis, KodeJenis, Deskripsi) 
                VALUES (16, 'NTCR_N3', 'N3T', 'Permohonan Pencatatan Isbat (NTCR N3)');
                INSERT OR IGNORE INTO JenisSurat (ID_Jenis, NamaJenis, KodeJenis, Deskripsi) 
                VALUES (17, 'NTCR_N4', 'N4T', 'Persetujuan Calon Pengantin (NTCR N4)');
                INSERT OR IGNORE INTO JenisSurat (ID_Jenis, NamaJenis, KodeJenis, Deskripsi) 
                VALUES (18, 'NTCR_N5', 'N5T', 'Surat Izin Orang Tua (NTCR N5)');
                INSERT OR IGNORE INTO JenisSurat (ID_Jenis, NamaJenis, KodeJenis, Deskripsi) 
                VALUES (19, 'NTCR_N6', 'N6T', 'Keterangan Kematian Suami/Istri (NTCR N6)');
                INSERT OR IGNORE INTO JenisSurat (ID_Jenis, NamaJenis, KodeJenis, Deskripsi) 
                VALUES (20, 'NTCR_N8', 'N8T', 'Surat Keterangan Numpang Nikah (NTCR N8)');";

            try
            {
                await connection.ExecuteAsync(ntcrSql);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to insert default NTCR data");
            }

            // Update existing JenisSurat dengan deskripsi jika belum ada
            var updateDescriptions = @"
                UPDATE JenisSurat SET Deskripsi = 'Surat Keterangan Domisili Umum' WHERE ID_Jenis = 1 AND (Deskripsi IS NULL OR Deskripsi = '');
                UPDATE JenisSurat SET Deskripsi = 'Surat Keterangan Domisili Warga' WHERE ID_Jenis = 2 AND (Deskripsi IS NULL OR Deskripsi = '');
                UPDATE JenisSurat SET Deskripsi = 'Surat Keterangan Domisili Instansi' WHERE ID_Jenis = 3 AND (Deskripsi IS NULL OR Deskripsi = '');
                UPDATE JenisSurat SET Deskripsi = 'Surat Keterangan Usaha' WHERE ID_Jenis = 4 AND (Deskripsi IS NULL OR Deskripsi = '');
                UPDATE JenisSurat SET Deskripsi = 'Surat Pengantar SKCK' WHERE ID_Jenis = 5 AND (Deskripsi IS NULL OR Deskripsi = '');
                UPDATE JenisSurat SET Deskripsi = 'Surat Izin Orang Tua' WHERE ID_Jenis = 6 AND (Deskripsi IS NULL OR Deskripsi = '');
                UPDATE JenisSurat SET Deskripsi = 'Surat Keterangan Garapan Sawah' WHERE ID_Jenis = 7 AND (Deskripsi IS NULL OR Deskripsi = '');
                UPDATE JenisSurat SET Deskripsi = 'Surat Keterangan Kematian' WHERE ID_Jenis = 8 AND (Deskripsi IS NULL OR Deskripsi = '');
                UPDATE JenisSurat SET Deskripsi = 'Surat Keterangan Tidak Mampu' WHERE ID_Jenis = 9 AND (Deskripsi IS NULL OR Deskripsi = '');
                UPDATE JenisSurat SET Deskripsi = 'Surat Keterangan Beda Nama' WHERE ID_Jenis = 10 AND (Deskripsi IS NULL OR Deskripsi = '');
                UPDATE JenisSurat SET Deskripsi = 'Surat Keterangan Kenal Lahir' WHERE ID_Jenis = 11 AND (Deskripsi IS NULL OR Deskripsi = '');
                UPDATE JenisSurat SET Deskripsi = 'Surat Keterangan Ahli Waris' WHERE ID_Jenis = 12 AND (Deskripsi IS NULL OR Deskripsi = '');";

            try
            {
                await connection.ExecuteAsync(updateDescriptions);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update JenisSurat descriptions");
            }

            // Update/muat deskripsi NTCR (N1-N6, N8) jika belum terisi. Deskripsi lama
            // yang masih memakai penamaan keliru (N2-N4 sebelum disesuaikan dengan
            // blanko resmi) ikut dimigrasi agar menu tidak menampilkan nama formulir
            // yang salah.
            try
            {
                await connection.ExecuteAsync(@"
                    UPDATE JenisSurat SET Deskripsi = 'Surat Pengantar Nikah (NTCR N1)' WHERE NamaJenis = 'NTCR_N1' AND (Deskripsi IS NULL OR Deskripsi = '');
                    UPDATE JenisSurat SET Deskripsi = 'Permohonan Kehendak Nikah (NTCR N2)' WHERE NamaJenis = 'NTCR_N2' AND (Deskripsi IS NULL OR Deskripsi = '' OR Deskripsi = 'Surat Keterangan Untuk Nikah (NTCR N2)');
                    UPDATE JenisSurat SET Deskripsi = 'Permohonan Pencatatan Isbat (NTCR N3)' WHERE NamaJenis = 'NTCR_N3' AND (Deskripsi IS NULL OR Deskripsi = '' OR Deskripsi = 'Surat Persetujuan Calon Mempelai (NTCR N3)');
                    UPDATE JenisSurat SET Deskripsi = 'Persetujuan Calon Pengantin (NTCR N4)' WHERE NamaJenis = 'NTCR_N4' AND (Deskripsi IS NULL OR Deskripsi = '' OR Deskripsi = 'Surat Keterangan Orang Tua (NTCR N4)');
                    UPDATE JenisSurat SET Deskripsi = 'Surat Izin Orang Tua (NTCR N5)' WHERE NamaJenis = 'NTCR_N5' AND (Deskripsi IS NULL OR Deskripsi = '');
                    UPDATE JenisSurat SET Deskripsi = 'Keterangan Kematian Suami/Istri (NTCR N6)' WHERE NamaJenis = 'NTCR_N6' AND (Deskripsi IS NULL OR Deskripsi = '');
                    UPDATE JenisSurat SET Deskripsi = 'Surat Keterangan Numpang Nikah (NTCR N8)' WHERE NamaJenis = 'NTCR_N8' AND (Deskripsi IS NULL OR Deskripsi = '');
                    UPDATE JenisSurat SET Deskripsi = 'Permohonan Print Out Rekening Koran' WHERE NamaJenis = 'REKENING_KORAN' AND (Deskripsi IS NULL OR Deskripsi = '');");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update NTCR descriptions");
            }
        }

        // UPDATE SCHEMA VERSION
        private async Task UpdateSchemaVersionAsync(SqliteConnection connection, string version)
        {
            // Idempoten. Sebelumnya setiap kali aplikasi dibuka satu baris baru
            // ditambahkan tanpa syarat, jadi tabel membengkak setiap hari tanpa
            // informasi apa pun. Lebih buruk: GetCurrentSchemaVersionAsync
            // membaca "baris terbaru" dari kolom DATETIME berpresisi detik,
            // jadi dua aplikasi yang mulai di detik sama menghasilkan versi
            // yang tidak menentu.
            var sql = @"
                INSERT INTO SchemaVersion (Version, Description)
                SELECT @Version, @Description
                WHERE NOT EXISTS (
                    SELECT 1 FROM SchemaVersion WHERE Version = @Version
                );";

            await connection.ExecuteAsync(sql, new
            {
                Version = version,
                Description = $"Schema updated to version {version} with enhanced features"
            });
        }

    }
}
