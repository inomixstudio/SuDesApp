using System.Text.RegularExpressions;
using Xunit;

namespace SuDesApp.Core.Tests
{
    /// <summary>
    /// Penjaga tinggi baris DataGrid. Pelajaran penting aplikasi ini:
    /// <c>RowHeight="Auto"</c> memicu loop layout tanpa henti → StackOverflowException
    /// (terbukti tiga kali menjatuhkan aplikasi), sehingga dilarang di seluruh XAML.
    /// Penggantinya: <c>TinggiBarisDinamis</c> — jumlah baris teks dihitung per baris
    /// data lewat FormattedText, lalu dipasang sebagai tinggi angka tetap per baris,
    /// sehingga teks sepanjang apa pun terbaca tanpa risiko loop layout.
    /// </summary>
    public sealed class TinggiBarisDinamisTests
    {
        /// <summary>Halaman dengan kolom teks panjang yang wajib memakai tinggi dinamis.</summary>
        private static readonly string[] HalamanKolomPanjang =
        {
            "SuDesApp.Wpf/Views/ApiView.xaml",
            "SuDesApp.Wpf/Views/RegisterSuratView.xaml",
            "SuDesApp.Wpf/Views/RegisterNtcrView.xaml",
            "SuDesApp.Wpf/Views/WargaView.xaml"
        };

        private static string Sumber(string jalurRelatif) =>
            CoreTestFixture.ReadProjectFile(jalurRelatif);

        [Fact]
        public void SeluruhXaml_TidakAdaRowHeightAuto()
        {
            // Satu pelanggaran = loop layout = aplikasi jatuh. Diperiksa pada semua
            // XAML tampilan dan tema, bukan hanya halaman yang pernah kena.
            // Komentar XML dibuang lebih dulu — beberapa halaman menyebut larangan
            // ini di komentar, dan komentar tidak pernah dieksekusi.
            var akar = System.IO.Path.Combine(CoreTestFixture.FindProjectRoot(), "SuDesApp.Wpf");
            var komentar = new Regex("<!--.*?-->", RegexOptions.Singleline);

            var diperiksa = new System.Collections.Generic.List<string>();
            foreach (var berkas in System.IO.Directory.EnumerateFiles(akar, "*.xaml", System.IO.SearchOption.AllDirectories))
            {
                var relatif = System.IO.Path.GetRelativePath(akar, berkas).Replace('\\', '/');
                if (relatif.StartsWith("obj/") || relatif.StartsWith("bin/")) continue;

                var isi = komentar.Replace(System.IO.File.ReadAllText(berkas), string.Empty);
                Assert.False(
                    isi.Contains("RowHeight=\"Auto\"", StringComparison.OrdinalIgnoreCase),
                    $"{relatif} memakai RowHeight=\"Auto\" — memicu loop layout (stack overflow). " +
                    "Gunakan input:TinggiBarisDinamis.Aktif=\"True\" atau tinggi angka tetap.");
                diperiksa.Add(relatif);
            }

            Assert.True(diperiksa.Count > 30, "Pemindaian XAML tidak masuk akal: " + diperiksa.Count);
        }

        [Fact]
        public void HalamanKolomPanjang_MemakaiTinggiBarisDinamis()
        {
            foreach (var jalur in HalamanKolomPanjang)
            {
                Assert.Contains("input:TinggiBarisDinamis.Aktif=\"True\"", Sumber(jalur));
            }
        }

        [Fact]
        public void TinggiBarisDinamis_TidakMengubahRowHeightGrid_MelainkanPerBaris()
        {
            // Mesinnya wajib memasang tinggi pada DataGridRow (angka tetap per baris),
            // BUKAN mengubah RowHeight koleksi grid — dan tentu bukan "Auto".
            // Doc-comment (///) dibuang karena memang menuliskan larangannya.
            var sumber = string.Join("\n", Sumber("SuDesApp.Wpf/Input/TinggiBarisDinamis.cs")
                .Split('\n')
                .Where(baris => !baris.TrimStart().StartsWith("///")));

            Assert.Contains("baris.Height = tinggi", sumber);
            Assert.DoesNotContain("RowHeight", sumber);

            // Penghitungan baris teks memakai FormattedText dengan lebar nyata.
            Assert.Contains("FormattedText", sumber);
            Assert.Contains("MaxTextWidth", sumber);
        }

        [Fact]
        public void TinggiBarisDinamis_AdaBatasMinimumDanMaksimum()
        {
            var sumber = Sumber("SuDesApp.Wpf/Input/TinggiBarisDinamis.cs");

            // Tanpa batas, satu baris dengan teks ekstrem bisa membengkak tak terkendali.
            Assert.Matches(@"TinggiMinimum\s*=\s*[\d.]+", sumber);
            Assert.Matches(@"TinggiMaksimum\s*=\s*[\d.]+", sumber);
            Assert.Contains("Math.Clamp(", sumber);
        }

