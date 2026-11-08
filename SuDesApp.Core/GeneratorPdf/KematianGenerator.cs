// File: SuDesApp/GeneratorPdf/KematianGenerator.cs
// Surat kematian adalah SALINAN FORMULIR resmi (kode F-2.17), jadi kepalanya bukan
// kop surat: identitas desa berlabel “Pemerintah Desa/Kecamatan/Kabupaten”.
// Seluruh tata letak disusun langsung dengan mesin SudesPdf (tanpa lapisan kompat).
using Microsoft.Extensions.Logging;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Repositories;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;
using SudesPdf.Core.Layout;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SuDesApp.GeneratorPdf
{
    public class KematianGenerator : SuratGeneratorBase
    {
        private const string KODE_FORMULIR_KEMATIAN = "Kode : F-2.17";
        protected override string JudulSurat => "SURAT KETERANGAN KEMATIAN";
        private const string DateFormatDb = "yyyy-MM-dd";
        private const string DateFormatUi = "dd-MM-yyyy";
        private const float LeadingRapat = 14f;
        private const float LeadingData = 16f;
        private const float JarakLabelTitikDua = 110f;

        public KematianGenerator(AppConfig config, FileService fileService, IDesaRepository desaRepository, ISuratRepository suratRepository, SettingsManager settingsManager, ILogger<KematianGenerator> logger, ILoggerFactory loggerFactory)
            : base(config, fileService, desaRepository, suratRepository, settingsManager, logger, loggerFactory)
        {
        }

        protected override void ComposeHalaman(BadanSurat halaman, SuratData suratData, string? keteranganTextBox = null)
        {
            var desa = suratData.Desa ?? new DesaData { NamaDesa = "[Desa]", Kecamatan = "[Kecamatan]", Kabupaten = "[Kabupaten]", KepalaDesa = "[Kepala Desa]" };
            var wargaAlmarhum = suratData.Warga ?? new WargaData();
            var kematianDetail = suratData.Kematian ?? new KematianData();

            // --- Kotak kode formulir di kanan atas (text box ala formulir F-2.17) ---
            // FIX: sebelumnya AlignRight() pada wadah blok TIDAK menggerakkan kotak
            // (HAlign wadah tabel diabaikan LayoutEngine) sehingga kotak F-2.17
            // menempel di kiri. Diganti BARIS TABEL 2 kolom: kolom spacER lebar
            // penuh + kolom kode 89pt — kotak dijamin di tepi kanan seperti lampiran.
            halaman.Blok(container => container.Table(tabel =>
            {
                tabel.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(100);
                    cols.ConstantColumn(LebarKotakKode);
                });
                tabel.Cell().Text(string.Empty);
                tabel.Cell()
                    .Border(0.75f).BorderColor(Colors.Black).Padding(3f)
                    .Text(KODE_FORMULIR_KEMATIAN).FontSize(DEFAULT_FONT_SIZE_KEMATIAN).Bold().AlignCenter();
            }));

            // --- Identitas desa berlabel ---
            halaman.Blok(container => container.Column(baris =>
            {
                BarisLabelNilai(baris, "Pemerintah Desa", desa.NamaDesa?.ToUpper() ?? "[NAMA DESA]");
                BarisLabelNilai(baris, "Kecamatan", desa.Kecamatan?.ToUpper() ?? "[KECAMATAN]");
                BarisLabelNilai(baris, "Kabupaten/Kota", desa.Kabupaten?.ToUpper() ?? "[KABUPATEN]", jarakBawahTerakhir: 15f);
            }));

            // --- Judul + Nomor ---
            string nomorTampil = (string.IsNullOrWhiteSpace(suratData.NomorSurat) || suratData.NomorSurat.Contains("..."))
                ? $"Nomor : 570 / {"".PadRight(17)} / Ds. {suratData.TanggalSurat:yyyy}"
                : $"Nomor : {suratData.NomorSurat}";

            halaman.Blok(container => container.Column(judul =>
            {
                judul.Item().AlignCenter()
                    .Text(JudulSurat)
                    .FontSize(TITLE_FONT_SIZE)
                    .Bold();
                judul.Item().PaddingTop(1f).PaddingBottom(15f).AlignCenter()
                    .Text(nomorTampil)
                    .FontSize(DEFAULT_FONT_SIZE)
                    .LineHeight(LeadingRapat / DEFAULT_FONT_SIZE);
            }));

            // --- Isi surat (sesuai lampiran Word: satu kalimat pembuka,
            //     lalu NIK/Nama/Umur dkk. tanpa titik dua tambahan di label —
            //     nilai setelah "…" dipertahankan pada halaman berikutnya) ---
            halaman.Paragraf("Yang bertanda tangan dibawah ini, menerangkan bahwa :", jarakAtas: 10, jarakBawah: 8);

            string umurAlmarhumDisplay = HitungUmurPadaTanggal(wargaAlmarhum.TanggalLahir!, kematianDetail.TanggalKematian!, "Tanggal Lahir Almarhum/ah", "Tanggal Kematian");

            halaman.Blok(container => TabelData(container, new List<(string Label, string? Value)> {
                ("NIK", wargaAlmarhum.NIK),
                ("Nama Lengkap Jenazah", wargaAlmarhum.Nama == null ? null : NamaFormatter.ToUpperNama(wargaAlmarhum.Nama)),
                ("Jenis Kelamin", wargaAlmarhum.JenisKelamin),
                ("Agama", wargaAlmarhum.Agama),
                ("Umur", umurAlmarhumDisplay),
                ("Pekerjaan", wargaAlmarhum.Pekerjaan),
                ("Alamat", AlamatFormulir(wargaAlmarhum.Dusun, wargaAlmarhum.Desa, wargaAlmarhum.Kecamatan, wargaAlmarhum.RT, wargaAlmarhum.RW))
            }));

            halaman.Paragraf("Telah meninggal dunia pada :", jarakAtas: 8, jarakBawah: 3);

            halaman.Blok(container => TabelData(container, new List<(string Label, string? Value)> {
                ("Hari", kematianDetail.HariKematian),
                ("Tanggal", FormatTanggalUntukTampilan(kematianDetail.TanggalKematian)),
                ("Pukul", string.IsNullOrWhiteSpace(kematianDetail.PukulKematian) ? null : $"{kematianDetail.PukulKematian} WIB"),
                ("Penyebab Kematian", kematianDetail.PenyebabKematian)
            }));

            halaman.Paragraf("Surat keterangan ini dibuat berdasarkan Keterangan Pelapor :", jarakAtas: 12, jarakBawah: 3);

            halaman.Paragraf("PELAPOR", tebal: true, jarakBawah: 2);

            string umurPelaporDisplay = HitungUmurPadaTanggal(kematianDetail.UmurPelapor!, suratData.TanggalSurat.ToString(DateFormatDb), "Tanggal Lahir Pelapor", "Tanggal Surat");

            halaman.Blok(container => TabelData(container, new List<(string Label, string? Value)> {
                ("NIK", kematianDetail.NIKPelapor),
                ("Nama lengkap", kematianDetail.NamaPelapor == null ? null : NamaFormatter.ToUpperNama(kematianDetail.NamaPelapor)),
                ("Agama", kematianDetail.AgamaPelapor),
                ("Umur", umurPelaporDisplay),
                ("Pekerjaan", kematianDetail.PekerjaanPelapor),
                ("Alamat", AlamatPelaporFormulir(kematianDetail.AlamatPelapor))
            }));

            // Baris Hubungan selalu tercetak seperti formulir contoh (F-2.17):
            // bila kosong, titik-titiknya yang tercetak — sama seperti baris
            // lain — supaya kerangka surat tidak kehilangan barisnya saat
            // pratinjau masih kosong atau data lama tanpa hubungan.
            string hubunganTampil = string.IsNullOrWhiteSpace(kematianDetail.HubunganPelapor)
                ? TitikIsian
                : kematianDetail.HubunganPelapor!.Trim();
            halaman.Paragraf($"Hubungan Pelapor dengan yang Meninggal Dunia : {hubunganTampil}",
                jarakAtas: 8, jarakBawah: 3);

            // --- Kaki surat gaya formulir: hanya blok pejabat desa/kelurahan ---
            string tanggalTerformat = FormatTanggalUntukTampilan(suratData.TanggalSurat.ToString(DateFormatDb));
            string namaDesa = desa.NamaDesa ?? "[NAMA DESA]";
            string namaKades = desa.KepalaDesa?.ToUpper() ?? "...............................";

            // Jabatan & NIP mengikuti lembaga: “LURAH …” + NIP untuk kelurahan,
            // “KEPALA DESA …” (non-ASN, tanpa NIP) untuk desa.
            string jabatanKades = $"{KopSurat.KataPejabat(desa)} {namaDesa}".ToUpperInvariant();
            string nipKades = KopSurat.IsKelurahan(desa)
                ? (desa.NipKepalaDesa ?? string.Empty).Trim()
                : string.Empty;

            halaman.Blok(container => container.PaddingTop(30f).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(50);
                    cols.RelativeColumn(50);
                });

                table.Cell().Text(string.Empty);

                table.Cell().Element(sel => sel.Column(kolom =>
                {
                    // Blok penanda tangan berada di kolom kanan; isinya rata tengah
                    // sesuai bentuk tanda tangan surat dinas.
                    kolom.Item().Element(c => SuratRenderer.Teks(c, $"{namaDesa}, {tanggalTerformat}", rata: Rata.Tengah));
                    kolom.Item().Element(c => SuratRenderer.Teks(c, jabatanKades, rata: Rata.Tengah));
                    kolom.Item().Height(45);
                    kolom.Item().Element(c => SuratRenderer.Teks(c, namaKades, tebal: true, rata: Rata.Tengah));
                    if (!string.IsNullOrWhiteSpace(nipKades))
                    {
                        kolom.Item().Element(c => SuratRenderer.Teks(c, $"NIP {nipKades}", ukuran: DEFAULT_FONT_SIZE_KEMATIAN - 1, rata: Rata.Tengah));
                    }
                }));
            }));
        }

        /// <summary>Baris “Pemerintah Desa : …” dengan titik dua yang lurus.</summary>
        private static void BarisLabelNilai(ColumnDescriptor baris, string label, string nilai, float jarakBawahTerakhir = 0f)
        {
            baris.Item().Row(row =>
            {
                row.ConstantItem(JarakLabelTitikDua).Text(label).FontSize(DEFAULT_FONT_SIZE_KEMATIAN).LineHeight(LeadingRapat / DEFAULT_FONT_SIZE_KEMATIAN);
                row.RelativeItem().Text($": {nilai}").FontSize(DEFAULT_FONT_SIZE_KEMATIAN).LineHeight(LeadingRapat / DEFAULT_FONT_SIZE_KEMATIAN);
            });

            if (jarakBawahTerakhir > 0f)
            {
                baris.Item().Height(jarakBawahTerakhir);
            }
        }

        private const float DEFAULT_FONT_SIZE_KEMATIAN = 12f;

        /// <summary>Lebar kotak "Kode : F-2.17" mengikuti formulir (sekitar 3,15 cm).</summary>
        private const float LebarKotakKode = 89f;

        /// <summary>Pengisi bila suatu isian kosong, seperti titik-titik pada formulir.</summary>
        private const string TitikIsian = "...............................";

        /// <summary>Blok data berlabel: label 35%, titik dua 3%, nilai 62% — mulai di margin.</summary>
        private static void TabelData(IContainer container, List<(string Label, string? Value)> items)
        {
            if (!items.Any()) return;

            container.Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(35);
                    cols.RelativeColumn(3);
                    cols.RelativeColumn(62);
                });

                foreach (var item in items)
                {
                    SelData(table.Cell(), item.Label ?? string.Empty);
                    SelData(table.Cell(), ":");
                    SelData(table.Cell(), string.IsNullOrWhiteSpace(item.Value) ? TitikIsian : item.Value);
                }
            });
        }

        private static void SelData(IContainer cell, string teks)
            => cell.Text(teks ?? string.Empty).FontSize(DEFAULT_FONT_SIZE_KEMATIAN).LineHeight(LeadingData / DEFAULT_FONT_SIZE_KEMATIAN);

        /// <summary>
        /// Baris "Alamat" tiga baris mengikuti formulir F-2.17: Dusun (beserta
        /// RT/RW), lalu Desa dan Kecamatan — ketiganya menjorok ke kolom nilai
        /// yang sama seperti pada formulir.
        /// </summary>
        private static string? AlamatFormulir(string? dusun, string? desa, string? kecamatan, string? rt, string? rw)
        {
            bool kosong = string.IsNullOrWhiteSpace(dusun) && string.IsNullOrWhiteSpace(desa) &&
                          string.IsNullOrWhiteSpace(kecamatan) && string.IsNullOrWhiteSpace(rt) &&
                          string.IsNullOrWhiteSpace(rw);
            if (kosong) return null;

            return $"Dusun : {GabungDusunRtRw(dusun, rt, rw)}\n" +
                   $"Desa : {AtauTitik(desa)}\n" +
                   $"Kecamatan : {AtauTitik(kecamatan)}";
        }

        /// <summary>
        /// Alamat pelapor di basis data tersimpan sebagai satu rangkaian
        /// "{dusun} Desa {desa} Kec. {kec} Kab. {kab}" (lih. KematianInputViewModel).
        /// Dipisah kembali supaya tata letaknya sama dengan blok alamat almarhum.
        /// </summary>
        private static string? AlamatPelaporFormulir(string? alamatLengkap)
        {
            if (string.IsNullOrWhiteSpace(alamatLengkap)) return null;

            string[] bagian = alamatLengkap
                .Split(new[] { "Desa", "Kec.", "Kab." }, StringSplitOptions.RemoveEmptyEntries)
                .Select(b => b.Trim())
                .Where(b => b.Length > 0)
                .ToArray();

            if (bagian.Length < 3)
            {
                // Format tak dikenal: tampilkan apa adanya, jangan buang data.
                return alamatLengkap;
            }

            return $"Dusun : {AtauTitik(bagian[0])}\n" +
                   $"Desa : {AtauTitik(bagian[1])}\n" +
                   $"Kecamatan : {AtauTitik(bagian[2])}";
        }

        /// <summary>Baris Dusun pada formulir: nilai dusun diikuti RT/RW bila ada.</summary>
        private static string GabungDusunRtRw(string? dusun, string? rt, string? rw)
        {
            var bagian = new List<string>();
            if (!string.IsNullOrWhiteSpace(dusun)) bagian.Add(dusun.Trim());

            string rtRw = AlamatFormatter.FormatRtRw(rt, rw);
            if (rtRw.Length > 0) bagian.Add(rtRw);

            return bagian.Count == 0 ? TitikIsian : string.Join(" ", bagian);
        }

        private static string AtauTitik(string? nilai) => string.IsNullOrWhiteSpace(nilai) ? TitikIsian : nilai;

        private string FormatTanggalUntukTampilan(string? tanggalInput)
        {
            if (string.IsNullOrWhiteSpace(tanggalInput)) return TitikIsian;
            if (DateTime.TryParseExact(tanggalInput, DateFormatDb, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDb))
                return parsedDb.ToString("dd MMMM yyyy", new CultureInfo("id-ID"));
            if (DateTime.TryParseExact(tanggalInput, DateFormatUi, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedUi))
                return parsedUi.ToString("dd MMMM yyyy", new CultureInfo("id-ID"));
            _logger.LogWarning("FormatTanggalUntukTampilan: Gagal mem-parse tanggal '{TanggalInput}'", tanggalInput);
            return tanggalInput;
        }

        private string HitungUmurPadaTanggal(string tanggalLahirStr, string tanggalReferensiStr, string fieldNameTglLahir, string fieldNameTglReferensi)
        {
            if (string.IsNullOrWhiteSpace(tanggalLahirStr) || string.IsNullOrWhiteSpace(tanggalReferensiStr)) return "...............................";

            DateTime tglLahir;
            DateTime tglReferensi;

            bool tglLahirValid = DateTime.TryParseExact(tanggalLahirStr, DateFormatDb, CultureInfo.InvariantCulture, DateTimeStyles.None, out tglLahir) ||
                                 DateTime.TryParseExact(tanggalLahirStr, DateFormatUi, CultureInfo.InvariantCulture, DateTimeStyles.None, out tglLahir);

            bool tglReferensiValid = DateTime.TryParseExact(tanggalReferensiStr, DateFormatDb, CultureInfo.InvariantCulture, DateTimeStyles.None, out tglReferensi) ||
                                     DateTime.TryParseExact(tanggalReferensiStr, DateFormatUi, CultureInfo.InvariantCulture, DateTimeStyles.None, out tglReferensi);

            if (tglLahirValid && tglReferensiValid)
            {
                if (tglLahir.Date > tglReferensi.Date)
                {
                    _logger.LogWarning("Tanggal lahir ({TglLahir}) lebih besar dari tanggal referensi ({TglReferensi}) untuk perhitungan umur.", tglLahir.ToShortDateString(), tglReferensi.ToShortDateString());
                    return "(Data Tanggal Tidak Valid)";
                }

                int years = tglReferensi.Year - tglLahir.Year;
                if (tglLahir.Date > tglReferensi.Date.AddYears(-years)) years--;
                return $"{years} Tahun";
            }
            _logger.LogWarning("Gagal menghitung umur. {FieldNameTglLahir}: {TglLahirStr}, {FieldNameTglReferensi}: {TglReferensiStr}", fieldNameTglLahir, tanggalLahirStr, fieldNameTglReferensi, tanggalReferensiStr);
            return "...............................";
        }
    }
}
