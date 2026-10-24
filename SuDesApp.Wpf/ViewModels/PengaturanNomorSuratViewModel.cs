using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SuDesApp.Wpf.Mvvm;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Satu baris pengaturan penomoran surat: awalan nomor yang bisa diganti
    /// pengguna (mis. SKD 470 → 471) dengan contoh hasil nomornya.
    /// </summary>
    public class PenomoranBarisVM : ObservableObject
    {
        private string _format;

        public string NamaJenis { get; }
        public string DisplayName { get; }
        public string KodeJenis { get; }

        /// <summary>Apakah surat ini ikut penomoran bersama (satu urutan dengan SKD).</summary>
        public bool IsSharedNumbering { get; }

        /// <summary>Format nomor bawaan aplikasi.</summary>
        public string FormatBawaan { get; }

        public string AwalanBawaan { get; }

        /// <summary>Format nomor penuh yang dipakai; kosong = kembali ke bawaan.</summary>
        public string Format
        {
            get => _format;
            set
            {
                if (SetProperty(ref _format, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(Contoh));
                    OnPropertyChanged(nameof(Awalan));
                    OnPropertyChanged(nameof(Disesuaikan));
                    OnPropertyChanged(nameof(Terubah));
                    OnPropertyChanged(nameof(PesanKesalahan));
                    OnPropertyChanged(nameof(AdaKesalahan));
                }
            }
        }

        public string FormatTersimpan { get; private set; }

        /// <summary>Awalan hasil format, dipakai untuk mendeteksi bentrok deret.</summary>
        public string Awalan => Format.Trim().Length == 0
            ? AwalanBawaan
            : SuDesApp.Services.PenomoranSuratService.AmbilAwalan(Format);

        /// <summary>Berbeda dari nilai tersimpan — perlu disimpan.</summary>
        public bool Terubah =>
            !string.Equals(Format.Trim(), FormatTersimpan.Trim(), StringComparison.Ordinal);

        /// <summary>Berbeda dari bawaan aplikasi (boleh dikembalikan lewat tombol Bawaan).</summary>
        public bool Disesuaikan =>
            !string.Equals(Format.Trim(), FormatBawaan, StringComparison.Ordinal);

        /// <summary>Pesan kesalahan pola nomor.</summary>
        public string PesanKesalahan
        {
            get
            {
                var bersih = Format.Trim();
                if (bersih.Length == 0 || string.Equals(bersih, FormatBawaan, StringComparison.Ordinal))
                    return string.Empty;

                return SuDesApp.Services.PenomoranSuratService.FormatValid(bersih, out var pesan)
                    ? string.Empty
                    : pesan;
            }
        }

        public bool AdaKesalahan => PesanKesalahan.Length > 0;

        /// <summary>Contoh nomor surat yang akan tercetak dengan pola saat ini.</summary>
        public string Contoh => SuDesApp.Services.PenomoranSuratService.Contoh(
            Format.Trim().Length == 0 ? FormatBawaan : Format,
            kodeJenis: KodeJenis);

        /// <summary>Keterangan apakah jenis surat ini berbagi urutan nomor dengan SKD.</summary>
        public string KeteranganUrutan => IsSharedNumbering
            ? "Berbagi urutan nomor dengan SKD dan surat keterangan lain"
            : "Urutan nomor tersendiri";

        public RelayCommand KembalikanBawaanCommand { get; }

        public PenomoranBarisVM(SuDesApp.Services.PenomoranSuratEntri entri)
        {
            NamaJenis = entri.NamaJenis;
            DisplayName = entri.DisplayName;
            KodeJenis = entri.KodeJenis;
            IsSharedNumbering = entri.IsSharedNumbering;
            FormatBawaan = entri.FormatBawaan;
            AwalanBawaan = entri.AwalanBawaan;
            _format = entri.Format;
            FormatTersimpan = entri.Format;
            KembalikanBawaanCommand = new RelayCommand(() => Format = FormatBawaan);
        }
    }

    /// <summary>
    /// Halaman Pengaturan Nomor Surat — halaman mandiri di navigasi Menu Utama
    /// untuk mengatur pola nomor tiap jenis surat. Dulunya bagian
    /// "Penomoran Surat" di dalam halaman Pengaturan Aplikasi.
    /// </summary>
    public class PengaturanNomorSuratViewModel : ObservableObject
    {
        private readonly ILogger<PengaturanNomorSuratViewModel> _logger;
        private readonly SuDesApp.Services.PenomoranSuratService? _penomoranSurat;

        public PengaturanNomorSuratViewModel(
            ILogger<PengaturanNomorSuratViewModel> logger,
            SuDesApp.Services.PenomoranSuratService? penomoranSurat = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _penomoranSurat = penomoranSurat;

            SimpanPenomoranCommand = new AsyncRelayCommand(SimpanPenomoranAsync, () => AdaPerubahanPenomoran && !PenomoranSibuk);
            MuatUlangPenomoranCommand = new RelayCommand(MuatPenomoran);
            MuatPenomoran();
        }

        // =======================================================================
        // Pengaturan penomoran surat per jenis (awalan nomor).
        // =======================================================================

        public ObservableCollection<PenomoranBarisVM> Penomoran { get; } = new();

        private string _penomoranInfo = string.Empty;
        private string _penomoranPeringatan = string.Empty;
        private bool _penomoranSibuk;

        /// <summary>Keterangan hasil muat/simpan pengaturan penomoran.</summary>
        public string PenomoranInfo
        {
            get => _penomoranInfo;
            private set => SetProperty(ref _penomoranInfo, value);
        }

        /// <summary>Peringatan awalan kembar (nomor berpotensi sama antar jenis surat).</summary>
        public string PenomoranPeringatan
        {
            get => _penomoranPeringatan;
            private set
            {
                if (SetProperty(ref _penomoranPeringatan, value))
                    OnPropertyChanged(nameof(AdaPeringatanPenomoran));
            }
        }

        public bool AdaPeringatanPenomoran => _penomoranPeringatan.Length > 0;

        public bool PenomoranSibuk
        {
            get => _penomoranSibuk;
            private set
            {
                if (SetProperty(ref _penomoranSibuk, value)) SimpanPenomoranCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>Ada awalan pada daftar yang berbeda dari nilai tersimpan.</summary>
        public bool AdaPerubahanPenomoran => Penomoran.Any(b => b.Terubah);

        /// <summary>Berkas penyesuaian penomoran (aman dari pembaruan aplikasi).</summary>
        public string PenomoranLokasiBerkas => _penomoranSurat?.BerkasPenyesuaian ?? "-";

        /// <summary>Berkas bawaan yang memuat daftar jenis surat + format default.</summary>
        public string PenomoranLokasiBawaan => _penomoranSurat?.BerkasBawaan ?? "-";

        public string PenomoranVersiInfo => _penomoranSurat?.RiwayatVersiTampil ?? "Riwayat versi pola nomor tidak tersedia.";
        public string PenomoranCakupanInfo => _penomoranSurat?.CakupanAturanTampil ?? "Profil peraturan daerah tidak tersedia.";

        public bool PenomoranTersedia => _penomoranSurat != null;

        public AsyncRelayCommand SimpanPenomoranCommand { get; }
        public RelayCommand MuatUlangPenomoranCommand { get; }

        /// <summary>Muat ulang daftar penomoran dari berkas (membuang perubahan belum disimpan).</summary>
        private void MuatPenomoran()
        {
            Penomoran.Clear();
            OnPropertyChanged(nameof(PenomoranVersiInfo));

            if (_penomoranSurat == null)
            {
                PenomoranInfo = "Pengaturan penomoran tidak tersedia pada sesi ini.";
                OnPropertyChanged(nameof(AdaPerubahanPenomoran));
                SimpanPenomoranCommand.RaiseCanExecuteChanged();
                return;
            }

            try
            {
                foreach (var entri in _penomoranSurat.MuatSemua())
                {
                    var baris = new PenomoranBarisVM(entri);
                    baris.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName != nameof(PenomoranBarisVM.Format)) return;

                        PerbaruiPeringatanPenomoran();
                        OnPropertyChanged(nameof(AdaPerubahanPenomoran));
                        SimpanPenomoranCommand.RaiseCanExecuteChanged();
                    };
                    Penomoran.Add(baris);
                }

                PerbaruiPeringatanPenomoran();
                PenomoranInfo = $"{Penomoran.Count} jenis surat. Pola penuh yang baru berlaku untuk surat " +
                                "yang dibuat setelah pengaturan ini disimpan; nomor terbit tidak berubah.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memuat pengaturan penomoran surat");
                PenomoranInfo = "Gagal memuat pengaturan penomoran: " + ex.Message;
            }

            OnPropertyChanged(nameof(AdaPerubahanPenomoran));
            OnPropertyChanged(nameof(PenomoranVersiInfo));
            SimpanPenomoranCommand.RaiseCanExecuteChanged();
        }

        /// <summary>
        /// Perbarui peringatan awalan kembar: awalan yang dipakai beberapa jenis
        /// surat dengan urutan nomor sendiri-sendiri bisa menghasilkan nomor sama.
        /// </summary>
        private void PerbaruiPeringatanPenomoran()
        {
            var entri = Penomoran.Select(b => new SuDesApp.Services.PenomoranSuratEntri
            {
                NamaJenis = b.NamaJenis,
                DisplayName = b.DisplayName,
                KodeJenis = b.KodeJenis,
                IsSharedNumbering = b.IsSharedNumbering,
                Awalan = b.Awalan
            });

            var bentrok = SuDesApp.Services.PenomoranSuratService.CariBentrok(entri);
            PenomoranPeringatan = bentrok.Count == 0 ? string.Empty : string.Join("\n", bentrok);
        }

        /// <summary>Simpan seluruh pola nomor yang diubah sekaligus.</summary>
        private async Task SimpanPenomoranAsync()
        {
            if (_penomoranSurat == null || PenomoranSibuk) return;

            var belumBenar = Penomoran.FirstOrDefault(b => b.AdaKesalahan);
            if (belumBenar != null)
            {
                PenomoranInfo = $"Pola nomor pada '{belumBenar.DisplayName}' belum benar: {belumBenar.PesanKesalahan}";
                return;
            }

            var perubahan = Penomoran.Where(b => b.Terubah)
                .Select(b => new SuDesApp.Services.PerubahanPenomoran
                {
                    NamaJenis = b.NamaJenis,
                    Format = b.Format
                })
                .ToList();

            if (perubahan.Count == 0)
            {
                PenomoranInfo = "Belum ada pola nomor yang diubah.";
                return;
            }

            PenomoranSibuk = true;
            try
            {
                var (ok, pesan) = await _penomoranSurat.SimpanAsync(perubahan);
                if (ok) MuatPenomoran();
                PenomoranInfo = pesan;

                if (ok)
                    _logger.LogInformation("Pengaturan penomoran surat diperbarui dari halaman Pengaturan Nomor Surat.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyimpan pengaturan penomoran surat");
                PenomoranInfo = "Gagal menyimpan penomoran: " + ex.Message;
            }
            finally
            {
                PenomoranSibuk = false;
            }
        }
    }
}
