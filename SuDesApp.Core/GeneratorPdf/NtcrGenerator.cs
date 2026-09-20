// NtcrGenerator.cs
// Blanko formulir NTCR (Model N1–N6) mengikuti Keputusan Direktur Jenderal
// Bimbingan Masyarakat Islam Nomor 473 Tahun 2020 tentang Petunjuk Teknis
// Pelaksanaan Pencatatan Pernikahan:
//   N1 Surat Pengantar Nikah            N2 Permohonan Kehendak Nikah
//   N3 Permohonan Pencatatan Isbat      N4 Persetujuan Calon Pengantin
//   N5 Surat Izin Orang Tua             N6 Surat Ket. Kematian Suami/Istri
//   N8 Surat Keterangan Numpang Nikah (numpang kawin — surat desa, bukan blanko Kepdirjen)
// Tata letak tiap blanko berbeda dari surat keterangan desa: N1 dan N6 memakai
// kepala "KANTOR DESA/KELURAHAN" dan ditandatangani Kepala Desa/Lurah; N2 dan N3
// adalah surat permohonan kepada KUA yang ditandatangani pemohon; N4 ditandatangani
// kedua calon pengantin; N5 oleh ayah dan ibu/wali.
// Seluruh tata letak dirender langsung dengan API QuestPDF (tanpa lapisan kompat).
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using System.Globalization;

namespace SuDesApp.GeneratorPdf
{
    /// <summary>
    /// Generator formulir NTCR (blanko Model N1–N6) dan surat keterangan numpang nikah
    /// (N8) untuk persyaratan pendaftaran pernikahan.
    /// </summary>
    public class NtcrGenerator : SuratGeneratorBase
    {
        /// <summary>Isian titik-titik blanko ketika data belum diisi.</summary>
        private const string Titik = ".......................................................";

        private const float LebarLabelKepala = 205f;

        /// <summary>Jarak baris tiap sel tabel data blanko mengikuti kerapatan surat yang aktif.</summary>
        private static float LeadingData => KerapatanSurat.Aktif.LeadingSelTabel;

        /// <summary>Lebar kolom label (%) pada tabel data — cukup agar label panjang tidak terpotong.</summary>
        private const float LebarLabelSel = 42f;

        private string _currentJudul = NtcrKatalog.NamaFormulir(SuratConstants.NTCR_N1);
        private static readonly CultureInfo Budaya = new("id-ID");

        public NtcrGenerator(
            AppConfig config,
            FileService fileService,
            IDesaRepository desaRepository,
            ISuratRepository suratRepository,
            SettingsManager settingsManager,
            ILogger<NtcrGenerator> logger,
            ILoggerFactory loggerFactory)
            : base(config, fileService, desaRepository, suratRepository, settingsManager, logger, loggerFactory)
        {
        }

        protected override string JudulSurat => _currentJudul;

        /// <summary>
        /// Blanko resmi NTCR berupa satu lembar (kertas F4/A4). Bila isi melimpah —
        /// umumnya di A4 yang lebih pendek — kerapatan surat dirapatkan otomatis
        /// (lihat KerapatanSurat) agar seluruh blanko tetap satu halaman.
        /// </summary>
        protected override int HalamanMaksimal => 1;

        /// <summary>Kepala surat, judul, dan tanda tangan diatur per blanko (bukan pola surat desa).</summary>
        protected override bool UseDefaultHeader => false;
        protected override bool UseDefaultFooter => false;
        protected override bool ShowPemohonInFooter => false;

        public override async Task GeneratePdfAsync(Stream outputStream, SuratData suratData, string? keteranganTextBox = null)
        {
            _currentJudul = NtcrKatalog.NamaFormulir(suratData?.NamaJenis);
            await base.GeneratePdfAsync(outputStream, suratData!, keteranganTextBox);
        }

        /// <summary>
        /// Cetak satu paket blanko sekaligus: setiap blanko pada daftar menempati satu
        /// halaman di dalam SATU berkas PDF, sehingga berkas hasilnya siap cetak/hantar
        /// ke KUA tanpa menyatukan PDF secara manual. Dipakai alur "sekali isi data satu
        /// pasangan" (lihat NtcrPaketService).
        /// </summary>
        /// <remarks>
        /// QuestPDF merender secara sinkron; metode ini mengembalikan Task agar
        /// seragam dengan sebutan generator lain saat dipanggil dari alur async.
        /// </remarks>
        public Task GeneratePaketPdfAsync(
            Stream outputStream,
            IReadOnlyList<SuratData> daftarBlanko,
            string? keteranganTextBox = null,
            CancellationToken cancellationToken = default)
        {
            if (outputStream == null)
                throw new ArgumentNullException(nameof(outputStream));
            if (!outputStream.CanWrite)
                throw new InvalidOperationException("Output stream tidak dapat ditulis.");
            if (daftarBlanko == null || daftarBlanko.Count == 0)
                throw new ArgumentException("Daftar blanko tidak boleh kosong.", nameof(daftarBlanko));

            foreach (var blanko in daftarBlanko)
            {
                if (blanko == null)
                    throw new ArgumentException("Daftar blanko memuat data surat yang kosong.", nameof(daftarBlanko));

                ValidateSuratData(blanko);
            }

            // Urutan halaman mengikuti urutan blanko (N1 → N6) supaya berkas paket
            // siap dilipat/distaples seperti berkas pendaftaran pernikahan.
            var halamanBlanko = daftarBlanko.ToList();
            var (lebarHalaman, tinggiHalaman) = PengaturanCetak.Dimensi();

            byte[]? ukuranPdf = null;
            int jumlahHalaman = 0;

            // Kerapatan berlaku untuk seluruh halaman; dokumen dirender ulang dengan
            // kerapatan lebih rapat sampai jumlah halamannya sama dengan jumlah blanko
            // (satu blanko = satu halaman, tanpa halaman sisa).
            foreach (var kerapatan in KerapatanBertingkat)
            {
                using var penanda = KerapatanSurat.Pakai(kerapatan);
                using var penampung = new MemoryStream();

                Document.Create(container =>
                {
                    for (int i = 0; i < halamanBlanko.Count; i++)
                    {
                        var suratData = halamanBlanko[i];
                        _currentJudul = NtcrKatalog.NamaFormulir(suratData.NamaJenis);

                        container.Page(page =>
                        {
                            SiapkanHalaman(page, lebarHalaman, tinggiHalaman);

                            page.Content().Column(kolom =>
                            {
                                var halaman = new BadanSurat();
                                ComposeHalaman(halaman, suratData, keteranganTextBox!);
                                foreach (var potongan in halaman.Potongan)
                                {
                                    var render = potongan;
                                    kolom.Item().Element(c => render(c));
                                }
                            });
                        });
                    }
                }).GeneratePdf(penampung);

                ukuranPdf = penampung.ToArray();
                jumlahHalaman = HitungJumlahHalaman(ukuranPdf);

                if (jumlahHalaman <= halamanBlanko.Count)
                {
                    if (!ReferenceEquals(kerapatan, KerapatanSurat.Normal))
                    {
                        _logger.LogInformation("Paket NTCR dirapatkan ke kerapatan {Kerapatan} agar tepat {Halaman} halaman.",
                            kerapatan.Nama, halamanBlanko.Count);
                    }

                    break;
                }

                _logger.LogInformation("Paket NTCR memakai kerapatan {Kerapatan} masih {Jumlah} halaman untuk {Blanko} blanko; mencoba kerapatan berikutnya.",
                    kerapatan.Nama, jumlahHalaman, halamanBlanko.Count);
            }

            if (jumlahHalaman > halamanBlanko.Count)
            {
                _logger.LogWarning("Paket NTCR tetap {Jumlah} halaman meski sudah memakai kerapatan paling rapat (idealnya {Ideal}).",
                    jumlahHalaman, halamanBlanko.Count);
            }

            var hasil = ukuranPdf ?? Array.Empty<byte>();
            outputStream.Write(hasil, 0, hasil.Length);

            _logger.LogInformation("Paket NTCR {Jumlah} blanko selesai dibuat ({Halaman} halaman, {Ukuran} byte).",
                halamanBlanko.Count, jumlahHalaman, hasil.Length);

            return Task.CompletedTask;
        }

