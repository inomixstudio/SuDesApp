using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Data.Repositories;
using System.Text.RegularExpressions;

namespace SuDesApp.Data.Models
{
    public class SuratDataValidator
    {
        private readonly SuratData _suratData;
        private readonly ILogger _logger;
        private readonly IWargaRepository _wargaRepository;
        private readonly IDesaRepository _desaRepository;

        public SuratDataValidator(
            SuratData suratData,
            ILogger logger,
            IWargaRepository wargaRepository,
            IDesaRepository desaRepository)
        {
            _suratData = suratData ?? throw new ArgumentNullException(nameof(suratData));
            _logger = logger ?? NullLogger.Instance;
            _wargaRepository = wargaRepository ?? throw new ArgumentNullException(nameof(wargaRepository));
            _desaRepository = desaRepository ?? throw new ArgumentNullException(nameof(desaRepository));
        }

        public async Task<IEnumerable<string>> ValidateAsync()
        {
            var errors = new List<string>();

            // Mode DRAFT: surat boleh belum lengkap (mis. baru NIK/Nama terisi saat
            // pengguna menekan Batal lalu memilih "simpan sebagai draft").
            // Lewati validasi ketat agar draft tetap dapat disimpan dan
            // dilanjutkan lewat Edit — surat menjadi valid penuh saat dilengkapi.
            if (string.Equals(_suratData.Status, "Draft", StringComparison.OrdinalIgnoreCase))
            {
                LogValidationResults(errors);
                return errors;
            }

            ValidateBasicFields(errors);
            ValidateStatus(errors); // ? VALIDASI STATUS BARU
            await ValidateDesaAsync(errors);

            if (_suratData.NamaJenis != SuratConstants.INSTANSI)
            {
                await ValidateWargaAsync(errors);
            }
            else
            {
                ValidateInstansi(errors);
            }

            ValidateSubmodels(errors);

            if (_suratData.Jenis == SuratData.JenisSuratEnum.SuratWarga &&
                (_suratData.Warga == null || !_suratData.Warga.IsValid()))
            {
                errors.Add("Data warga wajib diisi dan valid untuk Surat Warga.");
            }

            if (!SuratData.IsValidNamaJenis(_suratData.NamaJenis))
            {
                errors.Add($"Nama jenis surat '{_suratData.NamaJenis}' tidak valid.");
            }

            LogValidationResults(errors);
            return errors;
        }

        private void ValidateBasicFields(List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(_suratData.NomorSurat))
                errors.Add("Nomor surat wajib diisi.");
            else if (_suratData.NomorSurat.Length > 50)
                errors.Add("Nomor surat tidak boleh lebih dari 50 karakter.");

            if (_suratData.TanggalSurat == default)
                errors.Add("Tanggal surat wajib diisi.");
            else if (_suratData.TanggalSurat > DateTime.Now)
                errors.Add("Tanggal surat tidak boleh di masa depan.");
        }

        // ? VALIDASI STATUS BARU
        private void ValidateStatus(List<string> errors)
        {
            if (!string.IsNullOrEmpty(_suratData.Status) &&
                !SuratConstants.ValidStatus.Contains(_suratData.Status))
            {
                errors.Add($"Status '{_suratData.Status}' tidak valid. Status yang valid: {string.Join(", ", SuratConstants.ValidStatus)}");
            }
        }

        private async Task ValidateDesaAsync(List<string> errors)
        {
            if (!_suratData.IsDesaDataValid(_suratData.Desa))
            {
                try
                {
                    _suratData.Desa = await _desaRepository.GetInfoDesaAsync();
                    if (_suratData.Desa == null || !_suratData.IsDesaDataValid(_suratData.Desa))
                        errors.Add("Data desa tidak valid atau tidak ditemukan.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Gagal memuat data desa untuk validasi.");
                    errors.Add("Gagal memuat data desa.");
                }
            }
        }

