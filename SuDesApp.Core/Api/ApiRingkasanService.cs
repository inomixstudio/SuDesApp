using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Services;
using SuDesApp.WhatsApp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SuDesApp.Api
{
    /// <summary>
    /// Menyusun angka agregat untuk endpoint /statistik dan daftar ringkas
    /// perangkat desa untuk /perangkat-desa.
    ///
    /// Prinsipnya: layanan ini hanya membaca, tidak pernah menulis, dan tidak
    /// pernah mengeluarkan kolom yang bisa dipakai mengidentifikasi warga
    /// (NIK, alamat, nomor HP, tanggal lahir). Data pribadi hanya dibaca
    /// operator dari dalam aplikasi.
    /// </summary>
    public interface IApiRingkasanService
    {
        /// <summary>Angka agregat desa pada saat ini.</summary>
        Task<ApiRingkasan> AmbilAsync(CancellationToken ct = default);

        /// <summary>
        /// Daftar perangkat desa ringkas. <paramref name="hanyaAktif"/> dipakai
        /// sistem luar yang hanya butuh pemegang jabatan saat ini (mis. untuk
        /// menyusun kop surat).
        /// </summary>
        Task<IReadOnlyList<ApiPerangkatRingkas>> AmbilPerangkatAsync(
            bool hanyaAktif = true, CancellationToken ct = default);

        /// <summary>Daftar nama jenis surat yang bisa diminta lewat API.</summary>
        IReadOnlyList<string> AmbilJenisSuratOnline();

        /// <summary>
        /// Rekap laporan bulanan: jumlah surat per bulan untuk satu tahun
        /// (bawaan tahun berjalan), dirinci per status persetujuan. Tiap bulan
        /// selalu ada walau nol, supaya dashboard sistem luar bisa langsung
        /// membaca tren setahun penuh tanpa menyusun sendiri.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Tahun di luar 1900–2100.</exception>
        Task<ApiRekapBulanan> AmbilRekapBulananAsync(int? tahun = null, CancellationToken ct = default);
    }

    public class ApiRingkasanService : IApiRingkasanService
    {
        private readonly IWargaRepository _warga;
        private readonly IPerangkatDesaService _perangkat;
        private readonly ISuratRepository _surat;
        private readonly IPermintaanWaRepository _permintaan;
        private readonly ILogger<ApiRingkasanService> _logger;
        private readonly Func<DateTime> _sekarang;

        public ApiRingkasanService(
            IWargaRepository warga,
            IPerangkatDesaService perangkat,
            ISuratRepository surat,
            IPermintaanWaRepository permintaan,
            ILogger<ApiRingkasanService>? logger = null,
            Func<DateTime>? sekarang = null)
        {
            _warga = warga ?? throw new ArgumentNullException(nameof(warga));
            _perangkat = perangkat ?? throw new ArgumentNullException(nameof(perangkat));
            _surat = surat ?? throw new ArgumentNullException(nameof(surat));
            _permintaan = permintaan ?? throw new ArgumentNullException(nameof(permintaan));
            _logger = logger ?? NullLogger<ApiRingkasanService>.Instance;
            _sekarang = sekarang ?? (() => DateTime.Now);
        }

        public async Task<ApiRingkasan> AmbilAsync(CancellationToken ct = default)
        {
            var warga = await _warga.GetStatistikWargaAsync().ConfigureAwait(false);
            var perangkat = await _perangkat.AmbilStatistikAsync(ct).ConfigureAwait(false);
            var suratPerStatus = await HitungSuratPerStatusAsync(ct).ConfigureAwait(false);
            var suratPerPersetujuan = await HitungSuratPerStatusPersetujuanAsync(ct).ConfigureAwait(false);
            var permintaanPerStatus = await HitungPermintaanPerStatusAsync(ct).ConfigureAwait(false);

            return new ApiRingkasan
            {
                DibuatPada = _sekarang(),
                TotalWarga = warga.TotalSeluruh,
                WargaAktif = warga.TotalAktif,
                WargaLakiLaki = warga.LakiLaki,
                WargaPerempuan = warga.Perempuan,
                JumlahKartuKeluarga = warga.JumlahKartuKeluarga,
                WargaPerRt = (warga.PerRt ?? Array.Empty<WargaStatistikBaris>())
                    .Select(Baris)
                    .ToList(),

                PerangkatTotal = perangkat.Total,
                PerangkatAktif = perangkat.Aktif,
                PerangkatSelesai = perangkat.Selesai,
                JabatanIntiKosong = perangkat.JabatanIntiKosong,

                SuratTotal = suratPerStatus.Values.Sum(),
                SuratPerStatus = suratPerStatus,
                SuratPerStatusPersetujuan = suratPerPersetujuan,

                PermintaanTotal = permintaanPerStatus.Values.Sum(),
                PermintaanPerStatus = permintaanPerStatus,
                PermintaanBelumDibaca = await HitungPermintaanBelumDibacaAsync(ct).ConfigureAwait(false)
            };
        }

        public async Task<IReadOnlyList<ApiPerangkatRingkas>> AmbilPerangkatAsync(
            bool hanyaAktif = true, CancellationToken ct = default)
        {
            var filter = hanyaAktif
                ? new PerangkatDesaFilter { Status = StatusPerangkat.Aktif }
                : new PerangkatDesaFilter();

            var semua = await _perangkat.AmbilSemuaAsync(filter, ct).ConfigureAwait(false);

            return semua
                .Select(p => new ApiPerangkatRingkas
                {
                    Nama = p.Nama,
                    Jabatan = p.JabatanTampil,
                    Kelompok = p.Kelompok,
                    Wilayah = p.WilayahRingkas,
                    Status = p.StatusTampil,
                    MasaJabatanMulai = p.MasaJabatanMulai,
                    MasaJabatanSelesai = p.MasaJabatanSelesai
                })
                .ToList();
        }

        public IReadOnlyList<string> AmbilJenisSuratOnline()
            => WaFormatParser.KatalogSurat
                .Where(k => !WaFormatParser.IsOfflineOnly(k.NamaJenis))
                .Select(k => k.NamaJenis)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        public async Task<ApiRekapBulanan> AmbilRekapBulananAsync(
            int? tahun = null, CancellationToken ct = default)
        {
            var th = tahun ?? _sekarang().Year;
            if (th < 1900 || th > 2100)
                throw new ArgumentOutOfRangeException(nameof(tahun), th, "Tahun harus antara 1900 dan 2100.");

            var baris = await _surat.GetRekapPersetujuanPerBulanAsync(th, ct).ConfigureAwait(false);

            var perKunci = baris
                .Where(b => !string.IsNullOrWhiteSpace(b.Bulan))
                .GroupBy(b => b.Bulan!, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

            var daftar = new List<ApiBarisRekapBulanan>(12);
            var totalStatus = AwalStatusPersetujuan();
            var totalSemua = 0;

            for (var m = 1; m <= 12; m++)
            {
                var kunci = $"{th}-{m:00}";
                var statusBulan = AwalStatusPersetujuan();
                var jumlah = 0;

                if (perKunci.TryGetValue(kunci, out var isi))
                {
                    foreach (var barisBulan in isi)
                    {
                        var status = string.IsNullOrWhiteSpace(barisBulan.Status)
                            ? StatusPersetujuanSurat.TanpaAlur
                            : barisBulan.Status!;

                        jumlah += barisBulan.Jumlah;
                        statusBulan[status] = statusBulan.GetValueOrDefault(status) + barisBulan.Jumlah;
                        totalStatus[status] = totalStatus.GetValueOrDefault(status) + barisBulan.Jumlah;
                    }
                }

                totalSemua += jumlah;
                daftar.Add(new ApiBarisRekapBulanan
                {
                    Bulan = kunci,
                    JumlahSurat = jumlah,
                    PerStatusPersetujuan = statusBulan
                });
            }

            return new ApiRekapBulanan
            {
                Tahun = th,
                DibuatPada = _sekarang(),
                JumlahSuratTotal = totalSemua,
                SuratPerStatusPersetujuan = totalStatus,
                Bulan = daftar
            };
        }

        private static ApiBarisRingkas Baris(WargaStatistikBaris sumber) => new()
        {
            Kunci = sumber.Label,
            Jumlah = sumber.Jumlah,
            LakiLaki = sumber.LakiLaki,
            Perempuan = sumber.Perempuan
        };

        private async Task<Dictionary<string, int>> HitungSuratPerStatusAsync(CancellationToken ct)
        {
            try
            {
                var hasil = await _surat.CountByStatusAsync(new FilterConditions(), ct).ConfigureAwait(false);
                return hasil.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                // Statistik bersifat pelengkap: kegagalan di sini tidak boleh
                // menggagalkan seluruh respons /statistik.
                _logger.LogWarning(ex, "Gagal menghitung surat per status untuk API");
                return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Kunci bawaan hitungan persetujuan: empat status alur + TANPA_ALUR,
        /// semuanya bernilai nol sampai angka asli menimpanya. Sengaja selalu
        /// penuh supaya sistem luar cukup membaca kunci tanpa berurusan dengan
        /// kunci yang muncul-hilang.
        /// </summary>
        private static Dictionary<string, int> AwalStatusPersetujuan()
        {
            var hasil = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var status in StatusPersetujuanSurat.Semua)
                hasil[status] = 0;
            hasil[StatusPersetujuanSurat.TanpaAlur] = 0;
            return hasil;
        }

        private async Task<Dictionary<string, int>> HitungSuratPerStatusPersetujuanAsync(CancellationToken ct)
        {
            var hasil = AwalStatusPersetujuan();
            try
            {
                var hitung = await _surat.CountPerStatusPersetujuanAsync(ct).ConfigureAwait(false);
                foreach (var kv in hitung)
                    hasil[kv.Key] = kv.Value;
            }
            catch (Exception ex)
            {
                // Sama seperti statistik lain: kegagalan hitungan pelengkap
                // tidak boleh menggagalkan seluruh respons /statistik.
                _logger.LogWarning(ex, "Gagal menghitung surat per status persetujuan untuk API");
            }
            return hasil;
        }

        private async Task<Dictionary<string, int>> HitungPermintaanPerStatusAsync(CancellationToken ct)
        {
            var hasil = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var status in WaRequestStatus.All)
            {
                try
                {
                    hasil[status] = await _permintaan.CountByStatusAsync(status, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Gagal menghitung permintaan {Status} untuk API", status);
                }
            }
            return hasil;
        }

        private async Task<int> HitungPermintaanBelumDibacaAsync(CancellationToken ct)
        {
            try
            {
                return await _permintaan.CountUnreadAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal menghitung permintaan belum dibaca untuk API");
                return 0;
            }
        }
    }
}
