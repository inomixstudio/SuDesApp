using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using SuDesApp.Utilities;
using Xunit;

namespace SuDesApp.Core.Tests
{
    /// <summary>
    /// Uji setelan <b>tinggi maksimum baris tabel</b> yang bisa diatur operator lewat
    /// Pengaturan Aplikasi (Register Surat, Register NTCR, API Desa, Data Warga),
    /// termasuk tombol pilihan cepat Ringkas / Normal / Lega.
    ///
    /// Nilainya disimpan di preferensi aplikasi, jadi berkasnya ditunjuk lewat
    /// <c>AppPreferenceStore.LokasiOverride</c> seperti KunciIdlePreferenceTests —
    /// berkas preferensi profil user asli tidak pernah tersentuh. Koleksi "API" dipakai
    /// bersama karena override itu statis (satu proses).
    ///
    /// Keterhubungan mesin tinggi baris (proyek WPF) diperiksa dengan membaca berkas
    /// sumbernya, sama seperti TinggiBarisDinamisTests, karena proyek uji hanya
    /// mereferensi SuDesApp.Core.
    /// </summary>
    [Collection("API")]
    public sealed class TinggiBarisMaksimumPreferenceTests : IDisposable
    {
        private const string JalurMesin = "SuDesApp.Wpf/Input/TinggiBarisDinamis.cs";
        private const string JalurPengaturan = "SuDesApp.Wpf/ViewModels/PengaturanAplikasiViewModel.cs";
        private const string JalurXamlPengaturan = "SuDesApp.Wpf/Views/PengaturanAplikasiView.xaml";
        private const string JalurCatatanRilis = "SuDesApp.Wpf/ViewModels/CatatanRilisViewModel.cs";

        private readonly string _jalur;

        public TinggiBarisMaksimumPreferenceTests()
        {
            _jalur = Path.Combine(
                Path.GetTempPath(), "sudes-test",
                Guid.NewGuid().ToString("N"), "login_prefs.json");
            AppPreferenceStore.LokasiOverride = () => _jalur;
        }

        public void Dispose()
        {
            AppPreferenceStore.LokasiOverride = null;
            try
            {
                var dir = Path.GetDirectoryName(_jalur);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // Bersih-bersih berkas sementara non-kritis.
            }
        }

        private static string Sumber(string jalurRelatif) =>
            CoreTestFixture.ReadProjectFile(jalurRelatif);

        // ---------- preferensi ----------

        [Fact]
        public void TinggiBarisMaksimum_Bawaan_200Piksel()
        {
            Assert.Equal(200, AppPreferenceStore.TinggiBarisMaksimumBawaanPx);
            Assert.Equal(200, AppPreferenceStore.GetTinggiBarisMaksimumPx());
        }

        [Fact]
        public void TinggiBarisMaksimum_RentangSah_TersimpanTepat()
        {
            AppPreferenceStore.SetTinggiBarisMaksimumPx(AppPreferenceStore.TinggiBarisMaksimumTerendahPx);
            Assert.Equal(40, AppPreferenceStore.GetTinggiBarisMaksimumPx());

            AppPreferenceStore.SetTinggiBarisMaksimumPx(AppPreferenceStore.TinggiBarisMaksimumTertinggiPx);
            Assert.Equal(600, AppPreferenceStore.GetTinggiBarisMaksimumPx());

            AppPreferenceStore.SetTinggiBarisMaksimumPx(320);
            Assert.Equal(320, AppPreferenceStore.GetTinggiBarisMaksimumPx());
        }

        [Fact]
        public void TinggiBarisMaksimum_DiLuarRentang_JatuhKeBawaan()
        {
            // 0 px akan memampatkan seluruh baris (teks pasti terpotong) dan nilai
            // raksasa membuat satu baris memenuhi layar — keduanya ditolak.
            foreach (var nilai in new[] { 0, 1, 39, 601, 5000, -50 })
            {
                AppPreferenceStore.SetTinggiBarisMaksimumPx(nilai);
                Assert.Equal(200, AppPreferenceStore.GetTinggiBarisMaksimumPx());
            }
        }

