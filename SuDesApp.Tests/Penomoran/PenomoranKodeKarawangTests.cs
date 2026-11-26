using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using Xunit;

namespace SuDesApp.Tests.Penomoran;

/// <summary>
/// Mengunci awalan nomor surat pada kode klasifikasi arsip Kabupaten Karawang.
///
/// <para>
/// Dulu 20 dari 28 jenis surat memakai <c>470</c> dan satu deret bersama. Angka itu
/// bukan hasil pemetaan: lampiran Perbup Karawang 74/2020 memang mencetak 470, tetapi
/// hanya pada empat contoh (SK umum 8.a, kelahiran 8.b, kematian 8.c, ahli waris 8.e) —
/// dan 470 di sana adalah KELAS "Kependudukan dan Catatan Sipil" pada Perbup Karawang
/// 28/2019 tentang Pedoman Klasifikasi Arsip. Surat sejahtera sosial, usaha, pertanian,
/// dan kepolisian punya kelasnya sendiri, jadi memakai 470 untuk semuanya berarti nomor
/// surat desa tidak pernah cocok dengan klasifikasi arsipnya sendiri.
/// </para>
///
/// <para>
/// Uji ini tidak memaksa operator: berkas konfigurasi dan penyesuaian di Pengaturan
/// Nomor Surat tetap bisa diubah. Yang dijaga adalah bawaan aplikasi.
/// </para>
/// </summary>
public sealed class PenomoranKodeKarawangTests
{
    /// <summary>
    /// Kelas yang dipakai bawaan aplikasi, dengan urusan aslinya di Perbup 28/2019.
    /// 474.3/474.2 (blanko NTCR) dan TMPL (penanda internal) adalah pengecualian
    /// yang sudah tercatat di docs/referensi-penomoran-surat.md.
    /// </summary>
    private static readonly Dictionary<string, string> KelasDikenal = new()
    {
        ["470"] = "Kependudukan dan Catatan Sipil",
        ["471"] = "Pendaftaran Penduduk",
        ["472"] = "Pencatatan Sipil",
        ["465"] = "Kesejahteraan Sosial",
        ["330"] = "Keamanan",
        ["331"] = "Kepolisian",
        ["510"] = "Perdagangan",
        ["518"] = "Koperasi/UMKM",
        ["521"] = "Tanaman Pangan",
        ["580"] = "Perbankan Moneter",
        ["474.3"] = "blanko NTCR N1–N6 (Kepdirjen Bimas Islam 473/2020)",
        ["474.2"] = "blanko NTCR N8 (Kepdirjen Bimas Islam 473/2020)",
        ["TMPL"] = "penanda internal Template Surat"
    };

    private static List<JenisSuratConfig> MuatBawaan()
    {
        var jalur = Path.Combine(AkarRepo(), "SuDesApp.Core", "Configuration", "JenisSuratConfig.json");
        using var dokumen = JsonDocument.Parse(File.ReadAllText(jalur));
        return dokumen.RootElement.EnumerateArray().Select(e => new JenisSuratConfig
        {
            NamaJenis = e.GetProperty("NamaJenis").GetString()!,
            NomorFormat = e.GetProperty("NomorFormat").GetString()!,
            IsSharedNumbering = e.GetProperty("IsSharedNumbering").GetBoolean()
        }).ToList();
    }

    private static string Awalan(string format)
        => SuDesApp.Services.PenomoranSuratService.AmbilAwalan(format);

    [Fact]
    public void Setiap_awalan_adalah_kelas_klasifikasi_Karawang()
    {
        var asing = MuatBawaan()
            .Select(c => (c.NamaJenis, Awalan: Awalan(c.NomorFormat)))
            .Where(x => !KelasDikenal.ContainsKey(x.Awalan))
            .ToList();

        Assert.True(asing.Count == 0,
            "Awalan nomor surat harus kelas klasifikasi yang dikenal (lihat Perbup Karawang 28/2019): "
            + string.Join(", ", asing.Select(x => $"{x.NamaJenis}={x.Awalan}")));
    }

    [Fact]
    public void Tidak_semua_surat_bermukim_di_470()
    {
        // Inti laporan pengguna: 470 untuk semuanya. Empat contoh lampiran Perbup
        // 74/2020 memang 470, tetapi itu kelas kependudukan/capil — bukan kelas lain.
        var di470 = MuatBawaan().Where(c => Awalan(c.NomorFormat) == "470").ToList();

        Assert.Equal(new[]
        {
            SuratConstants.DOMISILI_WARGA,
            SuratConstants.KEMATIAN,
            SuratConstants.KENAL_LAHIR,
            SuratConstants.AHLI_WARIS,
            SuratConstants.SK_BELUM_TERBIT
        }.OrderBy(x => x).ToArray(), di470.Select(c => c.NamaJenis).OrderBy(x => x).ToArray());
    }