        protected override void ComposeHalaman(BadanSurat halaman, SuratData suratData, string? keteranganTextBox = null)
        {
            try
            {
                ValidateSuratData(suratData);

                var desa = suratData.Desa!;
                var warga = suratData.Warga!;
                var ntcr = suratData.Ntcr ?? new NtcrData();
                string jenis = suratData.NamaJenis?.ToUpperInvariant() ?? SuratConstants.NTCR_N1;

                // Kepala blanko: blok LAMPIRAN Kepdirjen + label "Model Nx",
                // persis seperti lembar asli tiap formulir. Surat numpang nikah (N8)
                // bukan blanko Kepdirjen sehingga memakai kop surat desa biasa.
                if (NtcrKatalog.PakaiBlokKepdirjen(jenis))
                {
                    ComposeBlokLampiran(halaman, jenis);
                }

                switch (jenis)
                {
                    case SuratConstants.NTCR_N2:
                        ComposeN2(halaman, suratData, desa, warga, ntcr, keteranganTextBox!);
                        break;
                    case SuratConstants.NTCR_N3:
                        ComposeN3(halaman, suratData, desa, warga, ntcr, keteranganTextBox!);
                        break;
                    case SuratConstants.NTCR_N4:
                        ComposeN4(halaman, suratData, desa, warga, ntcr, keteranganTextBox!);
                        break;
                    case SuratConstants.NTCR_N5:
                        ComposeN5(halaman, suratData, desa, warga, ntcr, keteranganTextBox!);
                        break;
                    case SuratConstants.NTCR_N6:
                        ComposeN6(halaman, suratData, desa, warga, ntcr, keteranganTextBox!);
                        break;
                    case SuratConstants.NTCR_N8:
                        ComposeN8(halaman, suratData, desa, warga, ntcr, keteranganTextBox!);
                        break;
                    default:
                        ComposeN1(halaman, suratData, desa, warga, ntcr, keteranganTextBox!);
                        break;
                }

                _logger.LogInformation("Berhasil membuat blanko NTCR ({NamaJenis}) untuk {NIK}", jenis, warga.NIK);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat blanko NTCR");
                throw;
            }
        }

        // =====================================================================
        // Kepala blanko: LAMPIRAN + Model Nx
        // =====================================================================

        /// <summary>
        /// Blok "LAMPIRAN IV / KEPUTUSAN DIREKTUR JENDERAL BIMBINGAN MASYARAKAT ISLAM /
        /// NOMOR 473 TAHUN 2020 / TENTANG / PETUNJUK TEKNIS PELAKSANAAN PENCATATAN
        /// PERNIKAHAN" di atas setiap blanko, dengan label "Model Nx" rata kanan
        /// sebaris dengan baris pertamanya.
        ///
        /// Ukuran fontnya mengikuti kerapatan surat yang sedang aktif: bila isi blanko
        /// terlalu panjang, blok ini ikut mengecil (lihat KerapatanSurat) sehingga
        /// seluruh blanko tetap muat satu halaman.
        /// </summary>
        private static void ComposeBlokLampiran(BadanSurat badan, string jenis)
        {
            float ukuran = KerapatanSurat.Aktif.UkuranFontLampiran;
            string lampiran = NtcrKatalog.Lampiran(jenis);
            string kode = NtcrKatalog.Kode(jenis);

            var baris = new List<string>();
            if (!string.IsNullOrWhiteSpace(lampiran))
            {
                baris.Add($"LAMPIRAN {lampiran}");
            }

            baris.Add("KEPUTUSAN DIREKTUR JENDERAL BIMBINGAN MASYARAKAT ISLAM");
            baris.Add("NOMOR 473 TAHUN 2020");
            baris.Add("TENTANG");
            baris.Add("PETUNJUK TEKNIS PELAKSANAAN PENCATATAN PERNIKAHAN");

            badan.Blok(container => container.PaddingBottom(SuratRenderer.JarakBlok(14f)).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(70);
                    cols.RelativeColumn(30);
                });

                table.Cell().Column(kolom =>
                {
                    foreach (var teks in baris)
                    {
                        kolom.Item().Element(c => SuratRenderer.Teks(c, teks, ukuran: ukuran));
                    }
                });

