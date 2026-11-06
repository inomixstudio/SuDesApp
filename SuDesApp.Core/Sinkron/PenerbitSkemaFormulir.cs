using System.Text.Json;
using Microsoft.Extensions.Logging;
using Npgsql;
using SuDesApp.Supabase;

namespace SuDesApp.Sinkron;

/// <summary>
/// Menerbitkan skema formulir desktop ke tabel <c>skema_formulir</c> Supabase
/// supaya aplikasi Android bisa membangun formulir dari sumber yang sama.
///
/// <para>
/// Tanpa penerbitan ini, Android tidak punya definisi formulir — dan kalau
/// Android menyalin skema sendiri, dua salinan akan menyimpang tanpa jejak.
/// Skema diterbitkan per tenant; versi naik setiap kali isinya berubah.
/// </para>
/// </summary>
public sealed class PenerbitSkemaFormulir
{
    private readonly SupabasePengaturan _pengaturan;
    private readonly ILogger<PenerbitSkemaFormulir> _logger;

    public PenerbitSkemaFormulir(
        SupabasePengaturan pengaturan,
        ILogger<PenerbitSkemaFormulir> logger)
    {
        _pengaturan = pengaturan ?? throw new System.ArgumentNullException(nameof(pengaturan));
        _logger = logger ?? throw new System.ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Terbitkan seluruh jenis surat. Mengembalikan jumlah jenis yang masuk,
    /// atau -1 bila gagal (tidak pernah melempar).
    /// </summary>
    public async Task<int> TerbitkanAsync(string? jalurBerkas = null, CancellationToken ct = default)
    {
        if (!_pengaturan.Lengkap) return -1;

        try
        {
            var skema = SkemaFormulir.Muat(jalurBerkas);
            var json = File.ReadAllText(jalurBerkas ?? Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "Configuration", "SkemaFormulir.json"));

            await using var koneksi = new SupabaseKoneksi(_pengaturan);
            await using var conn = await koneksi.BukaAsync(ct).ConfigureAwait(false);

            // jsonb_each memecah {"SKTM": {...}} menjadi baris (key, value).
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO skema_formulir (tenant_id, id_jenis, versi, skema)
                SELECT @tenant, e.key, 1, e.value
                  FROM jsonb_each(@skema::jsonb) AS e
                ON CONFLICT (tenant_id, id_jenis) DO UPDATE
                   SET skema = EXCLUDED.skema,
                       versi = skema_formulir.versi + 1,
                       diperbarui_pada = now()
                """, conn);
            cmd.Parameters.Add(new NpgsqlParameter("tenant", _pengaturan.Tenant));
            cmd.Parameters.Add(new NpgsqlParameter("skema", json));

            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Skema formulir diterbitkan: {Jenis} jenis untuk tenant {Tenant}.",
                skema.IdJenis.Count, _pengaturan.Tenant);

            return skema.IdJenis.Count;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gagal menerbitkan skema formulir.");
            return -1;
        }
    }
}
