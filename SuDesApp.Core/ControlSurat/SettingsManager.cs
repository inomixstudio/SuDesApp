using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;

namespace SuDesApp.ControlSurat
{
    public partial class SettingsManager
    {
        /// <summary>
        /// Dipicu setelah data desa benar-benar tersimpan (beserta data barunya).
        ///
        /// Dipakai untuk melanjutkan pekerjaan yang tadi ditahan karena data desa masih
        /// contoh — mis. surat yang dibuat beberapa halaman sekaligus — sehingga semua penyimpan data
        /// desa (halaman Pengaturan Surat, pemulihan database, apa pun yang datang
        /// kemudian) otomatis membuka penahanan itu tanpa perlu mendaftar sendiri-sendiri.
        /// </summary>
        public event EventHandler<DesaData>? PengaturanDesaTersimpan;

        private readonly IDesaRepository _desaRepository;
        private readonly string _connectionString;
        private readonly ILogger<SettingsManager> _logger;

        public SettingsManager(
            IDesaRepository desaRepository,
            AppConfig config,
            ILogger<SettingsManager>? logger = null)
        {
            _desaRepository = desaRepository ?? throw new ArgumentNullException(nameof(desaRepository));
            if (config == null) throw new ArgumentNullException(nameof(config));
            _connectionString = config.DatabaseConnectionString ?? throw new ArgumentNullException(nameof(config.DatabaseConnectionString));
            _logger = logger ?? NullLogger<SettingsManager>.Instance;
        }

        public async Task<bool> IsSettingsEmptyAsync()
        {
            try
            {
                _logger.LogInformation("Checking if InfoDesa is empty...");
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                // using: SqliteCommand memegang handle statement SQLite unmanaged yang
                // tidak segera dibebaskan GC. Jalur ini dipakai tiap aplikasi dibuka
                // dan tiap clerk menyimpan data desa, sehingga handle menumpuk.
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM InfoDesa";
                var count = Convert.ToInt32(await command.ExecuteScalarAsync());
                if (count == 0)
                {
                    _logger.LogInformation("InfoDesa is empty: True");
                    return true;
                }

                // InfoDesa dianggap "belum diisi" bila barisnya hanya placeholder
                // (mis. sisa data default "Default Village" dari setup lama).
                using var dataCommand = connection.CreateCommand();
                dataCommand.CommandText = "SELECT NamaDesa, Kecamatan, Kabupaten, KepalaDesa FROM InfoDesa LIMIT 1";
                using var reader = await dataCommand.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                {
                    _logger.LogInformation("InfoDesa is empty: True");
                    return true;
                }

                var namaDesa = reader.IsDBNull(0) ? null : reader.GetString(0).Trim();
                var kecamatan = reader.IsDBNull(1) ? null : reader.GetString(1).Trim();
                var kabupaten = reader.IsDBNull(2) ? null : reader.GetString(2).Trim();
                var kepalaDesa = reader.IsDBNull(3) ? null : reader.GetString(3).Trim();

                bool isEmpty = string.IsNullOrWhiteSpace(namaDesa) ||
                               string.IsNullOrWhiteSpace(kecamatan) ||
                               string.IsNullOrWhiteSpace(kabupaten) ||
                               string.IsNullOrWhiteSpace(kepalaDesa) ||
                               namaDesa.StartsWith("Default", StringComparison.OrdinalIgnoreCase) ||
                               namaDesa.StartsWith("Nama ", StringComparison.OrdinalIgnoreCase);

                _logger.LogInformation("InfoDesa is empty: {IsEmpty}", isEmpty);
                return isEmpty;
            }
            catch (SqliteException sqlEx)
            {
                _logger.LogError(sqlEx, "SQLite error while checking if InfoDesa is empty.");
                throw new Exception("Database error while checking settings.", sqlEx);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while checking if InfoDesa is empty.");
                throw new Exception("An unexpected error occurred while checking settings.", ex);
            }
        }

        public async Task<DesaData?> GetSettingsAsync()
        {
            try
            {
                _logger.LogInformation("Retrieving village settings from database...");
                var desaData = await _desaRepository.GetInfoDesaAsync();
                if (desaData == null)
                {
                    _logger.LogWarning("No data found in InfoDesa table.");
                    return new DesaData();
                }
                _logger.LogDebug("Pengaturan desa berhasil dibaca.");
                return desaData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve village settings.");
                throw new Exception("Gagal mengambil pengaturan desa dari database.", ex);
            }
        }

        /// <summary>
        /// Pastikan kolom <c>Email</c> ada pada InfoDesa — kolom opsional untuk email
        /// kantor desa yang dicetak biru di kop surat. Database yang dibuat sebelum fitur
        /// ini ada belum memilikinya, jadi kolomnya ditambahkan sekali di sini.
        /// </summary>
        private async Task PastikanKolomEmailAsync(SqliteConnection connection)
        {
            await PastikanKolomInfoDesaAsync(connection, "Email");
        }

