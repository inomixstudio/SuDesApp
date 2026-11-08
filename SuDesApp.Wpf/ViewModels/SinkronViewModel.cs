using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using SuDesApp.Sinkron;
using SuDesApp.Services;
using SuDesApp.Supabase;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Utilities;

namespace SuDesApp.Wpf.ViewModels;

/// <summary>
/// Halaman Sinkron Android: menarik surat yang dibuat aplikasi Android dari
/// antrean Supabase lalu mengimpornya ke register desktop.
///
/// <para>
/// Halaman ini juga tempat operator menghubungkan aplikasi ini ke Supabase
/// (host, tenant, kata sandi). Kata sandi disimpan lewat DPAPI, bukan teks
/// polos.
/// </para>
/// </summary>
public class SinkronViewModel : ObservableObject
{
    private readonly SinkronSuratService _sinkron;
    private readonly PerangkaianPerangkat _perangkaian;
    private readonly PenerbitSkemaFormulir _penerbitSkema;
    private readonly PenerbitPengaturanDesa _penerbitPengaturan;
    private readonly IMessageService _pesan;
    private readonly ILogger<SinkronViewModel> _logger;
    private readonly string _folderBerkas;
    private readonly SuDesApp.Services.PenjagaLisensi? _penjaga;

    private string _host = string.Empty;
    private int _port = 6543;
    private string _pengguna = string.Empty;
    private string _kataSandi = string.Empty;
    private string _tenant = string.Empty;
    private string _urlRest = string.Empty;
    private string _kunciPublik = string.Empty;

    private bool _sedangSinkron;
    private bool _sedangMemasang;
    private bool _sinkronOtomatis = PengaturanOtomatisSinkron.Aktif;
    private int _jumlahAntrean;
    private SinkronHasil? _hasil;
    private string _namaPerangkat = string.Empty;
    private ObservableCollection<PerangkatTerpasang> _perangkat = [];
    private PerangkatTerpasang? _perangkatDipilih;
    private HasilPerangkaian? _perangkatBaru;
    private BitmapSource? _gambarQr;
    private bool _modeKode;

    public SinkronViewModel(
        SinkronSuratService sinkron,
        PerangkaianPerangkat perangkaian,
        PenerbitSkemaFormulir penerbitSkema,
        PenerbitPengaturanDesa penerbitPengaturan,
        IMessageService pesan,
        ILogger<SinkronViewModel>? logger = null,
        SuDesApp.Services.PenjagaLisensi? penjaga = null)
    {
        _sinkron = sinkron ?? throw new System.ArgumentNullException(nameof(sinkron));
        _perangkaian = perangkaian ?? throw new System.ArgumentNullException(nameof(perangkaian));
        _penerbitSkema = penerbitSkema ?? throw new System.ArgumentNullException(nameof(penerbitSkema));
        _penerbitPengaturan = penerbitPengaturan ?? throw new System.ArgumentNullException(nameof(penerbitPengaturan));
        _pesan = pesan ?? throw new System.ArgumentNullException(nameof(pesan));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<SinkronViewModel>.Instance;
        _penjaga = penjaga;

        _folderBerkas = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SuDesApp", "Sinkron");

        SinkronCommand = new AsyncRelayCommand(SinkronkanAsync, () => !SedangSinkron && KonfigurasiLengkap);
        MuatUlangCommand = new AsyncRelayCommand(MuatUlangAsync, () => !SedangSinkron);
        ImporUlangCommand = new AsyncRelayCommand(ImporUlangArsipGagalAsync, () => !SedangSinkron);
        SimpanKonfigurasiCommand = new AsyncRelayCommand(SimpanKonfigurasiAsync, () => !SedangSinkron);
        PasangCommand = new AsyncRelayCommand(() => PasangAsync(modeKode: false), () => !SedangMemasang);
        BuatQrCommand = new AsyncRelayCommand(() => PasangAsync(modeKode: false), () => !SedangMemasang);
        BuatKodeCommand = new AsyncRelayCommand(() => PasangAsync(modeKode: true), () => !SedangMemasang);
        CabutCommand = new AsyncRelayCommand(CabutAsync, () => PerangkatDipilih is not null && !SedangMemasang);
        TerbitkanSkemaCommand = new AsyncRelayCommand(TerbitkanSkemaAsync, () => !SedangSinkron);
        TerbitkanPengaturanCommand = new AsyncRelayCommand(TerbitkanPengaturanAsync, () => !SedangSinkron);

        MuatKonfigurasi();
        _ = MuatUlangAsync();
    }

    public string HeaderTitle => "SINKRON ANDROID";

