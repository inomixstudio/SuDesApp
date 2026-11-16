using System;
using System.Globalization;
using System.IO;
using Microsoft.Extensions.Logging;
using SudesPdf.Core;
using SudesPdf.Core.Layout;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;

namespace SuDesApp.GeneratorPdf
{
    /// <summary>
    /// Isi formulir pendataan perangkat desa (blanko isian tangan). Formulir ini
    /// memuat seluruh 18 kolom berkas "DATA PERANGKAT" (DESA s.d. BPJS KES) dan
    /// dipakai menarik data perangkat baru sebelum datanya diisi ke aplikasi.
    /// </summary>
    public sealed class FormulirPerangkatData
    {
        public DesaData? Desa { get; init; }

        public string Judul { get; init; } = "FORMULIR PENDATAAN DATA PERANGKAT DESA";

        public DateTime TanggalCetak { get; init; } = DateTime.Now;
    }

    /// <summary>
    /// Membuat PDF formulir pendataan perangkat desa pada halaman portrait:
    /// kop surat desa, judul, petunjuk pengisian singkat, dan 18 isian mengikuti
    /// kolom berkas "DATA PERANGKAT" (dikelompokkan agar mudah diisi), ditutup
    /// blok tanda tangan. Isian yang belum ada ditulis garis titik-titik supaya
    /// bentuk formulirnya tetap terlihat; kolom "Desa" sudah terisi nama desa.
    ///
    /// Hasilnya selalu SATU halaman, pada kertas A4 maupun F4.
    /// </summary>
    public class FormulirPerangkatGenerator
    {
        private readonly AppConfig _config;
        private readonly ILogger<FormulirPerangkatGenerator> _logger;

        static FormulirPerangkatGenerator()
        {
        }

        public FormulirPerangkatGenerator(AppConfig config, ILogger<FormulirPerangkatGenerator> logger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Generator ini tidak mewarisi SuratGeneratorBase: pastikan font buku
            // (Times New Roman) sudah terdaftar juga bila ini PDF pertama sesi.
        }

        public void GeneratePdf(Stream outputStream, FormulirPerangkatData data)
        {
            if (outputStream == null) throw new ArgumentNullException(nameof(outputStream));
            data ??= new FormulirPerangkatData();

            _logger.LogInformation("Membuat PDF {Judul}", data.Judul);

            // Nama wilayah dipakai di kop dan blok tanda tangan; pakai salinan
            // yang sudah bersih supaya "Desa null" tidak pernah tercetak.
            var desa = KopSurat.DesaBersih(data.Desa);

            // Blanko ini sengaja SATU halaman. Susunan normal dipakai lebih dulu;
            // bila belum muat (mis. alamat desa yang panjang membuat kop tambah
            // baris), ulangi dengan jarak yang lebih rapat sampai muat. Yang
            // diubah hanya jarak vertikal — ukuran huruf tetap sama, jadi bentuk
            // blanko dan keterbacaannya tidak berubah di tingkat manapun.
            byte[] hasil = Array.Empty<byte>();
            foreach (float rapat in Kerapatan)
            {
                using var sementara = new MemoryStream();
                new FormulirPerangkatDocument(data, desa, _config, rapat)
                    .GeneratePdf(sementara, out int halaman);

                hasil = sementara.ToArray();
                if (halaman == 1)
                {
                    break;
                }

                _logger.LogDebug(
                    "Formulir pendataan belum muat satu halaman (rapat {Rapat}, halaman {Halaman})",
                    rapat, halaman);
            }

            outputStream.Write(hasil, 0, hasil.Length);
        }

        /// <summary>
        /// Tingkat kerapatan jarak vertikal yang dicoba berurutan: 1 = susunan
        /// normal, makin kecil makin rapat. Tingkat terakhir selalu dipakai bila
        /// yang lain belum muat, supaya pemanggil tetap menerima berkas PDF.
        /// </summary>
        private static readonly float[] Kerapatan = { 1.0f, 0.8f, 0.6f, 0.45f, 0.3f };
    }

