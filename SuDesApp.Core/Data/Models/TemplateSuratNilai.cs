using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SuDesApp.Data.Models
{
    /// <summary>
    /// Aturan pengisian dan pencetakan nilai kolom template surat buatan pengguna.
    ///
    /// Dipakai bersama oleh formulir pengisian (validasi sebelum cetak), pratinjau
    /// wizard, dan generator PDF — sehingga nilai yang terlihat di layar selalu sama
    /// dengan yang tercetak di surat.
    /// </summary>
    public static class TemplateSuratNilai
    {
        private static readonly CultureInfo Budaya = new("id-ID");

        /// <summary>Nilai kolom yang sudah dirapikan untuk disimpan/dicetak.</summary>
        public static string Rapikan(string? nilai) => (nilai ?? string.Empty).Trim();

        /// <summary>
        /// Nilai yang benar-benar tercetak: tanggal diubah ke bentuk Indonesia,
        /// kolom lain dikembalikan apa adanya. Kolom wajib yang kosong dicetak
        /// sebagai titik-titik agar surat tetap berbentuk formulir kosong.
        /// </summary>
        public static string NilaiCetak(KolomTemplateSurat kolom, string? nilai)
        {
            string isi = Rapikan(nilai);

            if (kolom?.Tipe == TipeKolomTemplate.Tanggal && isi.Length > 0)
            {
                return FormatTanggal(isi);
            }

            return isi;
        }

        /// <summary>
        /// Ubah teks tanggal (dd-MM-yyyy, dd/MM/yyyy, atau yyyy-MM-dd) menjadi
        /// "12 Januari 2026". Teks yang tidak dikenali dikembalikan apa adanya
        /// supaya isian pengguna tidak hilang.
        /// </summary>
        public static string FormatTanggal(string? nilai)
        {
            string isi = Rapikan(nilai);
            if (isi.Length == 0) return string.Empty;

            string[] pola = { "dd-MM-yyyy", "dd/MM/yyyy", "d-M-yyyy", "d/M/yyyy", "yyyy-MM-dd", "dd-MM-yyyy HH:mm", "dd-MM-yyyy HH:mm:ss" };
            foreach (var p in pola)
            {
                if (DateTime.TryParseExact(isi, p, CultureInfo.InvariantCulture, DateTimeStyles.None, out var tanggal))
                {
                    return tanggal.ToString("dd MMMM yyyy", Budaya);
                }
            }

            return isi;
        }

        /// <summary>
        /// Periksa seluruh kolom template terhadap nilai yang diisi. Kesalahan
        /// dikembalikan per kunci kolom (bukan hanya daftar pesan) supaya formulir
        /// dapat menandai baris yang bermasalah.
        /// </summary>
        public static Dictionary<string, string> Validasi(
            TemplateSuratKustom? template,
            IReadOnlyDictionary<string, string>? nilai)
        {
            var kesalahan = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (template?.Kolom == null) return kesalahan;

            foreach (var kolom in template.Kolom)
            {
                string isi = Ambil(nilai, kolom.Kunci);

                if (kolom.Wajib && isi.Length == 0)
                {
                    kesalahan[kolom.Kunci] = $"{Judul(kolom)} wajib diisi.";
                    continue;
                }

                if (isi.Length == 0) continue;

                switch (kolom.Tipe)
                {
                    case TipeKolomTemplate.Nik:
                        if (!isi.All(char.IsDigit) || isi.Length != 16)
                        {
                            kesalahan[kolom.Kunci] = $"{Judul(kolom)} harus terdiri atas 16 angka.";
                        }
                        break;

                    case TipeKolomTemplate.Angka:
                        if (!decimal.TryParse(isi, NumberStyles.Any, CultureInfo.InvariantCulture, out _) &&
                            !decimal.TryParse(isi, NumberStyles.Any, Budaya, out _))
                        {
                            kesalahan[kolom.Kunci] = $"{Judul(kolom)} harus berupa angka.";
                        }
                        break;

                    case TipeKolomTemplate.Tanggal:
                        if (!TryParseTanggal(isi, out _))
                        {
                            kesalahan[kolom.Kunci] = $"{Judul(kolom)} harus berupa tanggal (contoh: 17-08-2026).";
                        }
                        break;

                    case TipeKolomTemplate.Pilihan:
                        if (kolom.Pilihan != null && kolom.Pilihan.Count > 0 &&
                            !kolom.Pilihan.Any(p => string.Equals(p, isi, StringComparison.OrdinalIgnoreCase)))
                        {
                            kesalahan[kolom.Kunci] = $"{Judul(kolom)} harus dipilih dari daftar yang tersedia.";
                        }
                        break;
                }
            }

            return kesalahan;
        }

        /// <summary>Benar bila seluruh kolom template sudah lolos pemeriksaan.</summary>
        public static bool Valid(TemplateSuratKustom? template, IReadOnlyDictionary<string, string>? nilai) =>
            Validasi(template, nilai).Count == 0;

        /// <summary>Pesan ringkas kesalahan pengisian untuk ditampilkan ke pengguna.</summary>
        public static string RingkasKesalahan(IReadOnlyDictionary<string, string> kesalahan)
        {
            if (kesalahan == null || kesalahan.Count == 0) return string.Empty;
            return string.Join(Environment.NewLine, kesalahan.Values);
        }

        /// <summary>
        /// Nilai contoh untuk pratinjau (wizard) — dipakai supaya surat tidak
        /// tampil kosong saat dilihat sebelum pengguna mengisi datum sebenarnya.
        /// </summary>
        public static Dictionary<string, string> NilaiContoh(TemplateSuratKustom? template)
        {
            var hasil = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (template?.Kolom == null) return hasil;

            foreach (var kolom in template.Kolom)
            {
                if (string.IsNullOrWhiteSpace(kolom.Kunci)) continue;
                hasil[kolom.Kunci] = string.IsNullOrWhiteSpace(kolom.NilaiBawaan)
                    ? TemplateSuratKustom.ContohNilai(kolom)
                    : kolom.NilaiBawaan;
            }

            return hasil;
        }

        /// <summary>Ambil nilai dari kamus isian (tidak peka huruf besar/kecil).</summary>
        public static string Ambil(IReadOnlyDictionary<string, string>? nilai, string? kunci)
        {
            if (nilai == null || string.IsNullOrWhiteSpace(kunci)) return string.Empty;

            if (nilai.TryGetValue(kunci, out var langsung)) return Rapikan(langsung);

            foreach (var pasangan in nilai)
            {
                if (string.Equals(pasangan.Key, kunci, StringComparison.OrdinalIgnoreCase))
                {
                    return Rapikan(pasangan.Value);
                }
            }

            return string.Empty;
        }

        /// <summary>Judul kolom untuk pesan kesalahan; label kosong memakai \"Kolom\".</summary>
        private static string Judul(KolomTemplateSurat kolom) =>
            string.IsNullOrWhiteSpace(kolom.Label) ? "Kolom" : kolom.Label.Trim();

        /// <summary>Benar bila teks dapat dibaca sebagai tanggal.</summary>
        public static bool TryParseTanggal(string? teks, out DateTime tanggal)
        {
            tanggal = default;
            string isi = Rapikan(teks);
            if (isi.Length == 0) return false;

            string[] pola = { "dd-MM-yyyy", "dd/MM/yyyy", "d-M-yyyy", "d/M/yyyy", "yyyy-MM-dd" };
            foreach (var p in pola)
            {
                if (DateTime.TryParseExact(isi, p, CultureInfo.InvariantCulture, DateTimeStyles.None, out tanggal))
                {
                    return true;
                }
            }

            return DateTime.TryParse(isi, Budaya, DateTimeStyles.None, out tanggal);
        }
    }
}
