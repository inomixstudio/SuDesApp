using SuDesApp.GeneratorPdf;
using Xunit;

namespace SuDesApp.Tests.Surat
{
    /// <summary>
    /// Bentuk alamat dan kalimat keterangan Surat Ijin Tinggal Sementara kini
    /// dibangun dari SATU sumber (IjinTinggalGenerator) yang dipakai bersama
    /// generator PDF dan ViewModel input form — dulu keduanya membangun teks
    /// sendiri dengan format berbeda, sehingga isian tidak pernah persis sama
    /// dengan hasil cetak. Uji ini mengunci bentuk teks bersama itu.
    /// </summary>
    public class IjinTinggalTeksBersamaTests
    {
        [Fact]
        public void AlamatAsalTeks_BentukNyataDanBagianKosongDilewati()
        {
            Assert.Equal(
                "Dusun Sukamaju RT 003/RW 007, Desa Sukamaju, Kecamatan Tempuran, Kabupaten Karawang",
                IjinTinggalGenerator.AlamatAsalTeks(
                    "Sukamaju", "003", "007", "Sukamaju", "Tempuran", "Karawang"));

            // Dusun kosong: tidak ada kata "Dusun" telanjang (bug lama), RT/RW ikut hilang.
            Assert.Equal(
                "Desa Sukamaju, Kecamatan Tempuran, Kabupaten Karawang",
                IjinTinggalGenerator.AlamatAsalTeks("", null, null, "Sukamaju", "Tempuran", "Karawang"));

            // Kosong seluruhnya → kosong, bukan tanda baca nyasar.
            Assert.Equal(string.Empty, IjinTinggalGenerator.AlamatAsalTeks(null, null, null, null, null, null));
        }

        [Fact]
        public void AlamatTujuanTeks_DusunOpsionalDanSatuBaris()
        {
            Assert.Equal(
                "Dusun Pasir Gintung, Desa Sumberjaya, Kecamatan Tempuran, Kabupaten Karawang",
                IjinTinggalGenerator.AlamatTujuanTeks("Pasir Gintung", "Sumberjaya", "Tempuran", "Karawang"));

            Assert.Equal(
                "Desa Sumberjaya, Kecamatan Tempuran, Kabupaten Karawang",
                IjinTinggalGenerator.AlamatTujuanTeks("", "Sumberjaya", "Tempuran", "Karawang"));
        }

        [Fact]
        public void KeteranganTeks_SamaDenganKalimatBaku()
        {
            string teks = IjinTinggalGenerator.KeteranganTeks(
                "Sukamaju", "Tempuran", "Karawang",
                "Dusun Pasir Gintung, Desa Sumberjaya, Kecamatan Tempuran, Kabupaten Karawang");

            Assert.Contains("warga Desa Sukamaju, Kecamatan Tempuran, Kabupaten Karawang.", teks);
            Assert.Contains("bertempat tinggal sementara di Dusun Pasir Gintung, Desa Sumberjaya, "
                            + "Kecamatan Tempuran, Kabupaten Karawang.", teks);
        }

        /// <summary>
        /// Isi tabel pemohon memakai alamat format bersama: satu baris ber-koma,
        /// bukan dua baris format lama yang berbeda dari form.
        /// </summary>
        [Fact]
        public void AlamatAsalTeks_MengenaliRtRwSudahBerlabel()
        {
            // Operator yang mengetik "RT 003" pada kolom RT tidak membuat label ganda.
            Assert.Equal(
                "Dusun Sukamaju RT 003/RW 007, Desa Sukamaju",
                IjinTinggalGenerator.AlamatAsalTeks("Sukamaju", "RT 003", "RW 007", "Sukamaju", "", ""));
        }
    }
}
