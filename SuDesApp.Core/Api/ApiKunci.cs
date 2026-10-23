using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SuDesApp.Api
{
    /// <summary>
    /// Luas wewenang satu kunci API. Kunci utama selalu Penuh; kunci tambahan
    /// dibuat dengan cakupan sempit untuk pemakaian luar (dashboard kecamatan,
    /// website desa) supaya bocornya satu kunci tidak membuka semuanya.
    /// </summary>
    public enum ApiCakupan
    {
        /// <summary>Semua endpoint berkunci (kunci utama).</summary>
        Penuh,

        /// <summary>Hanya endpoint agregat: /status, /statistik, /perangkat-desa, /jenis-surat, /rekap-bulanan.</summary>
        Agregat,

        /// <summary>Endpoint permintaan: kirim dan pantau permintaan surat online.</summary>
        Permintaan
    }

    /// <summary>Satu kunci API beserta cakupannya (lihat <see cref="ApiKunci.MuatSemua"/>).</summary>
    public sealed class ApiKunciEntry
    {
        public string Nilai { get; init; } = string.Empty;
        public ApiCakupan Cakupan { get; init; }

        /// <summary>Nama bebas untuk mengenali pemegang kunci (mis. "Dashboard Kecamatan").</summary>
        public string? Nama { get; init; }

        public DateTime Dibuat { get; init; } = DateTime.Now;
    }
    /// <summary>
    /// Penyimpanan kunci API (API key) secara terenkripsi DPAPI per user Windows
    /// (CurrentUser), di %LOCALAPPDATA%\SuDesApp\ApiKunci.bin.
    ///
    /// Kunci API adalah satu-satunya penjaga endpoint ini, jadi tidak boleh
    /// disimpan plaintext di berkas preferensi (AppPreferenceStore) seperti
    /// Kredensial WhatsApp — berkas preferensi itu dibaca tanpa proteksi
    /// tambahan dan mudah ikut terkopi. DPAPI CurrentUser juga konsisten dengan
    /// penyimpanan token Google dan sandi aplikasi yang sudah ada: hanya akun
    /// Windows yang mengenkripsi yang dapat mendekripsi.
    ///
    /// Kunci dibuat acak 32 byte (256 bit) oleh <see cref="BuatBaru"/>; tidak
    /// ada kunci bawaan yang bisa ditebak, karena kalau begitu siapa pun yang
    /// menemukan port listener bisa langsung membaca data desa.
    /// </summary>
    public static class ApiKunci
    {
        /// <summary>Panjang kunci acak dalam byte (256 bit).</summary>
        public const int PanjangKunci = 32;

        /// <summary>Panjang maksimal kunci yang diterima (bihar buffer).</summary>
        private const int PanjangMaksimal = 256;

        /// <summary>
        /// Lokasi berkas untuk pengujian; null = lokasi asli milik user.
        /// </summary>
        internal static Func<string>? LokasiOverride { get; set; }

        private static string StorePath => LokasiOverride?.Invoke()
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SuDesApp", "ApiKunci.bin");

        /// <summary>True bila kunci API sudah pernah dibuat.</summary>
        public static bool Ada => File.Exists(StorePath);

        /// <summary>Lokasi berkas untuk pesan di UI & diagnosa.</summary>
        public static string LokasiTampil => StorePath;

        /// <summary>
        /// Muat kunci utama. Kompatibilitas untuk UI & pemanggil lama: sama dengan
        /// entri pertama <see cref="MuatSemua"/> (kunci utama, cakupan Penuh).
        /// </summary>
        public static string? Muat()
        {
            var utama = MuatSemua().FirstOrDefault(k => k.Cakupan == ApiCakupan.Penuh);
            return utama?.Nilai;
        }

        /// <summary>
        /// Muat SEMUA kunci tersimpan: kunci utama (Penuh) di depan, lalu kunci
        /// tambahan bercakupan sempit. Berkas format lama (satu kunci teks di
        /// dalam blob DPAPI) dimigrasikan di tempat: dibaca sebagai kunci utama
        /// tanpa kunci tambahan. Berkas hilang/rusak → daftar kosong (pemanggil
        /// memperlakukannya sebagai "belum ada kunci").
        /// </summary>
        public static IReadOnlyList<ApiKunciEntry> MuatSemua()
        {
            try
            {
                if (!File.Exists(StorePath)) return Array.Empty<ApiKunciEntry>();

                var sandi = System.Security.Cryptography.ProtectedData.Unprotect(
                    File.ReadAllBytes(StorePath),
                    optionalEntropy: null,
                    DataProtectionScope.CurrentUser);

                var isi = Encoding.UTF8.GetString(sandi).Trim();
                if (isi.Length == 0) return Array.Empty<ApiKunciEntry>();

                var hasil = new List<ApiKunciEntry>();
                if (isi.StartsWith('{'))
                {
                    var keadaan = JsonSerializer.Deserialize<KeadaanTersimpan>(isi);
                    if (!string.IsNullOrWhiteSpace(keadaan?.Utama))
                        hasil.Add(new ApiKunciEntry { Nilai = keadaan.Utama.Trim(), Cakupan = ApiCakupan.Penuh });
                    if (keadaan?.Tambahan != null)
                    {
                        foreach (var t in keadaan.Tambahan)
                        {
                            if (string.IsNullOrWhiteSpace(t.Nilai)) continue;
                            hasil.Add(new ApiKunciEntry
                            {
                                Nilai = t.Nilai.Trim(),
                                Cakupan = NormalisasiCakupan(t.Cakupan),
                                Nama = string.IsNullOrWhiteSpace(t.Nama) ? null : t.Nama!.Trim(),
                                Dibuat = t.Dibuat ?? DateTime.Now
                            });
                        }
                    }
                }
                else
                {
                    // Format lama: seluruh isi = kunci utama.
                    hasil.Add(new ApiKunciEntry { Nilai = isi, Cakupan = ApiCakupan.Penuh });
                }

                return hasil;
            }
            catch (Exception)
            {
                return Array.Empty<ApiKunciEntry>();
            }
        }

        private static ApiCakupan NormalisasiCakupan(string? cakupan) =>
            Enum.TryParse<ApiCakupan>(cakupan, ignoreCase: true, out var c) && c != ApiCakupan.Penuh
                ? c
                : ApiCakupan.Agregat; // nilai tak dikenal jatuh ke cakupan PALING SEMPIT

        /// <summary>
        /// Simpan kunci utama (cakupan Penuh) tanpa mengusik kunci tambahan yang
        /// sudah ada — regenerasi kunci dari halaman API tidak boleh mematikan
        /// integrasi luar yang memegang kunci cakupan sempit.
        /// </summary>
        public static void Simpan(string kunci)
        {
            if (string.IsNullOrWhiteSpace(kunci))
                throw new ArgumentException("Kunci API tidak boleh kosong.", nameof(kunci));
            if (kunci.Length > PanjangMaksimal)
                throw new ArgumentException("Kunci API terlalu panjang.", nameof(kunci));

            var keadaan = BacaKeadaan();
            keadaan.Utama = kunci.Trim();
            TulisKeadaan(keadaan);
        }

        /// <summary>
        /// Buat kunci TAMBAHAN bercakupan sempit (Agregat atau Permintaan) dan
        /// simpan. Nilai kunci hanya ditampilkan sekali kepada pembuatnya.
        /// Cakupan Penuh tidak lewat sini — untuk itu gunakan <see cref="BuatBaru"/>.
        /// </summary>
        public static ApiKunciEntry BuatTambahan(ApiCakupan cakupan, string? nama)
        {
            if (cakupan is not (ApiCakupan.Agregat or ApiCakupan.Permintaan))
                throw new ArgumentException(
                    "Kunci tambahan hanya untuk cakupan Agregat atau Permintaan; cakupan penuh memakai kunci utama.",
                    nameof(cakupan));

            var keadaan = BacaKeadaan();
            var baris = new ApiKunciEntry
            {
                Nilai = BuatNilaiAcak(),
                Cakupan = cakupan,
                Nama = string.IsNullOrWhiteSpace(nama) ? null : nama!.Trim(),
                Dibuat = DateTime.Now
            };
            keadaan.Tambahan.Add(new BarisTambahan
            {
                Nilai = baris.Nilai,
                Cakupan = cakupan.ToString(),
                Nama = baris.Nama,
                Dibuat = baris.Dibuat
            });
            TulisKeadaan(keadaan);
            return baris;
        }

        /// <summary>Hapus kunci tambahan berdasarkan nilainya; kunci utama tidak bisa dihapus di sini. True bila terhapus.</summary>
        public static bool HapusTambahan(string nilai)
        {
            if (string.IsNullOrWhiteSpace(nilai)) return false;

            var keadaan = BacaKeadaan();
            int sebelum = keadaan.Tambahan.Count;
            keadaan.Tambahan.RemoveAll(t => string.Equals(t.Nilai?.Trim(), nilai.Trim(), StringComparison.Ordinal));
            if (keadaan.Tambahan.Count == sebelum) return false;

            TulisKeadaan(keadaan);
            return true;
        }

        // ----- format tersimpan v2: JSON di dalam blob DPAPI yang sama -------

        private sealed class KeadaanTersimpan
        {
            [System.Text.Json.Serialization.JsonPropertyName("utama")]
            public string? Utama { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("tambahan")]
            public List<BarisTambahan> Tambahan { get; set; } = new();
        }

        private sealed class BarisTambahan
        {
            [System.Text.Json.Serialization.JsonPropertyName("nilai")]
            public string? Nilai { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("cakupan")]
            public string? Cakupan { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("nama")]
            public string? Nama { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("dibuat")]
            public DateTime? Dibuat { get; set; }
        }

        /// <summary>Baca keadaan v2; berkas lama (teks kunci tunggal) → kunci utama tanpa tambahan.</summary>
        private static KeadaanTersimpan BacaKeadaan()
        {
            try
            {
                if (!File.Exists(StorePath)) return new KeadaanTersimpan();

                var sandi = System.Security.Cryptography.ProtectedData.Unprotect(
                    File.ReadAllBytes(StorePath), optionalEntropy: null, DataProtectionScope.CurrentUser);
                var isi = Encoding.UTF8.GetString(sandi).Trim();

                if (isi.StartsWith('{'))
                {
                    var keadaan = JsonSerializer.Deserialize<KeadaanTersimpan>(isi);
                    if (keadaan != null) return keadaan;
                }
                else if (isi.Length > 0)
                {
                    return new KeadaanTersimpan { Utama = isi };
                }
            }
            catch
            {
                // Berkas rusak/tidak bisa didekripsi → mulai kosong (perilaku lama).
            }
            return new KeadaanTersimpan();
        }

        private static void TulisKeadaan(KeadaanTersimpan keadaan)
        {
            var dir = Path.GetDirectoryName(StorePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var isi = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(keadaan));
            var terenkripsi = System.Security.Cryptography.ProtectedData.Protect(
                isi,
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);
            File.WriteAllBytes(StorePath, terenkripsi);
        }

        /// <summary>
        /// Buat kunci acak baru dan langsung simpan. Nilai lama tidak berlaku
        /// lagi, jadi pemanggil yang menyimpan kunci sebelumnya harus diberi tahu
        /// (disarankan lewat <c>ApiAutentikasi</c>: kunci salah akan ditolak).
        /// </summary>
        public static string BuatBaru()
        {
            var kunci = BuatNilaiAcak();
            Simpan(kunci);
            return kunci;
        }

        /// <summary>
        /// Kunci acak dalam bentuk base64url (tanpa karakter + / =) supaya aman
        /// ditempel di header HTTP, URL, dan berkas teks tanpa escaping.
        /// </summary>
        internal static string BuatNilaiAcak()
        {
            var byteAcak = RandomNumberGenerator.GetBytes(PanjangKunci);
            return Encode(byteAcak);
        }

        /// <summary>Base64url: + menjadi -, / menjadi _, dan tanda = dibuang.</summary>
        internal static string Encode(byte[] data)
        {
            return Convert.ToBase64String(data)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        }
    }
}
