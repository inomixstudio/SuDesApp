using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Isi dialog persetujuan dokumen hukum yang muncul sebelum login pada
    /// pemasangan pertama — dan lagi setiap versi dokumen dinaikkan.
    ///
    /// Dialog ini sengaja tidak bisa ditutup lalu diabaikan: pengguna memilih
    /// "Setuju &amp; Lanjutkan" atau keluar dari aplikasi. Persetujuan yang
    /// tercatat memuat versi dokumen dan waktunya, sehingga halaman Tentang
    /// bisa menunjukkan apa yang disetujui dan kapan — bukan sekadar "sudah
    /// setuju" tanpa jejak.
    /// </summary>
    public class PersetujuanDokumenViewModel : ObservableObject
    {
        private readonly ILogger<PersetujuanDokumenViewModel>? _logger;

        /// <summary>Keadaan dokumen saat dialog dibuka (versi dibaca sekali).</summary>
        public IReadOnlyList<StatusPersetujuan> Dokumen { get; }

        /// <summary>True bila ada dokumen yang versinya berbeda dari yang disetujui.</summary>
        public bool AdaVersiBerubah => Dokumen.Any(d => d.VersiBerubah);

        /// <summary>
        /// Ada dokumen yang berkasnya tidak ditemukan / versinya tak terbaca.
        /// Dialog tetap bisa disetujui — tetapi teks persetujuannya harus jujur
        /// menyebut dokumen mana yang tidak tersedia, bukan seolah semuanya dibaca.
        /// </summary>
        public bool AdaBerkasHilang => Dokumen.Any(d => d.VersiTerpasang is null);

        /// <summary>Peringatan untuk dokumen yang tidak terbaca; kosong bila semua tersedia.</summary>
        public string PesanBerkasHilang => AdaBerkasHilang
            ? "Dokumen berikut tidak ditemukan atau versinya tidak terbaca di folder aplikasi: "
              + string.Join(", ", Dokumen.Where(d => d.VersiTerpasang is null).Select(d => d.Dokumen.Judul))
              + ". Anda dapat melanjutkan; pasang ulang aplikasi agar dokumennya lengkap."
            : string.Empty;

        /// <summary>True bila minimal satu dokumen belum pernah disetujui sama sekali.</summary>
        public bool AdaYangBelumPernah => Dokumen.Any(d => d.Tercatat is null);

        public string Judul => AdaVersiBerubah
            ? "Dokumen diperbarui"
            : "Sebelum memakai SuDesApp";

        public string Subjudul => AdaVersiBerubah
            ? "Kebijakan Privasi dan/atau Syarat dan Ketentuan telah diperbarui. "
              + "Mari baca versi barunya dulu — pemakaian aplikasi dilanjutkan setelah Anda menyetujuinya."
            : "Pertama kali aplikasi dibuka di komputer ini. Bacalah kebijakan privasi dan "
              + "syarat penggunaan berikut, lalu nyatakan persetujuan Anda.";

        /// <summary>
        /// Ringkasan di bawah daftar: menegaskan bahwa persetujuan hanya
        /// dicatat di komputer ini dan data warga tetap tinggal di desa.
        /// </summary>
        public string Catatan =>
            "Persetujuan dicatat di komputer ini beserta versi dokumen dan waktunya, "
            + "lalu ditampilkan di halaman Tentang → Persetujuan dokumen. Data warga "
            + "tidak ikut dikirim ke mana pun karena persetujuan ini.";

        private bool _terima;

        /// <summary>Centang "saya telah membaca dan menyetujui".</summary>
        public bool Terima
        {
            get => _terima;
            set
            {
                if (SetProperty(ref _terima, value))
                {
                    OnPropertyChanged(nameof(BisaLanjut));
                }
            }
        }

        private bool _menyimpan;

        /// <summary>True selama persetujuan sedang ditulis (mencegah klik ganda).</summary>
        private bool Menyimpan
        {
            get => _menyimpan;
            set
            {
                if (SetProperty(ref _menyimpan, value))
                {
                    OnPropertyChanged(nameof(BisaLanjut));
                }
            }
        }

        /// <summary>Tombol "Setuju &amp; Lanjutkan" aktif hanya bila dicentang dan tidak sedang menyimpan.</summary>
        public bool BisaLanjut => Terima && !Menyimpan;

        private string _pesanGalat = string.Empty;

        /// <summary>Alasan persetujuan gagal disimpan; kosong bila tidak ada.</summary>
        public string PesanGalat
        {
            get => _pesanGalat;
            private set
            {
                if (SetProperty(ref _pesanGalat, value))
                {
                    OnPropertyChanged(nameof(AdaGalat));
                }
            }
        }

        public bool AdaGalat => !string.IsNullOrWhiteSpace(PesanGalat);

        public PersetujuanDokumenViewModel(
            IReadOnlyList<StatusPersetujuan>? dokumen = null,
            ILogger<PersetujuanDokumenViewModel>? logger = null)
        {
            _logger = logger;
            Dokumen = dokumen ?? PersetujuanDokumenService.MuatStatus();

            if (Dokumen.Count == 0)
            {
                // Tidak ada dokumen sama sekali bukan alasan menahan pengguna;
                // dialog tetap tampil dengan penjelasan agar tidak terasa rusak.
                PesanGalat = "Dokumen kebijakan tidak ditemukan di folder aplikasi. "
                             + "Aplikasi tetap dapat dipakai; pasang ulang bila dokumennya diperlukan.";
            }
        }

        /// <summary>
        /// Catat persetujuan untuk semua dokumen yang perlu, lalu kembalikan
        /// hasilnya. Hanya dipanggil setelah pengguna mencentang dan menekan
        /// tombol setuju.
        /// </summary>
        public HasilCatatPersetujuan Catat(DateTime waktu)
        {
            Menyimpan = true;
            try
            {
                var hasil = PersetujuanDokumenService.CatatSemua(waktu);
                if (!hasil.Berhasil)
                {
                    PesanGalat = hasil.Pesan;
                    _logger?.LogWarning("Persetujuan dokumen gagal dicatat: {Pesan}", hasil.Pesan);
                }
                else
                {
                    PesanGalat = string.Empty;
                    _logger?.LogInformation(
                        "Persetujuan dokumen dicatat: {Dokumen}",
                        string.Join(", ", Dokumen.Select(d =>
                            $"{d.Dokumen.Kunci} v{d.VersiTerpasang?.Versi ?? "?"}")));
                }

                return hasil;
            }
            finally
            {
                Menyimpan = false;
            }
        }
    }
}
