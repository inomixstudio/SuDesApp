using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SudesPdf.Core.Layout;
using SuDesApp.Data.Models;

namespace SuDesApp.GeneratorPdf
{
    /// <summary>
    /// Mencetak naskah Perdes berhalaman memakai mesin PDF aplikasi sendiri.
    /// Sumbernya data naskah (bukan berkas Word), jadi halaman dapat disunting
    /// di formulir lalu langsung diterbitkan sebagai PDF.
    ///
    /// Susunan halaman mengikuti naskah peraturan desa: lambang negara, judul,
    /// nomor/tahun, Menimbang–Mengingat, BAB/Pasal, lalu blok penetapan dan
    /// tanda tangan. Nomor, tahun, dan tanggal diambil dari isian formulir agar
    /// naskah yang terbit tidak membawa nomor/tanggal contoh.
    /// </summary>
    public sealed class NaskahPerdesGenerator
    {
        private readonly DesaData _desa;
        private readonly NaskahPerdes _naskah;
        private readonly string _nomor;
        private readonly DateTime _tanggal;

        public NaskahPerdesGenerator(DesaData desa, NaskahPerdes naskah, string nomor, DateTime tanggal)
        {
            _desa = KopSurat.DesaBersih(desa ?? new DesaData());
            _naskah = naskah ?? throw new ArgumentNullException(nameof(naskah));
            _nomor = nomor ?? string.Empty;
            _tanggal = tanggal;
        }

        public void GeneratePdf(Stream outputStream)
        {
            ArgumentNullException.ThrowIfNull(outputStream);
            new NaskahPerdesDocument(_desa, _naskah, _nomor, _tanggal).GeneratePdf(outputStream);
        }

        public byte[] Buat()
        {
            using var stream = new MemoryStream();
            GeneratePdf(stream);
            return stream.ToArray();
        }

        private sealed class NaskahPerdesDocument : IDocument
        {
            private static readonly Regex Bab = new(@"^(BAB\s+[IVX]+)(?![IVX])\s*(.*)$", RegexOptions.Compiled);
            private static readonly Regex Pasal = new(@"^(Pasal\s+\d+)\b\s*(.*)$", RegexOptions.Compiled);
            private static readonly Regex Butir = new(
                @"^(?<label>(?:[a-zA-Z]|\d+|[ivxlcdmIVXLCDM]+)\.)\s+(?<isi>.+)$", RegexOptions.Compiled);
            private static readonly Regex Ayat = new(
                @"^(?<label>\([0-9a-zA-Z]+\))\s+(?<isi>.+)$", RegexOptions.Compiled);
            private static readonly Regex LabelBagian = new(
                @"^(Menimbang|Mengingat|Memperhatikan|Menetapkan)\b", RegexOptions.Compiled);
            private static readonly Regex TanggalPenetapan = new(
                @"(?<=[Pp]ada tanggal\s+)\d{1,2} [A-Za-z]+ \d{4}", RegexOptions.Compiled);
            private static readonly Regex TanggalBerlabel = new(
                @"(?<=Pada tanggal\s*:\s*)\d{1,2} [A-Za-z]+ \d{4}", RegexOptions.Compiled);
            private static readonly Regex TanggalPengundangan = new(
                @"(?<=Diundangkan pada tanggal\s+)\d{1,2} [A-Za-z]+ \d{4}", RegexOptions.Compiled);

            /// <summary>
            /// Batas panjang judul + isi pertama yang ditempel jadi satu kotak
            /// utuh. Di atas batas ini isi dicetak terpisah agar tidak ada kotak
            /// yang lebih tinggi dari satu halaman.
            /// </summary>
            private const int BatasJudulDenganIsi = 1200;

            private readonly DesaData _desa;
            private readonly NaskahPerdes _naskah;
            private readonly string _nomor;
            private readonly DateTime _tanggal;

            public NaskahPerdesDocument(DesaData desa, NaskahPerdes naskah, string nomor, DateTime tanggal)
            {
                _desa = desa;
                _naskah = naskah;
                _nomor = nomor;
                _tanggal = tanggal;
            }

