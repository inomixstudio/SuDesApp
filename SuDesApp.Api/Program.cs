// SuDesApp.Api — backend tunggal untuk ketiga platform (desktop, Android, browser).
// Seluruh logika bisnis tetap di SuDesApp.Core; proyek ini hanya membungkusnya
// sebagai HTTP supaya WPF/MAUI/Web membaca dan menulis data yang sama.
//
// Komposisi DI di bawah ini disalin dari registrasi di SuDesApp.Wpf/App.xaml.cs —
// hanya bagian yang dibutuhkan baca-statistik. Saat endpoint lain ditambahkan,
// ikuti registrasi yang sama di sana supaya perilakunya identik dengan desktop.

using SuDesApp;
using SuDesApp.Api;
using SuDesApp.Configuration;
using SuDesApp.Data.Handlers;
using SuDesApp.Data.Models;
using SuDesApp.Data.Queries;
using SuDesApp.Data.Repositories;
using SuDesApp.Services;
using SuDesApp.Utilities;
using Microsoft.AspNetCore.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// JSON mengikuti gaya Core (ApiListener.OpsiJson): camelCase, null tidak dikirim.
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

// ==== Lapisan konfigurasi & data (mirip App.xaml.cs) ====
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ICacheService, MemoryCacheService>();

builder.Services.AddSingleton(sp =>
{
    var cfg = new AppConfig(
        builder.Configuration,
        sp.GetService<ILogger<AppConfig>>());
    // Sama seperti startup WPF (App.xaml.cs): kunci database dari DPAPI + provider
    // SQLCipher dulu, baru connection string dipakai. Tanpa langkah ini berkas
    // terenkripsi dibuka tanpa Password dan SQLite menjawab "file is not a database".
    cfg.DatabaseConnectionString = EnkripsiDatabase.JaminTerkunci(
        cfg.DatabaseConnectionString,
        sp.GetService<ILogger<AppConfig>>());
    return cfg;
});

// Loader jenis surat: path mengikuti BaseDirectory, sama seperti WPF,
// jadi berkasnya ikut di-copy ke output (lihat SuDesApp.Api.csproj).
builder.Services.AddSingleton(sp => new JenisSuratConfigLoader(
    sp.GetRequiredService<ICacheService>(),
    sp.GetRequiredService<IConfiguration>(),
    sp.GetRequiredService<ILogger<JenisSuratConfigLoader>>(),
    Path.Combine(AppContext.BaseDirectory, "Configuration", "JenisSuratConfig.json")));
builder.Services.AddSingleton<IJenisSuratConfigLoader>(sp => sp.GetRequiredService<JenisSuratConfigLoader>());

builder.Services.AddSingleton<IDesaRepository, DesaRepository>();
builder.Services.AddSingleton(sp => new QueryProvider(
    Path.Combine(AppContext.BaseDirectory, "Data", "Queries", "SuratQueries.sql"),
    sp.GetService<ILogger<QueryProvider>>()));
builder.Services.AddSingleton<QueryInterceptor>();
// Dipakai SuratRepository (opsional di konstruktor, tapi didaftarkan seperti WPF).
builder.Services.AddSingleton<ActivityLogService>();
// Dibutuhkan ArsipSuratRepository; sama seperti "services.AddSingleton<FileService>()" di WPF.
builder.Services.AddSingleton<FileService>();

// Handler data per jenis surat (daftar sama persis dengan WPF).
builder.Services.AddScoped<ISuratDataHandler, SKUDataHandler>();
builder.Services.AddScoped<ISuratDataHandler, KematianDataHandler>();
builder.Services.AddScoped<ISuratDataHandler, BedaNamaDataHandler>();
builder.Services.AddScoped<ISuratDataHandler, GarapanDataHandler>();
builder.Services.AddScoped<ISuratDataHandler, IzinOrtuHandler>();
builder.Services.AddScoped<ISuratDataHandler, IjinTinggalDataHandler>();
builder.Services.AddScoped<ISuratDataHandler, InstansiDataHandler>();
builder.Services.AddScoped<ISuratDataHandler, KenalLahirDataHandler>();
builder.Services.AddScoped<ISuratDataHandler, AhliWarisDataHandler>();
builder.Services.AddScoped<ISuratDataHandler, PindahDataHandler>();
builder.Services.AddScoped<ISuratDataHandler, KeramaianDataHandler>();
foreach (var ntcrJenis in SuratConstants.NtcrSemua)
{
    var namaJenis = ntcrJenis;
    builder.Services.AddScoped<ISuratDataHandler>(sp => new NtcrDataHandler(
        namaJenis,
        sp.GetRequiredService<ILogger<NtcrDataHandler>>(),
        sp.GetRequiredService<ICacheService>()));
}

// Koneksi + unit of work + repository (scoped per permintaan, seperti per navigasi di WPF).
builder.Services.AddScoped(sp => new Microsoft.Data.Sqlite.SqliteConnection(
    sp.GetRequiredService<AppConfig>().DatabaseConnectionString));
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<IWargaRepository, WargaRepository>();
builder.Services.AddScoped<IJenisSuratRepository, JenisSuratRepository>();
builder.Services.AddScoped<ISuratRepository, SuratRepository>();
builder.Services.AddScoped<IIzinOrtuRepository, IzinOrtuRepository>();
builder.Services.AddScoped<IArsipSuratRepository, ArsipSuratRepository>();
builder.Services.AddScoped<IPermintaanWaRepository, PermintaanWaRepository>();
builder.Services.AddScoped<IPerangkatDesaRepository, PerangkatDesaRepository>();

// ==== Login & sesi (tahap autentikasi) ====
// Registrasi identik dengan App.xaml.cs WPF: akun dari tabel Pengguna, kata
// sandi PBKDF2, kunci 3x gagal — aturan yang sama persis dengan layar login
// desktop. Sesi token hidup di memori proses (lihat SesiService).
builder.Services.AddScoped<IPenggunaRepository, PenggunaRepository>();
builder.Services.AddScoped<IPenggunaService, PenggunaService>();
builder.Services.AddSingleton<SesiService>();

