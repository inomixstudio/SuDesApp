using System;
using System.Collections.Generic;
using System.Linq;

namespace SuDesApp.Utilities
{
    /// <summary>Jenis blok pada dokumen Markdown bawaan aplikasi.</summary>
    public enum JenisBlokMarkdown
    {
        /// <summary>Judul bab: <c>#</c> sampai <c>######</c> (lihat <see cref="BlokMarkdown.Tingkat"/>).</summary>
        Judul,
        Paragraf,
        /// <summary>Butir bertanda (<c>-</c>, <c>*</c>, <c>+</c>).</summary>
        Butir,
        /// <summary>Butir bernomor (<c>1.</c>, <c>2)</c>).</summary>
        ButirBernomor,
        Tabel,
        /// <summary>Blok kode berpagar (``` … ```).</summary>
        Kode,
        Kutipan,
        Garis
    }

    /// <summary>
    /// Satu blok hasil penguraian Markdown. Isi teks masih memuat penanda
    /// <b>tebal</b>, <i>miring</i>, <c>`kode`</c>, dan tautan karena penyusun
    /// tampilan yang mengubahnya — di sini hanya struktur blok yang ditentukan.
    /// </summary>
    public sealed class BlokMarkdown
    {
        public JenisBlokMarkdown Jenis { get; init; }

        /// <summary>Tingkat judul 1–6; nol untuk blok selain judul.</summary>
        public int Tingkat { get; init; }

        /// <summary>Isi satu baris (paragraf, kutipan, judul).</summary>
        public string Teks { get; init; } = string.Empty;

        /// <summary>Butir daftar (satu entri per baris) atau bahasa blok kode.</summary>
        public IReadOnlyList<string> Baris { get; init; } = Array.Empty<string>();

        /// <summary>Baris tabel; baris pertama adalah kepala tabel.</summary>
        public IReadOnlyList<IReadOnlyList<string>> IsiTabel { get; init; } =
            Array.Empty<IReadOnlyList<string>>();

        /// <summary>Bahasa pada pagar kode (mis. <c>json</c>), kosong bila tidak ditulis.</summary>
        public string Bahasa { get; init; } = string.Empty;
    }

