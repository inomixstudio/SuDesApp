using System.Globalization;
using System.Reflection;
using System.Text.Json;
using SuDesApp.Data.Models;

namespace SuDesApp.Sinkron;

/// <summary>Satu bidang formulir surat di Android.</summary>
/// <param name="Key">Kunci camelCase yang dikirim Android pada payload <c>fields</c>.</param>
/// <param name="Label">Label yang tampil di layar Android.</param>
/// <param name="Type">text | number | date | select.</param>
/// <param name="Target">Path properti SuratData, mis. "warga.nama" atau "sku.bidangUsaha".</param>
/// <param name="Required">Wajib diisi operator.</param>
/// <param name="Options">Pilihan untuk type=select.</param>
/// <param name="Otomatis">True bila dihitung desktop, bukan diminta dari Android.</param>
public sealed record SkemaBidang(
    string Key,
    string Label,
    string Type,
    string Target,
    bool Required = false,
    string[]? Options = null,
    bool Otomatis = false);

/// <summary>Skema formulir satu jenis surat.</summary>
public sealed record SkemaJenis(string IdJenis, string Label, List<SkemaBidang> Fields);

/// <summary>
/// Skema formulir surat yang dibagikan desktop → Android.
///
/// Android memakai skema ini untuk membangun formulir; desktop memakainya
/// untuk memetakan kembali isian Android ke <see cref="SuratData"/> saat
/// impor. Satu sumber kebenaran, jadi penambahan field di desktop tidak
/// membuat Android diam-diam kehilangan data.
///
/// Skema hidup di <c>Configuration/SkemaFormulir.json</c> (ikut terpasang).
/// File itu juga yang diterbitkan desktop ke tabel <c>skema_formulir</c>
/// Supabase untuk dibaca Android.
/// </summary>
public sealed class SkemaFormulir
{
    private const string JalurBawaan = "Configuration/SkemaFormulir.json";

    private readonly Dictionary<string, SkemaJenis> _jenis = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> IdJenis => _jenis.Keys;

    /// <summary>Skema kosong — dipakai bila berkas tidak ada; impor tetap jalan tapi hanya field yang dikenal yang masuk.</summary>
    public static SkemaFormulir Kosong() => new();

