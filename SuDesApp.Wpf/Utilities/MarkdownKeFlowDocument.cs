using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

using SuDesApp.Utilities;

namespace SuDesApp.Wpf.Utilities
{
    /// <summary>
    /// Menyusun dokumen Markdown bawaan aplikasi (folder <c>docs</c>) menjadi
    /// <see cref="FlowDocument"/> supaya bisa dibaca <b>di dalam aplikasi</b> —
    /// tanpa bergantung pada aplikasi asosiasi Windows yang sering tidak
    /// terpasang di komputer desa (berkas <c>.md</c> pun biasanya tidak punya
    /// asosiasi sama sekali).
    ///
    /// Struktur blok disiapkan di <see cref="MarkdownDokumen"/> (Core, bisa diuji
    /// tanpa UI); berkas ini hanya menata tampilannya: penanda tebal/miring/kode,
    /// tabel, dan blok kode berpagar.
    /// </summary>
    public static class MarkdownKeFlowDocument
    {
        /// <summary>Penanda sebaris: `kode`, **tebal**, *miring*, dan [teks](tautan).</summary>
        private static readonly Regex PolaInline = new(
            "`[^`]+`|\\*\\*[^*]+\\*\\*|\\*[^*]+\\*|\\[[^\\]]+\\]\\([^)]+\\)",
            RegexOptions.Compiled);

        private static readonly FontFamily FontIsi = new("Segoe UI");
        private static readonly FontFamily FontKode = new("Consolas");

        /// <summary>Susun daftar blok Markdown menjadi dokumen siap baca.</summary>
        public static FlowDocument Susun(IReadOnlyList<BlokMarkdown>? blok)
        {
            var dokumen = new FlowDocument
            {
                FontFamily = FontIsi,
                FontSize = 13,
                Foreground = Kuas("TextBrush", Color.FromRgb(0x1F, 0x24, 0x2B)),
                LineHeight = 20,
                PagePadding = new Thickness(26, 20, 26, 30),
                // Satu kolom lebar: dokumen aplikasi lebih enak dibaca memanjang
                // daripada terbelah dua kolom seperti halaman buku.
                ColumnWidth = 2000,
                TextAlignment = TextAlignment.Left
            };

            if (blok is not null)
            {
                foreach (var satuBlok in blok)
                {
                    TambahBlok(dokumen.Blocks, satuBlok);
                }
            }

            return dokumen;
        }

        private static void TambahBlok(BlockCollection tujuan, BlokMarkdown blok)
        {
            switch (blok.Jenis)
            {
                case JenisBlokMarkdown.Judul:
                    tujuan.Add(Judul(blok));
                    break;
                case JenisBlokMarkdown.Paragraf:
                    tujuan.Add(Paragraf(blok.Teks));
                    break;
                case JenisBlokMarkdown.Butir:
                    tujuan.Add(Daftar(blok.Baris, bernomor: false));
                    break;
                case JenisBlokMarkdown.ButirBernomor:
                    tujuan.Add(Daftar(blok.Baris, bernomor: true));
                    break;
                case JenisBlokMarkdown.Tabel:
                    tujuan.Add(Tabel(blok.IsiTabel));
                    break;
                case JenisBlokMarkdown.Kode:
                    TambahKode(tujuan, blok);
                    break;
                case JenisBlokMarkdown.Kutipan:
                    tujuan.Add(Kutipan(blok.Teks));
                    break;
                case JenisBlokMarkdown.Garis:
                    tujuan.Add(Garis());
                    break;
            }
        }

        // ── Blok ────────────────────────────────────────────────────────────

