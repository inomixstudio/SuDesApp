using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace SuDesApp.Core.Tests
{
    /// <summary>
    /// Padanan uji untuk <c>Tools/check-kontras-wcag.ps1</c>: menghitung rasio kontras
    /// WCAG 2.1 AA setiap pasangan teks/latar yang benar-benar dipakai aplikasi pada
    /// ketujuh tema, plus menjaga agar token tema tetap lengkap dan seragam antar tema.
    /// Tema kontras tinggi punya uji tambahan karena menargetkan AAA, bukan AA.
    ///
    /// Skrip PowerShell-nya sendiri tidak selalu bisa dijalankan (pwsh tidak terpasang
    /// di semua mesin), jadi aturan yang sama dijalankan di sini supaya <c>dotnet test</c>
    /// menjadi gerbangnya. Daftar pasangan diuji <b>sama persis</b> dengan daftar di
    /// skrip — kalau salah satu diubah tanpa yang lain, uji ini gagal.
    /// </summary>
    public sealed class KontrasTemaTests
    {
        private sealed record Pasangan(string Uji, string Fg, string Bg, double Ambang);

        /// <summary>Pasangan teks/latar wajib lolos, sama dengan <c>$pasangan</c> di skrip.</summary>
        private static readonly Pasangan[] PasanganWajib =
        {
            new("Teks utama di kartu", "TextBrush", "SurfaceBrush", 4.5),
            new("Teks utama di latar halaman", "TextBrush", "WindowBackgroundBrush", 4.5),
            new("Teks utama di panel", "TextBrush", "PanelBrush", 4.5),
            new("Teks utama baris zebra", "TextBrush", "ZebraBrush", 4.5),
            new("Teks utama baris terpilih", "TextBrush", "AccentSubtleBrush", 4.5),
            new("Teks utama di atas BorderBrush", "TextBrush", "BorderBrush", 4.5),
            new("Teks sekunder di kartu", "TextSecondaryBrush", "SurfaceBrush", 4.5),
            new("Teks sekunder di latar halaman", "TextSecondaryBrush", "WindowBackgroundBrush", 4.5),
            new("Teks sekunder di panel", "TextSecondaryBrush", "PanelBrush", 4.5),
            new("Teks sekunder baris zebra", "TextSecondaryBrush", "ZebraBrush", 4.5),
            new("Chip status bar (sekunder)", "TextSecondaryBrush", "AccentSubtleBrush", 4.5),
            new("Status bar", "StatusBarTextBrush", "StatusBarBackgroundBrush", 4.5),
            new("Teks sidebar", "SidebarTextBrush", "SidebarBackgroundBrush", 4.5),
            new("Judul header halaman", "HeaderTextBrush", "PrimaryBrush", 3.0),
            new("Subjudul header halaman", "HeaderSubtitleBrush", "PrimaryBrush", 4.5),
            new("Judul header (ujung gradien)", "HeaderTextBrush", "SecondaryBrush", 3.0),
            new("Subjudul header (ujung gradien)", "HeaderSubtitleBrush", "SecondaryBrush", 4.5),
            new("Teks tombol primer", "OnPrimaryTextBrush", "PrimaryBrush", 4.5),
            new("Teks tombol primer (ujung gradien)", "OnPrimaryTextBrush", "SecondaryBrush", 4.5),
            new("Teks tombol aksen", "OnAccentTextBrush", "AccentBrush", 4.5),
            new("Teks lencana sukses", "OnSuccessTextBrush", "SuccessBrush", 4.5),
            new("Teks lencana peringatan", "OnWarningTextBrush", "WarningBrush", 4.5),
            new("Teks lencana galat", "OnErrorTextBrush", "ErrorBrush", 4.5),
            new("Ikon/tautan aksen di kartu", "AccentTextBrush", "SurfaceBrush", 4.5),
            new("Ikon/tautan aksen di panel", "AccentTextBrush", "PanelBrush", 4.5),
            new("Ikon/tautan aksen di chip", "AccentTextBrush", "AccentSubtleBrush", 4.5),
            new("Ikon/tautan aksen di latar halaman", "AccentTextBrush", "WindowBackgroundBrush", 4.5),
            new("Ikon/tautan aksen baris zebra", "AccentTextBrush", "ZebraBrush", 4.5),
            new("Teks tab terpilih", "AccentTextBrush", "SurfaceBrush", 4.5),
            new("Teks terpilih di kartu", "TextSelectedBrush", "SurfaceBrush", 4.5),
            new("Sukses di badge", "SuccessTextBrush", "SuccessSubtleBrush", 4.5),
            new("Peringatan di badge", "WarningTextBrush", "WarningSubtleBrush", 4.5),
            new("Galat di badge", "ErrorTextBrush", "ErrorSubtleBrush", 4.5),
            new("Sukses di kartu", "SuccessTextBrush", "SurfaceBrush", 4.5),
            new("Peringatan di kartu", "WarningTextBrush", "SurfaceBrush", 4.5),
            new("Galat di kartu", "ErrorTextBrush", "SurfaceBrush", 4.5),
            new("Sukses di chip", "SuccessTextBrush", "AccentSubtleBrush", 4.5),
            new("Peringatan di chip", "WarningTextBrush", "AccentSubtleBrush", 4.5),
            new("Galat di chip", "ErrorTextBrush", "AccentSubtleBrush", 4.5),
            // Celah audit 1 Oktober 2026: pasangan ini benar-benar dipakai kode tetapi
            // tidak ada di daftar, sehingga pelanggaran nyata lolos. Ketujuhnya kini
            // dijaga bersama skrip kontras.
            new("Teks utama di kartu peringatan", "TextBrush", "WarningSubtleBrush", 4.5),
            new("Peringatan di panel", "WarningTextBrush", "PanelBrush", 4.5),
            new("Galat di panel", "ErrorTextBrush", "PanelBrush", 4.5),
            new("Peringatan di latar halaman", "WarningTextBrush", "WindowBackgroundBrush", 4.5),
            new("Sukses di latar halaman", "SuccessTextBrush", "WindowBackgroundBrush", 4.5),
            new("Galat di latar halaman", "ErrorTextBrush", "WindowBackgroundBrush", 4.5),
            new("Teks aksen sidebar", "SidebarAccentBrush", "SidebarBackgroundBrush", 4.5),
            // Gaya InfoBoxText (keterangan di kotak status) juga dipakai di atas
            // latar subtle sukses/peringatan/galat; dulu luput dari daftar.
            new("Teks sekunder di kartu sukses", "TextSecondaryBrush", "SuccessSubtleBrush", 4.5),
            new("Teks sekunder di kartu peringatan", "TextSecondaryBrush", "WarningSubtleBrush", 4.5),
            new("Teks sekunder di kartu galat", "TextSecondaryBrush", "ErrorSubtleBrush", 4.5)
        };

        /// <summary>Berkas tema warna yang bisa dipilih operator. Tema terakhir
        /// (HighContrastTheme) menargetkan WCAG AAA, sisanya AA.</summary>
        private static readonly string[] TemaBerwarna =
        {
            "LightTheme", "DarkTheme", "GreenTheme", "BlueTheme", "PinkTheme", "SlateTheme",
            "HighContrastTheme"
        };

        /// <summary>Tema yang menargetkan WCAG AAA (teks normal 7:1).</summary>
        private const string TemaKontrasTinggi = "HighContrastTheme";

        /// <summary>Kamus gaya bersama yang ikut menampung token (bukan tema berwarna).</summary>
        private static readonly string[] KamusBersama =
        {
            "ThemeStyles", "NotifikasiInline", "NavKiri", "KartuCatatanRilis"
        };

        /// <summary>Teks putih kaku yang masih diizinkan — harus disertai alasannya.</summary>
        private static readonly Dictionary<string, string> PengecualianTeksPutih = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Views/AboutView.xaml"] =
                "Avatar Google memakai warna merek tetap #4285F4 (bukan token tema), " +
                "jadi huruf awal nama di atasnya memang putih di semua tema."
        };

        private static readonly Regex ReWarna = new("""<Color x:Key="(?<k>[^"]+)">(?<v>#[0-9A-Fa-f]{6,8})</Color>""");
        private static readonly Regex ReBrush = new("""<SolidColorBrush\s+x:Key="(?<k>[^"]+)"\s+Color="(?<c>[^"]+)"(?:\s+Opacity="(?<o>[^"]+)")?\s*/>""");
        private static readonly Regex ReKunci = new("x:Key=\"(?<k>[^\"]+)\"");
        private static readonly Regex ReTeksPutih = new("Foreground=\"(?:White|WhiteSmoke|#FFFFFF|#FFF{5})\"");
        private static readonly Regex ReRujukanDinamis = new("""\{DynamicResource\s+(?<k>[A-Za-z0-9_]+)\}""");

        private sealed class TemaData
        {
            public Dictionary<string, string> Warna { get; } = new(StringComparer.Ordinal);
            public Dictionary<string, (string Warna, double Opasitas)> Brush { get; } = new(StringComparer.Ordinal);
            public HashSet<string> Kunci { get; } = new(StringComparer.Ordinal);
        }

        private static string AkarProyek => CoreTestFixture.FindProjectRoot();
        private static string JalurWpf => Path.Combine(AkarProyek, "SuDesApp.Wpf");
        private static string JalurTema(string nama) => Path.Combine(JalurWpf, "Themes", nama + ".xaml");

        // ---------- Pembacaan & perhitungan (sama dengan skrip PowerShell) ----------

        private static TemaData BacaTema(string nama)
        {
            var isi = File.ReadAllText(JalurTema(nama));
            var data = new TemaData();

            foreach (Match m in ReWarna.Matches(isi))
            {
                data.Warna[m.Groups["k"].Value] = m.Groups["v"].Value;
            }

            foreach (Match m in ReBrush.Matches(isi))
            {
                var opasitas = 1.0;
                if (m.Groups["o"].Success && m.Groups["o"].Value.Length > 0)
                {
                    opasitas = double.Parse(m.Groups["o"].Value, CultureInfo.InvariantCulture);
                }
                data.Brush[m.Groups["k"].Value] = (m.Groups["c"].Value, opasitas);
            }

            foreach (Match m in ReKunci.Matches(isi))
            {
                data.Kunci.Add(m.Groups["k"].Value);
            }

            return data;
        }

        private static string? SelesaikanWarna(string nilai, TemaData tema)
        {
            if (string.IsNullOrWhiteSpace(nilai)) return null;
            var v = nilai.Trim();

            if (v.StartsWith("{StaticResource", StringComparison.Ordinal))
            {
                var nama = v.Replace("{", string.Empty).Replace("}", string.Empty)
                            .Replace("StaticResource", string.Empty).Trim();
                return tema.Warna.TryGetValue(nama, out var hex) ? hex : null;
            }

            if (v.Equals("White", StringComparison.Ordinal)) return "#FFFFFF";
            if (v.Equals("Black", StringComparison.Ordinal)) return "#000000";
            return v.StartsWith('#') ? v : null;
        }

        private static int Kanal(string hex, int mulai)
        {
            var s = hex.Trim().TrimStart('#');
            if (s.Length == 8) s = s.Substring(2); // buang alfa AARRGGBB
            return Convert.ToInt32(s.Substring(mulai, 2), 16);
        }

        private static double KanalLinier(int c)
        {
            var v = c / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        private static double Luminansi(string hex) =>
            0.2126 * KanalLinier(Kanal(hex, 0)) +
            0.7152 * KanalLinier(Kanal(hex, 2)) +
            0.0722 * KanalLinier(Kanal(hex, 4));

        private static double Rasio(string a, string b)
        {
            var la = Luminansi(a);
            var lb = Luminansi(b);
            if (la < lb) (la, lb) = (lb, la);
            return (la + 0.05) / (lb + 0.05);
        }

        private static string Campur(string fg, string bg, double op)
        {
            var r = (int)Math.Round(Kanal(fg, 0) * op + Kanal(bg, 0) * (1 - op));
            var g = (int)Math.Round(Kanal(fg, 2) * op + Kanal(bg, 2) * (1 - op));
            var b = (int)Math.Round(Kanal(fg, 4) * op + Kanal(bg, 4) * (1 - op));
            return $"#{r:X2}{g:X2}{b:X2}";
        }

        private static IEnumerable<string> SemuaXaml()
        {
            foreach (var berkas in Directory.EnumerateFiles(JalurWpf, "*.xaml", SearchOption.AllDirectories))
            {
                var relatif = Path.GetRelativePath(JalurWpf, berkas).Replace('\\', '/');
                if (relatif.StartsWith("obj/", StringComparison.Ordinal) ||
                    relatif.StartsWith("bin/", StringComparison.Ordinal)) continue;
                yield return berkas;
            }
        }

        // ---------- 1. Kontras ----------

        [Fact]
        public void TemaBerwarna_MemenuhiAmbangKontrasWcag()
        {
            var temuan = new List<string>();

            foreach (var nama in TemaBerwarna)
            {
                var tema = BacaTema(nama);

                foreach (var p in PasanganWajib)
                {
                    if (!tema.Brush.TryGetValue(p.Fg, out var kuasFg))
                    {
                        temuan.Add($"{nama} kehilangan brush '{p.Fg}' (dipakai: {p.Uji})");
                        continue;
                    }
                    if (!tema.Brush.TryGetValue(p.Bg, out var kuasBg))
                    {
                        temuan.Add($"{nama} kehilangan brush '{p.Bg}' (dipakai: {p.Uji})");
                        continue;
                    }

                    var fg = SelesaikanWarna(kuasFg.Warna, tema);
                    var bg = SelesaikanWarna(kuasBg.Warna, tema);
                    if (fg is null || bg is null)
                    {
                        temuan.Add($"{nama}: warna '{p.Fg}'/'{p.Bg}' tidak dapat diselesaikan");
                        continue;
                    }

                    if (kuasFg.Opasitas < 1.0) fg = Campur(fg, bg, kuasFg.Opasitas);

                    var rasio = Math.Round(Rasio(fg, bg), 2);
                    if (rasio < p.Ambang)
                    {
                        temuan.Add($"{nama}: {p.Uji} — rasio {rasio.ToString(CultureInfo.InvariantCulture)} " +
                                   $"(butuh {p.Ambang.ToString(CultureInfo.InvariantCulture)}); teks {fg} di atas latar {bg}");
                    }
                }
            }

            Assert.True(temuan.Count == 0,
                "Kontras tema di bawah ambang WCAG AA:\n  " + string.Join("\n  ", temuan));
        }

        [Fact]
        public void DaftarPasangan_UjiDanSkripKontras_TidakMelenceng()
        {
            // Skrip PowerShell dan uji ini harus menguji pasangan yang sama; kalau tidak,
            // salah satunya akan lolos diam-diam dengan aturan yang berbeda.
            var skrip = CoreTestFixture.ReadProjectFile("Tools/check-kontras-wcag.ps1");
            var re = new Regex("""Uji = '(?<u>[^']+)';\s+Fg = '(?<f>[^']+)';\s+Bg = '(?<b>[^']+)';\s+Ambang = (?<a>[0-9.]+)""");

            var dariSkrip = re.Matches(skrip)
                .Select(m => new Pasangan(
                    m.Groups["u"].Value,
                    m.Groups["f"].Value,
                    m.Groups["b"].Value,
                    double.Parse(m.Groups["a"].Value, CultureInfo.InvariantCulture)))
                .ToList();

            Assert.True(dariSkrip.Count >= 30,
                $"Daftar pasangan di skrip kontras hanya terbaca {dariSkrip.Count} baris — periksa format skripnya.");

            var diUji = PasanganWajib.Select(p => $"{p.Uji}|{p.Fg}|{p.Bg}|{p.Ambang}").ToHashSet(StringComparer.Ordinal);
            var diSkrip = dariSkrip.Select(p => $"{p.Uji}|{p.Fg}|{p.Bg}|{p.Ambang}").ToHashSet(StringComparer.Ordinal);

            var hanyaDiSkrip = diSkrip.Except(diUji, StringComparer.Ordinal).ToList();
            var hanyaDiUji = diUji.Except(diSkrip, StringComparer.Ordinal).ToList();

            Assert.True(hanyaDiSkrip.Count == 0 && hanyaDiUji.Count == 0,
                "Daftar pasangan kontras berbeda antara Tools/check-kontras-wcag.ps1 dan KontrasTemaTests:\n" +
                $"  hanya di skrip: {string.Join(", ", hanyaDiSkrip)}\n" +
                $"  hanya di uji  : {string.Join(", ", hanyaDiUji)}");
        }

        /// <summary>
        /// Tema kontras tinggi menargetkan WCAG 2.1 AAA: setiap pasangan teks/latar wajib
        /// >= 7:1, kecuali dua pasangan judul header yang ambangnya sendiri sudah 3:1
        /// karena teksnya besar (20 px SemiBold) — untuk itu AAA menuntut 4,5:1.
        /// Skrip PowerShell menerapkan aturan yang sama untuk tema ini.
        /// </summary>
        [Fact]
        public void TemaKontrasTinggi_MemenuhiAmbangWcagAaa()
        {
            const double ambangTeksNormal = 7.0;
            const double ambangTeksBesar = 4.5;

            var tema = BacaTema(TemaKontrasTinggi);
            var temuan = new List<string>();

            foreach (var p in PasanganWajib)
            {
                var ambang = p.Ambang < 4.5 ? ambangTeksBesar : ambangTeksNormal;

                if (!tema.Brush.TryGetValue(p.Fg, out var kuasFg) ||
                    !tema.Brush.TryGetValue(p.Bg, out var kuasBg))
                {
                    temuan.Add($"{p.Uji}: brush '{p.Fg}' atau '{p.Bg}' tidak ada di {TemaKontrasTinggi}");
                    continue;
                }

                var fg = SelesaikanWarna(kuasFg.Warna, tema);
                var bg = SelesaikanWarna(kuasBg.Warna, tema);
                if (fg is null || bg is null)
                {
                    temuan.Add($"{p.Uji}: warna '{p.Fg}'/'{p.Bg}' tidak dapat diselesaikan");
                    continue;
                }

                if (kuasFg.Opasitas < 1.0) fg = Campur(fg, bg, kuasFg.Opasitas);

                var rasio = Math.Round(Rasio(fg, bg), 2);
                if (rasio < ambang)
                {
                    temuan.Add($"{p.Uji} — rasio {rasio.ToString(CultureInfo.InvariantCulture)} " +
                               $"(butuh {ambang.ToString(CultureInfo.InvariantCulture)}); teks {fg} di atas latar {bg}");
                }
            }

            Assert.True(temuan.Count == 0,
                "Tema kontras tinggi belum memenuhi ambang WCAG AAA:\n  " + string.Join("\n  ", temuan));
        }

        // ---------- 2. Kelengkapan token ----------

        [Fact]
        public void TemaBerwarna_PunyaKunciYangSama()
        {
            var acuan = BacaTema(TemaBerwarna[0]).Kunci;

            foreach (var nama in TemaBerwarna.Skip(1))
            {
                var kunci = BacaTema(nama).Kunci;
                var kurang = acuan.Except(kunci, StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToList();
                var lebih = kunci.Except(acuan, StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToList();

                Assert.True(kurang.Count == 0 && lebih.Count == 0,
                    $"{nama} berbeda dari {TemaBerwarna[0]} — kurang: [{string.Join(", ", kurang)}]; " +
                    $"lebih: [{string.Join(", ", lebih)}]");
            }
        }

        [Fact]
        public void TokenTema_KunciOn_AdaDiSemuaTema()
        {
            // Token "on" adalah pasangan wajib setiap latar berwarna: tanpa token ini,
            // halaman terpaksa memakai putih kaku yang tidak ikut tema.
            string[] wajib = { "OnPrimaryTextBrush", "OnAccentTextBrush", "OnSuccessTextBrush", "OnWarningTextBrush", "OnErrorTextBrush" };

            foreach (var nama in TemaBerwarna)
            {
                var tema = BacaTema(nama);
                foreach (var kunci in wajib)
                {
                    Assert.True(tema.Brush.ContainsKey(kunci), $"{nama} tidak mendefinisikan brush '{kunci}'.");
                }
            }
        }

        [Fact]
        public void RujukanDynamicResource_SelaluTerdefinisi()
        {
            // Salah ketik nama token tidak pernah dilaporkan WPF: nilainya cuma kosong
            // dan tampilan jadi putih/hitam bawaan. Uji ini menangkap ketikan salah itu.
            var didefinisikan = new HashSet<string>(StringComparer.Ordinal);

            foreach (var nama in TemaBerwarna.Concat(KamusBersama))
            {
                var isi = File.ReadAllText(JalurTema(nama));
                foreach (Match m in ReKunci.Matches(isi)) didefinisikan.Add(m.Groups["k"].Value);
            }

            // Kunci yang didefinisikan di App.xaml (konverter, template bersama, dsb.).
            var appXaml = File.ReadAllText(Path.Combine(JalurWpf, "App.xaml"));
            foreach (Match m in ReKunci.Matches(appXaml)) didefinisikan.Add(m.Groups["k"].Value);

            var hilang = new SortedSet<string>(StringComparer.Ordinal);

            foreach (var berkas in SemuaXaml())
            {
                var isi = File.ReadAllText(berkas);
                var relatif = Path.GetRelativePath(JalurWpf, berkas).Replace('\\', '/');

                // Kunci yang didefinisikan di berkas itu sendiri tetap sah.
                var lokal = ReKunci.Matches(isi).Select(m => m.Groups["k"].Value).ToHashSet(StringComparer.Ordinal);

                foreach (Match m in ReRujukanDinamis.Matches(isi))
                {
                    var kunci = m.Groups["k"].Value;
                    if (didefinisikan.Contains(kunci) || lokal.Contains(kunci)) continue;
                    hilang.Add($"{relatif} → {kunci}");
                }
            }

            Assert.True(hilang.Count == 0,
                "Rujukan DynamicResource tanpa definisi (nama token salah atau belum ada di tema):\n  " +
                string.Join("\n  ", hilang));
        }

        // ---------- 3. Warna tetap yang tidak ikut tema ----------

        [Fact]
        public void TeksPutihKaku_HanyaDiTempatYangDisengaja()
        {
            // Teks putih di atas latar berwarna tema adalah sumber kegagalan kontras
            // yang paling sering lolos: latarnya berubah antar tema, teksnya tidak.
            var pelanggar = new List<string>();

            foreach (var berkas in SemuaXaml())
            {
                var relatif = Path.GetRelativePath(JalurWpf, berkas).Replace('\\', '/');
                if (PengecualianTeksPutih.ContainsKey(relatif)) continue;

                var isi = File.ReadAllText(berkas);
                if (ReTeksPutih.IsMatch(isi)) pelanggar.Add(relatif);
            }

            Assert.True(pelanggar.Count == 0,
                "Ada teks putih kaku di luar daftar pengecualian — pakai token On*TextBrush " +
                "(OnAccentTextBrush/OnSuccessTextBrush/OnWarningTextBrush/OnErrorTextBrush/OnPrimaryTextBrush):\n  " +
                string.Join("\n  ", pelanggar.OrderBy(x => x, StringComparer.Ordinal)));
        }
    }
}