// Dibuat lewat factory supaya parameter opsional (Func<DateTime>, PenjagaLisensi)
// tidak bergantung pada perilaku container; API host sengaja tidak memasang
// rantai lisensi — penjaga lisensi = null (mode baca penuh) di backend ini.
builder.Services.AddScoped<IPerangkatDesaService>(sp => new PerangkatDesaService(
    sp.GetRequiredService<IPerangkatDesaRepository>(),
    sp.GetRequiredService<ILogger<PerangkatDesaService>>()));

builder.Services.AddScoped<IApiRingkasanService>(sp => new ApiRingkasanService(
    sp.GetRequiredService<IWargaRepository>(),
    sp.GetRequiredService<IPerangkatDesaService>(),
    sp.GetRequiredService<ISuratRepository>(),
    sp.GetRequiredService<IPermintaanWaRepository>(),
    sp.GetRequiredService<ILogger<ApiRingkasanService>>()));

// ==== Permintaan surat online (alur tulis pertama) ====
// WaEngine sengaja null: permintaan dari API langsung masuk antrean tanpa
// melewati kanal pesan mana pun: permintaan langsung masuk antrean.
builder.Services.AddScoped<IApiPermintaanService>(sp => new ApiPermintaanService(
    sp.GetRequiredService<IPermintaanWaRepository>(),
    audit: sp.GetService<ActivityLogService>(),
    logger: sp.GetRequiredService<ILogger<ApiPermintaanService>>()));

// Skema & migrasi database (idempoten) — tanpa ini database kosong baru tidak
// punya tabel. WPF memanggil InitializeAsync() dari App.OnStartup lewat scope.
builder.Services.AddSingleton<IDatabaseBackupService, DatabaseBackupService>();
builder.Services.AddScoped<IDatabaseInitializer, DatabaseInitializer>();

// CORS untuk WebView Android / browser saat pengembangan (dipakai di bawah).
builder.Services.AddCors();

var app = builder.Build();

// Gagal di sini berarti database tidak bisa dibuka sama sekali — hentikan lebih
// awal dengan pesan yang bisa ditindaklanjuti (sikap yang sama dengan WPF),
// supaya bukan tiap endpoint yang memunculkan "file is not a database".
using (var scope = app.Services.CreateScope())
{
    _ = scope.ServiceProvider.GetRequiredService<AppConfig>();
    await scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
}

