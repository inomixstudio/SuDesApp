using Microsoft.Extensions.Logging;
using SudesPdf.Core.Layout;
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

            // Kelengkapan hanya dituntut saat surat benar-benar diterbitkan. Pada
            // pratinjau form isian sengaja boleh kosong: kolom yang belum diisi
            // dicetak sebagai titik-titik, karena justru itu yang ingin dilihat
            // pengguna sebelum mengisi apa pun.
            if (!PratinjauIsian.Aktif)
            {
                ValidateRekeningKoranData(data);
            }

            _logger.LogDebug(
                "Membuat PDF Permohonan Rekening Koran, NomorSurat={NomorSurat}",
                data.NomorSurat);

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
                            SusunSurat(halaman, data);

                            halaman.Render(kolom);
                        });
                    });
                }).GeneratePdf(penampung);

                pdf = penampung.ToArray();
                jumlahHalaman = HitungJumlahHalaman(pdf);

                if (jumlahHalaman <= HalamanMaksimal)
                {
                    if (!ReferenceEquals(kerapatan, KerapatanSurat.Normal))
                    {
                        _logger.LogDebug(
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
            halaman.Blok(c => KopSurat.Gambar(c, desa, logoPath));

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
                    // Pratinjau mencetak titik-titik pada nomor yang belum ada, bukan
                    // baris kosong: kerangka surat tetap terbaca utuh.
                    kolom.Item().Text($"Nomor\t\t: {PratinjauIsian.Tampil(data.NomorSurat)}");
                    kolom.Item().PaddingTop(3).Text($"Perihal\t\t: {PratinjauIsian.Tampil(data.Perihal)}");
                });

                row.RelativeItem().PaddingLeft(40).Column(kolom =>
                {
                    kolom.Item().Text("Kepada Yth,").Bold();
                    kolom.Item().Text("Customer Service");
                    kolom.Item().Text($"Bank {PratinjauIsian.Tampil(data.Bank)}");
                    kolom.Item().Text($"KCP {PratinjauIsian.Tampil(data.KCP)}");
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

            // Tanda tangan: kolomnya menempel di sisi kanan, sedangkan isi bloknya
            // rata tengah. Tanggal tidak diulang karena sudah tercetak di kepala surat.
            string namaPejabat = NamaFormatter.ToUpperNama(data.NamaPejabat ?? desa.KepalaDesa ?? string.Empty);

            // NIP hanya untuk pejabat ASN kelurahan (Lurah); Kepala Desa tidak ASN.
            string nipPejabat = KopSurat.IsKelurahan(desa) ? (desa.NipKepalaDesa ?? string.Empty).Trim() : string.Empty;

            // Lebar blok ditentukan lewat baris (RelativeItem kosong + ConstantItem),
            // bukan Width pada anak kolom: mesin tata letak menata ISI kotak selebar
            // induknya, sehingga Width + AlignRight di sini menaruh teks di tengah
            // halaman, bukan di sisi kanan.
            halaman.Blok(c => c.PaddingTop(SuratRenderer.JarakBlok(12)).Row(row =>
            {
                row.RelativeItem();
                row.ConstantItem(250).Column(kolom =>
                {
                    kolom.Item().AlignCenter().Text(JabatanTercetak(data, desa)).Bold();
                    kolom.Item().Height(SuratRenderer.RuangTandaTangan);
                    kolom.Item().AlignCenter().Text(
                        string.IsNullOrWhiteSpace(namaPejabat) ? "_______________________" : namaPejabat).Bold();
                    if (!string.IsNullOrWhiteSpace(nipPejabat))
                    {
                        kolom.Item().AlignCenter().Text($"NIP {nipPejabat}");
                    }
                });
            }));
        }

        /// <summary>
        /// Jabatan yang tercetak: dari form bila ada, jika tidak mengikuti lembaga
        /// (“KEPALA DESA X” pada desa, “LURAH X” pada kelurahan).
        /// </summary>
        private static string JabatanTercetak(RekeningKoranData data, DesaData desa)
        {
            string dariForm = (data.Jabatan ?? string.Empty).Trim();
            if (dariForm.Length > 0) return dariForm.ToUpperInvariant();

            string kataPejabat = KopSurat.KataPejabat(desa).ToUpperInvariant();
            string namaDesa = (desa.NamaDesa ?? string.Empty).Trim();
            return namaDesa.Length > 0 ? $"{kataPejabat} {namaDesa.ToUpperInvariant()}" : kataPejabat;
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
            // Jenis wilayah dilengkapi bersama namanya: kop yang memakai nama kabupaten
            // dari pengaturan juga harus memakai kata jenis yang benar dari sana.
            if (string.IsNullOrWhiteSpace(dariForm.JenisKabupaten)) dariForm.JenisKabupaten = pengaturan.JenisKabupaten;
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
