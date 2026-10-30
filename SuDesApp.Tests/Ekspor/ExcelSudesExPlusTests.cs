using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using SudesExPlus;
using SudesExPlus.Style;
using SuDesApp.Services;
using Xunit;

namespace SuDesApp.Tests.Ekspor;

/// <summary>
/// SudesExPlus menggantikan EPPlus: berkas .xlsx harus tetap bisa dibaca
/// kembali oleh aplikasi sendiri (register tahunan, laporan, arsip) dan tetap
/// berupa .xlsx standar. Uji ini menjaga tiga hal yang mudah rusak tanpa
/// terlihat: nilai sel (termasuk tanggal dan angka), tata letak yang ditulis
/// (gabungan sel, format angka), dan nama sheet yang dipakai pembaca arsip.
/// </summary>
public sealed class ExcelSudesExPlusTests
{
    [Fact]
    public void Tulis_baca_ulang_mempertahankan_nilai_tanggal_dan_format()
    {
        using var penampung = new MemoryStream();
        using (var paket = new ExcelPackage())
        {
            var ws = paket.Workbook.Worksheets.Add("Data");
            ws.Cells[1, 1].Value = "Tanggal";
            ws.Cells[1, 2].Value = "Jumlah";
            ws.Cells[2, 1].Value = new DateTime(2026, 1, 5);
            ws.Cells[2, 2].Value = 3;
            ws.Cells[2, 2].Style.Numberformat.Format = "0.00";
            ws.Cells[3, 1].Value = "teks";
            ws.Cells[1, 1, 1, 2].Merge = true;

            paket.SaveAs(penampung);
        }

        penampung.Position = 0;
        string jalur = Path.Combine(Path.GetTempPath(), $"sudesexplus-uji-{Guid.NewGuid():N}.xlsx");
        File.WriteAllBytes(jalur, penampung.ToArray());

        try
        {
            using var paket = new ExcelPackage(new FileInfo(jalur));

            Assert.Equal(1, paket.Workbook.Worksheets.Count);
            var ws = paket.Workbook.Worksheets["Data"];
            Assert.NotNull(ws);
            Assert.NotNull(ws!.Dimension);
            Assert.Equal(3, ws.Dimension!.End.Row);
            Assert.Equal(2, ws.Dimension.End.Column);

            Assert.Equal(new DateTime(2026, 1, 5), ws.Cells[2, 1].Value);
            Assert.Equal("05/01/2026", ws.Cells[2, 1].Text);
            Assert.Equal(3d, ws.Cells[2, 2].Value);
            // Teks sel memakai pemisah desimal titik (invariant) supaya angka
            // yang dibaca ulang selalu bisa diurai tanpa bergantung budaya lokal.
            Assert.Equal("3.00", ws.Cells[2, 2].Text);
            Assert.Equal("teks", ws.Cells[3, 1].Value);
        }
        finally
        {
            File.Delete(jalur);
        }
    }

