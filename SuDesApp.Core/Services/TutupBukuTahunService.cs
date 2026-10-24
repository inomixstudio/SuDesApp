using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;

namespace SuDesApp.Services
{
    /// <summary>Alasan tindakan tutup/buka ditolak.</summary>
    public enum AlasanTolakTutupBuku
    {
        TidakAda,
        TahunTidakWajar,
        TahunMendatang,
        SudahTertutup,
        BelumTertutup,
        BlockingTanpaPaksa,
        TidakAdaSurat
    }

    /// <summary>Hasil satu percobaan tutup atau buka buku.</summary>
    public class HasilTutupBuku
    {
        public bool Berhasil { get; init; }
        public required int Tahun { get; init; }
        public AlasanTolakTutupBuku Alasan { get; init; } = AlasanTolakTutupBuku.TidakAda;
        public string? Pesan { get; init; }
        public TutupBukuTahun? Data { get; init; }
        public HasilVerifikasiNomor? Verifikasi { get; init; }

        /// <summary>Masalah yang ditemukan tapi tidak menutup buku, jadi perlu diperbaiki.</summary>
        public IReadOnlyList<string> Peringatan { get; init; } = Array.Empty<string>();
    }

    public interface ITutupBukuTahunService
    {
        /// <summary>Status satu tahun; kalau belum pernah ditutup, dianggap terbuka.</summary>
        Task<TutupBukuTahun> AmbilStatusAsync(int tahun, CancellationToken ct = default);

        Task<List<TutupBukuTahun>> AmbilSemuaAsync(CancellationToken ct = default);

        /// <summary>
        /// Tutup buku satu tahun setelah memverifikasi penomorannya. Tahun yang
        /// punya masalah blocking hanya bisa ditutup bila <paramref name="paksa"/>
        /// true, dan keputusan itu tercatat di snapshot. Tahun yang belum punya
        /// surat tidak bisa ditutup karena tidak ada yang diabalkan.
        /// </summary>
        Task<HasilTutupBuku> TutupAsync(
            int tahun, string? catatan, string? operatorNama, bool paksa = false, CancellationToken ct = default);

        /// <summary>Buka kembali buku tahun. Tidak menghapus apa pun.</summary>
        Task<HasilTutupBuku> BukaAsync(
            int tahun, string? alasan, string? operatorNama, CancellationToken ct = default);
    }

    /// <summary>
    /// Tutup buku tahunan.
    ///
    /// Prinsipnya: menutup tahun bersifat non-destruktif dan bisa dibalik.
    /// Tidak ada kode yang dihapus saat tutup, dan <see cref="BukaAsync"/>
    /// hanya mengubah status — jadi keputusan ini aman untuk dicoba di
    /// desa yang masih belajar.
    ///
    /// Tutup buku mengikat register, jadi tidak memotong deret secara fisik.
    /// Nomor surat yang sudah terbit tetap utuh, dan penanda tutup dipakai
    /// sebagai flag arsip (bukan kunci tulis) supaya tidak ada surat lama
    /// yang hilang selagi produk ini masih dipakai.
    /// </summary>
    public class TutupBukuTahunService : ITutupBukuTahunService
    {
        private readonly ITutupBukuTahunRepository _repo;
        private readonly IVerifikasiPenomoranService _verifikasi;
        private readonly ILogger _logger;
        private readonly Func<DateTime> _sekarang;

        private static readonly JsonSerializerOptions OpsiJson = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        public TutupBukuTahunService(
            ITutupBukuTahunRepository repo,
            IVerifikasiPenomoranService verifikasi,
            ILogger<TutupBukuTahunService>? logger = null,
            Func<DateTime>? sekarang = null)
        {
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
            _verifikasi = verifikasi ?? throw new ArgumentNullException(nameof(verifikasi));
            _logger = logger ?? (ILogger)NullLogger<TutupBukuTahunService>.Instance;
            _sekarang = sekarang ?? (() => DateTime.Now);
        }

        public async Task<TutupBukuTahun> AmbilStatusAsync(int tahun, CancellationToken ct = default)
        {
            ValidasiTahun(tahun);

            var data = await _repo.GetAsync(tahun, ct).ConfigureAwait(false);
            return data ?? new TutupBukuTahun
            {
                Tahun = tahun,
                Status = StatusTutupBuku.Terbuka
            };
        }