// ==== Pemeriksaan kunci API untuk endpoint eksternal ====
// Mengikuti aturan listener Core (docs/api-desa.md): kunci diambil dari header
// X-Api-Key atau Authorization: Bearer, disimpan lewat ApiKunci (DPAPI),
// dibandingkan constant-time, dan cakupannya diperiksa per endpoint.
// Kandidat kunci dari konfigurasi "Api:KunciTambahan" ditujukan untuk
// pengembangan (kunci yang tidak berasal dari halaman Pengaturan API WPF).
List<ApiKunciEntry> KandidatKunci()
{
    var daftar = ApiKunci.MuatSemua().ToList();
    var tambahan = builder.Configuration["Api:KunciTambahan"];
    if (!string.IsNullOrWhiteSpace(tambahan))
    {
        foreach (var k in tambahan.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            daftar.Add(new ApiKunciEntry { Nilai = k, Cakupan = ApiCakupan.Penuh, Nama = "Konfigurasi" });
    }
    return daftar;
}

/// <summary>Ambil token dari header Authorization: Bearer (null bila tidak ada).</summary>
string? TokenSesi(HttpContext ctx)
{
    var h = ctx.Request.Headers.Authorization.FirstOrDefault();
    if (string.IsNullOrWhiteSpace(h) || !h.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        return null;
    var token = h["Bearer".Length..].Trim();
    return token.Length == 0 ? null : token;
}

/// <summary>Sesi valid dari header permintaan, atau null bila tidak ada/kedaluwarsa.</summary>
SesiService.SesiAktif? SesiDari(HttpContext ctx)
    => ctx.RequestServices.GetRequiredService<SesiService>().Ambil(TokenSesi(ctx));

/// Kembalikan hasil tolak (401/403) bila jalur ini terlindungi kunci dan
/// permintaan tidak lolos; null berarti lanjutkan menjalankan endpoint.
IResult? TolakBilaTanpaKunci(HttpContext ctx, string metode, string jalur)
{
    // Pemetaan ke tabel rute Core hanya untuk endpoint yang memang berkey di sana.
    ApiEndpoint? endpoint = metode switch
    {
        "POST" when jalur == "/sudes/api/v1/permintaan" =>
            ApiRute.Semua.FirstOrDefault(e => e.Metode == "POST" && e.Jalur == "/permintaan"),
        "GET" when System.Text.RegularExpressions.Regex.IsMatch(jalur, "^/sudes/api/v1/permintaan/[^/]+$") =>
            ApiRute.Semua.FirstOrDefault(e => e.Jalur == "/permintaan/{kode}"),
        _ => null
    };
    if (endpoint == null) return null;

    var diberikan = ApiAutentikasi.AmbilKunci(h => ctx.Request.Headers[h].FirstOrDefault());
    var cocok = ApiAutentikasi.CariCocok(diberikan, KandidatKunci());
    if (cocok == null) return Results.Unauthorized();
    if (!ApiAutentikasi.CakupanCukup(cocok.Cakupan, endpoint))
        return Results.Json(
            new { sukses = false, pesan = "Kunci tidak mencakup endpoint ini." },
            statusCode: StatusCodes.Status403Forbidden);
    return null;
}

// Penahan kiriman formulir publik: 5 permintaan per 10 menit per alamat IP.
// Negara desa jarang punya IP publik berbeda-beda (satu kantor desa = satu IP),
// jadi batasnya sengaja longgar untuk pemakaian wajar tetapi cukup menahan
// kiriman berulang dari satu peramban.
var catatanKiriman = new System.Collections.Concurrent.ConcurrentDictionary<string, Queue<DateTime>>();
bool TerlaluSering(string kunci, int maks = 5, int menit = 10)
{
    var antre = catatanKiriman.GetOrAdd(kunci, _ => new Queue<DateTime>());
    lock (antre)
    {
        var batas = DateTime.UtcNow.AddMinutes(-menit);
        while (antre.Count > 0 && antre.Peek() < batas) antre.Dequeue();
        if (antre.Count >= maks) return true;
        antre.Enqueue(DateTime.UtcNow);
    }

    // Jaga ukuran peta: kunci lama yang tidak dipakai lagi dibuang.
    if (catatanKiriman.Count > 2000)
    {
        foreach (var usang in catatanKiriman.Where(p => p.Value.Count == 0 || p.Value.Peek() < DateTime.UtcNow.AddMinutes(-menit)).Take(500))
            catatanKiriman.TryRemove(usang.Key, out _);
    }
    return false;
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // WebView Android (MAUI) dan browser memanggil backend lintas origin saat
    // pengembangan. Produksi di belakangi origin yang sama / reverse proxy.
    app.UseCors(p => p.SetIsOriginAllowed(_ => true).AllowAnyHeader().AllowAnyMethod());
}

// ==== Identitas per-permintaan: sesi login → SessionContext Core ====
// Satu proses Api melayani banyak pengguna sekaligus, jadi identitas tidak
// boleh ditulis ke properti global SessionContext: peran Administrator milik
// satu permintaan akan terbaca permintaan lain. MulaiSesiPermintaan menyimpan
// identitas di AsyncLocal khusus aliran permintaan ini (lihat
// docs/policy-peran-pengguna.md), sehingga Core menilai izin atas nama pengguna
// yang benar dan kembali ke nilai sebelumnya begitu permintaan selesai.
//
// Tanpa Bearer token penegakan izin tetap mati, sama seperti jalur kunci API
// (SessionContext.SesiSistem) pada aplikasi desktop.
app.Use(async (ctx, next) =>
{
    var sesi = SesiDari(ctx);
    if (sesi == null)
    {
        await next(ctx);
        return;
    }

    using var lingkup = SessionContext.MulaiSesiPermintaan(
        sesi.Username, sesi.NamaTampilan, sesi.Peran);

    try
    {
        await next(ctx);
    }
    catch (IzinDitolakException ex)
    {
        // Core menolak berdasarkan peran. Balas 403 dengan pesan yang sama
        // seperti yang dilihat pengguna desktop — bukan 500.
        if (ctx.Response.HasStarted) throw;
        ctx.Response.Clear();
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        await ctx.Response.WriteAsJsonAsync(new
        {
            sukses = false,
            pesan = ex.Message,
            izin = ex.Izin.ToString()
        });
    }
});

// ==== Endpoint pembuka ====
app.MapGet("/api/health", () =>
    Results.Ok(new { status = "sehat", waktu = DateTime.UtcNow }))
    .WithName("Health");

app.MapGet("/api/versi", () =>
{
    var nama = typeof(AppConfig).Assembly.GetName();
    return Results.Ok(new { core = nama.Name, versi = nama.Version?.ToString() });
})
    .WithName("Versi");

// ==== Rute inti SuDesApp.Core (ApiRute.Prefix = /sudes/api/v1) ====
// Disajikan langsung dari layanan Core per permintaan, sehingga angkanya sama
// persis dengan yang dilihat aplikasi desktop.
app.MapGet("/sudes/api/v1/statistik", async (HttpContext ctx, CancellationToken ct) =>
    Results.Ok(await ctx.RequestServices.GetRequiredService<IApiRingkasanService>().AmbilAsync(ct)))
    .WithName("StatistikDesa");

app.MapGet("/sudes/api/v1/perangkat-desa", async (HttpContext ctx, bool? hanyaAktif, CancellationToken ct) =>
    Results.Ok(await ctx.RequestServices.GetRequiredService<IApiRingkasanService>()
        .AmbilPerangkatAsync(hanyaAktif ?? true, ct)))
    .WithName("PerangkatDesa");

app.MapGet("/sudes/api/v1/rekap-bulanan", async (HttpContext ctx, int? tahun, CancellationToken ct) =>
    Results.Ok(await ctx.RequestServices.GetRequiredService<IApiRingkasanService>()
        .AmbilRekapBulananAsync(tahun, ct)))
    .WithName("RekapBulanan");

app.MapGet("/sudes/api/v1/jenis-surat", (HttpContext ctx) =>
    Results.Ok(ctx.RequestServices.GetRequiredService<IApiRingkasanService>().AmbilJenisSuratOnline()))
    .WithName("JenisSuratOnline");

// ==== Menu aplikasi (daftar pilihan surat untuk sidebar) ====
// Isinya katalog yang sama dengan menu desktop: "Buat Surat Baru" dan blanko
// NTCR. Ditaruh di API supaya menu web tidak menyalin daftar sendiri dan bisa
// ketinggalan saat Core menambah jenis surat. Tidak memuat data pribadi, jadi
// boleh dibaca tanpa sesi (dipakai juga sebelum login selesai dimuat).
app.MapGet("/sudes/api/v1/menu", () => Results.Ok(new
{
    surat = MenuSuratKatalog.Semua.Select(p => new { label = p.Label, jenis = p.Jenis }),
    ntcr = NtcrKatalog.Semua.Select(n => new
    {
        judul = n.Judul,
        jenis = n.NamaJenis,
        kode = n.Kode,
        keterangan = n.Keterangan
    })
})).WithName("MenuAplikasi");

// ==== Data warga (muka internal) ====
// Satu halaman tabel dengan pencarian + penyaring status + paginasi. Isinya
// data pribadi (NIK, No KK, nomor HP), jadi wajib sesi login. Izin per-peran
// TIDAK diperiksa di sini karena halaman desktop juga tidak membatasinya:
// semua peran yang bisa masuk aplikasi desa boleh membaca data warga, dan
// yang dibatasi adalah perubahannya (belum ada endpoint tulis di web).
//
// Penyaringan dan paginasi dikerjakan repository Core (GetWargaPageAsync),
// bukan di sini: query yang sama dipakai halaman desktop, sehingga jumlah dan
// urutannya tidak bisa berbeda antara kedua platform.
app.MapGet("/sudes/api/v1/warga", async (
    HttpContext ctx, string? cari, string? status, int? halaman, int? ukuran, CancellationToken ct) =>
{
    if (SesiDari(ctx) == null) return Results.Unauthorized();

    var filter = new WargaFilter
    {
        Cari = string.IsNullOrWhiteSpace(cari) ? null : cari.Trim(),
        Status = StatusWargaTipe.Valid(status) ? StatusWargaTipe.Normalisasi(status) : null,
        Halaman = halaman ?? 1,
        UkuranHalaman = Math.Clamp(ukuran ?? 25, 5, 200)
    };

    var hasil = await ctx.RequestServices.GetRequiredService<IWargaRepository>()
        .GetWargaPageAsync(filter, ct);

    return Results.Ok(new
    {
        total = hasil.Total,
        halaman = hasil.Halaman,
        ukuran = hasil.UkuranHalaman,
        totalHalaman = hasil.TotalHalaman,
        ringkasan = hasil.Ringkasan,
        items = hasil.Items.Select(w => new
        {
            id = w.ID_Warga,
            nik = w.NIK,
            nama = w.Nama,
            jk = w.JenisKelamin,
            tempatLahir = w.TempatLahir,
            tanggalLahir = w.TanggalLahir,
            alamat = w.AlamatLengkap,
            dusun = w.Dusun,
            rt = w.RT,
            rw = w.RW,
            noKk = w.NoKK,
            status = w.StatusWarga,
            nomorHp = w.NomorHP,
            pekerjaan = w.Pekerjaan
        })
    });
}).WithName("DaftarWarga");

// ==== Register Surat (muka internal) ====
// Daftar surat satu desa dengan pencarian, penyaring status/tahun/jenis, dan
// paginasi. Penyaringan, urutan, dan hitungan dikerjakan ISuratRepository Core
// — kelas filter yang sama dengan halaman desktop — jadi hasil web dan desktop
// tidak bisa berbeda. Jenis NTCR dikecualikan persis seperti Register Surat
// desktop (NTCR punya registernya sendiri).
//
// Nama penerima, TTL, jenis kelamin, dan alamat dihitung lewat
// TemplateSuratTercatat seperti di desktop, supaya surat dari Template Surat
// tidak tampil kosong. Isinya data pribadi, jadi wajib sesi login.
app.MapGet("/sudes/api/v1/surat", async (
    HttpContext ctx, string? cari, string? status, string? tahun, string? jenis,
    int? halaman, int? ukuran, CancellationToken ct) =>
{
    if (SesiDari(ctx) == null) return Results.Unauthorized();

    var repo = ctx.RequestServices.GetRequiredService<ISuratRepository>();
    var jenisRepo = ctx.RequestServices.GetRequiredService<IJenisSuratRepository>();

    var filter = new FilterConditions
    {
        // Register umum memisahkan NTCR (lihat RegisterSuratViewModel.ExcludedJenisNames).
        ExcludeJenisNames = new List<string>(SuratConstants.NtcrSemua),
        JenisSurat = string.IsNullOrWhiteSpace(jenis) ? null : jenis.Trim(),
        SearchText = string.IsNullOrWhiteSpace(cari) ? null : cari.Trim(),
        // Nilai status yang tidak dikenal diabaikan, bukan ditolak: penyaring
        // kosong berarti "semua status" seperti kotak pilihan desktop.
        Status = SuratConstants.ValidStatus.FirstOrDefault(
            s => string.Equals(s, status, StringComparison.OrdinalIgnoreCase)),
        Tahun = int.TryParse(tahun, out var th) && th is >= 1900 and <= 3000
            ? th.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : null
    };

    var halamanKe = Math.Max(1, halaman ?? 1);
    var ukuranHalaman = Math.Clamp(ukuran ?? 25, 5, 200);

    // Query dijalankan BERURUTAN pada satu koneksi: Microsoft.Data.Sqlite tidak
    // thread-safe untuk query konkuren di koneksi bersama (aturan yang sama
    // dengan halaman desktop), dan koneksi ini scoped untuk permintaan ini.
    var data = (await repo.GetFilteredAsync(
        filter,
        sortBy: "ID_Surat",
        ascending: false,
        skip: (halamanKe - 1) * ukuranHalaman,
        take: ukuranHalaman,
        cancellationToken: ct)).ToList();

    var total = await repo.CountAsync(filter, ct);
    var perStatus = await repo.CountByStatusAsync(filter.CloneTanpaStatus(), ct);
    var labelJenis = await jenisRepo.GetJenisSuratDisplayNamesAsync();
    var tahunTersedia = await jenisRepo.GetAvailableYearsAsync();

    var totalHalaman = (int)Math.Ceiling(total / (double)ukuranHalaman);
    var mulai = total == 0 ? 0 : (halamanKe - 1) * ukuranHalaman + 1;
    var sampai = Math.Min(total, halamanKe * ukuranHalaman);

    return Results.Ok(new
    {
        total,
        halaman = halamanKe,
        ukuran = ukuranHalaman,
        totalHalaman,
        ringkasan = $"Menampilkan {mulai}–{sampai} dari {total} surat.",
        tahunTersedia,
        // Pilihan penyaring jenis: katalog konfigurasi Core dikurangi NTCR.
        jenisTersedia = labelJenis
            .Where(p => !SuratConstants.IsNtcr(p.Key))
            .OrderBy(p => p.Value, StringComparer.CurrentCultureIgnoreCase)
            .Select(p => new { jenis = p.Key, label = p.Value }),
        // Hitungan per status memakai filter yang sama tanpa syarat status,
        // sama seperti ringkasan "Status: ..." pada halaman desktop.
        perStatus = SuratConstants.ValidStatus.Select(s => new
        {
            status = s,
            label = s switch
            {
                "Active" => "Aktif",
                "Cancelled" => "Dibatalkan",
                _ => "Draft"
            },
            jumlah = perStatus.TryGetValue(s, out var jumlah) ? jumlah : 0
        }),
        items = data.Select(s =>
        {
            var jenisNama = string.IsNullOrWhiteSpace(s.NamaJenis)
                ? "-"
                : s.NamaJenis!.ToUpperInvariant();
            var dariTemplate = TemplateSuratTercatat.DariTemplateSurat(s);
            var warga = s.Warga;

            // Urutan sama dengan GetNamaDisplay desktop: instansi → template → warga.
            var nama = !string.IsNullOrWhiteSpace(s.Instansi?.NamaInstansi)
                ? s.Instansi!.NamaInstansi
                : dariTemplate
                    ? TemplateSuratTercatat.NamaPenerimaTampil(s)
                    : warga?.Nama;

            var ttl = dariTemplate
                ? TemplateSuratTercatat.TempatTanggalLahirTampil(s)
                : warga is null
                    ? null
                    : $"{warga.TempatLahir ?? "-"}, {warga.TanggalLahir ?? "-"}";

            var jk = dariTemplate
                ? TemplateSuratTercatat.JenisKelaminTampil(s)
                : string.IsNullOrEmpty(warga?.JenisKelamin)
                    ? null
                    : warga!.JenisKelamin![..1].ToUpperInvariant();

            var alamat = dariTemplate
                ? TemplateSuratTercatat.AlamatPenerimaTampil(s)
                : warga is not null
                    ? string.Join(", ", new[]
                        {
                            warga.Dusun, warga.Desa, warga.Kecamatan, warga.Kabupaten
                        }.Where(bagian => !string.IsNullOrWhiteSpace(bagian)))
                    : s.Instansi?.AlamatInstansi;

            var statusSurat = string.IsNullOrWhiteSpace(s.Status) ? "Draft" : s.Status!;

            return new
            {
                id = s.ID_Surat,
                nomor = s.NomorSurat,
                tanggal = s.TanggalSurat,
                keperluan = s.Keperluan,
                keterangan = s.Keterangan,
                jenis = jenisNama,
                labelJenis = labelJenis.TryGetValue(jenisNama, out var label)
                    ? label
                    : jenisNama.Replace('_', ' '),
                nama = string.IsNullOrWhiteSpace(nama) ? "-" : nama,
                jk,
                ttl = string.IsNullOrWhiteSpace(ttl) ? "-" : ttl,
                alamat = string.IsNullOrWhiteSpace(alamat) ? "-" : alamat,
                status = statusSurat,
                statusLabel = statusSurat switch
                {
                    "Active" => "Aktif",
                    "Cancelled" => "Dibatalkan",
                    _ => "Draft"
                },
                // Hanya surat yang memang melewati alur persetujuan yang
                // membawa keterangan; surat tanpa alur tidak perlu baris
                // "Tanpa persetujuan" di setiap baris register.
                statusPersetujuan = s.LewatPersetujuan ? s.StatusPersetujuanTampil : null,
                dibuatPada = s.CreatedAt
            };
        })
    });
}).WithName("DaftarSurat");

// ==== Permintaan surat online (alur tulis) ====
// POST — muka publik untuk sistem luar/warga; berkey seperti listener Core.
// Badan dibaca manual lewat ReadFromJsonAsync: dengan ada parameter HttpContext
// di lambda, inferensi binding minimal API bisa tidak mengikat badan JSON.
app.MapPost("/sudes/api/v1/permintaan", async (HttpContext ctx, CancellationToken ct) =>
{
    var tolak = TolakBilaTanpaKunci(ctx, "POST", "/sudes/api/v1/permintaan");
    if (tolak != null) return tolak;

    ApiPermintaanMasuk? masuk;
    try
    {
        masuk = await ctx.Request.ReadFromJsonAsync<ApiPermintaanMasuk>(cancellationToken: ct);
    }
    catch (System.Text.Json.JsonException)
    {
        return Results.BadRequest(new { sukses = false, pesan = "Badan permintaan tidak terbaca." });
    }
    if (masuk == null)
        return Results.BadRequest(new { sukses = false, pesan = "Badan permintaan kosong." });

    // Log tanpa data pribadi (ikut aturan Core: badan permintaan tidak dicatat).
    ctx.RequestServices.GetRequiredService<ILoggerFactory>()
        .CreateLogger("SuDesApp.Api.Permintaan")
        .LogInformation("Permintaan masuk: jenis={Jenis}, sumber=api", masuk.JenisSurat);

    var hasil = await ctx.RequestServices
        .GetRequiredService<IApiPermintaanService>().BuatAsync(masuk, ct);
    if (!hasil.Berhasil)
        return Results.BadRequest(new { sukses = false, pesan = hasil.Pesan });
    return Results.Ok(new
    {
        sukses = true,
        data = new { kode = hasil.Kode, sudahAda = hasil.SudahAda, pesan = hasil.Pesan }
    });
}).WithName("KirimPermintaan");

// POST publik — dipakai formulir "Layanan Online" di website desa.
// Warga mengirim tanpa akun dan tanpa kunci API, jadi jalur ini sengaja tidak
// memeriksa kunci; penahannya adalah batas kirim per alamat IP. Kanal berkey
// (POST /permintaan di atas) tetap ada untuk sistem desa yang memegang kunci.
app.MapPost("/sudes/api/v1/permintaan-publik", async (HttpContext ctx, CancellationToken ct) =>
{
    var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "tidak-dikenal";
    if (TerlaluSering($"permintaan-publik:{ip}"))
    {
        return Results.Json(
            new { sukses = false, pesan = "Terlalu banyak permintaan dari perangkat ini. Coba lagi dalam beberapa menit." },
            statusCode: StatusCodes.Status429TooManyRequests);
    }

    ApiPermintaanMasuk? masuk;
    try
    {
        masuk = await ctx.Request.ReadFromJsonAsync<ApiPermintaanMasuk>(cancellationToken: ct);
    }
    catch (System.Text.Json.JsonException)
    {
        return Results.BadRequest(new { sukses = false, pesan = "Badan permintaan tidak terbaca." });
    }
    if (masuk == null)
        return Results.BadRequest(new { sukses = false, pesan = "Badan permintaan kosong." });

    // Log tanpa data pribadi (ikut aturan Core: badan permintaan tidak dicatat).
    ctx.RequestServices.GetRequiredService<ILoggerFactory>()
        .CreateLogger("SuDesApp.Api.Permintaan")
        .LogInformation("Permintaan masuk: jenis={Jenis}, sumber=website", masuk.JenisSurat);

    var hasil = await ctx.RequestServices
        .GetRequiredService<IApiPermintaanService>().BuatAsync(masuk, ct);
    if (!hasil.Berhasil)
        return Results.BadRequest(new { sukses = false, pesan = hasil.Pesan });
    return Results.Ok(new
    {
        sukses = true,
        data = new { kode = hasil.Kode, sudahAda = hasil.SudahAda, pesan = hasil.Pesan }
    });
}).WithName("KirimPermintaanPublik");

// GET status satu permintaan (kontrak eksternal, juga berkey).
app.MapGet("/sudes/api/v1/permintaan/{kode}", async (HttpContext ctx, string kode, CancellationToken ct) =>
{
    var tolak = TolakBilaTanpaKunci(ctx, "GET", $"/sudes/api/v1/permintaan/{kode}");
    if (tolak != null) return tolak;

    var status = await ctx.RequestServices
        .GetRequiredService<IApiPermintaanService>().AmbilStatusAsync(kode, ct);
    return status == null
        ? Results.NotFound(new { sukses = false, pesan = "Kode permintaan tidak dikenal." })
        : Results.Ok(new { sukses = true, data = status });
}).WithName("StatusPermintaan");

// GET antrean — muka INTERNAL untuk UI aplikasi (Layanan Online), berisi data
// pribadi (nama, NIK, no. HP) yang oleh Core hanya ditampilkan ke operator.
// Pelindungnya: sesi login aplikasi (Bearer token) — tanpa sesi, 401.
app.MapGet("/sudes/api/v1/permintaan", async (HttpContext ctx, string? status, CancellationToken ct) =>
{
    if (SesiDari(ctx) == null) return Results.Unauthorized();

    var daftar = await ctx.RequestServices
        .GetRequiredService<IPermintaanWaRepository>().GetAllAsync(status, ct);
    return Results.Ok(daftar.Select(p => new
    {
        kode = p.KodePermintaan,
        tanggal = p.TanggalPermintaan,
        jenis = p.NamaJenis,
        nama = p.NamaWarga,
        nomorHp = p.NomorWA,
        nik = p.NIK,
        status = p.Status,
        sumber = p.Sumber,
        dibaca = p.IsRead
    }));
}).WithName("AntreanPermintaan");

// ==== Sesi login aplikasi (masuk / siapa / keluar / pemasangan) ====
// Aturannya identik dengan layar login desktop: PenggunaService (PBKDF2,
// kunci 3x gagal 5 menit, pesan gagal yang disamarkan). Yang ditambah di sini
// hanya pembungkus HTTP berupa token sesi — tanpa Bearer token, endpoint
// internal menjawab 401.
//
// Catat jujur: SessionContext (static global milik Core) TIDAK diset dari sini.
// Audit Core selalu menerima nama pengguna eksplisit, jadi jalur ini aman
// tanpa berbagi identitas antar-permintaan. Menegakkan izin per-permintaan
// (SessionContext.Wajib untuk simpan surat) adalah pekerjaan tahap berikutnya.

// Badan POST /sesi: nama field snake_case mengikuti kontrak API desa,
// dibaca manual (bukan inferensi minimal API) seperti endpoint permintaan.
app.MapPost("/sudes/api/v1/sesi", async (HttpContext ctx, CancellationToken ct) =>
{
    BadanMasuk? badan;
    try
    {
        badan = await ctx.Request.ReadFromJsonAsync<BadanMasuk>(cancellationToken: ct);
    }
    catch (System.Text.Json.JsonException)
    {
        return Results.BadRequest(new { sukses = false, pesan = "Badan permintaan tidak terbaca." });
    }
    if (badan == null || string.IsNullOrWhiteSpace(badan.Username) || string.IsNullOrEmpty(badan.KataSandi))
        return Results.BadRequest(new { sukses = false, pesan = "Nama pengguna dan kata sandi wajib diisi." });

    var penggunaSvc = ctx.RequestServices.GetRequiredService<IPenggunaService>();

    // Database tanpa satu pun akun = pemasangan baru. Pesannya dibedakan dari
    // "sandi salah" supaya operator tidak menebak-nebak di depan layar masuk.
    if (!await penggunaSvc.AdaPenggunaAsync(ct))
        return Results.Json(new
        {
            sukses = false,
            pesan = "Belum ada akun di database ini. Buka aplikasi desktop sekali untuk membuat akun, " +
                    "atau buat akun awal dari mesin lokal (endpoint /sudes/api/v1/sesi/awal)."
        }, statusCode: StatusCodes.Status403Forbidden);

    var hasil = await penggunaSvc.MasukAsync(
        badan.Username.Trim().ToLowerInvariant(), badan.KataSandi, ct);

    if (!hasil.Berhasil || hasil.Pengguna == null)
        return Results.Json(
            new { sukses = false, pesan = hasil.Pesan, terkunci = hasil.Terkunci },
            statusCode: StatusCodes.Status401Unauthorized);

    var sesi = ctx.RequestServices.GetRequiredService<SesiService>().Buat(hasil.Pengguna);
    ctx.RequestServices.GetRequiredService<ILoggerFactory>()
        .CreateLogger("SuDesApp.Api.Sesi")
        .LogInformation("Pengguna {Username} masuk dari {Remote}.",
            sesi.Username, ctx.Connection.RemoteIpAddress);

    return Results.Ok(new
    {
        sukses = true,
        token = sesi.Token,
        berlakuSampai = sesi.BerlakuSampai,
        pengguna = new { username = sesi.Username, nama = sesi.NamaTampilan, peran = sesi.Peran }
    });
}).WithName("MasukSesi");

// GET siapa — identitas sesi yang sedang berjalan (dipakai UI untuk memulihkan
// tampilan setelah refresh halaman).
app.MapGet("/sudes/api/v1/sesi/siapa", (HttpContext ctx) =>
{
    var sesi = SesiDari(ctx);
    return sesi == null
        ? Results.Unauthorized()
        : Results.Ok(new
        {
            username = sesi.Username,
            nama = sesi.NamaTampilan,
            peran = sesi.Peran,
            berlakuSampai = sesi.BerlakuSampai
        });
}).WithName("SiapaSesi");

// DELETE sesi — logout. Token yang sudah diakhiri langsung tidak berlaku
// untuk semua endpoint internal.
app.MapDelete("/sudes/api/v1/sesi", (HttpContext ctx) =>
{
    var token = TokenSesi(ctx);
    if (token == null) return Results.Unauthorized();
    ctx.RequestServices.GetRequiredService<SesiService>().Akhiri(token);
    return Results.Ok(new { sukses = true });
}).WithName("KeluarSesi");

// POST /sesi/awal — pembuatan akun pertama untuk database kosong (padanan
// PastikanAkunAdminAsync di layar login WPF). Dua syarat ganda agar tidak
// jadi pintu belakang: hanya dari loopback, dan hanya selama tabel Pengguna
// masih kosong (sekali seumur database). Lewat keduanya, endpoint ini selalu
// menjawab 409.
app.MapPost("/sudes/api/v1/sesi/awal", async (HttpContext ctx, CancellationToken ct) =>
{
    var remote = ctx.Connection.RemoteIpAddress;
    if (remote == null || !System.Net.IPAddress.IsLoopback(remote))
        return Results.Json(new { sukses = false, pesan = "Hanya bisa dijalankan dari mesin lokal." },
            statusCode: StatusCodes.Status403Forbidden);

    BadanMasuk? badan;
    try
    {
        badan = await ctx.Request.ReadFromJsonAsync<BadanMasuk>(cancellationToken: ct);
    }
    catch (System.Text.Json.JsonException)
    {
        return Results.BadRequest(new { sukses = false, pesan = "Badan permintaan tidak terbaca." });
    }
    if (badan == null || string.IsNullOrWhiteSpace(badan.Username) || string.IsNullOrEmpty(badan.KataSandi))
        return Results.BadRequest(new { sukses = false, pesan = "Nama pengguna dan kata sandi wajib diisi." });
    if (badan.KataSandi.Length < KataSandiPengguna.PanjangMinimal)
        return Results.BadRequest(new { sukses = false, pesan = $"Kata sandi minimal {KataSandiPengguna.PanjangMinimal} karakter." });

    var penggunaSvc = ctx.RequestServices.GetRequiredService<IPenggunaService>();
    if (await penggunaSvc.AdaPenggunaAsync(ct))
        return Results.Conflict(new { sukses = false, pesan = "Database sudah punya akun; pembuatan awal ditutup." });

    try
    {
        // dariMigrasi: true — jalur pemasangan pertama (sama seperti layar login
        // WPF memanggil TambahAsync sebelum ada sesi); validasi sandi ketat
        // tetap ditegakkan di atas lewat PanjangMinimal.
        var akun = await penggunaSvc.TambahAsync(
            badan.Username.Trim(),
            string.IsNullOrWhiteSpace(badan.Nama) ? badan.Username.Trim() : badan.Nama.Trim(),
            PeranPengguna.Administrator,
            badan.KataSandi,
            oleh: "pemasangan",
            dariMigrasi: true,
            ct);

        return Results.Ok(new
        {
            sukses = true,
            pengguna = new { username = akun.Username, nama = akun.NamaTampilan, peran = akun.Peran }
        });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { sukses = false, pesan = ex.Message });
    }
}).WithName("SesiAwal");

// ==== Kelola pengguna (menu "Pengguna & Peran") ====
// Jalur pertama yang benar-benar menegakkan izin Core: KelolaPengguna hanya
// dimiliki Administrator, dan pemeriksaannya tetap di PenggunaService
// (SessionContext.Wajib), bukan aturan yang disalin ulang di endpoint.
// Tanpa sesi → 401; sesi dengan peran lain → 403 dari middleware di atas.
// Hash/salt tidak pernah dikirim keluar.

app.MapGet("/sudes/api/v1/pengguna", async (HttpContext ctx, CancellationToken ct) =>
{
    if (SesiDari(ctx) == null) return Results.Unauthorized();

    SessionContext.Wajib(IzinAplikasi.KelolaPengguna);

    var daftar = await ctx.RequestServices.GetRequiredService<IPenggunaService>().DaftarAsync(ct);
    return Results.Ok(daftar.Select(p => new
    {
        id = p.ID,
        username = p.Username,
        nama = p.NamaTampilan,
        peran = p.Peran,
        peranTampil = p.PeranTampil,
        aktif = p.Aktif,
        loginTerakhir = p.LoginTerakhir,
        dibuatOleh = p.DibuatOleh,
        dibuatPada = p.CreatedAt
    }));
}).WithName("DaftarPengguna");

app.MapPost("/sudes/api/v1/pengguna", async (HttpContext ctx, CancellationToken ct) =>
{
    var sesi = SesiDari(ctx);
    if (sesi == null) return Results.Unauthorized();

    // Diperiksa lebih dulu supaya peran tanpa wewenang mendapat 403, bukan
    // pesan validasi yang membocorkan bentuk permintaan.
    SessionContext.Wajib(IzinAplikasi.KelolaPengguna);

    BadanPengguna? badan;
    try
    {
        badan = await ctx.Request.ReadFromJsonAsync<BadanPengguna>(cancellationToken: ct);
    }
    catch (System.Text.Json.JsonException)
    {
        return Results.BadRequest(new { sukses = false, pesan = "Badan permintaan tidak terbaca." });
    }

    if (badan == null || string.IsNullOrWhiteSpace(badan.Username))
        return Results.BadRequest(new { sukses = false, pesan = "Nama pengguna wajib diisi." });

    try
    {
        var akun = await ctx.RequestServices.GetRequiredService<IPenggunaService>().TambahAsync(
            badan.Username.Trim(),
            string.IsNullOrWhiteSpace(badan.Nama) ? badan.Username.Trim() : badan.Nama.Trim(),
            badan.Peran ?? PeranPengguna.Operator,
            badan.KataSandi ?? string.Empty,
            oleh: sesi.Username,
            ct: ct);

        return Results.Ok(new
        {
            sukses = true,
            pengguna = new
            {
                id = akun.ID,
                username = akun.Username,
                nama = akun.NamaTampilan,
                peran = akun.Peran,
                peranTampil = akun.PeranTampil
            }
        });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { sukses = false, pesan = ex.Message });
    }
}).WithName("TambahPengguna");

