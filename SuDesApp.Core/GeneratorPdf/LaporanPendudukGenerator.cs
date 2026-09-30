using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SuDesApp.Data.Models;

namespace SuDesApp.GeneratorPdf
{
    /// <summary>
    /// Generator Laporan Penduduk: satu dokumen berisi beberapa tabel demografi
    /// (kelompok usia, pendidikan, agama, status perkawinan, RT, status tinggal,
    /// Kartu Keluarga) preceded by ringkasan.
    ///
    /// Berbeda dari surat dan dari <see cref="KeputusanPeraturanGenerator"/>, laporan
    /// ini memakai layout buku tegak (potret) dengan kop sederhana dua baris dan blok
    /// penandatangan di akhir. Setiap tabel dimulai ulang di halaman baru supaya
    /// judul tabel tidak pernah menyatu dengan tabel sebelumnya, dan nomor halaman
    /// memakai format "Hal. 1 dari N" seperti laporan resmi.
    /// </summary>
    public class LaporanPendudukGenerator
    {
        private readonly DesaData _desaInfo;

        static LaporanPendudukGenerator()
        {
            QuestPdfLisensi.Pastikan();
        }

        public LaporanPendudukGenerator(DesaData? desaInfo)
        {
            // Laporan bisa menjadi PDF pertama pada sesi ini, jadi pastikan font
            // buku (Times New Roman) terdaftar lebih dulu — sama seperti
            // KeputusanPeraturanGenerator.
            SuratGeneratorBase.DaftarkanFont();

            // Kop & tanda tangan memakai nama wilayah; pakai salinan yang bersih
            // supaya "Desa null" tidak pernah tercetak.
            _desaInfo = KopSurat.DesaBersih(desaInfo);
        }

        public void GenerateLaporanPdf(Stream outputStream, LaporanPendudukData data)
        {
            if (outputStream == null) throw new ArgumentNullException(nameof(outputStream));
            if (data == null) throw new ArgumentNullException(nameof(data));

            if (!data.AdaIsi)
            {
                new LaporanPendudukDocument(data, _desaInfo).GenerateKosong(outputStream);
                return;
            }

            new LaporanPendudukDocument(data, _desaInfo).GeneratePdf(outputStream);
        }
    }

    internal class LaporanPendudukDocument : IDocument
    {
        private readonly LaporanPendudukData _data;
        private readonly DesaData _desa;

