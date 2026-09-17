// NtcrGenerator.cs
// Badan surat dibuat langsung dengan QuestPDF (BadanSurat), tanpa lapisan kompat.
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;
using System.Globalization;

namespace SuDesApp.GeneratorPdf
{
    /// <summary>
    /// Generator PDF untuk surat NTCR (N1-N4) — persyaratan pendaftaran pernikahan.
    /// Susunan konten mengikuti template baku masing-masing jenis:
    /// N1 Surat Pengantar Nikah, N2 Surat Keterangan Untuk Nikah (berisi status
    /// perkawinan &amp; temuan), N3 Surat Persetujuan Calon Mempelai (berisi pernyataan
    /// persetujuan &amp; tujuan surat), N4 Surat Keterangan Orang Tua (ayah &amp; ibu
    /// kedua pihak yang menerangkan).
    /// </summary>
    public class NtcrGenerator : SuratGeneratorBase
    {
        private string _currentJudul = "SURAT PENGANTAR NIKAH";

        public NtcrGenerator(
            AppConfig config,
            FileService fileService,
            IDesaRepository desaRepository,
            ISuratRepository suratRepository,
            SettingsManager settingsManager,
            ILogger<NtcrGenerator> logger,
            ILoggerFactory loggerFactory)
            : base(config, fileService, desaRepository, suratRepository, settingsManager, logger, loggerFactory)
        {
        }

        protected override string JudulSurat => _currentJudul;

        // Footer NTCR memakai pola baku desa: tempat, tanggal, jabatan, nama —
        // tanpa kotak tanda tangan pemohon di sebelah kiri.
        protected override bool ShowPemohonInFooter => false;

        public override async Task GeneratePdfAsync(Stream outputStream, SuratData suratData, string keteranganTextBox = null)
        {
            _currentJudul = GetJudulByNamaJenis(suratData?.NamaJenis);
            await base.GeneratePdfAsync(outputStream, suratData, keteranganTextBox);
        }

        protected override void ComposeBody(BadanSurat badan, SuratData suratData, string keteranganTextBox = null)
        {
            try
            {
                ValidateSuratData(suratData);
                var desa = suratData.Desa!;
                var warga = suratData.Warga!;
                var ntcr = suratData.Ntcr!;

                switch (suratData.NamaJenis?.ToUpperInvariant())
                {
                    case SuratConstants.NTCR_N2:
                        ComposeN2(badan, suratData, desa, warga, ntcr, keteranganTextBox);
                        break;
                    case SuratConstants.NTCR_N3:
                        ComposeN3(badan, suratData, desa, warga, ntcr, keteranganTextBox);
                        break;
                    case SuratConstants.NTCR_N4:
                        ComposeN4(badan, suratData, desa, warga, ntcr, keteranganTextBox);
                        break;
                    default:
                        ComposeN1(badan, suratData, desa, warga, ntcr, keteranganTextBox);
                        break;
                }

                _logger.LogInformation("Berhasil membuat dokumen NTCR ({NamaJenis}) untuk {NIK}",
                    suratData.NamaJenis, warga.NIK);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat dokumen NTCR");
                throw;
            }
        }

        // =====================================================================
        // N1 — SURAT PENGANTAR NIKAH
        // Pejabat menerangkan: data calon pria, calon wanita, orang tua kedua
        // pihak, pernyataan niat menikah, tujuan KUA.
        // =====================================================================
        private void ComposeN1(BadanSurat badan, SuratData suratData, DesaData desa, WargaData warga, NtcrData ntcr, string keteranganTextBox)
        {
            AddPengantarPejabat(badan);
            AddTabelCalonPria(badan, warga);

            JudulData(badan, "Calon mempelai wanita :");
            AddTabelCalonWanita(badan, ntcr);

            if (AdaDataOrangTua(ntcr))
            {
                JudulData(badan, "Data orang tua kedua belah pihak :");
                badan.TabelFormulir(CreateOrangTuaData(ntcr), indentKiri: 20);
            }

            AddBodyParagraph(badan,
                $"Bahwa yang namanya tersebut di atas adalah benar penduduk Desa {NamaDesa(desa)} " +
                $"Kecamatan {NamaKecamatan(desa)} dan sampai saat ini masih berkedudukan sebagai penduduk Desa {NamaDesa(desa)}.");

            AddBodyParagraph(badan,
                $"Bahwa calon mempelai pria tersebut di atas akan melangsungkan pernikahan dengan " +
                $"calon mempelai wanita tersebut di atas, dan Surat Pengantar Nikah ini dibuat untuk " +
                $"dipergunakan sebagai salah satu kelengkapan persyaratan pendaftaran keperluan " +
                $"pernikahan pada KUA Kecamatan {NamaKecamatan(desa)}.");

            AddKeteranganDanPenutup(badan, suratData, keteranganTextBox, desa);
        }

