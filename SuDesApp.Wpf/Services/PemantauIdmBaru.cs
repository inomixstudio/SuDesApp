using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SuDesApp.ControlSurat;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wilayah;
using SuDesApp.Wpf.Utilities;

namespace SuDesApp.Wpf.Services
{
    /// <summary>
    /// Deteksi otomatis data IDM tahun baru di portal pusat
    /// (<c>idm.kemendesa.go.id</c>).
    ///
    /// Halaman IDM mengambil data hanya saat dibuka. Kalau pusat menerbitkan
    /// tahun baru ketika aplikasi sedang dipakai, pengguna yang sudah melihat
    /// data lama tidak akan tahu sampai membuka halamannya lagi. Layanan ini
    /// menutup celah itu: beberapa saat setelah aplikasi dibuka — lalu tiap
    /// interval yang dipilih pengguna di Pengaturan Aplikasi (bawaan enam jam;
    /// bisa 24 jam atau sekali saja saat dibuka) — ia membandingkan tahun
    /// terbaru pada cadangan lokal (<see cref="IdmSimpanan"/>, hasil muat
    /// terakhir halaman IDM) dengan jawaban portal, dan mengirim notifikasi
    /// lonceng bila ada tahun yang lebih baru.
    ///
    /// Batas yang disengaja:
    /// <list type="bullet">
    ///   <item>Hanya memantau bila sudah ada cadangan untuk desa aktif —
    ///         tanpa tahun pembanding, "tahun baru" tidak bisa ditentukan.</item>
    ///   <item>Tidak menulis cadangan angka dan tidak mengubah halaman. Yang
    ///         dicatat hanya hasil pemeriksaannya (waktu + tahun terbit
    ///         terbaru) supaya halaman bisa menampilkan indikator data
    ///         terbaru; notifikasi mengarahkan pengguna membuka halaman IDM,
    ///         yang memuat ulang seluruh tahun (termasuk riwayat tren).</item>
    ///   <item>Portal yang tidak terjangkau bukan kegagalan: putaran itu diam,
    ///         dan pemeriksaan berikutnya mencoba lagi.</item>
    /// </list>
    /// </summary>
    public class PemantauIdmBaru
    {
        /// <summary>Kunci tujuan notifikasi → halaman IDM (dipetakan MainWindowViewModel).</summary>
        internal const string TujuanMenu = "idm";

        /// <summary>Hasil satu putaran: tahun yang ditemukan bila ada yang lebih baru.</summary>
        internal sealed record TahunBaruIdm(string KodeDesa, int TahunTersimpan, int TahunBaru);
        /// <summary>Jeda setelah jendela utama tampil — tidak menahan penggambaran awal.</summary>
        internal static readonly TimeSpan JedaAwal = TimeSpan.FromSeconds(45);

        /// <summary>
        /// Jeda antar pemeriksaan BERULANG sesuai pilihan pengguna; null berarti
        /// tidak ada pengulangan (hanya sekali saat aplikasi dibuka). Dibaca
        /// ulang setiap putaran supaya perubahan pengaturan berlaku tanpa
        /// menutup aplikasi.
        /// </summary>
        internal static TimeSpan? JedaBerulang =>
            IntervalPemantauanIdmPrefs.JedaBerulang(IntervalPemantauanIdmPrefs.SaatIni);

        private readonly IIdmKemendesaKlien _klien;
        private readonly IIdmSimpananStore _simpanan;
        private readonly IIdmPemantauanStore _pemantauan;
        private readonly SettingsManager _pengaturan;
        private readonly WilayahDataStore _wilayah;
        private readonly NotificationService _notifikasi;
        private readonly Action<string, string, string?> _kirimNotifikasi;
        private readonly ILogger<PemantauIdmBaru> _logger;
        private readonly Func<DateTime> _jam;