app.MapPut("/sudes/api/v1/pengguna/{id:int}", async (HttpContext ctx, int id, CancellationToken ct) =>
{
    var sesi = SesiDari(ctx);
    if (sesi == null) return Results.Unauthorized();

    SessionContext.Wajib(IzinAplikasi.KelolaPengguna);

    BadanPengguna? badan;
    try
    {
        badan = await ctx.Request.ReadFromJsonAsync<BadanPengguna>(cancellationToken: ct);
    }
    catch (System.Text.Json.JsonException)
    {
        return Results.BadRequest(new { sukses = false, pesan = "Badan permintaan tidak terbaca." });
    }

    if (badan == null)
        return Results.BadRequest(new { sukses = false, pesan = "Badan permintaan kosong." });

    var penggunaSvc = ctx.RequestServices.GetRequiredService<IPenggunaService>();

    // Keadaan sebelum diubah dibaca dulu supaya jelas sesi siapa yang harus
    // diputus setelah wewenangnya berubah.
    var sebelum = (await penggunaSvc.DaftarAsync(ct)).FirstOrDefault(p => p.ID == id);
    if (sebelum == null)
        return Results.NotFound(new { sukses = false, pesan = "Akun tidak ditemukan." });

    var peranBaru = PeranPengguna.Normalisasi(badan.Peran ?? sebelum.Peran);
    var aktifBaru = badan.Aktif ?? sebelum.Aktif;

    try
    {
        await penggunaSvc.UbahAsync(
            id, badan.Nama ?? sebelum.NamaTampilan, peranBaru, aktifBaru, sesi.Username, ct);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { sukses = false, pesan = ex.Message });
    }

    // Perubahan wewenang berlaku saat itu juga: tanpa ini token lama masih
    // membawa peran lama (atau akun nonaktif) sampai kedaluwarsa 12 jam.
    var diputus = 0;
    if (!aktifBaru || !string.Equals(peranBaru, sebelum.Peran, StringComparison.OrdinalIgnoreCase))
        diputus = ctx.RequestServices.GetRequiredService<SesiService>().AkhiriSemua(sebelum.Username);

    return Results.Ok(new { sukses = true, sesiDiputus = diputus });
}).WithName("UbahPengguna");

