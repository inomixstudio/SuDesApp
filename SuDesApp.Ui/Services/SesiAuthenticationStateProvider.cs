using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace SuDesApp.Ui.Services;

/// <summary>
/// Sumber AuthenticationState untuk ketiga platform: membangun ClaimsPrincipal
/// dari <see cref="SesiState"/> (bukan cookie — sesi aplikasi ini berupa token
/// Bearer ke SuDesApp.Api). Setiap perubahan sesi (masuk/keluar/pulih) langsung
/// diberitahukan ke komponen AuthorizeRouteView lewat NotifyAuthenticationStateChanged.
///
/// Provider berlangganan event SesiState dan berhenti saat di-Dispose — di
/// Blazor Server dispose dipanggil saat circuit selesai, jadi tidak ada
/// langganan yang menggantung antar circuit.
/// </summary>
public sealed class SesiAuthenticationStateProvider : AuthenticationStateProvider, IDisposable
{
    private readonly SesiState _sesi;

    public SesiAuthenticationStateProvider(SesiState sesi)
    {
        _sesi = sesi;
        _sesi.Berubah += BeriTahuPerubahan;
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (!_sesi.Adakah || _sesi.Pengguna is null)
            return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));

        var pengguna = _sesi.Pengguna;
        var klaim = new List<Claim>
        {
            new(ClaimTypes.Name, pengguna.Username),
            new(ClaimTypes.NameIdentifier, pengguna.Username),
            new(ClaimTypes.GivenName, pengguna.Nama),
            // Peran disimpan persis seperti nilai PeranPengguna (ADMINISTRATOR, …)
            // supaya nanti bisa dipakai [Authorize(Roles = "...")] tanpa konversi.
            new(ClaimTypes.Role, pengguna.Peran)
        };

        // "sesi" = skema autentikasi bukti token; tanpa nama skema, IsAuthenticated
        // tetap true selama identity punya nama.
        var identitas = new ClaimsIdentity(klaim, authenticationType: "sesi");
        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identitas)));
    }

    private void BeriTahuPerubahan() => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());

    public void Dispose() => _sesi.Berubah -= BeriTahuPerubahan;
}
