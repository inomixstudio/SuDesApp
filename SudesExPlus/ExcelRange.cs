using System.Collections.Generic;

namespace SudesExPlus;

/// <summary>
/// Rentang sel persegi. Nilai pada rentang dibaca dari sel kiri-atas dan
/// ditulis ke seluruh sel di dalamnya; gaya diteruskan ke setiap sel sehingga
/// menata satu baris tidak perlu memanggil sel satu per satu.
/// </summary>
public sealed class ExcelRange
{
    private readonly ExcelWorksheet _lembar;
    private readonly int _barisAwal;
    private readonly int _kolomAwal;
    private readonly int _barisAkhir;
    private readonly int _kolomAkhir;

    internal ExcelRange(ExcelWorksheet lembar, int barisAwal, int kolomAwal, int barisAkhir, int kolomAkhir)
    {
        _lembar = lembar;
        _barisAwal = Math.Min(barisAwal, barisAkhir);
        _kolomAwal = Math.Min(kolomAwal, kolomAkhir);
        _barisAkhir = Math.Max(barisAwal, barisAkhir);
        _kolomAkhir = Math.Max(kolomAwal, kolomAkhir);
    }

    /// <summary>True bila rentang hanya satu sel.</summary>
    private bool SatuSel => _barisAwal == _barisAkhir && _kolomAwal == _kolomAkhir;

    /// <summary>
    /// Isi sel. Membaca mengambil sel kiri-atas; menulis menyalin nilai ke semua
    /// sel pada rentang (perilaku yang sama dengan pustaka sebelumnya).
    /// </summary>
    public object? Value
    {
        get => _lembar.CariSel(_barisAwal, _kolomAwal)?.Value;
        set
        {
            if (SatuSel)
            {
                _lembar.SelAtauBuat(_barisAwal, _kolomAwal).Value = value;
                return;
            }

            for (int baris = _barisAwal; baris <= _barisAkhir; baris++)
            {
                for (int kolom = _kolomAwal; kolom <= _kolomAkhir; kolom++)
                {
                    _lembar.SelAtauBuat(baris, kolom).Value = value;
                }
            }
        }
    }

    /// <summary>
    /// Rumus tanpa "=" di depan. Membaca mengambil sel kiri-atas; menulis
    /// memasang rumus yang sama ke semua sel pada rentang (berguna untuk kolom
    /// hitung yang diisi sekaligus). Menyetel null menghapus rumus tanpa
    /// menghapus nilai sel.
    /// </summary>
    public string? Formula
    {
        get => _lembar.CariSel(_barisAwal, _kolomAwal)?.Formula;
        set
        {
            string? bersih = ExcelCell.NormalisasiRumus(value);
            foreach (var sel in SelRentang())
            {
                sel.Formula = bersih;
            }
        }
    }

    /// <summary>Teks tampilan sel kiri-atas.</summary>
    public string Text => _lembar.CariSel(_barisAwal, _kolomAwal)?.Text ?? string.Empty;

    /// <summary>Gaya seluruh rentang.</summary>
    public ExcelStyle Style => new(new TargetRentang(_lembar, _barisAwal, _kolomAwal, _barisAkhir, _kolomAkhir));

    /// <summary>
    /// Gabungkan sel pada rentang. Menetapkan false tidak melepas gabungan yang
    /// sudah ada — sama seperti pustaka sebelumnya, aplikasi hanya memakai true.
    /// </summary>
    public bool Merge
    {
        get => false;
        set
        {
            if (value) _lembar.Gabungkan(_barisAwal, _kolomAwal, _barisAkhir, _kolomAkhir);
        }
    }

    /// <summary>
    /// Atur lebar kolom mengikuti isi terpanjang, dibatasi
    /// <paramref name="lebarMinimal"/> dan <paramref name="lebarMaksimal"/>.
    /// </summary>
    public void AutoFitColumns(double lebarMinimal = 8.43, double lebarMaksimal = 60) =>
        _lembar.SesuaikanLebar(_kolomAwal, _kolomAkhir, lebarMinimal, lebarMaksimal);

    /// <summary>Semua sel pada rentang (dibuat bila belum ada).</summary>
    internal IEnumerable<ExcelCell> SelRentang()
    {
        for (int baris = _barisAwal; baris <= _barisAkhir; baris++)
        {
            for (int kolom = _kolomAwal; kolom <= _kolomAkhir; kolom++)
            {
                yield return _lembar.SelAtauBuat(baris, kolom);
            }
        }
    }
}

/// <summary>Penerjemah nomor baris/kolom menjadi rujukan A1 dan sebaliknya.</summary>
internal static class ExcelRujukan
{
    /// <summary>Nama kolom Excel: 1 → A, 27 → AA.</summary>
    public static string Kolom(int kolom)
    {
        var huruf = new System.Text.StringBuilder(3);
        int sisa = Math.Max(1, kolom);

        while (sisa > 0)
        {
            int indeks = (sisa - 1) % 26;
            huruf.Insert(0, (char)('A' + indeks));
            sisa = (sisa - 1) / 26;
        }

        return huruf.ToString();
    }

    /// <summary>Rujukan satu sel: baris 2 kolom 3 → "C2".</summary>
    public static string Sel(int baris, int kolom) => Kolom(kolom) + baris.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Rujukan rentang: "A1:D1".</summary>
    public static string Rentang(int barisAwal, int kolomAwal, int barisAkhir, int kolomAkhir) =>
        Sel(barisAwal, kolomAwal) + ":" + Sel(barisAkhir, kolomAkhir);

    /// <summary>Urai rujukan sel ("C12") menjadi nomor kolom dan baris (1-based).</summary>
    public static bool Urai(string? rujukan, out int baris, out int kolom)
    {
        baris = 0;
        kolom = 0;
        if (string.IsNullOrWhiteSpace(rujukan)) return false;

        int i = 0;
        int kolomHasil = 0;
        while (i < rujukan.Length && char.IsLetter(rujukan[i]))
        {
            kolomHasil = kolomHasil * 26 + (char.ToUpperInvariant(rujukan[i]) - 'A' + 1);
            i++;
        }

        if (kolomHasil == 0 || i >= rujukan.Length) return false;

        if (!int.TryParse(rujukan.AsSpan(i), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out int barisHasil) || barisHasil < 1)
        {
            return false;
        }

        baris = barisHasil;
        kolom = kolomHasil;
        return true;
    }
}
