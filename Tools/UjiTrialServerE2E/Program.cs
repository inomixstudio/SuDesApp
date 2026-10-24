using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Services;

// ============================================================================
// Uji E2E trial SERVER — alat permanen di Tools/, dijalankan ulang sebelum rilis.
//
//   Tools/uji-e2e-trial-server.sh
//
// Diuji terhadap server lisensi lokal (mode Development/memori) lewat HTTP
// sungguhan + kode klien yang SAMA dengan aplikasi:
//   1. desa didaftarkan lewat endpoint admin,
//   2. klien meminta trial → server yang memberi (bukan trial lokal),
//   3. token bertanda tangan diterima dan diverifikasi klien (kunci publik
//      diambil dari /kunci-publik, bukan ditanam),
//   4. keadaan & chip status setelah trial aktif (chip berbunyi Trial),
//   5. permintaan trial kedua dari "komputer lain" ditolak server TANPA
//      cadangan trial lokal.
//
// Folder cache dibuat di %TEMP% lewat penunjuk internal Core supaya cache
// lisensi milik pengguna yang menjalankan uji tidak pernah tersentuh.
// ============================================================================

string baseUrl = Environment.GetEnvironmentVariable("UJI_BASE_URL") ?? "http://127.0.0.1:5199";
string kode = Environment.GetEnvironmentVariable("UJI_KODE") ?? "UJI-TRIAL-E2E";
string opId = Environment.GetEnvironmentVariable("UJI_OP_ID") ?? "uji01";
string opToken = Environment.GetEnvironmentVariable("UJI_OP_TOKEN")
    ?? "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

string folder = Path.Combine(Path.GetTempPath(), "sudesapp-e2e-trial-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
SetInternalStatic(typeof(TrialLokal), "FolderOverride", folder);
SetInternalStatic(typeof(LisensiManager), "FolderCacheOverride", folder);

int gagal = 0;
void Cek(string nama, bool lulus, string detail = "")
{
    Console.WriteLine($"  {(lulus ? "LULUS" : "GAGAL")}  {nama}{(detail.Length == 0 ? "" : " — " + detail)}");
    if (!lulus) gagal++;
}

Console.WriteLine($"== 0) PRAKONDISI ==\n  server {baseUrl}\n  kode desa {kode}\n  folder uji {folder}");

using var admin = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(15) };
admin.DefaultRequestHeaders.Add("X-Admin-Operator", opId);
admin.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", opToken);

var health = await admin.GetAsync("/healthz");
Cek("GET /healthz → 200", health.StatusCode == HttpStatusCode.OK, $"HTTP {(int)health.StatusCode}");

// --- 1) Daftarkan desa uji -------------------------------------------------
var daftar = await admin.PostAsJsonAsync("/admin/pelanggan", new
{
    kodeAktivasi = kode,
    namaDesa = "Desa Uji Trial E2E",
    kontak = "uji@example.test",
    maxPerangkat = 2
});
string isiDaftar = await daftar.Content.ReadAsStringAsync();
Cek("POST /admin/pelanggan → 200", daftar.StatusCode == HttpStatusCode.OK,
    $"HTTP {(int)daftar.StatusCode} {Ringkas(isiDaftar)}");

// --- 2) Klien meminta trial (jalur aplikasi yang sebenarnya) ---------------
var klien1 = BuatKlien();
var penjaga1 = klien1.Penjaga;
var hasil1 = await penjaga1.MulaiPercobaanAsync();

Cek("MulaiPercobaanAsync → trial SERVER (bukan lokal)",
    hasil1.Asal == AsalPercobaan.Server, $"asal={hasil1.Asal}");
Cek("trial lokal TIDAK dibuat saat server menjawab",
    !File.Exists(Path.Combine(folder, "trial-lokal.json")));

// --- 3) Token: ada, bertanda tangan, klaim trial --------------------------
string? token = BacaTokenCache(folder);
Cek("token tersimpan di cache lisensi", token is not null,
    token is null ? "cache kosong" : $"{token.Length} karakter");
Cek("kunci publik diambil dari /kunci-publik (bukan bawaan)",
    !string.IsNullOrWhiteSpace(klien1.Lisensi.KunciPublik),
    $"panjang {klien1.Lisensi.KunciPublik?.Length ?? 0}");

if (token is not null)
{
    var bagian = token.Split('.');
    Cek("token berbentuk v1.payload.signature", bagian.Length == 3 && bagian[0] == "v1",
        $"{bagian.Length} bagian, versi '{bagian[0]}'");

    if (bagian.Length == 3)
    {
        string klaim = Encoding.UTF8.GetString(Convert.FromBase64String(B64Url(bagian[1])));
        Console.WriteLine($"  klaim token: {klaim}");

        using var doc = JsonDocument.Parse(klaim);
        var akar = doc.RootElement;
        Cek("klaim paket = trial", Ambil(akar, "paket") == "trial", Ambil(akar, "paket") ?? "-");
        Cek("klaim kode = kode desa uji", Ambil(akar, "kode") == kode, Ambil(akar, "kode") ?? "-");
    }
}

