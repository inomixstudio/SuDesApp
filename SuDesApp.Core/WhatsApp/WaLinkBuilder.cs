using SuDesApp.Utilities;
using System;
using System.Text;

namespace SuDesApp.WhatsApp
{
    /// <summary>
    /// Pembuat tautan layanan online berbasis Google Form/Sheet untuk warga:
    /// menautkan prefill token ke URL form (mode Sheet) atau membuat tautan
    /// konvensional (mode percakapan). Kelas ini tidak melakukan jaringan.
    /// </summary>
    public static class WaLinkBuilder
    {
        /// <summary>Generator token prefill pendek (12 karakter URL-safe).</summary>
        public static string NewToken()
        {
            const string alphabet = "abcdefghjkmnpqrstuvwxyz23456789";
            var sb = new StringBuilder(12);
            for (int i = 0; i < 12; i++)
                sb.Append(alphabet[Random.Shared.Next(alphabet.Length)]);
            return sb.ToString();
        }

        /// <summary>
        /// Tautan Google Formulir yang dikirim ke warga. Prioritas: URL Formulir
        /// yang diisi operator. Bila kosong, DITEBAK dari URL Sheet (hanya sah
        /// bila Sheet memang sheet respons Google Form) — itulah sebabnya kolom
        /// URL Formulir sebaiknya selalu diisi.
        /// </summary>
        public static string? BuildFormLink()
        {
            var form = WaSheetOptions.GetFormUrl();
            if (!string.IsNullOrWhiteSpace(form))
                return WaSheetOptions.NormalizeFormUrl(form);

            return WaSheetOptions.BuildFormUrlFromSheetUrl(WaSheetOptions.GetSheetUrl());
        }

        /// <summary>
        /// Tautan berprefill token untuk baris jawaban baru. Bila link mode nonaktif
        /// atau token kosong, kembalikan tautan baku (tanpa prefill).
        /// </summary>
        public static string? BuildPrefilledLink(string token)
        {
            var baseLink = BuildFormLink();
            if (string.IsNullOrWhiteSpace(baseLink) || string.IsNullOrWhiteSpace(token))
                return baseLink;

            var sep = baseLink.Contains('?', StringComparison.Ordinal) ? '&' : '?';
            return $"{baseLink}{sep}prefill_Token={Uri.EscapeDataString(token)}";
        }

        /// <summary>
        /// Tautan Google Sheet jawaban (mode hanya-sheet): warga mengisi baris baru
        /// di bagian bawah tabel yang dibagikan. Token warga diberi tahu juga, untuk
        /// dicatat pada kolom Token (opsional).
        /// </summary>
        public static string? BuildSheetLink()
        {
            var url = WaSheetOptions.GetSheetUrl();
            if (string.IsNullOrWhiteSpace(url)) return null;
            var id = WaSheetOptions.ExtractSheetId(url);
            return string.IsNullOrWhiteSpace(id) ? url : $"https://docs.google.com/spreadsheets/d/{id}/edit";
        }

        /// <summary>Header pesan balasan pertama (yang memuat tautan form).</summary>
        public static string FirstReplyHeader(string linkForm)
            => $"🏛️ LAYANAN SURAT ONLINE DESA\n\n" +
               $"Silakan isi form permohonan surat melalui tautan berikut:\n" +
               $"{linkForm}\n\n";

        /// <summary>Header pesan balasan yang mengingatkan tautan + token milik warga.</summary>
        public static string TokenReminder(string token)
            => "Catat juga Token pengisian Anda:\n" +
               $"Token: {token}\n\n" +
               "(Token menghubungkan form yang Anda isi dengan permohonan ini, " +
               "agar surat terkirim ke WhatsApp Anda.)";
    }
}
