using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SuDesApp.Data.Models;

namespace SuDesApp.Services
{
    /// <summary>
    /// Pembuat dan pengurai kode verifikasi surat.
    ///
    /// Setiap surat yang terbit mendapat satu kode acak yang dicetak di kaki
    /// surat bersama kode QR. Kode ini yang dicari saat seseorang memeriksa
    /// keaslian surat — baik dari dalam aplikasi (menu Verifikasi Surat)
    /// maupun lewat endpoint <c>GET /verifikasi/{kode}</c>.
    ///
    /// Kode sengaja dibuat pendek dan mudah dibaca manusia ("SD-7K3M-9QX2"):
    /// huruf I, L, O, dan U dibuang karena paling sering salah dibaca/diucapkan
    /// saat orang mengetik ulang kode dari kertas.
    /// </summary>
    public static class KodeVerifikasiSurat
    {
        /// <summary>Awalan kode yang tercetak, mis. "SD-7K3M-9QX2".</summary>
        public const string Awalan = "SD";

        /// <summary>
        /// Awalan isi QR ("SUDES|SD-…|HASH"). Dipakai supaya pemindai tahu isi
        /// QR ini memang milik aplikasi ini, bukan tautan biasa.
        /// </summary>
        public const string AwalanPayload = "SUDES";

        /// <summary>Jumlah karakter acak di dalam satu kode.</summary>
        public const int PanjangIsi = 8;

        /// <summary>Panjang cuplikan hash yang ikut ditanam di QR.</summary>
        public const int PanjangHashSingkat = 10;

        /// <summary>
        /// Alfabet Crockford base32 tanpa I, L, O, U. Panjangnya 32 sehingga
        /// pembagian byte acak (0–255) selalu rata — tidak ada bias.
        /// </summary>
        private const string Alfabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

        /// <summary>Buat satu kode acak baru, dalam bentuk tampil "SD-XXXX-XXXX".</summary>
        public static string BuatKode(RandomNumberGenerator? rng = null)
        {
            bool milikSendiri = rng == null;
            rng ??= RandomNumberGenerator.Create();

            try
            {
                Span<byte> acak = stackalloc byte[PanjangIsi];
                rng.GetBytes(acak);

                var isi = new StringBuilder(PanjangIsi);
                foreach (var b in acak)
                {
                    isi.Append(Alfabet[b % Alfabet.Length]);
                }

                return Format(isi.ToString());
            }
            finally
            {
                if (milikSendiri) rng.Dispose();
            }
        }

        /// <summary>
        /// Rapikan kode ke bentuk baku "SD-XXXX-XXXX". Masukan boleh berupa kode
        /// apa adanya (huruf kecil, spasi, tanpa tanda hubung) atau seluruh isi
        /// QR ("SUDES|SD-XXXX-XXXX|HASH"). Mengembalikan string kosong bila kode
        /// tidak dikenali.
        /// </summary>
        public static string Normalisasi(string? kode)
        {
            var (hasil, _) = UraiMasukan(kode);
            return hasil;
        }

        /// <summary>
        /// Urai masukan pengguna menjadi kode baku dan (bila ada) cuplikan hash
        /// dari QR. Kode kosong berarti masukan tidak dikenali.
        /// </summary>
        public static (string Kode, string? HashSingkat) UraiMasukan(string? masukan)
        {
            var teks = (masukan ?? string.Empty).Trim().ToUpperInvariant();
            if (teks.Length == 0) return (string.Empty, null);

            string? hash = null;

            // Isi QR: SUDES|SD-XXXX-XXXX|HASH
            if (teks.StartsWith(AwalanPayload, StringComparison.Ordinal))
            {
                var bagian = teks.Split('|');
                if (bagian.Length >= 2) teks = bagian[1];
                if (bagian.Length >= 3 && bagian[2].Length > 0) hash = bagian[2];
            }

            var isi = new string(teks.Where(char.IsLetterOrDigit).ToArray());
            if (isi.StartsWith(Awalan, StringComparison.Ordinal)) isi = isi[Awalan.Length..];

            if (isi.Length != PanjangIsi || !isi.All(c => Alfabet.Contains(c)))
                return (string.Empty, hash);

            return (Format(isi), hash);
        }

        /// <summary>Bentuk tampil kode dari 8 karakter isi: "SD-XXXX-XXXX".</summary>
        public static string Format(string isi)
        {
            if (isi.Length != PanjangIsi) return isi;
            return $"{Awalan}-{isi[..4]}-{isi[4..]}";
        }

        /// <summary>
        /// Isi QR yang dicetak di surat: kode + cuplikan hash dokumen, sehingga
        /// pemeriksa bisa tahu isi surat masih sama dengan yang diterbitkan.
        /// </summary>
        public static string BuatPayload(string kode, string? hash)
        {
            var singkat = HashSingkat(hash);
            return string.IsNullOrWhiteSpace(singkat)
                ? $"{AwalanPayload}|{kode}"
                : $"{AwalanPayload}|{kode}|{singkat}";
        }

        /// <summary>Cuplikan hash untuk QR (huruf besar, tanpa spasi).</summary>
        public static string HashSingkat(string? hash)
            => string.IsNullOrWhiteSpace(hash)
                ? string.Empty
                : hash!.Trim().ToUpperInvariant()[..Math.Min(PanjangHashSingkat, hash!.Trim().Length)];

        /// <summary>
        /// Cap dokumen: SHA-256 atas data inti surat. Dipakai dua arah —
        /// disimpan di database saat surat terbit, dan dihitung ulang saat
        /// verifikasi. Bila keduanya berbeda, isi surat berubah setelah terbit
        /// dan surat dinyatakan tidak lagi cocok dengan arsipnya.
        ///
        /// Yang masuk cap hanya data identitas dokumen (jenis, nomor, tanggal,
        /// NIK, nama pemohon) — sengaja tanpa detail panjang agar perubahan
        /// tata letak/keterangan tidak dianggap pemalsuan.
        /// </summary>
        public static string HitungHash(SuratData surat)
        {
            if (surat == null) throw new ArgumentNullException(nameof(surat));

            var pemohon = !string.IsNullOrWhiteSpace(surat.Warga?.Nama)
                ? surat.Warga!.Nama!.Trim().ToUpperInvariant()
                : (surat.Instansi?.NamaInstansi ?? string.Empty).Trim().ToUpperInvariant();

            var nik = (surat.Warga?.NIK ?? string.Empty).Trim();

            var isi = string.Join("|", new[]
            {
                (surat.NamaJenis ?? string.Empty).Trim().ToUpperInvariant(),
                (surat.NomorSurat ?? string.Empty).Trim(),
                surat.TanggalSurat.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                nik,
                pemohon
            });

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(isi)));
        }
    }
}
