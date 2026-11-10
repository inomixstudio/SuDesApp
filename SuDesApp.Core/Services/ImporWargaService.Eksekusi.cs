using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SudesExPlus;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;

namespace SuDesApp.Services
{
    public partial class ImporWargaService : IImporWargaService
    {
        // ------------------------------------------------------------------
        // Tahap 4: simpan
        // ------------------------------------------------------------------

        public async Task<int> JalankanAsync(
            HasilImporWarga hasil,
            IProgress<int>? progres = null,
            CancellationToken ct = default)
        {
            if (hasil is null) throw new ArgumentNullException(nameof(hasil));

            // Penegakan izin lapisan data: mengimpor (menulis) data warga massal
            // menuntut izin KelolaWarga. Pratinjau (ValidasiAsync) sengaja bebas —
            // hanya membaca berkas dan menghitung, tanpa menyentuh database.
            SessionContext.Wajib(IzinAplikasi.KelolaWarga);

            var siap = hasil.Baris
                .Where(b => !b.AdaKesalahan && b.Data != null && b.Tindakan != TindakanImpor.Lewati)
                .ToList();

            int tersimpan = 0;
            for (int i = 0; i < siap.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    await _warga.AddOrUpdateWargaAsync(siap[i].Data!).ConfigureAwait(false);
                    siap[i].Tindakan = TindakanImpor.Simpan;
                    tersimpan++;
                }
                catch (Exception ex)
                {
                    // Satu baris gagal tidak boleh menghentikan sisa impor.
                    _logger.LogError(ex, "Gagal mengimpor warga dari baris {Baris}.",
                        siap[i].NomorBaris);
                    siap[i].Tindakan = TindakanImpor.Lewati;
                    siap[i].Kesalahan.Add("Gagal disimpan: " + ex.Message);
                }

                progres?.Report(i + 1);
            }

            return tersimpan;
        }

        // ------------------------------------------------------------------
        // Normalisasi
        // ------------------------------------------------------------------

        private static string? Kosongkan(string? nilai) =>
            string.IsNullOrWhiteSpace(nilai) ? null : nilai.Trim();

        internal static string? NormalisasiJenisKelamin(string? nilai) =>
            (nilai ?? string.Empty).Trim().ToUpperInvariant() switch
            {
                "L" or "LAKI-LAKI" or "LAKI LAKI" or "PRIA" or "M" => "Laki-laki",
                "P" or "PEREMPUAN" or "WANITA" or "F" => "Perempuan",
                _ => null
            };

        /// <summary>
        /// Golongan darah disimpan sebagai kode singkat (kolomnya cuma 5
        /// karakter), jadi "golongan darah A" diterjemahkan menjadi "A".
        /// </summary>
        internal static string? NormalisasiGolonganDarah(string? nilai)
        {
            string t = (nilai ?? string.Empty).Trim().ToUpperInvariant();
            if (t.Length == 0) return null;

            // Buang kata pengantar seperti "Golongan Darah A" atau "O (Rhesus -)".
            string[] kata = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string kandidat = kata.Length > 0 ? kata[^1] : t;
            if (kandidat.StartsWith("(R", StringComparison.Ordinal)) kandidat = t;

            return kandidat is "A" or "B" or "AB" or "O"
                   or "A+" or "B+" or "AB+" or "O+"
                ? kandidat
                : null;
        }

        private static readonly string[] FormatTanggal =
        {
            "dd/MM/yyyy", "dd-MM-yyyy", "dd.MM.yyyy", "yyyy-MM-dd",
            "yyyy/MM/dd", "dd/MM/yy"
        };

        /// <summary>
        /// Terima penulisan tanggal yang lazim dipakai orang: dd/MM/yyyy dari
        /// Excel Indonesia, yyyy-MM-dd dari sistem, dan sebagainya. Tanggal yang
        /// tak terbaca ditolak, bukan ditebak — usia dan rekapitulasi
        /// kependudukan akan salah bila tanggal keliru.
        /// </summary>
        internal static bool CobaParseTanggal(string? teks, out DateTime? tanggal)
        {
            tanggal = null;
            string t = (teks ?? string.Empty).Trim();
            if (t.Length == 0) return false;

            if (DateTime.TryParseExact(t, FormatTanggal, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTime persis))
            {
                tanggal = persis;
                return true;
            }

            if (DateTime.TryParse(t, CultureInfo.GetCultureInfo("id-ID"),
                    DateTimeStyles.None, out DateTime culture))
            {
                tanggal = culture;
                return true;
            }

            return false;
        }

    }
}
