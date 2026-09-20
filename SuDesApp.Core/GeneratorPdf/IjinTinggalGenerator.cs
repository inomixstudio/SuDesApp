// IjinTinggalGenerator.cs
// Badan surat dibuat langsung dengan QuestPDF (BadanSurat), tanpa lapisan kompat.
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace SuDesApp.GeneratorPdf
{
    public class IjinTinggalGenerator : SuratGeneratorBase
    {
        public IjinTinggalGenerator(
            AppConfig config,
            FileService fileService,
            IDesaRepository desaRepository,
            ISuratRepository suratRepository,
            SettingsManager settingsManager,
            ILogger<IjinTinggalGenerator> logger,
            ILoggerFactory loggerFactory)
            : base(config, fileService, desaRepository, suratRepository, settingsManager, logger, loggerFactory)
        {
        }

        protected override string JudulSurat => "SURAT KETERANGAN IJIN TINGGAL SEMENTARA";
        protected override bool ShowPemohonInFooter => true;

        protected override void ComposeBody(BadanSurat badan, SuratData suratData, string? keteranganTextBox = null)
        {
            try
            {
                ValidateSuratData(suratData);
                var desa = suratData.Desa!;
                var warga = suratData.Warga!;

                badan.Paragraf("Yang bertanda tangan di bawah ini :", jarakAtas: 20);

                badan.TabelFormulir(CreatePejabatData(suratData), indentKiri: 20);

                badan.Paragraf("Dengan ini menerangkan bahwa :", jarakAtas: 10);

                string tanggalLahirFormatted = FormatTanggalLahir(warga.TanggalLahir);
                badan.TabelFormulir(CreatePemohonData(warga, tanggalLahirFormatted), indentKiri: 20);

                // Penanggung jawab di alamat tujuan tinggal
                if (!string.IsNullOrWhiteSpace(suratData.NamaPenanggungJawab) ||
                    !string.IsNullOrWhiteSpace(suratData.NikPenanggungJawab))
                {
                    badan.Paragraf("Adapun yang menjadi penanggung jawab selama tinggal sementara :", jarakAtas: 10);

                    string tglLahirPjFormatted = FormatTanggalLahir(suratData.TglLahirPenanggungJawab);
                    badan.TabelFormulir(CreatePenanggungJawabData(suratData, tglLahirPjFormatted), indentKiri: 20);
                }

                string keteranganFinal = GetKeteranganFinal(keteranganTextBox, suratData);
                AddKeteranganParagraph(badan, keteranganFinal);
                AddPenutupParagraph(badan);

                _logger.LogInformation("Berhasil membuat dokumen Ijin Tinggal untuk {Nik}", warga.NIK);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat dokumen Ijin Tinggal");
                throw;
            }
        }

        private void ValidateSuratData(SuratData suratData)
        {
            if (suratData?.Warga == null)
            {
                _logger.LogError("Data Warga tidak valid");
                throw new InvalidOperationException("Data Warga tidak boleh null");
            }

            if (suratData.Desa == null)
            {
                _logger.LogError("Data Desa tidak valid");
                throw new InvalidOperationException("Data Desa tidak boleh null");
            }
        }

        private string FormatTanggalLahir(string? tanggalLahirDb)
        {
            if (string.IsNullOrWhiteSpace(tanggalLahirDb))
                return "[Tanggal Lahir]";

            if (DateTime.TryParseExact(tanggalLahirDb, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTime tglLahir))
            {
                return tglLahir.ToString("dd MMMM yyyy", new CultureInfo("id-ID"));
            }

            if (DateTime.TryParseExact(tanggalLahirDb, "dd-MM-yyyy", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out tglLahir))
            {
                return tglLahir.ToString("dd MMMM yyyy", new CultureInfo("id-ID"));
            }

            _logger.LogWarning("Format tanggal lahir tidak valid: {TanggalLahir}", tanggalLahirDb);
            return tanggalLahirDb;
        }

        private List<(string Label, string? Value)> CreatePejabatData(SuratData suratData)
        {
            return new List<(string, string?)>
            {
                ("Nama", string.IsNullOrWhiteSpace(suratData.NamaPejabatPenandatangan)
                    ? "[Nama Pejabat]"
                    : NamaFormatter.ToUpperNama(suratData.NamaPejabatPenandatangan)),
                ("Jabatan", $"{suratData.PejabatPenandatangan} {suratData.Desa.NamaDesa}")
            };
        }

        private List<(string Label, string? Value)> CreatePemohonData(WargaData warga, string formattedTglLahir)
        {
            string alamatFormatted = FormatAlamatAsalForDisplay(warga);

            return new List<(string, string?)>
            {
                ("Nama", warga.Nama ?? "[Nama]"),
                ("NIK", warga.NIK ?? "[NIK]"),
                ("Tempat Tanggal Lahir", $"{warga.TempatLahir ?? "[Tempat Lahir]"}, {formattedTglLahir}"),
                ("Jenis Kelamin", warga.JenisKelamin ?? "[Jenis Kelamin]"),
                ("Agama", warga.Agama ?? "[Agama]"),
                ("Status Perkawinan", warga.StatusPerkawinan ?? "[Status]"),
                ("Pekerjaan", warga.Pekerjaan ?? "[Pekerjaan]"),
                ("Alamat", alamatFormatted)
            };
        }

        private List<(string Label, string? Value)> CreatePenanggungJawabData(SuratData suratData, string formattedTglLahir)
        {
            return new List<(string, string?)>
            {
                ("Nama", string.IsNullOrWhiteSpace(suratData.NamaPenanggungJawab)
                    ? "[Nama Penanggung Jawab]"
                    : NamaFormatter.ToUpperNama(suratData.NamaPenanggungJawab)),
                ("NIK", suratData.NikPenanggungJawab ?? "[NIK Penanggung Jawab]"),
                ("Tanggal Lahir", string.IsNullOrWhiteSpace(formattedTglLahir) ? "[Tanggal Lahir]" : formattedTglLahir),
                ("Pekerjaan", suratData.PekerjaanPenanggungJawab ?? "[Pekerjaan]")
            };
        }

        private string FormatAlamatAsalForDisplay(WargaData warga)
        {
            if (warga == null)
                return "[Alamat]";

            // Format alamat asal (alamat sebelumnya)
            string dusun = !string.IsNullOrWhiteSpace(warga.Dusun)
                ? $"{warga.Dusun}"
                : string.Empty;

            string desa = !string.IsNullOrWhiteSpace(warga.Desa)
                ? $"Desa {warga.Desa}"
                : "[Desa]";

            string kecamatan = !string.IsNullOrWhiteSpace(warga.Kecamatan)
                ? $"Kecamatan {warga.Kecamatan}"
                : "[Kecamatan]";

            string kabupaten = !string.IsNullOrWhiteSpace(warga.Kabupaten)
                ? $"Kabupaten {warga.Kabupaten}"
                : "[Kabupaten]";

            // Build address string, omit dusun if empty
            string alamatLine1 = string.IsNullOrWhiteSpace(dusun) ? desa : $"{dusun} {desa}";
            string alamatLine2 = $"{kecamatan} {kabupaten}";

            return $"{alamatLine1}\n {alamatLine2}";
        }

        private string GetKeteranganFinal(string? keteranganTextBox, SuratData suratData)
        {
            // Prioritize keterangan from textbox if provided
            if (!string.IsNullOrWhiteSpace(keteranganTextBox))
                return keteranganTextBox;

            // Use keterangan from SuratData if available
            if (!string.IsNullOrWhiteSpace(suratData.Keterangan))
                return suratData.Keterangan;

            try
            {
                var desa = suratData.Desa;
                var warga = suratData.Warga;

                if (warga == null)
                {
                    _logger.LogWarning("Data warga null saat membuat keterangan pada {Time}", DateTime.Now.ToString("HH:mm:ss"));
                    return "Keterangan tidak dapat dimuat: data warga tidak valid";
                }
                if (desa == null)
                {
                    _logger.LogWarning("Data desa null saat membuat keterangan pada {Time}", DateTime.Now.ToString("HH:mm:ss"));
                    return "Keterangan tidak dapat dimuat: data desa tidak valid";
                }

                // --- ALAMAT ASAL (prioritas: warga ? desa ? placeholder) ---
                string desaAsal = !string.IsNullOrWhiteSpace(warga.Desa)
                    ? warga.Desa
                    : (!string.IsNullOrWhiteSpace(desa.NamaDesa) ? desa.NamaDesa : "[Desa]");
                string kecAsal = !string.IsNullOrWhiteSpace(warga.Kecamatan)
                    ? warga.Kecamatan
                    : (!string.IsNullOrWhiteSpace(desa.Kecamatan) ? desa.Kecamatan : "[Kecamatan]");
                string kabAsal = !string.IsNullOrWhiteSpace(warga.Kabupaten)
                    ? warga.Kabupaten
                    : (!string.IsNullOrWhiteSpace(desa.Kabupaten) ? desa.Kabupaten : "[Kabupaten]");

                // --- ALAMAT TUJUAN (prioritas: suratData ? desa ? placeholder) ---
                string dusunTujuan = !string.IsNullOrWhiteSpace(suratData.DusunTujuan) ? suratData.DusunTujuan : string.Empty;
                string desaTujuan = !string.IsNullOrWhiteSpace(suratData.DesaTujuan)
                    ? suratData.DesaTujuan
                    : (!string.IsNullOrWhiteSpace(desa.NamaDesa) ? desa.NamaDesa : "[Desa]");
                string kecTujuan = !string.IsNullOrWhiteSpace(suratData.KecTujuan)
                    ? suratData.KecTujuan
                    : (!string.IsNullOrWhiteSpace(desa.Kecamatan) ? desa.Kecamatan : "[Kecamatan]");
                string kabTujuan = !string.IsNullOrWhiteSpace(suratData.KabTujuan)
                    ? suratData.KabTujuan
                    : (!string.IsNullOrWhiteSpace(desa.Kabupaten) ? desa.Kabupaten : "[Kabupaten]");

                _logger.LogDebug(
                    "Data keterangan: Asal={DesaAsal}, {KecAsal}, {KabAsal}; Tujuan={DusunTujuan}, {DesaTujuan}, {KecTujuan}, {KabTujuan}",
                    desaAsal, kecAsal, kabAsal, dusunTujuan, desaTujuan, kecTujuan, kabTujuan);

                // Build the destination address string
                string alamatTujuan = string.IsNullOrWhiteSpace(dusunTujuan)
                    ? $"Desa {desaTujuan} Kecamatan {kecTujuan} Kabupaten {kabTujuan}"
                    : $"{dusunTujuan} Desa {desaTujuan} Kecamatan {kecTujuan} Kabupaten {kabTujuan}";

                return $"Benar nama tersebut diatas adalah warga Desa {desaAsal} Kec. {kecAsal} Kab. {kabAsal}. " +
                       $"Menurut sepengetahuan kami nama tersebut diatas sampai saat ini " +
                       $"bertempat tinggal sementara di {alamatTujuan}.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error membuat keterangan otomatis pada {Time}", DateTime.Now.ToString("HH:mm:ss"));
                return "Keterangan tidak dapat dimuat";
            }
        }

        private void AddKeteranganParagraph(BadanSurat badan, string keterangan)
        {
            // Indentasi 50pt untuk seluruh baris (indent 30 + margin kiri 20 bentuk lama).
            badan.Paragraf(keterangan,
                rata: Rata.Justify,
                indentKiri: 50,
                jarakAtas: 15);
        }

        private void AddPenutupParagraph(BadanSurat badan)
        {
            badan.Paragraf("Demikian surat keterangan ini dibuat untuk dipergunakan sebagaimana mestinya.",
                rata: Rata.Justify,
                indentKiri: 30,
                jarakAtas: 20);
        }
    }
}
