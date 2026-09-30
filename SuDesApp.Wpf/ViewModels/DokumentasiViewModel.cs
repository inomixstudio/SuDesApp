using System.IO;
using Microsoft.Extensions.Logging;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Utilities;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Satu dokumen bawaan aplikasi pada halaman Dokumentasi. Keberadaan berkasnya
    /// diperiksa saat halaman dibuka: dokumen yang tidak ikut terpasang tetap
    /// ditampilkan (dengan penanda "berkas tidak ditemukan"), bukan disembunyikan —
    /// supaya pengguna tahu dokumen apa yang seharusnya ada di folder aplikasi.
    /// </summary>
    public class DokumentasiItem : ObservableObject
    {
        public DokumentasiItem(string judul, string keterangan, string jalurRelatif, bool pdf)
        {
            Judul = judul;
            Keterangan = keterangan;
            JalurRelatif = jalurRelatif;
            Pdf = pdf;
            LabelJenis = pdf ? "PDF" : "MARKDOWN";
        }

        /// <summary>
        /// True untuk berkas PDF. Menentukan cara membacanya di dalam aplikasi:
        /// PDF lewat pratinjau PDFium, berkas .md lewat halaman baca dokumen.
        /// </summary>
        public bool Pdf { get; }

        public string Judul { get; }

        /// <summary>Satu kalimat isi dokumen — dipakai sebagai keterangan di daftar.</summary>
        public string Keterangan { get; }

        /// <summary>Jalur relatif terhadap folder docs, mis. <c>regulasi/Perbup-74-2020-....pdf</c>.</summary>
        public string JalurRelatif { get; }

        /// <summary>Label jenis berkas yang tampil sebagai lencana (PDF / MARKDOWN).</summary>
        public string LabelJenis { get; }

        /// <summary>Jalur lengkap berkas yang ditemukan; null bila dokumen tidak ada.</summary>
        public string? JalurLengkap { get; private set; }

        /// <summary>True bila berkas dokumen benar-benar ada di folder aplikasi.</summary>
        public bool AdaBerkas => JalurLengkap is not null;

        private string _ukuranText = string.Empty;

        /// <summary>Ukuran berkas yang mudah dibaca, mis. "946 KB".</summary>
        public string UkuranText
        {
            get => _ukuranText;
            private set => SetProperty(ref _ukuranText, value);
        }

        /// <summary>Keterangan singkat di sisi kanan kartu: ukuran berkas, atau alasan tidak ada.</summary>
        public string StatusText => AdaBerkas ? UkuranText : "berkas tidak ditemukan";

        /// <summary>Cari berkasnya di folder dokumen aplikasi dan catat ukurannya.</summary>
        internal void PeriksaKeberadaan()
        {
            JalurLengkap = BerkasDokumentasi.Cari(JalurRelatif);

            try
            {
                UkuranText = JalurLengkap is null
                    ? string.Empty
                    : BerkasDokumentasi.UkuranTerbaca(new FileInfo(JalurLengkap).Length);
            }
            catch (Exception)
            {
                // Berkas terkunci/hilang tepat saat dibaca: tetap tampil tanpa ukuran,
                // tombol Buka yang akan melaporkan galatnya bila ditekan.
                UkuranText = string.Empty;
            }

            OnPropertyChanged(nameof(JalurLengkap));
            OnPropertyChanged(nameof(AdaBerkas));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    /// <summary>Kelompok dokumen pada halaman Dokumentasi (regulasi, panduan, kebijakan).</summary>
    public class KelompokDokumentasi
    {
        public KelompokDokumentasi(string nama, string keterangan, IReadOnlyList<DokumentasiItem> dokumen)
        {
            Nama = nama;
            Keterangan = keterangan;
            Dokumen = dokumen;
        }

        public string Nama { get; }

        public string Keterangan { get; }

        public IReadOnlyList<DokumentasiItem> Dokumen { get; }
    }

    /// <summary>
    /// ViewModel halaman Dokumentasi (seksi BANTUAN): daftar dokumen bawaan aplikasi
    /// beserta tombol baca per dokumen.
    ///
    /// Seluruh dokumen dibaca <b>di dalam aplikasi</b>: berkas PDF dibuka pada
    /// pratinjau PDFium yang sama dengan surat dan laporan, sedangkan berkas
    /// <c>.md</c> disusun menjadi dokumen siap baca. Tidak ada berkas yang diserahkan
    /// ke aplikasi asosiasi Windows — di komputer desa berkas <c>.md</c> umumnya tidak
    /// punya asosiasi sama sekali, dan jendela aplikasi luar membuat operator keluar
    /// dari alur kerja aplikasi.
    /// </summary>
    public class DokumentasiViewModel : ObservableObject
    {
        private readonly ILogger<DokumentasiViewModel> _logger;
        private readonly NavigationService _navigation;
        private readonly Func<string, string, PdfPreviewViewModel> _previewFactory;
        private readonly Func<string, string, Action?, DokumenBacaViewModel> _dokumenFactory;

        public DokumentasiViewModel(
            ILogger<DokumentasiViewModel> logger,
            NavigationService navigation,
            Func<string, string, PdfPreviewViewModel> previewFactory,
            Func<string, string, Action?, DokumenBacaViewModel> dokumenFactory)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _previewFactory = previewFactory ?? throw new ArgumentNullException(nameof(previewFactory));
            _dokumenFactory = dokumenFactory ?? throw new ArgumentNullException(nameof(dokumenFactory));

            // Daftar dokumen disusun di sini, bukan dipindai dari folder, supaya
            // tiap dokumen punya judul dan keterangan yang bisa dibaca pengguna desa
            // (nama berkas seperti policy-tutup-buku-dan-retensi.md tidak berarti apa pun).
            Kelompok = new List<KelompokDokumentasi>
            {
                new KelompokDokumentasi(
                    "Dasar regulasi",
                    "Berkas resmi yang menjadi acuan penomoran dan tata naskah surat desa.",
                    new List<DokumentasiItem>
                    {
                        new(
                            "Lampiran Perbup Karawang 74/2020 — Tata Naskah Dinas Desa",
                            "Dasar kode klasifikasi dan pola nomor surat desa (PDF lampiran Perbup).",
                            "regulasi/Perbup-74-2020-Lampiran-Tata-Naskah-Dinas-Desa.pdf",
                            pdf: true)
                    }),
                new KelompokDokumentasi(
                    "Panduan & referensi",
                    "Penjelasan cara kerja fitur yang dipakai sehari-hari.",
                    new List<DokumentasiItem>
                    {
                        new(
                            "Referensi penomoran surat",
                            "Awalan tiap jenis surat, aturan deret nomor bersama, dan dasar regulasinya.",
                            "referensi-penomoran-surat.md",
                            pdf: false),
                        new(
                            "Alur persetujuan surat",
                            "Tahapan ajukan → verifikasi → tanda tangan/tolak beserta peran yang berwenang.",
                            "alur-persetujuan-surat.md",
                            pdf: false),
                        new(
                            "Perangkat desa & SK",
                            "Aturan jabatan per wilayah, status jabatan, dan penerbitan SK perangkat.",
                            "perangkat-desa-dan-sk.md",
                            pdf: false),
                        new(
                            "API Desa (integrasi sistem luar)",
                            "Daftar endpoint, kunci API, batas laju permintaan, dan contoh pemakaian.",
                            "api-desa.md",
                            pdf: false)
                    }),
                new KelompokDokumentasi(
                    "Kebijakan",
                    "Keputusan yang mengikat cara data dan surat desa dikelola.",
                    new List<DokumentasiItem>
                    {
                        new(
                            "Kebijakan peran pengguna",
                            "Hak akses tiap peran (pengelola, operator, verifikator) di dalam aplikasi.",
                            "policy-peran-pengguna.md",
                            pdf: false),
                        new(
                            "Kebijakan tutup buku & retensi",
                            "Aturan penutupan buku tahunan dan masa simpan data desa.",
                            "policy-tutup-buku-dan-retensi.md",
                            pdf: false)
                    }),
                new KelompokDokumentasi(
                    "Catatan teknis",
                    "Bahan rujukan teknisi yang merawat aplikasi.",
                    new List<DokumentasiItem>
                    {
                        new(
                            "Pembaruan tambalan (teknis)",
                            "Cara kerja pembaruan kecil: patch.json, verifikasi SHA-256, dan penerapannya.",
                            "PEMBARUAN-TAMBALAN.md",
                            pdf: false)
                    })
            };

            var semua = Kelompok.SelectMany(k => k.Dokumen).ToList();
            foreach (var dokumen in semua)
            {
                dokumen.PeriksaKeberadaan();
            }

            JumlahTersedia = semua.Count(d => d.AdaBerkas);

            var folder = BerkasDokumentasi.CariFolder();
            LokasiInfo = folder is null
                ? "Folder dokumen tidak ditemukan. Dokumen ikut terpasang bersama aplikasi, "
                  + "jadi bila aplikasi dipindahkan sebagian, dokumennya ikut hilang."
                : "Dokumen tersimpan di " + folder;

            BukaCommand = new RelayCommand<DokumentasiItem>(Buka);
            BukaFolderCommand = new RelayCommand(BukaFolder);

            _logger.LogInformation(
                "Halaman Dokumentasi dibuka: {Tersedia} dari {Total} dokumen ditemukan",
                JumlahTersedia, semua.Count);
        }

        public string Title => "DOKUMENTASI";

        public string Subtitle => "Dokumen bawaan aplikasi — dasar regulasi, panduan, dan kebijakan";

        /// <summary>
        /// Identitas aplikasi pada statusbar halaman; disusun otomatis dari atribut
        /// assembly sehingga selalu mengikuti versi terbaru tanpa perlu disunting.
        /// </summary>
        public string VersionFooter => IdentitasAplikasi.FooterVersi;

        public IReadOnlyList<KelompokDokumentasi> Kelompok { get; }

        private int _jumlahTersedia;

        /// <summary>Jumlah dokumen yang berkasnya benar-benar ada di folder aplikasi.</summary>
        public int JumlahTersedia
        {
            get => _jumlahTersedia;
            private set => SetProperty(ref _jumlahTersedia, value);
        }

        /// <summary>Jumlah seluruh dokumen yang seharusnya ikut terpasang.</summary>
        public int JumlahTotal => Kelompok.Sum(k => k.Dokumen.Count);

        /// <summary>Ringkasan ketersediaan dokumen, mis. "8 dari 8 dokumen tersedia".</summary>
        public string RingkasanText => JumlahTersedia == JumlahTotal
            ? JumlahTotal + " dokumen tersedia"
            : $"{JumlahTersedia} dari {JumlahTotal} dokumen tersedia";

        private string _lokasiInfo = string.Empty;

        /// <summary>Folder tempat dokumen berada, atau penjelasan bila foldernya tak ada.</summary>
        public string LokasiInfo
        {
            get => _lokasiInfo;
            private set => SetProperty(ref _lokasiInfo, value);
        }

        private string _pesanStatus = string.Empty;

        /// <summary>Hasil tindakan terakhir (berhasil dibuka atau alasan gagal).</summary>
        public string PesanStatus
        {
            get => _pesanStatus;
            private set
            {
                if (SetProperty(ref _pesanStatus, value))
                {
                    OnPropertyChanged(nameof(AdaPesan));
                }
            }
        }

        /// <summary>True bila <see cref="PesanStatus"/> punya isi (untuk Visibility).</summary>
        public bool AdaPesan => !string.IsNullOrWhiteSpace(PesanStatus);

        public RelayCommand<DokumentasiItem> BukaCommand { get; }

        /// <summary>
        /// Buka folder dokumen di File Explorer. Bukan cara membaca dokumen —
        /// dokumen selalu dibaca di dalam aplikasi — melainkan untuk keperluan
        /// teknisi: menyalin berkas atau memastikan dokumen ikut terpasang.
        /// </summary>
        public RelayCommand BukaFolderCommand { get; }

        /// <summary>
        /// Tampilkan dokumen di dalam aplikasi: PDF pada pratinjau PDFium, berkas
        /// <c>.md</c> pada halaman baca. Halaman Dokumentasi ditinggalkan lebih dulu,
        /// jadi pesan status di sini terbaca lagi saat pengguna kembali.
        /// </summary>
        private void Buka(DokumentasiItem? dokumen)
        {
            if (dokumen is null)
            {
                return;
            }

            if (!dokumen.AdaBerkas)
            {
                PesanStatus = $"Berkas {dokumen.JalurRelatif} tidak ditemukan di folder dokumen.";
                return;
            }

            var jalur = dokumen.JalurLengkap!;

            if (dokumen.Pdf)
            {
                _logger.LogInformation("Dokumen PDF dibaca di dalam aplikasi: {Jalur}", jalur);
                _navigation.Navigate(_previewFactory(dokumen.Judul, jalur));
            }
            else
            {
                _logger.LogInformation("Dokumen Markdown dibaca di dalam aplikasi: {Jalur}", jalur);
                // Tombol Kembali halaman baca mengembalikan ke daftar dokumen ini,
                // bukan ke tampilan awal aplikasi.
                _navigation.Navigate(_dokumenFactory(dokumen.Judul, jalur, () => _navigation.Navigate(this)));
            }

            PesanStatus = $"Dokumen ditampilkan di dalam aplikasi: {dokumen.Judul}.";
        }

        private void BukaFolder()
        {
            var folder = BerkasDokumentasi.CariFolder();
            var galat = BerkasDokumentasi.Buka(folder);

            PesanStatus = galat is null
                ? "Folder dokumen dibuka."
                : "Folder dokumen tidak bisa dibuka: " + galat;
        }
    }
}
