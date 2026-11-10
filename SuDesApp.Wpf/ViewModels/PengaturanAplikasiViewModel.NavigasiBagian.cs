using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Windows;
using SuDesApp.Configuration;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Input;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Utilities;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class PengaturanAplikasiViewModel : ObservableObject
    {
        // =======================================================================
        // Navigasi bagian halaman. Pengaturan aplikasi sudah banyak, jadi isinya
        // dikelompokkan per bagian dengan daftar navigasi di sisi kiri — bukan
        // sederet kartu panjang yang harus digulir.
        // =======================================================================

        public ObservableCollection<PengaturanSectionVM> Sections { get; } = new();

        public PengaturanSectionVM BagianUmum { get; private set; } = null!;
        public PengaturanSectionVM BagianGoogleOauth { get; private set; } = null!;
        public PengaturanSectionVM BagianDatabase { get; private set; } = null!;
        public PengaturanSectionVM BagianInformasi { get; private set; } = null!;

        private PengaturanSectionVM? _selectedSection;

        /// <summary>Bagian yang sedang ditampilkan (dipilih dari daftar navigasi).</summary>
        public PengaturanSectionVM? SelectedSection
        {
            get => _selectedSection;
            set
            {
                if (value == null || ReferenceEquals(value, _selectedSection)) return;
                if (!SetProperty(ref _selectedSection, value)) return;

                foreach (var bagian in Sections) bagian.Aktif = ReferenceEquals(bagian, value);

                OnPropertyChanged(nameof(JudulSeksiAktif));
                OnPropertyChanged(nameof(RingkasanSeksiAktif));
            }
        }

        /// <summary>Judul bagian yang sedang tampil (dipakai kepala area isi).</summary>
        public string JudulSeksiAktif => _selectedSection?.Judul ?? string.Empty;

        /// <summary>Penjelasan singkat bagian yang sedang tampil.</summary>
        public string RingkasanSeksiAktif => _selectedSection?.Ringkasan ?? string.Empty;

        /// <summary>Susun daftar bagian + pilih bagian pertama (Umum) saat halaman dibuka.</summary>
        private void SiapkanBagian()
        {
            // Ikon memakai glyph Segoe Fluent Icons (IkonMenu) sehingga mengikuti
            // tema dan tampil konsisten dengan sidebar — bukan emoji berwarna.
            BagianUmum = new PengaturanSectionVM("umum", "Umum & Preferensi", IkonMenu.Pengaturan,
                "Perilaku aplikasi, kecepatan animasi antarmuka, preferensi kerja, dan pembersihan berkas berkala.");
            BagianGoogleOauth = new PengaturanSectionVM("google-oauth", "Kredensial Google", IkonMenu.KataSandi,
                "Client ID & Client Secret klien OAuth aplikasi: pemeriksaan sumber kredensial dan isian " +
                "untuk komputer yang paketnya tidak memuat kredensial tertanam.");
            BagianDatabase = new PengaturanSectionVM("database", "Database Desa", IkonMenu.Cadangkan,
                "Lokasi berkas database desa yang sedang dipakai dan pemindahannya ke folder data sendiri, " +
                "misalnya di drive lain — tanpa menyalin berkas ke folder aplikasi.");
            BagianInformasi = new PengaturanSectionVM("informasi", "Informasi", IkonMenu.Tentang,
                "Sesi login yang sedang aktif dan lokasi berkas pengaturan pada komputer ini.");

            foreach (var bagian in new[]
                     {
                         BagianUmum, BagianGoogleOauth, BagianDatabase, BagianInformasi
                     })
            {
                Sections.Add(bagian);
            }

            SelectedSection = BagianUmum;
        }

        /// <summary>Benar bila mulai ulang (penerapan lokasi database) sedang berjalan — menonaktifkan tombolnya.</summary>
        private bool _databaseSibuk;

        public bool DatabaseSibuk
        {
            get => _databaseSibuk;
            private set
            {
                if (SetProperty(ref _databaseSibuk, value)) SimpanMulaiUlangDatabaseCommand.RaiseCanExecuteChanged();
            }
        }


    }
}
