using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SuDesApp.Wpf.Utilities
{
    /// <summary>
    /// Satu akun di dalam berkas <c>Login.dat</c>.
    ///
    /// <paramref name="Pusat"/> menandai akun rahasia milik penyedia (kunci menu
    /// Pusat Administrasi): akun bertanda ini hidup HANYA di berkas ini dan tidak
    /// pernah dipindahkan (dimigrasikan) ke tabel <c>Pengguna</c>, sehingga tidak
    /// pernah muncul di halaman Kelola Pengguna.
    /// </summary>
    public sealed record AkunLogin(string Username, string Salt, string Hash, bool Pusat);

    /// <summary>
    /// Berkas akun lokal <c>Login.dat</c>: satu-satunya akun yang hidup di luar
    /// database desa.
    ///
    /// Kenapa berkas ini ada: instalasi lama menyimpan akun administrator di
    /// sini, dan akun rahasia penyedia (lihat <see cref="AkunPusat"/>) memang
    /// tidak boleh masuk tabel <c>Pengguna</c> — kalau masuk, namanya terlihat
    /// (dan bisa diganti/dihapus) oleh Administrator desa.
    ///
    /// Isi berkas DIENKRIPSI dengan DPAPI (<c>DataProtectionScope.CurrentUser</c>):
    /// berkas yang tersalin ke komputer atau akun Windows lain tidak bisa dibaca.
    /// Di dalamnya, kata sandi tetap disimpan sebagai SHA256 bergaram — bukan
    /// teks biasa, tetapi juga bukan PBKDF2 seperti tabel <c>Pengguna</c>.
    ///
    /// Bentuk isi (setelah didekripsi), satu akun per baris:
    /// <code>
    /// username:garam:hash
    /// username:garam:hash:pusat
    /// </code>
    /// Baris tanpa bidang keempat adalah akun biasa (boleh dimigrasikan ke tabel
    /// Pengguna seperti perilaku lama), baris berbidang keempat <c>pusat</c>
    /// adalah akun rahasia penyedia. Berkas versi lama yang hanya memuat satu
    /// baris <c>username:garam:hash</c> tetap terbaca tanpa perlu diubah.
    /// </summary>
    public static class BerkasLogin
    {
        /// <summary>Nama berkas akun lokal di folder aplikasi.</summary>
        public const string NamaBerkas = "Login.dat";

        /// <summary>Penanda bidang keempat untuk akun pusat (rahasia penyedia).</summary>
        public const string PenandaPusat = "pusat";

        /// <summary>
        /// Penunjuk lokasi berkas akun untuk pengujian (dan skenario khusus) —
        /// pola yang sama dengan AppPreferenceStore.LokasiOverride di Core,
        /// tetapi publik di sini karena proyek uji tidak melihat jenis internal
        /// rakitan WPF. Null = lokasi normal di samping executable.
        /// </summary>
        public static Func<string>? LokasiOverride { get; set; }

        /// <summary>Lokasi berkas akun yang dipakai aplikasi: di samping executable, sama seperti versi lama.</summary>
        public static string JalurBawaan =>
            LokasiOverride?.Invoke() ?? Path.Combine(AppContext.BaseDirectory, NamaBerkas);

        /// <summary>Garam acak 16 byte (base64), seperti format berkas yang lama.</summary>
        public static string BuatGaram()
        {
            byte[] garam = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(garam);
            }

            return Convert.ToBase64String(garam);
        }

        /// <summary>
        /// SHA256 dari kata sandi + garam (base64) — sama persis dengan
        /// perhitungan berkas lama, supaya berkas yang sudah ada tetap cocok.
        /// Pemanggil sudah menormalkan huruf besar/kecil kata sandi.
        /// </summary>
        public static string HitungHash(string kataSandi, string garam)
        {
            using var sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(kataSandi + garam));
            return Convert.ToBase64String(hash);
        }

        /// <summary>
        /// Cocokkan kata sandi dengan satu akun. Perbandingan hash memakai
        /// waktu tetap supaya lama pemeriksaan tidak membocorkan berapa banyak
        /// karakter yang sudah benar.
        /// </summary>
        public static bool Cocok(AkunLogin akun, string kataSandi) =>
            CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(HitungHash(kataSandi, akun.Salt)),
                Encoding.UTF8.GetBytes(akun.Hash));

        /// <summary>
        /// Baca seluruh akun dari berkas. Berkas yang tidak ada = daftar kosong.
        /// Baris yang tidak berbentuk akun dilewati (berkas rusak sebagian tidak
        /// boleh mengunci seluruh instalasi); berkas yang tidak bisa didekripsi
        /// dilempar sebagai pengecualian supaya pemanggil bisa melaporkannya.
        /// </summary>
        public static List<AkunLogin> Baca(string jalur)
        {
            if (!File.Exists(jalur))
            {
                return new List<AkunLogin>();
            }

            byte[] terenkripsi = File.ReadAllBytes(jalur);
            byte[] terdekripsi = ProtectedData.Unprotect(
                terenkripsi, null, DataProtectionScope.CurrentUser);

            var akun = new List<AkunLogin>();
            foreach (string baris in Encoding.UTF8.GetString(terdekripsi)
                         .Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var bidang = baris.Trim().Split(':');
                if (bidang.Length is not (3 or 4) || bidang[0].Length == 0)
                {
                    continue;
                }

                bool pusat = bidang.Length == 4
                    && bidang[3].Equals(PenandaPusat, StringComparison.OrdinalIgnoreCase);

                akun.Add(new AkunLogin(bidang[0], bidang[1], bidang[2], pusat));
            }

            return akun;
        }

        /// <summary>Tulis ulang seluruh akun ke berkas (terenkripsi DPAPI).</summary>
        public static void Tulis(string jalur, IEnumerable<AkunLogin> akun)
        {
            var teks = new StringBuilder();
            foreach (var a in akun)
            {
                teks.Append(a.Username).Append(':').Append(a.Salt).Append(':').Append(a.Hash);
                if (a.Pusat)
                {
                    teks.Append(':').Append(PenandaPusat);
                }

                teks.Append('\n');
            }

            byte[] terenkripsi = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(teks.ToString()), null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(jalur, terenkripsi);
        }

        /// <summary>Akun berdasarkan nama pengguna; null bila tidak ada. Nama pengguna tidak peka huruf besar/kecil.</summary>
        public static AkunLogin? Cari(string jalur, string? username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return null;
            }

            string dicari = username.Trim();
            return Baca(jalur)
                .FirstOrDefault(a => a.Username.Equals(dicari, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Pastikan satu akun ada di berkas (dipakai untuk akun bawaan dan akun
        /// pusat). Akun yang sudah ada TIDAK ditimpa — kata sandi yang pernah
        /// berganti tidak boleh dikembalikan ke bawaan hanya karena aplikasi
        /// dibuka lagi. Mengembalikan true bila akun baru benar-benar ditulis.
        /// </summary>
        public static bool PastikanAkun(string jalur, string username, string kataSandi, bool pusat)
        {
            var akun = Baca(jalur);
            if (akun.Any(a => a.Username.Equals(username, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            string garam = BuatGaram();
            akun.Add(new AkunLogin(username, garam, HitungHash(kataSandi, garam), pusat));
            Tulis(jalur, akun);
            return true;
        }
    }
}
