using System;
using System.Data;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Input;
using Xunit;

namespace SuDesApp.Tests.UI
{
    /// <summary>
    /// Isian awal kotak "Keterangan".
    ///
    /// Izin Keramaian dan Izin Tinggal Sementara pernah tampil dengan kotak
    /// keterangan KOSONG saat form baru dibuka, padahal surat lain (SKTM, domisili
    /// warga/instansi, keterangan terbit) langsung menyajikan kalimat bakunya.
    /// Pengisian otomatisnya ada, tetapi hanya dipanggil di jalur simpan — jadi
    /// operator melihat kotak kosong, dan suntingannya di Izin Tinggal justru
    /// ditimpa kalimat baru saat disimpan.
    /// </summary>
    public sealed class KeteranganIsianAwalTests
    {
        private const string NamaDesa = "Sukamaju";
        private const string NamaKecamatan = "Tempuran";
        private const string NamaKabupaten = "Karawang";

        [Fact]
        public async Task IzinKeramaian_form_baru_keterangan_sudah_terisi()
        {
            var vm = new KeramaianInputViewModel(
                NullLogger<KeramaianInputViewModel>.Instance, MuatConfig(), new UnitOfWorkPalsu(), new PesanPalsu());

            await vm.InitializeAsync();

            Assert.False(string.IsNullOrWhiteSpace(vm.Keterangan),
                "Keterangan Izin Keramaian kosong saat form baru dibuka.");

            // Isian form = kalimat yang dicetak PDF. Dulu keduanya disalin terpisah dan
            // versi form menulis "Kabupaten" apa pun wilayahnya, jadi surat dari kota
            // tercetak berbeda dari yang dibaca operator.
            Assert.Equal(
                GeneratorPdf.KeramaianGenerator.KeteranganTeks(BuatDesa()),
                vm.Keterangan);
        }

        [Fact]
        public async Task IzinTinggalSementara_form_baru_keterangan_sudah_terisi()
        {
            var vm = new IjinTinggalInputViewModel(
                NullLogger<IjinTinggalInputViewModel>.Instance, MuatConfig(), new UnitOfWorkPalsu(), new PesanPalsu());

            await vm.InitializeAsync();

            Assert.False(string.IsNullOrWhiteSpace(vm.Keterangan),
                "Keterangan Izin Tinggal Sementara kosong saat form baru dibuka.");
            // Bagian asal tidak boleh berbunyi "warga ." hanya karena pemohon belum dipilih.
            string alamatTujuan = GeneratorPdf.IjinTinggalGenerator.AlamatTujuanTeks(
                string.Empty, NamaDesa, NamaKecamatan, NamaKabupaten);
            Assert.Equal(
                GeneratorPdf.IjinTinggalGenerator.KeteranganTeks(
                    NamaDesa, NamaKecamatan, NamaKabupaten, alamatTujuan),
                vm.Keterangan);
        }

        [Fact]
        public async Task IzinTinggalSementara_keterangan_suntingan_operator_tidak_ditimpa_saat_simpan()
        {
            var vm = new IjinTinggalInputViewModel(
                NullLogger<IjinTinggalInputViewModel>.Instance, MuatConfig(), new UnitOfWorkPalsu(), new PesanPalsu());
            await vm.InitializeAsync();

            vm.Keterangan = "Keterangan khusus hasil suntingan operator.";
            var surat = new SuratData();
            await vm.CollectDataAsync(surat, ModeSimpan.Draft);

            Assert.Equal("Keterangan khusus hasil suntingan operator.", surat.Keterangan);
        }

        [Fact]
        public async Task IzinTinggalSementara_keterangan_tersimpan_dipakai_lagi_saat_edit()
        {
            var vm = new IjinTinggalInputViewModel(
                NullLogger<IjinTinggalInputViewModel>.Instance, MuatConfig(), new UnitOfWorkPalsu(), new PesanPalsu());

            await vm.FillDataAsync(new SuratData
            {
                Warga = new WargaData { Desa = NamaDesa, Kecamatan = NamaKecamatan, Kabupaten = NamaKabupaten },
                Keterangan = "Keterangan lama yang masih berlaku",
                DusunTujuan = "Cikampek",
                DesaTujuan = "Karawang",
                KecTujuan = "Klari",
                KabTujuan = "Karawang"
            });

            Assert.Equal("Keterangan lama yang masih berlaku", vm.Keterangan);
        }

