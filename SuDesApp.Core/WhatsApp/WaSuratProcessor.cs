using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace SuDesApp.WhatsApp
{
    /// <summary>
    /// Mengubah permintaan WhatsApp yang disetujui operator menjadi surat
    /// (SuratData) menggunakan pipeline penyimpanan yang sama dengan form
    /// biasa, sehingga penomoran, validasi, dan data terkait otomatis terisi.
    /// </summary>
    public class WaSuratProcessor
    {
        private readonly IPermintaanWaRepository _permintaanRepository;
        private readonly IWargaRepository _wargaRepository;
        private readonly IDesaRepository _desaRepository;
        private readonly IJenisSuratRepository _jenisSuratRepository;
        private readonly ISuratRepository _suratRepository;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<WaSuratProcessor> _logger;

        public WaSuratProcessor(
            IPermintaanWaRepository permintaanRepository,
            IWargaRepository wargaRepository,
            IDesaRepository desaRepository,
            IJenisSuratRepository jenisSuratRepository,
            ISuratRepository suratRepository,
            ILoggerFactory loggerFactory)
        {
            _permintaanRepository = permintaanRepository ?? throw new ArgumentNullException(nameof(permintaanRepository));
            _wargaRepository = wargaRepository ?? throw new ArgumentNullException(nameof(wargaRepository));
            _desaRepository = desaRepository ?? throw new ArgumentNullException(nameof(desaRepository));
            _jenisSuratRepository = jenisSuratRepository ?? throw new ArgumentNullException(nameof(jenisSuratRepository));
            _suratRepository = suratRepository ?? throw new ArgumentNullException(nameof(suratRepository));
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            _logger = _loggerFactory.CreateLogger<WaSuratProcessor>();
        }

        public class Hasil
        {
            public int IdSurat { get; set; }
            public string NomorSurat { get; set; } = string.Empty;
            public string NamaJenis { get; set; } = string.Empty;
        }

        /// <summary>
        /// Memproses permintaan: membuat surat Draft dan menandai permintaan
        /// SELESAI. Melempar exception jika data tidak bisa disimpan.
        /// </summary>
        public async Task<Hasil> ProsesAsync(int idPermintaan)
        {
            var permintaan = await _permintaanRepository.GetByIdAsync(idPermintaan)
                ?? throw new InvalidOperationException("Permintaan tidak ditemukan.");

            if (string.IsNullOrWhiteSpace(permintaan.DataJson))
                throw new InvalidOperationException("Data permintaan kosong.");

            var data = JsonSerializer.Deserialize<WaRequestData>(permintaan.DataJson)
                ?? throw new InvalidOperationException("Data permintaan tidak valid.");

            var jenis = await _jenisSuratRepository.GetJenisSuratByNamaAsync(data.NamaJenis)
                ?? throw new InvalidOperationException($"Jenis surat '{data.NamaJenis}' tidak ditemukan.");

            // 1. Siapkan warga (diperbarui bila perlu) agar terkait di DB.
            var warga = await PersiapanWargaAsync(data);

            // 2. Bangun SuratData mirip alur form biasa.
            var suratData = new SuratData(_suratRepository, _wargaRepository, _desaRepository, _jenisSuratRepository, _loggerFactory.CreateLogger<SuratData>())
            {
                NamaJenis = jenis.NamaJenis,
                TanggalSurat = DateTime.Now,
                Status = "Draft",
                Keperluan = data.Keperluan,
                Keterangan = data.Fields.GetValueOrDefault("keterangan")
                             ?? $"Permohonan online {permintaan.KodePermintaan}: {data.Keperluan}",
                Warga = warga
            };

            await IsiDataJenisKhusus(suratData, data);

            // 3. Simpan (validasi + nomor surat + data terkait dilakukan di dalam).
            var idSurat = await _suratRepository.AddSuratAsync(suratData);

            // 4. Perbarui permintaan.
            await _permintaanRepository.UpdateStatusAsync(
                idPermintaan,
                WaRequestStatus.SELESAI,
                $"Surat dibuat (Nomor: {suratData.NomorSurat})",
                idSurat,
                $"Surat {WaFormatParser.TampilanJenis(data.NamaJenis)} siap. Nomor: {suratData.NomorSurat}",
                default);

            _logger.LogInformation("Permintaan {Kode} diproses → Surat ID {IdSurat} (Nomor {Nomor})",
                permintaan.KodePermintaan, idSurat, suratData.NomorSurat);

            return new Hasil
            {
                IdSurat = idSurat,
                NomorSurat = suratData.NomorSurat,
                NamaJenis = suratData.NamaJenis!
            };
        }

        private async Task<WargaData> PersiapanWargaAsync(WaRequestData data)
        {
            // Untuk INSTANSI, gunakan warga dummy standar.
            if (data.NamaJenis.Equals(SuratConstants.INSTANSI, StringComparison.OrdinalIgnoreCase))
            {
                return new WargaData
                {
                    NIK = SuratConstants.NIK_INSTANSI,
                    Nama = data.Fields.GetValueOrDefault("nama") ?? data.Warga?.Nama ?? "Instansi",
                    Alamat = data.Fields.GetValueOrDefault("alamat") ?? string.Empty,
                    TempatLahir = "N/A",
                    TanggalLahir = "1900-01-01",
                    JenisKelamin = "N/A",
                    Agama = "N/A",
                    StatusPerkawinan = "N/A",
                    Pekerjaan = string.Empty,
                    Dusun = "N/A",
                    Desa = "N/A",
                    Kecamatan = "N/A",
                    Kabupaten = "N/A",
                    Pendidikan = "N/A",
                    Kewarganegaraan = "WNI",
                    IsForInstansi = true
                };
            }

            // Prioritaskan data terbaru dari DB (kalau sudah terdaftar).
            var nik = data.Warga?.NIK;
            var warga = !string.IsNullOrWhiteSpace(nik) ? await _wargaRepository.GetWargaByNikAsync(nik) : null;
            warga ??= data.Warga;

            if (warga == null)
                throw new InvalidOperationException("Data warga tidak tersedia pada permintaan.");

            if (string.IsNullOrWhiteSpace(warga.Kewarganegaraan)) warga.Kewarganegaraan = "WNI";

            // Field form yang masuk ke data pribadi (mis. Pendidikan & Kewarganegaraan untuk SKCK).
            var pendidikan = WaFormatParser.NormalkanPendidikan(data.Fields.GetValueOrDefault("pendidikan"));
            if (!string.IsNullOrWhiteSpace(pendidikan)) warga.Pendidikan = pendidikan;
            var kewarganegaraan = WaFormatParser.NormalkanKewarganegaraan(data.Fields.GetValueOrDefault("kewarganegaraan"));
            if (!string.IsNullOrWhiteSpace(kewarganegaraan)) warga.Kewarganegaraan = kewarganegaraan;

            _ = _desaRepository; // desa dimuat oleh pipeline validasi/generator.

            return warga;
        }

        private async Task IsiDataJenisKhusus(SuratData suratData, WaRequestData data)
        {
            var f = data.Fields;

            switch (suratData.NamaJenis.ToUpperInvariant())
            {
                case SuratConstants.SKU:
                    suratData.SKU.BidangUsaha = f.GetValueOrDefault("bidangusaha") ?? string.Empty;
                    if (int.TryParse(f.GetValueOrDefault("sejaktahun"), out var tahun))
                        suratData.SKU.SejakTahun = tahun;
                    suratData.SKU.LokasiUsaha = data.Warga?.AlamatLengkap;
                    break;

                case SuratConstants.SKTM:
                    suratData.SKTM.KeteranganKemiskinan = f.GetValueOrDefault("keterangan")
                        ?? data.Keperluan ?? "Keterangan tidak mampu";
                    break;

                case SuratConstants.IZIN_ORTU:
                    var anak = new WargaData
                    {
                        NIK = f.GetValueOrDefault("nikanak") ?? string.Empty,
                        Nama = f.GetValueOrDefault("namaanak") ?? string.Empty,
                        TempatLahir = f.GetValueOrDefault("tempatlahiranak") ?? string.Empty,
                        TanggalLahir = WaFormatParser.NormalkanTanggal(f.GetValueOrDefault("tanggallahiranak")) ?? string.Empty,
                        JenisKelamin = WaFormatParser.NormalkanJenisKelamin(f.GetValueOrDefault("jkanak")) ?? string.Empty,
                        Agama = WaFormatParser.NormalkanAgama(f.GetValueOrDefault("agamaanak")) ?? string.Empty,
                        StatusPerkawinan = WaFormatParser.NormalkanStatusPerkawinan(f.GetValueOrDefault("statusanak")) ?? string.Empty,
                        Pekerjaan = f.GetValueOrDefault("pekerjaananak") ?? string.Empty,
                        AlamatLengkap = f.GetValueOrDefault("alamatanak") ?? string.Empty,
                        Dusun = f.GetValueOrDefault("alamatanak") ?? string.Empty,
                        Pendidikan = string.Empty,
                        Kewarganegaraan = "WNI",
                        NamaJenis = SuratConstants.IZIN_ORTU,
                        IsForInstansi = false
                    };
                    int idAnak = await _wargaRepository.AddOrUpdateWargaAndGetIdAsync(anak);

                    suratData.IzinOrtu.ID_Anak = idAnak;
                    suratData.IzinOrtu.NIKAnak = anak.NIK;
                    suratData.IzinOrtu.NamaAnak = anak.Nama;
                    suratData.IzinOrtu.TempatLahirAnak = anak.TempatLahir;
                    suratData.IzinOrtu.TanggalLahirAnak = anak.TanggalLahir;
                    suratData.IzinOrtu.JenisKelaminAnak = anak.JenisKelamin;
                    suratData.IzinOrtu.AgamaAnak = anak.Agama;
                    suratData.IzinOrtu.StatusPerkawinanAnak = anak.StatusPerkawinan;
                    suratData.IzinOrtu.PekerjaanAnak = anak.Pekerjaan;
                    suratData.IzinOrtu.AlamatAnak = anak.AlamatLengkap;
                    suratData.IzinOrtu.NegaraTujuan = f.GetValueOrDefault("negaratujuan") ?? string.Empty;
                    suratData.IzinOrtu.NamaPT = f.GetValueOrDefault("namapt");
                    break;

                case SuratConstants.INSTANSI:
                    suratData.Instansi = new Instansi
                    {
                        NamaInstansi = f.GetValueOrDefault("nama") ?? data.Warga?.Nama ?? string.Empty,
                        AlamatInstansi = f.GetValueOrDefault("alamat") ?? data.Warga?.AlamatLengkap ?? string.Empty
                    };
                    break;
            }
        }
    }
}