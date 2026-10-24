using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using SuDesApp.GeneratorPdf;
using System.ComponentModel.DataAnnotations;

namespace SuDesApp.Services
{
    /// <summary>Hasil penyimpanan satu paket NTCR: surat yang tersimpan + berkas PDF gabungannya.</summary>
    public sealed class NtcrPaketHasil
    {
        public NtcrPaketHasil(IReadOnlyList<SuratData> surat, string berkasPdf)
        {
            Surat = surat ?? Array.Empty<SuratData>();
            BerkasPdf = berkasPdf ?? string.Empty;
        }

        /// <summary>Surat yang tersimpan, berurutan sesuai blanko (N1 → N6).</summary>
        public IReadOnlyList<SuratData> Surat { get; }

        /// <summary>Jalur berkas PDF gabungan siap cetak (satu blanko per halaman).</summary>
        public string BerkasPdf { get; }

        public int JumlahBlanko => Surat.Count;

        /// <summary>Daftar kode blanko yang dibuat, mis. "N1, N2, N3".</summary>
        public string DaftarBlanko => string.Join(", ", Surat.Select(s => NtcrKatalog.Kode(s.NamaJenis)));

        /// <summary>Nomor surat yang diberikan sistem untuk tiap blanko.</summary>
        public string DaftarNomor => string.Join(", ", Surat.Select(s => s.NomorSurat));
    }

    /// <summary>
    /// Alur "paket pernikahan": sekali isi data satu pasangan, seluruh blanko NTCR
    /// terpilih (N1–N6) disimpan sekaligus ke register lalu dicetak menjadi SATU
    /// berkas PDF gabungan — satu blanko per halaman, urut N1 → N6, siap cetak dan
    /// diserahkan ke KUA.
    /// </summary>
    public sealed class NtcrPaketService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly AppConfig _appConfig;
        private readonly NtcrGenerator _generator;
        private readonly SuratSaveService _simpanService;
        private readonly ILogger<NtcrPaketService> _logger;

