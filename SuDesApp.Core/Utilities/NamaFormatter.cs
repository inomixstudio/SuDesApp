// NamaFormatter.cs
// Penulisan nama pada dokumen: hanya NAMA-nya yang dicetak KAPITAL, sedangkan
// gelar dibiarkan apa adanya. Contoh:
//   "Dr. H. Ahmad Suryana, S.H., M.M."  ->  "Dr. H. AHMAD SURYANA, S.H., M.M."
//   "Siti Aminah, S.Pd."                ->  "SITI AMINAH, S.Pd."
//   "Muhammad Rizky Ramadhan"           ->  "MUHAMMAD RIZKY RAMADHAN"
using System;
using System.Collections.Generic;
using System.Linq;

namespace SuDesApp.Utilities
{
    /// <summary>Penulisan nama orang untuk surat: nama pokok kapital, gelar tetap.</summary>
    public static class NamaFormatter
    {
        /// <summary>
        /// Gelar/honorifik yang ditulis SEBELUM nama (tetap apa adanya):
        /// gelar akademik, keagamaan, kebangsawanan, dan kerohanian.
        /// </summary>
        private static readonly HashSet<string> GelarDepan = new(StringComparer.OrdinalIgnoreCase)
        {
            // akademik
            "prof", "profesor", "dr", "drg", "drs", "dra", "ir",
            // keagamaan & kehormatan
            "h", "hj", "haji", "hajjah", "kh", "kyai", "ust", "ustad", "ustadz", "ustadzah",
            // kebangsawanan
            "r", "ra", "rd", "rt", "rm", "rr", "rng", "kr", "kp", "bra", "ba", "aa", "mas", "mb",
        };

        /// <summary>
        /// Gelar yang ditulis SESUDAH nama tanpa koma, mis. "Ahmad Suryana S.H.".
        /// Bentuk bertitik dan bentuk singkatan (SE, MM, S.Pd) ikut dikenali.
        /// </summary>
        private static readonly HashSet<string> GelarBelakang = new(StringComparer.OrdinalIgnoreCase)
        {
            "se", "sh", "st", "sp", "ssi", "ssos", "spd", "sag", "si", "skom", "sfarm", "sked", "skep",
            "spsi", "stp", "sip", "shut", "skel", "spt", "sgz", "sikom", "sm", "snm", "skm", "sos",
            "mm", "mh", "msi", "mkom", "mpd", "mag", "mt", "mst", "mkeu", "mkes", "mfarm", "mpsi",
            "mmp", "mmpd", "msos", "mkn", "mip", "mtp", "mhut", "mkes", "mes", "me", "ma", "mphil", "phd",
            "amd", "apt", "ns", "bd", "nrs", "ners", "prof", "dr", "drs", "dra", "ir", "drg",
        };

        /// <summary>
        /// Nama orang dengan bagian namanya saja yang dijadikan kapital.
        /// Gelar depan (Dr., Hj., Ir., …) dan gelar belakang (S.H., M.M., …) tetap
        /// seperti penulisannya semula.
        /// </summary>
        public static string ToUpperNama(string? nama)
        {
            if (string.IsNullOrWhiteSpace(nama))
            {
                return nama ?? string.Empty;
            }

            var bagianKoma = nama.Split(',');
            string depan = bagianKoma[0].Trim();

            // Gelar belakang setelah koma: apa adanya, hanya dirapikan spasinya.
            var gelarSetelahKoma = bagianKoma
                .Skip(1)
                .Select(b => b.Trim())
                .Where(b => b.Length > 0)
                .ToList();

            var kata = depan.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (kata.Count == 0)
            {
                return nama;
            }

            // 1. Gelar depan: sisakan minimal satu kata sebagai nama.
            int awalNama = 0;
            while (awalNama < kata.Count - 1 && IsGelarDepan(kata[awalNama]))
            {
                awalNama++;
            }

            // 2. Gelar belakang tanpa koma: telusuri dari ujung belakang kata.
            int akhirNama = kata.Count;
            while (akhirNama - 1 > awalNama && IsGelarBelakang(kata[akhirNama - 1], akhirNama - 1 - awalNama))
            {
                akhirNama--;
            }

            var hasil = new List<string>();
            hasil.AddRange(kata.Take(awalNama));
            hasil.Add(string.Join(" ", kata.Skip(awalNama).Take(akhirNama - awalNama)).ToUpperInvariant());
            hasil.AddRange(kata.Skip(akhirNama));

            string namaKapital = string.Join(" ", hasil);
            return gelarSetelahKoma.Count > 0
                ? $"{namaKapital}, {string.Join(", ", gelarSetelahKoma)}"
                : namaKapital;
        }

        private static bool IsGelarDepan(string kata)
            => GelarDepan.Contains(Bersihkan(kata)) || GelarDepan.Contains(kata);

        private static bool IsGelarBelakang(string kata, int posisiRelatif)
        {
            string bersih = Bersihkan(kata);
            if (bersih.Length == 0)
            {
                return false;
            }

            if (GelarBelakang.Contains(bersih))
            {
                return true;
            }

            // Bentuk bertitik ("S.H.", "M.M.") sudah pasti gelar.
            if (kata.Contains('.'))
            {
                return true;
            }

            // Singkatan KAPITAL pendek setelah minimal dua kata nama, mis. "SH".
            // Aman karena bentuk yang sudah kapital tidak berubah tampilannya.
            return posisiRelatif >= 2
                && bersih.Length <= 5
                && bersih.All(c => !char.IsLetter(c) || char.IsUpper(c))
                && bersih.Any(char.IsLetter);
        }

        /// <summary>Buang titik dan spasi, mis. "S.H." → "sh".</summary>
        private static string Bersihkan(string kata)
            => kata.Replace(".", string.Empty).Replace(" ", string.Empty);
    }
}
