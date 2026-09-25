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
        /// contoh — mis. surat dari permintaan WhatsApp — sehingga semua penyimpan data
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

                var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM InfoDesa";
                var count = Convert.ToInt32(await command.ExecuteScalarAsync());
                if (count == 0)
                {
                    _logger.LogInformation("InfoDesa is empty: True");
                    return true;
                }

                // InfoDesa dianggap "belum diisi" bila barisnya hanya placeholder
                // (mis. sisa data default "Default Village" dari setup lama).
                var dataCommand = connection.CreateCommand();
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
                _logger.LogInformation("Village settings retrieved: NamaDesa={0}", desaData.NamaDesa);
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
            try
            {
                var periksa = connection.CreateCommand();
                periksa.CommandText =
                    "SELECT COUNT(*) FROM pragma_table_info('InfoDesa') WHERE name = 'Email' COLLATE NOCASE";

                if (Convert.ToInt32(await periksa.ExecuteScalarAsync()) > 0)
                {
                    return;
                }

                _logger.LogInformation("Menambahkan kolom Email pada InfoDesa (data desa lama).");
                var tambah = connection.CreateCommand();
                tambah.CommandText = "ALTER TABLE InfoDesa ADD COLUMN Email TEXT";
                await tambah.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                // Kolom opsional: kegagalan di sini dicatat, tidak menghentikan alur simpan.
                _logger.LogWarning(ex, "Gagal memastikan kolom Email pada InfoDesa");
            }
        }

        public async Task SaveSettingsAsync(DesaData desaData)
        {
            if (desaData == null)
            {
                _logger.LogError("DesaData is null in SaveSettingsAsync.");
                throw new ArgumentNullException(nameof(desaData));
            }

            _logger.LogInformation("Saving village settings: NamaDesa={NamaDesa}, Kecamatan={Kecamatan}, Kabupaten={Kabupaten}, Alamat={Alamat}, Kodepos={Kodepos}, KepalaDesa={KepalaDesa}, SekretarisDesa={SekretarisDesa}, Email={Email}, NamaCamat={NamaCamat}, NipCamat={NipCamat}, GolCamat={GolCamat}",
                desaData.NamaDesa, desaData.Kecamatan, desaData.Kabupaten, desaData.Alamat, desaData.Kodepos, desaData.KepalaDesa, desaData.SekretarisDesa, desaData.Email, desaData.NamaCamat, desaData.NipCamat, desaData.GolCamat);

            try
            {
                using var connection = new SqliteConnection(_connectionString);
                await connection.OpenAsync();

                var checkCommand = connection.CreateCommand();
                checkCommand.CommandText = "SELECT COUNT(*) FROM InfoDesa";
                var count = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());

                // Database lama belum punya kolom Email (email desa pada kop surat).
                // Pastikan kolomnya ada lebih dulu supaya penyimpanan tidak gagal walau
                // migrasi skema belum sempat berjalan.
                await PastikanKolomEmailAsync(connection);

                var command = connection.CreateCommand();

                if (count == 0)
                {
                    command.CommandText = @"INSERT INTO InfoDesa (NamaDesa, Kecamatan, Kabupaten, Alamat, Kodepos, KepalaDesa, SekretarisDesa, Email, NamaCamat, NipCamat, GolCamat)
                                    VALUES (@namaDesa, @kecamatan, @kabupaten, @alamat, @kodepos, @kepalaDesa, @sekretarisDesa, @email, @namaCamat, @nipCamat, @golCamat)";
                    _logger.LogInformation("Executing INSERT for InfoDesa.");
                }
                else
                {
                    command.CommandText = @"UPDATE InfoDesa
                                    SET NamaDesa = @namaDesa,
                                        Kecamatan = @kecamatan,
                                        Kabupaten = @kabupaten,
                                        Alamat = @alamat,
                                        Kodepos = @kodepos,
                                        KepalaDesa = @kepalaDesa,
                                        SekretarisDesa = @sekretarisDesa,
                                        Email = @email,
                                        NamaCamat = @namaCamat,
                                        NipCamat = @nipCamat,
                                        GolCamat = @golCamat";
                    _logger.LogInformation("Executing UPDATE for InfoDesa.");
                }

                AddParameterWithNullCheck(command, "@namaDesa", desaData.NamaDesa);
                AddParameterWithNullCheck(command, "@kecamatan", desaData.Kecamatan);
                AddParameterWithNullCheck(command, "@kabupaten", desaData.Kabupaten);
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

                await command.ExecuteNonQueryAsync();
                _logger.LogInformation("Village settings saved successfully: NamaDesa={NamaDesa}", desaData.NamaDesa);

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

        private void AddParameterWithNullCheck(SqliteCommand command, string parameterName, string? value)
        {
            if (value != null && value.Length > 255)
            {
                _logger.LogWarning("Input {ParameterName} truncated to 255 characters.", parameterName);
                value = value.Substring(0, 255);
            }
            command.Parameters.AddWithValue(parameterName, value == null ? DBNull.Value : (object)value);
        }
    }
}