        private static Paragraph Judul(BlokMarkdown blok)
        {
            double ukuran = blok.Tingkat switch
            {
                1 => 19.5,
                2 => 16.5,
                3 => 14.5,
                4 => 13.5,
                _ => 13
            };

            var paragraf = new Paragraph
            {
                FontSize = ukuran,
                FontWeight = FontWeights.SemiBold,
                Foreground = blok.Tingkat <= 2
                    ? Kuas("AccentBrush", Color.FromRgb(0x25, 0x63, 0xEB))
                    : Kuas("TextBrush", Color.FromRgb(0x1F, 0x24, 0x2B)),
                Margin = new Thickness(0, blok.Tingkat == 1 ? 14 : 12, 0, 7),
                // Judul tidak boleh terpisah dari isi yang mengikutinya.
                KeepWithNext = true
            };

            Isi(paragraf, blok.Teks);
            return paragraf;
        }

        private static Paragraph Paragraf(string teks)
        {
            var paragraf = new Paragraph { Margin = new Thickness(0, 0, 0, 9) };
            Isi(paragraf, teks);
            return paragraf;
        }

        private static List Daftar(IReadOnlyList<string> butir, bool bernomor)
        {
            var daftar = new List
            {
                MarkerStyle = bernomor ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                MarkerOffset = 14,
                Margin = new Thickness(20, 0, 0, 10),
                Padding = new Thickness(0)
            };

            foreach (var teks in butir)
            {
                var paragraf = new Paragraph { Margin = new Thickness(0, 0, 0, 3) };
                Isi(paragraf, teks);
                daftar.ListItems.Add(new ListItem(paragraf));
            }

            return daftar;
        }

        private static Table Tabel(IReadOnlyList<IReadOnlyList<string>> baris)
        {
            var tabel = new Table { CellSpacing = 0, Margin = new Thickness(0, 2, 0, 13) };
            int kolom = baris.Count == 0 ? 0 : baris.Max(b => b.Count);
            for (int c = 0; c < kolom; c++)
            {
                tabel.Columns.Add(new TableColumn());
            }

            var grup = new TableRowGroup();
            for (int r = 0; r < baris.Count; r++)
            {
                bool kepala = r == 0;
                var barisTabel = new TableRow();

                for (int c = 0; c < kolom; c++)
                {
                    var selParagraf = new Paragraph
                    {
                        Margin = new Thickness(0),
                        FontSize = 12,
                        FontWeight = kepala ? FontWeights.SemiBold : FontWeights.Normal
                    };
                    Isi(selParagraf, c < baris[r].Count ? baris[r][c] : string.Empty);

                    barisTabel.Cells.Add(new TableCell(selParagraf)
                    {
                        BorderBrush = Kuas("BorderBrush", Color.FromRgb(0xD9, 0xDE, 0xE5)),
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(7, 4, 7, 4),
                        Background = kepala
                            ? Kuas("AccentSubtleBrush", Color.FromRgb(0xEF, 0xF6, 0xFF))
                            : null
                    });
                }

                grup.Rows.Add(barisTabel);
            }

            tabel.RowGroups.Add(grup);
            return tabel;
        }

        private static void TambahKode(BlockCollection tujuan, BlokMarkdown blok)
        {
            // Penanda bahasa ditulis kecil di atas blok agar pembaca teknis tahu
            // isinya JSON, PowerShell, dan sejenisnya.
            if (!string.IsNullOrWhiteSpace(blok.Bahasa))
            {
                tujuan.Add(new Paragraph(new Run(blok.Bahasa))
                {
                    FontSize = 10.5,
                    Foreground = Kuas("TextSecondaryBrush", Color.FromRgb(0x5B, 0x64, 0x72)),
                    Margin = new Thickness(0, 0, 0, 2)
                });
            }

            var paragraf = new Paragraph
            {
                FontFamily = FontKode,
                FontSize = 12,
                LineHeight = 17,
                Background = Kuas("AccentSubtleBrush", Color.FromRgb(0xEF, 0xF6, 0xFF)),
                BorderBrush = Kuas("BorderBrush", Color.FromRgb(0xD9, 0xDE, 0xE5)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 13)
            };

            // Ditulis apa adanya: spasi awal baris pada contoh konfigurasi penting.
            paragraf.Inlines.Add(new Run(blok.Teks));
            tujuan.Add(paragraf);
        }

