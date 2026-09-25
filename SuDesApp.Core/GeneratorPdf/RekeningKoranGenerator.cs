using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SuDesApp.Configuration;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace SuDesApp.GeneratorPdf
{
    /// <summary>
    /// Generator Permohonan Print Out Rekening Koran.
    ///
    /// Turunan <see cref="SuratGeneratorBase"/> supaya ukuran kertas, margin, gaya
    /// teks, dan KOP-nya persis sama dengan surat-surat lain. Data desa diambil dari
    /// pengaturan aplikasi — bukan disusun ulang dari jabatan penandatangan — sehingga
    /// kop tidak lagi kehilangan kecamatan, kabupaten, alamat kantor, dan kodepos.
    /// Alamat pejabat dibentuk dari empat komponen (Dusun/Jalan, Desa, Kecamatan,
    /// Kabupaten) dengan pemformatan yang sama seperti hasil input surat.
    /// </summary>
    public class RekeningKoranGenerator : SuratGeneratorBase
    {
        public RekeningKoranGenerator(
            AppConfig config,
            FileService fileService,
            IDesaRepository desaRepository,
            ISuratRepository suratRepository,
            SettingsManager settingsManager,
            ILogger<RekeningKoranGenerator> logger,
            ILoggerFactory loggerFactory)
            : base(config, fileService, desaRepository, suratRepository, settingsManager, logger, loggerFactory)
        {
            _logger.LogInformation("RekeningKoranGenerator siap — memakai kop & tata letak SuratGeneratorBase.");
        }

        protected override string JudulSurat => "PERMOHONAN PRINT OUT REKENING KORAN";

        /// <summary>Isi surat ini pendek: cukup satu halaman.</summary>
        protected override int HalamanMaksimal => 1;

        /// <summary>Surat ditandatangani Kepala Desa saja (tanpa kolom pemohon).</summary>
        protected override bool ShowPemohonInFooter => false;

        /// <summary>
        /// Cetak permohonan dari data form. Dipakai tombol Buat PDF maupun saat surat
        /// dicetak ulang dari register (isi surat dibaca kembali dari payload JSON).
        /// </summary>
        public async Task GeneratePdfAsync(Stream outputStream, RekeningKoranData data)
        {
            if (outputStream == null) throw new ArgumentNullException(nameof(outputStream));
            if (data == null)
            {
                _logger.LogError("Data RekeningKoranData tidak boleh null.");
                throw new ArgumentNullException(nameof(data));
            }

            // Kop diambil dari pengaturan desa supaya lengkap; data dari form hanya
            // dipakai untuk kolom yang memang diisi form (nama desa & kepala desa).
            data.Desa = await DesaLengkapAsync(data.Desa).ConfigureAwait(false);

            ValidateRekeningKoranData(data);

            _logger.LogInformation(
                "Membuat PDF Permohonan Rekening Koran, NomorSurat={NomorSurat}, Desa={Desa}",
                data.NomorSurat, data.Desa?.NamaDesa);

            var (lebarHalaman, tinggiHalaman) = PengaturanCetak.Dimensi();
            byte[] pdf = RenderDenganKerapatan(data, lebarHalaman, tinggiHalaman);

            await outputStream.WriteAsync(pdf, 0, pdf.Length).ConfigureAwait(false);
        }

        /// <summary>
        /// Jalur cadangan bila generator dipanggil lewat API dasar (data SuratData):
        /// isi surat dibaca dari payload JSON di kolom AdditionalData.
        /// </summary>
        protected override void ComposeHalaman(BadanSurat halaman, SuratData suratData, string? keteranganTextBox = null)
        {
            var data = RekeningKoranData.FromJson(suratData.AdditionalData);
            if (data == null)
            {
                throw new InvalidOperationException(
                    "Isi permohonan rekening koran (payload JSON surat) tidak terbaca.");
            }

            data.NomorSurat = suratData.NomorSurat ?? data.NomorSurat;
            if (suratData.TanggalSurat != default) data.TanggalSurat = suratData.TanggalSurat;
            data.Desa = KomplitkanDesa(data.Desa);

            SusunSurat(halaman, data);
        }

        // =====================================================================
        // Penyusunan dokumen
        // =====================================================================

        /// <summary>
        /// Render berulang dengan kerapatan makin rapat sampai surat muat satu halaman —
        /// perilaku yang sama dengan seluruh surat lain di <see cref="SuratGeneratorBase"/>.
        /// </summary>
        private byte[] RenderDenganKerapatan(RekeningKoranData data, float lebarHalaman, float tinggiHalaman)
        {
            byte[]? pdf = null;
            int jumlahHalaman = 0;

            foreach (var kerapatan in KerapatanBertingkat)
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
                            SusunSurat(halaman, data);

                            foreach (var potongan in halaman.Potongan)
                            {
                                var render = potongan;
                                kolom.Item().Element(c => render(c));
                            }
                        });
                    });
                }).GeneratePdf(penampung);

                pdf = penampung.ToArray();
                jumlahHalaman = HitungJumlahHalaman(pdf);

                if (jumlahHalaman <= HalamanMaksimal)
                {
                    if (!ReferenceEquals(kerapatan, KerapatanSurat.Normal))
                    {
                        _logger.LogInformation(
                            "Permohonan rekening koran dirapatkan ke kerapatan {Kerapatan} agar muat {Halaman} halaman.",
                            kerapatan.Nama, HalamanMaksimal);
                    }
                    break;
                }
            }

            return pdf ?? Array.Empty<byte>();
        }

        /// <summary>Kop bersama + badan khas surat permohonan rekening koran.</summary>
        private void SusunSurat(BadanSurat halaman, RekeningKoranData data)
        {
            var desa = data.Desa ?? new DesaData();

            // Kop memakai renderer bersama — sama persis dengan surat keterangan lain.
            string logoPath = CariLogoPath();
            halaman.Blok(c => SuratRenderer.Kop(c, desa, logoPath));

            // Tempat & tanggal (rata kanan), seperti surat desa pada umumnya.
            string namaDesa = CultureInfo.CurrentCulture.TextInfo.ToTitleCase((desa.NamaDesa ?? string.Empty).ToLower());
            halaman.Paragraf(
                $"{namaDesa}, {FormatTanggalIndo(data.TanggalSurat)}",
                rata: Rata.Kanan, jarakAtas: 14, jarakBawah: 12);

            // Nomor & perihal di lajur kiri, alamat tujuan di lajur kanan.
            halaman.Blok(c => c.Row(row =>
            {
                row.RelativeItem().Column(kolom =>
                {
                    kolom.Item().Text($"Nomor\t\t: {data.NomorSurat}");
                    kolom.Item().PaddingTop(3).Text($"Perihal\t\t: {data.Perihal}");
                });

                row.RelativeItem().PaddingLeft(40).Column(kolom =>
                {
                    kolom.Item().Text("Kepada Yth,").Bold();
                    kolom.Item().Text("Customer Service");
                    kolom.Item().Text($"Bank {data.Bank}");
                    kolom.Item().Text($"KCP {data.KCP}");
                    kolom.Item().PaddingTop(5).Text("Di -");
                    kolom.Item().PaddingLeft(20).Text("Tempat");
                });
            }));

            halaman.Paragraf("Dengan Hormat,", jarakAtas: 22);
            halaman.Paragraf("Saya yang bertanda tangan di bawah ini:", jarakAtas: 12);

            // Alamat pejabat: empat komponen yang diformat sama seperti surat lain
            // (Dusun/Jalan & Desa, lalu Kecamatan & Kabupaten).
            halaman.TabelFormulir(
                new List<(string Label, string? Nilai)>
                {
                    ("Nama", data.NamaPejabat),
                    ("Jabatan", JabatanTercetak(data, desa)),
                    ("Alamat", data.AlamatPejabatLengkap)
                },
                jarakAtas: 10, jarakBawah: 10, indentKiri: 20);

            halaman.Paragraf("Bermaksud mengajukan Permohonan Print Out Rekening Koran atas nama:", jarakAtas: 14);

            halaman.TabelFormulir(
                new List<(string Label, string? Nilai)>
                {
                    ("Nama Pemegang Rekening", data.NamaPemegangRekening),
                    ("No. Rekening Giro", data.NomorRekening),
                    ("Rekening Koran Periode", FormatTanggalPeriode(data.PeriodeRekening))
                },
                jarakAtas: 10, jarakBawah: 10, tebalkanNama: false, indentKiri: 20);

            halaman.Paragraf(
                "Demikian permohonan ini kami sampaikan, atas perhatian dan kerjasamanya kami ucapkan terima kasih.",
                jarakAtas: 18);

            // Tanda tangan: jabatan + nama, sejajar kanan. Tanggal tidak diulang karena
            // sudah tercetak di kepala surat.
            string namaPejabat = NamaFormatter.ToUpperNama(data.NamaPejabat ?? desa.KepalaDesa ?? string.Empty);
            halaman.Blok(c => c.PaddingTop(SuratRenderer.JarakBlok(12)).AlignRight().Width(250).Column(kolom =>
            {
                kolom.Item().AlignCenter().Text(JabatanTercetak(data, desa)).Bold();
                kolom.Item().Height(SuratRenderer.RuangTandaTangan);
                kolom.Item().AlignCenter().Text(
                    string.IsNullOrWhiteSpace(namaPejabat) ? "_______________________" : namaPejabat).Bold();
            }));
        }

        /// <summary>Jabatan yang tercetak: dari form bila ada, jika tidak dari nama desa.</summary>
        private static string JabatanTercetak(RekeningKoranData data, DesaData desa)
        {
            string dariForm = (data.Jabatan ?? string.Empty).Trim();
            if (dariForm.Length > 0) return dariForm.ToUpperInvariant();

            string namaDesa = (desa.NamaDesa ?? string.Empty).Trim();
            return namaDesa.Length > 0 ? $"KEPALA DESA {namaDesa.ToUpperInvariant()}" : "KEPALA DESA";
        }

        // =====================================================================
        // Data desa
        // =====================================================================

        /// <summary>
        /// Ambil data desa dari pengaturan aplikasi. Form rekening koran dulu menyusun
        /// data desa hanya dari jabatan ("Kepala Desa Sumberjaya"), sehingga kop kehilangan
        /// kecamatan, kabupaten, alamat, dan kodepos. Di sini data itu dilengkapi lagi
        /// agar kop sama dengan surat lain — termasuk saat mencetak ulang surat lama.
        /// </summary>
        private async Task<DesaData> DesaLengkapAsync(DesaData? dariForm)
        {
            DesaData? pengaturan = null;
            try
            {
                pengaturan = await _desaRepository.GetInfoDesaAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal memuat data desa untuk kop permohonan rekening koran.");
            }

            if (pengaturan == null)
            {
                return KopSurat.DesaBersih(dariForm);
            }

            if (dariForm == null)
            {
                return KopSurat.DesaBersih(pengaturan);
            }

            // Kolom yang memang diisi form (nama desa & kepala desa) diutamakan;
            // sisanya dilengkapi dari pengaturan.
            if (string.IsNullOrWhiteSpace(dariForm.NamaDesa)) dariForm.NamaDesa = pengaturan.NamaDesa;
            if (string.IsNullOrWhiteSpace(dariForm.Kecamatan)) dariForm.Kecamatan = pengaturan.Kecamatan;
            if (string.IsNullOrWhiteSpace(dariForm.Kabupaten)) dariForm.Kabupaten = pengaturan.Kabupaten;
            if (string.IsNullOrWhiteSpace(dariForm.Alamat)) dariForm.Alamat = pengaturan.Alamat;
            if (string.IsNullOrWhiteSpace(dariForm.Kodepos)) dariForm.Kodepos = pengaturan.Kodepos;
            // Email desa (opsional) juga dilengkapi dari pengaturan: form rekening koran
            // tidak mengisinya sendiri, jadi tanpa baris ini kop kehilangan email yang
            // sudah diisi pengguna di Pengaturan Surat.
            if (string.IsNullOrWhiteSpace(dariForm.Email)) dariForm.Email = pengaturan.Email;
            if (string.IsNullOrWhiteSpace(dariForm.KepalaDesa)) dariForm.KepalaDesa = pengaturan.KepalaDesa;
            if (string.IsNullOrWhiteSpace(dariForm.SekretarisDesa)) dariForm.SekretarisDesa = pengaturan.SekretarisDesa;

            return KopSurat.DesaBersih(dariForm);
        }

        /// <summary>Salinan data desa yang komponennya sudah dirapikan (nama wilayah bersih).</summary>
        private static DesaData KomplitkanDesa(DesaData? desa) => KopSurat.DesaBersih(desa);

        // =====================================================================
        // Pembantu format
        // =====================================================================

        private static string FormatTanggalIndo(DateTime tanggal) =>
            tanggal.ToString("dd MMMM yyyy", new CultureInfo("id-ID"));

        /// <summary>"01-01-2026 s/d 31-03-2026" → "01 Januari 2026 s/d 31 Maret 2026".</summary>
        private static string FormatTanggalPeriode(string? periode)
        {
            if (string.IsNullOrWhiteSpace(periode)) return "N/A";

            string[] bagian = periode.Split(new[] { " s/d " }, StringSplitOptions.None);
            if (bagian.Length != 2) return periode;

            if (DateTime.TryParseExact(bagian[0], "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime mulai) &&
                DateTime.TryParseExact(bagian[1], "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime selesai))
            {
                return $"{FormatTanggalIndo(mulai)} s/d {FormatTanggalIndo(selesai)}";
            }

            return periode;
        }

        private void ValidateRekeningKoranData(RekeningKoranData data)
        {
            var kurang = new List<string>();

            if (string.IsNullOrWhiteSpace(data.NomorSurat)) kurang.Add("Nomor Surat");
            if (string.IsNullOrWhiteSpace(data.NamaPejabat)) kurang.Add("Nama Kepala Desa");
            if (string.IsNullOrWhiteSpace(data.Jabatan)) kurang.Add("Jabatan");
            if (data.AlamatKosong) kurang.Add("Alamat Kepala Desa");
            if (string.IsNullOrWhiteSpace(data.NamaPemegangRekening)) kurang.Add("Nama Pemegang Rekening");
            if (string.IsNullOrWhiteSpace(data.NomorRekening)) kurang.Add("Nomor Rekening");
            if (string.IsNullOrWhiteSpace(data.PeriodeRekening)) kurang.Add("Periode Rekening");
            if (string.IsNullOrWhiteSpace(data.Bank)) kurang.Add("Bank");

            if (kurang.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Data tidak lengkap. Kolom berikut harus diisi: {string.Join(", ", kurang)}.");
            }
        }
    }
}
