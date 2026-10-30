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
