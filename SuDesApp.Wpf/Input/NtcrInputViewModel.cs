using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;

namespace SuDesApp.Wpf.Input
{
    /// <summary>
    /// ViewModel input formulir NTCR: blanko N1–N6 sesuai Keputusan Dirjen Bimas Islam
    /// No. 473 Tahun 2020 ditambah N8 (surat keterangan numpang nikah buatan desa).
    /// Satu VM untuk seluruh jenis; NamaJenis di-set oleh InputControlFactory dan
    /// menentukan kolom mana yang tampil.
    /// Data calon suami (pemohon) diisi di blok dasar (WargaData), sedangkan calon
    /// istri, orang tua, dan kolom khusus tiap blanko disimpan di model NtcrData.
    /// </summary>
    public class NtcrInputViewModel : BaseSuratInputViewModel
    {
        private string _namaJenis = SuratConstants.NTCR_N1;

        // ===== Calon istri =====
        private string _nikIstri = string.Empty;
        private string _namaIstri = string.Empty;
        private string _tempatLahirIstri = string.Empty;
        private string _tanggalLahirIstri = string.Empty;
        private string _kewarganegaraanIstri = "WNI";
        private string _agamaIstri = string.Empty;
        private string _pekerjaanIstri = string.Empty;
        private string _alamatIstri = string.Empty;
        private string _statusPerkawinanIstri = string.Empty;

        // ===== Kolom khusus per blanko =====
        private string _pihakDiterangkanN1 = "Suami";
        private string _tujuanKua = string.Empty;
        private string _hariTanggalJamAkad = string.Empty;
        private string _tempatAkad = string.Empty;
        private string _tanggalPenetapanIsbat = string.Empty;
        private string _pengadilanAgama = string.Empty;
        private string _tanggalDiterima = string.Empty;
        private string _pihakAnakIzinOrtu = "Suami";
        private string _pihakMeninggal = "Suami";
        private string _tanggalMeninggal = string.Empty;
        private string _tempatMeninggal = string.Empty;

        // ===== Surat Numpang Nikah (N8) =====
        private string _desaNumpang = string.Empty;
        private string _kecamatanNumpang = string.Empty;
        private string _kabupatenNumpang = string.Empty;
        private string _kecamatanIstri = string.Empty;
        private string _kabupatenIstri = string.Empty;

        // ===== Warisan skema lama (tidak lagi tampil di form, tetap dibawa saat menyimpan) =====
        private string _keteranganTemuan = string.Empty;
        private string _tujuanSurat = string.Empty;

        /// <summary>Jenis blanko yang terakhir dicoba isi nomornya otomatis (anti dobel generate).</summary>
        private string? _jenisNomorTerakhirDicoba;

        /// <summary>
        /// Saat NamaJenis berubah (mis. membuka blanko berbeda pada VM yang sama),
        /// isi ulang nomor surat otomatis bila kotaknya masih kosong.
        /// </summary>
        public string NamaJenis
        {
            get => _namaJenis;
            set
            {
                if (SetProperty(ref _namaJenis, value))
                {
                    OnPropertyChanged(nameof(IsN1));
                    OnPropertyChanged(nameof(IsN2));
                    OnPropertyChanged(nameof(IsN3));
                    OnPropertyChanged(nameof(IsN4));
                    OnPropertyChanged(nameof(IsN5));
                    OnPropertyChanged(nameof(IsN6));
                    OnPropertyChanged(nameof(IsN8));
                    OnPropertyChanged(nameof(ButuhDataOrangTua));
                    OnPropertyChanged(nameof(ButuhPejabatDesa));
                    _ = IsiNomorSuratOtomatisAsync();
                }
            }
        }

        // Virtual: alur paket (NtcrPaketViewModel) menimpanya dengan daftar blanko
        // yang dipilih pengguna, sehingga seluruh blok form dan validasinya mengikuti
        // blanko yang akan dicetak sekaligus.
        public virtual bool IsN1 => JenisSama(SuratConstants.NTCR_N1);
        public virtual bool IsN2 => JenisSama(SuratConstants.NTCR_N2);
        public virtual bool IsN3 => JenisSama(SuratConstants.NTCR_N3);
        public virtual bool IsN4 => JenisSama(SuratConstants.NTCR_N4);
        public virtual bool IsN5 => JenisSama(SuratConstants.NTCR_N5);
        public virtual bool IsN6 => JenisSama(SuratConstants.NTCR_N6);

