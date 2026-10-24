using System;
using System.Collections.Generic;

namespace SuDesApp.Data.Models
{
    /// <summary>
    /// Status buku tahun. Disimpan sebagai teks dengan batasan CHECK di
    /// database; konstanta ini adalah satu-satunya sumber nilai yang sah.
    /// </summary>
    public static class StatusTutupBuku
    {
        public const string Terbuka = "TERBUKA";
        public const string Tertutup = "TERTUTUP";

        public static readonly IReadOnlyList<string> Semua = new[] { Terbuka, Tertutup };

        public static bool IsValid(string? status) =>
            status == Terbuka || status == Tertutup;
    }

    /// <summary>
    /// Status satu tahun buku: sudah ditutup atau masih terbuka. Satu baris per
    /// tahun. Tidak ada soft delete — membuka tahun hanya mengubah Status.
    /// </summary>
    public class TutupBukuTahun
    {
        public int ID { get; set; }
        public int Tahun { get; set; }
        public string Status { get; set; } = StatusTutupBuku.Terbuka;

        public string? TanggalTutup { get; set; }
        public int JumlahSurat { get; set; }
        public int JumlahDeret { get; set; }

        /// <summary>Ringkasan deret saat tutup, disimpan sebagai JSON.</summary>
        public string? Snapshot { get; set; }

        public string? Catatan { get; set; }
        public string? DitutupOleh { get; set; }
        public string? DitutupPada { get; set; }
        public string? DibukaOleh { get; set; }
        public string? DibukaPada { get; set; }
        public string? CreatedAt { get; set; }
        public string? UpdatedAt { get; set; }

        public bool Tertutup => Status == StatusTutupBuku.Tertutup;
    }

    /// <summary>
    /// Potret register saat buku ditutup. Ini yang membuat arsip tetap
    /// bermakna kalau nanti deret berubah: register tahun tertutup dibaca dari
    /// snapshot, bukan dari register yang sekarang.
    /// </summary>
    public class SnapshotTutupBuku
    {
        public int Tahun { get; set; }
        public string DitutupPada { get; set; } = string.Empty;
        public string DitutupOleh { get; set; } = string.Empty;
        public int JumlahSurat { get; set; }
        public bool Dipaksa { get; set; }
        public string? Catatan { get; set; }
        public List<SnapshotDeret> Deret { get; set; } = new();
    }

    /// <summary>Ringkasan satu deret nomor dalam potret tutup buku.</summary>
    public class SnapshotDeret
    {
        public string Awalan { get; set; } = string.Empty;
        public int Tahun { get; set; }
        public int Jumlah { get; set; }
        public int? NomorPertama { get; set; }
        public int? NomorTerakhir { get; set; }
        public bool MulaiDariSatu { get; set; }
        public List<int> NomorHilang { get; set; } = new();
        public List<int> NomorGanda { get; set; } = new();
        public List<string> NomorTidakTerbaca { get; set; } = new();
        public List<string> NamaJenis { get; set; } = new();
    }
}