        // =====================================================================
        // N2 — SURAT KETERANGAN UNTUK NIKAH
        // Pejabat menerangkan: data calon pria, calon wanita, status perkawinan
        // kedua pihak, temuan (bila janda/duda).
        // =====================================================================
        private void ComposeN2(BadanSurat badan, SuratData suratData, DesaData desa, WargaData warga, NtcrData ntcr, string keteranganTextBox)
        {
            AddPengantarPejabat(badan);
            AddTabelCalonPria(badan, warga);

            JudulData(badan, "Calon mempelai wanita :");
            AddTabelCalonWanita(badan, ntcr);

            AddBodyParagraph(badan,
                $"Bahwa yang namanya tersebut di atas adalah benar penduduk Desa {NamaDesa(desa)} " +
                $"Kecamatan {NamaKecamatan(desa)}.");

            AddBodyParagraph(badan,
                "Bahwa menurut sepengetahuan kami, calon mempelai pria tersebut di atas berstatus " +
                $"perkawinan {StatusAtauPlaceholder(warga.StatusPerkawinan)} dan sampai saat ini " +
                "belum pernah melangsungkan pernikahan yang tercatat di Desa kami.");

            string statusIstri = StatusAtauPlaceholder(ntcr.StatusPerkawinanIstri);
            string paragrafIstri = "Bahwa menurut sepengetahuan kami, calon mempelai wanita tersebut di atas " +
                $"berstatus perkawinan {statusIstri}";
            bool istriSudahKawin = (ntcr.StatusPerkawinanIstri ?? "").StartsWith("Sudah", StringComparison.OrdinalIgnoreCase);
            if (istriSudahKawin)
            {
                paragrafIstri += !string.IsNullOrWhiteSpace(ntcr.KeteranganTemuan)
                    ? $", sebagaimana ditemukan dalam {ntcr.KeteranganTemuan.Trim()}"
                    : ", dengan temuan sebagaimana tercantum pada kolom Temuan";
            }
            paragrafIstri += ".";
            AddBodyParagraph(badan, paragrafIstri);

            if (!string.IsNullOrWhiteSpace(ntcr.KeteranganTemuan) && !istriSudahKawin)
            {
                AddBodyParagraph(badan, $"Temuan: {ntcr.KeteranganTemuan.Trim()}");
            }

            AddKeteranganDanPenutup(badan, suratData, keteranganTextBox, desa);
        }

        // =====================================================================
        // N3 — SURAT PERSETUJUAN CALON MEMPELAI
        // Pejabat menerangkan: data calon pria & wanita, orang tua, pernyataan
        // persetujuan kedua mempelai, tujuan surat.
        // =====================================================================
        private void ComposeN3(BadanSurat badan, SuratData suratData, DesaData desa, WargaData warga, NtcrData ntcr, string keteranganTextBox)
        {
            AddPengantarPejabat(badan);
            AddTabelCalonPria(badan, warga);

            JudulData(badan, "Calon mempelai wanita :");
            AddTabelCalonWanita(badan, ntcr);

            if (AdaDataOrangTua(ntcr))
            {
                JudulData(badan, "Data orang tua kedua belah pihak :");
                badan.TabelFormulir(CreateOrangTuaData(ntcr), indentKiri: 20);
            }

            AddBodyParagraph(badan,
                $"Bahwa yang namanya tersebut di atas adalah benar-benar calon suami dan calon istri yang " +
                $"akan melangsungkan pernikahan, dan kedua calon mempelai tersebut telah menyatakan " +
                $"persetujuannya untuk melangsungkan pernikahan.");

            AddBodyParagraph(badan,
                $"Surat Persetujuan Calon Mempelai ini dibuat untuk keperluan " +
                $"{(string.IsNullOrWhiteSpace(ntcr.TujuanSurat) ? $"pendaftaran keperluan pernikahan pada KUA Kecamatan {NamaKecamatan(desa)}" : ntcr.TujuanSurat.Trim())}.");

            AddKeteranganDanPenutup(badan, suratData, keteranganTextBox, desa);
        }

