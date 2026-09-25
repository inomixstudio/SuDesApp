// SuratGeneratorBase.cs
// Kelas dasar seluruh generator surat. Seluruh dokumen — kop, judul, badan,
// dan kaki surat — dirender dengan API QuestPDF langsung (tanpa lapisan kompat).
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.ControlSurat; // SettingsManager
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Interfaces;
using SuDesApp.Utilities;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SuDesApp.GeneratorPdf
{
    public abstract class SuratGeneratorBase : ISuratGenerator
    {
        protected readonly AppConfig _config;
        protected readonly FileService _fileService;
        protected readonly IDesaRepository _desaRepository;
        protected readonly SettingsManager _settingsManager;
        protected readonly ILogger<SuratGeneratorBase> _logger;

        protected const float DEFAULT_FONT_SIZE = 12f;
        protected const float TITLE_FONT_SIZE = 14f;

        protected abstract string JudulSurat { get; }

        /// <summary>
        /// Jumlah halaman yang diizinkan. Bila surat melebihi batas ini — umumnya karena
        /// kertasnya A4 yang lebih pendek dari F4 — isinya dirapatkan otomatis. Dokumen
        /// yang memang beberapa halaman (mis. Ahli Waris) menaikkan batas ini.
        /// </summary>
        protected virtual int HalamanMaksimal => 1;

        /// <summary>Kerapatan tata letak yang dicoba berurutan: lapang → rapat.</summary>
        protected static IReadOnlyList<KerapatanSurat> KerapatanBertingkat => KerapatanSurat.Bertingkat;

        protected virtual bool UseDefaultLogo => true;
        protected virtual bool UseDefaultHeader => true;
        protected virtual bool UseDefaultFooter => true;
        protected virtual bool ShowPemohonInFooter => true;

        protected readonly ILoggerFactory _loggerFactory;
        protected readonly ISuratRepository _suratRepository;

        static SuratGeneratorBase()
        {
            QuestPDF.Settings.License = LicenseType.Community;
        }

        protected SuratGeneratorBase(
            AppConfig config,
            FileService fileService,
            IDesaRepository desaRepository,
            ISuratRepository suratRepository,
            SettingsManager settingsManager,
            ILogger<SuratGeneratorBase> logger,
            ILoggerFactory? loggerFactory = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
            _desaRepository = desaRepository ?? throw new ArgumentNullException(nameof(desaRepository));
            _suratRepository = suratRepository ?? throw new ArgumentNullException(nameof(suratRepository));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _logger = logger ?? NullLogger<SuratGeneratorBase>.Instance;
            _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;

            DaftarkanFont(_config, _logger);
        }

        private static bool _fontSudahDidaftarkan;

        /// <summary>
        /// Daftarkan seluruh font di folder font ke QuestPDF sehingga keluarga
        /// "Times New Roman" tersedia untuk semua dokumen surat. Statis dan sekali
        /// per proses — generator mana pun boleh memanggilnya, termasuk yang tidak
        /// mewarisi kelas ini (mis. SuratRegisterGenerator) agar PDF pertama pada
        /// sebuah sesi tetap punya font terdaftar.
        /// </summary>
        internal static void DaftarkanFont(AppConfig config, ILogger logger)
            => DaftarkanFontCore(config.FontFolder, logger);

        /// <summary>Overload tanpa konfigurasi: memakai folder font bawaan aplikasi
        /// (Resources/Fonts di direktori aplikasi). Untuk generator buku yang tidak
        /// menerima AppConfig (Keputusan, Agenda Surat Masuk/Keluar).</summary>
        internal static void DaftarkanFont(ILogger? logger = null)
            => DaftarkanFontCore(null, logger ?? NullLogger.Instance);

        private static void DaftarkanFontCore(string? fontFolder, ILogger logger)
        {
            if (_fontSudahDidaftarkan) return;

            fontFolder ??= System.IO.Path.Combine(AppContext.BaseDirectory, "Resources", "Fonts");
            if (!Directory.Exists(fontFolder))
            {
                logger.LogError("Folder font tidak ditemukan: {FontFolder}", fontFolder);
                throw new DirectoryNotFoundException($"Folder font tidak ditemukan: {fontFolder}");
            }

            foreach (var berkas in Directory.GetFiles(fontFolder, "*.ttf"))
            {
                try
                {
                    using var aliran = new MemoryStream(File.ReadAllBytes(berkas));
                    FontManager.RegisterFontFromStream(aliran);
                }
                catch
                {
                    // Font ganda atau tidak valid diabaikan.
                }
            }

            _fontSudahDidaftarkan = true;
            logger.LogInformation("Font dari {FontFolder} didaftarkan ke QuestPDF.", fontFolder);
        }

        public virtual async Task GeneratePdfAsync(Stream outputStream, int idSurat, string? keteranganTextBox = null)
        {
            try
            {
                _logger.LogInformation("Generating PDF for Surat ID={ID_Surat} (loading data from DB)", idSurat);
                var suratData = await _suratRepository.GetByIdAsync(idSurat);
                if (suratData == null)
                {
                    _logger.LogError("Data surat tidak ditemukan untuk ID_Surat={ID_Surat}", idSurat);
                    throw new InvalidOperationException("Data surat tidak ditemukan.");
                }
                await GeneratePdfAsync(outputStream, suratData, keteranganTextBox);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menghasilkan PDF untuk ID_Surat={ID_Surat}", idSurat);
                throw;
            }
        }

        public virtual async Task GeneratePdfAsync(Stream outputStream, SuratData suratData, string? keteranganTextBox = null)
        {
            if (outputStream == null)
                throw new ArgumentNullException(nameof(outputStream));
            if (suratData == null)
            {
                _logger.LogError("SuratData is null when generating PDF.");
                throw new ArgumentNullException(nameof(suratData), "SuratData tidak boleh null saat generate PDF.");
            }
            if (!outputStream.CanWrite)
            {
                _logger.LogError("Output stream tidak dapat ditulis di GeneratePdfAsync");
                throw new InvalidOperationException("Output stream tidak dapat ditulis.");
            }

            _logger.LogInformation("Generating PDF for Surat ID={ID_Surat} (using provided SuratData object)", suratData.ID_Surat);

            if (suratData.Desa == null || !suratData.IsDesaDataValid(suratData.Desa))
            {
                _logger.LogWarning("DesaData null atau tidak valid di SuratData. Memuat ulang dari pengaturan...");
                await EnsureDesaDataLoadedAsync(suratData);
            }
            else
            {
                _logger.LogInformation("DesaData sudah valid, tidak perlu memuat ulang.");
            }

            // Sekali saja di titik masuk: pakai salinan DesaData yang nama wilayahnya
            // sudah bersih, supaya kop, badan surat, dan footer sama-sama rapi
            // (“Kepala Desa Sumberjaya”, bukan “Kepala Desa Desa Sumberjaya”).
            suratData.Desa = KopSurat.DesaBersih(suratData.Desa);

            ValidateSuratData(suratData);

            // Ukuran kertas mengikuti Pengaturan Surat → Pengaturan Cetak (A4 bawaan, atau F4).
            var (lebarHalaman, tinggiHalaman) = PengaturanCetak.Dimensi();

            try
            {
                // Coba dulu tampilan lapang (Normal). Bila isinya melimpah ke halaman
                // berikutnya — biasa terjadi pada A4 yang lebih pendek dari F4 — dokumen
                // dirender ulang dengan kerapatan lebih rapat sampai muat, sehingga
                // surat yang sudah muat tidak berubah sama sekali.
                var ukuranPdf = RenderDenganPenyesuaian(suratData, keteranganTextBox!, lebarHalaman, tinggiHalaman);
                outputStream.Write(ukuranPdf, 0, ukuranPdf.Length);

                _logger.LogInformation("PDF generation completed for Surat ID={ID_Surat}, stream size: {Size} bytes", suratData.ID_Surat, outputStream.Length);
                if (outputStream.Length == 0)
                {
                    _logger.LogError("Stream PDF kosong setelah penulisan untuk ID_Surat={ID_Surat}", suratData.ID_Surat);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during PDF generation for Surat ID={ID_Surat}", suratData.ID_Surat);
                throw;
            }
        }

        /// <summary>
        /// Render surat berulang kali dengan kerapatan yang makin rapat sampai jumlah
        /// halamannya tidak melebihi <see cref="HalamanMaksimal"/>, lalu kembalikan PDF-nya.
        /// </summary>
        private byte[] RenderDenganPenyesuaian(SuratData suratData, string keteranganTextBox, float lebarHalaman, float tinggiHalaman)
        {
            byte[]? ukuranPdf = null;
            int jumlahHalaman = 0;

            foreach (var kerapatan in KerapatanSurat.Bertingkat)
            {
                using var penanda = KerapatanSurat.Pakai(kerapatan);
                using var penampung = new MemoryStream();

                Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        SiapkanHalaman(page, lebarHalaman, tinggiHalaman);

                        page.Content().Column(kolom =>
                        {
                            var halaman = new BadanSurat();
                            ComposeHalaman(halaman, suratData, keteranganTextBox);
                            foreach (var potongan in halaman.Potongan)
                            {
                                var render = potongan;
                                kolom.Item().Element(c => render(c));
                            }
                        });
                    });
                }).GeneratePdf(penampung);

                ukuranPdf = penampung.ToArray();
                jumlahHalaman = HitungJumlahHalaman(ukuranPdf);

                if (jumlahHalaman <= HalamanMaksimal)
                {
                    if (!ReferenceEquals(kerapatan, KerapatanSurat.Normal))
                    {
                        _logger.LogInformation("Surat ID={ID_Surat} dirapatkan ke kerapatan {Kerapatan} agar muat {Halaman} halaman.",
                            suratData.ID_Surat, kerapatan.Nama, HalamanMaksimal);
                    }
                    break;
                }

                _logger.LogInformation("Surat ID={ID_Surat} memakai kerapatan {Kerapatan} masih {Jumlah} halaman; mencoba kerapatan berikutnya.",
                    suratData.ID_Surat, kerapatan.Nama, jumlahHalaman);
            }

            if (jumlahHalaman > HalamanMaksimal)
            {
                _logger.LogWarning("Surat ID={ID_Surat} tetap {Jumlah} halaman meski sudah memakai kerapatan paling rapat (batas {Batas}).",
                    suratData.ID_Surat, jumlahHalaman, HalamanMaksimal);
            }

            return ukuranPdf ?? Array.Empty<byte>();
        }

        /// <summary>
        /// Ukuran kertas, margin, dan gaya teks baku seluruh surat. Dipakai satu
        /// halaman tunggal maupun dokumen gabungan (mis. paket NTCR N1–N6).
        /// </summary>
        protected static void SiapkanHalaman(PageDescriptor page, float lebarHalaman, float tinggiHalaman)
        {
            var kerapatan = KerapatanSurat.Aktif;

            page.Size(new PageSize(lebarHalaman, tinggiHalaman, Unit.Point));
            page.MarginTop(kerapatan.MarginAtas, Unit.Point);     // top, right, bottom, left
            page.MarginRight(45, Unit.Point);
            // Sisi bawah diberi ruang supaya tanda tangan tidak menyentuh
            // tepi kertas (area cetak printer umumnya butuh ±20pt); margin menipis
            // bersama kerapatan bila isi surat memang panjang.
            page.MarginBottom(kerapatan.MarginBawah, Unit.Point);
            page.MarginLeft(45, Unit.Point);
            page.DefaultTextStyle(x => x.FontFamily("Times New Roman").FontSize(DEFAULT_FONT_SIZE));
        }

        /// <summary>
        /// Jumlah halaman dokumen, dibaca dari pohon halaman PDF yang dihasilkan QuestPDF.
        /// </summary>
        protected static int HitungJumlahHalaman(byte[] pdf)
        {
            // Latin1: satu byte = satu karakter, sehingga pola dapat dicari langsung.
            string isi = System.Text.Encoding.Latin1.GetString(pdf);
            var cocok = System.Text.RegularExpressions.Regex.Match(isi, @"/Type\s*/Pages[\s\S]{0,256}?/Count\s+(\d+)");
            return cocok.Success && int.TryParse(cocok.Groups[1].Value, out int jumlah) ? jumlah : 1;
        }

        private async Task EnsureDesaDataLoadedAsync(SuratData suratData)
        {
            if (suratData.Desa == null || !suratData.IsDesaDataValid(suratData.Desa))
            {
                _logger.LogWarning("DesaData di SuratData null atau tidak valid saat diterima SuratGeneratorBase. Surat ID={ID_Surat}. Mencoba muat ulang sebagai fallback.", suratData.ID_Surat);
                suratData.Desa = await _desaRepository.GetInfoDesaAsync();

                if (suratData.Desa == null || !suratData.IsDesaDataValid(suratData.Desa))
                {
                    _logger.LogError("Gagal memuat DesaData yang valid bahkan setelah fallback di SuratGeneratorBase. Surat ID={ID_Surat}.", suratData.ID_Surat);
                    suratData.Desa = new DesaData
                    {
                        NamaDesa = "[KESALAHAN: Data Desa Tidak Terkonfigurasi]",
                        Kecamatan = "[N/A]",
                        Kabupaten = "[N/A]",
                        Alamat = "[N/A]",
                        Kodepos = "[N/A]",
                        KepalaDesa = "[N/A]",
                        SekretarisDesa = "[N/A]",
                        NamaCamat = "[N/A]"
                    };
                }
            }
            if (suratData.Desa == null)
            {
                throw new InvalidOperationException("Objek DesaData adalah null setelah semua upaya pemuatan.");
            }
            ValidateDesaData(suratData.Desa);
        }

        /// <summary>Lokasi berkas logo surat; null bila berkasnya tidak ditemukan.</summary>
        protected string CariLogoPath()
        {
            // Gambar kop mengikuti Pengaturan Surat (bisa diganti pengguna);
            // null berarti kop dicetak tanpa logo.
            string? logoPath = PengaturanCetak.JalurLogoEfektif(_config.LogoPath);
            if (logoPath != null)
            {
                return logoPath;
            }

            _logger.LogWarning("Gambar logo tidak ditemukan; kop surat dicetak tanpa logo.");
            return null!;
        }

        /// <summary>
        /// Susunan satu halaman surat: kop, judul + nomor, badan surat, lalu blok
        /// tanda tangan. Generator yang bentuknya khusus menimpa metode ini.
        /// </summary>
        protected virtual void ComposeHalaman(BadanSurat halaman, SuratData suratData, string? keteranganTextBox = null)
        {
            if (UseDefaultHeader)
            {
                var desa = suratData.Desa;
                string logoPath = CariLogoPath();
                halaman.Blok(c => SuratRenderer.Kop(c, desa!, logoPath));
            }

            ComposeJudul(halaman, suratData);
            ComposeBody(halaman, suratData, keteranganTextBox);

            if (UseDefaultFooter)
            {
                ComposeTandaTangan(halaman, suratData);
                ComposeFooterTambahan(halaman, suratData);
            }
        }

        /// <summary>Judul surat + baris NOMOR. Generator boleh menimpanya bila perlu gaya lain.</summary>
        protected virtual void ComposeJudul(BadanSurat judul, SuratData suratData)
        {
            string teksJudul = JudulSurat;
            string nomor = HitungNomorSurat(suratData);
            judul.Blok(c => SuratRenderer.JudulDanNomor(c, teksJudul, nomor));
        }

        /// <summary>Blok tanda tangan standar (pemohon opsional di kiri, Kepala Desa di kanan).</summary>
        protected virtual void ComposeTandaTangan(BadanSurat kaki, SuratData suratData)
        {
            var data = DataKakiSurat(suratData, "Pemohon :");
            kaki.Blok(c => SuratRenderer.TandaTangan(
                c, data.TampilkanPemohon, data.NamaPemohon!, data.NamaDesa, data.TanggalTerformat, data.Jabatan, data.NamaPejabat, data.LabelPemohon));
        }

        /// <summary>Nomor surat yang tercetak: dari data, atau disusun dari format jenis surat.</summary>
        protected string HitungNomorSurat(SuratData suratData)
        {
            string nomorSuratFormat = _config.SuratNumberFormats.TryGetValue(suratData.NamaJenis?.ToUpperInvariant() ?? "DEFAULT", out var format)
                ? format
                : "XXX/{0:D3}/Ds/{2:yyyy}";

            return string.IsNullOrWhiteSpace(suratData.NomorSurat) || suratData.NomorSurat.Contains("{")
                ? string.Format(nomorSuratFormat, suratData.ID_Surat > 0 ? suratData.ID_Surat : 0, suratData.ID_Jenis, suratData.TanggalSurat)
                : suratData.NomorSurat;
        }

        /// <summary>Data yang tercetak pada blok tanda tangan.</summary>
        protected (bool TampilkanPemohon, string? NamaPemohon, string NamaDesa, string TanggalTerformat, string Jabatan, string NamaPejabat, string LabelPemohon) DataKakiSurat(SuratData suratData, string labelPemohon)
        {
            // Desa sudah divalidasi saat generator menyiapkan data; bila tetap null,
            // gagalkan dengan pesan jelas alih-alih melempar NullReferenceException.
            var desa = suratData.Desa ?? throw new InvalidOperationException("Objek DesaData adalah null saat menyusun blok tanda tangan surat.");

            string tanggalTerformat = suratData.TanggalSurat.ToString("dd MMMM yyyy", new CultureInfo("id-ID"));

            var pejabat = string.IsNullOrEmpty(suratData.PejabatPenandatangan)
               ? "Kepala Desa"
               : suratData.PejabatPenandatangan;

            // Jabatan ditulis KAPITAL sesuai template resmi desa ("KEPALA DESA SUMBERJAYA").
            var jabatan = pejabat.Equals("Sekretaris Desa", StringComparison.OrdinalIgnoreCase)
                ? $"A/N Kepala Desa {desa.NamaDesa}\nSekretaris Desa".ToUpperInvariant()
                : $"Kepala Desa {desa.NamaDesa}".ToUpperInvariant();

            // Nama penandatangan: kapital pada namanya, gelar dibiarkan apa adanya.
            var namaPejabat = !string.IsNullOrEmpty(suratData.NamaPejabatPenandatangan)
                ? NamaFormatter.ToUpperNama(suratData.NamaPejabatPenandatangan)
                : (!string.IsNullOrEmpty(desa.KepalaDesa)
                    ? NamaFormatter.ToUpperNama(desa.KepalaDesa)
                    : "_______________________");

            bool tampilkanPemohon = ShowPemohonInFooter;
            if (tampilkanPemohon && suratData.Warga == null)
            {
                _logger.LogWarning("ShowPemohonInFooter true tapi WargaData null, blok pemohon dilewati.");
                tampilkanPemohon = false;
            }

            return (tampilkanPemohon, suratData.Warga?.Nama, desa.NamaDesa ?? "NAMA DESA", tanggalTerformat, jabatan, namaPejabat, labelPemohon);
        }

        /// <summary>
        /// Isi tambahan di bawah blok tanda tangan Kepala Desa (mis. tanda tangan
        /// Camat). Generator yang butuh menimpanya.
        /// </summary>
        protected virtual void ComposeFooterTambahan(BadanSurat badan, SuratData suratData)
        {
        }

        /// <summary>Badan surat masing-masing generator.</summary>
        protected virtual void ComposeBody(BadanSurat badan, SuratData suratData, string? keteranganTextBox = null)
        {
        }

        protected void ValidateSuratData(SuratData suratData)
        {
            if (suratData.NamaJenis?.ToUpperInvariant() != "INSTANSI")
            {
                if (suratData.Warga == null || string.IsNullOrWhiteSpace(suratData.Warga.Nama) || string.IsNullOrWhiteSpace(suratData.Warga.NIK))
                {
                    _logger.LogError("Data warga tidak lengkap: Nama={Nama}, NIK={NIK}, NamaJenis={NamaJenis}",
                        suratData.Warga?.Nama, suratData.Warga?.NIK, suratData.NamaJenis);
                    throw new InvalidOperationException("Data warga tidak lengkap untuk generate PDF.");
                }
            }
            else
            {
                if ((suratData.Warga == null || suratData.Warga.ID_Warga <= 0) && (suratData.Instansi == null || string.IsNullOrWhiteSpace(suratData.Instansi.NamaInstansi)))
                {
                    _logger.LogError("Data untuk INSTANSI tidak lengkap: Warga.ID_Warga={ID_Warga} atau Instansi.NamaInstansi={NamaInstansi}",
                        suratData.Warga?.ID_Warga, suratData.Instansi?.NamaInstansi);
                    throw new InvalidOperationException("Data instansi tidak lengkap untuk generate PDF.");
                }
            }
        }

        private void ValidateDesaData(DesaData desa)
        {
            if (desa == null)
            {
                _logger.LogError("DesaData is null during validation.");
                throw new InvalidOperationException("Data desa tidak boleh null.");
            }

            var missingFields = new List<string>();
            if (string.IsNullOrWhiteSpace(desa.NamaDesa)) missingFields.Add("Nama Desa");
            if (string.IsNullOrWhiteSpace(desa.Kecamatan)) missingFields.Add("Kecamatan");
            if (string.IsNullOrWhiteSpace(desa.Kabupaten)) missingFields.Add("Kabupaten");
            if (string.IsNullOrWhiteSpace(desa.Alamat)) missingFields.Add("Alamat Kantor Desa");
            if (string.IsNullOrWhiteSpace(desa.KepalaDesa)) missingFields.Add("Nama Kepala Desa");

            if (missingFields.Any())
            {
                _logger.LogError("Data desa tidak lengkap. Kolom yang kosong: {MissingFields}. Harap periksa Setelan Aplikasi.", string.Join(", ", missingFields));
                throw new InvalidOperationException($"Data desa tidak lengkap. Kolom berikut harus diisi di Setelan Aplikasi: {string.Join(", ", missingFields)}.");
            }
        }
    }
}