        public NtcrPaketService(
            IUnitOfWork unitOfWork,
            AppConfig appConfig,
            NtcrGenerator generator,
            SuratSaveService simpanService,
            ILogger<NtcrPaketService> logger)
        {
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _appConfig = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
            _generator = generator ?? throw new ArgumentNullException(nameof(generator));
            _simpanService = simpanService ?? throw new ArgumentNullException(nameof(simpanService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Blanko NTCR yang dapat disertakan dalam satu paket: blanko Kepdirjen N1–N6.
        /// Surat numpang nikah (N8) tidak ikut karena berbentuk surat keterangan desa
        /// tersendiri, bukan bagian blanko persyaratan KUA.
        /// </summary>
        public static IReadOnlyList<NtcrBlanko> BlankoTersedia => NtcrKatalog.UntukPaket;

        /// <summary>
        /// Simpan seluruh blanko terpilih dari satu data pasangan, lalu cetak satu
        /// berkas PDF gabungan.
        /// </summary>
        /// <param name="master">
        /// Data pasangan yang sudah diisi pada form (warga + NtcrData). Objek ini
        /// disalin untuk tiap blanko, jadi isian form tidak ikut berubah.
        /// </param>
        /// <param name="jenisTerpilih">Nama jenis blanko yang akan dibuat, mis. NTCR_N1.</param>
        public async Task<NtcrPaketHasil> SimpanDanCetakAsync(
            SuratData master,
            IEnumerable<string> jenisTerpilih,
            CancellationToken cancellationToken = default)
        {
            if (master == null) throw new ArgumentNullException(nameof(master));

            var jenis = NtcrKatalog.Urutkan(jenisTerpilih);
            if (jenis.Count == 0)
                throw new ValidationException("Pilih minimal satu blanko yang akan dibuat pada paket.");

            if (master.Warga == null || string.IsNullOrWhiteSpace(master.Warga.Nama))
                throw new ValidationException("Data calon pengantin (pemohon) belum lengkap.");

            if (master.Ntcr == null)
                throw new ValidationException("Data pasangan (calon istri dan orang tua) belum diisi.");

            if (master.TanggalSurat == default)
                master.TanggalSurat = DateTime.Now;

            // Data desa dipakai kop seluruh blanko; kosongkan hanya bila benar-benar
            // tidak tersedia di pengaturan.
            if (master.Desa == null || string.IsNullOrWhiteSpace(master.Desa.NamaDesa))
                master.Desa = await _unitOfWork.DesaRepository.GetInfoDesaAsync() ?? master.Desa;

            // ATOMIK: seluruh blanko tersimpan dalam SATU transaksi — jika satu
            // blanko gagal (nomor dobel, data rusak, dsb.), tidak ada blanko parsial
            // yang tertinggal di register. Penyimpanan per blanko lewat
            // SuratSaveService (pemilik alur simpan di Core).
            var tersimpan = await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                var daftar = new List<SuratData>(jenis.Count);
                foreach (var namaJenis in jenis)
                {
                    var surat = BuatSalinan(master, namaJenis);
                    int id = await _simpanService.SimpanDalamTransaksiAsync(
                        surat, _unitOfWork.CurrentTransaction!, cancellationToken);
                    surat.ID_Surat = id;
                    daftar.Add(surat);
                }
                return daftar;
            });

            string berkasPdf = await TulisBerkasAsync(tersimpan, master.Keterangan, cancellationToken);

            _logger.LogInformation("Paket NTCR {Blanko} tersimpan (nomor: {Nomor}); berkas {Berkas}.",
                string.Join(", ", tersimpan.Select(s => NtcrKatalog.Kode(s.NamaJenis))),
                string.Join(", ", tersimpan.Select(s => s.NomorSurat)),
                berkasPdf);

            return new NtcrPaketHasil(tersimpan, berkasPdf);
        }

        /// <summary>Buat berkas PDF gabungan dari surat yang sudah tersimpan.</summary>
        private async Task<string> TulisBerkasAsync(
            IReadOnlyList<SuratData> tersimpan,
            string? keterangan,
            CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(_appConfig.TempPdfFolder);

            string namaPemohon = BersihkanNamaBerkas(tersimpan.FirstOrDefault()?.Warga?.Nama);
            string berkas = Path.Combine(_appConfig.TempPdfFolder,
                $"NTCR_PAKET_{namaPemohon}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");

            using (var aliran = File.Create(berkas))
            {
                await _generator.GeneratePaketPdfAsync(aliran, tersimpan, keterangan, cancellationToken);
            }

            return berkas;
        }

        /// <summary>
        /// Salinan data pasangan untuk satu blanko: identitas pemohon, calon istri,
        /// orang tua, dan data desa sama; yang berbeda hanya jenis, keperluan, dan
        /// nomor surat (dibuat sistem saat penyimpanan).
        /// </summary>
        private static SuratData BuatSalinan(SuratData master, string namaJenis)
        {
            var salinan = new SuratData
            {
                NamaJenis = namaJenis,
                NomorSurat = string.Empty,          // dibuat repository dari format jenis surat
                TanggalSurat = master.TanggalSurat,
                Keterangan = master.Keterangan,
                Keperluan = NtcrKatalog.Keperluan(namaJenis),
                KodeJenis = master.KodeJenis,
                Status = "Active",
                Desa = master.Desa,
                Warga = master.Warga,
                PejabatPenandatangan = master.PejabatPenandatangan,
                NamaPejabatPenandatangan = master.NamaPejabatPenandatangan
            };

            salinan.Ntcr = SalinNtcr(master.Ntcr!);
            return salinan;
        }

        /// <summary>Salinan dalam (deep copy) data NTCR termasuk identitas orang tua/wali.</summary>
        private static NtcrData SalinNtcr(NtcrData sumber)
        {
            sumber ??= new NtcrData();

            return new NtcrData
            {
                ID_CalonIstri = 0,
                NikIstri = sumber.NikIstri,
                NamaIstri = sumber.NamaIstri,
                TempatLahirIstri = sumber.TempatLahirIstri,
                TanggalLahirIstri = sumber.TanggalLahirIstri,
                AgamaIstri = sumber.AgamaIstri,
                PekerjaanIstri = sumber.PekerjaanIstri,
                AlamatIstri = sumber.AlamatIstri,
                StatusPerkawinanIstri = sumber.StatusPerkawinanIstri,
                KewarganegaraanIstri = sumber.KewarganegaraanIstri,
                PihakDiterangkanN1 = sumber.PihakDiterangkanN1,
                TujuanKua = sumber.TujuanKua,
                HariTanggalJamAkad = sumber.HariTanggalJamAkad,
                TempatAkad = sumber.TempatAkad,
                TanggalPenetapanIsbat = sumber.TanggalPenetapanIsbat,
                PengadilanAgama = sumber.PengadilanAgama,
                LampiranTambahan = sumber.LampiranTambahan,
                TanggalDiterima = sumber.TanggalDiterima,
                PihakAnakIzinOrtu = sumber.PihakAnakIzinOrtu,
                PihakMeninggal = sumber.PihakMeninggal,
                TanggalMeninggal = sumber.TanggalMeninggal,
                TempatMeninggal = sumber.TempatMeninggal,
                DesaNumpang = sumber.DesaNumpang,
                KecamatanNumpang = sumber.KecamatanNumpang,
                KabupatenNumpang = sumber.KabupatenNumpang,
                KecamatanIstri = sumber.KecamatanIstri,
                KabupatenIstri = sumber.KabupatenIstri,
                KeteranganTemuan = sumber.KeteranganTemuan,
                TujuanSurat = sumber.TujuanSurat,
                AyahCalonSuami = sumber.AyahCalonSuami?.Clone() ?? new NtcrOrangTua(),
                IbuCalonSuami = sumber.IbuCalonSuami?.Clone() ?? new NtcrOrangTua(),
                AyahCalonIstri = sumber.AyahCalonIstri?.Clone() ?? new NtcrOrangTua(),
                IbuCalonIstri = sumber.IbuCalonIstri?.Clone() ?? new NtcrOrangTua()
            };
        }

        /// <summary>Nama pemohon disederhanakan agar aman dipakai sebagai nama berkas.</summary>
        private static string BersihkanNamaBerkas(string? nama)
        {
            if (string.IsNullOrWhiteSpace(nama)) return "PAKET";

            var bersih = new string(nama.Trim().ToUpperInvariant()
                .Where(c => char.IsLetterOrDigit(c) || c == ' ')
                .ToArray());

            bersih = string.Join("_", bersih.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            return string.IsNullOrWhiteSpace(bersih) ? "PAKET" : bersih;
        }
    }
}