    public string HeaderSubtitle =>
        "Tarik surat yang dibuat di aplikasi Android dari antrean Supabase, lalu impor ke register desktop. "
        + "Antrean dibatasi 10 surat; sinkron berjalan otomatis di latar dan manual tetap tersedia.";

    // ------------------------------------------------------------------
    // Konfigurasi
    // ------------------------------------------------------------------

    public string Host { get => _host; set => SetKonfigurasi(ref _host, value); }
    public int Port { get => _port; set => SetKonfigurasi(ref _port, value); }
    public string Pengguna { get => _pengguna; set => SetKonfigurasi(ref _pengguna, value); }
    public string KataSandi { get => _kataSandi; set => SetKonfigurasi(ref _kataSandi, value); }
    public string Tenant { get => _tenant; set => SetKonfigurasi(ref _tenant, value); }
    public string UrlRest { get => _urlRest; set => SetKonfigurasi(ref _urlRest, value); }
    public string KunciPublik { get => _kunciPublik; set => SetKonfigurasi(ref _kunciPublik, value); }

    public bool KonfigurasiLengkap =>
        !string.IsNullOrWhiteSpace(_host) &&
        !string.IsNullOrWhiteSpace(_pengguna) &&
        !string.IsNullOrWhiteSpace(_kataSandi) &&
        !string.IsNullOrWhiteSpace(_tenant);

    /// <summary>
    /// Impor antrean otomatis di latar (bawaan aktif). Dimatikan bila
    /// operator ingin selalu menekan Sinkron secara manual.
    /// </summary>
    public bool SinkronOtomatis
    {
        get => _sinkronOtomatis;
        set
        {
            if (SetProperty(ref _sinkronOtomatis, value))
            {
                AppPreferenceStore.SetBool(PengaturanOtomatisSinkron.Kunci, value);
            }
        }
    }

