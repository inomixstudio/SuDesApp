using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SuDesApp.Services
{
    /// <summary>
    /// Satu kartu paket langganan di halaman Langganan: kode yang dikirim ke
    /// server, teks yang dilihat pengguna, dan angka resmi server begitu
    /// katalognya diketahui.
    ///
    /// Angka harga SELALU berasal dari server (lihat <see cref="LanggananKlien"/>):
    /// kartu tanpa angka berarti server belum menjawab, bukan berarti gratis.
    /// Teks cadangan (<see cref="HariCadangan"/>, <see cref="CatatanCadangan"/>)
    /// ada hanya supaya kartu tidak kosong saat server belum terjangkau, dan
    /// sengaja sama dengan katalog bawaan server supaya tampilan tidak berubah
    /// begitu angka resminya datang.
    /// </summary>
    public sealed record PaketLanggananKlien(
        string Kode, string Label, int HariCadangan, string CatatanCadangan)
    {
        /// <summary>Harga resmi server; null selama katalog belum diketahui.</summary>
        public long? Harga { get; init; }

        /// <summary>Masa aktif resmi server; null selama katalog belum diketahui.</summary>
        public int? Hari { get; init; }

        /// <summary>Benar bila kartu sudah memakai angka resmi server.</summary>
        public bool HargaDiketahui => Harga is > 0 && Hari is > 0;

        /// <summary>Angka resmi server ("Rp150.000") atau teks jujur tanpa angka.</summary>
        public string HargaTampil => Harga is > 0 ? Rupiah.Format(Harga) : "Harga dari server";

        /// <summary>
        /// Harga per hari dari angka resmi server ("≈ Rp2,740/hari" untuk
        /// Rp1.200.000 / 365 hari) — pembanding jujur antar paket yang berbeda
        /// durasi. Kosong sampai angkanya diketahui.
        /// </summary>
        public string HargaPerHariTampil
        {
            get
            {
                if (!HargaDiketahui)
                {
                    return string.Empty;
                }

                long perHari = (long)Math.Round((double)Harga!.Value / Hari!.Value, MidpointRounding.AwayFromZero);
                return "≈ " + Rupiah.Format(perHari) + "/hari";
            }
        }

        /// <summary>Benar bila harga per hari bisa ditampilkan (angka server lengkap).</summary>
        public bool AdaHargaPerHari => HargaDiketahui;

        /// <summary>Masa aktif — angka server bila ada, teks cadangan bila belum.</summary>
        public string DurasiTampil => (Hari is > 0 ? Hari.Value : HariCadangan) + " hari";

        /// <summary>Salinan dengan angka dari katalog; entri tak masuk akal diabaikan.</summary>
        public PaketLanggananKlien DenganAngka(long harga, int hari) =>
            harga > 0 && hari > 0 ? this with { Harga = harga, Hari = hari } : this;
    }

    /// <summary>
    /// Katalog paket yang dikenali aplikasi ini beserta angka resmi server yang
    /// sudah diketahui — satu sumber untuk semua kebijakan tampilan kartu: mana
    /// yang ditandai "paling hemat", berapa persen hematnya, dan teks cadangan
    /// saat angkanya belum datang.
    ///
    /// Ada di Core (bukan di ViewModel) supaya kebijakan harga bisa diuji tanpa
    /// membuka jendela, dan supaya halaman Langganan tinggal menyalin hasilnya.
    /// Harga tetap milik server: kelas ini tidak pernah menghitung nominal yang
    /// akan ditagih, hanya menampilkan angka yang dikirim server.
    /// </summary>
    public sealed class KatalogLangganan
    {
        /// <summary>Budaya format persentase — dipatok id-ID supaya angka
        /// penghematan selalu memakai pemisah desimal yang sama di setiap
        /// komputer, apa pun locale Windows yang dipakai.</summary>
        private static readonly CultureInfo Indonesia = CultureInfo.GetCultureInfo("id-ID");

        private KatalogLangganan(IReadOnlyList<PaketLanggananKlien> kartu) => Kartu = kartu;

        /// <summary>
        /// Paket yang dikenali aplikasi ini tanpa angka (kode + teks cadangan).
        /// Urutannya adalah urutan kartu di halaman Langganan.
        /// </summary>
        public static KatalogLangganan Bawaan { get; } = new(new[]
        {
            // Tiga paket berbayar, urut dari yang paling pendek. TRIAL 30 hari
            // tidak ada di sini karena gratis — ia tombol terpisah yang tidak
            // pernah melewati tagihan Midtrans (LanggananKlien.MulaiTrialAsync).
            //
            // Angka di kartu tetap dikonfirmasi server (GET /paket); teks di
            // bawah ini hanya agar kartu tidak kosong saat server belum
            // terjangkau, dan durasinya sengaja sama dengan katalog bawaan
            // server supaya tampilan tidak melompat begitu angka resmi datang.
            new PaketLanggananKlien("bulanan", "Bulanan", 30,
                "Akses penuh semua fitur — diperpanjang tiap 30 hari"),
            new PaketLanggananKlien("enambulan", "6 Bulan", 180,
                "Akses penuh 6 bulan — lebih hemat per hari daripada bayar bulanan"),
            new PaketLanggananKlien("tahunan", "Tahunan", 365,
                "Akses penuh setahun — harga per hari paling murah")
        });

        /// <summary>Kartu paket, urut seperti di halaman.</summary>
        public IReadOnlyList<PaketLanggananKlien> Kartu { get; }

        /// <summary>Benar bila minimal satu kartu sudah memakai angka resmi server.</summary>
        public bool AdaHarga => Kartu.Any(k => k.HargaDiketahui);

        /// <summary>
        /// Nama paket untuk ditampilkan dari kode yang dilaporkan server.
        /// Kode <c>trial</c> sengaja TIDAK ada di <see cref="Kartu"/> karena
        /// gratis (tidak pernah lewat tagihan), jadi tanpa cabang ini kolom
        /// paket di halaman Langganan akan menampilkan "trial" mentah.
        /// Kode yang tidak dikenal ditampilkan apa adanya, bukan dikosongkan.
        /// </summary>
        public string LabelAktif(string? kode)
        {
            if (string.IsNullOrWhiteSpace(kode))
            {
                return "—";
            }

            string rapi = kode.Trim();
            if (string.Equals(rapi, "trial", StringComparison.OrdinalIgnoreCase))
            {
                return "Trial 30 hari";
            }

            return Cari(rapi)?.Label ?? rapi;
        }

        /// <summary>Kartu dengan kode tertentu; null bila kode itu tidak dikenal.</summary>
        public PaketLanggananKlien? Cari(string? kode) =>
            kode is null
                ? null
                : Kartu.FirstOrDefault(k => string.Equals(k.Kode, kode.Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Kode kartu yang ditandai "PALING HEMAT": harga per hari terendah dari
        /// angka resmi server. Sebelum angkanya datang, TAHUNAN yang ditandai —
        /// komitmen terpanjang memang yang paling murah per hari — dan tegasannya
        /// diperbarui begitu angka sebenarnya datang (mis. bila admin mengubah
        /// harga sehingga paket lain jadi termurah).
        /// </summary>
        public string? KodeDisorot
        {
            get
            {
                var termurah = Kartu
                    .Where(k => k.AdaHargaPerHari)
                    .OrderBy(k => (double)k.Harga!.Value / k.Hari!.Value)
                    .FirstOrDefault();

                return termurah?.Kode
                    ?? Cari("tahunan")?.Kode
                    ?? Kartu.LastOrDefault()?.Kode;
            }
        }

        /// <summary>
        /// Kalimat pembanding sebuah kartu. Bila harga bulanan dan paket ini
        /// sama-sama diketahui, tampilkan penghematan per hari yang SEBENARNYA
        /// dihitung dari angka server (bukan angka keras) — jadi bila admin
        /// mengubah harga, persen di kartu ikut berubah sendiri.
        /// </summary>
        public string Catatan(PaketLanggananKlien kartu)
        {
            var bulanan = Cari("bulanan");
            if (!string.Equals(kartu.Kode, "bulanan", StringComparison.OrdinalIgnoreCase) &&
                kartu.Harga is long harga && kartu.Hari is int hari && hari > 0 &&
                bulanan?.Harga is long hargaBulanan && bulanan.Hari is int hariBulanan && hariBulanan > 0)
            {
                double perHari = (double)harga / hari;
                double perHariBulanan = (double)hargaBulanan / hariBulanan;
                double hemat = (1 - perHari / perHariBulanan) * 100;
                if (hemat >= 1)
                {
                    return $"Hemat ±{hemat.ToString("F0", Indonesia)}% dibanding membayar tiap bulan";
                }
            }

            return kartu.CatatanCadangan;
        }

        /// <summary>
        /// Katalog baru dengan angka dari entri (bentuk JSON yang sama dengan
        /// katalog server dan salinan tersimpan). Kode yang tidak dikenal versi
        /// aplikasi ini dilewati; kode tak masuk akal diabaikan.
        /// </summary>
        public KatalogLangganan DenganAngka(IEnumerable<KatalogPaketTersimpan.Paket> entri)
        {
            var angka = new Dictionary<string, KatalogPaketTersimpan.Paket>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in entri)
            {
                if (p is null || string.IsNullOrWhiteSpace(p.Kode) || p.Harga <= 0 || p.Hari <= 0)
                {
                    continue;
                }

                angka[p.Kode.Trim()] = p;
            }

            if (angka.Count == 0)
            {
                return this;
            }

            return new KatalogLangganan(Kartu
                .Select(k => angka.TryGetValue(k.Kode, out var p) ? k.DenganAngka(p.Harga, p.Hari) : k)
                .ToList());
        }
    }

    /// <summary>Keadaan satu percobaan memuat katalog dari server.</summary>
    public enum HasilMuatKatalog
    {
        /// <summary>Server menjawab dan katalognya terpakai.</summary>
        Server = 0,

        /// <summary>Server menjawab tetapi tidak ada paket yang bisa dipakai (termasuk alamat server kosong).</summary>
        Kosong = 1,

        /// <summary>Server tidak terjangkau — percobaan berikutnya ditunda sebentar.</summary>
        Gagal = 2,

        /// <summary>Belum lewat jeda setelah kegagalan sebelumnya, jadi server tidak dihubungi.</summary>
        Dilewati = 3
    }

    /// <summary>Hasil pemuatan katalog: keadaan percobaan + katalognya bila ada.</summary>
    public sealed record HasilKatalog(HasilMuatKatalog Hasil, KatalogLangganan? Katalog)
    {
        internal static readonly HasilKatalog Kosong = new(HasilMuatKatalog.Kosong, null);
        internal static readonly HasilKatalog Dilewati = new(HasilMuatKatalog.Dilewati, null);
        internal static readonly HasilKatalog TidakTerjangkau = new(HasilMuatKatalog.Gagal, null);
    }

    /// <summary>
    /// Tagihan yang baru dibuat server. Semua angkanya dari server — klien tidak
    /// pernah menentukan nominal.
    /// </summary>
    public sealed record TagihanLangganan(string? OrderId, long? Nominal, int? Hari, string? LinkPembayaran)
    {
        /// <summary>Ringkasan untuk pesan status, mis. "Rp150.000 / 30 hari".</summary>
        public string RingkasanTampil => $"{Rupiah.Format(Nominal)} / {Hari} hari";
    }

    /// <summary>
    /// Hasil panggilan POST /buat-tagihan: tagihannya, atau tanpa tagihan +
    /// status HTTP penolakan server. Kegagalan jaringan tidak sampai ke sini —
    /// ia dilempar sebagai pengecualian supaya pemanggil bisa menampilkan
    /// pesannya sendiri.
    /// </summary>
    /// <summary>
    /// Hasil buat tagihan. <c>KodeAktivasi</c> TERISI hanya pada berlangganan
    /// awal — server menerbitkan kode baru untuk desa yang belum terdaftar;
    /// pada perpanjangan nilainya null (kode desa tidak berubah).
    /// </summary>
    public sealed record HasilTagihan(TagihanLangganan? Tagihan, int StatusHttp, string? KodeAktivasi = null)
    {
        internal static HasilTagihan Ditolak(int statusHttp) => new(null, statusHttp);
    }

    /// <summary>
    /// Klien endpoint langganan publik di server lisensi: katalog harga
    /// (<c>GET /paket</c>) dan pembuatan tagihan (<c>POST /buat-tagihan</c>).
    ///
    /// Kenapa dipisah dari halaman Langganan: urusan server (alamat, bentuk JSON,
    /// jeda coba ulang, salinan katalog di disk) bukan urusan tampilan. Halaman
    /// Langganan hanya menyalin hasilnya ke properti yang diikat XAML, sehingga
    /// perubahan bentuk balasan server tidak menyentuh ViewModel.
    ///
    /// Alamat server dimiliki <see cref="LisensiManager"/> — kelas ini memakai
    /// alamat yang sama lewat <see cref="LisensiManager.AlamatEndpoint"/>, jadi
    /// tidak ada dua tempat yang bisa menyimpang soal server mana yang dituju.
    /// Kode aktivasi sengaja dikirim pemanggil (halaman Langganan yang menyimpan
    /// dan mengujinya), bukan dibaca ulang dari manajer.
    ///
    /// Bukan operasi keamanan: katalog hanya untuk tampilan, dan nominal tagihan
    /// selalu diteguhkan ulang server dari katalognya sendiri.
    /// </summary>
    public sealed class LanggananKlien
    {
        /// <summary>
        /// Jeda sebelum mencoba katalog harga lagi setelah koneksi gagal.
        /// Mencegah belasan percobaan ke port yang sama dalam hitungan detik —
        /// sia-sia bila server memang belum hidup, dan koneksi berulang ke port
        /// yang salah bisa membuat Windows memblokir IP sesaat.
        /// </summary>
        private static readonly TimeSpan JedaCobaUlangKatalog = TimeSpan.FromSeconds(30);

        private readonly LisensiManager _lisensi;
        private readonly HttpClient _http;
        private readonly ILogger<LanggananKlien> _logger;
        private readonly SambunganServerLisensi? _sambungan;
        private readonly object _kunci = new();

        private bool _gagalBaruIni;
        private DateTime _gagalPada = DateTime.MinValue;

        /// <param name="sambungan">
        /// Perekam keadaan sambungan bersama (dipakai baris menu Pusat
        /// Administrasi di sidebar). Opsional supaya pengujian tidak perlu
        /// menyiapkannya.
        /// </param>
        public LanggananKlien(LisensiManager lisensi, HttpClient http, ILogger<LanggananKlien> logger,
            SambunganServerLisensi? sambungan = null)
        {
            _lisensi = lisensi ?? throw new ArgumentNullException(nameof(lisensi));
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _sambungan = sambungan;
        }

        /// <summary>
        /// Salinan katalog terakhir dari server yang tersimpan di disk — tanpa
        /// jaringan. Null bila belum pernah ada (atau berkasnya rusak), sehingga
        /// kartu dibiarkan apa adanya.
        /// </summary>
        public KatalogLangganan? MuatKatalogTersimpan()
        {
            var entri = KatalogPaketTersimpan.Muat();
            return entri is null ? null : KatalogLangganan.Bawaan.DenganAngka(entri);
        }

        /// <summary>
        /// Muat katalog harga resmi dari server, lalu simpan salinannya untuk
        /// pemakaian offline berikutnya. Percobaan yang gagal dicatat, lalu
        /// ditunda singkat sebelum dicoba lagi.
        /// </summary>
        /// <param name="paksa">
        /// True MELEWATI jeda itu — dipakai saat pengguna menekan "Cek Status"
        /// atau menyimpan alamat server, karena tindakan sengaja pengguna harus
        /// selalu benar-benar mencoba.
        /// </param>
        public async Task<HasilKatalog> MuatDariServerAsync(bool paksa = false, CancellationToken ct = default)
        {
            if (!paksa && TerhalangJeda())
            {
                return HasilKatalog.Dilewati;
            }

            var alamat = _lisensi.AlamatEndpoint("paket");
            if (alamat.Length == 0)
            {
                return HasilKatalog.Kosong;
            }

            try
            {
                var resp = await _http.GetAsync(alamat, ct).ConfigureAwait(false);

                // Balasan diterima (apa pun statusnya) = server bisa dihubungi;
                // jawaban 404/500 berarti layanannya hidup tetapi tidak siap,
                // bukan "tidak terjangkau".
                _sambungan?.CatatTerhubung();

                if (!resp.IsSuccessStatusCode)
                {
                    return HasilKatalog.Kosong;
                }

                var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return HasilKatalog.Kosong;
                }

                var entri = BacaEntri(doc.RootElement);
                TandaiBerhasil();

                // Salinan untuk tampilan offline; sukses TIDAK bergantung padanya.
                KatalogPaketTersimpan.Simpan(entri);

                return new HasilKatalog(
                    HasilMuatKatalog.Server,
                    KatalogLangganan.Bawaan.DenganAngka(entri));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Kegagalan jaringan sungguhan (koneksi ditolak, DNS, waktu
                // habis) — permintaan tidak pernah sampai ke server.
                _sambungan?.CatatTidakTerjangkau();
                TandaiGagal(ex);
                return HasilKatalog.TidakTerjangkau;
            }
            catch (Exception ex)
            {
                // Balasan server terima tetapi tidak bisa dipakai (JSON tidak
                // sah): servernya terjangkau, jadi sambungan TIDAK ditandai gagal.
                TandaiGagal(ex);
                return HasilKatalog.TidakTerjangkau;
            }
        }

        /// <summary>
        /// Buat tagihan untuk satu paket. Paket saja yang dikirim; nominal TIDAK —
        /// server meneguhkan harganya dari katalognya sendiri, jadi angka yang
        /// tampil dan yang ditagih tidak bisa berbeda.
        ///
        /// Dua jalur (paralel dengan server): kode aktivasi terisi = pasjangan;
        /// kode kosong + <paramref name="namaDesa"/> terisi = berlangganan awal —
        /// server mendaftarkan desa baru dan menerbitkan kode aktivasi yang
        /// dikembalikan pada <see cref="HasilTagihan.KodeAktivasi"/>.
        /// </summary>
        public async Task<HasilTagihan> BuatTagihanAsync(
            string kodePaket, string kodeAktivasi, string? namaDesa = null, CancellationToken ct = default)
        {
            var payload = JsonSerializer.Serialize(new
            {
                kodeAktivasi = (kodeAktivasi ?? string.Empty).Trim(),
                paket = kodePaket,
                namaDesa = string.IsNullOrWhiteSpace(namaDesa) ? null : namaDesa.Trim()
            });

            using var konten = new StringContent(payload, Encoding.UTF8, "application/json");

            // _http adalah HttpClient singleton dari DI (sama dengan yang dipakai
            // LisensiManager): jangan pernah `using` di sini, atau Dispose-nya
            // mematikan cek lisensi berikutnya.
            var resp = await _http.PostAsync(_lisensi.AlamatEndpoint("buat-tagihan"), konten, ct)
                .ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return HasilTagihan.Ditolak((int)resp.StatusCode);
            }

            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var orderId = root.TryGetProperty("orderId", out var oi) ? oi.GetString() : null;
            var link = root.TryGetProperty("link_pembayaran", out var lp) ? lp.GetString() : null;
            long? nominal = root.TryGetProperty("nominal", out var nm) && nm.ValueKind == JsonValueKind.Number
                ? nm.GetInt64()
                : null;
            int? hari = root.TryGetProperty("hari", out var hr) && hr.ValueKind == JsonValueKind.Number
                ? hr.GetInt32()
                : null;
            var kodeBaru = root.TryGetProperty("kode_aktivasi", out var ka) && ka.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(ka.GetString())
                    ? ka.GetString()!
                    : null;

            return new HasilTagihan(
                new TagihanLangganan(orderId, nominal, hari, link), (int)resp.StatusCode, kodeBaru);
        }

        /// <summary>
        /// Mulai trial 30 hari (gratis) untuk desa dengan kode aktivasi ini.
        /// Lepas dari pembayaran karena harganya 0: server yang menahan "sekali
        /// per desa" (kolom trial_sudah) dan "langganan belum aktif", jadi
        /// menekan tombol berkali-kali atau dari banyak PC tetap aman.
        ///
        /// Bentuk permintaan & balasan sama dengan POST /aktivasi; setelah jawaban
        /// aktif, pemanggil cukup memanggil <see cref="LisensiManager.CekAsync"/>
        /// agar token trial tersimpan di cache lisensi seperti biasa.
        /// </summary>
        public async Task<HasilTrialKlien> MulaiTrialAsync(
            string kodeAktivasi, string? idPerangkat, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(kodeAktivasi))
            {
                return new HasilTrialKlien(false, null, "Isi kode aktivasi terlebih dahulu.", 400);
            }

            var payload = JsonSerializer.Serialize(new
            {
                kodeAktivasi = kodeAktivasi.Trim(),
                idPerangkat = string.IsNullOrWhiteSpace(idPerangkat) ? null : idPerangkat.Trim()
            });

            using var konten = new StringContent(payload, Encoding.UTF8, "application/json");

            // _http HttpClient bersama singleton — JANGAN di-Dispose di sini.
            var resp = await _http.PostAsync(_lisensi.AlamatEndpoint("aktifkan-trial"), konten, ct)
                .ConfigureAwait(false);
            var status = (int)resp.StatusCode;
            if (!resp.IsSuccessStatusCode)
            {
                return new HasilTrialKlien(false, null, null, status);
            }

            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            bool aktif = root.TryGetProperty("aktif", out var a) && a.ValueKind == JsonValueKind.True;
            DateTime? berlaku = root.TryGetProperty("berlakuSampai", out var b) && b.ValueKind == JsonValueKind.String
                && DateTime.TryParse(b.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var t)
                ? t
                : null;
            string? pesan = root.TryGetProperty("pesan", out var p) ? p.GetString() : null;

            return new HasilTrialKlien(aktif, berlaku, pesan, status);
        }

        /// <summary>
        /// Hasil permintaan trial dari sisi klien: aktif/tidak, jatuh tempo
        /// (UTC), teks penolakan server, dan status HTTP penolakan bila ada.
        /// </summary>
        public sealed record HasilTrialKlien(bool Aktif, DateTime? BerlakuSampai, string? Pesan, int StatusHttp);

        /// <summary>Terjemahkan array JSON katalog menjadi entri harga yang dipakai bersama salinan disk.</summary>
        private static List<KatalogPaketTersimpan.Paket> BacaEntri(JsonElement array)
        {
            var entri = new List<KatalogPaketTersimpan.Paket>();

            foreach (var item in array.EnumerateArray())
            {
                var kode = item.TryGetProperty("kode", out var k) ? k.GetString() : null;
                if (string.IsNullOrWhiteSpace(kode))
                {
                    continue;
                }

                var nama = item.TryGetProperty("nama", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                long harga = item.TryGetProperty("harga", out var h) && h.ValueKind == JsonValueKind.Number
                    ? h.GetInt64()
                    : 0;
                int hari = item.TryGetProperty("hari", out var d) && d.ValueKind == JsonValueKind.Number
                    ? d.GetInt32()
                    : 0;

                // Hanya entri yang masuk akal; paket tambahan server tetap disimpan
                // supaya salinan tampilan lengkap, walau versi aplikasi ini belum
                // mengenal kodenya (DenganAngka melewatinya dengan aman).
                if (harga > 0 && hari > 0)
                {
                    entri.Add(new KatalogPaketTersimpan.Paket
                    {
                        Kode = kode!,
                        Nama = nama,
                        Harga = harga,
                        Hari = hari
                    });
                }
            }

            return entri;
        }

        private bool TerhalangJeda()
        {
            lock (_kunci)
            {
                return _gagalBaruIni && DateTime.Now - _gagalPada < JedaCobaUlangKatalog;
            }
        }

        private void TandaiBerhasil()
        {
            lock (_kunci)
            {
                _gagalBaruIni = false;
            }
        }

        /// <summary>
        /// Catat kegagalan + pasang jeda coba ulang. Dicatat sekali per jendela
        /// jeda supaya log tidak dibanjiri tumpukan kesalahan yang sama.
        /// </summary>
        private void TandaiGagal(Exception ex)
        {
            bool pertama;
            lock (_kunci)
            {
                pertama = !_gagalBaruIni;
                _gagalBaruIni = true;
                _gagalPada = DateTime.Now;
            }

            if (pertama)
            {
                _logger.LogWarning(ex,
                    "Katalog harga paket tidak terjangkau — kartu memakai harga tersimpan bila ada.");
            }
        }
    }

    /// <summary>
    /// Rupiah tanpa pecahan desimal: 150000 → "Rp150.000", memakai pemisah
    /// ribuan INDONESIA seperti dokumen lain di aplikasi ini (kuitansi pembayaran
    /// juga mencetak "Rp 150.000"). Sebelumnya di sini dipakai InvariantCulture
    /// ("Rp150,000") sehingga halaman Langganan mencetak angka dengan gaya yang
    /// justru terbaca sebagai angka desimal oleh pembaca Indonesia — mis. desa
    /// yang harus mentransfer Rp150.000 melihat "Rp150,000" di rincian QRIS.
    /// Satu tempat saja supaya kartu paket, harga per hari, dan ringkasan
    /// tagihan tidak pernah berbeda bentuk. Nilai kosong/nol berarti belum ada
    /// angka dari server — bukan "gratis".
    /// </summary>
    internal static class Rupiah
    {
        private static readonly CultureInfo Indonesia = CultureInfo.GetCultureInfo("id-ID");

        public static string Format(long? nominal) => nominal is null or <= 0
            ? "harga tidak terbaca"
            : "Rp" + nominal.Value.ToString("N0", Indonesia);
    }
}
