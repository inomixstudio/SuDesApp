using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using SuDesApp.ControlSurat;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class LanggananViewModel : ObservableObject
    {
        // =======================================================================
        // Server lisensi lokal (pusat administrasi di komputer ini).
        // =======================================================================
        // Halaman ini KENDALI yang terlihat operator; prosesnya milik
        // ServerLisensiLokal (Core) dan siklus hidupnya milik aplikasi
        // (dinyalakan saat dibuka, dimatikan saat keluar). Pilihan "nyalakan
        // bersama aplikasi" disimpan sebagai preferensi — App membaca nilai yang
        // sama pada pembukaan berikutnya, jadi tidak ada dua sumber kebenaran.
        //
        // Pusat Administrasi dibuka dari sini pada alamat yang SAMA dengan alamat
        // langganan: itulah yang membuat langganan yang dikelola operator
        // (aktivasi, perpanjangan, tandai lunas) adalah langganan yang diperiksa
        // halaman ini — bukan dua server yang berbeda.

        private readonly ServerLisensiLokal? _serverLokal;
        private readonly NavigationService? _navigasi;

        /// <summary>
        /// True bila blok server lokal ditampilkan: hanya pada build yang
        /// alamatnya boleh diganti (server uji lokal). Rilis yang terkunci pada
        /// server produksi tidak menjalankan server lisensi di komputer desa.
        /// </summary>
        public bool TampilServerLokal => _serverLokal is not null && !_lisensi.UrlServerTerkunci;

        /// <summary>
        /// Pilihan "jalankan server lisensi di komputer ini". Disimpan sebagai
        /// preferensi supaya pilihan ini berlaku juga pada pembukaan berikutnya
        /// (App yang menyalakannya), lalu dikerjakan sekarang juga.
        /// </summary>
        public bool ServerLokalAktif
        {
            get => AppPreferenceStore.IsServerLisensiLokalAktif();
            set
            {
                if (value == AppPreferenceStore.IsServerLisensiLokalAktif()) return;

                AppPreferenceStore.SetServerLisensiLokalAktif(value);
                OnPropertyChanged();
                SegarkanStatusServerLokal();

                if (value) _ = NyalakanServerLokalAsync();
                else HentikanServerLokal();
            }
        }

        /// <summary>True selama proses server yang dinyalakan aplikasi masih hidup.</summary>
        public bool ServerLokalBerjalan => _serverLokal?.Berjalan == true;

        /// <summary>Kalimat status server lokal di bawah kotak centangnya.</summary>
        public string ServerLokalStatus
        {
            get
            {
                if (_serverLokal is null) return "Server lokal tidak tersedia di aplikasi ini.";
                if (_serverLokal.Berjalan) return "Berjalan di " + _serverLokal.Alamat;

                return AppPreferenceStore.IsServerLisensiLokalAktif()
                    ? "Belum berjalan — periksa pesan kegagalan di bawah."
                    : "Tidak berjalan.";
            }
        }

        /// <summary>Di mana data langganan server lokal disimpan — dan bahwa data itu menetap.</summary>
        public string ServerLokalCatatan =>
            "Server ini yang melayani aktivasi dan perpanjangan di halaman ini, dan Pusat Administrasi " +
            "membuka alamat yang sama. Data langganan tersimpan di " +
            OpsiServerLokal.FolderDataBawaan() + " sehingga tetap ada setelah server dimatikan.";

        /// <summary>
        /// Tombol Pusat Administrasi hanya untuk sesi akun pusat — aturan yang
        /// sama dengan menu sidebar (docs/policy-peran-pengguna.md). Halaman
        /// admin sendiri tetap meminta kredensial operator server.
        /// </summary>
        public bool TampilPusatAdministrasi => _navigasi is not null && Utilities.AkunPusat.SesiIniPusat;

        public AsyncRelayCommand BukaPusatAdministrasiCommand { get; }

        private async Task NyalakanServerLokalAsync()
        {
            if (_serverLokal is null) return;

            StatusMessage = "Menyalakan server lisensi lokal…";
            try
            {
                if (await _serverLokal.NyalakanDanPakaiAsync(_lisensi))
                {
                    // Alamat server berubah ke server yang baru menyala: kartu
                    // paket dan chip status harus berbicara tentang server itu,
                    // bukan angka dari alamat sebelumnya.
                    UrlServer = _lisensi.UrlServer;
                    _angkaDariSumberIni = false;
                    await MuatKatalogAsync(paksa: true);

                    Beritahu(2, "Server lisensi lokal berjalan di " + _serverLokal.Alamat +
                        " — alamat ini yang dipakai halaman Langganan dan Pusat Administrasi.");
                    StatusMessage = "Server lisensi lokal berjalan.";
                }
                else
                {
                    // Gagal menyala: pilihan dikembalikan supaya kotak centang
                    // tidak menjanjikan server yang tidak ada.
                    AppPreferenceStore.SetServerLisensiLokalAktif(false);
                    OnPropertyChanged(nameof(ServerLokalAktif));
                    Beritahu(3, _serverLokal.GalatTerakhir ?? "Server lisensi lokal tidak bisa dinyalakan.");
                    StatusMessage = "Server lisensi lokal gagal dinyalakan.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal menyalakan server lisensi lokal dari halaman Langganan");
                AppPreferenceStore.SetServerLisensiLokalAktif(false);
                OnPropertyChanged(nameof(ServerLokalAktif));
                Beritahu(3, "Server lisensi lokal gagal dinyalakan: " + ex.Message);
                StatusMessage = "Server lisensi lokal gagal dinyalakan.";
            }
            finally
            {
                SegarkanStatusServerLokal();
            }
        }

        /// <summary>
        /// Matikan server lokal sekarang juga. Pilihan "nyalakan bersama
        /// aplikasi" dibiarkan seperti yang dipilih operator, sehingga server
        /// menyala lagi pada pembukaan berikutnya bila memang diinginkan.
        /// </summary>
        private void HentikanServerLokal()
        {
            try
            {
                _serverLokal?.Matikan();
                Beritahu(2, "Server lisensi lokal dimatikan. Server menyala lagi saat aplikasi dibuka bila pilihannya tetap aktif.");
                StatusMessage = "Server lisensi lokal dimatikan.";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal mematikan server lisensi lokal dari halaman Langganan");
                Beritahu(3, "Server lisensi lokal tidak bisa dimatikan: " + ex.Message);
            }
            finally
            {
                SegarkanStatusServerLokal();
            }
        }

        private void SegarkanStatusServerLokal()
        {
            OnPropertyChanged(nameof(ServerLokalBerjalan));
            OnPropertyChanged(nameof(ServerLokalStatus));
            OnPropertyChanged(nameof(ServerLokalCatatan));
        }

        /// <summary>
        /// Buka pusat administrasi DI DALAM aplikasi, pada alamat server yang
        /// sama dengan alamat langganan. Penjagaan akun pusat diulang di sini
        /// (bukan hanya lewat visibilitas tombol) supaya perintah tidak bisa
        /// dijalankan dari sesi akun desa.
        /// </summary>
        private async Task BukaPusatAdministrasiAsync()
        {
            if (_navigasi is null || !Utilities.AkunPusat.SesiIniPusat)
            {
                Beritahu(3, "Halaman pusat administrasi hanya tersedia untuk akun pusat.");
                return;
            }

            string alamat = _lisensi.AlamatPusatAdministrasi();
            if (alamat.Length == 0)
            {
                Beritahu(3, "Alamat server lisensi belum diatur, jadi pusat administrasi belum bisa dibuka.");
                return;
            }

            try
            {
                var halaman = new SuDesApp.Wpf.Views.PusatAdministrasiView();
                _navigasi.Navigate(halaman);
                await halaman.MuatAsync(alamat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka pusat administrasi {Alamat}", alamat);
                Beritahu(3, "Gagal membuka pusat administrasi: " + ex.Message);
            }
        }


    }
}
