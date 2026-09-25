using System;
using System.Collections.Generic;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;

namespace SuDesApp.GeneratorPdf
{
    /// <summary>
    /// Satu blok tanda tangan pejabat pada pratinjau: jabatan yang akan dicetak
    /// (huruf kapital, boleh dua baris) beserta nama pejabatnya.
    /// </summary>
    public sealed class BlokPejabatPratinjau
    {
        /// <summary>Judul blok di pratinjau, mis. "Kepala Desa".</summary>
        public string Judul { get; init; } = string.Empty;

        /// <summary>Baris jabatan persis seperti yang dicetak surat (huruf kapital).</summary>
        public string Jabatan { get; init; } = string.Empty;

        /// <summary>Nama pejabat yang dicetak; garis bawah bila kolomnya masih kosong.</summary>
        public string Nama { get; init; } = string.Empty;

        /// <summary>Baris tambahan di bawah nama (golongan, NIP) — dipakai blok camat.</summary>
        public IReadOnlyList<string> BarisTambahan { get; init; } = Array.Empty<string>();

        /// <summary>False bila kolom namanya masih kosong sehingga surat mencetak garis bawah.</summary>
        public bool Terisi { get; init; }
    }

    /// <summary>Isi pratinjau kop surat untuk halaman Pengaturan Surat.</summary>
    public sealed class KopSuratPratinjau
    {
        /// <summary>Kelima baris kop beserta ukuran hurufnya (point, sama dengan PDF).</summary>
        public IReadOnlyList<KopSurat.Baris> Baris { get; init; } = Array.Empty<KopSurat.Baris>();

        /// <summary>
        /// Surel desa yang akan dicetak biru di baris alamat kop; kosong bila kolomnya
        /// dikosongkan (kolom ini opsional).
        /// </summary>
        public string Surel { get; init; } = string.Empty;

        /// <summary>Blok tanda tangan Kepala Desa (selalu ada).</summary>
        public BlokPejabatPratinjau KepalaDesa { get; init; } = new();

        /// <summary>Blok tanda tangan Sekretaris Desa (a.n. Kepala Desa).</summary>
        public BlokPejabatPratinjau SekretarisDesa { get; init; } = new();

        /// <summary>Blok tanda tangan Camat; null bila Pejabat Kecamatan dikosongkan.</summary>
        public BlokPejabatPratinjau? Camat { get; init; }

        /// <summary>Lebar kotak logo di kop (point) — rasio gambar tetap dijaga.</summary>
        public float LebarLogo { get; init; } = KopSurat.LebarLogo;

        /// <summary>Nama desa setelah dibersihkan dari kata pengulang ("" bila masih kosong).</summary>
        public string NamaDesa { get; init; } = string.Empty;
    }

    /// <summary>
    /// Menyusun isi pratinjau kop surat dari data desa yang sedang diisi pengguna.
    ///
    /// Seluruh teks diambil dari sumber yang sama dengan generator PDF: kelima baris
    /// kop dari <see cref="KopSurat.BarisKop"/> dan jabatan blok tanda tangan dari
    /// aturan yang sama dengan <c>SuratGeneratorBase.DataKakiSurat</c>. Karena itu
    /// pratinjau di halaman Pengaturan Surat tidak bisa melenceng dari hasil cetak —
    /// termasuk pembersihan kata "Desa"/"Kecamatan" yang diketik di depan nama
    /// wilayah, dan penanda kurung siku/garis bawah untuk kolom yang belum diisi.
    /// </summary>
    public static class PratinjauKopSurat
    {
        /// <summary>Penanda nama pejabat yang masih kosong (sama dengan yang dicetak PDF).</summary>
        public const string GarisNamaKosong = "_______________________";

        /// <summary>Penanda kecamatan yang masih kosong pada blok camat (sama dengan PDF).</summary>
        public const string GarisKecamatanKosong = "____________________";

        /// <summary>Susun isi pratinjau kop dari data desa (boleh null atau sebagian kosong).</summary>
        public static KopSuratPratinjau Susun(DesaData? desa)
        {
            // Sama seperti generator: nama wilayah dibersihkan sekali di awal supaya
            // "Kecamatan Tempuran" tidak menjadi "KECAMATAN KECAMATAN TEMPURAN".
            var bersih = KopSurat.DesaBersih(desa);

            string namaDesa = (bersih.NamaDesa ?? string.Empty).Trim();
            string jabatanDesa = namaDesa.Length == 0 ? "[NAMA DESA]" : namaDesa.ToUpperInvariant();
            string kecamatan = (bersih.Kecamatan ?? string.Empty).Trim();

            string kades = (bersih.KepalaDesa ?? string.Empty).Trim();
            string sekdes = (bersih.SekretarisDesa ?? string.Empty).Trim();
            string namaCamat = (bersih.NamaCamat ?? string.Empty).Trim();
            string nipCamat = (bersih.NipCamat ?? string.Empty).Trim();
            string golCamat = (bersih.GolCamat ?? string.Empty).Trim();

            var barisTambahanCamat = new List<string>();
            if (golCamat.Length > 0)
            {
                barisTambahanCamat.Add(golCamat);
            }

            if (nipCamat.Length > 0)
            {
                barisTambahanCamat.Add($"NIP: {nipCamat}");
            }

            return new KopSuratPratinjau
            {
                Baris = KopSurat.BarisKop(bersih),
                Surel = KopSurat.SurelBersih(bersih.Email),
                NamaDesa = namaDesa,
                LebarLogo = KopSurat.LebarLogo,
                KepalaDesa = new BlokPejabatPratinjau
                {
                    Judul = "Kepala Desa",
                    Jabatan = $"Kepala Desa {jabatanDesa}".ToUpperInvariant(),
                    Nama = kades.Length == 0 ? GarisNamaKosong : NamaFormatter.ToUpperNama(kades),
                    Terisi = kades.Length > 0
                },
                SekretarisDesa = new BlokPejabatPratinjau
                {
                    Judul = "Sekretaris Desa",
                    Jabatan = $"A/N Kepala Desa {jabatanDesa}\nSekretaris Desa".ToUpperInvariant(),
                    Nama = sekdes.Length == 0 ? GarisNamaKosong : NamaFormatter.ToUpperNama(sekdes),
                    Terisi = sekdes.Length > 0
                },
                Camat = namaCamat.Length == 0 && nipCamat.Length == 0 && golCamat.Length == 0
                    ? null
                    : new BlokPejabatPratinjau
                    {
                        Judul = "Pejabat Kecamatan",
                        Jabatan = $"Camat {(kecamatan.Length == 0 ? GarisKecamatanKosong : kecamatan)}",
                        Nama = namaCamat.Length == 0 ? GarisNamaKosong : namaCamat.ToUpperInvariant(),
                        BarisTambahan = barisTambahanCamat,
                        Terisi = namaCamat.Length > 0
                    }
            };
        }
    }
}
