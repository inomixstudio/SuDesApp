using System.IO;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Interfaces;
using SuDesApp.Data.Models;
using SuDesApp.GeneratorPdf;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// ViewModel form Permohonan Rekening Koran — migrasi 1:1 dari
    /// Views/Tambahan/PermohonanRekeningKoran.cs (WinForms).
    /// Menghasilkan PDF Permohonan Print Out Rekening Koran via RekeningKoranGenerator.
    /// </summary>
    public class RekeningKoranViewModel : ObservableObject
    {
        /// <summary>Kode jenis surat di konfigurasi — deret nomor tersendiri (awalan 130).</summary>
        private const string KodeJenisRekeningKoran = "REKKOR";

        private readonly IUnitOfWork _unitOfWork;
        private readonly RekeningKoranGenerator _pdfGenerator;
        private readonly AppConfig _appConfig;
        private readonly ILogger<RekeningKoranViewModel> _logger;
        private readonly Func<string, string, PdfPreviewViewModel> _previewFactory;
        private readonly NavigationService _navigation;
        private readonly IMessageService _messageService;
        private readonly SuDesApp.Services.PenjagaLisensi? _penjaga;

        private string _nomorSurat = string.Empty;
        private string _namaKades = string.Empty;
        private string _jabatan = string.Empty;

        // Alamat kepala desa dipecah empat komponen — sama seperti kolom alamat di
        // menu input surat — lalu digabung saat dicetak.
        private string _alamatDusun = string.Empty;
        private string _alamatDesa = string.Empty;
        private string _alamatKecamatan = string.Empty;
        private string _alamatKabupaten = string.Empty;
        private string _alamatRt = string.Empty;
        private string _alamatRw = string.Empty;

        /// <summary>Data desa dari pengaturan; dipakai mengisi alamat &amp; kop surat.</summary>
        private DesaData _desaData = new();
        private string _namaRekening = string.Empty;
        private string _nomorRekening = string.Empty;
        private string _bank = string.Empty;
        private string _kcp = string.Empty;
        // Periode diketik sebagai teks dd-mm-yyyy (bukan pemilih tanggal);
        // dibaca ulang lewat TanggalIsian saat disimpan.
        private string _tanggalMulai = TanggalIsian.KeTeks(DateTime.Now);
        private string _tanggalSelesai = TanggalIsian.KeTeks(DateTime.Now);
        private bool _isBusy;

        /// <summary>ID surat di register; &gt; 0 berarti surat sudah tercatat (mode edit).</summary>
        private int _idSuratTercatat;

        /// <summary>Nomor sudah ditentukan (dimuat untuk edit / diubah pengguna) — jangan ditimpa lagi.</summary>
        private bool _nomorTerkunci;

        public event Action? RequestClose;

        public RekeningKoranViewModel(
            IUnitOfWork unitOfWork,
            RekeningKoranGenerator pdfGenerator,
            AppConfig appConfig,
            ILogger<RekeningKoranViewModel> logger,
            Func<string, string, PdfPreviewViewModel> previewFactory,
            NavigationService navigation,
            IMessageService messageService,
            SuDesApp.Services.PenjagaLisensi? penjaga = null)
        {
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _pdfGenerator = pdfGenerator ?? throw new ArgumentNullException(nameof(pdfGenerator));
            _appConfig = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _previewFactory = previewFactory ?? throw new ArgumentNullException(nameof(previewFactory));
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
            _penjaga = penjaga;

            SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
            PratinjauCommand = new AsyncRelayCommand(PratinjauAsync, () => !IsBusy);
            CancelCommand = new RelayCommand(Cancel);

            _ = InitializeAsync();
        }

        // ===== Properti ter-observasi =====
        public string NomorSurat
        {
            get => _nomorSurat;
            set
            {
                if (SetProperty(ref _nomorSurat, value))
                {
                    _nomorTerkunci = true;
                }
            }
        }

        /// <summary>
        /// Nomor surat pada form ini selalu dapat disunting: Rekening Koran hanya
        /// membuat permohonan baru (tidak punya mode edit seperti surat bawaan),
        /// jadi nomor yang disarankan register boleh diubah operator.
        /// </summary>
        public bool NomorSuratEnabled => true;
        public string NamaKades { get => _namaKades; set => SetProperty(ref _namaKades, value); }
        public string Jabatan { get => _jabatan; set => SetProperty(ref _jabatan, value); }
        public string AlamatDusun { get => _alamatDusun; set => SetProperty(ref _alamatDusun, value); }
        public string AlamatDesa { get => _alamatDesa; set => SetProperty(ref _alamatDesa, value); }
        public string AlamatKecamatan { get => _alamatKecamatan; set => SetProperty(ref _alamatKecamatan, value); }
        public string AlamatKabupaten { get => _alamatKabupaten; set => SetProperty(ref _alamatKabupaten, value); }
        public string AlamatRt { get => _alamatRt; set => SetProperty(ref _alamatRt, value); }
        public string AlamatRw { get => _alamatRw; set => SetProperty(ref _alamatRw, value); }

        /// <summary>
        /// Alamat kepala desa siap cetak — dibentuk dari empat komponen dengan
        /// pemformatan yang sama seperti surat lain (dipakai juga saat menyimpan ke register).
        /// </summary>
        public string AlamatKades => AlamatFormatter.Format(
            AlamatDusun, AlamatDesa, AlamatKecamatan, AlamatKabupaten, fallback: string.Empty,
            rt: AlamatRt, rw: AlamatRw);
        public string NamaRekening { get => _namaRekening; set => SetProperty(ref _namaRekening, value); }
        public string NomorRekening { get => _nomorRekening; set => SetProperty(ref _nomorRekening, value); }
        public string Bank { get => _bank; set => SetProperty(ref _bank, value); }
        public string KCP { get => _kcp; set => SetProperty(ref _kcp, value); }
        public string TanggalMulai
        {
            get => _tanggalMulai;
            set => SetProperty(ref _tanggalMulai, value ?? string.Empty);
        }

        public string TanggalSelesai
        {
            get => _tanggalSelesai;
            set => SetProperty(ref _tanggalSelesai, value ?? string.Empty);
        }

        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        public AsyncRelayCommand SaveCommand { get; }
        public AsyncRelayCommand PratinjauCommand { get; }
        public RelayCommand CancelCommand { get; }

        /// <summary>
        /// Tombol Pratinjau: buat PDF permohonan dari isian yang SEDANG ada di form
        /// lalu tampilkan di halaman pratinjau — tanpa mencatat ke register dan tanpa
        /// memakai nomor surat, jadi aman dilihat berulang sebelum benar-benar disimpan.
        /// Ini hanya pratinjau; Simpan tetap wajib untuk mencatat permohonan.
        ///
        /// Isian yang belum lengkap TIDAK menghalangi pratinjau — sama seperti form
        /// surat lain: selama PDF dibuat, <see cref="PratinjauIsian"/> mencetak kolom
        /// kosong sebagai titik-titik sehingga kerangka surat tetap terbaca. Inilah
        /// gunanya pratinjau: melihat bentuk surat sebelum mulai mengisi. Pemeriksaan
        /// kelengkapan hanya berlaku pada Simpan (lihat <see cref="SaveAsync"/>).
        /// </summary>
        private async Task PratinjauAsync()
        {
            if (IsBusy) return;

            IsBusy = true;
            try
            {
                var data = CreateData(PeriodePratinjau());
                _logger.LogInformation("Membuat pratinjau PDF Rekening Koran NomorSurat={NomorSurat}", data.NomorSurat);

                using var memoryStream = new MemoryStream();
                // Penanda pratinjau berlaku sampai PDF selesai dibuat: generator
                // melewati pemeriksaan kelengkapan dan mencetak titik-titik pada
                // kolom yang masih kosong.
                using (PratinjauIsian.Pakai())
                {
                    await _pdfGenerator.GeneratePdfAsync(memoryStream, data);
                }

                var pdfBytes = memoryStream.ToArray();
                if (pdfBytes.Length == 0)
                {
                    await _messageService.ShowErrorAsync("Gagal menghasilkan PDF atau PDF kosong.");
                    return;
                }

                string folder = _appConfig.TempPdfFolder;
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, $"Pratinjau_Rekening_Koran_{DateTime.Now:yyyyMMddHHmmss}.pdf");
                await File.WriteAllBytesAsync(path, pdfBytes);

                string judulNomor = string.IsNullOrWhiteSpace(data.NomorSurat)
                    ? "(nomor surat belum ada)"
                    : data.NomorSurat;
                var preview = _previewFactory(
                    $"Pratinjau Permohonan Rekening Koran — {judulNomor}", path);
                _navigation.Navigate(preview);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat pratinjau PDF Rekening Koran");
                await _messageService.ShowErrorAsync("Gagal membuat pratinjau Rekening Koran: " + ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task InitializeAsync()
        {
            try
            {
                var desaData = await _unitOfWork.DesaRepository.GetInfoDesaAsync();
                if (desaData == null)
                {
                    desaData = new DesaData
                    {
                        NamaDesa = string.Empty,
                        Kecamatan = string.Empty,
                        Kabupaten = string.Empty,
                        Alamat = string.Empty,
                        Kodepos = string.Empty,
                        KepalaDesa = string.Empty
                    };
                }

                // Simpan data desa lengkap: dipakai mengisi alamat kepala desa (desa,
                // kecamatan, kabupaten) sekaligus sebagai sumber kop surat.
                _desaData = desaData;

                // Nomor surat memakai deret sendiri dengan awalan 130 (130/xxx/Ds/tahun),
                // terpisah dari urutan SKD. Pada mode edit, nomor surat yang dibuka tidak
                // boleh ditimpa.
                if (!_nomorTerkunci)
                {
                    NomorSurat = await NomorSuratBerikutnyaAsync();
                }

                NamaKades = desaData.KepalaDesa?.ToUpperInvariant() ?? string.Empty;
                var namaDesa = desaData.NamaDesa ?? string.Empty;
                // Jabatan mengikuti lembaga yang dipilih di Pengaturan Surat:
                // “Kepala Desa X” pada desa, “Lurah X” pada kelurahan.
                Jabatan = KopSurat.KataPejabat(desaData) + " " + (namaDesa.Length > 0
                    ? char.ToUpper(namaDesa[0]) + namaDesa.Substring(1).ToLower()
                    : namaDesa);
                NamaRekening = "PEMERINTAH DESA " + namaDesa.ToUpperInvariant();

                // Alamat terisi otomatis dari pengaturan (Dusun/Jalan diketik pengguna).
                AlamatDesa = desaData.NamaDesa ?? string.Empty;
                AlamatKecamatan = desaData.Kecamatan ?? string.Empty;
                AlamatKabupaten = desaData.Kabupaten ?? string.Empty;

                NomorRekening = string.Empty;
                Bank = string.Empty;
                KCP = string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memuat data awal Rekening Koran");
            }
        }

        /// <summary>
        /// Nomor surat berikutnya dari register — jenis ini punya deret nomor sendiri
        /// (awalan 130), terpisah dari urutan SKD. Bila register tidak dapat dihubungi,
        /// dipakai nomor contoh agar form tetap bisa dibuka.
        /// </summary>
        private async Task<string> NomorSuratBerikutnyaAsync()
        {
            try
            {
                var nomor = await _unitOfWork.JenisSuratRepository.GenerateNomorSuratAsync(KodeJenisRekeningKoran);
                if (!string.IsNullOrWhiteSpace(nomor))
                {
                    return nomor;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal menghitung nomor surat rekening koran; memakai nomor contoh.");
            }

            return $"130/NOMOR/Ds/{DateTime.Now:yyyy}";
        }

        /// <summary>
        /// Buka surat yang sudah tercatat di register untuk diedit: isi form dari data
        /// surat + payload JSON-nya, nomor lama tetap dipakai, dan Simpan memperbarui
        /// surat yang sama (bukan membuat nomor baru).
        /// </summary>
        public async Task ConfigureForEditAsync(int idSurat)
        {
            _nomorTerkunci = true;

            try
            {
                var surat = await _unitOfWork.SuratRepository.GetByIdAsync(idSurat);
                if (surat == null)
                {
                    await _messageService.ShowWarningAsync("Surat tidak ditemukan.");
                    return;
                }

                var data = RekeningKoranData.FromJson(surat.AdditionalData);
                var periode = data?.PeriodeRekening ?? string.Empty;
                var (mulai, selesai) = PisahPeriode(periode);

                NomorSurat = surat.NomorSurat ?? string.Empty;
                NamaKades = data?.NamaPejabat ?? surat.NamaPejabatPenandatangan ?? string.Empty;
                Jabatan = data?.Jabatan ?? string.Empty;
                IsiAlamatDariData(data);
                NamaRekening = data?.NamaPemegangRekening ?? string.Empty;
                NomorRekening = data?.NomorRekening ?? string.Empty;
                Bank = data?.Bank ?? string.Empty;
                KCP = data?.KCP ?? string.Empty;
                TanggalMulai = mulai;
                TanggalSelesai = selesai;
                _idSuratTercatat = surat.ID_Surat;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memuat permohonan rekening koran #{Id} untuk diedit", idSurat);
                await _messageService.ShowErrorAsync($"Gagal memuat surat: {ex.Message}");
            }
        }

        /// <summary>
        /// Pecah teks periode "dd-MM-yyyy s/d dd-MM-yyyy" kembali ke dua teks tanggal
        /// isian; teks yang tidak terbaca diganti tanggal hari ini.
        /// </summary>
        private static (string Mulai, string Selesai) PisahPeriode(string periode)
        {
            var bagian = (periode ?? string.Empty).Split("s/d", StringSplitOptions.TrimEntries);
            if (bagian.Length == 2 &&
                TanggalIsian.Baca(bagian[0], "Periode Mulai", out DateTime? mulai, out _) &&
                TanggalIsian.Baca(bagian[1], "Periode Akhir", out DateTime? selesai, out _) &&
                mulai.HasValue && selesai.HasValue)
            {
                return (TanggalIsian.KeTeks(mulai), TanggalIsian.KeTeks(selesai));
            }

            return (TanggalIsian.KeTeks(DateTime.Now), TanggalIsian.KeTeks(DateTime.Now));
        }

        /// <summary>
        /// Baca periode rekening dari isian teks. Kedua tanggal wajib diisi dan
        /// urut; pesan galatnya sama bentuknya dengan form lain.
        /// </summary>
        private bool BacaPeriode(out DateTime mulai, out DateTime selesai, out string error)
        {
            mulai = default;
            selesai = default;

            if (!TanggalIsian.BacaWajib(TanggalMulai, "Periode Mulai", out mulai, out error)) return false;
            if (!TanggalIsian.BacaWajib(TanggalSelesai, "Periode Akhir", out selesai, out error)) return false;

            if (selesai < mulai)
            {
                error = "Tanggal akhir tidak boleh lebih awal dari tanggal mulai.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        /// <summary>
        /// Periode rekening untuk PRATINJAU: dibaca selonggar mungkin. Tanggal yang
        /// belum terbaca dipakai apa adanya sebagai teks supaya pratinjau tetap
        /// menampilkan apa yang sedang diketik, dan hanya bila keduanya kosong
        /// dipakai tanggal hari ini.
        /// </summary>
        private string PeriodePratinjau()
        {
            bool mulaiTerbaca = TanggalIsian.Baca(TanggalMulai, "Periode Mulai", out DateTime? mulai, out _);
            bool selesaiTerbaca = TanggalIsian.Baca(TanggalSelesai, "Periode Akhir", out DateTime? selesai, out _);

            if (mulaiTerbaca && selesaiTerbaca && mulai.HasValue && selesai.HasValue)
            {
                return $"{TanggalIsian.KeTeks(mulai.Value)} s/d {TanggalIsian.KeTeks(selesai.Value)}";
            }

            string teksMulai = (TanggalMulai ?? string.Empty).Trim();
            string teksSelesai = (TanggalSelesai ?? string.Empty).Trim();
            if (teksMulai.Length == 0 && teksSelesai.Length == 0)
            {
                string hariIni = TanggalIsian.KeTeks(DateTime.Now);
                return $"{hariIni} s/d {hariIni}";
            }

            return $"{teksMulai} s/d {teksSelesai}";
        }

        private bool ValidateInputs(out DateTime periodeMulai, out DateTime periodeSelesai, out string error)
        {
            periodeMulai = default;
            periodeSelesai = default;

            // Sebutkan kolom mana yang kurang: pesan "semua kolom wajib diisi"
            // menyulitkan pengguna mencari kolom yang terlewat.
            var kurang = new List<string>();
            if (string.IsNullOrWhiteSpace(NomorSurat)) kurang.Add("Nomor Surat");
            if (string.IsNullOrWhiteSpace(NamaKades)) kurang.Add("Nama Kepala Desa");
            if (string.IsNullOrWhiteSpace(Jabatan)) kurang.Add("Jabatan");
            if (string.IsNullOrWhiteSpace(AlamatDusun)) kurang.Add("Dusun/Jalan");
            if (string.IsNullOrWhiteSpace(AlamatDesa)) kurang.Add("Desa");
            if (string.IsNullOrWhiteSpace(AlamatKecamatan)) kurang.Add("Kecamatan");
            if (string.IsNullOrWhiteSpace(AlamatKabupaten)) kurang.Add("Kabupaten");
            if (string.IsNullOrWhiteSpace(NamaRekening)) kurang.Add("Nama Pemegang Rekening");
            if (string.IsNullOrWhiteSpace(NomorRekening)) kurang.Add("Nomor Rekening");
            if (string.IsNullOrWhiteSpace(Bank)) kurang.Add("Bank");
            if (string.IsNullOrWhiteSpace(KCP)) kurang.Add("KCP");

            if (kurang.Count > 0)
            {
                error = "Kolom berikut harus diisi: " + string.Join(", ", kurang) + ".";
                return false;
            }

            // Periode wajib: tanggalnya dipakai pada surat dan dicatat di register.
            if (!BacaPeriode(out periodeMulai, out periodeSelesai, out error)) return false;

            error = string.Empty;
            return true;
        }

        /// <summary>
        /// Isi empat komponen alamat dari data surat. Surat baru menyimpan komponennya;
        /// surat lama hanya menyimpan satu teks alamat, dan teks itu dikembalikan apa
        /// adanya sebagai komponen Dusun/Jalan.
        /// </summary>
        private void IsiAlamatDariData(RekeningKoranData? data)
        {
            AlamatDusun = data?.AlamatDusun ?? string.Empty;
            AlamatDesa = data?.AlamatDesa ?? _desaData.NamaDesa ?? string.Empty;
            AlamatKecamatan = data?.AlamatKecamatan ?? _desaData.Kecamatan ?? string.Empty;
            AlamatKabupaten = data?.AlamatKabupaten ?? _desaData.Kabupaten ?? string.Empty;
            AlamatRt = data?.AlamatRt ?? string.Empty;
            AlamatRw = data?.AlamatRw ?? string.Empty;

            bool adaKomponen = !string.IsNullOrWhiteSpace(data?.AlamatDusun) ||
                               !string.IsNullOrWhiteSpace(data?.AlamatDesa) ||
                               !string.IsNullOrWhiteSpace(data?.AlamatKecamatan) ||
                               !string.IsNullOrWhiteSpace(data?.AlamatKabupaten);

            if (!adaKomponen && !string.IsNullOrWhiteSpace(data?.AlamatPejabat))
            {
                AlamatDusun = data!.AlamatPejabat!;
            }
        }

        private RekeningKoranData CreateData(string periodeRekening)
        {
            return new RekeningKoranData
            {
                NomorSurat = NomorSurat,
                Perihal = "Permohonan Print Out Rekening Koran",
                TanggalSurat = DateTime.Now,
                NamaPejabat = NamaKades,
                Jabatan = Jabatan,

                // Empat komponen alamat + salinan gabungannya (agar surat lama tetap terbaca).
                AlamatDusun = AlamatDusun,
                AlamatDesa = AlamatDesa,
                AlamatKecamatan = AlamatKecamatan,
                AlamatKabupaten = AlamatKabupaten,
                AlamatRt = AlamatRt,
                AlamatRw = AlamatRw,
                AlamatPejabat = AlamatKades,

                NamaPemegangRekening = NamaRekening,
                NomorRekening = NomorRekening,
                PeriodeRekening = periodeRekening,
                Bank = Bank,
                KCP = KCP,

                // Kop memakai data desa dari pengaturan supaya lengkap (kecamatan,
                // kabupaten, alamat kantor, kodepos) — bukan susunan dari jabatan.
                Desa = new DesaData
                {
                    NamaDesa = !string.IsNullOrWhiteSpace(_desaData.NamaDesa)
                        ? _desaData.NamaDesa
                        : (Jabatan.StartsWith("Kepala Desa ", StringComparison.OrdinalIgnoreCase)
                            ? Jabatan.Substring("Kepala Desa ".Length)
                            : string.Empty),
                    Kecamatan = _desaData.Kecamatan ?? string.Empty,
                    Kabupaten = _desaData.Kabupaten ?? string.Empty,
                    // Jenis wilayah ikut ke kop: tanpa ini kop rekening koran selalu
                    // tercetak “PEMERINTAH KABUPATEN …” walau desanya di wilayah kota.
                    JenisKabupaten = _desaData.JenisKabupaten,
                    Alamat = _desaData.Alamat ?? string.Empty,
                    Kodepos = _desaData.Kodepos ?? string.Empty,
                    // Email desa (opsional) ikut ke kop; surat ini tidak punya kolomnya sendiri.
                    Email = _desaData.Email,
                    KepalaDesa = NamaKades,
                    SekretarisDesa = _desaData.SekretarisDesa,
                    NamaCamat = _desaData.NamaCamat,
                    NipCamat = _desaData.NipCamat,
                    GolCamat = _desaData.GolCamat
                }
            };
        }

        private async Task SaveAsync()
        {
            if (IsBusy) return;

            if (!ValidateInputs(out DateTime periodeMulai, out DateTime periodeSelesai, out var error))
            {
                await _messageService.ShowWarningAsync(error);
                return;
            }

            IsBusy = true;
            try
            {
                var data = CreateData(
                    $"{TanggalIsian.KeTeks(periodeMulai)} s/d {TanggalIsian.KeTeks(periodeSelesai)}");
                _logger.LogInformation("Membuat PDF Rekening Koran NomorSurat={NomorSurat}", data.NomorSurat);

                using var memoryStream = new MemoryStream();
                await _pdfGenerator.GeneratePdfAsync(memoryStream, data);
                var pdfBytes = memoryStream.ToArray();

                if (pdfBytes.Length == 0)
                {
                    await _messageService.ShowErrorAsync("Gagal menghasilkan PDF atau PDF kosong.");
                    return;
                }

                var outputFolder = _appConfig.PdfOutputPath;
                Directory.CreateDirectory(outputFolder);
                var outputPath = Path.Combine(
                    outputFolder, $"Permohonan_Rekening_Koran_{DateTime.Now:yyyyMMddHHmmss}.pdf");
                await File.WriteAllBytesAsync(outputPath, pdfBytes);

                // Catat surat ke register supaya nomornya benar-benar terpakai: nomor
                // sesudah ini (SKD atau rekening koran berikutnya) melanjut ke nomor
                // berikutnya, dan surat bisa dibuka/dicetak ulang dari Register Surat.
                if (!await CatatKeRegisterAsync(data))
                {
                    await _messageService.ShowWarningAsync(
                        "PDF sudah dibuat, tetapi surat belum tercatat di register sehingga " +
                        "nomornya belum terpakai. Coba simpan sekali lagi.");
                }

                var preview = _previewFactory(
                    $"Permohonan Rekening Koran — {data.NomorSurat}", outputPath);
                _navigation.Navigate(preview);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat PDF Rekening Koran");
                await _messageService.ShowErrorAsync("Gagal membuat PDF Rekening Koran: " + ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Simpan surat ke buku register (jenis REKENING_KORAN). Saat form dibuka untuk
        /// mengedit surat yang sudah ada, baris yang sama diperbarui — tidak menambah
        /// nomor baru. Pemohon dicatat sebagai pemerintah desa (warga dummy instansi),
        /// karena surat ini tidak mewakili perorangan.
        /// </summary>
        private async Task<bool> CatatKeRegisterAsync(RekeningKoranData data)
        {
            try
            {
                var surat = new SuratData
                {
                    ID_Surat = _idSuratTercatat,
                    NamaJenis = SuratConstants.REKENING_KORAN,
                    NomorSurat = data.NomorSurat,
                    TanggalSurat = data.TanggalSurat,
                    Status = "Active",
                    Keperluan = data.Perihal,
                    Keterangan = $"Print out rekening koran {data.Bank} {data.KCP} — periode {data.PeriodeRekening}",
                    AdditionalData = data.ToJson(),
                    Warga = new WargaData
                    {
                        NIK = SuratConstants.NIK_INSTANSI,
                        Nama = data.NamaPemegangRekening,
                        AlamatLengkap = data.AlamatPejabatLengkap,
                        IsForInstansi = true
                    }
                };

                if (_idSuratTercatat > 0)
                {
                    if (!await _unitOfWork.SuratRepository.UpdateAsync(surat))
                    {
                        _logger.LogWarning("Update surat rekening koran #{Id} tidak mengubah baris", _idSuratTercatat);
                        return false;
                    }
                }
                else
                {
                    // Surat BARU — mode baca-saja menolak (lapis kedua; navigasi
                    // form sudah dicegah di MainWindowViewModel).
                    if (_penjaga is { BacaSaja: true })
                    {
                        _logger.LogWarning("Pencatatan rekening koran ditolak: mode baca-saja");
                        await _messageService.ShowInfoAsync(SuDesApp.Services.PenjagaLisensi.PesanAksesDikunci);
                        return false;
                    }
                    _idSuratTercatat = await _unitOfWork.SuratRepository.AddSuratAsync(surat);
                    _logger.LogInformation("Permohonan rekening koran tercatat di register: #{Id} {Nomor}",
                        _idSuratTercatat, surat.NomorSurat);
                }

                return _idSuratTercatat > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mencatat permohonan rekening koran ke register");
                return false;
            }
        }

        private void Cancel() => RequestClose?.Invoke();
    }
}
