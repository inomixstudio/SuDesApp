using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SuDesApp.Utilities
{
    /// <summary>
    /// Penyimpanan preferensi aplikasi (di luar database, karena sebagian
    /// dibaca sebelum login). Berkas: %LOCALAPPDATA%/SuDesApp/login_prefs.json
    /// dengan format baris "kunci=nilai" sederhana — tahan korup; nilai tidak
    /// ada atau berkas rusak selalu jatuh ke default yang diberikan pemanggil.
    /// </summary>
    public static class AppPreferenceStore
    {
        private static readonly object FileLock = new();

        /// <summary>
        /// Penunjuk lokasi berkas preferensi untuk pengujian (dan skenario
        /// khusus). Null = lokasi profil user yang normal. Pola sama dengan
        /// ApiKunci.LokasiOverride / KunciDatabase.LokasiOverride.
        /// </summary>
        internal static Func<string>? LokasiOverride { get; set; }

        private static string PrefPath =>
            LokasiOverride?.Invoke()
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SuDesApp", "login_prefs.json");

        /// <summary>Ambil nilai boolean; default bila kunci tidak ada / berkas bermasalah.</summary>
        public static bool GetBool(string key, bool defaultValue)
        {
            var raw = GetString(key, null);
            if (raw == null) return defaultValue;
            return raw.Equals("1", StringComparison.Ordinal) || raw.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Simpan nilai boolean (1/0).</summary>
        public static void SetBool(string key, bool value) => SetString(key, value ? "1" : "0");

        /// <summary>Kode aktivasi langganan yang diketik pengguna (Pengaturan → Langganan).</summary>
        public static string GetKodeAktivasi() => GetString("kode_aktivasi", string.Empty) ?? string.Empty;

        /// <summary>Simpan kode aktivasi langganan.</summary>
        public static void SetKodeAktivasi(string kode) => SetString("kode_aktivasi", kode ?? string.Empty);

        /// <summary>
        /// URL server lisensi yang diatur pengguna dari halaman Langganan.
        /// Kosong = belum pernah diatur → memakai <c>Lisensi:UrlServer</c>
        /// dari appsettings. Ditaruh di preferensi karena appsettings ikut
        /// terbundel di instalasi dan tidak bisa diandalkan berisi alamat
        /// server yang hidup.
        /// </summary>
        public static string GetUrlServerLisensi() => GetString("lisensi_url_server", string.Empty) ?? string.Empty;

        /// <summary>Simpan URL server lisensi (kosongkan untuk kembali ke bawaan appsettings).</summary>
        public static void SetUrlServerLisensi(string url) => SetString("lisensi_url_server", url ?? string.Empty);

        /// <summary>Ambil nilai integer; default bila tidak ada / tidak sah.</summary>
        public static int GetInt(string key, int defaultValue)
        {
            var raw = GetString(key, null);
            return int.TryParse(raw, out var v) ? v : defaultValue;
        }

        /// <summary>Simpan nilai integer.</summary>
        public static void SetInt(string key, int value) => SetString(key, value.ToString());

        /// <summary>Ambil nilai string; null bila kunci tidak ada / berkas bermasalah.</summary>
        public static string? GetString(string key, string? defaultValue)
        {
            try
            {
                lock (FileLock)
                {
                    var path = PrefPath;
                    if (!File.Exists(path)) return defaultValue;
                    foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
                    {
                        var idx = line.IndexOf('=');
                        if (idx <= 0) continue;
                        if (!line.Substring(0, idx).Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                            continue;
                        return line.Substring(idx + 1).Trim();
                    }
                    return defaultValue;
                }
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>Simpan nilai string. Gagal menulis diabaikan (preferensi non-kritis).</summary>
        public static void SetString(string key, string value)
        {
            try
            {
                lock (FileLock)
                {
                    var path = PrefPath;
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                    var lines = File.Exists(path)
                        ? new List<string>(File.ReadAllLines(path, Encoding.UTF8))
                        : new List<string>();

                    var found = false;
                    for (int i = 0; i < lines.Count; i++)
                    {
                        var idx = lines[i].IndexOf('=');
                        if (idx <= 0) continue;
                        if (lines[i].Substring(0, idx).Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            lines[i] = $"{key}={value}";
                            found = true;
                            break;
                        }
                    }
                    if (!found) lines.Add($"{key}={value}");

                    File.WriteAllLines(path, lines, Encoding.UTF8);
                }
            }
            catch
            {
                // Preferensi non-kritis — kegagalan disimpan tidak boleh menggagalkan pemanggil.
            }
        }

        // =====================================================================
        // Kunci baku aplikasi
        // =====================================================================

        public const string KeyBackupDriveOnExit = "backupDriveOnExit";
        public const string KeyAutoBackupFormulirDrive = "autoBackupFormulirDrive";
        public const string KeyActivityLogging = "activityLogging";
        public const string KeyCleanupTempPdf = "cleanupTempPdf";
        public const string KeyCleanupOldExports = "cleanupOldExports";
        public const string KeyPeriksaPembaruan = "periksaPembaruan";
        public const string KeyPasangOtomatisSaatKeluar = "pasangOtomatisSaatKeluar";
        public const string KeyBatasUkuranTambalanMb = "batasUkuranTambalanMb";
        public const string KeyStartupDiamDiam = "startupDiamDiam";
        public const string KeyStartupDiamDiamMenit = "startupDiamDiamMenit";
        public const string KeyPanduanAwalSelesai = "panduanAwalSelesai";

        /// <summary>
        /// Pemberitahuan sambutan di lonceng notifikasi sudah pernah ditampilkan.
        /// Dibaca lewat <c>PemberitahuanSambutanStore</c>; setelah ditampilkan sekali,
        /// notifikasi itu tidak muncul lagi setiap aplikasi dibuka.
        /// </summary>
        public const string KeyPemberitahuanSambutan = "pemberitahuanSambutan";
        public const string KeyPenomoranSuratDiperiksa = "penomoranSuratDiperiksa";

        /// <summary>
        /// Tampilan sidebar navigasi terakhir ("terbuka", "ikon"). Dibaca
        /// lewat <c>SidebarModePrefs</c> supaya nilainya tetap stabil meskipun urutan
        /// enum di aplikasi berubah.
        /// </summary>
        public const string KeyModeSidebar = "modeSidebar";

        /// <summary>
        /// Kecepatan animasi antarmuka ("lambat", "normal", "cepat"). Dibaca lewat
        /// <c>KecepatanAnimasiPrefs</c> supaya nilai yang tidak dikenal tetap jatuh ke
        /// kecepatan bawaan.
        /// </summary>
        public const string KeyKecepatanAnimasi = "kecepatanAnimasi";

        /// <summary>
        /// Seberapa sering pemantau latar memeriksa data IDM baru
        /// ("6jam", "24jam", "dibuka"). Dibaca lewat
        /// <c>IntervalPemantauanIdmPrefs</c> supaya nilai tak dikenal tetap
        /// jatuh ke bawaan enam jam. Tidak ada = bawaan (perilaku lama).
        /// </summary>
        public const string KeyIntervalPemantauanIdm = "intervalPemantauanIdm";

        /// <summary>
        /// Lokasi berkas database desa yang dipilih pengguna (jalur absolut), mis. folder
        /// data desa di drive D. Kosong/tidak ada = pakai lokasi bawaan dari appsettings
        /// (desa.db di folder aplikasi). Dibaca <c>AppConfig</c> saat aplikasi dibuka,
        /// jadi penggantiannya baru berlaku setelah aplikasi ditutup lalu dibuka lagi.
        /// </summary>
        public const string KeyJalurDatabase = "jalurDatabase";

        /// <summary>
        /// Lokasi database pilihan pengguna, atau null bila masih memakai lokasi bawaan.
        /// </summary>
        public static string? GetJalurDatabase()
        {
            var nilai = GetString(KeyJalurDatabase, null);
            return string.IsNullOrWhiteSpace(nilai) ? null : nilai.Trim();
        }

        /// <summary>
        /// Simpan lokasi database pilihan pengguna; null/kosong mengembalikannya ke
        /// lokasi bawaan dari appsettings.
        /// </summary>
        public static void SetJalurDatabase(string? jalur) =>
            SetString(KeyJalurDatabase, string.IsNullOrWhiteSpace(jalur) ? string.Empty : jalur.Trim());

        /// <summary>
        /// Panduan awal (langkah mengisi data desa, pejabat, dan nomor surat) sudah
        /// ditutup/diselesaikan pengguna, jadi tidak dibuka otomatis lagi (default:
        /// belum). Panduan tetap bisa dibuka kapan saja dari menu Panduan Awal.
        /// </summary>
        public static bool IsPanduanAwalSelesai() => GetBool(KeyPanduanAwalSelesai, false);
        public static void SetPanduanAwalSelesai(bool v) => SetBool(KeyPanduanAwalSelesai, v);

        /// <summary>
        /// Pemberitahuan sambutan ("Notifikasi aktif — selamat datang …") sudah pernah
        /// muncul di lonceng notifikasi. Default: belum pernah.
        /// </summary>
        public static bool IsPemberitahuanSambutanPernahTampil() => GetBool(KeyPemberitahuanSambutan, false);
        public static void SetPemberitahuanSambutanPernahTampil(bool v) => SetBool(KeyPemberitahuanSambutan, v);

        /// <summary>
        /// Pengguna sudah menyatakan penomoran surat sesuai dengan kantor desanya
        /// (bawaan aplikasi boleh dipakai) — dipakai panduan awal untuk menandai
        /// langkah nomor surat selesai tanpa memaksa mengubah apa pun.
        /// </summary>
        public static bool IsPenomoranSuratDiperiksa() => GetBool(KeyPenomoranSuratDiperiksa, false);
        public static void SetPenomoranSuratDiperiksa(bool v) => SetBool(KeyPenomoranSuratDiperiksa, v);

        /// <summary>Backup otomatis ke Google Drive saat aplikasi ditutup (default: aktif).</summary>
        public static bool IsBackupDriveOnExitEnabled() => GetBool(KeyBackupDriveOnExit, true);
        public static void SetBackupDriveOnExitEnabled(bool v) => SetBool(KeyBackupDriveOnExit, v);

        /// <summary>
        /// Backup otomatis template formulir ke Google Drive setiap daftar
        /// template berubah / ada unduhan baru (default: aktif).
        /// </summary>
        public static bool IsAutoBackupFormulirDriveEnabled() => GetBool(KeyAutoBackupFormulirDrive, true);
        public static void SetAutoBackupFormulirDriveEnabled(bool v) => SetBool(KeyAutoBackupFormulirDrive, v);

        /// <summary>Pencatatan riwayat aktivitas (default: aktif).</summary>
        public static bool IsActivityLoggingEnabled() => GetBool(KeyActivityLogging, true);
        public static void SetActivityLoggingEnabled(bool v) => SetBool(KeyActivityLogging, v);

        /// <summary>Bersihkan PDF sementara lama saat aplikasi dimulai (default: aktif).</summary>
        public static bool IsCleanupTempPdfEnabled() => GetBool(KeyCleanupTempPdf, true);
        public static void SetCleanupTempPdfEnabled(bool v) => SetBool(KeyCleanupTempPdf, v);

        /// <summary>Hapus berkas ekspor lama (&gt;30 hari) saat aplikasi dimulai (default: aktif).</summary>
        public static bool IsCleanupOldExportsEnabled() => GetBool(KeyCleanupOldExports, true);
        public static void SetCleanupOldExportsEnabled(bool v) => SetBool(KeyCleanupOldExports, v);

        /// <summary>
        /// Periksa pembaruan aplikasi otomatis saat aplikasi dibuka (default: aktif).
        /// Hanya membaca informasi rilis terbaru di latar belakang lalu memberi tahu
        /// lewat lonceng notifikasi; pemasangan tetap menunggu persetujuan pengguna.
        /// </summary>
        public static bool IsPeriksaPembaruanSaatMulai() => GetBool(KeyPeriksaPembaruan, true);
        public static void SetPeriksaPembaruanSaatMulai(bool v) => SetBool(KeyPeriksaPembaruan, v);

        /// <summary>
        /// Pasang pembaruan kecil (tambalan) secara OTOMATIS saat aplikasi ditutup,
        /// tanpa menanya pengguna (default: aktif — batal centang bila tidak diinginkan).
        /// Hanya berlaku untuk tambalan; pembaruan besar selalu memerlukan persetujuan
        /// lewat halaman Pembaruan.
        /// </summary>
        public static bool IsPasangOtomatisSaatKeluar() => GetBool(KeyPasangOtomatisSaatKeluar, true);
        public static void SetPasangOtomatisSaatKeluar(bool v) => SetBool(KeyPasangOtomatisSaatKeluar, v);

        /// <summary>
        /// Batas ukuran paket pembaruan kecil yang masih boleh dipasang otomatis
        /// saat ditutup (dalam MB, default 25). Rilis dengan paket lebih besar
        /// tetap ditawarkan lewat notifikasi, tidak pernah diunduh senyap-senyap.
        /// </summary>
        public static int GetBatasUkuranTambalanMb()
        {
            int v = GetInt(KeyBatasUkuranTambalanMb, 25);
            return v is < 1 or > 500 ? 25 : v;
        }

        public static void SetBatasUkuranTambalanMb(int v) => SetInt(KeyBatasUkuranTambalanMb, v);

        /// <summary>
        /// Mode diam-diam startup (default: AKTIF): pekerjaan berat yang tidak penting
        /// bagi pengguna — pemeriksaan pembaruan online dan pembersihan otomatis
        /// (PDF sementara, hasil ekspor lama) — ditunda beberapa menit setelah
        /// aplikasi dibuka sehingga aplikasi terasa lebih cepat saat dibuka.
        /// </summary>
        public static bool IsStartupDiamDiam() => GetBool(KeyStartupDiamDiam, true);
        public static void SetStartupDiamDiam(bool v) => SetBool(KeyStartupDiamDiam, v);

        /// <summary>
        /// Jeda mode diam-diam dalam menit (1-60, bawaan 5). Rotasi log TIDAK ikut
        /// ditunda (kecil & perlu sejak dini); sisanya menunggu jeda ini berlalu.
        /// </summary>
        public static int GetStartupDiamDiamMenit()
        {
            int v = GetInt(KeyStartupDiamDiamMenit, 5);
            return v is < 1 or > 60 ? 5 : v;
        }

        public static void SetStartupDiamDiamMenit(int v) => SetInt(KeyStartupDiamDiamMenit, v);

        // =====================================================================
        // Kunci otomatis saat idle (layar kunci)
        // =====================================================================

        public const string KeyKunciIdleAktif = "kunciIdleAktif";
        public const string KeyKunciIdleMenit = "kunciIdleMenit";

        /// <summary>
        /// Kunci aplikasi otomatis setelah beberapa lama tidak dipakai
        /// (default: AKTIF begitu fitur ini dirilis — batal lewat Pengaturan
        /// bila tidak diinginkan).
        /// </summary>
        public static bool IsKunciIdleAktif() => GetBool(KeyKunciIdleAktif, true);

        public static void SetKunciIdleAktif(bool v) => SetBool(KeyKunciIdleAktif, v);

        /// <summary>
        /// Batas waktu diam sebelum aplikasi mengunci dirinya, dalam menit
        /// (1-120, bawaan 15). Nilai di luar rentang jatuh ke bawaan — 0 menit
        /// akan mengunci seketika dan tidak boleh sah.
        /// </summary>
        public static int GetKunciIdleMenit()
        {
            int v = GetInt(KeyKunciIdleMenit, 15);
            return v is < 1 or > 120 ? 15 : v;
        }

        public static void SetKunciIdleMenit(int v) => SetInt(KeyKunciIdleMenit, v);

        // =====================================================================
        // Mode edit awalan nomor surat di halaman Pengaturan
        // =====================================================================

        public const string KeyModeEditAwalan = "modeEditAwalan";

        /// <summary>
        /// Halaman Pengaturan Aplikasi dibuka kembali dalam mode edit (kotak
        /// isian awalan nomor surat tampil) atau mode tampilan biasa. Bawaan:
        /// MATI — surat lebih aman bila pengguna sengaja meminta mengedit,
        /// bukan dibuka langsung dalam keadaan bisa-disunting.
        /// </summary>
        public static bool IsModeEditAwalanAktif() => GetBool(KeyModeEditAwalan, false);

        public static void SetModeEditAwalan(bool v) => SetBool(KeyModeEditAwalan, v);
    }

}