            public DocumentMetadata GetMetadata() => new()
            {
                Title = string.IsNullOrWhiteSpace(_naskah.Judul) ? "Peraturan Desa" : _naskah.Judul,
                Subject = "Peraturan Desa",
                Creator = "SuDesApp"
            };

            public void Compose(IDocumentContainer container)
            {
                var berisi = _naskah.Halaman.Where(h => !h.Kosong).ToList();
                if (berisi.Count == 0)
                {
                    container.Page(halaman =>
                    {
                        SiapkanHalaman(halaman);
                        halaman.Content().Column(kolom =>
                            kolom.Item().AlignCenter().Text("Naskah belum diisi.").Italic().FontSize(10));
                    });
                    return;
                }

                bool halamanPertama = true;
                foreach (var bagian in berisi)
                {
                    bool pakaiLambang = halamanPertama;
                    container.Page(halaman =>
                    {
                        SiapkanHalaman(halaman);
                        halaman.Content().Column(kolom =>
                        {
                            if (pakaiLambang) NaskahDesa.LambangNegara(kolom);
                            TulisHalaman(kolom, bagian, pakaiLambang);
                        });
                    });
                    halamanPertama = false;
                }
            }

            private static void SiapkanHalaman(PageDescriptor halaman)
            {
                halaman.Size(NaskahDesa.UkuranHalaman());
                halaman.MarginTop(48);
                halaman.MarginRight(54);
                halaman.MarginBottom(54);
                halaman.MarginLeft(54);
                // Jarak baris naskah peraturan: 1,08 terlalu rapat sehingga paragraf
                // panjang terlihat padat dan sulit dibaca saat dicetak.
                halaman.DefaultTextStyle(x => x
                    .FontFamily("Times New Roman")
                    .FontSize(NaskahDesa.UkuranTeks)
                    .LineHeight(NaskahDesa.JarakBarisTeks));

                halaman.Footer().AlignCenter().DefaultTextStyle(s => s.FontSize(9)).Text(t =>
                {
                    t.Span("- ");
                    t.CurrentPageNumber();
                    t.Span(" -");
                });
            }

            private void TulisHalaman(
                ColumnDescriptor kolom, NaskahPerdesHalaman halaman, bool halamanPertama)
            {
                string isi = IsiDenganIsian(halaman, halamanPertama);
                string[] baris = isi
                    .Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Replace('\r', '\n')
                    .Split('\n');

                bool penutup = isi.Contains("Ditetapkan di", StringComparison.OrdinalIgnoreCase);

                // Halaman penutup punya susunan sendiri (dua lajur tanda tangan),
                // bukan rentetan paragraf rata kiri.
                if (penutup)
                {
                    TulisHalamanPenutup(kolom, baris);
                    return;
                }

                bool pembuka = !baris.Any(b => Bab.IsMatch(b.Trim()) || Pasal.IsMatch(b.Trim()));
                bool judulTentang = false;
                bool setelahBab = false;
                bool adaIsi = false;

                // Judul menunggu isi pertamanya supaya tidak tertinggal sendirian
                // di dasar halaman. Penempatan utuh hanya dipakai bila isi
                // pengikutnya pendek; paragraf panjang tetap boleh terpotong.
                var tertunda = new List<(JenisJudul Jenis, string Teks)>();

                foreach (string mentah in baris)
                {
                    string b = mentah.Trim();
                    if (b.Length == 0)
                    {
                        if (adaIsi) kolom.Item().HeightSpacer(6);
                        continue;
                    }

                    adaIsi = true;

                    if (pembuka && LabelBagian.IsMatch(b))
                    {
                        pembuka = false;
                        kolom.Item().PaddingTop(6).Text(b).Bold();
                        continue;
                    }

                    if (pembuka)
                    {
                        TulisBarisPembuka(kolom, b, ref judulTentang);
                        continue;
                    }

                    // Penutup pembukaan (persetujuan bersama / MEMUTUSKAN) muncul
                    // setelah daftar Menimbang–Mengingat, tetapi tetap rata tengah.
                    if (halamanPertama && !penutup && BarisPenutupPembuka(b))
                    {
                        bool tebal = b.ToUpperInvariant().StartsWith("MEMUTUSKAN", StringComparison.Ordinal);
                        if (tebal)
                        {
                            kolom.Item().PaddingTop(6).AlignCenter().Text(b).Bold();
                        }
                        else
                        {
                            kolom.Item().PaddingTop(4).AlignCenter().Text(b);
                        }
                        continue;
                    }

                    if (Bab.Match(b) is { Success: true } bab)
                    {
                        tertunda.Add((JenisJudul.Bab, bab.Groups[1].Value));
                        string judul = bab.Groups[2].Value.Trim();
                        if (judul.Length > 0) tertunda.Add((JenisJudul.JudulBab, judul));
                        setelahBab = true;
                        continue;
                    }

                    if (setelahBab)
                    {
                        tertunda.Add((JenisJudul.JudulBab, b));
                        setelahBab = false;
                        continue;
                    }

                    if (Pasal.Match(b) is { Success: true } pasal)
                    {
                        tertunda.Add((JenisJudul.Pasal, pasal.Groups[1].Value));
                        string sisa = pasal.Groups[2].Value.Trim();
                        if (sisa.Length > 0) Keluarkan(kolom, tertunda, sisa, penutup);
                        continue;
                    }

                    Keluarkan(kolom, tertunda, b, penutup);
                }

                // Bagian yang berakhir dengan judul saja tetap dicetak.
                foreach (var (jenis, teks) in tertunda) TulisJudul(kolom, jenis, teks);
            }