    internal class FormulirPerangkatDocument : IDocument
    {
        /// <summary>Garis titik-titik isian kosong, gaya blanko surat desa.</summary>
        private const string GarisIsian = ".......................................................";

        private readonly FormulirPerangkatData _data;
        private readonly DesaData _desa;
        private readonly AppConfig _config;
        private readonly CultureInfo _culture = new("id-ID");

        /// <summary>
        /// Pengali jarak vertikal: 1 = susunan normal, lebih kecil = lebih rapat.
        /// Hanya jarak yang diubah; ukuran huruf dan isi blanko tetap sama.
        /// </summary>
        private readonly float _rapat;

        public FormulirPerangkatDocument(FormulirPerangkatData data, DesaData desa, AppConfig config, float rapat = 1f)
        {
            _data = data;
            _desa = desa;
            _config = config;
            _rapat = rapat;
        }

        /// <summary>Skala jarak vertikal supaya blanko tetap muat satu halaman.</summary>
        private float J(float normal) => normal * _rapat;

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

        /// <summary>Halaman portrait mengikuti Pengaturan Cetak (A4 bawaan atau F4).</summary>
        private static PageSize UkuranHalaman()
        {
            var (lebar, tinggi) = PengaturanCetak.Dimensi(false);
            return new PageSize(lebar, tinggi, Unit.Point);
        }

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(UkuranHalaman());
                page.MarginTop(J(32), Unit.Point);
                page.MarginRight(45, Unit.Point);
                page.MarginBottom(J(24), Unit.Point);
                page.MarginLeft(45, Unit.Point);
                page.DefaultTextStyle(x => x.FontFamily("Times New Roman").FontSize(10));

