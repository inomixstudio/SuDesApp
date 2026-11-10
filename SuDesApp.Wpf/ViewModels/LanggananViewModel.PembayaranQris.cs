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
        // Pembayaran QRIS manual (QRIS statis merchant).
        // =======================================================================
        // Jalur ini TIDAK lewat gateway: pembeli memindai QRIS yang ditampilkan
        // di layar ini, lalu operator menandai lunas di pusat administer setelah
        // dana masuk. Karena itu halaman tidak pernah mengklaim langganan aktif
        // sendiri — kalimatnya selalu "aktif setelah admin menandai lunas".
        //
        // Gambar QR ditanam di rakitan aplikasi (terdaftar <Resource> di csproj),
        // bukan berkas di folder instalasi: gambar yang bisa ditukar dari disk
        // berarti pembayaran desa bisa dialihkan ke rekening lain tanpa jejak.

        /// <summary>
        /// Jalur resource QRIS di rakitan aplikasi (dipakai pemuat gambar dan uji).
        /// Isinya HANYA kode QR-nya — potongan dari poster QRIS milik merchant,
        /// sudah hitam-putih dan berzona-tenang sendiri — bukan poster lengkapnya.
        /// </summary>
        public const string NamaResourceQris = "Resources/Qris-Pembayaran.png";

        private bool _tampilQris;

        /// <summary>True saat panel QRIS terbuka di kartu paket.</summary>
        public bool TampilQris
        {
            get => _tampilQris;
            private set => SetProperty(ref _tampilQris, value);
        }

        private BitmapSource? _qrisGambar;

        /// <summary>
        /// Gambar QRIS, dimuat saat panel PERTAMA kali dibuka (bukan saat halaman
        /// dibuka) supaya pengguna yang tidak pernah membayar manual tidak perlu
        /// mendekode gambar ini. Null = gagal dimuat; panel tetap menampilkan
        /// nominal dan rinciannya.
        /// </summary>
        public BitmapSource? QrisGambar
        {
            get => _qrisGambar;
            private set => SetProperty(ref _qrisGambar, value);
        }

        /// <summary>
        /// Nomor admin dalam format internasional tanpa tanda, untuk tautan
        /// <c>wa.me</c>: 0851 1166 9951 → 6285111669951. Nomor sengaja TIDAK
        /// ditampilkan sebagai teks di halaman; pengguna menghubungi admin
        /// lewat tombol WhatsApp yang sudah ada.
        /// </summary>
        public const string NomorWaAdminWa = "6285111669951";

        /// <summary>
        /// NMID merchant yang WAJIB ada di dalam payload QRIS yang dibundel
        /// (tag 51, sub-tag 02).
        ///
        /// Konstanta ini yang dipegang uji <c>QrisPembayaranTests</c>: uji itu
        /// men-dekode gambar QRIS sungguhan lalu membandingkan NMID di dalamnya
        /// dengan nilai ini. Jadi mengganti berkas dengan QR merchant lain tidak
        /// bisa lolos diam-diam — uji gagal, bukan pembayaran desa yang pindah
        /// ke rekening lain.
        /// </summary>
        public const string QrisNmid = "ID1026608792580";

        /// <summary>
        /// Nama merchant di payload (tag 59) — inilah yang tampil di aplikasi
        /// bank/dompet digital pembeli, sedangkan "ARIEINO - FAMILY CLOTHING"
        /// hanya judul di poster yang tidak ikut ditampilkan lagi.
        /// </summary>
        public const string QrisNama = "arieino";

        /// <summary>
        /// Identitas penerima yang harus dicocokkan pembeli di aplikasi banknya.
        ///
        /// Disusun dari <see cref="QrisNama"/> dan <see cref="QrisNmid"/> supaya
        /// teks di layar tidak bisa menyimpang dari isi QR yang benar-benar
        /// dibundel: mengganti salah satunya tanpa yang lain membuat uji NMID
        /// gagal, bukan membuat nama penerima berbohong. WAJIB diperbarui
        /// bersamaan bila gambar <see cref="NamaResourceQris"/> diganti.
        /// </summary>
        public string QrisPenerima => $"{QrisNama} (NMID {QrisNmid}, Karawang)";

        /// <summary>Nominal menurut kartu paket terpilih (angka server bila sudah ada).</summary>
        public string QrisNominal => PaketTerpilihOpsi?.HargaTampil ?? "Harga dari server";

        /// <summary>
        /// Baris rincian yang dikirim pembeli ke admin sebagai bukti: kode
        /// aktivasi, paket, nominal, dan nama penerima. Kode aktivasi ikut karena
        /// QRIS statis tidak membawa nomor tagihan seperti gateway — itulah
        /// satu-satunya penanda desa yang bisa dicocokkan admin.
        /// </summary>
        public string QrisReferensi =>
            $"{(string.IsNullOrWhiteSpace(KodeAktivasi) ? "(kode aktivasi belum diisi)" : KodeAktivasi.Trim())} · " +
            $"{PaketTerpilihOpsi?.Label ?? "-"} · {QrisNominal} · {QrisPenerima}";

        /// <summary>Buka/tutup panel QRIS.</summary>
        public RelayCommand TampilkanQrisCommand { get; }

        /// <summary>Salin rincian pembayaran ke papan klip.</summary>
        public RelayCommand SalinRincianQrisCommand { get; }

        /// <summary>Buka WhatsApp admin (wa.me) dengan pesan bukti pembayaran terisi.</summary>
        public RelayCommand BukaWaAdminCommand { get; }

        /// <summary>
        /// Susun tautan <c>wa.me</c> untuk satu pesan. Statis supaya bisa diuji
        /// tanpa membangun ViewModel: nomor tujuan dan encoding pesan adalah
        /// satu aturan yang tidak boleh berbeda antara tombol dan uji.
        /// </summary>
        internal static string BangunTautanWa(string pesan) =>
            $"https://wa.me/{NomorWaAdminWa}?text={Uri.EscapeDataString(pesan)}";

        /// <summary>
        /// Pesan bukti pembayaran manual yang dikirim desa ke admin: identitas
        /// desa (kode aktivasi), paket, nominal, dan nama penerima — persis
        /// seperti <see cref="QrisReferensi"/> yang bisa disalin. Dua jalur
        /// mengirim rincian yang sama supaya admin tidak menerima dua versi.
        /// </summary>
        internal string PesanWaAdmin =>
            "Halo admin, saya ingin mengaktifkan/memperpanjang langganan SuDesApp.\n" +
            $"Rincian pembayaran: {QrisReferensi}.\n" +
            "Bukti pembayaran saya lampirkan. Terima kasih.";

        /// <summary>
        /// Buka WhatsApp admin di aplikasi/peramban bawaan dengan pesan yang
        /// sudah terisi. Kegagalan membuka (WhatsApp Desktop tidak ada dan
        /// peramban ditolak) tetap menyertakan tautan manual di banner, jadi
        /// pengguna tidak kehilangan jalan menghubungi admin.
        /// </summary>
        private void BukaWaAdmin()
        {
            var tautan = BangunTautanWa(PesanWaAdmin);
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = tautan,
                    UseShellExecute = true
                });
                Beritahu(2, "WhatsApp admin dibuka — kirim rincian dan bukti pembayaran. " +
                    "Langganan aktif setelah admin menandai lunas.");
                StatusMessage = "WhatsApp admin dibuka.";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal membuka WhatsApp admin {Tautan}", tautan);
                Beritahu(2, $"Buka manual: {tautan}. " +
                    "Sertakan rincian pembayaran berikut: " + QrisReferensi);
                StatusMessage = "WhatsApp tidak bisa dibuka otomatis — buka tautan manual di banner.";
            }
        }

        /// <summary>Umumkan ulang nominal &amp; rincian setelah paket/katalog berubah.</summary>
        private void SegarkanQris()
        {
            OnPropertyChanged(nameof(QrisNominal));
            OnPropertyChanged(nameof(QrisReferensi));

            // Panel sedang terbuka: kalimat pengarahnya memuat nominal, jadi ia
            // harus ikut berubah. Tanpa ini, desa yang mengganti paket setelah
            // panel terbuka masih membaca nominal LAMA di statusbar — dan
            // nominal itu yang berisiko ditransfer, karena QR-nya sama
            // (QRIS statis tidak membawa nominal).
            if (TampilQris)
            {
                TampilkanPesanQris();
            }
        }

        /// <summary>
        /// Satu-satunya tempat kalimat pengarah QRIS disusun — dipakai saat panel
        /// dibuka dan setiap kali nominalnya berubah, supaya keduanya tidak bisa
        /// berbeda. Kode aktivasi yang kosong ikut diingatkan di kalimat yang sama
        /// (bukan sebagai pesan terpisah), supaya pengingat itu tidak membuang
        /// petunjuk scan/nominal/WhatsApp di bawahnya.
        /// </summary>
        private void TampilkanPesanQris() => Beritahu(2,
            (string.IsNullOrWhiteSpace(KodeAktivasi)
                ? "Isi kode aktivasi terlebih dahulu — kode itulah yang dipakai admin untuk mencocokkan pembayaran dengan desa ini. "
                : "") +
            $"Scan QRIS di layar dengan aplikasi bank/e-wallet, masukkan nominal {QrisNominal}, " +
            "lalu hubungi admin via WhatsApp — tombol di panel. " +
            "Langganan aktif setelah admin menandai lunas.");

        /// <summary>
        /// Satu kartu paket di halaman. Isinya proyeksi dari
        /// <see cref="PaketLanggananKlien"/> milik Core: kelas ini hanya membuatnya
        /// dapat diikat XAML, sedangkan aturan harga dan teksnya ada di Core.
        /// </summary>
        public sealed class PaketOpsi : ObservableObject
        {
            private PaketLanggananKlien _paket;
            private string _catatan;
            private bool _disorot;
            private bool _sedangAktif;

            public PaketOpsi(PaketLanggananKlien paket)
            {
                _paket = paket ?? throw new ArgumentNullException(nameof(paket));
                _catatan = paket.CatatanCadangan;
            }

            /// <summary>Kode yang dikirim ke server ("bulanan" / "enambulan" / "tahunan").</summary>
            public string Kode => _paket.Kode;

            /// <summary>Teks yang dilihat pengguna.</summary>
            public string Label => _paket.Label;

            public override string ToString() => Label;

            /// <summary>Angka resmi server ("Rp100.000") atau teks jujur tanpa angka.</summary>
            public string HargaTampil => _paket.HargaTampil;

            /// <summary>
            /// Harga per hari dari angka resmi server, mis. "≈ Rp3.288/hari" —
            /// pembanding jujur antar paket yang berbeda durasi. Kosong sampai
            /// harga dan hari dari server sama-sama diketahui.
            /// </summary>
            public string HargaPerHariTampil => _paket.HargaPerHariTampil;

            /// <summary>True bila harga per hari bisa ditampilkan (angka server lengkap).</summary>
            public bool AdaHargaPerHari => _paket.AdaHargaPerHari;

            /// <summary>
            /// Masa aktif — angka dari server bila sudah ada; sebelum itu bawaan
            /// yang sama dengan katalog server, supaya tampilan tidak berubah
            /// setelah angka resmi datang.
            /// </summary>
            public string Durasi => _paket.DurasiTampil;

            /// <summary>Kalimat pembanding — dihitung dari harga asli server.</summary>
            public string Catatan
            {
                get => _catatan;
                private set => SetProperty(ref _catatan, value);
            }

            /// <summary>
            /// True untuk paket paling hemat (harga per hari terendah dari angka
            /// server). Ditandai "PALING HEMAT" di kartu supaya pengguna tidak
            /// perlu menghitung sendiri mana yang paling untung.
            /// </summary>
            public bool Disorot
            {
                get => _disorot;
                private set => SetProperty(ref _disorot, value);
            }

            /// <summary>True bila paket ini yang sedang aktif pada langganan berjalan.</summary>
            public bool SedangAktif
            {
                get => _sedangAktif;
                set => SetProperty(ref _sedangAktif, value);
            }

            /// <summary>Terapkan hasil katalog Core (angka, catatan, penanda hemat) ke kartu ini.</summary>
            internal void TerapkanKatalog(PaketLanggananKlien paket, string catatan, bool disorot)
            {
                bool angkaBerubah = _paket.Harga != paket.Harga || _paket.Hari != paket.Hari;
                _paket = paket;
                Catatan = catatan;
                Disorot = disorot;

                if (angkaBerubah)
                {
                    OnPropertyChanged(nameof(HargaTampil));
                    OnPropertyChanged(nameof(HargaPerHariTampil));
                    OnPropertyChanged(nameof(AdaHargaPerHari));
                    OnPropertyChanged(nameof(Durasi));
                }
            }
        }


    }
}
