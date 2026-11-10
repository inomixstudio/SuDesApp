using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Utilities;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class PembaruanViewModel : ObservableObject
{
        // =====================================================================
        // Riwayat pembaruan
        // =====================================================================

        /// <summary>Benar bila ada riwayat pembaruan untuk ditampilkan.</summary>
        public bool RiwayatTersedia { get => _riwayatTersedia; private set => SetProperty(ref _riwayatTersedia, value); }

        private bool _riwayatTersedia;
        private int _jumlahRiwayatTersimpan;

        /// <summary>
        /// Keterangan jumlah entri di kanan judul: halaman hanya menampilkan
        /// <see cref="MaksRiwayat"/> terbaru, sementara sisanya tetap tersimpan di
        /// berkas — angka ini yang menjelaskan tombol Bersihkan Riwayat.
        /// </summary>
        public string RingkasanRiwayat
        {
            get
            {
                if (RiwayatTidakTerbaca)
                {
                    return "berkas tidak terbaca";
                }

                if (_jumlahRiwayatTersimpan <= MaksRiwayat)
                {
                    return _jumlahRiwayatTersimpan.ToString() + " entri";
                }

                return $"{Riwayat.Count} dari {_jumlahRiwayatTersimpan} entri tersimpan";
            }
        }

        /// <summary>
        /// Benar bila berkas riwayat ada tetapi isinya tidak dapat dibaca — daftarnya
        /// kosong, jadi halaman menampilkan penjelasan singkat sebagai ganti kartu entri.
        /// </summary>
        public bool RiwayatTidakTerbaca { get => _riwayatTidakTerbaca; private set => SetProperty(ref _riwayatTidakTerbaca, value); }

        private bool _riwayatTidakTerbaca;

        /// <summary>Muat (atau muat ulang) riwayat dari disk — dipanggil saat halaman dibuka.</summary>
        public void MuatRiwayat()
        {
            Riwayat.Clear();

            try
            {
                var tersimpan = RiwayatPembaruanStore.Muat();
                _jumlahRiwayatTersimpan = tersimpan.Count;

                // Halaman hanya menampilkan sejumlah entri terbaru (lihat MaksRiwayat);
                // seluruhnya tetap tersimpan di berkas RiwayatPembaruanStore.
                foreach (var entri in tersimpan.Take(MaksRiwayat))
                {
                    Riwayat.Add(new BarisRiwayatPembaruan(entri));
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Riwayat pembaruan tidak dapat dimuat.");
                _jumlahRiwayatTersimpan = 0;
            }

            // Bila berkasnya ada tetapi isinya tidak terbaca, kartu tetap ditampilkan
            // supaya pengguna punya jalan membuang berkas itu dari halaman ini.
            RiwayatTidakTerbaca = Riwayat.Count == 0 && RiwayatPembaruanStore.AdaBerkas;
            RiwayatTersedia = Riwayat.Count > 0 || RiwayatTidakTerbaca;
            OnPropertyChanged(nameof(RingkasanRiwayat));
            RaiseCommandStates();
        }

        /// <summary>
        /// Buang seluruh riwayat pembaruan (berkas riwayat dihapus). Bersifat permanen,
        /// jadi keputusannya ditanyakan lewat kartu di dalam halaman — bukan dialog
        /// terpisah — dan hanya menyentuh riwayat, tidak menyentuh data desa.
        /// </summary>
        private async Task BersihkanRiwayatAsync()
        {
            if (!RiwayatTersedia)
            {
                return;
            }

            bool lanjut = await TanyaAsync(
                "Bersihkan Riwayat Pembaruan",
                $"Seluruh {_jumlahRiwayatTersimpan} catatan riwayat pembaruan akan dibuang dari komputer ini. " +
                "Surat, database, template, dan pengaturan Anda tidak ikut terhapus.",
                "Buang seluruh riwayat sekarang?",
                "Ya, bersihkan",
                "Batal",
                "bersihkan-riwayat");

            if (!lanjut)
            {
                StatusText = "Riwayat pembaruan tidak dibuang.";
                return;
            }

            try
            {
                RiwayatPembaruanStore.Kosongkan();
                MuatRiwayat();

                TampilkanPesan(
                    JenisPesanInline.Sukses,
                    "Riwayat pembaruan dibersihkan",
                    "Riwayat pembaruan sudah dibuang. Catatan baru akan muncul lagi di sini setiap ada pembaruan yang dicoba dipasang.",
                    "bersihkan-riwayat");
                StatusText = "Riwayat pembaruan sudah dibuang.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membersihkan riwayat pembaruan");
                TampilkanPesan(
                    JenisPesanInline.Galat,
                    "Riwayat pembaruan tidak dapat dibuang",
                    ex.Message,
                    "bersihkan-riwayat");
            }
        }

        /// <summary>Catat percobaan pemasangan yang gagal sebelum skrip penerap sempat menulis hasilnya.</summary>
        private void CatatRiwayatGagal(string versi, string jenis, string pesan)
        {
            try
            {
                RiwayatPembaruanStore.Tambah(new EntriRiwayatPembaruan
                {
                    Versi = versi,
                    Jenis = jenis,
                    JumlahBerkas = 0,
                    Berhasil = false,
                    Pesan = pesan,
                    DariVersi = GetCurrentVersion().ToString(),
                    Waktu = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Riwayat pembaruan gagal dicatat.");
            }
        }

        /// <summary>Baris siap tampil pada daftar riwayat pembaruan.</summary>
        public class BarisRiwayatPembaruan
        {
            public BarisRiwayatPembaruan(EntriRiwayatPembaruan entri)
            {
                Versi = string.IsNullOrWhiteSpace(entri.Versi) ? "-" : entri.Versi;
                Tanggal = entri.Waktu.ToString("dd MMM yyyy HH:mm");
                Hasil = entri.Berhasil ? "Berhasil" : "Gagal";
                Berhasil = entri.Berhasil;
                Pesan = string.IsNullOrWhiteSpace(entri.Pesan) ? "-" : entri.Pesan;

                Jenis = entri.Jenis switch
                {
                    "Installer" => "Installer penuh",
                    "Tambalan" => "Pembaruan kecil",
                    var lain => lain
                };

                var dari = string.IsNullOrWhiteSpace(entri.DariVersi) ? string.Empty : $" dari {entri.DariVersi}";
                Berkas = entri.JumlahBerkas > 0
                    ? $"{entri.JumlahBerkas} berkas{dari}"
                    : (string.IsNullOrWhiteSpace(entri.DariVersi) ? "-" : $"dari versi {entri.DariVersi}");
            }

            public string Versi { get; }
            public string Jenis { get; }
            public string Tanggal { get; }
            public string Hasil { get; }
            public bool Berhasil { get; }
            public string Berkas { get; }
            public string Pesan { get; }
        }


    }
}