        // =====================================================================
        // N4 — SURAT KETERANGAN ORANG TUA
        // Ayah & ibu kedua pihak yang menerangkan (bukan pejabat): data anak
        // calon pria & wanita, pernyataan mengizinkan pernikahan.
        // =====================================================================
        private void ComposeN4(BadanSurat badan, SuratData suratData, DesaData desa, WargaData warga, NtcrData ntcr, string keteranganTextBox)
        {
            string namaAyahPria = NamaAtauPlaceholder(ntcr.NamaAyahCalonSuami, "Nama Ayah Calon Pria");
            string namaIbuPria = NamaAtauPlaceholder(ntcr.NamaIbuCalonSuami, "Nama Ibu Calon Pria");
            string namaAyahWanita = NamaAtauPlaceholder(ntcr.NamaAyahCalonIstri, "Nama Ayah Calon Wanita");
            string namaIbuWanita = NamaAtauPlaceholder(ntcr.NamaIbuCalonIstri, "Nama Ibu Calon Wanita");

            badan.Paragraf("Kami yang bertanda tangan di bawah ini :", jarakAtas: 20);

            var orangTua = new List<(string Label, string? Value)>
            {
                ("Ayah Calon Mempelai Pria", namaAyahPria),
                ("Ibu Calon Mempelai Pria", namaIbuPria),
                ("Ayah Calon Mempelai Wanita", namaAyahWanita),
                ("Ibu Calon Mempelai Wanita", namaIbuWanita)
            };
            badan.TabelFormulir(orangTua, indentKiri: 20);

            JudulData(badan, "Menerangkan dengan sebenarnya bahwa anak kami :");

            AddTabelCalonPria(badan, warga);

            JudulData(badan, "Dan calon mempelai wanita :");
            AddTabelCalonWanita(badan, ntcr);

            AddBodyParagraph(badan,
                "Bahwa kami sebagai orang tua dari calon mempelai pria dan calon mempelai wanita tersebut di atas " +
                "dengan ini menyatakan tidak keberatan serta mengizinkan kedua anak kami untuk melangsungkan " +
                "pernikahan sesuai dengan agama dan peraturan perundang-undangan yang berlaku.");

            AddKeteranganDanPenutup(badan, suratData, keteranganTextBox, desa);
        }

        // =====================================================================
        // Blok konten bersama
        // =====================================================================

        private void AddPengantarPejabat(BadanSurat badan)
        {
            badan.Paragraf("Yang bertanda tangan di bawah ini :", jarakAtas: 20);

            JudulData(badan, "Menerangkan dengan sebenarnya bahwa calon mempelai pria :");
        }

        private void JudulData(BadanSurat badan, string text)
            => badan.Paragraf(text, jarakAtas: 10);

        private void AddTabelCalonPria(BadanSurat badan, WargaData warga)
        {
            string tglLahirFormatted = FormatTanggalLahir(warga.TanggalLahir);
            badan.TabelFormulir(CreateCalonPriaData(warga, tglLahirFormatted), indentKiri: 20);
        }

        private void AddTabelCalonWanita(BadanSurat badan, NtcrData ntcr)
        {
            string tglLahirIstri = FormatTanggalLahir(ntcr.TanggalLahirIstri);
            badan.TabelFormulir(CreateCalonWanitaData(ntcr, tglLahirIstri), indentKiri: 20);
        }

        private void AddBodyParagraph(BadanSurat badan, string text)
        {
            // Indentasi 50pt untuk seluruh baris (indent 30 + margin kiri 20 bentuk lama).
            badan.Paragraf(text,
                rata: Rata.Justify,
                indentKiri: 50,
                jarakAtas: 10);
        }

        /// <summary>Keterangan bebas (jika diisi) lalu penutup standar surat desa.</summary>
        private void AddKeteranganDanPenutup(BadanSurat badan, SuratData suratData, string keteranganTextBox, DesaData desa)
        {
            if (!string.IsNullOrWhiteSpace(keteranganTextBox) || !string.IsNullOrWhiteSpace(suratData.Keterangan))
            {
                string keterangan = !string.IsNullOrWhiteSpace(keteranganTextBox)
                    ? keteranganTextBox
                    : suratData.Keterangan ?? string.Empty;

                badan.Paragraf(keterangan,
                    rata: Rata.Justify,
                    indentKiri: 50,
                    jarakAtas: 15);
            }

            badan.Paragraf(
                    $"Demikian surat ini dibuat dengan sebenarnya untuk dapat dipergunakan sebagaimana mestinya, " +
                    $"begitu pula apa yang menjadi keterangan di dalamnya. Apabila di kemudian hari terdapat " +
                    $"ketidaksesuaian data, kami bersedia bertanggung jawab sesuai ketentuan yang berlaku.",
                rata: Rata.Justify,
                indentKiri: 30,
                jarakAtas: 20);
        }

        private static string NamaDesa(DesaData desa) => desa.NamaDesa ?? "[Nama Desa]";
        private static string NamaKecamatan(DesaData desa) => desa.Kecamatan ?? "[Kecamatan]";

        private static string StatusAtauPlaceholder(string? status) =>
            string.IsNullOrWhiteSpace(status) ? "[Status Perkawinan]" : status.Trim();

