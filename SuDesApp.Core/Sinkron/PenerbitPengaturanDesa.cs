using System.Text.Json;
using Microsoft.Extensions.Logging;
using Npgsql;
using SuDesApp.Configuration;
using SuDesApp.Data.Repositories;
using SuDesApp.Supabase;
using SuDesApp.Utilities;

namespace SuDesApp.Sinkron;

/// <summary>
/// Menerbitkan identitas kop surat dan pola penomoran desktop ke tabel
/// <c>pengaturan_desa</c> Supabase, supaya aplikasi Android memakai sumber
/// yang sama instead of mengetiknya ulang.
///
/// <para>
/// Android tidak pernah menulis tabel ini — desktop satu-satunya sumber.
/// Nomor urut terakhir ikut diterbitkan supaya draf di perangkat bisa
/// melanjutkan deret register, bukan mulai dari 001.
/// </para>
/// </summary>
public sealed class PenerbitPengaturanDesa
{
    private readonly SupabasePengaturan _pengaturan;
    private readonly IDesaRepository _desaRepository;
    private readonly ISuratRepository _suratRepository;
    private readonly AppConfig _appConfig;
    private readonly ILogger<PenerbitPengaturanDesa> _logger;

    /// <summary>Batas ukuran logo yang ikut diterbitkan (byte), ~650 KB.</summary>
    private const long BatasUkuranLogo = 900_000;

    public PenerbitPengaturanDesa(
        SupabasePengaturan pengaturan,
        IDesaRepository desaRepository,
        ISuratRepository suratRepository,
        AppConfig appConfig,
        ILogger<PenerbitPengaturanDesa> logger)
    {
        _pengaturan = pengaturan ?? throw new System.ArgumentNullException(nameof(pengaturan));
        _desaRepository = desaRepository ?? throw new System.ArgumentNullException(nameof(desaRepository));
        _suratRepository = suratRepository ?? throw new System.ArgumentNullException(nameof(suratRepository));
        _appConfig = appConfig ?? throw new System.ArgumentNullException(nameof(appConfig));
        _logger = logger ?? throw new System.ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Terbitkan kop + penomoran. Mengembalikan false bila konfigurasi Supabase
    /// belum lengkap atau pengiriman gagal (tidak pernah melempar).
    /// </summary>
    public async Task<bool> TerbitkanAsync(CancellationToken ct = default)
    {
        if (!_pengaturan.Lengkap) return false;

        try
        {
            var desa = await _desaRepository.GetInfoDesaAsync().ConfigureAwait(false);

            var kop = new Dictionary<string, object>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["namaDesa"] = desa?.NamaDesa ?? string.Empty,
                ["kecamatan"] = desa?.Kecamatan ?? string.Empty,
                ["kabupaten"] = desa?.Kabupaten ?? string.Empty,
                ["jenisKabupaten"] = KopLabel.Kabupaten(desa),
                ["jenisLembaga"] = KopLabel.Lembaga(desa),
                ["alamat"] = desa?.Alamat ?? string.Empty,
                ["kodepos"] = desa?.Kodepos ?? string.Empty,
                ["email"] = desa?.Email ?? string.Empty,
                ["kepalaDesa"] = desa?.KepalaDesa ?? string.Empty,
                ["sekretarisDesa"] = desa?.SekretarisDesa ?? string.Empty,
                ["nipKepalaDesa"] = desa?.NipKepalaDesa ?? string.Empty,
                ["ukuranKertas"] = PengaturanCetak.GetUkuranKertas() == UkuranKertasSurat.F4 ? "F4" : "A4",
            };

            await TambahkanLogoAsync(kop, ct).ConfigureAwait(false);

            var format = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var pasangan in _appConfig.SuratNumberFormats)
            {
                if (!string.IsNullOrWhiteSpace(pasangan.Value))
                    format[pasangan.Key] = pasangan.Value;
            }

            var tahun = DateTime.Now.Year;
            var urut = await _suratRepository.AmbilUrutTerakhirAsync(tahun, ct).ConfigureAwait(false);

            var nomor = new Dictionary<string, object>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["format"] = format,
                ["urutTerakhir"] = urut,
                ["tahun"] = tahun,
            };

            await using var koneksi = new SupabaseKoneksi(_pengaturan);
            await using var conn = await koneksi.BukaAsync(ct).ConfigureAwait(false);

            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO pengaturan_desa (tenant_id, kop, nomor)
                VALUES (@tenant, @kop::jsonb, @nomor::jsonb)
                ON CONFLICT (tenant_id) DO UPDATE
                   SET kop = EXCLUDED.kop,
                       nomor = EXCLUDED.nomor,
                       versi = pengaturan_desa.versi + 1,
                       diperbarui_pada = now()
                """, conn);
            cmd.Parameters.Add(new NpgsqlParameter("tenant", _pengaturan.Tenant));
            cmd.Parameters.Add(new NpgsqlParameter("kop", JsonSerializer.Serialize(kop)));
            cmd.Parameters.Add(new NpgsqlParameter("nomor", JsonSerializer.Serialize(nomor)));

            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Pengaturan desa diterbitkan: {Kop} kop, {Format} pola nomor, urut terakhir {Urut}.",
                kop.Count, format.Count, urut);

            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gagal menerbitkan pengaturan desa.");
            return false;
        }
    }

    /// <summary>
    /// Lampirkan logo kop bila ada dan ukurannya wajar. Logo bukan hal yang
    /// wajib, jadi kegagalannya tidak membatalkan penerbitan.
    /// </summary>
    private async Task TambahkanLogoAsync(Dictionary<string, object> kop, CancellationToken ct)
    {
        try
        {
            var jalur = PengaturanCetak.JalurLogoEfektif(_appConfig.LogoPath);
            if (string.IsNullOrWhiteSpace(jalur) || !File.Exists(jalur)) return;

            var info = new FileInfo(jalur);
            if (info.Length <= 0 || info.Length > BatasUkuranLogo) return;

            var bytes = await File.ReadAllBytesAsync(jalur, ct).ConfigureAwait(false);
            if (bytes.Length == 0) return;

            kop["logo"] = Convert.ToBase64String(bytes);
            kop["logoMime"] = info.Extension.ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                _ => "application/octet-stream",
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Logo kop tidak ikut diterbitkan.");
        }
    }
}

/// <summary>
/// Label wilayah untuk payload Android — dipisah supaya penerbit tidak
/// bergantung pada generator PDF.
/// </summary>
internal static class KopLabel
{
    public static string Kabupaten(Data.Models.DesaData? desa) =>
        string.Equals(desa?.JenisKabupaten?.Trim(), "Kota", StringComparison.OrdinalIgnoreCase)
            ? "Kota"
            : "Kabupaten";

    public static string Lembaga(Data.Models.DesaData? desa) =>
        string.Equals(desa?.JenisLembaga?.Trim(), "Kelurahan", StringComparison.OrdinalIgnoreCase)
            ? "Kelurahan"
            : "Desa";
}
