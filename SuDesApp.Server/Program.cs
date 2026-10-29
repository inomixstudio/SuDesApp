using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using SuDesApp.Server;

// Batas ukuran payload webhook Midtrans. Notifikasi aslinya 1-2 KB, jadi 64 KB
// memberi kelonggaran besar tanpa membuka jalur "kirim 30 MB".
const long BatasUkuranWebhook = 64 * 1024;

// Batas body untuk SATU endpoint yang menerima gambar: POST tandai-lunas dengan
// lampiran bukti transfer. Batas gambar yang disimpan 2 MB (BuktiTransfer), dan
// base64 membengkakkan 4/3, jadi 4 MB memberi ruang untuk teks + sampul JSON.
const long BatasUnggahBukti = BuktiTransfer.MaksUkuran * 2L;

var builder = WebApplication.CreateBuilder(args);

// Muat kredensial lokal (di luar git) bila ada: appsettings.lokal.json.
//
// CATATAN PENTING soal presedensi: provider yang ditambahkan lewat
// ConfigurationManager SELALU menang atas apa pun yang sudah dimuat — termasuk
// atas environment variable. Menambahkannya SETELAH CreateBuilder (seperti
// sebelumnya) membuat env var seperti LISENSI_KEY_PATH diam-diam diabaikan
// begitu berkas lokal ini mendefinisikan kunci yang sama. Konsekuensinya:
// container membuat kunci penandatangan baru tiap restart dan SETIAP lisensi
// pelanggan langsung invalid tanpa pesan error apa pun.
//
// Para pembaca kode di bawah memakai pola env-first secara eksplisit, jadi
// berkas ini sengaja dimuat paling akhir DAN selalu dibaca lewat Ambil().
builder.Configuration.AddJsonFile("appsettings.lokal.json", optional: true, reloadOnChange: false);

// Petakan environment variable operator admin yang didokumentasikan
// (SUDES_ADMIN_OPERATORS__<indeks>__*) ke bagian konfigurasi yang dibaca
// AdminOperatorRegistry (Lisensi:AdminOperators:<indeks>:*).
//
// Provider environment bawaan hanya mengubah "__" menjadi ":", sehingga tanpa
// pemetaan ini nilainya menjadi kunci "SUDES_ADMIN_OPERATORS:0:ID" — tidak
// pernah terlihat oleh registry. Akibatnya server produksi dengan env yang
// benar tetap menolak semua login admin (503 "operator tidak aktif").
foreach (var (kunci, nilai) in OperatorAdminEnvironment.Petakan(Environment.GetEnvironmentVariables()))
{
    builder.Configuration[kunci] = nilai;
}

// Katalog paket langganan — harga dan durasi, satu sumber kebenaran.
var katalog = new KatalogPaket(builder.Configuration);
builder.Services.AddSingleton(katalog);

// === Supabase Postgres sebagai penyimpanan pelanggan & langganan ===
// Environment variable DIDULUKAN atas appsettings: saat deploy, platform
// menyuntik credential lewat env, dan nilainya harus menang atas berkas lokal
// yang ikut terbawa. Urutan terbalik membuat pengujian override diam-diam
// menguji konfigurasi lama.
string? Ambil(string env, string key) =>
    Environment.GetEnvironmentVariable(env) ?? builder.Configuration[key];

var host = Ambil("SUPABASE_HOST", "Supabase:Host") ?? "";
// Role aplikasi = role TERBATAS (bukan pemilik tabel): RLS hanya berlaku
// untuk role seperti ini. Lihat schema.sql bagian "ROLE APLIKASI".
var userAplikasi = Ambil("SUPABASE_DB_USER", "Supabase:User") ?? "sudes_server";
var passwordAplikasi = Ambil("SUPABASE_DB_PASSWORD", "Supabase:DbPassword") ?? "";
// Kredensial pemilik HANYA untuk menerapkan schema.sql saat start; opsional.
var userMigrasi = Ambil("SUPABASE_MIGRATE_USER", "Supabase:MigrateUser") ?? "postgres";
var passwordMigrasi = Ambil("SUPABASE_MIGRATE_PASSWORD", "Supabase:MigratePassword") ?? "";
var dbPort = builder.Configuration.GetValue("Supabase:Port", 5432);
var sslMode = Ambil("SUPABASE_SSLMODE", "Supabase:SslMode") ?? "Require";

// Satu factory log untuk semua layanan yang dibangun di sini.
//
// Factory ini TIDAK pernah di-dispose sebelumnya, dan konfigurasinya
// (AddConsole tanpa opsi) mengabaikan setelan log dari appsettings.json —
// sehingga diagritik startup tidak bisa diredupkan maupun diformat.
// Sekarang mengikuti host dan ikut dibersihkan di akhir.
using var logs = LoggerFactory.Create(b =>
{
    b.AddConsole();
    b.AddConfiguration(builder.Configuration.GetSection("Logging"));
    b.SetMinimumLevel(
        builder.Configuration.GetValue("Logging:LogLevel:Default", LogLevel.Information));
});

// Penyimpanan: Supabase di produksi, atau berkas lokal di Development bila
// kredensial database belum diisi. Mode berkas membuat server bisa dijalankan
// di satu komputer desa — aplikasi yang menyalakannya, halaman /admin, dan alur
// lisensi — dengan data langganan yang TETAP ADA setelah server berhenti.
// Mode ini tidak pernah boleh dipakai di Production: ia tidak memakai RLS,
// tidak punya replika, dan tidak punya jejak akses lintas-tenant.
IRepositoryLangganan repo;
IAdminAuditStore audit;
IPengumumanStore pengumuman;
if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(passwordAplikasi))
{
    if (!builder.Environment.IsDevelopment())
    {
        Console.Error.WriteLine(
            "Kredensial Supabase belum diatur. Isi SuDesApp.Server/appsettings.lokal.json " +
            "(Supabase:Host + Supabase:DbPassword) atau set SUPABASE_HOST / SUPABASE_DB_PASSWORD. " +
            "Kata sandi itu milik role terbatas sudes_server — lihat schema.sql.");
        return;
    }

    string jalurLokal = PenyimpananLokal.JalurBawaan(builder.Configuration);
    var lokal = await PenyimpananLokal.BukaAsync(jalurLokal, logs.CreateLogger<PenyimpananLokal>());
    repo = lokal;
    audit = lokal;
    pengumuman = lokal;
    await repo.InitializeAsync();
    Console.WriteLine(
        "[dev] Supabase belum dikonfigurasi — memakai penyimpanan BERKAS: " + lokal.Jalur + ". " +
        "Desa, langganan, tagihan, pembayaran, dan pengumuman tetap ada setelah server berhenti. " +
        "Jangan pakai mode ini di produksi (tanpa RLS & tanpa replika).");
}
else
{
    var csAplikasi = SupabaseStore.BuildConnectionString(host, passwordAplikasi, userAplikasi, "postgres", dbPort, sslMode);
    var csMigrasi = string.IsNullOrWhiteSpace(passwordMigrasi)
        ? null
        : SupabaseStore.BuildConnectionString(host, passwordMigrasi, userMigrasi, "postgres", dbPort, sslMode);

    var loggerStore = logs.CreateLogger<SupabaseStore>();
    var store = new SupabaseStore(csAplikasi, csMigrasi, loggerStore);
    try
    {
        await store.InitializeAsync();
    }
    catch (Exception ex)
    {
        ServerLog.PersiapanSkemaGagal(loggerStore, ex);
        Console.Error.WriteLine(ex.Message);
        return;
    }

    repo = store;
    audit = store;
    pengumuman = store;
}

// ContentRootPath dikirim agar jalur kunci bawaan (keys/lisensi-signing.key)
// tetap menunjuk folder proyek walau proses dijalankan dari direktori lain.
var signing = new SigningService(
    builder.Configuration, logs.CreateLogger<SigningService>(), builder.Environment.ContentRootPath);
