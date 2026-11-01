using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SuDesApp.Services
{
    /// <summary>
    /// Catatan hasil pemeriksaan masa berlaku sertifikat TLS situs IDM:
    /// kapan diperiksa dan sampai kapan sertifikatnya berlaku.
    ///
    /// Ditulis oleh dua jalur yang sama-sama memeriksa situs — halaman IDM saat
    /// memuat data dan pemantau latar (PemantauIdmBaru) tiap interval pengguna —
    /// dan dibaca keduanya untuk menampilkan peringatan dini. Catatan ini yang
    /// membuat peringatan tetap tampil bahkan sebelum halaman IDM pernah dibuka.
    /// </summary>
    public sealed class IdmSertifikatCatatan
    {
        /// <summary>Tanggal mulai berlaku sertifikat (NotBefore).</summary>
        [JsonPropertyName("berlakuDari")] public DateTime? BerlakuDari { get; set; }

        /// <summary>Tanggal akhir berlaku sertifikat (NotAfter) — dasar peringatan dini.</summary>
        [JsonPropertyName("berlakuSampai")] public DateTime? BerlakuSampai { get; set; }

        /// <summary>Subjek sertifikat, untuk bahan pelaporan ke Kemendesa.</summary>
        [JsonPropertyName("subjek")] public string Subjek { get; set; } = "";

        /// <summary>Kapan sertifikat ini berhasil dibaca (bukan waktu pemeriksaan yang gagal).</summary>
        [JsonPropertyName("waktuDiperiksa")] public DateTime? WaktuDiperiksa { get; set; }
    }

    /// <summary>Keluar masuk catatan sertifikat (dipisah supaya bisa diuji tanpa disk).</summary>
    public interface IIdmSertifikatStore
    {
        /// <summary>Baca catatan; null bila belum ada atau tidak terbaca (bukan kegagalan fatal).</summary>
        IdmSertifikatCatatan? Muat();

        /// <summary>Timpa catatan. Kegagalan I/O tidak dibalikkan.</summary>
        void Simpan(IdmSertifikatCatatan catatan);
    }

    /// <summary>
    /// Catatan sertifikat versi berkas JSON lokal
    /// (<c>%LocalAppData%\SuDesApp\idm-sertifikat.json</c>). Pola yang sama
    /// dengan <see cref="IdmPemantauanStore"/>: berkas rusak/absen menghasilkan
    /// null dan aplikasi tetap jalan, hanya tanpa peringatan dini.
    /// </summary>
    public sealed class IdmSertifikatStore : IIdmSertifikatStore
    {
        /// <summary>Penunjuk folder untuk pengujian — pola IdmPemantauanStore.</summary>
        internal static string? FolderOverride { get; set; }

        private static string BerkasPath => Path.Combine(
            FolderOverride ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SuDesApp"),
            "idm-sertifikat.json");

        public IdmSertifikatCatatan? Muat()
        {
            try
            {
                var jalur = BerkasPath;
                if (!File.Exists(jalur)) return null;

                return JsonSerializer.Deserialize<IdmSertifikatCatatan>(File.ReadAllText(jalur));
            }
            catch (Exception)
            {
                return null;
            }
        }

        public void Simpan(IdmSertifikatCatatan catatan)
        {
            try
            {
                var jalur = BerkasPath;
                Directory.CreateDirectory(Path.GetDirectoryName(jalur)!);
                File.WriteAllText(jalur, JsonSerializer.Serialize(catatan));
            }
            catch (Exception)
            {
                // Catatan gagal ditulis tidak menghalangi pengguna: peringatan
                // hilang sampai pemeriksaan berikutnya, bukan galat yang tampil.
            }
        }
    }
}
