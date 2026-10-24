using System;
using System.Globalization;
using System.IO;
using System.Linq;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SuDesApp.Data.Models;
using SuDesApp.Services;

namespace SuDesApp.GeneratorPdf
{
    /// <summary>
    /// Generator Laporan IDM: satu dokumen berisi ringkasan penilaian (skor,
    /// status, IKS/IKE/IKL, target pusat, tahun-tahun yang diterbitkan) dan
    /// rincian seluruh indikator apa adanya dari idm.kemendesa.go.id, ditutup
    /// blok tanda tangan — supaya bisa langsung dilampirkan pada laporan desa.
    ///
    /// Kertas LANDSCAPE (A4 mendatar) karena tabel rincian memuat tujuh kolom
    /// (nomor, bidang, indikator, skor, keterangan pusat, kegiatan yang
    /// disarankan, pihak); pada kertas tegak kolom-kolom itu terpotong atau
    /// membungkus menjadi baris yang sangat tinggi. Angka TIDAK dihitung ulang:
    /// semua nilai berasal dari <see cref="LaporanIdmData"/> yang disusun
    /// <c>LaporanIdmBuilder</c> dari data pusat.
    /// </summary>
    public class LaporanIdmGenerator
    {
        static LaporanIdmGenerator()
        {
            QuestPdfLisensi.Pastikan();
        }

        public LaporanIdmGenerator()
        {
            // Laporan bisa menjadi PDF pertama pada sesi ini; daftarkan font buku
            // lebih dulu (idempoten), sama seperti generator laporan lain.
            SuratGeneratorBase.DaftarkanFont();
        }

        public void GenerateLaporanPdf(Stream outputStream, LaporanIdmData data)
        {
            if (outputStream == null) throw new ArgumentNullException(nameof(outputStream));
            if (data == null) throw new ArgumentNullException(nameof(data));

            new LaporanIdmDocument(data).GeneratePdf(outputStream);
        }
    }

    internal class LaporanIdmDocument : IDocument
    {
        private static readonly CultureInfo Id = CultureInfo.GetCultureInfo("id-ID");

        private readonly LaporanIdmData _data;

        public LaporanIdmDocument(LaporanIdmData data) => _data = data;

        public DocumentMetadata GetMetadata() => new()
        {
            Title = _data.Judul + (string.IsNullOrEmpty(_data.NamaDesa) ? string.Empty : " — " + _data.NamaDesa),
            Author = "SuDesApp",
            Subject = "Indeks Desa Membangun (IDM) — data idm.kemendesa.go.id",
            Creator = "SuDesApp",
        };

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(new PageSize(297, 210, Unit.Millimetre));
                page.Margin(1.1f, Unit.Centimetre);
                page.DefaultTextStyle(s => s.FontSize(9).FontFamily("Times New Roman"));

                page.Header().Element(ComposeHeader);
                page.Footer().Element(ComposeFooter);

