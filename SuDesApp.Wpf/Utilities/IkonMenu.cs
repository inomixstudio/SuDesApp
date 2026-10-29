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
        /// <summary>Dokumen/daftar surat — Register Surat, item surat, Register NTCR.</summary>
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
        /// <summary>Alias akun Google (memakai glyph orang).</summary>
        public const string AkunGoogle = Orang;

        // ==== Pengaturan & bantuan ====
        /// <summary>Gerigi — semua menu Pengaturan.</summary>
        public const string Pengaturan = "\uE713";
        /// <summary>Gembok — Ubah Kata Sandi.</summary>
        public const string KataSandi = "\uE72E";
        /// <summary>Gembok — penanda menu vital yang terkunci karena lisensi belum aktif.</summary>
        public const string Gembok = "\uE72E";
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
        /// <summary>Kartu pembayaran — halaman Langganan (status &amp; perpanjangan lisensi).</summary>
        public const string Langganan = "\uE8C7";
        /// <summary>Orang beroda gigi — pusat administrasi server lisensi (glyph "Admin").</summary>
        public const string PusatAdmin = "\uE7EF";
        /// <summary>Silang jendela — Keluar.</summary>
        public const string Keluar = "\uE711";

        // ==== Halaman fitur ====
        /// <summary>Data kependudukan — halaman Data Warga (glyph "People").</summary>
        public const string Warga = "\uE716";
        /// <summary>Buku bacaan — halaman Dokumentasi &amp; pembaca dokumen.</summary>
        public const string Dokumentasi = "\uE7BC";
        /// <summary>Dokumen laporan — halaman Laporan (glyph "ReportDocument").</summary>
        public const string Laporan = "\uE9F9";
        /// <summary>Kode program — halaman API (glyph "Code").</summary>
        public const string Api = "\uE943";
        /// <summary>Mesin cetak — tombol/aksi cetak (glyph "Print").</summary>
        public const string Cetak = "\uE749";
        /// <summary>Akun lain — halaman Kelola Pengguna (glyph "OtherUser").</summary>
        public const string Pengguna = "\uE7EE";
        /// <summary>Tanda centang — halaman Verifikasi Surat (glyph "CheckMark").</summary>
        public const string Verifikasi = "\uE73E";
        /// <summary>Kalender — halaman Tutup Buku Tahunan (glyph "Calendar").</summary>
        public const string TutupBuku = "\uE787";
        /// <summary>Kalender — tombol pembuka kalender di kolom tanggal DatePicker.</summary>
        public const string Kalender = "\uE787";

        // ==== Bagian halaman Perangkat Desa ====
        // Ikon bagian dipilih dari glyph yang ada di Segoe Fluent Icons maupun
        // Segoe MDL2 Assets, dan sudah diperiksa tampilannya satu per satu.
        /// <summary>Perisai — bagian LINMAS (satuan perlindungan masyarakat).</summary>
        public const string Linmas = "\uEA18";
        /// <summary>Detak jantung — bagian Posyandu (kesehatan ibu &amp; anak).</summary>
        public const string Posyandu = "\uE95E";
        /// <summary>Monitor kesehatan — bagian Kader Stunting (pemantauan tumbuh kembang).</summary>
        public const string KaderStunting = "\uE9D9";
        /// <summary>Silsilah keluarga — bagian PKK (pemberdayaan keluarga).</summary>
        public const string Pkk = "\uEBD2";
        /// <summary>Kotak arsip — bagian LPM (lembaga kemasyarakatan desa).</summary>
        public const string Lpm = "\uE7B8";
        /// <summary>Grup orang — bagian Karang Taruna (organisasi kepemudaan).</summary>
        public const string KarangTaruna = "\uE902";

        // ==== Keuangan & pembangunan desa ====
        /// <summary>Keranjang belanja — halaman APBDES (anggaran & realisasi desa).</summary>
        public const string Keuangan = "\uE7BF";
        /// <summary>Bagan pertumbuhan — halaman Indeks Desa Membangun.</summary>
        public const string Pembangunan = "\uE81D";

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

        // ==== Ikon dialog pesan (badge bulat pada MessageDialogWindow) ====
        // Dipilih dari glyph yang ada di Segoe Fluent Icons maupun Segoe MDL2
        // Assets, lalu diperiksa tampilannya satu per satu.
        /// <summary>Lingkaran berhuruf i — dialog informasi.</summary>
        public const string Info = "\uE946";
        /// <summary>Lingkaran bertanda tanya — dialog konfirmasi.</summary>
        public const string Tanya = "\uE9CE";
        /// <summary>Lingkaran bersilang — dialog galat (berbeda dari E711
        /// yang silang polos untuk tombol tutup).</summary>
        public const string GalatLingkaran = "\uEA39";

        // ==== Kolom sandi (jendela masuk) ====
        // Dipakai sebagai tombol mata di dalam kolom sandi: keadaan tersembunyi
        // menampilkan mata terbuka, keadaan terlihat menampilkan mata bercoret.
        // Kedua codepoint sudah diperiksa ada di Segoe Fluent Icons maupun
        // Segoe MDL2 Assets (lihat uji IkonMenuFontTests).
        /// <summary>Mata terbuka — tampilkan sandi yang sedang diketik.</summary>
        public const string LihatSandi = "\uE7B3";
        /// <summary>Mata bercoret — sembunyikan sandi kembali.</summary>
        public const string SembunyikanSandi = "\uED1A";

        // ==== Tindakan di dalam halaman ====
        /// <summary>Kaca pembesar — cari, cari di daftar.</summary>
        public const string Cari = "\uE721";
        /// <summary>Panah melingkar — muat ulang dari database.</summary>
        public const string MuatUlang = "\uE72C";
        /// <summary>Arsip terbuka — pilih berkas dari komputer.</summary>
        public const string BukaBerkas = "\uE8E5";
        /// <summary>Mata — pratinjau berkas/dokumen.</summary>
        public const string Pratinjau = "\uE890";
        /// <summary>Tempat sampah — hapus baris.</summary>
        public const string Hapus = "\uE74D";
        /// <summary>Pencil — ubah data.</summary>
        public const string Ubah = "\uE70F";
        /// <summary>Panah turun — muat daftar dari sumber lain (memakai glyph SuratMasuk).</summary>
        public const string MuatDaftar = SuratMasuk;
        /// <summary>Silang — kosongkan pencarian/saringan (memakai glyph Keluar).</summary>
        public const string Bersihkan = Keluar;
    }
}
