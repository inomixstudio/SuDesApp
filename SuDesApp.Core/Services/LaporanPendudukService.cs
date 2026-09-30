using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using QuestPDF.Fluent;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.GeneratorPdf;

namespace SuDesApp.Services
{
    /// <summary>
    /// Laporan kependudukan: satu titik masuk untuk menyusun, lalu menampilkan
    /// laporan sebagai PDF maupun Excel. Kedua keluaran memakai
    /// <see cref="LaporanPendudukData"/> yang sama, jadi angka di layar, di PDF, dan
    /// di Excel dijamin identik.
    /// </summary>
    public interface ILaporanPendudukService
    {
        /// <summary>
        /// Hitung angka dan susun tabel laporan. <paramref name="keteranganFilter"/>
        /// dicetak di kop sebagai penjelasan cakupan data.
        /// </summary>
        Task<LaporanPendudukData> SusunAsync(
            string? keteranganFilter = null,
            IReadOnlyList<JenisTabelLaporan>? tabelTermasuk = null,
            DateTime? saatCetak = null);

        /// <summary>Byte PDF siap disimpan. Never null; byte kosong bila tidak ada data.</summary>
        Task<byte[]> RenderPdfAsync(LaporanPendudukData data);

        /// <summary>Byte .xlsx: satu sheet ringkasan + satu sheet per tabel.</summary>
        Task<byte[]> RenderExcelAsync(LaporanPendudukData data);

        /// <summary>Susun lalu render PDF dalam satu langkah.</summary>
        Task<byte[]> BuatPdfAsync(
            string? keteranganFilter = null,
            IReadOnlyList<JenisTabelLaporan>? tabelTermasuk = null,
            DateTime? saatCetak = null);

        /// <summary>Susun lalu render Excel dalam satu langkah.</summary>
        Task<byte[]> BuatExcelAsync(
            string? keteranganFilter = null,
            IReadOnlyList<JenisTabelLaporan>? tabelTermasuk = null,
            DateTime? saatCetak = null);
    }

    public class LaporanPendudukService : ILaporanPendudukService
    {
        private readonly IWargaRepository _warga;
        private readonly IDesaRepository _desa;
        private readonly ILogger<LaporanPendudukService> _logger;

        public LaporanPendudukService(
            IWargaRepository warga,
            IDesaRepository desa,
            ILogger<LaporanPendudukService> logger)
        {
            _warga = warga;
            _desa = desa;
            _logger = logger;
        }

        public async Task<LaporanPendudukData> SusunAsync(
            string? keteranganFilter = null,
            IReadOnlyList<JenisTabelLaporan>? tabelTermasuk = null,
            DateTime? saatCetak = null)
        {
            var ringkasan = await _warga.GetStatistikWargaAsync().ConfigureAwait(false);

            // Kop laporan tidak boleh gagal hanya karena data desa belum diisi.
            // Tanpa data desa, laporan tetap terbit dengan nama wilayah kosong —
            // lebih berguna daripada tidak terbit sama sekali.
            var desa = await AmbilDesaAsync().ConfigureAwait(false);

            return LaporanPendudukBuilder.Susun(ringkasan, desa, saatCetak, keteranganFilter, tabelTermasuk);
        }

        private async Task<DesaData> AmbilDesaAsync()
        {
            try
            {
                return await _desa.GetInfoDesaAsync().ConfigureAwait(false) ?? new DesaData();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal memuat data desa; laporan terbit tanpa nama wilayah.");
                return new DesaData();
            }
        }

        public async Task<byte[]> RenderPdfAsync(LaporanPendudukData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            using var penampung = new MemoryStream();
            new LaporanPendudukGenerator(data.Desa).GenerateLaporanPdf(penampung, data);
            return penampung.ToArray();
        }

        public async Task<byte[]> RenderExcelAsync(LaporanPendudukData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            ExcelPackage.License.SetNonCommercialPersonal("ARIE INO");

            using var package = new ExcelPackage();

            TulisSheetRingkasan(package, data);
            foreach (var tabel in data.TabelTerisi)
            {
                TulisSheetTabel(package, tabel, data);
            }

            using var penampung = new MemoryStream();
            await package.SaveAsAsync(penampung).ConfigureAwait(false);
            return penampung.ToArray();
        }

