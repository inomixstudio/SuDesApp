using System;
using System.Globalization;

namespace SuDesApp.Utilities
{
    /// <summary>
    /// Kecepatan animasi antarmuka yang dipilih pengguna. Urutannya dari yang paling
    /// lambat ke paling cepat, mengikuti urutan tampil di halaman Pengaturan Aplikasi.
    /// </summary>
    public enum KecepatanAnimasi
    {
        Lambat,
        Normal,
        Cepat
    }

    /// <summary>
    /// Aturan kecepatan animasi antarmuka beserta penyimpanannya di preferensi aplikasi.
    ///
    /// Seluruh transisi yang bisa disesuaikan (lebar sidebar, daftar nama menu,
    /// dropdown grup, sorotan menu) mengambil durasinya dari sini lewat
    /// <see cref="Skala(TimeSpan)"/>, jadi satu pilihan pengguna berlaku serentak di
    /// semua tempat tanpa perlu membuka ulang aplikasi.
    ///
    /// Dipisah dari UI supaya bisa diperiksa uji tanpa membuka jendela.
    /// </summary>
    public static class KecepatanAnimasiPrefs
    {
        private const string NilaiLambat = "lambat";
        private const string NilaiNormal = "normal";
        private const string NilaiCepat = "cepat";

        /// <summary>
        /// Durasi terpendek & terpanjang setelah diskalakan. Batas bawah menjaga animasi
        /// tetap terlihat sebagai gerakan (bukan lompatan) walau pengguna memilih Cepat;
        /// batas atas menahan animasi agar tidak terasa menggantung.
        /// </summary>
        private static readonly TimeSpan DurasiMinimum = TimeSpan.FromMilliseconds(70);
        private static readonly TimeSpan DurasiMaksimum = TimeSpan.FromMilliseconds(2000);

        /// <summary>Pengali durasi dasar untuk sebuah pilihan kecepatan.</summary>
        public static double Faktor(KecepatanAnimasi kecepatan) => kecepatan switch
        {
            KecepatanAnimasi.Lambat => 1.9,
            KecepatanAnimasi.Cepat => 0.5,
            _ => 1.0
        };

        /// <summary>
        /// Durasi yang benar-benar dipakai untuk sebuah durasi dasar. Durasi nol (mis.
        /// animasi yang dimatikan pemanggilnya) dibiarkan nol.
        /// </summary>
        public static TimeSpan Durasi(KecepatanAnimasi kecepatan, TimeSpan dasar)
        {
            if (dasar <= TimeSpan.Zero)
            {
                return TimeSpan.Zero;
            }

            double ms = dasar.TotalMilliseconds * Faktor(kecepatan);
            ms = Math.Min(Math.Max(ms, DurasiMinimum.TotalMilliseconds), DurasiMaksimum.TotalMilliseconds);
            return TimeSpan.FromMilliseconds(Math.Round(ms));
        }

        /// <summary>Skalakan durasi dasar memakai kecepatan yang sedang berlaku.</summary>
        public static TimeSpan Skala(TimeSpan dasar) => Durasi(SaatIni, dasar);

        /// <summary>Durasi transisi sidebar (lebar kolom, panah, nama aplikasi) — nilai dasar.</summary>
        public static readonly TimeSpan DasarSidebar = TimeSpan.FromMilliseconds(260);

        /// <summary>Durasi tumbuh/menyusut isi dropdown grup — nilai dasar.</summary>
        public static readonly TimeSpan DasarDropdown = TimeSpan.FromMilliseconds(240);

        /// <summary>Durasi memudarnya isi dropdown saat dibuka/ditutup — nilai dasar.</summary>
        public static readonly TimeSpan DasarDropdownFade = TimeSpan.FromMilliseconds(220);

        /// <summary>Durasi sorotan menu saat kursor mendekat — nilai dasar.</summary>
        public static readonly TimeSpan DasarSorotMasuk = TimeSpan.FromMilliseconds(160);

        /// <summary>Durasi sorotan menu saat kursor menjauh — nilai dasar.</summary>
        public static readonly TimeSpan DasarSorotKeluar = TimeSpan.FromMilliseconds(220);

        /// <summary>Jeda sebelum daftar nama menu menutup setelah kursor menjauh.</summary>
        public static readonly TimeSpan DasarJedaDaftarMenu = TimeSpan.FromMilliseconds(320);

        /// <summary>Durasi transisi saat berpindah halaman (isi jendela memudar masuk).</summary>
        public static readonly TimeSpan DasarTransisiHalaman = TimeSpan.FromMilliseconds(280);