        /// <summary>N8 — Surat Keterangan Numpang Nikah (numpang kawin), diterbitkan desa.</summary>
        public virtual bool IsN8 => JenisSama(SuratConstants.NTCR_N8);

        /// <summary>N1 &amp; N5 memuat identitas lengkap orang tua/wali.</summary>
        public bool ButuhDataOrangTua => IsN1 || IsN5;

        /// <summary>Blok pejabat desa terpakai pada surat yang ditandatangani desa (N1, N6, N8).</summary>
        public bool ButuhPejabatDesa => IsN1 || IsN6 || IsN8;

        /// <summary>
        /// N8 hanya mencetak identitas pokok calon istri (nama, tempat/tanggal lahir,
        /// alamat), sehingga NIK/agama/pekerjaan/status kawin tidak diwajibkan.
        /// </summary>
        public bool ButuhIdentitasIstriLengkap => !IsN8;

        /// <summary>N2 &amp; N3 sama-sama surat permohonan kepada KUA/PPN LN.</summary>
        public bool IsSuratPermohonanKua => IsN2 || IsN3;

        private bool JenisSama(string namaJenis) =>
            string.Equals(NamaJenis, namaJenis, StringComparison.OrdinalIgnoreCase);

        // ===== Calon istri =====
        public string NikIstri { get => _nikIstri; set => SetProperty(ref _nikIstri, value); }
        public string NamaIstri { get => _namaIstri; set => SetProperty(ref _namaIstri, value); }
        public string TempatLahirIstri { get => _tempatLahirIstri; set => SetProperty(ref _tempatLahirIstri, value); }
        public string TanggalLahirIstri { get => _tanggalLahirIstri; set => SetProperty(ref _tanggalLahirIstri, value); }
        public string KewarganegaraanIstri { get => _kewarganegaraanIstri; set => SetProperty(ref _kewarganegaraanIstri, value); }
        public string AgamaIstri { get => _agamaIstri; set => SetProperty(ref _agamaIstri, value); }
        public string PekerjaanIstri { get => _pekerjaanIstri; set => SetProperty(ref _pekerjaanIstri, value); }
        public string AlamatIstri { get => _alamatIstri; set => SetProperty(ref _alamatIstri, value); }
        public string StatusPerkawinanIstri { get => _statusPerkawinanIstri; set => SetProperty(ref _statusPerkawinanIstri, value); }

        // ===== Kolom khusus per blanko =====
        /// <summary>N1: pihak yang diterangkan — "Suami" (pemohon) atau "Istri".</summary>
        public string PihakDiterangkanN1 { get => _pihakDiterangkanN1; set => SetProperty(ref _pihakDiterangkanN1, value); }

        /// <summary>N2/N3: KUA/PPN LN tujuan surat.</summary>
        public string TujuanKua { get => _tujuanKua; set => SetProperty(ref _tujuanKua, value); }

        /// <summary>N2: hari, tanggal, dan jam rencana akad nikah.</summary>
        public string HariTanggalJamAkad { get => _hariTanggalJamAkad; set => SetProperty(ref _hariTanggalJamAkad, value); }

        /// <summary>N2: tempat akad nikah.</summary>
        public string TempatAkad { get => _tempatAkad; set => SetProperty(ref _tempatAkad, value); }

        /// <summary>N3: tanggal penetapan isbat Pengadilan Agama.</summary>
        public string TanggalPenetapanIsbat { get => _tanggalPenetapanIsbat; set => SetProperty(ref _tanggalPenetapanIsbat, value); }

        /// <summary>N3: Pengadilan Agama yang menetapkan.</summary>
        public string PengadilanAgama { get => _pengadilanAgama; set => SetProperty(ref _pengadilanAgama, value); }

