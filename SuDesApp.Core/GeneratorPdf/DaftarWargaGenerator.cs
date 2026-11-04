using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Extensions.Logging;
using SudesPdf.Core.Layout;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;

namespace SuDesApp.GeneratorPdf
{
    /// <summary>
    /// Satu baris pada cetakan Daftar Data Warga — kolomnya mengikuti tabel di
    /// layar halaman Data Warga (No, NIK, Nama, No KK, L/P, TTL, Umur, Alamat,
    /// HP, Status); nilai kosong dicetak "-".
    /// </summary>
    public sealed class DaftarWargaBaris
    {
        public int Nomor { get; init; }
        public string Nik { get; init; } = "-";
        public string Nama { get; init; } = "-";
        public string NoKk { get; init; } = "-";
        public string JenisKelamin { get; init; } = "-";
        public string Ttl { get; init; } = "-";
        public string Umur { get; init; } = "-";
        public string Alamat { get; init; } = "-";
        public string NomorHp { get; init; } = "-";
        public string Status { get; init; } = "-";
    }

    /// <summary>Isi cetakan daftar warga (tombol Cetak PDF halaman Data Warga).</summary>
    public sealed class DaftarWargaData
    {
        public DesaData? Desa { get; init; }

        public string Judul { get; init; } = "DAFTAR DATA WARGA";

        public DateTime TanggalCetak { get; init; } = DateTime.Now;

        /// <summary>
        /// Keterangan filter yang menghasilkan daftar ini (kata kunci, status,
        /// wilayah) supaya pembaca tahu cakupan datanya; "Seluruh warga" bila
        /// tanpa saringan.
        /// </summary>
        public string FilterTeks { get; init; } = "Seluruh warga";

        /// <summary>Baris tabel, seluruh warga yang cocok dengan filter.</summary>
        public IReadOnlyList<DaftarWargaBaris> Baris { get; init; } = Array.Empty<DaftarWargaBaris>();

        /// <summary>Jumlah warga yang tercetak.</summary>
        public int JumlahWarga { get; init; }
    }

    /// <summary>
    /// Membuat PDF Daftar Data Warga: kop surat desa, judul, ringkasan tanggal
    /// cetak + cakupan filter, tabel 10 kolom pada halaman landscape (NIK dan
    /// No KK 16 digit butuh ruang), serta blok tanda tangan Kepala Desa.
    /// Daftar dicetak lengkap mengikuti filter yang sedang aktif — sama dengan
    /// ekspor — bukan hanya halaman tabel yang sedang tampil.
    /// </summary>
    public class DaftarWargaGenerator
    {
        private readonly AppConfig _config;
        private readonly ILogger<DaftarWargaGenerator> _logger;

        public DaftarWargaGenerator(AppConfig config, ILogger<DaftarWargaGenerator> logger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Seperti StrukturPerangkatGenerator, kelas ini tidak mewarisi
            // SuratGeneratorBase: mesin memakai metrik font bawaan PDF, jadi
            // tidak ada pendaftaran font yang perlu dilakukan di sini.
        }

        public void GeneratePdf(Stream outputStream, DaftarWargaData data)
        {
            if (outputStream == null) throw new ArgumentNullException(nameof(outputStream));
            data ??= new DaftarWargaData();

            _logger.LogInformation(
                "Membuat PDF Daftar Data Warga judul={Judul} baris={Jumlah} filter={Filter}",
                data.Judul, data.Baris.Count, data.FilterTeks);

            // Nama wilayah dipakai di kop dan blok tanda tangan; pakai salinan
            // yang sudah bersih supaya "Desa null" tidak pernah tercetak.
            var desa = KopSurat.DesaBersih(data.Desa);
            new DaftarWargaDocument(data, desa, _config).GeneratePdf(outputStream);
        }
    }

    internal class DaftarWargaDocument : IDocument
    {
        /// <summary>Skala tiga baris judul kop: ruang vertikal landscape lebih pendek.</summary>
        private const float SkalaJudulKopLandscape = 0.82f;

        /// <summary>Ukuran teks tabel; 10 kolom muat nyaman sedikit lebih besar dari tabel 18 kolom perangkat.</summary>
        private const float UkuranTabel = 8f;

