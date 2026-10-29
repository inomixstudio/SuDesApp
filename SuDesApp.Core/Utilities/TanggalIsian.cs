using System;
using System.Globalization;

namespace SuDesApp.Utilities
{
    /// <summary>
    /// Isian tanggal berbentuk teks (<c>dd-mm-yyyy</c>) pengganti pemilih tanggal:
    /// menerima beberapa pola yang lazim diketik operator dan menghasilkan pesan
    /// galat yang seragam di seluruh form. Dipakai halaman Data Perangkat Desa,
    /// Isi Template Surat, Register Surat/NTCR, dan Permohonan Rekening Koran.
    ///
    /// Satu-satunya tempat aturan baca/tulis tanggal isian ditulis: proyek UI
    /// memakainya dari sini, bukan menyalinnya ke lapisan WPF.
    /// </summary>
    public static class TanggalIsian
    {
        /// <summary>
        /// Pola yang diterima: dd-mm-yyyy dan variasi titik/garis miring serta
        /// urutan tahun-di-depan, supaya kebiasaan menulis operator tidak
        /// menghambat pengisian.
        /// </summary>
        public static readonly string[] Pola =
        {
            "dd-MM-yyyy", "d-M-yyyy", "dd.MM.yyyy", "d.M.yyyy", "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd"
        };

        /// <summary>Contoh format untuk petunjuk isian dan pesan galat.</summary>
        public const string Contoh = "dd-mm-yyyy";

        /// <summary>Ubah tanggal menjadi teks isian dd-mm-yyyy (kosong bila tidak ada).</summary>
        public static string KeTeks(DateTime? tanggal) =>
            tanggal?.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture) ?? string.Empty;

        /// <summary>
        /// Baca isian tanggal. Mengembalikan true bila isian kosong (boleh kosong)
        /// atau berhasil dibaca; mengembalikan false disertai pesan bila teksnya
        /// tidak dikenali sebagai tanggal.
        /// </summary>
        public static bool Baca(string? teks, string label, out DateTime? tanggal, out string pesan)
        {
            tanggal = null;
            pesan = string.Empty;

            string isi = (teks ?? string.Empty).Trim();
            if (isi.Length == 0) return true;

            if (DateTime.TryParseExact(isi, Pola, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTime hasil))
            {
                tanggal = hasil;
                return true;
            }

            pesan = Pesan(label, isi);
            return false;
        }


        /// <summary>Baca isian tanggal yang wajib diisi: kosong pun menjadi galat, sebab
        /// dokumennya tidak bisa dibuat tanpa tanggal.</summary>
        public static bool BacaWajib(string? teks, string label, out DateTime tanggal, out string pesan)
        {
            tanggal = default;
            if (!Baca(teks, label, out DateTime? hasil, out pesan)) return false;

            if (hasil is null)
            {
                pesan = $"Isian {label} wajib diisi. Tulis dengan format {Contoh}, mis. 17-08-2026.";
                return false;
            }

            tanggal = hasil.Value;
            return true;
        }

        /// <summary>Pesan galat format tanggal yang seragam untuk seluruh form.</summary>
        public static string Pesan(string label, string isi) =>
            $"Isian {label} belum berupa tanggal yang sah. Tulis dengan format {Contoh}, "
            + $"mis. 17-08-2026 (isian sekarang: \u201C{isi}\u201D).";
    }
}
