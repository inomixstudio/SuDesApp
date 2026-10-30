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

    private static IdmViewModel BuatViewModel(
        IIdmKemendesaKlien? klien = null,
        IIdmPemantauanStore? pemantauan = null,
        IIdmSimpananStore? simpanan = null)
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
            klien ?? new KlienIdmPalsu(),
            NullLogger<IdmViewModel>.Instance,
            () => new DateTime(2026, 10, 30, 9, 0, 0),
            simpanan: simpanan,
            pemantauan: pemantauan);
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

    /// <summary>
    /// Keterangan "data terbaru yang terbit" dan kalimat tahun berpatokan pada
    /// tahun TERBIT terakhir, bukan tahun yang sedang dibuka: memilih chip 2023
    /// tidak boleh membuat halaman terlihat memakai data cadangan yang basi.
    /// </summary>
    [Fact]
    public async Task Keterangan_tahun_terbit_tetap_pada_tahun_terakhir_saat_tahun_lama_dibuka()
    {
        var vm = BuatViewModel(pemantauan: new PemantauanPalsu());

        await vm.MuatAsync();
        vm.PilihTahunCommand.Execute(2023);

        Assert.Contains("2024", vm.IndikatorTerbitTampil);
        Assert.DoesNotContain("2023", vm.IndikatorTerbitTampil);
        Assert.DoesNotContain("cadangan", vm.IndikatorTerbitTampil);

        Assert.Contains("2024", vm.JudulSeriLamaTampil);
        Assert.Contains("Tahun terakhir terbit 2024", vm.RingkasanTahunDimuat);
        Assert.Contains("menampilkan penilaian tahun 2024", vm.PesanTahun);
    }

    /// <summary>
    /// Kebalikannya: bila catatan pemantau memang lebih baru dari deret yang
    /// dimuat (data cadangan), kalimatnya tetap mengajak muat ulang — dan menyebut
    /// tahun yang benar-benar dimuat, bukan tahun yang sedang dibuka.
    /// </summary>
    [Fact]
    public async Task Data_cadangan_yang_lebih_tua_dari_catatan_pemantau_mengajak_muat_ulang()
    {
        var vm = BuatViewModel(pemantauan: new PemantauanTetap(new IdmPemantauan
        {
            WaktuPeriksa = new DateTime(2026, 10, 30, 9, 0, 0),
            TahunTerbit = 2025,
        }));

        await vm.MuatAsync();
        vm.PilihTahunCommand.Execute(2023);

        Assert.Contains("2025", vm.IndikatorTerbitTampil);
        Assert.Contains("masih memuat tahun 2024 (data cadangan)", vm.IndikatorTerbitTampil);
        Assert.DoesNotContain("2023", vm.IndikatorTerbitTampil);
    }

    /// <summary>
    /// Saat situs tidak terjangkau, cadangan berkas dipasang lewat jalur yang sama
    /// dengan pemuatan langsung. Cadangan dari versi aplikasi lama bisa memuat
    /// tahun kembar; deret harus tetap satu entri per tahun dan entri pertama
    /// untuk tahun itu yang dipakai.
    /// </summary>
    [Fact]
    public async Task Cadangan_berkas_dengan_tahun_kembar_dipasang_sebagai_satu_entri_per_tahun()
    {
        static HasilIdm Tahun(int tahun, double skor, string indikator) => new(
            new RingkasanIdm(tahun, skor, "MAJU", "MANDIRI", 0.8, 0.0556, 0.68, 0.75, 0.80),
            null,
            new List<BarisIdm> { new(1, "IKS", indikator, "4", null, null, null, false) });

        var cadangan = new IdmSimpanan
        {
            KodeDesa = KodeDesa,
            WaktuMuat = new DateTime(2026, 10, 1, 8, 0, 0),
            Hasil = new List<HasilIdm>
            {
                Tahun(2024, 0.7444, "Indikator 2024-A"),
                Tahun(2024, 0.7000, "Indikator 2024-B"), // tahun kembar dari versi lama
                Tahun(2023, 0.7000, "Indikator 2023-A"),
            },
        };

        var vm = BuatViewModel(
            new KlienIdmGagal(), simpanan: new SimpananPalsu(cadangan));

        await vm.MuatAsync();

        Assert.True(vm.AdaDataIdm);
        Assert.Equal(2024, vm.TahunDipilih);
        Assert.Equal(2, vm.JumlahTahunDimuat);
        Assert.Equal(2, vm.Riwayat.Count);
        Assert.Contains("0,7444", vm.SkorTampil);
        Assert.Contains(vm.Indikator, b => b.Indikator == "Indikator 2024-A");
        Assert.DoesNotContain(vm.Indikator, b => b.Indikator == "Indikator 2024-B");
        Assert.Contains("Menampilkan data terakhir", vm.PesanCadangan);
        Assert.Contains("2026", vm.PesanCadangan);
    }

    [Fact]
    public async Task Memuat_ulang_mengembalikan_pilihan_ke_tahun_terbaru()
    {
        var vm = BuatViewModel();

        await vm.MuatAsync();
        vm.PilihTahunCommand.Execute(2023);
        Assert.Equal(2023, vm.TahunDipilih);

        await vm.MuatAsync();

        Assert.Equal(2024, vm.TahunDipilih);
        Assert.Contains(vm.Indikator, b => b.Indikator == "Indikator 2024-A");
    }

    /// <summary>
    /// Pusat tidak memuat data untuk desa ini: halaman harus kosong, tanpa tahun
    /// terpilih dan tanpa sisa rincian, dan chip yang tidak ada tidak bisa dipilih.
    /// </summary>
    [Fact]
    public async Task Pusat_tanpa_data_tidak_meninggalkan_pilihan_atau_rincian()
    {
        var vm = BuatViewModel(new KlienIdmKosong());

        await vm.MuatAsync();

        Assert.False(vm.AdaData);
        Assert.Equal(0, vm.TahunDipilih);
        Assert.Empty(vm.Indikator);
        Assert.Empty(vm.Riwayat);

        vm.PilihTahunCommand.Execute(2024);

        Assert.Equal(0, vm.TahunDipilih);
        Assert.Empty(vm.Indikator);
    }

    /// <summary>
    /// Situs pusat bisa mengabaikan parameter tahun dan menjawab tahun yang sama
    /// untuk setiap permintaan. Deret tetap satu entri per tahun, dan pilihan awal
    /// tetap tahun itu.
    /// </summary>
    [Fact]
    public async Task Jawaban_kembar_tidak_menggandakan_chip_atau_rincian()
    {
        var vm = BuatViewModel(new KlienIdmKembar());

        await vm.MuatAsync();

        Assert.Equal(1, vm.JumlahTahunDimuat);
        Assert.Single(vm.Riwayat);
        Assert.Equal(2024, vm.TahunDipilih);
        Assert.Single(vm.Indikator);
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

    /// <summary>Situs pusat tidak terjangkau untuk setiap permintaan.</summary>
    private sealed class KlienIdmGagal : IIdmKemendesaKlien
    {
        public Task<MuatanIdm> MuatAsync(string kodeDesa, int tahun, CancellationToken ct = default) =>
            Task.FromResult(MuatanIdm.Galat(
                "Situs pusat tidak terjangkau. Periksa sambungan internet lalu muat ulang."));
    }

    /// <summary>Cadangan berkas di memori — pola IdmSimpananStore tanpa disk.</summary>
    private sealed class SimpananPalsu : IIdmSimpananStore
    {
        private readonly IdmSimpanan _isi;

        public SimpananPalsu(IdmSimpanan isi) => _isi = isi;

        public IdmSimpanan? Muat() => _isi;

        public void Simpan(IdmSimpanan simpanan)
        {
            // Jalur cadangan tidak menulis; isi berkas sengaja tetap seperti semula.
        }
    }

    /// <summary>Situs pusat menjawab "tidak ada data" untuk setiap tahun.</summary>
    private sealed class KlienIdmKosong : IIdmKemendesaKlien
    {
        public Task<MuatanIdm> MuatAsync(string kodeDesa, int tahun, CancellationToken ct = default) =>
            Task.FromResult(MuatanIdm.Kosong("ID Desa tidak ditemukan"));
    }

    /// <summary>Situs pusat mengabaikan parameter tahun dan selalu menjawab 2024.</summary>
    private sealed class KlienIdmKembar : IIdmKemendesaKlien
    {
        public Task<MuatanIdm> MuatAsync(string kodeDesa, int tahun, CancellationToken ct = default) =>
            Task.FromResult(new MuatanIdm(
                StatusMuatIdm.Berhasil,
                new HasilIdm(
                    new RingkasanIdm(2024, 0.7444, "MAJU", "MANDIRI", 0.8, 0.0556, 0.68, 0.75, 0.80),
                    new IdentitasIdm("JAWA BARAT", "KARAWANG", "LEMAHABANG", "KARANGTANJUNG", KodeDesa),
                    new List<BarisIdm>
                    {
                        new(1, "IKS", "Indikator 2024-A", "4", null, null, null, false),
                    }),
                string.Empty));
    }

    /// <summary>Catatan pemeriksaan portal di memori — pola IdmPemantauanStore tanpa disk.</summary>
    private sealed class PemantauanPalsu : IIdmPemantauanStore
    {
        private IdmPemantauan? _catatan;

        public IdmPemantauan? Muat() => _catatan;

        public void Simpan(IdmPemantauan pemantauan) => _catatan = pemantauan;
    }

    /// <summary>
    /// Catatan pemeriksaan yang tidak pernah berubah — menirukan pemantau latar
    /// yang menemukan tahun lebih baru daripada data yang sempat dimuat halaman.
    /// </summary>
    private sealed class PemantauanTetap : IIdmPemantauanStore
    {
        private readonly IdmPemantauan _catatan;

        public PemantauanTetap(IdmPemantauan catatan) => _catatan = catatan;

        public IdmPemantauan? Muat() => _catatan;

        public void Simpan(IdmPemantauan pemantauan)
        {
            // Catatan pemantau sengaja tidak ditimpa halaman pada uji ini.
        }
    }
}
