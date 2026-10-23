using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace SuDesApp.Configuration
{
    /// <summary>
    /// Penyesuaian penomoran surat. Setelah kode kabupaten/kota tersimpan, profil
    /// penyesuaian dipisahkan menurut kode wilayah Kemendagri.
    ///
    /// Disimpan TERPISAH dari berkas bawaan <c>Configuration/JenisSuratConfig.json</c>
    /// karena berkas bawaan itu ikut ditimpa saat aplikasi diperbarui — penyesuaian
    /// pengguna harus tetap bertahan. Berkas bawaan tetap menjadi daftar jenis surat
    /// dan nilai default; berkas ini hanya "menempel" di atasnya.
    ///
    /// Isi berkas: peta NamaJenis → NomorFormat lengkap, mis.
    /// <c>{ "SKD_UMUM": "470/{0:D3}/Ds/{2:yyyy}" }</c>. Jenis yang tidak tercantum
    /// memakai format bawaan, sehingga "kembalikan bawaan" cukup menghapus entrinya.
    /// </summary>
    public static class PenomoranOverrideStore
    {
        private const string NamaFolder = "SuDesApp";
        private const string NamaBerkas = "penomoran-surat.json";

        private static readonly object _kunci = new();
        private static string? _path;
        private static bool _jalurManual;
        private static string? _kodeDaerah;
        private static string? _pathDatabase;

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
            set
            {
                _jalurManual = !string.IsNullOrWhiteSpace(value);
                _path = _jalurManual ? value : null;
            }
        }

        /// <summary>
        /// Isolasi penomoran menurut berkas database desa yang sedang dipakai.
        /// Konfigurasi lama profil pengguna dipindahkan satu kali ke profil database
        /// pertama agar nilai yang sudah disetel tidak hilang.
        /// </summary>
        public static void GunakanDatabase(string? databasePath)
        {
            if (_jalurManual || string.IsNullOrWhiteSpace(databasePath) || databasePath == ":memory:") return;
            try
            {
                string identitas = System.IO.Path.GetFullPath(databasePath.Trim());
                string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identitas.ToUpperInvariant())))
                    .ToLowerInvariant();
                string folder = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), NamaFolder, "Penomoran");
                Directory.CreateDirectory(folder);
                string tujuan = System.IO.Path.Combine(folder, hash + ".json");
                if (!File.Exists(tujuan) && File.Exists(PathBawaan))
                    File.Copy(PathBawaan, tujuan, overwrite: false);
                _path = tujuan;
                _pathDatabase = tujuan;
                _kodeDaerah = null;
            }
            catch
            {
                // Jika profil per database belum bisa ditentukan, pertahankan
                // penyimpanan lama agar aplikasi tetap dapat membuka pengaturan.
                _path = PathBawaan;
            }
        }

        /// <summary>
        /// Pilih profil peraturan dan penomoran menurut kode Kemendagri
        /// kabupaten/kota yang tersimpan pada data desa. Sebelum kode daerah
        /// terisi, pakai profil database agar konfigurasi lama tetap tersedia.
        /// </summary>
        public static void GunakanDaerah(string? kodeKabupaten)
        {
            if (_jalurManual) return;
            if (string.IsNullOrWhiteSpace(kodeKabupaten))
            {
                if (!string.IsNullOrWhiteSpace(_pathDatabase)) _path = _pathDatabase;
                _kodeDaerah = null;
                return;
            }
            var kode = kodeKabupaten.Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(kode, @"^\d{2}\.\d{2}$")) return;
            try
            {
                string folder = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), NamaFolder, "Penomoran");
                Directory.CreateDirectory(folder);
                string tujuan = System.IO.Path.Combine(folder, "wilayah-" + kode.Replace('.', '-') + ".json");
                if (!File.Exists(tujuan))
                {
                    var sumber = _pathDatabase ?? PathBawaan;
                    if (File.Exists(sumber)) File.Copy(sumber, tujuan, overwrite: false);
                }
                _path = tujuan;
                _kodeDaerah = kode;
            }
            catch
            {
                // Pertahankan profil database aktif seandainya profil wilayah
                // belum dapat dibuat/ditulis.
            }
        }

        public static string? KodeDaerah => _kodeDaerah;

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
