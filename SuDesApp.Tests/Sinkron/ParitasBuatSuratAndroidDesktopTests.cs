using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using SuDesApp.Sinkron;
using Xunit;

namespace SuDesApp.Tests.Sinkron;

/// <summary>
/// JAGA-PARITAS Android ↔ desktop untuk menu Buat Surat — dari cara pengisian
/// sampai hasil cetak PDF. Android membangun formulir dan PDF dari skema yang
/// sama dengan desktop (<c>Configuration/SkemaFormulir.json</c>), jadi
/// pengeditan skema/generator di satu sisi tanpa salinan ke sisi lain membuat
/// hasil cetak kedua aplikasi diam-diam berbeda. Tes ini menolak itu:
///
/// 1. Aset APK (<c>android/app/src/main/assets/skema-formulir.json</c>) harus
///    identik dengan skema desktop — perangkat yang belum tersambung memakai
///    salinan APK; kalau salinan itu basi, formulir dan PDF-nya beda.
/// 2. SEMUA target skema harus dapat dipetakan kembali ke <c>SuratData</c>
///    lewat <see cref="SkemaFormulir.Bangun"/> — kalau desktop mengganti nama
///    properti tanpa memperbaiki skema, isian Android hilang saat impor.
/// 3. Semua <c>type</c> skema harus dikenali mesin formulir Android, semua
///    <c>kustumId</c> badan harus punya perender di SuratPdf.kt, dan semua
///    <c>$kunci</c> templat badan harus terselesaikan oleh nilai yang disusun
///    BuatSuratActivity — kunci yang tidak terselesaikan tercetak KOSONG.
/// </summary>
public sealed class ParitasBuatSuratAndroidDesktopTests
{
    private static readonly string AkarRepo = CariAkarRepo();

    private static readonly string SkemaDesktop = Path.Combine(AkarRepo, "SuDesApp.Core", "Configuration", "SkemaFormulir.json");
    private static readonly string SkemaAndroid = Path.Combine(AkarRepo, "android", "app", "src", "main", "assets", "skema-formulir.json");
    private static readonly string AktivitasAndroid = Path.Combine(AkarRepo, "android", "app", "src", "main", "java", "dev", "sumberjaya", "sudes", "android", "BuatSuratActivity.kt");
    private static readonly string PdfAndroid = Path.Combine(AkarRepo, "android", "app", "src", "main", "java", "dev", "sumberjaya", "sudes", "android", "SuratPdf.kt");

