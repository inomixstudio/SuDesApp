using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using SudesExPlus;

namespace SuDesApp.Sinkron;

/// <summary>
/// Menulis berkas .xlsx jejak audit dari antrean yang diunduh desktop.
///
/// <para>
/// Berkas ini bukan input impor — impor langsung dari baris antrean yang
/// dibaca Supabase. Berkas ada untuk dua hal: operator bisa memeriksa apa
/// yang masuk tanpa membuka aplikasi, dan ada salinan di luar sistem bila
/// sesuatu gagal di tengah jalan.
/// </para>
/// </summary>
public static class PembuatBerkasSinkron
{
    /// <summary>
    /// Tulis antrean ke <c>&lt;folder&gt;/Sinkron-YYYYmmdd-HHMMSS.xlsx</c>.
    /// Mengembalikan jalur berkas, atau null bila penulisan gagal (folder
    /// tidak bisa dibuat / penuh) — kegagalan berkas TIDAK menghentikan impor.
    /// </summary>
    public static async Task<string?> TulisAsync(
        string folder,
        IReadOnlyList<AntreanSuratBaris> baris,
        string? nomorVersi = null)
    {
        try
        {
            Directory.CreateDirectory(folder);

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var nama = $"Sinkron-{stamp}.xlsx";
            var jalur = Path.Combine(folder, nama);

            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Antrean");

            string[] kolom =
            {
                "Id Antrean", "Jenis", "Nomor Sementara", "NIK", "Tanggal",
                "Status", "Percobaan", "Catatan", "Dibuat",
            };

            for (var c = 0; c < kolom.Length; c++)
            {
                ws.Cells[1, c + 1].Value = kolom[c];
                ws.Cells[1, c + 1].Style.Font.Bold = true;
            }

            var barisSekarang = 2;
            foreach (var b in baris)
            {
                ws.Cells[barisSekarang, 1].Value = b.Id.ToString();
                ws.Cells[barisSekarang, 2].Value = b.IdJenis;
                ws.Cells[barisSekarang, 3].Value = b.NomorSementara;
                ws.Cells[barisSekarang, 4].Value = b.Nik;
                ws.Cells[barisSekarang, 5].Value = b.TanggalSurat.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                ws.Cells[barisSekarang, 6].Value = b.Status;
                ws.Cells[barisSekarang, 7].Value = b.BatasCoba;
                ws.Cells[barisSekarang, 8].Value = b.Catatan;
                ws.Cells[barisSekarang, 9].Value = b.DibuatPada.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                barisSekarang++;
            }

            ws.Cells[1, 1, barisSekarang - 1, kolom.Length].AutoFitColumns();

            using var penampung = new MemoryStream();
            await package.SaveAsAsync(penampung).ConfigureAwait(false);
            await File.WriteAllBytesAsync(jalur, penampung.ToArray()).ConfigureAwait(false);

            return jalur;
        }
        catch
        {
            return null;
        }
    }
}