        /// <summary>
        /// Pastikan kolom <c>JenisKabupaten</c> ada pada InfoDesa — jenis wilayah
        /// tingkat kabupaten/kota (“Kabupaten”/“Kota”) yang menentukan kata pada baris
        /// identitas surat (“PEMERINTAH KOTA …”). Database lama belum memilikinya, jadi
        /// tanpa kolom ini data desa lama gagal disimpan sampai skema dimigrasi.
        /// </summary>
        private async Task PastikanKolomJenisKabupatenAsync(SqliteConnection connection)
        {
            await PastikanKolomInfoDesaAsync(connection, "JenisKabupaten");
        }

        /// <summary>
        /// Pastikan kolom <c>JenisLembaga</c> ada pada InfoDesa — jenis lembaga
        /// pemerintahan tingkat desa (“Desa”/“Kelurahan”). Menentukan kata pada kop
        /// (“PEMERINTAH DESA …” / “KELURAHAN …”), jabatan penandatangan
        /// (“Kepala Desa…”/“Lurah …”), dan kalimat badan surat. Database yang dibuat
        /// sebelum fitur ini ada belum memilikinya.
        /// </summary>
        private async Task PastikanKolomJenisLembagaAsync(SqliteConnection connection)
        {
            await PastikanKolomInfoDesaAsync(connection, "JenisLembaga");
        }

        /// <summary>
        /// Pastikan kolom <c>NipKepalaDesa</c> ada pada InfoDesa — NIP pejabat
        /// penandatangan tingkat desa/kelurahan. Hanya tercetak pada wilayah
        /// Kelurahan (Lurah, ASN); Kepala Desa (non-ASN) tidak memakai NIP.
        /// </summary>
        private async Task PastikanKolomNipKepalaDesaAsync(SqliteConnection connection)
        {
            await PastikanKolomInfoDesaAsync(connection, "NipKepalaDesa");
        }

        /// <summary>
        /// Tambahkan satu kolom InfoDesa bila belum ada. Dipakai kolom yang ditambahkan
        /// setelah database lama dibuat — CREATE TABLE IF NOT EXISTS tidak mengubah
        /// tabel yang sudah terlanjur ada.
        /// </summary>
        private async Task PastikanKolomInfoDesaAsync(SqliteConnection connection, string kolom)
        {
            try
            {
                using var periksa = connection.CreateCommand();
                periksa.CommandText =
                    "SELECT COUNT(*) FROM pragma_table_info('InfoDesa') WHERE name = @kolom COLLATE NOCASE";
                periksa.Parameters.AddWithValue("@kolom", kolom);

                if (Convert.ToInt32(await periksa.ExecuteScalarAsync()) > 0)
                {
                    return;
                }

                _logger.LogInformation("Menambahkan kolom {Kolom} pada InfoDesa (data desa lama).", kolom);
                using var tambah = connection.CreateCommand();
                tambah.CommandText = $"ALTER TABLE InfoDesa ADD COLUMN {kolom} TEXT";
                await tambah.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                // Kolom opsional: kegagalan di sini dicatat, tidak menghentikan alur simpan.
                _logger.LogWarning(ex, "Gagal memastikan kolom {Kolom} pada InfoDesa", kolom);
            }
        }

        public async Task SaveSettingsAsync(DesaData desaData)
        {
            if (desaData == null)
            {
                _logger.LogError("DesaData is null in SaveSettingsAsync.");
                throw new ArgumentNullException(nameof(desaData));
            }

            _logger.LogDebug("Penyimpanan pengaturan desa: NamaDesa={NamaDesa}, Kecamatan={Kecamatan}, Kabupaten={Kabupaten}, JenisKabupaten={JenisKabupaten}, Alamat={Alamat}, Kodepos={Kodepos}, KepalaDesa={KepalaDesa}, SekretarisDesa={SekretarisDesa}, Email={Email}, NamaCamat={NamaCamat}, NipCamat={NipCamat}, GolCamat={GolCamat}",
                desaData.NamaDesa, desaData.Kecamatan, desaData.Kabupaten, desaData.JenisKabupaten, desaData.Alamat, desaData.Kodepos, desaData.KepalaDesa, desaData.SekretarisDesa, desaData.Email, desaData.NamaCamat, desaData.NipCamat, desaData.GolCamat);

            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                using var checkCommand = connection.CreateCommand();
                checkCommand.CommandText = "SELECT COUNT(*) FROM InfoDesa";
                var count = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());

                // Database lama belum punya kolom Email (email desa pada kop surat).
                // Pastikan kolomnya ada lebih dulu supaya penyimpanan tidak gagal walau
                // migrasi skema belum sempat berjalan.
                await PastikanKolomEmailAsync(connection);
                await PastikanKolomJenisKabupatenAsync(connection);
                await PastikanKolomJenisLembagaAsync(connection);
                await PastikanKolomNipKepalaDesaAsync(connection);

