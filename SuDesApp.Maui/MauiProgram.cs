using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Components.Authorization;
using SuDesApp.Ui.Services;

namespace SuDesApp.Maui;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();

		// Sesi login — kode yang sama dengan browser (SuDesApp.Ui): state sesi,
		// provider AuthenticationState, dan klien HTTP ber-Bearer token.
		builder.Services.AddSingleton<SesiState>();
		builder.Services.AddScoped<AuthenticationStateProvider, SesiAuthenticationStateProvider>();

		// Klien backend SuDesApp.Api. Untuk Android ganti alamatnya:
		// emulator -> http://10.0.2.2:5188/, perangkat fisik -> http://IP-PC:5188/.
		builder.Services.AddSingleton(sp => new SuDesApiClient(
			new HttpClient
			{
				BaseAddress = new Uri(SuDesApiClient.AlamatBawaan)
			},
			sp.GetRequiredService<SesiState>()));

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
