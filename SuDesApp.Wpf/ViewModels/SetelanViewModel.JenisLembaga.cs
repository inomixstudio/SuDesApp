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
        // Jenis lembaga (Desa/Kelurahan) dan NIP pejabat. Menentukan kata “DESA”
        // atau “KELURAHAN” pada kop dan badan surat, jabatan penandatangan
        // (“Kepala Desa …” / “Lurah …”), serta apakah NIP tercetak di bawah nama
        // penandatangan: hanya Lurah (kelurahan) yang ASN (
        // Kepala Desa non-ASN tidak memakai NIP).
        // Pada mode pilih, jenisnya mengikuti wilayah yang dipilih dari daftar
        // (WilayahJenis.Desa/Kelurahan); pada mode manual, keduanya dipilih pakai
        // dua pilihan di bawah kolom Nama Desa.
        // =================================================================

        /// <summary>Benar bila lembaga berjenis Desa (nilai bawaan data lama).</summary>
        public bool LembagaDesa
        {
            get => !LembagaKelurahan;
            set { if (value) TerapkanJenisLembaga(_konstLembagaDesa); }
        }

        /// <summary>Benar bila lembaga berjenis Kelurahan — penandatangan Lurah + NIP.</summary>
        public bool LembagaKelurahan
        {
            get => _jenisLembaga == _konstLembagaKelurahan;
            set { if (value) TerapkanJenisLembaga(_konstLembagaKelurahan); }
        }

        /// <summary>Keterangan kata yang tercetak pada kop dan badan surat.</summary>
        public string InfoJenisLembaga => LembagaKelurahan
            ? "Surat mencetak \"KELURAHAN …\"; penandatangan Lurah dan NIP ikut tercetak di bawah nama."
            : "Surat mencetak \"PEMERINTAH DESA …\"; penandatangan Kepala Desa (non-ASN, tanpa NIP).";

        /// <summary>Label kolom pejabat penandatangan: “Kepala Desa *” atau “Lurah *”.</summary>
        public string LabelPejabatKepala => LembagaKelurahan ? "Lurah *" : "Kepala Desa *";

        /// <summary>Keterangan kolom pejabat penandatangan pada bagian Pejabat Desa.</summary>
        public string InfoPejabatKepala => LembagaKelurahan
            ? "Nama Lurah pada tanda tangan surat; kolom NIP di bawahnya ikut tercetak."
            : "Nama Kepala Desa pada tanda tangan surat.";

        /// <summary>
        /// NIP pejabat penandatangan — hanya tercetak untuk Kelurahan (Lurah).
        /// Untuk Desa, kolomnya boleh dikosongkan: Kepala Desa bukan ASN.
        /// </summary>
        public string NipKepalaDesa { get => _nipKepalaDesa; set { if (SetProperty(ref _nipKepalaDesa, value)) OnFieldChanged(); } }

        /// <summary>Ubah jenis lembaga sebagai tindakan pengguna (ikut menyalakan tombol Simpan).</summary>
        private void TerapkanJenisLembaga(string jenis)
        {
            if (string.Equals(_jenisLembaga, jenis, StringComparison.Ordinal))
            {
                return;
            }

            _jenisLembaga = jenis;
            SegarkanJenisLembaga();
            OnFieldChanged();
            _logger.LogInformation("Jenis lembaga diubah menjadi {Jenis}", jenis);
        }

        /// <summary>
        /// Ambil jenis lembaga dari desa/kelurahan yang sedang dipilih pada jenjang
        /// wilayah — sama seperti jenis kabupaten/kota, supaya tidak perlu minta
        /// pengguna memilih ulang yang sudah jelas dari daftar.
        /// </summary>
        /// <returns>True bila jenis lembaganya berubah.</returns>
        private bool SinkronkanJenisLembagaDariPilihan()
        {
            var desa = _pemilih.DesaTerpilih;
            if (desa == null)
            {
                // Belum ada pilihan tingkat desa/kelurahan: nilai tersimpan tidak
                // dihapus supaya isian manual tidak kehilangan keterangannya.
                return false;
            }

            string jenis = desa.Jenis == WilayahJenis.Kelurahan ? _konstLembagaKelurahan : _konstLembagaDesa;
            if (string.Equals(_jenisLembaga, jenis, StringComparison.Ordinal))
            {
                return false;
            }

            _jenisLembaga = jenis;
            SegarkanJenisLembaga();
            OnFieldChanged();
            return true;
        }

        private void SegarkanJenisLembaga()
        {
            OnPropertyChanged(nameof(LembagaDesa));
            OnPropertyChanged(nameof(LembagaKelurahan));
            OnPropertyChanged(nameof(InfoJenisLembaga));
            OnPropertyChanged(nameof(LabelPejabatKepala));
            OnPropertyChanged(nameof(InfoPejabatKepala));
        }
        public string Alamat { get => _alamat; set { if (SetProperty(ref _alamat, value)) OnFieldChanged(); } }
        public string Kodepos { get => _kodepos; set { if (SetProperty(ref _kodepos, value)) OnFieldChanged(); } }


    }
}
