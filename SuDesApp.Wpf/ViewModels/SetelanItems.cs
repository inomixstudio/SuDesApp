using System.Windows;
using SuDesApp.GeneratorPdf;
using SuDesApp.Wpf.Mvvm;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Satu bagian pada navigasi halaman Pengaturan Surat (Data Desa, Pejabat Desa,
    /// Pejabat Kecamatan, Cetak &amp; Logo). Bagian yang sedang dibuka ditandai
    /// <see cref="Aktif"/>, dan <see cref="Lencana"/> meringkas keadaannya supaya
    /// pengguna langsung tahu bagian mana yang masih perlu diisi.
    /// </summary>
    public class BagianSetelanItemViewModel : ObservableObject
    {
        private bool _aktif;
        private string _lencana = string.Empty;
        private bool _lencanaPeringatan;

        public BagianSetelanItemViewModel(int nomor, string judul, string keterangan)
        {
            Nomor = nomor;
            Judul = judul;
            Keterangan = keterangan;
        }

        public int Nomor { get; }

        public string Judul { get; }

        /// <summary>Keterangan singkat isi bagian ini.</summary>
        public string Keterangan { get; }

        public string NomorTeks => Nomor.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>Bagian yang sedang ditampilkan di sebelah kanan navigasi.</summary>
        public bool Aktif
        {
            get => _aktif;
            set => SetProperty(ref _aktif, value);
        }

        /// <summary>Ringkasan keadaan bagian (mis. \"2 kolom masih contoh\" atau \"Lengkap\").</summary>
        public string Lencana
        {
            get => _lencana;
            set
            {
                if (SetProperty(ref _lencana, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(AdaLencana));
                }
            }
        }

        public bool AdaLencana => _lencana.Length > 0;

        /// <summary>True bila lencana menandakan hal yang perlu diperbaiki (warna peringatan).</summary>
        public bool LencanaPeringatan
        {
            get => _lencanaPeringatan;
            set => SetProperty(ref _lencanaPeringatan, value);
        }

        /// <summary>Nomor bagian dalam bentuk teks untuk pengujian/bantuan.</summary>
        public override string ToString() => $"{Nomor}. {Judul}";
    }

    /// <summary>
    /// Satu blok tanda tangan pejabat pada pratinjau (Kepala Desa, Sekretaris Desa,
    /// atau Camat) — memakai jabatan dan nama persis seperti yang dicetak surat.
    /// </summary>
    public sealed class PratinjauPejabatViewModel
    {
        public PratinjauPejabatViewModel(
            string judul, string jabatan, string nama, string barisTambahan, bool terisi)
        {
            Judul = judul ?? string.Empty;
            Jabatan = jabatan ?? string.Empty;
            Nama = nama ?? string.Empty;
            BarisTambahan = barisTambahan ?? string.Empty;
            Terisi = terisi;
        }

        public string Judul { get; }

        /// <summary>Baris jabatan untuk dicetak (boleh dua baris, dipisah baris baru).</summary>
        public string Jabatan { get; }

        public string Nama { get; }

        /// <summary>Baris tambahan di bawah nama (golongan, NIP) — boleh kosong.</summary>
        public string BarisTambahan { get; }

        /// <summary>False bila kolom namanya masih kosong (surat mencetak garis bawah).</summary>
        public bool Terisi { get; }

        public bool AdaBarisTambahan => BarisTambahan.Length > 0;

        /// <summary>Benar bila blok ini perlu ditandai "belum diisi" pada pratinjau.</summary>
        public bool Kosong => !Terisi;

        public override string ToString() => $"{Judul}: {Nama}";
    }
}