        public async Task<List<TutupBukuTahun>> AmbilSemuaAsync(CancellationToken ct = default) =>
            await _repo.GetAllAsync(ct).ConfigureAwait(false);

        public async Task<HasilTutupBuku> TutupAsync(
            int tahun, string? catatan, string? operatorNama, bool paksa = false, CancellationToken ct = default)
        {
            try
            {
                ValidasiTahun(tahun);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return Gagal(tahun, AlasanTolakTutupBuku.TahunTidakWajar, ex.Message);
            }

            int tahunSekarang = _sekarang().Year;
            if (tahun > tahunSekarang)
            {
                return Gagal(tahun, AlasanTolakTutupBuku.TahunMendatang,
                    $"Tahun {tahun} belum tiba. Buku yang bisa ditutup paling lama tahun {tahunSekarang}.");
            }

            var sebelumnya = await AmbilStatusAsync(tahun, ct).ConfigureAwait(false);
            if (sebelumnya.Tertutup)
            {
                return new HasilTutupBuku
                {
                    Berhasil = false,
                    Tahun = tahun,
                    Alasan = AlasanTolakTutupBuku.SudahTertutup,
                    Pesan = $"Buku tahun {tahun} sudah tertutup.",
                    Data = sebelumnya
                };
            }

            var hasilVerifikasi = await _verifikasi.PeriksaAsync(tahun).ConfigureAwait(false);
            var peringatan = KumpulkanPeringatan(hasilVerifikasi);

            if (hasilVerifikasi.JumlahSeluruhSurat == 0)
            {
                return new HasilTutupBuku
                {
                    Berhasil = false,
                    Tahun = tahun,
                    Alasan = AlasanTolakTutupBuku.TidakAdaSurat,
                    Pesan = $"Tidak ada surat tahun {tahun}, jadi tidak ada yang perlu ditutup.",
                    Verifikasi = hasilVerifikasi
                };
            }

            if (hasilVerifikasi.AdaMasalahBlocking && !paksa)
            {
                string detail = string.Join("; ", hasilVerifikasi.NomorTahunTidakCocok.Count > 0
                    ? hasilVerifikasi.NomorTahunTidakCocok
                    : hasilVerifikasi.Deret
                        .Where(d => d.AdaNomorGanda || d.NomorTidakTerbaca.Count > 0)
                        .Select(d => d.Ringkasan));

                return new HasilTutupBuku
                {
                    Berhasil = false,
                    Tahun = tahun,
                    Alasan = AlasanTolakTutupBuku.BlockingTanpaPaksa,
                    Pesan = "Penomoran tahun ini bermasalah. Perbaiki dulu, atau tutup dengan paksa " +
                            $"dan catat alasannya. Rincian: {detail}",
                    Verifikasi = hasilVerifikasi,
                    Peringatan = peringatan
                };
            }

            DateTime sekarang = _sekarang();
            var data = new TutupBukuTahun
            {
                Tahun = tahun,
                Status = StatusTutupBuku.Tertutup,
                TanggalTutup = FormatTanggal(sekarang),
                JumlahSurat = hasilVerifikasi.JumlahSeluruhSurat,
                JumlahDeret = hasilVerifikasi.Deret.Count,
                Snapshot = JsonSerializer.Serialize(SusunSnapshot(tahun, hasilVerifikasi, catatan, operatorNama, paksa, sekarang), OpsiJson),
                Catatan = catatan,
                DitutupOleh = operatorNama,
                DitutupPada = FormatTanggal(sekarang),
                CreatedAt = sebelumnya.CreatedAt,
                DibukaOleh = null,
                DibukaPada = null
            };

            await _repo.UpsertAsync(data, ct).ConfigureAwait(false);
            _logger.LogInformation("Buku tahun {Tahun} ditutup ({Jumlah} surat, {Deret} deret){Paksa}.",
                tahun, data.JumlahSurat, data.JumlahDeret, paksa ? " dengan paksa" : string.Empty);

            return new HasilTutupBuku
            {
                Berhasil = true,
                Tahun = tahun,
                Data = data,
                Verifikasi = hasilVerifikasi,
                Peringatan = peringatan
            };
        }

