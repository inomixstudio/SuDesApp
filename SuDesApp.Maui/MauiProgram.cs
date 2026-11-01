using Microsoft.Extensions.Logging;
using SuDesApp.Mobile;

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

        // Satu penyimpanan untuk profil desa dan arsip surat: berkas JSON di
        // folder data aplikasi, jadi tidak perlu menyalin basis data desktop.
        builder.Services.AddSingleton(_ =>
            new LayananSuratPonsel(new PenyimpananSurat(FileSystem.AppDataDirectory)));

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
