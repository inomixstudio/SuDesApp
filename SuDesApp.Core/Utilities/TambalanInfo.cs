using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SuDesApp.Utilities
{
    /// <summary>
    /// Satu berkas di dalam paket tambalan: jalur relatif terhadap folder aplikasi
    /// beserta sidik jari SHA-256 dan ukurannya.
    /// </summary>
    public class BerkasTambalan
    {
        /// <summary>Jalur relatif memakai garis miring depan, mis. <c>SuDesApp.Core.dll</c>.</summary>
        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;

        /// <summary>SHA-256 heksadesimal (64 digit) isi berkas pada rilis baru.</summary>
        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; } = string.Empty;

        [JsonPropertyName("ukuran")]
        public long Ukuran { get; set; }
    }

    /// <summary>
    /// Isi berkas <c>patch.json</c> yang dilampirkan pada sebuah rilis.
    ///
    /// Dengan berkas ini aplikasi yang sudah terpasang tidak perlu installer penuh:
    /// cukup mengunduh berkas-berkas yang benar-benar berubah, memverifikasi
    /// SHA-256-nya, lalu menggantinya di folder aplikasi. Rilis dengan perubahan
    /// besar tetap memakai installer (<c>jenis: "besar"</c>).
    /// </summary>
    public class TambalanInfo
    {
        [JsonPropertyName("versi")]
        public string Versi { get; set; } = string.Empty;

        /// <summary>
        /// Versi terpasang yang boleh memakai tambalan ini (versi penerbitan
        /// sebelumnya). Versi lain memakai installer penuh agar isi berkasnya pasti
        /// cocok.
        /// </summary>
        [JsonPropertyName("dariVersi")]
        public List<string> DariVersi { get; set; } = new();

        /// <summary><c>"kecil"</c> (tambalan berkas) atau <c>"besar"</c> (installer penuh).</summary>
        [JsonPropertyName("jenis")]
        public string Jenis { get; set; } = "kecil";

        /// <summary>Daftar singkat perbaikan/penambahan yang dibawa pembaruan ini.</summary>
        [JsonPropertyName("ringkasan")]
        public List<string> Ringkasan { get; set; } = new();

        /// <summary>Nama aset zip tambalan di halaman rilis, mis. <c>patch-2.4.5.zip</c>.</summary>
        [JsonPropertyName("berkasPatch")]
        public string BerkasPatch { get; set; } = string.Empty;

        /// <summary>SHA-256 berkas zip tambalan (verifikasi sebelum diekstrak).</summary>
        [JsonPropertyName("sha256Patch")]
        public string Sha256Patch { get; set; } = string.Empty;

        [JsonPropertyName("berkas")]
        public List<BerkasTambalan> Berkas { get; set; } = new();

        /// <summary>Berkas yang harus dihapus dari folder aplikasi (mis. berkas lama).</summary>
        [JsonPropertyName("berkasDihapus")]
        public List<string> BerkasDihapus { get; set; } = new();

        [JsonPropertyName("tanggal")]
        public DateTime? Tanggal { get; set; }

        [JsonPropertyName("catatan")]
        public string? Catatan { get; set; }

        /// <summary>Jumlah berkas yang diganti.</summary>
        [JsonIgnore]
        public int JumlahBerkas => Berkas?.Count ?? 0;

        /// <summary>Total ukuran berkas baru (bukan ukuran zip; hanya untuk keterangan).</summary>
        [JsonIgnore]
        public long TotalByte => Berkas?.Sum(b => b.Ukuran) ?? 0;

        /// <summary>Pembaruan kecil yang benar-benar bisa dipasang sebagai tambalan.</summary>
        [JsonIgnore]
        public bool BisaDitambal =>
            !string.Equals(Jenis, "besar", StringComparison.OrdinalIgnoreCase) &&
            JumlahBerkas > 0 &&
            !string.IsNullOrWhiteSpace(BerkasPatch);

        /// <summary>Benar bila versi terpasang termasuk salah satu versi asal tambalan.</summary>
        public bool CocokDenganVersiTerpasang(string? versiTerpasang)
        {
            if (string.IsNullOrWhiteSpace(versiTerpasang)) return false;
            var asal = DariVersi ?? new List<string>();
            if (asal.Count == 0) return false;

            var terpasang = VersiSama(versiTerpasang);
            if (terpasang.Length == 0) return false;

            return asal.Any(v => string.Equals(VersiSama(v), terpasang, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Rapikan tulisan versi menjadi bentuk yang bisa dibandingkan
        /// (<c>v2.4.5.0</c> dan <c>2.4.5</c> dianggap sama).
        /// </summary>
        public static string VersiSama(string? versi)
        {
            if (string.IsNullOrWhiteSpace(versi)) return string.Empty;

            var bersih = versi.Trim().TrimStart('v', 'V').Trim();
            var angka = new string(bersih.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray()).Trim('.');
            if (angka.Length == 0) return string.Empty;

            // Buang bagian ".0" di ujung supaya 2.4.5.0 == 2.4.5.
            var bagian = angka.Split('.', StringSplitOptions.RemoveEmptyEntries).ToList();
            while (bagian.Count > 2 && bagian[^1] == "0") bagian.RemoveAt(bagian.Count - 1);

            return string.Join('.', bagian);
        }

        /// <summary>
        /// Baca patch.json. JSON rusak, tanpa versi, atau tanpa daftar berkas
        /// mengembalikan null supaya pemanggil dapat memakai installer penuh.
        /// </summary>
        public static TambalanInfo? FromJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            try
            {
                var info = JsonSerializer.Deserialize<TambalanInfo>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    NumberHandling = JsonNumberHandling.AllowReadingFromString
                });

                if (info == null) return null;
                if (string.IsNullOrWhiteSpace(info.Versi)) return null;

                info.Berkas ??= new List<BerkasTambalan>();
                info.BerkasDihapus ??= new List<string>();
                info.DariVersi ??= new List<string>();
                info.Ringkasan ??= new List<string>();

                // Hanya berkas dengan jalur & SHA-256 yang masuk akal yang diterima;
                // sisanya dibuang agar patch cacat tidak pernah ikut dipasang.
                info.Berkas = info.Berkas
                    .Where(b => b != null &&
                                !string.IsNullOrWhiteSpace(b.Path) &&
                                IsSha256Valid(b.Sha256))
                    .ToList();

                return info;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>Benar bila tulisan SHA-256 berbentuk 64 digit heksadesimal.</summary>
        public static bool IsSha256Valid(string? sha256)
        {
            if (string.IsNullOrWhiteSpace(sha256)) return false;
            var bersih = sha256.Trim();
            if (bersih.Length != 64) return false;
            return bersih.All(Uri.IsHexDigit);
        }
    }
}
