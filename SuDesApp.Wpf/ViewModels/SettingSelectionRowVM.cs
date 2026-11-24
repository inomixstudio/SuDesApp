using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Satu baris pengaturan berupa pilihan dari daftar teks (mis. bahasa tampilan).
    /// Nilai yang tersimpan adalah kode pilihan (<c>id</c> / <c>en</c>), sedangkan
    /// teks yang ditampilkan dipetakan dari <see cref="Lokalisasi"/> supaya baris
    /// ini ikut berpindah bahasa bila pengguna mengganti bahasa.
    /// </summary>
    public class SettingSelectionRowVM : ObservableObject
    {
        private string _nilai;

        /// <summary>Kunci judul yang dipetakan oleh <see cref="Lokalisasi"/>.</summary>
        public string JudulKunci { get; }

        /// <summary>Judul yang sudah dipetakan ke bahasa tampilan saat ini.</summary>
        public string Judul => Lokalisasi.Get(JudulKunci);

        /// <summary>Keterangan yang dipetakan oleh <see cref="Lokalisasi"/>.</summary>
        public string KeteranganKunci { get; }

        /// <summary>Keterangan yang sudah dipetakan ke bahasa tampilan saat ini.</summary>
        public string Keterangan => Lokalisasi.Get(KeteranganKunci);

        /// <summary>Dipanggil setiap kali pilihan berubah, agar halaman menyimpan nilainya.</summary>
        public Action<string>? Diubah { get; set; }

        /// <summary>Kode pilihan yang sedang terpilih ("id" atau "en").</summary>
        public string Nilai
        {
            get => _nilai;
            set
            {
                if (SetProperty(ref _nilai, value)) Diubah?.Invoke(value);
            }
        }

        /// <summary>Daftar pilihan yang tersedia, teksnya sudah dipetakan ke bahasa saat ini.</summary>
        public IEnumerable<PilihanItem> Pilihan => Lokalisasi.BahasaPilihan(Nilai);

        public SettingSelectionRowVM(
            string judulKunci, string keteranganKunci, string nilaiAwal)
        {
            JudulKunci = judulKunci ?? throw new ArgumentNullException(nameof(judulKunci));
            KeteranganKunci = keteranganKunci ?? throw new ArgumentNullException(nameof(keteranganKunci));
            _nilai = nilaiAwal ?? "id";
        }
    }

    /// <summary>
    /// Salah satu pilihan dalam <see cref="SettingSelectionRowVM"/>. <see cref="Kode"/>
    /// dipakai <c>SelectedValuePath</c> di ComboBox, <see cref="Teks"/> yang
    /// menggantikan kode di layar.
    /// </summary>
    public sealed class PilihanItem
    {
        public string Kode { get; }

        public string Teks { get; }

        public PilihanItem(string kode, string teks)
        {
            Kode = kode ?? throw new ArgumentNullException(nameof(kode));
            Teks = teks ?? throw new ArgumentNullException(nameof(teks));
        }

        public override string ToString() => Teks;
    }
}
