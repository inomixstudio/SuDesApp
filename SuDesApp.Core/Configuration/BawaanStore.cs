using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SuDesApp.Configuration
{
    /// <summary>
    /// Penanda data bawaan (contoh) yang sudah pernah disemai — SATU berkas untuk
    /// seluruh jenis contoh.
    ///
    /// Disimpan di profil pengguna (<c>%LOCALAPPDATA%\SuDesApp\bawaan.json</c>),
    /// TERPISAH dari folder Bawaan milik aplikasi (yang ikut ditimpa pembaruan)
    /// dan dari database. Gunanya satu: mencegah aplikasi menyemai ulang contoh
    /// yang sudah pernah terpasang lalu dihapus pengguna — sehingga hapusan
    /// menempel — sambil tetap menyemaikan contoh BARU yang dikirim pembaruan
    /// (kuncinya belum tercatat, jadi penyemaian otomatis menyiapkannya).
    ///
    /// Bentuk berkas: <c>{ "TemplateSurat": ["kode-1", "kode-2"] }</c>.
    /// Berkas penanda lama (<c>template-bawaan.json</c>, daftar datar kode
    /// Template Surat) masih ikut dibaca untuk jenis TemplateSurat supaya
    /// penanda instalasi lama tidak hilang saat naik versi.
    /// </summary>
    public static class BawaanStore
    {
        /// <summary>Jenis penanda: contoh Template Surat (kodenya, mis. "pengantar-rt-rw").</summary>
        public const string JenisTemplateSurat = "TemplateSurat";

        private const string NamaFolder = "SuDesApp";
        private const string NamaBerkas = "bawaan.json";
        private const string NamaBerkasLama = "template-bawaan.json";

        private static readonly object _kunci = new();
        private static string? _path;
        private static string? _pathLama;

        private static readonly JsonSerializerOptions OpsiJson = new()
        {
            WriteIndented = true
        };

        /// <summary>Lokasi bawaan berkas penanda (profil pengguna Windows).</summary>
        public static string PathBawaan => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            NamaFolder,
            NamaBerkas);

        /// <summary>Lokasi bawaan berkas penanda lama (dibaca untuk migrasi).</summary>
        public static string PathLamaBawaan => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            NamaFolder,
            NamaBerkasLama);

        /// <summary>
        /// Lokasi berkas penanda yang sedang dipakai. Dapat diarahkan ke berkas lain
        /// (dipakai pengujian agar tidak menyentuh penanda pengguna asli).
        /// </summary>
        public static string Path
        {
            get => _path ?? PathBawaan;
            set => _path = string.IsNullOrWhiteSpace(value) ? null : value;
        }

        /// <summary>Lokasi berkas penanda lama yang sedang dipakai (pengujian).</summary>
        public static string PathLama
        {
            get => _pathLama ?? PathLamaBawaan;
            set => _pathLama = string.IsNullOrWhiteSpace(value) ? null : value;
        }

        /// <summary>
        /// Baca kunci penanda satu jenis. Berkas tidak ada atau rusak dianggap
        /// "belum pernah" (bukan kegagalan aplikasi). Untuk TemplateSurat, hasil
        /// digabung dengan berkas penanda lama bila masih ada.
        /// </summary>
        public static HashSet<string> Muat(string jenis)
        {
            var hasil = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(jenis)) return hasil;

            try
            {
                string path = Path;
                if (File.Exists(path))
                {
                    var peta = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(
                        File.ReadAllText(path));
                    if (peta != null && peta.TryGetValue(jenis, out var kunci) && kunci != null)
                    {
                        foreach (var satu in kunci)
                        {
                            if (!string.IsNullOrWhiteSpace(satu)) hasil.Add(satu.Trim());
                        }
                    }
                }
            }
            catch
            {
                // Penanda utama rusak — diperlakukan sebagai kosong, bukan gagal.
            }

            if (string.Equals(jenis, JenisTemplateSurat, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string lama = PathLama;
                    if (File.Exists(lama))
                    {
                        var daftarLama = JsonSerializer.Deserialize<List<string>>(
                            File.ReadAllText(lama));
                        foreach (var satu in daftarLama ?? new List<string>())
                        {
                            if (!string.IsNullOrWhiteSpace(satu)) hasil.Add(satu.Trim());
                        }
                    }
                }
                catch
                {
                    // Penanda lama rusak — diabaikan (penanda utama tetap dipakai).
                }
            }

            return hasil;
        }

        /// <summary>
        /// Simpan kunci penanda satu jenis; jenis lain pada berkas yang sama tidak
        /// disentuh. Daftar kosong menghapus entri jenis itu (dan berkasnya bila
        /// seluruh jenis kosong).
        /// </summary>
        public static void Simpan(string jenis, IEnumerable<string> kunci)
        {
            if (string.IsNullOrWhiteSpace(jenis))
            {
                throw new ArgumentException("Jenis penanda bawaan tidak boleh kosong.", nameof(jenis));
            }

            var bersih = (kunci ?? Enumerable.Empty<string>())
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => k.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToList();

            lock (_kunci)
            {
                var peta = BacaPeta();
                if (bersih.Count == 0) peta.Remove(jenis);
                else peta[jenis] = bersih;

                string path = Path;
                string? folder = System.IO.Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(folder))
                {
                    throw new InvalidOperationException("Lokasi berkas penanda bawaan tidak valid.");
                }

                if (peta.Count == 0)
                {
                    if (File.Exists(path)) File.Delete(path);
                    return;
                }

                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                File.WriteAllText(path, JsonSerializer.Serialize(peta, OpsiJson));
            }
        }

        /// <summary>Lupakan seluruh penanda (dipakai pengujian dan pemulihan keadaan awal).</summary>
        public static void Lupakan()
        {
            lock (_kunci)
            {
                HapusBerkas(Path);
                HapusBerkas(PathLama);
            }
        }

        private static Dictionary<string, List<string>> BacaPeta()
        {
            try
            {
                string path = Path;
                if (File.Exists(path))
                {
                    var peta = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(
                        File.ReadAllText(path));
                    if (peta != null) return peta;
                }
            }
            catch
            {
                // Penanda rusak — dibangun ulang dari nol.
            }

            return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        }

        private static void HapusBerkas(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // Penanda tidak dapat dihapus (berkas terkunci) — diabaikan.
            }
        }
    }
}
