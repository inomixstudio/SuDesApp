using System;
using System.Collections.Generic;
using SuDesApp.Data.Models;

namespace SuDesApp.GeneratorPdf
{
    /// <summary>
    /// Isi kop surat desa (lima baris + ukuran fontnya). Dipakai bersama oleh
    /// SELURUH generator — surat keterangan, daftar hadir, dan rekening koran —
    /// supaya tampilan kop tidak pernah berbeda antar dokumen.
    ///
    /// Urutannya:
    ///   PEMERINTAH KABUPATEN ...
    ///   KECAMATAN ...
    ///   PEMERINTAH DESA ...
    ///   Alamat ... Desa ... Kec. ... Kodepos ...
    ///   NAMA KABUPATEN
    /// </summary>
    public static class KopSurat
    {
        /// <summary>Lebar kotak logo di kop (point), rasio gambar dipertahankan.</summary>
        public const float LebarLogo = 60f;

        // Ukuran font tiap baris kop (point).
        public const float FontKabupaten = 16f;
        public const float FontKecamatan = 18f;
        public const float FontNamaDesa = 22f;
        public const float FontAlamat = 9f;
        public const float FontKabupatenBawah = 9f;

        /// <summary>Ketebalan garis ganda pemisah kop (point), berurutan dari atas.</summary>
        public const float GarisTipis = 0.8f;
        public const float GarisTebal = 1.5f;

        /// <summary>Satu baris kop: teks, ukuran font, dan apakah dicetak tebal.</summary>
        public readonly record struct Baris(string Teks, float FontSize, bool Tebal);

        /// <summary>
        /// Susun kelima baris kop dari data desa.
        /// </summary>
        /// <param name="desa">Data desa; nilai kosong dicetak sebagai penanda kurung siku.</param>
        /// <param name="skalaJudul">
        /// Pengali untuk tiga baris judul saja (bukan baris alamat/kabupaten).
        /// Dipakai dokumen landscape yang perlu kop lebih pendek. Nilai 1 = ukuran baku.
        /// </param>
        public static IReadOnlyList<Baris> BarisKop(DesaData? desa, float skalaJudul = 1f)
        {
            desa ??= new DesaData();

            string kabupaten = NamaWilayah(desa.Kabupaten, "Kabupaten", "Kab.");
            string kecamatan = NamaWilayah(desa.Kecamatan, "Kecamatan", "Kec.");
            string namaDesa = NamaWilayah(desa.NamaDesa, "Desa");
            if (kabupaten.Length == 0) kabupaten = "[KABUPATEN]";
            if (kecamatan.Length == 0) kecamatan = "[KECAMATAN]";
            if (namaDesa.Length == 0) namaDesa = "[NAMA DESA]";

            string alamat = (desa.Alamat ?? string.Empty).Trim();
            if (alamat.Length == 0) alamat = "[Alamat Kantor Desa]";
            string kodepos = (desa.Kodepos ?? string.Empty).Trim();

            return new List<Baris>
            {
                new($"PEMERINTAH KABUPATEN {kabupaten.ToUpperInvariant()}", FontKabupaten * skalaJudul, true),
                new($"KECAMATAN {kecamatan.ToUpperInvariant()}", FontKecamatan * skalaJudul, true),
                new($"PEMERINTAH DESA {namaDesa.ToUpperInvariant()}", FontNamaDesa * skalaJudul, true),
                new($"Alamat {alamat} Desa {namaDesa} Kec. {kecamatan} Kodepos {kodepos}".TrimEnd(), FontAlamat, false),
                new(kabupaten.ToUpperInvariant(), FontKabupatenBawah, true),
            };
        }

        /// <summary>Lebar kotak logo yang dipakai bersama skala judul yang sama.</summary>
        public static float LebarLogoUntuk(float skalaJudul) =>
            skalaJudul <= 0f ? LebarLogo : LebarLogo * skalaJudul;

        /// <summary>
        /// Salinan <see cref="DesaData"/> dengan nama wilayah sudah dibersihkan dari
        /// kata pengulang. Dipanggil sekali sebelum dokumen dirender supaya SELURUH
        /// isi dokumen — kop, badan surat, dan footer (“Kepala Desa Sumberjaya”,
        /// “Camat Tempuran”) — memakai nama yang bersih, bukan hanya kopnya.
        ///
        /// Sengaja mengembalikan salinan: objek DesaData dari repositori tidak ikut berubah.
        /// </summary>
        public static DesaData DesaBersih(DesaData? desa)
        {
            if (desa == null)
            {
                return new DesaData();
            }

            return new DesaData
            {
                NamaDesa = NamaWilayah(desa.NamaDesa, "Desa"),
                Kecamatan = NamaWilayah(desa.Kecamatan, "Kecamatan", "Kec."),
                Kabupaten = NamaWilayah(desa.Kabupaten, "Kabupaten", "Kab."),
                Alamat = desa.Alamat,
                Kodepos = desa.Kodepos,
                KepalaDesa = desa.KepalaDesa,
                SekretarisDesa = desa.SekretarisDesa,
                NamaCamat = desa.NamaCamat,
                NipCamat = desa.NipCamat,
                GolCamat = desa.GolCamat
            };
        }

        /// <summary>
        /// Buang kata pengulang di depan nilai setelan. Bila pengguna mengetik
        /// “Kabupaten Karawang” pada kolom Kabupaten, kop tetap tercetak sebagai
        /// “PEMERINTAH KABUPATEN KARAWANG” — bukan “PEMERINTAH KABUPATEN KABUPATEN KARAWANG”.
        /// </summary>
        public static string NamaWilayah(string? nilai, string kataPengulang, string? singkatan = null)
        {
            string teks = (nilai ?? string.Empty).Trim();
            if (teks.Length == 0)
            {
                return string.Empty;
            }

            foreach (string ?awalan in new[] { kataPengulang, singkatan })
            {
                if (string.IsNullOrEmpty(awalan) ||
                    teks.Length <= awalan.Length ||
                    !teks.StartsWith(awalan, StringComparison.OrdinalIgnoreCase) ||
                    !char.IsWhiteSpace(teks[awalan.Length]))
                {
                    continue;
                }

                // Sisa setelah kata pengulang harus benar-benar ada isinya.
                string sisa = teks[(awalan.Length + 1)..].Trim();
                if (sisa.Length > 0)
                {
                    return sisa;
                }
            }

            return teks;
        }
    }
}
