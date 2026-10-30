using System.Collections.Generic;
using System.Linq;

namespace SudesExPlus;

/// <summary>Alamat satu sel (1-based), dipakai pada <see cref="ExcelDimension"/>.</summary>
public readonly struct ExcelCellAddress
{
    public ExcelCellAddress(int row, int column)
    {
        Row = row;
        Column = column;
    }

    public int Row { get; }

    public int Column { get; }
}

/// <summary>Batas wilayah berisi data pada satu worksheet.</summary>
public sealed class ExcelDimension
{
    internal ExcelDimension(ExcelCellAddress awal, ExcelCellAddress akhir)
    {
        Start = awal;
        End = akhir;
    }

    public ExcelCellAddress Start { get; }

    public ExcelCellAddress End { get; }
}

/// <summary>Lebar satu kolom.</summary>
public sealed class ExcelColumn
{
    private readonly ExcelWorksheet _lembar;
    private readonly int _kolom;

    internal ExcelColumn(ExcelWorksheet lembar, int kolom)
    {
        _lembar = lembar;
        _kolom = kolom;
    }

    /// <summary>Lebar kolom dalam satuan karakter Excel.</summary>
    public double Width
    {
        get => _lembar.LebarKolom.TryGetValue(_kolom, out double lebar) ? lebar : 8.43;
        set => _lembar.LebarKolom[_kolom] = value;
    }
}

/// <summary>Tinggi satu baris.</summary>
public sealed class ExcelRow
{
    private readonly ExcelWorksheet _lembar;
    private readonly int _baris;

    internal ExcelRow(ExcelWorksheet lembar, int baris)
    {
        _lembar = lembar;
        _baris = baris;
    }

    /// <summary>Tinggi baris dalam poin.</summary>
    public double Height
    {
        get => _lembar.TinggiBaris.TryGetValue(_baris, out double tinggi) ? tinggi : 15;
        set => _lembar.TinggiBaris[_baris] = value;
    }
}

/// <summary>
/// Koleksi sel worksheet dengan bentuk pemanggilan <c>ws.Cells[baris, kolom]</c>
/// dan <c>ws.Cells[barisAwal, kolomAwal, barisAkhir, kolomAkhir]</c>.
/// </summary>
public sealed class ExcelCells
{
    private readonly ExcelWorksheet _lembar;

    internal ExcelCells(ExcelWorksheet lembar) => _lembar = lembar;

    /// <summary>Satu sel (1-based).</summary>
    public ExcelRange this[int baris, int kolom] => _lembar.Rentang(baris, kolom);

    /// <summary>Rentang sel (1-based, inklusif).</summary>
    public ExcelRange this[int barisAwal, int kolomAwal, int barisAkhir, int kolomAkhir] =>
        _lembar.Rentang(barisAwal, kolomAwal, barisAkhir, kolomAkhir);
}

/// <summary>Keadaan tampilan worksheet (saat ini: bekukan panel).</summary>
public sealed class ExcelSheetView
{
    private readonly ExcelWorksheet _lembar;

    internal ExcelSheetView(ExcelWorksheet lembar) => _lembar = lembar;

    /// <summary>
    /// Bekukan baris di atas <paramref name="baris"/> dan kolom di kiri
    /// <paramref name="kolom"/>; keduanya 1-based seperti pustaka sebelumnya.
    /// </summary>
    public void FreezePanes(int baris, int kolom)
    {
        _lembar.Bekukan = (Math.Max(1, baris), Math.Max(1, kolom));
    }
}

/// <summary>
/// Satu worksheet. Sel dibuat saat diminta (lazy) supaya membangun tabel ribuan
/// baris tidak mengalokasikan sel kosong yang tidak pernah diisi.
/// </summary>
public sealed class ExcelWorksheet
{
    private readonly Dictionary<(int Baris, int Kolom), ExcelCell> _sel = new();
    private readonly List<string> _gabungan = new();

    internal ExcelWorksheet(string nama)
    {
        Name = nama;
        Cells = new ExcelCells(this);
    }

    /// <summary>Koleksi sel; dipakai sebagai <c>ws.Cells[baris, kolom]</c>.</summary>
    public ExcelCells Cells { get; }

