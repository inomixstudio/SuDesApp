using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;

namespace SuDesApp.Services
{
    /// <summary>
    /// Satu orang yang dipilih untuk lampiran SK, beserta peran dan unitnya di
    /// dalam lampiran tersebut. Peran/unit boleh kosong: peran yang kosong jatuh
    /// ke jabatan orangnya, unit yang kosong berarti baris tanpa kelompok.
    /// </summary>
    public sealed class LampiranSumber
    {
        public PerangkatDesa Orang { get; init; } = new();

        public string Peran { get; init; } = string.Empty;

        public string Unit { get; init; } = string.Empty;
    }

    /// <summary>
    /// Menyusun baris lampiran SK: siapa orangnya (dari data perangkat desa atau
    /// dari NIK warga), dan data pribadi yang diminta lampiran (pekerjaan, agama,
    /// golongan darah, status perkawinan, pendidikan, alamat) diambil dari tabel
    /// Warga lewat NIK.
    ///
    /// Seluruh NIK dibaca dengan satu query, bukan satu query per orang, sebab satu
    /// lampiran SK bisa memuat puluhan nama (mis. 30 kader Posyandu).
    /// </summary>
    public class SkPerangkatLampiranService
    {
        private readonly IWargaRepository _wargaRepository;
        private readonly ILogger<SkPerangkatLampiranService> _logger;

        public SkPerangkatLampiranService(
            IWargaRepository wargaRepository,
            ILogger<SkPerangkatLampiranService>? logger = null)
        {
            _wargaRepository = wargaRepository ?? throw new ArgumentNullException(nameof(wargaRepository));
            _logger = logger ?? NullLogger<SkPerangkatLampiranService>.Instance;
        }

        /// <summary>
        /// Susun baris lampiran untuk sejumlah orang, urut seperti urutan sumbernya.
        /// Orang yang NIK-nya tidak ada di data warga tetap dicetak — hanya kolom
        /// pribadinya yang kosong — supaya satu NIK yang salah ketik tidak
        /// menggagalkan seluruh SK.
        /// </summary>
        public async Task<IReadOnlyList<BarisLampiranSk>> SusunAsync(
            IReadOnlyList<LampiranSumber> sumber,
            CancellationToken ct = default)
        {
            if (sumber == null) throw new ArgumentNullException(nameof(sumber));

            var daftar = sumber.Where(s => s?.Orang != null).ToList();
            if (daftar.Count == 0)
            {
                return Array.Empty<BarisLampiranSk>();
            }

            var peta = await AmbilPetaWargaAsync(
                daftar.Select(s => s.Orang.NIK), ct).ConfigureAwait(false);

            var hasil = new List<BarisLampiranSk>(daftar.Count);
            foreach (var s in daftar)
            {
                var orang = s.Orang;
                string nik = (orang.NIK ?? string.Empty).Trim();
                peta.TryGetValue(nik, out var warga);

                if (warga == null && nik.Length > 0)
                {
                    _logger.LogDebug(
                        "Lampiran SK: NIK {NIK} ({Nama}) tidak ada di data warga; kolom pribadinya dikosongkan.",
                        nik, orang.Nama);
                }

                hasil.Add(Bangun(warga, orang, s.Peran, s.Unit));
            }

            return hasil;
        }

        /// <summary>
        /// Susun satu baris lampiran dari NIK warga saja — jalur "tambah nama"
        /// untuk orang yang bukan perangkat desa (mis. kader yang belum tercatat
        /// di Data Perangkat Desa). Null bila NIK tidak ada di data warga.
        /// </summary>
        public async Task<BarisLampiranSk?> SusunSatuAsync(
            string? nik,
            string? peran = null,
            string? unit = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(nik))
            {
                return null;
            }

            string kunci = nik.Trim();
            try
            {
                var warga = await _wargaRepository.GetWargaByNikAsync(kunci).ConfigureAwait(false);
                if (warga == null)
                {
                    return null;
                }

                return Bangun(warga, null, peran, unit);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membaca warga NIK {NIK} untuk lampiran SK", kunci);
                return null;
            }
        }

