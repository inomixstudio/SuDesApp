using SuDesApp.Services;
using Xunit;

namespace SuDesApp.Tests.Licensing
{
    /// <summary>
    /// Normalisasi kode aktivasi manual: pencarian server sensitif huruf
    /// besar, sedangkan salinan WhatsApp sering membawa spasi dan huruf
    /// kecil. Tanpa normalisasi ini kode yang benar pun ditolak
    /// ("tidak dikenal").
    /// </summary>
    public sealed class FormatKodeAktivasiTests
    {
        [Fact]
        public void Kode_resmi_tidak_berubah()
        {
            Assert.Equal("SUDES-K7Q2-4831", FormatKodeAktivasi.Normalisasi("SUDES-K7Q2-4831"));
            Assert.True(FormatKodeAktivasi.BentukSah("SUDES-K7Q2-4831"));
        }

        [Fact]
        public void Huruf_kecil_dan_spasi_dirapikan()
        {
            Assert.Equal("SUDES-K7Q2-4831", FormatKodeAktivasi.Normalisasi(" sudes-k7q2-4831 "));
            Assert.Equal("SUDES-K7Q2-4831", FormatKodeAktivasi.Normalisasi("sudes k7q2 4831"));
            Assert.Equal("SUDES-K7Q2-4831", FormatKodeAktivasi.Normalisasi("SUDES\nK7Q2\t4831"));
        }

        [Fact]
        public void Tanda_hubung_hilang_dan_ganda_dirapikan()
        {
            Assert.Equal("SUDES-K7Q2-4831", FormatKodeAktivasi.Normalisasi("SUDESK7Q24831"));
            Assert.Equal("SUDES-K7Q2-4831", FormatKodeAktivasi.Normalisasi("SUDES-K7Q24831"));
            Assert.Equal("SUDES-K7Q2-4831", FormatKodeAktivasi.Normalisasi("SUDES--K7Q2--4831"));
        }

        [Fact]
        public void Kosong_dan_tidak_berbentuk_tidak_diterima()
        {
            Assert.Equal(string.Empty, FormatKodeAktivasi.Normalisasi(null));
            Assert.Equal(string.Empty, FormatKodeAktivasi.Normalisasi("   "));
            Assert.False(FormatKodeAktivasi.BentukSah(null));
            Assert.False(FormatKodeAktivasi.BentukSah(""));
            Assert.False(FormatKodeAktivasi.BentukSah("SUDES-K7Q2")); // segmen kurang
            Assert.False(FormatKodeAktivasi.BentukSah("K7Q2-4831")); // tanpa awalan
            Assert.False(FormatKodeAktivasi.BentukSah("XXXX-XXXX")); // awalan salah
        }

        [Fact]
        public void Bentuk_sah_dengan_huruf_kecil_lolos_setelah_dinormalisasi()
        {
            Assert.True(FormatKodeAktivasi.BentukSah("sudes-k7q2-4831"));
            Assert.True(FormatKodeAktivasi.BentukSah("sudes k7q2 4831"));
        }
    }
}
