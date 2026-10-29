namespace SuDesApp.GeneratorPdf
{
    /// <summary>
    /// Perataan teks yang dipakai seluruh primitive tata letak surat
    /// (<see cref="BadanSurat"/> dan <see cref="SuratRenderer"/>).
    ///
    /// Jenis tersendiri — bukan bagian dari <see cref="KerapatanSurat"/> — supaya
    /// perataan bisa dipakai sebagai pola <c>switch</c> dan tidak tercampur dengan
    /// pengaturan jarak baris.
    /// </summary>
    public enum Rata
    {
        Kiri,
        Tengah,
        Kanan,
        Justify
    }
}
