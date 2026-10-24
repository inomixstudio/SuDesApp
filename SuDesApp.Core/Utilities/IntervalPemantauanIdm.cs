using System;

namespace SuDesApp.Utilities
{
    /// <summary>
    /// Seberapa sering pemantau latar memeriksa data IDM baru di portal pusat.
    /// Urutannya mengikuti urutan tampil di halaman Pengaturan Aplikasi.
    /// </summary>
    public enum IntervalPemantauanIdm
    {
        /// <summary>Pemeriksaan berulang tiap enam jam (bawaan, perilaku lama).</summary>
        EnamJam,

        /// <summary>Pemeriksaan berulang sekali sehari.</summary>
        DuaPuluhEmpatJam,

        /// <summary>
        /// Tanpa pengulangan: satu pemeriksaan sesaat setelah aplikasi dibuka,
        /// lalu pemantau latar berhenti sampai aplikasi dibuka lagi.
        /// </summary>
        HanyaSaatDibuka
    }

    /// <summary>
    /// Aturan interval pemantauan IDM beserta penyimpanannya di preferensi
    /// aplikasi. Dipisah dari UI dan dari layanan pemantau supaya bisa diuji
    /// tanpa membuka jendela.
    ///
    /// Bawaan (belum pernah memilih) adalah enam jam — persis perilaku aplikasi
    /// sebelum pilihan ini ada, jadi pembaruan aplikasi tidak mengubah apa pun
    /// bagi pengguna lama.
    /// </summary>
    public static class IntervalPemantauanIdmPrefs
    {
        private const string NilaiEnamJam = "6jam";
        private const string NilaiDuaPuluhEmpatJam = "24jam";
        private const string NilaiHanyaSaatDibuka = "dibuka";

        /// <summary>Pilihan yang sedang tersimpan.</summary>
        public static IntervalPemantauanIdm SaatIni => Muat();

        /// <summary>Baca pilihan dari preferensi; nilai tak dikenal jatuh ke bawaan.</summary>
        public static IntervalPemantauanIdm Muat() =>
            DariPreferensi(AppPreferenceStore.GetString(AppPreferenceStore.KeyIntervalPemantauanIdm, null));

        /// <summary>Simpan pilihan pengguna.</summary>
        public static void Simpan(IntervalPemantauanIdm pilihan) =>
            AppPreferenceStore.SetString(AppPreferenceStore.KeyIntervalPemantauanIdm, KePreferensi(pilihan));

        /// <summary>
        /// Jeda antar pemeriksaan BERULANG. Null berarti tidak ada pengulangan —
        /// satu pemeriksaan saja saat aplikasi dibuka
        /// (<see cref="IntervalPemantauanIdm.HanyaSaatDibuka"/>).
        /// Pemindaian pertama setelah aplikasi dibuka selalu memakai jeda awal
        /// layanan pemantau, bukan nilai ini.
        /// </summary>
        public static TimeSpan? JedaBerulang(IntervalPemantauanIdm pilihan) => pilihan switch
        {
            IntervalPemantauanIdm.DuaPuluhEmpatJam => TimeSpan.FromHours(24),
            IntervalPemantauanIdm.HanyaSaatDibuka => null,
            _ => TimeSpan.FromHours(6)
        };

        /// <summary>Nama pilihan untuk ditampilkan di pengaturan ("Setiap 6 jam", …).</summary>
        public static string Judul(IntervalPemantauanIdm pilihan) => pilihan switch
        {
            IntervalPemantauanIdm.DuaPuluhEmpatJam => "Setiap 24 jam",
            IntervalPemantauanIdm.HanyaSaatDibuka => "Hanya saat aplikasi dibuka",
            _ => "Setiap 6 jam"
        };

        /// <summary>Penjelasan akibat pilihan, ditulis apa adanya di halaman pengaturan.</summary>
        public static string Keterangan(IntervalPemantauanIdm pilihan) => pilihan switch
        {
            IntervalPemantauanIdm.DuaPuluhEmpatJam =>
                "Portal diperiksa sekali sehari. Lebih hemat kuota internet, tetapi data tahun baru "
                + "bisa baru diketahui sampai sehari kemudian.",
            IntervalPemantauanIdm.HanyaSaatDibuka =>
                "Satu pemeriksaan saja sekitar 45 detik setelah aplikasi dibuka, lalu berhenti — "
                + "tidak ada pemeriksaan berulang selama aplikasi terbuka. Halaman IDM tetap bisa "
                + "memuat data kapan saja lewat tombol Muat ulang.",
            _ =>
                "Bawaan — portal diperiksa empat kali sehari (tiap enam jam) selama aplikasi terbuka, "
                + "jadi data tahun baru biasanya diketahui pada hari yang sama."
        };

        /// <summary>
        /// Nilai preferensi sebuah pilihan. Ditulis sebagai teks stabil supaya
        /// mengurutkan ulang enum di kemudian hari tidak mengubah arti nilai lama.
        /// </summary>
        public static string KePreferensi(IntervalPemantauanIdm pilihan) => pilihan switch
        {
            IntervalPemantauanIdm.DuaPuluhEmpatJam => NilaiDuaPuluhEmpatJam,
            IntervalPemantauanIdm.HanyaSaatDibuka => NilaiHanyaSaatDibuka,
            _ => NilaiEnamJam
        };

        /// <summary>Baca nilai preferensi; kosong/tak dikenal selalu jatuh ke enam jam.</summary>
        public static IntervalPemantauanIdm DariPreferensi(string? nilai) => nilai?.Trim().ToLowerInvariant() switch
        {
            NilaiDuaPuluhEmpatJam => IntervalPemantauanIdm.DuaPuluhEmpatJam,
            NilaiHanyaSaatDibuka => IntervalPemantauanIdm.HanyaSaatDibuka,
            _ => IntervalPemantauanIdm.EnamJam
        };
    }
}