        private readonly DaftarWargaData _data;
        private readonly DesaData _desa;
        private readonly AppConfig _config;
        private readonly CultureInfo _culture = new("id-ID");

        public DaftarWargaDocument(DaftarWargaData data, DesaData desa, AppConfig config)
        {
            _data = data;
            _desa = desa;
            _config = config;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

        /// <summary>Halaman landscape mengikuti Pengaturan Cetak (A4 bawaan atau F4),
        /// sebab kolom NIK dan alamat butuh lebar penuh.</summary>
        private static PageSize UkuranHalaman()
        {
            var (lebar, tinggi) = PengaturanCetak.Dimensi(true);
            return new PageSize(lebar, tinggi, Unit.Point);
        }

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(UkuranHalaman());
                page.MarginTop(32, Unit.Point);
                page.MarginRight(45, Unit.Point);
                page.MarginBottom(18, Unit.Point);
                page.MarginLeft(45, Unit.Point);
                page.DefaultTextStyle(x => x.FontFamily("Times New Roman").FontSize(10));

                // Kop + judul di header supaya tetap tampil di tiap halaman.
                page.Header().Element(ComposeHeader);
                page.Content().Element(ComposeContent);
            });
        }

        private void ComposeHeader(IContainer container)
        {
            container.Column(column =>
            {
                column.Item().Element(ComposeKopSurat);
                column.Item().PaddingTop(12).Element(ComposeJudul);
                column.Item().PaddingTop(4).Element(ComposeInfoCetak);
            });
        }

        /// <summary>Kop surat desa: logo (bila ada) + baris KopSurat, ditutup garis ganda.</summary>
        private void ComposeKopSurat(IContainer container)
        {
            float skalaJudul = SkalaJudulKopLandscape;
            KopSurat.Gambar(container, _desa, PengaturanCetak.JalurLogoEfektif(_config.LogoPath), skalaJudul);
        }

        private void ComposeJudul(IContainer container) =>
            container.AlignCenter().Text(_data.Judul).FontSize(15).SemiBold().Underline();

        /// <summary>Tanggal cetak dan cakupan filter — pembaca perlu tahu daftar ini diambil kapan dan dari data mana.</summary>
        private void ComposeInfoCetak(IContainer container)
        {
            string tanggal = _data.TanggalCetak.ToString("dddd, d MMMM yyyy", _culture);
            string cakupan = string.IsNullOrWhiteSpace(_data.FilterTeks) || _data.FilterTeks == "Seluruh warga"
                ? "seluruh warga"
                : $"saringan: {_data.FilterTeks}";

            container.AlignCenter()
                .Text($"Tanggal cetak: {tanggal} — {_data.JumlahWarga} warga ({cakupan})")
                .FontSize(9.5f)
                .FontColor(Colors.Grey.Darken1);
        }

        private void ComposeContent(IContainer container)
        {
            container.Column(column =>
            {
                column.Item().PaddingTop(10);

                if (_data.Baris.Count == 0)
                {
                    // Daftar benar-benar kosong: beri keterangan, jangan mencetak
                    // tabel tanpa satu pun baris.
                    column.Item().AlignCenter()
                        .Text("Belum ada data warga yang sesuai filter untuk dicetak.")
                        .SemiBold();
                    return;
                }

                column.Item().Table(ComposeTable);

                // ShowEntire: blok tanda tangan tidak boleh terbelah antar halaman.
                column.Item().PaddingTop(14).ShowEntire().Element(ComposeTandaTangan);
            });
        }

        private void ComposeTable(TableDescriptor table)
        {
            // Lebar kolom mengikuti tabel di layar: NIK dan No KK 16 digit serta
            // alamat butuh ruang relatif, sisanya kolom sempit berukuran tetap.
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(26);    // No
                columns.RelativeColumn(2.3f);  // NIK (16 digit, jangan terbelah)
                columns.RelativeColumn(2.2f);  // Nama
                columns.RelativeColumn(2.1f);  // No. KK (16 digit)
                columns.ConstantColumn(32);    // L/P
                columns.RelativeColumn(2.1f);  // Tempat, Tgl Lahir
                columns.ConstantColumn(38);    // Umur
                columns.RelativeColumn(3.0f);  // Alamat
                columns.ConstantColumn(68);    // No. HP
                columns.ConstantColumn(58);    // Status
            });

            table.Header(header =>
            {
                string[] judul =
                {
                    "No", "NIK", "NAMA", "NO. KK", "L/P",
                    "TEMPAT, TGL LAHIR", "UMUR", "ALAMAT", "NO. HP", "STATUS"
                };
                foreach (var teks in judul)
                {
                    header.Cell().Element(HeaderCell).AlignCenter().Text(teks)
                        .SemiBold().FontSize(UkuranTabel);
                }
            });

            foreach (var baris in _data.Baris)
            {
                table.Cell().Element(BodyCell).AlignCenter().Text(baris.Nomor.ToString(_culture)).FontSize(UkuranTabel);
                table.Cell().Element(BodyCell).AlignLeft().Text(baris.Nik).FontSize(UkuranTabel);
                table.Cell().Element(BodyCell).AlignLeft().Text(baris.Nama).FontSize(UkuranTabel);
                table.Cell().Element(BodyCell).AlignLeft().Text(baris.NoKk).FontSize(UkuranTabel);
                table.Cell().Element(BodyCell).AlignCenter().Text(baris.JenisKelamin).FontSize(UkuranTabel);
                table.Cell().Element(BodyCell).AlignLeft().Text(baris.Ttl).FontSize(UkuranTabel);
                table.Cell().Element(BodyCell).AlignCenter().Text(baris.Umur).FontSize(UkuranTabel);
                table.Cell().Element(BodyCell).AlignLeft().Text(baris.Alamat).FontSize(UkuranTabel);
                table.Cell().Element(BodyCell).AlignLeft().Text(baris.NomorHp).FontSize(UkuranTabel);
                table.Cell().Element(BodyCell).AlignCenter().Text(baris.Status).FontSize(UkuranTabel);
            }
        }

        private static IContainer HeaderCell(IContainer container) => container
            .Border(1)
            .BorderColor(Colors.Black)
            .Background(Colors.Grey.Lighten4)
            .PaddingVertical(3)
            .PaddingHorizontal(2);

        private static IContainer BodyCell(IContainer container) => container
            .Border(1)
            .BorderColor(Colors.Black)
            .PaddingVertical(2)
            .PaddingHorizontal(2)
            .MinHeight(18);

        /// <summary>Blok tanda tangan Kepala Desa di bagian bawah dokumen.</summary>
        private void ComposeTandaTangan(IContainer container)
        {
            string namaDesa = (_desa.NamaDesa ?? string.Empty).Trim();
            string tempatTanggal = namaDesa.Length == 0
                ? TanggalIndo(_data.TanggalCetak)
                : $"{namaDesa}, {TanggalIndo(_data.TanggalCetak)}";

            string kepalaDesa = (_desa.KepalaDesa ?? string.Empty).Trim();
            if (kepalaDesa.Length == 0) kepalaDesa = "...........................";

            // Kolom penanda tangan menempel di sisi kanan halaman; isi bloknya
            // rata tengah, sesuai bentuk baku tanda tangan surat dinas. Dipakai
            // baris (spacer + lebar tetap), bukan Width() pada anak kolom: pada
            // mesin tata letak SudesPdf lebar tetap kolom hanya dipakai saat
            // mengukur, sehingga blok ini dulu tercetak di tengah halaman.
            container.Row(row =>
            {
                row.RelativeItem();
                row.ConstantItem(260).Column(column =>
                {
                    column.Item().AlignCenter().Text(tempatTanggal);
                    column.Item().AlignCenter()
                        .Text($"{KopSurat.KataPejabat(_desa)} {namaDesa}".ToUpperInvariant().Trim())
                        .SemiBold();
                    column.Item().Height(40);
                    column.Item().AlignCenter().Text(kepalaDesa.ToUpperInvariant()).Bold().Underline();
                    string nipPejabat = KopSurat.IsKelurahan(_desa)
                        ? (_desa.NipKepalaDesa ?? string.Empty).Trim()
                        : string.Empty;
                    if (!string.IsNullOrWhiteSpace(nipPejabat))
                    {
                        column.Item().AlignCenter().Text($"NIP {nipPejabat}");
                    }
                });
            });
        }

        private string TanggalIndo(DateTime tanggal) => tanggal.ToString("d MMMM yyyy", _culture);
    }
}
