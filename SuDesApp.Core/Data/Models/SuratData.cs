using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Data.Repositories;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json.Serialization;

namespace SuDesApp.Data.Models
{
    public class SuratData
    {
        private readonly ILogger<SuratData> _logger;
        private static readonly CultureInfo _indonesianCulture = new("id-ID");
        private readonly ISuratRepository _suratRepository;
        private readonly IWargaRepository _wargaRepository;
        private readonly IDesaRepository _desaRepository;
        private readonly IJenisSuratRepository _jenisSuratRepository;

        public int ID_Surat { get; set; }

        private string _namaJenis;
        [Required]
        public string NamaJenis
        {
            get => _namaJenis;
            set
            {
                _namaJenis = value;
                // Sinkronkan enum Jenis otomatis agar objek hasil pemetaan Dapper /
                // object initializer (yang tidak memanggil SetJenisFromNamaJenis)
                // tidak diam-diam memiliki Jenis default (Kematian).
                if (!string.IsNullOrWhiteSpace(value) && TryMapNamaJenisToEnum(value, out var jenis))
                {
                    Jenis = jenis;
                }
            }
        }
        [Required] public int ID_Jenis { get; set; }
        [Required] public string NomorSurat { get; set; }
        [Required] public DateTime TanggalSurat { get; set; }
        public string Keterangan { get; set; }
        public string Keperluan { get; set; }
        public string KodeJenis { get; set; }
        public string AdditionalData { get; set; }

        // ? KOLOM STATUS BARU
        public string Status { get; set; } = "Draft"; // Draft, Active, Cancelled

        // ? TIMESTAMP KOLOM BARU
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        // Ijin Tinggal Sementara
        public string? DusunTujuan { get; set; }
        public string? DesaTujuan { get; set; }
        public string? KecTujuan { get; set; }
        public string? KabTujuan { get; set; }

        // Ijin Tinggal Sementara — Penanggung Jawab di alamat tujuan
        public string? NikPenanggungJawab { get; set; }
        public string? NamaPenanggungJawab { get; set; }
        public string? TglLahirPenanggungJawab { get; set; }
        public string? PekerjaanPenanggungJawab { get; set; }


        // ? MODEL PROPERTIES YANG DIUPDATE
        public WargaData Warga { get; set; }
        public KematianData Kematian { get; set; }
        public SKUData SKU { get; set; }
        public SKTMData SKTM { get; set; } // ? BARU: Model SKTM terpisah
        public IzinOrtuData IzinOrtu { get; set; }
        public IjinTinggalData IjinTinggal { get; set; } // ? BARU: Model IjinTinggal
        public NtcrData Ntcr { get; set; } // ? BARU: Model NTCR (persyaratan pernikahan N1-N4)
        public GarapanData Garapan { get; set; }
        public Instansi Instansi { get; set; }
        public DesaData Desa { get; set; }
        public KenalLahirData KenalLahir { get; set; }
        public BedaNamaData BedaNama { get; set; }
        public AhliWaris AhliWaris { get; set; }
        public AhliWarisData AhliWarisData { get; set; }
        public JenisSuratEnum Jenis { get; set; }
        public JenisSuratKelas JenisSurat { get; set; }

        public List<GarapanData> RincianGarapans { get; set; }
        public string PejabatPenandatangan { get; set; }
        public string NamaPejabatPenandatangan { get; set; }
        public WargaData WargaKK { get; set; }
        public string DataSource1 { get; set; }
        public string DataSource2 { get; set; }

        public enum JenisSuratEnum
        {
            Kematian,
            SKU,
            IzinOrtu,
            Garapan,
            SKTM,
            SuratWarga,
            Instansi,
            BedaNama,
            KenalLahir,
            AhliWaris,
            IjinTinggal, // ? BARU
            Ntcr // ? BARU: NTCR (persyaratan pernikahan N1-N4)
        }

        public SuratData() : this(null, null, null, null) { }

        public SuratData(
            ISuratRepository suratRepository = null,
            IWargaRepository wargaRepository = null,
            IDesaRepository desaRepository = null,
            IJenisSuratRepository jenisSuratRepository = null,
            ILogger<SuratData> logger = null)
        {
            _logger = logger ?? NullLogger<SuratData>.Instance;
            _suratRepository = suratRepository;
            _wargaRepository = wargaRepository;
            _desaRepository = desaRepository;
            _jenisSuratRepository = jenisSuratRepository;

            InitializeSubmodels();
        }