builder.Services.AddSingleton(signing);
builder.Services.AddSingleton(audit);
builder.Services.AddSingleton(pengumuman);
// Daftarkan lewat interface: LayananWebhook bergantung pada IRepositoryLangganan
// supaya aturan pembayaran bisa diuji dengan LanggananMemori tanpa database.
builder.Services.AddSingleton(repo);

var midtrans = new MidtransService(builder.Configuration, logs.CreateLogger<MidtransService>());
builder.Services.AddSingleton(midtrans);
builder.Services.AddSingleton<IPenyediaStatusPembayaran>(midtrans);
builder.Services.AddSingleton<IPembuatTagihan>(midtrans);
builder.Services.AddSingleton<LayananWebhook>();

// === Pelacak notifikasi webhook (jejak di memori) ===
// Didaftarkan SEBELUM LayananWebhook supaya pelacaknya ikut terinjeksi ke
// sana: notifikasi yang masuk dicatat sebagai buktinya, supaya operator
// bisa melihat "notifikasi sungguhan datang atau tidak" tanpa menggali log.
// Jejak ini di MEMORI saja (bertahan selama server hidup) — bukan arsip.
builder.Services.AddSingleton<PelacakWebhook>();

// === Aturan endpoint yang memberi hak akses ===
// Dipindahkan dari lambda endpoint ke kelas sendiri supaya bisa diuji tanpa
// HTTP: batas perangkat, kedaluwarsa, dan pemilihan harga adalah aturan yang
// paling mahal kalau salah, sama seperti aturan webhook.
builder.Services.AddSingleton(sp => new LayananAktivasi(
    sp.GetRequiredService<IRepositoryLangganan>(),
    signing,
    sp.GetRequiredService<ILogger<LayananAktivasi>>()));
builder.Services.AddSingleton(sp => new LayananTagihan(
    sp.GetRequiredService<IRepositoryLangganan>(),
    katalog,
    sp.GetRequiredService<IPembuatTagihan>(),
    sp.GetRequiredService<ILogger<LayananTagihan>>(),
    pendaftarDesa: sp.GetRequiredService<LayananAdmin>()));

// === Pusat administer (kelola perangkat per desa) ===
// Dipisah jadi layanan supaya aksi yang MEMBUKA kuota lisensi bisa diuji tanpa
// HTTP. Endpoint /admin/* memakainya setelah operator dan izinnya diverifikasi.
builder.Services.AddSingleton(sp => new LayananAdmin(
    sp.GetRequiredService<IRepositoryLangganan>(),
    sp.GetRequiredService<ILogger<LayananAdmin>>()));

// === Pembukuan pembayaran manual (QRIS statis / transfer bank) ===
// Uang sudah diterima operator, jadi jalur ini MENAMBAH hak akses tanpa
// gateway. Ia memakai PerpanjangAsync yang SAMA dengan webhook supaya aturan
// idempotensi & perpanjangan tetap satu salinan; pemanggilnya WAJIB endpoint
// bertoken admin (lihat /admin/pelanggan/{kode}/tandai-lunas).
builder.Services.AddSingleton(sp => new LayananPembayaranManual(
    sp.GetRequiredService<IRepositoryLangganan>(),
    katalog,
    sp.GetRequiredService<ILogger<LayananPembayaranManual>>()));

// === Uji pembayaran sungguhan (alur checkout uji) ===
// Alur operator: buat transaksi Snap NYATA (production bila servernya
// production), bayar lewat halaman Midtrans, lalu lihat notifikasi aslinya
// sampai server membalas dan perpanjangan langganan terjadi.
//
// Harga & durasi tetap dari katalog server, tagihan tetap dicatat sebelum
// gateway dipanggil, dan perpanjangan tetap lewat PerpanjangAsync yang sama
// dengan webhook — jadi jalur uji ini tidak punya aturan uangnya sendiri.
// Yang membedakannya hanya satu: notifikasi transaksi uji boleh diarahkan ke
// URL khusus lewat header X-Override-Notification, supaya operator bisa
// membuktikan alurnya di server yang sedang diuji.
var opsiUji = OpsiUjiPembayaran.DariKonfigurasi(builder.Configuration);
builder.Services.AddSingleton(opsiUji);
builder.Services.AddSingleton(sp => new LayananUjiPembayaran(
    sp.GetRequiredService<IRepositoryLangganan>(),
    katalog,
    sp.GetRequiredService<IPembuatTagihan>(),
    sp.GetRequiredService<IPenyediaStatusPembayaran>(),
    sp.GetRequiredService<PelacakWebhook>(),
    sp.GetRequiredService<LayananAdmin>(),
    opsiUji,
    sp.GetRequiredService<ILogger<LayananUjiPembayaran>>()));

// === Rekonsiliasi pembayaran (backstop webhook) ===
// Webhook tetap jalur utama; rekonsiliasi menutup kasus notifikasi yang tidak
// pernah sampai. Ia memakai LayananWebhook yang SAMA, jadi batas nominal,
// daftar status berduit, pemeriksaan fraud, dan penjaga idempotensi tidak
// punya salinan kedua yang bisa menyimpang dari jalur utama.
var opsiRekonsiliasi = OpsiRekonsiliasi.DariKonfigurasi(builder.Configuration);
builder.Services.AddSingleton(opsiRekonsiliasi);
builder.Services.AddSingleton(sp => new LayananRekonsiliasi(
    sp.GetRequiredService<IRepositoryLangganan>(),
    sp.GetRequiredService<IPenyediaStatusPembayaran>(),
    sp.GetRequiredService<LayananWebhook>(),
    sp.GetRequiredService<ILogger<LayananRekonsiliasi>>(),
    opsiRekonsiliasi));

if (opsiRekonsiliasi.Terjadwal && midtrans.Aktif)
{
    builder.Services.AddHostedService<RekonsiliasiLatar>();
}
else
{
    // Alasan dimatikan harus terbaca di log start: backstop yang tidak jalan
    // tanpa penjelasan lebih sulit ditemukan daripada yang mati berisik.
    Console.WriteLine(!opsiRekonsiliasi.Terjadwal
        ? "[rekonsiliasi] Penjadwalan latar dimatikan (interval 0 menit) — endpoint /admin/rekonsiliasi tetap bisa dipanggil manual."
        : "[rekonsiliasi] Penjadwalan latar dimatikan: ServerKey Midtrans belum diisi, status transaksi tidak bisa ditanyakan.");
}

// Mode pengembangan: hanya menyala lewat ASPNETCORE_ENVIRONMENT=Development.
// Di produksi webhook TIDAK BOLEH menerima notifikasi tanpa tanda tangan —
// tanpa ServerKey berarti siapa pun bisa memalsukan pembayaran.
var dev = builder.Environment.IsDevelopment();
if (!midtrans.Aktif && !dev)
{
    Console.Error.WriteLine(
        "ServerKey Midtrans belum diatur dan aplikasi tidak berjalan dalam mode Development. " +
        "Server refuses start: accepting unsigned webhooks would let anyone extend a " +
        "subscription for free. Set MIDTRANS_SERVER_KEY, or run with " +
        "ASPNETCORE_ENVIRONMENT=Development for local testing only.");
    return;
}
if (!midtrans.Aktif)
    Console.WriteLine("[midtrans] ServerKey belum di-set — Development: tagihan memakai link demo.");

// Pembatas laju memakai IP ASLI klien. Di belakang reverse proxy (Railway,
// Render, Caddy, nginx — lihat docs/deploy-server.md), RemoteIpAddress tanpa
// UseForwardedHeaders() adalah IP proxy untuk SEMUA permintaan: semua klien
// berbagi satu ember (limit jadi tak berguna sekaligus menyakitkan pengguna
// yang sah). ForwardedHeaders harus dipasang SEBELUM rate limiter.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
    ProxyForwarding.Configure(options, builder.Configuration));

