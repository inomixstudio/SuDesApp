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
        // Tindakan.
        // =======================================================================

        /// <summary>
        /// Jalankan satu tindakan halaman sebagai SATU operasi sibuk: klik beruntun
        /// diabaikan, tombol lain nonaktif selama berjalan, dan penanda sibuk selalu
        /// dilepas walau tindakannya gagal.
        ///
        /// Dulu potongan penjagaan ini disalin di setiap perintah, dan "Simpan &amp; Uji"
        /// sempat melewatkannya: dua klik beruntun menjalankan dua pemuatan katalog +
        /// dua cek status yang saling menimpa, sementara tombol Bayar/Trial tetap bisa
        /// ditekan di tengah pengujian alamat. Satu tempat ini yang memiliki aturannya.
        /// </summary>
        private async Task JalankanSibukAsync(Func<Task> tindakan)
        {
            if (LanggananSibuk) return;
            LanggananSibuk = true;
            try
            {
                await tindakan();
            }
            finally
            {
                LanggananSibuk = false;
            }
        }

        /// <summary>
        /// Jalankan <paramref name="aksi"/> di thread UI bila sedang di thread
        /// latar. Sinkron dengan batas 500 ms: bila dispatchernya tidak pernah
        /// memompa pesan (mis. sesi uji), jatuh ke set langsung supaya pesannya
        /// tetap tercatat. Invoke tanpa batas bisa menggantung selamanya pada
        /// dispatcher semacam itu.
        /// </summary>
        private static void JalankanDiUi(Action aksi)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
            {
                aksi();
                return;
            }

            try
            {
                var operasi = dispatcher.InvokeAsync(aksi);
                operasi.Task.Wait(TimeSpan.FromMilliseconds(500));
                if (!operasi.Task.IsCompleted)
                {
                    aksi();
                }
            }
            catch (Exception)
            {
                // Dispatcher tidak bisa dipakai: lanjutkan langsung.
                aksi();
            }
        }

        /// <summary>
        /// Satu-satunya cara menulis banner untuk pesan sementara (galat simpan,
        /// petunjuk QRIS, hasil panduan): nada dan kalimat selalu berubah bersama,
        /// jadi tidak ada banner merah yang berbunyi seolah berhasil.
        /// </summary>
        private void Beritahu(int nada, string keterangan)
        {
            JalankanDiUi(() =>
            {
                LanggananTone = nada;
                LanggananInfo = keterangan;
            });
        }

        private async Task CekLanggananAsync()
        {
            try
            {
                _lisensi.KodeAktivasi = FormatKodeAktivasi.Normalisasi(KodeAktivasi);
                StatusMessage = "Memeriksa langganan ke server…";
                var hasil = await _lisensi.CekAsync();
                // Terapkan dulu ke penjaga fitur, lalu tampilkan — supaya lencana
                // keadaan (Demo / Full Service / Baca-saja) yang dihitung dari
                // penjaga ikut mencerminkan hasil yang baru saja diterima,
                // bukan status dari cek sebelumnya.
                _penjaga.Terapkan(hasil);
                TampilkanStatusLangganan(hasil);

                // Aktivasi (trial atau kode langganan yang sah) juga menjadi titik
                // pencatatan persetujuan — jaring pengaman bila preferensi pengguna
                // terhapus setelah login sehingga dialog awal tidak sempat muncul.
                // Catatan yang sudah ada tidak pernah ditimpa: waktu persetujuan
                // pertama tetap utuh sebagai bukti.
                if (hasil.Aktif)
                {
                    CatatPersetujuanSaatAktivasi();
                }

                // Cek Status SELALU memuat ulang katalog harga (melewati jeda
                // kegagalan): server baru saja terbukti terjangkau, dan pengguna
                // sengaja menekan tombol ini.
                await MuatKatalogAsync(paksa: true);
                StatusMessage = "Status langganan diperbarui.";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cek langganan gagal");
                Beritahu(3, "Gagal menghubungi server lisensi: " + ex.Message);
                StatusMessage = "Cek langganan gagal.";
            }
        }

        /// <summary>
        /// Catat persetujuan dokumen yang belum punya catatan sama sekali.
        /// Kegagalan hanya dicatat ke log — aktivasi langganan tetap berhasil,
        /// dan dialog persetujuan akan muncul lagi saat aplikasi dibuka.
        /// </summary>
        private void CatatPersetujuanSaatAktivasi()
        {
            var hasil = PersetujuanDokumenService.CatatBilaBelumAda(DateTime.Now);
            if (!hasil.Berhasil)
            {
                _logger.LogWarning("Persetujuan dokumen saat aktivasi belum tercatat: {Pesan}", hasil.Pesan);
            }
        }

        /// <summary>
        /// Isi tombol "Simpan &amp; Cek": kode dinormalisasi dulu (spasi, baris
        /// baru, dan huruf kecil dari salinan WhatsApp dirapikan), dibentuk
        /// resminya dicek sebelum disimpan, lalu statusnya langsung diperiksa —
        /// ketiganya satu operasi sibuk. Tanpa normalisasi ini kode yang
        /// benar pun gagal ("tidak dikenal") karena pencarian server sensitif
        /// huruf besar.
        /// </summary>
        private async Task SimpanLaluCekAsync()
        {
            var mentah = KodeAktivasi ?? string.Empty;
            if (string.IsNullOrWhiteSpace(mentah))
            {
                Beritahu(2, "Kode aktivasi masih kosong. Salin kode dari WhatsApp admin, gunakan tombol Tempel, lalu tekan Simpan & Cek.");
                StatusMessage = "Kode aktivasi belum diisi.";
                return;
            }

            var ternormalisasi = FormatKodeAktivasi.Normalisasi(mentah);
            if (!FormatKodeAktivasi.BentukSah(ternormalisasi))
            {
                KodeAktivasi = ternormalisasi;
                StatusMessage = "Kode aktivasi belum lengkap.";
                Beritahu(2, "Kode itu belum berformat SUDES-XXXX-XXXX (13 karakter, huruf besar, contoh: SUDES-K7Q2-4831). " +
                    "Periksa lagi salinan dari admin — satu huruf yang hilang berarti kode tidak dikenal. " +
                    "Bila sulit dibaca, gunakan Tempel dari WhatsApp supaya tidak salah ketik.");
                return;
            }

            KodeAktivasi = ternormalisasi;
            AppPreferenceStore.SetKodeAktivasi(ternormalisasi);
            _statusSesaat.Tampilkan("Tersimpan — kode aktivasi");
            await CekLanggananAsync();
        }

        /// <summary>
        /// Tempel kode dari papan klip (salinan WhatsApp admin) dan rapikan
        /// langsung — spasi dan huruf kecil tidak lagi jadi penyebab gagal.
        /// </summary>
        private void TempelKodeDariClipboard()
        {
            string? dariClipboard;
            try
            {
                dariClipboard = System.Windows.Clipboard.GetText();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Papan klip tidak bisa dibaca untuk tempel kode aktivasi");
                Beritahu(3, "Papan klip tidak bisa dibaca. Ketik kode manual: SUDES-XXXX-XXXX.");
                return;
            }

            var ternormalisasi = FormatKodeAktivasi.Normalisasi(dariClipboard);
            if (ternormalisasi.Length == 0)
            {
                Beritahu(2, "Papan klip kosong atau tidak berisi kode. Salin kode dari WhatsApp admin dulu, lalu tekan Tempel.");
                return;
            }

            KodeAktivasi = ternormalisasi;
            var ok = FormatKodeAktivasi.BentukSah(ternormalisasi);
            Beritahu(ok ? 1 : 2, ok
                ? "Kode ditempel dan formatnya sudah benar — tekan Simpan & Cek."
                : "Isi papan klip ditempel, tetapi bentuknya belum SUDES-XXXX-XXXX. Periksa lagi salinan dari admin.");
            StatusMessage = ok ? "Kode ditempel." : "Kode ditempel, format belum lengkap.";
        }

        private void HapusKode()
        {
            KodeAktivasi = string.Empty;
            AppPreferenceStore.SetKodeAktivasi(string.Empty);
            StatusMessage = "Kode aktivasi dihapus.";
        }

        /// <summary>
        /// Simpan alamat server lisensi (preferensi bertahan antarjalankan),
        /// terapkan seketika ke LisensiManager, lalu buktikan koneksinya dengan
        /// langsung mengecek status bila kode aktivasi sudah terisi.
        /// </summary>
        private async Task SimpanServerAsync()
        {
            var url = (UrlServer ?? string.Empty).Trim().TrimEnd('/');
            if (url.Length == 0 ||
                !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                Beritahu(3, "Alamat server belum sah — isi alamat lengkap yang diawali http:// atau https://.");
                StatusMessage = "Alamat server tidak disimpan.";
                return;
            }

            // Penjagaan kedua di luar binding read-only: jangan pernah menulis
            // preferensi yang berbeda dari alamat produksi yang sudah dikunci.
            if (_lisensi.UrlServerTerkunci
                && !string.Equals(url, PenentuAlamatLisensi.Rapikan(_lisensi.UrlServer), StringComparison.Ordinal))
            {
                Beritahu(3, "Alamat server dikunci oleh konfigurasi aplikasi dan tidak bisa diganti dari halaman ini.");
                StatusMessage = "Alamat server tidak disimpan.";
                UrlServer = _lisensi.UrlServer;
                return;
            }

            // Alamat yang dikunci konfigurasi tidak ditulis ke preferensi:
            // kalau build berikutnya kembali ke loopback, preferensi lama tidak
            // boleh menghidupkan lagi alamat produksi yang sudah usang.
            if (!_lisensi.UrlServerTerkunci)
            {
                AppPreferenceStore.SetUrlServerLisensi(url);
            }
            _lisensi.UrlServer = url;
            UrlServer = url;
            _statusSesaat.Tampilkan("Tersimpan — server lisensi");

            // Alamat server baru = katalog harga baru; muat ulang kartu paket
            // sekarang juga (paksa, lewati jeda kegagalan) supaya angka di layar
            // tidak tetap berasal dari server lama.
            _angkaDariSumberIni = false;
            await MuatKatalogAsync(paksa: true);
            StatusMessage = "Alamat server disimpan — katalog harga diuji ulang.";

            // "Simpan & Uji" juga membuktikan langganan memakai alamat baru.
            // Pemanggilnya (JalankanSibukAsync) menahan penanda sibuk sampai di
            // sini, jadi tidak ada tombol lain yang menyela di tengah pengujian.
            if (!string.IsNullOrWhiteSpace(KodeAktivasi))
            {
                await CekLanggananAsync();
            }
        }

        /// <summary>
        /// Hubungkan otomatis: simpan alamat saat ini, nyalakan server lokal
        /// bila tersedia & belum berjalan, lalu coba sambung berulang sampai
        /// server menjawab atau percobaan habis (5×, jeda 2 detik).
        /// Setelah terhubung, status langganan langsung diperiksa.
        /// </summary>
        private async Task HubungkanAsync()
        {
            var url = (UrlServer ?? string.Empty).Trim().TrimEnd('/');
            if (url.Length == 0 ||
                !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                Beritahu(3, "Alamat server belum sah — isi alamat lengkap yang diawali http:// atau https://, lalu tekan Hubungkan.");
                StatusMessage = "Gagal menghubungkan: alamat server tidak sah.";
                return;
            }

            if (_lisensi.UrlServerTerkunci
                && !string.Equals(url, PenentuAlamatLisensi.Rapikan(_lisensi.UrlServer), StringComparison.Ordinal))
            {
                Beritahu(3, "Alamat server dikunci oleh konfigurasi aplikasi; Hubungkan memakai alamat produksi tersebut.");
            }
            else if (!_lisensi.UrlServerTerkunci)
            {
                AppPreferenceStore.SetUrlServerLisensi(url);
            }

            _lisensi.UrlServer = url;
            UrlServer = url;
            _angkaDariSumberIni = false;
            StatusMessage = "Menghubungkan ke server " + url + " …";

            // Nyalakan server lokal otomatis bila build mendukungnya dan
            // server belum berjalan — tanpa ini alamat localhost tidak akan
            // pernah menjawab walau dicoba berulang.
            if (TampilServerLokal && _serverLokal is { Berjalan: false })
            {
                ServerLokalAktif = true;
            }

            const int maksPercobaan = 6;
            for (var percobaan = 1; percobaan <= maksPercobaan; percobaan++)
            {
                await MuatKatalogAsync(paksa: true);
                if (_keadaanServer == KeadaanServer.Terhubung) break;
                if (percobaan < maksPercobaan)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2));
                }
            }

            if (_keadaanServer == KeadaanServer.Terhubung)
            {
                await CekLanggananAsync();
                StatusMessage = "Terhubung — server lisensi menjawab di " + url + ".";
                Beritahu(1, "Terhubung — server lisensi menjawab di " + url + ".");
                return;
            }

            Beritahu(3, "Server tidak menjawab di " + url + ". Periksa koneksi internet, dan pastikan " +
                "server lisensi sedang berjalan.");
            StatusMessage = "Gagal menghubungi server lisensi.";
        }

        /// <summary>
        /// Mulai masa uji coba 30 hari dari halaman ini — TANPA memaksa kode
        /// aktivasi, sama seperti popup saat aplikasi dibuka.
        ///
        /// Aturannya satu dan dipakai bersama (<see cref="PenjagaLisensi.MulaiPercobaanAsync"/>):
        /// bila kode sudah diisi dan server terjangkau, trial dicatat SERVER
        /// (sekali per desa, token bertanda tangan); bila kode belum ada atau
        /// server tidak terjangkau, trial LOKAL yang dipakai supaya desa tetap
        /// bisa mencoba saat offline. Server yang menjawab MENOLAK tidak diberi
        /// cadangan lokal — dua jalur trial tidak boleh dijumlahkan.
        /// </summary>
        private async Task MulaiTrialAsync()
        {
            // Trial lokal yang sudah dipakai menutup trial server juga: dua jalur
            // trial yang sama-sama 30 hari tidak boleh menjadi 60 hari.
            if (_penjaga.TrialPernahDimulai)
            {
                Beritahu(2, "Masa uji coba 30 hari sudah pernah dipakai di komputer ini. " +
                    "Selesaikan pembayaran lisensi untuk melanjutkan.");
                StatusMessage = "Trial tidak diaktifkan.";
                return;
            }

            try
            {
                // Kode yang sudah diketik (belum disimpan) ikut dipakai supaya
                // trial server bisa dipilih saat server memang terjangkau.
                if (!string.IsNullOrWhiteSpace(KodeAktivasi))
                {
                    _lisensi.KodeAktivasi = FormatKodeAktivasi.Normalisasi(KodeAktivasi);
                }

                StatusMessage = "Menyiapkan masa uji coba 30 hari…";

                var percobaan = await _penjaga.MulaiPercobaanAsync();
                var (judul, pesan) = PenjagaLisensi.JelaskanPercobaan(percobaan);
                if (percobaan.BerlakuSampai is DateTime sampai)
                {
                    pesan += $" Berlaku sampai {sampai.ToLocalTime():dd MMM yyyy}.";
                }

                bool ditolak = percobaan.Asal
                    is AsalPercobaan.DitolakServer or AsalPercobaan.SudahPernahTrial;

                // Tampilkan status terbaru: trial server sudah membawa token,
                // trial lokal belum — banner memakai ringkasan trial (lihat
                // TampilkanStatusLangganan) supaya tidak terbaca "Tidak aktif"
                // padahal menu sudah terbuka.
                var status = _penjaga.Status ?? await _lisensi.CekAsync();
                _penjaga.Terapkan(status);
                TampilkanStatusLangganan(status);

                // Banner di atas sudah menampilkan keadaan (trial/aktif). Yang
                // hanya terbaca di sini: ALASAN penolakan server, dan tanggal
                // berakhir hasil percobaan.
                if (ditolak)
                {
                    Beritahu(2, judul + " — " + pesan);
                }

                StatusMessage = ditolak
                    ? "Trial tidak diaktifkan."
                    : percobaan.BerlakuSampai is DateTime berakhir
                        ? $"{judul} s.d. {berakhir.ToLocalTime():dd MMM yyyy}."
                        : judul + ".";

                if (status.Aktif)
                {
                    CatatPersetujuanSaatAktivasi();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Aktifkan trial gagal");
                Beritahu(3, "Gagal menyiapkan trial: " + ex.Message);
                StatusMessage = "Trial gagal diaktifkan.";
            }
        }

        /// <summary>
        /// Buat tagihan pembayaran. Dua jalur, sama seperti aturan server:
        /// <list type="number">
        /// <item>kode aktivasi terisi = TERPANJANG — desa yang sudah punya kode
        /// memperpanjang masa aktifnya;</item>
        /// <item>kode kosong = BERLANGGANAN AWAL — nama desa diambil dari profil
        /// desa lokal, dan server memberi kode aktivasi baru; kode itu disimpan
        /// ke preferensi seketika supaya tidak hilang, lalu layak untuk
        /// aktivasi setelah pembayaran lunas.</item>
        /// </list>
        /// Paket ikut dikirim; nominal TIDAK — server yang menentukan harga
        /// supaya tidak bisa dimanipulasi dari sisi klien.
        /// </summary>
        private async Task BayarLanggananAsync()
        {
            string? namaDesa = null;
            if (string.IsNullOrWhiteSpace(KodeAktivasi))
            {
                // Berlangganan awal: tanpa kode, nama desa wajib. Sumbernya
                // profil desa lokal (Pengaturan aplikasi), bukan ketikan ulang.
                namaDesa = (await AmbilNamaDesaProfilAsync()).Trim();
                if (namaDesa.Length == 0)
                {
                    Beritahu(2,
                        "Desa belum punya kode aktivasi. Isi nama desa di Pengaturan Aplikasi " +
                        "terlebih dahulu, lalu tekan Bayar lagi — kode aktivasi akan diterbitkan otomatis " +
                        "setelah tagihan dibuat.");
                    return;
                }
            }

            try
            {
                var hasil = await _klien.BuatTagihanAsync(PaketTerpilih, KodeAktivasi, namaDesa);
                if (hasil.Tagihan is null)
                {
                    Beritahu(3, $"Gagal membuat tagihan (HTTP {hasil.StatusHttp}).");
                    return;
                }

                // Kode baru dari berlangganan awal: simpan SEKETIKA dan tampil di
                // halaman ini (kotak Kode Aktivasi yang sama), supaya desa tidak
                // kehilangan aksesnya sebelum sempat membuat catatan.
                if (!string.IsNullOrWhiteSpace(hasil.KodeAktivasi) &&
                    !string.Equals(hasil.KodeAktivasi, KodeAktivasi, StringComparison.Ordinal))
                {
                    KodeAktivasi = hasil.KodeAktivasi;
                    AppPreferenceStore.SetKodeAktivasi(hasil.KodeAktivasi);
                    Beritahu(2,
                        $"KODE AKTIVASI BARU: {hasil.KodeAktivasi} — telah disimpan otomatis. " +
                        "Aktif setelah pembayaran lunas; tekan Cek Status setelahnya.");
                }

                var tagihan = hasil.Tagihan;

                // Tampilkan angka dari server: yang dicetak di sini adalah yang
                // benar-benar akan ditagihkan, bukan perkiraan lokal.
                var ringkas = tagihan.RingkasanTampil;
                if (!string.IsNullOrWhiteSpace(tagihan.LinkPembayaran) &&
                    Uri.TryCreate(tagihan.LinkPembayaran, UriKind.Absolute, out var uri))
                {
                    bool terbuka = CobaBukaPeramban(uri);
                    Beritahu(2, terbuka
                        ? $"Tagihan {ringkas} ({tagihan.OrderId}) dibuat — selesaikan pembayaran QRIS/VA di peramban, lalu tekan Cek Status."
                        : $"Tagihan {ringkas} ({tagihan.OrderId}) SUDAH dibuat di server, tetapi peramban tidak bisa dibuka otomatis. " +
                          $"Buka link ini di peramban lalu tekan Cek Status: {uri}");
                    StatusMessage = terbuka
                        ? "Tagihan dibuat — lanjutkan pembayaran di peramban."
                        : "Tagihan dibuat; peramban tidak bisa dibuka otomatis.";
                }
                else
                {
                    Beritahu(2, $"Tagihan {ringkas} ({tagihan.OrderId}) dibuat. Link pembayaran belum tersedia — hubungi admin.");
                    StatusMessage = "Tagihan dibuat tanpa link pembayaran.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Buat tagihan langganan gagal");
                Beritahu(3, "Gagal membuat tagihan: " + ex.Message);
                StatusMessage = "Gagal membuat tagihan.";
            }
        }

        /// <summary>
        /// Nama desa dari profil lokal untuk jalur berlangganan awal. Profil
        /// desa kosong dianggap wajar (instalasi baru yang belum diisi), jadi
        /// pemanggil menyaungkan kalimat panduan bila hasilnya kosong.
        /// </summary>
        private async Task<string> AmbilNamaDesaProfilAsync()
        {
            if (_pengaturanDesa is null)
            {
                return string.Empty;
            }

            try
            {
                var desa = await _pengaturanDesa.GetSettingsAsync().ConfigureAwait(false);
                return desa?.NamaDesa?.Trim() ?? string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Profil desa tidak bisa dibaca untuk berlangganan awal");
                return string.Empty;
            }
        }

        /// <summary>
        /// Buka link pembayaran di peramban bawaan. Dipisah supaya kegagalannya
        /// tidak pernah terbaca sebagai "gagal membuat tagihan": saat ini terjadi
        /// tagihannya SUDAH tercatat di server, dan pesan yang salah membuat desa
        /// membuat tagihan kedua — lalu mungkin membayar dua kali.
        /// </summary>
        private bool CobaBukaPeramban(Uri alamat)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = alamat.ToString(),
                    UseShellExecute = true
                });
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal membuka peramban untuk link pembayaran {Alamat}", alamat);
                return false;
            }
        }

        /// <summary>
        /// Buka/tutup panel QRIS. Panel SELALU terbuka saat tombol ditekan — inilah
        /// isi tombolnya, "tampilkan QR"; dulu kode kosong hanya memunculkan
        /// peringatan dan QR tak pernah tampil. Kode aktivasi tetap diingatkan
        /// saat kosong (di kalimat yang sama, bukan menimpa — satu penulis
        /// banner), karena QRIS statis tidak membawa nomor tagihan: kode itulah
        /// satu-satunya penanda desa yang bisa dicocokkan admin.
        /// </summary>
        private void AlihTampilQris()
        {
            TampilQris = !TampilQris;
            if (!TampilQris) return;

            // Dimuat saat panel pertama dibuka saja: pengguna yang membayar lewat
            // tagihan otomatis tidak perlu mendekode gambar apa pun.
            QrisGambar ??= MuatGambarQris();

            TampilkanPesanQris();
        }

        /// <summary>
        /// Salin rincian ke papan klip supaya pengguna bisa mengirimkannya ke admin
        /// tanpa mengetik ulang. Papan klip bisa ditolak proses lain — itu bukan
        /// kegagalan halaman, jadi cukup dicatat di log.
        /// </summary>
        private void SalinRincianQris()
        {
            try
            {
                System.Windows.Clipboard.SetText(QrisReferensi);
                Beritahu(2, $"Rincian disalin — kirim ke admin via WhatsApp: {QrisReferensi}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal menyalin rincian QRIS ke papan klip");
                Beritahu(2, $"Rincian: {QrisReferensi}");
            }
        }

        /// <summary>
        /// Muat gambar QRIS dari resource rakitan. Alamat pertama memakai nama
        /// rakitan eksplisit (pola yang sama dengan AboutViewModel):
        /// <c>pack://application:,,,/Resources/…</c> menunjuk rakitan yang SEDANG
        /// berjalan — pada test host itu rakitan uji, bukan SuDesApp — sehingga
        /// tanpa nama rakitan gambarnya gagal dimuat di luar aplikasi.
        /// </summary>
        private BitmapSource? MuatGambarQris()
        {
            string[] alamat =
            {
                $"pack://application:,,,/SuDesApp;component/{NamaResourceQris}",
                $"pack://application:,,,/{NamaResourceQris}",
            };

            foreach (var satu in alamat)
            {
                try
                {
                    var gambar = new BitmapImage();
                    gambar.BeginInit();
                    gambar.CacheOption = BitmapCacheOption.OnLoad;
                    gambar.UriSource = new Uri(satu, UriKind.Absolute);
                    gambar.EndInit();
                    gambar.Freeze();
                    return gambar;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "QRIS tidak termuat dari {Alamat}", satu);
                }
            }

            _logger.LogWarning("Gambar QRIS tidak ditemukan di resource aplikasi ({Berkas}).", NamaResourceQris);
            return null;
        }

        /// <summary>
        /// Terapkan satu status lisensi ke tampilan halaman: kalimat dan nada
        /// banner, petak ringkasan, tombol trial, serta penanda paket aktif.
        ///
        /// Kalimat dan angkanya datang dari <see cref="RingkasanLangganan"/>
        /// (Core) — halaman ini tidak menyusun aturan lisensi sendiri, hanya
        /// menyalin hasilnya. Selain teks, hanya dua hal yang diputuskan di sini:
        /// panel QRIS ditutup saat langganan aktif, dan kartu paket aktif ditandai.
        /// </summary>
        private void TampilkanStatusLangganan(StatusLisensi hasil)
        {
            // Trial lokal belum punya token: banner langganan biasa akan berbunyi
            // "Tidak aktif" padahal menu vital sudah terbuka. Ringkasan trial
            // menjelaskan sisa harinya dan tetap menawarkan pembayaran.
            _ringkasan = _penjaga.Keadaan == KeadaanLisensi.Trial
                ? RingkasanLangganan.DariTrial(_penjaga.TrialBerakhirUtc, _penjaga.SisaHariTrial)
                : RingkasanLangganan.Dari(hasil, KodeAktivasi);
            LanggananStatus = _ringkasan.Judul;
            Beritahu(_ringkasan.Nada, _ringkasan.Keterangan);
            OnPropertyChanged(nameof(SedangTrial));

            // Sudah aktif = tidak ada lagi yang perlu dibayar di layar ini; panel
            // QRIS yang masih terbuka dari percobaan sebelumnya hanya
            // membingungkan (nominalnya pun sudah tidak relevan).
            if (hasil.Aktif)
            {
                TampilQris = false;
            }

            // Tandai paket yang sedang aktif pada kartu pilihan paket supaya
            // pengguna tahu paket mana yang sedang dipakai sebelum memperpanjang.
            foreach (var opsi in PilihanPaket)
            {
                opsi.SedangAktif = hasil.Aktif && !string.IsNullOrWhiteSpace(hasil.Paket) &&
                    string.Equals(opsi.Kode, hasil.Paket, StringComparison.OrdinalIgnoreCase);
            }

            // Segarkan turunan status yang dipakai lencana header, tombol trial, dan petak KPI.
            OnPropertyChanged(nameof(KeadaanLabel));
            OnPropertyChanged(nameof(KeadaanTone));
            OnPropertyChanged(nameof(BacaSaja));
            OnPropertyChanged(nameof(TampilTombolTrial));
            OnPropertyChanged(nameof(PaketNama));
            OnPropertyChanged(nameof(BerlakuSampaiTampil));
            OnPropertyChanged(nameof(SisaHariTampil));
            OnPropertyChanged(nameof(TerakhirDiperiksaTampil));
        }


    }
}
