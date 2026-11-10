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
    /// <summary>
    /// Satu baris pengaturan dengan tombol geser (toggle). Perubahan nilai
    /// LANGSUNG disimpan lewat aksi persist yang diberikan — tanpa tombol Simpan.
    /// </summary>
    public class SettingRowVM : ObservableObject
    {
        private readonly Action<bool> _persist;
        private bool _nilai;

        public string Judul { get; }
        public string Keterangan { get; }

        public bool Nilai
        {
            get => _nilai;
            set
            {
                if (SetProperty(ref _nilai, value))
                {
                    try
                    {
                        _persist(value);
                    }
                    catch
                    {
                        // Preferensi non-kritis — kegagalan menyimpan diabaikan.
                    }
                }
            }
        }

        public SettingRowVM(string judul, string keterangan, bool nilaiAwal, Action<bool> persist)
        {
            Judul = judul;
            Keterangan = keterangan;
            _nilai = nilaiAwal;
            _persist = persist;
        }
    }

    /// <summary>
    /// Satu pilihan cepat pada baris pengaturan bernilai angka (mis. Ringkas /
    /// Normal / Lega untuk tinggi maksimum baris tabel). Operator cukup menekan
    /// tombolnya — tidak perlu mengetik angka; angkanya sendiri tetap bisa
    /// disesuaikan lewat kotak isian di sebelahnya.
    /// </summary>
    public class PilihanAngkaVM : ObservableObject
    {
        private bool _aktif;

        public string Judul { get; }

        /// <summary>Nilai yang dipakai saat tombol ini ditekan.</summary>
        public int Nilai { get; }

        /// <summary>Sedang dipakai (nilai baris sama dengan nilai pilihan ini).</summary>
        public bool Aktif
        {
            get => _aktif;
            internal set => SetProperty(ref _aktif, value);
        }

        public RelayCommand PakaiCommand { get; }

        public PilihanAngkaVM(string judul, int nilai, Action<int> pakai)
        {
            Judul = judul;
            Nilai = nilai;
            PakaiCommand = new RelayCommand(() => pakai(nilai));
        }

        public override string ToString() => $"{Judul} ({Nilai})";
    }

    /// <summary>
    /// Baris pengaturan bernilai angka (mis. batas ukuran pembaruan otomatis dalam
    /// MB). Tampil sebagai kotak teks kecil di samping label; nilai di luar rentang
    /// ditolak dan dikembalikan ke nilai semula. Boleh dilengkapi pilihan cepat
    /// supaya nilai yang sering dipakai tidak perlu diketik.
    /// </summary>
    public class SettingRowAngkaVM : ObservableObject
    {
        private readonly Action<int> _persist;
        private readonly int _min;
        private readonly int _maks;
        private string _teks;

        public string Judul { get; }
        public string Keterangan { get; }

        /// <summary>Tombol nilai siap pakai (kosong = baris hanya punya kotak isian).</summary>
        public ObservableCollection<PilihanAngkaVM> PilihanCepat { get; } = new();

        public bool AdaPilihanCepat => PilihanCepat.Count > 0;

        public string TeksNilai
        {
            get => _teks;
            set
            {
                var baru = value?.Trim() ?? string.Empty;
                if (!SetProperty(ref _teks, baru)) return;

                if (int.TryParse(baru, out var angka) && angka >= _min && angka <= _maks)
                {
                    try { _persist(angka); } catch { /* preferensi non-kritis */ }
                }
                else
                {
                    // Kembalikan ke nilai tersimpan bila bukan angka valid.
                    SetProperty(ref _teks, _persistGet().ToString(), nameof(TeksNilai));
                }

                PerbaruiPilihanCepatAktif();
            }
        }

        private readonly Func<int> _persistGet;

        public SettingRowAngkaVM(
            string judul, string keterangan, int nilaiAwal,
            Action<int> persist, Func<int> persistGet,
            int min = 1, int maks = 999,
            IEnumerable<(string Judul, int Nilai)>? pilihanCepat = null)
        {
            Judul = judul;
            Keterangan = keterangan;
            _teks = nilaiAwal.ToString();
            _persist = persist;
            _persistGet = persistGet;
            _min = min;
            _maks = maks;

            if (pilihanCepat != null)
            {
                foreach (var (pilihanJudul, nilai) in pilihanCepat)
                {
                    // Di luar rentang tidak mungkin dipakai — lebih baik tidak ditawarkan.
                    if (nilai < _min || nilai > _maks) continue;
                    PilihanCepat.Add(new PilihanAngkaVM(pilihanJudul, nilai, Pakai));
                }
            }

            PerbaruiPilihanCepatAktif();
        }

        /// <summary>Tekan satu pilihan cepat: kotak isian ikut berubah dan nilainya tersimpan.</summary>
        private void Pakai(int angka)
        {
            if (angka < _min || angka > _maks) return;

            if (SetProperty(ref _teks, angka.ToString(), nameof(TeksNilai)))
            {
                try { _persist(angka); } catch { /* preferensi non-kritis */ }
            }

            PerbaruiPilihanCepatAktif();
        }

        private void PerbaruiPilihanCepatAktif()
        {
            bool adaAngka = int.TryParse(_teks, out var aktif);
            foreach (var pilihan in PilihanCepat)
            {
                pilihan.Aktif = adaAngka && aktif == pilihan.Nilai;
            }
        }
    }

    /// <summary>
    /// Satu pilihan interval pemantauan data IDM (setiap 6 jam / 24 jam / hanya
    /// saat aplikasi dibuka) di halaman Pengaturan Aplikasi. Teksnya diturunkan
    /// dari <see cref="IntervalPemantauanIdmPrefs" /> supaya judul di daftar,
    /// penjelasan, dan jeda yang benar-benar dipakai pemantau tidak melenceng.
    /// </summary>
    public class PilihanIntervalIdmVM
    {
        public PilihanIntervalIdmVM(IntervalPemantauanIdm nilai)
        {
            Nilai = nilai;
            Judul = IntervalPemantauanIdmPrefs.Judul(nilai);
            Keterangan = IntervalPemantauanIdmPrefs.Keterangan(nilai);
        }

        /// <summary>Nilai yang disimpan ke preferensi aplikasi.</summary>
        public IntervalPemantauanIdm Nilai { get; }

        public string Judul { get; }

        /// <summary>Penjelasan akibat pilihan ini pada pemantauan latar.</summary>
        public string Keterangan { get; }

        public override string ToString() => Judul;
    }

    /// <summary>
    /// Satu pilihan kecepatan animasi antarmuka (Lambat/Normal/Cepat) di halaman
    /// Pengaturan Aplikasi. Semua teksnya diturunkan dari aturan di
    /// <see cref="KecepatanAnimasiPrefs"/>, sehingga pilihan kartu, penjelasan, dan
    /// durasi yang benar-benar dipakai animasi tidak bisa saling melenceng.
    /// </summary>
    public class KecepatanAnimasiPilihanVM
    {
        public KecepatanAnimasiPilihanVM(KecepatanAnimasi nilai)
        {
            Nilai = nilai;
            Judul = KecepatanAnimasiPrefs.Judul(nilai);
            FaktorTeks = KecepatanAnimasiPrefs.FaktorTeks(nilai);
            Keterangan = KecepatanAnimasiPrefs.Keterangan(nilai);
            ContohSidebar = "sidebar " + KecepatanAnimasiPrefs.ContohTeks(nilai, KecepatanAnimasiPrefs.DasarSidebar);
            ContohDropdown = "dropdown " + KecepatanAnimasiPrefs.ContohTeks(nilai, KecepatanAnimasiPrefs.DasarDropdown);
            // Glyph Segoe Fluent Icons: pelan = jam, normal = centang, cepat = kilat.
            Ikon = nilai switch
            {
                KecepatanAnimasi.Lambat => IkonMenu.Riwayat,      // \uE81C jam
                KecepatanAnimasi.Cepat => "\uE945",               // kilat ganda
                _ => "\uE73E"                                     // centang
            };
        }

        /// <summary>Nilai yang disimpan ke preferensi aplikasi.</summary>
        public KecepatanAnimasi Nilai { get; }

        public string Judul { get; }

        /// <summary>Pengali dibanding bawaan, mis. "1,9× lebih lambat dari bawaan".</summary>
        public string FaktorTeks { get; }

        /// <summary>Kapan pilihan ini cocok dipakai.</summary>
        public string Keterangan { get; }

        public string Ikon { get; }

        /// <summary>Durasi nyata transisi sidebar pada pilihan ini, mis. "sidebar 0,49 dtk".</summary>
        public string ContohSidebar { get; }

        /// <summary>Durasi nyata dropdown grup pada pilihan ini, mis. "dropdown 0,46 dtk".</summary>
        public string ContohDropdown { get; }

        /// <summary>Ringkasan durasi kedua transisi utama untuk kartu pilihan.</summary>
        public string ContohTeks => ContohSidebar + " · " + ContohDropdown;

        public override string ToString() => Judul;
    }

    /// <summary>
    /// Satu bagian pada navigasi halaman Pengaturan Aplikasi. Hanya bagian yang
    /// aktif yang ditampilkan, sehingga halaman tetap ringkas walau pengaturannya
    /// sudah banyak.
    /// </summary>
    public class PengaturanSectionVM : ObservableObject
    {
        private bool _aktif;

        /// <summary>Kunci bagian; sama dengan Tag kartu untuk fokus dari statusbar.</summary>
        public string Key { get; }
        public string Judul { get; }
        public string Ikon { get; }
        public string Ringkasan { get; }

        public bool Aktif
        {
            get => _aktif;
            set => SetProperty(ref _aktif, value);
        }

        public PengaturanSectionVM(string key, string judul, string ikon, string ringkasan)
        {
            Key = key;
            Judul = judul;
            Ikon = ikon;
            Ringkasan = ringkasan;
        }
    }

    /// <summary>
    /// Halaman Pengaturan Aplikasi — pusat preferensi perilaku aplikasi
    /// (berbeda dari Pengaturan Surat yang mengelola data desa).
    ///
    /// Isinya dikelompokkan menjadi beberapa bagian dengan navigasi sendiri di
    /// sisi kiri, dan setiap tombol geser tersimpan langsung saat digeser.
    /// </summary>
    public partial class PengaturanAplikasiViewModel : ObservableObject
    {
        private readonly IServiceProvider _provider;
        private readonly NavigationService _navigation;
        private readonly ILogger<PengaturanAplikasiViewModel> _logger;
        private readonly GoogleDriveService? _googleDrive;
        private readonly AppConfig? _appConfig;
        private readonly IMessageService? _pesan;

        public ObservableCollection<SettingRowVM> Rows { get; } = new();

        /// <summary>Baris pengaturan bernilai angka (mis. batas ukuran MB).</summary>
        public ObservableCollection<SettingRowAngkaVM> RowsAngka { get; } = new();


    }
}
