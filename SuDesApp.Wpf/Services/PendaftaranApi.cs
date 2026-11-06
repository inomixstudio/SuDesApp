using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SuDesApp.Wpf.Services
{
    /// <summary>
    /// Registrasi layanan untuk API Desa (listener HTTP lokal di komputer kantor).
    /// Dipisah dari <c>App.Layanan.cs</c> supaya komposisinya bisa diuji: endpoint
    /// <c>/statistik</c>, <c>/perangkat-desa</c>, dan <c>/rekap-bulanan</c> menjawab
    /// 500 bila <see cref="SuDesApp.Api.IApiRingkasanService"/> tidak bisa dibangun
    /// karena salah satu dependensinya hilang dari kontainer.
    /// </summary>
    public static class PendaftaranApi
    {
        public static void Daftar(IServiceCollection services)
        {
            // Layanan per permintaan dibuat lewat scope dari provider, jadi tidak
            // ada layanan scoped yang ikut tertahan hidup selama listener berdiri.
            services.AddScoped<SuDesApp.Api.IApiRingkasanService, SuDesApp.Api.ApiRingkasanService>();
            services.AddScoped<SuDesApp.Services.IVerifikasiSuratService, SuDesApp.Services.VerifikasiSuratService>();

            // Listener singleton: halaman API menyalakannya, dan pengaturannya
            // dibaca dari preferensi (bukan database) agar bisa dibuka sebelum login.
            services.AddSingleton(sp => new SuDesApp.Api.ApiListener(
                () => sp, sp.GetRequiredService<ILogger<SuDesApp.Api.ApiListener>>()));
        }
    }
}
