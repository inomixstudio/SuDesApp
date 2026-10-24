using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SuDesApp.Utilities
{
    /// <summary>Satu berkas cadangan yang ditemukan di folder database.</summary>
    public sealed record CadanganDatabaseInfo(
        string Path,
        string Nama,
        long UkuranByte,
        DateTime Diubah,
        bool SebelumImpor);

    /// <summary>
    /// Penemuan berkas cadangan di folder database.
    ///
    /// Kenapa dipisah dari ViewModel: halaman Cadangkan &amp; Pulihkan perlu
    /// menampilkan cadangan terbaru (termasuk yang dibuat otomatis sebelum
    /// impor) tanpa memaksa pengguna menelusuri folder lewat dialog berkas.
    /// Logika "berkas mana yang cadangan" mudah salah — database aktif,
    /// berkas -wal/-shm, dan hasil ekspor bercampur di folder yang sama —
    /// jadi aturannya diuji langsung di sini, bukan lewat UI.
    /// </summary>
    public static class CadanganDatabase
    {
        /// <summary>Jumlah cadangan terbaru yang ditampilkan di halaman.</summary>
        public const int MaksimumTampil = 6;

        /// <summary>
        /// Daftar berkas .db di <paramref name="folder"/>, terbaru lebih dulu.
        /// Database yang sedang dipakai selalu dikecualikan supaya tombol
        /// "Pulihkan" tidak pernah menunjuk ke berkas yang sedang terbuka.
        /// </summary>
        public static IReadOnlyList<CadanganDatabaseInfo> Pindai(
            string folder,
            string? databaseAktif,
            int maksimum = MaksimumTampil)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder) || maksimum <= 0)
            {
                return Array.Empty<CadanganDatabaseInfo>();
            }

            string aktif;
            try
            {
                aktif = string.IsNullOrWhiteSpace(databaseAktif)
                    ? string.Empty
                    : Path.GetFullPath(databaseAktif);
            }
            catch
            {
                aktif = string.Empty;
            }

            var hasil = new List<CadanganDatabaseInfo>();
            IEnumerable<string> berkas;
            try
            {
                berkas = Directory.EnumerateFiles(folder, "*.db", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                return Array.Empty<CadanganDatabaseInfo>();
            }

            foreach (var jalur in berkas)
            {
                try
                {
                    var penuh = Path.GetFullPath(jalur);
                    if (string.Equals(penuh, aktif, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var info = new FileInfo(penuh);
                    if (!info.Exists)
                    {
                        continue;
                    }

                    var nama = info.Name;
                    hasil.Add(new CadanganDatabaseInfo(
                        penuh,
                        nama,
                        info.Length,
                        info.LastWriteTime,
                        nama.StartsWith("desa_backup_before_import", StringComparison.OrdinalIgnoreCase)));
                }
                catch (IOException)
                {
                    // Berkas hilang/terkunci saat dipindai: lewati, jangan gagalkan halaman.
                }
                catch (UnauthorizedAccessException)
                {
                    // Sama: satu berkas tak terbaca tidak boleh mengosongkan daftar.
                }
            }

            return hasil
                .OrderByDescending(c => c.Diubah)
                .ThenBy(c => c.Nama, StringComparer.OrdinalIgnoreCase)
                .Take(maksimum)
                .ToList();
        }

        /// <summary>
        /// Nama berkas cadangan bawaan yang mudah dibaca manusia:
        /// <c>desa_cadangan_2026-10-21_1430.db</c>. Tanggal di depan supaya
        /// berkas urut secara alfabetis sama seperti urut waktu.
        /// </summary>
        public static string NamaEksporBawaan(DateTime waktu)
            => "desa_cadangan_" + waktu.ToString("yyyy-MM-dd_HHmm") + ".db";
    }
}
