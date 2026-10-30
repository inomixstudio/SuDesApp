using System;
using System.Threading;
using System.Windows.Controls;
using SuDesApp.Wpf.Utilities;
using Xunit;

namespace SuDesApp.Tests.UI
{
    /// <summary>
    /// Kolom tanggal harus menyusun sendiri tanda hubung supaya pengguna cukup
    /// mengetik deretan angka (mis. 01011990) dan melihat 01-01-1990.
    /// </summary>
    public class TanggalIsianOtomatisTests
    {
        [Theory]
        [InlineData("01011990", "01-01-1990")]
        [InlineData("010119901234", "01-01-1990")]   // angka berlebih dipotong
        [InlineData("0101", "01-01-")]               // pemisah bulan langsung muncul
        [InlineData("01", "01-")]                    // pemisah pertama langsung muncul
        [InlineData("0", "0")]
        [InlineData("ab01cd01ef1990", "01-01-1990")] // huruf dari tempel dibuang
        [InlineData("01-01-1990", "01-01-1990")]     // tanda hubung yang sudah ada dirapikan
        [InlineData("", "")]
        public void Isian_tanggal_dirakit_dengan_tanda_hubung(string masukan, string diharapkan)
        {
            Assert.Equal(diharapkan, Ketik(masukan));
        }

        [Fact]
        public void Kolom_dibatasi_sepuluh_karakter()
        {
            Assert.Equal(10, BatasPanjang());
        }

        /// <summary>TextBox hanya bisa dibuat di thread STA.</summary>
        private static string Ketik(string masukan)
        {
            string hasil = null;
            Exception galat = null;

            var utas = new Thread(() =>
            {
                try
                {
                    var kolom = new TextBox();
                    TanggalIsianOtomatis.SetAktif(kolom, true);
                    kolom.Text = masukan;
                    hasil = kolom.Text;
                }
                catch (Exception ex)
                {
                    galat = ex;
                }
            });

            utas.SetApartmentState(ApartmentState.STA);
            utas.Start();
            utas.Join();

            if (galat is not null) throw galat;
            return hasil;
        }

        private static int BatasPanjang()
        {
            int hasil = 0;
            var utas = new Thread(() =>
            {
                var kolom = new TextBox();
                TanggalIsianOtomatis.SetAktif(kolom, true);
                hasil = kolom.MaxLength;
            });
            utas.SetApartmentState(ApartmentState.STA);
            utas.Start();
            utas.Join();
            return hasil;
        }
    }
}