// --- 4) Keadaan & chip setelah trial aktif --------------------------------
var status1 = penjaga1.Status;
Cek("status lisensi aktif", status1?.Aktif == true);
Cek("keadaan gerbang = Berlangganan (token sah menang atas trial lokal)",
    penjaga1.Keadaan == KeadaanLisensi.Berlangganan, penjaga1.Keadaan.ToString());
Cek("menu vital terbuka", !penjaga1.Terkunci);
Cek("paket terbaca trial", string.Equals(status1?.Paket, "trial", StringComparison.OrdinalIgnoreCase),
    status1?.Paket ?? "-");

if (status1 is not null)
{
    int sisa = status1.SisaHariTenggang;
    // Sama persis dengan MainWindowViewModel.TerapkanChipLisensi: chip memakai
    // keadaan TAMPIL, sehingga token trial server berbunyi Trial.
    var keadaanTampil = PenjagaLisensi.KeadaanTampil(penjaga1.Keadaan, status1.Paket);
    string chip = PenjagaLisensi.LabelChip(keadaanTampil, sisa);
    string tooltip = $"Keadaan tampil: {PenjagaLisensi.LabelKeadaan(keadaanTampil)}, nada {PenjagaLisensi.NadaKeadaan(keadaanTampil)}";
    Console.WriteLine($"  CHIP STATUS: \"{chip}\"  ({tooltip})");
    Console.WriteLine($"  sah sampai: {(status1.SahSampai ?? status1.BerlakuSampai):O}  (sisa {sisa} hari)");
    Cek("chip menyebut sisa hari trial", chip.Contains($"sisa {sisa} hari"), chip);
    Cek("chip berbunyi Trial (bukan Full Service)",
        chip.StartsWith("Trial", StringComparison.Ordinal), chip);
    Cek("masa trial ±14 hari", sisa is >= 13 and <= 14, $"{sisa} hari");
}

// --- 5) "Komputer lain": trial kedua harus DITOLAK, tanpa cadangan lokal --
string folder2 = Path.Combine(folder, "komputer-lain");
Directory.CreateDirectory(folder2);
SetInternalStatic(typeof(TrialLokal), "FolderOverride", folder2);
SetInternalStatic(typeof(LisensiManager), "FolderCacheOverride", folder2);

var klien2 = BuatKlien();
var hasil2 = await klien2.Penjaga.MulaiPercobaanAsync();

Cek("trial kedua dari perangkat lain → DitolakServer",
    hasil2.Asal == AsalPercobaan.DitolakServer, $"asal={hasil2.Asal}");
Cek("tidak ada trial lokal sebagai cadangan saat server menolak",
    !File.Exists(Path.Combine(folder2, "trial-lokal.json")));
Cek("gerbang tetap terkunci pada mesin kedua", klien2.Penjaga.Terkunci);

Console.WriteLine(gagal == 0 ? "\nSEMUA LULUS" : $"\n{gagal} GAGAL");
return gagal == 0 ? 0 : 1;

// ---------------------------------------------------------------- pembantu
static (PenjagaLisensi Penjaga, LisensiManager Lisensi) BuatKlien()
{
    // HttpClient tanpa BaseAddress: alamat selalu dari LisensiManager.UrlServer,
    // sama seperti aplikasi (satu sumber alamat endpoint).
    var lisensi = new LisensiManager(NullLogger<LisensiManager>.Instance, new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(15)
    })
    {
        UrlServer = Environment.GetEnvironmentVariable("UJI_BASE_URL") ?? "http://127.0.0.1:5199",
        KodeAktivasi = Environment.GetEnvironmentVariable("UJI_KODE") ?? "UJI-TRIAL-E2E"
    };

    var klien = new LanggananKlien(
        lisensi, new HttpClient { Timeout = TimeSpan.FromSeconds(15) },
        NullLogger<LanggananKlien>.Instance);
    var penjaga = new PenjagaLisensi(
        lisensi, new TrialLokal(), NullLogger<PenjagaLisensi>.Instance, klien);
    return (penjaga, lisensi);
}

static string? BacaTokenCache(string folder)
{
    string berkas = Path.Combine(folder, "lisensi-cache.json");
    if (!File.Exists(berkas)) return null;

    using var doc = JsonDocument.Parse(File.ReadAllText(berkas));
    foreach (var properti in doc.RootElement.EnumerateObject())
    {
        if (properti.Value.ValueKind == JsonValueKind.String
            && properti.Value.GetString() is { } nilai
            && nilai.StartsWith("v1.", StringComparison.Ordinal))
        {
            return nilai;
        }
    }
    return null;
}

static string? Ambil(JsonElement akar, string nama)
{
    foreach (var properti in akar.EnumerateObject())
    {
        if (string.Equals(properti.Name, nama, StringComparison.OrdinalIgnoreCase)
            && properti.Value.ValueKind == JsonValueKind.String)
        {
            return properti.Value.GetString();
        }
    }
    return null;
}

static string Ringkas(string isi) =>
    isi.Length <= 200 ? isi : isi[..200] + "…";

static string B64Url(string isi) =>
    isi.Replace('-', '+').Replace('_', '/') +
    new string('=', (4 - isi.Length % 4) % 4);

static void SetInternalStatic(Type tipe, string properti, object? nilai) =>
    tipe.GetProperty(properti, BindingFlags.NonPublic | BindingFlags.Static)!
        .SetValue(null, nilai);
