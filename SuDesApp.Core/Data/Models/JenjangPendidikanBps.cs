using System;
using System.Collections.Generic;
using System.Linq;

namespace SuDesApp.Data.Models
{
    /// <summary>
    /// Pemetaan nilai bebas kolom <c>Pendidikan</c> ke jenjang baku BPS. Free text
    /// dari operator desa sangat beragam, sementara tabel laporan kependudukan
    /// selalu memakai jenjang baku — supaya angka antar Desa bisa dibandingkan.
    /// </summary>
    public static class JenjangPendidikanBps
    {
        /// <summary>Label yang dipakai bila warga belum mengisi pendidikan.</summary>
        public const string BelumSekolah = "(tidak diisi)";

        /// <summary>Jenjang baku, dari yang terendah. Urutan ini yang dicetak.</summary>
        public static readonly IReadOnlyList<string> Lista = new[]
        {
            "Tidak sekolah",
            "SD/MI sederajat",
            "SMP/MTs sederajat",
            "SMA/MA/SMK sederajat",
            "Diploma I/II/III",
            "Akademi",
            "Universitas"
        };

        /// <summary>
        /// Jenjang baku untuk satu nilai pendidikan. Teks kosong, "-", atau "N/A"
        /// dianggap belum diisi.
        /// </summary>
        public static string Tentukan(string? pendidikan)
        {
            if (string.IsNullOrWhiteSpace(pendidikan)) return BelumSekolah;

            string t = Normalisasi(pendidikan);

            if (Kosong(t)) return BelumSekolah;

            // Urutan pemeriksaan dari jenjang tertinggi ke terendah: "D3/SMK" harus
            // masuk SMA, bukan diploma; "SMA" harus masuk SMA, bukan SMP.
            if (Sebutkan(t, "UNIVERSITAS", "S1", "S2", "S3", "SARJANA", "MAGISTER", "DOKTOR", "PT"))
                return "Universitas";

            if (Sebutkan(t, "AKADEMI", "A.MA", "AMA"))
                return "Akademi";

            if (Sebutkan(t, "DIPLOMA", "D1", "D2", "D3", "D4", "DIPLOMA I", "DIPLOMA II", "DIPLOMA III"))
                return "Diploma I/II/III";

            if (Sebutkan(t, "SMK", "SMA", "MA", "SMP", "MTS", "SEDERAJAT"))
                return MenggolongkanSmkSma(t) ? "SMA/MA/SMK sederajat" : "SMP/MTs sederajat";

            if (Sebutkan(t, "SD", "MI", "SDN", "MIK", "SEKOLAH DASAR"))
                return "SD/MI sederajat";

            if (Sebutkan(t, "TIDAK SEKOLAH", "TIDAK LULUS", "TIDAK PENDIDIKAN", "BUTA"))
                return "Tidak sekolah";

            return BelumSekolah;
        }

        /// <summary>
        /// True berarti jenjang atas (SMA/MA/SMK), False berarti jenjang tengah
        /// (SMP/MTs). "SMP", "SMP/MTs", dan "Lulus SMP" harus tengah; "SMA",
        /// "SMK", "Lulus SMA" harus atas. "MA" diperiksa paling akhir supaya
        /// tidak ikut cocok di dalam "SMA" — dan supaya "MA" sendiri tetap
        /// terhitung sebagai jenjang atas.
        /// </summary>
        private static bool MenggolongkanSmkSma(string teks)
        {
            if (MengandungKata(teks, "SMK") || MengandungKata(teks, "SMA")) return true;
            if (MengandungKata(teks, "SMP") || MengandungKata(teks, "MTS")) return false;
            return MengandungKata(teks, "MA");
        }

        /// <summary>
        /// Normalisasi ke huruf kecil. Tanda baca diganti spasi (bukan dihapus)
        /// supaya pemisah tidak menggabungkan dua jenjang: "SD/SMP" harus tetap
        /// dua kata, kalau "/" dihapus menjadi "sdsmp" lalu "SMP" tidak lagi
        /// dikenali sebagai kata utuh. Spasi berlebih dirapatkan.
        /// </summary>
        private static string Normalisasi(string teks)
        {
            var hasil = new System.Text.StringBuilder(teks.Length);

            foreach (char c in teks.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '/') hasil.Append(c);
                else hasil.Append(' ');
            }

            return System.Text.RegularExpressions.Regex.Replace(hasil.ToString(), @"\s+", " ").Trim();
        }

        private static bool Kosong(string teks) =>
            teks.Length == 0 ||
            teks.All(c => !char.IsLetterOrDigit(c)) ||
            new string(teks.Where(char.IsLetter).ToArray()) is "na" or "n a" or "tidak ada" or "kosong" or "nol";

        /// <summary>Sebutkan bila teks mengandung salah satu penanda sebagai kata utuh.</summary>
        private static bool Sebutkan(string teks, params string[] penanda)
        {
            foreach (var p in penanda)
            {
                if (MengandungKata(teks, p)) return true;
            }
            return false;
        }

        /// <summary>
        /// True bila <paramref name="kata"/> muncul sebagai kata utuh. Pencocokan
        /// batas dipakai supaya "MA" tidak cocok di dalam "mahasiswa" dan "S1" tidak
        /// cocok di dalam "S12".
        ///
        /// Pencocokan tidak membedakan huruf besar-kecil: teks dinormalisasi ke
        /// huruf kecil tapi penanda ditulis huruf besar supaya mudah dibaca, jadi
        /// keduanya harus dibandingkan case-insensitive.
        /// </summary>
        private static bool MengandungKata(string teks, string kata)
        {
            if (kata.Length == 0) return false;
            if (teks.Length < kata.Length) return false;

            int dari = 0;
            while (dari <= teks.Length - kata.Length)
            {
                int posisi = teks.IndexOf(kata, dari, StringComparison.OrdinalIgnoreCase);
                if (posisi < 0) return false;

                bool batasKiri = posisi == 0 || !char.IsLetterOrDigit(teks[posisi - 1]);
                int akhir = posisi + kata.Length;
                bool batasKanan = akhir == teks.Length || !char.IsLetterOrDigit(teks[akhir]);

                if (batasKiri && batasKanan) return true;
                dari = posisi + 1;
            }

            return false;
        }
    }
}
