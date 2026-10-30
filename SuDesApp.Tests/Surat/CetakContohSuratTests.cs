using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using SudesPdf;
using SudesPdf.Core;
using SuDesApp.Configuration;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.GeneratorPdf;
using SuDesApp.Utilities;
using Xunit;

namespace SuDesApp.Tests.Surat;

/// <summary>
/// Render contoh nyata tiap keluarga surat keterangan lewat generator aslinya
/// (mesin PDF aplikasi sendiri, kop &amp; blok tanda tangan sungguhan) lalu
/// memastikan hasilnya PDF valid berjumlah SATU halaman — sama seperti yang
/// dicetak aplikasi. Berkas sampel ikut ditulis ke folder sementara
/// <c>%TEMP%/sudesapp-sampel</c> sehingga tata letaknya bisa diperiksa visual
/// (paragraf, spasi, alinea) tanpa membuka aplikasi.
///
/// Dua uji terakhir memeriksa ISI yang tercetak (kop, judul + nomor, nama
/// kapital, blok tanda tangan), bukan sekadar jumlah halamannya: tata letak
/// bersama itu milik <see cref="SuratRenderer"/> dan <see cref="KopSurat"/>, dan
/// pernah hilang dari hasil cetak tanpa membuat uji halaman gagal.
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

    private async Task<byte[]> CetakAsync(SuratGeneratorBase generator, SuratData data)
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

    /// <summary>
    /// Teks seluruh halaman PDF dibaca lewat library SudesPdf sendiri — mesin
    /// render dan ekstraksi teks yang dipakai aplikasi. Isi surat ditulis sebagai
    /// aliran operator yang terkompresi, jadi barisnya tidak dapat dicari langsung
    /// dari byte PDF. Berkas sampelnya sekalian ditulis untuk inspeksi visual.
    /// </summary>
    private string BacaTeks(byte[] pdf, string nama)
    {
        string jalur = Path.Combine(_folderSampel, nama + ".pdf");
        File.WriteAllBytes(jalur, pdf);

        var dokumen = Document.LoadAsync(jalur).GetAwaiter().GetResult();
        try
        {
            var teks = new StringBuilder();
            for (int halaman = 0; halaman < dokumen.PageCount; halaman++)
            {
                teks.AppendLine(dokumen.GetTextAsync(halaman).GetAwaiter().GetResult());
            }
            return teks.ToString();
        }
        finally
        {
            dokumen.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    /// <summary>Pastikan setiap baris benar-benar tercetak; pesan galat memuat seluruh teks surat.</summary>
    private static void PastikanTercetak(string teks, params string[] baris)
    {
        foreach (string dicari in baris)
        {
            Assert.True(
                teks.Contains(dicari, StringComparison.Ordinal),
                $"Baris \"{dicari}\" tidak tercetak. Teks surat:\n{teks}");
        }
    }

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

    /// <summary>
    /// Surat desa harus mencetak kop bersama, judul + baris nomor, nama orang
    /// KAPITAL, dan blok tanda tangan Perbup — bukan cuma PDF satu halaman.
    /// </summary>
    [Fact]
    public async Task SkdUmumMencetakKopJudulNomorDanNamaKapital()
    {
        var config = BuatConfig();
        var gen = new SKDGenerator(config, new FileService(config, NullLogger<FileService>.Instance),
            new DesaRepoPalsu(), new SuratRepoPalsu(),
            new SettingsManager(new DesaRepoPalsu(), config),
            NullLogger<SKDGenerator>.Instance, NullLoggerFactory.Instance);

        var surat = SuratContoh(SuratConstants.SKD_UMUM);
        var pdf = await CetakAsync(gen, surat);
        string teks = BacaTeks(pdf, "10-skd-umum-isi");

        // Kop: setiap baris milik KopSurat tercetak apa adanya, sehingga surat ini
        // tidak memakai kop tersendiri yang berbeda dari dokumen lain.
        PastikanTercetak(teks, KopSurat.BarisKop(surat.Desa).Select(baris => baris.Teks).ToArray());

        // Judul + baris nomor: nomor dari register yang tercetak, lengkap dengan labelnya.
        PastikanTercetak(teks, "SURAT KETERANGAN DESA", $"NOMOR : {surat.NomorSurat}");

        // Nama orang dicetak KAPITAL pada tabel isian.
        PastikanTercetak(teks, "BUDI SANTOSO");
        Assert.DoesNotContain("Budi Santoso", teks, StringComparison.Ordinal);

        // Blok pejabat: jabatan diikuti langsung oleh nama. Frasa "bertanda tangan"
        // tetap sah pada isi surat, tetapi label tersendiri di kolom pejabat harus hilang.
        PastikanTercetak(teks, "KEPALA DESA SUKAMAJU", "H. AHMAD SURYANA");
        Assert.False(Regex.IsMatch(
            teks,
            @"KEPALA DESA SUKAMAJU\s+tanda tangan\s+H\.\s*AHMAD SURYANA",
            RegexOptions.IgnoreCase));
    }

    /// <summary>
    /// Garis ganda kop harus benar-benar dua garis: satu tipis (0,8 pt), jarak
    /// 1 pt, lalu satu tebal (1,5 pt). Padding pernah ikut tercat sebagai latar
    /// sehingga garis tipis menjadi 3,8 pt dan kedua garis tampak menyatu.
    /// </summary>
    [Fact]
    public async Task KopMemakaiDuaGarisTerpisah()
    {
        var config = BuatConfig();
        var gen = new SKDGenerator(config, new FileService(config, NullLogger<FileService>.Instance),
            new DesaRepoPalsu(), new SuratRepoPalsu(),
            new SettingsManager(new DesaRepoPalsu(), config),
            NullLogger<SKDGenerator>.Instance, NullLoggerFactory.Instance);

        var pdf = await CetakAsync(gen, SuratContoh(SuratConstants.SKD_UMUM));
        var dokumen = PdfDocument.Load(pdf);
        var halaman = dokumen.Pages[0];
        string isi = string.Join("\n", PdfContentInterpreter
            .ReadContents(dokumen, halaman.ContentsRaw)
            .Select(Encoding.Latin1.GetString));

        // Balok selebar hampir seluruh isi halaman (garis kop), bukan sel tabel.
        var balok = Regex.Matches(isi, @"(-?[\d.]+) (-?[\d.]+) ([\d.]+) ([\d.]+) re f")
            .Select(m => new
            {
                Y = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                Lebar = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture),
                Tinggi = double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture)
            })
            .Where(b => b.Lebar > 300 && b.Tinggi < 5)
            .OrderByDescending(b => b.Y)
            .ToList();

        Assert.Equal(2, balok.Count);
        Assert.Equal(KopSurat.GarisTipis, balok[0].Tinggi, 3);
        Assert.Equal(KopSurat.GarisTebal, balok[1].Tinggi, 3);

        // Jarak 1 pt di antara kedua garis adalah ruang kosong, bukan latar.
        double jarak = balok[0].Y - (balok[1].Y + balok[1].Tinggi);
        Assert.Equal(1.0, jarak, 2);
    }

    /// <summary>
    /// Pratinjau form Permohonan Rekening Koran harus jalan walau isian masih
    /// kosong: dulu tombol Pratinjau ditolak "Semua kolom wajib diisi!" sehingga
    /// menu itu satu-satunya yang tidak bisa dilihat sebelum diisi. Selama
    /// pratinjau, kolom kosong dicetak sebagai titik-titik; di luar pratinjau,
    /// kelengkapan tetap wajib supaya surat resmi tidak terbit setengah jadi.
    /// </summary>
    [Fact]
    public async Task PratinjauRekeningKoranMencetakTitikWalauIsianKosong()
    {
        var config = BuatConfig();
        var gen = new RekeningKoranGenerator(config,
            new FileService(config, NullLogger<FileService>.Instance),
            new DesaRepoPalsu(), new SuratRepoPalsu(),
            new SettingsManager(new DesaRepoPalsu(), config),
            NullLogger<RekeningKoranGenerator>.Instance, NullLoggerFactory.Instance);

        // Kepala desa & alamat sengaja dikosongkan: itulah keadaan form yang baru
        // dibuka. Data desa tetap ada supaya kop surat dapat dicetak.
        static RekeningKoranData IsianKosong() => new()
        {
            NomorSurat = string.Empty,
            Perihal = "Permohonan Print Out Rekening Koran",
            TanggalSurat = new DateTime(2026, 10, 30),
            NamaPejabat = string.Empty,
            Jabatan = string.Empty,
            AlamatDusun = string.Empty,
            AlamatDesa = string.Empty,
            AlamatKecamatan = string.Empty,
            AlamatKabupaten = string.Empty,
            NamaPemegangRekening = string.Empty,
            NomorRekening = string.Empty,
            PeriodeRekening = "30 Oktober 2026 s/d 30 Oktober 2026",
            Bank = string.Empty,
            KCP = string.Empty,
            Desa = DesaContoh()
        };

        byte[] pdf;
        using (PratinjauIsian.Pakai())
        using (var penampung = new MemoryStream())
        {
            await gen.GeneratePdfAsync(penampung, IsianKosong());
            pdf = penampung.ToArray();
        }

        Assert.Equal(1, HitungHalaman(pdf));
        string teks = BacaTeks(pdf, "12-pratinjau-rekening-koran");
        // Jarak antarkolom tercetak sebagai spasi saat teks dibaca kembali, jadi
        // baris nomor diperiksa dengan pola, bukan tab persis.
        Assert.Matches(@"Nomor\s*:\s*\.{10,}", teks);
        PastikanTercetak(teks,
            $"Bank {PratinjauIsian.TitikKosong}",
            $"KCP {PratinjauIsian.TitikKosong}");

        // Jalur terbit tetap menolak isian kosong — pratinjau tidak boleh
        // melonggarkan pemeriksaan surat yang benar-benar akan dicetak.
        using var terbit = new MemoryStream();
        var galat = await Assert.ThrowsAsync<InvalidOperationException>(
            () => gen.GeneratePdfAsync(terbit, IsianKosong()));
        Assert.Contains("Nomor Surat", galat.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Surat kelurahan harus mencetak kop "KELURAHAN …", jabatan Lurah, dan NIP
    /// pejabat ASN di bawah namanya — bedanya dari desa hanya dari data lembaganya.
    /// </summary>
    [Fact]
    public async Task SkdKelurahanMencetakJabatanLurahDanNip()
    {
        var config = BuatConfig();
        var gen = new SKDGenerator(config, new FileService(config, NullLogger<FileService>.Instance),
            new DesaRepoPalsu(), new SuratRepoPalsu(),
            new SettingsManager(new DesaRepoPalsu(), config),
            NullLogger<SKDGenerator>.Instance, NullLoggerFactory.Instance);

        var surat = SuratContoh(SuratConstants.SKD_UMUM);
        surat.Desa = DesaKelurahanContoh();

        var pdf = await CetakAsync(gen, surat);
        string teks = BacaTeks(pdf, "11-skd-kelurahan-isi");

        PastikanTercetak(teks, "KELURAHAN SUKAMAJU", "Jabatan : Lurah Sukamaju",
            "LURAH SUKAMAJU", $"NIP {surat.Desa.NipKepalaDesa}");
    }

    /// <summary>
    /// Daftar Hadir dengan peserta banyak: tabel harus memecah ke halaman
    /// berikutnya dengan baris kepala tercetak ulang di setiap halaman, dan blok
    /// tanda tangan Kepala Desa tetap utuh (tidak terpotong tepi bawah kertas).
    /// Dulu baris kepala hilang sama sekali dan sisa peserta yang tidak muat
    /// dibuang tanpa pindah halaman.
    /// </summary>
    [Fact]
    public async Task DaftarHadirBanyakPesertaMemecahHalamanDanBlokTandaTanganUtuh()
    {
        var config = BuatConfig();
        var gen = new DaftarHadirGenerator(config,
            new FileService(config, NullLogger<FileService>.Instance),
            NullLogger<DaftarHadirGenerator>.Instance);

        var data = new DaftarHadirData
        {
            Judul = "DAFTAR HADIR\nRAPAT MUSYAWARAH DESA",
            HariTanggal = DaftarHadirData.HariTanggalIndo(new DateTime(2026, 10, 30)),
            TanggalCetak = new DateTime(2026, 10, 30),
            Pukul = "09.00 WIB s/d Selesai",
            Tempat = "Aula Kantor Desa Sukamaju",
            Desa = DesaContoh(),
            Peserta = Enumerable.Range(1, 40).Select(i => new DaftarHadirPeserta
            {
                Nama = "PESERTA " + i,
                Jabatan = "Anggota",
            }).ToList(),
        };

        byte[] pdf;
        using (var penampung = new MemoryStream())
        {
            gen.GeneratePdf(penampung, data);
            pdf = penampung.ToArray();
        }

        int halaman = HitungHalaman(pdf);
        Assert.True(halaman >= 2, $"40 peserta seharusnya tidak muat satu halaman, tetapi {halaman} halaman.");

        await using var dokumen = await Document.LoadAsync(Penyimpanan(pdf, "13-daftar-hadir"));
        {
            var semua = new StringBuilder();
            int halamanBlok = -1;
            for (int p = 0; p < dokumen.PageCount; p++)
            {
                string teks = await dokumen.GetTextAsync(p);
                semua.AppendLine(teks);

                // Kop, judul, dan baris Hari/Pukul/Tempat hanya di halaman pertama.
                if (p == 0)
                {
                    PastikanTercetak(teks, "PEMERINTAH DESA SUKAMAJU", "DAFTAR HADIR", "Hari/Tanggal");
                }
                else
                {
                    Assert.DoesNotContain("PEMERINTAH DESA SUKAMAJU", teks, StringComparison.Ordinal);
                    Assert.DoesNotContain("RAPAT MUSYAWARAH DESA", teks, StringComparison.Ordinal);
                }

                // Halaman yang masih memuat baris tabel mengulang baris kepalanya;
                // halaman berisi blok tanda tangan saja tidak perlu baris kepala.
                if (teks.Contains("PESERTA", StringComparison.Ordinal))
                {
                    PastikanTercetak(teks, "No.", "Nama", "Jabatan");
                }

                if (!teks.Contains("KEPALA DESA", StringComparison.Ordinal)) continue;
                Assert.Equal(-1, halamanBlok);
                halamanBlok = p;
                // Blok tanda tangan lengkap: baris kota, jabatan, dan nama pejabat.
                PastikanTercetak(teks, "Sukamaju,", "KEPALA DESA SUKAMAJU", "H. AHMAD SURYANA");
            }

            Assert.True(halamanBlok == dokumen.PageCount - 1,
                $"Blok tanda tangan seharusnya di halaman terakhir, bukan halaman {halamanBlok + 1} dari {dokumen.PageCount}.");

            // Semua peserta tercetak: tidak ada baris yang hilang saat memecah halaman.
            for (int i = 1; i <= 40; i++) PastikanTercetak(semua.ToString(), "PESERTA " + i);
        }
    }

    /// <summary>
    /// Logo kop 1507×2050 px pernah ditanam utuh ke kotak 60 pt, sehingga setiap
    /// surat ±490 KB padahal yang tercetak hanya ±250 px (300 dpi). Gambar harus
    /// diturunkan ke resolusi cetak saat disisipkan, bukan saat digambar.
    /// </summary>
    [Fact]
    public async Task LogoKopDitanamPadaResolusiCetak()
    {
        var config = BuatConfig();
        var gen = new SKDGenerator(config, new FileService(config, NullLogger<FileService>.Instance),
            new DesaRepoPalsu(), new SuratRepoPalsu(),
            new SettingsManager(new DesaRepoPalsu(), config),
            NullLogger<SKDGenerator>.Instance, NullLoggerFactory.Instance);

        var pdf = await CetakAsync(gen, SuratContoh(SuratConstants.SKD_UMUM));
        SimpanSampel("14-logo-resolusi-cetak", pdf);

        var lebar = Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Width (\d+)")
            .Select(cocok => int.Parse(cocok.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToArray();

        Assert.NotEmpty(lebar);
        Assert.All(lebar, px => Assert.InRange(px, 230, 260));
        Assert.True(pdf.Length < 300_000,
            $"Surat satu halaman seharusnya di bawah 300 KB setelah gambar diturunkan, tetapi {pdf.Length} byte.");
    }

    /// <summary>
    /// Blok tanda tangan permohonan rekening koran menempel di sisi kanan dengan
    /// isi rata tengah. Dulu blok ditulis `AlignRight().Width(250)` pada anak kolom:
    /// mesin tata letak menata ISI kotak selebar induknya, jadi jabatan dan nama
    /// kepala desa tercetak rata tengah HALAMAN — bukan di kolom kanan.
    /// </summary>
    [Fact]
    public async Task RekeningKoranMenaruhTandaTanganRataTengahDiSisiKanan()
    {
        var config = BuatConfig();
        var gen = new RekeningKoranGenerator(config,
            new FileService(config, NullLogger<FileService>.Instance),
            new DesaRepoPalsu(), new SuratRepoPalsu(),
            new SettingsManager(new DesaRepoPalsu(), config),
            NullLogger<RekeningKoranGenerator>.Instance, NullLoggerFactory.Instance);

        var data = new RekeningKoranData
        {
            NomorSurat = "470/002/Ds/2026",
            Perihal = "Permohonan Print Out Rekening Koran",
            TanggalSurat = new DateTime(2026, 10, 30),
            NamaPejabat = "H. Ahmad Suryana",
            Jabatan = "Kepala Desa Sukamaju",
            AlamatDusun = "Jl. Melati No. 12",
            AlamatDesa = "Sukamaju",
            AlamatKecamatan = "Cikarang",
            AlamatKabupaten = "Bekasi",
            NamaPemegangRekening = "Pemerintah Desa Sukamaju",
            NomorRekening = "1234567890",
            PeriodeRekening = "01-01-2026 s/d 31-03-2026",
            Bank = "BJB",
            KCP = "Cikarang",
            Desa = DesaContoh()
        };

        byte[] pdf;
        using (var penampung = new MemoryStream())
        {
            await gen.GeneratePdfAsync(penampung, data);
            pdf = penampung.ToArray();
        }

        SimpanSampel("15-rekening-koran-ttd", pdf);

        var dokumen = PdfDocument.Load(pdf);
        Assert.Equal(1, dokumen.PageCount);
        double lebarHalaman = dokumen.Pages[0].Width;

        // Dua baris terakhir halaman adalah jabatan lalu nama kepala desa.
        var baris = BarisBerTinta(dokumen);
        Assert.True(baris.Count >= 2, "Tanda tangan tidak tercetak sama sekali.");
        var nama = baris[^1];
        var jabatan = baris[^2];

        double tengahNama = (nama.Kiri + nama.Kanan) / 2;
        double tengahJabatan = (jabatan.Kiri + jabatan.Kanan) / 2;

        // Isi blok rata tengah: kedua baris punya sumbu yang sama.
        Assert.InRange(tengahNama, tengahJabatan - 6, tengahJabatan + 6);

        // Bloknya di sisi kanan, bukan di tengah halaman.
        Assert.True(tengahJabatan > lebarHalaman * 0.65,
            $"Blok tanda tangan berada di x={tengahJabatan:0.0} pt dari lebar {lebarHalaman:0.0} pt — " +
            "seharusnya menempel di sisi kanan.");
    }

    /// <summary>
    /// Rentang mendatar setiap baris ber-tinta pada halaman pertama (satuan point),
    /// diukur dari raster — ekstraksi teks PDF tidak membawa koordinat.
    /// </summary>
    private static List<(double Kiri, double Kanan)> BarisBerTinta(PdfDocument dokumen)
    {
        const double skala = 2.0;
        var raster = PdfRenderer.RenderPage(dokumen, 0, skala);
        var hasil = new List<(double, double)>();

        bool Gelap(int x, int y)
        {
            int o = (y * raster.Width + x) * 4;
            return raster.Pixels[o] < 160 && raster.Pixels[o + 1] < 160 && raster.Pixels[o + 2] < 160;
        }

        int? atas = null;
        for (int y = 0; y <= raster.Height; y++)
        {
            bool ada = false;
            for (int x = 0; y < raster.Height && x < raster.Width && !ada; x++)
            {
                ada = Gelap(x, y);
            }

            if (ada && atas is null) atas = y;
            if (ada || atas is null) continue;

            int bawah = y - 1;
            int kiri = int.MaxValue, kanan = -1;
            for (int baris = atas.Value; baris <= bawah; baris++)
            {
                for (int x = 0; x < raster.Width; x++)
                {
                    if (!Gelap(x, baris)) continue;
                    if (x < kiri) kiri = x;
                    if (x > kanan) kanan = x;
                }
            }

            hasil.Add((kiri / skala, kanan / skala));
            atas = null;
        }

        return hasil;
    }

    /// <summary>Tulis PDF ke folder sampel lalu kembalikan jalurnya (untuk dibaca ulang).</summary>
    private string Penyimpanan(byte[] pdf, string nama)
    {
        string jalur = Path.Combine(_folderSampel, nama + ".pdf");
        File.WriteAllBytes(jalur, pdf);
        return jalur;
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
