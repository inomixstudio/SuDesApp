namespace SudesExPlus.Style;

/// <summary>Jenis isian latar sel. Hanya isian penuh yang dipakai aplikasi.</summary>
public enum ExcelFillStyle
{
    /// <summary>Tanpa isian (sel tembus latar worksheet).</summary>
    None = 0,

    /// <summary>Warna penuh menutupi seluruh sel.</summary>
    Solid = 1,
}

/// <summary>Perataan isi sel secara mendatar.</summary>
public enum ExcelHorizontalAlignment
{
    /// <summary>Mengikuti jenis data (teks kiri, angka kanan).</summary>
    General = 0,

    Left = 1,
    Center = 2,
    Right = 3,
}

/// <summary>Perataan isi sel secara tegak.</summary>
public enum ExcelVerticalAlignment
{
    Top = 0,
    Center = 1,
    Bottom = 2,
}

/// <summary>Ketebalan garis tepi sel.</summary>
public enum ExcelBorderStyle
{
    None = 0,
    Hair = 1,
    Thin = 2,
    Medium = 3,
    Thick = 4,
    Double = 5,
}
