using System.Collections.Generic;
using System.Linq;

namespace SuDesApp.Data.Models
{
    /// <summary>
    /// Kumpulan kolom isian siap pakai untuk template surat buatan pengguna:
    /// kolom identitas warga yang paling sering dipakai surat desa (NIK, Nama,
    /// tempat/tanggal lahir, dan lainnya) beserta contoh blok teks baku.
    ///
    /// Dipakai wizard Template Surat pada langkah "Kolom Isian" (tombol
    /// "Tambah kolom umum") supaya pengguna tidak perlu mengetik satu per satu.
    /// </summary>
    public static class TemplateSuratPreset
    {
        /// <summary>Satu preset kolom: label yang tercetak dan tipenya.</summary>
        public readonly record struct KolomPreset(string Label, TipeKolomTemplate Tipe, bool Wajib, string[]? Pilihan = null);

        /// <summary>Kolom identitas warga yang paling sering dipakai.</summary>
        public static readonly KolomPreset[] KolomUmum =
        {
            new("NIK", TipeKolomTemplate.Nik, true),
            new("Nama", TipeKolomTemplate.Teks, true),
            new("Tempat Lahir", TipeKolomTemplate.Teks, false),
            new("Tanggal Lahir", TipeKolomTemplate.Tanggal, false),
            new("Jenis Kelamin", TipeKolomTemplate.Pilihan, false, new[] { "Laki-laki", "Perempuan" }),
            new("Agama", TipeKolomTemplate.Pilihan, false,
                new[] { "Islam", "Kristen", "Katolik", "Hindu", "Buddha", "Konghucu" }),
            new("Pekerjaan", TipeKolomTemplate.Teks, false),
            new("Status Perkawinan", TipeKolomTemplate.Pilihan, false,
                new[] { "Belum Kawin", "Kawin", "Cerai Hidup", "Cerai Mati" }),
            new("Kewarganegaraan", TipeKolomTemplate.Pilihan, false, new[] { "WNI", "WNA" }),
            new("Alamat", TipeKolomTemplate.Paragraf, true),
            new("Keperluan", TipeKolomTemplate.Paragraf, true),
            new("Keterangan", TipeKolomTemplate.Paragraf, false)
        };

        /// <summary>Ada kolom umum dengan label tertentu pada definisi template.</summary>
        public static bool SudahAda(TemplateSuratKustom? template, string label) =>
            template?.Kolom != null &&
            template.Kolom.Any(k => string.Equals(k.Label, label, System.StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Buat kolom dari preset, dengan kunci unik yang belum dipakai template.
        /// </summary>
        public static KolomTemplateSurat BuatKolom(KolomPreset preset, IEnumerable<string> kunciTerpakai)
        {
            var kolom = new KolomTemplateSurat
            {
                Label = preset.Label,
                Tipe = preset.Tipe,
                Wajib = preset.Wajib,
                Pilihan = preset.Pilihan == null ? new List<string>() : new List<string>(preset.Pilihan)
            };
            kolom.Kunci = TemplateSuratKunci.Unik(preset.Label, kunciTerpakai);
            return kolom;
        }

        /// <summary>Contoh blok teks yang sering dipakai surat keterangan desa.</summary>
        public static readonly (string Isi, RataBlokTemplate Rata, bool Miring)[] BlokUmum =
        {
            ("Yang bertanda tangan di bawah ini menerangkan bahwa:", RataBlokTemplate.Kiri, false),
            ("Demikian surat keterangan ini dibuat untuk dipergunakan sebagaimana mestinya.", RataBlokTemplate.Justify, false)
        };

        /// <summary>Satu preset field data diri seseorang pada blok Data Diri.</summary>
        public readonly record struct FieldDataDiri(string Label, TipeKolomTemplate Tipe, bool Wajib, string[]? Pilihan = null);

        /// <summary>
        /// Field baku data diri seseorang: lengkap (NIK, nama, … alamat) atau ringkas
        /// (nama &amp; jabatan). Wan wizard "Tambahkan Data Diri".
        /// </summary>
        public static class DataDiri
        {
            /// <summary>Seluruh field data diri standar.</summary>
            public static readonly FieldDataDiri[] Lengkap =
            {
                new("NIK", TipeKolomTemplate.Nik, true),
                new("Nama", TipeKolomTemplate.Teks, true),
                new("Jabatan", TipeKolomTemplate.Teks, false),
                new("Tempat Lahir", TipeKolomTemplate.Teks, false),
                new("Tanggal Lahir", TipeKolomTemplate.Tanggal, false),
                new("Jenis Kelamin", TipeKolomTemplate.Pilihan, false, new[] { "Laki-laki", "Perempuan" }),
                new("Agama", TipeKolomTemplate.Pilihan, false,
                    new[] { "Islam", "Kristen", "Katolik", "Hindu", "Buddha", "Konghucu" }),
                new("Pekerjaan", TipeKolomTemplate.Teks, false),
                new("Status Perkawinan", TipeKolomTemplate.Pilihan, false,
                    new[] { "Belum Kawin", "Kawin", "Cerai Hidup", "Cerai Mati" }),
                new("Kewarganegaraan", TipeKolomTemplate.Pilihan, false, new[] { "WNI", "WNA" }),
                new("Alamat", TipeKolomTemplate.Paragraf, true)
            };

            /// <summary>Hanya nama &amp; jabatan (untuk penandatangan atau panggilan singkat).</summary>
            public static readonly FieldDataDiri[] NamaJabatan =
            {
                new("Nama", TipeKolomTemplate.Teks, true),
                new("Jabatan", TipeKolomTemplate.Teks, true)
            };
        }
    }

    /// <summary>Pembuat kunci teknis unik dari label kolom.</summary>
    public static class TemplateSuratKunci
    {
        /// <summary>
        /// Ubah label menjadi kunci teknis (huruf kecil, tanpa spasi/tanda baca),
        /// lalu tambahkan angka bila kuncinya sudah terpakai.
        /// </summary>
        public static string Unik(string? label, IEnumerable<string>? terpakai)
        {
            string dasar = Slug(label);
            if (dasar.Length == 0) dasar = "kolom";

            var dipakai = terpakai == null
                ? new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(terpakai, System.StringComparer.OrdinalIgnoreCase);
            if (!dipakai.Contains(dasar)) return dasar;

            for (int i = 2; i < 1000; i++)
            {
                string kandidat = dasar + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!dipakai.Contains(kandidat)) return kandidat;
            }
            return dasar + "X";
        }

        /// <summary>Slug huruf kecil dari label: "Tempat Lahir" → "tempat_lahir".</summary>
        public static string Slug(string? label)
        {
            var teks = (label ?? string.Empty).Trim().ToLowerInvariant();
            var hasil = new System.Text.StringBuilder(teks.Length);
            foreach (char c in teks)
            {
                if (char.IsLetterOrDigit(c)) hasil.Append(c);
                else if (hasil.Length > 0 && hasil[^1] != '_') hasil.Append('_');
            }
            return hasil.ToString().Trim('_');
        }
    }
}
