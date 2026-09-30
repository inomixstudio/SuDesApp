namespace SuDesApp.GeneratorPdf
{
    /// <summary>
    /// Satu-satunya tempat tingkat lisensi QuestPDF ditetapkan untuk aplikasi ini.
    ///
    /// QuestPDF menolak menggambar dokumen apa pun bila
    /// <c>QuestPDF.Settings.License</c> belum diisi, dengan pesan
    /// "Please configure the QuestPDF license by setting 'QuestPDF.Settings.License'
    /// at application startup". Karena itu <see cref="Pastikan"/> dipanggil dari dua sisi:
    /// startup aplikasi (sebelum jendela pertama tampil) dan static constructor setiap
    /// kelas yang menggambar dokumen. Panggilan ganda tidak berbahaya — penetapannya
    /// idempoten dan tidak menimpa tingkat yang sudah diisi.
    ///
    /// Penulisan tingkat lisensi <b>hanya boleh ada di berkas ini</b>. Ada uji penjaga yang
    /// menolak berkas lain yang menulisnya
    /// (<c>LisensiQuestPdfTests.PenulisanLisensiQuestPdf_HanyaDiBerkasPenetapan</c>), jadi
    /// generator baru cukup memanggil <see cref="Pastikan"/> — tidak perlu dan tidak boleh
    /// menyalin baris penetapan ke dalam generatornya. Justru kebiasaan menyalin itulah yang
    /// dulu membuat satu generator baru gagal mencetak dokumen pertamanya.
    ///
    /// Tidak memakai <c>[ModuleInitializer]</c>: analyzer CA2255 menolaknya untuk
    /// pustaka (assembly yang bukan aplikasi) dan memang sebaiknya tidak ada kode yang
    /// berjalan sendiri saat DLL dimuat. Riwayat kenapa ini dipusatkan: generator
    /// pembuat SK perangkat tidak mewarisi <see cref="SuratGeneratorBase"/> dan lupa
    /// menulis baris lisensi, sehingga SK pertama setelah aplikasi dibuka gagal
    /// ("Please configure the QuestPDF license…") sedangkan SK berikutnya berhasil —
    /// kegagalan yang bergantung urutan pemakaian.
    /// </summary>
    public static class QuestPdfLisensi
    {
        /// <summary>Tingkat lisensi aplikasi ini: Community (gratis untuk pemerintahan desa).</summary>
        public const QuestPDF.Infrastructure.LicenseType Tingkat =
            QuestPDF.Infrastructure.LicenseType.Community;

        /// <summary>
        /// Pastikan lisensi terpasang. Idempoten: aman dipanggil berkali-kali, dan
        /// tidak menimpa tingkat yang sudah diisi lebih dulu.
        /// </summary>
        public static void Pastikan()
        {
            if (QuestPDF.Settings.License == null)
            {
                QuestPDF.Settings.License = Tingkat;
            }
        }
    }
}