        [Fact]
        public void KolomMelipat_MenuliskanTextWrappingWrap_PadaElementStyle()
        {
            // Mesin penghitung melipat kolom yang ber-TextWrapping di ElementStyle-nya
            // (atau yang TextBlock selnya memang melipat); pastikan halaman-halaman
            // panjang tetap menandainya supaya tinggi dinamisnya benar-benar bekerja.
            foreach (var jalur in HalamanKolomPanjang)
            {
                var isi = Sumber(jalur);
                Assert.True(
                    isi.Contains("TextWrapping\" Value=\"Wrap\"", StringComparison.OrdinalIgnoreCase),
                    $"{jalur} tidak punya kolom ber-TextWrapping=Wrap; tinggi dinamis tidak akan bekerja.");
            }
        }

        [Fact]
        public void SeluruhXaml_TidakMemakaiSetterTextBlockTextWrappingYangDiabaikan()
        {
            // Pelajaran dari pemeriksaan aplikasi berjalan: setter
            // <Setter Property="TextBlock.TextWrapping" Value="Wrap"/> di dalam gaya
            // DataGridCell DIABAIKAN WPF — TextBlock sel tetap NoWrap, sehingga alamat
            // panjang terpotong TANPA baris tambahan walau XAML-nya “sudah melipat”.
            // Register Surat & Register NTCR sempat memakai pola ini. TextWrapping
            // harus ditulis di ElementStyle kolomnya (atau TextBlock template).
            var akar = System.IO.Path.Combine(CoreTestFixture.FindProjectRoot(), "SuDesApp.Wpf");
            var komentar = new Regex("<!--.*?-->", RegexOptions.Singleline);

            var diperiksa = new System.Collections.Generic.List<string>();
            foreach (var berkas in System.IO.Directory.EnumerateFiles(akar, "*.xaml", System.IO.SearchOption.AllDirectories))
            {
                var relatif = System.IO.Path.GetRelativePath(akar, berkas).Replace('\\', '/');
                if (relatif.StartsWith("obj/") || relatif.StartsWith("bin/")) continue;

                var isi = komentar.Replace(System.IO.File.ReadAllText(berkas), string.Empty);
                Assert.False(
                    isi.Contains("Property=\"TextBlock.TextWrapping\"", StringComparison.OrdinalIgnoreCase),
                    $"{relatif} memakai setter Property=\"TextBlock.TextWrapping\" yang diabaikan WPF " +
                    "(teks tetap NoWrap). Pindahkan TextWrapping ke ElementStyle kolom.");
                diperiksa.Add(relatif);
            }

            Assert.True(diperiksa.Count > 30, "Pemindaian XAML tidak masuk akal: " + diperiksa.Count);
        }

        [Fact]
        public void TinggiBarisDinamis_MengukurDariMetrikTextBlockSelYangTerRender()
        {
            // Pelajaran dari pengukuran aplikasi berjalan: hitungan versi lama memakai
            // huruf GAYA kolom/grid + lebar kolom, sehingga menghasilkan satu baris LEBIH
            // BANYAK daripada yang dirender (baris pendek menyisakan satu baris kosong;
            // Register Surat: “Dusun Krajan RT 004 RW 002, Desa Sumberrejo” dihitung 2
            // baris padahal terlihat 1 baris). Sekarang huruf, lebar lipat, dan tinggi
            // barisnya diambil dari TextBlock sel yang sudah ter-render — sumber yang sama
            // dengan yang dilihat pengguna.
            var mesin = Sumber("SuDesApp.Wpf/Input/TinggiBarisDinamis.cs");

            Assert.Contains("CariTextBlockSel(kolom, baris)", mesin);
            Assert.Contains("terRender.FontFamily, terRender.FontStyle,", mesin);
            Assert.Contains("Math.Max(terRender.ActualWidth, LebarKolom(kolom))", mesin);
            Assert.Contains("terformat.LineHeight", mesin);

            // Tinggi tidak lagi dihitung dengan pengali tetap ukuran huruf.
            Assert.DoesNotContain("jumlah * ukuranKolom * FaktorBaris", mesin);

            // Tidak boleh ada sisa instrumentasi sementara (pernah dipakai saat mengukur).
            Assert.DoesNotContain("SMOKE", mesin);
            Assert.DoesNotContain("DiagnostikUji", mesin);
        }

        [Fact]
        public void TinggiBarisDinamis_MembacaLipatanDariTextBlockSelYangTerRender()
        {
            // Sumber kebenaran lipatan adalah TextBlock sel yang benar-benar
            // ter-render — bukan hanya XAML kolomnya. Tanpa ini, kolom teks yang
            // melipat karena gaya lain berhenti di tinggi minimum (teks terpotong).
            var sumber = Sumber("SuDesApp.Wpf/Input/TinggiBarisDinamis.cs");

            Assert.Contains("GetCellContent", sumber);
            Assert.Contains("CariTextBlockSel", sumber);
            Assert.Contains("terRender.TextWrapping != TextWrapping.NoWrap", sumber);
        }
    }
}