        private static string NamaAtauPlaceholder(string? nama, string placeholder) =>
            string.IsNullOrWhiteSpace(nama) ? $"[{placeholder}]" : nama.Trim();

        // =====================================================================
        // Pembantu umum
        // =====================================================================

        private static string GetJudulByNamaJenis(string? namaJenis)
        {
            return namaJenis?.ToUpperInvariant() switch
            {
                SuratConstants.NTCR_N1 => "SURAT PENGANTAR NIKAH",
                SuratConstants.NTCR_N2 => "SURAT KETERANGAN UNTUK NIKAH",
                SuratConstants.NTCR_N3 => "SURAT PERSETUJUAN CALON MEMPELAI",
                SuratConstants.NTCR_N4 => "SURAT KETERANGAN ORANG TUA",
                _ => "SURAT PENGANTAR NIKAH"
            };
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

        private List<(string Label, string? Value)> CreateCalonPriaData(WargaData warga, string formattedTglLahir)
        {
            return new List<(string, string?)>
            {
                ("Nama", warga.Nama ?? "[NAMA PEMOHON]"),
                ("NIK", warga.NIK ?? "[NIK]"),
                ("Tempat Tanggal Lahir", $"{warga.TempatLahir ?? "[Tempat Lahir]"}, {formattedTglLahir}"),
                ("Jenis Kelamin", warga.JenisKelamin ?? "[Jenis Kelamin]"),
                ("Agama", warga.Agama ?? "[Agama]"),
                ("Status Perkawinan", warga.StatusPerkawinan ?? "[Status]"),
                ("Pekerjaan", warga.Pekerjaan ?? "[Pekerjaan]"),
                ("Alamat", FormatAlamatAsal(warga))
            };
        }

        private List<(string Label, string? Value)> CreateCalonWanitaData(NtcrData ntcr, string formattedTglLahir)
        {
            return new List<(string, string?)>
            {
                ("Nama", ntcr.NamaIstri ?? "[NAMA CALON ISTRI]"),
                ("NIK", ntcr.NikIstri ?? "[NIK Calon Istri]"),
                ("Tempat Tanggal Lahir", $"{ntcr.TempatLahirIstri ?? "[Tempat Lahir]"}, {formattedTglLahir}"),
                ("Agama", ntcr.AgamaIstri ?? "[Agama]"),
                ("Status Perkawinan", string.IsNullOrWhiteSpace(ntcr.StatusPerkawinanIstri) ? "[Status]" : ntcr.StatusPerkawinanIstri),
                ("Pekerjaan", ntcr.PekerjaanIstri ?? "[Pekerjaan]"),
                ("Alamat", ntcr.AlamatIstri ?? "[Alamat Calon Istri]")
            };
        }

        private List<(string Label, string? Value)> CreateOrangTuaData(NtcrData ntcr)
        {
            return new List<(string Label, string? Value)>
            {
                ("Ayah Calon Pria", ntcr.NamaAyahCalonSuami ?? "[Nama Ayah Pria]"),
                ("Ibu Calon Pria", ntcr.NamaIbuCalonSuami ?? "[Nama Ibu Pria]"),
                ("Ayah Calon Wanita", ntcr.NamaAyahCalonIstri ?? "[Nama Ayah Wanita]"),
                ("Ibu Calon Wanita", ntcr.NamaIbuCalonIstri ?? "[Nama Ibu Wanita]")
            };
        }

        private bool AdaDataOrangTua(NtcrData ntcr)
        {
            return !string.IsNullOrWhiteSpace(ntcr.NamaAyahCalonSuami) ||
                   !string.IsNullOrWhiteSpace(ntcr.NamaIbuCalonSuami) ||
                   !string.IsNullOrWhiteSpace(ntcr.NamaAyahCalonIstri) ||
                   !string.IsNullOrWhiteSpace(ntcr.NamaIbuCalonIstri);
        }

        private string FormatAlamatAsal(WargaData warga)
        {
            if (warga == null) return "[Alamat]";

            string dusun = !string.IsNullOrWhiteSpace(warga.Dusun) ? warga.Dusun : string.Empty;
            string desa = !string.IsNullOrWhiteSpace(warga.Desa) ? $"Desa {warga.Desa}" : "[Desa]";
            string kecamatan = !string.IsNullOrWhiteSpace(warga.Kecamatan) ? $"Kecamatan {warga.Kecamatan}" : "[Kecamatan]";
            string kabupaten = !string.IsNullOrWhiteSpace(warga.Kabupaten) ? $"Kabupaten {warga.Kabupaten}" : "[Kabupaten]";

            string line1 = string.IsNullOrWhiteSpace(dusun) ? desa : $"{dusun} {desa}";
            return $"{line1}\n {kecamatan} {kabupaten}";
        }
    }
}
