using System;
using System.Collections.Generic;

namespace SuDesApp.Data.Models
{
    /// <summary>
    /// Nilai contoh (bawaan aplikasi) untuk data desa, beserta pemeriksaannya.
    ///
    /// Data desa yang belum diisi pengguna memakai nilai contoh seperti
    /// "Nama Desa"/"Nama Kepala Desa" supaya formulir dan surat tidak kosong.
    /// Halaman Pengaturan Surat dan notifikasi lonceng memakai pemeriksaan di sini
    /// untuk mengingatkan pengguna mengganti data contoh tersebut dengan data
    /// desanya yang sebenarnya — dan berhenti mengingatkan begitu sudah diisi.
    /// </summary>
    public static class DesaContoh
    {
        public const string NamaDesa = "Nama Desa";
        public const string Kecamatan = "Nama Kecamatan";
        public const string Kabupaten = "Nama Kabupaten";
        public const string Alamat = "Alamat Desa";
        public const string Kodepos = "00000";
        public const string KepalaDesa = "Nama Kepala Desa";
        public const string SekretarisDesa = "Nama Sekretaris Desa";

        /// <summary>Awalan nilai contoh gaya lama (mis. "Nama Desa Sumberjaya").</summary>
        private const string AwalanContoh = "Nama ";

        /// <summary>
        /// Benar bila nilai masih berupa contoh bawaan aplikasi. Nilai kosong
        /// dihitung contoh hanya untuk kolom yang wajib diisi — kolom opsional
        /// (mis. Pejabat Kecamatan) boleh dikosongkan.
        /// </summary>
        public static bool NilaiContoh(string? nilai, string contoh, bool wajib = false)
        {
            string bersih = (nilai ?? string.Empty).Trim();
            if (bersih.Length == 0) return wajib;
            if (bersih.Equals(contoh, StringComparison.OrdinalIgnoreCase)) return true;

            // Sisa penamaan contoh lama: "Nama …" atau "Default …".
            return bersih.StartsWith(AwalanContoh, StringComparison.OrdinalIgnoreCase)
                || bersih.StartsWith("Default", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Data desa masih memakai nilai contoh (belum disesuaikan pengguna) —
        /// dipakai untuk menampilkan peringatan di Pengaturan Surat dan notifikasi.
        /// </summary>
        public static bool MasihContoh(DesaData? desa) => FieldContoh(desa).Count > 0;

        /// <summary>
        /// Nama kolom wajib yang isinya masih contoh/kosong, urut sesuai formulir.
        /// Pejabat Kecamatan tidak pernah masuk daftar ini karena boleh dikosongkan.
        /// </summary>
        public static List<string> FieldContoh(DesaData? desa)
        {
            var hasil = new List<string>();
            if (desa == null)
            {
                hasil.Add("Data desa");
                return hasil;
            }

            void Periksa(string label, string? nilai, string contoh)
            {
                if (NilaiContoh(nilai, contoh, wajib: true)) hasil.Add(label);
            }

            Periksa("Nama Desa", desa.NamaDesa, NamaDesa);
            Periksa("Kecamatan", desa.Kecamatan, Kecamatan);
            Periksa("Kabupaten", desa.Kabupaten, Kabupaten);
            Periksa("Alamat Desa", desa.Alamat, Alamat);
            Periksa("Kepala Desa", desa.KepalaDesa, KepalaDesa);
            Periksa("Sekretaris Desa", desa.SekretarisDesa, SekretarisDesa);

            return hasil;
        }

        /// <summary>Ringkasan singkat untuk pesan notifikasi, mis. "Nama Desa, Kecamatan, dan 2 lainnya".</summary>
        public static string RingkasField(IReadOnlyList<string> field)
        {
            if (field == null || field.Count == 0) return string.Empty;
            if (field.Count == 1) return field[0];
            if (field.Count == 2) return $"{field[0]} dan {field[1]}";

            int sisa = field.Count - 2;
            return $"{field[0]}, {field[1]}, dan {(sisa == 1 ? "1 kolom lain" : $"{sisa} kolom lain")}";
        }
    }
}
