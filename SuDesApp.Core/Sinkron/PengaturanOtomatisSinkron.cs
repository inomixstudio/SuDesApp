namespace SuDesApp.Sinkron;

/// <summary>
/// Saklar sinkronisasi otomatis (impor antrean di latar).
/// Bawaan aktif; operator bisa mematikannya di halaman Sinkron Android.
/// </summary>
public static class PengaturanOtomatisSinkron
{
    public const string Kunci = "Supabase:SinkronOtomatis";

    public static bool Aktif => SuDesApp.Utilities.AppPreferenceStore.GetBool(Kunci, true);
}
