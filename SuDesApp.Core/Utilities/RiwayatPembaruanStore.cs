using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SuDesApp.Utilities
{
    /// <summary>Satu baris riwayat: sebuah pembaruan yang pernah dicoba dipasang.</summary>
    public class EntriRiwayatPembaruan
    {
        /// <summary>Versi yang dipasang/dicoba, mis. "2.5.1".</summary>
        public string Versi { get; set; } = string.Empty;

        /// <summary>Jenis pembaruan: "Tambalan" (kecil) atau "Installer" (besar).</summary>
        public string Jenis { get; set; } = "Tambalan";

        /// <summary>Jumlah berkas yang diganti/dihapus oleh pemasangan.</summary>
        public int JumlahBerkas { get; set; }

        /// <summary>Berhasil atau tidak.</summary>
        public bool Berhasil { get; set; }

        /// <summary>Keterangan singkat: pesan skrip penerap, atau alasan gagal.</summary>
        public string Pesan { get; set; } = string.Empty;

        /// <summary>Versi asal sebelum pembaruan (bila diketahui).</summary>
        public string DariVersi { get; set; } = string.Empty;

        /// <summary>Waktu pemasangan (waktu lokal).</summary>
        public DateTime Waktu { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Riwayat pembaruan aplikasi: daftar pembaruan (tambalan kecil maupun installer
    /// besar) yang pernah dicoba dipasang, terbaru dulu. Disimpan di profil pengguna
    /// (%LOCALAPPDATA%\SuDesApp\riwayat-pembaruan.json) — terpisah dari folder aplikasi
    /// sehingga tidak ikut ditimpa pemasangan pembaruan itu sendiri, dan tetap ada
    /// meski aplikasi diganti installer. Halaman Pembaruan membaca store ini untuk
    /// menampilkan versi yang pernah dipasang, kapan, berapa berkas, dan hasilnya.
    /// </summary>
    public static class RiwayatPembaruanStore
    {
        private const string NamaFolder = "SuDesApp";
        private const string NamaBerkas = "riwayat-pembaruan.json";

        /// <summary>Riwayat dibatasi jumlahnya agar berkas tetap ringan.</summary>
        public const int BatasEntri = 50;

        private static readonly object _kunci = new();
        private static string? _path;

        /// <summary>Lokasi bawaan berkas riwayat (profil pengguna Windows).</summary>
        public static string PathBawaan => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            NamaFolder,
            NamaBerkas);

        /// <summary>
        /// Lokasi berkas riwayat yang sedang dipakai. Dapat diarahkan ke berkas lain
        /// (dipakai pengujian agar tidak menyentuh riwayat pengguna asli).
        /// </summary>
        public static string Path
        {
            get => _path ?? PathBawaan;
            set => _path = string.IsNullOrWhiteSpace(value) ? null : value;
        }

        /// <summary>Baca seluruh riwayat (terbaru dulu). Berkas tidak ada atau rusak → daftar kosong.</summary>
        public static List<EntriRiwayatPembaruan> Muat()
        {
            try
            {
                string path = Path;
                if (!File.Exists(path)) return new List<EntriRiwayatPembaruan>();

                var daftar = JsonSerializer.Deserialize<List<EntriRiwayatPembaruan>>(File.ReadAllText(path));
                if (daftar == null) return new List<EntriRiwayatPembaruan>();

                // Terbaru dulu + jaga-jaga bila berkas lama tidak terurut.
                return daftar
                    .Where(e => e != null)
                    .OrderByDescending(e => e.Waktu)
                    .ToList();
            }
            catch (Exception)
            {
                // Riwayat yang rusak tidak boleh mengganggu aplikasi; cukup dianggap kosong.
                return new List<EntriRiwayatPembaruan>();
            }
        }

        /// <summary>Tambah satu entri ke riwayat, terbaru dulu, dibatasi <see cref="BatasEntri"/>.</summary>
        public static void Tambah(EntriRiwayatPembaruan entri)
        {
            if (entri == null) return;
            if (string.IsNullOrWhiteSpace(entri.Versi)) entri.Versi = "-";

            lock (_kunci)
            {
                var daftar = Muat();
                daftar.Insert(0, entri);

                var teks = JsonSerializer.Serialize(
                    daftar.Take(BatasEntri).ToList(),
                    new JsonSerializerOptions { WriteIndented = true });

                string path = Path;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                File.WriteAllText(path, teks);
            }
        }

        /// <summary>Kosongkan seluruh riwayat (mis. dipakai pengujian).</summary>
        public static void Kosongkan()
        {
            lock (_kunci)
            {
                try { if (File.Exists(Path)) File.Delete(Path); } catch { /* terbaik saja */ }
            }
        }
    }
}
