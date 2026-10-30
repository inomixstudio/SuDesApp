using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Services;
using SuDesApp.Wilayah;
using SuDesApp.Wpf.ViewModels;
using Xunit;

namespace SuDesApp.Tests.Idm;

/// <summary>
/// Chip tahun pada halaman IDM harus benar-benar mengganti apa yang dibaca
/// pengguna: angka ringkasan DAN rincian indikator tahun itu. Tahun terakhir
/// tetap menjadi pilihan awal setelah pemuatan, penanda "TERAKHIR" tidak ikut
/// berpindah, dan tahun yang tidak dimuat tidak boleh mengubah apa pun.
/// </summary>
public sealed class IdmPilihTahunTests
{
    private const string KodeDesa = "3215192005"; // Karangtanjung, Lemahabang, Karawang

    [Fact]
    public async Task Memilih_tahun_mengganti_ringkasan_dan_rincian_indikator()
    {
        var vm = BuatViewModel();

        await vm.MuatAsync();

        // Pilihan awal = tahun terakhir.
        Assert.Equal(2024, vm.TahunDipilih);
        Assert.Equal("Penilaian tahun 2024", vm.TahunTampil);
        Assert.Equal("0,7444", vm.SkorTampil);
        Assert.Equal(2, vm.Indikator.Count);

        // Klik chip 2023.
        vm.PilihTahunCommand.Execute(2023);

        Assert.Equal(2023, vm.TahunDipilih);
        Assert.Equal("Penilaian tahun 2023", vm.TahunTampil);
        Assert.Equal("0,7000", vm.SkorTampil);
        Assert.Equal(3, vm.Indikator.Count);
        Assert.Contains(vm.Indikator, b => b.Indikator == "Indikator 2023-C");

        // Penanda chip: yang terbuka pindah, TERAKHIR tetap di 2024.
        Assert.False(vm.Riwayat.Single(r => r.Tahun == 2024).Ditampilkan);
        Assert.True(vm.Riwayat.Single(r => r.Tahun == 2023).Ditampilkan);
        Assert.True(vm.Riwayat.Single(r => r.Tahun == 2024).Terbaru);
        Assert.False(vm.Riwayat.Single(r => r.Tahun == 2023).Terbaru);

        // Tahun yang tidak tersedia tidak mengubah apa pun.
        vm.PilihTahunCommand.Execute(2019);
        Assert.Equal(2023, vm.TahunDipilih);

        // Kembali ke tahun terakhir.
        vm.PilihTahunCommand.Execute(2024);
        Assert.Equal(2024, vm.TahunDipilih);
        Assert.Equal(2, vm.Indikator.Count);
    }

    private static IdmViewModel BuatViewModel()
    {
        var desa = new DesaData
        {
            NamaDesa = "Karangtanjung",
            Kecamatan = "Lemahabang",
            Kabupaten = "Karawang",
        };

        string tmp = Path.Combine(Path.GetTempPath(), "sudesapp-idm-pilih-tahun");
        Directory.CreateDirectory(tmp);
        string cfg = Path.Combine(tmp, $"appsettings-{Guid.NewGuid():N}.json");
        File.WriteAllText(cfg, $$"""
        {
          "AppConfig": {
            "databaseConnectionString": "Data Source={{Path.Combine(tmp, "desa.db").Replace('\\', '/')}}",
            "templateFolder": "{{Path.Combine(tmp, "Templates").Replace('\\', '/')}}",
            "tempPdfFolder": "{{Path.Combine(tmp, "TempPDF").Replace('\\', '/')}}",
            "pdfOutputPath": "{{Path.Combine(tmp, "Output").Replace('\\', '/')}}"
          }
        }
        """);

        var pengaturan = new SettingsManager(new DesaRepoPalsu(desa), new AppConfig(cfg));
        var wilayah = new WilayahDataStore(
            NullLogger<WilayahDataStore>.Instance,
            Path.Combine(AppContext.BaseDirectory, "Data", "Wilayah", "wilayah.tsv.gz"));

        return new IdmViewModel(
            pengaturan,
            wilayah,
            new KlienIdmPalsu(),
            NullLogger<IdmViewModel>.Instance,
            () => new DateTime(2026, 10, 30, 9, 0, 0));
    }

    private sealed class DesaRepoPalsu : IDesaRepository
    {
        private readonly DesaData _desa;

        public DesaRepoPalsu(DesaData desa) => _desa = desa;

        public Task InitializeAsync() => Task.CompletedTask;

        public Task<DesaData> GetInfoDesaAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_desa);

        public Task UpdateInfoDesaAsync(DesaData desaData) => Task.CompletedTask;

        public Task SaveInfoDesaAsync(DesaData desaData) => Task.CompletedTask;

        public Task<DesaData> GetInfoDesaFromCacheAsync() => Task.FromResult(_desa);

        public Task InvalidateCacheAsync() => Task.CompletedTask;
    }

    /// <summary>Dua tahun berisi data (2024 dan 2023); tahun lain tidak diterbitkan.</summary>
    private sealed class KlienIdmPalsu : IIdmKemendesaKlien
    {
        public Task<MuatanIdm> MuatAsync(string kodeDesa, int tahun, CancellationToken ct = default)
        {
            var hasil = tahun switch
            {
                2024 => new HasilIdm(
                    new RingkasanIdm(2024, 0.7444, "MAJU", "MANDIRI", 0.8, 0.0556, 0.68, 0.75, 0.80),
                    new IdentitasIdm("JAWA BARAT", "KARAWANG", "LEMAHABANG", "KARANGTANJUNG", KodeDesa),
                    new List<BarisIdm>
                    {
                        new(1, "IKS", "Indikator 2024-A", "4", null, null, null, false),
                        new(2, "IKE", "Indikator 2024-B", "3", null, null, null, false),
                    }),
                2023 => new HasilIdm(
                    new RingkasanIdm(2023, 0.7000, "BERKEMBANG", "MAJU", 0.7, 0, 0.66, 0.71, 0.73),
                    new IdentitasIdm("JAWA BARAT", "KARAWANG", "LEMAHABANG", "KARANGTANJUNG", KodeDesa),
                    new List<BarisIdm>
                    {
                        new(1, "IKS", "Indikator 2023-A", "4", null, null, null, false),
                        new(2, "IKE", "Indikator 2023-B", "3", null, null, null, false),
                        new(3, "IKL", "Indikator 2023-C", "2", null, null, null, false),
                    }),
                _ => null,
            };

            return Task.FromResult(hasil == null
                ? MuatanIdm.Kosong("ID Desa tidak ditemukan")
                : new MuatanIdm(StatusMuatIdm.Berhasil, hasil, string.Empty));
        }
    }
}