    [Fact]
    public async Task Simpan_asinkron_ke_berkas_dan_gaya_rentang_berlaku_per_sel()
    {
        string jalur = Path.Combine(Path.GetTempPath(), $"sudesexplus-async-{Guid.NewGuid():N}.xlsx");

        try
        {
            using (var paket = new ExcelPackage())
            {
                var ws = paket.Workbook.Worksheets.Add("Data Warga");
                ws.Cells[1, 1].Value = "No";
                ws.Cells[1, 2].Value = "Nama";
                ws.Cells[2, 1].Value = 1;
                ws.Cells[2, 2].Value = "Sukamaju";

                // Latar baris lalu garis keliling seluruh tabel: gaya rentang
                // kedua tidak boleh menghapus latar yang sudah dipasang.
                ws.Cells[2, 1, 2, 2].Style.Fill.PatternType = ExcelFillStyle.Solid;
                ws.Cells[2, 1, 2, 2].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(248, 250, 252));
                ws.Cells[1, 1, 2, 2].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                ws.Cells[1, 1, 2, 2].AutoFitColumns(10, 45);

                await paket.SaveAsAsync(new FileInfo(jalur));
            }

            Assert.True(new FileInfo(jalur).Length > 0);

            using var dibaca = new ExcelPackage(new FileInfo(jalur));
            var lembar = dibaca.Workbook.Worksheets["Data Warga"];
            Assert.Equal("No", lembar!.Cells[1, 1].Value);
            Assert.Equal(1d, lembar.Cells[2, 1].Value);
            Assert.Equal("Sukamaju", lembar.Cells[2, 2].Value);
        }
        finally
        {
            if (File.Exists(jalur)) File.Delete(jalur);
        }
    }

    [Fact]
    public void Gaya_visual_rumus_ukuran_dan_gabungan_bertahan_setelah_simpan_ulang()
    {
        string jalur = Path.Combine(Path.GetTempPath(), $"sudesexplus-gaya-{Guid.NewGuid():N}.xlsx");

        try
        {
            using (var paket = new ExcelPackage())
            {
                var ws = paket.Workbook.Worksheets.Add("Laporan");

                ws.Cells[1, 1].Value = "Judul";
                ws.Cells[1, 1, 1, 3].Merge = true;

                var judul = ws.Cells[1, 1];
                judul.Style.Font.Name = "Arial";
                judul.Style.Font.Size = 14;
                judul.Style.Font.Bold = true;
                judul.Style.Font.Underline = true;
                judul.Style.Font.Color.SetColor(System.Drawing.Color.White);
                judul.Style.Fill.PatternType = ExcelFillStyle.Solid;
                judul.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(37, 99, 235));
                judul.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                judul.Style.VerticalAlignment = ExcelVerticalAlignment.Center;

                ws.Cells[2, 1].Value = 1;
                ws.Cells[2, 2].Value = 2;
                ws.Cells[3, 1].Value = "Total";
                ws.Cells[3, 2].Formula = "=SUM(A2:B2)";
                ws.Cells[3, 2].Value = 3d;
                ws.Cells[3, 2].Style.Font.Italic = true;
                ws.Cells[3, 2].Style.Numberformat.Format = "#,##0.00";
                ws.Cells[3, 2].Style.Border.Left = ExcelBorderStyle.Medium;
                ws.Cells[3, 2].Style.Border.Bottom = ExcelBorderStyle.Double;

                ws.Column(1).Width = 28;
                ws.Row(1).Height = 30;
                ws.View.FreezePanes(2, 1);

                paket.SaveAs(jalur);
            }

            using var dibaca = new ExcelPackage(new FileInfo(jalur));
            var lembar = dibaca.Workbook.Worksheets["Laporan"];
            Assert.NotNull(lembar);

            var judulDibaca = lembar!.Cells[1, 1];
            Assert.Equal("Arial", judulDibaca.Style.Font.Name);
            Assert.Equal(14, judulDibaca.Style.Font.Size);
            Assert.True(judulDibaca.Style.Font.Bold);
            Assert.True(judulDibaca.Style.Font.Underline);
            Assert.Equal(System.Drawing.Color.White.ToArgb(), judulDibaca.Style.Font.Color.Value?.ToArgb());
            Assert.Equal(System.Drawing.Color.FromArgb(37, 99, 235).ToArgb(),
                judulDibaca.Style.Fill.BackgroundColor.Value?.ToArgb());
            Assert.Equal(ExcelHorizontalAlignment.Center, judulDibaca.Style.HorizontalAlignment);
            Assert.Equal(ExcelVerticalAlignment.Center, judulDibaca.Style.VerticalAlignment);

            // Rumus dan nilai terakhirnya sama-sama bertahan.
            Assert.Equal("SUM(A2:B2)", lembar.Cells[3, 2].Formula);
            Assert.Equal(3d, lembar.Cells[3, 2].Value);
            Assert.Equal("#,##0.00", lembar.Cells[3, 2].Style.Numberformat.Format);
            Assert.True(lembar.Cells[3, 2].Style.Font.Italic);
            Assert.Equal(ExcelBorderStyle.Medium, lembar.Cells[3, 2].Style.Border.Left);
            Assert.Equal(ExcelBorderStyle.Double, lembar.Cells[3, 2].Style.Border.Bottom);

            Assert.Contains("A1:C1", lembar.MergedCells);
            Assert.Equal(28, lembar.Column(1).Width);
            Assert.Equal(30, lembar.Row(1).Height);
            Assert.Equal(1, lembar.View.FrozenRows);
            Assert.Equal(0, lembar.View.FrozenColumns);
        }
        finally
        {
            if (File.Exists(jalur)) File.Delete(jalur);
        }
    }

    [Fact]
    public void Rumus_bersama_pihak_lain_digeser_sesuai_baris_masing_masing()
    {
        // Bentuk yang ditulis Excel saat satu rumus disalin ke bawah: hanya sel
        // pertama yang menyimpan teks rumus, sisanya menunjuk indeks bersama.
        string jalur = Path.Combine(Path.GetTempPath(), $"sudesexplus-shared-{Guid.NewGuid():N}.xlsx");

        try
        {
            BuatBerkasMinimal(jalur,
                "<sheetData>" +
                "<row r=\"1\"><c r=\"A1\"><v>1</v></c><c r=\"B1\"><f t=\"shared\" si=\"0\" ref=\"B1:B3\">A1*2</f><v>2</v></c></row>" +
                "<row r=\"2\"><c r=\"A2\"><v>2</v></c><c r=\"B2\"><f t=\"shared\" si=\"0\"/><v>4</v></c></row>" +
                "<row r=\"3\"><c r=\"A3\"><v>3</v></c><c r=\"B3\"><f t=\"shared\" si=\"0\"/><v>6</v></c></row>" +
                "</sheetData>");

            using var paket = new ExcelPackage(new FileInfo(jalur));
            var lembar = paket.Workbook.Worksheets["Data"];
            Assert.NotNull(lembar);

            Assert.Equal("A1*2", lembar!.Cells[1, 2].Formula);
            Assert.Equal("A2*2", lembar.Cells[2, 2].Formula);
            Assert.Equal("A3*2", lembar.Cells[3, 2].Formula);
            Assert.Equal(4d, lembar.Cells[2, 2].Value);
        }
        finally
        {
            if (File.Exists(jalur)) File.Delete(jalur);
        }
    }

    [Fact]
    public void Warna_tema_dan_tint_dari_berkas_pihak_lain_diterjemahkan_ke_rgb()
    {
        // Excel menulis latar sel sebagai warna tema (theme="4") plus rona,
        // bukan RGB. Tanpa terjemahan ini isian akan hilang saat disimpan ulang.
        string jalur = Path.Combine(Path.GetTempPath(), $"sudesexplus-tema-{Guid.NewGuid():N}.xlsx");

        try
        {
            string tema = "<a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" name=\"Office\">" +
                          "<a:themeElements><a:clrScheme name=\"Office\">" +
                          "<a:dk1><a:sysClr val=\"windowText\" lastClr=\"000000\"/></a:dk1>" +
                          "<a:lt1><a:sysClr val=\"window\" lastClr=\"FFFFFF\"/></a:lt1>" +
                          "<a:dk2><a:srgbClr val=\"44546A\"/></a:dk2>" +
                          "<a:lt2><a:srgbClr val=\"E7E6E6\"/></a:lt2>" +
                          "<a:accent1><a:srgbClr val=\"4472C4\"/></a:accent1>" +
                          "<a:hlink><a:srgbClr val=\"0563C1\"/></a:hlink>" +
                          "<a:folHlink><a:srgbClr val=\"954F72\"/></a:folHlink>" +
                          "</a:clrScheme></a:themeElements></a:theme>";

            string gaya = "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                          "<fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
                          "<fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill>" +
                          "<fill><patternFill patternType=\"gray125\"/></fill>" +
                          "<fill><patternFill patternType=\"solid\"><fgColor theme=\"4\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>" +
                          "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                          "<cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/>" +
                          "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"2\" borderId=\"0\" applyFill=\"1\"/></cellXfs>" +
                          "</styleSheet>";

            BuatBerkasMinimal(jalur,
                "<sheetData><row r=\"1\"><c r=\"A1\" s=\"1\" t=\"inlineStr\"><is><t>Judul</t></is></c></row></sheetData>",
                gaya, tema);

            using var paket = new ExcelPackage(new FileInfo(jalur));
            var sel = paket.Workbook.Worksheets["Data"].Cells[1, 1];

            Assert.Equal("Judul", sel.Value);
            Assert.Equal("#4472C4", WarnaHex(sel.Style.Fill.BackgroundColor.Value));
        }
        finally
        {
            if (File.Exists(jalur)) File.Delete(jalur);
        }
    }

    private static string WarnaHex(System.Drawing.Color? warna) =>
        warna.HasValue ? $"#{warna.Value.R:X2}{warna.Value.G:X2}{warna.Value.B:X2}" : "-";

    /// <summary>
    /// Tulis .xlsx minimum (satu sheet) untuk meniru bentuk berkas pihak lain
    /// yang lebih kaya dari yang bisa dihasilkan model di atas.
    /// </summary>
    private static void BuatBerkasMinimal(
        string jalur,
        string isiSheet,
        string? stylesXml = null,
        string? themeXml = null)
    {
        using var aliran = File.Create(jalur);
        using var arsip = new System.IO.Compression.ZipArchive(aliran, System.IO.Compression.ZipArchiveMode.Create);

        void Bagian(string nama, string isi)
        {
            var entri = arsip.CreateEntry(nama);
            using var tulis = new StreamWriter(entri.Open());
            tulis.Write(isi);
        }

        string isiTypes = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                          "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                          "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                          "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                          "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                          (stylesXml != null
                              ? "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>"
                              : string.Empty) +
                          (themeXml != null
                              ? "<Override PartName=\"/xl/theme/theme1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.theme+xml\"/>"
                              : string.Empty) +
                          "</Types>";

        Bagian("[Content_Types].xml", isiTypes);
        Bagian("_rels/.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
        Bagian("xl/workbook.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
            "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
            "<sheets><sheet name=\"Data\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");

        string relasi = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>";
        if (stylesXml != null)
        {
            relasi += "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>";
        }

        if (themeXml != null)
        {
            relasi += "<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme\" Target=\"theme/theme1.xml\"/>";
        }

        relasi += "</Relationships>";
        Bagian("xl/_rels/workbook.xml.rels", relasi);

        Bagian("xl/worksheets/sheet1.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
            isiSheet + "</worksheet>");

        if (stylesXml != null) Bagian("xl/styles.xml", stylesXml);
        if (themeXml != null) Bagian("xl/theme/theme1.xml", themeXml);
    }

    [Fact]
    public void Nama_sheet_dibersihkan_dan_dibuat_unik()
    {
        using var paket = new ExcelPackage();
        var pertama = paket.Workbook.Worksheets.Add("Register 2026/Desa");
        var kedua = paket.Workbook.Worksheets.Add("Register 2026/Desa");

        Assert.Equal("Register 2026-Desa", pertama.Name);
        Assert.NotEqual(pertama.Name, kedua.Name);
        Assert.Equal(2, paket.Workbook.Worksheets.Count);
    }

    [Fact]
    public void Register_tahunan_menghasilkan_tiga_sheet_yang_bisa_dibaca_ulang()
    {
        var data = new RegisterTahunanData
        {
            Tahun = 2026,
            NamaDesa = "Karangtanjung",
            DicetakPada = new DateTime(2026, 10, 30, 8, 0, 0),
            StatusBuku = new SuDesApp.Data.Models.TutupBukuTahun
            {
                Tahun = 2026,
                Status = SuDesApp.Data.Models.StatusTutupBuku.Terbuka,
            },
            Verifikasi = new HasilVerifikasiNomor
            {
                Tahun = 2026,
                Deret = new List<DeretNomor>
                {
                    new()
                    {
                        Awalan = "470",
                        Tahun = 2026,
                        Jumlah = 2,
                        NomorPertama = 1,
                        NomorTerakhir = 2,
                    },
                },
                NomorTahunTidakCocok = Array.Empty<string>(),
                TahunTersedia = new[] { 2026 },
            },
            Baris = new List<SuDesApp.Data.Models.BarisRegisterSurat>
            {
                new()
                {
                    NomorSurat = "470/001/2026",
                    TanggalSurat = "2026-01-05",
                    NamaJenis = "Surat Keterangan Domisili",
                    NamaWarga = "Sukamaju",
                    NikWarga = "3215022001000001",
                    Keperluan = "Keperluan administrasi",
                },
            },
        };

        byte[] isi = ArsipRegisterTahunanService.RenderExcel(data);
        Assert.NotEmpty(isi);

        string jalur = Path.Combine(Path.GetTempPath(), $"sudesexplus-register-{Guid.NewGuid():N}.xlsx");
        File.WriteAllBytes(jalur, isi);

        try
        {
            using var paket = new ExcelPackage(new FileInfo(jalur));

            var nama = paket.Workbook.Worksheets.Select(w => w.Name).ToList();
            Assert.Equal(new[] { "Ringkasan", "Register", "Deret Nomor" }, nama);

            var register = paket.Workbook.Worksheets["Register"];
            Assert.NotNull(register);
            Assert.Equal("No", register!.Cells[1, 1].Value);
            Assert.Equal("Nomor Surat", register.Cells[1, 2].Value);
            Assert.Equal(1d, register.Cells[2, 1].Value);
            Assert.Equal("470/001/2026", register.Cells[2, 2].Value);

            var ringkasan = paket.Workbook.Worksheets["Ringkasan"];
            Assert.Equal("ARSIP REGISTER SURAT DESA", ringkasan!.Cells[1, 1].Value);
        }
        finally
        {
            File.Delete(jalur);
        }
    }
}