                table.Cell().Element(c => SuratRenderer.Teks(c,
                    $"Model {(string.IsNullOrEmpty(kode) ? "N1" : kode)}", ukuran: ukuran, rata: Rata.Kanan));
            }));
        }

        // =====================================================================
        // N1 — SURAT PENGANTAR NIKAH (diterbitkan & ditandatangani desa)
        // =====================================================================
        private void ComposeN1(BadanSurat badan, SuratData suratData, DesaData desa, WargaData warga, NtcrData ntcr, string keteranganTextBox)
        {
            bool istriDiterangkan = string.Equals(ntcr.PihakDiterangkanN1, "Istri", StringComparison.OrdinalIgnoreCase);
            var (ayah, ibu) = istriDiterangkan
                ? (ntcr.AyahCalonIstri, ntcr.IbuCalonIstri)
                : (ntcr.AyahCalonSuami, ntcr.IbuCalonSuami);

            ComposeKepalaDesa(badan, desa);
            badan.Blok(c => SuratRenderer.JudulDanNomor(c, "FORMULIR PENGANTAR NIKAH", HitungNomorSurat(suratData)));

            badan.Paragraf("Yang bertanda tangan di bawah ini menjelaskan dengan sesungguhnya bahwa:", jarakBawah: 8);

            var baris = new List<(string Label, string? Nilai)>
            {
                ("1. Nama", NamaFormatter.ToUpperNama(istriDiterangkan ? ntcr.NamaIstri : warga.Nama)),
                ("2. Nomor Induk Kependudukan (NIK)", Isi(istriDiterangkan ? ntcr.NikIstri : warga.NIK)),
                ("3. Jenis Kelamin", Isi(istriDiterangkan ? "Perempuan" : (string.IsNullOrWhiteSpace(warga.JenisKelamin) ? "Laki-laki" : warga.JenisKelamin))),
                ("4. Tempat dan tanggal lahir", TempatTanggalLahir(
                    istriDiterangkan ? ntcr.TempatLahirIstri : warga.TempatLahir,
                    istriDiterangkan ? ntcr.TanggalLahirIstri : warga.TanggalLahir)),
                ("5. Kewarganegaraan", Isi(istriDiterangkan ? ntcr.KewarganegaraanIstri ?? "WNI" : warga.Kewarganegaraan ?? "WNI")),
                ("6. Agama", Isi(istriDiterangkan ? ntcr.AgamaIstri : warga.Agama)),
                ("7. Pekerjaan", Isi(istriDiterangkan ? ntcr.PekerjaanIstri : warga.Pekerjaan)),
                ("8. Alamat", AlamatCalon(istriDiterangkan, ntcr, warga))
            };
            TabelBaris(badan, baris);

            // 9. Status pernikahan — blanko mencetak kedua baris (laki-laki & perempuan);
            // baris yang tidak sesuai diberi tanda "-" agar blanko tetap utuh.
            string statusSuami = StatusSebutan(false, istriDiterangkan ? null : warga.StatusPerkawinan);
            string statusIstri = StatusSebutan(true, istriDiterangkan ? ntcr.StatusPerkawinanIstri : null);
            badan.Paragraf("9. Status pernikahan :", indentKiri: 20, jarakAtas: 6);
            TabelBaris(badan, new List<(string, string?)>
            {
                ("a. Laki-laki (Jejaka/Duda)", statusSuami),
                ("b. Perempuan (Perawan/Janda)", statusIstri)
            }, indentKiri: 40);

            badan.Paragraf("Adalah benar anak dari pernikahan seorang pria :", jarakAtas: 8);
            TabelBaris(badan, BarisOrangTua(ayah!, pakaiBin: false));

            badan.Paragraf("dengan seorang wanita :", jarakAtas: 8);
            TabelBaris(badan, BarisOrangTua(ibu!, pakaiBin: false));

            AddKeteranganBebas(badan, suratData, keteranganTextBox);

            badan.Paragraf("Demikian, Surat pengantar ini dibuat dengan mengingat sumpah jabatan dan untuk dipergunakan sebagaimana mestinya.",
                rata: Rata.Justify, indentKiri: 20, jarakAtas: 12);

            TandaTanganDesa(badan, suratData, desa);
        }

        // =====================================================================
        // N2 — PERMOHONAN KEHENDAK NIKAH (diajukan pemohon kepada KUA/PPN LN)
        // =====================================================================
        private void ComposeN2(BadanSurat badan, SuratData suratData, DesaData desa, WargaData warga, NtcrData ntcr, string keteranganTextBox)
        {
            ComposeJudulPermohonan(badan, suratData, desa, "FORMULIR PERMOHONAN KEHENDAK NIKAH", "Permohonan kehendak nikah");
            ComposeAlamatTujuan(badan, "Kepala KUA Kecamatan /PPN LN", ntcr.TujuanKua);

            badan.Paragraf("Dengan hormat, kami mengajukan permohonan kehendak nikah untuk atas nama", jarakAtas: 10, jarakBawah: 4);

            TabelBaris(badan, new List<(string, string?)>
            {
                ("Calon suami", NamaFormatter.ToUpperNama(warga.Nama)),
                ("Calon istri", NamaFormatter.ToUpperNama(ntcr.NamaIstri)),
                ("Hari/Tanggal/Jam", Isi(ntcr.HariTanggalJamAkad)),
                ("Tempat akad nikah", Isi(ntcr.TempatAkad))
            });

            badan.Paragraf("Bersama ini kami sampaikan surat-surat yang diperlukan untuk diperiksa sebagai berikut:", jarakAtas: 10, jarakBawah: 4);
            badan.DaftarBernomor(NtcrLampiran.UntukN2(), indentKiri: 20);

            AddKeteranganBebas(badan, suratData, keteranganTextBox);

            badan.Paragraf("Demikian permohonan ini kami sampaikan, kiranya dapat diperiksa, dihadiri, dan dicatat sesuai dengan ketentuan peraturan perundang-undangan.",
                rata: Rata.Justify, indentKiri: 20, jarakAtas: 12);

            TandaTanganPemohonKua(badan, suratData, warga, ntcr);
        }

        // =====================================================================
        // N3 — PERMOHONAN PENCATATAN ISBAT
        // =====================================================================
        private void ComposeN3(BadanSurat badan, SuratData suratData, DesaData desa, WargaData warga, NtcrData ntcr, string keteranganTextBox)
        {
            ComposeJudulPermohonan(badan, suratData, desa, "FORMULIR PERMOHONAN PENCATATAN ISBAT", "Permohonan pencatatan isbat");
            ComposeAlamatTujuan(badan, "Kepala KUA Kecamatan /PPN LN", ntcr.TujuanKua);

            badan.Paragraf("Dengan hormat, kami mengajukan permohonan pencatatan isbat untuk atas nama", jarakAtas: 10, jarakBawah: 4);

            // N3 boleh dikosongkan: nilai kosong dicetak sebagai titik-titik seperti
            // template, lalu diisi tangan saat surat dibawa ke KUA/pengadilan.
            TabelBaris(badan, new List<(string, string?)>
            {
                ("Suami", NamaAtauTitik(warga?.Nama)),
                ("Istri", NamaAtauTitik(ntcr.NamaIstri)),
                ("Tanggal penetapan", Isi(TanggalIndonesia(ntcr.TanggalPenetapanIsbat))),
                ("Pengadilan Agama", Isi(ntcr.PengadilanAgama))
            });

            badan.Paragraf("Bersama ini kami sampaikan surat-surat yang diperlukan untuk diperiksa sebagai berikut:", jarakAtas: 10, jarakBawah: 4);
            badan.DaftarBernomor(NtcrLampiran.UntukN3(), indentKiri: 20);

            AddKeteranganBebas(badan, suratData, keteranganTextBox);

            badan.Paragraf("Demikian permohonan ini kami sampaikan, kiranya dapat diperiksa, dihadiri, dan dicatat sesuai dengan ketentuan peraturan perundang-undangan.",
                rata: Rata.Justify, indentKiri: 20, jarakAtas: 12);

            TandaTanganPemohonKua(badan, suratData, warga!, ntcr);
        }

        // =====================================================================
        // N4 — PERSETUJUAN CALON PENGANTIN (ditandatangani kedua calon)
        // =====================================================================
        private void ComposeN4(BadanSurat badan, SuratData suratData, DesaData desa, WargaData warga, NtcrData ntcr, string keteranganTextBox)
        {
            badan.Blok(c => SuratRenderer.JudulTengah(c, "SURAT PERSETUJUAN PENGANTIN"));

            badan.Paragraf("Yang bertanda tangan di bawah ini:", jarakAtas: 15, jarakBawah: 4);

            badan.Paragraf("A. Calon suami:", tebal: true, jarakBawah: 2);
            TabelBaris(badan, BarisIdentitasCalon(false, warga, ntcr));

            badan.Paragraf("B. Calon Istri:", tebal: true, jarakAtas: 10, jarakBawah: 2);
            TabelBaris(badan, BarisIdentitasCalon(true, warga, ntcr));

            AddKeteranganBebas(badan, suratData, keteranganTextBox);

            badan.Paragraf("Menyatakan dengan sesungguhnya bahwa atas dasar suka rela, dengan kesadaran sendiri, tanpa ada paksaan dari siapapun juga, setuju untuk melangsungkan pernikahan.",
                rata: Rata.Justify, indentKiri: 20, jarakAtas: 12);

            badan.Paragraf("Demikian Surat persetujuan ini di buat untuk digunakan seperlunya.",
                rata: Rata.Justify, indentKiri: 20, jarakAtas: 8);

            TandaTanganDuaPihak(
                badan,
                desa,
                suratData,
                ("Calon Suami", NamaFormatter.ToUpperNama(warga.Nama)),
                ("Calon Istri", NamaFormatter.ToUpperNama(ntcr.NamaIstri)));
        }

        // =====================================================================
        // N5 — SURAT IZIN ORANG TUA (ditandatangani ayah & ibu/wali)
        // =====================================================================
        private void ComposeN5(BadanSurat badan, SuratData suratData, DesaData desa, WargaData warga, NtcrData ntcr, string keteranganTextBox)
        {
            bool anakIstri = string.Equals(ntcr.PihakAnakIzinOrtu, "Istri", StringComparison.OrdinalIgnoreCase);
            var (ayah, ibu) = anakIstri
                ? (ntcr.AyahCalonIstri, ntcr.IbuCalonIstri)
                : (ntcr.AyahCalonSuami, ntcr.IbuCalonSuami);

            badan.Blok(c => SuratRenderer.JudulTengah(c, "SURAT IZIN ORANG TUA"));

            badan.Paragraf("Yang bertanda tangan di bawah ini:", jarakAtas: 15, jarakBawah: 4);

            badan.Paragraf("A. Ayah/wali/pengampu:", tebal: true, jarakBawah: 2);
            TabelBaris(badan, BarisOrangTua(ayah!, pakaiBin: true));

            badan.Paragraf("B. Ibu/wali/pengampu:", tebal: true, jarakAtas: 10, jarakBawah: 2);
            TabelBaris(badan, BarisOrangTua(ibu!, pakaiBin: true));

            badan.Paragraf("adalah ayah dan ibu kandung/wali/pengampu dari:", jarakAtas: 12, jarakBawah: 2);
            TabelBaris(badan, BarisIdentitasCalon(anakIstri, warga, ntcr));

            badan.Paragraf("Memberikan izin kepada anak kami untuk melakukan pernikahan dengan:", jarakAtas: 12, jarakBawah: 2);
            TabelBaris(badan, BarisIdentitasCalon(!anakIstri, warga, ntcr));

            AddKeteranganBebas(badan, suratData, keteranganTextBox);

            badan.Paragraf("Demikian Surat izin ini di buat dengan kesadaran tanpa ada paksaan dari siapapun dan untuk digunakan seperlunya.",
                rata: Rata.Justify, indentKiri: 20, jarakAtas: 12);

            TandaTanganDuaPihak(
                badan,
                desa,
                suratData,
                ("Ayah/wali/pengampu", NamaFormatter.ToUpperNama(ayah.Nama)),
                ("Ibu/wali/pengampu", NamaFormatter.ToUpperNama(ibu.Nama)));
        }

        // =====================================================================
        // N6 — SURAT KETERANGAN KEMATIAN SUAMI/ISTRI (diterbitkan desa)
        // =====================================================================
        private void ComposeN6(BadanSurat badan, SuratData suratData, DesaData desa, WargaData warga, NtcrData ntcr, string keteranganTextBox)
        {
            bool suamiMeninggal = !string.Equals(ntcr.PihakMeninggal, "Istri", StringComparison.OrdinalIgnoreCase);

            ComposeKepalaDesa(badan, desa);
            badan.Blok(c => SuratRenderer.JudulDanNomor(c, "SURAT KETERANGAN KEMATIAN SUAMI/ISTRI", HitungNomorSurat(suratData)));

            badan.Paragraf("Yang bertanda tangan di bawah ini menerangkan dengan sesungguhnya bahwa:", jarakBawah: 8);

            var almarhum = new List<(string Label, string? Nilai)>
            {
                ("1. Nama lengkap dan alias", NamaFormatter.ToUpperNama(suamiMeninggal ? warga.Nama : ntcr.NamaIstri)),
                ("2. Bin/Binti", Isi(NamaFormatter.ToUpperNama(suamiMeninggal ? ntcr.AyahCalonSuami.Nama : ntcr.AyahCalonIstri.Nama))),
                ("3. Nomor Induk Kependudukan", Isi(suamiMeninggal ? warga.NIK : ntcr.NikIstri)),
                ("4. Tempat dan tanggal lahir", TempatTanggalLahir(
                    suamiMeninggal ? warga.TempatLahir : ntcr.TempatLahirIstri,
                    suamiMeninggal ? warga.TanggalLahir : ntcr.TanggalLahirIstri)),
                ("5. Kewarganegaraan", Isi(suamiMeninggal ? warga.Kewarganegaraan ?? "WNI" : ntcr.KewarganegaraanIstri ?? "WNI")),
                ("6. Agama", Isi(suamiMeninggal ? warga.Agama : ntcr.AgamaIstri)),
                ("7. Pekerjaan", Isi(suamiMeninggal ? warga.Pekerjaan : ntcr.PekerjaanIstri)),
                ("8. Alamat", AlamatCalon(suamiMeninggal, ntcr, warga))
            };
            TabelBaris(badan, almarhum);

            TabelBaris(badan, new List<(string, string?)>
            {
                ("Telah meninggal dunia pada tanggal", Isi(TanggalIndonesia(ntcr.TanggalMeninggal))),
                ("Di", Isi(ntcr.TempatMeninggal))
            }, jarakAtas: 8);

            badan.Paragraf("Yang bersangkutan adalah suami/istri*) dari:", jarakAtas: 10, jarakBawah: 4);

            var ahli = new List<(string Label, string? Nilai)>
            {
                ("1. Nama lengkap dan alias", NamaFormatter.ToUpperNama(suamiMeninggal ? ntcr.NamaIstri : warga.Nama)),
                ("2. Bin/Binti", Isi(NamaFormatter.ToUpperNama(suamiMeninggal ? ntcr.AyahCalonIstri.Nama : ntcr.AyahCalonSuami.Nama))),
                ("3. Nomor Induk Kependudukan", Isi(suamiMeninggal ? ntcr.NikIstri : warga.NIK)),
                ("4. Tempat dan tanggal lahir", TempatTanggalLahir(
                    suamiMeninggal ? ntcr.TempatLahirIstri : warga.TempatLahir,
                    suamiMeninggal ? ntcr.TanggalLahirIstri : warga.TanggalLahir)),
                ("5. Kewarganegaraan", Isi(suamiMeninggal ? ntcr.KewarganegaraanIstri ?? "WNI" : warga.Kewarganegaraan ?? "WNI")),
                ("6. Agama", Isi(suamiMeninggal ? ntcr.AgamaIstri : warga.Agama)),
                ("7. Pekerjaan", Isi(suamiMeninggal ? ntcr.PekerjaanIstri : warga.Pekerjaan)),
                ("8. Alamat", AlamatCalon(!suamiMeninggal, ntcr, warga))
            };
            TabelBaris(badan, ahli);

            AddKeteranganBebas(badan, suratData, keteranganTextBox);

            badan.Paragraf("Demikian surat keterangan ini dibuat dengan mengingat sumpah jabatan dan untuk digunakan seperlunya.",
                rata: Rata.Justify, indentKiri: 20, jarakAtas: 12);

            badan.Paragraf("*) coret yang tidak perlu", ukuran: 9f, jarakAtas: 6);

            TandaTanganDesa(badan, suratData, desa);
        }

        // =====================================================================
        // N8 — SURAT KETERANGAN NUMPANG NIKAH (numpang kawin, diterbitkan desa)
        //
        // Surat desa biasa (bukan blanko Kepdirjen): memakai kop surat desa yang sama
        // dengan surat keterangan lain, lalu menerangkan bahwa warga berdomisili di
        // desa ini akan melangsungkan akad nikah di wilayah lain.
        // =====================================================================
        private void ComposeN8(BadanSurat badan, SuratData suratData, DesaData desa, WargaData warga, NtcrData ntcr, string keteranganTextBox)
        {
            badan.Blok(c => SuratRenderer.Kop(c, desa, CariLogoPath()));
            badan.Blok(c => SuratRenderer.JudulDanNomor(c, "SURAT KETERANGAN NUMPANG NIKAH", HitungNomorSurat(suratData)));

            badan.Paragraf("Yang bertandatangan di bawah ini :", jarakBawah: 8);
            TabelBaris(badan, new List<(string, string?)>
            {
                ("Nama", NamaPejabatSurat(suratData, desa)),
                ("Jabatan", Isi(JabatanPejabatSurat(suratData, desa)))
            });

            badan.Paragraf("Dengan ini menerangkan bahwa :", jarakAtas: 10, jarakBawah: 8);
            TabelBaris(badan, new List<(string, string?)>
            {
                ("Nama", NamaFormatter.ToUpperNama(warga.Nama)),
                ("Tempat, Tgl. Lahir", TempatTanggalLahir(warga.TempatLahir, warga.TanggalLahir)),
                ("Jenis Kelamin", Isi(string.IsNullOrWhiteSpace(warga.JenisKelamin) ? "Laki-laki" : warga.JenisKelamin)),
                ("Kewarganegaraan", Isi(warga.Kewarganegaraan ?? "WNI")),
                ("Agama", Isi(warga.Agama)),
                ("Pekerjaan", Isi(warga.Pekerjaan)),
                ("Alamat", AlamatCalon(false, ntcr, warga))
            });

            badan.Paragraf(
                $"Yang bersangkutan adalah benar warga Desa {Isi(desa.NamaDesa)} Kecamatan {Isi(desa.Kecamatan)} " +
                $"Kabupaten {Isi(desa.Kabupaten)} dan maksud yang bersangkutan akan numpang nikah di :",
                rata: Rata.Justify, indentKiri: 20, jarakAtas: 10, jarakBawah: 6);

            TabelBaris(badan, new List<(string, string?)>
            {
                ("Desa/Kelurahan", Isi(ntcr.DesaNumpang)),
                ("Kecamatan", Isi(ntcr.KecamatanNumpang)),
                ("Kabupaten/Kota", Isi(ntcr.KabupatenNumpang))
            });

            badan.Paragraf("Dengan seorang perempuan :", jarakAtas: 10, jarakBawah: 6);
            TabelBaris(badan, new List<(string, string?)>
            {
                ("Nama", NamaFormatter.ToUpperNama(ntcr.NamaIstri)),
                ("Tempat, Tgl. Lahir", TempatTanggalLahir(ntcr.TempatLahirIstri, ntcr.TanggalLahirIstri)),
                ("Alamat", Isi(ntcr.AlamatIstri)),
                ("Kecamatan", Isi(ntcr.KecamatanIstri)),
                ("Kabupaten/Kota", Isi(ntcr.KabupatenIstri))
            });

            AddKeteranganBebas(badan, suratData, keteranganTextBox);

            badan.Paragraf("Demikian Surat Keterangan ini kami buat, agar kepada yang berkepentingan menjadi tahu adanya.",
                rata: Rata.Justify, indentKiri: 20, jarakAtas: 12);

            TandaTanganDesa(badan, suratData, desa);
        }

        // =====================================================================
        // Tabel data blanko
        // =====================================================================

        /// <summary>
        /// Tabel "label : nilai" rapat khas blanko (lebar label lebih lega daripada
        /// TabelFormulir surat desa, tanpa jarak antar baris) agar seluruh blanko
        /// NTCR muat di halaman yang seharusnya.
        /// </summary>
        private static void TabelBaris(
            BadanSurat badan,
            IEnumerable<(string Label, string? Nilai)>? baris,
            float indentKiri = 20f,
            float jarakAtas = 4f,
            float jarakBawah = 0f)
        {
            var daftar = baris?.ToList() ?? new List<(string Label, string? Nilai)>();
            if (daftar.Count == 0) return;

            badan.Blok(container =>
            {
                var area = container;
                if (jarakAtas > 0f) area = area.PaddingTop(SuratRenderer.JarakBlok(jarakAtas));
                if (jarakBawah > 0f) area = area.PaddingBottom(SuratRenderer.JarakBlok(jarakBawah));
                if (indentKiri > 0f) area = area.PaddingLeft(indentKiri);

                area.Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn(LebarLabelSel);
                        cols.RelativeColumn(3);
                        cols.RelativeColumn(100f - LebarLabelSel - 3f);
                    });

                    foreach (var (label, nilai) in daftar)
                    {
                        SelData(table.Cell(), label ?? string.Empty);
                        SelData(table.Cell(), ":");
                        SelData(table.Cell(), Isi(nilai));
                    }
                });
            });
        }

        private static void SelData(IContainer cell, string teks)
        {
            float leading = LeadingData;
            float padding = KerapatanSurat.Aktif.PaddingSelTabel;

            cell.PaddingTop(padding).PaddingBottom(padding)
                .Text(teks ?? string.Empty)
                .FontSize(DEFAULT_FONT_SIZE)
                .LineHeight(leading / DEFAULT_FONT_SIZE);
        }

        // =====================================================================
        // Blok kepala surat
        // =====================================================================

        /// <summary>Kepala blanko desa: "KANTOR DESA/KELURAHAN : …" (N1 &amp; N6).</summary>
        private static void ComposeKepalaDesa(BadanSurat badan, DesaData desa)
        {
            badan.Blok(c => c.PaddingBottom(12f).Column(kolom =>
            {
                BarisLabel(kolom, "KANTOR DESA/KELURAHAN", desa.NamaDesa?.ToUpperInvariant() ?? "[NAMA DESA]", 6f);
                BarisLabel(kolom, "KECAMATAN", desa.Kecamatan?.ToUpperInvariant() ?? "[KECAMATAN]", 6f);
                BarisLabel(kolom, "KABUPATEN/KOTA", desa.Kabupaten?.ToUpperInvariant() ?? "[KABUPATEN]", 0f);
            }));
        }

        /// <summary>Judul + perihal + tanggal pada blanko permohonan kepada KUA (N2 &amp; N3).</summary>
        private static void ComposeJudulPermohonan(BadanSurat badan, SuratData suratData, DesaData desa, string judul, string perihal)
        {
            badan.Paragraf(judul, tebal: true, rata: Rata.Tengah, ukuran: TITLE_FONT_SIZE, jarakAtas: 12);

            string tempatTanggal = $"{desa.NamaDesa}, {suratData.TanggalSurat.ToString("dd MMMM yyyy", Budaya)}";

            badan.Blok(c => c.PaddingTop(14).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(60);
                    cols.RelativeColumn(40);
                });

                table.Cell().Column(kolom =>
                {
                    kolom.Item().Element(c2 => SuratRenderer.Teks(c2, $"Nomor : {HitungNomorTampil(suratData)}", ukuran: DEFAULT_FONT_SIZE));
                    kolom.Item().Element(c2 => SuratRenderer.Teks(c2, $"Perihal : {perihal}", ukuran: DEFAULT_FONT_SIZE));
                });

                table.Cell().Element(c2 => SuratRenderer.Teks(c2, tempatTanggal, ukuran: DEFAULT_FONT_SIZE, rata: Rata.Kanan));
            }));
        }

        /// <summary>Blok "Kepada yth, … di …" pada blanko permohonan (N2 &amp; N3).</summary>
        private static void ComposeAlamatTujuan(BadanSurat badan, string pejabat, string tujuan)
        {
            badan.Paragraf("Kepada yth,", jarakAtas: 15);
            badan.Paragraf(pejabat, indentKiri: 20);
            badan.Paragraf($"di {TujuanKua(tujuan)}", indentKiri: 20);
        }

        /// <summary>Nama KUA tujuan: dilengkapi "Kecamatan" bila pengguna belum menulisnya.</summary>
        private static string TujuanKua(string? tujuan)
        {
            if (string.IsNullOrWhiteSpace(tujuan)) return Titik;

            string bersih = tujuan.Trim();
            return bersih.Contains("kecamatan", StringComparison.OrdinalIgnoreCase) ||
                   bersih.StartsWith("KUA", StringComparison.OrdinalIgnoreCase) ||
                   bersih.Contains("PPN", StringComparison.OrdinalIgnoreCase)
                ? bersih
                : $"Kecamatan {bersih}";
        }

        private static void BarisLabel(ColumnDescriptor kolom, string label, string nilai, float jarakBawah)
        {
            kolom.Item().Row(row =>
            {
                row.ConstantItem(LebarLabelKepala).Text($"{label} :");
                row.RelativeItem().Text(nilai);
            });

            if (jarakBawah > 0f)
            {
                kolom.Item().Height(jarakBawah);
            }
        }

        // =====================================================================
        // Blok tanda tangan
        // =====================================================================

        /// <summary>Blok tanda tangan desa (satu kolom, rata kanan) untuk N1, N6, &amp; N8.</summary>
        private void TandaTanganDesa(BadanSurat badan, SuratData suratData, DesaData desa)
        {
            bool sekdes = ApakahSekdes(suratData);
            string jabatan = sekdes
                ? $"A/N Kepala Desa {desa.NamaDesa}\nSekretaris Desa"
                : "Kepala Desa/Lurah";

            string nama = NamaPejabatSurat(suratData, desa);
            string tempatTanggal = $"{desa.NamaDesa}, {suratData.TanggalSurat.ToString("dd MMMM yyyy", Budaya)}";

            TandaTanganKanan(badan, suratData, desa, jabatan.Split('\n'), nama: nama, barisAtas: tempatTanggal);
        }

        /// <summary>Apakah surat ditandatangani Sekretaris Desa (a.n. Kepala Desa).</summary>
        private static bool ApakahSekdes(SuratData suratData) =>
            string.Equals(suratData.PejabatPenandatangan, "Sekretaris Desa", StringComparison.OrdinalIgnoreCase);

        /// <summary>Nama pejabat penandatangan: dari form, atau dari data desa.</summary>
        private static string NamaPejabatSurat(SuratData suratData, DesaData desa)
        {
            bool sekdes = ApakahSekdes(suratData);
            return NamaFormatter.ToUpperNama(!string.IsNullOrWhiteSpace(suratData.NamaPejabatPenandatangan)
                ? suratData.NamaPejabatPenandatangan
                : (sekdes ? desa.SekretarisDesa : desa.KepalaDesa));
        }

        /// <summary>Jabatan pejabat penandatangan yang tercetak di badan surat (N8).</summary>
        private static string JabatanPejabatSurat(SuratData suratData, DesaData desa) =>
            ApakahSekdes(suratData)
                ? $"Sekretaris Desa {desa.NamaDesa}"
                : $"Kepala Desa {desa.NamaDesa}";

        /// <summary>Blok tanda tangan rata kanan (desa/KUA) dengan ruang tanda tangan.</summary>
        private static void TandaTanganKanan(BadanSurat badan, SuratData suratData, DesaData desa, string[] jabatan, string? nama, string? barisAtas = null)
        {
            string namaFinal = string.IsNullOrWhiteSpace(nama)
                ? "..................................."
                : nama;
            string[] baris = barisAtas != null
                ? new[] { barisAtas }.Concat(jabatan).ToArray()
                : jabatan;

            badan.Blok(c => c.PaddingTop(20).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(50);
                    cols.RelativeColumn(50);
                });

                table.Cell().Text(string.Empty);
                table.Cell().Element(sel => sel.Column(kolom =>
                {
                    foreach (var b in baris)
                    {
                        kolom.Item().Element(c2 => SuratRenderer.Teks(c2, b, rata: Rata.Tengah));
                    }

                    kolom.Item().Height(SuratRenderer.RuangTandaTangan);
                    kolom.Item().Element(c2 => SuratRenderer.Teks(c2, namaFinal, tebal: true, rata: Rata.Tengah));
                }));
            }));
        }

        /// <summary>Blok tanda tangan dua kolom setara (N4: calon suami &amp; istri; N5: ayah &amp; ibu).</summary>
        private static void TandaTanganDuaPihak(
            BadanSurat badan,
            DesaData desa,
            SuratData suratData,
            (string Jabatan, string Nama) kiri,
            (string Jabatan, string Nama) kanan)
        {
            string tempatTanggal = $"{desa.NamaDesa}, {suratData.TanggalSurat.ToString("dd MMMM yyyy", Budaya)}";

            badan.Blok(c => c.PaddingTop(10).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(1);
                    cols.RelativeColumn(1);
                });

                // Kiri: jabatan di atas, lalu nama setelah ruang tanda tangan.
                table.Cell().Element(sel => sel.Column(kolom =>
                {
                    kolom.Item().Height(SuratRenderer.TinggiBaris * 2);
                    kolom.Item().Element(c2 => SuratRenderer.Teks(c2, kiri.Jabatan, rata: Rata.Tengah));
                    kolom.Item().Height(SuratRenderer.RuangTandaTangan);
                    kolom.Item().Element(c2 => SuratRenderer.Teks(c2, NamaFormatter.ToUpperNama(kiri.Nama), tebal: true, rata: Rata.Tengah));
                }));

                // Kanan: tempat & tanggal, jabatan, nama.
                table.Cell().Element(sel => sel.Column(kolom =>
                {
                    kolom.Item().Element(c2 => SuratRenderer.Teks(c2, tempatTanggal, rata: Rata.Tengah));
                    kolom.Item().Element(c2 => SuratRenderer.Teks(c2, kanan.Jabatan, rata: Rata.Tengah));
                    kolom.Item().Height(SuratRenderer.RuangTandaTangan);
                    kolom.Item().Element(c2 => SuratRenderer.Teks(c2, NamaFormatter.ToUpperNama(kanan.Nama), tebal: true, rata: Rata.Tengah));
                }));
            }));
        }

        /// <summary>Blok "Diterima Tanggal … / Yang menerima, Kepala KUA" + "Wassalam, Pemohon" (N2 &amp; N3).</summary>
        private void TandaTanganPemohonKua(BadanSurat badan, SuratData suratData, WargaData warga, NtcrData ntcr)
        {
            string diterima = string.IsNullOrWhiteSpace(ntcr.TanggalDiterima)
                ? "Diterima Tanggal ..............."
                : $"Diterima Tanggal {TanggalIndonesia(ntcr.TanggalDiterima)}";

            badan.Blok(c => c.PaddingTop(20).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(1);
                    cols.RelativeColumn(1);
                });

                table.Cell().Element(sel => sel.Column(kolom =>
                {
                    kolom.Item().Element(c2 => SuratRenderer.Teks(c2, diterima, rata: Rata.Tengah));
                    kolom.Item().Element(c2 => SuratRenderer.Teks(c2, "Yang menerima,", rata: Rata.Tengah));
                    kolom.Item().Element(c2 => SuratRenderer.Teks(c2, "Kepala KUA/PPN Luar Negeri", rata: Rata.Tengah));
                    kolom.Item().Height(SuratRenderer.RuangTandaTangan);
                    kolom.Item().Element(c2 => SuratRenderer.Teks(c2, "...................................", rata: Rata.Tengah));
                }));

                table.Cell().Element(sel => sel.Column(kolom =>
                {
                    kolom.Item().Height(SuratRenderer.TinggiBaris * 3);
                    kolom.Item().Element(c2 => SuratRenderer.Teks(c2, "Wassalam,", rata: Rata.Tengah));
                    kolom.Item().Element(c2 => SuratRenderer.Teks(c2, "Pemohon", rata: Rata.Tengah));
                    kolom.Item().Height(SuratRenderer.RuangTandaTangan);
                    kolom.Item().Element(c2 => SuratRenderer.Teks(c2, NamaFormatter.ToUpperNama(warga.Nama), tebal: true, rata: Rata.Tengah));
                }));
            }));
        }

        // =====================================================================
        // Baris identitas
        // =====================================================================

        /// <summary>Identitas calon mempelai ala blanko N4/N5 (nama, bin/binti, NIK, TTL, …).</summary>
        private List<(string Label, string? Nilai)> BarisIdentitasCalon(bool istri, WargaData warga, NtcrData ntcr)
        {
            var orangTuaAyah = istri ? ntcr.AyahCalonIstri : ntcr.AyahCalonSuami;

            return new List<(string, string?)>
            {
                ("1. Nama lengkap dan alias", NamaFormatter.ToUpperNama(istri ? ntcr.NamaIstri : warga.Nama)),
                ($"2. {(istri ? "Binti" : "Bin")}", Isi(NamaFormatter.ToUpperNama(orangTuaAyah?.Nama))),
                ("3. Nomor Induk Kependudukan", Isi(istri ? ntcr.NikIstri : warga.NIK)),
                ("4. Tempat dan tanggal lahir", TempatTanggalLahir(istri ? ntcr.TempatLahirIstri : warga.TempatLahir,
                    istri ? ntcr.TanggalLahirIstri : warga.TanggalLahir)),
                ("5. Kewarganegaraan", Isi(istri ? ntcr.KewarganegaraanIstri ?? "WNI" : warga.Kewarganegaraan ?? "WNI")),
                ("6. Agama", Isi(istri ? ntcr.AgamaIstri : warga.Agama)),
                ("7. Pekerjaan", Isi(istri ? ntcr.PekerjaanIstri : warga.Pekerjaan)),
                ("8. Alamat", AlamatCalon(istri, ntcr, warga))
            };
        }

        /// <summary>Identitas orang tua/wali ala blanko N1 &amp; N5.</summary>
        private List<(string Label, string? Nilai)> BarisOrangTua(NtcrOrangTua orangTua, bool pakaiBin)
        {
            orangTua ??= new NtcrOrangTua();

            var baris = new List<(string, string?)>();
            if (pakaiBin)
            {
                baris.Add(("1. Nama lengkap dan alias", NamaFormatter.ToUpperNama(orangTua.Nama)));
                baris.Add(("2. Bin/Binti", Isi(NamaFormatter.ToUpperNama(orangTua.BinBinti))));
                baris.Add(("3. Nomor Induk Kependudukan", Isi(orangTua.Nik)));
                baris.Add(("4. Tempat dan tanggal lahir", TempatTanggalLahir(orangTua.TempatLahir, orangTua.TanggalLahir)));
                baris.Add(("5. Kewarganegaraan", Isi(orangTua.Kewarganegaraan)));
                baris.Add(("6. Agama", Isi(orangTua.Agama)));
                baris.Add(("7. Pekerjaan", Isi(orangTua.Pekerjaan)));
                baris.Add(("8. Alamat", Isi(orangTua.Alamat)));
            }
            else
            {
                baris.Add(("Nama Lengkap dan alias", NamaFormatter.ToUpperNama(orangTua.Nama)));
                baris.Add(("Nomor Induk Kependudukan (NIK)", Isi(orangTua.Nik)));
                baris.Add(("Tempat dan tanggal lahir", TempatTanggalLahir(orangTua.TempatLahir, orangTua.TanggalLahir)));
                baris.Add(("Kewarganegaraan", Isi(orangTua.Kewarganegaraan)));
                baris.Add(("Agama", Isi(orangTua.Agama)));
                baris.Add(("Pekerjaan", Isi(orangTua.Pekerjaan)));
                baris.Add(("Alamat", Isi(orangTua.Alamat)));
            }

            return baris;
        }


        /// <summary>Keterangan bebas dari form (jika diisi) — dicetak sebagai paragraf tersendiri.</summary>
        private static void AddKeteranganBebas(BadanSurat badan, SuratData suratData, string keteranganTextBox)
        {
            if (!string.IsNullOrWhiteSpace(keteranganTextBox) || !string.IsNullOrWhiteSpace(suratData.Keterangan))
            {
                string keterangan = !string.IsNullOrWhiteSpace(keteranganTextBox)
                    ? keteranganTextBox
                    : suratData.Keterangan ?? string.Empty;

                badan.Paragraf(keterangan, rata: Rata.Justify, indentKiri: 20, jarakAtas: 12);
            }
        }

        // =====================================================================
        // Pembantu umum
        // =====================================================================

        private static string Isi(string? nilai) => string.IsNullOrWhiteSpace(nilai) ? Titik : nilai.Trim();

        /// <summary>Nama untuk blanko yang boleh kosong: titik-titik bila belum diisi.</summary>
        private static string NamaAtauTitik(string? nama) =>
            string.IsNullOrWhiteSpace(nama) ? Titik : NamaFormatter.ToUpperNama(nama);

        /// <summary>
        /// Sebutan status pernikahan menurut blanko N1: laki-laki memakai
        /// Jejaka/Duda, perempuan memakai Perawan/Janda.
        /// </summary>
        private static string StatusSebutan(bool perempuan, string? status)
        {
            if (string.IsNullOrWhiteSpace(status)) return null!;

            string bersih = status.Trim();
            if (bersih.Contains("Belum", StringComparison.OrdinalIgnoreCase)) return perempuan ? "Perawan" : "Jejaka";
            if (bersih.Contains("Cerai", StringComparison.OrdinalIgnoreCase) ||
                bersih.Contains("Janda", StringComparison.OrdinalIgnoreCase) ||
                bersih.Contains("Duda", StringComparison.OrdinalIgnoreCase))
            {
                return perempuan ? "Janda" : "Duda";
            }

            return bersih;
        }

        private static string HitungNomorTampil(SuratData suratData) =>
            string.IsNullOrWhiteSpace(suratData.NomorSurat) ? Titik : suratData.NomorSurat;

        /// <summary>Alamat calon mempelai (pemohon memakai komponen alamat, calon istri teks bebas).</summary>
        private static string AlamatCalon(bool istri, NtcrData ntcr, WargaData warga)
        {
            if (istri)
            {
                return Isi(ntcr.AlamatIstri);
            }

            var bagian = new[]
            {
                warga.Dusun,
                string.IsNullOrWhiteSpace(warga.Desa) ? null : $"Desa {warga.Desa}",
                string.IsNullOrWhiteSpace(warga.Kecamatan) ? null : $"Kecamatan {warga.Kecamatan}",
                string.IsNullOrWhiteSpace(warga.Kabupaten) ? null : $"Kabupaten {warga.Kabupaten}"
            }.Where(b => !string.IsNullOrWhiteSpace(b)).Select(b => b.Trim());

            string alamat = string.Join(", ", bagian);
            return Isi(string.IsNullOrWhiteSpace(alamat) ? null : alamat);
        }

        /// <summary>"Tempat, dd MMMM yyyy" — titik-titik bila belum lengkap.</summary>
        private string TempatTanggalLahir(string? tempat, string? tanggalDb)
        {
            string? tempatBersih = tempat?.Trim();
            string tanggal = FormatTanggalLahir(tanggalDb);

            if (string.IsNullOrWhiteSpace(tempatBersih) && tanggal == null) return Titik;
            if (string.IsNullOrWhiteSpace(tempatBersih)) return Isi(tanggal);
            if (tanggal == null) return Isi(tempatBersih);
            return $"{tempatBersih}, {tanggal}";
        }

        /// <summary>Tanggal DB/UI menjadi "dd MMMM yyyy"; null bila kosong/tidak terbaca.</summary>
        private static string FormatTanggalLahir(string? tanggal)
        {
            if (string.IsNullOrWhiteSpace(tanggal)) return null!;

            if (DateTime.TryParseExact(tanggal.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var db))
                return db.ToString("dd MMMM yyyy", Budaya);

            if (DateTime.TryParseExact(tanggal.Trim(), "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var ui))
                return ui.ToString("dd MMMM yyyy", Budaya);

            return tanggal.Trim();
        }

        private static string TanggalIndonesia(string? tanggal)
        {
            if (string.IsNullOrWhiteSpace(tanggal)) return null!;
            return FormatTanggalLahir(tanggal);
        }
    }
}
