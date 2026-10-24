using SuDesApp.Utilities;
using System;

namespace SuDesApp.Api
{
    /// <summary>
    /// Pengaturan API yang dibaca listener dan ditulis halaman API. Disimpan di
    /// AppPreferenceStore (baris "kunci=nilai" di %LOCALAPPDATA%\SuDesApp\login_prefs.json)
    /// supaya tidak perlu tabel database dan tetap terbaca sebelum login selesai.
    ///
    /// Semua nilai punya default yang AMAN: API mati, hanya localhost, kunci
    /// tetap wajib ada. Menyalakan API harus lewat halaman pengaturan, bukan
    /// sekadar mengubah berkas.
    /// </summary>
    public static class ApiPengaturan
    {
        // Kunci preferensi; ditulis di sini agar seluruh pengaturan API berada
        // dalam satu berkas.
        private const string KunciAktif = "apiAktif";
        private const string KunciPort = "apiPort";
        private const string KunciJaringan = "apiJaringan";
        private const string KunciCatatAktivitas = "apiCatatAktivitas";
        private const string KunciBatasPermintaan = "apiBatasPermintaan";

        /// <summary>Port bawaan. Dipilih berbeda dari 8787 (webhook Meta) supaya tidak bentrok.</summary>
        public const int PortBawaan = 8790;

        /// <summary>
        /// Port di bawah 1024 dipakai service system dan butuh hak admin;
        /// listener lokal tidak perlu itu, jadi ditolak lebih awal dengan pesan
        /// yang jelas.
        /// </summary>
        public const int PortMinimum = 1025;

        public const int PortMaksimum = 65535;

        public const int BatasPermintaanBawaan = 60;

        /// <summary>API aktif? Default: tidak, sampai operator menyalakannya sendiri.</summary>
        public static bool Aktif => AppPreferenceStore.GetBool(KunciAktif, false);

        /// <summary>True bila listener juga dibuka untuk jaringan (bukan hanya localhost).</summary>
        public static bool IzinkanJaringan => AppPreferenceStore.GetBool(KunciJaringan, false);

        /// <summary>Catat setiap panggilan API ke Riwayat Aktivitas? Default: ya.</summary>
        public static bool CatatAktivitas => AppPreferenceStore.GetBool(KunciCatatAktivitas, true);

        /// <summary>Port listener, selalu dalam rentang yang aman.</summary>
        public static int Port => NormalisasiPort(AppPreferenceStore.GetInt(KunciPort, PortBawaan));

        /// <summary>
        /// Batas permintaan per menit per alamat pengirim. Mencegah satu klien
        /// yang salah (atau mengulang tanpa henti) membebani aplikasi.
        /// </summary>
        public static int BatasPermintaan => Math.Clamp(
            AppPreferenceStore.GetInt(KunciBatasPermintaan, BatasPermintaanBawaan), 5, 600);

        public static void SetAktif(bool nilai) => AppPreferenceStore.SetBool(KunciAktif, nilai);

        public static void SetIzinkanJaringan(bool nilai) => AppPreferenceStore.SetBool(KunciJaringan, nilai);

        public static void SetCatatAktivitas(bool nilai) => AppPreferenceStore.SetBool(KunciCatatAktivitas, nilai);

        public static void SetBatasPermintaan(int nilai) =>
            AppPreferenceStore.SetInt(KunciBatasPermintaan, Math.Clamp(nilai, 5, 600));

        /// <summary>Simpan port; nilai di luar rentang ditolak.</summary>
        public static void SetPort(int nilai)
        {
            if (!NormalisasiPort(nilai, out var hasil))
                throw new ArgumentOutOfRangeException(
                    nameof(nilai), nilai, $"Port harus antara {PortMinimum} dan {PortMaksimum}.");

            AppPreferenceStore.SetInt(KunciPort, hasil);
        }

        /// <summary>Port dalam rentang yang aman untuk listener.</summary>
        public static int NormalisasiPort(int port)
        {
            NormalisasiPort(port, out var hasil);
            return hasil;
        }

        /// <summary>
        /// True bila port sah. Nilai di luar rentang dianggap tidak sah (bukan
        /// diam-diam dipaksa) supaya pesan di UI bisa mengatakannya.
        /// </summary>
        public static bool NormalisasiPort(int port, out int hasil)
        {
            hasil = port;
            return port >= PortMinimum && port <= PortMaksimum;
        }

        /// <summary>
        /// Alamat dasar yang diklik pengguna di browser, mis.
        /// "http://localhost:8790/". Kalau jaringan dibuka, pakai nama mesin
        /// supaya alamatnya bisa dipakai dari komputer lain.
        /// </summary>
        public static string AlamatDasar(int? port = null)
        {
            var nomorPort = NormalisasiPort(port ?? Port);
            var host = IzinkanJaringan ? Environment.MachineName : "localhost";
            return $"http://{host}:{nomorPort}/";
        }

        /// <summary>Alamat dasar lengkap dengan prefix endpoint, tanpa garis miring akhir.</summary>
        public static string AlamatPrefix(int? port = null)
            => AlamatDasar(port) + ApiRute.Prefix.TrimStart('/');

        /// <summary>
        /// Syarat listener boleh dinyalakan: harus aktif, kunci API harus ada
        /// dan terbaca, serta portnya sah. Alasan kegagalan dikembalikan untuk
        /// ditampilkan di halaman API.
        /// </summary>
        public static bool SiapDijalankan(out string? alasan)
        {
            if (!Aktif)
            {
                alasan = "API belum diaktifkan.";
                return false;
            }

            if (!ApiKunci.Ada)
            {
                alasan = "Kunci API belum dibuat.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(ApiKunci.Muat()))
            {
                alasan = "Kunci API tidak dapat dibaca (berkas rusak atau dibuat user Windows lain).";
                return false;
            }

            if (!NormalisasiPort(AppPreferenceStore.GetInt(KunciPort, PortBawaan), out _))
            {
                alasan = "Port tidak sah.";
                return false;
            }

            alasan = null;
            return true;
        }
    }
}
