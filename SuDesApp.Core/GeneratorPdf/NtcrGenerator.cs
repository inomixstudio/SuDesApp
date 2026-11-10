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
// Seluruh tata letak dirender langsung dengan API SudesPdf.Layout (tanpa lapisan kompat).
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;
using SudesPdf.Core.Layout;
using System.Globalization;

namespace SuDesApp.GeneratorPdf
{
    /// <summary>
    /// Generator formulir NTCR (blanko Model N1–N6) dan surat keterangan numpang nikah
    /// (N8) untuk persyaratan pendaftaran pernikahan.
    /// </summary>
    public partial class NtcrGenerator : SuratGeneratorBase
    {
        /// <summary>Isian titik-titik blanko ketika data belum diisi.</summary>
        private const string Titik = ".......................................................";

        private const float LebarLabelKepala = 205f;

        protected override string JudulSurat => NtcrKatalog.NamaFormulir(SuratConstants.NTCR_N1);

        /// <summary>Jarak baris tiap sel tabel data blanko mengikuti kerapatan surat yang aktif.</summary>
        private static float LeadingData => KerapatanSurat.Aktif.LeadingSelTabel;

        /// <summary>Lebar kolom label (%) pada tabel data — cukup agar label panjang tidak terpotong.</summary>
        private const float LebarLabelSel = 42f;

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

        /// <summary>
        /// Cetak satu paket blanko sekaligus: setiap blanko pada daftar menempati satu
        /// halaman di dalam SATU berkas PDF, sehingga berkas hasilnya siap cetak/hantar
        /// ke KUA tanpa menyatukan PDF secara manual. Dipakai alur "sekali isi data satu
        /// pasangan" (lihat NtcrPaketService).
        /// </summary>
        /// <remarks>
        /// Mesin SudesPdf merender secara sinkron; metode ini mengembalikan Task agar
        /// seragam dengan sebutan generator lain saat dipanggil dari alur async.
        /// </remarks>
        public virtual Task GeneratePaketPdfAsync(
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
            foreach (var kerapatan in KerapatanSurat.Bertingkat)
            {
                using var penanda = KerapatanSurat.Pakai(kerapatan);
                using var penampung = new MemoryStream();

                Document.Create(container =>
                {
                    for (int i = 0; i < halamanBlanko.Count; i++)
                    {
                        var suratData = halamanBlanko[i];
                        container.Page(page =>
                        {
                            SiapkanHalaman(page, lebarHalaman, tinggiHalaman);

                            page.Content().Column(kolom =>
                            {
                                var halaman = new BadanSurat();
                                ComposeHalaman(halaman, suratData, keteranganTextBox!);
                                halaman.Render(kolom);
                            });
                        });
                    }
                }).GeneratePdf(penampung, out jumlahHalaman);

                ukuranPdf = penampung.ToArray();

                if (jumlahHalaman <= halamanBlanko.Count)
                {
                    if (!ReferenceEquals(kerapatan, KerapatanSurat.Normal))
                    {
                        _logger.LogDebug("Paket NTCR dirapatkan ke kerapatan {Kerapatan} agar tepat {Halaman} halaman.",
                            kerapatan.Nama, halamanBlanko.Count);
                    }

                    break;
                }

                _logger.LogDebug("Paket NTCR memakai kerapatan {Kerapatan} masih {Jumlah} halaman untuk {Blanko} blanko; mencoba kerapatan berikutnya.",
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

                _logger.LogDebug("Blanko NTCR {NamaJenis} selesai dibuat.", jenis);
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

    }
}
