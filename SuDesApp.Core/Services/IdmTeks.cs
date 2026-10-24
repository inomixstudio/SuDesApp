using System;
using System.Globalization;

namespace SuDesApp.Services
{
    /// <summary>
    /// Teks baku halaman IDM: format angka, status, kalimat target, dan baris
    /// sumber data. Dipakai BERSAMA oleh layar (IdmViewModel) dan laporan PDF
    /// (LaporanIdmBuilder) supaya angka di kertas tidak pernah berbeda dari angka
    /// di layar — termasuk tanda pisah desimal koma dan susunan kalimat target.
    /// </summary>
    public static class IdmTeks
    {
        /// <summary>Tanda untuk nilai yang tidak dikirim pusat.</summary>
        public const string Kosong = "—";

        private static readonly CultureInfo Id = CultureInfo.GetCultureInfo("id-ID");

        /// <summary>Angka gaya Indonesia; null menjadi <see cref="Kosong"/>.</summary>
        public static string Angka(double? nilai, string format = "0.0000") =>
            nilai.HasValue ? nilai.Value.ToString(format, Id) : Kosong;

        /// <summary>Status pusat ditulis kapital; kosong menjadi <see cref="Kosong"/>.</summary>
        public static string Status(string? status) =>
            string.IsNullOrWhiteSpace(status) ? Kosong : status.Trim().ToUpperInvariant();

        /// <summary>
        /// Kalimat jarak ke status berikutnya — hanya memakai angka dari pusat
        /// (skor, skor minimal, penambahan), tanpa menghitung ulang apa pun.
        /// </summary>
        public static string PesanTarget(RingkasanIdm r)
        {
            if (r == null) return string.Empty;

            if (r.TargetStatus.Length == 0) return string.Empty;

            if (r.SkorMinimal.HasValue && r.Skor >= r.SkorMinimal.Value)
            {
                // Tabel rincian berada DI BAWAH ringkasan, baik di layar (pita
                // ringkas di atas tabel) maupun di laporan PDF.
                return "Skor sudah melewati batas " + r.TargetStatus.ToUpperInvariant() + "."
                       + " Tabel di bawah menampilkan kegiatan yang disarankan pusat.";
            }

            if (r.Penambahan.HasValue)
            {
                return "Perlu tambahan " + Angka(r.Penambahan, "0.0000")
                       + " untuk mencapai " + r.TargetStatus.ToUpperInvariant()
                       + (r.SkorMinimal.HasValue ? " (skor minimal " + Angka(r.SkorMinimal, "0.0000") + ")." : ".");
            }

            if (r.SkorMinimal.HasValue)
            {
                return "Kurang " + Angka(r.SkorMinimal.Value - r.Skor, "0.0000")
                       + " untuk mencapai " + r.TargetStatus.ToUpperInvariant()
                       + " (skor minimal " + Angka(r.SkorMinimal, "0.0000") + ").";
            }

            return "Target pusat: " + r.TargetStatus.ToUpperInvariant() + ".";
        }

        /// <summary>
        /// Baris sumber data. Tanggal pengambilan ikut dicetak supaya pembaca
        /// laporan tahu angkanya bukan angka hari ini.
        /// </summary>
        /// <param name="pemisah">
        /// Pemisah sebelum kata "dimuat". Layar memakai titik tengah ("·");
        /// laporan cetak memakai garis tegak karena sebagian pembaca PDF di
        /// komputer desa menampilkan titik tengah dari font tersemat sebagai
        /// kotak.
        /// </param>
        public static string Sumber(DateTime? waktuMuat, string pemisah = "·")
        {
            if (waktuMuat == null) return "Sumber: idm.kemendesa.go.id (data pusat)";
            return "Sumber: idm.kemendesa.go.id " + pemisah + " dimuat "
                   + waktuMuat.Value.ToString("dd MMMM yyyy HH.mm", Id);
        }
    }
}
