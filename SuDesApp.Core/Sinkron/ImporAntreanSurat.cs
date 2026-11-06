using System.Text.Json;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;

namespace SuDesApp.Sinkron;

/// <summary>Hasil impor satu baris antrean.</summary>
/// <param name="Berhasil">True bila surat masuk register.</param>
/// <param name="IdSurat">Id surat di register (0 bila gagal).</param>
/// <param name="NomorResmi">Nomor resmi yang diberikan desktop.</param>
/// <param name="FieldDiterapkan">Berapa field isian yang dikenali skema.</param>
/// <param name="PesanGalat">Penyebab gagal, bila gagal.</param>
public sealed record HasilImpor(
    bool Berhasil,
    int IdSurat,
    string? NomorResmi,
    int FieldDiterapkan,
    string? PesanGalat);

/// <summary>
/// Mengubah satu baris antrean Android menjadi surat di register desktop.
///
/// <para>
/// Penomoran TIDAK diambil dari Android: nomor Android hanyalah placeholder
/// yang bisa dobel. <see cref="ISuratRepository.AddSuratAsync"/>-lah yang
/// menormalkan — nomor kosong atau sudah dipakai akan dibangkitkan ulang, dan
/// NIK yang sudah ada akan dipakai warganya (tidak menimpa, tidak membuat
/// ganda). Karena itu tabrakan nomor/NIK antar-perangkat bukan masalah.
/// </para>
///
/// <para>
/// PDF TIDAK dibuat di sini: register desktop membangkitkan PDF resmi dari data
/// surat saat dicetak/dipratinjau, sehingga nomor di PDF selalu nomor resmi.
/// </para>
/// </summary>
public sealed class ImporAntreanSurat
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly SkemaFormulir _skema;
    private readonly ILogger<ImporAntreanSurat> _logger;

    public ImporAntreanSurat(
        IUnitOfWork unitOfWork,
        SkemaFormulir skema,
        ILogger<ImporAntreanSurat> logger)
    {
        _unitOfWork = unitOfWork ?? throw new System.ArgumentNullException(nameof(unitOfWork));
        _skema = skema ?? throw new System.ArgumentNullException(nameof(skema));
        _logger = logger ?? throw new System.ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Keterangan bawaan SKTM — sama persis dengan yang dibentuk
    /// <c>SktmInputViewModel.GetDefaultKeteranganAsync</c> saat form dibuka.
    /// </summary>
    private async Task<string> DefaultKeteranganSktmAsync(CancellationToken ct)
    {
        try
        {
            var desa = await _unitOfWork.DesaRepository!.GetInfoDesaAsync().ConfigureAwait(false);
            var namaDesa = string.IsNullOrWhiteSpace(desa?.NamaDesa) ? "-" : desa!.NamaDesa.Trim();
            var kecamatan = string.IsNullOrWhiteSpace(desa?.Kecamatan) ? "-" : desa!.Kecamatan.Trim();
            var kabupaten = string.IsNullOrWhiteSpace(desa?.Kabupaten) ? "-" : desa!.Kabupaten.Trim();

            return $"Benar nama tersebut di atas adalah warga Desa {namaDesa} " +
                   $"Kecamatan {kecamatan} " +
                   $"Kabupaten {kabupaten} " +
                   "dan menurut sepengetahuan kami orang tersebut di atas tergolong keluarga ekonomi tidak mampu.";
        }
        catch
        {
            return "Keterangan tidak dapat dimuat.";
        }
    }

    /// <summary>
    /// Impor satu baris. Tidak pernah melempar — kegagalan dikembalikan sebagai
    /// <see cref="HasilImpor"/> dengan pesan, sehingga satu baris buruk tidak
    /// menghentikan sisa antrean.
    /// </summary>
    public async Task<HasilImpor> ImporAsync(AntreanSuratBaris baris, CancellationToken ct = default)
    {
        try
        {
            var fields = baris.Fields;
            var surat = _skema.Bangun(
                baris.IdJenis,
                fields,
                baris.NomorSementara,
                baris.TanggalSurat,
                out var diterapkan);

            // Kolom nik pada baris antrean adalah salinan yang dipakai Android
            // untuk pencarian; pastikan pihak warga tetap punya NIK bila isian
            // Android belum mengisinya (mis. form yang hanya meminta nama).
            if (string.IsNullOrWhiteSpace(surat.Warga?.NIK) && !string.IsNullOrWhiteSpace(baris.Nik))
            {
                surat.Warga!.NIK = baris.Nik!.Trim();
            }

            // Jenis yang skemanya menandai keterangan sebagai "otomatis"
            // dihitung desktop, bukan diminta dari Android — sama seperti saat
            // form dibuka di desktop. Tanpa ini, surat hasil impor SKTM keluar
            // dengan keterangan kosong.
            if (string.IsNullOrWhiteSpace(surat.Keterangan) &&
                string.Equals(baris.IdJenis, "SKTM", StringComparison.OrdinalIgnoreCase))
            {
                surat.Keterangan = await DefaultKeteranganSktmAsync(ct).ConfigureAwait(false);
            }

            var id = await _unitOfWork.SuratRepository
                .AddSuratAsync(surat, null, ct)
                .ConfigureAwait(false);

            // AddSuratAsync menulis status lewat query InsertSurat yang TIDAK
            // memuat kolom Status (definisi terakhir di SuratQueries.sql), jadi
            // surat baru selalu mendarat sebagai 'Draft'. Surat hasil impor
            // dianggap sudah terbit — naikkan statusnya di sini, bukan
            // mengharap operator mengubahnya dari register.
            if (string.Equals(surat.Status, "Active", StringComparison.OrdinalIgnoreCase))
            {
                await _unitOfWork.SuratRepository
                    .UpdateStatusAsync(id, "Active", null, ct)
                    .ConfigureAwait(false);
            }

            _logger.LogInformation(
                "Surat antrean {AntreanId} diimpor menjadi surat {IdSurat} nomor {Nomor} ({Jenis}, {Field} field)",
                baris.Id, id, surat.NomorSurat, surat.NamaJenis, diterapkan);

            return new HasilImpor(true, id, surat.NomorSurat, diterapkan, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gagal mengimpor antrean {AntreanId} ({Jenis})", baris.Id, baris.IdJenis);
            return new HasilImpor(false, 0, null, 0, ex.Message);
        }
    }
}
