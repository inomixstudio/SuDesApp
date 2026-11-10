using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Microsoft.Extensions.Logging;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.GeneratorPdf;
using SuDesApp.Utilities;
using SuDesApp.Wilayah;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class SetelanViewModel : ObservableObject
    {
        // =================================================================
        // Jenis wilayah tingkat kabupaten/kota. Ini yang menentukan kata pada
        // baris pertama kop: “PEMERINTAH KOTA BANDUNG” atau “PEMERINTAH
        // KABUPATEN BANDUNG”. Pada mode pilih, nilainya mengikuti pilihan dari
        // daftar wilayah (Kabupaten Bandung dan Kota Bandung bernama sama, jadi
        // hanya pilihan pengguna yang bisa memastikan). Pada mode manual, pengguna
        // memilihnya lewat dua pilihan di bawah kolom Kabupaten.
        // =================================================================

        /// <summary>Benar bila wilayah desa berada di sebuah kabupaten.</summary>
        public bool WilayahKabupaten
        {
            get => _jenisKabupaten == _konstJenisKabupaten;
            set { if (value) TerapkanJenisKabupaten(_konstJenisKabupaten); }
        }

        /// <summary>Benar bila wilayah desa berada di sebuah kota.</summary>
        public bool WilayahKota
        {
            get => _jenisKabupaten == _konstJenisKota;
            set { if (value) TerapkanJenisKabupaten(_konstJenisKota); }
        }

        /// <summary>Keterangan kata yang akan dicetak pada baris pertama kop surat.</summary>
        public string InfoJenisWilayah => _jenisKabupaten switch
        {
            _konstJenisKota => "Kop dicetak \"PEMERINTAH KOTA …\".",
            _konstJenisKabupaten => "Kop dicetak \"PEMERINTAH KABUPATEN …\".",
            _ => "Jenis wilayah belum ditentukan — kop dicetak \"PEMERINTAH KABUPATEN …\" " +
                 "seperti sebelumnya. Pilih salah satu supaya penulisannya pasti."
        };

        /// <summary>Ubah jenis wilayah sebagai tindakan pengguna (ikut menandai perubahan belum disimpan).</summary>
        private void TerapkanJenisKabupaten(string jenis)
        {
            if (string.Equals(_jenisKabupaten, jenis, StringComparison.Ordinal))
            {
                return;
            }

            _jenisKabupaten = jenis;
            SegarkanJenisKabupaten();
            OnFieldChanged();
            _logger.LogInformation("Jenis wilayah kabupaten/kota diubah menjadi {Jenis}", jenis);
        }

        /// <summary>
        /// Ambil jenis wilayah dari kabupaten/kota yang sedang dipilih pada jenjang.
        /// Dipakai saat pilihan wilayah dikenali dari data tersimpan maupun saat
        /// pengguna memilih dari daftar — supaya kop surat tidak perlu menebak.
        /// </summary>
        /// <returns>True bila jenis wilayahnya berubah.</returns>
        private bool SinkronkanJenisKabupatenDariPilihan()
        {
            var kabupaten = _pemilih.KabupatenTerpilih;
            if (kabupaten == null)
            {
                // Belum ada pilihan tingkat kabupaten/kota: nilai tersimpan tidak
                // dihapus supaya isian manual tidak kehilangan keterangannya.
                return false;
            }

            string jenis = kabupaten.Jenis == WilayahJenis.Kota ? _konstJenisKota : _konstJenisKabupaten;
            if (string.Equals(_jenisKabupaten, jenis, StringComparison.Ordinal))
            {
                return false;
            }

            _jenisKabupaten = jenis;
            SegarkanJenisKabupaten();

            // Jenis wilayah menentukan isi kop dan apakah data masih berbeda dari yang
            // tersimpan — data desa lama belum menyimpan kolom ini, jadi setelah
            // dikenali dari pilihan wilayah tombol Simpan perlu ikut menyala.
            OnFieldChanged();
            return true;
        }

        private void SegarkanJenisKabupaten()
        {
            OnPropertyChanged(nameof(WilayahKabupaten));
            OnPropertyChanged(nameof(WilayahKota));
            OnPropertyChanged(nameof(InfoJenisWilayah));
        }


    }
}
