using System;
using QRCoder;

namespace SuDesApp.Utilities
{
    /// <summary>
    /// Pembuat gambar QR (PNG) untuk kaki surat. Dipisah dari generator PDF
    /// supaya ukuran/format QR bisa diuji sendiri, dan supaya generator yang
    /// tidak mewarisi <c>SuratGeneratorBase</c> tetap bisa memakainya.
    ///
    /// Isi QR ditentukan pemanggil (lihat <c>KodeVerifikasiSurat.BuatPayload</c>);
    /// di sini murni soal gambar. Koreksi galat memakai level M — cukup tahan
    /// noda/terlipat pada kertas, tanpa membuat modul QR terlalu rapat untuk
    /// dicetak printer biasa.
    /// </summary>
    public static class QrCodeSurat
    {
        /// <summary>
        /// Hasilkan PNG QR. <paramref name="piksel"/> adalah perkiraan lebar
        /// gambar; jumlah piksel per modul dihitung otomatis supaya QR tetap
        /// tajam di ukuran berapa pun.
        /// </summary>
        public static byte[] BuatPng(string teks, int piksel = 240)
        {
            if (string.IsNullOrWhiteSpace(teks))
                throw new ArgumentException("Isi QR tidak boleh kosong.", nameof(teks));

            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(teks, QRCodeGenerator.ECCLevel.M);

            int modul = data.ModuleMatrix.Count;
            int perModul = Math.Max(2, piksel / Math.Max(1, modul + 8));

            var qr = new PngByteQRCode(data);
            return qr.GetGraphic(perModul);
        }
    }
}
