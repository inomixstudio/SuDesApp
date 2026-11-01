using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Api;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Services;
using SuDesApp.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SuDesApp.Tests.Api
{
    /// <summary>
    /// Lingkungan uji bersama untuk endpoint API: kunci API dan preferensi
    /// dialihkan ke folder sementara, lalu listener sungguhan dinyalakan di
    /// localhost pada port bebas. Dipakai tes yang memanggil HTTP langsung
    /// maupun tes yang memakai klien ponsel <c>ApiDesktop</c>.
    ///
    /// Layanan yang disediakan adalah pengganti sederhana: yang diuji jalur HTTP,
    /// pemeriksaan kunci/sesi, dan pemetaan data — bukan PBKDF2 desktop.
    /// </summary>
    internal sealed class ApiUjiLingkungan : IDisposable
    {
        /// <summary>Kunci API yang dipasang untuk seluruh uji di lingkungan ini.</summary>
        public const string Kunci = "kunci-uji-1234567890";

        /// <summary>Akun tunggal yang dikenal <see cref="PenggunaApiUji"/>.</summary>
        public const string Username = "budi";

        public const string KataSandi = "rahasia123";

        private readonly Func<string>? _lokasiKunciAwal;
        private readonly Func<string>? _lokasiPreferensiAwal;
        private readonly bool _apiAktifAwal;
        private readonly int _portAwal;
        private readonly ServiceProvider _provider;

        public ApiUjiLingkungan()
        {
            _lokasiKunciAwal = ApiKunci.LokasiOverride;
            _lokasiPreferensiAwal = AppPreferenceStore.LokasiOverride;
            _apiAktifAwal = ApiPengaturan.Aktif;
            _portAwal = ApiPengaturan.Port;

            Folder = Path.Combine(Path.GetTempPath(), "sudes-api-uji-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);

            ApiKunci.LokasiOverride = () => Path.Combine(Folder, "ApiKunci.bin");
            AppPreferenceStore.LokasiOverride = () => Path.Combine(Folder, "preferensi.json");

            ApiKunci.Simpan(Kunci);
            ApiPengaturan.SetBatasPermintaan(600);
            ApiPengaturan.SetCatatAktivitas(false);
            ApiPengaturan.SetIzinkanJaringan(false);
            ApiPengaturan.SetPort(PortBebas());
            ApiPengaturan.SetAktif(true);

            var layanan = new ServiceCollection();
            layanan.AddSingleton<IPenggunaService>(new PenggunaApiUji());
            layanan.AddSingleton<IDesaRepository>(new DesaApiUji());
            _provider = layanan.BuildServiceProvider();

            Listener = new ApiListener(() => _provider, NullLogger<ApiListener>.Instance);
            if (!Listener.Mulai())
                throw new InvalidOperationException("Listener API gagal dinyalakan: " + Listener.GalatTerakhir);

            Alamat = $"http://localhost:{ApiPengaturan.Port}";
            Klien = new HttpClient
            {
                BaseAddress = new Uri(Alamat),
                Timeout = TimeSpan.FromSeconds(15)
            };

            // Kunci API dikirim pada semua permintaan; endpoint yang menguji
            // ketiadaannya memakai klien terpisah tanpa header ini.
            Klien.DefaultRequestHeaders.Add("X-Api-Key", Kunci);
        }

        /// <summary>Folder sementara bersama untuk berkas uji.</summary>
        public string Folder { get; }

        /// <summary>Alamat dasar listener, mis. <c>http://localhost:53123</c>.</summary>
        public string Alamat { get; }

        public ApiListener Listener { get; }

        public HttpClient Klien { get; }

        public static readonly JsonSerializerOptions OpsiUji = new(JsonSerializerDefaults.Web);

        /// <summary>Masuk lewat HTTP dan balikan token sesinya.</summary>
        public async Task<string> MasukAsync(CancellationToken ct = default)
        {
            using var jawaban = await Klien.PostAsJsonAsync(
                "/sudes/api/v1/sesi",
                new { username = Username, kata_sandi = KataSandi },
                OpsiUji,
                ct);

            string isi = await jawaban.Content.ReadAsStringAsync(ct);
            if (!jawaban.IsSuccessStatusCode)
                throw new InvalidOperationException($"Login uji gagal: {(int)jawaban.StatusCode} {isi}");

            using var dokumen = JsonDocument.Parse(isi);
            return dokumen.RootElement.GetProperty("data").GetProperty("token").GetString()!;
        }

        /// <summary>Permintaan memakai token tertentu, tanpa mengubah header klien utama.</summary>
        public async Task<HttpResponseMessage> DenganTokenAsync(
            HttpMethod metode, string jalur, string token, CancellationToken ct = default)
        {
            var permintaan = new HttpRequestMessage(metode, jalur);
            permintaan.Headers.Add("Authorization", "Bearer " + token);
            return await Klien.SendAsync(permintaan, ct);
        }

        public void Dispose()
        {
            Klien.Dispose();
            Listener.Berhenti();
            Listener.Dispose();
            _provider.Dispose();

            ApiPengaturan.SetAktif(_apiAktifAwal);
            ApiPengaturan.SetPort(_portAwal);
            ApiKunci.LokasiOverride = _lokasiKunciAwal;
            AppPreferenceStore.LokasiOverride = _lokasiPreferensiAwal;

            try
            {
                Directory.Delete(Folder, recursive: true);
            }
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

            // Port 1025+ agar lolos ApiPengaturan.NormalisasiPort.
            return port is >= 1025 and <= 65535 ? port : 8791;
        }
    }

    /// <summary>Pengguna sungguhan hanya untuk jalur login: satu akun, satu sandi.</summary>
    internal sealed class PenggunaApiUji : IPenggunaService
    {
        private static readonly Pengguna Akun = new()
        {
            ID = 1,
            Username = ApiUjiLingkungan.Username,
            NamaTampilan = "Budi Santoso",
            Peran = PeranPengguna.Administrator,
            Aktif = true
        };

        public Task<bool> AdaPenggunaAsync(CancellationToken ct = default) => Task.FromResult(true);

        public Task<HasilMasukPengguna> MasukAsync(
            string username, string kataSandi, CancellationToken ct = default)
        {
            var cocok = string.Equals(username, ApiUjiLingkungan.Username, StringComparison.OrdinalIgnoreCase)
                && kataSandi == ApiUjiLingkungan.KataSandi;

            return Task.FromResult(cocok
                ? HasilMasukPengguna.Sukses(Akun)
                : HasilMasukPengguna.Gagal("Nama pengguna atau kata sandi salah."));
        }

        public Task<List<Pengguna>> DaftarAsync(CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Pengguna?> AmbilAsync(string username, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Pengguna> TambahAsync(
            string username, string namaTampilan, string peran, string kataSandi,
            string? oleh = null, bool dariMigrasi = false, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task UbahAsync(
            int id, string namaTampilan, string peran, bool aktif,
            string? oleh = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task ResetKataSandiAsync(
            int id, string kataSandiBaru, string? oleh = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<bool> PasangKataSandiAwalAsync(
            int id, string kataSandi, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<bool> UbahKataSandiAsync(
            int id, string kataSandiLama, string kataSandiBaru, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task HapusAsync(int id, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    /// <summary>InfoDesa yang sama bentuknya dengan isian Pengaturan Surat desktop.</summary>
    internal sealed class DesaApiUji : IDesaRepository
    {
        private static readonly DesaData Data = new()
        {
            NamaDesa = "Sukamaju",
            Kecamatan = "Cikarang",
            Kabupaten = "Bekasi",
            JenisKabupaten = "Kabupaten",
            Alamat = "Jl. Raya Desa No. 1",
            Kodepos = "17530",
            Email = "desa.sukamaju@contoh.id",
            KepalaDesa = "H. Ahmad Fauzi",
            JenisLembaga = "Desa"
        };

        public Task InitializeAsync() => Task.CompletedTask;

        public Task<DesaData> GetInfoDesaAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Data);

        public Task UpdateInfoDesaAsync(DesaData desaData) => Task.CompletedTask;

        public Task SaveInfoDesaAsync(DesaData desaData) => Task.CompletedTask;

        public Task<DesaData> GetInfoDesaFromCacheAsync() => Task.FromResult(Data);

        public Task InvalidateCacheAsync() => Task.CompletedTask;
    }
}
