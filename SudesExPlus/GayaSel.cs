using System.Drawing;
using SudesExPlus.Style;

namespace SudesExPlus;

/// <summary>
/// Gaya mentah satu sel: nilai persis yang dipakai penulis .xlsx. Disimpan per
/// sel (bukan per rentang) supaya menata satu rentang tidak menghapus gaya yang
/// sudah dipasang pada sel-sel di dalamnya — mis. latar baris ganjil yang
/// dipasang lebih dulu, lalu garis keliling seluruh tabel.
/// </summary>
internal sealed class GayaSel
{
    public bool Bold { get; set; }
    public bool Italic { get; set; }

    /// <summary>Garis bawah huruf; dipertahankan saat berkas pihak lain dibaca.</summary>
    public bool GarisBawahHuruf { get; set; }

    /// <summary>Nama huruf (mis. Calibri, Arial); null berarti huruf bawaan.</summary>
    public string? NamaHuruf { get; set; }

    public double? UkuranHuruf { get; set; }
    public Color? WarnaHuruf { get; set; }
    public Color? WarnaIsian { get; set; }
    public ExcelHorizontalAlignment? RataMendatar { get; set; }
    public ExcelVerticalAlignment? RataTegak { get; set; }

    /// <summary>Teks dibungkus ke baris berikutnya di dalam sel.</summary>
    public bool BungkusTeks { get; set; }
    public string? FormatAngka { get; set; }
    public ExcelBorderStyle GarisKiri { get; set; }
    public ExcelBorderStyle GarisKanan { get; set; }
    public ExcelBorderStyle GarisAtas { get; set; }
    public ExcelBorderStyle GarisBawah { get; set; }

    /// <summary>True bila sel memakai gaya apa pun; dipakai penulis untuk memilih xf.</summary>
    public bool AdaGaya =>
        Bold || Italic || GarisBawahHuruf || !string.IsNullOrEmpty(NamaHuruf) ||
        UkuranHuruf.HasValue || WarnaHuruf.HasValue || WarnaIsian.HasValue ||
        RataMendatar.HasValue || RataTegak.HasValue || BungkusTeks || !string.IsNullOrEmpty(FormatAngka) ||
        GarisKiri != ExcelBorderStyle.None || GarisKanan != ExcelBorderStyle.None ||
        GarisAtas != ExcelBorderStyle.None || GarisBawah != ExcelBorderStyle.None;

    /// <summary>Salinan dangkal; dipakai saat sel diminta lewat rentang berbeda.</summary>
    public GayaSel Clone() => (GayaSel)MemberwiseClone();
}

/// <summary>
/// Sasaran perubahan gaya. Ada dua bentuk: satu sel, atau satu rentang. Rentang
/// meneruskan perubahan ke setiap sel yang sudah ada di dalamnya, sehingga
/// rantai seperti <c>ws.Cells[r,1,r,n].Style.Fill…</c> berlaku untuk seluruh
/// baris tanpa membuat objek gaya bersama yang bisa saling menimpa.
/// </summary>
internal interface ITargetGaya
{
    /// <summary>Gaya contoh untuk pembacaan (sel pertama yang ada, atau kosong).</summary>
    GayaSel Contoh { get; }

    /// <summary>Terapkan perubahan ke semua sel sasaran.</summary>
    void Ubah(Action<GayaSel> ubah);

    /// <summary>Pasang garis hanya di tepi terluar wilayah sasaran.</summary>
    void GarisKeliling(ExcelBorderStyle gaya);
}

/// <summary>Target gaya untuk satu sel.</summary>
internal sealed class TargetSel : ITargetGaya
{
    private readonly ExcelCell _sel;

    public TargetSel(ExcelCell sel) => _sel = sel;

    public GayaSel Contoh => _sel.Gaya;

    public void Ubah(Action<GayaSel> ubah) => ubah(_sel.Gaya);

    public void GarisKeliling(ExcelBorderStyle gaya)
    {
        _sel.Gaya.GarisKiri = gaya;
        _sel.Gaya.GarisKanan = gaya;
        _sel.Gaya.GarisAtas = gaya;
        _sel.Gaya.GarisBawah = gaya;
    }
}

/// <summary>Target gaya untuk satu rentang sel pada satu worksheet.</summary>
internal sealed class TargetRentang : ITargetGaya
{
    private readonly ExcelWorksheet _lembar;
    private readonly int _barisAwal;
    private readonly int _kolomAwal;
    private readonly int _barisAkhir;
    private readonly int _kolomAkhir;

    public TargetRentang(ExcelWorksheet lembar, int barisAwal, int kolomAwal, int barisAkhir, int kolomAkhir)
    {
        _lembar = lembar;
        _barisAwal = Math.Min(barisAwal, barisAkhir);
        _kolomAwal = Math.Min(kolomAwal, kolomAkhir);
        _barisAkhir = Math.Max(barisAwal, barisAkhir);
        _kolomAkhir = Math.Max(kolomAwal, kolomAkhir);
    }

    public GayaSel Contoh =>
        _lembar.CariSel(_barisAwal, _kolomAwal)?.Gaya ?? new GayaSel();

    public void Ubah(Action<GayaSel> ubah)
    {
        // Rentang dipakai pada baris tabel yang seluruh kolomnya sudah berisi
        // nilai; sel kosong di dalamnya tetap dibuat supaya gaya latar/garis
        // tidak berlubang di kolom yang kebetulan kosong.
        for (int baris = _barisAwal; baris <= _barisAkhir; baris++)
        {
            for (int kolom = _kolomAwal; kolom <= _kolomAkhir; kolom++)
            {
                ubah(_lembar.SelUntukGaya(baris, kolom).Gaya);
            }
        }
    }

    public void GarisKeliling(ExcelBorderStyle gaya)
    {
        for (int kolom = _kolomAwal; kolom <= _kolomAkhir; kolom++)
        {
            _lembar.SelUntukGaya(_barisAwal, kolom).Gaya.GarisAtas = gaya;
            _lembar.SelUntukGaya(_barisAkhir, kolom).Gaya.GarisBawah = gaya;
        }

        for (int baris = _barisAwal; baris <= _barisAkhir; baris++)
        {
            _lembar.SelUntukGaya(baris, _kolomAwal).Gaya.GarisKiri = gaya;
            _lembar.SelUntukGaya(baris, _kolomAkhir).Gaya.GarisKanan = gaya;
        }
    }
}
