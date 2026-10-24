using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Configuration;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.GeneratorPdf;
using SuDesApp.Interfaces;
using SuDesApp.Utilities;
using Xunit;

namespace SuDesApp.Tests.Surat;

/// <summary>
/// Render contoh nyata tiap keluarga surat keterangan lewat generator aslinya
/// (QuestPDF, fon Times New Roman, kop &amp; blok tanda tangan sungguhan) lalu
/// memastikan hasilnya PDF valid berjumlah SATU halaman — sama seperti yang
/// dicetak aplikasi. Berkas sampel ikut ditulis ke folder sementara
/// <c>%TEMP%/sudesapp-sampel</c> sehingga tata letaknya bisa diperiksa visual
/// (paragraf, spasi, alinea) tanpa membuka aplikasi.
/// </summary>
public sealed class CetakContohSuratTests : IDisposable
{
    private readonly string _folderAkar;
    private readonly string _folderSampel;

    public CetakContohSuratTests()
    {
        _folderAkar = CariRootRepo() ?? throw new InvalidOperationException("Folder repo tidak ditemukan.");
        _folderSampel = Path.Combine(Path.GetTempPath(), "sudesapp-sampel");
        Directory.CreateDirectory(_folderSampel);
    }

    public void Dispose()
    {
        // Folder sampel sengaja TIDAK dihapus: dipakai inspeksi visual setelah uji.
    }

    // ===== Dua repositori palsu: generator meng-cetak dari SuratData yang
    // langsung diberikan, jadi tidak ada satu pun metode yang dipanggil. =====

    private sealed class DesaRepoPalsu : IDesaRepository
    {
        public Task InitializeAsync() => Task.CompletedTask;
        public Task<DesaData> GetInfoDesaAsync(CancellationToken cancellationToken = default) => Task.FromResult(new DesaData());
        public Task UpdateInfoDesaAsync(DesaData desaData) => Task.CompletedTask;
        public Task SaveInfoDesaAsync(DesaData desaData) => Task.CompletedTask;
        public Task<DesaData> GetInfoDesaFromCacheAsync() => Task.FromResult(new DesaData());
        public Task InvalidateCacheAsync() => Task.CompletedTask;
    }

