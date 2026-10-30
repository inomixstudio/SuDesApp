using SuDesApp.Web.Components;
using SuDesApp.Ui.Services;
using Microsoft.AspNetCore.Components.Authorization;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Sesi login: state + provider diambil dari SuDesApp.Ui sehingga MAUI memakai
// kode yang persis sama. SesiState singleton per circuit (layanan scoped di
// Blazor Server), token disimpan di localStorage dan divalidasi ulang ke
// /sudes/api/v1/sesi/siapa setelah refresh halaman.
// Scoped (bukan singleton): satu sesi per circuit. Dengan singleton, satu
// pengguna yang masuk akan ikut "mengaktifkan" sesi di circuit milik pengguna lain.
// Di MAUI layanan ini didaftarkan singleton karena aplikasinya satu pengguna.
builder.Services.AddScoped<SesiState>();
builder.Services.AddScoped<AuthenticationStateProvider, SesiAuthenticationStateProvider>();

// Klien backend SuDesApp.Api (dipanggil dari sisi server, jadi tidak terhambat CORS).
// Alamat bisa diubah lewat "Api": { "BaseUrl": "..." } di appsettings.
// Kunci API ("Api": "Kunci") dikirim ke endpoint eksternal yang berkey,
// sama seperti aturan di docs/api-desa.md.
var kunciApi = builder.Configuration["Api:Kunci"];
builder.Services.AddHttpClient<SuDesApiClient>(c =>
{
    c.BaseAddress = new Uri(builder.Configuration["Api:BaseUrl"] ?? SuDesApiClient.AlamatBawaan);
    if (!string.IsNullOrWhiteSpace(kunciApi))
        c.DefaultRequestHeaders.Add("X-Api-Key", kunciApi);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    // Endpoint Blazor selalu diperlakukan sebagai anonim di tingkat HTTP.
    // @attribute [Authorize] pada komponen ikut terbaca sebagai metadata endpoint,
    // sehingga halaman pertama memicu Challenge yang menuntut IAuthenticationService
    // (lihat dotnet/aspnetcore#55678) dan berujung 500. AllowAnonymous di sini
    // menonaktifkan tantangan itu; penjaga rute yang sebenarnya tetap
    // <AuthorizeRouteView> di Routes.razor, dan data tetap dijaga API lewat Bearer token.
    .AllowAnonymous()
    // Halaman dari Razor Class Library (SuDesApp.Ui) harus didaftarkan juga di
    // endpoint, bukan hanya di <Router>. Tanpa ini, / dan /modul/* balik 404.
    .AddAdditionalAssemblies(typeof(SuDesApp.Ui.Pages.Beranda).Assembly);

app.Run();
