using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Services;
using Xunit;

namespace SuDesApp.Tests.Licensing;

/// <summary>
/// Alamat server lisensi menentukan server yang dituju SEMUA panggilan (status,
/// katalog, tagihan, dan halaman <c>/admin</c> milik menu Pusat Administrasi).
/// Baris menu itu menampilkan alamat tersebut di sidebar dan menyegarkan
/// teksnya lewat <see cref="LisensiManager.AlamatBerubah"/>.
///
/// Yang dijaga di sini: kabar itu menyala HANYA saat alamat benar-benar
/// berganti — supaya sidebar tidak berkedip tanpa sebab, dan tidak tertinggal
/// menampilkan server lama setelah alamat diganti di halaman Langganan.
/// </summary>
public sealed class AlamatLisensiBerubahTests
{
    [Fact]
    public void Mengganti_alamat_memberi_tahu_pendengar_dan_nilai_yang_sama_tidak()
    {
        var lisensi = new LisensiManager(NullLogger<LisensiManager>.Instance, new HttpClient());
        int pemberitahuan = 0;
        lisensi.AlamatBerubah += () => pemberitahuan++;

        lisensi.UrlServer = lisensi.UrlServer;
        Assert.Equal(0, pemberitahuan);

        lisensi.UrlServer = "https://lisensi.contoh.go.id";
        Assert.Equal(1, pemberitahuan);
        Assert.Equal("https://lisensi.contoh.go.id/admin", lisensi.AlamatPusatAdministrasi());

        // Server uji memakai port: sidebar harus ikut menyebutkannya agar tidak
        // tertukar dengan produksi.
        lisensi.UrlServer = "http://localhost:5000/";
        Assert.Equal(2, pemberitahuan);
        Assert.Equal("http://localhost:5000/admin", lisensi.AlamatPusatAdministrasi());
    }
}
