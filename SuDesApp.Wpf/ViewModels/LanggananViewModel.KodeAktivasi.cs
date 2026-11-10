using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using SuDesApp.ControlSurat;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class LanggananViewModel : ObservableObject
    {
        // =======================================================================
        // Kode aktivasi & status langganan.
        // =======================================================================

        private string _kodeAktivasi = string.Empty;

        /// <summary>Kode aktivasi yang diketik pengguna (tersimpan saat Simpan).</summary>
        public string KodeAktivasi
        {
            get => _kodeAktivasi;
            set
            {
                if (SetProperty(ref _kodeAktivasi, value))
                {
                    // Kode ikut ke rincian QRIS (referensi pembayaran manual).
                    OnPropertyChanged(nameof(QrisReferensi));
                    OnPropertyChanged(nameof(KodeAktivasiFormatOK));
                    OnPropertyChanged(nameof(KodeAktivasiPetunjuk));
                }
            }
        }

        /// <summary>
        /// True bila kode dalam bentuk resmi SUDES-XXXX-XXXX (setelah
        /// dinormalisasi). Dipakai untuk warna petunjuk di bawah kotak input.
        /// </summary>
        public bool KodeAktivasiFormatOK => FormatKodeAktivasi.BentukSah(_kodeAktivasi);

        /// <summary>
        /// Petunjuk bentuk kode yang selalu terlihat di bawah kotak input,
        /// supaya kegagalan "tidak dikenal" tidak membingungkan: bentuknya
        /// dicek sebelum dikirim, bukan hanya ditebak dari balasan server.
        /// </summary>
        public string KodeAktivasiPetunjuk => KodeAktivasiFormatOK
            ? "Format sudah benar (SUDES-XXXX-XXXX) — tekan Simpan & Cek, atau tekan Enter."
            : string.IsNullOrWhiteSpace(_kodeAktivasi)
                ? "Contoh bentuk kode: SUDES-K7Q2-4831 (13 karakter, huruf besar). Tempel dari papan klip boleh — spasi dan huruf kecil dirapikan otomatis."
                : "Bentuknya belum lengkap — harus SUDES-XXXX-XXXX (13 karakter). Perbaiki dulu, atau gunakan Tempel agar tidak salah ketik.";

        private string _langgananStatus = "Belum diperiksa";

        /// <summary>Status langganan yang terbaca (Tidak aktif / Aktif s.d. …).</summary>
        public string LanggananStatus
        {
            get => _langgananStatus;
            private set => SetProperty(ref _langgananStatus, value);
        }

        private string _langgananInfo = string.Empty;

        /// <summary>Nama desa + sumber status (cache offline atau cek server).</summary>
        public string LanggananInfo
        {
            get => _langgananInfo;
            private set => SetProperty(ref _langgananInfo, value);
        }

        private int _langgananTone;

        /// <summary>Nada kotak status langganan: 1 = aktif (hijau), 2 = tidak aktif (kuning), 3 = galat (merah), 0 = netral.</summary>
        public int LanggananTone
        {
            get => _langgananTone;
            private set => SetProperty(ref _langgananTone, value);
        }


    }
}
