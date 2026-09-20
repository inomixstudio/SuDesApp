using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SuDesApp.Configuration
{
    /// <summary>
    /// Penyesuaian penomoran surat yang diubah pengguna (mis. awalan SKD 470 → 471).
    ///
    /// Disimpan TERPISAH dari berkas bawaan <c>Configuration/JenisSuratConfig.json</c>
    /// karena berkas bawaan itu ikut ditimpa saat aplikasi diperbarui — penyesuaian
    /// pengguna harus tetap bertahan. Berkas bawaan tetap menjadi daftar jenis surat
    /// dan nilai default; berkas ini hanya "menempel" di atasnya.
    ///
    /// Isi berkas: peta NamaJenis → NomorFormat lengkap, mis.
    /// <c>{ "SKD_UMUM": "471/{0:D3}/Ds/{2:yyyy}" }</c>. Jenis yang tidak tercantum
    /// memakai format bawaan, sehingga "kembalikan bawaan" cukup menghapus entrinya.
    /// </summary>
    public static class PenomoranOverrideStore
    {
        private const string NamaFolder = "SuDesApp";
        private const string NamaBerkas = "penomoran-surat.json";

        private static readonly object _kunci = new();
        private static string? _path;

        /// <summary>Lokasi bawaan berkas penyesuaian (profil pengguna Windows).</summary>
        public static string PathBawaan => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            NamaFolder,
            NamaBerkas);

        /// <summary>
        /// Lokasi berkas penyesuaian yang sedang dipakai. Dapat diarahkan ke berkas
        /// lain (dipakai pengujian agar tidak menyentuh pengaturan pengguna asli).
        /// </summary>
        public static string Path
        {
            get => _path ?? PathBawaan;
            set => _path = string.IsNullOrWhiteSpace(value) ? null : value;
        }

        /// <summary>
        /// Baca seluruh penyesuaian. Berkas tidak ada / rusak → peta kosong (bukan
        /// kegagalan), supaya aplikasi tetap jalan dengan format bawaan.
        /// </summary>
        public static Dictionary<string, string> Muat()
        {
            var hasil = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var path = Path;
                if (!File.Exists(path)) return hasil;

                var teks = File.ReadAllText(path);
                var peta = JsonSerializer.Deserialize<Dictionary<string, string>>(teks);
                if (peta == null) return hasil;

                foreach (var pasangan in peta)
                {
                    if (string.IsNullOrWhiteSpace(pasangan.Key) || string.IsNullOrWhiteSpace(pasangan.Value))
                        continue;

                    hasil[pasangan.Key.Trim()] = pasangan.Value.Trim();
                }
            }
            catch
            {
                // Berkas rusak/tak terbaca → anggap tidak ada penyesuaian.
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            return hasil;
        }

        /// <summary>
        /// Tulis ulang seluruh penyesuaian (peta kosong = tidak ada penyesuaian sama
        /// sekali). Berkas lama tetap dicadangkan lebih dulu oleh pemanggil.
        /// </summary>
        public static void Simpan(IReadOnlyDictionary<string, string> peta)
        {
            lock (_kunci)
            {
                var path = Path;
                var folder = System.IO.Path.GetDirectoryName(path);

                if (string.IsNullOrEmpty(folder))
                    throw new InvalidOperationException("Lokasi berkas penomoran tidak valid.");
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

                if (peta == null || peta.Count == 0)
                {
                    // Tidak ada penyesuaian: berkas dihapus agar daftar jenis surat
                    // sepenuhnya kembali ke bawaan aplikasi.
                    if (File.Exists(path)) File.Delete(path);
                    return;
                }

                var teks = JsonSerializer.Serialize(peta,
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, teks);
            }
        }

        /// <summary>Format penyesuaian untuk satu jenis surat; null bila tidak ada.</summary>
        public static string? FormatUntuk(string namaJenis)
        {
            if (string.IsNullOrWhiteSpace(namaJenis)) return null;
            var peta = Muat();
            return peta.TryGetValue(namaJenis.Trim(), out var format) ? format : null;
        }
    }
}