        /// <summary>N2/N3: tanggal berkas diterima KUA.</summary>
        public string TanggalDiterima { get => _tanggalDiterima; set => SetProperty(ref _tanggalDiterima, value); }

        /// <summary>N5: anak yang diberi izin — "Suami" atau "Istri".</summary>
        public string PihakAnakIzinOrtu { get => _pihakAnakIzinOrtu; set => SetProperty(ref _pihakAnakIzinOrtu, value); }

        /// <summary>N6: pihak yang meninggal — "Suami" atau "Istri".</summary>
        public string PihakMeninggal { get => _pihakMeninggal; set => SetProperty(ref _pihakMeninggal, value); }

        /// <summary>N6: tanggal meninggal dunia.</summary>
        public string TanggalMeninggal { get => _tanggalMeninggal; set => SetProperty(ref _tanggalMeninggal, value); }

        /// <summary>N6: tempat meninggal dunia.</summary>
        public string TempatMeninggal { get => _tempatMeninggal; set => SetProperty(ref _tempatMeninggal, value); }

        // ===== Surat Numpang Nikah (N8) =====

        /// <summary>N8: desa/kelurahan tempat akan numpang nikah.</summary>
        public string DesaNumpang { get => _desaNumpang; set => SetProperty(ref _desaNumpang, value); }

        /// <summary>N8: kecamatan tempat numpang nikah.</summary>
        public string KecamatanNumpang { get => _kecamatanNumpang; set => SetProperty(ref _kecamatanNumpang, value); }

        /// <summary>N8: kabupaten/kota tempat numpang nikah.</summary>
        public string KabupatenNumpang { get => _kabupatenNumpang; set => SetProperty(ref _kabupatenNumpang, value); }

        /// <summary>N8: kecamatan tempat tinggal calon istri.</summary>
        public string KecamatanIstri { get => _kecamatanIstri; set => SetProperty(ref _kecamatanIstri, value); }

        /// <summary>N8: kabupaten/kota tempat tinggal calon istri.</summary>
        public string KabupatenIstri { get => _kabupatenIstri; set => SetProperty(ref _kabupatenIstri, value); }

        // ===== Warisan skema lama (tidak lagi tampil di form, dibawa saat menyimpan) =====
        public string KeteranganTemuan { get => _keteranganTemuan; set => SetProperty(ref _keteranganTemuan, value); }
        public string TujuanSurat { get => _tujuanSurat; set => SetProperty(ref _tujuanSurat, value); }

        // ===== Data orang tua/wali (di-bind lewat DataTemplate di NtcrInputView) =====
        public NtcrOrangTua AyahCalonSuami { get; } = new NtcrOrangTua();
        public NtcrOrangTua IbuCalonSuami { get; } = new NtcrOrangTua();
        public NtcrOrangTua AyahCalonIstri { get; } = new NtcrOrangTua();
        public NtcrOrangTua IbuCalonIstri { get; } = new NtcrOrangTua();

        // ===== Daftar pilihan =====
        public IReadOnlyList<string> StatusPerkawinanIstriOptions => ValidStatusPerkawinanOptions;
        public IReadOnlyList<string> PihakPilihanOptions { get; } = new[] { "Suami", "Istri" };
        public IReadOnlyList<string> PejabatPenandatanganOptions { get; } = new[] { "Kepala Desa", "Sekretaris Desa" };

        /// <summary>Pejabat penandatangan (sinkron dengan pilihan Kepala/Sekretaris Desa).</summary>
        public string SelectedPejabat
        {
            get => IsSekdesSelected ? "Sekretaris Desa" : "Kepala Desa";
            set
            {
                if (string.Equals(value, "Sekretaris Desa", StringComparison.OrdinalIgnoreCase))
                    IsSekdesSelected = true;
                else
                    IsKadesSelected = true;
                OnPropertyChanged(nameof(SelectedPejabat));
            }
        }

        public NtcrInputViewModel(
            ILogger<NtcrInputViewModel> logger,
            AppConfig appConfig,
            IUnitOfWork unitOfWork,
            IMessageService messageService)
            : base(logger, appConfig, unitOfWork, messageService)
        {
        }

