using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.GeneratorPdf;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>Satu kolom isian pada formulir pengisian surat dari template.</summary>
    public class KolomIsianViewModel : ObservableObject
    {
        private string _nilai = string.Empty;
        private string _kesalahan = string.Empty;

        public KolomIsianViewModel(KolomTemplateSurat kolom)
        {
            Kunci = kolom.Kunci;
            Label = kolom.Label;
            Tipe = kolom.Tipe;
            Wajib = kolom.Wajib;
            Pilihan = kolom.Pilihan ?? new List<string>();
            Kelompok = kolom.Kelompok ?? string.Empty;
            _nilai = kolom.NilaiBawaan ?? string.Empty;
        }

        public string Kunci { get; }
        public string Label { get; }
        public TipeKolomTemplate Tipe { get; }
        public bool Wajib { get; }
        public IReadOnlyList<string> Pilihan { get; }

        /// <summary>Nama kelompok bagian (mis. "Pemohon"); kosong bila tidak berkelompok.</summary>
        public string Kelompok { get; }

        /// <summary>Benar bila ini kolom pertama dengan kelompok yang sama (untuk kepala kelompok).</summary>
        public bool TampilKelompok { get; set; }

        /// <summary>Isian pengguna untuk kolom ini.</summary>
        public string Nilai
        {
            get => _nilai;
            set
            {
                if (SetProperty(ref _nilai, value))
                {
                    Kesalahan = string.Empty;
                }
            }
        }

        /// <summary>Pesan kesalahan khusus kolom ini (kosong bila sudah benar).</summary>
        public string Kesalahan
        {
            get => _kesalahan;
            set
            {
                if (SetProperty(ref _kesalahan, value))
                {
                    OnPropertyChanged(nameof(AdaKesalahan));
                }
            }
        }

        public bool AdaKesalahan => !string.IsNullOrWhiteSpace(_kesalahan);

        public bool PunyaPilihan => Tipe == TipeKolomTemplate.Pilihan && Pilihan.Count > 0;

        /// <summary>Kolom paragraf diisi dengan kotak teks yang bisa beberapa baris.</summary>
        public bool MultiBaris => Tipe == TipeKolomTemplate.Paragraf;

        /// <summary>Label yang tampil, ditandai bintang bila wajib diisi.</summary>
        public string LabelTampil => Wajib ? Label + " *" : Label;

        /// <summary>Panduan singkat tipe kolom (mis. format tanggal).</summary>
        public string Bantuan => Tipe switch
        {
            TipeKolomTemplate.Nik => "16 angka",
            TipeKolomTemplate.Tanggal => "contoh: 17-08-2026",
            TipeKolomTemplate.Angka => "angka saja",
            TipeKolomTemplate.Pilihan => "pilih salah satu",
            TipeKolomTemplate.Paragraf => "boleh beberapa baris",
            _ => string.Empty
        };

        /// <summary>Kembalikan isian ke nilai bawaan template.</summary>
        public void Kosongkan()
        {
            Nilai = string.Empty;
        }
    }

    /// <summary>
    /// Formulir pengisian surat dari Template Surat buatan pengguna: kolom yang dipilih
    /// saat wizard tampil di sini, nomor surat terisi otomatis, dan hasilnya dicetak
    /// sebagai PDF yang langsung dipratinjau.
    /// </summary>
    public class IsiTemplateSuratViewModel : ObservableObject
    {
        private readonly TemplateSuratGenerator _generator;
        private readonly ITemplateSuratRepository _repository;
        private readonly TemplateSuratRegisterService _registerService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly AppConfig _appConfig;
        private readonly Func<string, string, PdfPreviewViewModel> _previewFactory;
        private readonly NavigationService _navigation;
        private readonly IMessageService _messageService;
        private readonly ILogger<IsiTemplateSuratViewModel> _logger;

        /// <summary>Id baris Register Surat surat ini (0 = surat baru; &gt; 0 = sedang mengedit).</summary>
        private int _idSuratTercatat;

        /// <summary>Formulir dibuka untuk memperbaiki surat yang sudah tercatat di register.</summary>
        private bool _modeEdit;

        private TemplateSuratKustom? _template;
        private string _namaTemplate = string.Empty;
        private string _judulSurat = string.Empty;
        private string _nomorSurat = string.Empty;
        private string _namaPejabat = string.Empty;
        private DateTime _tanggalSurat = DateTime.Today;
        private string _pesanKesalahan = string.Empty;
        private string _ringkasanSusunan = string.Empty;
        private bool _isBusy;

        /// <summary>Nomor urut yang terpakai saat surat ini dicetak (0 = belum).</summary>
        private int _urutTerpakai;

        /// <summary>Nomor otomatis yang disarankan; dipakai membandingkan bila pengguna mengedit nomor.</summary>
        private string _nomorOtomatis = string.Empty;

        public IsiTemplateSuratViewModel(
            TemplateSuratGenerator generator,
            ITemplateSuratRepository repository,
            TemplateSuratRegisterService registerService,
            IUnitOfWork unitOfWork,
            AppConfig appConfig,
            Func<string, string, PdfPreviewViewModel> previewFactory,
            NavigationService navigation,
            IMessageService messageService,
            ILogger<IsiTemplateSuratViewModel> logger)
        {
            _generator = generator ?? throw new ArgumentNullException(nameof(generator));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _registerService = registerService ?? throw new ArgumentNullException(nameof(registerService));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _appConfig = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
            _previewFactory = previewFactory ?? throw new ArgumentNullException(nameof(previewFactory));
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            Kolom = new ObservableCollection<KolomIsianViewModel>();

            SimpanCommand = new AsyncRelayCommand(SimpanAsync, () => !IsBusy && _template != null);
            BatalCommand = new RelayCommand(Batal);
            KosongkanCommand = new RelayCommand(() => { foreach (var k in Kolom) k.Kosongkan(); });
        }

        public ObservableCollection<KolomIsianViewModel> Kolom { get; }

        public string NamaTemplate
        {
            get => _namaTemplate;
            private set => SetProperty(ref _namaTemplate, value);
        }

        public string JudulSurat
        {
            get => _judulSurat;
            private set => SetProperty(ref _judulSurat, value);
        }

        public string NomorSurat
        {
            get => _nomorSurat;
            set => SetProperty(ref _nomorSurat, value);
        }

        public DateTime TanggalSurat
        {
            get => _tanggalSurat;
            set => SetProperty(ref _tanggalSurat, value);
        }

        /// <summary>Nama penandatangan; kosong = memakai nama Kepala Desa dari pengaturan.</summary>
        public string NamaPejabat
        {
            get => _namaPejabat;
            set => SetProperty(ref _namaPejabat, value);
        }

        public string RingkasanSusunan
        {
            get => _ringkasanSusunan;
            private set => SetProperty(ref _ringkasanSusunan, value);
        }

        public string PesanKesalahan
        {
            get => _pesanKesalahan;
            private set
            {
                if (SetProperty(ref _pesanKesalahan, value))
                {
                    OnPropertyChanged(nameof(AdaKesalahan));
                }
            }
        }

        public bool AdaKesalahan => !string.IsNullOrWhiteSpace(_pesanKesalahan);

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    SimpanCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool AdaKolom => Kolom.Count > 0;

        /// <summary>Template tanpa kolom isian: hanya nomor, tanggal, dan tanda tangan.</summary>
        public bool TidakAdaKolom => Kolom.Count == 0;

        /// <summary>Formulir sedang memperbaiki surat yang sudah tercatat di register.</summary>
        public bool ModeEdit => _modeEdit;

        /// <summary>Keterangan kepala halaman sesuai mode (surat baru / perbaikan).</summary>
        public string KeteranganHalaman => _modeEdit
            ? "Perbaiki isian surat yang sudah tercatat di Register Surat — nomor lamanya tetap dipakai"
            : "Isi kolom di bawah ini, lalu cetak surat sebagai PDF — surat tercatat otomatis di Register Surat";

        /// <summary>Label tombol simpan sesuai mode.</summary>
        public string TeksTombolSimpan => _modeEdit ? "Simpan Perubahan & Cetak" : "Cetak Surat (PDF)";

        public AsyncRelayCommand SimpanCommand { get; }
        public RelayCommand BatalCommand { get; }
        public RelayCommand KosongkanCommand { get; }

        /// <summary>
        /// Aksi tombol Batal. Bila tidak diisi, halaman dikosongkan (kembali ke tampilan
        /// awal); halaman daftar template mengisinya agar kembali ke daftar template.
        /// </summary>
        public Action? SebelumBatal { get; set; }

        private void Batal()
        {
            if (SebelumBatal != null)
            {
                SebelumBatal();
                return;
            }

            _navigation.ShowDefault();
        }

        /// <summary>Siapkan formulir: kolom isian, nomor surat otomatis, dan penandatangan.</summary>
        public async Task ConfigureAsync(TemplateSuratKustom template)
        {
            _template = template ?? throw new ArgumentNullException(nameof(template));

            Kolom.Clear();
            string kelompokSebelumnya = string.Empty;
            foreach (var kolom in template.Kolom ?? new List<KolomTemplateSurat>())
            {
                var item = new KolomIsianViewModel(kolom);
                item.TampilKelompok = !string.IsNullOrWhiteSpace(item.Kelompok) &&
                    !string.Equals(item.Kelompok, kelompokSebelumnya, StringComparison.OrdinalIgnoreCase);
                kelompokSebelumnya = string.IsNullOrWhiteSpace(item.Kelompok) ? string.Empty : item.Kelompok;
                Kolom.Add(item);
            }
            OnPropertyChanged(nameof(AdaKolom));
            OnPropertyChanged(nameof(TidakAdaKolom));

            NamaTemplate = template.NamaTampil;
            JudulSurat = string.IsNullOrWhiteSpace(template.Judul) ? template.NamaTampil : template.Judul;
            RingkasanSusunan = template.RingkasanSusunan;
            TanggalSurat = DateTime.Today;

            int urut = 0;
            if (template.PakaiNomor && !template.NomorManual)
            {
                _nomorOtomatis = template.NomorBerikutnya(TanggalSurat, out urut);
                NomorSurat = _nomorOtomatis;
            }
            else
            {
                // Nomor manual: kotak dikosongkan supaya diketik sendiri saat mencetak.
                _nomorOtomatis = string.Empty;
                NomorSurat = string.Empty;
            }
            _urutTerpakai = urut;

            SimpanCommand.RaiseCanExecuteChanged();
            await Task.CompletedTask;
        }

        /// <summary>
        /// Buka surat yang sudah tercatat di Register Surat untuk diperbaiki. Definisi
        /// surat dan isiannya dibaca dari payload surat itu sendiri, nomor lama tetap
        /// dipakai, dan Simpan memperbarui baris register yang sama — tidak menerbitkan
        /// nomor baru dan tidak mengubah surat yang sudah dicetak.
        /// </summary>
        public async Task ConfigureForEditAsync(int idSurat)
        {
            try
            {
                var surat = await _unitOfWork.SuratRepository.GetByIdAsync(idSurat);
                if (surat == null)
                {
                    await _messageService.ShowWarningAsync("Surat tidak ditemukan di Register Surat.");
                    return;
                }

                var payload = TemplateSuratTercatat.FromJson(surat.AdditionalData);
                if (payload == null || !payload.BisaDicetak)
                {
                    await _messageService.ShowWarningAsync(
                        $"Isi surat {surat.NomorSurat} tidak dapat dibaca sehingga belum bisa diedit. " +
                        "Surat tetap dapat dibuka dan dicetak ulang dari register.");
                    return;
                }

                await ConfigureAsync(payload.Template);

                foreach (var kolom in Kolom)
                {
                    if (payload.Nilai.TryGetValue(kolom.Kunci, out var nilai) && nilai != null)
                    {
                        kolom.Nilai = nilai;
                    }
                }

                NomorSurat = string.IsNullOrWhiteSpace(surat.NomorSurat) ? payload.NomorSurat : surat.NomorSurat!;
                TanggalSurat = surat.TanggalSurat == default ? payload.TanggalSurat : surat.TanggalSurat;
                NamaPejabat = payload.NamaPejabat ?? string.Empty;

                _idSuratTercatat = idSurat;
                _modeEdit = true;
                OnPropertyChanged(nameof(ModeEdit));
                OnPropertyChanged(nameof(KeteranganHalaman));
                OnPropertyChanged(nameof(TeksTombolSimpan));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memuat surat template #{Id} untuk diedit", idSurat);
                await _messageService.ShowErrorAsync("Gagal memuat surat untuk diedit: " + ex.Message);
            }
        }

        /// <summary>
        /// Hasilkan PDF surat, simpan ke folder keluaran, lalu catat pemakaian nomor
        /// pada template supaya surat berikutnya memakai nomor urut selanjutnya.
        /// </summary>
        private async Task SimpanAsync()
        {
            if (IsBusy || _template == null) return;

            var nilai = Kolom.ToDictionary(k => k.Kunci, k => k.Nilai, StringComparer.OrdinalIgnoreCase);
            var kesalahan = TemplateSuratNilai.Validasi(_template, nilai);

            foreach (var kolom in Kolom)
            {
                kolom.Kesalahan = kesalahan.TryGetValue(kolom.Kunci, out var pesan) ? pesan : string.Empty;
            }

            if (kesalahan.Count > 0)
            {
                PesanKesalahan = TemplateSuratNilai.RingkasKesalahan(kesalahan);
                return;
            }

            if (_template.PakaiNomor && string.IsNullOrWhiteSpace(NomorSurat))
            {
                PesanKesalahan = "Nomor surat belum diisi.";
                return;
            }

            PesanKesalahan = string.Empty;
            IsBusy = true;

            try
            {
                // 1. Catat ke Register Surat lebih dulu (atau perbarui baris yang sedang
                //    diedit). Nomor yang sudah dipakai surat lain harus ketahuan SEBELUM
                //    surat dianggap terbit — supaya tidak ada surat tercetak yang tidak
                //    ditemukan di register.
                var hasilCatat = await _registerService.CatatAsync(SusunPayload(nilai), _idSuratTercatat);
                if (!hasilCatat.Berhasil)
                {
                    PesanKesalahan = hasilCatat.Pesan;
                    await _messageService.ShowWarningAsync(hasilCatat.Pesan);
                    return;
                }

                _idSuratTercatat = hasilCatat.IdSurat;

                // 2. Baru cetak suratnya.
                var pdf = await _generator.BuatPdfAsync(_template, nilai, NomorSurat, TanggalSurat, NamaPejabat);
                if (pdf.Length == 0)
                {
                    PesanKesalahan = "Gagal menghasilkan PDF atau PDF kosong.";
                    return;
                }

                string keluaran = _appConfig.PdfOutputPath;
                Directory.CreateDirectory(keluaran);

                string namaBerkas = $"Template_{BersihkanNamaBerkas(_template.NamaTampil)}_{DateTime.Now:yyyyMMddHHmmss}.pdf";
                string berkas = Path.Combine(keluaran, namaBerkas);
                await File.WriteAllBytesAsync(berkas, pdf);

                // 3. Urutan nomor template hanya maju untuk surat BARU; memperbaiki surat
                //    yang sudah terbit tidak boleh menghabiskan nomor berikutnya. Nomor
                //    manual tak dicatat karena nomornya ditentukan sendiri tiap cetak.
                if (!_modeEdit && !_template.NomorManual)
                {
                    await CatatPemakaianNomorAsync();
                }

                _logger.LogInformation("Surat template '{Nama}' {Aksi}: {Berkas}",
                    _template.NamaTampil, _modeEdit ? "diperbarui" : "dibuat", berkas);

                _navigation.Navigate(_previewFactory($"Surat {_template.NamaTampil} — {NomorSurat}", berkas));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat surat dari template #{Id}", _template.Id);
                PesanKesalahan = "Gagal membuat surat: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Payload surat untuk Register Surat: nomor, tanggal, penandatangan, isian
        /// pengguna, dan SALINAN definisi template — sehingga surat dapat dibuka lagi
        /// dan dicetak ulang persis, walaupun templatenya kemudian diubah atau dihapus.
        /// </summary>
        private TemplateSuratTercatat SusunPayload(IReadOnlyDictionary<string, string> nilai)
        {
            var template = _template!;

            return new TemplateSuratTercatat
            {
                TemplateId = template.Id,
                NamaTemplate = template.NamaTampil,
                JudulSurat = template.Judul ?? string.Empty,
                NomorSurat = (NomorSurat ?? string.Empty).Trim(),
                TanggalSurat = TanggalSurat,
                NamaPejabat = (NamaPejabat ?? string.Empty).Trim(),
                Template = template,
                Nilai = nilai.ToDictionary(
                    pasangan => pasangan.Key,
                    pasangan => pasangan.Value ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase)
            };
        }

        /// <summary>
        /// Simpan nomor urut terakhir ke template. Bila nomor diubah manual (tidak sama
        /// dengan hitungan otomatis), yang dicatat tetap nomor terakhir yang dipakai
        /// agar urutan berikutnya melanjut dari situ.
        /// </summary>
        private async Task CatatPemakaianNomorAsync()
        {
            if (_template == null) return;

            try
            {
                int urut = _urutTerpakai;

                // Nomor diedit pengguna?: urutan dibaca dari nomor yang dicetak memakai
                // pola (bukan kelompok angka pertama — bisa jadi milik awalan, mis.
                // "474.3/005/Ds/2026" → 5, bukan 474). Cadangan: kelompok angka
                // pertama seperti perilaku lama.
                if (!string.Equals((NomorSurat ?? string.Empty).Trim(), _nomorOtomatis.Trim(), StringComparison.Ordinal))
                {
                    int? dariPola = TemplateSuratNomor.BacaUrutDariNomor(_template.PolaNomor, NomorSurat);
                    if (dariPola is > 0 and < 1000000)
                    {
                        urut = dariPola.Value;
                    }
                    else
                    {
                        var kelompok = (NomorSurat ?? string.Empty)
                            .Split(new[] { '/', '-', '.', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                            .Where(b => b.Length > 0 && b.All(char.IsDigit))
                            .ToList();
                        if (kelompok.Count > 0 && int.TryParse(kelompok[0], out var dariNomor) && dariNomor > 0 && dariNomor < 1000000)
                        {
                            urut = dariNomor;
                        }
                    }
                }

                if (urut <= 0) return;

                _template.NomorTerakhir = urut;
                _template.TahunNomor = TanggalSurat.Year;
                await _repository.UpdateAsync(_template);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Nomor surat template #{Id} gagal dicatat; urutan berikutnya mungkin mengulang.", _template.Id);
            }
        }

        /// <summary>Bersihkan nama template menjadi bagian nama berkas yang aman.</summary>
        private static string BersihkanNamaBerkas(string nama)
        {
            var sah = new HashSet<char>(Path.GetInvalidFileNameChars());
            var bersih = new string((nama ?? string.Empty)
                .Select(c => sah.Contains(c) || c == ' ' ? '_' : c)
                .ToArray())
                .Trim('_');
            return bersih.Length == 0 ? "surat" : (bersih.Length > 40 ? bersih.Substring(0, 40) : bersih);
        }
    }
}
