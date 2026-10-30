using System;
using System.Collections.Generic;

namespace SuDesApp.Data.Models
{
    /// <summary>
    /// Status sebuah permintaan surat online. Permintaan masuk lewat website
    /// (halaman Layanan Online) atau lewat API desa, lalu diproses operator.
    ///
    /// Nama lama (WhatsApp/Google Form) sengaja masih diterima saat membaca
    /// baris warisan supaya riwayat antrean lama tetap tampil wajar — kanal
    /// itu sendiri sudah tidak dipakai lagi.
    /// </summary>
    public static class StatusPermintaan
    {
        public const string BARU = "BARU";
        public const string DIPROSES = "DIPROSES";
        public const string SELESAI = "SELESAI";
        public const string DITOLAK = "DITOLAK";
        public const string PERLU_PERBAIKAN = "PERLU_PERBAIKAN";

        /// <summary>Status warisan kanal Google Form; hanya dibaca untuk data lama.</summary>
        public const string MENUNGGU_FORM = "MENUNGGU_FORM";

        public static readonly string[] All = { BARU, DIPROSES, SELESAI, DITOLAK, PERLU_PERBAIKAN };

        /// <summary>Label sumber warisan: percakapan WhatsApp (data lama).</summary>
        public const string SumberWhatsApp = "WA";

        /// <summary>Label sumber warisan: Google Form/Sheet (data lama).</summary>
        public const string SumberGoogleSheet = "SHEET";

        /// <summary>Label sumber: dikirim website/API desa.</summary>
        public const string SumberApi = "API";

        /// <summary>Label sumber yang aman ditampilkan di layar antrean.</summary>
        public static string TampilanSumber(string? sumber)
            => sumber switch
            {
                SumberApi => "Website",
                SumberGoogleSheet => "Google Form (lama)",
                SumberWhatsApp => "WhatsApp (lama)",
                _ => "Lainnya"
            };

        public static bool IsValid(string? status)
            => !string.IsNullOrWhiteSpace(status) && Array.IndexOf(All, status) >= 0;
    }

    /// <summary>
    /// Satu baris antrean permintaan surat. Sejak kanal WhatsApp dihapus,
    /// baris baru hanya berasal dari website dan API desa; kolom bertanda
    /// "warisan" dibiarkan ada supaya database lama tetap terbaca.
    /// </summary>
    public class PermintaanWa
    {
        public int ID_Permintaan { get; set; }
        public string KodePermintaan { get; set; } = string.Empty;

        /// <summary>Nomor HP/WhatsApp warga; "-" bila pemohon tidak mengisi.</summary>
        public string NomorWA { get; set; } = string.Empty;

        public string? NamaWarga { get; set; }
        public string? NIK { get; set; }
        public string NamaJenis { get; set; } = string.Empty;
        public string? PesanMentah { get; set; }
        public string? DataJson { get; set; }
        public string Status { get; set; } = StatusPermintaan.BARU;
        public int? IdSurat { get; set; }
        public bool IsRead { get; set; }
        public DateTime TanggalPermintaan { get; set; } = DateTime.Now;
        public DateTime? TanggalDiproses { get; set; }
        public string? Catatan { get; set; }
        public string? PesanBalasan { get; set; }

        /// <summary>Asal permintaan: "API" (website/sistem luar) atau label warisan "WA"/"SHEET".</summary>
        public string Sumber { get; set; } = StatusPermintaan.SumberApi;

        /// <summary>Kolom warisan mode Google Sheet — tidak lagi diisi.</summary>
        public string? SheetToken { get; set; }

        /// <summary>Kolom warisan mode Google Sheet — tidak lagi diisi.</summary>
        public int? SheetRowId { get; set; }

        /// <summary>
        /// Kode unik dari sistem pengirim lewat API desa. Pengiriman ulang dengan
        /// referensi sama tidak menghasilkan permintaan ganda (idempoten).
        /// </summary>
        public string? Referensi { get; set; }
    }

    /// <summary>
    /// Data terstruktur permintaan (hasil parsing pesan/isi formulir website).
    /// Disimpan sebagai JSON di kolom PermintaanWa.DataJson dan dibaca ulang
    /// saat operator membuat surat dari antrean.
    /// </summary>
    public class PermintaanData
    {
        public string NamaJenis { get; set; } = string.Empty;
        public string Keperluan { get; set; } = string.Empty;
        public string NomorWA { get; set; } = string.Empty;
        public WargaData? Warga { get; set; }
        public Dictionary<string, string> Fields { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Kolom warisan mode Google Sheet — tidak lagi diisi.</summary>
        public string? SheetId { get; set; }

        /// <summary>Kolom warisan mode Google Sheet — tidak lagi diisi.</summary>
        public int? SheetRowNumber { get; set; }
    }
}