            // ============================================== halaman penutup

            /// <summary>
            /// Satu blok tanda tangan pada halaman penutup: baris tempat/tanggal,
            /// jabatan, lalu nama pejabat dengan ruang tanda tangan di antaranya.
            /// </summary>
            private sealed class BlokTandaTanganPenutup
            {
                public List<string> TempatTanggal { get; } = new();
                public string? Jabatan { get; set; }
                public string? Nama { get; set; }

                public bool Kosong => TempatTanggal.Count == 0 && Jabatan is null && Nama is null;
            }

            /// <summary>
            /// Halaman penutup Perdes: sisa isi pasal dicetak seperti biasa, lalu
            /// blok penetapan (kanan) dan pengundangan (kiri) disusun berdampingan
            /// dengan ruang tanda tangan. Sebelumnya seluruh baris penutup dicetak
            /// rata kiri berurutan: nama pejabat menempel di bawah jabatannya tanpa
            /// ruang tanda tangan, dan blok pengundangan tidak berada di kiri.
            /// </summary>
            private static void TulisHalamanPenutup(ColumnDescriptor kolom, string[] baris)
            {
                var kiri = new BlokTandaTanganPenutup();
                var kanan = new BlokTandaTanganPenutup();
                var lembaran = new List<string>();
                var sebelumPenutup = new List<string>();

                BlokTandaTanganPenutup? aktif = null;
                bool menungguNama = false;

                foreach (string mentah in baris)
                {
                    string b = mentah.Trim();
                    if (b.Length == 0) continue;

                    if (b.StartsWith("Ditetapkan", StringComparison.OrdinalIgnoreCase))
                    {
                        aktif = kanan;
                        menungguNama = false;
                        kanan.TempatTanggal.AddRange(PecahTempatTanggal(b));
                        continue;
                    }

                    if (b.StartsWith("Diundangkan", StringComparison.OrdinalIgnoreCase))
                    {
                        aktif = kiri;
                        menungguNama = false;
                        kiri.TempatTanggal.AddRange(PecahTempatTanggal(b));
                        continue;
                    }

                    if (b.StartsWith("LEMBARAN DESA", StringComparison.OrdinalIgnoreCase) ||
                        b.StartsWith("TAHUN ", StringComparison.OrdinalIgnoreCase) && lembaran.Count > 0)
                    {
                        lembaran.Add(b);
                        continue;
                    }

                    // Isi pasal yang masih tersisa di halaman penutup dicetak dulu.
                    if (aktif is null)
                    {
                        sebelumPenutup.Add(b);
                        continue;
                    }

                    if (b.StartsWith("Pada tanggal", StringComparison.OrdinalIgnoreCase))
                    {
                        aktif.TempatTanggal.Add(b);
                        menungguNama = false;
                        continue;
                    }

                    if (JabatanPenandaTangan(b))
                    {
                        bool kepala = b.Contains("KEPALA", StringComparison.OrdinalIgnoreCase) ||
                                      b.Contains("LURAH", StringComparison.OrdinalIgnoreCase);
                        aktif = kepala ? kanan : kiri;
                        aktif.Jabatan = b;
                        menungguNama = true;
                        continue;
                    }

                    if (menungguNama)
                    {
                        aktif.Nama = b;
                        menungguNama = false;
                        continue;
                    }

                    aktif.TempatTanggal.Add(b);
                }

                foreach (string b in sebelumPenutup) TulisIsi(kolom, b, penutup: false);

                if (!kiri.Kosong || !kanan.Kosong)
                {
                    // ShowEntire: kedua lajur tanda tangan tidak boleh terbelah
                    // antar halaman.
                    kolom.Item().PaddingTop(24).ShowEntire().Row(barisTandaTangan =>
                    {
                        barisTandaTangan.RelativeItem().Column(k => TulisBlokPenutup(k, kiri));
                        barisTandaTangan.ConstantItem(28);
                        barisTandaTangan.RelativeItem().Column(k => TulisBlokPenutup(k, kanan));
                    });
                }

                // Lembaran Desa berada di bawah blok pengundangan (lajur kiri).
                foreach (string b in lembaran)
                {
                    kolom.Item().PaddingTop(JarakLembaran).Text(b).FontSize(NaskahDesa.UkuranTeks - 2);
                }
            }