        public SuratData(
            int idSurat, int idJenis, string namaJenis, string nomorSurat, DateTime tanggalSurat,
            string keterangan, int idWarga, string kodeJenis = null,
            ISuratRepository suratRepository = null, IWargaRepository wargaRepository = null,
            IDesaRepository desaRepository = null, IJenisSuratRepository jenisSuratRepository = null,
            ILogger<SuratData> logger = null)
            : this(suratRepository, wargaRepository, desaRepository, jenisSuratRepository, logger)
        {
            ID_Surat = idSurat;
            ID_Jenis = idJenis;
            NamaJenis = namaJenis;
            NomorSurat = nomorSurat;
            TanggalSurat = tanggalSurat;
            Keterangan = keterangan ?? string.Empty;
            KodeJenis = kodeJenis;
            Warga = new WargaData { ID_Warga = idWarga };
            SetJenisFromNamaJenis(namaJenis);
        }

        public SuratData(
            string namaJenis, string nomorSurat, string tanggalSurat, string keterangan, int idWarga,
            string kodeJenis = null, ISuratRepository suratRepository = null, IWargaRepository wargaRepository = null,
            IDesaRepository desaRepository = null, IJenisSuratRepository jenisSuratRepository = null,
            ILogger<SuratData> logger = null)
            : this(suratRepository, wargaRepository, desaRepository, jenisSuratRepository, logger)
        {
            NamaJenis = namaJenis;
            NomorSurat = nomorSurat;
            Keterangan = keterangan ?? string.Empty;
            KodeJenis = kodeJenis;
            Warga = new WargaData { ID_Warga = idWarga };
            SetJenisFromNamaJenis(namaJenis);

            if (!DateTime.TryParse(tanggalSurat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                _logger.LogWarning("Format tanggal surat tidak valid: {TanggalSurat}", tanggalSurat);
                throw new ArgumentException("Format tanggal surat tidak valid.");
            }
            TanggalSurat = parsedDate;
        }

        private void InitializeSubmodels()
        {
            Warga = new WargaData();
            Kematian = new KematianData
            {
                WargaMeninggal = new WargaData(),
                WargaPelapor = new WargaData()
            };
            SKU = new SKUData();
            SKTM = new SKTMData(); // ? BARU
            IzinOrtu = new IzinOrtuData();
            IjinTinggal = new IjinTinggalData(); // ? BARU
            Ntcr = new NtcrData(); // ? BARU
            Garapan = new GarapanData();
            RincianGarapans = new List<GarapanData>();
            Instansi = new Instansi();
            KenalLahir = new KenalLahirData();
            AhliWarisData = new AhliWarisData();
            BedaNama = new BedaNamaData();
            Desa = new DesaData();
            JenisSurat = new JenisSuratKelas();
        }

        public void SetJenisFromNamaJenis(string namaJenis)
        {
            if (string.IsNullOrWhiteSpace(namaJenis))
            {
                _logger.LogWarning("NamaJenis kosong di SetJenisFromNamaJenis");
                throw new ArgumentException("Nama jenis surat tidak boleh kosong.", nameof(namaJenis));
            }

            if (!TryMapNamaJenisToEnum(namaJenis, out var jenis))
            {
                throw new ArgumentException($"Nama jenis surat '{namaJenis}' tidak valid.");
            }

            Jenis = jenis;
            _logger.LogDebug("Jenis set: {Jenis} untuk NamaJenis: {NamaJenis}", Jenis, namaJenis);
        }

        private static bool TryMapNamaJenisToEnum(string namaJenis, out JenisSuratEnum jenis)
        {
            switch (namaJenis.ToUpperInvariant())
            {
                case SuratConstants.KEMATIAN: jenis = JenisSuratEnum.Kematian; return true;
                case SuratConstants.SKU: jenis = JenisSuratEnum.SKU; return true;
                case SuratConstants.IZIN_ORTU: jenis = JenisSuratEnum.IzinOrtu; return true;
                case SuratConstants.GARAPAN_SAWAH: jenis = JenisSuratEnum.Garapan; return true;
                case SuratConstants.SKTM: jenis = JenisSuratEnum.SKTM; return true;
                case SuratConstants.INSTANSI: jenis = JenisSuratEnum.Instansi; return true;
                case SuratConstants.DOMISILI_WARGA: jenis = JenisSuratEnum.SuratWarga; return true;
                case SuratConstants.SKD_UMUM: jenis = JenisSuratEnum.SuratWarga; return true;
                case SuratConstants.PENGANTAR_SKCK: jenis = JenisSuratEnum.SuratWarga; return true;
                case SuratConstants.BEDANAMA: jenis = JenisSuratEnum.BedaNama; return true;
                case SuratConstants.KENAL_LAHIR: jenis = JenisSuratEnum.KenalLahir; return true;
                case SuratConstants.AHLI_WARIS: jenis = JenisSuratEnum.AhliWaris; return true;
                case SuratConstants.IJIN_TINGGAL: jenis = JenisSuratEnum.IjinTinggal; return true; // ? BARU
                case SuratConstants.NTCR_N1: jenis = JenisSuratEnum.Ntcr; return true; // ? BARU
                case SuratConstants.NTCR_N2: jenis = JenisSuratEnum.Ntcr; return true; // ? BARU
                case SuratConstants.NTCR_N3: jenis = JenisSuratEnum.Ntcr; return true; // ? BARU
                case SuratConstants.NTCR_N4: jenis = JenisSuratEnum.Ntcr; return true; // ? BARU
                default: jenis = default; return false;
            }
        }

        public async Task<IEnumerable<string>> ValidateAsync()
        {
            try
            {
                if (_wargaRepository == null || _desaRepository == null)
                {
                    _logger?.LogWarning("Repository dependencies are null. Skipping full validation.");
                    var basicErrors = new List<string>();

                    if (string.IsNullOrWhiteSpace(NomorSurat))
                        basicErrors.Add("Nomor surat wajib diisi.");

                    if (TanggalSurat == default)
                        basicErrors.Add("Tanggal surat wajib diisi.");

                    return basicErrors;
                }

                var validator = new SuratDataValidator(this, _logger, _wargaRepository, _desaRepository);
                return await validator.ValidateAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error during validation, continuing with save operation");
                return new List<string>();
            }
        }

        public static bool IsValidNamaJenis(string namaJenis)
        {
            return !string.IsNullOrWhiteSpace(namaJenis) &&
                   SuratConstants.ValidJenisSurat.Contains(namaJenis, StringComparer.OrdinalIgnoreCase);
        }


        public async Task EnsureDesaDataLoadedAsync(IDesaRepository? desaRepository = null)
        {
            // Cek apakah data desa sudah valid
            if (IsDesaDataValid(Desa) && !string.IsNullOrWhiteSpace(Desa.Alamat))
            {
                _logger.LogInformation("Data desa sudah valid: NamaDesa={NamaDesa}", Desa.NamaDesa);
                return;
            }

            if (desaRepository == null)
            {
                _logger.LogError("IDesaRepository tidak tersedia");
                // Jangan throw exception, buat data default
                Desa = CreateDefaultDesaData();
                return;
            }

            try
            {
                var loadedDesa = await desaRepository.GetInfoDesaAsync();
                if (loadedDesa != null && IsDesaDataValid(loadedDesa))
                {
                    Desa = loadedDesa;
                    _logger.LogInformation("Data desa berhasil dimuat: NamaDesa={NamaDesa}", Desa.NamaDesa);
                }
                else
                {
                    _logger.LogWarning("Data desa dari database tidak valid, menggunakan data default");
                    Desa = CreateDefaultDesaData();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memuat data desa, menggunakan data default");
                Desa = CreateDefaultDesaData();
            }
        }

        // Method helper untuk membuat data desa default
        private DesaData CreateDefaultDesaData()
        {
            return new DesaData
            {
                NamaDesa = "Nama Desa",
                Kecamatan = "Nama Kecamatan",
                Kabupaten = "Nama Kabupaten",
                Alamat = "Alamat Desa",
                Kodepos = "00000",
                KepalaDesa = "Nama Kepala Desa",
                SekretarisDesa = "Nama Sekretaris Desa",
                NamaCamat = "Nama Camat",
                NipCamat = "000000000000000000",
                GolCamat = "IV/a"
            };
        }

        // Perbaikan method IsDesaDataValid
        public bool IsDesaDataValid(DesaData desa)
        {
            if (desa == null)
            {
                _logger.LogWarning("DesaData null");
                return false;
            }

            var requiredFields = new Dictionary<string, string>
            {
                { nameof(desa.NamaDesa), desa.NamaDesa },
                { nameof(desa.Kecamatan), desa.Kecamatan },
                { nameof(desa.Kabupaten), desa.Kabupaten },
                { nameof(desa.KepalaDesa), desa.KepalaDesa }
            };

            var missingFields = requiredFields
                .Where(f => string.IsNullOrWhiteSpace(f.Value))
                .Select(f => f.Key)
                .ToList();

            if (missingFields.Any())
            {
                _logger.LogWarning("Data desa tidak lengkap: MissingFields={MissingFields}",
                    string.Join(", ", missingFields));
                return false;
            }

            return true;
        }
    }
    public class JenisSuratKelas
    {
        public int ID_Jenis { get; set; }
        public string KodeJenis { get; set; }
        public string NamaJenis { get; set; }
        public string Deskripsi { get; set; } // ? KOLOM BARU
        public bool IsActive { get; set; } = true; // ? KOLOM BARU
    }

    // ==============================================
    // WARGA DATA (UPDATED)
    // ==============================================
    public class WargaData
    {
        // Properties yang sudah ada tetap sama...
        public bool IsForInstansi { get; set; } = false;
        public bool IsForKematian { get; set; } = false;

        [Required]
        public int ID_Warga { get; set; }

        [Required]
        [StringLength(16, MinimumLength = 16)]
        public string NIK { get; set; }

        [Required]
        [StringLength(100)]
        public string Nama { get; set; }

        [StringLength(100)]
        public string TempatLahir { get; set; }
        public string TanggalLahir { get; set; }
        public string JenisKelamin { get; set; }
        public string Agama { get; set; }
        public string StatusPerkawinan { get; set; }

        [StringLength(100)]
        public string Pekerjaan { get; set; }

        [StringLength(100)]
        public string Dusun { get; set; }

        [Required]
        [StringLength(100)]
        public string Desa { get; set; }

        [Required]
        [StringLength(100)]
        public string Kecamatan { get; set; }

        [Required]
        [StringLength(100)]
        public string Kabupaten { get; set; }

        public string AlamatLengkap { get; set; }

        // ? LEGACY COMPATIBILITY: Alamat property for legacy code
        public string Alamat 
        { 
            get => AlamatLengkap; 
            set => AlamatLengkap = value; 
        }

        [StringLength(100)]
        public string Pendidikan { get; set; }

        [StringLength(100)]
        public string Kewarganegaraan { get; set; }
        public string NamaJenis { get; set; }

        // ? TIMESTAMP KOLOM BARU
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        public bool IsWargaMeninggal { get; set; }
        public bool IsWargaPelapor { get; set; }

        // ? LEGACY COMPATIBILITY: lowercase property names
public bool isForInstansi
        {
            get => IsForInstansi;
            set => IsForInstansi = value;
        }
        public bool isForKematian
        {
            get => IsForKematian;
            set => IsForKematian = value;
        }

        // Constructors dan methods tetap sama...
        public WargaData() { }

        public WargaData(WargaData otherWarga)
        {
            if (otherWarga == null) return;
            ID_Warga = otherWarga.ID_Warga;
            NIK = otherWarga.NIK;
            Nama = otherWarga.Nama;
            TempatLahir = otherWarga.TempatLahir;
            TanggalLahir = otherWarga.TanggalLahir;
            JenisKelamin = otherWarga.JenisKelamin;
            Agama = otherWarga.Agama;
            StatusPerkawinan = otherWarga.StatusPerkawinan;
            Pekerjaan = otherWarga.Pekerjaan;
            Dusun = otherWarga.Dusun;
            Desa = otherWarga.Desa;
            Kecamatan = otherWarga.Kecamatan;
            Kabupaten = otherWarga.Kabupaten;
            Pendidikan = otherWarga.Pendidikan;
            Kewarganegaraan = otherWarga.Kewarganegaraan;
            NamaJenis = otherWarga.NamaJenis;
            IsForInstansi = otherWarga.IsForInstansi;
            IsForKematian = otherWarga.IsForKematian;
            CreatedAt = otherWarga.CreatedAt;
            UpdatedAt = otherWarga.UpdatedAt;
        }

        // Method IsValid tetap sama...
        public bool IsValid(out List<string> errors)
        {
            errors = new List<string>();

            // Validasi NIK - wajib dan harus 16 digit
            if (string.IsNullOrWhiteSpace(NIK))
            {
                errors.Add("NIK tidak boleh kosong");
            }
            else if (NIK.Length != 16 || !NIK.All(char.IsDigit))
            {
                errors.Add($"NIK harus 16 digit angka (saat ini: {NIK.Length} karakter)");
            }

            // Validasi nama - wajib dan minimal 2 karakter
            if (string.IsNullOrWhiteSpace(Nama))
            {
                errors.Add("Nama tidak boleh kosong");
            }
            else if (Nama.Trim().Length < 2)
            {
                errors.Add("Nama minimal 2 karakter");
            }

            // Validasi berbeda untuk warga meninggal vs pelapor
            if (IsWargaMeninggal)
            {
                if (string.IsNullOrWhiteSpace(Nama))
                    errors.Add("Nama warga meninggal wajib diisi");
            }
            else if (IsWargaPelapor)
            {
                if (string.IsNullOrWhiteSpace(NIK) || NIK.Length != 16)
                    errors.Add("NIK pelapor harus 16 digit");

                if (string.IsNullOrWhiteSpace(Nama))
                    errors.Add("Nama pelapor wajib diisi");
            }

            // Jika untuk kematian, hanya validasi dasar
            if (IsForKematian)
            {
                return errors.Count == 0;
            }

            // Jika untuk instansi, validasi khusus
            if (IsForInstansi)
            {
                if (string.IsNullOrWhiteSpace(NIK))
                {
                    errors.Add("NIK wajib diisi untuk surat instansi");
                }
                return errors.Count == 0;
            }

            if (!IsForInstansi && string.IsNullOrWhiteSpace(NIK))
            {
                errors.Add("NIK tidak boleh kosong");
            }

            // Validasi standar untuk warga biasa
            if (string.IsNullOrWhiteSpace(TempatLahir))
                errors.Add("Tempat lahir tidak boleh kosong");

            if (string.IsNullOrWhiteSpace(TanggalLahir))
                errors.Add("Tanggal lahir tidak boleh kosong");

            if (string.IsNullOrWhiteSpace(JenisKelamin))
                errors.Add("Jenis kelamin tidak boleh kosong");

            if (string.IsNullOrWhiteSpace(Desa))
                errors.Add("Desa tidak boleh kosong");

            if (string.IsNullOrWhiteSpace(Kecamatan))
                errors.Add("Kecamatan tidak boleh kosong");

            if (string.IsNullOrWhiteSpace(Kabupaten))
                errors.Add("Kabupaten tidak boleh kosong");

            // Validasi khusus berdasarkan jenis surat
            if (!string.IsNullOrWhiteSpace(NamaJenis))
            {
                switch (NamaJenis.ToUpperInvariant())
                {
                    case SuratConstants.BEDANAMA:
                        if (string.IsNullOrWhiteSpace(Agama))
                            errors.Add("Agama wajib diisi untuk surat Beda Nama");
                        if (string.IsNullOrWhiteSpace(StatusPerkawinan))
                            errors.Add("Status perkawinan wajib diisi untuk surat Beda Nama");
                        break;

                    case SuratConstants.GARAPAN_SAWAH:
                        if (string.IsNullOrWhiteSpace(Agama))
                            errors.Add("Agama wajib diisi");
                        break;

                    default:
                        if (string.IsNullOrWhiteSpace(Agama))
                            errors.Add("Agama tidak boleh kosong");
                        if (string.IsNullOrWhiteSpace(StatusPerkawinan))
                            errors.Add("Status perkawinan tidak boleh kosong");
                        break;
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(Agama))
                    errors.Add("Agama tidak boleh kosong");
                if (string.IsNullOrWhiteSpace(StatusPerkawinan))
                    errors.Add("Status perkawinan tidak boleh kosong");
            }

            return errors.Count == 0;
        }

        public bool IsValid()
        {
            var errors = new List<string>();
            return IsValid(out errors);
        }
    }

    // ==============================================
    // KEMATIAN DATA (UPDATED)
    // ==============================================
    public class KematianData
    {
        public int ID_Surat { get; set; }

        [Required]
        public int ID_Warga_Meninggal { get; set; }

        [Required]
        public int ID_Warga_Pelapor { get; set; }

        public string NIKPelapor { get; set; }
        public string NamaPelapor { get; set; }
        public string AgamaPelapor { get; set; }
        public string UmurPelapor { get; set; }
        public string PekerjaanPelapor { get; set; }
        public string AlamatPelapor { get; set; }
        public string TanggalKematian { get; set; }
        public string HariKematian { get; set; }
        public string PukulKematian { get; set; }

        [Required]
        public string PenyebabKematian { get; set; }

        // ? KOLOM BARU
        public string TempatKematian { get; set; }

        [Required]
        public string HubunganPelapor { get; set; }

        // Data tambahan untuk tampilan (tidak disimpan di database)
        public WargaData WargaMeninggal { get; set; }
        public WargaData WargaPelapor { get; set; }

        public bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(PenyebabKematian) &&
                   !string.IsNullOrWhiteSpace(HubunganPelapor) &&
                   !string.IsNullOrWhiteSpace(NamaPelapor);
        }
    }

