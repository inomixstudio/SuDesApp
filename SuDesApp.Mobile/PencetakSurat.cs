using SudesPdf.Core.Layout;

namespace SuDesApp.Mobile;

/// <summary>Satu baris peserta pada daftar hadir.</summary>
public sealed class PesertaHadir
{
    public string Nama { get; set; } = string.Empty;

    public string Jabatan { get; set; } = string.Empty;

    public string Keterangan { get; set; } = string.Empty;
}

/// <summary>Satu halaman surat yang dicetak dalam satu berkas gabungan.</summary>
public sealed record HalamanSurat(
    DefinisiSurat Surat,
    IReadOnlyDictionary<string, string> Nilai,
    string Nomor);

/// <summary>
/// Pencetak PDF surat untuk aplikasi Android. Seluruh halaman disusun dengan
/// mesin <c>SudesPdf.Core</c> yang sama dengan versi desktop, sehingga huruf,
/// kop, dan blok tanda tangannya tampil sama di kedua aplikasi.
/// </summary>
public static class PencetakSurat
{
    private const float UkuranTeks = 12f;
    private const float JarakBaris = 15f;

    /// <summary>PDF satu surat menurut definisi katalog dan isian pengguna.</summary>
    public static byte[] BuatSurat(
        ProfilDesa desa,
        DefinisiSurat surat,
        IReadOnlyDictionary<string, string> nilai,
        string nomor,
        DateTime tanggal)
    {
        return Susun(desa, (container) => IsiSurat(container, desa, surat, nilai, nomor, tanggal));
    }

    /// <summary>
    /// Beberapa surat dalam SATU berkas PDF, satu surat per halaman. Dipakai paket
    /// NTCR: blanko N1-N6 dari satu data pasangan dicetak berurutan seperti versi
    /// desktop.<br/>
    /// Ukuran kertas halaman mengikuti halaman pertama, jadi seluruh blanko dalam
    /// satu paket memakai ukuran yang sama.
    /// </summary>
    public static byte[] BuatBerkasBanyak(
        ProfilDesa desa,
        IReadOnlyList<HalamanSurat> halaman,
        DateTime tanggal)
    {
        if (halaman == null || halaman.Count == 0)
        {
            throw new InvalidOperationException("Tidak ada halaman surat yang akan dicetak.");
        }

        using var penampung = new MemoryStream();

        Document.Create(container =>
        {
            foreach (var surat in halaman)
            {
                container.Page(page =>
                {
                    AturHalaman(page);
                    page.Content().Element(root =>
                    {
                        root.Column(kolom =>
                        {
                            kolom.Item().Element(c =>
                                IsiSurat(c, desa, surat.Surat, surat.Nilai, surat.Nomor, tanggal));
                        });
                    });
                });
            }
        }).GeneratePdf(penampung);

        return penampung.ToArray();
    }

    /// <summary>Isi satu surat: kop (bila dipakai), judul, isian, penutup, tanda tangan.</summary>
    private static void IsiSurat(
        IContainer container,
        ProfilDesa desa,
        DefinisiSurat surat,
        IReadOnlyDictionary<string, string> nilai,
        string nomor,
        DateTime tanggal)
    {
        var baris = surat.Kolom
            .Select(k => (Label: k.Label, Nilai: NilaiCetak(k, nilai), Wajib: k.Wajib))
            .ToList();

        container.Column(kolom =>
        {
            if (surat.PakaiKop)
            {
                kolom.Item().Element(c => Kop(c, desa));
            }

            kolom.Item().Element(c => JudulDanNomor(c, surat.Judul, nomor));

            foreach (var paragraf in surat.Pembuka)
            {
                kolom.Item().PaddingTop(4).Element(c => Paragraf(c, paragraf));
            }

            kolom.Item().PaddingTop(6).Element(c => TabelIsian(c, baris));

            foreach (var paragraf in surat.Penutup)
            {
                kolom.Item().PaddingTop(6).Element(c => Paragraf(c, paragraf));
            }

            if (surat.TandaTangan)
            {
                string jabatan = string.IsNullOrWhiteSpace(surat.JabatanPenandatangan)
                    ? "Kepala Desa"
                    : surat.JabatanPenandatangan;

                string? nama = null;
                if (!string.IsNullOrWhiteSpace(surat.PenandatanganDariKunci) &&
                    nilai.TryGetValue(surat.PenandatanganDariKunci, out var dariIsian) &&
                    !string.IsNullOrWhiteSpace(dariIsian))
                {
                    nama = dariIsian;
                }

                kolom.Item().Element(c => TandaTangan(c, desa, jabatan, tanggal, nama));
            }
        });
    }

