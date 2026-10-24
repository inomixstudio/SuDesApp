using SuDesApp.Data.Models;
using System;
using System.Collections.Concurrent;

namespace SuDesApp.WhatsApp
{
    /// <summary>
    /// Menyimpan keadaan percakapan per nomor WhatsApp secara aman di
    /// memori (singleton). Dipakai untuk menunggu konfirmasi "YA/TIDAK"
    /// dari warga sebelum permintaan disimpan.
    /// </summary>
    public class WaSessionStore
    {
        private readonly ConcurrentDictionary<string, PendingWaRequest> _pending =
            new(StringComparer.Ordinal);

        public static readonly TimeSpan Expiry = TimeSpan.FromHours(6);

        public PendingWaRequest? Get(string nomorWA)
        {
            if (!_pending.TryGetValue(nomorWA, out var p)) return null;
            if (DateTime.Now - p.CreatedAt > Expiry)
            {
                _pending.TryRemove(nomorWA, out _);
                return null;
            }
            return p;
        }

        public void Set(string nomorWA, PendingWaRequest request)
            => _pending[nomorWA] = request;

        public void Remove(string nomorWA)
            => _pending.TryRemove(nomorWA, out _);
    }

    /// <summary>Permintaan yang sudah di-parse, menunggu konfirmasi warga.</summary>
    public class PendingWaRequest
    {
        public WaRequestData Data { get; set; } = new();
        public string LastRawText { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}