        private static Paragraph Kutipan(string teks)
        {
            var paragraf = new Paragraph
            {
                BorderBrush = Kuas("AccentBrush", Color.FromRgb(0x25, 0x63, 0xEB)),
                BorderThickness = new Thickness(3, 0, 0, 0),
                Padding = new Thickness(10, 2, 0, 2),
                Margin = new Thickness(0, 2, 0, 12),
                Foreground = Kuas("TextSecondaryBrush", Color.FromRgb(0x5B, 0x64, 0x72)),
                FontStyle = FontStyles.Italic
            };

            Isi(paragraf, teks);
            return paragraf;
        }

        private static Paragraph Garis() => new()
        {
            BorderBrush = Kuas("BorderBrush", Color.FromRgb(0xD9, 0xDE, 0xE5)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 8, 0, 14),
            FontSize = 1
        };

        // ── Penanda sebaris ─────────────────────────────────────────────────

        /// <summary>
        /// Isi paragraf dengan teks yang penandanya (tebal, miring, kode, tautan)
        /// diubah menjadi gaya. Tautan sengaja <b>tidak</b> membuka apa pun: hanya
        /// bergaris bawah dengan keterangan tujuannya, supaya tidak ada dokumen yang
        /// terlempar ke aplikasi luar.
        /// </summary>
        private static void Isi(Paragraph paragraf, string? teks)
        {
            if (string.IsNullOrEmpty(teks))
            {
                return;
            }

            int posisi = 0;
            foreach (Match cocok in PolaInline.Matches(teks))
            {
                if (cocok.Index > posisi)
                {
                    paragraf.Inlines.Add(new Run(teks[posisi..cocok.Index]));
                }

                var penanda = cocok.Value;
                if (penanda.StartsWith('`'))
                {
                    paragraf.Inlines.Add(new Run(penanda[1..^1])
                    {
                        FontFamily = FontKode,
                        FontSize = 12,
                        Background = Kuas("AccentSubtleBrush", Color.FromRgb(0xEF, 0xF6, 0xFF))
                    });
                }
                else if (penanda.StartsWith("**", StringComparison.Ordinal))
                {
                    paragraf.Inlines.Add(new Run(penanda[2..^2]) { FontWeight = FontWeights.Bold });
                }
                else if (penanda.StartsWith('['))
                {
                    int pemisah = penanda.IndexOf("](", StringComparison.Ordinal);
                    var label = penanda[1..pemisah];
                    var tujuan = penanda[(pemisah + 2)..^1];
                    paragraf.Inlines.Add(new Run(label)
                    {
                        Foreground = Kuas("AccentBrush", Color.FromRgb(0x25, 0x63, 0xEB)),
                        TextDecorations = TextDecorations.Underline,
                        ToolTip = "Rujukan dokumen: " + tujuan
                    });
                }
                else
                {
                    paragraf.Inlines.Add(new Run(penanda[1..^1]) { FontStyle = FontStyles.Italic });
                }

                posisi = cocok.Index + cocok.Length;
            }

            if (posisi < teks.Length)
            {
                paragraf.Inlines.Add(new Run(teks[posisi..]));
            }
        }

        /// <summary>
        /// Warna dari tema aplikasi, dengan cadangan bila tema belum dimuat
        /// (mis. saat dokumen disusun sebelum jendela pertama tampil). Dokumen
        /// disusun ulang setiap kali dibuka, jadi pergantian tema ikut terbawa.
        /// </summary>
        private static Brush Kuas(string kunci, Color cadangan)
        {
            if (Application.Current?.TryFindResource(kunci) is Brush kuas)
            {
                return kuas;
            }

            var fallback = new SolidColorBrush(cadangan);
            fallback.Freeze();
            return fallback;
        }
    }
}
