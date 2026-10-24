using SuDesApp.Data.Models;

namespace SuDesApp.Wpf.Input;

/// <summary>
/// Maps resident records to and from the shared fields on a letter form.
/// Date presentation remains owned by the form because individual forms can
/// apply different date policies to additional fields.
/// </summary>
internal static class WargaFormMapper
{
    public static WargaFormValues ToFormValues(WargaData warga)
    {
        return new WargaFormValues(
            warga.Nama ?? string.Empty,
            warga.TempatLahir ?? string.Empty,
            warga.TanggalLahir,
            warga.Pekerjaan ?? string.Empty,
            warga.Pendidikan ?? string.Empty,
            warga.JenisKelamin ?? string.Empty,
            warga.Agama ?? string.Empty,
            warga.StatusPerkawinan ?? string.Empty,
            warga.Kewarganegaraan ?? string.Empty);
    }
}

internal sealed record WargaFormValues(string Nama, string TempatLahir, string? TanggalLahir,
    string Pekerjaan, string Pendidikan, string JenisKelamin, string Agama, string StatusPerkawinan,
    string Kewarganegaraan);
