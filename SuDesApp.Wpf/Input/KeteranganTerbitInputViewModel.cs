using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using SuDesApp.GeneratorPdf;
using SuDesApp.Utilities;

namespace SuDesApp.Wpf.Input
{
    /// <summary>
    /// Form "Surat Keterangan Belum Terbit / Sudah Terbit".
    ///
    /// Isian, tata letak, dan peraturannya sama dengan SKTM (diwarisi dari
    /// <see cref="SktmInputViewModel"/>) — bedanya operator memilih satu dari dua
    /// varian, dan baris keterangan (yang membahas kondisi administratif) diisi
    /// berbeda. Varian tersimpan pada <see cref="SuratData.Keperluan"/> supaya
    /// tidak perlu kolom baru di database.
    /// </summary>
    public class KeteranganTerbitInputViewModel : SktmInputViewModel
    {
        private string _jenisKeterangan = KeteranganTerbitGenerator.VarianBelumTerbit;

        public KeteranganTerbitInputViewModel(
            ILogger<SktmInputViewModel> logger,
            AppConfig appConfig,
            IUnitOfWork unitOfWork,
            IMessageService messageService)
            : base(logger, appConfig, unitOfWork, messageService)
        {
        }

        /// <summary>Kode jenis surat — dipakai penomoran dan register.</summary>
        public override string NamaJenis => SuratConstants.SK_BELUM_TERBIT;

        /// <summary>Dua varian yang didukung.</summary>
        public IReadOnlyList<string> JenisKeteranganOptions { get; } = new[]
        {
            KeteranganTerbitGenerator.VarianBelumTerbit,
            KeteranganTerbitGenerator.VarianSudahTerbit
        };

        /// <summary>
        /// Varian terpilih. Mengubahnya langsung menyusun ulang baris keterangan
        /// bawaan supaya operator tidak mengopas teks dari surat lain.
        /// </summary>
        public string JenisKeterangan
        {
            get => _jenisKeterangan;
            set
            {
                var baru = JenisKeteranganOptions.FirstOrDefault(o => string.Equals(o, value, StringComparison.OrdinalIgnoreCase))
                           ?? KeteranganTerbitGenerator.VarianBelumTerbit;
                if (!SetProperty(ref _jenisKeterangan, baru)) return;

                // Keterangan lama (dibuat untuk varian lain) jangan dipertahankan:
                // kalau tertinggal, surat terbit menyebut variian yang salah.
                Keterangan = KeteranganBawaan();
            }
        }

        protected override async Task InitializeSpecificAsync()
        {
            if (string.IsNullOrWhiteSpace(Keterangan))
            {
                _jenisKeterangan = KeteranganTerbitGenerator.VarianBelumTerbit;
                OnPropertyChanged(nameof(JenisKeterangan));
                OnPropertyChanged(nameof(JenisKeteranganOptions));
                Keterangan = KeteranganBawaan();
            }

            await Task.CompletedTask;
        }

        protected override async Task CollectSpecificDataAsync(Data.Models.SuratData? suratData)
        {
            if (suratData == null) return;

            suratData.NamaJenis = NamaJenis;
            suratData.PejabatPenandatangan = IsKadesSelected ? "Kepala Desa" : "Sekretaris Desa";

            // Varian disimpan pada Keperluan. Keterangan tetap berisi paragraf isi
            // surat — keduanya perlu: varian menentukan JUDUL surat, keterangan
            // menentukan badan suratnya.
            suratData.Keperluan = _jenisKeterangan;
            suratData.Keterangan = string.IsNullOrWhiteSpace(Keterangan)
                ? KeteranganBawaan()
                : Keterangan.Trim();

            if (suratData.Warga != null)
            {
                suratData.Warga.NamaJenis = NamaJenis;
                suratData.Warga.IsForInstansi = false;
                suratData.Warga.IsForKematian = false;
            }

            await Task.CompletedTask;
        }

        protected override void ValidateSpecificFields(List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(Keterangan))
            {
                errors.Add("Keterangan harus diisi");
            }
        }

        /// <summary>
        /// Baris keterangan bawaan untuk varian: menyebut kondisi administrasi
        /// ("belum terbit"/"sudah terbit"), BUKAN kondisi ekonomi warga — itulah
        /// perbedaan mendasar dengan SKTM.
        /// </summary>
        private string KeteranganBawaan()
        {
            var wilayah = WilayahRingkas();

            var badan = _jenisKeterangan == KeteranganTerbitGenerator.VarianSudahTerbit
                ? $"Bahwa nama tersebut di atas adalah warga {wilayah} dan berdasarkan keterangan yang " +
                  "bersangkutan serta data yang ada pada kami, hal tersebut SUDAH TERBIT."
                : $"Bahwa nama tersebut di atas adalah warga {wilayah} dan berdasarkan keterangan yang " +
                  "bersangkutan serta data yang ada pada kami, hal tersebut BELUM TERBIT.";

            return badan;
        }

        private string WilayahRingkas()
        {
            var desa = Desa?.Trim();
            var kec = Kecamatan?.Trim();
            var kab = Kabupaten?.Trim();
            if (string.IsNullOrEmpty(desa) && string.IsNullOrEmpty(kec) && string.IsNullOrEmpty(kab))
                return "Desa -";

            return $"Desa {desa ?? "-"} Kecamatan {kec ?? "-"} Kabupaten {kab ?? "-"}";
        }
    }
}