        public override async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (!IsKadesSelected && !IsSekdesSelected) IsKadesSelected = true;
            await InitializeSpecificAsync();
            await IsiNomorSuratOtomatisAsync(cancellationToken);
        }

        /// <summary>
        /// Isi kotak "Nomor Surat" otomatis bila masih kosong — nomor berikutnya
        /// menurut penomoran jenis surat di register untuk blanko ini (N1..N8).
        /// Alur paket (NtcrPaketViewModel) menimpanya dengan pengisian per blanko
        /// sehingga tidak terjadi nomor ganda; pada mode edit nomor sudah terisi.
        /// </summary>
        protected virtual async Task IsiNomorSuratOtomatisAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(NomorSurat) || !NomorSuratEnabled)
                    return;

                var namaJenis = NamaJenis?.Trim().ToUpperInvariant();
                if (string.IsNullOrEmpty(namaJenis) ||
                    string.Equals(_jenisNomorTerakhirDicoba, namaJenis, StringComparison.Ordinal))
                {
                    return; // percobaan untuk jenis ini sudah pernah dilakukan
                }

                _jenisNomorTerakhirDicoba = namaJenis;

                // GetJenisSuratByNamaAsync melempar bila jenis belum terdaftar di
                // database; nomor tetap dibuat sistem saat menyimpan (InsertAsync).
                var jenis = await _unitOfWork.JenisSuratRepository.GetJenisSuratByNamaAsync(namaJenis);
                var nomor = await _unitOfWork.JenisSuratRepository.GenerateNomorSuratAsync(jenis.KodeJenis!);
                if (!string.IsNullOrWhiteSpace(nomor) && string.IsNullOrWhiteSpace(NomorSurat))
                {
                    NomorSurat = nomor;
                }
            }
            catch (Exception ex)
            {
                // Nomor tetap dibuat sistem saat menyimpan (SuratRepository.InsertAsync),
                // jadi kegagalan di sini tidak perlu menggagalkan pembukaan form.
                _logger.LogWarning(ex, "Gagal mengisi nomor surat otomatis untuk {NamaJenis}", NamaJenis);
            }
        }

        protected override async Task InitializeSpecificAsync()
        {
            if (string.IsNullOrWhiteSpace(Kewarganegaraan)) Kewarganegaraan = "WNI";
            if (string.IsNullOrWhiteSpace(KewarganegaraanIstri)) KewarganegaraanIstri = "WNI";

            // KUA tujuan baku mengikuti kecamatan desa (bisa diubah pengguna).
            if (string.IsNullOrWhiteSpace(TujuanKua))
            {
                await InitializeAlamatAsync();
                if (!string.IsNullOrWhiteSpace(Kecamatan))
                {
                    TujuanKua = $"Kecamatan {Kecamatan}";
                }
            }
        }

        public override async Task FillDataAsync(SuratData? suratData)
        {
            if (suratData == null)
            {
                ClearControls();
                await InitializeAlamatAsync();
                return;
            }

            await base.FillDataAsync(suratData);
        }

        protected override void ClearControls()
        {
            base.ClearControls();
            NikIstri = string.Empty;
            NamaIstri = string.Empty;
            TempatLahirIstri = string.Empty;
            TanggalLahirIstri = string.Empty;
            KewarganegaraanIstri = "WNI";
            AgamaIstri = string.Empty;
            PekerjaanIstri = string.Empty;
            AlamatIstri = string.Empty;
            StatusPerkawinanIstri = string.Empty;

            PihakDiterangkanN1 = "Suami";
            TujuanKua = string.Empty;
            HariTanggalJamAkad = string.Empty;
            TempatAkad = string.Empty;
            TanggalPenetapanIsbat = string.Empty;
            PengadilanAgama = string.Empty;
            TanggalDiterima = string.Empty;
            PihakAnakIzinOrtu = "Suami";
            PihakMeninggal = "Suami";
            TanggalMeninggal = string.Empty;
            TempatMeninggal = string.Empty;
            DesaNumpang = string.Empty;
            KecamatanNumpang = string.Empty;
            KabupatenNumpang = string.Empty;
            KecamatanIstri = string.Empty;
            KabupatenIstri = string.Empty;
            KeteranganTemuan = string.Empty;
            TujuanSurat = string.Empty;
            _jenisNomorTerakhirDicoba = null;

            foreach (var orangTua in SemuaOrangTua())
            {
                orangTua.CopyFrom(new NtcrOrangTua());
            }
        }

        protected override Task FillSpecificDataAsync(SuratData? suratData)
        {
            if (suratData?.Ntcr == null) return Task.CompletedTask;

            var ntcr = suratData.Ntcr;
            NikIstri = ntcr.NikIstri ?? string.Empty;
            NamaIstri = ntcr.NamaIstri ?? string.Empty;
            TempatLahirIstri = ntcr.TempatLahirIstri ?? string.Empty;
            TanggalLahirIstri = ntcr.TanggalLahirIstri ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(ntcr.KewarganegaraanIstri)) KewarganegaraanIstri = ntcr.KewarganegaraanIstri;
            AgamaIstri = ntcr.AgamaIstri ?? string.Empty;
            PekerjaanIstri = ntcr.PekerjaanIstri ?? string.Empty;
            AlamatIstri = ntcr.AlamatIstri ?? string.Empty;
            StatusPerkawinanIstri = ntcr.StatusPerkawinanIstri ?? string.Empty;

            PihakDiterangkanN1 = string.IsNullOrWhiteSpace(ntcr.PihakDiterangkanN1) ? "Suami" : ntcr.PihakDiterangkanN1;
            TujuanKua = ntcr.TujuanKua ?? string.Empty;
            HariTanggalJamAkad = ntcr.HariTanggalJamAkad ?? string.Empty;
            TempatAkad = ntcr.TempatAkad ?? string.Empty;
            TanggalPenetapanIsbat = ntcr.TanggalPenetapanIsbat ?? string.Empty;
            PengadilanAgama = ntcr.PengadilanAgama ?? string.Empty;
            TanggalDiterima = ntcr.TanggalDiterima ?? string.Empty;
            PihakAnakIzinOrtu = string.IsNullOrWhiteSpace(ntcr.PihakAnakIzinOrtu) ? "Suami" : ntcr.PihakAnakIzinOrtu;
            PihakMeninggal = string.IsNullOrWhiteSpace(ntcr.PihakMeninggal) ? "Suami" : ntcr.PihakMeninggal;
            TanggalMeninggal = ntcr.TanggalMeninggal ?? string.Empty;
            TempatMeninggal = ntcr.TempatMeninggal ?? string.Empty;
            DesaNumpang = ntcr.DesaNumpang ?? string.Empty;
            KecamatanNumpang = ntcr.KecamatanNumpang ?? string.Empty;
            KabupatenNumpang = ntcr.KabupatenNumpang ?? string.Empty;
            KecamatanIstri = ntcr.KecamatanIstri ?? string.Empty;
            KabupatenIstri = ntcr.KabupatenIstri ?? string.Empty;
            KeteranganTemuan = ntcr.KeteranganTemuan ?? string.Empty;
            TujuanSurat = ntcr.TujuanSurat ?? string.Empty;

            AyahCalonSuami.CopyFrom(ntcr.AyahCalonSuami!);
            IbuCalonSuami.CopyFrom(ntcr.IbuCalonSuami!);
            AyahCalonIstri.CopyFrom(ntcr.AyahCalonIstri!);
            IbuCalonIstri.CopyFrom(ntcr.IbuCalonIstri!);

            if (string.Equals(suratData.PejabatPenandatangan, "Sekretaris Desa", StringComparison.OrdinalIgnoreCase))
            {
                IsSekdesSelected = true;
            }
            else
            {
                IsKadesSelected = true;
            }

            return Task.CompletedTask;
        }

        protected override async Task CollectSpecificDataAsync(SuratData? suratData)
        {
            if (suratData == null) return;

            suratData.NamaJenis = NamaJenis.Trim();
            suratData.Keperluan = NtcrKatalog.Keperluan(NamaJenis);
            suratData.PejabatPenandatangan = IsKadesSelected ? "Kepala Desa" : "Sekretaris Desa";

            var ntcr = suratData.Ntcr ??= new NtcrData();
            ntcr.NikIstri = NikIstri.Trim();
            ntcr.NamaIstri = NamaIstri.Trim();
            ntcr.TempatLahirIstri = TempatLahirIstri.Trim();
            ntcr.TanggalLahirIstri = ParseTanggalLahirToDbFormat(TanggalLahirIstri);
            ntcr.KewarganegaraanIstri = string.IsNullOrWhiteSpace(KewarganegaraanIstri) ? "WNI" : KewarganegaraanIstri.Trim();
            ntcr.AgamaIstri = AgamaIstri.Trim();
            ntcr.PekerjaanIstri = PekerjaanIstri.Trim();
            ntcr.AlamatIstri = AlamatIstri.Trim();
            ntcr.StatusPerkawinanIstri = StatusPerkawinanIstri.Trim();

            ntcr.PihakDiterangkanN1 = PihakDiterangkanN1;
            ntcr.TujuanKua = TujuanKua.Trim();
            ntcr.HariTanggalJamAkad = HariTanggalJamAkad.Trim();
            ntcr.TempatAkad = TempatAkad.Trim();
            ntcr.TanggalPenetapanIsbat = TanggalPenetapanIsbat.Trim();
            ntcr.PengadilanAgama = PengadilanAgama.Trim();
            ntcr.TanggalDiterima = TanggalDiterima.Trim();
            ntcr.PihakAnakIzinOrtu = PihakAnakIzinOrtu;
            ntcr.PihakMeninggal = PihakMeninggal;
            ntcr.TanggalMeninggal = TanggalMeninggal.Trim();
            ntcr.TempatMeninggal = TempatMeninggal.Trim();
            ntcr.DesaNumpang = DesaNumpang.Trim();
            ntcr.KecamatanNumpang = KecamatanNumpang.Trim();
            ntcr.KabupatenNumpang = KabupatenNumpang.Trim();
            ntcr.KecamatanIstri = KecamatanIstri.Trim();
            ntcr.KabupatenIstri = KabupatenIstri.Trim();

            // Warisan skema lama — dibawa apa adanya agar surat lama tidak kehilangan data.
            ntcr.KeteranganTemuan = KeteranganTemuan.Trim();
            ntcr.TujuanSurat = TujuanSurat.Trim();

            ntcr.AyahCalonSuami.CopyFrom(AyahCalonSuami);
            ntcr.IbuCalonSuami.CopyFrom(IbuCalonSuami);
            ntcr.AyahCalonIstri.CopyFrom(AyahCalonIstri);
            ntcr.IbuCalonIstri.CopyFrom(IbuCalonIstri);

            if (suratData.Warga != null)
            {
                suratData.Warga.Dusun = Dusun.Trim();
                suratData.Warga.Desa = Desa.Trim();
                suratData.Warga.Kecamatan = Kecamatan.Trim();
                suratData.Warga.Kabupaten = Kabupaten.Trim();
                suratData.Warga.NamaJenis = NamaJenis.Trim();
                suratData.Warga.IsForInstansi = false;
                suratData.Warga.IsForKematian = false;
            }

            await Task.CompletedTask;
        }

        protected override void ValidateSpecificFields(List<string> errors)
        {
            // KUA tujuan baku mengikuti kecamatan desa; diisi di sini juga supaya
            // blanko N2/N3 tetap lengkap walau form dibuka tanpa memuat alamat.
            if (string.IsNullOrWhiteSpace(TujuanKua) && !string.IsNullOrWhiteSpace(Kecamatan))
            {
                TujuanKua = $"Kecamatan {Kecamatan.Trim()}";
            }

            // NIK calon istri tidak tercetak pada surat numpang nikah (N8), jadi hanya
            // diwajibkan saat blanko N1–N6 yang memang memuat kolom NIK.
            if (ButuhIdentitasIstriLengkap)
            {
                if (string.IsNullOrWhiteSpace(NikIstri))
                {
                    errors.Add("NIK calon istri tidak boleh kosong");
                }
                else if (!Validator.ValidateNik(NikIstri.Trim(), out var nikIstriError))
                {
                    errors.Add($"NIK calon istri: {nikIstriError}");
                }
            }

            if (string.IsNullOrWhiteSpace(NamaIstri))
            {
                errors.Add("Nama calon istri tidak boleh kosong");
            }

            if (string.IsNullOrWhiteSpace(TempatLahirIstri))
            {
                errors.Add("Tempat lahir calon istri tidak boleh kosong");
            }

            if (string.IsNullOrWhiteSpace(TanggalLahirIstri))
            {
                errors.Add("Tanggal lahir calon istri tidak boleh kosong");
            }

            if (ButuhIdentitasIstriLengkap)
            {
                if (string.IsNullOrWhiteSpace(AgamaIstri) ||
                    !ValidAgamaOptions.Contains(AgamaIstri.Trim(), StringComparer.OrdinalIgnoreCase))
                {
                    errors.Add("Agama calon istri harus dipilih dari daftar yang tersedia");
                }

                if (string.IsNullOrWhiteSpace(PekerjaanIstri))
                {
                    errors.Add("Pekerjaan calon istri tidak boleh kosong");
                }
            }

            if (string.IsNullOrWhiteSpace(StatusPerkawinan) ||
                !ValidStatusPerkawinanOptions.Contains(StatusPerkawinan.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                errors.Add("Status Perkawinan calon suami harus dipilih dari daftar yang tersedia");
            }

            // N1 — Surat Pengantar Nikah: identitas orang tua pihak yang diterangkan.
            if (IsN1)
            {
                var (ayah, ibu, sebutan) = PihakDiterangkan();
                if (string.IsNullOrWhiteSpace(ayah.Nama)) errors.Add($"Nama ayah {sebutan} tidak boleh kosong (blanko N1)");
                if (string.IsNullOrWhiteSpace(ibu.Nama)) errors.Add($"Nama ibu {sebutan} tidak boleh kosong (blanko N1)");
            }

            // N2 — Permohonan Kehendak Nikah.
            if (IsN2)
            {
                if (string.IsNullOrWhiteSpace(TujuanKua)) errors.Add("KUA/PPN tujuan surat tidak boleh kosong (blanko N2)");
                if (string.IsNullOrWhiteSpace(HariTanggalJamAkad)) errors.Add("Hari/tanggal/jam akad nikah tidak boleh kosong (blanko N2)");
                if (string.IsNullOrWhiteSpace(TempatAkad)) errors.Add("Tempat akad nikah tidak boleh kosong (blanko N2)");
            }

            // N3 — Permohonan Pencatatan Isbat. Sengaja longgar, senada dengan
            // SuratDataValidator: jarang dipakai dan biasanya diketik tangan di KUA,
            // semua isian boleh dikosongkan sehingga blanko tercetak sebagai template
            // (titik-titik) untuk diisi manual.

            // N5 — Surat Izin Orang Tua: ayah & ibu/wali anak yang diberi izin.
            if (IsN5)
            {
                var (ayah, ibu, sebutan) = PasanganAnak();
                if (string.IsNullOrWhiteSpace(ayah.Nama)) errors.Add($"Nama ayah/wali {sebutan} tidak boleh kosong (blanko N5)");
                if (string.IsNullOrWhiteSpace(ibu.Nama)) errors.Add($"Nama ibu/wali {sebutan} tidak boleh kosong (blanko N5)");
            }

            // N6 — Surat Keterangan Kematian Suami/Istri.
            if (IsN6)
            {
                if (string.IsNullOrWhiteSpace(TanggalMeninggal)) errors.Add("Tanggal meninggal dunia tidak boleh kosong (blanko N6)");
                if (string.IsNullOrWhiteSpace(TempatMeninggal)) errors.Add("Tempat meninggal dunia tidak boleh kosong (blanko N6)");
            }

            // N8 — Surat Keterangan Numpang Nikah: tujuan numpang nikah wajib diisi
            // karena itulah inti suratnya.
            if (IsN8)
            {
                if (string.IsNullOrWhiteSpace(DesaNumpang)) errors.Add("Desa/kelurahan tempat numpang nikah tidak boleh kosong (blanko N8)");
                if (string.IsNullOrWhiteSpace(KecamatanNumpang)) errors.Add("Kecamatan tempat numpang nikah tidak boleh kosong (blanko N8)");
                if (string.IsNullOrWhiteSpace(KabupatenNumpang)) errors.Add("Kabupaten/kota tempat numpang nikah tidak boleh kosong (blanko N8)");
                if (string.IsNullOrWhiteSpace(AlamatIstri)) errors.Add("Alamat calon istri tidak boleh kosong (blanko N8)");
            }
        }

        /// <summary>Orang tua dari pihak yang diterangkan pada blanko N1.</summary>
        public (NtcrOrangTua Ayah, NtcrOrangTua Ibu, string Sebutan) PihakDiterangkan()
        {
            bool istri = string.Equals(PihakDiterangkanN1, "Istri", StringComparison.OrdinalIgnoreCase);
            return istri
                ? (AyahCalonIstri, IbuCalonIstri, "calon istri")
                : (AyahCalonSuami, IbuCalonSuami, "calon suami");
        }

        /// <summary>Orang tua dari anak yang diberi izin pada blanko N5.</summary>
        public (NtcrOrangTua Ayah, NtcrOrangTua Ibu, string Sebutan) PasanganAnak()
        {
            bool istri = string.Equals(PihakAnakIzinOrtu, "Istri", StringComparison.OrdinalIgnoreCase);
            return istri
                ? (AyahCalonIstri, IbuCalonIstri, "calon istri")
                : (AyahCalonSuami, IbuCalonSuami, "calon suami");
        }

        private IEnumerable<NtcrOrangTua> SemuaOrangTua()
        {
            yield return AyahCalonSuami;
            yield return IbuCalonSuami;
            yield return AyahCalonIstri;
            yield return IbuCalonIstri;
        }

        public virtual async Task OnNikIstriLostFocusAsync(CancellationToken cancellationToken = default)
        {
            var nik = NikIstri?.Trim() ?? string.Empty;

            try
            {
                if (string.IsNullOrWhiteSpace(nik))
                {
                    ClearDataWargaIstri();
                    return;
                }

                if (!Validator.ValidateNik(nik, out var nikError))
                {
                    await _messageService.ShowErrorAsync(nikError);
                    ClearDataWargaIstri();
                    return;
                }

                var warga = await _unitOfWork.WargaRepository.GetWargaByNikAsync(nik);
                if (warga != null)
                {
                    FillFieldsFromWargaIstri(warga);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memproses NIK calon istri: {NIK}", nik);
                await _messageService.ShowErrorAsync("Gagal memproses NIK calon istri");
            }
        }

        private void FillFieldsFromWargaIstri(WargaData warga)
        {
            if (warga == null) return;

            NamaIstri = warga.Nama ?? string.Empty;
            TempatLahirIstri = warga.TempatLahir ?? string.Empty;
            StatusPerkawinanIstri = warga.StatusPerkawinan ?? StatusPerkawinanIstri;
            if (!string.IsNullOrWhiteSpace(warga.Kewarganegaraan)) KewarganegaraanIstri = warga.Kewarganegaraan;
            if (!string.IsNullOrWhiteSpace(warga.TanggalLahir))
            {
                if (DateTime.TryParse(warga.TanggalLahir, out var tgl))
                {
                    TanggalLahirIstri = tgl.ToString(DateFormatUi);
                }
                else
                {
                    TanggalLahirIstri = warga.TanggalLahir;
                }
            }
            AgamaIstri = warga.Agama ?? string.Empty;
            PekerjaanIstri = warga.Pekerjaan ?? string.Empty;
            AlamatIstri = warga.Alamat ?? string.Empty;
        }

        private void ClearDataWargaIstri()
        {
            NamaIstri = string.Empty;
            TempatLahirIstri = string.Empty;
            TanggalLahirIstri = string.Empty;
            AgamaIstri = string.Empty;
            PekerjaanIstri = string.Empty;
            AlamatIstri = string.Empty;
        }
    }
}
