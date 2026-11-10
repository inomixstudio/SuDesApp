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
        // Katalog harga dari server (GET /paket) — penyusunannya ada di
        // LanggananKlien; halaman ini hanya menerapkan hasilnya.
        // =======================================================================

        private string _keteranganHarga =
            "Harga resmi ditetapkan server. Bila kartu belum menampilkan angka, server belum terjangkau — " +
            "harga tetap dikonfirmasi server saat tagihan dibuat.";

        /// <summary>
        /// Kalimat di atas kartu paket: berubah setelah katalog server termuat,
        /// supaya pengguna tahu apakah angka di kartu sudah angka resmi.
        /// </summary>
        public string KeteranganHarga
        {
            get => _keteranganHarga;
            private set => SetProperty(ref _keteranganHarga, value);
        }

        /// <summary>
        /// Benar bila angka di kartu berasal dari sumber yang sedang dipakai
        /// (katalog server atau salinan resminya). Dipakai hanya untuk memilih
        /// keterangan: setelah server diganti, angka lama di kartu tidak boleh
        /// membuat pesan "server belum terjangkau" tampak tidak perlu.
        /// </summary>
        private bool _angkaDariSumberIni;

        /// <summary>
        /// Muat katalog harga dari server lalu terapkan ke kartu. Kegagalan
        /// jaringan di sini wajar (halaman masih berguna offline), jadi tidak
        /// pernah ditampilkan sebagai galat.
        /// </summary>
        private async Task MuatKatalogAsync(bool paksa = false)
        {
            HasilKatalog hasil;
            try
            {
                hasil = await _klien.MuatDariServerAsync(paksa);
            }
            catch (Exception ex)
            {
                // Konstruktor halaman memanggil ini tanpa ditunggu
                // (fire-and-forget), jadi pengecualian yang lolos akan hilang
                // sebagai unobserved exception tanpa jejak di layar. Kegagalan
                // jaringan di sini wajar — cukup dicatat, lalu keadaan server
                // ditandai seperti kegagalan biasa.
                _logger.LogWarning(ex, "Pemuatan katalog harga paket gagal");
                TetapkanKeadaanServer(KeadaanServer.TidakTerjangkau);
                return;
            }

            PerbaruiKeadaanServer(hasil);

            if (hasil.Katalog != null)
            {
                TerapkanKatalog(hasil.Katalog);
                _angkaDariSumberIni = hasil.Katalog.AdaHarga;
                KeteranganHarga =
                    "Harga di bawah diambil langsung dari katalog server — angka yang sama persis dengan " +
                    "yang akan ditagih saat pembayaran.";
                return;
            }

            // Bila kartu SUDAH punya angka (dari salinan tersimpan), jangan
            // menimpanya dengan pesan "server belum terjangkau" — angka itu
            // lebih berguna daripada keterangan kegagalan.
            if (hasil.Hasil == HasilMuatKatalog.Gagal && !_angkaDariSumberIni)
            {
                KeteranganHarga =
                    "Server lisensi belum bisa dijangkau, jadi kartu belum menampilkan angka. " +
                    "Harga tetap dikonfirmasi server saat tagihan dibuat.";
            }
        }

        /// <summary>
        /// Terjemahkan hasil panggilan katalog menjadi keadaan chip server.
        /// Hanya panggilan yang BENAR-BENAR terjadi yang mengubah keadaan:
        /// percobaan yang dilewati karena jeda coba ulang (setelah gagal) tidak
        /// mengubah apa pun, supaya chip tidak berkedip "tidak terjangkau"
        /// hanya karena permintaannya sengaja ditahan.
        /// </summary>
        private void PerbaruiKeadaanServer(HasilKatalog hasil)
        {
            switch (hasil.Hasil)
            {
                case HasilMuatKatalog.Server:
                    TetapkanKeadaanServer(KeadaanServer.Terhubung);
                    break;

                case HasilMuatKatalog.Gagal:
                    TetapkanKeadaanServer(KeadaanServer.TidakTerjangkau);
                    break;

                case HasilMuatKatalog.Kosong:
                    // Server menjawab tetapi katalognya tidak bisa dipakai — atau
                    // memang belum ada alamat untuk dihubungi. Keduanya bukan
                    // "terhubung", tetapi jangan juga disebut "belum diatur"
                    // bila alamatnya sudah diisi.
                    TetapkanKeadaanServer(string.IsNullOrWhiteSpace(_urlServer)
                        ? KeadaanServer.BelumDiatur
                        : KeadaanServer.TidakTerjangkau);
                    break;

                // HasilMuatKatalog.Dilewati: tidak ada panggilan → keadaan terakhir tetap.
            }
        }

        /// <summary>
        /// Tampilkan angka dari salinan katalog terakhir (tanpa jaringan) supaya
        /// kartu sudah berisi sejak halaman dibuka walau server lisensi
        /// belum/tidak bisa dihubungi.
        /// </summary>
        private void TampilkanKatalogTersimpan()
        {
            var salinan = _klien.MuatKatalogTersimpan();
            if (salinan is null) return;

            TerapkanKatalog(salinan);
            _angkaDariSumberIni = salinan.AdaHarga;
            KeteranganHarga =
                "Harga di bawah adalah angka terakhir yang dikonfirmasi server. " +
                "Angka yang benar-benar ditagih tetap ditentukan server saat pembayaran.";
        }

        /// <summary>
        /// Salin angka &amp; kebijakan katalog (catatan kartu dan penanda "paling
        /// hemat") ke kartu di layar. Aturannya ada di
        /// <see cref="KatalogLangganan"/>, jadi halaman ini tidak punya salinan
        /// aturan harga yang bisa menyimpang.
        /// </summary>
        private void TerapkanKatalog(KatalogLangganan katalog)
        {
            foreach (var opsi in PilihanPaket)
            {
                var kartu = katalog.Cari(opsi.Kode);
                if (kartu is null) continue;

                opsi.TerapkanKatalog(kartu, katalog.Catatan(kartu), katalog.KodeDisorot == opsi.Kode);
            }

            // Nominal QRIS mengikuti kartu paket terpilih, jadi ia ikut berubah
            // saat angka resmi server (atau salinan tersimpan) tiba.
            SegarkanQris();
        }


    }
}
