using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Utilities;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>Satu baris persetujuan dokumen pada bagian "Persetujuan dokumen".</summary>
    public sealed class PersetujuanDokumenTampil
    {
        public PersetujuanDokumenTampil(StatusPersetujuan status)
        {
            Judul = status.Dokumen.Judul;
            SudahDisetujui = status.Tercatat is not null;
            PerluPembaruan = status.VersiBerubah;

            if (status.Tercatat is { } tercatat)
            {
                WaktuDisetujui = tercatat.DisetujuiPada;
                VersiDisetujui = $"Versi {tercatat.Versi}";
                DisetujuiPadaTampil = PersetujuanDokumen.WaktuTampil(tercatat.DisetujuiPada);
            }

            VersiTerpasang = status.VersiTerpasang is null
                ? "Versi dokumen tidak terbaca dari folder aplikasi."
                : $"Versi terpasang: {status.VersiTerpasang.Versi}"
                  + (string.IsNullOrWhiteSpace(status.VersiTerpasang.TanggalBerlaku)
                      ? string.Empty
                      : $" · berlaku sejak {status.VersiTerpasang.TanggalBerlaku}");
        }

        public string Judul { get; }

        /// <summary>Versi yang benar-benar disetujui pengguna, mis. "Versi 1.0".</summary>
        public string VersiDisetujui { get; } = "Belum disetujui";

        /// <summary>Waktu persetujuan siap tampil; kosong bila belum pernah.</summary>
        public string DisetujuiPadaTampil { get; } = "Belum pernah disetujui di komputer ini.";

        /// <summary>
        /// Waktu persetujuan untuk perhitungan ringkasan. Dokumen yang belum
        /// disetujui memakai nilai minimum agar tidak pernah terpilih sebagai
        /// "persetujuan terakhir".
        /// </summary>
        public DateTime WaktuDisetujui { get; } = DateTime.MinValue;

        /// <summary>Versi dokumen yang terpasang saat halaman ini dibuka.</summary>
        public string VersiTerpasang { get; }

        public bool SudahDisetujui { get; }

        /// <summary>Dokumen di komputer ini sudah lebih baru dari yang disetujui.</summary>
        public bool PerluPembaruan { get; }

        public string StatusTampil => SudahDisetujui ? "Disetujui" : "Belum disetujui";

        public string Catatan => PerluPembaruan
            ? "Dokumen di komputer ini sudah diperbarui — persetujuan versi baru akan diminta saat aplikasi dibuka berikutnya."
            : string.Empty;
    }

}
