using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Api;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Services;
using Xunit;

namespace SuDesApp.Tests.Api
{
    /// <summary>
    /// Penjaga wiring API Desa. Ketiga endpoint data (<c>/statistik</c>,
    /// <c>/perangkat-desa</c>, <c>/rekap-bulanan</c>) membangun
    /// <see cref="IApiRingkasanService"/> di dalam scope permintaan. Bila
    /// dependensi layanan itu hilang dari kontainer, endpoint menjawab 500 —
    /// sementara tombol "Uji Koneksi" di halaman API hanya menguji
    /// <c>/status</c>, sehingga halaman tetap menampilkan "API aktif".
    /// </summary>
    /// <remarks>
    /// Satu koleksi dengan <see cref="ApiListenerSesiTests"/>: keduanya mengalihkan
    /// lokasi kunci API dan preferensi yang bersifat statis proses, dan keduanya
    /// membuka HttpListener di port bebas — jalankan berurutan supaya port tidak
    /// saling tabrakan.
    /// </remarks>
    [Collection("ApiListener")]
    public class ApiDesaWiringTests
    {
        [Fact]
        public void IApiRingkasanService_bisa_dibangun_dari_registrasi_api()
        {
            var layanan = new ServiceCollection();

            // Dependensi IApiRingkasanService yang didaftarkan desktop (App.Layanan.cs).
            // Proksi antarmuka dipakai sebagai pengganti nyata: uji ini hanya memastikan
            // layanannya bisa dibangun, bukan menjalankan logika statistik.
            layanan.AddScoped<IWargaRepository>(_ => Proksi.Buat<IWargaRepository>());
            layanan.AddScoped<ISuratRepository>(_ => Proksi.Buat<ISuratRepository>());
            layanan.AddScoped<IPerangkatDesaService>(_ => Proksi.Buat<IPerangkatDesaService>());
            layanan.AddSingleton<ILogger<ApiRingkasanService>>(NullLogger<ApiRingkasanService>.Instance);

            PendaftaranApi.Daftar(layanan);

            using var provider = layanan.BuildServiceProvider();
            using var scope = provider.CreateScope();

            var svc = scope.ServiceProvider.GetRequiredService<IApiRingkasanService>();
            Assert.NotNull(svc);
        }

        [Fact]
        public async Task Endpoint_data_api_menjawab_200_dan_amplop_sukses()
        {
            await using var lingkungan = new LingkunganDataApi();

            foreach (var jalur in new[] { "statistik", "perangkat-desa", "rekap-bulanan" })
            {
                using var balasan = await lingkungan.Klien.GetAsync(
                    $"/sudes/api/v1/{jalur}", lingkungan.Batal);

                Assert.Equal(HttpStatusCode.OK, balasan.StatusCode);

                var isi = await balasan.Content.ReadAsStringAsync(lingkungan.Batal);
                using var dokumen = JsonDocument.Parse(isi);
                Assert.True(dokumen.RootElement.GetProperty("sukses").GetBoolean(),
                    $"{jalur} harus menjawab sukses: {isi}");
            }
        }

        /// <summary>Provider minimal yang mendaftarkan IApiRingkasanService palsu.</summary>
        private sealed class LingkunganDataApi : IAsyncDisposable
        {
            private readonly string _folder;
            private readonly bool _apiAktifAwal;
            private readonly int _portAwal;
            private readonly Func<string>? _lokasiKunciAwal;
            private readonly Func<string>? _lokasiPreferensiAwal;
            private readonly ServiceProvider _provider;

            public LingkunganDataApi()
            {
                _folder = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "sudes-api-wiring-" + Guid.NewGuid().ToString("N"));
                System.IO.Directory.CreateDirectory(_folder);

                _lokasiKunciAwal = ApiKunci.LokasiOverride;
                _lokasiPreferensiAwal = AppPreferenceStore.LokasiOverride;
                _apiAktifAwal = ApiPengaturan.Aktif;
                _portAwal = ApiPengaturan.Port;

                ApiKunci.LokasiOverride = () => System.IO.Path.Combine(_folder, "ApiKunci.bin");
                AppPreferenceStore.LokasiOverride = () => System.IO.Path.Combine(_folder, "preferensi.json");

