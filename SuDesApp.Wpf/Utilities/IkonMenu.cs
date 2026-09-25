namespace SuDesApp.Wpf.Utilities
{
    /// <summary>
    /// Pusat codepoint ikon sidebar &amp; jendela utama. Semua nilai adalah glyph dari
    /// font ikon <c>Segoe Fluent Icons</c> / <c>Segoe MDL2 Assets</c> (resource
    /// <c>AppIconFont</c> di ThemeStyles.xaml) — BUKAN karakter teks biasa.
    ///
    /// Aturan pemakaian:
    /// - C#: langsung <c>IkonMenu.Beranda</c> (konstanta, tanpa alokasi runtime).
    /// - XAML: <c>Text="{x:Static u:IkonMenu.Beranda}"</c> dengan
    ///   <c>xmlns:u="clr-namespace:SuDesApp.Wpf.Utilities"</c> dan
    ///   <c>FontFamily="{StaticResource AppIconFont}"</c>.
    ///
    /// Jangan menulis glyph literal (★ ● ⚠ ❯ …) atau codepoint "\uE7xx" baru di
    /// view/view model — tambahkan konstanta di sini. Glyph teks biasa tidak punya
    /// gambar di font ikon sehingga tampil sebagai kotak/karakter rusak, dan
    /// codepoint tersebar sulit diaudit ketika font/ganti ikon perlu diseragamkan.
    /// </summary>
    public static class IkonMenu
    {
        // ==== Navigasi utama ====
        /// <summary>Rumah — halaman Beranda.</summary>
        public const string Beranda = "\uE80F";
        /// <summary>Dokumen/daftar surat — Register Surat, item surat, Register NTCR, Panduan WhatsApp.</summary>
        public const string Dokumen = "\uE8F1";
        /// <summary>Halaman dokumen polos — fallback item tanpa ikon khusus.</summary>
        public const string Halaman = "\uE7C9";
        /// <summary>Blanko/formulir yang diisi — Template Surat, SK/Perdes/Perkades, blanko NTCR, Formulir.</summary>
        public const string Blanko = "\uE8A5";
        /// <summary>Tanda plus — buat baru / tambah.</summary>
        public const string Tambah = "\uE710";
        /// <summary>Folder — chip header accordion bawaan.</summary>
        public const string Folder = "\uE8B7";

        // ==== Kelompok surat ====
        /// <summary>Produk hukum — Surat Peraturan, Catatan Rilis.</summary>
        public const string Peraturan = "\uE7C3";
        /// <summary>Arsip/agenda — akordeon Surat Masuk/Keluar.</summary>
        public const string Agenda = "\uE896";
        /// <summary>Panah masuk — agenda surat masuk &amp; Daftar Hadir.</summary>
        public const string SuratMasuk = "\uE716";
        /// <summary>Panah keluar — agenda surat keluar.</summary>
        public const string SuratKeluar = "\uE717";
        /// <summary>Alias Daftar Hadir (memakai glyph panah masuk).</summary>
        public const string DaftarHadir = SuratMasuk;

        // ==== Layanan & akun ====
        /// <summary>Orang/kontak — akordeon NTCR &amp; Login dengan Google.</summary>
        public const string Orang = "\uE77B";
        /// <summary>Jam riwayat — Riwayat Aktivitas.</summary>
        public const string Riwayat = "\uE81C";
        /// <summary>Obrolan/layanan daring — Layanan Online.</summary>
        public const string LayananOnline = "\uE774";
        /// <summary>Alias akun Google (memakai glyph orang).</summary>
        public const string AkunGoogle = Orang;

        // ==== Pengaturan & bantuan ====
        /// <summary>Gerigi — semua menu Pengaturan.</summary>
        public const string Pengaturan = "\uE713";
        /// <summary>Gembok — Ubah Kata Sandi.</summary>
        public const string KataSandi = "\uE72E";
        /// <summary>Simpan — Cadangkan &amp; Pulihkan.</summary>
        public const string Cadangkan = "\uE74E";
        /// <summary>Palet warna — akordeon Tema.</summary>
        public const string Tema = "\uE790";
        /// <summary>Bintang garis — tema bawaan di daftar tema.</summary>
        public const string TemaDefault = "\uE735";
        /// <summary>Bintang isi — tema pilihan lain di daftar tema.</summary>
        public const string TemaLain = "\uE734";
        /// <summary>Halaman Pembaruan (memakai glyph arsip/agenda).</summary>
        public const string Pembaruan = Agenda;
        /// <summary>Lapisan peta — Panduan Awal.</summary>
        public const string Panduan = "\uE897";
        /// <summary>Info — halaman Tentang.</summary>
        public const string Tentang = "\uE946";
        /// <summary>Silang jendela — Keluar.</summary>
        public const string Keluar = "\uE711";

        // ==== Elemen jendela (status bar, header sidebar) ====
        /// <summary>Tiga garis — tombol buka/tutup sidebar.</summary>
        public const string GeserSidebar = "\uE00F";
        /// <summary>Chevron kanan — penanda arah accordion &amp; hover item.</summary>
        public const string ChevronKanan = "\uE76C";
        /// <summary>Lonceng notifikasi — status bar.</summary>
        public const string Lonceng = "\uE7ED";
        /// <summary>Sinkronisasi — animasi pemeriksaan pembaruan.</summary>
        public const string Sinkron = "\uE895";
        /// <summary>Segitiga peringatan — toast notifikasi.</summary>
        public const string Peringatan = "\uE7BA";
    }
}