        public LaporanPendudukDocument(LaporanPendudukData data, DesaData desa)
        {
            _data = data;
            _desa = desa;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

        /// <summary>
        /// Susun halaman: satu halaman ringkasan, lalu satu halaman per tabel
        /// supaya judul tabel tidak pernah menempel di kaki tabel sebelumnya.
        /// Blok tanda tangan hanya ditambahkan pada halaman terakhir.
        /// </summary>
        public void Compose(IDocumentContainer container)
        {
            var tabel = _data.TabelTerisi;

            if (tabel.Count == 0)
            {
                // Operator mematikan seluruh tabel ("Hapus semua"): laporan tetap
                // terbit sebagai satu halaman ringkasan bertanda tangan, bukan
                // melempar galat indeks kosong.
                container.Page(page => ComposeHalaman(page, ringkasan: true, tabel: null, tandaTangan: true));
                return;
            }

            // Satu halaman per tabel: pemisahan halaman membuat isi laporan
            // mudah dirujuk ("Pendidikan ada di halaman 3") dan tidak mungkin
            // terpotong di tengah tabel.
            container.Page(page => ComposeHalaman(page, ringkasan: true, tabel[0], tandaTangan: false));

            for (int i = 0; i < tabel.Count; i++)
            {
                bool terakhir = i == tabel.Count - 1;
                var isi = tabel[i];
                container.Page(page => ComposeHalaman(page, ringkasan: false, isi, tandaTangan: terakhir));
            }
        }

        /// <summary>
        /// Laporan tanpa satu pun warga. Tetap menghasilkan satu halaman berisi
        /// keterangan, bukan PDF kosong — petugas harus melihat why laporan ini
        /// tidak berisi apa-apa.
        /// </summary>
        public void GenerateKosong(Stream stream)
        {
            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(new PageSize(215, 330, Unit.Millimetre));
                    page.Margin(1.5f, Unit.Centimetre);
                    page.DefaultTextStyle(s => s.FontSize(11).FontFamily("Times New Roman"));

                    page.Header().Element(ComposeHeader);
                    page.Content().Column(col =>
                    {
                        col.Item().PaddingTop(2, Unit.Centimetre).AlignCenter()
                            .Text("Belum ada data warga yang dapat dilaporkan.").SemiBold();
                        col.Item().PaddingTop(0.4f, Unit.Centimetre).AlignCenter()
                            .Text("Tambahkan data warga terlebih dahulu, atau periksa kembali filter yang dipakai.");
                    });
                    page.Footer().Element(ComposeFooter);
                });
            }).GeneratePdf(stream);
        }

        private void ComposeHalaman(
            PageDescriptor page,
            bool ringkasan,
            LaporanTabel? tabel,
            bool tandaTangan)
        {
            page.Size(new PageSize(215, 330, Unit.Millimetre));
            page.Margin(1.2f, Unit.Centimetre);
            page.DefaultTextStyle(s => s.FontSize(11).FontFamily("Times New Roman"));

            page.Header().Element(ComposeHeader);

            page.Content().Column(col =>
            {
                col.Spacing(8);

                if (ringkasan)
                {
                    col.Item().Element(ComposeKartuAngka);
                }

                if (tabel != null)
                {
                    col.Item().PaddingTop(ringkasan ? 4f : 0f).Element(c => ComposeTabel(c, tabel));
                }

                if (tandaTangan)
                {
                    // Spasi 10pt + blok tanda tangan (~40pt ruang kosong) harus
                    // muat di halaman terakhir. Kalau tabel terakhir sangat
                    // tinggi, QuestPDF melempar DocumentLayoutException — itu
                    // lebih baik daripada blok tanda tangan terpotong.
                    col.Item().PaddingTop(14f).Element(ComposeTandaTangan);
                }
            });

            page.Footer().Element(ComposeFooter);
        }

        private void ComposeHeader(IContainer container)
        {
            container.Column(col =>
            {
                col.Spacing(2);
                col.Item().AlignCenter().Text(_data.Judul).SemiBold().FontSize(14);
                col.Item().AlignCenter()
                    .Text($"DESA {T(_desa.NamaDesa)} KECAMATAN {T(_desa.Kecamatan)} KABUPATEN {T(_desa.Kabupaten)}")
                    .SemiBold().FontSize(11);
                col.Item().AlignCenter().Text(_data.Periode).SemiBold().FontSize(11);

                if (!string.IsNullOrWhiteSpace(_data.KeteranganFilter))
                {
                    col.Item().AlignCenter().Text($"Cakupan: {_data.KeteranganFilter}").FontSize(9.5f)
                        .FontColor(Colors.Grey.Darken1);
                }
            });
        }

        /// <summary>
        /// Angka kunci di atas laporan: penduduk, laki-laki, perempuan, Kepala
        /// Keluarga (laki-laki/wanita), dan jumlah Kartu Keluarga. Dicetak sebagai
        /// kotak karena ini yang pertama dicari pembaca laporan, sebelum tabel
        /// mana pun.
        /// </summary>
        private void ComposeKartuAngka(IContainer container)
        {
            var r = _data.Ringkasan;
            int penduduk = r.TotalAktif + r.TotalBaru;

            var kartu = new (string Label, string Nilai)[]
            {
                ("Penduduk", Angka(penduduk)),
                ("Laki-laki", Angka(r.LakiLaki)),
                ("Perempuan", Angka(r.Perempuan)),
                ("Kepala Keluarga", Angka(r.JumlahKepalaKeluarga)),
                ("KK Laki-laki", Angka(r.KepalaKeluargaLakiLaki)),
                ("KK Wanita", Angka(r.KepalaKeluargaPerempuan)),
                ("Kartu Keluarga", Angka(r.JumlahKartuKeluarga))
            };

            container.Border(1).BorderColor(Colors.Grey.Lighten2).Padding(6f).Row(row =>
            {
                foreach (var (label, nilai) in kartu)
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().AlignCenter().Text(label).FontSize(9f)
                            .FontColor(Colors.Grey.Darken2);
                        c.Item().AlignCenter().Text(nilai).SemiBold().FontSize(13);
                    });
                }
            });
        }

        private void ComposeTabel(IContainer container, LaporanTabel tabel)
        {
            container.Column(col =>
            {
                col.Item().Text(tabel.Judul).SemiBold().FontSize(11.5f);
                col.Item().PaddingTop(3f);

                col.Item().Table(table =>
                {
                    // Kolom pertama adaptsif; sisanya sempit karena isinya angka
                    // ("1.234 (12,3 %)").
                    table.ColumnsDefinition(cd =>
                    {
                        cd.RelativeColumn();
                        for (int i = 0; i < tabel.JudulKolom.Count; i++) cd.RelativeColumn(1.1f);
                    });

                    table.Header(header =>
                    {
                        IContainer Gaya(IContainer c) =>
                            c.Background(Colors.Grey.Lighten3).BorderBottom(1)
                             .BorderColor(Colors.Grey.Darken1).PaddingVertical(4).PaddingHorizontal(3);

                        header.Cell().Element(Gaya).AlignLeft().Text(tabel.LabelKolom).SemiBold();
                        foreach (var judul in tabel.JudulKolom)
                        {
                            header.Cell().Element(Gaya).AlignRight().Text(judul).SemiBold();
                        }
                    });

                    foreach (var baris in tabel.Baris)
                    {
                        TambahBaris(table, baris, tabel.JudulKolom.Count, tebal: false);
                    }

                    if (tabel.BarisTotal is { Length: > 0 })
                    {
                        TambahBaris(table, tabel.BarisTotal, tabel.JudulKolom.Count, tebal: true);
                    }
                });

                if (!string.IsNullOrWhiteSpace(tabel.Catatan))
                {
                    col.Item().PaddingTop(3f).Text(tabel.Catatan!).FontSize(8.5f)
                        .FontColor(Colors.Grey.Darken2);
                }
            });
        }

        /// <summary>
        /// Satu baris tabel. <paramref name="jumlahKolomAngka"/> harus sama dengan
        /// <c>tabel.JudulKolom.Count</c>; sel yang tidak punya isi tetap dicetak
        /// kosong supaya kolom tidak bergeser antar baris.
        /// </summary>
        private static void TambahBaris(
            TableDescriptor table, IReadOnlyList<string> baris, int jumlahKolomAngka, bool tebal)
        {
            static IContainer Gaya(IContainer c, bool tebal) =>
                tebal
                    ? c.BorderBottom(1).BorderColor(Colors.Grey.Darken1)
                        .PaddingVertical(3).PaddingHorizontal(3)
                        .Background(Colors.Grey.Lighten4)
                    : c.BorderBottom(1).BorderColor(Colors.Grey.Lighten2)
                        .PaddingVertical(3).PaddingHorizontal(3);

            static IContainer Sel(IContainer c, bool tebal) => tebal
                ? c.DefaultTextStyle(s => s.SemiBold())
                : c;

            table.Cell().Element(c => Sel(Gaya(c, tebal), tebal)).AlignLeft()
                  .Text(baris.Count > 0 ? baris[0] : string.Empty);

            for (int i = 0; i < jumlahKolomAngka; i++)
            {
                string teks = i + 1 < baris.Count ? baris[i + 1] : string.Empty;
                table.Cell().Element(c => Sel(Gaya(c, tebal), tebal)).AlignRight().Text(teks);
            }
        }

        /// <summary>
        /// Kaki halaman. Hanya memuat ringkasan cakupan dan nomor halaman.
        /// Blok tanda tangan TIDAK boleh ada di sini: footer diulang pada
        /// setiap halaman, sehingga blok bertanda tangan basah akan tercetak
        /// ulang di seluruh halaman dan dokumen terlihat ditandatangani
        /// beberapa kali. Blok tanda tangan dicetak sekali di akhir dokumen
        /// (lihat <see cref="ComposeTandaTangan"/>).
        /// </summary>
        private void ComposeFooter(IContainer container)
        {
            var r = _data.Ringkasan;
            int penduduk = r.TotalAktif + r.TotalBaru;

            container.Column(col =>
            {
                col.Item().PaddingTop(2f).Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().AlignLeft()
                            .Text($"Jumlah penduduk: {Angka(penduduk)} orang  |  " +
                                  $"Warga tercatat: {Angka(r.TotalSeluruh)} orang  |  " +
                                  $"{_data.TabelTerisi.Count} tabel")
                            .FontSize(8.5f).FontColor(Colors.Grey.Darken2);
                    });

                    row.ConstantItem(210f).Column(c =>
                    {
                        c.Item().AlignRight().Text(_data.Periode)
                            .FontSize(8.5f).FontColor(Colors.Grey.Darken2);

                        // "Hal. 1 dari 9" — laporan multi halaman yang dikirim
                        // ke dinas harus bisa dipastikan lengkap; tanpa nomor
                        // halaman, halaman yang hilang tidak terdeteksi.
                        //
                        // Dua catatan API: Text(handler) mengembalikan void
                        // sehingga .FontSize() tidak bisa dirantai, dan
                        // Span() mengembalikan TextSpanDescriptor yang tidak
                        // punya CurrentPageNumber. Gaya teks dipasang lewat
                        // TextDescriptor.DefaultTextStyle di dalam handler —
                        // DefaultTextStyle pada elemennya akan menambah anak
                        // kedua pada container dan memicu exception.
                        c.Item().AlignRight().Text(text =>
                        {
                            text.DefaultTextStyle(s => s.FontSize(8.5f).FontColor(Colors.Grey.Darken2));
                            text.Span("Hal. ");
                            text.CurrentPageNumber();
                            text.Span(" dari ");
                            text.TotalPages();
                        });
                    });
                });
            });
        }

        /// <summary>
        /// Blok penandatangan: kepala desa di kiri, sekretaris desa di kanan.
        /// Dipanggil sekali saja, di akhir halaman terakhir, bukan di footer.
        /// </summary>
        private void ComposeTandaTangan(IContainer container)
        {
            container.Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().AlignCenter().Text("Mengetahui,").FontSize(10f);
                    c.Item().AlignCenter().Text($"KEPALA DESA {T(_desa.NamaDesa)}").SemiBold().FontSize(10f);
                    c.Item().Height(40f);
                    c.Item().AlignCenter().Text(Atas(_desa.KepalaDesa)).Underline().SemiBold();
                });

                row.ConstantItem(150f).Column(c =>
                {
                    c.Item().AlignCenter()
                        .Text($"{_desa.NamaDesa}, {_data.TanggalCetak.ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("id-ID"))}");
                    c.Item().AlignCenter().Text("SEKRETARIS DESA").SemiBold();
                    c.Item().Height(40f);
                    c.Item().AlignCenter().Text(Atas(_desa.SekretarisDesa)).Underline().SemiBold();
                });
            });
        }

        /// <summary>Wilayah kosong menjadi "....................." agar kop tetap rapi.</summary>
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

        private static string Angka(int jumlah) =>
            jumlah.ToString("N0", CultureInfo.GetCultureInfo("id-ID"));
    }
}
