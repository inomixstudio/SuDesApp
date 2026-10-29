using SuDesApp.Services;
using Xunit;

namespace SuDesApp.Tests.Idm;

/// <summary>
/// Pembacaan jawaban <c>idm.kemendesa.go.id/open/api/desa/rumusan/{kode}/{tahun}</c>.
/// Angka yang tampil di halaman dan tercetak di laporan harus persis angka pusat,
/// jadi uji ini menjaga dua hal yang mudah rusak tanpa terlihat: skor tidak boleh
/// dihitung sendiri dari IKS/IKE/IKL, dan baris subtotal hanya dikenali dari
/// namanya — bukan sekadar dari ada/tidaknya nomor.
/// </summary>
public sealed class IdmPusatTests
{
    private const string JawabanLengkap = """
        {
          "error": false,
          "mapData": {
            "IDENTITAS": [
              {
                "nama_provinsi": "JAWA BARAT",
                "nama_kab_kota": "BEKASI",
                "nama_kecamatan": "CIKARANG",
                "nama_desa": "SUKAMAJU",
                "id_desa": "3215022001"
              }
            ],
            "SUMMARIES": {
              "SKOR_SAAT_INI": 0.7444,
              "STATUS": "MAJU",
              "TARGET_STATUS": "MANDIRI",
              "SKOR_MINIMAL": 0.8,
              "PENAMBAHAN": 0.0556,
              "TAHUN": 2024
            },
            "ROW": [
              { "NO": 1, "INDIKATOR": "Pendidikan dasar tersedia", "SKOR": "4" },
              { "NO": 2, "INDIKATOR": "Fasilitas kesehatan", "SKOR": "3", "KETERANGAN": "Puskesmas" },
              { "INDIKATOR": "IKS 2024", "SKOR": "0.68" },
              { "NO": 3, "INDIKATOR": "Akses jalan", "SKOR": "2" },
              { "INDIKATOR": "IKE 2024", "SKOR": "0.75" },
              { "NO": 4, "INDIKATOR": "Kelembagaan dan pelayanan", "SKOR": "5" },
              { "INDIKATOR": "IKL 2024", "SKOR": "0.80" },
              { "INDIKATOR": "IDM 2024", "SKOR": "0.7444" },
              { "INDIKATOR": "STATUS", "SKOR": "MAJU" }
            ]
          }
        }
        """;

    [Fact]
    public void Urai_membaca_ringkasan_dan_mengisi_bidang_dari_baris_subtotal()
    {
        var muatan = IdmKemendesaBaca.Urai(JawabanLengkap, 2024);

        Assert.Equal(StatusMuatIdm.Berhasil, muatan.Status);
        var hasil = Assert.IsType<HasilIdm>(muatan.Hasil);

        Assert.Equal(2024, hasil.Ringkasan.Tahun);
        Assert.Equal(0.7444, hasil.Ringkasan.Skor, 4);
        Assert.Equal("MAJU", hasil.Ringkasan.Status);
        Assert.Equal("MANDIRI", hasil.Ringkasan.TargetStatus);
        Assert.Equal(0.8, hasil.Ringkasan.SkorMinimal);
        Assert.Equal(0.68, hasil.Ringkasan.Iks);
        Assert.Equal(0.75, hasil.Ringkasan.Ike);
        Assert.Equal(0.8, hasil.Ringkasan.Ikl);

        Assert.Equal("SUKAMAJU", hasil.Identitas?.Desa);
        Assert.Equal("3215022001", hasil.Identitas?.KodeDesa);

        Assert.Equal(9, hasil.Baris.Count);
        Assert.Equal("IKS", hasil.Baris[0].Bidang);
        Assert.Equal("IKS", hasil.Baris[1].Bidang);
        Assert.Equal("IKE", hasil.Baris[3].Bidang);
        Assert.Equal("IKL", hasil.Baris[5].Bidang);

        // Lima baris subtotal resmi: IKS, IKE, IKL, IDM, STATUS.
        Assert.Equal(5, hasil.Baris.Count(b => b.Ringkasan));
        Assert.Equal("0,68", hasil.Baris[2].SkorTampil);
    }

    [Fact]
    public void Urai_tidak_menghitung_skor_dari_rata_rata_iks_ike_ikl()
    {
        // SUMMARIES tanpa SKOR_SAAT_INI dan tanpa baris subtotal IDM: pusat tidak
        // menerbitkan skor, jadi aplikasi tidak boleh mengarang skor dari rata-rata.
        const string jawaban = """
            {
              "error": false,
              "mapData": {
                "SUMMARIES": { "STATUS": "MAJU", "TAHUN": 2024 },
                "ROW": [
                  { "INDIKATOR": "IKS 2024", "SKOR": "0.68" },
                  { "INDIKATOR": "IKE 2024", "SKOR": "0.75" },
                  { "INDIKATOR": "IKL 2024", "SKOR": "0.80" }
                ]
              }
            }
            """;

        var muatan = IdmKemendesaBaca.Urai(jawaban, 2024);

        Assert.Equal(StatusMuatIdm.Gagal, muatan.Status);
        Assert.Contains("tidak memuat skor IDM", muatan.Pesan);
        Assert.Null(muatan.Hasil);
    }

    [Fact]
    public void Urai_baris_tanpa_nomor_yang_bukan_subtotal_tetap_indikator()
    {
        // NO bisa dikirim sebagai teks oleh pusat. Baris seperti itu bukan
        // subtotal, sehingga tidak boleh ditebalkan sebagai ringkasan.
        const string jawaban = """
            {
              "error": false,
              "mapData": {
                "SUMMARIES": { "SKOR_SAAT_INI": 0.6, "STATUS": "BERKEMBANG", "TAHUN": 2024 },
                "ROW": [
                  { "NO": "1", "INDIKATOR": "Indikator bernomor teks", "SKOR": "3" },
                  { "INDIKATOR": "IKS 2024", "SKOR": "0.6" }
                ]
              }
            }
            """;

        var hasil = IdmKemendesaBaca.Urai(jawaban, 2024).Hasil;

        Assert.NotNull(hasil);
        Assert.False(hasil!.Baris[0].Ringkasan);
        Assert.Equal("IKS", hasil.Baris[0].Bidang);
        Assert.True(hasil.Baris[1].Ringkasan);
    }

    [Fact]
    public void Urai_mengembalikan_tidak_ditemukan_saat_pusat_menjawab_galat()
    {
        const string jawaban = """{ "error": true, "message": "ID Desa tidak ditemukan" }""";

        var muatan = IdmKemendesaBaca.Urai(jawaban, 2026);

        Assert.Equal(StatusMuatIdm.TidakDitemukan, muatan.Status);
        Assert.Equal("ID Desa tidak ditemukan", muatan.Pesan);
        Assert.Null(muatan.Hasil);
    }

    [Fact]
    public void Urai_jawaban_bukan_json_dianggap_gagal()
    {
        var muatan = IdmKemendesaBaca.Urai("<html>maintenance</html>", 2024);

        Assert.Equal(StatusMuatIdm.Gagal, muatan.Status);
        Assert.Contains("bukan JSON", muatan.Pesan);
    }
}