    /// <summary>PDF surat bebas: judul, paragraf yang diketik sendiri, lalu tanda tangan.</summary>
    public static byte[] BuatSuratBebas(
        ProfilDesa desa,
        string judul,
        string nomor,
        IReadOnlyList<string> paragraf,
        DateTime tanggal)
    {
        return Susun(desa, (container) =>
        {
            container.Column(kolom =>
            {
                kolom.Item().Element(c => Kop(c, desa));
                kolom.Item().Element(c => JudulDanNomor(c, judul, nomor));

                foreach (var isi in paragraf.Where(p => !string.IsNullOrWhiteSpace(p)))
                {
                    kolom.Item().PaddingTop(6).Element(c => Paragraf(c, isi));
                }

                kolom.Item().Element(c => TandaTangan(c, desa, "Kepala Desa", tanggal));
            });
        });
    }

    /// <summary>PDF daftar hadir: kop, judul, keterangan waktu, tabel peserta, tanda tangan.</summary>
    public static byte[] BuatDaftarHadir(
        ProfilDesa desa,
        string judul,
        string hariTanggal,
        string pukul,
        string tempat,
        IReadOnlyList<PesertaHadir> peserta,
        DateTime tanggal)
    {
        string judulCetak = string.IsNullOrWhiteSpace(judul) ? "DAFTAR HADIR" : judul.Trim();

        return Susun(desa, (container) =>
        {
            container.Column(kolom =>
            {
                kolom.Item().Element(c => Kop(c, desa));

                kolom.Item().PaddingTop(14).AlignCenter()
                    .Text(judulCetak.ToUpperInvariant()).FontSize(15).Bold().Underline();

                kolom.Item().PaddingTop(8).Element(c =>
                {
                    c.Column(info =>
                    {
                        void Baris(string label, string isi)
                        {
                            if (string.IsNullOrWhiteSpace(isi)) return;
                            info.Item().Row(row =>
                            {
                                row.ConstantItem(90).Text(label);
                                row.ConstantItem(10).Text(":");
                                row.RelativeItem().Text(isi.Trim());
                            });
                        }

                        Baris("Hari/Tanggal", hariTanggal);
                        Baris("Pukul", pukul);
                        Baris("Tempat", tempat);
                    });
                });

                kolom.Item().PaddingTop(12).Table(table => TabelPeserta(table, peserta, tanggal));
                kolom.Item().Element(c => TandaTangan(c, desa, "Kepala Desa", tanggal));
            });
        });
    }