                ApiKunci.Simpan(ApiUjiLingkungan.Kunci);
                ApiPengaturan.SetBatasPermintaan(600);
                ApiPengaturan.SetCatatAktivitas(false);
                ApiPengaturan.SetIzinkanJaringan(false);
                ApiPengaturan.SetPort(PortBebas());
                ApiPengaturan.SetAktif(true);

                var layanan = new ServiceCollection();
                layanan.AddSingleton<IApiRingkasanService>(new RingkasanPalsu());
                layanan.AddSingleton<IPenggunaService>(new PenggunaApiUji());
                layanan.AddSingleton<IDesaRepository>(new DesaApiUji());
                _provider = layanan.BuildServiceProvider();

                Listener = new ApiListener(() => _provider, NullLogger<ApiListener>.Instance);
                if (!Listener.Mulai())
                    throw new InvalidOperationException("Listener API gagal dinyalakan: " + Listener.GalatTerakhir);

                Klien = new HttpClient
                {
                    BaseAddress = new Uri($"http://localhost:{ApiPengaturan.Port}"),
                    Timeout = TimeSpan.FromSeconds(15)
                };
                Klien.DefaultRequestHeaders.Add("X-Api-Key", ApiUjiLingkungan.Kunci);

                Batal = new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token;
            }

            public ApiListener Listener { get; }
            public HttpClient Klien { get; }
            public CancellationToken Batal { get; }

            public async ValueTask DisposeAsync()
            {
                Klien.Dispose();
                Listener.Berhenti();
                Listener.Dispose();
                _provider.Dispose();

                ApiPengaturan.SetAktif(_apiAktifAwal);
                ApiPengaturan.SetPort(_portAwal);
                ApiKunci.LokasiOverride = _lokasiKunciAwal;
                AppPreferenceStore.LokasiOverride = _lokasiPreferensiAwal;

                try { System.IO.Directory.Delete(_folder, recursive: true); }
                catch (IOException)
                {
                    // Berkas uji masih dipegang proses lain; folder sementara boleh tertinggal.
                }
            }

            private static int PortBebas()
            {
                var pendengar = new TcpListener(IPAddress.Loopback, 0);
                pendengar.Start();
                int port = ((IPEndPoint)pendengar.LocalEndpoint).Port;
                pendengar.Stop();
                return port is >= 1025 and <= 65535 ? port : 8791;
            }
        }

        /// <summary>
        /// Pengganti implementasi antarmuka yang tidak perlu benar di uji wiring:
        /// setiap metode mengembalikan nilai bawaan (0/kosong/Task selesai).
        /// </summary>
        private static class Proksi
        {
            public static T Buat<T>() where T : class => DispatchProxy.Create<T, ProksiDispatch>();

            private class ProksiDispatch : DispatchProxy
            {
                protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
                {
                    var returnType = targetMethod?.ReturnType ?? typeof(void);

                    if (returnType == typeof(Task))
                        return Task.CompletedTask;

                    if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
                    {
                        var hasil = returnType.GetGenericArguments()[0];
                        var nilai = DefaultUntuk(hasil);
                        var fromResult = typeof(Task)
                            .GetMethod(nameof(Task.FromResult))!
                            .MakeGenericMethod(hasil);
                        return fromResult.Invoke(null, new[] { nilai });
                    }

                    return DefaultUntuk(returnType);
                }

                private static object? DefaultUntuk(Type t)
                {
                    if (t == typeof(string)) return string.Empty;
                    if (t.IsValueType) return Activator.CreateInstance(t);
                    return null;
                }
            }
        }

        private sealed class RingkasanPalsu : IApiRingkasanService
        {
            public Task<ApiRingkasan> AmbilAsync(CancellationToken ct = default)
                => Task.FromResult(new ApiRingkasan { TotalWarga = 7, SuratTotal = 3 });

            public Task<IReadOnlyList<ApiPerangkatRingkas>> AmbilPerangkatAsync(
                bool hanyaAktif = true, CancellationToken ct = default)
                => Task.FromResult<IReadOnlyList<ApiPerangkatRingkas>>(Array.Empty<ApiPerangkatRingkas>());

            public IReadOnlyList<string> AmbilJenisSuratOnline() => Array.Empty<string>();

            public Task<ApiRekapBulanan> AmbilRekapBulananAsync(int? tahun = null, CancellationToken ct = default)
                => Task.FromResult(new ApiRekapBulanan { Tahun = tahun ?? 2026 });
        }
    }
}
