using System;
using System.Collections.Generic;
using SuDesApp.Data.Models;

namespace SuDesApp.GeneratorPdf
{
    /// <summary>
    /// Isi kop surat desa beserta ukuran fontnya. Dipakai bersama oleh SELURUH
    /// generator — surat keterangan, daftar hadir, dan rekening koran — supaya
    /// tampilan kop tidak pernah berbeda antar dokumen.
    ///
    /// Urutannya:
    ///   PEMERINTAH KABUPATEN ...
    ///   KECAMATAN ...
    ///   PEMERINTAH DESA ...
    ///   Alamat ... Desa ... Kec. ... Kodepos ...
    ///   NAMA KABUPATEN
    ///   Email ... (HANYA bila surel desa diisi)
    ///
    /// Surel kantor desa bersifat <b>opsional</b> dan dicetak sebagai baris tersendiri
    /// DI BAWAH nama kabupaten, berwarna <b>biru</b> (lihat <see cref="WarnaSurel"/>)
    /// supaya mudah dikenali — baik pada PDF maupun pratinjau kop di Pengaturan Surat.
    /// </summary>
    public static class KopSurat
    {
        /// <summary>Lebar kotak logo di kop (point), rasio gambar dipertahankan.</summary>
        public const float LebarLogo = 60f;

        /// <summary>
        /// Warna surel desa pada kop (biru). Dipakai bersama generator PDF dan
        /// pratinjau kop di layar supaya keduanya menampilkan warna yang sama.
        /// </summary>
        public const string WarnaSurel = "#1565C0";

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
        public readonly record struct Baris(string Teks, float FontSize, bool Tebal)
        {
            /// <summary>
            /// Surel desa (opsional) yang dicetak <b>biru</b> sebagai baris tersendiri di
            /// bawah nama kabupaten. Bila <see cref="Teks"/> kosong, surel itulah satu-satunya
            /// isi baris. Kosong berarti barisnya dicetak polos seperti biasa.
            /// </summary>
            public string? Surel { get; init; }

            /// <summary>Benar bila baris ini membawa surel untuk dicetak biru.</summary>
            public bool AdaSurel => !string.IsNullOrWhiteSpace(Surel);
        }

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
            string surel = SurelKop(desa);

            var baris = new List<Baris>
            {
                new($"PEMERINTAH KABUPATEN {kabupaten.ToUpperInvariant()}", FontKabupaten * skalaJudul, true),
                new($"KECAMATAN {kecamatan.ToUpperInvariant()}", FontKecamatan * skalaJudul, true),
                new($"PEMERINTAH DESA {namaDesa.ToUpperInvariant()}", FontNamaDesa * skalaJudul, true),
                new($"Alamat {alamat} Desa {namaDesa} Kec. {kecamatan} Kodepos {kodepos}".TrimEnd(), FontAlamat, false),
                new(kabupaten.ToUpperInvariant(), FontKabupatenBawah, true),
            };

            // Surel desa (opsional) dicetak sebagai BARIS SENDIRI di bawah nama kabupaten,
            // bukan disambung di ujung baris alamat. Tanpa surel, baris ini tidak ada.
            if (surel.Length > 0)
            {
                baris.Add(new(string.Empty, FontAlamat, false) { Surel = surel });
            }

            return baris;
        }

        /// <summary>Lebar kotak logo yang dipakai bersama skala judul yang sama.</summary>
        public static float LebarLogoUntuk(float skalaJudul) =>
            skalaJudul <= 0f ? LebarLogo : LebarLogo * skalaJudul;

        /// <summary>
        /// Bagian surel yang dicetak biru di ujung baris alamat kop — kosong bila kolom
        /// surel desa tidak diisi (kolomnya opsional). Bentuknya “Email nama@desa.id”,
        /// menyatu dengan gaya baris alamat yang sudah memakai “Kodepos …”.
        /// </summary>
        public static string SurelKop(DesaData? desa)
        {
            string surel = SurelBersih(desa?.Email);
            return surel.Length == 0 ? string.Empty : $"Email {surel}";
        }

        /// <summary>
        /// Bersihkan isian surel pengguna: buang spasi berlebih, awalan “mailto:”, dan
        /// kata “Email”/“Surel”/“E-mail” yang mungkin ikut diketik di kolomnya — supaya
        /// kop tidak pernah mencetak “Email Email desa@x.id”.
        /// </summary>
        public static string SurelBersih(string? nilai)
        {
            string teks = (nilai ?? string.Empty).Trim();

            foreach (string awalan in new[] { "mailto:", "email:", "e-mail:", "surel:", "email ", "e-mail ", "surel " })
            {
                if (teks.StartsWith(awalan, StringComparison.OrdinalIgnoreCase))
                {
                    teks = teks[awalan.Length..].Trim();
                    break;
                }
            }

            return teks;
        }

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
                Email = desa.Email,
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
