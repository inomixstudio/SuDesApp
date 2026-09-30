using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Data.Models;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>Satu pilihan peran untuk ComboBox (kode + nama tampil).</summary>
    public sealed record OpsiPeran(string Kode, string Tampilan);

    /// <summary>
    /// Halaman Kelola Pengguna: kartu "Akun Saya" (identitas akun yang sedang dipakai),
    /// daftar akun, tambah akun, ubah nama/peran/status, reset kata sandi, dan hapus akun.
    ///
    /// Halaman ini boleh dibuka semua peran: kartu "Akun Saya" dan tombol ubah kata
    /// sandi selalu tampil, sedangkan daftar &amp; formulir pengelolaan akun hanya muncul
    /// untuk peran yang memegang izin <see cref="IzinAplikasi.KelolaPengguna"/>
    /// (lihat <see cref="BolehAkses"/>); peran lain melihat keterangan
    /// <see cref="PesanTidakBerhak"/>.
    /// </summary>
    public class KelolaPenggunaViewModel : ObservableObject
    {
        private readonly IPenggunaService _pengguna;
        private readonly IMessageService _message;
        private readonly ILogger<KelolaPenggunaViewModel> _logger;

        private Pengguna? _terpilih;
        private bool _modeBaru;
        private string _nama = string.Empty;
        private string _username = string.Empty;
        private string _peran = PeranPengguna.Operator;
        private string _kataSandi = string.Empty;
        private bool _aktif = true;
        private bool _sedangSibuk;
        private string _pesan = string.Empty;
        private bool _adaGalat;
        private string _cariTeks = string.Empty;
        private string _ringkasanDaftar = string.Empty;
        private readonly List<Pengguna> _semua = new();

        // Kartu "Akun Saya" (identitas akun yang sedang dipakai).
        private string _akunSayaNama = string.Empty;
        private string _akunSayaPeran = string.Empty;
        private string _akunSayaStatus = string.Empty;
        private string _akunSayaMasuk = string.Empty;
        private string _akunSayaCatatan = string.Empty;
        private bool _akunSayaAktif = true;

        public KelolaPenggunaViewModel(
            IPenggunaService pengguna,
            IMessageService message,
            ILogger<KelolaPenggunaViewModel>? logger = null)
        {
            _pengguna = pengguna ?? throw new ArgumentNullException(nameof(pengguna));
            _message = message ?? throw new ArgumentNullException(nameof(message));
            _logger = logger ?? NullLogger<KelolaPenggunaViewModel>.Instance;

            DaftarPeran = PeranPengguna.Semua
                .Select(p => new OpsiPeran(p, PeranPengguna.Tampilan(p)))
                .ToList();

            BaruCommand = new RelayCommand(MulaiBaru);
            SimpanCommand = new AsyncRelayCommand(SimpanAsync, () => !SedangSibuk);
            ResetSandiCommand = new AsyncRelayCommand(ResetSandiAsync, () => !SedangSibuk && _terpilih != null && !_modeBaru);
            HapusCommand = new AsyncRelayCommand(HapusAsync, () => !SedangSibuk && _terpilih != null && !_modeBaru);
            BersihkanCariCommand = new RelayCommand(() => CariTeks = string.Empty);
            BukaPanelSandiCommand = new RelayCommand(BukaPanelSandi);
            TutupPanelSandiCommand = new RelayCommand(() => IsPanelSandiTerbuka = false);
            SimpanSandiCommand = new AsyncRelayCommand(SimpanSandiAsync);
        }

        public string HeaderTitle => "Kelola Pengguna";

        public string HeaderSubtitle =>
            "Atur siapa saja yang boleh memakai aplikasi ini beserta perannya. Setiap perubahan tercatat di Riwayat Aktivitas.";

        /// <summary>Halaman ini hanya untuk Administrator; menu lain pun mengikuti <see cref="HakAkses"/>.</summary>
        public bool BolehAkses => SessionContext.Boleh(IzinAplikasi.KelolaPengguna);

        /// <summary>
        /// Nama akun yang sedang dipakai. Ditampilkan di panel ubah kata sandi supaya
        /// pemakai tahu akun mana yang sedang diganti sandinya — panel ini tersedia
        /// untuk semua peran, bukan hanya Administrator.
        /// </summary>
        public string AkunSaya =>
            string.IsNullOrWhiteSpace(SessionContext.CurrentUser) ? "(akun ini)" : SessionContext.CurrentUser!;

        public string PesanTidakBerhak =>
            "Hanya pengguna dengan peran Administrator yang boleh mengelola akun.";

        // =====================================================================
        // Kartu "Akun Saya" — identitas akun yang sedang dipakai
        // =====================================================================

        /// <summary>
        /// Nama akun yang sedang dipakai, dibaca dari database (bukan dari sesi) supaya
        /// ikut berubah bila Administrator mengganti namanya. Diisi untuk SEMUA peran.
        /// </summary>
        public string AkunSayaNama
        {
            get => _akunSayaNama;
            private set
            {
                if (SetProperty(ref _akunSayaNama, value)) OnPropertyChanged(nameof(AkunSayaInisial));
            }
        }

        /// <summary>Inisial nama untuk avatar kartu (maksimal dua huruf).</summary>
        public string AkunSayaInisial
        {
            get
            {
                var kata = AkunSayaNama.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var awal = kata.Length switch
                {
                    0 => string.Empty,
                    1 => kata[0][..1],
                    _ => string.Concat(kata[0][0], kata[1][0])
                };
                return awal.Length == 0 ? "?" : awal.ToUpperInvariant();
            }
        }

        /// <summary>Peran akun dalam bentuk yang dibaca manusia (mis. "Sekretaris Desa").</summary>
        public string AkunSayaPeran
        {
            get => _akunSayaPeran;
            private set => SetProperty(ref _akunSayaPeran, value);
        }

        /// <summary>"Aktif" atau "Nonaktif".</summary>
        public string AkunSayaStatus
        {
            get => _akunSayaStatus;
            private set => SetProperty(ref _akunSayaStatus, value);
        }

        /// <summary>True bila akun aktif — menggerakkan warna lencana status di kartu.</summary>
        public bool AkunSayaAktif
        {
            get => _akunSayaAktif;
            private set => SetProperty(ref _akunSayaAktif, value);
        }

        /// <summary>Cara masuk sesi ini: "Masuk dengan Google" atau "Masuk dengan akun aplikasi".</summary>
        public string AkunSayaMasuk
        {
            get => _akunSayaMasuk;
            private set => SetProperty(ref _akunSayaMasuk, value);
        }

        /// <summary>Keterangan tambahan pada kartu; kosong bila tidak ada yang perlu dijelaskan.</summary>
        public string AkunSayaCatatan
        {
            get => _akunSayaCatatan;
            private set
            {
                if (SetProperty(ref _akunSayaCatatan, value)) OnPropertyChanged(nameof(AdaCatatanAkunSaya));
            }
        }

        public bool AdaCatatanAkunSaya => !string.IsNullOrEmpty(AkunSayaCatatan);

        public ObservableCollection<Pengguna> Daftar { get; } = new();

        /// <summary>Saring daftar akun menurut nama tampilan, nama akun, atau peran.</summary>
        public string CariTeks
        {
            get => _cariTeks;
            set
            {
                if (SetProperty(ref _cariTeks, value ?? string.Empty))
                    TerapkanFilter();
            }
        }

        public string RingkasanDaftar
        {
            get => _ringkasanDaftar;
            private set => SetProperty(ref _ringkasanDaftar, value);
        }

        public IReadOnlyList<OpsiPeran> DaftarPeran { get; }

        public Pengguna? Terpilih
        {
            get => _terpilih;
            set
            {
                if (SetProperty(ref _terpilih, value) && value != null)
                {
                    ModeBaru = false;
                    Nama = value.NamaTampilan;
                    Username = value.Username;
                    Peran = value.Peran;
                    Aktif = value.Aktif;
                    KataSandi = string.Empty;
                    Pesan = string.Empty;
                }

                PerbaruiTombol();
            }
        }

        public bool ModeBaru
        {
            get => _modeBaru;
            private set
            {
                SetProperty(ref _modeBaru, value);
                OnPropertyChanged(nameof(JudulForm));
                OnPropertyChanged(nameof(UsernameBisaDiubah));
                PerbaruiTombol();
            }
        }

        public string JudulForm => ModeBaru ? "AKUN BARU" : "UBAH AKUN";

        /// <summary>Nama akun dipakai di riwayat dan tidak bisa diubah setelah dibuat.</summary>
        public bool UsernameBisaDiubah => ModeBaru;

        public string Nama
        {
            get => _nama;
            set => SetProperty(ref _nama, value);
        }

        public string Username
        {
            get => _username;
            set => SetProperty(ref _username, value);
        }

        public string Peran
        {
            get => _peran;
            set
            {
                SetProperty(ref _peran, value);
                OnPropertyChanged(nameof(KeteranganPeran));
            }
        }

        public string KeteranganPeran => PeranPengguna.Keterangan(Peran);

        /// <summary>Kata sandi baru: wajib untuk akun baru, opsional saat mengubah akun.</summary>
        public string KataSandi
        {
            get => _kataSandi;
            set => SetProperty(ref _kataSandi, value);
        }

        public bool Aktif
        {
            get => _aktif;
            set => SetProperty(ref _aktif, value);
        }

        public bool SedangSibuk
        {
            get => _sedangSibuk;
            private set
            {
                SetProperty(ref _sedangSibuk, value);
                PerbaruiTombol();
            }
        }

        public string Pesan
        {
            get => _pesan;
            private set
            {
                SetProperty(ref _pesan, value);
                OnPropertyChanged(nameof(AdaPesan));
            }
        }

        public bool AdaPesan => !string.IsNullOrEmpty(Pesan);

        public bool AdaGalat
        {
            get => _adaGalat;
            private set => SetProperty(ref _adaGalat, value);
        }

        public ICommand BaruCommand { get; }
        public ICommand SimpanCommand { get; }
        public ICommand ResetSandiCommand { get; }
        public ICommand HapusCommand { get; }

        /// <summary>Kosongkan kotak pencarian daftar akun (tombol ✕ di kotak cari).</summary>
        public ICommand BersihkanCariCommand { get; }

        // =====================================================================
        // Ubah kata sandi sendiri (pengganti menu "Ubah Kata Sandi" lama)
        // =====================================================================

        private bool _isPanelSandiTerbuka;
        private string _sandiLama = string.Empty;
        private string _sandiBaru = string.Empty;
        private string _konfirmasiSandi = string.Empty;
        private string _pesanSandi = string.Empty;
        private bool _galatSandi;

        public ICommand BukaPanelSandiCommand { get; }
        public ICommand TutupPanelSandiCommand { get; }
        public ICommand SimpanSandiCommand { get; }

        /// <summary>Overlay ubah kata sandi terbuka.</summary>
        public bool IsPanelSandiTerbuka
        {
            get => _isPanelSandiTerbuka;
            private set => SetProperty(ref _isPanelSandiTerbuka, value);
        }

        /// <summary>Nilai PasswordBox disinkronkan code-behind (PasswordBox tak mendukung binding TwoWay).</summary>
        public string SandiLama { get => _sandiLama; set => SetProperty(ref _sandiLama, value); }
        public string SandiBaru { get => _sandiBaru; set => SetProperty(ref _sandiBaru, value); }
        public string KonfirmasiSandi { get => _konfirmasiSandi; set => SetProperty(ref _konfirmasiSandi, value); }

        public string PesanSandi
        {
            get => _pesanSandi;
            private set => SetProperty(ref _pesanSandi, value);
        }

        public bool GalatSandi
        {
            get => _galatSandi;
            private set => SetProperty(ref _galatSandi, value);
        }

        private void BukaPanelSandi()
        {
            SandiLama = string.Empty;
            SandiBaru = string.Empty;
            KonfirmasiSandi = string.Empty;
            PesanSandi = string.Empty;
            IsPanelSandiTerbuka = true;
        }

        private async Task SimpanSandiAsync()
        {
            if (string.Equals(SessionContext.LoginMethod, "google", StringComparison.OrdinalIgnoreCase))
            {
                PesanSandi = "Akun Google tidak memakai kata sandi aplikasi.";
                GalatSandi = true;
                return;
            }
            if (string.IsNullOrEmpty(SandiLama) || string.IsNullOrEmpty(SandiBaru))
            {
                PesanSandi = "Isi kata sandi lama dan kata sandi baru.";
                GalatSandi = true;
                return;
            }
            if (SandiBaru.Length < 8)
            {
                PesanSandi = "Kata sandi baru minimal 8 karakter.";
                GalatSandi = true;
                return;
            }
            if (!string.Equals(SandiBaru, KonfirmasiSandi, StringComparison.Ordinal))
            {
                PesanSandi = "Konfirmasi kata sandi baru tidak sama.";
                GalatSandi = true;
                return;
            }

            try
            {
                var akun = await _pengguna.AmbilAsync(SessionContext.CurrentUser);
                if (akun == null)
                {
                    PesanSandi = "Akun yang sedang dipakai tidak ditemukan pada database.";
                    GalatSandi = true;
                    return;
                }

                bool berhasil = await _pengguna.UbahKataSandiAsync(akun.ID, SandiLama, SandiBaru);
                if (!berhasil)
                {
                    PesanSandi = "Kata sandi lama salah.";
                    GalatSandi = true;
                    return;
                }

                PesanSandi = "Kata sandi berhasil diubah.";
                GalatSandi = false;
                SandiLama = string.Empty;
                SandiBaru = string.Empty;
                KonfirmasiSandi = string.Empty;
                _logger?.LogInformation("Kata sandi akun {Username} diubah sendiri.", akun.Username);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Gagal mengubah kata sandi akun {Username}", SessionContext.CurrentUser);
                PesanSandi = "Gagal mengubah kata sandi: " + ex.Message;
                GalatSandi = true;
            }
        }

        /// <summary>
        /// Isi kartu "Akun Saya": nama, peran, dan status akun yang sedang dipakai.
        /// Dipanggil untuk semua peran — termasuk yang tidak berhak mengelola akun —
        /// karena halaman ini satu-satunya tempat pemakai melihat akunnya sendiri.
        /// </summary>
        public async Task MuatAkunSayaAsync()
        {
            AkunSayaMasuk = string.Equals(SessionContext.LoginMethod, "google", StringComparison.OrdinalIgnoreCase)
                ? "Masuk dengan Google"
                : "Masuk dengan akun aplikasi";

            try
            {
                var akun = await _pengguna.AmbilAsync(SessionContext.CurrentUser);
                if (akun != null)
                {
                    AkunSayaNama = string.IsNullOrWhiteSpace(akun.NamaTampilan) ? akun.Username : akun.NamaTampilan;
                    AkunSayaPeran = akun.PeranTampil;
                    AkunSayaStatus = akun.StatusTampil;
                    AkunSayaAktif = akun.Aktif;
                    AkunSayaCatatan = akun.Aktif
                        ? string.Empty
                        : "Akun ini ditandai nonaktif. Hubungi Administrator sebelum keluar, karena sesi ini tidak bisa dipakai masuk lagi.";
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal membaca akun sendiri untuk kartu Akun Saya");
            }

            // Tidak ada akun aplikasi bernama ini — mis. masuk lewat Google dengan email
            // yang belum terdaftar. Peran yang ditampilkan adalah peran sesi yang sedang
            // berlaku (peran itulah yang dipakai penegakan izin), bukan tebakan.
            AkunSayaNama = SessionContext.NamaPanggil;
            AkunSayaPeran = PeranPengguna.Tampilan(SessionContext.Peran);
            AkunSayaStatus = "Aktif";
            AkunSayaAktif = true;
            AkunSayaCatatan = $"Belum ada akun aplikasi bernama '{SessionContext.CurrentUser}'; peran di atas adalah peran sesi masuk.";
        }

        /// <summary>Muat ulang daftar akun; dipanggil saat halaman dibuka dan setelah perubahan.</summary>
        public async Task MuatAsync()
        {
            // Kartu "Akun Saya" diisi lebih dulu dan untuk semua peran; daftar akun
            // di bawahnya hanya untuk Administrator.
            await MuatAkunSayaAsync();

            if (!BolehAkses) return;

            try
            {
                var daftar = await _pengguna.DaftarAsync();
                _semua.Clear();
                _semua.AddRange(daftar);
                TerapkanFilter();

                if (_terpilih != null)
                    Terpilih = Daftar.FirstOrDefault(p => p.ID == _terpilih.ID);

                // Ringkasan dipasang di judul kartu daftar akun, bukan di baris status.
                // Baris status hanya dipakai untuk hasil tindakan (simpan/reset/hapus).
                RingkasanDaftar = $"{_semua.Count(p => p.Aktif)} aktif dari {_semua.Count} akun";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memuat daftar pengguna");
                Pesan = "Gagal memuat daftar akun: " + ex.Message;
                AdaGalat = true;
            }
        }

        private void TerapkanFilter()
        {
            string kunci = _cariTeks.Trim();
            IEnumerable<Pengguna> sumber = _semua;
            if (!string.IsNullOrEmpty(kunci))
            {
                sumber = _semua.Where(p =>
                    (p.NamaTampilan?.Contains(kunci, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (p.Username?.Contains(kunci, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (p.PeranTampil?.Contains(kunci, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (p.Peran?.Contains(kunci, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            int? idTerpilih = _terpilih?.ID;
            Daftar.Clear();
            foreach (var akun in sumber) Daftar.Add(akun);

            // Daftar dibangun ulang dari nol, jadi ListBox sempat kehilangan pilihannya
            // (dan tombol Reset/Hapus ikut mati). Kembalikan pilihan lama bila akunnya
            // masih lolos saringan — langsung ke backing field supaya isian form yang
            // sedang diketik pengguna tidak diisi ulang — lalu segarkan ulang tombol.
            if (idTerpilih is int id && Daftar.FirstOrDefault(p => p.ID == id) is { } masihAda)
                _terpilih = masihAda;
            else if (idTerpilih != null)
                _terpilih = null;

            OnPropertyChanged(nameof(Terpilih));
            PerbaruiTombol();
        }

        private void MulaiBaru()
        {
            ModeBaru = true;
            _terpilih = null;
            OnPropertyChanged(nameof(Terpilih));
            Nama = string.Empty;
            Username = string.Empty;
            Peran = PeranPengguna.Operator;
            KataSandi = string.Empty;
            Aktif = true;
            Pesan = string.Empty;
            PerbaruiTombol();
        }

        private async Task SimpanAsync()
        {
            SedangSibuk = true;
            try
            {
                if (ModeBaru)
                {
                    var baru = await _pengguna.TambahAsync(
                        Username, Nama, Peran, KataSandi, SessionContext.CurrentUser);

                    Pesan = $"Akun '{baru.Username}' dibuat sebagai {baru.PeranTampil}.";
                    AdaGalat = false;
                }
                else if (_terpilih != null)
                {
                    await _pengguna.UbahAsync(
                        _terpilih.ID, Nama, Peran, Aktif, SessionContext.CurrentUser);

                    Pesan = $"Perubahan akun '{_terpilih.Username}' tersimpan.";
                    AdaGalat = false;
                }

                await MuatAsync();
            }
            catch (Exception ex)
            {
                Pesan = ex.Message;
                AdaGalat = true;
            }
            finally
            {
                SedangSibuk = false;
            }
        }

        private async Task ResetSandiAsync()
        {
            if (_terpilih == null) return;

            if (string.IsNullOrWhiteSpace(KataSandi))
            {
                Pesan = "Isi kata sandi baru lebih dulu di kolom Kata Sandi, lalu tekan Reset Kata Sandi.";
                AdaGalat = true;
                return;
            }

            bool lanjut = await _message.ShowConfirmationAsync(
                "Reset kata sandi",
                $"Ganti kata sandi akun '{_terpilih.Username}' dengan yang baru?");
            if (!lanjut) return;

            SedangSibuk = true;
            try
            {
                await _pengguna.ResetKataSandiAsync(_terpilih.ID, KataSandi, SessionContext.CurrentUser);
                KataSandi = string.Empty;
                Pesan = $"Kata sandi akun '{_terpilih.Username}' berhasil direset.";
                AdaGalat = false;
                await MuatAsync();
            }
            catch (Exception ex)
            {
                Pesan = ex.Message;
                AdaGalat = true;
            }
            finally
            {
                SedangSibuk = false;
            }
        }

        private async Task HapusAsync()
        {
            if (_terpilih == null) return;

            if (string.Equals(_terpilih.Username, SessionContext.CurrentUser, StringComparison.OrdinalIgnoreCase))
            {
                Pesan = "Akun yang sedang dipakai tidak bisa dihapus. Keluar dulu, lalu masuk dengan akun Administrator lain.";
                AdaGalat = true;
                return;
            }

            bool lanjut = await _message.ShowConfirmationAsync(
                "Hapus akun",
                $"Hapus akun '{_terpilih.Username}' ({_terpilih.PeranTampil})? Tindakan ini tidak bisa dibatalkan.");
            if (!lanjut) return;

            SedangSibuk = true;
            try
            {
                string nama = _terpilih.Username;
                await _pengguna.HapusAsync(_terpilih.ID);
                Pesan = $"Akun '{nama}' dihapus.";
                AdaGalat = false;
                await MuatAsync();
            }
            catch (Exception ex)
            {
                Pesan = ex.Message;
                AdaGalat = true;
            }
            finally
            {
                SedangSibuk = false;
            }
        }

        private void PerbaruiTombol()
        {
            ((AsyncRelayCommand)SimpanCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)ResetSandiCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)HapusCommand).RaiseCanExecuteChanged();
        }
    }
}