    private static string CariAkarRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SuDesApp.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repo tidak ditemukan dari " + AppContext.BaseDirectory);
    }

    private static JsonElement Baca(string jalur)
    {
        var teks = File.ReadAllText(jalur).TrimStart('\uFEFF');
        return JsonDocument.Parse(teks).RootElement;
    }

    // ---------------------------------------------------------------- 1. kesamaan berkas

    [Fact]
    public void AsetSkemaAndroid_IdentikDenganSkemaDesktop()
    {
        var desktop = Baca(SkemaDesktop).EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetRawText(), StringComparer.OrdinalIgnoreCase);
        var android = Baca(SkemaAndroid).EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetRawText(), StringComparer.OrdinalIgnoreCase);

        Assert.Equal(desktop.Keys.OrderBy(x => x, StringComparer.Ordinal),
                     android.Keys.OrderBy(x => x, StringComparer.Ordinal));

        // Bandingkan secara struktural (kanonik), bukan teks mentah: spasi atau
        // urutan kunci yang berbeda tidak berarti isi berbeda.
        var beda = desktop.Keys.Where(k => Canonical(desktop[k]) != Canonical(android[k]))
                              .OrderBy(k => k, StringComparer.Ordinal).ToArray();
        Assert.True(beda.Length == 0,
            "Skema di aset APK berbeda dari desktop untuk: " + string.Join(", ", beda) +
            ". Perbarui android/app/src/main/assets/skema-formulir.json dari Configuration/SkemaFormulir.json.");
    }

    private static string Canonical(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(doc.RootElement);
    }

    // ---------------------------------------------------------------- 2. semua target terpetakan

    [Fact]
    public void SemuaTargetSkema_DapatDipetakanKeSuratData()
    {
        var skema = SkemaFormulir.Muat(SkemaDesktop);
        var gagal = new List<string>();

        foreach (var idJenis in skema.IdJenis.OrderBy(x => x, StringComparer.Ordinal))
        {
            var jenis = skema.Cari(idJenis)!;

            // Field jalur refleksi biasa — bukan list/payload/array khusus
            // (ketiganya sudah diuji SkemaFormulirTests).
            var biasa = jenis.Fields
                .Where(f => !f.Otomatis && f.Type != "list" && f.Target.Length > 0 && !KasusKhusus(f.Target))
                .ToArray();

            // Payload penuh: target yang sama boleh diisi BERBAGAI field
            // (dusunPelapor/desaPelapor/... → kematian.alamatPelapor; komponen
            // alamat anak → kenalLahir.anak.dusun/…). Yang diuji: setiap target
            // harus ada YANG MEMETAKANNYA, dan setiap field harus berhasil di
            // probe sendirian ATAU targetnya sama dengan field yang sudah lolos.
            var payload = new Dictionary<string, object>();
            foreach (var f in biasa)
                payload[f.Key] = f.Type == "number" ? 1 : (f.Type == "date" ? "01/01/1990" : "x");

            using var dokumen = JsonDocument.Parse(JsonSerializer.Serialize(payload));
            skema.Bangun(idJenis, dokumen.RootElement, null, null, out var diterapkan);

            var lolos = new List<SkemaBidang>();
            foreach (var f in biasa)
            {
                using var satu = JsonDocument.Parse(JsonSerializer.Serialize(
                    new Dictionary<string, object> { [f.Key] = payload[f.Key] }));
                skema.Bangun(idJenis, satu.RootElement, null, null, out var n);
                if (n > 0) { lolos.Add(f); continue; }
                if (lolos.Any(l => l.Target == f.Target) || TabrakanDilarang(idJenis, f.Target)) continue;
                gagal.Add($"{idJenis}: target '{f.Target}' (key {f.Key}) tidak terpetakan ke SuratData");
            }

            // Jumlah yang diterapkan payload penuh tidak boleh di bawah jumlah
            // target unik yang dikenal field (invarian agregat impor).
            var targetUnik = lolos.Select(l => l.Target).Distinct(StringComparer.Ordinal).Count();
            Assert.True(diterapkan >= targetUnik,
                $"{idJenis}: Bangun menerapkan {diterapkan} padahal {targetUnik} target unik dikenal skema.");
        }

        Assert.True(gagal.Count == 0, string.Join(Environment.NewLine, gagal));
    }

    /// <summary>Tabrakan target yang memang direncanakan (satu string disusun
    /// beberapa komponen alamat) — jangan dilapor sebagai kegagalan peta.</summary>
    private static bool TabrakanDilarang(string idJenis, string target) =>
        (idJenis.Equals("KEMATIAN", StringComparison.OrdinalIgnoreCase) && target == "kematian.alamatPelapor") ||
        (idJenis.Equals("KENAL_LAHIR", StringComparison.OrdinalIgnoreCase) && target.StartsWith("kenalLahir.anak.", StringComparison.Ordinal));

    private static bool KasusKhusus(string target) =>
        target.StartsWith("additionalData.", StringComparison.Ordinal) ||
        target.StartsWith("rincianGarapans[", StringComparison.Ordinal) ||
        target.Equals("ahliwarisdata.anak[]", StringComparison.Ordinal);

    // ---------------------------------------------------------------- 3. mesin formulir & PDF Android

    [Fact]
    public void Android_MenanganiSemuaTypeSkema()
    {
        var skema = Baca(SkemaDesktop);
        var pakai = new HashSet<string>(StringComparer.Ordinal);
        foreach (var jenis in skema.EnumerateObject())
            foreach (var bidang in jenis.Value.GetProperty("fields").EnumerateArray())
            {
                pakai.Add(bidang.GetProperty("type").GetString()!);
                if (bidang.TryGetProperty("children", out var ch))
                    foreach (var c in ch.EnumerateArray()) pakai.Add(c.GetProperty("type").GetString()!);
            }

        // Tipe yang didukung mesin formulir Android: when (b.type) di
        // bangunForm/buatBarisList + inputType/isSingleLine di bidangTeks.
        var didukung = new HashSet<string>(StringComparer.Ordinal)
            { "text", "number", "date", "textarea", "select", "list" };
        var takDidukung = pakai.Where(t => !didukung.Contains(t)).OrderBy(x => x).ToArray();
        Assert.True(takDidukung.Length == 0,
            "type skema tanpa dukungan di mesin formulir Android: " + string.Join(", ", takDidukung));
    }

    [Fact]
    public void Android_MerenSemuaKustumIdSkema()
    {
        var skema = Baca(SkemaDesktop);
        var pdf = File.ReadAllText(PdfAndroid);
        var perender = Regex.Matches(pdf, "\"([A-Za-z0-9_.]+)\" ->").Cast<Match>()
            .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

        var dipakai = new HashSet<string>(StringComparer.Ordinal);
        foreach (var jenis in skema.EnumerateObject())
        {
            if (!jenis.Value.TryGetProperty("badan", out var badan)) continue;
            foreach (var b in badan.EnumerateArray())
            {
                var id = b.TryGetProperty("kustumId", out var k) ? k.GetString() : null;
                if (!string.IsNullOrWhiteSpace(id)) dipakai.Add(id!);
            }
        }

        var hilang = dipakai.Where(id => !perender.Contains(id)).OrderBy(x => x).ToArray();
        Assert.True(hilang.Length == 0,
            "kustumId skema tanpa perender di SuratPdf.kt (dicetak sebagai catatan): " + string.Join(", ", hilang));
    }

    [Fact]
    public void Android_SemuaKunciBadanTerselesaikan()
    {
        var skema = Baca(SkemaDesktop);
        var aktivitas = File.ReadAllText(AktivitasAndroid);

        var target = new HashSet<string>(StringComparer.Ordinal);
        void Jelangjang(JsonElement bidang)
        {
            var t = bidang.TryGetProperty("target", out var tp) ? tp.GetString() : bidang.GetProperty("key").GetString();
            if (!string.IsNullOrWhiteSpace(t)) target.Add(t!);
            if (bidang.TryGetProperty("children", out var ch))
                foreach (var c in ch.EnumerateArray()) Jelangjang(c);
        }

        foreach (var jenis in skema.EnumerateObject())
            foreach (var f in jenis.Value.GetProperty("fields").EnumerateArray()) Jelangjang(f);

        // Pasangan alias Kotlin: "sisiAndroid" to "sisiSkema" (ALIAS_KUNCI dan
        // ALIAS_PENUH dibaca apa adanya dari sumber).
        var alias = Regex.Matches(aktivitas, "\"([\\w.]+)\"\\s+to\\s+\"([\\w.]+)\"").Cast<Match>()
            .Select(m => (a: m.Groups[1].Value, b: m.Groups[2].Value)).ToList();

        // Kunci yang dihitung sendiri oleh BuatSuratActivity: nilai["kunci"] = ...
        var turunan = Regex.Matches(aktivitas, "nilai\\[\"([^\"]+)\"\\]").Cast<Match>()
            .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        // Awalan daftar dinamis: nilai["ahliwaris.anak[" + idx + "].nama"] →
        // dicatat sebagai "ahliwaris.anak[" agar ref bernomor ikut terselesaikan.
        foreach (Match m in Regex.Matches(aktivitas, "\"([A-Za-z0-9_.]+\\[)\\$"))
            turunan.Add(m.Groups[1].Value);

        // Kunci baris identitas anak/pasangan pada blanko N5 — nama yang
        // ditulis BuatSuratActivity, bukan target skema.
        var childNtcR = new HashSet<string>(StringComparer.Ordinal)
        {
            "ntcr.namaAnak", "ntcr.nikAnak", "ntcr.tempatTanggalLahirAnak",
            "ntcr.kewarganegaraanAnak", "ntcr.agamaAnak", "ntcr.pekerjaanAnak", "ntcr.alamatAnak",
            "ntcr.ayahCalonAnak.nama",
            "ntcr.namaPasanganAnak", "ntcr.nikPasanganAnak", "ntcr.tempatTanggalLahirPasanganAnak",
            "ntcr.kewarganegaraanPasanganAnak", "ntcr.agamaPasanganAnak",
            "ntcr.pekerjaanPasanganAnak", "ntcr.alamatPasanganAnak", "ntcr.ayahCalonPasangan.nama",
            "ntcr.tempatTanggalLahirIstri",
        };

        var laporan = new List<string>();
        foreach (var jenis in skema.EnumerateObject())
        {
            if (!jenis.Value.TryGetProperty("badan", out var badan)) continue;
            var refs = Regex.Matches(badan.GetRawText(), "\\$([A-Za-z][A-Za-z0-9_.]*(?:\\[[0-9]+\\])?(?:\\.[A-Za-z][A-Za-z0-9_.]*)?)")
                .Cast<Match>().Select(m => m.Groups[1].Value.TrimEnd('.'))
                // urai() Android membuang titik di ujung kunci sebelum mencari.
                .Distinct();

            foreach (var r in refs)
                if (!Selesai(r)) laporan.Add($"{jenis.Name}: ${r}");
        }

        Assert.True(laporan.Count == 0,
            "Kunci templat badan tidak terselesaikan di Android (tercetak kosong):" +
            Environment.NewLine + string.Join(Environment.NewLine, laporan));

        bool Selesai(string kunci)
        {
            if (turunan.Contains(kunci) || target.Contains(kunci)) return true;

            // Varian huruf-kecil-awal: target skema menulis "ntcr.AyahCalonSuami.nama"
            // (=nama properti C#) sementara badan surat memakai ejaan camelCase
            // JSON. BuatSuratActivity menjembatani keduanya — tes mengakui
            // jembatan yang sama.
            var kecil = char.ToLowerInvariant(kunci[0]) + kunci[1..];
            var besar = char.ToUpperInvariant(kunci[0]) + kunci[1..];
            if (target.Contains(kecil) || target.Contains(besar) ||
                turunan.Contains(kecil) || turunan.Contains(besar)) return true;
            var titik = kunci.LastIndexOf('.');
            // Prefiks "ntcr.<x>.<anak>": Android menulis ulang blok orang tua
            // per sub-key dari target "ntcr.<X>.<anak>" (huruf besar) plus
            // gabungan tempatTanggalLahir. Aturan yang sama di sini menangkap
            // ref karangan (mis. ntcr.ayahCalonAnak.nama) yang tidak punya
            // sumber di skema.
            var anakKey = titik > 0 ? kunci[(titik + 1)..] : kunci;
            // Kunci turunan ejaan badan: nilai["..."] = (termasuk yang disusun
            // lewat string template ${} seperti "$orang.tempatTanggalLahir")
            // dan pemetaan eksplisit baris blanko N5 (namaAnak, ayahCalonAnak…).
            if (turunan.Contains(anakKey) && kunci.StartsWith("ntcr.", StringComparison.Ordinal)) return true;
            if (childNtcR.Contains(kunci)) return true;
            if (titik > 5 && kunci.StartsWith("ntcr.", StringComparison.Ordinal))
            {
                var indukKecil = kunci[5..titik];
                var indukBesar = char.ToUpperInvariant(indukKecil[0]) + indukKecil[1..];
                var dijembatani = new[] { "nama", "nik", "tempatLahir", "tanggalLahir",
                    "kewarganegaraan", "agama", "pekerjaan", "alamat", "binBinti" };
                if (dijembatani.Contains(anakKey) &&
                    (target.Contains($"ntcr.{indukBesar}.{anakKey}") || target.Contains($"ntcr.{indukKecil}.{anakKey}")))
                    return true;
                if (anakKey == "tempatTanggalLahir" &&
                    (target.Contains($"ntcr.{indukBesar}.tempatLahir") || target.Contains($"ntcr.{indukKecil}.tempatLahir")))
                    return true;
            }
            if (turunan.Any(t => t.EndsWith("." + anakKey, StringComparison.Ordinal))) return true;

            // Ref bernomor (ahliwaris.anak[0].nama, garapanTambahan[2].luas):
            // terselesaikan bila prefiks berindeks dikenal sebagai turunan
            // dinamis atau target daftar.
            var berindeks = Regex.Replace(kunci, "\\[\\d+\\]", "[");
            if (turunan.Any(t => t.EndsWith("[", StringComparison.Ordinal) &&
                                 berindeks.StartsWith(t, StringComparison.Ordinal))) return true;
            if (target.Any(t => t.EndsWith("[", StringComparison.Ordinal) &&
                                berindeks.StartsWith(t, StringComparison.Ordinal))) return true;
            // Target daftar desktop: "ahliwarisdata.anak[].nama" / "rincianGarapans[..]"
            if (target.Any(t => t.EndsWith("[", StringComparison.Ordinal) &&
                                kunci.StartsWith(t[..^1], StringComparison.Ordinal))) return true;

            // Jembatan alias dua arah: Android membaca sisi `b` lalu menulis
            // ulang berprefiks `a` (ALIAS_KUNCI) atau kunci penuh (ALIAS_PENUH),
            // jadi ref dari salah satu sisi terselesaikan bila sisi lawannya known.
            foreach (var (a, b) in alias)
            foreach (var (dari, ke) in new[] { (a, b), (b, a) })
            {
                if (kunci == a && (target.Contains(b) || turunan.Contains(b))) return true;
                if (kunci.StartsWith(dari + ".", StringComparison.Ordinal))
                {
                    var sisa = kunci[(dari.Length + 1)..];
                    foreach (var potong in Pecah(sisa))
                    {
                        var calon = ke + "." + potong;
                        if (target.Contains(calon) || turunan.Contains(calon)) return true;
                        if (turunan.Any(t => t.EndsWith("[", StringComparison.Ordinal) &&
                                             (calon + "[").StartsWith(t, StringComparison.Ordinal))) return true;
                    }
                    // daftar: a.x[0].y dengan turunan a.x[ (sudah lewat) atau target a.x[]
                    if (target.Any(t => t.EndsWith("[", StringComparison.Ordinal) &&
                                        (ke + "." + sisa).StartsWith(t, StringComparison.Ordinal))) return true;
                }
            }
            return false;
        }

        // "anak[0].nama" → ["anak[0].nama", "nama"]; "ayah.nama" → keduanya.
        static IEnumerable<string> Pecah(string sisa)
        {
            yield return sisa;
            var titik = sisa.IndexOf('.');
            if (titik >= 0 && titik + 1 < sisa.Length) yield return sisa[(titik + 1)..];
        }
    }

    // ---------------------------------------------------------------- 4. kartu pengelompokan (grup)

    [Fact]
    public void Skema_MengandungGrupKartu_SepertiSectionDesktop()
    {
        var skema = SkemaFormulir.Muat(SkemaDesktop);
        var gagal = new List<string>();

        foreach (var idJenis in skema.IdJenis.OrderBy(x => x, StringComparer.Ordinal))
        {
            var jenis = skema.Cari(idJenis)!;
            var urutan = jenis.GrupUrutan ?? new List<string>();
            if (urutan.Count == 0) { gagal.Add($"{idJenis}: grupUrutan kosong"); continue; }

            foreach (var f in jenis.Fields)
            {
                if (string.IsNullOrWhiteSpace(f.Grup))
                    gagal.Add($"{idJenis}.{f.Key}: tanpa grup");
                else if (!urutan.Contains(f.Grup, StringComparer.Ordinal))
                    gagal.Add($"{idJenis}.{f.Key}: grup '{f.Grup}' tidak ada di grupUrutan");
            }

            // Setiap kartu harus benar-benar berisi minimal satu bidang —
            // kartu kosong hanya polusi tampilan.
            var terpakai = jenis.Fields.Select(f => f.Grup).ToHashSet(StringComparer.Ordinal);
            foreach (var judul in urutan.Where(u => !terpakai.Contains(u)))
                gagal.Add($"{idJenis}: grupUrutan '{judul}' tidak dipakai bidang mana pun");
        }

        Assert.True(gagal.Count == 0, string.Join(Environment.NewLine, gagal));
    }

    [Fact]
    public void Android_MembacaDanMerenderGrupSkema()
    {
        // Kalau salah satu pihak berhenti memakai grup, kartu Android kembali
        // menyatu jadi satu daftar panjang — gejala yang persis dikeluhkan
        // operator, jadi dijaga di sini.
        var skemaKt = File.ReadAllText(Path.Combine(AkarRepo, "android", "app", "src", "main", "java",
            "dev", "sumberjaya", "sudes", "android", "SkemaFormulir.kt"));
        Assert.Contains("grupUrutan", skemaKt, StringComparison.Ordinal);
        Assert.Contains("fun grupkan", skemaKt, StringComparison.Ordinal);

        var aktivitas = File.ReadAllText(AktivitasAndroid);
        Assert.Contains("kartuGrup", aktivitas, StringComparison.Ordinal);
        Assert.Contains("grupkan(", aktivitas, StringComparison.Ordinal);
    }
}
