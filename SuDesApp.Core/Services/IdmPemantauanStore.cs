using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SuDesApp.Services
{
    /// <summary>
    /// Catatan pemeriksaan portal IDM terakhir: kapan portal benar-benar
    /// dijawab, dan tahun penilaian terbaru yang terbit menurut jawaban itu.
    ///
    /// Ditulis oleh dua jalur yang sama-sama menghubungi portal — halaman IDM
    /// saat memuat data, dan pemantau latar (PemantauIdmBaru) yang memeriksa
    /// tiap enam jam. Halaman memakainya untuk indikator "data terbaru yang
    /// terbit + tanggal pemeriksaan", termasuk saat yang tampil adalah data
    /// cadangan.
    /// </summary>
    public sealed class IdmPemantauan
    {
        /// <summary>Waktu pemeriksaan portal terakhir yang berhasil dijawab.</summary>
        [JsonPropertyName("waktuPeriksa")] public DateTime? WaktuPeriksa { get; set; }

        /// <summary>Tahun penilaian terbaru yang terbit menurut pemeriksaan itu.</summary>
        [JsonPropertyName("tahunTerbit")] public int? TahunTerbit { get; set; }
    }

    /// <summary>Keluar masuk catatan pemeriksaan IDM (dipisah supaya bisa diuji tanpa disk).</summary>
    public interface IIdmPemantauanStore
    {
        /// <summary>Baca catatan; null bila belum ada atau tidak terbaca (bukan kegagalan fatal).</summary>
        IdmPemantauan? Muat();

        /// <summary>Timpa catatan. Kegagalan I/O tidak dibalikkan.</summary>
        void Simpan(IdmPemantauan pemantauan);
    }

    /// <summary>
    /// Catatan pemeriksaan versi berkas JSON lokal
    /// (<c>%LocalAppData%\SuDesApp\idm-pemantauan.json</c>). Pola yang sama dengan
    /// <see cref="IdmSimpananStore"/>: berkas rusak/absen menghasilkan null dan
    /// halaman tetap jalan, hanya tanpa indikator pemeriksaan.
    /// </summary>
    public sealed class IdmPemantauanStore : IIdmPemantauanStore
    {
        /// <summary>Penunjuk folder untuk pengujian — pola IdmSimpananStore.</summary>
        internal static string? FolderOverride { get; set; }

        private static string BerkasPath => Path.Combine(
            FolderOverride ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SuDesApp"),
            "idm-pemantauan.json");

        public IdmPemantauan? Muat()
        {
            try
            {
                var jalur = BerkasPath;
                if (!File.Exists(jalur)) return null;

                return JsonSerializer.Deserialize<IdmPemantauan>(File.ReadAllText(jalur));
            }
            catch (Exception)
            {
                return null;
            }
        }

        public void Simpan(IdmPemantauan pemantauan)
        {
            try
            {
                var jalur = BerkasPath;
                Directory.CreateDirectory(Path.GetDirectoryName(jalur)!);
                File.WriteAllText(jalur, JsonSerializer.Serialize(pemantauan));
            }
            catch (Exception)
            {
                // Catatan gagal ditulis tidak menghalangi pengguna: halaman tetap
                // menampilkan data, hanya indikator pemeriksaannya tidak terbarui.
            }
        }
    }
}
