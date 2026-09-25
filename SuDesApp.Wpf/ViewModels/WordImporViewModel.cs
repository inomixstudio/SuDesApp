using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using SuDesApp.Data.Models;
using SuDesApp.Services;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Satu bagian surat pada dialog \"Buat dari File Word\": dapat dicentang dan
    /// namanya diubah sebelum disimpan menjadi template.
    /// </summary>
    public class WordImporBarisViewModel : ObservableObject
    {
        private bool _pilih;
        private string _nama;
        private BitmapSource? _gambarPratinjau;
        private string _keteranganPratinjau = string.Empty;
        private string _pesanPratinjau = string.Empty;
        private bool _sedangPratinjau;

        public WordImporBarisViewModel(WordSuratKandidat kandidat, bool pilih, string nama)
        {
            Kandidat = kandidat ?? throw new ArgumentNullException(nameof(kandidat));
            _pilih = pilih;
            _nama = nama ?? string.Empty;

            PratinjauCommand = new AsyncRelayCommand(PratinjauAsync, () => !SedangPratinjau);
        }

        public WordSuratKandidat Kandidat { get; }

        /// <summary>Dipanggil setiap kali kotak centang berubah (memperbarui ringkasan).</summary>
        public Action? PilihBerubah { get; set; }

        /// <summary>Permintaan pratinjau dari baris ini (disediakan dialog).</summary>
        public Func<WordImporBarisViewModel, Task>? MintaPratinjau { get; set; }

        /// <summary>Bagian ini disimpan menjadi template surat.</summary>
        public bool Pilih
        {
            get => _pilih;
            set
            {
                if (SetProperty(ref _pilih, value))
                {
                    PilihBerubah?.Invoke();
                }
            }
        }

        /// <summary>Nama template yang akan dibuat (boleh diubah pengguna).</summary>
        public string Nama
        {
            get => _nama;
            set => SetProperty(ref _nama, value);
        }

        public string Keterangan => Kandidat.Keterangan;

        /// <summary>Ringkasan isi: judul, jumlah baris, dan kolom isian yang dikenali.</summary>
        public string Ringkasan =>
            $"{Kandidat.JumlahBaris} baris • {Kandidat.JumlahKolom} kolom isian • {Kandidat.Definisi.RingkasanSusunan}";

        /// <summary>Cuplikan awal isi surat supaya pengguna bisa memastikan bagiannya.</summary>
        public string Cuplikan => Kandidat.Cuplikan;

        // ===== Pratinjau singkat (halaman pertama hasil cetak) =====

        public AsyncRelayCommand PratinjauCommand { get; }

        /// <summary>Gambar halaman pertama surat contoh; null bila belum diminta.</summary>
        public BitmapSource? GambarPratinjau
        {
            get => _gambarPratinjau;
            private set
            {
                if (SetProperty(ref _gambarPratinjau, value))
                {
                    OnPropertyChanged(nameof(AdaPratinjau));
                }
            }
        }

        public bool AdaPratinjau => _gambarPratinjau != null;

        /// <summary>Keterangan "Halaman 1 dari N" di bawah gambar.</summary>
        public string KeteranganPratinjau
        {
            get => _keteranganPratinjau;
            private set => SetProperty(ref _keteranganPratinjau, value);
        }

        /// <summary>Keterangan saat pratinjau gagal dibuat (ditampilkan di kartu).</summary>
        public string PesanPratinjau
        {
            get => _pesanPratinjau;
            private set
            {
                if (SetProperty(ref _pesanPratinjau, value))
                {
                    OnPropertyChanged(nameof(AdaPesanPratinjau));
                }
            }
        }

        public bool AdaPesanPratinjau => _pesanPratinjau.Length > 0;

        /// <summary>Sedang membuat pratinjau (tombol dinonaktifkan & diberi keterangan).</summary>
        public bool SedangPratinjau
        {
            get => _sedangPratinjau;
            private set
            {
                if (SetProperty(ref _sedangPratinjau, value))
                {
                    PratinjauCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string TeksTombolPratinjau => AdaPratinjau ? "Perbarui Pratinjau" : "Pratinjau";

        /// <summary>Minta pratinjau singkat bagian ini (dipakai tombol Pratinjau).</summary>
        public Task PratinjauAsync() => MintaPratinjau?.Invoke(this) ?? Task.CompletedTask;

        /// <summary>Pratinjau berhasil dibuat untuk bagian ini.</summary>
        public void TetapkanPratinjau(BitmapSource? gambar, string keterangan)
        {
            GambarPratinjau = gambar;
            KeteranganPratinjau = keterangan ?? string.Empty;
            PesanPratinjau = string.Empty;
            OnPropertyChanged(nameof(TeksTombolPratinjau));
        }

        /// <summary>Pratinjau gagal (mis. template belum bisa dicetak) — pesan ditampilkan.</summary>
        public void TetapkanGagalPratinjau(string pesan)
        {
            GambarPratinjau = null;
            KeteranganPratinjau = string.Empty;
            PesanPratinjau = pesan ?? string.Empty;
            OnPropertyChanged(nameof(TeksTombolPratinjau));
        }

        public void SedangMemuatPratinjau(bool sedang) => SedangPratinjau = sedang;
    }

    /// <summary>
    /// Data dialog pemilihan bagian surat ketika satu berkas Word memuat lebih dari
    /// satu bentuk surat (beberapa halaman/bagian).
    /// </summary>
    public class WordImporViewModel : ObservableObject
    {
        private readonly Func<TemplateSuratKustom, Task<PratinjauTemplateSurat?>>? _pratinjau;

        /// <param name="pratinjau">
        /// Pembuat pratinjau singkat (halaman pertama) untuk satu bagian surat. Bila null,
        /// tombol pratinjau tetap ada tetapi melaporkan bahwa pratinjau tidak tersedia.
        /// </param>
        public WordImporViewModel(
            WordSuratImpor hasil,
            IEnumerable<string>? namaTerpakai,
            Func<TemplateSuratKustom, Task<PratinjauTemplateSurat?>>? pratinjau = null)
        {
            if (hasil == null) throw new ArgumentNullException(nameof(hasil));

            _pratinjau = pratinjau;
            NamaBerkas = hasil.NamaBerkas;
            Peringatan = hasil.Peringatan?.ToArray() ?? Array.Empty<string>();

            // Nama bawaan: judul surat yang dikenali, atau nama berkas + nomor bagian.
            // Nama selalu dijaga tetap unik agar tidak tertukar pada daftar template.
            var dipakai = new HashSet<string>(
                (namaTerpakai ?? Enumerable.Empty<string>())
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(n => n.Trim()),
                StringComparer.OrdinalIgnoreCase);

            Baris = new ObservableCollection<WordImporBarisViewModel>();
            foreach (var kandidat in hasil.Kandidat)
            {
                string dasar = string.IsNullOrWhiteSpace(kandidat.Judul)
                    ? kandidat.Definisi.Nama
                    : kandidat.Judul;
                string nama = TemplateSuratWordImporService.NamaUnik(dasar, dipakai);
                dipakai.Add(nama);

                var baris = new WordImporBarisViewModel(kandidat, pilih: true, nama: nama);
                baris.PilihBerubah = PerbaruiRingkasan;
                baris.MintaPratinjau = PratinjauBagianAsync;
                Baris.Add(baris);
            }
        }

        public string NamaBerkas { get; }

        public ObservableCollection<WordImporBarisViewModel> Baris { get; }

        public IReadOnlyList<string> Peringatan { get; }

        public bool AdaPeringatan => Peringatan.Count > 0;

        public string TeksPeringatan => string.Join(Environment.NewLine, Peringatan);

        /// <summary>Keterangan singkat di atas daftar bagian.</summary>
        public string Keterangan =>
            $"Berkas ini berisi {Baris.Count} bagian surat (setiap halaman/bagian Word menjadi satu template). " +
            "Pilih yang ingin dijadikan template, lalu periksa namanya.";

        public int JumlahDipilih => Baris.Count(b => b.Pilih);

        public bool BisaBuat => JumlahDipilih > 0;

        public string RingkasanPilih => JumlahDipilih == Baris.Count
            ? $"Seluruh {Baris.Count} bagian akan dibuat menjadi template surat."
            : $"{JumlahDipilih} dari {Baris.Count} bagian akan dibuat menjadi template surat.";

        public string TeksTombolBuat => JumlahDipilih == 1
            ? "Buat 1 Template"
            : $"Buat {JumlahDipilih} Template";

        /// <summary>Centang/hilangkan centang seluruh bagian sekaligus.</summary>
        public void PilihSemua(bool pilih)
        {
            foreach (var baris in Baris)
            {
                baris.Pilih = pilih;
            }
            PerbaruiRingkasan();
        }

        /// <summary>
        /// Definisi template yang dipilih, lengkap dengan nama terakhir yang diketik
        /// pengguna (dijaga tetap unik terhadap daftar template yang sudah ada).
        /// </summary>
        public List<(string Nama, TemplateSuratKustom Template)> Terpilih(IEnumerable<string>? namaTerpakai = null)
        {
            var dipakai = new HashSet<string>(
                (namaTerpakai ?? Enumerable.Empty<string>())
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(n => n.Trim()),
                StringComparer.OrdinalIgnoreCase);

            var hasil = new List<(string, TemplateSuratKustom)>();
            foreach (var baris in Baris.Where(b => b.Pilih))
            {
                string nama = TemplateSuratWordImporService.NamaUnik(baris.Nama, dipakai);
                dipakai.Add(nama);
                baris.Kandidat.Definisi.Nama = nama;
                hasil.Add((nama, baris.Kandidat.Definisi));
            }
            return hasil;
        }

        /// <summary>
        /// Buat pratinjau singkat satu bagian surat, lengkap dengan penanganan gagal supaya
        /// kartu tetap menampilkan pesan yang jelas (dialog tidak pernah ikut gagal).
        /// </summary>
        private async Task PratinjauBagianAsync(WordImporBarisViewModel baris)
        {
            if (_pratinjau == null)
            {
                baris.TetapkanGagalPratinjau("Pratinjau belum tersedia untuk bagian ini.");
                return;
            }

            baris.SedangMemuatPratinjau(true);
            try
            {
                var hasil = await _pratinjau(baris.Kandidat.Definisi);
                if (hasil?.Gambar == null)
                {
                    baris.TetapkanGagalPratinjau(
                        "Pratinjau tidak dapat dibuat. Periksa dulu susunan bagian ini di wizard.");
                    return;
                }

                baris.TetapkanPratinjau(hasil.Gambar, hasil.Keterangan);
            }
            catch (Exception ex)
            {
                baris.TetapkanGagalPratinjau("Pratinjau gagal dibuat: " + ex.Message);
            }
            finally
            {
                baris.SedangMemuatPratinjau(false);
            }
        }

        private void PerbaruiRingkasan()
        {
            OnPropertyChanged(nameof(JumlahDipilih));
            OnPropertyChanged(nameof(BisaBuat));
            OnPropertyChanged(nameof(RingkasanPilih));
            OnPropertyChanged(nameof(TeksTombolBuat));
            OnPropertyChanged(nameof(TeksTombolPilih));
        }

        /// <summary>Balikkan pilihan seluruh bagian.</summary>
        public void BalikPilihSemua() => PilihSemua(JumlahDipilih != Baris.Count);

        /// <summary>Label tombol centang-semua (berubah sesuai keadaan pilihan).</summary>
        public string TeksTombolPilih => JumlahDipilih == Baris.Count ? "Kosongkan Pilihan" : "Pilih Semua";
    }
}
