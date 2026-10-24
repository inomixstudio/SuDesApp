using System;
using System.IO;
using System.Windows.Documents;

using Microsoft.Extensions.Logging;

using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Utilities;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Halaman baca dokumen Markdown bawaan aplikasi (folder <c>docs</c>).
    ///
    /// Isinya disusun menjadi <see cref="FlowDocument"/> sehingga dokumen dibaca
    /// <b>di dalam aplikasi</b>. Sebelumnya berkas dibuka lewat
    /// <c>Process.Start</c> (aplikasi asosiasi Windows), yang bermasalah di komputer
    /// desa: berkas <c>.md</c> umumnya tidak punya asosiasi sama sekali, aplikasi
    /// pembaca PDF belum tentu terpasang, dan jendela luar menutupi aplikasi sehingga
    /// operator bisa tersesat keluar dari alur kerjanya.
    /// </summary>
    public class DokumenBacaViewModel : ObservableObject
    {
        private readonly ILogger<DokumenBacaViewModel>? _logger;

        /// <param name="judul">Judul dokumen seperti tampil di daftar Dokumentasi.</param>
        /// <param name="jalurBerkas">Jalur lengkap berkas yang sudah ditemukan.</param>
        /// <param name="kembali">
        /// Aksi tombol Kembali. Kosong = kembali ke tampilan awal (bawaan); halaman
        /// Dokumentasi mengisinya agar pembaca kembali ke daftar dokumen.
        /// </param>
        public DokumenBacaViewModel(
            string judul,
            string jalurBerkas,
            NavigationService navigation,
            Action? kembali = null,
            ILogger<DokumenBacaViewModel>? logger = null)
        {
            if (navigation is null)
            {
                throw new ArgumentNullException(nameof(navigation));
            }

            _logger = logger;

            Title = string.IsNullOrWhiteSpace(judul) ? "Dokumen" : judul.Trim();
            JalurBerkas = jalurBerkas ?? string.Empty;
            NamaBerkas = JalurBerkas.Length == 0 ? string.Empty : Path.GetFileName(JalurBerkas);

            BatalCommand = new RelayCommand(kembali ?? navigation.ShowDefault);

            Muat();
        }

        public string Title { get; }

        public string Subtitle => "Dokumen bawaan aplikasi — dibaca di dalam aplikasi";

        /// <summary>Jalur lengkap berkas yang dibaca, ditampilkan sebagai keterangan.</summary>
        public string JalurBerkas { get; }

        /// <summary>Nama berkasnya saja, untuk keterangan ringkas di header.</summary>
        public string NamaBerkas { get; }

        private string _ukuranText = string.Empty;

        /// <summary>Ukuran berkas yang mudah dibaca, mis. "12 KB".</summary>
        public string UkuranText
        {
            get => _ukuranText;
            private set => SetProperty(ref _ukuranText, value);
        }

        private FlowDocument? _isi;

        /// <summary>Dokumen siap tampil; null bila berkasnya gagal dibaca.</summary>
        public FlowDocument? Isi
        {
            get => _isi;
            private set => SetProperty(ref _isi, value);
        }

        private string _pesanGalat = string.Empty;

        /// <summary>Alasan dokumen tidak bisa dibaca; kosong bila berhasil.</summary>
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

        /// <summary>True bila ada galat baca (untuk Visibility strip peringatan).</summary>
        public bool AdaGalat => !string.IsNullOrWhiteSpace(PesanGalat);

        public RelayCommand BatalCommand { get; }

        private void Muat()
        {
            if (JalurBerkas.Length == 0 || !File.Exists(JalurBerkas))
            {
                PesanGalat = "Berkas dokumen tidak ditemukan di folder aplikasi.";
                return;
            }

            try
            {
                UkuranText = BerkasDokumentasi.UkuranTerbaca(new FileInfo(JalurBerkas).Length);
                Isi = MarkdownKeFlowDocument.Susun(MarkdownDokumen.Uraikan(File.ReadAllText(JalurBerkas)));
                _logger?.LogInformation("Dokumen bawaan dibaca di dalam aplikasi: {Jalur}", JalurBerkas);
            }
            catch (Exception ex)
            {
                PesanGalat = "Dokumen tidak bisa dibaca: " + ex.Message;
                _logger?.LogWarning(ex, "Gagal membaca dokumen bawaan {Jalur}", JalurBerkas);
            }
        }
    }
}