        private async Task ValidateWargaAsync(List<string> errors)
        {
            if (_suratData.Warga == null)
            {
                _logger.LogDebug("Warga null untuk NamaJenis={NamaJenis}", _suratData.NamaJenis);
                errors.Add("Data warga tidak boleh kosong untuk jenis surat ini.");
                return;
            }

            // ? VALIDASI NIK KHUSUS
            if (_suratData.NamaJenis?.ToUpperInvariant() == SuratConstants.INSTANSI)
            {
                if (_suratData.Warga.NIK != SuratConstants.NIK_INSTANSI)
                {
                    errors.Add($"NIK untuk surat INSTANSI harus '{SuratConstants.NIK_INSTANSI}'.");
                }
                _suratData.Warga.IsForInstansi = true;
            }
            else if (_suratData.NamaJenis?.ToUpperInvariant() == SuratConstants.KEMATIAN)
            {
                _suratData.Warga.IsForKematian = true;
                if (_suratData.Warga.NIK != SuratConstants.NIK_KEMATIAN &&
                    (string.IsNullOrWhiteSpace(_suratData.Warga.NIK) || !_suratData.Warga.NIK.All(char.IsDigit)))
                {
                    errors.Add("NIK warga (almarhum/ah) harus 16 digit angka.");
                }
            }
            else if (!_suratData.Warga.IsValid())
            {
                errors.Add("Data warga tidak valid.");
            }

            // ? VALIDASI BERDASARKAN JENIS SURAT
            switch (_suratData.NamaJenis?.ToUpperInvariant())
            {
                case SuratConstants.IJIN_TINGGAL:
                    ValidateIjinTinggalAsync(errors);
                    break;
                case SuratConstants.NTCR_N1:
                case SuratConstants.NTCR_N2:
                case SuratConstants.NTCR_N3:
                case SuratConstants.NTCR_N4:
                    ValidateNtcr(errors);
                    break;
            }
        }

        // ? VALIDASI IJIN TINGGAL BARU
        private void ValidateIjinTinggalAsync(List<string> errors)
        {
            // UI menyimpan alamat tujuan di properti top-level SuratData (DusunTujuan/DesaTujuan/
            // KecTujuan/KabTujuan), bukan di sub-model IjinTinggal. Validasi mengikuti form.
            if (string.IsNullOrWhiteSpace(_suratData.DesaTujuan))
            {
                errors.Add("Desa tujuan wajib diisi.");
            }

            if (string.IsNullOrWhiteSpace(_suratData.KecTujuan))
            {
                errors.Add("Kecamatan tujuan wajib diisi.");
            }

            if (string.IsNullOrWhiteSpace(_suratData.KabTujuan))
            {
                errors.Add("Kabupaten tujuan wajib diisi.");
            }
        }

        // ? VALIDASI NTCR BARU (N1-N4)
        private void ValidateNtcr(List<string> errors)
        {
            if (_suratData.Ntcr == null)
            {
                errors.Add("Data calon mempelai tidak boleh kosong untuk surat NTCR.");
                return;
            }

            // Calon istri wajib diisi
            if (string.IsNullOrWhiteSpace(_suratData.Ntcr.NamaIstri))
            {
                errors.Add("Nama calon istri wajib diisi.");
            }

            if (string.IsNullOrWhiteSpace(_suratData.Ntcr.NikIstri) ||
                _suratData.Ntcr.NikIstri.Length != 16 ||
                !_suratData.Ntcr.NikIstri.All(char.IsDigit))
            {
                errors.Add("NIK calon istri harus 16 digit angka.");
            }

            // Nama orang tua calon mempelai untuk N1/N3/N4
            if (_suratData.NamaJenis?.ToUpperInvariant() is SuratConstants.NTCR_N1 or SuratConstants.NTCR_N3 or SuratConstants.NTCR_N4)
            {
                if (string.IsNullOrWhiteSpace(_suratData.Ntcr.NamaAyahCalonSuami))
                {
                    errors.Add("Nama ayah calon suami wajib diisi.");
                }

                if (string.IsNullOrWhiteSpace(_suratData.Ntcr.NamaAyahCalonIstri))
                {
                    errors.Add("Nama ayah calon istri wajib diisi.");
                }
            }
        }

