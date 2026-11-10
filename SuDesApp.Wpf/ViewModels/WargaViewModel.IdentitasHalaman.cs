using System;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SudesExPlus;
using SudesExPlus.Style;
using System.Drawing;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.GeneratorPdf;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class WargaViewModel : ObservableObject
    {
        // ----- Identitas halaman -----
        public string HeaderTitle => "DATA WARGA";
        public string HeaderSubtitle =>
            "Data kependudukan desa: nomor induk, Kartu Keluarga, alamat RT/RW, dan status tinggal warga.";

        public ObservableCollection<WargaBaris> Items { get; private set; } = new();
        public ObservableCollection<WargaStatistikKartu> KartuStatistik { get; private set; } = new();
        public ObservableCollection<WargaStatistikBaris> RekapRt { get; private set; } = new();
        public ObservableCollection<WargaStatistikBaris> RekapKelompokUsia { get; private set; } = new();

        /// <summary>Pilihan status untuk dropdown filter; item pertama berarti "semua".</summary>
        public IReadOnlyList<string> PilihanStatus { get; } =
            new[] { string.Empty }.Concat(StatusWargaTipe.Semua).ToList();

        /// <summary>Pilihan status untuk form ubah; dipakai <c>UbahStatusCommand</c>.</summary>
        public IReadOnlyList<string> PilihanStatusForm { get; } = StatusWargaTipe.Semua;

        public IReadOnlyList<string> DaftarAgama { get; } =
            new[] { "Islam", "Kristen", "Katolik", "Hindu", "Buddha", "Konghucu" };

        public IReadOnlyList<string> DaftarJenisKelamin { get; } = new[] { "Laki-laki", "Perempuan" };

        public IReadOnlyList<string> DaftarStatusPerkawinan { get; } =
            new[] { "Belum Kawin", "Kawin", "Cerai Hidup", "Cerai Mati" };

        /// <summary>Kedudukan dalam Kartu Keluarga untuk dropdown form warga.</summary>
        public IReadOnlyList<string> DaftarStatusKeluarga { get; } =
            new[] { string.Empty }.Concat(StatusKeluargaTipe.Semua).ToArray();

        public IReadOnlyList<string> DaftarGolonganDarah { get; } =
            new[] { "A", "B", "AB", "O", "Tidak diketahui" };

        /// <summary>
        /// Pendidikan untuk dropdown form warga. Memakai jenjang baku BPS — daftar
        /// yang sama dengan tabel laporan kependudukan — sehingga pilihan operator
        /// langsung masuk kelompok yang benar, tanpa dipetakan ulang saat dicetak.
        /// </summary>
        public IReadOnlyList<string> PilihanPendidikan { get; } = JenjangPendidikanBps.Lista;

        /// <summary>
        /// Saran pekerjaan untuk form warga. Kolom Pekerjaan tetap bebas diisi
        /// karena pekerjaan warga desa sangat beragam; daftar ini hanya jalan pintas.
        /// </summary>
        public IReadOnlyList<string> DaftarPekerjaanUmum { get; } =
            new[] { "Petani", "Buruh Tani", "Wiraswasta", "Pedagang", "Buruh Pabrik", "ASN/PNS", "TNI/Polri", "Guru/Dosen", "Tenaga Kesehatan", "Pensiunan", "Pelajar/Mahasiswa", "Ibu Rumah Tangga", "Lainnya" };


    }
}
