namespace SuDesApp.Mobile;

/// <summary>
/// Sambungan ponsel ke aplikasi desktop: alamat, kunci API, dan token sesi
/// hasil login akun desktop.
///
/// Disimpan di folder data aplikasi (bukan di kode) karena kunci API bisa
/// diganti operator kapan saja. Sambungan dianggap aktif hanya bila tokennya
/// belum kedaluwarsa; sesi desktop berumur 12 jam dan ikut hilang saat aplikasi
/// desktop ditutup.
/// </summary>
public sealed class SambunganDesktop
{
    public string Alamat { get; set; } = string.Empty;

    public string KunciApi { get; set; } = string.Empty;

    public string Token { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Nama { get; set; } = string.Empty;

    public string Peran { get; set; } = string.Empty;

    /// <summary>Kapan sesi ini dimulai di ponsel.</summary>
    public DateTime WaktuMasuk { get; set; }

    /// <summary>Waktu token berakhir menurut desktop.</summary>
    public DateTime Berakhir { get; set; }

    /// <summary>Kapan pengaturan surat terakhir ditarik dari desktop.</summary>
    public DateTime PengaturanDiambil { get; set; }

    /// <summary>True bila token masih berlaku (dengan jeda aman satu menit).</summary>
    public bool Aktif =>
        !string.IsNullOrWhiteSpace(Alamat)
        && !string.IsNullOrWhiteSpace(Token)
        && Berakhir > DateTime.Now.AddMinutes(1);

    /// <summary>Label siap tampil, mis. "Budi (Administrator)".</summary>
    public string Label =>
        string.IsNullOrWhiteSpace(Nama)
            ? Username
            : string.IsNullOrWhiteSpace(Peran) ? Nama : $"{Nama} ({Peran})";
}
