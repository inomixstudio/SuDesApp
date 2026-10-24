using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SuDesApp.Wilayah
{
    /// <summary>
    /// Rantai wilayah lengkap dari provinsi sampai desa/kelurahan — satu baris
    /// pilihan pengguna pada Pengaturan Surat.
    /// </summary>
    public sealed record RantaiWilayah(
        WilayahDaerah Provinsi,
        WilayahDaerah Kabupaten,
        WilayahDaerah Kecamatan,
        WilayahDaerah Desa);

    /// <summary>Hasil pencocokan nama wilayah tersimpan terhadap data terbundel.</summary>
    public enum StatusPencocokanWilayah
    {
        /// <summary>Tidak ada data wilayah terbundel (berkas hilang/rusak).</summary>
        TanpaData = 0,

        /// <summary>Tidak ada rantai yang cocok — nama tersimpan di luar data wilayah.</summary>
        TidakDitemukan = 1,

        /// <summary>Lebih dari satu rantai cocok, jadi tidak boleh dipilih otomatis.</summary>
        Ambigu = 2,

        /// <summary>Tepat satu rantai cocok dan aman dipakai.</summary>
        Ditemukan = 3
    }

    /// <summary>Hasil pencarian rantai wilayah beserta alasan bila tidak tunggal.</summary>
    public sealed record HasilPencocokanWilayah(
        StatusPencocokanWilayah Status,
        RantaiWilayah? Rantai,
        int JumlahKandidat);

    /// <summary>
    /// Pencocokan nama wilayah antara isian pengguna dengan data terbundel.
    ///
    /// Isian desa sudah lebih dulu ada sebagai teks bebas (mis. "Kabupaten Karawang"
    /// atau "KECAMATAN TEMPURAN"), jadi pemasangan yang sudah jalan perlu dikenali
    /// kembali saat halaman Pengaturan dibuka. Pencocokan di sini sengaja longgar:
    /// huruf besar-kecil, tanda baca, spasi berlebih, dan awalan tingkat diabaikan —
    /// tetapi hasilnya hanya dipakai bila tunggal, karena menebak wilayah yang salah
    /// berarti salah pada semua surat berikutnya.
    /// </summary>
    public static class WilayahPencocokan
    {
        /// <summary>
        /// Awalan tingkat yang dibuang saat membandingkan nama wilayah, terpanjang
        /// lebih dulu: "desa adat" harus diperiksa sebelum "desa", dan "kabupaten"
        /// sebelum "kab", supaya nama yang tersisa tidak terpotong tanggung.
        /// </summary>
        private static readonly string[] AwalanTingkat =
        {
            "desa adat", "provinsi", "propinsi", "kabupaten", "kecamatan", "kelurahan",
            "kotamadya", "kampung", "gampong", "nagari", "kab", "kec", "kel", "kota", "desa"
        };

        /// <summary>
        /// Bentuk pembanding nama wilayah: huruf kecil, tanpa tanda baca, tanpa
        /// spasi ganda, dan tanpa awalan tingkat ("Kabupaten", "Kec.", …).
        /// </summary>
        public static string Normalisasi(string? nama)
        {
            string teks = (nama ?? string.Empty).Trim();
            if (teks.Length == 0)
            {
                return string.Empty;
            }

            var bersih = new StringBuilder(teks.Length);
            foreach (char c in teks)
            {
                bersih.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
            }

            string hasil = RingkasSpasi(bersih.ToString());

            // Awalan tingkat bisa berlapis ("KABUPATEN KOTA …" tidak lazim, tetapi
            // "Kab. Karawang" dan "Kecamatan Tempuran" keduanya nyata di isian lama).
            bool berubah = true;
            while (berubah)
            {
                berubah = false;
                foreach (string awalan in AwalanTingkat)
                {
                    if (hasil.Length > awalan.Length + 1 &&
                        hasil.StartsWith(awalan + " ", StringComparison.Ordinal))
                    {
                        hasil = hasil[(awalan.Length + 1)..].Trim();
                        berubah = true;
                        break;
                    }
                }
            }

            return hasil;
        }

        /// <summary>
        /// Cari rantai wilayah yang cocok dengan nama desa, kecamatan, dan kabupaten
        /// yang tersimpan. Kecamatan dan desa dipakai untuk memisahkan nama kembar
        /// (mis. Kabupaten dan Kota Bandung sama-sama punya kecamatan Andir).
        /// </summary>
        public static HasilPencocokanWilayah CariRantai(
            WilayahDataStore data,
            string? namaDesa,
            string? namaKecamatan,
            string? namaKabupaten)
        {
            if (!data.AdaData)
            {
                return new HasilPencocokanWilayah(StatusPencocokanWilayah.TanpaData, null, 0);
            }

            string desa = Normalisasi(namaDesa);
            string kecamatan = Normalisasi(namaKecamatan);
            string kabupaten = Normalisasi(namaKabupaten);
            if (kabupaten.Length == 0 || kecamatan.Length == 0 || desa.Length == 0)
            {
                // Rantai hanya berguna bila ketiga tingkat terisi: tanpa nama desa,
                // pilihan Kabupaten/Kecamatan saja sudah bisa diketik ulang pengguna.
                return new HasilPencocokanWilayah(StatusPencocokanWilayah.TidakDitemukan, null, 0);
            }

            var kandidat = new List<RantaiWilayah>();
            foreach (var kab in data.CariBanyak(WilayahJenis.Kabupaten, kabupaten)
                         .Concat(data.CariBanyak(WilayahJenis.Kota, kabupaten)))
            {
                var provinsi = data.Ambil(kab.Induk);
                if (provinsi == null)
                {
                    continue;
                }

                foreach (var kec in data.DaftarAnak(kab.Kode))
                {
                    if (!string.Equals(Normalisasi(kec.Nama), kecamatan, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    foreach (var d in data.DaftarAnak(kec.Kode))
                    {
                        if (string.Equals(Normalisasi(d.Nama), desa, StringComparison.Ordinal))
                        {
                            kandidat.Add(new RantaiWilayah(provinsi, kab, kec, d));
                        }
                    }
                }
            }

            if (kandidat.Count == 1)
            {
                return new HasilPencocokanWilayah(StatusPencocokanWilayah.Ditemukan, kandidat[0], 1);
            }

            return kandidat.Count == 0
                ? new HasilPencocokanWilayah(StatusPencocokanWilayah.TidakDitemukan, null, 0)
                : new HasilPencocokanWilayah(StatusPencocokanWilayah.Ambigu, null, kandidat.Count);
        }

        private static string RingkasSpasi(string teks)
        {
            var hasil = new StringBuilder(teks.Length);
            bool spasiSebelumnya = false;
            foreach (char c in teks)
            {
                if (c == ' ')
                {
                    if (spasiSebelumnya)
                    {
                        continue;
                    }

                    spasiSebelumnya = true;
                }
                else
                {
                    spasiSebelumnya = false;
                }

                hasil.Append(c);
            }

            return hasil.ToString().Trim();
        }
    }
}
