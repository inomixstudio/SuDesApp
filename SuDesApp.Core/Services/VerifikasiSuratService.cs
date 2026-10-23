using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;

namespace SuDesApp.Services
{
    /// <summary>Hasil pemeriksaan satu kode verifikasi surat.</summary>
    public sealed class HasilVerifikasiSurat
    {
        private HasilVerifikasiSurat() { }

        /// <summary>Kode dikenal dan datanya ditemukan di register.</summary>
        public bool Ditemukan { get; private init; }

        /// <summary>
        /// Kode ditemukan DAN cap dokumen masih cocok — inilah surat yang sah.
        /// </summary>
        public bool Sah { get; private init; }

        /// <summary>Kode dalam bentuk baku, mis. "SD-7K3M-9QX2".</summary>
        public string? Kode { get; private init; }

        public int ID_Surat { get; private init; }
        public string? NomorSurat { get; private init; }
        public string? NamaJenis { get; private init; }
        public DateTime? TanggalSurat { get; private init; }

        /// <summary>Nama pemohon seperti tercatat di arsip (bukan NIK).</summary>
        public string? NamaPemohon { get; private init; }

        /// <summary>Ringkasan untuk dibaca manusia di halaman Verifikasi Surat.</summary>
        public string Pesan { get; private init; } = string.Empty;

        /// <summary>True bila isi surat berubah setelah terbit (cap tidak cocok).</summary>
        public bool DataBerubah { get; private init; }

        /// <summary>
        /// Status alur persetujuan surat (DIAJUKAN/DIVERIFIKASI/TERBIT/DITOLAK),
        /// atau TANPA_ALUR bila surat tidak melewati alur. Untuk surat dalam
        /// alur, nilainya sama dengan label yang tercetak di kertas surat.
        /// </summary>
        public string StatusPersetujuan { get; private init; } = StatusPersetujuanSurat.TanpaAlur;

        public static HasilVerifikasiSurat TidakDikenal(string? masukan) => new()
        {
            Pesan = string.IsNullOrWhiteSpace(masukan)
                ? "Kode verifikasi belum diisi."
                : "Kode verifikasi tidak dikenali. Periksa kembali penulisannya (bentuknya seperti SD-7K3M-9QX2)."
        };

        public static HasilVerifikasiSurat TidakAda(string kode) => new()
        {
            Kode = kode,
            Pesan = $"Kode {kode} tidak ditemukan pada arsip surat desa. Surat dengan kode ini tidak pernah diterbitkan aplikasi ini."
        };

        public static HasilVerifikasiSurat Dari(SuratData surat, string kode, bool hashCocok, bool cuplikanCocok) => new()
        {
            Ditemukan = true,
            Sah = hashCocok && cuplikanCocok,
            DataBerubah = !hashCocok || !cuplikanCocok,
            Kode = kode,
            ID_Surat = surat.ID_Surat,
            NomorSurat = surat.NomorSurat,
            NamaJenis = surat.NamaJenis,
            TanggalSurat = surat.TanggalSurat == default ? null : surat.TanggalSurat,
            NamaPemohon = string.IsNullOrWhiteSpace(surat.Warga?.Nama)
                ? surat.Instansi?.NamaInstansi
                : surat.Warga!.Nama,
            StatusPersetujuan = StatusPersetujuanSurat.Normalisasi(surat.StatusPersetujuan)
                ?? StatusPersetujuanSurat.TanpaAlur,
            Pesan = hashCocok && cuplikanCocok
                ? "Surat sah: kode terdaftar di arsip desa dan isinya masih sama dengan saat diterbitkan."
                : "Kode terdaftar, tetapi isi surat tercatat sudah berubah dari yang diterbitkan. Jangan dipakai sebelum diperiksa operator desa."
        };
    }