        private void ValidateInstansi(List<string> errors)
        {
            _logger.LogDebug("Validating INSTANSI data");

            if (_suratData.Instansi == null || !_suratData.Instansi.IsValid())
                errors.Add("Data instansi tidak valid atau kosong.");

            if (_suratData.Warga != null &&
                _suratData.Warga.NIK != SuratConstants.NIK_INSTANSI &&
                !_suratData.Warga.IsValid())
                errors.Add("Data warga penanggung jawab instansi tidak valid.");
        }

        private void ValidateSubmodels(List<string> errors)
        {
            switch (_suratData.NamaJenis?.ToUpperInvariant())
            {
                case SuratConstants.KEMATIAN:
                    ValidateKematian(errors);
                    break;
                case SuratConstants.SKU:
                    ValidateSKU(errors);
                    break;
                case SuratConstants.SKTM:
                    ValidateSKTM(errors);
                    break;
                case SuratConstants.IJIN_TINGGAL:
                    ValidateIjinTinggalModel(errors);
                    break;
                case SuratConstants.GARAPAN_SAWAH:
                    ValidateGarapan(errors);
                    break;
                case SuratConstants.INSTANSI:
                    ValidateInstansiModel(errors);
                    break;
                case SuratConstants.KENAL_LAHIR:
                    ValidateKenalLahir(errors);
                    break;
                case SuratConstants.AHLI_WARIS:
                    ValidateAhliWaris(errors);
                    break;
                case SuratConstants.BEDANAMA:
                    ValidateBedaNama(errors);
                    break;
            }
        }

        // ? VALIDASI KEMATIAN DENGAN KOLOM BARU
        private void ValidateKematian(List<string> errors)
        {
            if (_suratData.Kematian == null || !_suratData.Kematian.IsValid())
            {
                errors.Add("Data kematian tidak valid atau kosong.");
                return;
            }

            // Validasi hubungan pelapor
            if (!string.IsNullOrEmpty(_suratData.Kematian.HubunganPelapor) &&
                !SuratConstants.ValidHubunganKeluarga.Contains(_suratData.Kematian.HubunganPelapor))
            {
                errors.Add($"Hubungan pelapor '{_suratData.Kematian.HubunganPelapor}' tidak valid.");
            }
        }

        // ? VALIDASI SKU DENGAN KOLOM BARU
        private void ValidateSKU(List<string> errors)
        {
            if (_suratData.SKU == null || !_suratData.SKU.IsValid())
            {
                errors.Add("Data SKU tidak valid atau kosong.");
                return;
            }
        }

        // ? VALIDASI SKTM BARU
        private void ValidateSKTM(List<string> errors)
        {
            // Data utama SKTM tersimpan di Surat.Keterangan (tidak ada tabel tambahan).
            // Sub-model hanya membawa nilai tambahan opsional, jadi jangan blokir simpan
            // hanya karena sub-model kosong.
            if (_suratData.SKTM == null)
            {
                return;
            }

            if (_suratData.SKTM.PenghasilanPerBulan.HasValue && _suratData.SKTM.PenghasilanPerBulan < 0)
            {
                errors.Add("Penghasilan per bulan tidak boleh negatif.");
            }

            if (_suratData.SKTM.JumlahTanggungan.HasValue && _suratData.SKTM.JumlahTanggungan < 0)
            {
                errors.Add("Jumlah tanggungan tidak boleh negatif.");
            }
        }

        // ? VALIDASI IJIN TINGGAL MODEL
        private void ValidateIjinTinggalModel(List<string> errors)
        {
            // UI menyimpan data tujuan di properti top-level SuratData (DusunTujuan..KabTujuan),
            // bukan di sub-model IjinTinggal. Sub-model hanya divalidasi bila terisi.
            if (_suratData.IjinTinggal != null && !_suratData.IjinTinggal.IsValid())
                errors.Add("Data ijin tinggal tidak valid atau kosong.");
        }

