using System;
using System.Collections.Generic;
using System.Linq;

namespace SuDesApp.Data.Models
{
    /// <summary>
    /// Status alur persetujuan surat. Alur ini **opsional per surat**: surat yang
    /// dibuat seperti biasa (tanpa diajukan) tidak punya status apa pun dan tetap
    /// bisa dicetak seperti selama ini — kebiasaan operator tidak berubah.
    ///
    /// Status hanya terisi bila operator secara sadar menekan "Ajukan Verifikasi",
    /// biasanya untuk surat yang perlu diperiksa Sekdes lalu ditandatangani Kades:
    ///
    ///   (kosong) ──ajukan──► DIAJUKAN ──verifikasi──► DIVERIFIKASI ──tanda tangan──► TERBIT
    ///                            │                        │
    ///                            └────────tolak───────────┴──────────► DITOLAK
    ///
    /// Surat yang belum selesai alurnya TIDAK dipalsukan jadi "biasa saja" saat
    /// dicetak: kakinya diberi tanda jelas (lihat <see cref="LabelCetak"/>) supaya
    /// kertas setengah jadi tidak beredar sebagai surat resmi.
    /// </summary>
    public static class StatusPersetujuanSurat
    {
        /// <summary>Menunggu pemeriksaan Sekdes/pejabat yang berwenang.</summary>
        public const string Diajukan = "DIAJUKAN";

        /// <summary>Sudah diperiksa, menunggu tanda tangan Kepala Desa.</summary>
        public const string Diverifikasi = "DIVERIFIKASI";

        /// <summary>Sudah ditandatangani — alur selesai.</summary>
        public const string Terbit = "TERBIT";

        /// <summary>Ditolak; tidak boleh dipakai sebagai surat resmi.</summary>
        public const string Ditolak = "DITOLAK";

        /// <summary>
        /// Kunci agregat untuk surat yang TIDAK melewati alur (status kosong).
        /// Dipakai hitungan dan rekap API supaya setiap baris surat selalu punya
        /// kunci — memudahkan sistem luar menambahkan seluruh status menjadi
        /// jumlah total tanpa berurusan dengan nilai kosong/null.
        /// </summary>
        public const string TanpaAlur = "TANPA_ALUR";

        public static readonly IReadOnlyList<string> Semua = new[]
        {
            Diajukan, Diverifikasi, Terbit, Ditolak
        };

        /// <summary>True bila status dikenal (string kosong diperbolehkan: tanpa alur).</summary>
        public static bool Valid(string? status) =>
            string.IsNullOrWhiteSpace(status)
            || Semua.Contains(status.Trim().ToUpperInvariant(), StringComparer.Ordinal);

        public static string? Normalisasi(string? status) =>
            string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToUpperInvariant();

        /// <summary>Kalimat status untuk kolom register dan halaman detail.</summary>
        public static string Tampilan(string? status) => Normalisasi(status) switch
        {
            Diajukan => "Menunggu verifikasi",
            Diverifikasi => "Menunggu tanda tangan",
            Terbit => "Sudah ditandatangani",
            Ditolak => "Ditolak",
            _ => "Tanpa persetujuan"
        };

        /// <summary>
        /// True bila surat masih di tengah alur (belum terbit dan belum ditolak) —
        /// dasar label "jangan dipakai" saat dicetak.
        /// </summary>
        public static bool MasihDalamAlur(string? status) =>
            Normalisasi(status) is Diajukan or Diverifikasi;

        /// <summary>
        /// True bila label cetak harus tampil sebagai peringatan (merah): surat
        /// yang belum sah atau sudah ditolak. Label SALINAN tidak termasuk — salinan
        /// memang sah dipakai, hanya perlu dibedakan dari lembar aslinya.
        /// </summary>
        public static bool PeringatanCetak(string? status)
        {
            var rapi = Normalisasi(status);
            return rapi == Ditolak || MasihDalamAlur(rapi);
        }

        /// <summary>
        /// Label yang dicetak di atas judul surat. Null berarti surat dicetak
        /// seperti biasa (tanpa tanda tambahan) — inilah perilaku lama, supaya
        /// surat yang tidak memakai alur persetujuan tidak berubah sama sekali.
        ///
        /// Urutan penentuan:
        /// 1. Surat yang ditolak selalu diberi tanda paling keras.
        /// 2. Surat yang masih dalam alur diberi tanda statusnya.
        /// 3. Surat yang sudah pernah dicetak diberi tanda SALINAN + cetakan ke-berapa.
        /// </summary>
        /// <param name="jumlahCetak">Jumlah cetakan yang sudah tercatat (0 = belum pernah).</param>
        public static string? LabelCetak(string? status, int jumlahCetak)
        {
            var rapi = Normalisasi(status);

            if (rapi == Ditolak)
                return "DITOLAK — TIDAK SAH, JANGAN DIPAKAI";

            if (rapi == Diajukan)
                return "DRAF — MENUNGGU VERIFIKASI";

            if (rapi == Diverifikasi)
                return "SUDAH DIVERIFIKASI — MENUNGGU TANDA TANGAN";

            // Cetakan pertama bebas label; cetakan berikutnya adalah salinan.
            if (jumlahCetak >= 1)
                return $"SALINAN — CETAKAN KE-{jumlahCetak + 1}";

            return null;
        }
    }

    /// <summary>
    /// Satu baris mentah rekap persetujuan per bulan (query
    /// <c>RekapPersetujuanPerBulan</c>): jumlah surat pada satu bulan dengan
    /// satu status. Bulan tanpa surat tidak menghasilkan baris — penyusunan
    /// 12 bulan penuh dilakukan oleh pemanggil (ApiRingkasanService).
    /// </summary>
    public sealed class BarisRekapPersetujuan
    {
        /// <summary>Bulan "yyyy-MM"; null hanya bila tanggal surat tidak terbaca.</summary>
        public string? Bulan { get; set; }

        /// <summary>Status persetujuan huruf kapital, atau <see cref="StatusPersetujuanSurat.TanpaAlur"/>.</summary>
        public string? Status { get; set; }

        public int Jumlah { get; set; }
    }
}
