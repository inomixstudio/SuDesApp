namespace SuDesApp.Server
{
    /// <summary>
    /// Satu pengumuman broadcast (pemberitahuan ke semua instalasi desa) yang
    /// diterima aplikasi dari server. Semua kolomnya datang dari tabel
    /// <c>pengumuman</c> di Supabase (lihat schema.sql).
    ///
    /// Kelas ini HIDUP di sisi SERVER dan sisi KLIEN sekaligus (berbagi bentuk
    /// record supaya JSON server dan parser klien tidak bisa menyimpang) —
    /// untuk itu ia menempati namespace bersama tanpa dependensi bersama.
    /// </summary>
    public sealed record PengumumanServer(
        string Id,
        string Judul,
        string Isi,
        string Jenis,
        DateTime Dibuat,
        DateTime? Sampai)
    {
        /// <summary>
        /// Nilai kolom <c>aktif</c>. False = pengumuman dimatikan/diarsipkan
        /// operator: tidak boleh muncul di endpoint publik, tetapi tetap
        /// terlihat di daftar admin. Default true supaya pemanggil lama yang
        /// tidak menyebut kolom ini tidak diam-diam menyembunyikan pengumuman.
        /// </summary>
        public bool Aktif { get; init; } = true;

        /// <summary>
        /// Benar bila pengumuman ini berada di dalam jendela tampilnya pada
        /// <paramref name="waktu"/> (kolom mulai/sampai saja; kolom aktif
        /// diperiksa terpisah oleh <see cref="LayananPengumuman.SaringAktif"/>).
        /// Logika jendela ini diuji unit sehingga tetap kecil dan benar; SQL
        /// hanya membaca baris mentah.
        /// </summary>
        public bool TampilPada(DateTime waktu) =>
            (Sampai is null || Sampai > waktu)
            && (Mulai is null || Mulai <= waktu);

        /// <summary>
        /// Nilai kolom <c>mulai</c> timestamptz — ikut dalam record supaya logika
        /// jendela bisa lengkap, tetapi TIDAK dikirim server ke klien: klien
        /// tidak perlu tahu kapan suatu pengumuman mulai boleh hidup, ia hanya
        /// menerima yang sudah layak.
        /// </summary>
        public DateTime? Mulai { get; init; }
    }

    /// <summary>
    /// Aturan endpoint pengumuman: mana yang aktif dan bentuk JSON yang dikirim
    /// klien. Dipisah dari lambda endpoint supaya bisa diuji unit tanpa HTTP
    /// maupun Postgres — pola yang sama dengan LayananWebhook/LayananAdmin.
    /// </summary>
    public static class LayananPengumuman
    {
        /// <summary>Maksimal baris yang diambil per satu putaran baca tabel.</summary>
        public const int BatasMuat = 50;

        /// <summary>
        /// Saring daftar mentah dari tabel menjadi yang benar-benar boleh
        /// tampil: aktif, di dalam jendela waktu, dan hanya yang masuk akal
        /// (id + judul terisi). Daftar diurutkan terbaru dulu.
        /// </summary>
        public static List<PengumumanServer> SaringAktif(
            IEnumerable<PengumumanServer> mentah, DateTime waktuSeimbang)
        {
            return mentah
                // Kolom aktif adalah hak veto operator: pengumuman yang
                // dimatikan tidak pernah boleh lolos ke klien, apa pun
                // jendela waktunya.
                .Where(p => p.Aktif && p.TampilPada(waktuSeimbang))
                .Where(p => !string.IsNullOrWhiteSpace(p.Id) && !string.IsNullOrWhiteSpace(p.Judul))
                .OrderByDescending(p => p.Dibuat)
                .ToList();
        }

        // ---- Validasi pengumuman baru (endpoint admin POST /admin/pengumuman) --
        // Murni tanpa I/O supaya bisa diuji unit; lambda endpoint hanya
        // meneriskan null/alasan ke HTTP.

        public const int BatasJudul = 150;
        public const int BatasIsi = 4000;

        /// <summary>
        /// Sah=null; ketidak-sahannya = teks alasannya untuk pengguna halaman
        /// admin. Jenis diterima: "info" (bawaan) / "peringatan" — nilai lain
        /// bukan galat tetapi dirapikan ke "info" oleh <see cref="RapikanJenis"/>,
        /// jadi tabel pulih tidak penuh oleh jenis aneh.
        /// </summary>
        public static string? ValidasiBuat(
            string? judul, string? isi, string? jenis, DateTime? mulai, DateTime? sampai)
        {
            var judulBersih = (judul ?? string.Empty).Trim();
            if (judulBersih.Length == 0)
                return "Judul wajib diisi.";
            if (judulBersih.Length > BatasJudul)
                return $"Judul maksimal {BatasJudul} karakter.";

            var isiBersih = (isi ?? string.Empty).Trim();
            if (isiBersih.Length > BatasIsi)
                return $"Isi maksimal {BatasIsi} karakter.";

            if (mulai.HasValue && sampai.HasValue && sampai.Value <= mulai.Value)
                return "\"Berhenti\" harus lebih akhir daripada \"Mulai\".";

            return null;
        }

        /// <summary>Jenis yang dikenal: sekali di sini, bukan tersebar di UI.</summary>
        public static bool JenisDikenal(string? jenis) =>
            string.Equals(jenis, "info", StringComparison.OrdinalIgnoreCase)
            || string.Equals(jenis, "peringatan", StringComparison.OrdinalIgnoreCase);

        public static string RapikanJenis(string? jenis) =>
            JenisDikenal(jenis) ? jenis!.Trim().ToLowerInvariant() : "info";
    }
}