    [Fact]
    public void Jenis_seawalan_ikut_deret_bersama_supaya_nomor_tidak_ganda()
    {
        // Cabang "deret sendiri" menghitung per KodeJenis: dua jenis dengan awalan
        // sama akan sama-sama mulai 001 dan bertabrakan (persis bug blanko N1–N6).
        var kelompok = MuatBawaan()
            .GroupBy(c => Awalan(c.NomorFormat))
            .Where(g => g.Count() > 1 && g.Any(c => !c.IsSharedNumbering))
            .Select(g => $"{g.Key}: " + string.Join(", ", g.Where(c => !c.IsSharedNumbering).Select(c => c.NamaJenis)))
            .ToList();

        Assert.True(kelompok.Count == 0,
            "Jenis yang berbagi awalan wajib IsSharedNumbering=true, kalau tidak nomornya ganda:"
            + Environment.NewLine + string.Join(Environment.NewLine, kelompok));
    }

    [Fact]
    public void Pemetaan_bawaan_sesuai_kelas_urusannya()
    {
        var harapan = new Dictionary<string, string>
        {
            [SuratConstants.SKTM] = "465",
            [SuratConstants.PENGHASILAN] = "465",
            [SuratConstants.SK_TIDAK_PUNYA_RUMAH] = "465",
            [SuratConstants.SK_PINDAH] = "471",
            [SuratConstants.IJIN_TINGGAL] = "471",
            [SuratConstants.BEDANAMA] = "471",
            [SuratConstants.PENGANTAR_ADMINDUK] = "471",
            [SuratConstants.SKD_UMUM] = "472",
            [SuratConstants.IZIN_ORTU] = "472",
            [SuratConstants.GARAPAN_SAWAH] = "521",
            [SuratConstants.SKU] = "518",
            [SuratConstants.INSTANSI] = "510",
            [SuratConstants.PENGANTAR_SKCK] = "331",
            [SuratConstants.IZIN_KERAMAIAN] = "330",
            [SuratConstants.REKENING_KORAN] = "580"
        };

        var salah = MuatBawaan()
            .Where(c => harapan.TryGetValue(c.NamaJenis, out var kode) && Awalan(c.NomorFormat) != kode)
            .Select(c => $"{c.NamaJenis}: {Awalan(c.NomorFormat)} (harus {harapan[c.NamaJenis]})")
            .ToList();

        Assert.True(salah.Count == 0,
            "Pemetaan kelas salah — lihat docs/referensi-penomoran-surat.md:"
            + Environment.NewLine + string.Join(Environment.NewLine, salah));
    }

    [Fact]
    public void Bootstrap_configloader_tidak_menyimpang_dari_katalog()
    {
        // CreateDefaultConfigAsync menulis berkas bootstrap (dua entri) bila
        // JenisSuratConfig.json belum ada. Kalau pola di sana berbeda dari katalog,
        // database baru mulai dengan awalan yang menyimpang tanpa ada yang sadar.
        var sumber = File.ReadAllText(Path.Combine(
            AkarRepo(), "SuDesApp.Core", "Configuration", "JenisSuratConfigLoader.cs"));

        var pasangan = System.Text.RegularExpressions.Regex.Matches(
            sumber,
            @"NamaJenis = ""(?<jenis>[A-Z_0-9]+)"",[\s\S]{0,400}?NomorFormat = ""(?<pola>[^""]+)""");
        Assert.True(pasangan.Count > 0, "Bootstrap konfigurasi tidak ditemukan di JenisSuratConfigLoader.cs.");

        var katalog = MuatBawaan().ToDictionary(c => c.NamaJenis, c => Awalan(c.NomorFormat));
        var beda = pasangan
            .Select(m => (Jenis: m.Groups["jenis"].Value, Pola: Awalan(m.Groups["pola"].Value)))
            .Select(x => (x.Jenis, Bawaan: x.Pola, Katalog: katalog.TryGetValue(x.Jenis, out var kode) ? kode : null))
            .Where(x => x.Katalog is not null && x.Katalog != x.Bawaan)
            .Select(x => $"{x.Jenis}: bootstrap {x.Bawaan}, katalog {x.Katalog}")
            .ToList();

        Assert.True(beda.Count == 0, string.Join(Environment.NewLine, beda));
    }

    private static string AkarRepo()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null)
        {
            if (File.Exists(Path.Combine(folder.FullName, "SuDesApp.slnx"))) return folder.FullName;
            folder = folder.Parent;
        }

        throw new InvalidOperationException("Folder repo tidak ditemukan dari " + AppContext.BaseDirectory);
    }
}