// Reset kata sandi oleh Administrator. KelolaPengguna diperiksa dua kali:
// sekali di sini (403 sebelum badan dibaca) dan sekali di Core lewat Wajib.
// Sesudah reset, token lama akun itu diputus — kata sandi baru berarti sesi
// lama tidak boleh tetap berjalan.
app.MapPost("/sudes/api/v1/pengguna/{id:int}/kata-sandi", async (HttpContext ctx, int id, CancellationToken ct) =>
{
    var sesi = SesiDari(ctx);
    if (sesi == null) return Results.Unauthorized();

    SessionContext.Wajib(IzinAplikasi.KelolaPengguna);

    BadanPengguna? badan;
    try
    {
        badan = await ctx.Request.ReadFromJsonAsync<BadanPengguna>(cancellationToken: ct);
    }
    catch (System.Text.Json.JsonException)
    {
        return Results.BadRequest(new { sukses = false, pesan = "Badan permintaan tidak terbaca." });
    }

    if (badan == null || string.IsNullOrWhiteSpace(badan.KataSandi))
        return Results.BadRequest(new { sukses = false, pesan = "Kata sandi baru wajib diisi." });

    var penggunaSvc = ctx.RequestServices.GetRequiredService<IPenggunaService>();
    var target = (await penggunaSvc.DaftarAsync(ct)).FirstOrDefault(p => p.ID == id);
    if (target == null)
        return Results.NotFound(new { sukses = false, pesan = "Akun tidak ditemukan." });

    try
    {
        await penggunaSvc.ResetKataSandiAsync(id, badan.KataSandi, sesi.Username, ct);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { sukses = false, pesan = ex.Message });
    }

    var diputus = ctx.RequestServices.GetRequiredService<SesiService>().AkhiriSemua(target.Username);
    return Results.Ok(new { sukses = true, sesiDiputus = diputus });
}).WithName("ResetKataSandiPengguna");

