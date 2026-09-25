using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Services;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Baris Register NTCR: kolom surat umum ditambah data pasangan calon
    /// pengantin yang hanya relevan untuk blanko NTCR (N1–N8).
    /// </summary>
    public class NtcrDisplayModel : SuratDisplayModel
    {
        /// <summary>Nama calon istri (pihak kedua) atau "-".</summary>
        public string CalonIstriDisplay { get; set; } = "-";

        /// <summary>Alamat/asal calon istri atau "-".</summary>
        public string AlamatIstriDisplay { get; set; } = "-";

        /// <summary>
        /// Tujuan surat: KUA/PPN tujuan (N2/N3), Pengadilan Agama (isbat), atau
        /// desa/kecamatan numpang nikah (N8). "-" bila blanko tidak bertujuan.
        /// </summary>
        public string TujuanDisplay { get; set; } = "-";
    }

    /// <summary>
    /// Register NTCR: buku register khusus blanko persyaratan pernikahan desa
    /// (N1–N8). Terpisah dari <see cref="RegisterSuratViewModel"/> — punya judul,
    /// daftar filter, kolom, dan nama berkas sendiri, sehingga register surat
    /// desa umum tidak perlu lagi memuat kolom calon mempelai.
    ///
    /// Logika bersama (pencarian, filter, paging, statistik, aksi surat) tetap
    /// dipakai lewat kelas dasar; yang berbeda cukup di-override di sini.
    /// </summary>
    public class RegisterNtcrViewModel : RegisterSuratViewModel
    {
        private const string GroupNtcrFilterValue = "GROUP_NTCR";
        private const string DisplayNameSemuaNtcr = "Semua Blanko NTCR";

        public RegisterNtcrViewModel(
            IUnitOfWork unitOfWork,
            ILoggerFactory loggerFactory,
            SettingsManager settingsManager,
            AppConfig appConfig,
            FileService fileService,
            IMessageService messageService,
            IServiceProvider serviceProvider,
            NavigationService navigation,
            Func<string, string, int?, PdfPreviewViewModel> previewFactory,
            PdfPrintService pdfPrintService,
            IPeringatanDataDesaContoh? peringatan = null)
            : base(
                unitOfWork,
                loggerFactory,
                settingsManager,
                appConfig,
                fileService,
                messageService,
                serviceProvider,
                navigation,
                previewFactory,
                pdfPrintService,
                peringatan)
        {
        }

        public override string PageTitle => "Register NTCR";

        protected override string RegisterFileBaseName => "RegisterNtcr";

        /// <summary>Register ini memang berisi NTCR, jadi tidak ada jenis yang disembunyikan.</summary>
        protected override IReadOnlyList<string>? ExcludedJenisNames => null;

        protected override SuratDisplayModel NewDisplayModel() => new NtcrDisplayModel();

        /// <summary>
        /// Pilihan jenis surat: satu grup "Semua Blanko NTCR" (menampilkan N1–N8
        /// sekaligus) atau satu blanko tertentu. Label blanko diambil dari
        /// <see cref="NtcrKatalog"/> supaya sama dengan menu dan hasil cetak.
        /// </summary>
        protected override async Task<List<FilterItem>> BuildJenisFilterItemsAsync()
        {
            var items = new List<FilterItem>
            {
                new FilterItem { DisplayName = DisplayNameSemuaNtcr, FilterValue = GroupNtcrFilterValue }
            };

            var displayNames = await _unitOfWork.JenisSuratRepository.GetJenisSuratDisplayNamesAsync();

            foreach (var blanko in NtcrKatalog.Semua)
            {
                // Hanya blanko yang benar-benar terdaftar di database yang ditawarkan.
                if (!displayNames.Keys.Any(k => string.Equals(k, blanko.NamaJenis, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                items.Add(new FilterItem
                {
                    DisplayName = blanko.Judul,
                    FilterValue = blanko.NamaJenis.ToUpperInvariant()
                });
            }

            return items;
        }

        protected override void FillDisplayModelExtras(SuratDisplayModel model, SuratData surat)
        {
            if (model is not NtcrDisplayModel row)
            {
                return;
            }

            var ntcr = surat.Ntcr;
            row.CalonIstriDisplay = string.IsNullOrWhiteSpace(ntcr?.NamaIstri) ? "-" : ntcr!.NamaIstri;
            row.AlamatIstriDisplay = BuildAlamatIstri(ntcr);
            row.TujuanDisplay = BuildTujuan(ntcr);
        }

        protected override object CreateRegisterForReturn() =>
            _serviceProvider.CreateScope().ServiceProvider.GetRequiredService<RegisterNtcrViewModel>();

        private static string BuildAlamatIstri(NtcrData? ntcr)
        {
            if (ntcr == null) return "-";

            var parts = new[] { ntcr.AlamatIstri, ntcr.KecamatanIstri, ntcr.KabupatenIstri }
                .Where(p => !string.IsNullOrWhiteSpace(p));

            return parts.Any() ? string.Join(", ", parts!) : "-";
        }

        private static string BuildTujuan(NtcrData? ntcr)
        {
            if (ntcr == null) return "-";

            // N8: desa/kelurahan tempat akan melangsungkan akad nikah (numpang nikah).
            var numpang = new[] { ntcr.DesaNumpang, ntcr.KecamatanNumpang, ntcr.KabupatenNumpang }
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();
            if (numpang.Any())
            {
                return string.Join(", ", numpang);
            }

            // N2/N3: KUA/PPN tujuan surat.
            if (!string.IsNullOrWhiteSpace(ntcr.TujuanKua)) return ntcr.TujuanKua!;

            // N3: Pengadilan Agama yang menetapkan isbat.
            if (!string.IsNullOrWhiteSpace(ntcr.PengadilanAgama)) return ntcr.PengadilanAgama!;

            return "-";
        }
    }
}
