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
    /// NIK khusus untuk jenis yang form desktopnya tidak punya kolom NIK.
    /// Null bila jenis tersebut wajib meminta NIK dari operator.
    /// </summary>
    internal static string? NikKhususUntuk(string? idJenis) =>
        (idJenis ?? string.Empty).ToUpperInvariant() switch
        {
            "AHLI_WARIS" => SuratConstants.NIK_KEMATIAN,
            "INSTANSI" or "REKENING_KORAN" => SuratConstants.NIK_INSTANSI,
            _ => null,
        };

    /// <summary>
    /// Susun daftar waris dari pasangan, anak, dan penerima kuasa — sama
    /// persis dengan <c>AhliWarisInputViewModel</c>. NIK waris memakai
    /// <see cref="SuratConstants.NIK_KEMATIAN"/> karena surat ahli waris tidak
    /// memuat NIK tiap waris di form desktop.
    /// </summary>
    private static void BangunWaris(SuratData surat)
    {
        var data = surat.AhliWarisData;
        if (data is null) return;

        var waris = new AhliWaris { Waris = new List<Waris>() };

        if (!string.IsNullOrWhiteSpace(data.NamaPasangan))
        {
            waris.Waris.Add(new Waris
            {
                NamaWaris = data.NamaPasangan.Trim(),
                NIKWaris = SuratConstants.NIK_KEMATIAN,
                HubunganWaris = data.JenisKelaminPasangan == "Perempuan" ? "Istri" : "Suami",
            });
        }

        foreach (var anak in data.Anak)
        {
            if (string.IsNullOrWhiteSpace(anak.Nama)) continue;

            waris.Waris.Add(new Waris
            {
                NamaWaris = anak.Nama.Trim(),
                NIKWaris = string.IsNullOrWhiteSpace(anak.NIK) ? SuratConstants.NIK_KEMATIAN : anak.NIK.Trim(),
                HubunganWaris = "Anak",
            });
        }

        if (!string.IsNullOrWhiteSpace(data.NamaPenerimaKuasa))
        {
            waris.Waris.Add(new Waris
            {
                NamaWaris = data.NamaPenerimaKuasa.Trim(),
                NIKWaris = SuratConstants.NIK_KEMATIAN,
                HubunganWaris = "Kuasa",
            });
        }

        if (waris.Waris.Count > 0)
        {
            surat.AhliWaris = waris;
        }
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

            // Jenis yang form desktopnya tidak punya kolom NIK memakai NIK
            // khusus (paritas AhliWarisInputViewModel / RekeningKoranViewModel):
            // AHLI_WARIS = almarhum (NOL), INSTANSI/REKENING_KORAN = penanda
            // instansi (SEMbilan). Tanpa ini GetOrCreateWargaAsync membuat warga
            // ber-NIK kosong dan validator menolak surat INSTANSI.
            if (string.IsNullOrWhiteSpace(surat.Warga?.NIK))
            {
                var nikKhusus = NikKhususUntuk(baris.IdJenis);
                if (nikKhusus is not null)
                {
                    surat.Warga!.NIK = nikKhusus;
                }
            }

            // AhliWaris.Waris dibangkitkan desktop dari pasangan + anak + penerima
            // kuasa (lihat AhliWarisInputViewModel.BuildSuratData). Android tidak
            // mengirim daftar waris tersendiri, jadi impor menirukan aturan yang
            // sama — kalau daftar ini kosong, AhliWarisDataHandler menolak surat.
            if (string.Equals(baris.IdJenis, SuratConstants.AHLI_WARIS, StringComparison.OrdinalIgnoreCase))
            {
                BangunWaris(surat);
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