app.MapDelete("/sudes/api/v1/pengguna/{id:int}", async (HttpContext ctx, int id, CancellationToken ct) =>
{
    if (SesiDari(ctx) == null) return Results.Unauthorized();

    SessionContext.Wajib(IzinAplikasi.KelolaPengguna);

    var penggunaSvc = ctx.RequestServices.GetRequiredService<IPenggunaService>();
    var target = (await penggunaSvc.DaftarAsync(ct)).FirstOrDefault(p => p.ID == id);
    if (target == null)
        return Results.NotFound(new { sukses = false, pesan = "Akun tidak ditemukan." });

    try
    {
        await penggunaSvc.HapusAsync(id, ct);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { sukses = false, pesan = ex.Message });
    }

    // Token milik akun yang sudah tidak ada tidak boleh tetap bisa dipakai.
    var diputus = ctx.RequestServices.GetRequiredService<SesiService>().AkhiriSemua(target.Username);
    return Results.Ok(new { sukses = true, sesiDiputus = diputus });
}).WithName("HapusPengguna");

app.Run();

/// <summary>Badan POST/PUT /pengguna.</summary>
sealed class BadanPengguna
{
    [System.Text.Json.Serialization.JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("nama")]
    public string? Nama { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("peran")]
    public string? Peran { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("kata_sandi")]
    public string? KataSandi { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("aktif")]
    public bool? Aktif { get; set; }
}

/// <summary>Badan POST /sesi dan /sesi/awal.</summary>
sealed class BadanMasuk
{
    [System.Text.Json.Serialization.JsonPropertyName("username")]
    public string? Username { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("nama")]
    public string? Nama { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("kata_sandi")]
    public string? KataSandi { get; set; }
}
