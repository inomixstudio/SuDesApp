using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SuDesApp.Data.Models;

namespace SuDesApp.WhatsApp
{
    /// <summary>
    /// Kontrak koneksi WhatsApp (text-only). Aplikasi HANYA mengirim pesan teks
    /// berisi tautan (link Google Formulir untuk pengisian, link Drive untuk
    /// unduh PDF) — tidak pernah mengirim berkas.
    /// </summary>
    public interface IWhatsAppGateway
    {
        /// <summary>Nama provider / mode saat ini.</summary>
        string Name { get; }

        /// <summary>Menandai apakah koneksi sudah siap dipakai (token, nomor, dsb.).</summary>
        bool IsConfigured { get; }

        /// <summary>
        /// Mengambil pesan masuk baru dari WhatsApp. Implementasi harus
        /// mengembalikan pesan yang belum pernah diambil (dedupe berdasarkan
        /// MessageId). Saat tidak ada pesan baru, kembalikan daftar kosong.
        /// </summary>
        Task<IReadOnlyList<WaInboundMessage>> FetchInboundAsync(CancellationToken cancellationToken = default);

        /// <summary>Mengirim pesan teks / tautan ke satu nomor.</summary>
        Task<WaSendResult> SendTextAsync(string toNumber, string text, CancellationToken cancellationToken = default);
    }

    /// <summary>Hasil pengiriman pesan ke WhatsApp.</summary>
    public class WaSendResult
    {
        public bool Success { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? Detail { get; set; }
    }
}