        [Fact]
        public void TinggiBarisMaksimum_BerkasPreferensiRusak_JatuhKeBawaan()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_jalur)!);
            File.WriteAllText(_jalur, "tinggiBarisMaksimumPx=abc\n");

            Assert.Equal(200, AppPreferenceStore.GetTinggiBarisMaksimumPx());
        }

        [Fact]
        public void TinggiBarisMaksimum_TerendahSelaluDiAtasTinggiMinimumBaris()
        {
            // Mesin memasang Math.Clamp(nilai, TinggiMinimum, batas operator). Bila
            // batas terendah yang boleh dipilih operator berada di bawah TinggiMinimum,
            // rentangnya terbalik dan pengukuran gagal. Penjaga ini mengikat kedua angka
            // yang tinggal di dua proyek berbeda.
            var mesin = Sumber(JalurMesin);
            var m = Regex.Match(mesin, @"TinggiMinimum\s*=\s*(?<n>[\d.]+)");
            Assert.True(m.Success, "TinggiMinimum mesin tinggi baris tidak ditemukan.");

            double tinggiMinimum = double.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture);
            Assert.True(
                AppPreferenceStore.TinggiBarisMaksimumTerendahPx > tinggiMinimum,
                $"Batas terendah setelan ({AppPreferenceStore.TinggiBarisMaksimumTerendahPx}) wajib di atas " +
                $"TinggiMinimum mesin ({tinggiMinimum}).");
        }

        // ---------- keterhubungan mesin & halaman pengaturan ----------

        [Fact]
        public void MesinTinggiBaris_MemakaiBatasMaksimumDariPreferensi()
        {
            var mesin = Sumber(JalurMesin);

            Assert.Contains("using SuDesApp.Utilities;", mesin);
            Assert.Contains("TinggiMaksimumEfektif", mesin);
            Assert.Contains("AppPreferenceStore.GetTinggiBarisMaksimumPx()", mesin);
            Assert.Contains("SegarkanBatasMaksimum", mesin);

            // Batas yang benar-benar dipakai Clamp adalah nilai efektif, bukan konstanta
            // bawaan — kalau tidak, setelan operator tidak akan pernah berpengaruh.
            Assert.Contains("Math.Clamp(Math.Ceiling(tinggiTeks), TinggiMinimum, batasMaksimum)", mesin);
            Assert.DoesNotContain("Math.Clamp(Math.Ceiling(tinggiTeks), TinggiMinimum, TinggiMaksimum)", mesin);

            // Pembacaan berkas preferensi terjadi sekali (diingat), bukan per baris tabel.
            Assert.Contains("_tinggiMaksimumEfektif ??= AppPreferenceStore.GetTinggiBarisMaksimumPx()", mesin);
        }

        [Fact]
        public void PengaturanAplikasi_MenyediakanBarisTinggiMaksimum_DenganRentangDariStore()
        {
            var pengaturan = Sumber(JalurPengaturan);

            Assert.Contains("Tinggi maksimum baris tabel (piksel)", pengaturan);
            Assert.Contains("AppPreferenceStore.GetTinggiBarisMaksimumPx(),", pengaturan);
            Assert.Contains("AppPreferenceStore.SetTinggiBarisMaksimumPx(v);", pengaturan);
            Assert.Contains("min: AppPreferenceStore.TinggiBarisMaksimumTerendahPx", pengaturan);
            Assert.Contains("maks: AppPreferenceStore.TinggiBarisMaksimumTertinggiPx", pengaturan);

            // Setelan baru langsung dipakai tabel yang sedang terbuka.
            Assert.Contains("TinggiBarisDinamis.SegarkanBatasMaksimum();", pengaturan);
        }

        [Fact]
        public void CatatanRilis_MencatatSetelanTinggiMaksimumBaris()
        {
            var catatan = Sumber(JalurCatatanRilis);

            Assert.Contains("tinggi maksimum baris tabel", catatan, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("40-600", catatan);
            Assert.Contains("Ringkas, Normal, atau Lega", catatan);
        }

        // ---------- tombol pilihan cepat ----------

        /// <summary>Blok daftar pilihan cepat tinggi baris pada ViewModel pengaturan.</summary>
        private static string BlokPilihanCepat()
        {
            var pengaturan = Sumber(JalurPengaturan);
            var awal = pengaturan.IndexOf("PilihanTinggiBarisTabel() => new", StringComparison.Ordinal);
            Assert.True(awal >= 0, "Daftar pilihan cepat tinggi baris tidak ditemukan.");

            var blok = pengaturan[awal..];
            var akhir = blok.IndexOf("};", StringComparison.Ordinal);
            Assert.True(akhir > 0, "Daftar pilihan cepat tidak tertutup rapi.");
            return blok[..akhir];
        }

        /// <summary>
        /// Nilai langsung sebuah pilihan cepat, mis. ("Ringkas", 80) → 80. Sengaja tanpa
        /// regex bertanda kutip: berkas sumbernya diperiksa apa adanya.
        /// </summary>
        private static int NilaiPilihan(string judul)
        {
            var blok = BlokPilihanCepat();
            var awal = blok.IndexOf(judul + "\", ", StringComparison.Ordinal);
            Assert.True(awal >= 0, "Pilihan cepat " + judul + " tidak ditemukan.");

            var sisa = blok[(awal + judul.Length + 3)..];
            var akhir = sisa.IndexOf(')');
            Assert.True(akhir > 0, "Nilai pilihan cepat " + judul + " tidak diakhiri tanda kurung.");

            var teks = sisa[..akhir].Trim();
            Assert.True(int.TryParse(teks, NumberStyles.Integer, CultureInfo.InvariantCulture, out var nilai),
                "Nilai pilihan " + judul + " bukan angka langsung: " + teks);
            return nilai;
        }

        [Fact]
        public void PilihanCepat_Tersedia_RingkasNormalLega_DalamRentangSah()
        {
            var blok = BlokPilihanCepat();

            // "Normal" wajib memakai angka bawaan aplikasi (bukan angka yang ditulis ulang)
            // supaya tombolnya tidak pernah melenceng dari setelan bawaan.
            Assert.Contains("Lega", blok);
            Assert.Contains("AppPreferenceStore.TinggiBarisMaksimumBawaanPx", blok);
            Assert.DoesNotContain("\"Normal\", 2", blok);

            int ringkas = NilaiPilihan("Ringkas");
            int lega = NilaiPilihan("Lega");

            // Ketiganya harus berada dalam rentang yang diizinkan dan bermakna berurutan.
            foreach (var nilai in new[] { ringkas, lega })
            {
                Assert.InRange(nilai, AppPreferenceStore.TinggiBarisMaksimumTerendahPx,
                    AppPreferenceStore.TinggiBarisMaksimumTertinggiPx);
            }

            Assert.True(ringkas < AppPreferenceStore.TinggiBarisMaksimumBawaanPx,
                "Pilihan Ringkas wajib lebih rendah dari bawaan.");
            Assert.True(lega > AppPreferenceStore.TinggiBarisMaksimumBawaanPx,
                "Pilihan Lega wajib lebih tinggi dari bawaan.");
        }

        [Fact]
        public void PilihanCepat_TekanTombol_MenyimpanNilaiDanMenandaiYangAktif()
        {
            var pengaturan = Sumber(JalurPengaturan);

            // Tombol memakai perintah sendiri (tanpa parameter) dan jalur simpan yang sama
            // dengan kotak isian, sehingga tabel yang terbuka langsung menyesuaikan.
            Assert.Contains("PakaiCommand = new RelayCommand(() => pakai(nilai));", pengaturan);
            Assert.Contains("private void Pakai(int angka)", pengaturan);
            Assert.Contains("try { _persist(angka); } catch", pengaturan);

            // Pilihan yang sedang dipakai ditandai supaya operator tahu posisinya.
            Assert.Contains("PerbaruiPilihanCepatAktif();", pengaturan);
            Assert.Contains("pilihan.Aktif = adaAngka && aktif == pilihan.Nilai;", pengaturan);
            Assert.Contains("public bool AdaPilihanCepat => PilihanCepat.Count > 0;", pengaturan);

            // Baris tinggi maksimum memang diberi pilihan cepat, dan nilai di luar rentang
            // tidak pernah ditawarkan sebagai tombol.
            Assert.Contains("pilihanCepat: PilihanTinggiBarisTabel()", pengaturan);
            Assert.Contains("if (nilai < _min || nilai > _maks) continue;", pengaturan);
        }

        [Fact]
        public void PengaturanAplikasi_Xaml_MenampilkanTombolPilihanCepatDiSampingKotakAngka()
        {
            var xaml = Sumber(JalurXamlPengaturan);

            Assert.Contains("ItemsSource=\"{Binding PilihanCepat}\"", xaml);
            Assert.Contains("Command=\"{Binding PakaiCommand}\"", xaml);
            Assert.Contains("Visibility=\"{Binding AdaPilihanCepat, Converter={StaticResource BoolToVis}}\"", xaml);
            Assert.Contains("Style=\"{StaticResource ChipAngkaStyle}\"", xaml);

            // Gaya chip: latar aksen saat pilihan itu sedang dipakai.
            Assert.Contains("x:Key=\"ChipAngkaStyle\"", xaml);
            Assert.Contains("<DataTrigger Binding=\"{Binding Aktif}\" Value=\"True\">", xaml);

            // Kotak isian tetap ada di sebelah tombol cepat (kolom ketiga).
            Assert.Contains("Grid.Column=\"2\" Style=\"{StaticResource SettingInput}\"", xaml);
        }
    }
}
