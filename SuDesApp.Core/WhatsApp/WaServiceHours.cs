using System;
using System.Collections.Generic;
using System.Linq;

namespace SuDesApp.WhatsApp
{
    /// <summary>
    /// Jam layanan otomatis WhatsApp. Rentang disimpan sebagai dua string
    /// "HH:mm" (jam buka &amp; jam tutup); bila salah satu kosong/tidak valid
    /// layanan dianggap buka 24 jam. Mendukung rentang lintas tengah malam
    /// (mis. buka 20:00, tutup 06:00). Hari layanan disimpan sebagai daftar
    /// indeks DayOfWeek (Minggu=0, Senin=1, … Sabtu=6) dipisah koma;
    /// kosong = setiap hari.
    /// </summary>
    public static class WaServiceHours
    {
        private static readonly string[] NamaHari =
            { "Minggu", "Senin", "Selasa", "Rabu", "Kamis", "Jumat", "Sabtu" };

        /// <summary>Urutan hari untuk kompresi tampilan: Senin dulu, Minggu terakhir.</summary>
        private static readonly int[] UrutanHari = { 1, 2, 3, 4, 5, 6, 0 };

        /// <summary>
        /// Parse daftar hari (mis. "1,2,3,4,5"). Null/kosong/tidak valid =
        /// setiap hari (kembalikan null). Nilai di luar 0–6 diabaikan.
        /// </summary>
        public static HashSet<int>? ParseDays(string? daysStr)
        {
            if (string.IsNullOrWhiteSpace(daysStr)) return null;
            var set = new HashSet<int>();
            foreach (var part in daysStr.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(part.Trim(), out var d) && d >= 0 && d <= 6)
                    set.Add(d);
            }
            return set.Count == 0 ? null : set; // kosong = semua hari
        }

        /// <summary>Apakah hari ini termasuk hari layanan (days null = setiap hari).</summary>
        public static bool HariLayanan(DateTime now, string? daysStr)
        {
            var days = ParseDays(daysStr);
            return days == null || days.Contains((int)now.DayOfWeek);
        }
        /// <summary>Apakah layanan buka pada waktu yang diberikan.</summary>
        public static bool IsOpen(DateTime now, string? openStr, string? closeStr, string? daysStr = null)
        {
            if (!HariLayanan(now, daysStr)) return false;

            var open = ParseHm(openStr);
            var close = ParseHm(closeStr);
            if (open == null || close == null) return true; // tidak dikonfigurasi = 24 jam

            var t = now.TimeOfDay;
            if (open == close) return true; // buka = tutup dianggap 24 jam

            if (open < close)
            {
                // Rentang normal, mis. 08:00–16:00.
                return t >= open.Value && t < close.Value;
            }

            // Rentang lintas tengah malam, mis. 20:00–06:00.
            return t >= open.Value || t < close.Value;
        }

        /// <summary>
        /// Menit dari sekarang sampai layanan buka kembali (mempertimbangkan
        /// hari layanan), atau null bila layanan 24 jam penuh.
        /// </summary>
        public static int? MenitSampaiBuka(DateTime now, string? openStr, string? closeStr, string? daysStr = null)
        {
            var open = ParseHm(openStr);
            var close = ParseHm(closeStr);
            var days = ParseDays(daysStr);

            // Benar-benar 24 jam: tanpa batas jam dan tanpa batas hari.
            if ((open == null || close == null || open == close) && days == null) return null;
            if (IsOpen(now, openStr, closeStr, daysStr)) return null;

            // Jam buka efektif: jam yang dikonfigurasi, atau tengah malam bila
            // hanya hari yang membatasi.
            var jamBuka = open ?? new TimeSpan(0, 0, 0);

            // Cari buka berikutnya: periksa maksimal 8 hari ke depan.
            for (int offset = 0; offset <= 8; offset++)
            {
                var tanggal = now.Date.AddDays(offset);
                if (days != null && !days.Contains((int)tanggal.DayOfWeek)) continue;

                var kandidat = tanggal + jamBuka;
                if (kandidat > now)
                    return (int)Math.Ceiling((kandidat - now).TotalMinutes);
            }
            return 24 * 60; // fallback aman
        }

        /// <summary>Teks rentang jam + hari yang sedang berlaku, mis. "Senin–Jumat, 08:00–16:00".</summary>
        public static string RentangTampil(string? openStr, string? closeStr, string? daysStr = null)
        {
            var hari = FormatDaysTampil(daysStr);
            var open = ParseHm(openStr);
            var close = ParseHm(closeStr);
            var jam = open == null || close == null || open == close
                ? "24 jam"
                : $"{open:hh\\:mm}–{close:hh\\:mm}";

            if (hari == "setiap hari" && jam == "24 jam") return "24 jam";
            if (hari == "setiap hari") return jam;
            return $"{hari}, {jam}";
        }

        /// <summary>Teks daftar hari layanan, dengan kompresi rentang berurutan (Senin–Jumat).</summary>
        public static string FormatDaysTampil(string? daysStr)
        {
            var days = ParseDays(daysStr);
            if (days == null || days.Count == 7) return "setiap hari";

            var urut = UrutanHari.Where(days.Contains).ToList();
            var bagian = new List<string>();
            int i = 0;
            while (i < urut.Count)
            {
                int j = i;
                while (j + 1 < urut.Count && (urut[j + 1] - urut[j]) == 1) j++;

                if (j == i) bagian.Add(NamaHari[urut[i]]);
                else if (j == i + 1) bagian.Add($"{NamaHari[urut[i]]}, {NamaHari[urut[j]]}");
                else bagian.Add($"{NamaHari[urut[i]]}–{NamaHari[urut[j]]}");
                i = j + 1;
            }
            return string.Join(", ", bagian);
        }

        private static TimeSpan? ParseHm(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var parts = s.Trim().Split(':');
            if (parts.Length != 2) return null;
            if (!int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m)) return null;
            if (h < 0 || h > 23 || m < 0 || m > 59) return null;
            return new TimeSpan(h, m, 0);
        }
    }
}