        /// <summary>Kunci "kodeDesa/tahun" yang sudah diumumkan pada sesi ini.</summary>
        private readonly HashSet<string> _sudahDiumumkan = new(StringComparer.Ordinal);

        private System.Windows.Threading.DispatcherTimer? _timer;
        private bool _sedangMemuat;

        public PemantauIdmBaru(
            IIdmKemendesaKlien klien,
            IIdmSimpananStore simpanan,
            IIdmPemantauanStore pemantauan,
            SettingsManager pengaturan,
            WilayahDataStore wilayah,
            NotificationService notifikasi,
            ILogger<PemantauIdmBaru> logger,
            Func<DateTime>? jam = null,
            Action<string, string, string?>? kirimNotifikasi = null)
        {
            _klien = klien ?? throw new ArgumentNullException(nameof(klien));
            _simpanan = simpanan ?? throw new ArgumentNullException(nameof(simpanan));
            _pemantauan = pemantauan ?? throw new ArgumentNullException(nameof(pemantauan));
            _pengaturan = pengaturan ?? throw new ArgumentNullException(nameof(pengaturan));
            _wilayah = wilayah ?? throw new ArgumentNullException(nameof(wilayah));
            _notifikasi = notifikasi ?? throw new ArgumentNullException(nameof(notifikasi));
            _kirimNotifikasi = kirimNotifikasi ?? _notifikasi.Info;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _jam = jam ?? (() => DateTime.Now);
        }

        /// <summary>
        /// Mulai pemantauan (dipanggil dari OnStartup). Aman dipanggil
        /// berkali-kali. Timer hidup di thread UI, sehingga notifikasi langsung
        /// dikerjakan di sana tanpa Invoke tambahan.
        /// </summary>
        public void Mulai() => TerapkanPreferensi();

        /// <summary>
        /// Sesuaikan timer dengan pilihan interval yang tersimpan. Dipanggil
        /// saat aplikasi dibuka dan setiap kali pengguna mengganti pilihan di
        /// Pengaturan Aplikasi, sehingga:
        /// <list type="bullet">
        ///   <item>pilihan "hanya saat dibuka" menghentikan pengulangan —
        ///         pemeriksaan pertama yang sudah terjadwal (45 detik setelah
        ///         aplikasi dibuka) tetap berjalan sekali;</item>
        ///   <item>interval baru langsung dipakai pada jadwal berikutnya, tanpa
        ///         perlu menutup aplikasi;</item>
        ///   <item>memilih interval berulang setelah sempat memilih "hanya saat
        ///         dibuka" menjadwalkan pemeriksaan pertama lagi.</item>
        /// </list>
        /// </summary>
        public void TerapkanPreferensi()
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                _logger.LogWarning("Pemantau IDM tidak dimulai — tak ada Dispatcher (mode uji?).");
                return;
            }

