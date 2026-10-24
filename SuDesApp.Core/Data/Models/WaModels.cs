using System;
using System.Collections.Generic;

namespace SuDesApp.Data.Models
{
    /// <summary>
    /// Status sebuah permintaan surat online (via WhatsApp).
    /// </summary>
    public static class WaRequestStatus
    {
        public const string BARU = "BARU";
        public const string DIPROSES = "DIPROSES";
        public const string SELESAI = "SELESAI";
        public const string DITOLAK = "DITOLAK";
        public const string PERLU_PERBAIKAN = "PERLU_PERBAIKAN";
        /// <summary>Mode tautan: token sudah diberikan ke warga, menunggu form diisi.</summary>
        public const string MENUNGGU_FORM = "MENUNGGU_FORM";

        public static readonly string[] All = { BARU, DIPROSES, SELESAI, DITOLAK, PERLU_PERBAIKAN };

        /// <summary>Label sumber permintaan: percakapan format langsung via WhatsApp.</summary>
        public const string SumberWhatsApp = "WA";
        /// <summary>Label sumber permintaan: warga mengisi Google Form/Sheet.</summary>
        public const string SumberGoogleSheet = "SHEET";
        /// <summary>Label sumber permintaan: dikirim sistem luar lewat API desa.</summary>
        public const string SumberApi = "API";

        /// <summary>Apakah label sumber dikenal (untuk tampilan aman di panel operator).</summary>
        public static string TampilanSumber(string? sumber)
            => sumber switch
            {
                SumberGoogleSheet => "Google Sheet",
                SumberApi => "API",
                SumberWhatsApp => "WhatsApp",
                _ => "Lainnya"
            };

        public static bool IsValid(string? status)
            => !string.IsNullOrWhiteSpace(status) && Array.IndexOf(All, status) >= 0;
    }

    /// <summary>
    /// Antrian permintaan surat dari warga melalui WhatsApp.
    /// Setiap baris memicu notifikasi ke operator di aplikasi.
    /// </summary>
    public class PermintaanWa
    {
        public int ID_Permintaan { get; set; }
        public string KodePermintaan { get; set; } = string.Empty;
        public string NomorWA { get; set; } = string.Empty;
        public string? NamaWarga { get; set; }
        public string? NIK { get; set; }
        public string NamaJenis { get; set; } = string.Empty;
        public string? PesanMentah { get; set; }
        public string? DataJson { get; set; }
        public string Status { get; set; } = WaRequestStatus.BARU;
        public int? IdSurat { get; set; }
        public bool IsRead { get; set; }
        public DateTime TanggalPermintaan { get; set; } = DateTime.Now;
        public DateTime? TanggalDiproses { get; set; }
        public string? Catatan { get; set; }
        public string? PesanBalasan { get; set; }

        /// <summary>Asal permintaan: "WA" (percakapan), "SHEET" (Google Form/Sheet), atau "API" (sistem luar).</summary>
        public string Sumber { get; set; } = WaRequestStatus.SumberWhatsApp;
        /// <summary>Token prefill yang dikirim ke warga (mode Sheet); untuk menghubungkan balasan.</summary>
        public string? SheetToken { get; set; }
        /// <summary>Baris Sheet jawaban yang menghasilkan permintaan ini (mode Sheet).</summary>
        public int? SheetRowId { get; set; }

        /// <summary>
        /// Kode unik dari sistem pengirim lewat API desa. Pengiriman ulang dengan
        /// referensi sama tidak menghasilkan permintaan ganda (idempoten).
        /// </summary>
        public string? Referensi { get; set; }
    }

    /// <summary>
    /// Data terstruktur hasil parsing format pesan warga.
    /// Disimpan sebagai JSON di kolom PermintaanWa.DataJson.
    /// </summary>
    public class WaRequestData
    {
        public string NamaJenis { get; set; } = string.Empty;
        public string Keperluan { get; set; } = string.Empty;
        public string NomorWA { get; set; } = string.Empty;
        public WargaData? Warga { get; set; }
        public Dictionary<string, string> Fields { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Mode Sheet: ID spreadsheet sumber jawaban (untuk menulis status balik).</summary>
        public string? SheetId { get; set; }
        /// <summary>Mode Sheet: nomor baris jawaban di Sheet (untuk menulis status balik).</summary>
        public int? SheetRowNumber { get; set; }
    }

    /// <summary>
    /// Pesan masuk dari gateway WhatsApp.
    /// </summary>
    public class WaInboundMessage
    {
        public string MessageId { get; set; } = Guid.NewGuid().ToString("N");
        public string FromNumber { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public DateTime ReceivedAt { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Hasil pengiriman balasan WA (untuk log / simulasi).
    /// </summary>
    public class WaOutboundLogEntry
    {
        public DateTime SentAt { get; set; } = DateTime.Now;
        public string To { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string? FilePath { get; set; }
        public bool Success { get; set; } = true;
        public string Status { get; set; } = "OK";

        public override string ToString()
            => string.IsNullOrEmpty(FilePath)
                ? $"[{SentAt:HH:mm:ss}] → {To}: {Text}"
                : $"[{SentAt:HH:mm:ss}] → {To}: 📄 {FilePath}";
    }
}