        private static DesaData BuatDesa() => new()
        {
            NamaDesa = NamaDesa,
            Kecamatan = NamaKecamatan,
            Kabupaten = NamaKabupaten
        };

        // ===== peranti uji =====

        private static AppConfig MuatConfig()
        {
            var folder = Path.Combine(Path.GetTempPath(), "sudesapp-keterangan-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var cfg = Path.Combine(folder, "appsettings.test.json");
            File.WriteAllText(cfg, $$"""
            {
              "AppConfig": {
                "databaseConnectionString": "Data Source={{Path.Combine(folder, "desa.db").Replace("\\", "\\\\")}}",
                "templateFolder": "{{Path.Combine(folder, "Templates").Replace("\\", "\\\\")}}",
                "tempPdfFolder": "{{Path.Combine(folder, "TempPDF").Replace("\\", "\\\\")}}",
                "pdfOutputPath": "{{Path.Combine(folder, "Output").Replace("\\", "\\\\")}}",
                "fontFolder": "{{Path.Combine(folder, "Fonts").Replace("\\", "\\\\")}}",
                "logoPath": "{{Path.Combine(folder, "logo.png").Replace("\\", "\\\\")}}",
                "updateCheckUrl": null
              }
            }
            """);
            return new AppConfig(cfg);
        }

        private sealed class DesaRepoPalsu : IDesaRepository
        {
            public Task InitializeAsync() => Task.CompletedTask;

            public Task<DesaData> GetInfoDesaAsync(CancellationToken cancellationToken = default)
                => Task.FromResult(new DesaData
                {
                    NamaDesa = NamaDesa,
                    Kecamatan = NamaKecamatan,
                    Kabupaten = NamaKabupaten,
                    KepalaDesa = "Dedi Mulyadi",
                    SekretarisDesa = "Siti Aminah"
                });

            public Task<DesaData> GetInfoDesaFromCacheAsync() => GetInfoDesaAsync();
            public Task UpdateInfoDesaAsync(DesaData desaData) => Task.CompletedTask;
            public Task SaveInfoDesaAsync(DesaData desaData) => Task.CompletedTask;
            public Task InvalidateCacheAsync() => Task.CompletedTask;
        }

        private sealed class UnitOfWorkPalsu : IUnitOfWork
        {
            public IDesaRepository DesaRepository { get; } = new DesaRepoPalsu();
            public IWargaRepository WargaRepository => throw new NotSupportedException("hanya jalur draft");
            public ISuratRepository SuratRepository => throw new NotSupportedException();
            public IJenisSuratRepository JenisSuratRepository => throw new NotSupportedException();
            public IIzinOrtuRepository IzinOrtuRepository => throw new NotSupportedException();

            public Task BeginTransactionAsync() => Task.CompletedTask;
            public Task CommitTransactionAsync() => Task.CompletedTask;
            public Task RollbackTransactionAsync() => Task.CompletedTask;
            public Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation) => operation();
            public Task ExecuteInTransactionAsync(Func<Task> operation) => operation();
            public IDbTransaction? CurrentTransaction => null;
            public SqliteConnection Connection { get; } = new SqliteConnection("Data Source=:memory:");
            public bool HasActiveTransaction => false;
            public void Dispose() { }
        }

        private sealed class PesanPalsu : IMessageService
        {
            public Task ShowMessageAsync(string message, string title, AppMessageButton buttons, AppMessageIcon icon) => Task.CompletedTask;
            public Task ShowErrorAsync(string message) => Task.CompletedTask;
            public Task ShowWarningAsync(string message) => Task.CompletedTask;
            public Task ShowInfoAsync(string message) => Task.CompletedTask;
            public Task<bool> ShowConfirmationAsync(string title, string message) => Task.FromResult(true);
        }
    }
}