        /// <summary>Nama pilihan kecepatan untuk ditampilkan ("Lambat", "Normal", "Cepat").</summary>
        public static string Judul(KecepatanAnimasi kecepatan) => kecepatan switch
        {
            KecepatanAnimasi.Lambat => "Lambat",
            KecepatanAnimasi.Cepat => "Cepat",
            _ => "Normal"
        };

        /// <summary>Ringkasan pengali, mis. "1,9× lebih lambat dari bawaan".</summary>
        public static string FaktorTeks(KecepatanAnimasi kecepatan) => kecepatan switch
        {
            KecepatanAnimasi.Lambat => "1,9× lebih lambat dari bawaan",
            KecepatanAnimasi.Cepat => "0,5× dari bawaan (setengah waktu)",
            _ => "1× — bawaan aplikasi"
        };

        /// <summary>Kapan pilihan ini sebaiknya dipakai.</summary>
        public static string Keterangan(KecepatanAnimasi kecepatan) => kecepatan switch
        {
            KecepatanAnimasi.Lambat =>
                "Setiap perpindahan bergerak paling halus, sehingga arah dan tujuan animasinya mudah diikuti mata.",
            KecepatanAnimasi.Cepat =>
                "Nyaris seketika — cocok bila halaman sering berpindah dan tidak ingin menunggu gerakan selesai.",
            _ =>
                "Bawaan aplikasi: gerakannya halus tetapi tetap gesit, seimbang untuk pemakaian sehari-hari."
        };

        /// <summary>
        /// Contoh nyata dampak pilihan ini, mis. "sidebar ± 0,13 dtk". Dipakai di halaman
        /// pengaturan supaya pengguna tahu bedanya sebelum menekan contoh animasi.
        /// </summary>
        public static string ContohTeks(KecepatanAnimasi kecepatan, TimeSpan dasar)
            => "± " + Detik(Durasi(kecepatan, dasar)) + " dtk";

        /// <summary>Durasi dalam detik dengan dua angka di belakang koma (koma sebagai desimal).</summary>
        public static string Detik(TimeSpan durasi)
            => (durasi.TotalSeconds).ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');

        /// <summary>Ringkasan satu baris untuk statusbar/informasi halaman pengaturan.</summary>
        public static string Ringkasan(KecepatanAnimasi kecepatan)
            => $"Kecepatan animasi: {Judul(kecepatan)} ({FaktorTeks(kecepatan)}) — " +
               $"sidebar {ContohTeks(kecepatan, DasarSidebar)}, dropdown {ContohTeks(kecepatan, DasarDropdown)}.";

        // ===== Preferensi =====

        private static KecepatanAnimasi? _cache;

        /// <summary>
        /// Kecepatan yang sedang berlaku. Dibaca sekali lalu disimpan di memori agar
        /// animasi (yang berjalan puluhan kali per detik) tidak membaca berkas preferensi
        /// terus-menerus; <see cref="Simpan"/> memperbarui nilai di memori ini juga.
        /// </summary>
        public static KecepatanAnimasi SaatIni => _cache ??= Muat();

        /// <summary>Baca ulang pilihan dari berkas preferensi (mis. setelah diubah di luar halaman).</summary>
        public static void Segarkan() => _cache = Muat();

        /// <summary>
        /// Pilihan tersimpan pengguna. Pemasangan lama (belum pernah memilih) memakai
        /// kecepatan bawaan, jadi pembaruan aplikasi tidak mengubah gerakan aplikasi.
        /// </summary>
        public static KecepatanAnimasi Muat()
            => DariPreferensi(AppPreferenceStore.GetString(AppPreferenceStore.KeyKecepatanAnimasi, null));

        /// <summary>Simpan pilihan kecepatan, sekaligus menerapkannya pada animasi berikutnya.</summary>
        public static void Simpan(KecepatanAnimasi kecepatan)
        {
            _cache = kecepatan;
            AppPreferenceStore.SetString(AppPreferenceStore.KeyKecepatanAnimasi, KePreferensi(kecepatan));
        }

        /// <summary>Nilai preferensi sebuah pilihan (stabil, tidak ikut berubah bila enum diurutkan ulang).</summary>
        public static string KePreferensi(KecepatanAnimasi kecepatan) => kecepatan switch
        {
            KecepatanAnimasi.Lambat => NilaiLambat,
            KecepatanAnimasi.Cepat => NilaiCepat,
            _ => NilaiNormal
        };

        /// <summary>Baca nilai preferensi; nilai tidak dikenal selalu jatuh ke kecepatan bawaan.</summary>
        public static KecepatanAnimasi DariPreferensi(string? nilai) => nilai?.Trim().ToLowerInvariant() switch
        {
            NilaiLambat => KecepatanAnimasi.Lambat,
            NilaiCepat => KecepatanAnimasi.Cepat,
            _ => KecepatanAnimasi.Normal
        };
    }
}
