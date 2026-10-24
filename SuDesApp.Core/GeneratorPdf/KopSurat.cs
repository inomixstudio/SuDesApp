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
    ///   PEMERINTAH KABUPATEN/KOTA ... (lihat <see cref="LabelKabupaten"/>)
    ///   KECAMATAN ...
    ///   PEMERINTAH DESA ...
    ///   Alamat ... Desa ... Kec. ... Kodepos ...
    ///   NAMA KABUPATEN/KOTA
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

            string kabupaten = NamaKabupaten(desa);
            string kecamatan = NamaWilayah(desa.Kecamatan, "Kecamatan", "Kec.");
            string namaDesa = NamaWilayah(desa.NamaDesa, "Desa");
            if (kabupaten.Length == 0) kabupaten = "[KABUPATEN]";
            if (kecamatan.Length == 0) kecamatan = "[KECAMATAN]";
            if (namaDesa.Length == 0) namaDesa = "[NAMA DESA]";
            string kataLembaga = KataLembaga(desa);

            string alamat = (desa.Alamat ?? string.Empty).Trim();
            if (alamat.Length == 0) alamat = "[Alamat Kantor Desa]";
            string kodepos = (desa.Kodepos ?? string.Empty).Trim();
            string surel = SurelKop(desa);

            var baris = new List<Baris>
            {
                new($"PEMERINTAH {LabelKabupaten(desa)} {kabupaten.ToUpperInvariant()}", FontKabupaten * skalaJudul, true),
                new($"KECAMATAN {kecamatan.ToUpperInvariant()}", FontKecamatan * skalaJudul, true),
                // Baris ketiga: “PEMERINTAH DESA X” untuk desa, “KELURAHAN X”
                // untuk kelurahan — sesuai lembaga yang dipilih pengguna.
                new(LabelLembagaKop(desa), FontNamaDesa * skalaJudul, true),
                new($"Alamat {alamat} {kataLembaga} {namaDesa} Kec. {kecamatan} Kodepos {kodepos}".TrimEnd(), FontAlamat, false),
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
        /// Benar bila lembaga yang dipilih pengguna adalah kelurahan. Data lama
        /// tanpa pilihan dianggap Desa, termasuk nilai yang belum terisi.
        /// </summary>
        public static bool IsKelurahan(DesaData? desa)
        {
            string nilai = JenisLembagaBersih(desa);
            return nilai.Equals("Kelurahan", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Jenis lembaga yang tersimpan pada data desa, dinormalkan menjadi
        /// “Desa” atau “Kelurahan”. Nilai kosong (data lama) dianggap “Desa”.
        /// </summary>
        public static string JenisLembagaBersih(DesaData? desa)
        {
            string nilai = (desa?.JenisLembaga ?? string.Empty).Trim();

            if (nilai.Equals("Kelurahan", StringComparison.OrdinalIgnoreCase))
            {
                return "Kelurahan";
            }

            if (nilai.Equals("Desa", StringComparison.OrdinalIgnoreCase))
            {
                return "Desa";
            }

            // Data lama belum pernah memilih: dianggap Desa, sama seperti sebelum
            // kolom jenis lembaga ada.
            return "Desa";
        }

        /// <summary>
        /// Kata jenis lembaga pada kalimat badan surat: “warga Desa X” atau
        /// “warga Kelurahan X” — huruf awal besar, mengikuti tata penulisan surat.
        /// </summary>
        public static string KataLembaga(DesaData? desa) => JenisLembagaBersih(desa);

        /// <summary>
        /// Jabatan penandatangan tingkat desa: “Kepala Desa” atau “Lurah” — kosong
        /// bila isian surat menyebut pejabat lain (mis. “a.n. Kepala Desa”.).
        /// Dipakai bila nilai tersimpan kosong atau masih kata lembaga standar.
        /// </summary>
        public static string KataPejabat(DesaData? desa, string? pejabatQ = null)
        {
            string pejabat = (pejabatQ ?? string.Empty).Trim();

            // “Kepala Desa” tersimpan pada surat lama berarti nilai bawaan; pada
            // kelurahan pejabat yang dimaksud penandatangannya adalah Lurah.
            if (pejabat.Length == 0 || pejabat.Equals("Kepala Desa", StringComparison.OrdinalIgnoreCase))
            {
                return IsKelurahan(desa) ? "Lurah" : "Kepala Desa";
            }

            return pejabat;
        }

        /// <summary>
        /// Baris kop identifikasi lembaga: “PEMERINTAH DESA X” untuk desa,
        /// “KELURAHAN X” untuk kelurahan (kelurahan bukan pemerintah desa dan
        /// tidak pernah menulis “PEMERINTAH DESA” pada kopnya).
        /// </summary>
        public static string LabelLembagaKop(DesaData? desa)
        {
            string nama = NamaWilayah(desa?.NamaDesa, "Desa");
            if (nama.Length == 0)
            {
                nama = "[NAMA DESA]";
            }

            return IsKelurahan(desa)
                ? $"KELURAHAN {nama.ToUpperInvariant()}"
                : $"PEMERINTAH DESA {nama.ToUpperInvariant()}";
        }

        /// <summary>
        /// Salinan DesaData dengan nama wilayah sudah dibersihkan dari
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
                Kabupaten = NamaKabupaten(desa),
                JenisKabupaten = JenisKabupatenBersih(desa),
                Alamat = desa.Alamat,
                Kodepos = desa.Kodepos,
                Email = desa.Email,
                KepalaDesa = desa.KepalaDesa,
                SekretarisDesa = desa.SekretarisDesa,
                JenisLembaga = JenisLembagaBersih(desa),
                NipKepalaDesa = desa.NipKepalaDesa,
                NamaCamat = desa.NamaCamat,
                NipCamat = desa.NipCamat,
                GolCamat = desa.GolCamat
            };
        }

        /// <summary>
        /// Jenis wilayah tingkat kabupaten/kota yang tersimpan pada data desa,
        /// dinormalkan menjadi “Kabupaten” atau “Kota”; kosong bila belum diketahui.
        /// Nilainya dipilih pengguna dari daftar wilayah di halaman Pengaturan Surat,
        /// jadi tidak perlu ditebak dari namanya.
        /// </summary>
        public static string JenisKabupatenBersih(DesaData? desa)
        {
            string nilai = (desa?.JenisKabupaten ?? string.Empty).Trim();

            if (nilai.Equals("Kota", StringComparison.OrdinalIgnoreCase))
            {
                return "Kota";
            }

            return nilai.Equals("Kabupaten", StringComparison.OrdinalIgnoreCase) ? "Kabupaten" : string.Empty;
        }

        /// <summary>
        /// Kata jenis wilayah tingkat kabupaten/kota dengan huruf awal besar
        /// (“Kabupaten”/“Kota”) — untuk kalimat pada badan surat.
        ///
        /// Sumbernya berurutan:
        /// 1. <see cref="DesaData.JenisKabupaten"/> — hasil pilihan dari daftar wilayah,
        ///    jadi paling dapat dipercaya (Kabupaten Bandung dan Kota Bandung bernama
        ///    sama, sehingga namanya saja tidak cukup);
        /// 2. awalan yang ikut diketik pengguna pada kolom Kabupaten (mis. “Kota Bandung”)
        ///    — untuk data desa lama yang belum pernah memilih wilayahnya;
        /// 3. “Kabupaten” sebagai bawaan, sama seperti perilaku aplikasi sebelumnya.
        /// </summary>
        public static string KataJenisKabupaten(DesaData? desa)
        {
            string jenis = JenisKabupatenBersih(desa);
            if (jenis.Length > 0)
            {
                return jenis;
            }

            return MulaiDenganKata((desa?.Kabupaten ?? string.Empty).Trim(), "Kota") ? "Kota" : "Kabupaten";
        }

        /// <summary>
        /// Sama dengan <see cref="KataJenisKabupaten"/> tetapi sudah huruf besar
        /// (“KABUPATEN”/“KOTA”), untuk baris kop dan blok identitas surat yang memang
        /// dicetak dengan huruf besar seluruhnya.
        /// </summary>
        public static string LabelKabupaten(DesaData? desa) =>
            KataJenisKabupaten(desa).ToUpperInvariant();

        /// <summary>
        /// Nama kabupaten/kota tanpa kata jenisnya: “Kabupaten Karawang” dan
        /// “Kota Bandung” sama-sama dicetak sebagai nama wilayahnya saja, karena kata
        /// jenisnya sudah dicetak sekali oleh <see cref="LabelKabupaten"/>.
        /// </summary>
        public static string NamaKabupaten(DesaData? desa)
        {
            string teks = NamaWilayah(desa?.Kabupaten, "Kabupaten", "Kab.");
            return NamaWilayah(teks, "Kota");
        }

        /// <summary>Benar bila teks dimulai dengan satu kata utuh (bukan potongan kata).</summary>
        private static bool MulaiDenganKata(string teks, string kata) =>
            teks.Length > kata.Length &&
            teks.StartsWith(kata, StringComparison.OrdinalIgnoreCase) &&
            char.IsWhiteSpace(teks[kata.Length]);

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
