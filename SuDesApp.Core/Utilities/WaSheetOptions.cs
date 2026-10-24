using System;

namespace SuDesApp.Utilities
{
    /// <summary>
    /// Preferensi layanan WhatsApp mode Google Sheet/Form. Bila aktif, pesan
    /// apa pun dari warga dibalas tautan Google Form/Sheet untuk diisi sendiri;
    /// poller membaca baris MENUNGGU dari Sheet, menyimpannya ke database, dan
    /// setelah PDF siap warga menerima tautan unduh (link Drive, bukan lampiran).
    /// Semua pengaturan disimpan via <see cref="AppPreferenceStore"/>.
    /// </summary>
    public static class WaSheetOptions
    {
        public const string KeyWaSheetMode = "waSheetMode";
        public const string KeyWaSheetFormUrl = "waSheetFormUrl";
        public const string KeyWaSheetUrl = "waSheetUrl";
        public const string KeyWaSheetTabName = "waSheetTabName";
        public const string KeyWaFormId = "waFormId";
        public const string KeyWaFormAuto = "waFormAuto";

        /// <summary>Mode tautan form aktif (default: aktif — alur yang diinginkan pengguna).</summary>
        public static bool IsLinkModeEnabled() => GetBool(KeyWaSheetMode, true);
        public static void SetLinkModeEnabled(bool v) => SetBool(KeyWaSheetMode, v);

        /// <summary>
        /// URL Google Form (bentuk viewform / viewform?usp=send_form, bukan /formResponse)
        /// yang dibagikan ke warga. Kosong = dibuat dari Sheet (lihat BuildFormUrlFromSheetUrl).
        /// </summary>
        public static string? GetFormUrl() => GetString(KeyWaSheetFormUrl, string.Empty);
        public static void SetFormUrl(string? v) => SetString(KeyWaSheetFormUrl, v ?? string.Empty);

        /// <summary>
        /// URL Google Sheet tempat jawaban form terkumpul (URL edit berisi
        /// /d/SHEET_ID/edit…). Wajib diisi bila FormUrl kosong.
        /// </summary>
        public static string? GetSheetUrl() => GetString(KeyWaSheetUrl, string.Empty);
        public static void SetSheetUrl(string? v) => SetString(KeyWaSheetUrl, v ?? string.Empty);

        /// <summary>Nama tab (worksheet) jawaban; default "Form Responses 1".</summary>
        public static string GetTabName() => GetString(KeyWaSheetTabName, "Form Responses 1") ?? "Form Responses 1";
        public static void SetTabName(string? v) => SetString(KeyWaSheetTabName, v ?? string.Empty);

        /// <summary>
        /// ID Google Form (bukan URL) untuk formulir yang DIBUAT aplikasi. Dipakai
        /// membaca jawaban via Forms API dan menyinkronkannya ke Sheet mirror.
        /// </summary>
        public static string? GetFormId() => GetString(KeyWaFormId, string.Empty);
        public static void SetFormId(string? v) => SetString(KeyWaFormId, v ?? string.Empty);

        /// <summary>
        /// Formulir jawaban dipantau lewat Forms API (dibuat otomatis oleh aplikasi)
        /// dan disalin ke Sheet; bukan form yang mengisi Sheet-nya sendiri.
        /// </summary>
        public static bool IsAutoForm() => GetBool(KeyWaFormAuto, false);
        public static void SetAutoForm(bool v) => SetBool(KeyWaFormAuto, v);

        /// <summary>Konfigurasi lengkap? (Sheet wajib; Form opsional bila Sheet diisi).</summary>
        public static bool IsConfigured()
            => !string.IsNullOrWhiteSpace(GetSheetUrl())
               || !string.IsNullOrWhiteSpace(GetFormUrl());

        /// <summary>
        /// Mengambil Sheet ID dari URL (bagian antara /d/ dan /edit atau /view).
        /// Mendukung URL edit, HTML view, dan export.
        /// </summary>
        public static string? ExtractSheetId(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            var s = url.Trim();
            var marker = s.IndexOf("/d/", StringComparison.Ordinal);
            if (marker < 0) return null;
            var start = marker + 3;
            var end = start;
            while (end < s.Length && s[end] != '/' && s[end] != '?' && s[end] != '#') end++;
            return end > start ? s[start..end] : null;
        }

        /// <summary>
        /// Menormalkan URL form: hapus query tracking, pastikan berbentuk viewform
        /// (bukan formResponse — warga harus MENGISI form, bukan POST API).
        /// </summary>
        public static string? NormalizeFormUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            var s = url.Trim();

            // URL terbit hasil "Kirim" (published): .../forms/d/e/<ID>/viewform —
            // inilah tautan yang benar untuk warga. JANGAN dipecah pada "/d/",
            // karena segmen setelahnya adalah "e" sehingga terpotong menjadi
            // .../d/e/viewform dan menghasilkan HTTP 404.
            if (s.Contains("/d/e/", StringComparison.Ordinal))
            {
                var vf = s.IndexOf("/viewform", StringComparison.Ordinal);
                return vf >= 0 ? s[..(vf + "/viewform".Length)] : s;
            }

            // URL gaya editor: .../forms/d/<ID>/edit → .../forms/d/<ID>/viewform
            var marker = s.IndexOf("/d/", StringComparison.Ordinal);
            if (marker < 0) return s;
            var baseEnd = s.IndexOf('/', marker + 3);
            var baseUrl = baseEnd > marker ? s[..baseEnd] : s;
            return baseUrl + "/viewform";
        }

        /// <summary>
        /// Membuat URL form dari URL Sheet jawaban (…/edit#gid=0 → formView).
        /// Hanya berlaku bila Sheet tersebut memang sheet respons Google Form.
        /// </summary>
        public static string? BuildFormUrlFromSheetUrl(string? sheetUrl)
        {
            var id = ExtractSheetId(sheetUrl);
            return string.IsNullOrWhiteSpace(id) ? null : $"https://docs.google.com/forms/d/{id}/viewform";
        }

        // ── Wrapper tipis agar kelas ini mudah diuji dan konsisten gaya AppPreferenceStore ──
        private static bool GetBool(string key, bool def) => AppPreferenceStore.GetBool(key, def);
        private static void SetBool(string key, bool v) => AppPreferenceStore.SetBool(key, v);
        private static string? GetString(string key, string? def) => AppPreferenceStore.GetString(key, def);
        private static void SetString(string key, string? v) => AppPreferenceStore.SetString(key, v ?? string.Empty);
    }
}
