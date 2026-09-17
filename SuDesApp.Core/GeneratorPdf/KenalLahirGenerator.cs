// File: SuDesApp/GeneratorPdf/KenalLahirGenerator.cs
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

namespace SuDesApp.GeneratorPdf
{
    public class KenalLahirGenerator : SuratGeneratorBase
    {
        public KenalLahirGenerator(
            AppConfig config,
            FileService fileService,
            IDesaRepository desaRepository,
            ISuratRepository suratRepository,
            SettingsManager settingsManager,
            ILogger<KenalLahirGenerator> logger,
            ILoggerFactory loggerFactory)
            : base(config, fileService, desaRepository, suratRepository, settingsManager, logger, loggerFactory)
        {
        }

        protected override string JudulSurat => "SURAT KETERANGAN KENAL LAHIR";
        protected override bool ShowPemohonInFooter => false;

        protected override void ComposeBody(BadanSurat badan, SuratData suratData, string keteranganTextBox = null)
        {
            var desa = suratData.Desa;
            var kenalLahir = suratData.KenalLahir;
            var ayah = kenalLahir?.Ayah;
            var ibu = kenalLahir?.Ibu;

            if (desa == null || kenalLahir == null || ayah == null || ibu == null)
            {
                _logger.LogError("Data tidak valid: Desa={Desa}, KenalLahir={KenalLahir}, Ayah={Ayah}, Ibu={Ibu}",
                    desa != null, kenalLahir != null, ayah != null, ibu != null);
                throw new InvalidOperationException("Data Desa, KenalLahir, Ayah, atau Ibu tidak boleh null untuk membuat SK KENAL LAHIR.");
            }

            // Ambil data anak dari KenalLahirData (dengan fallback ke AnakData)
            string namaAnak = GetNamaAnak(kenalLahir);
            DateTime? tanggalLahirAnak = GetTanggalLahirAnak(kenalLahir);
            string tempatLahirAnak = GetTempatLahirAnak(kenalLahir);
            int anakKe = kenalLahir.AnakKe;
            string lahirDi = kenalLahir.LahirDi;
            string alamatAnak = kenalLahir.AlamatLengkapAnak;

            // Validasi data anak minimal
            if (string.IsNullOrWhiteSpace(namaAnak))
            {
                _logger.LogError("Nama anak tidak valid");
                throw new InvalidOperationException("Nama anak tidak boleh kosong untuk membuat SK KENAL LAHIR.");
            }

            // Paragraf pembuka — nama desa dicetak tebal dalam satu baris yang sama.
            badan.ParagrafCampur(new List<(string, bool)>
            {
                ("Yang bertanda tangan dibawah ini, Kepala Desa ", false),
                (desa.NamaDesa, true),
                ($" Kecamatan {desa.Kecamatan} Kabupaten {desa.Kabupaten} menerangkan berdasarkan keterangan dari :", false)
            },
            rata: Rata.Justify,
            jarakAtas: 15);

            // Data Ayah Kandung
            badan.Paragraf("Ayah Kandung", tebal: true, jarakAtas: 10);
            badan.TabelFormulir(CreateWargaData(ayah), indentKiri: 20);

            // Data Ibu Kandung
            badan.Paragraf("Ibu Kandung", tebal: true, jarakAtas: 10);
            badan.TabelFormulir(CreateWargaData(ibu), indentKiri: 20);

            // Data Anak
            badan.Paragraf("Telah Lahir seorang anak", tebal: true, jarakAtas: 10);
            badan.TabelFormulir(CreateAnakData(namaAnak, tempatLahirAnak, tanggalLahirAnak, anakKe, lahirDi, alamatAnak), indentKiri: 20);

            // Paragraf Penutup
            badan.Paragraf("Demikian surat keterangan ini dibuat untuk dipergunakan sebagaimana mestinya.",
                rata: Rata.Justify,
                indentKiri: 30,
                jarakAtas: 20);

            _logger.LogInformation("Berhasil membuat dokumen KENAL LAHIR untuk Anak: {NamaAnak}", namaAnak);
        }

        /// <summary>
        /// Kaki surat kenal lahir: pelapor (ayah) di kiri dengan label “Pelapor,”,
        /// Kepala Desa di kanan.
        /// </summary>
        protected override void ComposeTandaTangan(BadanSurat kaki, SuratData suratData)
        {
            var data = DataKakiSurat(suratData, "Pelapor,");
            string namaPelapor = suratData.KenalLahir?.Ayah?.Nama;

            kaki.Blok(c => SuratRenderer.TandaTangan(
                c, !string.IsNullOrWhiteSpace(namaPelapor), namaPelapor, data.NamaDesa, data.TanggalTerformat,
                data.Jabatan, data.NamaPejabat, data.LabelPemohon));
        }

