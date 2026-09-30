using System;
using System.Windows.Input;
using SuDesApp.Data.Models;
using SuDesApp.Wpf.Mvvm;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Satu baris lampiran SK di panel: nama dan data pribadinya sudah final
    /// (diambil dari data perangkat desa + data Warga lewat NIK), sedangkan peran
    /// dan unitnya masih bisa disunting operator — peran mis. "Ketua" dan unit
    /// mis. "POSYANDU SAKURA I" menentukan judul blok pada lembar lampiran.
    /// </summary>
    public class LampiranBarisViewModel : ObservableObject
    {
        private readonly BarisLampiranSk _orang;
        private readonly Action<LampiranBarisViewModel> _hapus;
        private string _peran;
        private string _unit;

        public LampiranBarisViewModel(BarisLampiranSk orang, Action<LampiranBarisViewModel> hapus)
        {
            _orang = orang ?? throw new ArgumentNullException(nameof(orang));
            _hapus = hapus ?? throw new ArgumentNullException(nameof(hapus));
            _peran = orang.Peran;
            _unit = orang.Unit;
            HapusCommand = new RelayCommand(() => _hapus(this));
        }

        public string Nama => _orang.Nama;

        public string Nik => _orang.NIK;

        /// <summary>
        /// True bila kolom pribadi yang diambil dari data Warga ada isinya. Dipakai
        /// panel untuk memberitahu berapa orang yang datanya belum lengkap — bukan
        /// untuk melarang cetak, sebab lampiran tetap sah walau sebagian kolom kosong.
        /// </summary>
        public bool DataWargaLengkap =>
            !string.IsNullOrWhiteSpace(_orang.Pekerjaan)
            || !string.IsNullOrWhiteSpace(_orang.Agama)
            || !string.IsNullOrWhiteSpace(_orang.GolonganDarah)
            || !string.IsNullOrWhiteSpace(_orang.StatusPerkawinan);

        /// <summary>Data pribadi yang ikut tercetak, untuk diperiksa operator di panel.</summary>
        public string DataRingkas
        {
            get
            {
                var bagian = new System.Collections.Generic.List<string>();
                Tambah(bagian, "TTL", _orang.TempatTanggalLahir);
                Tambah(bagian, "kerja", _orang.Pekerjaan);
                Tambah(bagian, "agama", _orang.Agama);
                Tambah(bagian, "gol", _orang.GolonganDarah);
                Tambah(bagian, "kawin", _orang.StatusPerkawinan);
                return bagian.Count == 0
                    ? "Data warga belum lengkap — kolom kosong tercetak tanda hubung."
                    : string.Join(", ", bagian);
            }
        }

        private static void Tambah(System.Collections.Generic.List<string> bagian, string label, string? nilai)
        {
            if (!string.IsNullOrWhiteSpace(nilai))
            {
                bagian.Add($"{label}: {nilai!.Trim()}");
            }
        }

        /// <summary>Peran dalam unitnya; kosong berarti jabatannya yang dipakai.</summary>
        public string Peran
        {
            get => _peran;
            set
            {
                if (SetProperty(ref _peran, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(PeranTampil));
                }
            }
        }

        /// <summary>
        /// Unit pengelompokan (mis. satu Posyandu). Baris dengan unit yang sama
        /// dicetak dalam satu blok bernomor sendiri.
        /// </summary>
        public string Unit
        {
            get => _unit;
            set => SetProperty(ref _unit, value ?? string.Empty);
        }

        public string PeranTampil =>
            string.IsNullOrWhiteSpace(Peran) ? "—" : Peran.Trim();

        public ICommand HapusCommand { get; }

        /// <summary>Baris final untuk generator: peran/unit yang sudah disunting.</summary>
        public BarisLampiranSk KeBaris() => new()
        {
            Nama = _orang.Nama,
            NIK = _orang.NIK,
            TempatTanggalLahir = _orang.TempatTanggalLahir,
            Peran = Peran.Trim(),
            Pendidikan = _orang.Pendidikan,
            Pekerjaan = _orang.Pekerjaan,
            Agama = _orang.Agama,
            GolonganDarah = _orang.GolonganDarah,
            StatusPerkawinan = _orang.StatusPerkawinan,
            Alamat = _orang.Alamat,
            NomorHP = _orang.NomorHP,
            Unit = Unit.Trim()
        };
    }
}
