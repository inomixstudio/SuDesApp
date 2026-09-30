using System;
using System.Security.Cryptography;
using System.Text;

namespace SuDesApp.Api
{
    /// <summary>
    /// Pemeriksaan kunci API untuk setiap permintaan masuk.
    ///
    /// Dua hal yang dijaga di sini:
    /// 1. Bila kunci belum dibuat di komputer ini, semua permintaan DITOLAK
    ///    (bukan diterima dengan kunci kosong) — kalau tidak, ada celah di mana
    ///    API aktif tapi tanpa kunci sehingga siapa pun yang menemukan port
    ///    bisa membaca data desa.
    /// 2. Perbandingan kunci memakai waktu tetap (constant-time). Perbandingan
    ///    string biasa berhenti di karakter pertama yang berbeda, sehingga
    ///    penyerang bisa menebak kunci satu demi satu dengan mengukur waktu
    ///    respons. Karena itu kedua kunci dicerna lebih dulu (SHA-256) lalu
    ///    dibandingkan dengan <see cref="CryptographicOperations.FixedTimeEquals"/>:
    ///    panjangnya selalu sama dan setiap byte tetap dibandingkan.
    /// </summary>
    public static class ApiAutentikasi
    {
        /// <summary>Header baku untuk kunci API.</summary>
        public const string HeaderKunci = "X-Api-Key";

        public const string HeaderOtorisasi = "Authorization";

        private const string AwalBearer = "Bearer ";

        /// <summary>
        /// True bila kunci yang diberikan caller cocok dengan kunci tersimpan.
        /// Kunci kosong di kedua sisi tetap ditolak.
        /// </summary>
        public static bool Diizinkan(string? kunciDiberikan, string? kunciTersimpan)
        {
            if (string.IsNullOrWhiteSpace(kunciTersimpan)) return false;
            if (string.IsNullOrWhiteSpace(kunciDiberikan)) return false;

            return PerbandinganTetap(kunciDiberikan!, kunciTersimpan!);
        }

        /// <summary>
        /// Cari kunci yang cocok di antara semua kunci tersimpan (utama +
        /// tambahan). Perbandingan tiap kandidat tetap constant-time. Null bila
        /// tidak ada yang cocok — pemanggil membalas 401.
        /// </summary>
        public static ApiKunciEntry? CariCocok(string? kunciDiberikan, IReadOnlyList<ApiKunciEntry> tersimpan)
        {
            if (string.IsNullOrWhiteSpace(kunciDiberikan) || tersimpan.Count == 0) return null;

            foreach (var kandidat in tersimpan)
            {
                if (string.IsNullOrWhiteSpace(kandidat.Nilai)) continue;
                if (PerbandinganTetap(kunciDiberikan!, kandidat.Nilai)) return kandidat;
            }
            return null;
        }

        /// <summary>
        /// True bila cakupan kunci mencakup endpoint yang diminta. Kunci Penuh
        /// membuka semuanya; kunci Agregat membuka endpoint agregat dan data
        /// perangkat desa (baca-saja untuk kop/dashboard); kunci Permintaan
        /// membuka alur permintaan surat online. Kategori lain tidak tercakup
        /// kunci sempit mana pun.
        /// </summary>
        public static bool CakupanCukup(ApiCakupan cakupanKunci, ApiEndpoint endpoint)
        {
            if (cakupanKunci == ApiCakupan.Penuh) return true;
            return endpoint.Kategori switch
            {
                ApiKategori.Agregat or ApiKategori.Perangkat => cakupanKunci == ApiCakupan.Agregat,
                ApiKategori.Permintaan => cakupanKunci == ApiCakupan.Permintaan,
                _ => false
            };
        }

        /// <summary>
        /// Ambil kunci dari header permintaan. <c>X-Api-Key</c> dipakai lebih
        /// dulu; bila tidak ada, mencoba <c>Authorization: Bearer &lt;kunci&gt;</c>
        /// supaya pemanggil yang hanya bisa memakai bearer token tetap bisa.
        ///
        /// <paramref name="pengambilHeader"/> diberikan sebagai fungsi agar
        /// logika ini bisa diuji tanpa membuka soket sungguhan.
        /// </summary>
        public static string? AmbilKunci(Func<string, string?> pengambilHeader)
        {
            if (pengambilHeader is null) return null;

            var langsung = pengambilHeader(HeaderKunci);
            if (!string.IsNullOrWhiteSpace(langsung))
                return langsung!.Trim();

            var otorisasi = pengambilHeader(HeaderOtorisasi);
            if (string.IsNullOrWhiteSpace(otorisasi)) return null;

            var nilai = otorisasi!.Trim();
            if (nilai.StartsWith(AwalBearer, StringComparison.OrdinalIgnoreCase))
                nilai = nilai[AwalBearer.Length..].Trim();

            return string.IsNullOrWhiteSpace(nilai) ? null : nilai;
        }

        /// <summary>
        /// Perbandingan waktu tetap untuk dua string. Keduanya dicerna SHA-256
        /// lebih dulu supaya <see cref="CryptographicOperations.FixedTimeEquals"/>
        /// (yang mensyaratkan panjang sama) tetap bisa dipakai untuk kunci
        /// dengan panjang berbeda.
        /// </summary>
        internal static bool PerbandinganTetap(string kiri, string kanan)
        {
            var h1 = SHA256.HashData(Encoding.UTF8.GetBytes(kiri.Trim()));
            var h2 = SHA256.HashData(Encoding.UTF8.GetBytes(kanan.Trim()));
            return CryptographicOperations.FixedTimeEquals(h1, h2);
        }
    }
}