            private const float JarakLembaran = 10f;

            /// <summary>
            /// Pisahkan baris penetapan/pengundangan yang ditulis satu baris menjadi
            /// baris tempat dan baris tanggal, supaya tanggal tidak terpotong di tengah
            /// (mis. tinggal “2026” atau “Sukamaju” sendirian di baris kedua). Dua gaya
            /// naskah didukung:
            ///   “Ditetapkan di Sukamaju pada tanggal 2 November 2026”
            ///   “Diundangkan pada tanggal 2 November 2026 di Sukamaju”
            /// Baris yang sudah terpisah — mis. “Ditetapkan di : Sukamaju” —
            /// dikembalikan apa adanya.
            /// </summary>
            private static string[] PecahTempatTanggal(string baris)
            {
                const string Penanda = " pada tanggal";
                int mulai = baris.IndexOf(Penanda, StringComparison.OrdinalIgnoreCase);
                if (mulai <= 0) return new[] { baris };

                string tempat = baris[..mulai].Trim();
                string tanggal = ("pada tanggal" + baris[(mulai + Penanda.Length)..]).Trim();

                // “Diundangkan pada tanggal … di Sukamaju” → tempatnya “Diundangkan di Sukamaju”.
                int di = tanggal.LastIndexOf(" di ", StringComparison.OrdinalIgnoreCase);
                if (di > 0)
                {
                    tempat = tempat + tanggal[di..];
                    tanggal = tanggal[..di].TrimEnd();
                }

                return tempat.Length == 0 || tanggal.Length == 0
                    ? new[] { baris }
                    : new[] { tempat, tanggal };
            }

            /// <summary>Baris jabatan penanda tangan, mis. “KEPALA DESA SUKAMAJU,” atau “Ketua BPD,”.</summary>
            private static bool JabatanPenandaTangan(string baris)
            {
                string atas = baris.ToUpperInvariant();
                if (!atas.Contains("KEPALA", StringComparison.Ordinal) &&
                    !atas.Contains("LURAH", StringComparison.Ordinal) &&
                    !atas.Contains("SEKRETARIS", StringComparison.Ordinal) &&
                    !atas.Contains("BPD", StringComparison.Ordinal) &&
                    !atas.Contains("CAMAT", StringComparison.Ordinal))
                {
                    return false;
                }

                // Nama pejabat yang ditulis kapital juga mengandung nama desa, tetapi
                // bukan jabatan: jabatan selalu diakhiri koma atau ditulis kapital
                // penuh satu baris pendek tanpa tanda baca titik.
                return baris.EndsWith(",", StringComparison.Ordinal) || atas == baris;
            }