    // ==============================================
    // SKU DATA (UPDATED)
    // ==============================================
    public class SKUData
    {
        [Required]
        [StringLength(100)]
        public string BidangUsaha { get; set; }

        [Required]
        public int SejakTahun { get; set; }

        public string LokasiUsaha { get; set; }

        public bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(BidangUsaha) &&
                   SejakTahun > 1900 &&
                   SejakTahun <= DateTime.Now.Year;
        }
    }


    // ==============================================
    // SKTM DATA (BARU - TERPISAH)
    // ==============================================
    public class SKTMData
    {
        public string KeteranganKemiskinan { get; set; }

        // ? KOLOM BARU
        public decimal? PenghasilanPerBulan { get; set; }

        // ? KOLOM BARU
        public int? JumlahTanggungan { get; set; }

        public bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(KeteranganKemiskinan);
        }
    }

    // ==============================================
    // IJIN TINGGAL DATA (BARU)
    // ==============================================
    public class IjinTinggalData
    {
        public int ID_Penjamin { get; set; }

        public string AlamatAsal { get; set; }

        public DateTime? TanggalMulai { get; set; }
        public DateTime? TanggalSelesai { get; set; }

        public string TujuanTinggal { get; set; }

        public bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(AlamatAsal) &&
                   !string.IsNullOrWhiteSpace(TujuanTinggal) &&
                   (TanggalSelesai == null || TanggalMulai == null || TanggalSelesai >= TanggalMulai);
        }
    }

    // ==============================================
    // NTCR DATA (BARU) — N1, N2, N3, N4
    // Data calon mempelai dan orang tua untuk surat
    // persyaratan pendaftaran pernikahan (NTCR).
    // ==============================================
    public class NtcrData
    {
        public int ID_CalonIstri { get; set; }

        // Calon istri
        public string NikIstri { get; set; }
        public string NamaIstri { get; set; }
        public string TempatLahirIstri { get; set; }
        public string TanggalLahirIstri { get; set; }
        public string AgamaIstri { get; set; }
        public string PekerjaanIstri { get; set; }
        public string AlamatIstri { get; set; }

        // Ayah & ibu calon suami (pemohon)
        public string NamaAyahCalonSuami { get; set; }
        public string NamaIbuCalonSuami { get; set; }

        // Ayah & ibu calon istri
        public string NamaAyahCalonIstri { get; set; }
        public string NamaIbuCalonIstri { get; set; }

        // Status perkawinan calon istri (penting untuk N2 — sumber temuan)
        public string StatusPerkawinanIstri { get; set; }

        // Kolom temuan (N2): dasar keterangan janda/duda, mis. "Akta Kematian No. 123/2020"
        public string KeteranganTemuan { get; set; }

        // Tujuan surat (N3): kepentingan pendaftaran pernikahan di KUA/Kemenag
        public string TujuanSurat { get; set; }

        public bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(NamaIstri) ||
                   !string.IsNullOrWhiteSpace(NamaAyahCalonSuami) ||
                   !string.IsNullOrWhiteSpace(NamaAyahCalonIstri);
        }
    }

    // ==============================================
    // IZIN ORTU DATA (UPDATED)
    // ==============================================
    public class IzinOrtuData
    {
        [Required]
        public int ID_Surat { get; set; }

        [Required]
        public int ID_Anak { get; set; }

        // ? LEGACY COMPATIBILITY: ID_Warga_Anak property
        public int ID_Warga_Anak
        {
            get => ID_Anak;
            set => ID_Anak = value;
        }

        [Required]
        [StringLength(100)]
        public string NegaraTujuan { get; set; }

        [StringLength(100)]
        public string NamaPT { get; set; }

        // Data anak untuk tampilan (tidak disimpan di database)
        public string NamaAnak { get; set; }
        public string NIKAnak { get; set; }
        public string TempatLahirAnak { get; set; }
        public string TanggalLahirAnak { get; set; }
        public string JenisKelaminAnak { get; set; }
        public string AgamaAnak { get; set; }
        public string StatusPerkawinanAnak { get; set; }
        public string AlamatAnak { get; set; }
        public string PekerjaanAnak { get; set; }
    }

    // ==============================================
    // INSTANSI (UPDATED)
    // ==============================================
    public class Instansi
    {
        [Required]
        [StringLength(100)]
        public string NamaInstansi { get; set; }

        [Required]
        [StringLength(200)]
        public string AlamatInstansi { get; set; }

        // ? KOLOM BARU
        public string PimpinanInstansi { get; set; }


             public bool IsValid()
             {
                 return !string.IsNullOrWhiteSpace(NamaInstansi) &&
                        !string.IsNullOrWhiteSpace(AlamatInstansi);
             }
         }

        // ==============================================
        // BEDA NAMA DATA (UPDATED)
        // ==============================================
    public class BedaNamaData
    {
        [Required]
        public int ID_Warga { get; set; } // ? DIPERBAIKI: Hanya satu ID_Warga

        [Required]
        public string SumberDataKoreksi { get; set; }

        [Required]
        public string SumberDataKeliru { get; set; }

        // ? KOLOM BARU
        public string AlasanPerbedaan { get; set; }

        // Data tambahan untuk tampilan
        public WargaData Warga { get; set; }

        // Warga kedua (perbandingan data), disimpan denormalized
        public string NIK2 { get; set; }
        public string Nama2 { get; set; }
        public string TempatLahir2 { get; set; }
        public string TanggalLahir2 { get; set; }
        public string JenisKelamin2 { get; set; }
        public string Dusun2 { get; set; }
        public string Desa2 { get; set; }
        public string Kecamatan2 { get; set; }
        public string Kabupaten2 { get; set; }

        public bool IsValid()
        {
            return ID_Warga > 0 ||
                   !string.IsNullOrWhiteSpace(NIK2) ||
                   !string.IsNullOrWhiteSpace(SumberDataKoreksi) ||
                   !string.IsNullOrWhiteSpace(SumberDataKeliru);
        }
    }

    public class GarapanData
    {
        public int ID_GarapanItem { get; set; }

        [Required]
        public double Luas { get; set; }

        [Required]
        [StringLength(200)]
        public string Lokasi { get; set; }

        [Required]
        [StringLength(100)]
        public string PemilikTanah { get; set; }

        [StringLength(50)]
        public string NomorPersil { get; set; }

        [Required]
        [StringLength(50)]
        public string KeteranganGarapan { get; set; }

        public bool IsValid()
        {
            return Luas > 0 &&
                   !string.IsNullOrWhiteSpace(Lokasi) &&
                   !string.IsNullOrWhiteSpace(PemilikTanah) &&
                   !string.IsNullOrWhiteSpace(KeteranganGarapan);
        }
    }

    public class DesaData
    {
        [Required]
        public string NamaDesa { get; set; }
        [Required]
        public string Kecamatan { get; set; }
        [Required]
        public string Kabupaten { get; set; }
        [Required]
        public string Alamat { get; set; }
        [Required]
        public string Kodepos { get; set; }
        [Required]
        public string KepalaDesa { get; set; }
        [Required]
        public string SekretarisDesa { get; set; }
        public string NamaCamat { get; set; }
        public string NipCamat { get; set; }
        public string GolCamat { get; set; }
    }
}
