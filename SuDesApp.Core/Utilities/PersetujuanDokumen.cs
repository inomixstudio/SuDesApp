using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace SuDesApp.Utilities
{
    /// <summary>Satu dokumen hukum bawaan aplikasi yang perlu disetujui pengguna.</summary>
    /// <param name="Kunci">Kunci stabil untuk preferensi — jangan diubah setelah rilis.</param>
    /// <param name="Judul">Nama dokumen seperti tampil di dialog dan halaman Tentang.</param>
    /// <param name="BerkasRelatif">Nama berkas di folder <c>docs</c>.</param>
    public sealed record DokumenHukum(string Kunci, string Judul, string BerkasRelatif);

    /// <summary>
    /// Versi yang dibaca dari kepala dokumen Markdown, mis. baris
    /// <c>&gt; Status dokumen: **berlaku sejak 19 Oktober 2026 · Versi 1.0**</c>.
    /// Tanggal berlaku disimpan sebagai teks apa adanya supaya tampil sama dengan
    /// yang tertulis di dokumen, bukan hasil format ulang yang bisa menyimpang.
    /// </summary>
    public sealed record VersiDokumen(string Versi, string TanggalBerlaku);

    /// <summary>Satu persetujuan yang sudah tercatat di komputer ini.</summary>
    /// <param name="Versi">Versi dokumen yang disetujui saat itu.</param>
    /// <param name="TanggalBerlaku">Tanggal berlaku dokumen saat disetujui.</param>
    /// <param name="DisetujuiPada">Waktu persetujuan (waktu lokal komputer).</param>
    public sealed record PersetujuanTercatat(string Versi, string TanggalBerlaku, DateTime DisetujuiPada);

    /// <summary>
    /// Persetujuan Privacy Policy dan Terms of Service: membaca versi dari
    /// dokumen yang benar-benar terpasang, mencatat persetujuan di preferensi
    /// aplikasi, dan menentukan kapan persetujuan perlu diminta lagi.
    ///
    /// Kenapa versi dibaca dari berkas Markdown, bukan konstanta di kode:
    /// dokumen adalah sumber kebenaran yang dibaca pengguna. Bila isinya
    /// diperbarui dan versinya dinaikkan, aplikasi otomatis meminta
    /// persetujuan ulang tanpa ada dua angka versi yang bisa saling
    /// menyimpang. Bila berkas tidak ditemukan, versi dianggap tidak terbaca —
    /// aplikasi TIDAK dikunci karena itu (lihat <see cref="PerluPersetujuan"/>).
    ///
    /// Penyimpanan memakai <see cref="AppPreferenceStore"/> (bukan database)
    /// karena persetujuan terjadi SEBELUM login dan melekat pada instalasi:
    /// komputer yang dipakai bergantian oleh beberapa akun desa tetap
    /// menunjukkan persetujuan yang sama.
    /// </summary>
    public static class PersetujuanDokumen
    {
        public const string KunciPrivasi = "privasi";
        public const string KunciKetentuan = "ketentuan";

        /// <summary>Dokumen yang wajib disetujui, dalam urutan tampil.</summary>
        public static IReadOnlyList<DokumenHukum> Daftar { get; } = new[]
        {
            new DokumenHukum(KunciPrivasi, "Kebijakan Privasi", "kebijakan-privasi.md"),
            new DokumenHukum(KunciKetentuan, "Syarat dan Ketentuan", "syarat-ketentuan.md"),
        };

        /// <summary>
        /// Hanya kepala dokumen yang dipindai. Baris isi bisa menyebut kata
        /// "Versi" dalam konteks lain (mis. "Versi aplikasi"), jadi pencarian
        /// seluruh berkas justru bisa mengambil angka yang salah.
        /// </summary>
        private const int BarisKepala = 15;

        private static readonly Regex PolaVersi = new(
            @"\bVersi\s+(?<versi>\d+(?:\.\d+)*)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex PolaBerlaku = new(
            @"berlaku\s+sejak\s+(?<tanggal>\d{1,2}\s+[A-Za-z\u00C0-\u024F]+\s+\d{4})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly CultureInfo Budaya = CultureInfo.GetCultureInfo("id-ID");

        /// <summary>
        /// Baca versi dari kepala Markdown. Null bila tidak ada baris versi —
        /// dokumen tanpa versi tidak boleh membuat pengguna terkunci, jadi
        /// pemanggil memperlakukan null sebagai "tidak terbaca".
        /// </summary>
        public static VersiDokumen? BacaVersi(string? markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown))
            {
                return null;
            }

            string kepala = string.Join("\n",
                markdown!.Replace("\r\n", "\n").Replace('\r', '\n')
                    .Split('\n')
                    .Take(BarisKepala));

            var versi = PolaVersi.Match(kepala);
            if (!versi.Success)
            {
                return null;
            }

            var berlaku = PolaBerlaku.Match(kepala);
            return new VersiDokumen(
                versi.Groups["versi"].Value,
                berlaku.Success ? berlaku.Groups["tanggal"].Value : string.Empty);
        }

        /// <summary>Kunci preferensi satu bidang untuk satu dokumen.</summary>
        public static string KunciPreferensi(DokumenHukum dokumen, string bidang) =>
            $"persetujuan_{dokumen.Kunci}_{bidang}";

        /// <summary>Persetujuan yang tercatat untuk dokumen ini; null bila belum pernah.</summary>
        public static PersetujuanTercatat? BacaPersetujuan(DokumenHukum dokumen)
        {
            var versi = AppPreferenceStore.GetString(KunciPreferensi(dokumen, "versi"), null);
            if (string.IsNullOrWhiteSpace(versi))
            {
                return null;
            }

            var tanggal = AppPreferenceStore.GetString(KunciPreferensi(dokumen, "berlaku"), null) ?? string.Empty;
            var waktu = AppPreferenceStore.GetString(KunciPreferensi(dokumen, "disetujui"), null);

            if (!DateTime.TryParse(waktu, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var disetujui))
            {
                // Versi tercatat tetapi waktunya rusak: perlakukan sebagai belum
                // disetujui agar dialog muncul sekali lagi, bukan menampilkan
                // tanggal karangan.
                return null;
            }

            return new PersetujuanTercatat(versi!.Trim(), tanggal, disetujui.ToLocalTime());
        }

        /// <summary>
        /// Catat persetujuan satu dokumen. Waktu disimpan UTC ber-ISO-8601 dan
        /// dibaca kembali sebagai waktu lokal. Kegagalan tulis tidak dilempar —
        /// pemanggil yang memverifikasi hasilnya (lihat layanan WPF).
        /// </summary>
        public static void Catat(DokumenHukum dokumen, VersiDokumen versi, DateTime waktu)
        {
            AppPreferenceStore.SetString(KunciPreferensi(dokumen, "versi"), versi.Versi);
            AppPreferenceStore.SetString(KunciPreferensi(dokumen, "berlaku"), versi.TanggalBerlaku);
            AppPreferenceStore.SetString(KunciPreferensi(dokumen, "disetujui"),
                waktu.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// True bila dokumen ini perlu disetujui (belum pernah, atau versinya
        /// berubah). Versi yang tidak terbaca (berkas hilang) TIDAK memicu
        /// permintaan ulang: aplikasi tidak boleh menahan pengguna karena
        /// dokumen yang gagal dibaca.
        /// </summary>
        public static bool PerluPersetujuan(DokumenHukum dokumen, VersiDokumen? versiSaatIni)
        {
            var tercatat = BacaPersetujuan(dokumen);
            if (tercatat is null)
            {
                return true;
            }

            if (versiSaatIni is null)
            {
                return false;
            }

            return !string.Equals(tercatat.Versi, versiSaatIni.Versi, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Waktu persetujuan dalam bentuk yang mudah dibaca.</summary>
        public static string WaktuTampil(DateTime waktu) =>
            waktu.ToString("d MMMM yyyy, HH.mm", Budaya);
    }
}
