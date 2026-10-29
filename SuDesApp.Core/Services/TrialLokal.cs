using System;
using System.IO;
using System.Text.Json;

namespace SuDesApp.Services
{
    /// <summary>
    /// Trial 30 hari LOKAL — jalan buka akses untuk instalasi yang belum punya
    /// kode aktivasi (belum berlangganan) tanpa menyentuh server lisensi.
    ///
    /// Sengaja tidak memakai server: instal pertama di desa sering belum punya
    /// koneksi/kode, tetapi tetap harus bisa membuka fitur selama masa uji coba.
    /// Waktu mulai disimpan di berkas <c>%LocalAppData%\SuDesApp\trial-lokal.json</c>
    /// (pola yang sama dengan cache lisensi, lihat <see cref="LisensiManager"/>).
    ///
    /// Batasannya jelas dan disadari: trial lokal bisa diakali dengan menghapus
    /// atau mengedit berkas. Ini adalah keputusan produk yang diterima — trial
    /// hanyalah jembatan sementara menuju langganan berbayar yang diverifikasi
    /// tanda tangannya oleh server.
    /// </summary>
    public sealed class TrialLokal
    {
        public const int DURASI_HARI = 30;

        /// <summary>Penunjuk folder untuk pengujian; null = lokasi bawaan %LocalAppData%\SuDesApp.</summary>
        internal static string? FolderOverride { get; set; }

        private static string Folder => FolderOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SuDesApp");

        private static string LokasiBerkas => Path.Combine(Folder, "trial-lokal.json");

        private sealed class IsiTrial
        {
            public DateTime MulaiUtc { get; set; }
        }

        private DateTime? _mulaiUtcCache;

        /// <summary>Waktu mulai trial (UTC), atau null bila belum pernah dimulai.</summary>
        public DateTime? MulaiUtc => BacaMulaiUtc();

        /// <summary>True bila trial pernah dimulai pada perangkat ini.</summary>
        public bool SudahMulai => BacaMulaiUtc() is not null;

        /// <summary>Batas akhir trial (UTC), atau null bila belum pernah dimulai.</summary>
        public DateTime? BerakhirUtc => BacaMulaiUtc() is DateTime mulai
            ? mulai.AddDays(DURASI_HARI)
            : null;

        /// <summary>True bila trial masih berjalan (sekarang &lt; mulai + 30 hari).</summary>
        public bool Aktif(DateTime? sekarangUtc = null)
        {
            var akhir = BerakhirUtc;
            if (akhir is null) return false;
            return (sekarangUtc ?? DateTime.UtcNow) < akhir.Value;
        }

        /// <summary>Sisa hari trial (0 bila habis atau belum mulai).</summary>
        public int SisaHari(DateTime? sekarangUtc = null)
        {
            var akhir = BerakhirUtc;
            if (akhir is null) return 0;
            return Math.Max(0, (int)Math.Ceiling((akhir.Value - (sekarangUtc ?? DateTime.UtcNow)).TotalDays));
        }

        /// <summary>
        /// Mulai trial sekarang. Tidak mengubah apa pun bila sudah pernah dimulai
        /// (trial tidak bisa direset dengan memanggil ulang). True = baru dimulai.
        /// </summary>
        public bool MulaiSekarang()
        {
            if (SudahMulai) return false;

            _mulaiUtcCache = DateTime.UtcNow;
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(LokasiBerkas, JsonSerializer.Serialize(
                    new IsiTrial { MulaiUtc = _mulaiUtcCache.Value }));
            }
            catch
            {
                // Gagal menulis berkas tidak menggagalkan permulaan trial di sesi
                // ini; hanya saja tidak bertahan saat aplikasi ditutup.
            }

            return true;
        }

        private DateTime? BacaMulaiUtc()
        {
            if (_mulaiUtcCache is not null) return _mulaiUtcCache;

            try
            {
                if (File.Exists(LokasiBerkas))
                {
                    var isi = JsonSerializer.Deserialize<IsiTrial>(File.ReadAllText(LokasiBerkas));
                    if (isi is not null && isi.MulaiUtc != default)
                    {
                        _mulaiUtcCache = isi.MulaiUtc;
                    }
                }
            }
            catch
            {
                // Berkas rusak/hilang = dianggap belum pernah trial (fail closed).
            }

            return _mulaiUtcCache;
        }
    }
}