            private static void TulisBlokPenutup(ColumnDescriptor kolom, BlokTandaTanganPenutup blok)
            {
                if (blok.Kosong) return;

                foreach (string b in blok.TempatTanggal)
                {
                    kolom.Item().AlignCenter().Text(b).FontSize(NaskahDesa.UkuranTeks);
                }

                if (blok.Jabatan is { Length: > 0 } jabatan)
                {
                    var item = kolom.Item();
                    if (blok.TempatTanggal.Count > 0) item = item.PaddingTop(NaskahDesa.JarakBagian);
                    item.AlignCenter().Text(jabatan.ToUpperInvariant()).FontSize(NaskahDesa.UkuranTeks);
                }

                if (blok.Nama is { Length: > 0 } nama)
                {
                    // Ruang tanda tangan; nama pejabat ditebalkan, sedangkan
                    // titik-titik kosong dibiarkan apa adanya.
                    kolom.Item().Height(RuangTandaTanganPenutup);
                    var item = kolom.Item().AlignCenter().Text(nama).FontSize(NaskahDesa.UkuranTeks);
                    if (!nama.TrimStart().StartsWith('(')) item.Bold().Underline();
                }
            }

            private const float RuangTandaTanganPenutup = 46f;

            /// <summary>Jenis baris judul yang menunggu isi pengikutnya.</summary>
            private enum JenisJudul
            {
                Bab,
                JudulBab,
                Pasal
            }

            /// <summary>
            /// Cetak judul yang menunggu beserta isi pertamanya. Bila isi pengikut
            /// panjang (bisa melebihi satu halaman), judul dan isi dicetak terpisah
            /// agar isi tetap boleh mengalir lintas halaman.
            /// </summary>
            private static void Keluarkan(
                ColumnDescriptor kolom,
                List<(JenisJudul Jenis, string Teks)> tertunda,
                string isi,
                bool penutup)
            {
                if (tertunda.Count == 0)
                {
                    TulisIsi(kolom, isi, penutup);
                    return;
                }

                int panjang = isi.Length + tertunda.Sum(j => j.Teks.Length);
                if (panjang <= BatasJudulDenganIsi)
                {
                    var judul = tertunda.ToList();
                    kolom.Item().Element(blok => blok.Column(k =>
                    {
                        foreach (var (jenis, teks) in judul) TulisJudul(k, jenis, teks);
                        TulisIsi(k, isi, penutup);
                    })).ShowEntire();
                }
                else
                {
                    foreach (var (jenis, teks) in tertunda) TulisJudul(kolom, jenis, teks);
                    TulisIsi(kolom, isi, penutup);
                }

                tertunda.Clear();
            }

            private static void TulisJudul(ColumnDescriptor kolom, JenisJudul jenis, string teks)
            {
                switch (jenis)
                {
                    case JenisJudul.Bab:
                        kolom.Item().PaddingTop(NaskahDesa.JarakBagian)
                            .AlignCenter().Text(teks).Bold();
                        break;
                    case JenisJudul.JudulBab:
                        kolom.Item().PaddingBottom(NaskahDesa.JarakRapat)
                            .AlignCenter().Text(teks).Bold();
                        break;
                    default:
                        kolom.Item().PaddingTop(NaskahDesa.JarakParagraf)
                            .AlignCenter().Text(teks).Bold();
                        break;
                }
            }

            private static void TulisIsi(ColumnDescriptor kolom, string b, bool penutup)
            {
                if (penutup)
                {
                    kolom.Item().Text(b);
                    return;
                }

                if (Butir.Match(b) is { Success: true } butir)
                {
                    TulisButir(kolom, butir.Groups["label"].Value, butir.Groups["isi"].Value);
                    return;
                }

                if (Ayat.Match(b) is { Success: true } ayat)
                {
                    TulisButir(kolom, ayat.Groups["label"].Value, ayat.Groups["isi"].Value);
                    return;
                }

                TulisParagraf(kolom, b);
            }