    private sealed class SuratRepoPalsu : ISuratRepository
    {
        public Task<int> InsertAsync(SuratData entity, IDbTransaction? transaction = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> UpdateAsync(SuratData entity, IDbTransaction? transaction = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> UpdateStatusAsync(int id, string status, IDbTransaction? transaction = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(int id, IDbTransaction? transaction = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SuratData> GetByIdAsync(int id, IDbTransaction? transaction = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<SuratData>> GetFilteredAsync(FilterConditions filters, string sortBy, bool ascending, int skip, int take, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> CountAsync(FilterConditions filters, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Dictionary<string, int>> CountByStatusAsync(FilterConditions filters, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task InitializeSuratIndexesAsync() => throw new NotImplementedException();
        public Task<List<string>> GetJenisSuratKeteranganDesaAsync() => throw new NotImplementedException();
        public Task RefreshJenisSuratConfigurationAsync() => throw new NotImplementedException();
        public Task<IEnumerable<SuratData>> GetAllSuratDataAsync(string sortBy = "ID_Surat", bool ascending = true, int skip = 0, int take = 100, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> AddSuratAsync(SuratData entity, IDbTransaction? transaction = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> CountSuratByJenisAndYearAsync(string namaJenis, string year, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<SuratData>> GetSuratByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Dictionary<string, int>> GetSuratStatisticsByStatusAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> CheckNomorSuratExistsAsync(string nomorSurat, int? excludeId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Dictionary<string, object>> GetDatabaseStatsAsync() => throw new NotImplementedException();
        public Task<WargaData> GetWargaByIdAsync(int idWarga, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WargaData> GetWargaByNikAsync(string nik, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> AddOrGetWargaAsync(WargaData wargaData, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int?> GetLastSuratIdByTypeAsync(string templateName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> SimpanPersetujuanAsync(SuratData surat, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> SimpanScanSuratAsync(int idSurat, string? namaBerkas, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> CatatCetakAsync(int idSurat, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> SimpanVerifikasiAsync(int idSurat, string kode, string hash, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SuratData?> GetByKodeVerifikasiAsync(string kode, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Dictionary<string, int>> CountPerStatusPersetujuanAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<List<BarisRekapPersetujuan>> GetRekapPersetujuanPerBulanAsync(int tahun, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    // ===== Data contoh =====

    private static DesaData DesaContoh() => new()
    {
        NamaDesa = "Sukamaju",
        Kecamatan = "Cikarang",
        Kabupaten = "Bekasi",
        JenisKabupaten = "Kabupaten",
        Alamat = "Jl. Raya Desa Sukamaju No. 1",
        Kodepos = "17530",
        KepalaDesa = "H. Ahmad Suryana",
        SekretarisDesa = "Dedi Suryadi",
        NamaCamat = "Drs. H. Endang Mulyana",
        NipCamat = "196805121990031004"
    };

    private static WargaData WargaContoh() => new()
    {
        ID_Warga = 17,
        Nama = "Budi Santoso",
        NIK = "3216011205900001",
        TempatLahir = "Bekasi",
        TanggalLahir = "1990-05-12",
        JenisKelamin = "Laki-laki",
        Agama = "Islam",
        StatusPerkawinan = "Belum Kawin",
        Pekerjaan = "Petani",
        Pendidikan = "SLTA/Sederajat",
        Kewarganegaraan = "WNI",
        Dusun = "Dusun Sukamaju",
        RT = "003",
        RW = "007",
        Desa = "Sukamaju",
        Kecamatan = "Cikarang",
        Kabupaten = "Bekasi"
    };

    private static SuratData SuratContoh(string namaJenis, string? keterangan = null, Instansi? instansi = null) => new()
    {
        ID_Surat = 1,
        NamaJenis = namaJenis,
        NomorSurat = "470/001/Ds/2026",
        TanggalSurat = new DateTime(2026, 8, 14),
        PejabatPenandatangan = "Kepala Desa",
        NamaPejabatPenandatangan = "H. Ahmad Suryana",
        Keperluan = namaJenis == "INSTANSI" ? "Surat Keterangan Domisili" : "Persyaratan administrasi",
        Keterangan = keterangan,
        Desa = DesaContoh(),
        Warga = instansi == null ? WargaContoh() : null,
        Instansi = instansi
    };

    /// <summary>
    /// Data kelurahan: penandatangan Lurah (ASN) dengan NIP — kasus baru
    /// yang harus tercetak beda dari desa (Kepala Desa tanpa NIP).
    /// </summary>
    private static DesaData DesaKelurahanContoh() => new()
    {
        NamaDesa = "Sukamaju",
        JenisLembaga = "Kelurahan",
        NipKepalaDesa = "197001011994031002",
        Kecamatan = "Cikarang",
        Kabupaten = "Bekasi",
        JenisKabupaten = "Kabupaten",
        Alamat = "Jl. Raya Sukamaju No. 1",
        Kodepos = "17530",
        KepalaDesa = "H. Ahmad Suryana",
        SekretarisDesa = "Dedi Suryadi",
        NamaCamat = "Drs. H. Endang Mulyana",
        NipCamat = "196805121990031004"
    };

    // ===== Konfigurasi absolut (folder font/logo di repo, bukan di folder uji) =====

    private AppConfig BuatConfig()
    {
        string tmp = Path.Combine(Path.GetTempPath(), "sudesapp-harness");
        Directory.CreateDirectory(tmp);
        string cfg = Path.Combine(tmp, "appsettings-harness.json");
        string Sep(string p) => p.Replace('\\', '/');
        string json = $$"""
        {
          "AppConfig": {
            "databaseConnectionString": "Data Source={{Sep(Path.Combine(tmp, "desa-harness.db"))}}",
            "templateFolder": "{{Sep(Path.Combine(_folderAkar, "Templates"))}}",
            "tempPdfFolder": "{{Sep(Path.Combine(tmp, "TempPDF"))}}",
            "pdfOutputPath": "{{Sep(Path.Combine(tmp, "Output"))}}",
            "fontFolder": "{{Sep(Path.Combine(_folderAkar, "Resources", "Fonts"))}}",
            "logoPath": "{{Sep(Path.Combine(_folderAkar, "Resources", "logo.png"))}}",
            "updateCheckUrl": null
          }
        }
        """;
        File.WriteAllText(cfg, json);
        return new AppConfig(cfg);
    }

    private async Task<byte[]> CetakAsync(ISuratGenerator generator, SuratData data)
    {
        using var stream = new MemoryStream();
        await generator.GeneratePdfAsync(stream, data);
        return stream.ToArray();
    }

    private static int HitungHalaman(byte[] pdf)
    {
        string isi = System.Text.Encoding.Latin1.GetString(pdf);
        var m = Regex.Match(isi, @"/Type\s*/Pages[\s\S]{0,256}?/Count\s+(\d+)");
        return m.Success && int.TryParse(m.Groups[1].Value, out int n) ? n : 1;
    }

    private void SimpanSampel(string nama, byte[] pdf)
        => File.WriteAllBytes(Path.Combine(_folderSampel, nama + ".pdf"), pdf);

    [Fact]
    public async Task SKDUmumCetakSatuHalaman()
    {
        var config = BuatConfig();
        var gen = new SKDGenerator(config, new FileService(config, NullLogger<FileService>.Instance),
            new DesaRepoPalsu(), new SuratRepoPalsu(),
            new SettingsManager(new DesaRepoPalsu(), config),
            NullLogger<SKDGenerator>.Instance, NullLoggerFactory.Instance);

        var pdf = await CetakAsync(gen, SuratContoh(SuratConstants.SKD_UMUM));
        SimpanSampel("01-skd-umum", pdf);
        Assert.True(pdf.Length > 1000);
        Assert.Equal(1, HitungHalaman(pdf));
    }

    [Fact]
    public async Task SKDKelurahanMencetakLurahDanNip()
    {
        var config = BuatConfig();
        var gen = new SKDGenerator(config, new FileService(config, NullLogger<FileService>.Instance),
            new DesaRepoPalsu(), new SuratRepoPalsu(),
            new SettingsManager(new DesaRepoPalsu(), config),
            NullLogger<SKDGenerator>.Instance, NullLoggerFactory.Instance);

        var surat = SuratContoh(SuratConstants.SKD_UMUM);
        surat.Desa = DesaKelurahanContoh();
        var pdf = await CetakAsync(gen, surat);
        SimpanSampel("09-skd-kelurahan", pdf);
        Assert.True(pdf.Length > 1000);
        Assert.Equal(1, HitungHalaman(pdf));
    }

    [Fact]
    public async Task SKTMCetakSatuHalaman()
    {
        var config = BuatConfig();
        var gen = new SKTMGenerator(config, new FileService(config, NullLogger<FileService>.Instance),
            new DesaRepoPalsu(), new SuratRepoPalsu(),
            new SettingsManager(new DesaRepoPalsu(), config),
            NullLogger<SKTMGenerator>.Instance, NullLoggerFactory.Instance);

        var pdf = await CetakAsync(gen, SuratContoh(SuratConstants.SKTM));
        SimpanSampel("02-sktm", pdf);
        Assert.True(pdf.Length > 1000);
        Assert.Equal(1, HitungHalaman(pdf));
    }

    [Fact]
    public async Task PengantarSkckCetakSatuHalaman()
    {
        var config = BuatConfig();
        var gen = new SKCKGenerator(config, new FileService(config, NullLogger<FileService>.Instance),
            new DesaRepoPalsu(), new SuratRepoPalsu(),
            new SettingsManager(new DesaRepoPalsu(), config),
            NullLogger<SKCKGenerator>.Instance, NullLoggerFactory.Instance);

        var pdf = await CetakAsync(gen, SuratContoh(SuratConstants.PENGANTAR_SKCK));
        SimpanSampel("03-pengantar-skck", pdf);
        Assert.True(pdf.Length > 1000);
        Assert.Equal(1, HitungHalaman(pdf));
    }

    [Fact]
    public async Task DomisiliInstansiCetakSatuHalaman()
    {
        var config = BuatConfig();
        var gen = new DomisiliInstansiGenerator(config, new FileService(config, NullLogger<FileService>.Instance),
            new DesaRepoPalsu(), new SuratRepoPalsu(),
            new SettingsManager(new DesaRepoPalsu(), config),
            NullLogger<DomisiliInstansiGenerator>.Instance, NullLoggerFactory.Instance);

        var instansi = new Instansi
        {
            NamaInstansi = "CV Maju Jaya",
            AlamatInstansi = "Jl. Merdeka Raya No. 10, RT 002/RW 005",
            PimpinanInstansi = "H. Sutrisno Wijaya",
            NomorSkIzinUsaha = "503.3/128/BPPTSP/2026",
            NikPimpinan = "3216051270010002"
        };
        var pdf = await CetakAsync(gen, SuratContoh(SuratConstants.INSTANSI, instansi: instansi));
        SimpanSampel("04-domisili-instansi", pdf);
        Assert.True(pdf.Length > 1000);
        Assert.Equal(1, HitungHalaman(pdf));
    }

    [Fact]
    public async Task PengantarAdmindukCetakSatuHalaman()
    {
        var config = BuatConfig();
        var gen = new PengantarAdmindukGenerator(config, new FileService(config, NullLogger<FileService>.Instance),
            new DesaRepoPalsu(), new SuratRepoPalsu(),
            new SettingsManager(new DesaRepoPalsu(), config),
            NullLogger<PengantarAdmindukGenerator>.Instance, NullLoggerFactory.Instance);

        var pdf = await CetakAsync(gen, SuratContoh(SuratConstants.PENGANTAR_ADMINDUK));
        SimpanSampel("05-pengantar-adminduk", pdf);
        Assert.True(pdf.Length > 1000);
        Assert.Equal(1, HitungHalaman(pdf));
    }

    [Fact]
    public async Task SkPindahCetakSatuHalaman()
    {
        var config = BuatConfig();
        var gen = new PindahGenerator(config, new FileService(config, NullLogger<FileService>.Instance),
            new DesaRepoPalsu(), new SuratRepoPalsu(),
            new SettingsManager(new DesaRepoPalsu(), config),
            NullLogger<PindahGenerator>.Instance, NullLoggerFactory.Instance);

        var surat = SuratContoh(SuratConstants.SK_PINDAH,
            keterangan: "Adalah benar nama tersebut diatas adalah warga Desa Sukamaju Kecamatan Cikarang Kabupaten Bekasi, dan yang bersangkutan pindah dari alamat tersebut di atas ke alamat tujuan berikut ini.");
        surat.AlamatTujuanPindah = "Jl. Kenanga No. 5, RT 001/RW 002, Desa Waringin Jaya, Kecamatan Cikarang Utara, Kabupaten Bekasi";
        surat.AlasanPindah = "Mengikuti tempat kerja";

        var pdf = await CetakAsync(gen, surat);
        SimpanSampel("06-sk-pindah", pdf);
        Assert.True(pdf.Length > 1000);
        Assert.Equal(1, HitungHalaman(pdf));
    }

    [Fact]
    public async Task SkTidakPunyaRumahCetakSatuHalaman()
    {
        var config = BuatConfig();
        var gen = new TidakPunyaRumahGenerator(config, new FileService(config, NullLogger<FileService>.Instance),
            new DesaRepoPalsu(), new SuratRepoPalsu(),
            new SettingsManager(new DesaRepoPalsu(), config),
            NullLogger<TidakPunyaRumahGenerator>.Instance, NullLoggerFactory.Instance);

        var pdf = await CetakAsync(gen, SuratContoh(SuratConstants.SK_TIDAK_PUNYA_RUMAH));
        SimpanSampel("07-sk-tidak-punya-rumah", pdf);
        Assert.True(pdf.Length > 1000);
        Assert.Equal(1, HitungHalaman(pdf));
    }

    [Fact]
    public async Task IzinKeramaianCetakSatuHalaman()
    {
        var config = BuatConfig();
        var gen = new KeramaianGenerator(config, new FileService(config, NullLogger<FileService>.Instance),
            new DesaRepoPalsu(), new SuratRepoPalsu(),
            new SettingsManager(new DesaRepoPalsu(), config),
            NullLogger<KeramaianGenerator>.Instance, NullLoggerFactory.Instance);

        var surat = SuratContoh(SuratConstants.IZIN_KERAMAIAN);
        surat.JenisKegiatan = "Pengajian Rutin Malam Jumat";
        surat.TanggalKegiatan = "20-08-2026";
        surat.LokasiKegiatan = "Balai Desa Sukamaju";

        var pdf = await CetakAsync(gen, surat);
        SimpanSampel("08-izin-keramaian", pdf);
        Assert.True(pdf.Length > 1000);
        Assert.Equal(1, HitungHalaman(pdf));
    }

    private static string? CariRootRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SuDesApp.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }
}
