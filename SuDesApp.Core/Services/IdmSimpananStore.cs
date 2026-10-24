using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SuDesApp.Services
{
    /// <summary>
    /// Cadangan data IDM terakhir yang BERHASIL dimuat dari situs pusat,
    /// beserta kode desa dan waktu pengambilannya. Disimpan di
    /// <c>%LocalAppData%\SuDesApp\idm-cadangan.json</c> supaya halaman IDM
    /// tetap menampilkan angka resmi terakhir (dan tanggalnya) ketika internet
    /// mati — termasuk setelah aplikasi dibuka ulang.
    ///
    /// Ini bukan sumber data: begitu situs pusat berhasil dihubungi, angka
    /// segar menimpa cadangan ini. Kode desa ikut disimpan supaya cadangan desa
    /// lain tidak pernah tampil (mis. pengguna ganti desa di Pengaturan Surat).
    /// </summary>
    public sealed class IdmSimpanan
    {
        [JsonPropertyName("kodeDesa")] public string KodeDesa { get; set; } = "";
        [JsonPropertyName("waktuMuat")] public DateTime? WaktuMuat { get; set; }
        [JsonPropertyName("lewatHttp")] public bool LewatHttp { get; set; }
        [JsonPropertyName("hasil")] public List<HasilIdm> Hasil { get; set; } = new();
    }

    /// <summary>Keluar masuk cadangan IDM (dipisah supaya halaman bisa diuji tanpa disk).</summary>
    public interface IIdmSimpananStore
    {
        /// <summary>Baca cadangan; null bila belum ada atau tidak terbaca (bukan kegagalan fatal).</summary>
        IdmSimpanan? Muat();

        /// <summary>Timpa cadangan dengan hasil muat terbaru. Kegagalan I/O tidak dibalikkan.</summary>
        void Simpan(IdmSimpanan simpanan);
    }

    /// <summary>
    /// Cadangan IDM versi berkas JSON lokal. Gagal baca (berkas rusak/absen)
    /// menghasilkan null — halaman jalan tanpa cadangan, persis seperti belum
    /// pernah memuat. Pola yang sama dengan KatalogPaketTersimpan/ApbdesStore.
    /// </summary>
    public sealed class IdmSimpananStore : IIdmSimpananStore
    {
        /// <summary>Penunjuk folder untuk pengujian — pola KatalogPaketTersimpan.</summary>
        internal static string? FolderOverride { get; set; }

        private static string BerkasPath => Path.Combine(
            FolderOverride ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SuDesApp"),
            "idm-cadangan.json");

        public IdmSimpanan? Muat()
        {
            try
            {
                var jalur = BerkasPath;
                if (!File.Exists(jalur)) return null;

                return JsonSerializer.Deserialize<IdmSimpanan>(File.ReadAllText(jalur));
            }
            catch (Exception)
            {
                return null;
            }
        }

        public void Simpan(IdmSimpanan simpanan)
        {
            try
            {
                var jalur = BerkasPath;
                Directory.CreateDirectory(Path.GetDirectoryName(jalur)!);
                File.WriteAllText(jalur, JsonSerializer.Serialize(simpanan));
            }
            catch (Exception)
            {
                // Cadangan gagal ditulis tidak menghalangi pengguna: layar
                // tetap menampilkan data dari sesi ini; cuma sesi berikutnya
                // tidak punya cadangan.
            }
        }
    }
}