    /// <summary>PDF register: daftar surat yang pernah dibuat, siap diarsipkan atau dicetak.</summary>
    public static byte[] BuatRegister(
        ProfilDesa desa,
        string judul,
        IReadOnlyList<SuratTersimpan> daftar,
        DateTime tanggal)
    {
        var urut = daftar.OrderBy(s => s.Waktu).ToList();

        return Susun(desa, (container) =>
        {
            container.Column(kolom =>
            {
                kolom.Item().Element(c => Kop(c, desa));
                kolom.Item().PaddingTop(14).AlignCenter()
                    .Text(judul.ToUpperInvariant()).FontSize(15).Bold().Underline();

                kolom.Item().PaddingTop(12).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(30);
                        columns.ConstantColumn(95);
                        columns.ConstantColumn(85);
                        columns.RelativeColumn(3f);
                        columns.RelativeColumn(2f);
                    });

                    void Kepala(string teks)
                    {
                        table.Cell().Border(0.5f).BorderColor(Colors.Grey.Medium).Padding(4)
                            .AlignCenter().Text(teks).FontSize(10).Bold();
                    }

                    table.Header(header =>
                    {
                        header.Cell().Element(c => c.Border(0.5f).BorderColor(Colors.Grey.Medium).Padding(4).AlignCenter())
                            .Text("No.").FontSize(10).Bold();
                        header.Cell().Element(c => c.Border(0.5f).BorderColor(Colors.Grey.Medium).Padding(4).AlignCenter())
                            .Text("Nomor").FontSize(10).Bold();
                        header.Cell().Element(c => c.Border(0.5f).BorderColor(Colors.Grey.Medium).Padding(4).AlignCenter())
                            .Text("Tanggal").FontSize(10).Bold();
                        header.Cell().Element(c => c.Border(0.5f).BorderColor(Colors.Grey.Medium).Padding(4).AlignCenter())
                            .Text("Jenis Surat").FontSize(10).Bold();
                        header.Cell().Element(c => c.Border(0.5f).BorderColor(Colors.Grey.Medium).Padding(4).AlignCenter())
                            .Text("Pemohon").FontSize(10).Bold();
                    });

                    int nomor = 1;
                    foreach (var surat in urut)
                    {
                        void Sel(string teks, bool tengah = false)
                        {
                            var sel = table.Cell().Border(0.5f).BorderColor(Colors.Grey.Medium).Padding(4);
                            var isi = (tengah ? sel.AlignCenter() : sel).Text(teks).FontSize(10);
                            _ = isi;
                        }

                        Sel(nomor.ToString(), tengah: true);
                        Sel(surat.Nomor);
                        Sel(surat.Waktu.ToString("dd-MM-yyyy"), tengah: true);
                        Sel(surat.NamaJenis);
                        Sel(surat.RingkasanPemohon);
                        nomor++;
                    }
                });

                if (urut.Count == 0)
                {
                    kolom.Item().PaddingTop(8).Text("Belum ada surat yang tercatat.").Italic();
                }

                kolom.Item().Element(c => TandaTangan(c, desa, "Kepala Desa", tanggal));
            });
        });
    }

    // =====================================================================
    // Bagian halaman
    // =====================================================================

    private static byte[] Susun(ProfilDesa desa, Action<IContainer> isi)
    {
        using var penampung = new MemoryStream();

        Document.Create(container =>
        {
            container.Page(page =>
            {
                AturHalaman(page);

                page.Content().Element(root =>
                {
                    root.Column(kolom =>
                    {
                        kolom.Item().Element(c => isi(c));
                    });
                });
            });
        }).GeneratePdf(penampung);

        return penampung.ToArray();
    }

    /// <summary>Ukuran dan gaya dasar halaman yang dipakai seluruh surat ponsel.</summary>
    private static void AturHalaman(PageDescriptor page)
    {
        page.Size(PageSizes.A4);
        page.MarginTop(42, Unit.Point);
        page.MarginRight(45, Unit.Point);
        page.MarginBottom(36, Unit.Point);
        page.MarginLeft(45, Unit.Point);
        page.DefaultTextStyle(x => x.FontFamily("Times New Roman").FontSize(UkuranTeks));
    }

    /// <summary>Kop surat: kabupaten, kecamatan, nama desa, alamat, lalu garis ganda.</summary>
    private static void Kop(IContainer container, ProfilDesa desa)
    {
        container.Column(kolom =>
        {
            kolom.Item().AlignCenter().Text($"PEMERINTAH KABUPATEN {FormatSurat.Kapital(desa.Kabupaten)}")
                .FontSize(16).Bold();
            kolom.Item().AlignCenter().Text($"KECAMATAN {FormatSurat.Kapital(desa.Kecamatan)}")
                .FontSize(18).Bold();
            kolom.Item().AlignCenter().Text($"PEMERINTAH DESA {FormatSurat.Kapital(desa.NamaDesa)}")
                .FontSize(22).Bold();

            string alamat = $"Alamat {FormatSurat.Bersih(desa.Alamat)} Desa {FormatSurat.Bersih(desa.NamaDesa)} " +
                            $"Kec. {FormatSurat.Bersih(desa.Kecamatan)}" +
                            (string.IsNullOrWhiteSpace(desa.Kodepos) ? string.Empty : $" Kodepos {desa.Kodepos.Trim()}");
            kolom.Item().AlignCenter().Text(alamat.Trim()).FontSize(9);

            if (!string.IsNullOrWhiteSpace(desa.Email))
            {
                kolom.Item().AlignCenter().Text($"Email {desa.Email.Trim()}").FontSize(9).FontColor("#1565C0");
            }

            kolom.Item().MarginTop(3).Height(0.8f).Background(Colors.Black);
            kolom.Item().MarginTop(1).Height(1.5f).Background(Colors.Black);
        });
    }

    private static void JudulDanNomor(IContainer container, string judul, string nomor)
    {
        container.Column(kolom =>
        {
            kolom.Item().PaddingTop(18).AlignCenter()
                .Text((judul ?? string.Empty).ToUpperInvariant())
                .FontSize(14).LineHeight(JarakBaris / 14f).Bold().Underline();

            if (!string.IsNullOrWhiteSpace(nomor))
            {
                kolom.Item().AlignCenter().Text($"NOMOR : {nomor.Trim()}")
                    .FontSize(UkuranTeks).LineHeight(JarakBaris / UkuranTeks);
            }
        });
    }

    private static void Paragraf(IContainer container, string teks)
    {
        container.Text(paragraf =>
        {
            paragraf.Justify();
            paragraf.ParagraphFirstLineIndentation(24);
            paragraf.Span(teks ?? string.Empty).FontSize(UkuranTeks).LineHeight(1.5f);
        });
    }

    /// <summary>Baris "Label : isi" dengan kolom label rata, seperti surat resmi desa.</summary>
    private static void TabelIsian(IContainer container, IReadOnlyList<(string Label, string Nilai, bool Wajib)> baris)
    {
        container.Column(kolom =>
        {
            foreach (var (label, nilai, wajib) in baris)
            {
                // Kolom wajib yang kosong tetap dicetak bertitik agar bentuk formulir
                // terlihat; kolom opsional yang kosong dibiarkan kosong.
                string isi = string.IsNullOrWhiteSpace(nilai)
                    ? (wajib ? "...................." : string.Empty)
                    : nilai;

                kolom.Item().PaddingLeft(20).Row(row =>
                {
                    row.ConstantItem(140).Text(label);
                    row.ConstantItem(10).Text(":");
                    row.RelativeItem().Text(isi);
                });
            }
        });
    }

    private static void TabelPeserta(TableDescriptor table, IReadOnlyList<PesertaHadir> peserta, DateTime tanggal)
    {
        table.ColumnsDefinition(columns =>
        {
            columns.ConstantColumn(30);
            columns.RelativeColumn(3f);
            columns.RelativeColumn(2f);
            columns.RelativeColumn(2f);
        });

        void Kepala(string teks)
        {
            table.Cell().Border(0.5f).BorderColor(Colors.Grey.Medium).Padding(4)
                .AlignCenter().Text(teks).FontSize(11).Bold();
        }

        table.Header(header =>
        {
            header.Cell().Element(c => c.Border(0.5f).BorderColor(Colors.Grey.Medium).Padding(4).AlignCenter())
                .Text("No.").FontSize(11).Bold();
            header.Cell().Element(c => c.Border(0.5f).BorderColor(Colors.Grey.Medium).Padding(4).AlignCenter())
                .Text("Nama").FontSize(11).Bold();
            header.Cell().Element(c => c.Border(0.5f).BorderColor(Colors.Grey.Medium).Padding(4).AlignCenter())
                .Text("Jabatan").FontSize(11).Bold();
            header.Cell().Element(c => c.Border(0.5f).BorderColor(Colors.Grey.Medium).Padding(4).AlignCenter())
                .Text("Keterangan").FontSize(11).Bold();
        });

        int nomor = 1;
        foreach (var orang in peserta.Where(p => !string.IsNullOrWhiteSpace(p.Nama)))
        {
            void Sel(string teks, bool tengah = false)
            {
                var sel = table.Cell().Border(0.5f).BorderColor(Colors.Grey.Medium).Padding(4);
                (tengah ? sel.AlignCenter() : sel).Text(teks).FontSize(11);
            }

            Sel(nomor.ToString(), tengah: true);
            Sel(orang.Nama);
            Sel(orang.Jabatan);
            Sel(orang.Keterangan);
            nomor++;
        }

        // Baris kosong penambah ruang tanda tangan manual.
        for (int i = nomor; i <= Math.Max(10, nomor + 3); i++)
        {
            void Kosong(bool tengah = false)
            {
                var sel = table.Cell().Border(0.5f).BorderColor(Colors.Grey.Medium).Padding(6);
                (tengah ? sel.AlignCenter() : sel).Text(" ").FontSize(11);
            }

            Kosong(tengah: true);
            Kosong();
            Kosong();
            Kosong();
        }

        _ = tanggal;
    }

    /// <summary>
    /// Blok tanda tangan di sisi kanan. Lebar blok dikunci lewat kolom tetap di
    /// dalam Row — pola yang sama dengan versi desktop — supaya isinya benar-benar
    /// berada di kanan halaman, bukan di tengah.
    /// </summary>
    private static void TandaTangan(
        IContainer container,
        ProfilDesa desa,
        string jabatan,
        DateTime tanggal,
        string? nama = null)
    {
        string tempatTanggal = string.IsNullOrWhiteSpace(desa.NamaDesa)
            ? FormatSurat.TanggalPanjang(tanggal)
            : $"{desa.NamaDesa.Trim()}, {FormatSurat.TanggalPanjang(tanggal)}";

        string namaCetak = FormatSurat.Kapital(
            string.IsNullOrWhiteSpace(nama) ? desa.NamaKepalaDesa : nama);

        container.PaddingTop(20).Row(row =>
        {
            row.RelativeItem();
            row.ConstantItem(220).Column(kolom =>
            {
                kolom.Item().AlignCenter().Text(tempatTanggal).FontSize(UkuranTeks);
                kolom.Item().PaddingTop(2).AlignCenter().Text(jabatan.ToUpperInvariant())
                    .FontSize(UkuranTeks).Bold();
                kolom.Item().Height(56);
                kolom.Item().AlignCenter()
                    .Text(namaCetak)
                    .FontSize(UkuranTeks).Bold().Underline();
            });
        });
    }

    private static string NilaiCetak(KolomIsian kolom, IReadOnlyDictionary<string, string> nilai)
    {
        nilai.TryGetValue(kolom.Kunci, out var isi);
        string teks = FormatSurat.Bersih(isi);

        if (teks.Length == 0)
        {
            return FormatSurat.Bersih(kolom.Bawaan);
        }

        return kolom.Tipe switch
        {
            "tanggal" => FormatSurat.BacaTanggal(teks) is { } tanggal
                ? FormatSurat.TanggalPanjang(tanggal)
                : teks,
            _ => teks
        };
    }
}
