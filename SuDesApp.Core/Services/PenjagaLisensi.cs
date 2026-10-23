using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SuDesApp.Services
{
    /// <summary>
    /// Keadaan lisensi seperti yang ditampilkan ke pengguna: apakah aplikasi
    /// ini sudah berlangganan, masih mode Demo, atau sudah berakhir (baca-saja).
    /// </summary>
    public enum KeadaanLisensi
    {
        /// <summary>Belum pernah berlangganan dan belum mulai trial — fitur vital terkunci.</summary>
        Demo,

        /// <summary>Trial 14 hari lokal sedang berjalan — fitur terbuka sementara.</summary>
        Trial,

        /// <summary>Langganan aktif menurut token bertanda tangan yang terverifikasi.</summary>
        Berlangganan,

        /// <summary>Langganan berakhir ATAU trial lokal sudah habis — hanya baca.</summary>
        BacaSaja
    }

    /// <summary>
    /// Dari mana masa uji coba yang baru diminta berasal. Trial SERVER selalu
    /// didahulukan bila desa sudah punya kode aktivasi; trial lokal hanya
    /// dipakai sebagai cadangan saat server tidak terjangkau (atau belum ada
    /// kode sama sekali).
    /// </summary>
    public enum AsalPercobaan
    {
        /// <summary>Langganan sudah aktif — trial tidak diperlukan.</summary>
        SudahAktif,

        /// <summary>Trial diterima server; token bertanda tangannya sudah terpasang.</summary>
        Server,

        /// <summary>Trial lokal 14 hari (tanpa kode aktivasi, atau server tak terjangkau).</summary>
        Lokal,

        /// <summary>Trial lokal di perangkat ini sudah pernah dipakai dan tidak bisa diulang.</summary>
        SudahPernahTrial,

        /// <summary>Server menjawab dan menolak (mis. trial desa sudah dipakai) — tanpa cadangan lokal.</summary>
        DitolakServer,

        /// <summary>Server menerima trial, tetapi tokennya belum diterima klien (mis. koneksi putus).</summary>
        ServerTertunda
    }

    /// <summary>
    /// Hasil permintaan masa uji coba: asalnya, pesan penolakan server bila ada,
    /// dan batas berlakunya (UTC) supaya pemanggil bisa menyebut tanggalnya.
    /// </summary>
    public sealed record HasilPercobaan(
        AsalPercobaan Asal,
        string? Pesan = null,
        DateTime? BerlakuSampai = null);

    /// <summary>
    /// Pengecualian khusus untuk tindakan yang diblokir mode baca-saja karena
    /// langganan berakhir. Diturunkan dari InvalidOperationException supaya
    /// penangan galat yang sudah ada tetap berlaku, tetapi pemanggil bisa
    /// menampilkan pesan yang tepat (arah ke menu Langganan) tanpa menebak
    /// dari teks.
    /// </summary>
    public sealed class KunciLisensiException : InvalidOperationException
    {
        public KunciLisensiException()
            : base(PenjagaLisensi.PesanAksesDikunci)
        {
        }

        public KunciLisensiException(string pesan) : base(pesan)
        {
        }
    }

    /// <summary>
    /// Penjaga gerbang fitur berdasarkan status lisensi — satu-satunya tempat
    /// memutuskan "aplikasi masih bisa dipakai penuh atau hanya bisa dibaca".
    ///
    /// ATURAN (alur uji coba 14 hari):
    /// 1. Langganan berbayar yang sah → <see cref="KeadaanLisensi.Berlangganan"/>,
    ///    semua menu terbuka.
    /// 2. Trial 14 hari lokal yang masih berjalan → <see cref="KeadaanLisensi.Trial"/>,
    ///    semua menu terbuka sementara.
    /// 3. Instalasi baru yang belum pernah memulai trial → <see cref="KeadaanLisensi.Demo"/>,
    ///    seluruh menu vital TERKUNCI dan aplikasi menawarkan memulai trial.
    /// 4. Trial sudah berakhir (atau langganan berakhir) → <see cref="KeadaanLisensi.BacaSaja"/>,
    ///    menu vital tetap TERKUNCI sampai pembayaran lisensi selesai.
    ///
    /// <para>Selain gerbang fitur, kelas ini juga sumber tunggal untuk chip
    /// status di menu utama: <see cref="Keadaan"/> membedakan Demo / Trial /
    /// Full Service / Baca-saja dari sumber yang sama, sehingga chip tidak bisa
    /// berbeda dari perilaku aplikasinya.</para>
    ///
    /// Nilai diperbarui oleh detak lisensi (App.OnStartup → pemantauan berkala)
    /// dan oleh halaman Langganan setiap kali status dicek ulang. Cache lokal
    /// bisa diterapkan seketika (<see cref="SegarkanDariCache"/>) sebelum cek
    /// online selesai, supaya gerbang sudah aktif sejak aplikasi dibuka.
    /// </summary>
    public sealed class PenjagaLisensi
    {
        /// <summary>
        /// Pesan standar ketika sebuah fitur terkunci — dipakai bersama oleh
        /// semua gerbang (Demo maupun BacaSaja), jadi bunyinya menyebut kedua
        /// jalan keluar: mulai trial atau bayar lisensi.
        /// </summary>
        public const string PesanAksesDikunci =
            "Fitur ini terkunci karena lisensi belum aktif. Data tetap bisa dibaca, " +
            "tetapi membuat dan mencetak surat baru dikunci. " +
            "Mulai trial 14 hari atau aktifkan langganan lewat menu Langganan.";

        /// <summary>Pesan saat instalasi baru belum memulai trial sama sekali.</summary>
        public const string PesanBelumTrial =
            "Menu vital aplikasi masih terkunci. Mulai trial gratis 14 hari untuk membuka " +
            "seluruh fitur, atau aktifkan lisensi berbayar lewat menu Langganan.";

        /// <summary>Pertanyaan sebelum trial lokal dimulai (dipakai popup startup &amp; menu).</summary>
        public const string PesanMulaiTrial =
            "Mulai trial gratis 14 hari sekarang? Selama masa uji coba seluruh menu vital terbuka. " +
            "Bila trial berakhir tanpa pembayaran, menu vital terkunci kembali dan aplikasi hanya bisa dibaca.";

        /// <summary>Pesan saat trial 14 hari sudah habis tanpa pembayaran.</summary>
        public const string PesanTrialHabis =
            "Masa uji coba 14 hari sudah berakhir. Menu vital aplikasi terkunci dan data hanya bisa " +
            "dibaca. Selesaikan pembayaran lisensi lewat menu Langganan untuk membuka kembali seluruh fitur.";

        /// <summary>
        /// Tooltip singkat menu vital yang terkunci saat trial belum dimulai —
        /// tampil di sidebar supaya penguncian terlihat SEBELUM menu diklik.
        /// </summary>
        public const string TooltipTerkunciDemo =
            "menu vital terkunci — klik untuk mulai trial 14 hari";

        /// <summary>Tooltip singkat menu vital yang terkunci karena trial/langganan berakhir.</summary>
        public const string TooltipTerkunciBacaSaja =
            "menu vital terkunci — klik untuk menyelesaikan pembayaran lisensi";

        /// <summary>
        /// Pilih kalimat tooltip yang tepat untuk keadaan terkunci: instalasi baru
        /// ditawari trial, trial/langganan habis diarahkan membayar. Keadaan lain
        /// tidak pernah memanggil ini (menu hanya ditandai saat terkunci).
        /// </summary>
        public static string TooltipTerkunci(KeadaanLisensi keadaan) =>
            keadaan == KeadaanLisensi.Demo ? TooltipTerkunciDemo : TooltipTerkunciBacaSaja;

        /// <summary>
        /// Rute menu yang masuk kategori VITAL: membuat/mengubah data administrasi
        /// dan mencetak surat. Membaca data (Register, Riwayat, Beranda), halaman
        /// Langganan, pencadangan/pemulihan, dan bantuan sengaja tetap terbuka —
        /// mengunci semuanya akan membuat data desa tidak bisa diselamatkan.
        /// </summary>
        private static readonly HashSet<string> RuteVital = new(StringComparer.OrdinalIgnoreCase)
        {
            "template-surat",
            "rekening-koran",
            "ntcr-paket",
            "daftar-hadir",
            "warga",
            "perangkat-desa",
            "sotk-desa",
            "apbdes",
            "idm",
            "pengaturan-surat",
            "formulir",
            "pengaturan-formulir",
            "penomoran-surat",
            "kelola-pengguna"
        };

        /// <summary>Awalan rute vital yang membawa parameter (jenis surat/keputusan/agenda/formulir).</summary>
        private static readonly string[] AwalanRuteVital =
        {
            "surat:",
            "keputusan:",
            "agenda:",
            "template-pdf:"
        };

        /// <summary>
        /// Benar bila rute menu termasuk menu vital yang wajib terkunci selama
        /// lisensi belum aktif. Aturannya ada di Core supaya gerbang navigasi dan
        /// pengujian membaca daftar yang sama.
        /// </summary>
        public static bool ApakahMenuVital(string? route)
        {
            if (string.IsNullOrWhiteSpace(route)) return false;
            if (RuteVital.Contains(route)) return true;
            return AwalanRuteVital.Any(awalan =>
                route.StartsWith(awalan, StringComparison.OrdinalIgnoreCase));
        }

        private readonly LisensiManager _lisensi;
        private readonly TrialLokal _trial;
        private readonly ILogger<PenjagaLisensi> _logger;

        /// <summary>
        /// Klien endpoint trial server. Opsional supaya pengujian dan pemakaian
        /// tanpa server (mis. alat CLI) tetap bisa membuat penjaga ini; tanpa
        /// klien, permintaan trial langsung memakai trial lokal.
        /// </summary>
        private readonly LanggananKlien? _klien;
        private volatile KeadaanLisensi _keadaan;

        /// <summary>Sisa hari trial terakhir yang dilaporkan — supaya chip ikut berubah saat harinya berkurang.</summary>
        private int _sisaTrialTerakhir = -1;

        public PenjagaLisensi(
            LisensiManager lisensi,
            TrialLokal trial,
            ILogger<PenjagaLisensi> logger,
            LanggananKlien? klien = null)
        {
            _lisensi = lisensi ?? throw new ArgumentNullException(nameof(lisensi));
            _trial = trial ?? throw new ArgumentNullException(nameof(trial));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _klien = klien;
        }

        /// <summary>
        /// Satu-satunya tempat memutuskan keadaan yang ditampilkan/diberlakukan.
        /// Urutan cabangnya penting: langganan aktif menang atas trial, trial
        /// aktif menang atas "terkunci", dan instalasi yang belum pernah trial
        /// dibedakan dari trial yang sudah habis supaya popup yang ditampilkan
        /// tepat (ajakan memulai trial vs kewajiban membayar).
        /// Murni &amp; statis supaya aturannya diuji tanpa LisensiManager.
        /// </summary>
        public static KeadaanLisensi HitungKeadaan(
            bool statusAktif,
            bool trialAktif,
            bool trialPernahMulai,
            bool punyaKodeAktivasi,
            bool kunciTanpaLangganan)
        {
            if (statusAktif) return KeadaanLisensi.Berlangganan;
            if (trialAktif) return KeadaanLisensi.Trial;
            if (trialPernahMulai || punyaKodeAktivasi || kunciTanpaLangganan)
            {
                return KeadaanLisensi.BacaSaja;
            }

            return KeadaanLisensi.Demo;
        }

        /// <summary>
        /// Benar bila keadaan ini terkunci — Demo (belum mulai trial) maupun
        /// BacaSaja (trial/langganan berakhir). Turunan dari aturan yang sama,
        /// bukan aturan kedua.
        /// </summary>
        public static bool HitungTerkunci(KeadaanLisensi keadaan) =>
            keadaan is KeadaanLisensi.Demo or KeadaanLisensi.BacaSaja;

        /// <summary>
        /// Label chip untuk keadaan ini. Ada di Core, bersebelahan dengan
        /// aturannya, supaya teks yang dilihat pengguna dan gerbang fitur tidak
        /// bisa menyimpang — dan bisa diuji tanpa UI.
        /// </summary>
        public static string LabelKeadaan(KeadaanLisensi keadaan) => keadaan switch
        {
            KeadaanLisensi.Berlangganan => "Full Service",
            KeadaanLisensi.Trial => "Trial",
            KeadaanLisensi.BacaSaja => "Baca-saja",
            _ => "Demo"
        };

        /// <summary>Nada warna chip: 1 = aktif (hijau), 2 = Demo/Trial (kuning), 3 = baca-saja (merah).</summary>
        public static int NadaKeadaan(KeadaanLisensi keadaan) => keadaan switch
        {
            KeadaanLisensi.Berlangganan => 1,
            KeadaanLisensi.BacaSaja => 3,
            _ => 2
        };

        /// <summary>
        /// Label chip LENGKAP untuk menu utama: label keadaan plus sisa hari
        /// selama masih ada. Sisa hari dicetak langsung di chip — bukan hanya
        /// di tooltip — supaya masa aktif terbaca sekilas tanpa menahan kursor.
        /// Angkanya sama dengan yang dipakai gerbang (<c>StatusLisensi.SisaHariTenggang</c>),
        /// jadi chip tidak bisa menampilkan angka lain dari perilaku aplikasinya.
        /// </summary>
        public static string LabelChip(KeadaanLisensi keadaan, int sisaHari) =>
            sisaHari > 0
                ? $"{LabelKeadaan(keadaan)} • sisa {sisaHari} hari"
                : LabelKeadaan(keadaan);

        /// <summary>Keadaan lisensi terakhir yang diketahui (sumber chip status di menu utama).</summary>
        public KeadaanLisensi Keadaan => _keadaan;

        /// <summary>
        /// Status lengkap yang terakhir diterapkan — dipakai menampilkan tanggal
        /// berakhir di tooltip. Null sebelum status pertama diterapkan.
        /// </summary>
        public StatusLisensi? Status { get; private set; }

        /// <summary>
        /// True bila menu vital sedang terkunci (Demo atau BacaSaja). Nama lama
        /// dipertahankan supaya seluruh gerbang fitur yang sudah ada otomatis
        /// berlaku untuk instalasi baru juga.
        /// </summary>
        public bool BacaSaja => Terkunci;

        /// <summary>Benar bila menu vital sedang terkunci.</summary>
        public bool Terkunci => HitungTerkunci(_keadaan);

        /// <summary>Benar bila trial 14 hari lokal masih berjalan.</summary>
        public bool TrialAktif => _trial.Aktif();

        /// <summary>Benar bila trial pernah dimulai pada perangkat ini (mungkin sudah habis).</summary>
        public bool TrialPernahDimulai => _trial.SudahMulai;

        /// <summary>Sisa hari trial (0 bila habis atau belum dimulai).</summary>
        public int SisaHariTrial => _trial.SisaHari();

        /// <summary>Batas akhir trial (UTC), null bila belum pernah dimulai.</summary>
        public DateTime? TrialBerakhirUtc => _trial.BerakhirUtc;

        /// <summary>Benar bila kode aktivasi terpasang pada instalasi ini.</summary>
        public bool PunyaKodeAktivasi => !string.IsNullOrWhiteSpace(_lisensi.KodeAktivasi);

        /// <summary>
        /// Bila true, instalasi tanpa kode aktivasi ikut dikunci. Properti ini
        /// dipertahankan untuk kompatibilitas konfigurasi lama; sejak alur trial
        /// 14 hari diberlakukan, instalasi baru SELALU terkunci sampai trial
        /// dimulai atau lisensi dibayar.
        /// </summary>
        public bool KunciTanpaLangganan { get; set; }

        /// <summary>Dibangkitkan di thread siapa pun ketika keadaan lisensi berubah.</summary>
        public event Action? Berubah;

        /// <summary>
        /// Terapkan status yang baru diketahui (dari server atau cache). Mode
        /// berubah hanya bila hasil perhitungan berbeda dari keadaan sekarang —
        /// memanggil berulang tidak memicu event lagi.
        /// </summary>
        public void Terapkan(StatusLisensi? status)
        {
            var keadaanBaru = HitungKeadaan(
                status?.Aktif == true,
                _trial.Aktif(),
                _trial.SudahMulai,
                !string.IsNullOrWhiteSpace(_lisensi.KodeAktivasi),
                KunciTanpaLangganan);
            // Tanggal berakhir ikut menentukan: token yang diperbarui detak
            // lisensi bisa menggeser tanggalnya tanpa mengubah keadaan, dan
            // chip yang menampilkan tanggal basi itu informasi yang salah.
            // Sisa hari trial juga ikut: chip "Trial • sisa N hari" harus
            // menyusut walau keadaan (Trial) tetap sama.
            int sisaTrial = _trial.SisaHari();
            var infoBerubah = Status?.Aktif != status?.Aktif ||
                              Status?.BerlakuSampai != status?.BerlakuSampai ||
                              sisaTrial != _sisaTrialTerakhir;
            _sisaTrialTerakhir = sisaTrial;
            Status = status;

            if (keadaanBaru == _keadaan && !infoBerubah) return;

            _keadaan = keadaanBaru;
            _logger.LogInformation(
                "Keadaan lisensi menjadi {Keadaan} (terkunci: {Terkunci}, sisa trial: {Sisa} hari).",
                keadaanBaru, HitungTerkunci(keadaanBaru), sisaTrial);
            Berubah?.Invoke();
        }

        /// <summary>
        /// Mulai masa uji coba dengan urutan yang benar:
        ///
        /// 1. Langganan sudah aktif → tidak ada yang perlu dilakukan.
        /// 2. Kode aktivasi ada DAN server terjangkau → <b>trial server</b>
        ///    (sekali per desa, token bertanda tangan, batas perangkat).
        /// 3. Kode belum ada, ATAU server tidak terjangkau → <b>trial lokal</b>
        ///    14 hari sebagai cadangan offline.
        ///
        /// Server yang MENJAWAB dan menolak (mis. trial desa sudah dipakai)
        /// sengaja TIDAK diberi cadangan lokal: kalau diberi, desa yang sudah
        /// pernah trial bisa mengumpulkan 28 hari dari dua jalur yang berbeda —
        /// padahal maksud produknya 14 hari per desa. Penolakan karena gangguan
        /// sesaat (5xx, 404 endpoint lama, timeout, 408/429) tetap dianggap
        /// "tidak terjangkau" supaya desa tidak terkunci karena server bermasalah.
        /// </summary>
        public async Task<HasilPercobaan> MulaiPercobaanAsync(CancellationToken ct = default)
        {
            if (Status?.Aktif == true)
            {
                return new HasilPercobaan(
                    AsalPercobaan.SudahAktif, null, Status.SahSampai ?? Status.BerlakuSampai);
            }

            if (_trial.SudahMulai)
            {
                return new HasilPercobaan(AsalPercobaan.SudahPernahTrial);
            }

            if (PunyaKodeAktivasi && _klien is not null)
            {
                try
                {
                    var hasil = await _klien
                        .MulaiTrialAsync(_lisensi.KodeAktivasi!, _lisensi.IdPerangkat, ct)
                        .ConfigureAwait(false);

                    if (hasil.Aktif)
                    {
                        // Server menerima; token trial diambil lewat jalur biasa
                        // supaya cache & verifikasinya sama dengan langganan berbayar.
                        var status = await _lisensi.CekAsync(ct).ConfigureAwait(false);
                        Terapkan(status);

                        if (status.Aktif)
                        {
                            _logger.LogInformation(
                                "Trial server aktif sampai {Sampai:O}.", status.BerlakuSampai);
                            return new HasilPercobaan(
                                AsalPercobaan.Server, null, status.SahSampai ?? status.BerlakuSampai);
                        }

                        _logger.LogWarning(
                            "Server menerima trial tetapi token belum diterima; trial lokal tidak diberikan.");
                        return new HasilPercobaan(AsalPercobaan.ServerTertunda, hasil.Pesan);
                    }

                    if (MenolakFinal(hasil.StatusHttp))
                    {
                        _logger.LogInformation(
                            "Server menolak trial (HTTP {Status}); trial lokal tidak diberikan.",
                            hasil.StatusHttp);
                        return new HasilPercobaan(AsalPercobaan.DitolakServer, hasil.Pesan);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    _logger.LogWarning(ex,
                        "Server trial tidak terjangkau; memakai trial lokal sebagai cadangan.");
                }
            }

            if (!_trial.MulaiSekarang())
            {
                return new HasilPercobaan(AsalPercobaan.SudahPernahTrial);
            }

            _logger.LogInformation("Trial 14 hari lokal dimulai; berlaku sampai {Sampai:O}.",
                _trial.BerakhirUtc);
            Terapkan(Status);
            return new HasilPercobaan(AsalPercobaan.Lokal, null, _trial.BerakhirUtc);
        }

        /// <summary>
        /// Benar bila status HTTP itu penolakan yang PASTI dari server (bukan
        /// gangguan sesaat), sehingga trial lokal tidak boleh menggantikannya.
        /// 404/408/429 dan seluruh 5xx dianggap gangguan: endpoint lama belum
        /// ada, permintaan timeout, atau server sedang sibuk.
        /// </summary>
        internal static bool MenolakFinal(int statusHttp) =>
            statusHttp is >= 400 and < 500 and not 404 and not 408 and not 429;

        /// <summary>
        /// Judul &amp; kalimat untuk hasil <see cref="MulaiPercobaanAsync"/>, supaya
        /// popup startup dan klik menu terkunci menyampaikan hal yang sama.
        /// </summary>
        public static (string Judul, string Pesan) JelaskanPercobaan(HasilPercobaan hasil)
        {
            ArgumentNullException.ThrowIfNull(hasil);

            return hasil.Asal switch
            {
                AsalPercobaan.Server => (
                    "Trial server aktif",
                    "Server lisensi menerima masa uji coba untuk desa ini. Seluruh menu vital sudah terbuka. " +
                    "Setelah trial berakhir, selesaikan pembayaran di menu Langganan."),

                AsalPercobaan.Lokal => (
                    "Trial lokal aktif",
                    "Server lisensi belum terjangkau, jadi masa uji coba 14 hari dicatat di komputer ini. " +
                    "Seluruh menu vital sudah terbuka. Setelah trial berakhir, selesaikan pembayaran di menu Langganan."),

                AsalPercobaan.ServerTertunda => (
                    "Trial diterima server",
                    "Server menerima trial, tetapi tokennya belum sampai ke komputer ini. Sambungkan internet lalu " +
                    "tekan Cek Status di halaman Langganan."),

                AsalPercobaan.DitolakServer => (
                    "Trial tidak diberikan",
                    string.IsNullOrWhiteSpace(hasil.Pesan)
                        ? "Server menolak permintaan trial — kemungkinan trial desa ini sudah pernah dipakai. " +
                          "Lanjutkan dengan pembayaran lisensi lewat menu Langganan."
                        : hasil.Pesan!),

                AsalPercobaan.SudahPernahTrial => ("Trial sudah pernah dipakai", PesanTrialHabis),

                _ => ("Lisensi sudah aktif", "Langganan desa sudah aktif; masa uji coba tidak diperlukan.")
            };
        }

        /// <summary>
        /// Mulai trial 14 hari lokal. Mengembalikan true bila trial benar-benar
        /// baru dimulai — trial tidak bisa diulang, dan langganan aktif tidak
        /// perlu trial. Keadaan gerbang langsung diperbarui sehingga menu vital
        /// terbuka tanpa perlu menunggu cek lisensi berikutnya.
        ///
        /// Dipakai jalur yang memang TIDAK boleh menyentuh server; alur popup dan
        /// menu terkunci memakai <see cref="MulaiPercobaanAsync"/> yang mendahulukan
        /// trial server.
        /// </summary>
        public bool MulaiTrialLokal()
        {
            if (Status?.Aktif == true || _trial.SudahMulai) return false;
            if (!_trial.MulaiSekarang()) return false;

            _logger.LogInformation("Trial 14 hari lokal dimulai; berlaku sampai {Sampai:O}.",
                _trial.BerakhirUtc);
            Terapkan(Status);
            return true;
        }

        /// <summary>Terapkan status dari cache lokal tanpa jaringan (cepat, disebut saat startup).</summary>
        public void SegarkanDariCache() => Terapkan(_lisensi.BacaCachePublik());
    }
}