                page.Header().Element(ComposeHeader);
                page.Content().Element(ComposeContent);
            });
        }

        private void ComposeHeader(IContainer container)
        {
            container.Column(column =>
            {
                column.Item().Element(ComposeKopSurat);
                column.Item().PaddingTop(J(12)).AlignCenter()
                    .Text(_data.Judul).FontSize(14).SemiBold().Underline();
                column.Item().PaddingTop(J(2)).AlignCenter()
                    .Text("Isian mengikuti kolom berkas DATA PERANGKAT (18 kolom)")
                    .FontSize(9).FontColor(Colors.Grey.Darken1);
            });
        }

        /// <summary>
        /// Kop surat desa: logo (bila ada) + baris KopSurat, ditutup garis ganda.
        /// Susunannya milik <see cref="KopSurat.Gambar"/> supaya sama dengan dokumen lain.
        /// </summary>
        private void ComposeKopSurat(IContainer container)
            => KopSurat.Gambar(container, _desa, PengaturanCetak.JalurLogoEfektif(_config.LogoPath));

        private void ComposeContent(IContainer container)
        {
            container.Column(column =>
            {
                column.Item().PaddingTop(J(8)).Element(ComposePetunjuk);
                column.Item().PaddingTop(J(6)).Element(ComposeIsian);
                column.Item().PaddingTop(J(4)).ShowEntire().Element(ComposeTandaTangan);
            });
        }

        /// <summary>Petunjuk pengisian singkat — mengurangi salah isi saat pendataan.</summary>
        private void ComposePetunjuk(IContainer container)
        {
            container.Border(0.75f).BorderColor(Colors.Grey.Darken1)
                .Background(Colors.Grey.Lighten4).Padding(J(8))
                .Column(column =>
                {
                    column.Item().Text("Petunjuk pengisian:").SemiBold().FontSize(9.5f);
                    column.Item().PaddingTop(J(2)).Text(
                        "1. Isi dengan huruf kapital dan tinta hitam; tanggal ditulis dd-mm-yyyy.\n" +
                        "2. Kolom yang belum ada diisi tanda \u201c-\u201d, jangan dikosongkan.\n" +
                        "3. Kolom JABATAN memakai nama jabatan resmi (mis. Sekretaris Desa, Kaur Keuangan, " +
                        "Kasi Pelayanan, Kadus).\n" +
                        "4. Lampirkan salinan KTP/KK, ijazah terakhir, serta rekomendasi/SK bila sudah ada.")
                        .FontSize(8.5f).LineHeight(MathF.Max(1.05f, 1.3f * _rapat));
                });
        }

        /// <summary>
        /// 18 isian dari berkas DATA PERANGKAT, dikelompokkan supaya mudah diisi:
        /// identitas, pendidikan, pengangkatan/rekomendasi, serta keuangan & kesehatan.
        /// </summary>
        private void ComposeIsian(IContainer container)
        {
            container.Table(table =>
            {
                // Label tetap, titik dua lurus, dan nilai selebar sisanya.
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(168);
                    columns.ConstantColumn(10);
                    columns.RelativeColumn();
                });

                Bagian(table, "A. IDENTITAS");
                Baris(table, "DESA", NamaDesa(), null);
                Baris(table, "NAMA PERANGKAT", null, "nama lengkap sesuai KTP");
                Baris(table, "JABATAN", null, "mis. Sekretaris Desa, Kaur Keuangan, Kasi Pelayanan, Kadus");
                Baris(table, "JENIS KELAMIN", null, "Laki-laki / Perempuan");
                Baris(table, "TEMPAT LAHIR", null, null);
                Baris(table, "TGL. LAHIR", null, "dd-mm-yyyy");
                Baris(table, "NIK", null, "16 digit sesuai KTP");
                Baris(table, "AGAMA", null, "Islam / Kristen / Katolik / Hindu / Buddha / Konghucu");
                Baris(table, "NO. TELPON", null, "nomor yang aktif (dapat dihubungi)");

                Bagian(table, "B. PENDIDIKAN");
                Baris(table, "PENDIDIKAN", null, "mis. SD, SMP/MTs, SMA/MA/SMK, D3, S1");
                Baris(table, "TAHUN LULUS", null, "mis. 2015");
                Baris(table, "NO. IJASAH", null, null);

                Bagian(table, "C. PENGANGKATAN & REKOMENDASI");
                Baris(table, "NOMOR REKOMENDASI", null, "nomor rekomendasi dari Kecamatan");
                Baris(table, "TGL. REKOMENDASI", null, "dd-mm-yyyy");
                Baris(table, "NO SK PENGANGKATAN", null, "mis. 141.3/Kep.01/Ds/2020");
                Baris(table, "TGL SK PENGANGKATAN", null, "dd-mm-yyyy");

                Bagian(table, "D. KEUANGAN & KESEHATAN");
                Baris(table, "NOREK BJB", null, "nomor rekening Bank BJB");
                Baris(table, "BPJS KES", null, "nomor kartu BPJS Kesehatan");
            });
        }

        /// <summary>Satu baris isian: label, titik dua, dan nilai (garis titik-titik bila kosong).</summary>
        private void Baris(TableDescriptor table, string label, string? nilai, string? petunjuk)
        {
            table.Cell().Element(c => SelLabel(c, _rapat)).Text(label).FontSize(9.5f);
            table.Cell().Element(c => SelLabel(c, _rapat)).AlignCenter().Text(":").FontSize(9.5f);
            table.Cell().Element(c => SelNilai(c, _rapat)).Column(column =>
            {
                column.Item().Text(string.IsNullOrWhiteSpace(nilai) ? GarisIsian : nilai.Trim())
                    .FontSize(9.5f);
                if (!string.IsNullOrWhiteSpace(petunjuk))
                {
                    column.Item().Text(petunjuk).FontSize(7.5f).FontColor(Colors.Grey.Darken1);
                }
            });
        }

        /// <summary>Judul kelompok isian — menutupi ketiga kolom tabel.</summary>
        private void Bagian(TableDescriptor table, string judul)
        {
            table.Cell().ColumnSpan(3).PaddingTop(J(4)).PaddingBottom(J(1))
                .BorderBottom(0.75f).BorderColor(Colors.Grey.Darken2)
                .Text(judul).SemiBold().FontSize(9.5f);
        }

        // Padding baris sengaja rapat: 18 baris isian + petunjuk + tanda tangan
        // harus muat pada SATU halaman portrait, jangan sampai tanda tangan
        // terlempar ke halaman kedua sendirian.
        private static IContainer SelLabel(IContainer container, float rapat) => container.PaddingVertical(1.5f * rapat);

        private static IContainer SelNilai(IContainer container, float rapat) => container.PaddingVertical(1.5f * rapat);

        private void ComposeTandaTangan(IContainer container)
        {
            string namaDesa = (_desa.NamaDesa ?? string.Empty).Trim();
            string tempatTanggal = namaDesa.Length == 0
                ? TanggalIndo(_data.TanggalCetak)
                : $"{namaDesa}, {TanggalIndo(_data.TanggalCetak)}";
            string kepalaDesa = (_desa.KepalaDesa ?? string.Empty).Trim();

            // Dua blok tanda tangan disusun sebagai tabel berkolom sama: baris
            // yang dipakai bersama membuat garis tanda tangan di kiri (pemberi
            // data) dan di kanan (pejabat desa) otomatis sejajar, tanpa bergantung
            // pada tinggi baris huruf. Seluruh isi blok rata tengah.
            container.PaddingTop(J(2)).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.ConstantColumn(30);
                    columns.RelativeColumn();
                });

                // Baris 1: label pemberi data sejajar dengan tempat & tanggal.
                SelTengah(table, "Yang memberi data,");
                table.Cell();
                SelTengah(table, tempatTanggal);

                // Baris 2: jabatan pejabat di kanan; sel kiri dibiarkan kosong
                // dengan tinggi baris yang sama.
                table.Cell();
                table.Cell();
                SelTengah(table, $"{KopSurat.KataPejabat(_desa)} {namaDesa}".ToUpperInvariant().Trim(), tebal: true);

                // Baris 3: ruang tanda tangan — tinggi yang sama di kedua kolom.
                table.Cell().Height(J(40));
                table.Cell();
                table.Cell().Height(J(40));

                // Baris 4: garis tanda tangan, kini setingkat di kedua belah pihak.
                SelTengah(table, GarisIsian, ukuran: 9f);
                table.Cell();
                SelTengah(
                    table,
                    kepalaDesa.Length == 0 ? GarisIsian : NamaFormatter.ToUpperNama(kepalaDesa),
                    tebal: true,
                    garisBawah: true);

                // Baris 5: keterangan. NIP hanya untuk pejabat ASN kelurahan (Lurah).
                SelTengah(table, "Nama terang & tanda tangan", ukuran: 8f, warna: Colors.Grey.Darken1);
                table.Cell();
                string nipPejabat = KopSurat.IsKelurahan(_desa)
                    ? (_desa.NipKepalaDesa ?? string.Empty).Trim()
                    : string.Empty;
                if (!string.IsNullOrWhiteSpace(nipPejabat))
                {
                    SelTengah(table, $"NIP {nipPejabat}", ukuran: 9f);
                }
                else
                {
                    table.Cell();
                }
            });
        }

        /// <summary>
        /// Sel tabel berisi teks rata tengah — dipakai blok tanda tangan supaya
        /// kolom kiri dan kanan berbagi tinggi baris yang sama.
        /// </summary>
        private static void SelTengah(
            TableDescriptor table,
            string teks,
            float ukuran = 10f,
            bool tebal = false,
            bool garisBawah = false,
            string? warna = null)
        {
            table.Cell().AlignCenter().Text(t =>
            {
                var span = t.Span(teks).FontSize(ukuran);
                if (tebal)
                {
                    span.Bold();
                }
                if (garisBawah)
                {
                    span.Underline();
                }
                if (warna is not null)
                {
                    span.FontColor(warna);
                }
            });
        }

        /// <summary>Nama desa untuk baris "DESA"; kurung siku bila belum diatur.</summary>
        private string NamaDesa()
        {
            string nama = (_desa.NamaDesa ?? string.Empty).Trim();
            return nama.Length == 0 ? "[NAMA DESA]" : nama;
        }

        private string TanggalIndo(DateTime tanggal) => tanggal.ToString("d MMMM yyyy", _culture);
    }
}
