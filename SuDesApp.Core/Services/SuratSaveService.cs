using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using System.ComponentModel.DataAnnotations;
using System.Data;

namespace SuDesApp.Services
{
    /// <summary>Mode penyimpanan surat — eksplisit, bukan flag ambient.</summary>
    public enum ModeSimpan
    {
        /// <summary>Surat aktif: validasi penuh wajib lolos sebelum tersimpan.</summary>
        Aktif,

        /// <summary>Surat draf: isian boleh belum lengkap, boleh dikembangkan lewat Edit.</summary>
        Draft
    }

    /// <summary>Hasil satu operasi simpan: identitas baris + ukuran kelengkapannya.</summary>
    public sealed class HasilSimpanSurat
    {
        public HasilSimpanSurat(int idSurat, string nomorSurat, string status, IReadOnlyList<string> kekurangan)
        {
            ID_Surat = idSurat;
            NomorSurat = nomorSurat ?? string.Empty;
            Status = status ?? string.Empty;
            Kekurangan = kekurangan ?? Array.Empty<string>();
        }

        public int ID_Surat { get; }
        public string NomorSurat { get; }
        public string Status { get; }

        /// <summary>Kosong bila surat lengkap; berisi temuan bila masih draf.</summary>
        public IReadOnlyList<string> Kekurangan { get; }

        public bool Lengkap => Kekurangan.Count == 0;
    }

    /// <summary>
    /// Pemilik alur penyimpanan surat di Core: kumpul → validasi (draft eksplisit,
    /// bukan flag) → transaksi atomik → penomoran → log aktivitas. PDF tetap di lapis
    /// UI (SuratPdfHelper) karena butuh resolver generator per presentasi.
    ///
    /// Sumber kebenaran tunggal sehingga alur baru (WhatsApp, paket NTCR, surat lain)
    /// otomatis mendapat perilaku draft/atomik yang benar tanpa menyalin orkestrasi.
    /// </summary>
    public sealed class SuratSaveService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<SuratSaveService> _logger;

        public SuratSaveService(IUnitOfWork unitOfWork, ILogger<SuratSaveService> logger)
        {
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Simpan surat baru sesuai mode: kumpul → ukur kelengkapan → insert atomik
        /// (nomor dibuat repository bila kosong). Kolektor tidak perlu memedulikan
        /// status; validasi "boleh belum lengkap" adalah keputusan mode, bukan tanda
        /// yang harus di-set pemanggil.
        /// </summary>
        /// <param name="kumpulkan">
        /// Mengisi objek <see cref="SuratData"/> dari form. TIDAK boleh melempar
        /// untuk data belum lengkap — itulah konsekuensi memakai ModeSimpan.Draft;
        /// pengecualian hanya untuk kegagalan teknis (DB, dsb.).
        /// </param>
        public async Task<HasilSimpanSurat> SimpanBaruAsync(
            SuratData suratData,
            ModeSimpan mode,
            Func<SuratData, Task> kumpulkan,
            CancellationToken cancellationToken = default)
        {
            if (suratData == null) throw new ArgumentNullException(nameof(suratData));
            if (kumpulkan == null) throw new ArgumentNullException(nameof(kumpulkan));

            await kumpulkan(suratData);

            // Ukur kelengkapan dengan validasi penuh — surat dianggap aktif dulu agar
            // validator tidak kena short-circuit mode Draft, lalu status asli dipulihkan.
            var statusAsli = suratData.Status;
            suratData.Status = "Active";
            var kekurangan = (await suratData.ValidateAsync()).ToList();
            suratData.Status = string.IsNullOrWhiteSpace(statusAsli) ? "Draft" : statusAsli;

            bool draft = mode == ModeSimpan.Draft;
            if (!draft && kekurangan.Count > 0)
            {
                throw new ValidationException("Surat belum lengkap: " + string.Join("; ", kekurangan));
            }
            if (draft && kekurangan.Count > 0)
            {
                suratData.Status = "Draft";
                _logger.LogInformation(
                    "Surat disimpan sebagai DRAFT ({Jumlah} kekurangan): {Ringkas}",
                    kekurangan.Count, string.Join("; ", kekurangan.Take(3)));
            }
            else if (string.IsNullOrWhiteSpace(suratData.Status) || suratData.Status == "Draft")
            {
                // Lengkap tetapi dipanggil lewat mode Draft → naik ke Aktif (paritas alur lama).
                suratData.Status = "Active";
            }

            int id = await _unitOfWork.SuratRepository.AddSuratAsync(suratData, null, cancellationToken);
            return new HasilSimpanSurat(id, suratData.NomorSurat ?? string.Empty, suratData.Status ?? string.Empty, kekurangan);
        }

        /// <summary>
        /// Simpan satu blanko paket di dalam transaksi yang SUDAH dibuka pemanggil.
        /// Dipakai NtcrPaketService agar seluruh blanko atomik; nomor dibuat sistem
        /// bila kosong (paritas insert biasa).
        /// </summary>
        public Task<int> SimpanDalamTransaksiAsync(
            SuratData suratData,
            IDbTransaction transaction,
            CancellationToken cancellationToken = default)
        {
            if (suratData == null) throw new ArgumentNullException(nameof(suratData));
            return _unitOfWork.SuratRepository.AddSuratAsync(suratData, transaction, cancellationToken);
        }

        /// <summary>
        /// Perbarui surat yang ada sesuai mode: draft boleh belum lengkap (otomatis
        /// naik ke Aktif bila kini sudah lengkap), aktif wajib valid. Nomor surat
        /// terkunci (tidak boleh berubah lewat edit).
        /// </summary>
        public async Task<HasilSimpanSurat> PerbaruiAsync(
            SuratData existing,
            ModeSimpan mode,
            Func<SuratData, Task> kumpulkan,
            CancellationToken cancellationToken = default)
        {
            if (existing == null) throw new ArgumentNullException(nameof(existing));
            if (kumpulkan == null) throw new ArgumentNullException(nameof(kumpulkan));

            var nomorAsli = existing.NomorSurat;
            var statusAsli = existing.Status;
            var wasDraft = string.Equals(statusAsli, "Draft", StringComparison.OrdinalIgnoreCase);

            await kumpulkan(existing);

            // Kunci nomor: edit tidak boleh mengubah nomor (menghindari duplikat).
            existing.NomorSurat = nomorAsli;
            existing.Status = string.IsNullOrWhiteSpace(statusAsli) ? "Draft" : statusAsli;

            // Ukur kelengkapan (aktif sementara agar tidak kena short-circuit Draft).
            existing.Status = "Active";
            var kekurangan = (await existing.ValidateAsync()).ToList();
            existing.Status = statusAsli ?? "Draft";

            if (!wasDraft && kekurangan.Count > 0)
            {
                throw new ValidationException("Surat belum lengkap: " + string.Join("; ", kekurangan));
            }

            existing.Status = wasDraft ? (kekurangan.Count > 0 ? "Draft" : "Active") : existing.Status;

            bool ok = await _unitOfWork.SuratRepository.UpdateAsync(existing);
            if (!ok)
            {
                throw new InvalidOperationException(
                    $"Perubahan surat #{existing.ID_Surat} tidak tersimpan di database.");
            }

            return new HasilSimpanSurat(existing.ID_Surat, existing.NomorSurat ?? string.Empty,
                existing.Status ?? string.Empty, kekurangan);
        }
    }
}
