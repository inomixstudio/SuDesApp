// IjinTinggalGenerator.cs
// Badan surat dibuat langsung dengan mesin SudesPdf (BadanSurat), tanpa lapisan kompat.
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

        // =================================================================
        // Pembangun teks bersama: SATU sumber untuk bentuk alamat dan kalimat
        // keterangan — dipakai generator PDF di bawah DAN ViewModel input form
        // (IjinTinggalInputViewModel), supaya yang tampil di form persis sama
        // dengan yang tercetak. Dulu keduanya membangun teks sendiri-sendiri
        // dengan format berbeda (koma vs spasi, "Kec." vs "Kecamatan", dua baris
        // vs satu), sehingga isian dan PDF tidak pernah persis sama.
        // =================================================================

        /// <summary>
        /// Alamat asal satu baris: "Dusun X RT 001/RW 002, Desa Y, Kecamatan Z,
        /// Kabupaten W" — bagian kosong dilewati (isian nyata bisa sebagian kosong).
        /// </summary>
        public static string AlamatAsalTeks(
            string? dusun, string? rt, string? rw,
            string? desa, string? kecamatan, string? kabupaten)
        {
            var bagian = new List<string>();

            var depan = new List<string>();
            if (!string.IsNullOrWhiteSpace(dusun)) depan.Add("Dusun " + dusun.Trim());
            string rtRw = AlamatFormatter.FormatRtRw(rt, rw);
            if (rtRw.Length > 0) depan.Add(rtRw);
            if (depan.Count > 0) bagian.Add(string.Join(" ", depan));

            if (!string.IsNullOrWhiteSpace(desa)) bagian.Add("Desa " + desa.Trim());
            if (!string.IsNullOrWhiteSpace(kecamatan)) bagian.Add("Kecamatan " + kecamatan.Trim());
            if (!string.IsNullOrWhiteSpace(kabupaten)) bagian.Add("Kabupaten " + kabupaten.Trim());

            return string.Join(", ", bagian);
        }

        /// <summary>Alamat tujuan satu baris: "Dusun X, Desa Y, Kecamatan Z, Kabupaten W".</summary>
        public static string AlamatTujuanTeks(
            string? dusun, string? desa, string? kecamatan, string? kabupaten)
        {
            var bagian = new List<string>();
            if (!string.IsNullOrWhiteSpace(dusun)) bagian.Add("Dusun " + dusun.Trim());
            if (!string.IsNullOrWhiteSpace(desa)) bagian.Add("Desa " + desa.Trim());
            if (!string.IsNullOrWhiteSpace(kecamatan)) bagian.Add("Kecamatan " + kecamatan.Trim());
            if (!string.IsNullOrWhiteSpace(kabupaten)) bagian.Add("Kabupaten " + kabupaten.Trim());
            return string.Join(", ", bagian);
        }

        /// <summary>
        /// Kalimat keterangan baku — WORDING yang sama dipakai form (isian otomatis)
        /// dan generator (cadangan bila keterangan kosong), jadi pratinjau isi form
        /// tidak pernah berbeda dengan hasil cetak.
        /// </summary>
        public static string KeteranganTeks(
            string? desaAsal, string? kecAsal, string? kabAsal, string alamatTujuan)
        {
            string asal = AlamatAsalTeks(null, null, null, desaAsal, kecAsal, kabAsal);
            return $"Benar nama tersebut diatas adalah warga {asal}. "
                   + "Menurut sepengetahuan kami nama tersebut diatas sampai saat ini "
                   + $"bertempat tinggal sementara di {alamatTujuan}.";
        }

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

                _logger.LogDebug("Dokumen Ijin Tinggal selesai dibuat.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat dokumen Ijin Tinggal");
                throw;
            }
        }

        private new void ValidateSuratData(SuratData suratData)
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

            _logger.LogWarning("Format tanggal lahir tidak valid.");
            return tanggalLahirDb;
        }

        private List<(string Label, string? Value)> CreatePejabatData(SuratData suratData)
        {
            return new List<(string, string?)>
            {
                ("Nama", string.IsNullOrWhiteSpace(suratData.NamaPejabatPenandatangan)
                    ? "[Nama Pejabat]"
                    : NamaFormatter.ToUpperNama(suratData.NamaPejabatPenandatangan)),
                ("Jabatan", $"{suratData.PejabatPenandatangan} {suratData.Desa?.NamaDesa}")
            };
        }

        private List<(string Label, string? Value)> CreatePemohonData(WargaData warga, string formattedTglLahir)
        {
            // Alamat asal memakai pembangun teks bersama — format yang sama dengan
            // isian form, bukan format dua baris yang berbeda seperti dulu.
            string alamatFormatted = AlamatAsalTeks(
                warga.Dusun, warga.RT, warga.RW,
                warga.Desa, warga.Kecamatan, warga.Kabupaten);

            return new List<(string, string?)>
            {
                ("Nama", warga.Nama ?? "[Nama]"),
                ("NIK", warga.NIK ?? "[NIK]"),
                ("Tempat Tanggal Lahir", $"{warga.TempatLahir ?? "[Tempat Lahir]"}, {formattedTglLahir}"),
                ("Jenis Kelamin", warga.JenisKelamin ?? "[Jenis Kelamin]"),
                ("Agama", warga.Agama ?? "[Agama]"),
                ("Status Perkawinan", warga.StatusPerkawinan ?? "[Status]"),
                ("Pekerjaan", warga.Pekerjaan ?? "[Pekerjaan]"),
                ("Alamat", string.IsNullOrWhiteSpace(alamatFormatted) ? "[Alamat]" : alamatFormatted)
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

                // --- ALAMAT ASAL (prioritas: warga → desa → placeholder) ---
                string desaAsal = !string.IsNullOrWhiteSpace(warga.Desa)
                    ? warga.Desa
                    : (!string.IsNullOrWhiteSpace(desa.NamaDesa) ? desa.NamaDesa : string.Empty);
                string kecAsal = !string.IsNullOrWhiteSpace(warga.Kecamatan)
                    ? warga.Kecamatan
                    : (!string.IsNullOrWhiteSpace(desa.Kecamatan) ? desa.Kecamatan : string.Empty);
                string kabAsal = !string.IsNullOrWhiteSpace(warga.Kabupaten)
                    ? warga.Kabupaten
                    : (!string.IsNullOrWhiteSpace(desa.Kabupaten) ? desa.Kabupaten : string.Empty);

                // --- ALAMAT TUJUAN (prioritas: suratData → desa) ---
                string dusunTujuan = !string.IsNullOrWhiteSpace(suratData.DusunTujuan) ? suratData.DusunTujuan : string.Empty;
                string desaTujuan = !string.IsNullOrWhiteSpace(suratData.DesaTujuan)
                    ? suratData.DesaTujuan
                    : (!string.IsNullOrWhiteSpace(desa.NamaDesa) ? desa.NamaDesa : string.Empty);
                string kecTujuan = !string.IsNullOrWhiteSpace(suratData.KecTujuan)
                    ? suratData.KecTujuan
                    : (!string.IsNullOrWhiteSpace(desa.Kecamatan) ? desa.Kecamatan : string.Empty);
                string kabTujuan = !string.IsNullOrWhiteSpace(suratData.KabTujuan)
                    ? suratData.KabTujuan
                    : (!string.IsNullOrWhiteSpace(desa.Kabupaten) ? desa.Kabupaten : string.Empty);

                _logger.LogDebug(
                    "Data keterangan: Asal={DesaAsal}, {KecAsal}, {KabAsal}; Tujuan={DusunTujuan}, {DesaTujuan}, {KecTujuan}, {KabTujuan}",
                    desaAsal, kecAsal, kabAsal, dusunTujuan, desaTujuan, kecTujuan, kabTujuan);

                // Kalimat baku dari pembangun bersama — identik dengan isian otomatis
                // pada form, jadi pratinjau tidak pernah berbeda dengan hasil cetak.
                string alamatTujuan = AlamatTujuanTeks(dusunTujuan, desaTujuan, kecTujuan, kabTujuan);
                return KeteranganTeks(desaAsal, kecAsal, kabAsal, alamatTujuan);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error membuat keterangan otomatis pada {Time}", DateTime.Now.ToString("HH:mm:ss"));
                return "Keterangan tidak dapat dimuat";
            }
        }

        private void AddKeteranganParagraph(BadanSurat badan, string keterangan)
        {
            // Alinea pembuka: baris pertama indentasi 50pt, baris lanjutan
            // rata kiri kembali ke margin — bentuk alinea surat resmi.
            badan.Paragraf(keterangan,
                rata: Rata.Justify,
                indentBarisPertama: 50,
                jarakAtas: 10);
        }

        private void AddPenutupParagraph(BadanSurat badan)
        {
            badan.Paragraf("Demikian surat keterangan ini dibuat untuk dipergunakan sebagaimana mestinya.",
                rata: Rata.Justify,
                jarakAtas: 10);
        }
    }
}