    /// <summary>
    /// Baca skema dari berkas. Berkas tidak ada / rusak mengembalikan skema
    /// kosong, bukan galat — sinkron tidak boleh berhenti hanya karena skema
    /// belum disiapkan.
    /// </summary>
    public static SkemaFormulir Muat(string? jalur = null)
    {
        var path = jalur ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, JalurBawaan);
        try
        {
            if (!File.Exists(path)) return Kosong();

            var json = File.ReadAllText(path);
            return MuatDariJson(json);
        }
        catch
        {
            return Kosong();
        }
    }

    /// <summary>Parse skema dari teks JSON. Kesalahan parse → skema kosong.</summary>
    public static SkemaFormulir MuatDariJson(string json)
    {
        var skema = Kosong();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return skema;

            foreach (var jenis in doc.RootElement.EnumerateObject())
            {
                var label = jenis.Name;
                var fields = new List<SkemaBidang>();

                if (jenis.Value.ValueKind == JsonValueKind.Object)
                {
                    if (jenis.Value.TryGetProperty("label", out var l) && l.ValueKind == JsonValueKind.String)
                        label = l.GetString() ?? jenis.Name;

                    if (jenis.Value.TryGetProperty("fields", out var f) && f.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var bidang in f.EnumerateArray())
                        {
                            if (bidang.ValueKind != JsonValueKind.Object) continue;

                            var key = AmbilString(bidang, "key");
                            if (string.IsNullOrWhiteSpace(key)) continue;

                            var target = AmbilString(bidang, "target") ?? key;
                            var options = bidang.TryGetProperty("options", out var o) && o.ValueKind == JsonValueKind.Array
                                ? o.EnumerateArray().Select(x => x.ToString()).ToArray()
                                : null;

                            fields.Add(new SkemaBidang(
                                key,
                                AmbilString(bidang, "label") ?? key,
                                AmbilString(bidang, "type") ?? "text",
                                target,
                                bidang.TryGetProperty("required", out var r) && r.ValueKind == JsonValueKind.True,
                                options,
                                bidang.TryGetProperty("otomatis", out var ot) && ot.ValueKind == JsonValueKind.True));
                        }
                    }
                }

                skema._jenis[jenis.Name] = new SkemaJenis(jenis.Name, label, fields);
            }
        }
        catch
        {
            return Kosong();
        }

        return skema;
    }

    public SkemaJenis? Cari(string? idJenis) =>
        string.IsNullOrWhiteSpace(idJenis) ? null : _jenis.GetValueOrDefault(idJenis);

    /// <summary>
    /// Bangun <see cref="SuratData"/> dari isian Android. Hanya field yang
    /// skemanya dikenal yang diterapkan; field lain diabadian (tidak melempar)
    /// supaya surat tetap bisa diimpor dan diperiksa operator.
    /// </summary>
    /// <param name="idJenis">Kode jenis surat.</param>
    /// <param name="fields">Elemen <c>fields</c> dari payload antrean.</param>
    /// <param name="nomorSementara">Nomor placeholder dari Android.</param>
    /// <param name="tanggalSurat">Tanggal surat bila sudah diketahui.</param>
    /// <param name="diterapkan">Jumlah field yang berhasil diterapkan (untuk log/diagnostik).</param>
    public SuratData Bangun(
        string idJenis,
        JsonElement fields,
        string? nomorSementara,
        DateTime? tanggalSurat,
        out int diterapkan)
    {
        var surat = new SuratData
        {
            NamaJenis = idJenis,
            NomorSurat = nomorSementara,
            TanggalSurat = tanggalSurat ?? DateTime.Now,
            Status = "Active",
        };

        diterapkan = 0;
        var skema = Cari(idJenis);
        if (skema is null || fields.ValueKind != JsonValueKind.Object) return surat;

        // Kasus khusus yang tidak bisa dilakukan sekadar reflection:
        //   additionalData.<x>      -> payload JSON (Rekening Koran, Template Surat)
        //   rincianGarapans[...]    -> daftar garapan (GARAPAN_SAWAH)
        //   ahliwarisdata.anak[]    -> daftar anak (AHLI_WARIS)
        var additional = new Dictionary<string, object?>(StringComparer.Ordinal);
        var garapan = new List<GarapanData>();
        var adaGarapan = false;

        foreach (var bidang in skema.Fields)
        {
            if (bidang.Otomatis) continue;
            if (!fields.TryGetProperty(bidang.Key, out var nilai)) continue;
            if (nilai.ValueKind == JsonValueKind.Null || nilai.ValueKind == JsonValueKind.Undefined) continue;

            var target = bidang.Target;

            if (target.StartsWith("additionalData.", StringComparison.Ordinal))
            {
                var sisa = target["additionalData.".Length..];
                if (sisa.StartsWith("nilai[", StringComparison.Ordinal) && sisa.EndsWith(']'))
                {
                    var kunci = bidang.Key.StartsWith("kolom_", StringComparison.Ordinal)
                        ? bidang.Key["kolom_".Length..]
                        : bidang.Key;
                    additional["nilai:" + kunci] = NilaiUntukJson(nilai);
                }
                else
                {
                    additional[Pascal(sisa)] = NilaiUntukJson(nilai);
                }

                diterapkan++;
                continue;
            }

            if (target.StartsWith("rincianGarapans[0].", StringComparison.Ordinal))
            {
                if (garapan.Count == 0) garapan.Add(new GarapanData());
                if (Terapkan(garapan[0], target["rincianGarapans[0].".Length..], nilai))
                {
                    adaGarapan = true;
                    diterapkan++;
                }
                continue;
            }

            if (target.StartsWith("rincianGarapans[", StringComparison.Ordinal) && target.EndsWith("..]"))
            {
                foreach (var item in BacaArrayObjek(nilai, nameof(GarapanData)))
                {
                    garapan.Add(item);
                    adaGarapan = true;
                }

                diterapkan++;
                continue;
            }

            if (target.Equals("ahliwarisdata.anak[]", StringComparison.Ordinal))
            {
                if (nilai.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in nilai.EnumerateArray())
                    {
                        var anak = new AnakAhliWarisData();
                        var ubah = false;
                        foreach (var p in item.EnumerateObject())
                        {
                            if (Terapkan(anak, p.Name, p.Value)) ubah = true;
                        }

                        if (ubah) surat.AhliWarisData!.Anak.Add(anak);
                    }
                }

                diterapkan++;
                continue;
            }

            if (Terapkan(surat, target, nilai))
            {
                diterapkan++;
            }
        }

        if (additional.Count > 0)
        {
            var json = new Dictionary<string, object?>(StringComparer.Ordinal);
            Dictionary<string, object?>? nilai = null;

            foreach (var kv in additional)
            {
                if (kv.Key.StartsWith("nilai:", StringComparison.Ordinal))
                {
                    nilai ??= new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    nilai[kv.Key["nilai:".Length..]] = kv.Value;
                }
                else
                {
                    json[kv.Key] = kv.Value;
                }
            }

            if (nilai is not null) json["Nilai"] = nilai;

            surat.AdditionalData = JsonSerializer.Serialize(json);
        }

        if (adaGarapan)
        {
            surat.RincianGarapans = garapan;
        }

        return surat;
    }

    /// <summary>Ubah JsonElement menjadi nilai CLR agar bisa diserialisasi ulang.</summary>
    private static object? NilaiUntukJson(JsonElement nilai) => nilai.ValueKind switch
    {
        JsonValueKind.String => nilai.GetString(),
        JsonValueKind.Number => nilai.TryGetInt64(out var l) ? l : nilai.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Array => nilai.EnumerateArray().Select(NilaiUntukJson).ToList(),
        JsonValueKind.Object => nilai.EnumerateObject().ToDictionary(p => p.Name, p => NilaiUntukJson(p.Value)),
        _ => null,
    };

    /// <summary>
    /// Baca daftar objek garapan dari JSON. Tiap item dipetakan per properti
    /// memakai <see cref="Terapkan"/>, jadi penamaan kunci mengikuti skema.
    /// </summary>
    private static IEnumerable<GarapanData> BacaArrayObjek(JsonElement nilai, string namaTipe)
    {
        if (nilai.ValueKind != JsonValueKind.Array) yield break;

        foreach (var item in nilai.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;

            var data = new GarapanData();
            foreach (var p in item.EnumerateObject())
            {
                Terapkan(data, p.Name, p.Value);
            }

            yield return data;
        }
    }

    private static string Pascal(string camel) =>
        string.IsNullOrEmpty(camel) ? camel : char.ToUpperInvariant(camel[0]) + camel[1..];

    /// <summary>
    /// Terapkan satu nilai JSON ke path properti SuratData ("warga.nama",
    /// "sku.sejakTahun", ...). Mengembalikan false bila path tidak dikenal —
    /// pemanggil tidak perlu tahu mana yang gagal.
    /// </summary>
    internal static bool Terapkan(object akar, string jalur, JsonElement nilai)
    {
        var bagian = jalur.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (bagian.Length == 0) return false;

        object? saatIni = akar;
        for (var i = 0; i < bagian.Length - 1; i++)
        {
            if (saatIni is null) return false;
            var prop = saatIni.GetType().GetProperty(bagian[i],
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop is null) return false;

            saatIni = prop.GetValue(saatIni);
        }

        if (saatIni is null) return false;
        var target = saatIni.GetType().GetProperty(bagian[^1],
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (target is null || !target.CanWrite) return false;

        var tipeDasar = Nullable.GetUnderlyingType(target.PropertyType) ?? target.PropertyType;

        if (nilai.ValueKind == JsonValueKind.Null)
        {
            if (!target.PropertyType.IsValueType || Nullable.GetUnderlyingType(target.PropertyType) is not null)
            {
                target.SetValue(saatIni, null);
                return true;
            }
            return false;
        }

        object? konversi = tipeDasar.Name switch
        {
            "String" => nilai.ValueKind == JsonValueKind.String ? nilai.GetString() : nilai.ToString(),
            "Int32" => nilai.TryGetInt32(out var i) ? i : 0,
            "Int64" => nilai.TryGetInt64(out var l) ? l : 0L,
            "Double" => nilai.TryGetDouble(out var d) ? d : 0d,
            "Decimal" => nilai.TryGetDecimal(out var m) ? m : 0m,
            "Boolean" => nilai.ValueKind == JsonValueKind.True
                || (nilai.ValueKind == JsonValueKind.String && bool.TryParse(nilai.GetString(), out var b) && b),
            "DateTime" => nilai.ValueKind == JsonValueKind.String
                && DateTime.TryParse(nilai.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var dt)
                ? dt
                : null,
            _ => null,
        };

        if (konversi is null)
        {
            return false;
        }

        try
        {
            target.SetValue(saatIni, konversi);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? AmbilString(JsonElement el, string nama) =>
        el.TryGetProperty(nama, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}
