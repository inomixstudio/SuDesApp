using System;
using System.Windows;
using SuDesApp.Sinkron;
using SuDesApp.Wpf.Services;

namespace SuDesApp.Wpf.ViewModels
{
    // Status sinkron Android di status bar. Ditempatkan di modul sendiri supaya
    // berkas utama dan modul status lain tidak bertambah panjang.
    public partial class MainWindowViewModel
    {
        private readonly PemantauAndroidLatar? _pemantauAndroid;
        private readonly PenerbitanSinkronLatar? _penerbitanSinkron;

        /// <summary>Perangkat yang terakhir dilaporkan tersambung — penanda agar notifikasi tidak diulang tiap putaran.</summary>
        private string? _perangkatTerhubungTerakhir;

        private string _statusAndroidTampil = string.Empty;
        private string _statusAndroidTooltip = string.Empty;
        private bool _statusAndroidVisible;
        private int _statusAndroidTone;

        /// <summary>Teks chip: "Android terhubung: &lt;nama&gt;" atau "Android: n surat menunggu".</summary>
        public string StatusAndroidTampil
        {
            get => _statusAndroidTampil;
            private set => SetProperty(ref _statusAndroidTampil, value);
        }

        public string StatusAndroidTooltip
        {
            get => _statusAndroidTooltip;
            private set => SetProperty(ref _statusAndroidTooltip, value);
        }

        public bool StatusAndroidVisible
        {
            get => _statusAndroidVisible;
            private set => SetProperty(ref _statusAndroidVisible, value);
        }

        /// <summary>1 = perangkat tersambung (hijau), 0 = hanya antrean menunggu.</summary>
        public int StatusAndroidTone
        {
            get => _statusAndroidTone;
            private set => SetProperty(ref _statusAndroidTone, value);
        }

        /// <summary>
        /// Pemeriksaan berjalan di thread latar; perubahan properti harus
        /// terjadi di thread UI. Nilai pertama kali muncul setelah aplikasi
        /// dijalankan — begitu pula notifikasi "Android terhubung".
        /// </summary>
        private void TerapkanStatusAndroid(RingkasanAndroid ringkasan)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
            {
                TerapkanStatusAndroidSekarang(ringkasan);
                return;
            }

            _ = dispatcher.BeginInvoke(() => TerapkanStatusAndroidSekarang(ringkasan));
        }

        /// <summary>
        /// Notifikasi hasil impor otomatis (≥1 surat). Dipanggil dari thread
        /// latar; pindah ke UI seperti <see cref="TerapkanStatusAndroid"/>.
        /// </summary>
        private void TerapkanImporOtomatis(SinkronHasil hasil)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
            {
                NotifikasiImporOtomatis(hasil);
                return;
            }

            _ = dispatcher.BeginInvoke(() => NotifikasiImporOtomatis(hasil));
        }

        private void NotifikasiImporOtomatis(SinkronHasil hasil)
        {
            _notifications.Success(
                "Sinkron otomatis",
                $"{hasil.Berhasil} surat dari perangkat masuk ke register desktop.",
                "sinkron-android");
        }

        /// <summary>Potong teks untuk chip status bar; nama lengkap tetap di tooltip.</summary>
        private static string Pendek(string teks, int maksimum)
        {
            if (string.IsNullOrEmpty(teks) || teks.Length <= maksimum) return teks ?? string.Empty;
            return teks[..(maksimum - 1)].TrimEnd() + "…";
        }

        private void TerapkanStatusAndroidSekarang(RingkasanAndroid ringkasan)
        {
            if (!ringkasan.Terkonfigurasi)
            {
                StatusAndroidVisible = false;
                StatusAndroidTampil = string.Empty;
                _perangkatTerhubungTerakhir = null;
                return;
            }

            if (ringkasan.AdaPerangkatTerhubung)
            {
                var nama = ringkasan.NamaPerangkat!;
                StatusAndroidTone = 1;

                // Chip status bar harus ringkas (letaknya di samping lonceng):
                // nama panjang dipotong, nama lengkap tetap ada di tooltip.
                StatusAndroidTampil = "Android · " + Pendek(nama, 14);
                StatusAndroidTooltip =
                    $"Perangkat \"{nama}\" melapor ke desktop dan sudah mengambil pengaturan desa. " +
                    "Surat yang dibuat di perangkat masuk ke antrean Supabase.";
                StatusAndroidVisible = true;

                // Pemberitahuan hanya saat perangkat BARU tersambung, bukan tiap
                // putaran pemeriksaan (20 detik) — supaya lonceng tidak banjir.
                if (!string.Equals(_perangkatTerhubungTerakhir, nama, StringComparison.Ordinal))
                {
                    _notifications.Success(
                        "Android terhubung",
                        $"Perangkat {nama} tersambung ke desktop dan sudah mengambil pengaturan desa.",
                        "sinkron-android");
                }

                _perangkatTerhubungTerakhir = nama;
                return;
            }

            _perangkatTerhubungTerakhir = null;

            if (ringkasan.JumlahAntrean > 0)
            {
                StatusAndroidTone = 0;
                StatusAndroidTampil = $"{ringkasan.JumlahAntrean} surat menunggu";
                StatusAndroidTooltip =
                    $"{ringkasan.JumlahAntrean} surat dari perangkat menunggu diimpor. " +
                    "Buka halaman Sinkron Android untuk mengimpornya.";
                StatusAndroidVisible = true;
                return;
            }

            StatusAndroidVisible = false;
            StatusAndroidTampil = string.Empty;
            StatusAndroidTooltip = string.Empty;
        }
    }
}