            /// <summary>Baris penutup bagian pembuka yang tetap rata tengah.</summary>
            private static bool BarisPenutupPembuka(string baris)
            {
                string atas = baris.ToUpperInvariant();
                return atas.StartsWith("MEMUTUSKAN", StringComparison.Ordinal) ||
                       atas.StartsWith("DENGAN PERSETUJUAN BERSAMA", StringComparison.Ordinal) ||
                       atas.StartsWith("DENGAN KESEPAKATAN BERSAMA", StringComparison.Ordinal) ||
                       // "dan" penghubung antara BPD dan Kepala Desa tetap di blok
                       // tengah, walau naskah menulisnya dengan huruf kecil.
                       (atas == "DAN" || atas == "DAN," ||
                       atas.StartsWith("BADAN PERMUSYAWARATAN DESA ", StringComparison.Ordinal) ||
                       atas.StartsWith("KEPALA DESA ", StringComparison.Ordinal));
            }

            private static void TulisBarisPembuka(
                ColumnDescriptor kolom, string baris, ref bool judulTentang)
            {
                string atas = baris.ToUpperInvariant();

                if (atas.StartsWith("DENGAN RAHMAT", StringComparison.Ordinal) ||
                    atas.StartsWith("KEPALA DESA", StringComparison.Ordinal) ||
                    atas.StartsWith("DENGAN PERSETUJUAN", StringComparison.Ordinal) ||
                    atas.StartsWith("DENGAN KESEPAKATAN", StringComparison.Ordinal) ||
                    atas == "DAN" || atas == "DAN," ||
                    atas.StartsWith("BADAN PERMUSYAWARATAN DESA", StringComparison.Ordinal))
                {
                    judulTentang = false;
                    kolom.Item().AlignCenter().Text(baris);
                    return;
                }

                bool judulUtama = atas.StartsWith("PERATURAN DESA", StringComparison.Ordinal) ||
                                  atas.StartsWith("PERATURAN KEPALA DESA", StringComparison.Ordinal) ||
                                  atas.StartsWith("KEPUTUSAN BADAN PERMUSYAWARATAN", StringComparison.Ordinal);
                bool nomor = atas.StartsWith("NOMOR", StringComparison.Ordinal);
                bool tentang = atas == "TENTANG" || atas.StartsWith("TENTANG ", StringComparison.Ordinal);
                bool memutuskan = atas.StartsWith("MEMUTUSKAN", StringComparison.Ordinal);
                bool barisJudul = judulTentang;

                if (tentang) judulTentang = true;
                else if (atas.StartsWith("DENGAN RAHMAT", StringComparison.Ordinal)) judulTentang = false;

                // Lebar mengikuti area cetak (A4 atau F4), bukan angka tetap,
                // supaya judul panjang membungkus sesuai kertas yang dipilih.
                var item = kolom.Item().AlignCenter();
                if (judulUtama)
                {
                    item.Text(baris).Bold().FontSize(NaskahDesa.UkuranJudul);
                }
                else if (nomor || tentang || barisJudul || memutuskan)
                {
                    if (!tentang) judulTentang = barisJudul;
                    item.Text(baris).Bold();
                }
                else
                {
                    item.Text(baris);
                }
            }

            private static void TulisButir(ColumnDescriptor kolom, string label, string isi)
            {
                // Butir diberi jarak bawah tipis supaya daftar a., b., c. tidak
                // menempel menjadi satu blok.
                kolom.Item().PaddingBottom(NaskahDesa.JarakRapat).Row(baris =>
                {
                    baris.ConstantItem(30).Text(label);
                    baris.RelativeItem().Text(t =>
                    {
                        t.Justify();
                        t.Span(isi);
                    });
                });
            }

            private static void TulisParagraf(ColumnDescriptor kolom, string teks)
            {
                kolom.Item().PaddingBottom(NaskahDesa.JarakParagraf).Text(t =>
                {
                    t.Justify();
                    t.ParagraphFirstLineIndentation(24);
                    t.Span(teks);
                });
            }