builder.Services.AddRateLimiter(options =>
{
    // Pembatas per-IP untuk endpoint sensitif: mencegah enumerasi kode aktivasi
    // dan spam pembuatan tagihan yang menguras kuota Midtrans merchant.
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Pengaman cadangan untuk semua endpoint: setiap kebijakan di atas hanya
    // berlaku pada endpoint yang menyebutkannya, jadi endpoint yang lupa
    // (.RequireRateLimiting) tetap terbuka penuh tanpa pembatas apa pun.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "tidak-diketahui",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    options.AddPolicy("aktivasi", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "tidak-diketahui",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    options.AddPolicy("tagihan", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "tidak-diketahui",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    // Endpoint pusat administer. Terpisah dari "tagihan" karena operator boleh
    // memeriksa lalu menghapus beberapa kali saat menolong satu pelanggan, dan
    // pembuatan tagihan tetap harus ketat (kuota Midtrans). 30/menit cukup
    // lapang untuk satu operator, tetap membatasi enumerasi massal.
    options.AddPolicy("admin", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "tidak-diketahui",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    // Webhook Midtrans datang dari banyak IP resmi dan TAK BOLEH terlalu ketat
    // (notifikasi bisa datang dari IP berbeda dalam hitungan detik), tapi
    // endpoint ini tetap perlu pembatas: tanpa ini siapa pun bisa mengirim
    // body 30 MB terus-menerus.
    options.AddPolicy("webhook", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "tidak-diketahui",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 600,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// Batas ukuran body default berlaku di semua endpoint. 128 KB jauh di atas
// payload webhook Midtrans (~1-2 KB) tapi menutup jalur "kirim 30 MB" yang
// sebelumnya terbuka karena Kestrel default 30 MB.
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 128 * 1024;
    options.AddServerHeader = false;   // jangan dyinggikan versi Kestrel
});

// Exception yang tidak tertangani jadi ProblemDetails terstruktur, bukan
// teks ekspresi yang bisa membocorkan detail internal ke peramban.
builder.Services.AddProblemDetails();

builder.Services.AddHealthChecks();

var app = builder.Build();
var pengumumanStore = app.Services.GetRequiredService<IPengumumanStore>();

app.UseForwardedHeaders();

// Endpoint lisensi dan admin membawa token aktivasi atau token operator, atau
// perubahan pembayaran. Hanya health probe yang boleh diakses lewat HTTP;
// Development tetap mendukung loopback agar pengujian lokal tidak perlu TLS.
// X-Forwarded-Proto hanya dipercaya dari proxy yang dikonfigurasi di atas.
app.UseTransportSecurity(dev);

// WAJIB: AddRateLimiter() hanya mendaftarkan layanan DI. Tanpa pemanggilan
// middleware ini, seluruh kebijakan di atas adalah no-op — RejectionStatusCode
// tidak pernah menyala dan RateLimitPartition tidak pernah dievaluasi.
app.UseRateLimiter();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Sajikan halaman checkout statis (wwwroot) — sama-origin, jadi tanpa CORS.
app.UseStaticFiles();

// Batas 128 KB di bawah berlaku untuk SEMUA request, tetapi satu endpoint
// memang menerima gambar: lampiran bukti transfer pada pembukuan manual.
// Tanpa pengecualian ini, Kestrel menolak body-nya (413) SEBELUM lampirannya
// sempat dinilai — jadi tidak ada gambar yang bisa tersimpan sama sekali.
//
// Pengecualiannya sengaja sempit: hanya POST ke jalur tandai-lunas, bukan
// seluruh /admin/*. Endpoint lain (webhook, tagihan, pengumuman) tetap 128 KB.
// Dinaikkan lewat fitur HTTP (bukan konfigurasi Kestrel global) karena batas
// global itulah yang menjaga jalur "kirim 30 MB" tetap tertutup.
app.Use(async (context, next) =>
{
    if (HttpMethods.IsPost(context.Request.Method) &&
        context.Request.Path.Value?.EndsWith("/tandai-lunas", StringComparison.Ordinal) == true &&
        context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } batas)
    {
        batas.MaxRequestBodySize = BatasUnggahBukti;
    }

    await next();
});

// GET /healthz — untuk Docker HEALTHCHECK dan Railway/Render. Sengaja tidak
// menyentuh database: tujuannya membedakan "proses hidup" dari "proses mati",
// bukan melaporkan detail database. Pemeriksaan DB tetap ada di /admin.
app.MapGet("/healthz", () => Results.Ok(new { status = "sehat" }))
   .AllowAnonymous();

app.MapGet("/", () => Results.Ok(new { status = "SuDesApp lisensi server", waktu = DateTime.UtcNow }));

// GET /checkout — halaman web untuk memilih paket & membuat tagihan pembayaran
// (dibuka pembeli di peramban). Halaman hanya memakai endpoint publik
// (/paket, /buat-tagihan), jadi tidak ada rahasia (ServerKey/admin) di sana.
app.MapGet("/checkout", () =>
{
    var webRoot = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
    return Results.File(Path.Combine(webRoot, "checkout.html"), "text/html; charset=utf-8");
});

// GET /admin — halaman web "pusat administer" (operator). Halaman ini SENDIRI
// statis dan tanpa rahasia: kredensial operator diketik di peramban dan dikirim
// sebagai header Authorization di tiap panggilan /admin/*, jadi tidak ada token
// yang tertanam di HTML. Semua data tetap ditolak tanpa token yang sah.
app.MapGet("/admin", () =>
{
    var webRoot = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
    return Results.File(Path.Combine(webRoot, "admin.html"), "text/html; charset=utf-8");
});

// GET /checkout-uji — halaman web untuk UJI PEMBAYARAN (operator).
// Sengaja DI LUAR /admin: halaman ini statis dan tanpa rahasia (kredensial
// operator diketik di peramban lalu dikirim sebagai header tiap panggilan),
// sedangkan pengaman operator untuk /admin/* bekerja pada PERMINTAAN dengan
// header — yang tidak bisa dibawa navigasi halaman biasa. Halaman /admin
// sendiri dikecualikan karena alasan yang sama.
app.MapGet("/checkout-uji", () =>
{
    var webRoot = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
    return Results.File(Path.Combine(webRoot, "checkout-uji.html"), "text/html; charset=utf-8");
});

// GET /kunci-publik — kunci publik penandatangan. Client memverifikasi tanda
// tangan token dengan kunci ini secara LOKAL, jadi verifikasi tetap mungkin
// saat offline (server tidak perlu dihubungi sama sekali).
app.MapGet("/kunci-publik", () => Results.Ok(new { kunci = signing.KunciPublikB64Url, algoritma = "ES256" }));

// POST /aktivasi — cek kode aktivasi, batasi perangkat, terbitkan token lisensi
// BERTANDATANGAN. Token inilah yang membuat aplikasi bisa diverifikasi offline.
//
// Seluruh aturannya ada di LayananAktivasi (kode → perangkat → kedaluwarsa →
// token) dan bisa diuji tanpa HTTP server maupun database. Lambda ini hanya
// adaptasi HTTP → hasil; menaruh aturannya di sini membuatnya mustahil diuji.
// Pembalasan /aktivasi & /aktifkan-trial dibuat SAMA persis: kedua jalur
// memberi token lisensi bertandatangan, jadi klien cukup satu parser.
static IResult BalasAktivasi(HasilAktivasi hasil)
{
    if (hasil.Status != 200)
        return hasil.Status switch
        {
            400 => Results.BadRequest(new { pesan = hasil.Pesan }),
            404 => Results.NotFound(new { pesan = hasil.Pesan }),
            _ => Results.Json(new { pesan = hasil.Pesan }, statusCode: hasil.Status)
        };

    if (!hasil.Aktif)
    {
        // Tanpa token: client tidak boleh menganggap sah hanya karena pernah
        // punya token valid — token lama tetap dipakai hanya dalam masa
        // tenggangnya di sisi client.
        return Results.Ok(new
        {
            id = hasil.PelangganId,
            namaDesa = hasil.NamaDesa,
            aktif = false,
            berlakuSampai = hasil.BerlakuSampai?.ToString("O"),
            pesan = hasil.Pesan
        });
    }

    return Results.Ok(new
    {
        id = hasil.PelangganId,
        namaDesa = hasil.NamaDesa,
        aktif = true,
        berlakuSampai = hasil.BerlakuSampai?.ToString("O"),
        token = hasil.Token
    });
}

app.MapPost("/aktivasi", async (AktivasiRequest req, LayananAktivasi layanan, CancellationToken ct) =>
{
    var hasil = await layanan.AktivasiAsync(req, ct);
    return BalasAktivasi(hasil);
})
.RequireRateLimiting("aktivasi");

// POST /aktifkan-trial — mulai trial 30 hari (tanpa pembayaran) untuk desa
// yang kode aktivasi terdaftar. Guard-nya di server:
//   • sekali per desa SEUMUR HIDUP (kolom pelanggan.trial_sudah);
//   • hanya bila langganan belum sedang aktif;
//   • batas perangkat tetap berlaku.
// Bentuk permintaan & balasan identik dengan /aktivasi — klien mengirim
// { kodeAktivasi, idPerangkat }, dan menerima { aktif, berlakuSampai, token }.
app.MapPost("/aktifkan-trial", async (AktivasiRequest req, LayananAktivasi layanan, CancellationToken ct) =>
{
    var hasil = await layanan.AktifkanTrialAsync(req, ct);
    return BalasAktivasi(hasil);
})
.RequireRateLimiting("aktivasi");

// POST /buat-tagihan — tagihan via Midtrans Snap (QRIS/VA).
// Paket dipilih pengguna ("bulanan" / "tahunan"), tetapi HARGA tetap dari
// katalog server: klien tidak pernah menentukan nominal.
// Aturannya (kode dikenal → paket & HARGA dari katalog server → order id →
// tagihan dicatat → gateway) ada di LayananTagihan. Lambda ini hanya memetakan
// hasilnya ke bentuk JSON yang sudah dipakai klien.
app.MapPost("/buat-tagihan", async (BuatTagihanRequest req, LayananTagihan layanan, CancellationToken ct) =>
{
    var hasil = await layanan.BuatAsync(req, ct);

    if (hasil.GagalGateway)
        return Results.Problem(title: "Midtrans gagal", detail: hasil.Pesan);

    if (hasil.Status != 200)
        return hasil.Status switch
        {
            400 => Results.BadRequest(new { pesan = hasil.Pesan }),
            _ => Results.NotFound(new { pesan = hasil.Pesan })
        };

    var paket = hasil.Paket!;

    // Dua bentuk balasan dipertahankan PERSIS seperti sebelum aturannya pindah:
    // mode demo tidak mengirim medan "token" sama sekali (bukan token null),
    // dan mode midtrans mengirim token + tautan Snap. Klien lama membaca bentuk
    // ini apa adanya, jadi mempersatukannya menjadi satu objek berisi
    // "token": null adalah perubahan format yang tidak perlu.
    //
    // kode_aktivasi: hanya jalur BERLANGGANAN AWAL (desa baru) yang
    // menerbitkan kode — null pada perpanjangan, sehingga klien dapat
    // menyimpan kode itu saat pertama kali dibayar.
    return hasil.Mode == "demo"
        ? Results.Ok(new
        {
            orderId = hasil.OrderId,
            paket = paket.Kode,
            paketNama = paket.Nama,
            nominal = paket.Harga,
            hari = paket.Hari,
            kode_aktivasi = hasil.KodeAktivasi,
            link_pembayaran = hasil.LinkPembayaran,
            mode = "demo"
        })
        : Results.Ok(new
        {
            orderId = hasil.OrderId,
            paket = paket.Kode,
            paketNama = paket.Nama,
            nominal = paket.Harga,
            hari = paket.Hari,
            token = hasil.Token,
            kode_aktivasi = hasil.KodeAktivasi,
            link_pembayaran = hasil.LinkPembayaran,
            mode = "midtrans"
        });
})
.RequireRateLimiting("tagihan");

// GET /paket — daftar paket + harga untuk ditampilkan di aplikasi, supaya
// pengguna melihat angka yang sama persis dengan yang ditagih server.
app.MapGet("/paket", () => Results.Ok(katalog.Semua.Select(p => new
{
    kode = p.Kode,
    nama = p.Nama,
    harga = p.Harga,
    hari = p.Hari
})));

// GET /pengumuman — daftar pengumuman broadcast aktif untuk seluruh instalasi
// desa (jenis informasi terbaru yang diterbitkan operator dari tabel
// `pengumuman` di Supabase — lihat schema.sql / docs/pengumuman-aplikasi.md).
//
// Publik TANPA kunci, tapi isinya terbatas: hanya judul/isi/jenis — tidak ada
// data desa sama sekali, dan penyaringan aktif+jendela waktu dilakukan di
// LayananPengumuman yang bisa diuji tanpa HTTP. Aplikasi desa menagih ini
// tiap 60 detik dan menampilkan informasi yang belum pernah tampil di
// perangkatnya sebagai popup + lonceng notifikasi.
app.MapGet("/pengumuman", async () =>
{
    var mentah = await pengumumanStore.BacaPengumumanAsync();
    var aktif = LayananPengumuman.SaringAktif(mentah, DateTime.UtcNow);

    // Kolom `mulai` sengaja tidak dikirim (lihat PengumumanServer.Mulai).
    return Results.Ok(aktif.Select(p => new
    {
        id = p.Id,
        judul = p.Judul,
        isi = p.Isi,
        jenis = string.IsNullOrWhiteSpace(p.Jenis) ? "info" : p.Jenis,
        dibuat = p.Dibuat.ToString("O"),
        sampai = p.Sampai?.ToString("O")
    }));
});

// POST /webhook-pembayaran — notifikasi Midtrans.
//
// Seluruh aturan ada di LayananWebhook, yang bisa diuji tanpa HTTP server dan
// tanpa database. Endpoint ini hanya adaptasi HTTP → hasil. Mengilogika
// pembayaran di dalam lambda di sini membuatnya mustahil diuji, padahal ini
// justru lapisan yang memberi hak akses.
app.MapPost("/webhook-pembayaran", async (HttpContext ctx) =>
{
    // Batas ukuran ditegakkan SEBELUM membaca body. Sebelumnya body dibaca
    // penuh tanpa batas apa pun (Kestrel default 30 MB) lalu signature
    // diverifikasi belakangan — jadi penyerang bisa mengirim 30 MB × N kali
    // sebelum kemudian ditolak.
    if (ctx.Request.ContentLength is > BatasUkuranWebhook)
        return Results.Json(new { ditolak = true, pesan = "Body webhook terlalu besar." },
            statusCode: StatusCodes.Status413PayloadTooLarge);

    var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync(ctx.RequestAborted);
    if (Encoding.UTF8.GetByteCount(body) > BatasUkuranWebhook)
        return Results.Json(new { ditolak = true, pesan = "Body webhook terlalu besar." },
            statusCode: StatusCodes.Status413PayloadTooLarge);

    var webhook = ctx.RequestServices.GetRequiredService<LayananWebhook>();
    var hasil = await webhook.ProsesAsync(
        body,
        ctx.Connection.RemoteIpAddress?.ToString(),
        ctx.Request.Headers.UserAgent.ToString());

    if (hasil.Status == 200)
    {
        return Results.Ok(new
        {
            diterima = true,
            pesan = hasil.Pesan,
            paket = hasil.Paket,
            hariDitambah = hasil.HariDitambah,
            duplikat = hasil.Duplikat
        });
    }

    return Results.Json(new { ditolak = true, pesan = hasil.Pesan },
        statusCode: hasil.Status);
})
.RequireRateLimiting("webhook");

// Tiap operator memakai token pribadi dan izin eksplisit; rahasia token hanya
// disimpan dalam konfigurasi deployment, sedangkan registry hanya menyimpan hash.
var adminOperators = new AdminOperatorRegistry(builder.Configuration);
app.UseAdminOperatorSecurity(adminOperators);

app.MapGet("/admin/sesi", (HttpContext context) =>
{
    var operatorAktif = context.GetAdminOperator()!;
    return Results.Ok(new
    {
        id = operatorAktif.Id,
        nama = operatorAktif.Name,
        izin = operatorAktif.Permissions.OrderBy(izin => izin, StringComparer.Ordinal).ToArray()
    });
}).RequireRateLimiting("admin");

// POST /admin/pelanggan — daftarkan desa baru. Izin spesifik diverifikasi
// terpusat sebelum handler rute berjalan.

app.MapPost("/admin/pelanggan", async (HttpRequest req, DaftarkanPelangganRequest body, LayananAdmin admin) =>
{
    try
    {
        var p = await admin.DaftarkanAsync(body.KodeAktivasi, body.NamaDesa, body.Kontak ?? "", body.MaxPerangkat);
        return Results.Ok(new
        {
            p.Id,
            p.KodeAktivasi,
            p.NamaDesa,
            p.MaxPerangkat,
            belumBayar = true,
            catatan = "Langganan aktif setelah pembayaran pertama masuk lewat webhook."
        });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { pesan = ex.Message });
    }
})
.RequireRateLimiting("admin");

