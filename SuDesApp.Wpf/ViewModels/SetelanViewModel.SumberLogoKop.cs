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
        // Sumber logo kop: bawaan / unggahan sendiri / lambang kabupaten.
        // Pilihan bersifat OPSIONAL — logo desa yang diunggah tetap yang
        // utama; lambang kabupaten hanya dipakai bila pengguna memilihnya,
        // dan pilihan otomatis hanya terjadi bila gambarnya benar-benar
        // tersedia (lihat LogoKabupaten).
        // =================================================================

        /// <summary>Benar bila sumber logo sedang "logo yang saya unggah".</summary>
        public bool SumberLogoUnggahan
        {
            get => PengaturanCetak.GetSumberLogo() == PengaturanCetak.SumberLogo.Unggahan;
            set { if (value) TerapkanSumberLogo(PengaturanCetak.SumberLogo.Unggahan); }
        }

        /// <summary>Benar bila sumber logo sedang "lambang kabupaten terpilih".</summary>
        public bool SumberLogoKabupaten
        {
            get => PengaturanCetak.GetSumberLogo() == PengaturanCetak.SumberLogo.LambangKabupaten;
            set { if (value) TerapkanSumberLogo(PengaturanCetak.SumberLogo.LambangKabupaten); }
        }

        /// <summary>
        /// Lambang kabupaten yang tersedia untuk dipilih. Hanya berisi entri bila
        /// berkasnya benar-benar ada, jadi daftar tidak pernah menawarkan lambang
        /// yang tidak bisa ditampilkan. Nilai null bila kabupaten terpilih belum
        /// punya lambang.
        /// </summary>
        public IReadOnlyList<LambangKabupatenItem> DaftarLambangKabupaten { get; private set; } = Array.Empty<LambangKabupatenItem>();

        /// <summary>Lambang kabupaten yang sedang dipilih (null bila tidak ada).</summary>
        public LambangKabupatenItem? LambangKabupatenTerpilih
        {
            get
            {
                string kode = PengaturanCetak.GetKodeLogoKabupaten();
                return DaftarLambangKabupaten.FirstOrDefault(l => string.Equals(l.Kode, kode, StringComparison.OrdinalIgnoreCase));
            }
            set
            {
                if (value == null)
                {
                    return;
                }

                PengaturanCetak.SetKodeLogoKabupaten(value.Kode);
                TerapkanSumberLogo(PengaturanCetak.SumberLogo.LambangKabupaten);
                OnPropertyChanged(nameof(LambangKabupatenTerpilih));
            }
        }

        /// <summary>Kalimat penjelas untuk pilihan lambang kabupaten saat ini.</summary>
        public string InfoLambangKabupaten
        {
            get
            {
                string nama = _pemilih.KabupatenTerpilih?.NamaLengkap ?? "kabupaten/kota terpilih";
                if (DaftarLambangKabupaten.Count == 0)
                {
                    return $"Belum ada berkas lambang untuk {nama}. Taruh berkasnya (nama = kode kabupaten, mis. 32.15.png) " +
                           "di folder LogosKabupaten, lalu buka lagi halaman ini.";
                }

                return DaftarLambangKabupaten.Count == 1
                    ? $"1 lambang tersedia untuk {nama}."
                    : $"{DaftarLambangKabupaten.Count} lambang tersedia untuk {nama}.";
            }
        }

        /// <summary>
        /// Pindahkan sumber logo dan segarkan pratinjau.
        /// Gambar kop (berat) di-debounce via PerbaruiKopCetak; teks pratinjau
        /// boleh instan — keduanya dipanggil di PerbaruiPratinjau.
        /// </summary>
        private void TerapkanSumberLogo(PengaturanCetak.SumberLogo sumber)
        {
            PengaturanCetak.SetSumberLogo(sumber);
            OnPropertyChanged(nameof(SumberLogoUnggahan));
            OnPropertyChanged(nameof(SumberLogoKabupaten));
            SegarkanLogoEfektif();
        }

        /// <summary>Segarkan jalur logo yang ditampilkan pratinjau sesuai sumber terkini.</summary>
        private void SegarkanLogoEfektif()
        {
            JalurLogo = PengaturanCetak.JalurLogoEfektif() ?? string.Empty;
            PerbaruiPratinjau();
        }

        /// <summary>
        /// Bangun daftar lambang kabupaten yang tersedia untuk kabupaten terpilih,
        /// lalu — HANYA bila ada lambang — pilih otomatis sebagai sumber logo.
        /// Otomatis ini aman karena tidak mengambil alih logo desa yang diunggah
        /// (pengguna bisa kembali kapan saja lewat pilihan sumber), dan hanya terjadi
        /// bila berkas lambangnya benar-benar ada.
        /// </summary>
        private void SegarkanLambangKabupaten(bool pilihOtomatis)
        {
            var kab = _pemilih.KabupatenTerpilih;
            var semua = LogoKabupaten.SemuaTersedia();

            DaftarLambangKabupaten = kab == null
                ? Array.Empty<LambangKabupatenItem>()
                : semua
                    .Where(kvp => string.Equals(kvp.Key.Trim(), kab.Kode.Trim(), StringComparison.OrdinalIgnoreCase))
                    .Select(kvp => new LambangKabupatenItem(kvp.Key, kvp.Value, kab.NamaLengkap))
                    .ToList();

            OnPropertyChanged(nameof(DaftarLambangKabupaten));
            OnPropertyChanged(nameof(InfoLambangKabupaten));
            OnPropertyChanged(nameof(LambangKabupatenTerpilih));

            if (pilihOtomatis && DaftarLambangKabupaten.Count > 0 && BolehPilihOtomatisLambang())
            {
                LambangKabupatenTerpilih = DaftarLambangKabupaten[0];
            }
        }

        /// <summary>
        /// Pemilihan lambang kabupaten otomatis boleh terjadi bila:
        ///   • pengguna sudah memakai lambang kabupaten (ikut berganti), atau
        ///   • pengguna belum pernah mengunggah logo desa sendiri.
        /// Logo desa yang diunggah selalu menang — lambang kabupaten bersifat
        /// alternatif, jadi pilihan otomatis tidak boleh menimpa keputusan eksplisit.
        ///
        /// Saat halaman dibuka (pilihOtomatis=false saat load, lihat
        /// SiapkanPilihanWilayahAsync), auto-select tidak terjadi lewat sini —
        /// dilakukan terpisah di akhir pemuatan wilayah supaya konsisten.
        /// </summary>
        private static bool BolehPilihOtomatisLambang()
        {
            var sumber = PengaturanCetak.GetSumberLogo();
            if (sumber == PengaturanCetak.SumberLogo.LambangKabupaten)
            {
                // Sudah memakai lambang: boleh ikut berganti mengikuti kabupaten baru.
                return true;
            }

            // Bawaan / unggahan: otomatis hanya bila belum pernah mengunggah logo.
            return string.IsNullOrWhiteSpace(PengaturanCetak.GetJalurLogo());
        }

        /// <summary>Satu entri lambang kabupaten yang tersedia untuk dipilih di UI.</summary>
        public sealed record LambangKabupatenItem(string Kode, string Jalur, string Nama);

        public AsyncRelayCommand GantiLogoCommand { get; }
        public AsyncRelayCommand PakaiLogoBawaanCommand { get; }

        /// <summary>Simpan pilihan ukuran kertas; langsung berlaku untuk PDF berikutnya.</summary>
        private void TerapkanUkuranKertas(UkuranKertasSurat ukuran)
        {
            if (_ukuranKertas == ukuran)
            {
                return;
            }

            _ukuranKertas = ukuran;
            PengaturanCetak.SetUkuranKertas(ukuran);
            OnPropertyChanged(nameof(UkuranKertasA4));
            OnPropertyChanged(nameof(UkuranKertasF4));
            PerbaruiLencanaBagian(DesaContoh.FieldContoh(DariForm()));
            _logger.LogInformation("Ukuran kertas PDF diubah menjadi {Ukuran}", ukuran);

            // Skala pratinjau dihitung dari lebar kertas, jadi pratinjau ikut disegarkan.
            PerbaruiPratinjau();
        }

        /// <summary>Muat pengaturan cetak yang tersimpan.</summary>
        private void MuatPengaturanCetak()
        {
            _ukuranKertas = PengaturanCetak.GetUkuranKertas();
            JalurLogo = PengaturanCetak.JalurLogoEfektif() ?? string.Empty;
            OnPropertyChanged(nameof(UkuranKertasA4));
            OnPropertyChanged(nameof(UkuranKertasF4));
            OnPropertyChanged(nameof(SumberLogoUnggahan));
            OnPropertyChanged(nameof(SumberLogoKabupaten));
            PerbaruiLencanaBagian(DesaContoh.FieldContoh(DariForm()));
            PerbaruiPratinjau();
        }

        /// <summary>Pilih gambar logo baru dari komputer, lalu salin ke folder data aplikasi.</summary>
        private async Task GantiLogoAsync()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Pilih gambar logo kop surat",
                Filter = "Gambar (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
                CheckFileExists = true,
                Multiselect = false,
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            string? tersimpan = PengaturanCetak.SimpanLogoPengguna(dialog.FileName);
            if (tersimpan == null)
            {
                await _messageService.ShowErrorAsync("Gambar logo gagal dipakai. Pastikan berkasnya masih ada dan bisa dibaca.");
                return;
            }

            JalurLogo = tersimpan;
            PerbaruiLencanaBagian(DesaContoh.FieldContoh(DariForm()));
            PerbaruiPratinjau();
            _logger.LogInformation("Logo kop surat diganti: {Jalur}", tersimpan);
            await _messageService.ShowInfoAsync("Logo kop surat berhasil diganti dan langsung dipakai pada surat berikutnya.");
        }

        /// <summary>Kembalikan logo kop ke logo bawaan aplikasi.</summary>
        private async Task PakaiLogoBawaanAsync()
        {
            PengaturanCetak.HapusLogoPengguna();
            JalurLogo = PengaturanCetak.JalurLogoEfektif() ?? string.Empty;
            PerbaruiLencanaBagian(DesaContoh.FieldContoh(DariForm()));

            // Pratinjau langsung memakai logo bawaan lagi.
            PerbaruiPratinjau();
            await _messageService.ShowInfoAsync("Logo kop surat dikembalikan ke logo bawaan aplikasi.");
        }

        public string NamaDesa { get => _namaDesa; set { if (SetProperty(ref _namaDesa, value)) OnFieldChanged(); } }
        public string Kecamatan { get => _kecamatan; set { if (SetProperty(ref _kecamatan, value)) OnFieldChanged(); } }
        public string Kabupaten { get => _kabupaten; set { if (SetProperty(ref _kabupaten, value)) OnFieldChanged(); } }


    }
}