    /// <summary>
    /// Pengurai Markdown seadanya untuk dokumen bawaan aplikasi (folder
    /// <c>docs</c>) — dipakai halaman Dokumentasi agar berkas <c>.md</c> dibaca di
    /// dalam aplikasi, bukan diserahkan ke aplikasi asosiasi Windows (yang sering
    /// tidak ada di komputer desa).
    ///
    /// Sengaja bukan pengurai Markdown penuh: hanya tata bahasa yang benar-benar
    /// dipakai dokumen aplikasi — judul, paragraf, daftar butir/bernomor, tabel,
    /// pagar kode, kutipan, dan garis pemisah. Penguraiannya dipisah dari penyusunan
    /// tampilan (yang memakai WPF) supaya bisa diuji tanpa menjalankan UI.
    /// </summary>
    public static class MarkdownDokumen
    {
        /// <summary>
        /// Uraikan teks Markdown menjadi daftar blok berurutan. Teks kosong/null
        /// menghasilkan daftar kosong; baris yang tidak dikenali menjadi paragraf.
        /// </summary>
        public static IReadOnlyList<BlokMarkdown> Uraikan(string? markdown)
        {
            var hasil = new List<BlokMarkdown>();
            if (string.IsNullOrWhiteSpace(markdown))
            {
                return hasil;
            }

            // Baris dinormalkan lebih dulu: berkas dokumen bisa berakhiran CRLF
            // maupun LF, dan sisa spasi di ujung tidak pernah bermakna.
            var baris = markdown!
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n')
                .Select(b => b.TrimEnd())
                .ToList();

            var paragraf = new List<string>();

            void TutupParagraf()
            {
                if (paragraf.Count == 0)
                {
                    return;
                }

                hasil.Add(new BlokMarkdown
                {
                    Jenis = JenisBlokMarkdown.Paragraf,
                    Teks = string.Join(" ", paragraf.Select(b => b.Trim()))
                });
                paragraf.Clear();
            }

            for (int i = 0; i < baris.Count; i++)
            {
                var teks = baris[i];
                var rapi = teks.Trim();

                if (rapi.Length == 0)
                {
                    TutupParagraf();
                    continue;
                }

                // Pagar kode: seluruh isi sampai pagar penutup dipertahankan apa adanya.
                if (rapi.StartsWith("```", StringComparison.Ordinal))
                {
                    TutupParagraf();
                    var bahasa = rapi.Length > 3 ? rapi[3..].Trim() : string.Empty;
                    var isiKode = new List<string>();

                    i++;
                    while (i < baris.Count && !baris[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
                    {
                        isiKode.Add(baris[i]);
                        i++;
                    }

                    hasil.Add(new BlokMarkdown
                    {
                        Jenis = JenisBlokMarkdown.Kode,
                        Bahasa = bahasa,
                        Teks = string.Join(Environment.NewLine, isiKode)
                    });
                    continue;
                }

                if (rapi.StartsWith('#'))
                {
                    int tingkat = rapi.TakeWhile(k => k == '#').Count();
                    if (tingkat is >= 1 and <= 6 && rapi.Length > tingkat)
                    {
                        TutupParagraf();
                        hasil.Add(new BlokMarkdown
                        {
                            Jenis = JenisBlokMarkdown.Judul,
                            Tingkat = tingkat,
                            Teks = rapi[tingkat..].Trim().TrimEnd('#').Trim()
                        });
                        continue;
                    }
                }

                if (ApakahGarisPemisah(rapi))
                {
                    TutupParagraf();
                    hasil.Add(new BlokMarkdown { Jenis = JenisBlokMarkdown.Garis });
                    continue;
                }

                // Tabel: baris berpagar "|" yang diikuti baris pemisah "|---|---|".
                if (rapi.Contains('|') && i + 1 < baris.Count && ApakahPemisahTabel(baris[i + 1]))
                {
                    TutupParagraf();
                    var isiTabel = new List<IReadOnlyList<string>> { PecahSel(rapi) };

                    i += 2;
                    while (i < baris.Count && baris[i].Trim().Contains('|'))
                    {
                        isiTabel.Add(PecahSel(baris[i].Trim()));
                        i++;
                    }

                    i--;
                    hasil.Add(new BlokMarkdown
                    {
                        Jenis = JenisBlokMarkdown.Tabel,
                        IsiTabel = isiTabel
                    });
                    continue;
                }

                if (rapi.StartsWith('>'))
                {
                    TutupParagraf();
                    var kutipan = new List<string>();
                    while (i < baris.Count && baris[i].TrimStart().StartsWith('>'))
                    {
                        kutipan.Add(baris[i].TrimStart().TrimStart('>').Trim());
                        i++;
                    }

                    i--;
                    hasil.Add(new BlokMarkdown
                    {
                        Jenis = JenisBlokMarkdown.Kutipan,
                        Teks = string.Join(" ", kutipan.Where(k => k.Length > 0))
                    });
                    continue;
                }

                var butir = CocokButir(rapi);
                if (butir is not null)
                {
                    TutupParagraf();
                    var daftar = new List<string> { butir };
                    while (i + 1 < baris.Count)
                    {
                        var berikutnya = CocokButir(baris[i + 1].Trim());
                        if (berikutnya is null) break;
                        daftar.Add(berikutnya);
                        i++;
                    }

                    hasil.Add(new BlokMarkdown
                    {
                        Jenis = JenisBlokMarkdown.Butir,
                        Baris = daftar
                    });
                    continue;
                }

                var bernomor = CocokButirBernomor(rapi);
                if (bernomor is not null)
                {
                    TutupParagraf();
                    var daftar = new List<string> { bernomor };
                    while (i + 1 < baris.Count)
                    {
                        var berikutnya = CocokButirBernomor(baris[i + 1].Trim());
                        if (berikutnya is null) break;
                        daftar.Add(berikutnya);
                        i++;
                    }

                    hasil.Add(new BlokMarkdown
                    {
                        Jenis = JenisBlokMarkdown.ButirBernomor,
                        Baris = daftar
                    });
                    continue;
                }

                paragraf.Add(rapi);
            }

            TutupParagraf();
            return hasil;
        }

        /// <summary>Isi butir "<c>- teks</c>", atau null bila barisnya bukan butir.</summary>
        private static string? CocokButir(string rapi)
        {
            if (rapi.Length < 3 || rapi[1] != ' ')
            {
                return null;
            }

            return rapi[0] is '-' or '*' or '+' ? rapi[2..].Trim() : null;
        }

        /// <summary>Isi butir "<c>1. teks</c>" / "<c>1) teks</c>", atau null.</summary>
        private static string? CocokButirBernomor(string rapi)
        {
            int angka = 0;
            while (angka < rapi.Length && char.IsAsciiDigit(rapi[angka]))
            {
                angka++;
            }

            if (angka == 0 || angka + 1 >= rapi.Length || rapi[angka] is not ('.' or ')') || rapi[angka + 1] != ' ')
            {
                return null;
            }

            return rapi[(angka + 2)..].Trim();
        }

        private static bool ApakahGarisPemisah(string rapi) =>
            rapi.Length >= 3 && (rapi.All(k => k == '-') || rapi.All(k => k == '*') || rapi.All(k => k == '_'));

        /// <summary>Baris pemisah kepala tabel, mis. <c>|---|---|</c> atau <c>--- | ---</c>.</summary>
        private static bool ApakahPemisahTabel(string baris)
        {
            var rapi = baris.Trim().Trim('|').Trim();
            if (rapi.Length == 0)
            {
                return false;
            }

            return rapi
                .Split('|', StringSplitOptions.RemoveEmptyEntries)
                .All(sel => sel.Trim().Length > 0 && sel.Trim().Trim(':').All(k => k == '-'));
        }

        /// <summary>Pecah baris tabel menjadi sel-sel, tanpa pipa di ujung dan tanpa spasi berlebih.</summary>
        private static IReadOnlyList<string> PecahSel(string baris)
        {
            var rapi = baris.Trim();
            if (rapi.StartsWith('|')) rapi = rapi[1..];
            if (rapi.EndsWith('|')) rapi = rapi[..^1];
            return rapi.Split('|').Select(s => s.Trim()).ToList();
        }
    }
}
