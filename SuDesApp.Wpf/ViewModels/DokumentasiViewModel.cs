using System.IO;
using SuDesApp.Configuration;
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
        public DokumentasiItem(string judul, string keterangan, string jalurRelatif, bool pdf, string? url = null)
        {
            Judul = judul;
            Keterangan = keterangan;
            JalurRelatif = jalurRelatif;
            Pdf = pdf;
            Url = url;
        }

        /// <summary>
        /// True untuk berkas PDF. Menentukan cara membacanya di dalam aplikasi:
        /// PDF lewat pratinjau SudesPdf, berkas .md lewat halaman baca dokumen.
        /// </summary>
        public bool Pdf { get; }

        public string Judul { get; }

        /// <summary>Satu kalimat isi dokumen — dipakai sebagai keterangan di daftar.</summary>
        public string Keterangan { get; }

        /// <summary>Jalur relatif terhadap folder docs, mis. <c>regulasi/Perbup-74-2020-....pdf</c>.</summary>
        public string JalurRelatif { get; }

        /// <summary>
        /// Alamat web resmi dokumen (http/https) bila dokumen ini juga tersedia
        /// daring. Tautan ini dapat diklik dan dibuka di peramban — dipakai untuk
        /// sumber pemerintah yang tidak pernah ikut terpasang sebagai berkas.
        /// </summary>
        public string? Url { get; }

        /// <summary>True bila dokumen punya tautan resmi yang dapat diklik.</summary>
        public bool HasUrl => TautanWeb.AdalahTautanWeb(Url);

        /// <summary>Nama host situs resmi, mis. <c>satudata.kemendesa.go.id</c>.</summary>
        public string HostTampil => TautanWeb.NamaHost(Url);

        /// <summary>
        /// Label jenis berkas yang tampil sebagai lencana: PDF, MARKDOWN, atau
        /// TAUTAN WEB untuk dokumen yang hanya tersedia daring.
        /// </summary>
        public string LabelJenis
        {
            get
            {
                if (Pdf) return "PDF";
                if (!AdaBerkas && HasUrl) return "TAUTAN WEB";
                return "MARKDOWN";
            }
        }

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

        /// <summary>Keterangan singkat di sisi kanan kartu: ukuran berkas, host situs, atau alasan tidak ada.</summary>
        public string StatusText
        {
            get
            {
                if (AdaBerkas) return UkuranText;
                if (HasUrl) return HostTampil;
                return "berkas tidak ditemukan";
            }
        }

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
            OnPropertyChanged(nameof(LabelJenis));
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
    /// pratinjau SudesPdf yang sama dengan surat dan laporan, sedangkan berkas
    /// <c>.md</c> disusun menjadi dokumen siap baca. Tidak ada berkas yang diserahkan
    /// ke aplikasi asosiasi Windows — di komputer desa berkas <c>.md</c> umumnya tidak
    /// punya asosiasi sama sekali, dan jendela aplikasi luar membuat operator keluar
    /// dari alur kerja aplikasi.
    /// </summary>
    public class DokumentasiViewModel : ObservableObject
    {
        private readonly ILogger<DokumentasiViewModel> _logger;
        private readonly NavigationService _navigation;
        private readonly IUnitOfWork _unitOfWork;
        private readonly Func<string, string, PdfPreviewViewModel> _previewFactory;
        private readonly Func<string, string, Action?, DokumenBacaViewModel> _dokumenFactory;

        public DokumentasiViewModel(
            ILogger<DokumentasiViewModel> logger,
            NavigationService navigation,
            IUnitOfWork unitOfWork,
            Func<string, string, PdfPreviewViewModel> previewFactory,
            Func<string, string, Action?, DokumenBacaViewModel> dokumenFactory)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _previewFactory = previewFactory ?? throw new ArgumentNullException(nameof(previewFactory));
            _dokumenFactory = dokumenFactory ?? throw new ArgumentNullException(nameof(dokumenFactory));

            // Daftar dokumen disusun di sini, bukan dipindai dari folder, supaya
            // tiap dokumen punya judul dan keterangan yang bisa dibaca pengguna desa
            // (nama berkas seperti policy-tutup-buku-dan-retensi.md tidak berarti apa pun).
            Kelompok = new List<KelompokDokumentasi>
            {
                new KelompokDokumentasi(
                    "Dasar regulasi",
                    "Memuat regulasi sesuai kabupaten/kota yang dipilih di Pengaturan Surat.",
                    new List<DokumentasiItem>()),
                new KelompokDokumentasi(
                    "Sumber resmi 2025",
                    "Ringkasan sumber resmi pemerintah untuk data Indeks Desa 2025.",
                    new List<DokumentasiItem>
                    {
                        new(
                            "Sumber resmi Indeks Desa 2025 (baca di dalam aplikasi)",
                            "Ringkasan sumber: dataset pusat, dasar pengukuran, dan batas rincian yang ditampilkan aplikasi.",
                            "indeks-desa-2025.md",
                            pdf: false)
                    }),
                new KelompokDokumentasi(
                    "Panduan & referensi",
                    "Penjelasan cara kerja fitur yang dipakai sehari-hari.",
                    new List<DokumentasiItem>
                    {
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
                            pdf: false),
                        new(
                            "Kebijakan privasi",
                            "Data apa yang tinggal di komputer desa, apa yang dikirim ke server, dan hak warga.",
                            "kebijakan-privasi.md",
                            pdf: false),
                        new(
                            "Syarat dan ketentuan",
                            "Aturan pemakaian, masa aktif & uji coba, pembayaran, dan batas tanggung jawab.",
                            "syarat-ketentuan.md",
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
            BukaTautanCommand = new RelayCommand<DokumentasiItem>(BukaTautan);

            _logger.LogInformation(
                "Halaman Dokumentasi dibuka: {Tersedia} dari {Total} dokumen ditemukan",
                JumlahTersedia, semua.Count);
        }

        /// <summary>Load the regulation that matches the selected village district.</summary>
        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            string? kabupaten = null;
            string? jenisKabupaten = null;
            try
            {
                var infoDesa = await _unitOfWork.DesaRepository.GetInfoDesaAsync(cancellationToken);
                kabupaten = infoDesa?.Kabupaten?.Trim();
                jenisKabupaten = infoDesa?.JenisKabupaten?.Trim();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal membaca kabupaten untuk regulasi Dokumentasi");
            }

            string namaKabupaten = string.IsNullOrWhiteSpace(kabupaten) ? string.Empty : kabupaten;
            var dokumenRegulasi = new List<DokumentasiItem>();
            if (RegulasiKarawangCocok(namaKabupaten, jenisKabupaten))
            {
                dokumenRegulasi.Add(new(
                    "Perbup Karawang Nomor 74 Tahun 2020 — Tata Naskah Dinas Pemerintah Desa",
                    "Acuan daerah untuk tata naskah dan penomoran surat desa. Periksa lagi perubahan terbaru dan ketentuan sektoral sebelum mengubah pola.",
                    "regulasi/Perbup-74-2020-Lampiran-Tata-Naskah-Dinas-Desa.pdf",
                    pdf: true));
            }
            else if (RegulasiBekasiCocok(namaKabupaten, jenisKabupaten))
            {
                dokumenRegulasi.Add(new(
                    "Perbup Bekasi Nomor 215 Tahun 2022 — Tata Naskah Dinas Pemerintah Desa",
                    "Status berlaku pada JDIH Kabupaten Bekasi. Lampiran memberi contoh awalan 470 untuk empat jenis surat keterangan; lihat juga referensi penomoran Bekasi untuk batas penerapannya.",
                    "regulasi/Perbup-215-2022-Bekasi-Tata-Naskah-Dinas-Desa.pdf",
                    pdf: true));
            }

            bool fileTersedia = dokumenRegulasi.Any(d => d.AdaBerkas);
            if (!fileTersedia)
            {
                dokumenRegulasi.Clear();
            }

            string keterangan = fileTersedia
                ? $"Berkas regulasi untuk {namaKabupaten}."
                : string.IsNullOrWhiteSpace(namaKabupaten)
                    ? "Kabupaten belum dipilih. File regulasi tidak tersedia."
                    : $"File regulasi untuk {namaKabupaten} tidak tersedia.";

            foreach (var dokumen in dokumenRegulasi)
            {
                dokumen.PeriksaKeberadaan();
            }

            var kelompok = Kelompok.ToList();
            kelompok[0] = new KelompokDokumentasi(
                string.IsNullOrWhiteSpace(namaKabupaten)
                    ? "Dasar regulasi"
                    : $"Dasar regulasi — {namaKabupaten}",
                keterangan,
                dokumenRegulasi);

            bool referensiKarawang = RegulasiKarawangCocok(namaKabupaten, jenisKabupaten);
            bool referensiBekasi = RegulasiBekasiCocok(namaKabupaten, jenisKabupaten);
            bool referensiLokalTersedia = referensiKarawang || referensiBekasi;
            string keteranganReferensi = referensiKarawang
                ? "Referensi lokal tersedia untuk Karawang. Cakupan format dan batas penerapannya dijelaskan di dalam dokumen."
                : referensiBekasi
                    ? "Contoh awalan 470 tersedia untuk beberapa surat keterangan di Perbup Bekasi. Pola lengkap dan kebijakan deret untuk jenis lain belum ditetapkan oleh bukti yang diperiksa."
                : string.IsNullOrWhiteSpace(namaKabupaten)
                    ? "Referensi penomoran lokal belum tersedia karena kabupaten/kota belum dipilih."
                    : $"Referensi pola penomoran yang terverifikasi untuk {namaKabupaten} belum tersedia. Acuan regulasi yang ada belum cukup untuk memastikan format bawaan aplikasi.";
            var panduanReferensi = kelompok[2];
            var dokumenReferensi = panduanReferensi.Dokumen
                .Where(d => d.JalurRelatif is not "referensi-penomoran-surat.md"
                    and not "referensi-penomoran-surat-bekasi.md")
                .ToList();
            if (referensiLokalTersedia)
            {
                var judulReferensi = referensiKarawang
                    ? "Referensi penomoran surat — Kabupaten Karawang"
                    : "Referensi penomoran surat — Kabupaten Bekasi";
                var keteranganDokumenReferensi = referensiKarawang
                    ? "Contoh format 470 dalam lampiran Perbup, batas cakupan bukti, dan kebijakan deret yang diterapkan aplikasi."
                    : "Contoh resmi nomor 470 pada empat surat keterangan serta bagian pola yang belum ditetapkan lampiran.";
                var berkasReferensi = referensiKarawang
                    ? "referensi-penomoran-surat.md"
                    : "referensi-penomoran-surat-bekasi.md";
                var itemReferensi = new DokumentasiItem(
                    judulReferensi,
                    keteranganDokumenReferensi,
                    berkasReferensi,
                    pdf: false);
                itemReferensi.PeriksaKeberadaan();
                dokumenReferensi.Insert(0, itemReferensi);
            }
            kelompok[2] = new KelompokDokumentasi(
                panduanReferensi.Nama,
                keteranganReferensi,
                dokumenReferensi);

            Kelompok = kelompok;
            JumlahTersedia = Kelompok.SelectMany(k => k.Dokumen).Count(d => d.AdaBerkas);
            OnPropertyChanged(nameof(Kelompok));
            OnPropertyChanged(nameof(JumlahTotal));
            OnPropertyChanged(nameof(JumlahTautan));
            OnPropertyChanged(nameof(RingkasanText));
        }

        private static bool RegulasiKarawangCocok(string kabupaten, string? jenisKabupaten)
        {
            if (string.Equals(jenisKabupaten, "Kota", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            const string awalan = "Kabupaten ";
            var nama = kabupaten.StartsWith(awalan, StringComparison.OrdinalIgnoreCase)
                ? kabupaten[awalan.Length..].Trim()
                : kabupaten;
            return string.Equals(nama, "Karawang", StringComparison.OrdinalIgnoreCase);
        }

        private static bool RegulasiBekasiCocok(string kabupaten, string? jenisKabupaten)
        {
            if (string.Equals(jenisKabupaten, "Kota", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            const string awalan = "Kabupaten ";
            var nama = kabupaten.StartsWith(awalan, StringComparison.OrdinalIgnoreCase)
                ? kabupaten[awalan.Length..].Trim()
                : kabupaten;
            return string.Equals(nama, "Bekasi", StringComparison.OrdinalIgnoreCase);
        }

        public string Title => "DOKUMENTASI";

        public string Subtitle => "Dokumen bawaan aplikasi — dasar regulasi, panduan, dan kebijakan";

        /// <summary>
        /// Identitas aplikasi pada statusbar halaman; disusun otomatis dari atribut
        /// assembly sehingga selalu mengikuti versi terbaru tanpa perlu disunting.
        /// </summary>
        public string VersionFooter => IdentitasAplikasi.FooterVersi;

        public IReadOnlyList<KelompokDokumentasi> Kelompok { get; private set; }

        private int _jumlahTersedia;

        /// <summary>Jumlah dokumen yang berkasnya benar-benar ada di folder aplikasi.</summary>
        public int JumlahTersedia
        {
            get => _jumlahTersedia;
            private set => SetProperty(ref _jumlahTersedia, value);
        }

        /// <summary>Jumlah dokumen yang tersedia sebagai tautan resmi daring.</summary>
        public int JumlahTautan => Kelompok.Sum(k => k.Dokumen.Count(d => d.HasUrl));

        /// <summary>Jumlah seluruh dokumen yang seharusnya ikut terpasang.</summary>
        public int JumlahTotal => Kelompok.Sum(k => k.Dokumen.Count);

        /// <summary>
        /// Ringkasan ketersediaan dokumen. Dokumen daring (tautan resmi) selalu
        /// tersedia, jadi hanya berkas bawaan yang bisa "belum ditemukan".
        /// </summary>
        public string RingkasanText
        {
            get
            {
                int berkas = JumlahTotal - JumlahTautan;
                string bagianBerkas = JumlahTersedia == berkas
                    ? $"{berkas} berkas tersedia"
                    : $"{JumlahTersedia} dari {berkas} berkas tersedia";

                return JumlahTautan > 0
                    ? $"{bagianBerkas} · {JumlahTautan} tautan resmi"
                    : bagianBerkas;
            }
        }

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
        /// Buka tautan resmi (situs pemerintah) di peramban bawaan. Dipakai untuk
        /// dokumen yang hanya tersedia daring — tautannya diklik langsung dari
        /// kartu pada daftar.
        /// </summary>
        public RelayCommand<DokumentasiItem> BukaTautanCommand { get; }

        /// <summary>
        /// Buka folder dokumen di File Explorer. Bukan cara membaca dokumen —
        /// dokumen selalu dibaca di dalam aplikasi — melainkan untuk keperluan
        /// teknisi: menyalin berkas atau memastikan dokumen ikut terpasang.
        /// </summary>
        public RelayCommand BukaFolderCommand { get; }

        /// <summary>
        /// Tampilkan dokumen di dalam aplikasi: PDF pada pratinjau SudesPdf, berkas
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

        /// <summary>
        /// Buka tautan resmi di peramban bawaan. Tautan government (dataset,
        /// JDIH) memang hidup di internet, jadi di sinilah peramban dipakai —
        /// dokumen yang ikut terpasang tetap dibaca di dalam aplikasi.
        /// </summary>
        private void BukaTautan(DokumentasiItem? dokumen)
        {
            if (dokumen is null || !dokumen.HasUrl)
            {
                return;
            }

            var galat = TautanWeb.Buka(dokumen.Url);
            if (galat is null)
            {
                PesanStatus = $"Tautan resmi dibuka di peramban: {dokumen.HostTampil}.";
                _logger.LogInformation("Tautan resmi dibuka: {Url}", dokumen.Url);
                return;
            }

            PesanStatus = "Tautan tidak bisa dibuka: " + galat;
            _logger.LogWarning("Gagal membuka tautan resmi {Url}: {Galat}", dokumen.Url, galat);
        }
    }
}