        public async Task<byte[]> BuatPdfAsync(
            string? keteranganFilter = null,
            IReadOnlyList<JenisTabelLaporan>? tabelTermasuk = null,
            DateTime? saatCetak = null)
        {
            var data = await SusunAsync(keteranganFilter, tabelTermasuk, saatCetak).ConfigureAwait(false);
            return await RenderPdfAsync(data).ConfigureAwait(false);
        }

        public async Task<byte[]> BuatExcelAsync(
            string? keteranganFilter = null,
            IReadOnlyList<JenisTabelLaporan>? tabelTermasuk = null,
            DateTime? saatCetak = null)
        {
            var data = await SusunAsync(keteranganFilter, tabelTermasuk, saatCetak).ConfigureAwait(false);
            return await RenderExcelAsync(data).ConfigureAwait(false);
        }

        // ---------------------------------------------------------------------
        // Excel
        // ---------------------------------------------------------------------

        private static void TulisSheetRingkasan(ExcelPackage package, LaporanPendudukData data)
        {
            var ws = package.Workbook.Worksheets.Add("Ringkasan");
            var r = data.Ringkasan;
            int penduduk = r.TotalAktif + r.TotalBaru;

            Judul(ws, 1, data.Judul, 3);
            ws.Cells[2, 1].Value = $"{data.Desa.NamaDesa} — {data.Periode}";

            int baris = 4;
            Baris(ws, baris++, new[] { "Uraian", "Jumlah", "Persentase" }, tebal: true, biru: true);
            Tambah(ws, ref baris, "Penduduk (tinggal di desa)", penduduk, r.TotalSeluruh);
            Tambah(ws, ref baris, "Laki-laki", r.LakiLakiPenduduk, penduduk);
            Tambah(ws, ref baris, "Perempuan", r.PerempuanPenduduk, penduduk);

            // Selaras dengan tabel Ringkasan: penduduk yang jenis kelaminnya belum
            // diisi barisnya sendiri, bukan diam-diam tidak terhitung.
            if (r.PendudukJenisKelaminTidakDiketahui > 0)
            {
                Tambah(ws, ref baris, "Jenis kelamin belum diisi", r.PendudukJenisKelaminTidakDiketahui, penduduk);
            }

            Tambah(ws, ref baris, "Kepala Keluarga", r.JumlahKepalaKeluarga, r.JumlahKepalaKeluarga);
            Tambah(ws, ref baris, "Kepala Keluarga laki-laki", r.KepalaKeluargaLakiLaki, r.JumlahKepalaKeluarga);
            Tambah(ws, ref baris, "Kepala Keluarga wanita", r.KepalaKeluargaPerempuan, r.JumlahKepalaKeluarga);
            Tambah(ws, ref baris, "Warga pindah", r.TotalPindah, r.TotalSeluruh);
            Tambah(ws, ref baris, "Warga meninggal", r.TotalMeninggal, r.TotalSeluruh);
            Tambah(ws, ref baris, "Jumlah seluruh warga terdata", r.TotalSeluruh, r.TotalSeluruh);

            ws.Column(1).Width = 34;
            ws.Column(2).Width = 14;
            ws.Column(3).Width = 14;
        }