                using var command = connection.CreateCommand();

                if (count == 0)
                {
                    command.CommandText = @"INSERT INTO InfoDesa (NamaDesa, Kecamatan, Kabupaten, JenisKabupaten, Alamat, Kodepos, KepalaDesa, SekretarisDesa, Email, NamaCamat, NipCamat, GolCamat, JenisLembaga, NipKepalaDesa)
                                    VALUES (@namaDesa, @kecamatan, @kabupaten, @jenisKabupaten, @alamat, @kodepos, @kepalaDesa, @sekretarisDesa, @email, @namaCamat, @nipCamat, @golCamat, @jenisLembaga, @nipKepalaDesa)";
                    _logger.LogInformation("Executing INSERT for InfoDesa.");
                }
                else
                {
                    command.CommandText = @"UPDATE InfoDesa
                                    SET NamaDesa = @namaDesa,
                                        Kecamatan = @kecamatan,
                                        Kabupaten = @kabupaten,
                                        JenisKabupaten = @jenisKabupaten,
                                        Alamat = @alamat,
                                        Kodepos = @kodepos,
                                        KepalaDesa = @kepalaDesa,
                                        SekretarisDesa = @sekretarisDesa,
                                        Email = @email,
                                        NamaCamat = @namaCamat,
                                        NipCamat = @nipCamat,
                                        GolCamat = @golCamat,
                                        JenisLembaga = @jenisLembaga,
                                        NipKepalaDesa = @nipKepalaDesa";
                    _logger.LogInformation("Executing UPDATE for InfoDesa.");
                }

                AddParameterWithNullCheck(command, "@namaDesa", desaData.NamaDesa);
                AddParameterWithNullCheck(command, "@kecamatan", desaData.Kecamatan);
                AddParameterWithNullCheck(command, "@kabupaten", desaData.Kabupaten);
                AddParameterWithNullCheck(command, "@jenisKabupaten", desaData.JenisKabupaten);
                AddParameterWithNullCheck(command, "@alamat", desaData.Alamat);
                AddParameterWithNullCheck(command, "@kodepos", desaData.Kodepos);
                AddParameterWithNullCheck(command, "@kepalaDesa", desaData.KepalaDesa);
                AddParameterWithNullCheck(command, "@sekretarisDesa", desaData.SekretarisDesa);
                // Surel opsional: nilai kosong tetap tersimpan sebagai NULL/teks kosong
                // dan tidak pernah menghalangi penyimpanan data desa.
                AddParameterWithNullCheck(command, "@email", desaData.Email);
                AddParameterWithNullCheck(command, "@namaCamat", desaData.NamaCamat);
                AddParameterWithNullCheck(command, "@nipCamat", desaData.NipCamat);
                AddParameterWithNullCheck(command, "@golCamat", desaData.GolCamat);
                // Jenis lembaga (“Desa”/“Kelurahan”) dan NIP pejabat: kolom opsional,
                // data desa lama tanpa keduanya tetap dipakai sebagai Desa.
                AddParameterWithNullCheck(command, "@jenisLembaga", desaData.JenisLembaga);
                AddParameterWithNullCheck(command, "@nipKepalaDesa", desaData.NipKepalaDesa);

                await command.ExecuteNonQueryAsync();
                _logger.LogInformation("Pengaturan desa berhasil disimpan.");

                // Data desa dibaca lewat cache (bertahan sampai 1 jam). Tanpa pembersihan
                // ini, halaman lain — mis. Daftar Hadir yang mengisi "Aula Kantor Desa …"
                // atau generator surat — masih memakai nama desa yang lama.
                await _desaRepository.InvalidateCacheAsync();
                _logger.LogInformation("Cache data desa dibersihkan setelah pengaturan disimpan.");

                // Pemberitahuan ini sengaja dilakukan paling akhir: pelanggannya membaca
                // data desa dari database, jadi harus melihat isi yang baru.
                PengaturanDesaTersimpan?.Invoke(this, desaData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save village settings.");
                throw new Exception("Database error while saving settings.", ex);
            }
        }

        /// <summary>
        /// Tambahkan parameter teks. TIDAK dipotong pada 255 karakter: kolom TEXT
        /// SQLite tidak punya batas panjang, dan memotong di sini pernah memotong
        /// alamat desa di tengah kata sehingga kop EVERY surat berikutnya tampil
        /// tidak lengkap padahal simpan terlihat sukses. Yang dicatat cukup
        /// peringatan bila panjangnya tidak wajar.
        /// </summary>
        private void AddParameterWithNullCheck(SqliteCommand command, string parameterName, string? value)
        {
            if (value != null && value.Length > 255)
            {
                _logger.LogInformation(
                    "Input {ParameterName} berpanjang {Panjang} karakter (di atas 255) — disimpan utuh.",
                    parameterName, value.Length);
            }
            command.Parameters.AddWithValue(parameterName, value == null ? DBNull.Value : (object)value);
        }
    }
}
