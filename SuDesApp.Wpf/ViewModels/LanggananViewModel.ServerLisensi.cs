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
        // Server lisensi — alamat dipakai semua panggilan status, katalog, dan
        // tagihan. Yang disimpan di sini hanya teks yang sedang diketik; pemilik
        // alamat yang dipakai panggilan adalah LisensiManager.
        // =======================================================================

        private string _urlServer = string.Empty;

        /// <summary>
        /// Kunci sesi: dipasang setelah server benar-benar menjawab, supaya
        /// alamat yang sudah terbukti tidak tergeser tanpa sengaja (mis. karena
        /// mengetik di kotak alamat). Pengguna tetap bisa membukanya lewat
        /// tombol Ubah alamat; kunci konfigurasi rilis tidak bisa dibuka.
        /// </summary>
        private bool _alamatTerkunciSesi;

        /// <summary>
        /// Keadaan sambungan ke server lisensi menurut panggilan terakhir yang
        /// BENAR-BENAR terjadi — bukan tebakan dari bentuk alamatnya.
        /// </summary>
        public enum KeadaanServer
        {
            /// <summary>Alamat belum diisi: belum ada yang bisa dihubungi.</summary>
            BelumDiatur,

            /// <summary>Alamat sudah diisi, tetapi belum ada panggilan yang selesai dari halaman ini.</summary>
            BelumDiuji,

            /// <summary>Server menjawab dan katalognya terbaca.</summary>
            Terhubung,

            /// <summary>Panggilan terakhir tidak sampai ke server, atau jawabannya tidak bisa dipakai.</summary>
            TidakTerjangkau
        }

        private KeadaanServer _keadaanServer = KeadaanServer.BelumDiuji;

        /// <summary>
        /// URL server lisensi yang diisi pengguna (tersimpan sebagai preferensi,
        /// awalnya diambil dari <c>LisensiManager.UrlServer</c>).
        /// </summary>
        public string UrlServer
        {
            get => _urlServer;
            set
            {
                // Kotak alamat sudah read-only saat terkunci, tetapi penjagaan
                // ini tetap perlu: perubahan bisa datang dari binding, kode, atau
                // test. Alamat yang terkunci tidak boleh berganti diam-diam.
                if (UrlServerTerkunci && !string.Equals(_urlServer, value, StringComparison.Ordinal))
                {
                    OnPropertyChanged(nameof(UrlServer));
                    return;
                }

                if (SetProperty(ref _urlServer, value))
                {
                    // Alamat yang diketik berbeda dari alamat yang terakhir diuji:
                    // keadaan lama tidak lagi menggambarkan isi kotak ini.
                    TetapkanKeadaanServer(string.IsNullOrWhiteSpace(value)
                        ? KeadaanServer.BelumDiatur
                        : KeadaanServer.BelumDiuji);
                }
            }
        }

        /// <summary>
        /// True bila alamat server tidak boleh diubah dari halaman ini: karena
        /// ditanam di konfigurasi rilis, atau karena koneksi ke alamat ini sudah
        /// terbukti pada sesi berjalan.
        /// </summary>
        public bool UrlServerTerkunci => _lisensi.UrlServerTerkunci || _alamatTerkunciSesi;

        /// <summary>Kebalikan <see cref="UrlServerTerkunci"/> untuk mengikat kontrol input.</summary>
        public bool UrlServerDapatDiubah => !UrlServerTerkunci;

        /// <summary>Tombol Ubah alamat hanya masuk akal untuk kunci sesi.</summary>
        public bool TampilBukaKunciServer => _alamatTerkunciSesi && !_lisensi.UrlServerTerkunci;

        /// <summary>Keterangan mengapa kotak alamat tidak bisa diubah.</summary>
        public string CatatanAlamatServer => !UrlServerTerkunci
            ? string.Empty
            : _lisensi.UrlServerTerkunci
                ? "Alamat server dikunci oleh konfigurasi aplikasi. Perubahan alamat hanya bisa lewat rilis baru."
                : "Alamat server dikunci karena koneksi sudah terverifikasi. Tekan Ubah alamat bila memang perlu menggantinya.";

        /// <summary>
        /// Buka kunci sesi secara sadar. Kunci dari konfigurasi rilis tidak bisa
        /// dibuka dari aplikasi.
        /// </summary>
        private void BukaKunciServer()
        {
            if (_lisensi.UrlServerTerkunci) return;
            _alamatTerkunciSesi = false;
            OnPropertyChanged(nameof(UrlServerTerkunci));
            OnPropertyChanged(nameof(UrlServerDapatDiubah));
            OnPropertyChanged(nameof(TampilBukaKunciServer));
            OnPropertyChanged(nameof(CatatanAlamatServer));
            BukaKunciServerCommand.RaiseCanExecuteChanged();
            StatusMessage = "Alamat server dibuka — ganti alamat lalu tekan Simpan & Uji.";
        }

        /// <summary>
        /// True bila chip dan pesan peringatan harus tampil — yaitu selama server
        /// BELUM terbukti menjawab (alamat kosong, belum diuji, atau tidak
        /// terjangkau).
        ///
        /// Nilainya datang dari hasil panggilan sungguhan, bukan dari kata pada
        /// alamat: alamat <c>localhost</c> yang servernya benar-benar menjawab
        /// (server uji di komputer sendiri) dihitung TERHUBUNG. Sebelumnya setiap
        /// alamat yang memuat "localhost" selalu dilabeli "Belum diatur" walau
        /// status dan pembayaran berjalan normal dari alamat itu.
        /// </summary>
        public bool ServerBelumTersambung => _keadaanServer != KeadaanServer.Terhubung;

        /// <summary>Teks chip status server di kartu Server Lisensi.</summary>
        public string ServerChipTeks => _keadaanServer switch
        {
            KeadaanServer.Terhubung => "Terhubung",
            KeadaanServer.TidakTerjangkau => "Belum terjangkau",
            KeadaanServer.BelumDiuji => "Belum diuji",
            _ => "Belum diatur"
        };

        /// <summary>
        /// Pesan pengarahan di bawah kolom alamat server. Mengikuti keadaan
        /// sambungan supaya tidak lagi menyalahkan alamat yang sebenarnya bekerja
        /// (dulu pesannya selalu berbunyi "Cek Status dan pembayaran akan gagal"
        /// selama alamatnya memuat "localhost").
        /// </summary>
        public string PesanServer => _keadaanServer switch
        {
            KeadaanServer.Terhubung =>
                "Server menjawab di alamat ini — Cek Status dan pembayaran memakai alamat yang sama.",
            KeadaanServer.TidakTerjangkau =>
                "Server tidak menjawab di alamat ini. Periksa alamatnya, koneksi internet ke server, " +
                "atau apakah servernya sedang berjalan — Cek Status dan pembayaran akan gagal sampai " +
                "server terjangkau.",
            KeadaanServer.BelumDiuji =>
                "Alamat sudah tersimpan, tetapi belum pernah dibuktikan dari halaman ini. " +
                "Tekan Cek Status atau Simpan & Uji untuk memeriksanya.",
            _ => "Server belum diatur — isi alamat hasil pemasangan server lisensi, lalu tekan Simpan & Uji."
        };

        /// <summary>
        /// Simpan keadaan sambungan baru beserta seluruh teks yang bergantung
        /// padanya, supaya chip, warna, dan pesannya tidak bisa menyimpang.
        /// </summary>
        private void TetapkanKeadaanServer(KeadaanServer baru)
        {
            if (_keadaanServer == baru) return;

            bool terkunciSebelumnya = UrlServerTerkunci;
            _keadaanServer = baru;

            // Koneksi yang sudah terbukti mengunci alamat untuk sesi ini. Ini
            // bukan hukuman: tombol Ubah alamat tetap tersedia, tetapi alamat
            // tidak bisa tergeser karena salah ketik saat halaman dibuka lagi.
            if (baru == KeadaanServer.Terhubung && !_lisensi.UrlServerTerkunci)
            {
                _alamatTerkunciSesi = true;
            }

            OnPropertyChanged(nameof(ServerBelumTersambung));
            OnPropertyChanged(nameof(ServerChipTeks));
            OnPropertyChanged(nameof(PesanServer));

            if (terkunciSebelumnya != UrlServerTerkunci)
            {
                OnPropertyChanged(nameof(UrlServerTerkunci));
                OnPropertyChanged(nameof(UrlServerDapatDiubah));
                OnPropertyChanged(nameof(TampilBukaKunciServer));
                OnPropertyChanged(nameof(CatatanAlamatServer));
                BukaKunciServerCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>
        /// Denyut sambungan melaporkan server kembali menjawab: muat ulang
        /// katalog (paksa) supaya chip berpindah ke "Terhubung" tanpa menunggu
        /// halaman dibuka ulang. Dipanggil dari helper langganan lemah — bisa
        /// dari utas latar, jadi perubahan keadaan tampilan diarahkan ke
        /// Dispatcher halaman.
        /// </summary>
        internal void SambunganServerBerubah()
        {
            if (_sambungan.Sekarang != KeadaanSambunganServer.Terhubung) return;

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null) return;

            if (dispatcher.CheckAccess())
            {
                SegarkanKarenaDenyut();
            }
            else
            {
                dispatcher.BeginInvoke(new Action(SegarkanKarenaDenyut));
            }
        }

        /// <summary>
        /// Penjaga agar event beruntun tidak menjalankan dua pemuatan katalog
        /// sekaligus. Hanya disentuh dari utas halaman.
        /// </summary>
        private bool _menyegarkanKarenaDenyut;

        private void SegarkanKarenaDenyut()
        {
            if (_keadaanServer == KeadaanServer.Terhubung) return;
            if (_menyegarkanKarenaDenyut) return;

            _menyegarkanKarenaDenyut = true;
            _ = SegarkanKarenaDenyutAsync();
        }

        private async Task SegarkanKarenaDenyutAsync()
        {
            try
            {
                await MuatKatalogAsync(paksa: true);
            }
            catch (Exception ex)
            {
                // MuatKatalogAsync sudah menangani kegagalan jaringan (wajar,
                // halaman tetap berguna offline); ini jaring pengaman supaya
                // penanda tidak macet bila ada galat tak terduga.
                _logger.LogWarning(ex, "Penyegaran katalog karena denyut sambungan gagal");
            }
            finally
            {
                _menyegarkanKarenaDenyut = false;
            }
        }


    }
}
