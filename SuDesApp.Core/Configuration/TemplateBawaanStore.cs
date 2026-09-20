using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SuDesApp.Configuration
{
    /// <summary>
    /// Penanda kode contoh Template Surat bawaan yang sudah pernah dipasang otomatis.
    ///
    /// Disimpan di profil pengguna (%LOCALAPPDATA%\SuDesApp) — TERPISAH dari database
    /// dan dari berkas bawaan aplikasi yang ikut ditimpa saat aplikasi diperbarui.
    /// Gunanya satu: mencegah aplikasi menambahkan lagi contoh yang sengaja sudah
    /// dihapus pengguna pada pemasangan otomatis berikutnya.
    /// </summary>
    public static class TemplateBawaanStore
    {
        private const string NamaFolder = "SuDesApp";
        private const string NamaBerkas = "template-bawaan.json";

        private static readonly object _kunci = new();
        private static string? _path;

        /// <summary>Lokasi bawaan berkas penanda (profil pengguna Windows).</summary>
        public static string PathBawaan => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            NamaFolder,
            NamaBerkas);

        /// <summary>
        /// Lokasi berkas penanda yang sedang dipakai. Dapat diarahkan ke berkas lain
        /// (dipakai pengujian agar tidak menyentuh pengaturan pengguna asli).
        /// </summary>
        public static string Path
        {
            get => _path ?? PathBawaan;
            set => _path = string.IsNullOrWhiteSpace(value) ? null : value;
        }

        /// <summary>
        /// Baca kode contoh yang sudah pernah dipasang otomatis. Berkas tidak ada atau
        /// rusak dianggap "belum pernah" (bukan kegagalan aplikasi).
        /// </summary>
        public static HashSet<string> Muat()
        {
            var hasil = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                string path = Path;
                if (!File.Exists(path)) return hasil;

                var kode = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path));
                if (kode == null) return hasil;

                foreach (var satu in kode)
                {
                    if (!string.IsNullOrWhiteSpace(satu)) hasil.Add(satu.Trim());
                }
            }
            catch
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            return hasil;
        }

        /// <summary>Simpan kode yang sudah dipasang; daftar kosong menghapus berkas penanda.</summary>
        public static void Simpan(IEnumerable<string> kode)
        {
            var bersih = (kode ?? Enumerable.Empty<string>())
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => k.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToList();

            lock (_kunci)
            {
                string path = Path;
                string? folder = System.IO.Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(folder))
                {
                    throw new InvalidOperationException("Lokasi berkas penanda template bawaan tidak valid.");
                }

                if (bersih.Count == 0)
                {
                    if (File.Exists(path)) File.Delete(path);
                    return;
                }

                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                File.WriteAllText(path, JsonSerializer.Serialize(bersih,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
        }

        /// <summary>Sudah pernah dipasang otomatis?</summary>
        public static bool SudahPernah() => Muat().Count > 0;

        /// <summary>Lupakan penanda (dipakai pengujian dan pemulihan keadaan awal).</summary>
        public static void Lupakan()
        {
            lock (_kunci)
            {
                try
                {
                    string path = Path;
                    if (File.Exists(path)) File.Delete(path);
                }
                catch
                {
                    // Penanda tidak dapat dihapus (berkas terkunci) — diabaikan.
                }
            }
        }
    }
}
