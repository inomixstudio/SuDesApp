using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;
using SuDesApp.WhatsApp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SuDesApp.Api
{
    /// <summary>
    /// Menerima permintaan surat dari luar aplikasi dan menaruhnya di antrean
    /// operator yang sudah ada (tabel PermintaanWa, menu "Layanan Online").
    ///
    /// Kenapa tidak membuat tabel dan layar sendiri? Karena operator sudah
    /// punya alur kerja untuk memeriksa, menyetujui, membuat surat, dan
    /// mengirim tautan unduh. Permintaan dari API masuk ke antrean yang sama,
    /// jadi hanya ada satu tempat untuk memantau semua channel (WhatsApp,
    /// Google Form, API) tanpa membuat operator memeriksa tiga aplikasi berbeda.
    /// </summary>
    public interface IApiPermintaanService
    {
        /// <summary>Buat permintaan baru dari isi API.</summary>
        Task<ApiHasilPermintaan> BuatAsync(ApiPermintaanMasuk masuk, CancellationToken ct = default);

        /// <summary>Status satu permintaan berdasarkan kode; null bila kode tidak dikenal.</summary>
        Task<ApiStatusPermintaan?> AmbilStatusAsync(string kode, CancellationToken ct = default);

        /// <summary>Normalisasi &amp; validasi jenis surat (juga dipakai pengujian).</summary>
        string? TentukanJenisSurat(string? jenisDiberikan, string? pesan);
    }

    public class ApiPermintaanService : IApiPermintaanService
    {
        /// <summary>
        /// Opsi json untuk DataJson yang disimpan di PermintaanWa.
        ///
        /// Sengaja TANPA JsonNamingPolicy dan TANPA PropertyNameCaseInsensitive:
        /// WaSuratProcessor membaca kolom ini dengan
        /// JsonSerializer.Deserialize&lt;WaRequestData&gt; memakai opsi bawaan
        /// (nama properti PascalCase, bandage case), sehingga DataJson yang
        /// ditulis dengan gaya lain tidak akan terbaca operator. Selain itu
        /// PropertyNameCaseInsensitive membuat System.Text.Json menganggap dua
        /// properti yang namanya beda huruf besar saja sebagai nama yang sama,
        /// dan warga yang punya properti warisan "isForInstansi" di samping
        /// "IsForInstansi" akan membuat serialisasi meledak.
        /// </summary>
        private static readonly JsonSerializerOptions OpsiJson = new()
        {
            WriteIndented = false
        };

        private readonly IPermintaanWaRepository _repo;
        private readonly WaEngine? _engine;
        private readonly ActivityLogService? _audit;
        private readonly ILogger<ApiPermintaanService> _logger;
        private readonly Func<DateTime> _sekarang;

        public ApiPermintaanService(
            IPermintaanWaRepository repo,
            WaEngine? engine = null,
            ActivityLogService? audit = null,
            ILogger<ApiPermintaanService>? logger = null,
            Func<DateTime>? sekarang = null)
        {
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
            _engine = engine;
            _audit = audit;
            _logger = logger ?? NullLogger<ApiPermintaanService>.Instance;
            _sekarang = sekarang ?? (() => DateTime.Now);
        }

        public async Task<ApiHasilPermintaan> BuatAsync(ApiPermintaanMasuk masuk, CancellationToken ct = default)
        {
            if (masuk is null)
                return Gagal(ApiKodeGalat.PermintaanTidakSah, "Badan permintaan tidak terbaca.");

            var pesan = Rapi(masuk.Pesan);
            var nama = Rapi(masuk.NamaWarga);
            var nik = NormalisasiNik(masuk.Nik);
            var nomorHp = Rapi(masuk.NomorHp);
            var referensi = Rapi(masuk.Referensi);

            var namaJenis = TentukanJenisSurat(masuk.JenisSurat, pesan);
            if (string.IsNullOrWhiteSpace(namaJenis))
            {
                return Gagal(
                    ApiKodeGalat.PermintaanTidakSah,
                    "Jenis surat tidak dikenali. Isi field jenis_surat dengan salah satu jenis di " +
                    "GET /sudes/api/v1/jenis-surat, atau tulis jenis surat pada baris pertama pesan.");
            }

            // Pengiriman ulang dengan referensi sama tidak boleh jadi permintaan
            // ganda: pemanggil luar sering mencoba ulang karena jawabannya lambat.
            if (referensi != null)
            {
                var ada = await _repo.GetByReferensiAsync(referensi, ct).ConfigureAwait(false);
                if (ada != null)
                {
                    return new ApiHasilPermintaan
                    {
                        Berhasil = true,
                        Kode = ada.KodePermintaan,
                        SudahAda = true,
                        Pesan = "Permintaan dengan referensi ini sudah pernah masuk."
                    };
                }
            }

            // NIK boleh ikut dibaca dari pesan berformat bila tidak dikirim terpisah.
            var parsed = pesan != null ? WaFormatParser.Parse(pesan) : new ParsedWaFormat();
            if (nik == null) nik = NormalisasiNik(parsed.NIK);
            if (nama == null) nama = AmbilField(parsed, "nama");

            var sekarang = _sekarang();
            var permintaan = new PermintaanWa
            {
                KodePermintaan = await _repo
                    .NextKodePermintaanAsync(sekarang.Year, ct).ConfigureAwait(false),
                NomorWA = nomorHp ?? "-",
                NamaWarga = nama,
                NIK = nik,
                NamaJenis = namaJenis,
                PesanMentah = pesan ?? BuildPesanBawaan(namaJenis, nama, nik),
                DataJson = BangunDataJson(namaJenis, nomorHp, parsed, masuk.Data),
                Status = WaRequestStatus.BARU,
                IsRead = false,
                TanggalPermintaan = sekarang,
                Sumber = WaRequestStatus.SumberApi,
                Referensi = referensi
            };

            await _repo.InsertAsync(permintaan, ct).ConfigureAwait(false);

            _audit?.Log("PERMOHONIAN", permintaan.KodePermintaan, "Simpan dari API",
                $"{namaJenis}" + (nama != null ? $" — {nama}" : string.Empty)
                + (referensi != null ? $" (referensi {referensi})" : string.Empty));

            _logger.LogInformation(
                "Permintaan surat {Kode} masuk dari API: {Jenis}", permintaan.KodePermintaan, namaJenis);

            // Naikkan event yang sama dengan jalur WhatsApp/Google Form supaya
            // panel operator diperbarui dan auto-processor (bila aktif) bekerja
            // tanpa percabangan khusus API.
            _engine?.RaiseRequestCreated(permintaan);

            return new ApiHasilPermintaan
            {
                Berhasil = true,
                Kode = permintaan.KodePermintaan,
                Pesan = "Permintaan masuk antrean operator."
            };
        }

        public async Task<ApiStatusPermintaan?> AmbilStatusAsync(string kode, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(kode)) return null;

            var permintaan = await _repo.GetByKodeAsync(kode.Trim(), ct).ConfigureAwait(false);
            if (permintaan is null) return null;

            return new ApiStatusPermintaan
            {
                Kode = permintaan.KodePermintaan,
                Status = permintaan.Status,
                Sumber = WaRequestStatus.TampilanSumber(permintaan.Sumber),
                JenisSurat = WaFormatParser.TampilanJenis(permintaan.NamaJenis),
                Tanggal = permintaan.TanggalPermintaan,
                Catatan = string.IsNullOrWhiteSpace(permintaan.Catatan) ? null : permintaan.Catatan,
                AdaSurat = permintaan.IdSurat.HasValue && permintaan.IdSurat.Value > 0
            };
        }

        public string? TentukanJenisSurat(string? jenisDiberikan, string? pesan)
        {
            var jenis = (jenisDiberikan ?? string.Empty).Trim();
            if (jenis != string.Empty)
            {
                var cocok = WaFormatParser.KatalogSurat.FirstOrDefault(k =>
                    string.Equals(k.NamaJenis, jenis, StringComparison.OrdinalIgnoreCase));
                if (cocok.NamaJenis != null && !WaFormatParser.IsOfflineOnly(cocok.NamaJenis))
                    return cocok.NamaJenis;

                // Nama jenis dari luar mungkin ditulis dengan spasi atau garis
                // bawah — dicoba dinormalkan lebih dulu.
                var dinormalkan = WaFormatParser.TampilanJenis(jenis)
                    .Replace(' ', '_')
                    .ToUpperInvariant();
                var cocokLain = WaFormatParser.KatalogSurat.FirstOrDefault(k =>
                    string.Equals(k.NamaJenis, dinormalkan, StringComparison.OrdinalIgnoreCase));
                if (cocokLain.NamaJenis != null && !WaFormatParser.IsOfflineOnly(cocokLain.NamaJenis))
                    return cocokLain.NamaJenis;
            }

            // Caller tidak menyebut jenis, atau nilainya tidak dikenal: coba
            // tebak dari isi pesan memakai parser yang sama dengan WhatsApp.
            // Tebakan juga disaring jenis offline: kalau tidak, permintaan yang
            // menyimpang tetap bisa masuk antrean padahal suratnya tidak bisa
            // dibuat dari kanal online.
            var tebakan = WaFormatParser.DetectJenisSurat(pesan ?? string.Empty);
            if (tebakan == null || WaFormatParser.IsOfflineOnly(tebakan)) return null;
            return tebakan;
        }

        private static ApiHasilPermintaan Gagal(string kode, string pesan) => new()
        {
            Berhasil = false,
            Pesan = pesan + " (" + kode + ")"
        };

        /// <summary>NIK hanya boleh angka; panjang 16 digit diperiksa pemroses surat.</summary>
        private static string? NormalisasiNik(string? nik)
        {
            if (string.IsNullOrWhiteSpace(nik)) return null;
            var angka = new string(nik!.Where(char.IsDigit).ToArray());
            return angka.Length == 0 ? null : angka;
        }

        private static string? AmbilField(ParsedWaFormat parsed, string kunci)
            => parsed.Fields.TryGetValue(kunci, out var nilai) && !string.IsNullOrWhiteSpace(nilai)
                ? nilai.Trim()
                : null;

        private static string? Rapi(string? nilai)
            => string.IsNullOrWhiteSpace(nilai) ? null : nilai.Trim();

        /// <summary>
        /// Pesan cadangan bila pengirim hanya mengirim field terpisah, supaya
        /// operator tetap melihat isiannya di panel.
        /// </summary>
        private static string BuildPesanBawaan(string namaJenis, string? nama, string? nik)
        {
            var baris = new List<string> { namaJenis };
            if (!string.IsNullOrWhiteSpace(nama)) baris.Add("Nama: " + nama);
            if (!string.IsNullOrWhiteSpace(nik)) baris.Add("NIK: " + nik);
            baris.Add("(dikirim lewat API)");
            return string.Join(Environment.NewLine, baris);
        }

        private static string BangunDataJson(
            string namaJenis,
            string? nomorHp,
            ParsedWaFormat parsed,
            Dictionary<string, string>? dataTambahan)
        {
            var isi = new WaRequestData
            {
                NamaJenis = namaJenis,
                NomorWA = nomorHp ?? string.Empty,
                Fields = new Dictionary<string, string>(parsed.Fields, StringComparer.OrdinalIgnoreCase)
            };

            if (dataTambahan != null)
            {
                foreach (var (kunci, nilai) in dataTambahan)
                {
                    if (!string.IsNullOrWhiteSpace(kunci))
                        isi.Fields[kunci.Trim()] = nilai ?? string.Empty;
                }
            }

            return JsonSerializer.Serialize(isi, OpsiJson);
        }
    }
}
