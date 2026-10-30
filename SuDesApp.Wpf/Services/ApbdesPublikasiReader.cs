using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SuDesApp.Wpf.Services
{
    /// <summary>
    /// Satu baris ringkasan publikasi (per desa &amp; tahun) yang terbaca dari berkas:
    /// angka yang tidak ada pada berkas dibiarkan <c>null</c> — tidak dipaksa nol.
    /// </summary>
    public sealed class BarisPublikasi
    {
        public int Tahun { get; set; }
        public string NamaDesa { get; set; } = "";
        public string KodeDesa { get; set; } = "";
        public long? AnggaranPendapatan { get; set; }
        public long? RealisasiPendapatan { get; set; }
        public long? AnggaranBelanja { get; set; }
        public long? RealisasiBelanja { get; set; }
        public long? AnggaranPembiayaan { get; set; }
        public long? RealisasiPembiayaan { get; set; }
        public long? DanaDesa { get; set; }

        public bool PunyaAngka =>
            AnggaranPendapatan.HasValue || RealisasiPendapatan.HasValue ||
            AnggaranBelanja.HasValue || RealisasiBelanja.HasValue ||
            AnggaranPembiayaan.HasValue || RealisasiPembiayaan.HasValue ||
            DanaDesa.HasValue;
    }

    /// <summary>Hasil membaca satu berkas publikasi.</summary>
    public sealed class HasilBacaPublikasi
    {
        public List<BarisPublikasi> Baris { get; } = new();
        public List<string> KolomDikenali { get; } = new();
        public List<string> KolomDilewati { get; } = new();
        public string NamaBerkas { get; init; } = "";
    }

    /// <summary>
    /// Pembaca berkas data APBDes yang dipublikasikan pemerintah (Excel .xlsx atau
    /// CSV): kepala kolomnya dinormalisasi lalu dipetakan ke metrik standar
    /// (pendapatan, belanja, pembiayaan, Dana Desa) per desa per tahun.
    ///
    /// Latar: portal pusat (PELITA Kemendagri, SIKD Dana Desa, PPID) menampilkan
    /// data tetapi tidak membuka API unduhan programatik, jadi jalur yang andal
    /// adalah pengguna mengunduh berkas publikasinya lalu mengimpornya di sini.
    /// .xlsx dibaca SudesExPlus, pustaka Excel milik proyek ini.
    /// </summary>
    public static class ApbdesPublikasiReader
    {
        private const int TahunMinimum = 2000;

        // Urutan alias berpengaruh: alias yang lebih spesifik ditulis lebih dulu
        // dan dicari sebelनnya; alias generik ("pendapatan", "belanja") hanya
        // dipakai bila kolom lain tidak lebih pas.
        private static readonly (string Slot, string[] Alias)[] PetaKolom =
        {
            ("tahun", new[] { "tahun anggaran", "tahun", "ta" }),
            ("kodeDesa", new[] { "kode desa", "kode wilayah desa", "kode desa kemendagri", "kode wilayah" }),
            ("namaDesa", new[] { "nama desa", "desa", "nama wilayah" }),
            ("realisasiPendapatan", new[] { "realisasi pendapatan", "realisasi pendapatan desa" }),
            ("anggaranPendapatan", new[] { "anggaran pendapatan", "pendapatan dianggarkan", "pendapatan desa", "pendapatan" }),
            ("realisasiBelanja", new[] { "realisasi belanja", "realisasi belanja desa" }),
            ("anggaranBelanja", new[] { "anggaran belanja", "belanja dianggarkan", "belanja desa", "belanja" }),
            ("realisasiPembiayaan", new[] { "realisasi pembiayaan", "realisasi pembiayaan desa" }),
            ("anggaranPembiayaan", new[] { "anggaran pembiayaan", "pembiayaan neto", "pembiayaan desa", "pembiayaan" }),
            ("danaDesa", new[] { "dana desa", "alokasi dana desa", "add" }),
        };

        /// <summary>Alias slot yang paling lama cocok secara awalan (dipakai bila kepala kolom berawalan sama).</summary>
        private static string? CocokkanSlot(string kepala)
        {
            // Kecocokan persis mendahului: beragam ejaan resmi umumnya sama persis.
            foreach (var (slot, alias) in PetaKolom)
            {
                if (alias.Contains(kepala, StringComparer.Ordinal)) return slot;
            }

            // Tanpa kecocokan persis, alias terpanjang yang menjadi awalan dipilih
            // (mis. "pendapatan (desa)" dinormalisasi "pendapatan desa" persis dikenal
            // di atas; "pendapatan sendiri" tidak persis, lalu menjadi awalan).
            string? terbaik = null; string slotTerbaik = "";
            foreach (var (slot, alias) in PetaKolom)
            {
                foreach (var a in alias)
                {
                    if (a.Length < 6) continue;
                    if (terbaik != null && a.Length <= terbaik.Length) continue;
                    if (kepala.StartsWith(a, StringComparison.Ordinal))
                    {
                        terbaik = a; slotTerbaik = slot;
                    }
                }
            }
            return terbaik == null ? null : slotTerbaik;
        }

        /// <summary>
        /// Baca berkas publikasi: Excel (.xlsx) atau CSV. Kelainan format atau
        /// kepala kolom yang tidak dikenali dilaporkan lewat hasil (bukan galat)
        /// selama setidaknya satu baris berangka ditemukan.
        /// </summary>
        public static HasilBacaPublikasi Baca(string jalur)
        {
            string ext = Path.GetExtension(jalur).ToLowerInvariant();
            if (ext == ".csv" || ext == ".txt")
            {
                return BacaCsv(jalur);
            }
            if (ext == ".xlsx" || ext == ".xls")
            {
                return BacaExcel(jalur);
            }
            throw new NotSupportedException(
                "Format berkas publikasi tidak didukung: gunakan Excel (.xlsx) atau CSV (.csv).");
        }

        // =====================================================================
        // CSV
        // =====================================================================

        internal static HasilBacaPublikasi BacaCsv(string jalur)
        {
            string isi = File.ReadAllText(jalur);
            var barisTeks = PisahCsv(isi);
            return BacaTabel(Path.GetFileName(jalur), () => barisTeks);
        }

        /// <summary>Pemisah CSV terdeteksi dari baris pertama (koma, titik koma, atau tab).</summary>
        internal static IEnumerable<string[]> PisahCsv(string isi)
        {
            char pemisah = DeteksiPemisah(isi);
            var hasil = new List<string[]>();
            var berjalan = new List<string>();

            int i = 0;
            while (i < isi.Length)
            {
                int[] col = new int[1];
                while (i < isi.Length && isi[i] != '\n' && isi[i] != '\r')
                {
                    if (isi[i] == '"')
                    {
                        i++;
                        var kutip = new StringBuilder();
                        while (i < isi.Length)
                        {
                            if (isi[i] == '"')
                            {
                                if (i + 1 < isi.Length && isi[i + 1] == '"')
                                {
                                    kutip.Append('"'); i += 2; continue;
                                }
                                i++; break;
                            }
                            kutip.Append(isi[i]); i++;
                        }
                        berjalan.Add(kutip.ToString());
                    }
                    else if (isi[i] == pemisah)
                    {
                        berjalan.Add(string.Empty); i++;
                    }
                    else
                    {
                        var mulai = i;
                        while (i < isi.Length && isi[i] != pemisah && isi[i] != '\n' && isi[i] != '\r')
                        {
                            i++;
                        }
                        berjalan.Add(isi.Substring(mulai, i - mulai));
                    }
                }

                hasil.Add(berjalan.ToArray());
                berjalan.Clear();

                if (i >= isi.Length) break;
                if (isi[i] == '\r') i++;
                if (i < isi.Length && isi[i] == '\n') i++;
            }

            return hasil;
        }

        private static char DeteksiPemisah(string isi)
        {
            int titikKoma = 0, koma = 0, tab = 0;
            foreach (char ch in isi)
            {
                if (ch == ';') titikKoma++;
                else if (ch == ',') koma++;
                else if (ch == '\t') tab++;
            }
            if (titikKoma >= koma && titikKoma >= tab && titikKoma > 0) return ';';
            if (tab >= koma && tab > 0) return '\t';
            return ',';
        }

        // =====================================================================
        // Excel (.xlsx)
        // =====================================================================

        internal static HasilBacaPublikasi BacaExcel(string jalur)
        {
            using var paket = new SudesExPlus.ExcelPackage(new FileInfo(jalur));
            var lembar = paket.Workbook.Worksheets.FirstOrDefault(w => w.Dimension != null)
                         ?? throw new InvalidDataException("Lembar kerja Excel kosong.");

            int barisAkhir = lembar.Dimension.End.Row;
            int kolomAkhir = lembar.Dimension.End.Column;
            if (barisAkhir > 10_000) barisAkhir = 10_000;
            if (kolomAkhir > 40) kolomAkhir = 40;

            var hasil = BacaTabel(Path.GetFileName(jalur),
                () => BacaSel(lembar, barisAkhir, kolomAkhir));
            return hasil;
        }

        private static IEnumerable<string[]> BacaSel(
            SudesExPlus.ExcelWorksheet lembar, int barisAkhir, int kolomAkhir)
        {
            for (int r = 1; r <= barisAkhir; r++)
            {
                var sel = new string[kolomAkhir];
                for (int c = 1; c <= kolomAkhir; c++)
                {
                    sel[c - 1] = lembar.Cells[r, c].Text ?? "";
                }
                yield return sel;
            }
        }

        // =====================================================================
        // Tabel generik (kepala + baris data) — dipakai CSV dan Excel.
        // =====================================================================

        /// <summary>
        /// Kepala tabel dicari di antara 10 baris pertama: baris dengan slot
        /// terbanyak (minimal "tahun" dan satu metrik) dipilih. Kepala kolom
        /// ternormalisasi huruf kecil tanpa tanda baca.
        /// </summary>
        internal static HasilBacaPublikasi BacaTabel(
            string namaBerkas, Func<IEnumerable<string[]>> ambilTabel)
        {
            var hasil = new HasilBacaPublikasi { NamaBerkas = namaBerkas };
            var tabel = ambilTabel().ToList();

            int barisKepala = -1;
            string[] peta = Array.Empty<string>();
            int gunaTerbanyak = -1;

            int batasKepala = Math.Min(tabel.Count, 10);
            for (int kandidat = 0; kandidat < batasKepala; kandidat++)
            {
                var kepala = tabel[kandidat];
                int lebar = Math.Min(kepala.Length, 40);
                var map = new string[lebar];
                int guna = 0;
                for (int c = 0; c < lebar; c++)
                {
                    map[c] = CocokkanSlot(Normalisasi(kepala[c])) ?? "";
                    if (map[c].Length > 0) guna++;
                }
                bool adaTahun = map.Contains("tahun");
                if (adaTahun && guna > gunaTerbanyak)
                {
                    gunaTerbanyak = guna;
                    barisKepala = kandidat;
                    peta = map;
                }
            }

            if (barisKepala < 0)
            {
                throw new InvalidDataException(
                    "Kepala kolom tidak dikenali: berkas harus punya kolom \"Tahun\" beserta " +
                    "anggaran/realisasi (mis. \"Anggaran Pendapatan\", \"Realisasi Belanja\", \"Dana Desa\").");
            }

            foreach (string slot in peta.Distinct().Where(s => s.Length > 0))
            {
                hasil.KolomDikenali.Add(slot);
            }

            for (int r = barisKepala + 1; r < tabel.Count; r++)
            {
                var b = tabel[r];
                int tahun = BacaTahun(b, peta);
                if (tahun < TahunMinimum) continue;

                var baris = new BarisPublikasi
                {
                    Tahun = tahun,
                    NamaDesa = Ambil(b, peta, "namaDesa").Trim(),
                    KodeDesa = Ambil(b, peta, "kodeDesa").Trim(),
                    AnggaranPendapatan = BacaAngka(Ambil(b, peta, "anggaranPendapatan")),
                    RealisasiPendapatan = BacaAngka(Ambil(b, peta, "realisasiPendapatan")),
                    AnggaranBelanja = BacaAngka(Ambil(b, peta, "anggaranBelanja")),
                    RealisasiBelanja = BacaAngka(Ambil(b, peta, "realisasiBelanja")),
                    AnggaranPembiayaan = BacaAngka(Ambil(b, peta, "anggaranPembiayaan")),
                    RealisasiPembiayaan = BacaAngka(Ambil(b, peta, "realisasiPembiayaan")),
                    DanaDesa = BacaAngka(Ambil(b, peta, "danaDesa")),
                };

                if (baris.PunyaAngka || baris.NamaDesa.Length > 0) hasil.Baris.Add(baris);
            }

            if (hasil.Baris.Count == 0)
            {
                throw new InvalidDataException(
                    "Tidak ada baris berangka yang terbaca: kolom tahun wajib berisi angka tahun " +
                    "anggaran (mis. 2024) dan minimal satu kolom nilai (pendapatan/belanja/pembiayaan/Dana Desa).");
            }

            return hasil;
        }

        private static string Ambil(IReadOnlyList<string> b, string[] peta, string slot)
        {
            if (b == null) return "";
            for (int c = 0; c < Math.Min(peta.Length, b.Count); c++)
            {
                if (peta[c] == slot) return b[c] ?? "";
            }
            return "";
        }

        private static int BacaTahun(IReadOnlyList<string> b, string[] peta)
        {
            string nilai = Ambil(b, peta, "tahun").Trim();
            // "2024/2025" dan "TA 2024" dipotong: ambil potongan 4 digit pertama.
            for (int i = 0; i + 4 <= nilai.Length; i++)
            {
                if (char.IsDigit(nilai[i]) && int.TryParse(nilai.Substring(i, 4), out int t))
                {
                    return t;
                }
            }
            return 0;
        }

        /// <summary>Teks sel jadi angka rupiah: "Rp1.234.567", "1.234.567,89", "1,234,567.89",
        /// "(1.234)" pun dinegatifkan, dan tanda hubung kosong menghasilkan null.</summary>
        internal static long? BacaAngka(string? teksSel)
        {
            string teks = (teksSel ?? "").Trim();
            if (teks.Length == 0) return null;
            teks = teks.Replace("Rp", "", StringComparison.OrdinalIgnoreCase).Trim();
            if (teks.Length == 0 || teks == "-") return null;

            teks = teks.Replace("\u2212", "-"); // minus Unicode juga dianggap negatif
            bool negatif = teks.StartsWith("(", StringComparison.Ordinal) && teks.EndsWith(")", StringComparison.Ordinal)
                           || teks.StartsWith("-", StringComparison.Ordinal);
            teks = teks.Trim('(', ')', '-', '+', ' ');

            bool adaKoma = teks.Contains(',', StringComparison.Ordinal);
            bool adaTitik = teks.Contains('.', StringComparison.Ordinal);

            // Pemisah desimal & ribuan: bila keduanya ada, yang terakhir adalah desimal
            // (mis. "1.234.567,89" ribuan titik desimal koma; "1,234,567.89" sebaliknya).
            char desimal = adaKoma && adaTitik
                ? (teks.LastIndexOf(',') > teks.LastIndexOf('.') ? ',' : '.')
                : adaKoma || adaTitik ? (adaKoma ? ',' : '.')
                : '\0';

            // Satu pemisah saja: tiga digit berturut setelahnya = ribuan ("1,234");
            // satu atau dua digit berturut setelahnya = desimal ("1,23" atau "1,2").
            if (desimal != '\0' && !(adaKoma && adaTitik))
            {
                int pos = desimal == ',' ? teks.LastIndexOf(',') : teks.LastIndexOf('.');
                int strail = teks.Length - pos - 1;
                if (strail == 3)
                {
                    desimal = '\0';
                }
                else if (strail is 1 or 2)
                {
                    // desimal dipertahankan pada dasarnya
                }
                else
                {
                    // beberapa pemisah dalam satu sel (mis. 1,234.567) dengan bentuk lain
                    desimal = '\0';
                }
            }

            string angka = teks.Replace(" ", "");

            if (desimal != '\0')
            {
                char ribuan = desimal == ',' ? '.' : ',';
                angka = angka.Replace(ribuan.ToString(), string.Empty); // buang pemisah ribuan
                angka = angka.Replace(desimal, '.');  // pakai titik sebagai desimal
            }
            else
            {
                angka = angka.Replace(',', '.').Replace(".", ""); // semua pemisah ribuan dibuang
            }
            // Bila desimal hadir, bagian desimalnya dihitung lalu dibulatkan ke rupiah.
            if (desimal != '\0' && double.TryParse(angka, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double nilaiDesimal))
            {
                // Pembulatan rupiah: 0,5 ke atas, bukan pembulatan genap (banker's).
                return (long)Math.Abs(Math.Round(nilaiDesimal, MidpointRounding.AwayFromZero)) * (negatif ? -1 : 1);
            }

            string bilang = new string(angka.Where(char.IsDigit).ToArray());
            return bilang.Length == 0 ? null : long.Parse(bilang, NumberStyles.Integer, CultureInfo.InvariantCulture) * (negatif ? -1 : 1);
        }

        /// <summary>Kepala kolom jadi huruf kecil tanpa tanda baca dan tanpa spasi ganda.</summary>
        internal static string Normalisasi(string? teks)
        {
            var simbol = new StringBuilder();
            foreach (char ch in teks ?? "")
            {
                if (char.IsLetterOrDigit(ch)) simbol.Append(char.ToLowerInvariant(ch));
                else if (simbol.Length > 0 && simbol[simbol.Length - 1] != ' ') simbol.Append(' ');
            }
            return simbol.ToString().Trim();
        }
    }
}