                page.Content().PaddingTop(8).Column(col =>
                {
                    col.Spacing(9);

                    col.Item().Element(ComposeJudul);

                    if (_data.AdaData)
                    {
                        col.Item().Element(ComposeRingkasan);

                        if (!string.IsNullOrWhiteSpace(_data.CatatanTarget))
                        {
                            col.Item().Text(_data.CatatanTarget).FontSize(8.5f)
                                .FontColor(Colors.Grey.Darken2);
                        }

                        if (_data.Riwayat.Count > 0)
                        {
                            col.Item().Element(ComposeRiwayat);
                        }

                        col.Item().Element(ComposeIndikator);
                        col.Item().PaddingTop(10).Element(ComposeTandaTangan);
                    }
                    else
                    {
                        col.Item().PaddingTop(30).AlignCenter()
                            .Text("Belum ada data IDM yang bisa dicetak.").SemiBold().FontSize(11);
                        col.Item().AlignCenter()
                            .Text("Muat data dari situs pusat terlebih dahulu, lalu cetak kembali halaman ini.")
                            .FontSize(9).FontColor(Colors.Grey.Darken2);
                    }
                });
            });
        }

        // =================================================================
        // Kepala & kaki halaman (berulang di setiap halaman)
        // =================================================================

        private void ComposeHeader(IContainer container)
        {
            container.BorderBottom(0.7f).BorderColor(Colors.Grey.Lighten1)
                .PaddingBottom(3).Row(row =>
                {
                    row.RelativeItem().Text(text =>
                    {
                        text.DefaultTextStyle(s => s.FontSize(8).FontColor(Colors.Grey.Darken2));
                        text.Span("LAPORAN INDEKS DESA MEMBANGUN");
                        if (!string.IsNullOrWhiteSpace(_data.NamaDesa))
                        {
                            text.Span(" | " + _data.NamaDesa.ToUpperInvariant());
                        }

                        if (_data.Tahun > 0)
                        {
                            text.Span(" | TAHUN " + _data.Tahun.ToString(Id));
                        }
                    });

                    row.ConstantItem(170).AlignRight().Text("Sumber angka: idm.kemendesa.go.id")
                        .FontSize(8).FontColor(Colors.Grey.Darken2);
                });
        }

        private void ComposeFooter(IContainer container)
        {
            container.Column(col =>
            {
                col.Item().PaddingTop(3).Row(row =>
                {
                    row.RelativeItem().Text(_data.Sumber)
                        .FontSize(7.6f).FontColor(Colors.Grey.Darken2);

                    // "Hal. 1 dari 4" — laporan yang dilampirkan harus bisa
                    // dipastikan lengkap saat dicetak.
                    row.ConstantItem(150).AlignRight().Text(text =>
                    {
                        text.DefaultTextStyle(s => s.FontSize(7.6f).FontColor(Colors.Grey.Darken2));
                        text.Span("Hal. ");
                        text.CurrentPageNumber();
                        text.Span(" dari ");
                        text.TotalPages();
                    });
                });
            });
        }

        // =================================================================
        // Judul & ringkasan
        // =================================================================

        private void ComposeJudul(IContainer container)
        {
            container.Column(col =>
            {
                col.Item().AlignCenter().Text(_data.Judul).SemiBold().FontSize(14);

                string identitas = string.Join("  |  ", new[]
                {
                    string.IsNullOrWhiteSpace(_data.NamaDesa) ? null : "Desa " + _data.NamaDesa,
                    string.IsNullOrWhiteSpace(_data.Wilayah) ? null : _data.Wilayah,
                }.Where(x => !string.IsNullOrWhiteSpace(x)));

                if (identitas.Length > 0)
                {
                    col.Item().AlignCenter().Text(identitas).SemiBold().FontSize(10);
                }

                if (_data.Tahun > 0)
                {
                    col.Item().AlignCenter()
                        .Text("Tahun Penilaian " + _data.Tahun.ToString(Id))
                        .SemiBold().FontSize(10);
                }

                if (!string.IsNullOrWhiteSpace(_data.KodeDesa))
                {
                    col.Item().AlignCenter()
                        .Text("Kode Desa " + _data.KodeDesa)
                        .FontSize(8.5f).FontColor(Colors.Grey.Darken2);
                }
            });
        }

        private void ComposeRingkasan(IContainer container)
        {
            container.Column(col =>
            {
                col.Item().Text("RINGKASAN PENILAIAN").SemiBold().FontSize(10);

                col.Item().PaddingTop(3).Border(1).BorderColor(Colors.Grey.Lighten2)
                    .Padding(7).Column(kotak =>
                    {
                        kotak.Item().Row(row =>
                        {
                            Kartu(row, "SKOR IDM", _data.Skor, 1.1f);
                            Kartu(row, "STATUS DESA", _data.Status, 1.1f);
                            Kartu(row, "IKS - KETAHANAN SOSIAL", _data.Iks);
                            Kartu(row, "IKE - KETAHANAN EKONOMI", _data.Ike);
                            Kartu(row, "IKL - KETAHANAN EKOLOGI", _data.Ikl);
                        });

                        kotak.Item().PaddingTop(7).Row(row =>
                        {
                            Kartu(row, "TARGET PUSAT", _data.Target);
                            Kartu(row, "SKOR MINIMAL TARGET", _data.SkorMinimal);
                            Kartu(row, "TAHUN PENILAIAN", _data.Tahun > 0 ? _data.Tahun.ToString(Id) : null);
                            Kartu(row, "KODE DESA", _data.KodeDesa);
                        });
                    });

                col.Item().PaddingTop(3).Text(
                        "Seluruh angka pada laporan ini disalin dari idm.kemendesa.go.id; "
                        + "aplikasi tidak menghitung ulang skor maupun status.")
                    .FontSize(7.8f).FontColor(Colors.Grey.Darken2);
            });
        }

        private static void Kartu(RowDescriptor row, string label, string? nilai, float lebar = 1f)
        {
            row.RelativeItem(lebar).Column(col =>
            {
                col.Item().Text(label).FontSize(7.4f).FontColor(Colors.Grey.Darken2);
                col.Item().Text(SelRingkas(nilai)).SemiBold().FontSize(12);
            });
        }

        /// <summary>
        /// Nilai ringkas untuk kartu. Tanda pisah panjang "—" diganti tanda hubung
        /// biasa supaya seluruh laporan memakai satu tanda yang sama dan pasti ada
        /// di font buku.
        /// </summary>
        private static string SelRingkas(string? nilai)
        {
            if (string.IsNullOrWhiteSpace(nilai) || nilai.Trim() == IdmTeks.Kosong) return "-";
            return nilai.Trim();
        }

        private void ComposeRiwayat(IContainer container)
        {
            container.Column(col =>
            {
                col.Item().Text("TAHUN YANG DITERBITKAN PUSAT").SemiBold().FontSize(10);

                col.Item().PaddingTop(3).Width(330).Table(table =>
                {
                    table.ColumnsDefinition(cd =>
                    {
                        cd.ConstantColumn(70);
                        cd.RelativeColumn();
                        cd.RelativeColumn();
                    });

                    table.Header(header =>
                    {
                        HeaderCell(header, "Tahun", alignRight: false);
                        HeaderCell(header, "Skor IDM", alignRight: true);
                        HeaderCell(header, "Status", alignRight: false);
                    });

                    foreach (var tahun in _data.Riwayat)
                    {
                        BodyCell(table, tahun.Tahun.ToString(Id), alignRight: false);
                        BodyCell(table, tahun.Skor, alignRight: true);
                        BodyCell(table, tahun.Status, alignRight: false);
                    }
                });
            });
        }

        // =================================================================
        // Rincian indikator
        // =================================================================

        private void ComposeIndikator(IContainer container)
        {
            container.Column(col =>
            {
                col.Item().Text("RINCIAN INDIKATOR - DATA PUSAT").SemiBold().FontSize(10);
                col.Item().PaddingTop(2).Text(
                        "Nomor indikator dimulai ulang setiap bidang (IKS, IKE, IKL); baris tebal adalah subtotal resmi. "
                        + "Semua kolom disalin apa adanya dari idm.kemendesa.go.id.")
                    .FontSize(7.8f).FontColor(Colors.Grey.Darken2);

                if (_data.Indikator.Count == 0)
                {
                    col.Item().PaddingTop(6).Text(
                            "Situs pusat tidak mengirim rincian indikator untuk desa ini; "
                            + "hanya ringkasan penilaian yang bisa dicetak.")
                        .Italic().FontSize(8.5f);
                    return;
                }

                col.Item().PaddingTop(4).Table(table =>
                {
                    table.ColumnsDefinition(cd =>
                    {
                        cd.ConstantColumn(26);   // No
                        cd.ConstantColumn(36);   // Bidang
                        cd.RelativeColumn(2.1f); // Indikator
                        cd.ConstantColumn(42);   // Skor
                        cd.RelativeColumn(1.7f); // Keterangan pusat
                        cd.RelativeColumn(1.45f);// Kegiatan disarankan
                        cd.ConstantColumn(92);   // Pihak
                    });

                    table.Header(header =>
                    {
                        HeaderCell(header, "No", alignRight: false, kecil: true);
                        HeaderCell(header, "Bidang", alignRight: false, kecil: true);
                        HeaderCell(header, "Indikator", alignRight: false, kecil: true);
                        HeaderCell(header, "Skor", alignRight: true, kecil: true);
                        HeaderCell(header, "Keterangan pusat", alignRight: false, kecil: true);
                        HeaderCell(header, "Kegiatan yang disarankan", alignRight: false, kecil: true);
                        HeaderCell(header, "Pihak (pusat/prov/kab/desa)", alignRight: false, kecil: true);
                    });

                    foreach (var baris in _data.Indikator)
                    {
                        BodyCell(table, baris.Nomor, alignRight: false, subtotal: baris.Subtotal);
                        BodyCell(table, baris.Bidang, alignRight: false, subtotal: baris.Subtotal);
                        BodyCell(table, baris.Indikator, alignRight: false, subtotal: baris.Subtotal);
                        BodyCell(table, baris.Skor, alignRight: true, subtotal: baris.Subtotal);
                        BodyCell(table, baris.Keterangan, alignRight: false, subtotal: baris.Subtotal);
                        BodyCell(table, baris.Kegiatan, alignRight: false, subtotal: baris.Subtotal);
                        BodyCell(table, baris.Pihak, alignRight: false, subtotal: baris.Subtotal, kecil: true);
                    }
                });
            });
        }

        // =================================================================
        // Blok tanda tangan
        // =================================================================

        private void ComposeTandaTangan(IContainer container)
        {
            string tempatTanggal =
                $"{T(_data.Desa.NamaDesa)}, {_data.TanggalCetak.ToString("d MMMM yyyy", Id)}";

            container.Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().AlignCenter().Text("Mengetahui,").FontSize(9.5f);
                    c.Item().AlignCenter()
                        .Text("KEPALA DESA " + T(_data.Desa.NamaDesa)).SemiBold().FontSize(9.5f);
                    c.Item().Height(38);
                    c.Item().AlignCenter().Text(Atas(_data.Desa.KepalaDesa)).Underline().SemiBold();
                });

                row.ConstantItem(190).Column(c =>
                {
                    c.Item().AlignCenter().Text(tempatTanggal).FontSize(9.5f);
                    c.Item().AlignCenter().Text("SEKRETARIS DESA").SemiBold().FontSize(9.5f);
                    c.Item().Height(38);
                    c.Item().AlignCenter().Text(Atas(_data.Desa.SekretarisDesa)).Underline().SemiBold();
                });
            });
        }

        // =================================================================
        // Pembantu sel tabel
        // =================================================================

        private static void HeaderCell(TableCellDescriptor barisKepala, string judul, bool alignRight, bool kecil = false)
        {
            barisKepala.Cell().Element(c => c.Background(Colors.Grey.Lighten3)
                    .BorderBottom(1).BorderColor(Colors.Grey.Darken1)
                    .PaddingVertical(3).PaddingHorizontal(2.5f))
                .Element(c => alignRight ? c.AlignRight() : c.AlignLeft())
                .Text(judul).SemiBold().FontSize(kecil ? 7.2f : 8);
        }

        private static void BodyCell(
            TableDescriptor table, string? isi, bool alignRight, bool subtotal = false, bool kecil = false)
        {
            var teks = table.Cell().Element(c =>
                {
                    var sel = c.BorderBottom(0.6f).BorderColor(Colors.Grey.Lighten2)
                        .PaddingVertical(2.4f).PaddingHorizontal(2.5f);

                    if (subtotal)
                    {
                        sel = sel.Background(Colors.Grey.Lighten4);
                    }

                    return sel;
                })
                .Element(c => alignRight ? c.AlignRight() : c.AlignLeft())
                .Text(BersihSel(isi))
                .FontSize(kecil ? 7 : 7.6f);

            if (subtotal) teks.SemiBold();
        }

        /// <summary>
        /// Isi sel tabel: kosong atau tanda "—" dari layer tampilan menjadi tanda
        /// hubung biasa, sehingga laporan dicetak dengan satu gaya tanda yang sama.
        /// </summary>
        private static string BersihSel(string? isi)
        {
            if (string.IsNullOrWhiteSpace(isi)) return "-";
            string teks = isi.Trim();
            return teks == IdmTeks.Kosong ? "-" : teks;
        }

        /// <summary>Nama wilayah kosong menjadi titik-titik agar kop tetap rapi.</summary>
        private static string T(string? nilai)
        {
            string teks = (nilai ?? string.Empty).Trim().ToUpperInvariant();
            return teks.Length == 0 ? "....................." : teks;
        }

        /// <summary>Nama pejabat kosong menjadi garis bawah, sama seperti generator lain.</summary>
        private static string Atas(string? nama)
        {
            string teks = (nama ?? string.Empty).Trim().ToUpperInvariant();
            return teks.Length == 0 ? "....................." : teks;
        }
    }
}
