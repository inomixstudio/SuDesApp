using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SuDesApp.Services
{
    /// <summary>
    /// Salinan katalog paket TERAKHIR yang berhasil diambil dari server,
    /// disimpan di <c>%LocalAppData%\SuDesApp\katalog-paket.json</c>.
    ///
    /// Kenapa ada: kartu paket di halaman Langganan menampilkan harga resmi
    /// dari <c>GET /paket</c>. Bila server lisensi sedang tidak bisa dijangkau
    /// (belum dijalankan, jaringan putus, atau alamat server keliru), tanpa
    /// salinan ini semua kartu kehilangan angkanya dan hanya menampilkan
    /// "Harga dari server" — padahal angkanya sudah pernah diketahui dan jarang
    /// berubah.
    ///
    /// PENTING: salinan ini HANYA untuk tampilan. Nominal yang benar-benar
    /// ditagih tetap ditentukan server saat <c>/buat-tagihan</c> dibuat, jadi
    /// salinan yang basi tidak bisa membuat pengguna membayar harga yang salah.
    /// </summary>
    public static class KatalogPaketTersimpan
    {
        /// <summary>
        /// Penunjuk folder untuk pengujian. Null = lokasi bawaan
        /// (<c>%LocalAppData%\SuDesApp</c>). Pola sama dengan
        /// <c>LisensiManager.FolderCacheOverride</c>.
        /// </summary>
        internal static string? FolderOverride { get; set; }

        private static string Folder => FolderOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SuDesApp");

        private static string Berkas => Path.Combine(Folder, "katalog-paket.json");

        /// <summary>Satu entri harga paket, persis seperti yang dikirim server.</summary>
        public sealed class Paket
        {
            [JsonPropertyName("kode")] public string Kode { get; set; } = "";
            [JsonPropertyName("nama")] public string Nama { get; set; } = "";
            [JsonPropertyName("harga")] public long Harga { get; set; }
            [JsonPropertyName("hari")] public int Hari { get; set; }
        }

        /// <summary>
        /// Simpan katalog. Gagal diam-diam: ini hanya bantuan tampilan, bukan
        /// data yang wajib — kegagalan menulis tidak boleh menggagalkan apa pun.
        /// </summary>
        public static void Simpan(IEnumerable<Paket> paket)
        {
            try
            {
                var daftar = new List<Paket>();
                foreach (var p in paket)
                {
                    // Hanya entri yang masuk akal: harga/hari <= 0 bukan paket
                    // yang bisa ditampilkan dengan benar.
                    if (!string.IsNullOrWhiteSpace(p.Kode) && p.Harga > 0 && p.Hari > 0)
                        daftar.Add(p);
                }
                if (daftar.Count == 0) return;

                Directory.CreateDirectory(Folder);
                File.WriteAllText(Berkas, JsonSerializer.Serialize(
                    daftar, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
                // Bantuan tampilan non-kritis.
            }
        }

        /// <summary>
        /// Muat katalog tersimpan. Null bila belum pernah ada, berkas rusak,
        /// atau isinya kosong — pemanggil lalu membiarkan kartu apa adanya.
        /// </summary>
        public static IReadOnlyList<Paket>? Muat()
        {
            try
            {
                if (!File.Exists(Berkas)) return null;
                var daftar = JsonSerializer.Deserialize<List<Paket>>(File.ReadAllText(Berkas));
                if (daftar is null || daftar.Count == 0) return null;
                return daftar;
            }
            catch
            {
                // Berkas rusak/terpotong → anggap belum ada.
                return null;
            }
        }
    }
}