        private static void TulisSheetTabel(ExcelPackage package, LaporanTabel tabel, LaporanPendudukData data)
        {
            var ws = package.Workbook.Worksheets.Add(
                NamaSheetUnik(NamaSheetAman(tabel.Judul), package));

            Judul(ws, 1, tabel.Judul, tabel.JudulKolom.Count + 1);
            ws.Cells[2, 1].Value = $"{data.Desa.NamaDesa} — {data.Periode}";

            int baris = 4;
            var header = new List<string> { tabel.LabelKolom };
            header.AddRange(tabel.JudulKolom);
            Baris(ws, baris++, header.ToArray(), tebal: true, biru: true);

            foreach (var b in tabel.Baris)
            {
                Baris(ws, baris++, b, tebal: false, biru: false);
            }

            if (tabel.BarisTotal is { Length: > 0 })
            {
                Baris(ws, baris++, tabel.BarisTotal, tebal: true, biru: false);
            }

            if (!string.IsNullOrWhiteSpace(tabel.Catatan))
            {
                ws.Cells[baris + 1, 1].Value = tabel.Catatan;
                ws.Cells[baris + 1, 1].Style.Font.Italic = true;
            }

            ws.Column(1).Width = 36;
            for (int c = 2; c <= tabel.JudulKolom.Count + 1; c++) ws.Column(c).Width = 20;
        }

        private static void Judul(ExcelWorksheet ws, int baris, string teks, int jumlahKolom)
        {
            ws.Cells[baris, 1].Value = teks;
            ws.Cells[baris, 1, baris, jumlahKolom].Merge = true;
            ws.Cells[baris, 1].Style.Font.Bold = true;
            ws.Cells[baris, 1].Style.Font.Size = 14;
            ws.Cells[baris, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        }

        private static void Baris(ExcelWorksheet ws, int baris, string?[] isi, bool tebal, bool biru)
        {
            for (int i = 0; i < isi.Length; i++)
            {
                ws.Cells[baris, i + 1].Value = isi[i];
            }

            var gaya = ws.Cells[baris, 1, baris, isi.Length];

            if (tebal) gaya.Style.Font.Bold = true;

            if (biru)
            {
                gaya.Style.Fill.PatternType = ExcelFillStyle.Solid;
                gaya.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(37, 99, 235));
                gaya.Style.Font.Color.SetColor(Color.White);
                gaya.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            }
        }

        private static void Tambah(ExcelWorksheet ws, ref int baris, string uraian, int jumlah, int total)
        {
            Baris(ws, baris++, new[]
            {
                uraian,
                jumlah.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("id-ID")),
                total <= 0 ? string.Empty
                    : (jumlah * 100.0 / total).ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("id-ID")) + " %"
            }, tebal: false, biru: false);
        }

        /// <summary>
        /// Nama worksheet Excel maksimal 31 karakter dan tidak boleh memuat
        /// karakter terlarang (<code>: \ / ? * [ ]</code>). Judul tabel hasil
        /// susunan sudah aman, tapi tetap dipangkas supaya aman juga kalau nanti
        /// judulnya diubah. Pemotongan memakai tanda baca "…" di akhir supaya
        /// terlihat bahwa nama masih terpotong, bukan salah ketik.
        /// </summary>
        private static string NamaSheetAman(string judul)
        {
            foreach (char terlarang in new[] { ':', '\\', '/', '?', '*', '[', ']' })
            {
                judul = judul.Replace(terlarang, '-');
            }

            judul = judul.Trim();
            if (judul.Length == 0) return "Tabel";

            const int batas = 31;
            if (judul.Length <= batas) return judul;

            // Sisakan 1 karakter untuk "…".
            return judul[..(batas - 1)].TrimEnd() + "…";
        }

        /// <summary>
        /// Dua judul tabel yang sama-sama panjang bisa terpotong jadi nama yang
        /// sama; Excel lalu menolak atau diam-diam mengganti nama. Pastikan
        /// setiap nama dalam workbook ini unik.
        /// </summary>
        private static string NamaSheetUnik(string nama, ExcelPackage package)
        {
            var ada = new HashSet<string>(
                package.Workbook.Worksheets.Select(w => w.Name), StringComparer.OrdinalIgnoreCase);

            if (ada.Add(nama)) return nama;

            for (int urutan = 2; urutan < 100; urutan++)
            {
                var akhiran = " " + urutan.ToString();
                // Sisakan ruang untuk akhiran angka.
                var awalan = nama.Length > 31 - akhiran.Length
                    ? nama[..(31 - akhiran.Length)].TrimEnd()
                    : nama;
                var kandidat = awalan + akhiran;
                if (ada.Add(kandidat)) return kandidat;
            }

            return nama;
        }
    }
}
