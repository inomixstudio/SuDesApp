using System;

namespace SuDesApp.Wilayah
{
    /// <summary>
    /// Tingkat wilayah administratif pada data wilayah yang dibundel aplikasi.
    /// Kabupaten dan kota dipisah karena surat menyebut keduanya dengan awalan
    /// yang berbeda ("PEMERINTAH KABUPATEN …" vs "PEMERINTAH KOTA …").
    /// </summary>
    public enum WilayahJenis
    {
        Provinsi = 1,
        Kabupaten = 2,
        Kota = 3,
        Kecamatan = 4,
        Desa = 5,
        Kelurahan = 6
    }

    /// <summary>
    /// Satu wilayah administratif dari berkas data terbundel: kode Kemendagri
    /// (mis. "32.15.20.2011"), nama tanpa awalan tingkat (mis. "Sumberjaya"),
    /// induknya, dan kode pos saran untuk desa/kelurahan.
    /// </summary>
    public sealed record WilayahDaerah(
        string Kode,
        string Nama,
        WilayahJenis Jenis,
        string Induk,
        string Kodepos)
    {
        /// <summary>Benar bila wilayah ini punya kode pos saran (hanya desa/kelurahan).</summary>
        public bool Berkodepos => !string.IsNullOrWhiteSpace(Kodepos);

        /// <summary>Benar untuk tingkat yang dipilih pengguna sebagai desa/kelurahan tempatnya.</summary>
        public bool DesaAtauKelurahan => Jenis is WilayahJenis.Desa or WilayahJenis.Kelurahan;

        /// <summary>Awalan pengenal tingkat, mengikuti penulisan surat resmi.</summary>
        public string Awalan => Jenis switch
        {
            WilayahJenis.Provinsi => "Provinsi",
            WilayahJenis.Kabupaten => "Kabupaten",
            WilayahJenis.Kota => "Kota",
            WilayahJenis.Kecamatan => "Kecamatan",
            WilayahJenis.Desa => "Desa",
            WilayahJenis.Kelurahan => "Kelurahan",
            _ => string.Empty
        };

        /// <summary>Nama wilayah lengkap dengan awalannya, mis. "Kabupaten Karawang".</summary>
        public string NamaLengkap => Awalan.Length == 0 ? Nama : Awalan + " " + Nama;

        /// <summary>Nama yang ditampilkan pada daftar pilihan (tanpa awalan tingkat).</summary>
        public override string ToString() => Nama;
    }

    /// <summary>
    /// Asal-usul data wilayah terbundel, dibaca dari kepala berkasnya. Dipakai
    /// halaman Pengaturan untuk memberi tahu pengguna seberapa mutakhir datanya
    /// (data wilayah mengikuti Kepmendagri, jadi versinya perlu bisa dilihat).
    /// </summary>
    public sealed record WilayahInfo(
        string Acuan,
        string SumberDiperbarui,
        int JumlahBaris)
    {
        public static readonly WilayahInfo Kosong = new(string.Empty, string.Empty, 0);
    }
}
