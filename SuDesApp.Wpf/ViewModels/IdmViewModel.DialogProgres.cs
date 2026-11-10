using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.GeneratorPdf;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wilayah;
using SuDesApp.Wpf.Controls;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Utilities;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class IdmViewModel : ObservableObject
    {
        // ===================== Dialog progres (gaya splash) =====================

        /// <summary>
        /// Deret langkah pemuatan dari situs pusat — dipakai dialog progres
        /// bergaya SplashStartupWindow: langkah yang sedang berjalan berdenyut,
        /// langkah selesai tercentang. Ditambahkan bertahap oleh
        /// <see cref="MuatAsync"/> persis seperti <c>SplashStartupWindow.Tahap</c>.
        /// </summary>
        public ObservableCollection<TahapMemuatTampil> TahapanMemuat { get; } = new();

        /// <summary>
        /// Mul satu langkah baru: langkah sebelumnya ditandai selesai lalu
        /// langkah ini menjadi yang aktif — pola yang sama dengan alur tahap
        /// splash (centang berjalan berikutnya begitu langkah lanjut dimulai).
        /// </summary>
        private void TambahTahapMemuat(string teks)
        {
            SelesaikanTahapMemuat();

            // Garis penghubung hanya digambar DI ANTARA titik: titik terakhir
            // tidak boleh menyisakan ekor garis yang menggantung ke kanan.
            foreach (var lama in TahapanMemuat)
            {
                lama.Terakhir = false;
            }

            TahapanMemuat.Add(new TahapMemuatTampil
            {
                Teks = teks,
                Status = TahapMemuatTampil.AktifStatus,
                Terakhir = true,
            });
            OnPropertyChanged(nameof(TahapAktifTeks));
        }

        /// <summary>
        /// Teks langkah yang sedang berjalan — ditampilkan di bawah deret ceklis
        /// dialog progres (pola splash). Langkah yang terakhir ditambahkan selalu
        /// yang aktif, dan teksnya tetap yang terakhir setelah semua selesai.
        /// </summary>
        public string TahapAktifTeks =>
            TahapanMemuat.Count > 0 ? TahapanMemuat[^1].Teks : string.Empty;

        /// <summary>Tandai langkah aktif terakhir sebagai selesai (tercentang).</summary>
        private void SelesaikanTahapMemuat()
        {
            foreach (var tahap in TahapanMemuat)
            {
                if (tahap.Status == TahapMemuatTampil.AktifStatus)
                {
                    tahap.Status = TahapMemuatTampil.SelesaiStatus;
                }
            }
        }

        /// <summary>Pemberitahuan bahwa data terpaksa diambil tanpa enkripsi.</summary>
        public string PeringatanSumber
        {
            get => _peringatanSumber;
            private set
            {
                if (SetProperty(ref _peringatanSumber, value)) OnPropertyChanged(nameof(AdaPeringatanSumber));
            }
        }

        public bool AdaPeringatanSumber => _peringatanSumber.Length > 0;

        /// <summary>
        /// Peringatan dini masa berlaku sertifikat situs pusat, dihitung dari
        /// catatan pemeriksaan terakhir (ditulis pemantau latar dan pemeriksa
        /// sertifikat) — tampil bahkan sebelum halaman memuat data, supaya
        /// pengguna tahu sertifikat mau habis jauh sebelum halaman menjadi
        /// kosong. Kosong bila sertifikat sehat atau belum pernah diperiksa.
        /// </summary>
        public string PesanSertifikat
        {
            get
            {
                var catatan = _sertifikatCatatan;
                if (catatan?.BerlakuSampai is not { } berlaku) return string.Empty;

                var status = IdmSertifikatStatus.Nilai(berlaku, _jam());
                return IdmSertifikatStatus.Pesan(status, berlaku, _jam());
            }
        }

        public bool AdaPesanSertifikat => PesanSertifikat.Length > 0;


    }
}