    public bool SedangSinkron
    {
        get => _sedangSinkron;
        private set
        {
            SetProperty(ref _sedangSinkron, value);
            ((AsyncRelayCommand)SinkronCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)MuatUlangCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)ImporUlangCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)SimpanKonfigurasiCommand).RaiseCanExecuteChanged();
        }
    }

    public bool SedangMemasang
    {
        get => _sedangMemasang;
        private set
        {
            SetProperty(ref _sedangMemasang, value);
            ((AsyncRelayCommand)PasangCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)BuatQrCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)BuatKodeCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)CabutCommand).RaiseCanExecuteChanged();
        }
    }

    /// <summary>Mode pasang terakhir: true = tombol Kode, false = tombol QR.</summary>
    public bool ModePasangKode => _modeKode;

    /// <summary>QR ditampilkan hanya bila tombol Buat QR yang ditekan.</summary>
    public bool TampilkanQr => !_modeKode;

    /// <summary>Blok kode selalu ditampilkan (QR selalu punya kode pendamping).</summary>
    public bool TampilkanKode => true;

    /// <summary>Petunjuk utama panel kode sesuai tombol yang ditekan.</summary>
    public string PetunjukPasangUtama => _modeKode
        ? "Ketik kode ini di aplikasi Android"
        : "Pindai dari aplikasi Android";

    /// <summary>Petunjuk kedua panel kode (jalur alternatif).</summary>
    public string PetunjukPasangKedua => _modeKode
        ? "Kode berlaku 10 menit, sekali pakai"
        : "atau ketik kode ini";

    /// <summary>Batas antrean Supabase per desa (trigger basis data menolak surat ke-11).</summary>
    public const int AntreanMaksimum = 10;

    public int JumlahAntrean
    {
        get => _jumlahAntrean;
        private set
        {
            SetProperty(ref _jumlahAntrean, value);
            OnPropertyChanged(nameof(RingkasanAntrean));
            OnPropertyChanged(nameof(RasioAntrean));
            OnPropertyChanged(nameof(AntreanPenuh));
        }
    }

    /// <summary>"2 / 10" untuk kartu ringkasan.</summary>
    public string RingkasanAntrean => $"{_jumlahAntrean} / {AntreanMaksimum}";

    /// <summary>Persentase isi antrean (0–100) untuk bilah kemajuan.</summary>
    public double RasioAntrean => Math.Min(100d, _jumlahAntrean * 100d / AntreanMaksimum);

    public bool AntreanPenuh => _jumlahAntrean >= AntreanMaksimum;

    public int JumlahPerangkat => Perangkat.Count;

    public int JumlahPerangkatAktif => Perangkat.Count(p => p.Aktif);

    public bool AdaPerangkat => Perangkat.Count > 0;

    public SinkronHasil? Hasil
    {
        get => _hasil;
        private set
        {
            SetProperty(ref _hasil, value);
            OnPropertyChanged(nameof(AdaHasil));
            OnPropertyChanged(nameof(HasilRingkasan));
        }
    }

    public bool AdaHasil => _hasil is not null;

    public string HasilRingkasan => _hasil switch
    {
        null => string.Empty,
        { } h when !string.IsNullOrWhiteSpace(h.PesanGalat) => h.PesanGalat,
        { } h => $"{h.Berhasil} surat masuk, {h.Gagal} gagal, {h.Dilewati} dilewati.",
    };

    public string FolderBerkas => _folderBerkas;

    // ------------------------------------------------------------------
    // Perangkat
    // ------------------------------------------------------------------

    public string NamaPerangkat
    {
        get => _namaPerangkat;
        set
        {
            SetProperty(ref _namaPerangkat, value);
            ((AsyncRelayCommand)PasangCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)BuatQrCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)BuatKodeCommand).RaiseCanExecuteChanged();
        }
    }

    public ObservableCollection<PerangkatTerpasang> Perangkat
    {
        get => _perangkat;
        private set
        {
            SetProperty(ref _perangkat, value);
            OnPropertyChanged(nameof(JumlahPerangkat));
            OnPropertyChanged(nameof(JumlahPerangkatAktif));
            OnPropertyChanged(nameof(AdaPerangkat));
        }
    }

    public PerangkatTerpasang? PerangkatDipilih
    {
        get => _perangkatDipilih;
        set
        {
            SetProperty(ref _perangkatDipilih, value);
            ((AsyncRelayCommand)CabutCommand).RaiseCanExecuteChanged();
        }
    }

    public HasilPerangkaian? PerangkatBaru
    {
        get => _perangkatBaru;
        private set
        {
            SetProperty(ref _perangkatBaru, value);
            OnPropertyChanged(nameof(AdaPerangkatBaru));
            OnPropertyChanged(nameof(QrPayload));
            OnPropertyChanged(nameof(KodePerangkat));
            GambarQr = QrGambar.Buat(QrPayload);
        }
    }

    public BitmapSource? GambarQr
    {
        get => _gambarQr;
        private set => SetProperty(ref _gambarQr, value);
    }

    public bool AdaPerangkatBaru => PerangkatBaru is { Berhasil: true };

    /// <summary>
    /// Isi QR yang discan aplikasi Android. Cukup URL dan kode 4 angka —
    /// surel dan kata sandi tidak pernah tampil di layar, Android menukar
    /// kodenya lewat <c>ambil_pasangan</c>.
    /// </summary>
    public string QrPayload => PerangkatBaru is { Berhasil: true, Kode: { } kode }
        ? $"{UrlRest.TrimEnd('/')}\n{kode}"
        : string.Empty;

    /// <summary>Kode verifikasi 4 angka yang harus dibaca operator Android.</summary>
    public string KodePerangkat => PerangkatBaru is { Berhasil: true, Kode: { } kode }
        ? kode
        : string.Empty;

    public ICommand SinkronCommand { get; }
    public ICommand MuatUlangCommand { get; }
    public ICommand ImporUlangCommand { get; }
    public ICommand SimpanKonfigurasiCommand { get; }
    public ICommand PasangCommand { get; }
    public ICommand BuatQrCommand { get; }
    public ICommand BuatKodeCommand { get; }
    public ICommand CabutCommand { get; }
    public ICommand TerbitkanSkemaCommand { get; }
    public ICommand TerbitkanPengaturanCommand { get; }

    private void SetKonfigurasi<T>(ref T field, T value)
    {
        if (SetProperty(ref field, value))
        {
            ((AsyncRelayCommand)SinkronCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)SimpanKonfigurasiCommand).RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(KonfigurasiLengkap));
        }
    }

    private void MuatKonfigurasi()
    {
        var p = SupabasePengaturan.Muat();
        _host = p.Host;
        _port = p.Port;
        _pengguna = p.Pengguna;
        _kataSandi = p.KataSandi;
        _tenant = p.Tenant;
        _urlRest = p.UrlRest;
        _kunciPublik = p.KunciPublik;

        OnPropertyChanged(nameof(Host));
        OnPropertyChanged(nameof(Port));
        OnPropertyChanged(nameof(Pengguna));
        OnPropertyChanged(nameof(KataSandi));
        OnPropertyChanged(nameof(Tenant));
        OnPropertyChanged(nameof(UrlRest));
        OnPropertyChanged(nameof(KunciPublik));
        OnPropertyChanged(nameof(KonfigurasiLengkap));
    }

    private async Task MuatUlangAsync()
    {
        try
        {
            JumlahAntrean = await _sinkron.HitungAntreanAsync().ConfigureAwait(true);
            var daftar = await _perangkaian.AmbilDaftarAsync().ConfigureAwait(true);
            Perangkat = new ObservableCollection<PerangkatTerpasang>(daftar);
        }
        catch (Exception ex)
        {
            // Dipanggil juga dari konstruktor (fire-and-forget), jadi galat
            // jaringan/konfigurasi tidak boleh hilang tanpa jejak.
            _logger.LogWarning(ex, "Gagal memuat ulang ringkasan antrean.");
        }
    }

    private async Task SimpanKonfigurasiAsync()
    {
        try
        {
            new SupabasePengaturan
            {
                Host = Host.Trim(),
                Port = Port,
                Pengguna = Pengguna.Trim(),
                KataSandi = KataSandi,
                Tenant = Tenant.Trim(),
                UrlRest = UrlRest.Trim(),
                KunciPublik = KunciPublik.Trim(),
            }.Simpan();

            await _pesan.ShowInfoAsync("Konfigurasi Supabase disimpan.");
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Gagal menyimpan konfigurasi Supabase.");
            await _pesan.ShowErrorAsync("Gagal menyimpan konfigurasi: " + ex.Message);
        }
    }

    private async Task SinkronkanAsync()
    {
        if (_penjaga is { SinkronTerkunci: true })
        {
            await _pesan.ShowInfoAsync(
                SuDesApp.Services.PenjagaLisensi.PesanSinkronTerkunci);
            return;
        }

        SedangSinkron = true;
        Hasil = null;
        try
        {
            var hasil = await _sinkron.SinkronkanAsync().ConfigureAwait(true);
            Hasil = hasil;

            if (!string.IsNullOrWhiteSpace(hasil.PesanGalat))
            {
                await _pesan.ShowErrorAsync(hasil.PesanGalat);
            }
            else if (hasil.AdaYangDiproses)
            {
                // Baris yang gagal diimpor dicoba lagi pada putaran berikutnya
                // (maks 3 kali) lalu dibuang; "dilewati" kini hanya berarti
                // status barisnya tidak dikenal oleh impor.
                var pesan = $"{hasil.Berhasil} surat berhasil diimpor ke register.";
                if (hasil.Gagal > 0)
                {
                    pesan += $" {hasil.Gagal} gagal dan akan dicoba lagi pada sinkron berikutnya.";
                }

                if (hasil.Dilewati > 0)
                {
                    pesan += $" {hasil.Dilewati} dilewati karena statusnya bukan antrean baru.";
                }

                await _pesan.ShowInfoAsync(pesan);
            }

            await MuatUlangAsync().ConfigureAwait(true);
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Sinkron gagal.");
            await _pesan.ShowErrorAsync("Sinkron gagal: " + ex.Message);
        }
        finally
        {
            SedangSinkron = false;
        }
    }

    private async Task ImporUlangArsipGagalAsync()
    {
        if (_penjaga is { SinkronTerkunci: true })
        {
            await _pesan.ShowInfoAsync(
                SuDesApp.Services.PenjagaLisensi.PesanSinkronTerkunci);
            return;
        }

        SedangSinkron = true;
        Hasil = null;
        try
        {
            var hasil = await _sinkron.ImporUlangArsipGagalAsync().ConfigureAwait(true);
            Hasil = hasil;

            if (!string.IsNullOrWhiteSpace(hasil.PesanGalat))
            {
                await _pesan.ShowErrorAsync(hasil.PesanGalat);
            }
            else if (hasil.AdaYangDiproses)
            {
                await _pesan.ShowInfoAsync(
                    $"{hasil.Berhasil} arsip gagal berhasil diimpor ulang, {hasil.Gagal} masih gagal.");
            }
            else
            {
                await _pesan.ShowInfoAsync("Tidak ada arsip gagal untuk diimpor ulang.");
            }

            await MuatUlangAsync().ConfigureAwait(true);
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Impor ulang arsip gagal.");
            await _pesan.ShowErrorAsync("Impor ulang gagal: " + ex.Message);
        }
        finally
        {
            SedangSinkron = false;
        }
    }

    private async Task PasangAsync(bool modeKode = false)
    {
        if (_penjaga is { SinkronTerkunci: true })
        {
            await _pesan.ShowInfoAsync(
                SuDesApp.Services.PenjagaLisensi.PesanSinkronTerkunci);
            return;
        }

        _modeKode = modeKode;
        OnPropertyChanged(nameof(ModePasangKode));
        OnPropertyChanged(nameof(TampilkanQr));
        OnPropertyChanged(nameof(TampilkanKode));
        OnPropertyChanged(nameof(PetunjukPasangUtama));
        OnPropertyChanged(nameof(PetunjukPasangKedua));

        SedangMemasang = true;
        PerangkatBaru = null;
        try
        {
            // Nama tidak wajib: kosong → Core membuat nama otomatis.
            var nama = string.IsNullOrWhiteSpace(NamaPerangkat)
                ? string.Empty
                : NamaPerangkat.Trim();
            var hasil = await _perangkaian.PasangAsync(nama).ConfigureAwait(true);
            PerangkatBaru = hasil;

            if (!hasil.Berhasil)
            {
                await _pesan.ShowErrorAsync(hasil.PesanGalat ?? "Gagal memasang perangkat.");
                return;
            }

            NamaPerangkat = string.Empty;
            await MuatUlangAsync().ConfigureAwait(true);
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Gagal memasang perangkat.");
            await _pesan.ShowErrorAsync("Gagal memasang perangkat: " + ex.Message);
        }
        finally
        {
            SedangMemasang = false;
        }
    }

    private async Task TerbitkanSkemaAsync()
    {
        if (_penjaga is { SinkronTerkunci: true })
        {
            await _pesan.ShowInfoAsync(
                SuDesApp.Services.PenjagaLisensi.PesanSinkronTerkunci);
            return;
        }

        try
        {
            var jumlah = await _penerbitSkema.TerbitkanAsync().ConfigureAwait(true);
            if (jumlah < 0)
            {
                await _pesan.ShowErrorAsync("Gagal menerbitkan skema formulir. Periksa koneksi Supabase.");
                return;
            }

            // Kop surat dan pola nomor diterbitkan bersamaan: Android tidak
            // menyimpan sendiri identitas desa, jadi keduanya harus selalu
            // seversi. Menerbitkan skema saja akan membuat formulir baru
            // memakai kop lama.
            var kopOk = await _penerbitPengaturan.TerbitkanAsync().ConfigureAwait(true);

            await _pesan.ShowInfoAsync(
                kopOk
                    ? $"Skema formulir ({jumlah} jenis) dan pengaturan kop surat diterbitkan. Android mengambilnya otomatis."
                    : $"Skema formulir diterbitkan ({jumlah} jenis), tetapi pengaturan kop gagal. Ulangi lagi.");
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Gagal menerbitkan skema.");
            await _pesan.ShowErrorAsync("Gagal menerbitkan skema: " + ex.Message);
        }
    }

    /// <summary>
    /// Terbitkan ulang kop surat dan pola nomor saja — dipakai saat pengaturan
    /// desa berubah dan Android belum menerima pembaruan.
    /// </summary>
    private async Task TerbitkanPengaturanAsync()
    {
        if (_penjaga is { SinkronTerkunci: true })
        {
            await _pesan.ShowInfoAsync(
                SuDesApp.Services.PenjagaLisensi.PesanSinkronTerkunci);
            return;
        }

        try
        {
            var ok = await _penerbitPengaturan.TerbitkanAsync().ConfigureAwait(true);
            await _pesan.ShowInfoAsync(
                ok
                    ? "Pengaturan kop surat dan pola nomor diterbitkan. Android mengambilnya otomatis."
                    : "Gagal menerbitkan pengaturan kop. Periksa koneksi Supabase.");
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Gagal menerbitkan pengaturan desa.");
            await _pesan.ShowErrorAsync("Gagal menerbitkan pengaturan kop: " + ex.Message);
        }
    }

    private async Task CabutAsync()
    {
        if (_penjaga is { SinkronTerkunci: true })
        {
            await _pesan.ShowInfoAsync(
                SuDesApp.Services.PenjagaLisensi.PesanSinkronTerkunci);
            return;
        }

        if (PerangkatDipilih is null) return;

        var yakin = await _pesan.ShowConfirmationAsync(
            "Cabut perangkat",
            $"Cabut '{PerangkatDipilih!.Nama}'? Antrean yang belum tersinkron ikut terhapus.")
            .ConfigureAwait(true);
        if (!yakin) return;

        SedangMemasang = true;
        try
        {
            var ok = await _perangkaian.CabutAsync(PerangkatDipilih!.Id).ConfigureAwait(true);
            if (!ok)
            {
                await _pesan.ShowErrorAsync("Gagal mencabut perangkat.");
                return;
            }

            PerangkatDipilih = null;
            await MuatUlangAsync().ConfigureAwait(true);
        }
        finally
        {
            SedangMemasang = false;
        }
    }
}
