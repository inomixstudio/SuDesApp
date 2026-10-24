using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;

namespace SuDesApp.GeneratorPdf
{
    /// <summary>
    /// Surat Keterangan Belum Terbit / Sudah Terbit.
    ///
    /// Bentuk, susunan tabel, dan blok tanda tangan SAMA PERSIS dengan SKTM
    /// (diwarisi dari <see cref="SKTMGenerator"/>): yang berbeda hanya judul
    /// surat dan bunyi katerangannya. Perbedaannya khususnya penting —
    /// SKTM menyebut kondisi ekonomi warga, padahal permintaan pemerintah kini
    /// memapar kondisi administratif ("belum terbit"/"sudah terbit") sebagai
    /// pengganti, mis. "tidak mampu" atau "belum menikah".
    ///
    /// Varian dipilih operator di form dan disimpan pada
    /// <see cref="SuratData.Keperluan"/> — kolom yang memang berisi tujuan
    /// surat, jadi tidak ada kolom baru di database.
    /// </summary>
    public class KeteranganTerbitGenerator : SKTMGenerator
    {
        /// <summary>Nilai "nilai dapat" yang diketik pengguna di form.</summary>
        public const string VarianBelumTerbit = "BELUM TERBIT";
        public const string VarianSudahTerbit = "SUDAH TERBIT";

        private string _varian = VarianBelumTerbit;

        public KeteranganTerbitGenerator(
            AppConfig config,
            FileService fileService,
            IDesaRepository desaRepository,
            ISuratRepository suratRepository,
            ControlSurat.SettingsManager settingsManager,
            ILogger<SKTMGenerator> logger,
            ILoggerFactory loggerFactory)
            : base(config, fileService, desaRepository, suratRepository, settingsManager, logger, loggerFactory)
        {
        }

        /// <summary>Judul mengikuti varian yang dipilih operator.</summary>
        protected override string JudulSurat => _varian switch
        {
            VarianSudahTerbit => "SURAT KETERANGAN SUDAH TERBIT",
            _ => "SURAT KETERANGAN BELUM TERBIT"
        };

        /// <summary>
        /// Baca varian dari <see cref="SuratData.Keperluan"/> lebih dulu, lalu
        /// jalur dasar yang sama. <see cref="SuratData.Keperluan"/> juga dicetak
        /// sebagai baris "dipakai untuk ..." oleh SuratGeneratorBase.
        /// </summary>
        public override async Task GeneratePdfAsync(
            Stream outputStream, SuratData suratData, string? keteranganTextBox = null)
        {
            ArgumentNullException.ThrowIfNull(suratData);
            _varian = KembalikanVarian(suratData.Keperluan);
            await base.GeneratePdfAsync(outputStream, suratData, keteranganTextBox);
        }

        /// <summary>
        /// Tentukan varian dari isian form: menerima "BELUM TERBIT",
        /// "SUDAH TERBIT", atau "&lt;varian&gt; - perincian" (seperti ditulis form).
        /// </summary>
        public static string KembalikanVarian(string? keperluan)
        {
            var teks = (keperluan ?? string.Empty).Trim().ToUpperInvariant();
            if (teks.StartsWith(VarianSudahTerbit, StringComparison.Ordinal)) return VarianSudahTerbit;
            if (teks.StartsWith(VarianBelumTerbit, StringComparison.Ordinal)) return VarianBelumTerbit;

            // Tidak mengenali varian: pakai "Belum Terbit", bentuk yang paling
            // sering diminta, dan jangan buat surat dengan judul kosong.
            return VarianBelumTerbit;
        }
    }
}
