using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SuDesApp.Utilities
{
    /// <summary>
    /// Berkas preferensi sederhana berformat <c>kunci=nilai</c> per baris, ditulis
    /// secara ATOMIK: isi baru ditulis ke berkas sementara, lalu dipasang dengan
    /// <see cref="File.Replace(string,string,string?)"/>.
    ///
    /// Kenapa tidak <c>File.WriteAllLines</c> langsung: menulis di tempat membuat
    /// berkas terpotong bila proses berhenti di tengah (listrik putus, aplikasi
    /// ditutup paksa). Pembaca lalu jatuh ke nilai bawaan — seluruh pengaturan
    /// hilang, termasuk alamat server lisensi dan kode aktivasi. Dengan penggantian
    /// atomik, pembaca selalu melihat berkas lama yang utuh atau berkas baru yang
    /// utuh; salinan sebelumnya tetap tersedia sebagai <c>.bak</c> bila berkas
    /// utama ternyata rusak.
    ///
    /// Kelas ini hanya mengurus bentuk berkas. Kebijakan kunci milik aplikasi
    /// tetap di <see cref="AppPreferenceStore"/>.
    /// </summary>
    public static class BerkasPreferensi
    {
        /// <summary>Akhiran berkas sementara (tidak pernah dibaca sebagai sumber).</summary>
        public const string AkhiranSementara = ".tmp";

        /// <summary>Akhiran cadangan yang dibuat saat penggantian atomik.</summary>
        public const string AkhiranCadangan = ".bak";

        /// <summary>
        /// Baca nilai satu kunci. Berkas utama dipakai lebih dulu; bila tidak ada,
        /// kosong, atau tidak terbaca, cadangan <c>.bak</c> dicoba. Null bila kunci
        /// tidak ditemukan di keduanya.
        /// </summary>
        public static string? Baca(string path, string key)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            foreach (var kandidat in new[] { path, path + AkhiranCadangan })
            {
                var baris = BacaBaris(kandidat);
                if (baris == null) continue;

                var nilai = Cari(baris, key);
                if (nilai != null) return nilai;
            }

            return null;
        }

        /// <summary>
        /// Simpan satu nilai tanpa merusak kunci lain. Nilai yang sudah ada diganti
        /// di tempatnya; kunci baru ditambahkan di akhir.
        /// </summary>
        public static void Tulis(string path, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Jalur berkas preferensi tidak boleh kosong.", nameof(path));
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Kunci preferensi tidak boleh kosong.", nameof(key));

            string? folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            // Bila berkas utama rusak, mulai dari cadangan agar kunci lain tidak hilang.
            var baris = BacaBaris(path) ?? BacaBaris(path + AkhiranCadangan) ?? new List<string>();

            bool diganti = false;
            for (int i = 0; i < baris.Count; i++)
            {
                int idx = baris[i].IndexOf('=');
                if (idx <= 0) continue;

                if (baris[i].Substring(0, idx).Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    baris[i] = $"{key}={value}";
                    diganti = true;
                    break;
                }
            }

            if (!diganti) baris.Add($"{key}={value}");

            string sementara = path + AkhiranSementara;
            try
            {
                File.WriteAllLines(sementara, baris, new UTF8Encoding(false));
                PasangAtomik(sementara, path);
            }
            finally
            {
                // Berkas sementara yang tertinggal bukan data pengguna; buang bila
                // penggantian gagal sebelum sempat menghapusnya.
                try
                {
                    if (File.Exists(sementara)) File.Delete(sementara);
                }
                catch (IOException)
                {
                    // Berkas bisa masih dipakai pemindai antivirus — biarkan.
                }
            }
        }

        /// <summary>Ganti berkas tujuan dengan isi berkas sementara secara atomik.</summary>
        private static void PasangAtomik(string sementara, string tujuan)
        {
            if (!File.Exists(tujuan))
            {
                File.Move(sementara, tujuan);
                return;
            }

            try
            {
                // File.Replace menulis .bak berisi versi lama, lalu menukar nama
                // dalam satu operasi filesystem. Pembaca tidak pernah melihat
                // berkas setengah jadi.
                File.Replace(sementara, tujuan, tujuan + AkhiranCadangan, ignoreMetadataErrors: true);
            }
            catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or UnauthorizedAccessException)
            {
                // Sebagian filesystem (mis. berbagi jaringan) tidak mendukung
                // Replace. Tulis ulang langsung tetap lebih baik daripada gagal
                // total; jendela tidak-atomik ini hanya berlaku di sana.
                File.Move(sementara, tujuan, overwrite: true);
            }
        }

        /// <summary>Seluruh baris berkas, atau null bila tidak ada/tidak terbaca/kosong.</summary>
        private static List<string>? BacaBaris(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;

                // Berkas 0 byte adalah hasil penulisan lama yang terpotong; jangan
                // dianggap "tidak ada kunci apa pun" bila cadangan masih berisi.
                if (new FileInfo(path).Length == 0) return null;

                return new List<string>(File.ReadAllLines(path, Encoding.UTF8));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static string? Cari(List<string> baris, string key)
        {
            foreach (string line in baris)
            {
                int idx = line.IndexOf('=');
                if (idx <= 0) continue;
                if (!line.Substring(0, idx).Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                    continue;

                return line.Substring(idx + 1).Trim();
            }

            return null;
        }
    }
}