    internal Dictionary<int, double> LebarKolom { get; } = new();

    internal Dictionary<int, double> TinggiBaris { get; } = new();

    internal (int Baris, int Kolom)? Bekukan { get; set; }

    internal IReadOnlyList<string> Gabungan => _gabungan;

    /// <summary>Nama sheet; dibersihkan dari karakter terlarang saat ditulis.</summary>
    public string Name { get; set; }

    /// <summary>Batas data, atau null bila worksheet kosong.</summary>
    public ExcelDimension? Dimension
    {
        get
        {
            var terpakai = _sel.Values.Where(s => s.AdaIsi || s.Gaya.AdaGaya).ToList();
            if (terpakai.Count == 0) return null;

            int barisAwal = terpakai.Min(s => s.Baris);
            int barisAkhir = terpakai.Max(s => s.Baris);
            int kolomAwal = terpakai.Min(s => s.Kolom);
            int kolomAkhir = terpakai.Max(s => s.Kolom);

            return new ExcelDimension(
                new ExcelCellAddress(barisAwal, kolomAwal),
                new ExcelCellAddress(barisAkhir, kolomAkhir));
        }
    }

    /// <summary>Semua sel yang berisi, urut baris lalu kolom.</summary>
    internal IEnumerable<ExcelCell> Sel =>
        _sel.Values.Where(s => s.AdaIsi).OrderBy(s => s.Baris).ThenBy(s => s.Kolom);

    /// <summary>Satu sel.</summary>
    internal ExcelRange Rentang(int baris, int kolom) => new(this, baris, kolom, baris, kolom);

    /// <summary>Rentang sel, dari sudut kiri-atas ke kanan-bawah.</summary>
    internal ExcelRange Rentang(int barisAwal, int kolomAwal, int barisAkhir, int kolomAkhir) =>
        new(this, barisAwal, kolomAwal, barisAkhir, kolomAkhir);

    public ExcelColumn Column(int kolom) => new(this, kolom);

    public ExcelRow Row(int baris) => new(this, baris);

    public ExcelSheetView View => new(this);

    /// <summary>Sel yang sudah ada, atau null.</summary>
    internal ExcelCell? CariSel(int baris, int kolom) =>
        _sel.TryGetValue((baris, kolom), out var sel) ? sel : null;

    /// <summary>Sel yang sudah ada atau baru dibuat (untuk mengisi nilai).</summary>
    internal ExcelCell SelAtauBuat(int baris, int kolom)
    {
        if (!_sel.TryGetValue((baris, kolom), out var sel))
        {
            sel = new ExcelCell(this, baris, kolom);
            _sel[(baris, kolom)] = sel;
        }

        return sel;
    }

    /// <summary>
    /// Sel untuk keperluan gaya: dibuat bila belum ada, supaya latar dan garis
    /// tidak berlubang pada sel yang isinya kosong.
    /// </summary>
    internal ExcelCell SelUntukGaya(int baris, int kolom) => SelAtauBuat(baris, kolom);

    /// <summary>Gabungkan rentang menjadi satu sel tampilan.</summary>
    internal void Gabungkan(int barisAwal, int kolomAwal, int barisAkhir, int kolomAkhir)
    {
        string rujukan = ExcelRujukan.Rentang(barisAwal, kolomAwal, barisAkhir, kolomAkhir);
        if (!_gabungan.Contains(rujukan)) _gabungan.Add(rujukan);
    }

    /// <summary>Lebar kolom otomatis dari panjang teks, dibatasi min/maks.</summary>
    internal void SesuaikanLebar(int kolomAwal, int kolomAkhir, double minimal, double maksimal)
    {
        for (int kolom = kolomAwal; kolom <= kolomAkhir; kolom++)
        {
            double terpanjang = 0;
            foreach (var sel in _sel.Values.Where(s => s.Kolom == kolom && s.Value != null))
            {
                terpanjang = Math.Max(terpanjang, sel.Text.Length);
            }

            // Dua karakter ekstra memberi ruang agar teks tidak menempel garis.
            LebarKolom[kolom] = Math.Clamp(terpanjang + 2, minimal, maksimal);
        }
    }
}