            dispatcher.Invoke(() =>
            {
                var jeda = JedaBerulang;
                var aksi = AksiTimer(
                    timerPernahDibuat: _timer != null,
                    sedangAktif: _timer?.IsEnabled == true,
                    intervalSekarang: _timer?.Interval ?? JedaAwal,
                    jedaBerulang: jeda);

                switch (aksi)
                {
                    case AksiJadwalTimer.JadwalkanPemeriksaanPertama:
                        _timer = new System.Windows.Threading.DispatcherTimer { Interval = JedaAwal };
                        _timer.Tick += async (_, _) => await PeriksaSekaliAsync();
                        _timer.Start();
                        break;
                    case AksiJadwalTimer.HentikanPengulangan:
                        _timer!.Stop();
                        break;
                    case AksiJadwalTimer.MulaiUlangDenganJedaAwal:
                        _timer!.Interval = JedaAwal;
                        _timer.Start();
                        break;
                    case AksiJadwalTimer.GantiJeda:
                        _timer!.Interval = jeda!.Value;
                        break;
                    case AksiJadwalTimer.Pertahankan:
                    default:
                        break;
                }
            });
        }

        /// <summary>Keputusan yang harus diambil untuk timer pemantau.</summary>
        internal enum AksiJadwalTimer
        {
            /// <summary>Timer belum ada — jadwalkan pemeriksaan pertama (JedaAwal).</summary>
            JadwalkanPemeriksaanPertama,

            /// <summary>"Hanya saat dibuka" — hentikan pengulangan yang sedang menunggu.</summary>
            HentikanPengulangan,

            /// <summary>Jangan ubah apa pun (termasuk: biarkan pemeriksaan pertama yang sudah terjadwal).</summary>
            Pertahankan,

            /// <summary>Timer berhenti lalu dihidupkan lagi — mulai dari pemeriksaan pertama.</summary>
            MulaiUlangDenganJedaAwal,

            /// <summary>Timer sedang menunggu putaran berikutnya — pakai jeda baru sekarang.</summary>
            GantiJeda
        }

        /// <summary>
        /// Aturan jadwal murni (tanpa timer sungguhan) supaya perilaku
        /// penggantian interval bisa diuji tanpa menunggu berjam-jam:
        /// <list type="bullet">
        ///   <item>timer belum ada → selalu jadwalkan pemeriksaan PERTAMA, termasuk
        ///         pada "hanya saat dibuka" (sekali periksa, bukan nol kali);</item>
        ///   <item>"hanya saat dibuka" → hentikan pengulangan, tetapi jangan
        ///         membatalkan pemeriksaan pertama yang belum berjalan;</item>
        ///   <item>timer berhenti (mis. setelah mode "hanya saat dibuka") lalu
        ///         pengguna memilih interval berulang → mulai lagi dari JedaAwal;</item>
        ///   <item>timer sedang menunggu putaran berikutnya → ganti jedanya saat itu juga.</item>
        /// </list>
        /// </summary>
        internal static AksiJadwalTimer AksiTimer(
            bool timerPernahDibuat, bool sedangAktif, TimeSpan intervalSekarang, TimeSpan? jedaBerulang)
        {
            if (!timerPernahDibuat)
            {
                return AksiJadwalTimer.JadwalkanPemeriksaanPertama;
            }

            bool menungguJedaAwal = intervalSekarang == JedaAwal;
            if (jedaBerulang is null)
            {
                return menungguJedaAwal
                    ? AksiJadwalTimer.Pertahankan
                    : AksiJadwalTimer.HentikanPengulangan;
            }

            if (!sedangAktif)
            {
                return AksiJadwalTimer.MulaiUlangDenganJedaAwal;
            }

            return menungguJedaAwal
                ? AksiJadwalTimer.Pertahankan
                : AksiJadwalTimer.GantiJeda;
        }

        /// <summary>Berhenti (dipanggil saat aplikasi menutup).</summary>
        public void Berhenti() => _timer?.Stop();

        /// <summary>
        /// Satu putaran pemeriksaan: cadangan lokal → tahun yang belum ada →
        /// tanya portal dari tahun berjalan mundur → umumkan yang ditemukan.
        /// Tidak pernah melempar; guard <c>_sedangMemuat</c> mencegah dua
        /// putaran tumpang-tindih bila jawaban portal lambat.
        /// </summary>
        internal async Task<TahunBaruIdm?> PeriksaSekaliAsync()
        {
            if (_sedangMemuat) return null;

            _sedangMemuat = true;
            try
            {
                // Selesai satu putaran: jadwalkan pengulangan sesuai pilihan
                // pengguna, atau berhenti bila ia memilih "hanya saat dibuka".
                if (_timer != null && _timer.Interval == JedaAwal)
                {
                    if (JedaBerulang is { } jeda) _timer.Interval = jeda;
                    else _timer.Stop();
                }

                var cadangan = _simpanan.Muat();
                if (cadangan?.Hasil is not { Count: > 0 }
                    || string.IsNullOrWhiteSpace(cadangan.KodeDesa))
                {
                    // Belum pernah memuat halaman IDM untuk desa ini — tidak ada
                    // tahun pembanding, jadi tidak ada yang bisa diumumkan.
                    return null;
                }

                // Cadangan harus milik desa yang SEDANG dipilih. Bila pengguna
                // mengganti desa di Pengaturan Surat, cadangan lama belum tentu
                // sudah tergantikan — jangan mengumumkan tahun baru desa lain.
                string kodeAktif = await IdmDesaAktif.KodeAsync(_pengaturan, _wilayah);
                if (kodeAktif.Length == 0
                    || !string.Equals(cadangan.KodeDesa, kodeAktif, StringComparison.Ordinal))
                {
                    return null;
                }

                int tahunTersimpan = cadangan.Hasil.Max(h => h.Ringkasan.Tahun);
                int tahunBerjalan = _jam().Year;
                if (tahunTersimpan >= tahunBerjalan) return null;

                int? tahunTerbit = null;
                for (int tahun = tahunBerjalan; tahun > tahunTersimpan; tahun--)
                {
                    var muatan = await _klien.MuatAsync(cadangan.KodeDesa, tahun);
                    if (muatan.Status == StatusMuatIdm.Gagal)
                    {
                        // Portal tidak terjangkau / jawaban tak dikenali. Bukan
                        // kegagalan yang perlu dilaporkan; coba lagi nanti. Catatan
                        // pemeriksaan TIDAK ditulis: pemeriksaan belum selesai.
                        return null;
                    }

                    if (muatan.Status != StatusMuatIdm.Berhasil || muatan.Hasil == null)
                    {
                        continue;
                    }

                    tahunTerbit = muatan.Hasil.Ringkasan.Tahun;
                    break;
                }

                // Portal menjawab seluruh rentang: bila tak ada yang baru, tahun
                // terbit terbaru tetap sama dengan data tersimpan.
                _pemantauan.Simpan(new IdmPemantauan
                {
                    WaktuPeriksa = _jam(),
                    TahunTerbit = tahunTerbit ?? tahunTersimpan,
                });

                if (tahunTerbit is int baru)
                {
                    Umumkan(cadangan.KodeDesa, tahunTersimpan, baru);
                    return new TahunBaruIdm(cadangan.KodeDesa, tahunTersimpan, baru);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Pemeriksaan data IDM tahun baru gagal.");
            }
            finally
            {
                _sedangMemuat = false;
            }

            return null;
        }

        /// <summary>Judul notifikasi lonceng untuk satu tahun baru.</summary>
        internal static string JudulNotifikasi(int tahunBaru) =>
            "Data IDM tahun " + tahunBaru + " sudah terbit";

        /// <summary>Isi notifikasi: tahun baru, tahun tersimpan, dan jalan keluarnya.</summary>
        internal static string PesanNotifikasi(int tahunTersimpan, int tahunBaru) =>
            "Portal pusat kini memuat penilaian IDM tahun " + tahunBaru
            + " — yang tersimpan di perangkat masih tahun " + tahunTersimpan
            + ". Buka halaman IDM untuk memuat angka terbaru.";

        /// <summary>Kirim notifikasi sekali per desa+tahun pada sesi ini.</summary>
        private void Umumkan(string kodeDesa, int tahunTersimpan, int tahunBaru)
        {
            if (tahunBaru <= tahunTersimpan) return;
            if (!_sudahDiumumkan.Add(kodeDesa + "/" + tahunBaru)) return;

            _kirimNotifikasi(
                JudulNotifikasi(tahunBaru),
                PesanNotifikasi(tahunTersimpan, tahunBaru),
                TujuanMenu);

            _logger.LogInformation(
                "Data IDM tahun {Tahun} terdeteksi di portal pusat untuk desa {Kode} "
                + "(sebelumnya tahun {Sebelumnya}).",
                tahunBaru, kodeDesa, tahunTersimpan);
        }
    }
}
