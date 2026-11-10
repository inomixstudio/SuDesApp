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
        // Pratinjau kop surat di layar (mengikuti isian, belum perlu disimpan)
        // =================================================================

        /// <summary>
        /// Lebar "kertas" pratinjau di layar (px). Dipakai sebagai acuan skala: ukuran
        /// huruf kop di PDF (point) dikalikan lebar ini dibagi lebar halaman, sehingga
        /// perbandingan besar-kecil huruf pada kop tetap tergambar apa adanya.
        /// </summary>
        public const double LebarKertasPratinjau = 600.0;

        private bool _pratinjauTerbuka = true;
        private System.Windows.Media.ImageSource? _pratinjauKopGambar;
        private CancellationTokenSource? _kopCetakBatal;
        private string _kopCetakKunci = string.Empty;
        private string _pratinjauInfoKertas = string.Empty;
        private string _pratinjauInfoCamat = string.Empty;
        private string _pratinjauInfoSurel = string.Empty;
        private string _pratinjauCatatanBersih = string.Empty;
        private string _pratinjauStatus = string.Empty;
        private bool _pratinjauSesuaiTersimpan = true;
        private int _pratinjauKolomPejabat = 2;

        /// <summary>Blok tanda tangan pejabat pada pratinjau (Kepala Desa, Sekretaris Desa, Camat).</summary>
        public ObservableCollection<PratinjauPejabatViewModel> PratinjauPejabat { get; } = new();

        /// <summary>Jumlah kolom blok pejabat: dua, atau tiga bila Pejabat Kecamatan diisi.</summary>
        public int PratinjauKolomPejabat
        {
            get => _pratinjauKolomPejabat;
            private set => SetProperty(ref _pratinjauKolomPejabat, value);
        }

        /// <summary>Lebar kertas pratinjau (px) — acuan skala yang sama dengan perhitungan huruf.</summary>
        public double PratinjauLebarKertas => LebarKertasPratinjau;

        /// <summary>
        /// Kop surat sebagai gambar hasil mesin PDF — logo, baris kop, garis ganda,
        /// dan jaraknya sama persis dengan yang tercetak. Bukan gambar ulang di XAML.
        /// </summary>
        public System.Windows.Media.ImageSource? PratinjauKopGambar
        {
            get => _pratinjauKopGambar;
            private set => SetProperty(ref _pratinjauKopGambar, value);
        }

        /// <summary>Keterangan surel kop: dipakai/dikosongkan, dan warnanya saat dicetak.</summary>
        public string PratinjauInfoSurel
        {
            get => _pratinjauInfoSurel;
            private set => SetProperty(ref _pratinjauInfoSurel, value ?? string.Empty);
        }

        /// <summary>Keterangan ukuran kertas dan asal logo untuk pratinjau.</summary>
        public string PratinjauInfoKertas
        {
            get => _pratinjauInfoKertas;
            private set => SetProperty(ref _pratinjauInfoKertas, value ?? string.Empty);
        }

        /// <summary>Keterangan blok camat: dipakai pada surat, atau dikosongkan.</summary>
        public string PratinjauInfoCamat
        {
            get => _pratinjauInfoCamat;
            private set => SetProperty(ref _pratinjauInfoCamat, value ?? string.Empty);
        }

        /// <summary>Catatan bila kata pengulang ("Desa", "Kecamatan") otomatis dibuang dari kop.</summary>
        public string PratinjauCatatanBersih
        {
            get => _pratinjauCatatanBersih;
            private set
            {
                if (SetProperty(ref _pratinjauCatatanBersih, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(AdaPratinjauCatatanBersih));
                }
            }
        }

        public bool AdaPratinjauCatatanBersih => _pratinjauCatatanBersih.Length > 0;

        /// <summary>Keadaan pratinjau: sudah sesuai data tersimpan atau masih isian di layar.</summary>
        public string PratinjauStatus
        {
            get => _pratinjauStatus;
            private set => SetProperty(ref _pratinjauStatus, value ?? string.Empty);
        }

        /// <summary>True bila isian sama dengan data tersimpan (lencana pratinjau berwarna aman).</summary>
        public bool PratinjauSesuaiTersimpan
        {
            get => _pratinjauSesuaiTersimpan;
            private set => SetProperty(ref _pratinjauSesuaiTersimpan, value);
        }

        /// <summary>Pratinjau bisa disembunyikan agar isian tetap lega di layar pendek.</summary>
        public bool PratinjauTerbuka
        {
            get => _pratinjauTerbuka;
            private set
            {
                if (SetProperty(ref _pratinjauTerbuka, value))
                {
                    OnPropertyChanged(nameof(PratinjauToggleTeks));
                }
            }
        }

        public string PratinjauToggleTeks => _pratinjauTerbuka ? "Sembunyikan pratinjau" : "Tampilkan pratinjau";

        public RelayCommand PratinjauToggleCommand { get; }

        private void TogglePratinjau() => PratinjauTerbuka = !PratinjauTerbuka;

        /// <summary>
        /// Susun ulang seluruh isi pratinjau dari isian yang sedang di layar. Dipanggil
        /// setiap kali isian berubah supaya pengguna melihat akibatnya seketika —
        /// sebelum data desa disimpan.
        /// </summary>
        public void PerbaruiPratinjau()
        {
            var desa = DariForm();
            var kop = PratinjauKopSurat.Susun(desa);

            PratinjauPejabat.Clear();
            PratinjauPejabat.Add(BlokPejabat(kop.KepalaDesa));
            PratinjauPejabat.Add(BlokPejabat(kop.SekretarisDesa));
            if (kop.Camat != null)
            {
                PratinjauPejabat.Add(BlokPejabat(kop.Camat));
            }

            PratinjauKolomPejabat = kop.Camat == null ? 2 : 3;

            string? logo = PengaturanCetak.JalurLogoEfektif();
            PerbaruiKopCetak(desa, logo);

            string asalLogo = logo == null
                ? "tanpa gambar logo"
                : string.IsNullOrWhiteSpace(PengaturanCetak.GetJalurLogo())
                    ? "logo bawaan aplikasi"
                    : $"logo Anda ({System.IO.Path.GetFileName(logo)})";
            PratinjauInfoKertas = $"Kertas {PengaturanCetak.LabelUkuranKertas(_ukuranKertas)} · {asalLogo}";

            PratinjauInfoCamat = kop.Camat == null
                ? "Pejabat Kecamatan dikosongkan — blok camat tidak dicetak pada surat."
                : "Pejabat Kecamatan terisi — blok camat ikut dicetak pada surat yang memintanya.";

            // Surel bersifat opsional: keterangannya menjelaskan kedua keadaan, bukan
            // menyuruh pengguna mengisinya.
            PratinjauInfoSurel = kop.Surel.Length == 0
                ? "Email desa belum diisi — kop dicetak tanpa email (kolom ini opsional)."
                : $"Email {kop.Surel} dicetak berwarna biru pada baris sendiri di bawah nama kabupaten; " +
                  "kosongkan kolomnya bila kantor desa tidak memakai email surat.";

            PratinjauCatatanBersih = CatatanBersih(desa);
            PratinjauSesuaiTersimpan = !HasChanges;
            PratinjauStatus = HasChanges
                ? "Masih isian di layar — belum disimpan"
                : "Sesuai data yang sudah disimpan";
        }

        /// <summary>
        /// Perbarui gambar kop dari isian yang sedang di layar. Pengetikan memanggil
        /// metode ini pada setiap huruf, jadi hasilnya ditunda sebentar dan hanya
        /// dirender bila ada isian yang benar-benar memengaruhi kop.
        /// </summary>
        private void PerbaruiKopCetak(DesaData desa, string? logoPath)
        {
            string kunci = string.Join('\u001f',
                desa.NamaDesa, desa.JenisLembaga, desa.Kecamatan, desa.Kabupaten, desa.JenisKabupaten,
                desa.Alamat, desa.Kodepos, desa.Email, desa.NipKepalaDesa, logoPath ?? "-",
                _ukuranKertas.ToString());
            if (string.Equals(kunci, _kopCetakKunci, StringComparison.Ordinal)) return;
            _kopCetakKunci = kunci;

            BatalkanKopCetakLama();
            var batal = _kopCetakBatal = new CancellationTokenSource();
            _ = MuatKopCetakAsync(desa, logoPath, _ukuranKertas, batal.Token);
        }

        private void BatalkanKopCetakLama()
        {
            var lama = _kopCetakBatal;
            _kopCetakBatal = null;
            if (lama is null) return;
            try { lama.Cancel(); } catch { /* sudah selesai */ }
            // Dispose sekarang aman: tugas MuatKopCetakAsync tidak lagi
            // membaca IsCancellationRequested setelah Cancel (await ditunda).
            try { lama.Dispose(); } catch { /* sudah dibuang */ }
        }

        private async Task MuatKopCetakAsync(
            DesaData desa, string? logoPath, UkuranKertasSurat ukuran, CancellationToken batal)
        {
            try
            {
                // Ketikan cepat tidak perlu merender setiap huruf: tunggu sampai diam.
                await Task.Delay(350, batal);
                var gambar = await KopSuratPratinjauService.BuatAsync(desa, logoPath, ukuran, batal);
                if (!batal.IsCancellationRequested) PratinjauKopGambar = gambar;
            }
            catch (OperationCanceledException)
            {
                // Isian berubah lagi sebelum selesai; percobaan terbaru yang dipakai.
            }
            catch (Exception ex)
            {
                // Pratinjau hanya bantuan tampilan: isian tetap bisa disimpan walau
                // gambar kop gagal dibuat (mis. berkas logo hilang).
                _logger.LogWarning(ex, "Gagal membuat pratinjau kop surat.");
            }
        }

        private static PratinjauPejabatViewModel BlokPejabat(BlokPejabatPratinjau blok)
            => new(blok.Judul, blok.Jabatan, blok.Nama, string.Join("\n", blok.BarisTambahan), blok.Terisi);

        /// <summary>
        /// Catatan bila pengguna mengetik kata pengulang di depan nama wilayah
        /// (mis. "Kecamatan Tempuran"): kop tetap dicetak sekali saja. Pemeriksaannya
        /// memakai aturan pembersih yang sama dengan generator PDF.
        /// </summary>
        private static string CatatanBersih(DesaData desa)
        {
            var kolom = new List<string>();
            if (AdaKataPengulang(desa.NamaDesa, "Desa")) kolom.Add("Nama Desa");
            if (AdaKataPengulang(desa.Kecamatan, "Kecamatan", "Kec.")) kolom.Add("Kecamatan");
            if (AdaKataPengulang(desa.Kabupaten, "Kabupaten", "Kab.")) kolom.Add("Kabupaten");

            return kolom.Count == 0
                ? string.Empty
                : $"Kata pengulang pada {string.Join(", ", kolom)} otomatis dibuang di kop dan tanda tangan " +
                  "(mis. kolom Kecamatan tetap dicetak sebagai “KECAMATAN ...”, tanpa kata ganda).";
        }

        private static bool AdaKataPengulang(string? nilai, string kataPengulang, string? singkatan = null)
        {
            string asli = (nilai ?? string.Empty).Trim();
            string bersih = KopSurat.NamaWilayah(asli, kataPengulang, singkatan);
            return bersih.Length > 0 && !string.Equals(bersih, asli, StringComparison.Ordinal);
        }


    }
}