// POST /admin/kode-aktivasi — daftarkan desa baru dengan kode aktivasi yang
// DIBUATKAN SERVER (GeneratorKodeAktivasi, bentuk SUDES-XXXX-XXXX). Operator
// pusat administer cukup mengisi nama desa; kode yang dihasilkan dikirim ke
// desa lewat WhatsApp. Jalur pendaftaran manual (kode diketik operator) tetap
// tersedia lewat POST /admin/pelanggan.
app.MapPost("/admin/kode-aktivasi", async (BuatKodeAktivasiRequest body, LayananAdmin admin) =>
{
    try
    {
        var p = await admin.DaftarkanDenganKodeBaruAsync(body.NamaDesa, body.Kontak ?? "", body.MaxPerangkat);
        return Results.Ok(new
        {
            p.Id,
            p.KodeAktivasi,
            p.NamaDesa,
            p.MaxPerangkat,
            belumBayar = true,
            catatan = "Kode dibuat otomatis. Langganan aktif setelah pembayaran pertama masuk " +
                      "lewat webhook, atau ditandai manual lewat Aktivasi Manual di halaman ini."
        });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { pesan = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Json(new { pesan = ex.Message }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
})
.RequireRateLimiting("admin");

// POST /admin/rekonsiliasi — jalankan satu putaran rekonsiliasi SEKARANG.
//
// Dipakai operator setelah kejadian yang membuat webhook tidak dipercaya
// (URL webhook berubah, server sempat mati, Midtrans terlambat mengirim).
// Membutuhkan izin payments.reconcile: endpoint ini membaca daftar tagihan
// SEMUA desa, jadi tidak boleh terbuka untuk operator tanpa tugas tersebut.
app.MapPost("/admin/rekonsiliasi", async (HttpRequest req, CancellationToken ct) =>
{

    var layanan = req.HttpContext.RequestServices.GetRequiredService<LayananRekonsiliasi>();
    var ringkas = await layanan.JalankanAsync(ct);

    return Results.Ok(new
    {
        diperiksa = ringkas.Diperiksa,
        diselesaikan = ringkas.Diselesaikan,
        sudahDiselesaikan = ringkas.SudahDiselesaikan,
        belumBayar = ringkas.BelumBayar,
        perluDiperiksa = ringkas.PerluDiperiksa,
        tidakPasti = ringkas.TidakPasti,
        catatan = ringkas.Catatan
    });
})
.RequireRateLimiting("tagihan");

// POST /admin/pelanggan/{kode}/tandai-lunas — pembukuan pembayaran MANUAL.
//
// Dipakai untuk QRIS statis yang ditampilkan di layar pembayaran aplikasi desa
// (juga transfer bank): uangnya masuk ke rekening operator, bukan lewat gateway,
// jadi tidak ada signature maupun status Midtrans yang bisa diperiksa. Karena
// itu ia HANYA boleh dipanggil endpoint bertoken admin — ia memberi hak akses.
// Aturannya (paket → hari, nominal yang dicatat, idempotensi, perpanjangan dari
// sisa masa aktif) ada di LayananPembayaranManual, memakai PerpanjangAsync yang
// sama dengan webhook sehingga tidak ada salinan aturan kedua yang bisa
// menyimpang.
app.MapPost("/admin/pelanggan/{kode}/tandai-lunas",
    async (string kode, HttpRequest req, TandaiLunasRequest body, LayananPembayaranManual manual) =>
{

    // Batas yang sama dengan pengecualian body di middleware di atas, tapi
    // diperiksa lebih dulu supaya operator mendapat pesan yang jelas — bukan
    // 413 tanpa keterangan dari Kestrel.
    if (req.ContentLength is > BatasUnggahBukti)
        return Results.Json(
            new { pesan = $"Lampiran terlalu besar ({BuktiTransfer.UkuranTeks((int)Math.Min(int.MaxValue, req.ContentLength.Value))}). " +
                      $"Maksimum gambar {BuktiTransfer.UkuranTeks(BuktiTransfer.MaksUkuran)}." },
            statusCode: StatusCodes.Status413PayloadTooLarge);

    // Lampiran bukti transfer (opsional) dinilai di dalam LayananPembayaranManual
    // lewat BuktiTransfer: endpoint hanya meneruskan teks base64 & nama berkasnya.
    var hasil = await manual.TandaiLunasAsync(
        kode, body.Paket, body.Nominal, body.Catatan, body.BuktiBase64, body.BuktiNama);
    if (!hasil.Berhasil)
        return Results.Json(new { pesan = hasil.Pesan }, statusCode: hasil.Status);

    return Results.Ok(new
    {
        orderId = hasil.OrderId,
        kode = hasil.KodeAktivasi,
        paket = hasil.Paket,
        nominal = hasil.Nominal,
        hari = hasil.Hari,
        berlakuSampai = hasil.BerlakuSampai?.ToString("O"),
        // Keterangan lampiran supaya halaman admin bisa menyebutnya di pesan
        // sukses ("bukti tersimpan") tanpa menebak-nebak dari input lokalnya.
        adaBukti = hasil.AdaBukti,
        tipeBukti = hasil.TipeBukti,
        ukuranBukti = hasil.UkuranBukti,
        pesan = hasil.Pesan
    });
})
.RequireRateLimiting("admin");

// GET /admin/pengumuman — SEMUA pengumuman (termasuk nonaktif) untuk halaman
// admin. Terpisah dari publik GET /pengumuman supaya operator melihat apa
// adanya isi tabel, termasuk yang belum mulai/sudah lewat.
app.MapGet("/admin/pengumuman", async (HttpRequest req) =>
{

    try
    {
        var semua = await pengumumanStore.DaftarPengumumanSemuaAsync();
        return Results.Ok(new
        {
            jumlah = semua.Count,
            pengumuman = semua.Select(p => new
            {
                id = p.Id,
                judul = p.Judul,
                isi = p.Isi,
                jenis = p.Jenis,
                aktif = p.Aktif,
                mulai = p.Mulai?.ToString("O"),
                sampai = p.Sampai?.ToString("O"),
                dibuat = p.Dibuat.ToString("O")
            })
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new { pesan = "Gagal membaca pengumuman: " + ex.Message },
            statusCode: StatusCodes.Status500InternalServerError);
    }
})
.RequireRateLimiting("admin");

// POST /admin/pengumuman — terbitkan pengumuman broadcast (formulir halaman
// admin). Validasinya di LayananPengumuman.ValidasiBuat (diuji unit); kalau
// jenisnya tidak dikenal dirapikan ke "info" di sini, bukan ditolak.
app.MapPost("/admin/pengumuman", async (HttpRequest req, BuatPengumumanRequest body) =>
{

    var jenis = LayananPengumuman.RapikanJenis(body.Jenis);
    if (LayananPengumuman.ValidasiBuat(body.Judul, body.Isi, jenis, body.Mulai, body.Sampai) is { } alasan)
        return Results.BadRequest(new { pesan = alasan });

    try
    {
        var id = await pengumumanStore.BuatPengumumanAsync(
            body.Judul!.Trim(), (body.Isi ?? string.Empty).Trim(), jenis, body.Mulai, body.Sampai);
        return Results.Ok(new
        {
            id,
            pesan = "Pengumuman terbit — seluruh aplikasi desa akan menampilkannya dalam ±60 detik."
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new { pesan = "Gagal menulis pengumuman: " + ex.Message },
            statusCode: StatusCodes.Status500InternalServerError);
    }
})
.RequireRateLimiting("admin");

// DELETE /admin/pengumuman/{id} — hapus permanen satu pengumuman. Alternatif
// menonaktifkan (.SQL UPDATE aktif=false) tetap bisa lewat SQL Editor.
app.MapDelete("/admin/pengumuman/{id}", async (string id, HttpRequest req) =>
{

    try
    {
        var terhapus = await pengumumanStore.HapusPengumumanAsync(id);
        return terhapus > 0
            ? Results.Ok(new { dihapus = true, pesan = "Pengumuman dihapus." })
            : Results.NotFound(new { pesan = "Pengumuman tidak dikenal." });
    }
    catch (Exception ex)
    {
        return Results.Json(new { pesan = "Gagal menghapus pengumuman: " + ex.Message },
            statusCode: StatusCodes.Status500InternalServerError);
    }
})
.RequireRateLimiting("admin");

// =====================================================================
// PUSAT ADMINISTER — kelola perangkat per desa
// =====================================================================
//
// Inilah jalan pemulihan saat kuota perangkat habis: penolakan di /aktivasi
// berbunyi "Hapus perangkat lain di pusat administer", dan berikut adalah
// caranya. Semua rutenya dilindungi gerbang autentikasi admin global di atas.
//
// Alur operator yang lazim:
//   1. GET  /admin/pelanggan                         → desa mana yang penuh
//   2. GET  /admin/pelanggan/{kode}/perangkat        → perangkat mana saja
//   3. DELETE /admin/pelanggan/{kode}/perangkat/{id} → hapus yang sudah tak
//      dipakai (atau DELETE tanpa {id} untuk reset) lalu pelanggan bisa
//      mengaktivasi komputer barunya
//
// Tools/Admin-Lisensi.ps1 membungkus alur ini agar tidak perlu menulis curl.

// GET /admin/pelanggan — daftar semua desa + ringkasan.
app.MapGet("/admin/pelanggan", async (HttpRequest req, LayananAdmin admin) =>
{

    var daftar = await admin.DaftarPelangganAsync();
    return Results.Ok(new
    {
        jumlah = daftar.Count,
        pelanggan = daftar.Select(d => new
        {
            id = d.Id,
            kodeAktivasi = d.KodeAktivasi,
            namaDesa = d.NamaDesa,
            maxPerangkat = d.MaxPerangkat,
            jumlahPerangkat = d.JumlahPerangkat,
            paket = d.Paket,
            berlakuSampai = d.BerlakuSampai?.ToString("O"),
            // Dibandingkan di server supaya klien tidak perlu tahu aturannya.
            penuh = d.JumlahPerangkat >= d.MaxPerangkat
        })
    });
})
.RequireRateLimiting("admin");

// GET /admin/pelanggan/{kode}/perangkat — perangkat terdaftar satu desa.
app.MapGet("/admin/pelanggan/{kode}/perangkat", async (string kode, HttpRequest req, LayananAdmin admin) =>
{

    var perangkat = await admin.DaftarPerangkatAsync(kode);
    if (perangkat is null)
        return Results.NotFound(new { pesan = $"Kode aktivasi '{kode}' tidak dikenal." });

    return Results.Ok(new
    {
        kode,
        jumlah = perangkat.Count,
        perangkat = perangkat.Select(p => new
        {
            id = p.IdPerangkat,
            terakhirLihat = p.TerakhirLihat.ToString("O")
        })
    });
})
.RequireRateLimiting("admin");

// GET /admin/pelanggan/{kode}/pembayaran — riwayat pembayaran (dana masuk) satu desa.
// Bukan operasi uang: hanya membaca jejak audit `pembayaran` desa itu (dibatasi
// RLS setelah desa ditentukan dari kodenya), plus paket dari tagihan terkait.
app.MapGet("/admin/pelanggan/{kode}/pembayaran", async (string kode, HttpRequest req, LayananAdmin admin) =>
{

    var daftar = await admin.DaftarPembayaranAsync(kode);
    if (daftar is null)
        return Results.NotFound(new { pesan = $"Kode aktivasi '{kode}' tidak dikenal." });

    return Results.Ok(new
    {
        kode,
        jumlah = daftar.Count,
        total = daftar.Sum(p => p.Nominal),
        pembayaran = daftar.Select(p => new
        {
            orderId = p.OrderId,
            nominal = p.Nominal,
            status = p.Status,
            dicatat = p.Dicatat.ToString("O"),
            paket = p.Paket,
            // Referensi bukti bayar dari pembukuan manual; null untuk gateway.
            catatan = p.Catatan,
            // Lampiran bukti transfer: hanya keterangan jenis & ukurannya di
            // daftar ini. Gambarnya diambil satu per satu lewat endpoint
            // .../bukti supaya membuka riwayat tidak mengunduh seluruh gambar.
            adaBukti = p.AdaBukti,
            tipeBukti = p.TipeBukti,
            ukuranBukti = p.UkuranBukti,
            buktiTeks = BuktiTransfer.Ringkas(p.AdaBukti, p.TipeBukti, p.UkuranBukti)
        })
    });
})
.RequireRateLimiting("admin");

// GET /admin/pelanggan/{kode}/pembayaran/{orderId}/bukti — lampiran gambar bukti
// transfer hasil pembukuan manual.
//
// Bukan operasi uang: hanya menyajikan kembali gambar yang sudah tercatat pada
// baris pembayaran desa itu. Ditutup token admin seperti kuitansi — gambar
// transfer berisi nama pengirim, nomor rekening, dan nominal, jadi ia tidak
// boleh terbaca dari luar. Disajikan inline dengan jenis hasil pembacaan isi
// berkas (bukan kata pengirim) dan nosniff supaya peramban tidak pernah
// menebak-nebak jenisnya sendiri.
app.MapGet("/admin/pelanggan/{kode}/pembayaran/{orderId}/bukti",
    async (string kode, string orderId, HttpRequest req, HttpResponse res, LayananAdmin admin) =>
{

    var bukti = await admin.AmbilBuktiAsync(kode, orderId);
    if (bukti is null)
        return Results.NotFound(new { pesan = "Pembayaran ini tidak punya lampiran bukti transfer." });

    // nosniff: peramban tidak boleh menebak jenis sendiri — kalau isi berkas
    // keliru terbaca sebagai HTML, halaman bertoken ini jadi jalan XSS.
    // no-store: bukti transfer berisi nama pengirim dan nominal, jadi ia tidak
    // boleh tinggal di cache disk peramban bersama.
    res.Headers["X-Content-Type-Options"] = "nosniff";
    res.Headers["Cache-Control"] = "private, no-store";

    // Tanpa fileDownloadName: gambar disajikan INLINE supaya bisa langsung
    // dilihat operator di tab baru (yang menyimpannya tetap bisa "Simpan gambar
    // sebagai…" dari peramban). Jenisnya hasil pembacaan isi berkas, bukan
    // kepercayaan pada pengirim.
    return Results.File(bukti.Isi, contentType: bukti.Tipe);
})
.RequireRateLimiting("admin");

// GET /admin/pelanggan/{kode}/pembayaran/{orderId}/kuitansi — bukti bayar siap cetak.
//
// Bukan operasi uang: hanya merender ulang data yang SUDAH tercatat di tabel
// pembayaran. Ditutup token admin karena memuat nama desa dan kode aktivasi;
// yang mencetak adalah operator, lalu mengirimkannya ke desa untuk
// pertanggungjawaban anggaran. HTML (bukan PDF) dipilih supaya tidak menambah
// dependensi perender PDF di server dan desa bisa langsung "Cetak → Simpan PDF".
app.MapGet("/admin/pelanggan/{kode}/pembayaran/{orderId}/kuitansi",
    async (string kode, string orderId, HttpRequest req, LayananAdmin admin) =>
{

    var desa = await admin.CariDesaAsync(kode);
    if (desa is null)
        return Results.NotFound(new { pesan = $"Kode aktivasi '{kode}' tidak dikenal." });

    var daftar = await admin.DaftarPembayaranAsync(kode);
    var bayar = daftar?.FirstOrDefault(p =>
        string.Equals(p.OrderId, orderId, StringComparison.Ordinal));
    if (bayar is null)
        return Results.NotFound(new { pesan = $"Pembayaran '{orderId}' tidak ada untuk desa ini." });

    var kuitansi = KuitansiPembayaran.Html(new DataKuitansi(
        bayar.OrderId,
        desa.NamaDesa,
        desa.KodeAktivasi,
        bayar.Paket,
        bayar.Nominal,
        bayar.Dicatat,
        desa.Langganan?.BerlakuSampai,
        bayar.Catatan,
        BuktiTransfer.Ringkas(bayar.AdaBukti, bayar.TipeBukti, bayar.UkuranBukti)));

    return Results.Content(kuitansi, "text/html; charset=utf-8");
})
.RequireRateLimiting("admin");

// GET /admin/ekspor/desa.csv — seluruh daftar desa sebagai berkas CSV.
// Jalur ini sengaja pakai awalan /ekspor/, BUKAN /admin/pelanggan/{kode}:
// "ekspor" di bawah /pelanggan/ akan bertabrakan dengan pola {kode} dan desa
// yang kode aktivasinya "ekspor" jadi tak bisa dijangkau.
// Bentuk kolomnya sama persis dengan unduhan dari halaman /admin (CsvLaporan),
// supaya laporan CLI dan laporan peramban tidak pernah berbeda.
app.MapGet("/admin/ekspor/desa.csv", async (HttpRequest req, LayananAdmin admin) =>
{

    var daftar = await admin.DaftarPelangganAsync();
    var isi = CsvLaporan.DaftarDesa(daftar, DateTime.UtcNow);
    var nama = $"daftar-desa-{DateTime.UtcNow:yyyy-MM-dd}.csv";

    return Results.File(CsvLaporan.JadiBerkasUtf8(isi), "text/csv; charset=utf-8", nama);
})
.RequireRateLimiting("admin");

// GET /admin/ekspor/pembayaran/{kode}.csv — riwayat pembayaran satu desa.
app.MapGet("/admin/ekspor/pembayaran/{kode}.csv", async (string kode, HttpRequest req, LayananAdmin admin) =>
{

    var daftar = await admin.DaftarPembayaranAsync(kode);
    if (daftar is null)
        return Results.NotFound(new { pesan = $"Kode aktivasi '{kode}' tidak dikenal." });

    var isi = CsvLaporan.Pembayaran(daftar);
    var nama = $"pembayaran-{kode.Trim()}.csv";

    return Results.File(CsvLaporan.JadiBerkasUtf8(isi), "text/csv; charset=utf-8", nama);
})
.RequireRateLimiting("admin");

// GET /admin/pelanggan/{kode} — data satu desa (bahan formulir "ubah data desa").
app.MapGet("/admin/pelanggan/{kode}", async (string kode, HttpRequest req, LayananAdmin admin) =>
{

    var desa = await admin.CariDesaAsync(kode);
    if (desa is null)
        return Results.NotFound(new { pesan = $"Kode aktivasi '{kode}' tidak dikenal." });

    return Results.Ok(new
    {
        kodeAktivasi = desa.KodeAktivasi,
        namaDesa = desa.NamaDesa,
        kontak = desa.Kontak,
        maxPerangkat = desa.MaxPerangkat
    });
})
.RequireRateLimiting("admin");

// PUT /admin/pelanggan/{kode} — ubah nama & kontak desa. Kode aktivasi TIDAK
// ikut berubah: kode itulah yang diketik di aplikasi desa, menggantinya berarti
// setiap PC desa itu harus mengaktivasi ulang.
app.MapPut("/admin/pelanggan/{kode}", async (string kode, HttpRequest req, UbahDesaRequest body, LayananAdmin admin) =>
{

    try
    {
        if (!await admin.UbahDesaAsync(kode, body.NamaDesa, body.Kontak ?? ""))
            return Results.NotFound(new { pesan = $"Kode aktivasi '{kode}' tidak dikenal." });

        return Results.Ok(new { kode, pesan = "Data desa diperbarui." });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { pesan = ex.Message });
    }
})
.RequireRateLimiting("admin");

// DELETE /admin/pelanggan/{kode}?konfirmasi=... — hapus desa (beserta langganan,
// perangkat, tagihan, pembayaran). Ditolak 409 bila masih ada tagihan atau
// pembayaran — jejak uang tidak boleh hilang karena satu klik.
//
// Bagian "paksa" butuh ?konfirmasi=<kode aktivasi yang sama>. Nilainya harus
// sama persis; "true"/"ya"/kosong TIDAK dihitung. Ini kerenggan yang menggantikan
// kotak dialog: satu klik tidak bisa menghapus jejak uang, tapi operator yang
// memang berniat tetap punya jalan — dan jejaknya tertulis di log.
// Data desa (surat/warga) ada di aplikasi lokal, bukan di sini.
app.MapDelete("/admin/pelanggan/{kode}", async (string kode, HttpRequest req, LayananAdmin admin) =>
{

    var bersih = kode.Trim();
    var konfirmasi = req.Query["konfirmasi"].ToString().Trim();

    if (konfirmasi.Length > 0 && !string.Equals(konfirmasi, bersih, StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new
        {
            pesan = $"Konfirmasi harus sama dengan kode aktivasi ('{bersih}')."
        });

    var rincian = await admin.HapusDesaAsync(bersih, paksa: konfirmasi.Length > 0);

    return rincian.Status switch
    {
        HasilHapusDesa.TidakDikenal =>
            Results.NotFound(new { pesan = $"Kode aktivasi '{bersih}' tidak dikenal." }),
        HasilHapusDesa.AdaRiwayat => Results.Json(new
        {
            pesan = $"Desa ini masih punya {rincian.Tagihan} tagihan dan {rincian.Pembayaran} " +
                    "pembayaran; penghapusan ditolak.",
            tagihan = rincian.Tagihan,
            pembayaran = rincian.Pembayaran
        }, statusCode: StatusCodes.Status409Conflict),
        _ when rincian.Pembayaran > 0 => Results.Ok(new
        {
            dihapus = true,
            pesan = $"Desa '{bersih}' dihapus BESERTA {rincian.Pembayaran} baris pembayaran."
        }),
        _ => Results.Ok(new
        {
            dihapus = true,
            tagihan = rincian.Tagihan,
            pembayaran = rincian.Pembayaran,
            pesan = rincian.Tagihan > 0
                ? $"Desa '{bersih}' dihapus beserta {rincian.Tagihan} tagihan yang belum pernah dibayar."
                : $"Desa '{bersih}' dihapus."
        })
    };
})
.RequireRateLimiting("admin");

// DELETE /admin/pelanggan/{kode}/perangkat/{perangkat} — hapus satu perangkat.
app.MapDelete("/admin/pelanggan/{kode}/perangkat/{perangkat}",
    async (string kode, string perangkat, HttpRequest req, LayananAdmin admin) =>
{

    var dihapus = await admin.HapusPerangkatAsync(kode, perangkat);
    if (dihapus is null)
        return Results.NotFound(new { pesan = $"Kode aktivasi '{kode}' tidak dikenal." });

    return Results.Ok(new
    {
        dihapus,
        pesan = dihapus == 0 ? "Perangkat tidak ditemukan." : "Perangkat dihapus."
    });
})
.RequireRateLimiting("admin");

// DELETE /admin/pelanggan/{kode}/perangkat — reset SEMUA perangkat desa.
// Dipakai saat pelanggan mengganti komputer dan tidak tahu id perangkat mana
// yang tersisa; bagaimanapun juga perangkat akan tercatat ulang saat aktivasi.
app.MapDelete("/admin/pelanggan/{kode}/perangkat",
    async (string kode, HttpRequest req, LayananAdmin admin) =>
{

    var dihapus = await admin.HapusPerangkatAsync(kode, null);
    if (dihapus is null)
        return Results.NotFound(new { pesan = $"Kode aktivasi '{kode}' tidak dikenal." });

    return Results.Ok(new { dihapus, pesan = "Semua perangkat desa direset." });
})
.RequireRateLimiting("admin");

// POST /admin/pelanggan/{kode}/batas — ubah batas jumlah perangkat ({ "batas": 3 }).
app.MapPost("/admin/pelanggan/{kode}/batas",
    async (string kode, HttpRequest req, UbahBatasRequest body, LayananAdmin admin) =>
{

    if (body.Batas is < 1 or > 100)
        return Results.BadRequest(new { pesan = "Batas perangkat harus antara 1 dan 100." });

    if (!await admin.UbahBatasAsync(kode, body.Batas))
        return Results.NotFound(new { pesan = $"Kode aktivasi '{kode}' tidak dikenal." });

    return Results.Ok(new { kode, maxPerangkat = body.Batas });
})
.RequireRateLimiting("admin");

// =====================================================================
// UJI PEMBAYARAN — transaksi Snap sungguhan + bukti notifikasi nyata
// =====================================================================
//
// Alur lengkapnya ada di halaman /checkout-uji: buat transaksi Snap NYATA
// (production kalau servernya production), bayar di halaman Midtrans, lalu
// lihat notifikasi aslinya sampai server membalas dan akses desanya bertambah.
//
// Endpoint di bawah hanya adaptasi HTTP → hasil. Aturannya (harga dari
// katalog server, tagihan dicatat sebelum gateway dipanggil, perpanjangan
// idempoten lewat PerpanjangAsync yang sama dengan webhook) ada di
// LayananUjiPembayaran, yang memakai layanan yang SAMA dengan jalur
// pembayaran pelanggan — jadi tidak ada salinan aturan uang di jalur uji.
//
// Semuanya di bawah /admin/*, jadi hanya terbuka untuk operator dengan izin
// payments.test: endpoint POST ini MEMBUAT transaksi sungguhan di akun
// merchant, dan membukanya untuk semua operator akan consumes kuota API.

// GET /admin/uji-pembayaran — keadaan jalur uji (lingkungan gateway, apakah
// notifikasi bisa diarahkan ke server ini) supaya halaman uji bisa
// menampilkan peringatan yang tepat sebelum operator menekan tombol.
app.MapGet("/admin/uji-pembayaran", async (HttpRequest req) =>
{
    var layanan = req.HttpContext.RequestServices.GetRequiredService<LayananUjiPembayaran>();
    var midtrans = req.HttpContext.RequestServices.GetRequiredService<MidtransService>();

    return Results.Ok(new
    {
        lingkungan = layanan.Lingkungan,
        urlNotifikasi = layanan.UrlNotifikasi ?? "(URL dashboard akun Midtrans)",
        notifikasiDialihkan = layanan.UrlNotifikasi is not null,
        gatewayAktif = midtrans.Aktif
    });
})
.RequireRateLimiting("admin");

// POST /admin/uji-pembayaran — buat transaksi uji untuk satu desa uji.
app.MapPost("/admin/uji-pembayaran",
    async (HttpRequest req, PermintaanTagihanUji body, LayananUjiPembayaran layanan) =>
{
    var hasil = await layanan.BuatTagihanAsync(body, req.HttpContext.RequestAborted);
    if (hasil.Status != 200)
        return Results.Json(new { pesan = hasil.Pesan }, statusCode: hasil.Status);

    return Results.Ok(new
    {
        orderId = hasil.OrderId,
        kodeAktivasi = hasil.KodeAktivasi,
        namaDesa = hasil.NamaDesa,
        paket = hasil.PaketKode,
        paketNama = hasil.PaketNama,
        nominal = hasil.Nominal,
        hari = hasil.Hari,
        token = hasil.Token,
        linkPembayaran = hasil.LinkPembayaran,
        lingkungan = hasil.Lingkungan,
        urlNotifikasi = hasil.UrlNotifikasi,
        notifikasiDialihkan = hasil.NotifikasiDialihkan,
        desaBaru = hasil.DesaBaru,
        pesan = hasil.Pesan
    });
})
.RequireRateLimiting("tagihan");

// GET /admin/uji-pembayaran/{orderId} — keadaan satu transaksi uji: apa yang
// said gateway, notifikasi apa saja yang masuk, dan akibatnya pada langganan
// desanya.
app.MapGet("/admin/uji-pembayaran/{orderId}", async (string orderId, HttpRequest req) =>
{
    var layanan = req.HttpContext.RequestServices.GetRequiredService<LayananUjiPembayaran>();
    var status = await layanan.AmbilStatusAsync(orderId, req.HttpContext.RequestAborted);
    if (status is null)
        return Results.NotFound(new { pesan = $"Order '{orderId}' tidak dikenal server." });

    return Results.Ok(new
    {
        orderId = status.OrderId,
        kodeAktivasi = status.KodeAktivasi,
        namaDesa = status.NamaDesa,
        paket = status.PaketKode,
        paketNama = status.PaketNama,
        nominal = status.Nominal,
        hari = status.Hari,
        berlakuSampai = status.BerlakuSampai?.ToString("O"),
        lingkungan = status.Lingkungan,
        urlNotifikasi = status.UrlNotifikasi,
        gateway = new
        {
            ditemukan = status.GatewayDitemukan,
            transactionStatus = status.TransactionStatus,
            grossAmount = status.GrossGateway,
            fraudStatus = status.FraudGateway,
            catatan = status.CatatanGateway
        },
        webhookMasuk = status.WebhookMasuk,
        webhookDiterima = status.WebhookDiterima,
        notifikasi = status.Notifikasi.Select(n => new
        {
            waktu = n.Waktu.ToString("O"),
            orderId = n.OrderId,
            transactionStatus = n.TransactionStatus,
            grossAmount = n.GrossAmount,
            fraudStatus = n.FraudStatus,
            paymentType = n.PaymentType,
            signatureValid = n.SignatureValid,
            statusBalasan = n.StatusBalasan,
            diterima = n.Diterima,
            duplikat = n.Duplikat,
            hariDitambah = n.HariDitambah,
            pesan = n.Pesan,
            alamatIp = n.AlamatIp,
            userAgent = n.UserAgent
        }),
        pembayaran = status.Pembayaran.Select(p => new
        {
            orderId = p.OrderId,
            nominal = p.Nominal,
            status = p.Status,
            dicatat = p.Dicatat.ToString("O"),
            paket = p.Paket
        })
    });
})
.RequireRateLimiting("admin");

// GET /admin/uji-pembayaran/notifikasi — notifikasi webhook terakhir yang
// masuk (memori server), untuk semua order. Dipakai halaman uji saat
// operator sedang menunggu notifikasi tanpa order id di tangan.
app.MapGet("/admin/uji-pembayaran/notifikasi", async (HttpRequest req) =>
{
    var pelacak = req.HttpContext.RequestServices.GetRequiredService<PelacakWebhook>();
    var semua = pelacak.Semua();

    return Results.Ok(new
    {
        jumlah = semua.Count,
        notifikasi = semua.Select(n => new
        {
            waktu = n.Waktu.ToString("O"),
            orderId = n.OrderId,
            transactionStatus = n.TransactionStatus,
            grossAmount = n.GrossAmount,
            fraudStatus = n.FraudStatus,
            paymentType = n.PaymentType,
            signatureValid = n.SignatureValid,
            statusBalasan = n.StatusBalasan,
            diterima = n.Diterima,
            duplikat = n.Duplikat,
            hariDitambah = n.HariDitambah,
            pesan = n.Pesan,
            alamatIp = n.AlamatIp,
            userAgent = n.UserAgent
        })
    });
})
.RequireRateLimiting("admin");

app.Run();