        public async Task<HasilTutupBuku> BukaAsync(
            int tahun, string? alasan, string? operatorNama, CancellationToken ct = default)
        {
            try
            {
                ValidasiTahun(tahun);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return Gagal(tahun, AlasanTolakTutupBuku.TahunTidakWajar, ex.Message);
            }

            var sebelumnya = await AmbilStatusAsync(tahun, ct).ConfigureAwait(false);
            if (!sebelumnya.Tertutup)
            {
                return new HasilTutupBuku
                {
                    Berhasil = false,
                    Tahun = tahun,
                    Alasan = AlasanTolakTutupBuku.BelumTertutup,
                    Pesan = $"Buku tahun {tahun} memang belum ditutup.",
                    Data = sebelumnya
                };
            }

            DateTime sekarang = _sekarang();
            var data = new TutupBukuTahun
            {
                ID = sebelumnya.ID,
                Tahun = tahun,
                Status = StatusTutupBuku.Terbuka,

                // Ditutup* sengaja dikosongkan: begitu buku dibuka, statusnya
                // bukan "pernah ditutup" dan snapshot lama tidak lagi
                // mewakili register sekarang.
                TanggalTutup = null,
                JumlahSurat = sebelumnya.JumlahSurat,
                JumlahDeret = sebelumnya.JumlahDeret,
                Snapshot = null,
                Catatan = string.IsNullOrWhiteSpace(alasan)
                    ? sebelumnya.Catatan
                    : $"[Dibuka] {alasan.Trim()}",
                DitutupOleh = null,
                DitutupPada = null,
                DibukaOleh = operatorNama,
                DibukaPada = FormatTanggal(sekarang),
                CreatedAt = sebelumnya.CreatedAt
            };

            await _repo.UpsertAsync(data, ct).ConfigureAwait(false);
            _logger.LogInformation("Buku tahun {Tahun} dibuka kembali.", tahun);

            return new HasilTutupBuku
            {
                Berhasil = true,
                Tahun = tahun,
                Data = data
            };
        }

        /// <summary>
        /// Peringatan yang tidak menutup buku diambil dari hasil verifikasi,
        /// supaya arsip register dan tutup buku memakai perhitungan yang sama.
        /// </summary>
        internal static IReadOnlyList<string> KumpulkanPeringatan(HasilVerifikasiNomor hasil) =>
            hasil.Peringatan();

        internal static SnapshotTutupBuku SusunSnapshot(
            int tahun, HasilVerifikasiNomor hasil, string? catatan, string? operatorNama, bool paksa, DateTime sekarang)
        {
            return new SnapshotTutupBuku
            {
                Tahun = tahun,
                DitutupPada = FormatTanggal(sekarang),
                DitutupOleh = operatorNama ?? "-",
                JumlahSurat = hasil.JumlahSeluruhSurat,
                Dipaksa = paksa,
                Catatan = catatan,
                Deret = hasil.Deret.Select(d => new SnapshotDeret
                {
                    Awalan = d.Awalan,
                    Tahun = d.Tahun,
                    Jumlah = d.Jumlah,
                    NomorPertama = d.NomorPertama,
                    NomorTerakhir = d.NomorTerakhir,
                    MulaiDariSatu = d.MulaiDariSatu,
                    NomorHilang = d.NomorHilang.ToList(),
                    NomorGanda = d.NomorGanda.ToList(),
                    NomorTidakTerbaca = d.NomorTidakTerbaca.ToList(),
                    NamaJenis = d.NamaJenis.ToList()
                }).ToList()
            };
        }

        private static string FormatRentang(IReadOnlyList<int> nomor)
        {
            if (nomor.Count == 0) return "-";
            if (nomor.Count <= 5) return string.Join(", ", nomor.Select(n => n.ToString("000", CultureInfo.InvariantCulture)));
            return string.Join(", ", nomor.Take(3).Select(n => n.ToString("000", CultureInfo.InvariantCulture)))
                   + $", … (+{nomor.Count - 3})";
        }

        private static string FormatTanggal(DateTime waktu) =>
            waktu.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        private static void ValidasiTahun(int tahun)
        {
            if (tahun is < 1900 or > 3000)
                throw new ArgumentOutOfRangeException(nameof(tahun), tahun, "Tahun di luar rentang wajar.");
        }

        private static HasilTutupBuku Gagal(int tahun, AlasanTolakTutupBuku alasan, string pesan) =>
            new() { Berhasil = false, Tahun = tahun, Alasan = alasan, Pesan = pesan };
    }
}