        private async Task<Dictionary<string, WargaData>> AmbilPetaWargaAsync(
            IEnumerable<string?> daftarNik, CancellationToken ct)
        {
            var nik = daftarNik
                .Select(n => (n ?? string.Empty).Trim())
                .Where(n => n.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (nik.Count == 0)
            {
                return new Dictionary<string, WargaData>(StringComparer.Ordinal);
            }

            var peta = new Dictionary<string, WargaData>(StringComparer.Ordinal);
            try
            {
                var warga = await _wargaRepository.AmbilDaftarByNikAsync(nik, ct).ConfigureAwait(false);
                foreach (var w in warga)
                {
                    string kunci = (w.NIK ?? string.Empty).Trim();
                    if (kunci.Length > 0)
                    {
                        peta[kunci] = w;
                    }
                }
            }
            catch (Exception ex)
            {
                // Lampiran tetap harus bisa dicetak walau data warga gagal dibaca:
                // nama dari data perangkat desa sudah cukup untuk sebuah SK.
                _logger.LogError(ex, "Gagal membaca {Jumlah} warga untuk lampiran SK", nik.Count);
            }

            return peta;
        }

        private static BarisLampiranSk Bangun(
            WargaData? warga, PerangkatDesa? orang, string? peran, string? unit)
        {
            string jabatan = orang?.JabatanTampil ?? string.Empty;

            return new BarisLampiranSk
            {
                Nama = Pilih(warga?.Nama, orang?.Nama),
                NIK = Pilih(warga?.NIK, orang?.NIK),
                TempatTanggalLahir = TempatTanggalLahir(warga, orang),
                Peran = Pilih(peran, jabatan),
                Pendidikan = Pilih(warga?.Pendidikan, orang?.Pendidikan),
                Pekerjaan = Pilih(warga?.Pekerjaan, null),
                Agama = Pilih(warga?.Agama, null),
                GolonganDarah = Pilih(warga?.GolonganDarah, null),
                StatusPerkawinan = Pilih(warga?.StatusPerkawinan, null),
                Alamat = Alamat(warga, orang),
                NomorHP = Pilih(warga?.NomorHP, orang?.NomorHP),
                Unit = (unit ?? string.Empty).Trim()
            };
        }

        /// <summary>
        /// "Tempat, tanggal lahir": data warga sudah menyimpan tanggalnya sebagai
        /// teks siap tampil (dd-MM-yyyy); data perangkat desa menyimpannya sebagai
        /// tanggal, sehingga diformat di sini agar bentuknya sama.
        /// </summary>
        private static string TempatTanggalLahir(WargaData? warga, PerangkatDesa? orang)
        {
            string tempat = Pilih(warga?.TempatLahir, orang?.TempatLahir);
            string tanggal = warga != null && !string.IsNullOrWhiteSpace(warga.TanggalLahir)
                ? warga.TanggalLahir!.Trim()
                : orang?.TanggalLahir?.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture) ?? string.Empty;

            if (tempat.Length == 0)
            {
                return tanggal;
            }

            return tanggal.Length == 0 ? tempat : $"{tempat}, {tanggal}";
        }

        private static string Alamat(WargaData? warga, PerangkatDesa? orang)
        {
            string alamat = Pilih(warga?.AlamatLengkap, orang?.Alamat);
            if (alamat.Length > 0)
            {
                return alamat;
            }

            string wilayah = orang?.WilayahRingkas ?? string.Empty;
            return wilayah == "-" ? string.Empty : wilayah;
        }

        /// <summary>Nilai pertama yang terisi; keduanya kosong berarti string kosong.</summary>
        private static string Pilih(string? utama, string? cadangan)
        {
            if (!string.IsNullOrWhiteSpace(utama)) return utama!.Trim();
            return string.IsNullOrWhiteSpace(cadangan) ? string.Empty : cadangan!.Trim();
        }
    }
}