    /// <summary>
    /// Satu pintu untuk urusan keaslian surat: memberi kode pada surat yang
    /// terbit, dan memeriksa kode yang dibawa warga/instansi.
    /// </summary>
    public interface IVerifikasiSuratService
    {
        /// <summary>
        /// Pastikan surat punya kode verifikasi + cap dokumen. Kode yang sudah
        /// ada tidak pernah diganti supaya surat yang sudah dicetak tetap sah
        /// (dan perubahan setelah terbit justru terdeteksi saat verifikasi).
        /// </summary>
        Task<string> PastikanTerdaftarAsync(SuratData surat, CancellationToken ct = default);

        /// <summary>
        /// Periksa masukan pengguna — kode apa adanya ("sd7k3m9qx2") atau
        /// seluruh isi QR ("SUDES|SD-7K3M-9QX2|A1B2C3D4E5").
        /// </summary>
        Task<HasilVerifikasiSurat> VerifikasiAsync(string masukan, CancellationToken ct = default);
    }

    public sealed class VerifikasiSuratService : IVerifikasiSuratService
    {
        private readonly ISuratRepository _surat;
        private readonly ILogger<VerifikasiSuratService> _logger;

        public VerifikasiSuratService(
            ISuratRepository surat,
            ILogger<VerifikasiSuratService>? logger = null)
        {
            _surat = surat ?? throw new ArgumentNullException(nameof(surat));
            _logger = logger ?? NullLogger<VerifikasiSuratService>.Instance;
        }

        public async Task<string> PastikanTerdaftarAsync(SuratData surat, CancellationToken ct = default)
        {
            if (surat == null) throw new ArgumentNullException(nameof(surat));

            if (!string.IsNullOrWhiteSpace(surat.KodeVerifikasi))
                return surat.KodeVerifikasi!;

            if (surat.ID_Surat <= 0 || string.IsNullOrWhiteSpace(surat.NomorSurat))
                throw new InvalidOperationException(
                    "Kode verifikasi hanya bisa dibuat setelah surat tersimpan dan bernomor.");

            var kode = KodeVerifikasiSurat.BuatKode();
            var hash = KodeVerifikasiSurat.HitungHash(surat);

            await _surat.SimpanVerifikasiAsync(surat.ID_Surat, kode, hash, ct).ConfigureAwait(false);

            surat.KodeVerifikasi = kode;
            surat.HashVerifikasi = hash;

            _logger.LogInformation("Kode verifikasi dibuat untuk surat #{Id} ({Nomor}).",
                surat.ID_Surat, surat.NomorSurat);

            return kode;
        }

        public async Task<HasilVerifikasiSurat> VerifikasiAsync(string masukan, CancellationToken ct = default)
        {
            var (kode, cuplikanHash) = KodeVerifikasiSurat.UraiMasukan(masukan);
            if (kode.Length == 0)
                return HasilVerifikasiSurat.TidakDikenal(masukan);

            var surat = await _surat.GetByKodeVerifikasiAsync(kode, ct).ConfigureAwait(false);
            if (surat == null)
            {
                _logger.LogInformation("Verifikasi kode {Kode}: tidak ada di arsip.", kode);
                return HasilVerifikasiSurat.TidakAda(kode);
            }

            var hashSekarang = KodeVerifikasiSurat.HitungHash(surat);
            bool hashCocok = string.IsNullOrWhiteSpace(surat.HashVerifikasi)
                || string.Equals(surat.HashVerifikasi, hashSekarang, StringComparison.OrdinalIgnoreCase);

            // Cuplikan hash di QR harus sama dengan cap dokumen yang dihitung
            // ulang dari data saat ini — inilah yang mendeteksi arsip yang diubah.
            bool cuplikanCocok = string.IsNullOrWhiteSpace(cuplikanHash)
                || hashSekarang.StartsWith(cuplikanHash!, StringComparison.OrdinalIgnoreCase);

            var hasil = HasilVerifikasiSurat.Dari(surat, kode, hashCocok, cuplikanCocok);

            _logger.LogInformation("Verifikasi kode {Kode}: {Status} (surat #{Id}).",
                kode, hasil.Sah ? "sah" : "tidak cocok", surat.ID_Surat);

            return hasil;
        }
    }
}
