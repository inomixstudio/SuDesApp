using System.Globalization;
using System.Text.RegularExpressions;

namespace SuDesApp.Utilities
{
    public static class Validator
    {
        public static bool ValidateNama(string nama, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (string.IsNullOrWhiteSpace(nama))
            {
                errorMessage = "Nama harus diisi.";
                return false;
            }
            if (nama.Length < 3)
            {
                errorMessage = "Nama minimal 3 karakter.";
                return false;
            }
            if (!Regex.IsMatch(nama, @"^[a-zA-Z\s\.]+$"))
            {
                errorMessage = "Nama hanya boleh berisi huruf dan spasi.";
                return false;
            }
            return true;
        }

        public static bool ValidateTanggalLahir(string tanggalLahir, string fieldName, out string errorMessage)
        {
            errorMessage = string.Empty;

            // Check if the input is empty
            if (string.IsNullOrWhiteSpace(tanggalLahir))
            {
                errorMessage = $"{fieldName} tidak boleh kosong.";
                return false;
            }

            // Validate the date format dd-MM-yyyy
            if (!DateTime.TryParseExact(tanggalLahir, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDate))
            {
                errorMessage = $"{fieldName} harus dalam format dd-MM-yyyy.";
                return false;
            }

            // Check if the date is in the future
            if (parsedDate > DateTime.Now)
            {
                errorMessage = $"{fieldName} tidak boleh di masa depan.";
                return false;
            }

            // If everything is fine, return true
            return true;
        }

        public static bool ValidateNik(string nik, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (string.IsNullOrWhiteSpace(nik))
            {
                errorMessage = "NIK harus diisi.";
                return false;
            }
            if (!Regex.IsMatch(nik, @"^\d{16}$"))
            {
                errorMessage = "NIK harus terdiri dari 16 digit.";
                return false;
            }

            // Validasi opsional pola NIK sesuai format Dukcapil (contoh sederhana)
            // 6 digit pertama = kode wilayah, 6 digit berikutnya = tanggal lahir (ddmmyy), 4 digit terakhir = nomor urut
            string wilayah = nik.Substring(0, 6);
            string tanggalLahirPart = nik.Substring(6, 6);
            string nomorUrut = nik.Substring(12, 4);

            if (!Regex.IsMatch(wilayah, @"^\d{6}$") || !Regex.IsMatch(tanggalLahirPart, @"^\d{6}$") || !Regex.IsMatch(nomorUrut, @"^\d{4}$"))
            {
                errorMessage = "Format NIK tidak sesuai struktur standar Dukcapil.";
                return false;
            }

            return true;
        }

        public static bool ValidateRequired(string? value, string fieldName, out string message)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                message = $"{fieldName} harus diisi.";
                return false;
            }
            // Pesan hanya dibaca ketika validasi gagal; saat sukses cukup diisi kosong.
            message = string.Empty;
            return true;
        }
    }
}
