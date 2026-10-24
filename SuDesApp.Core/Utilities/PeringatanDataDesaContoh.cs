using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;

namespace SuDesApp.Utilities
{
    /// <summary>
    /// Keadaan data desa terhadap nilai contoh bawaan aplikasi, beserta pesan
    /// peringatan siap tampil.
    /// </summary>
    public sealed class KeadaanDataDesaContoh
    {
        /// <summary>Data desa sudah diisi pengguna — surat aman dicetak/disimpan.</summary>
        public static KeadaanDataDesaContoh Bersih { get; } = new();

        /// <summary>Nama kolom wajib yang isinya masih contoh/kosong.</summary>
        public IReadOnlyList<string> Field { get; init; } = Array.Empty<string>();

        /// <summary>Benar bila masih ada kolom wajib yang memakai data contoh.</summary>
        public bool MasihContoh => Field.Count > 0;

        /// <summary>Ringkasan kolom, mis. "Nama Desa, Alamat Desa, dan 1 kolom lain".</summary>
        public string RingkasField => DesaContoh.RingkasField(Field);

        /// <summary>
        /// Pesan peringatan untuk sebuah kegiatan ("mencetak surat", "menyimpan surat"):
        /// menyebut kolom yang masih contoh sekaligus jalan keluarnya.
        /// </summary>
        public string Pesan(string kegiatan) =>
            $"{kegiatan}\n\n" +
            $"Data desa berikut masih memakai contoh bawaan aplikasi: {RingkasField}. " +
            "Kop surat dan blok tanda tangan akan memakai data contoh itu. " +
            "Buka Pengaturan Surat → bagian Data Desa untuk menggantinya dengan data desa yang sebenarnya.";
    }

    /// <summary>
    /// Penjaga data desa contoh: dipakai sebelum mencetak atau menyimpan surat.
    /// Selama data desa masih bawaan aplikasi, surat resmi tidak boleh keluar
    /// memakai nama desa/pejabat contoh tanpa sepengetahuan pengguna.
    /// </summary>
    public interface IPeringatanDataDesaContoh
    {
        /// <summary>Periksa data desa yang berlaku sekarang (aman bila gagal dibaca).</summary>
        Task<KeadaanDataDesaContoh> PeriksaAsync(CancellationToken ct = default);

        /// <summary>
        /// Benar bila kegiatan boleh dilanjutkan. Bila data desa masih contoh,
        /// pengguna diberi peringatan lebih dulu dan boleh memilih berhenti
        /// (mengisi data desa dulu) atau tetap melanjutkan.
        /// </summary>
        /// <param name="kegiatan">
        /// Kalimat yang menjelaskan apa yang akan dilakukan, mis.
        /// "Surat ini akan disimpan dan dicetak."
        /// </param>
        /// <param name="message">Layanan pesan untuk menanyakan keputusan pengguna.</param>
        Task<bool> BolehLanjutAsync(string kegiatan, IMessageService message, CancellationToken ct = default);
    }

    /// <summary>
    /// Implementasi penjaga data desa contoh: membaca data desa dari
    /// <see cref="SettingsManager"/> (cache database yang sudah dibersihkan setiap
    /// kali pengaturan disimpan) lalu memakai <see cref="DesaContoh"/> untuk menilai.
    ///
    /// Kegagalan membaca data desa sengaja TIDAK menghalangi pekerjaan: bila datanya
    /// tidak terbaca, penjaga melaporkan keadaan bersih dan hanya mencatat log —
    /// lebih baik pengguna tetap bisa bekerja daripada tertahan karena galat baca.
    /// </summary>
    public sealed class PeringatanDataDesaContoh : IPeringatanDataDesaContoh
    {
        /// <summary>Judul dialog peringatan (dipakai juga oleh uji & halaman lain).</summary>
        public const string JudulPeringatan = "Data desa masih contoh";

        private readonly SettingsManager _settings;
        private readonly ILogger<PeringatanDataDesaContoh> _logger;

        public PeringatanDataDesaContoh(
            SettingsManager settings,
            ILogger<PeringatanDataDesaContoh> logger)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<KeadaanDataDesaContoh> PeriksaAsync(CancellationToken ct = default)
        {
            try
            {
                var desa = await _settings.GetSettingsAsync();
                var field = DesaContoh.FieldContoh(desa);
                if (field.Count == 0)
                {
                    return KeadaanDataDesaContoh.Bersih;
                }

                return new KeadaanDataDesaContoh { Field = field };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Data desa tidak terbaca; peringatan data contoh dilewati.");
                return KeadaanDataDesaContoh.Bersih;
            }
        }

        public async Task<bool> BolehLanjutAsync(
            string kegiatan,
            IMessageService message,
            CancellationToken ct = default)
        {
            var keadaan = await PeriksaAsync(ct);
            if (!keadaan.MasihContoh)
            {
                return true;
            }

            if (message == null)
            {
                // Tanpa layanan pesan tidak ada cara bertanya — biarkan berjalan.
                _logger.LogWarning(
                    "Data desa masih contoh ({Field}) tetapi tidak ada layanan pesan untuk memperingatkan.",
                    keadaan.RingkasField);
                return true;
            }

            _logger.LogWarning(
                "Peringatan data desa contoh ditampilkan sebelum kegiatan berikut: {Kegiatan} ({Field})",
                kegiatan.Replace('\n', ' '), keadaan.RingkasField);

            return await message.ShowConfirmationAsync(
                JudulPeringatan,
                keadaan.Pesan(kegiatan) + "\n\nTetap lanjutkan sekarang?");
        }
    }
}