        private void ValidateGarapan(List<string> errors)
        {
            if (_suratData.RincianGarapans == null || _suratData.RincianGarapans.Count == 0)
            {
                errors.Add("Minimal satu rincian data garapan harus diisi.");
            }
            else
            {
                for (int i = 0; i < _suratData.RincianGarapans.Count; i++)
                {
                    if (!_suratData.RincianGarapans[i].IsValid())
                    {
                        errors.Add($"Data rincian garapan ke-{i + 1} tidak valid.");
                    }
                }
            }
        }

        private void ValidateInstansiModel(List<string> errors)
        {
            if (_suratData.Instansi == null || !_suratData.Instansi.IsValid())
                errors.Add("Data instansi tidak valid atau kosong.");
        }

        // ? VALIDASI KENAL LAHIR DENGAN KOLOM BARU
        private void ValidateKenalLahir(List<string> errors)
        {
            if (_suratData.KenalLahir == null || !_suratData.KenalLahir.IsValid())
            {
                errors.Add("Data Kenal Lahir (Ayah, Ibu, Anak) tidak valid atau kosong.");
                return;
            }

        }

        // ? VALIDASI AHLI WARIS DENGAN KOLOM BARU
        private void ValidateAhliWaris(List<string> errors)
        {
            if (_suratData.AhliWaris == null || _suratData.AhliWaris.Waris == null || !_suratData.AhliWaris.Waris.Any())
            {
                errors.Add("Data ahli waris tidak valid atau kosong.");
                return;
            }

            foreach (var waris in _suratData.AhliWaris.Waris)
            {
                if (string.IsNullOrWhiteSpace(waris.NamaWaris) || string.IsNullOrWhiteSpace(waris.NIKWaris))
                    errors.Add("NamaWaris dan NIKWaris wajib diisi.");
                else if (!Regex.IsMatch(waris.NIKWaris, @"^\d{16}$"))
                    errors.Add($"NIKWaris '{waris.NIKWaris}' tidak valid (harus 16 digit angka).");

                // Validasi hubungan waris
                if (!string.IsNullOrEmpty(waris.HubunganWaris) &&
                    !SuratConstants.ValidHubunganKeluarga.Contains(waris.HubunganWaris))
                {
                    errors.Add($"Hubungan waris '{waris.HubunganWaris}' tidak valid.");
                }
            }
        }

        // ? VALIDASI BEDA NAMA DENGAN KOLOM BARU
        private void ValidateBedaNama(List<string> errors)
        {
            if (_suratData.BedaNama == null)
            {
                errors.Add("Data BedaNama tidak boleh kosong.");
                return;
            }

            if (!_suratData.BedaNama.IsValid())
            {
                errors.Add("Data BedaNama tidak valid.");
            }

            // Validasi sumber data tidak boleh sama
            if (_suratData.BedaNama.SumberDataKoreksi?.Equals(_suratData.BedaNama.SumberDataKeliru, StringComparison.OrdinalIgnoreCase) == true)
            {
                errors.Add("Sumber data koreksi dan sumber data keliru tidak boleh sama.");
            }

            // Validasi sumber data
            if (string.IsNullOrWhiteSpace(_suratData.BedaNama.SumberDataKoreksi))
            {
                errors.Add("Sumber data koreksi wajib diisi.");
            }

            if (string.IsNullOrWhiteSpace(_suratData.BedaNama.SumberDataKeliru))
            {
                errors.Add("Sumber data keliru wajib diisi.");
            }

            // Validasi warga harus ada
            if (_suratData.BedaNama.Warga == null)
            {
                errors.Add("Data warga untuk BedaNama tidak lengkap.");
            }
        }

        private void LogValidationResults(List<string> errors)
        {
            if (errors.Any())
            {
                _logger.LogDebug("Validation errors: {Errors}, Warga: NIK={NIK}, Nama={Nama}, Status={Status}",
                    string.Join("; ", errors),
                    _suratData.Warga?.NIK ?? "null",
                    _suratData.Warga?.Nama ?? "null",
                    _suratData.Status ?? "null");
            }
            else
            {
                _logger.LogDebug("ValidateAsync passed for NamaJenis={NamaJenis}, Status={Status}",
                    _suratData.NamaJenis, _suratData.Status);
            }
        }
    }
}
