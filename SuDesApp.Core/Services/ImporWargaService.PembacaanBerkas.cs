using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SudesExPlus;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;

namespace SuDesApp.Services
{
    public partial class ImporWargaService : IImporWargaService
    {
        private static HasilImporWarga Gagal(string nama, string pesan) => new()
        {
            NamaBerkas = nama,
            Baris = Array.Empty<BarisImporWarga>(),
            KolomTidakDikenali = Array.Empty<string>(),
            KesalahanBerkas = pesan
        };

        private static List<string[]> BacaXlsx(string path)
        {

            using var package = new ExcelPackage(new FileInfo(path));
            if (package.Workbook.Worksheets.Count == 0)
                throw new InvalidDataException("Berkas tidak memiliki sheet apa pun.");

            ExcelWorksheet ws = package.Workbook.Worksheets[0];
            if (ws.Dimension == null || ws.Dimension.End.Row < 1)
                throw new InvalidDataException("Sheet pertama kosong.");

            int barisAkhir = ws.Dimension.End.Row;
            int kolomAkhir = ws.Dimension.End.Column;
            var hasil = new List<string[]>(barisAkhir);

            for (int r = 1; r <= barisAkhir; r++)
            {
                var baris = new string[kolomAkhir];
                for (int c = 1; c <= kolomAkhir; c++)
                    baris[c - 1] = TeksSel(ws.Cells[r, c]);
                hasil.Add(baris);
            }

            // Baris yang seluruh selnya kosong (sering tersisa dari sel berformat
            // pada berkas template) dibuang seperti jalur CSV — tanpa ini, satu
            // template dengan ratusan baris berformat menampilkan ratusan
            // "NIK kosong" sebagai baris ditolak di pratinjau impor.
            hasil.RemoveAll(b => b.All(v => string.IsNullOrWhiteSpace(v)));

            return hasil;
        }

        /// <summary>
        /// Sel .xlsx bisa berisi teks, angka, atau tanggal. Tanggal diambil
        /// sebagai ISO supaya usia dan rekapitulasi penduduk tidak salah hitung.
        ///
        /// Excel menyimpan tanggal sebagai nomor seri dan format selnya sering
        /// tetap "General", jadi angka dalam rentang tahun yang masuk akal
        /// (1913-2064) ikut dibaca sebagai tanggal. Rentang itu dipilih supaya
        /// kolom numerik lain tidak berubah arti: RT/RW (1-99) serta NIK, No KK,
        /// dan nomor HP yang jauh lebih besar tidak pernah dianggap tanggal.
        /// </summary>
        private static string TeksSel(ExcelRange? sel)
        {
            if (sel?.Value == null) return string.Empty;

            if (sel.Value is DateTime dt)
                return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            if (sel.Value is double angka && BisaSerialTanggal(angka, sel.Style.Numberformat.Format))
            {
                try
                {
                    return DateTime.FromOADate(angka)
                        .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                }
                catch (ArgumentException)
                {
                    // Di luar rentang tanggal OADate; biarkan sebagai teks.
                }
            }

            return Convert.ToString(sel.Value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private const double SerialTanggalMin = 5000;   // 1913-05-18
        private const double SerialTanggalMaks = 60000;  // 2064-03-15

        private static bool BisaSerialTanggal(double angka, string? format)
        {
            if (double.IsNaN(angka) || double.IsInfinity(angka)) return false;
            if (angka is >= SerialTanggalMin and <= SerialTanggalMaks) return true;

            // Seri di luar rentang itu tetap diterima bila format selnya jelas
            // tanggal, misalnya angka tahun 1900-an yang ditulis manual.
            if (string.IsNullOrEmpty(format)) return false;

            string f = format.ToLowerInvariant();
            return f.Contains('y') || f.Contains('d') || f.Contains('h');
        }

        private static List<string[]> BacaCsv(string path)
        {
            // Excel di Windows sering menulis CSV dengan pemisah ';' dan awalan BOM.
            using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            string isi = reader.ReadToEnd();

            var hasil = PemisahBaris(isi, TebakPemisah(isi));
            if (hasil.Count == 0) throw new InvalidDataException("Berkas CSV kosong.");
            return hasil;
        }

        /// <summary>
        /// Excel Indonesia memakai ';', daftar luar negeri biasanya ','. Kalau
        /// keduanya muncul, pilih yang paling banyak — kata Indonesia lebih
        /// sering mengandung koma daripada titik koma.
        /// </summary>
        private static char TebakPemisah(string isi)
        {
            int batas = Math.Min(isi.Length, 8192);
            int koma = 0, titikKoma = 0;
            for (int i = 0; i < batas; i++)
            {
                if (isi[i] == ',') koma++;
                else if (isi[i] == ';') titikKoma++;
            }

            return titikKoma > koma ? ';' : ',';
        }

        /// <summary>
        /// Pemecah CSV sesuai RFC 4180: field berpapit boleh memuat pemisah,
        /// tanda kutip ganda ("\" di dalam "" berarti satu tanda kutip), dan
        /// baris baru. Ditulis manual karena proyek tidak memakai pustaka CSV.
        /// </summary>
        internal static List<string[]> PemisahBaris(string isi, char pemisah)
        {
            var hasil = new List<string[]>();
            var field = new StringBuilder();
            var baris = new List<string>();
            bool dalamKutip = false;

            void AkhiriBaris()
            {
                baris.Add(field.ToString());
                field.Clear();
                if (baris.Any(v => v.Trim().Length > 0))
                    hasil.Add(baris.ToArray());
                baris.Clear();
            }

            for (int i = 0; i < isi.Length; i++)
            {
                char ch = isi[i];

                if (dalamKutip)
                {
                    if (ch != '"') { field.Append(ch); continue; }

                    if (i + 1 < isi.Length && isi[i + 1] == '"') { field.Append('"'); i++; }
                    else dalamKutip = false;
                    continue;
                }

                if (ch == '"') { dalamKutip = true; continue; }
                if (ch == pemisah) { baris.Add(field.ToString()); field.Clear(); continue; }
                if (ch == '\r') continue;
                if (ch == '\n') { AkhiriBaris(); continue; }

                field.Append(ch);
            }

            if (field.Length > 0 || baris.Count > 0) AkhiriBaris();

            return hasil;
        }


    }
}