        private static List<(string Label, string? Value)> CreateWargaData(WargaData warga)
        {
            string tglLahir = FormatTanggal(warga.TanggalLahir);
            return new List<(string, string?)>
            {
                ("Nama", warga.Nama ?? "[Nama]"),
                ("NIK", warga.NIK ?? "[NIK]"),
                ("Tempat/Tgl. Lahir", $"{warga.TempatLahir ?? "[Tempat Lahir]"}, {tglLahir}"),
                ("Agama", warga.Agama ?? "[Agama]"),
                ("Pekerjaan", warga.Pekerjaan ?? "[Pekerjaan]"),
                ("Alamat", string.IsNullOrWhiteSpace(warga.AlamatLengkap) ? "[Alamat]" : warga.AlamatLengkap)
            };
        }

        private static List<(string Label, string? Value)> CreateAnakData(string namaAnak, string? tempatLahirAnak, DateTime? tanggalLahirAnak, int anakKe, string? lahirDi, string? alamatAnak)
        {
            string tglLahirAnak = FormatTanggalDateTime(tanggalLahirAnak);
            string anakKeText = ConvertAnakKeToText(anakKe);

            return new List<(string, string?)>
            {
                ("Nama", namaAnak),
                ("Tempat/Tgl. Lahir", $"{tempatLahirAnak ?? "[Tempat Lahir]"}, {tglLahirAnak}"),
                ("Anak Ke", $"{anakKe} ({anakKeText})"),
                ("Lahir di", lahirDi ?? "[Lahir Di]"),
                ("Alamat", string.IsNullOrWhiteSpace(alamatAnak) ? "[Alamat]" : alamatAnak)
            };
        }

        // Helper methods untuk mengambil data anak dengan fallback strategy
        private static string GetNamaAnak(KenalLahirData kenalLahir)
        {
            // Prioritas: KenalLahirData.NamaAnak -> AnakData.NamaAnak
            if (!string.IsNullOrWhiteSpace(kenalLahir.NamaAnak))
                return kenalLahir.NamaAnak;

            if (kenalLahir.Anak != null && !string.IsNullOrWhiteSpace(kenalLahir.Anak.NamaAnak))
                return kenalLahir.Anak.NamaAnak;

            return "[Nama Anak]";
        }

        private static DateTime? GetTanggalLahirAnak(KenalLahirData kenalLahir)
        {
            // Prioritas: KenalLahirData.TanggalLahirAnak -> AnakData.TanggalLahir
            if (kenalLahir.TanggalLahirAnak.HasValue)
                return kenalLahir.TanggalLahirAnak;

            string tanggalLahirString = kenalLahir.Anak?.TanggalLahir;
            if (!string.IsNullOrWhiteSpace(tanggalLahirString))
            {
                if (DateTime.TryParseExact(tanggalLahirString, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
                    return t;
                if (DateTime.TryParse(tanggalLahirString, out var t2))
                    return t2;
            }

            return null;
        }

        private static string GetTempatLahirAnak(KenalLahirData kenalLahir)
        {
            // Hanya ada di KenalLahirData.TempatLahirAnak
            return kenalLahir.TempatLahirAnak ?? "[Tempat Lahir]";
        }

        private static string ConvertAnakKeToText(int anakKe)
        {
            return anakKe switch
            {
                1 => "Kesatu",
                2 => "Kedua",
                3 => "Ketiga",
                4 => "Keempat",
                5 => "Kelima",
                6 => "Keenam",
                7 => "Ketujuh",
                8 => "Kedelapan",
                9 => "Kesembilan",
                10 => "Kesepuluh",
                11 => "Kesebelas",
                12 => "Kedua Belas",
                13 => "Ketiga Belas",
                14 => "Keempat Belas",
                15 => "Kelima Belas",
                16 => "Keenam Belas",
                17 => "Ketujuh Belas",
                18 => "Kedelapan Belas",
                19 => "Kesembilan Belas",
                20 => "Kedua Puluh",
                _ => anakKe.ToString() // fallback jika di luar 1-20
            };
        }

        // Method untuk format DateTime? (tanggal lahir anak)
        private static string FormatTanggalDateTime(DateTime? tanggal)
        {
            if (tanggal.HasValue)
            {
                return tanggal.Value.ToString("dd MMMM yyyy", new CultureInfo("id-ID"));
            }
            return "[Tanggal Lahir]";
        }

        // Method untuk format string tanggal (tanggal lahir orang tua)
        private static string FormatTanggal(string tanggal)
        {
            if (string.IsNullOrWhiteSpace(tanggal))
                return "[Tanggal Lahir]";

            if (DateTime.TryParseExact(tanggal, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
            {
                return t.ToString("dd MMMM yyyy", new CultureInfo("id-ID"));
            }

            // Coba parse format lain jika diperlukan
            if (DateTime.TryParse(tanggal, out var t2))
            {
                return t2.ToString("dd MMMM yyyy", new CultureInfo("id-ID"));
            }

            return "[Tanggal Lahir]";
        }
    }
}
