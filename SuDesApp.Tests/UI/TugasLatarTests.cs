using System;
using System.Threading;
using System.Threading.Tasks;
using SuDesApp.Wpf.Mvvm;
using Xunit;

namespace SuDesApp.Tests.UI
{
    /// <summary>
    /// Tugas latar yang tidak ditunggu pemanggil harus selesai tanpa melempar
    /// dan tanpa menggantung — kegagalannya dicatat, bukan menghilang.
    /// </summary>
    public class TugasLatarTests
    {
        [Fact]
        public async Task JalankanAsync_menelan_galat_dan_selesai()
        {
            await TugasLatar.JalankanAsync(
                () => Task.FromException(new InvalidOperationException("kegagalan uji")),
                "uji tugas latar");
        }

        [Fact]
        public async Task JalankanAsync_mengabaikan_pembatalan()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await TugasLatar.JalankanAsync(
                () => Task.FromCanceled(cts.Token),
                "uji pembatalan");
        }

        [Fact]
        public async Task JalankanAsync_menunggu_tugas_selesai()
        {
            bool selesai = false;

            await TugasLatar.JalankanAsync(
                async () =>
                {
                    await Task.Delay(10);
                    selesai = true;
                },
                "uji sukses");

            Assert.True(selesai);
        }
    }
}