            /// <summary>
            /// Salinan isi halaman dengan nomor/tahun/tanggal formulir. Halaman
            /// lampiran (mis. Keputusan BPD) tidak diubah karena tanggal dan
            /// nomornya milik dokumen lampiran itu sendiri.
            /// </summary>
            private string IsiDenganIsian(NaskahPerdesHalaman halaman, bool halamanPertama)
            {
                string teks = halaman.Isi ?? string.Empty;
                if (halaman.Judul.Contains("lampiran", StringComparison.OrdinalIgnoreCase)) return teks;

                string tahun = _tanggal.Year.ToString(CultureInfo.InvariantCulture);
                string nomor = string.IsNullOrWhiteSpace(_nomor) ? "........." : _nomor.Trim();

                // Hanya kepala surat berhuruf kapital yang diubah; rujukan hukum
                // seperti "Nomor 6 Tahun 2014" memakai huruf campur dan tidak tersentuh.
                teks = Regex.Replace(teks, @"\bNOMOR\s+\d+\s+TAHUN\s+\d{4}\b",
                    $"NOMOR {nomor} TAHUN {tahun}");

                // Tahun anggaran milik Perdes ini ada di Menimbang dan diktum
                // Menetapkan; rujukan seperti "APBD Tahun Anggaran 2018" di antara
                // Mengingat–Memperhatikan adalah dokumen lain dan tidak diubah.
                if (halamanPertama) teks = GantiTahunAnggaran(teks, tahun);

                if (teks.Contains("Ditetapkan di", StringComparison.OrdinalIgnoreCase))
                {
                    string tanggal = NaskahDesa.TanggalIndo(_tanggal);
                    teks = TanggalPengundangan.Replace(teks, tanggal);
                    teks = TanggalBerlabel.Replace(teks, tanggal);
                    teks = TanggalPenetapan.Replace(teks, tanggal);

                    // "LEMBARAN DESA ... TAHUN 2025" ikut tahun penetapan baru.
                    teks = Regex.Replace(teks,
                        @"(LEMBARAN DESA[^\n]{0,120}?TAHUN\s+)\d{4}",
                        "${1}" + tahun);
                }

                return teks;
            }

            /// <summary>
            /// Tahun setelah "Tahun Anggaran" diganti di Menimbang dan diktum,
            /// tetapi dilewati di daftar rujukan hukum yang menyebut anggaran
            /// daerah tahun lain.
            /// </summary>
            private static string GantiTahunAnggaran(string teks, string tahun)
            {
                // Hanya halaman pembukaan yang memuat Menimbang–Mengingat.
                if (teks.IndexOf("Menimbang", StringComparison.OrdinalIgnoreCase) < 0) return teks;

                int mulai = PosisiLabel(teks, "Mengingat", "Memperhatikan");
                if (mulai < 0)
                {
                    return GantiAnggaran(teks, tahun);
                }

                int akhir = PosisiLabel(teks[mulai..], "Memutuskan", "Menetapkan");
                if (akhir < 0)
                {
                    return GantiAnggaran(teks[..mulai], tahun) + teks[mulai..];
                }

                akhir += mulai;
                return GantiAnggaran(teks[..mulai], tahun) +
                       teks[mulai..akhir] +
                       GantiAnggaran(teks[akhir..], tahun);
            }

            private static int PosisiLabel(string teks, string pertama, string kedua)
            {
                int a = teks.IndexOf(pertama, StringComparison.OrdinalIgnoreCase);
                int b = teks.IndexOf(kedua, StringComparison.OrdinalIgnoreCase);
                if (a < 0) return b;
                if (b < 0) return a;
                return Math.Min(a, b);
            }

            private static string GantiAnggaran(string teks, string tahun) =>
                Regex.Replace(teks, @"\b(TAHUN ANGGARAN)\s+\d{4}\b",
                    m => $"{m.Groups[1].Value} {tahun}", RegexOptions.IgnoreCase);
        }
    }
}
