namespace SudesPdf.Core;

/// <summary>
/// Koreksi gamma cakupan huruf, diambil LANGSUNG dari pdfium
/// (<c>core/fxge/cfx_renderdevice.cpp</c>, <c>kTextGammaAdjust</c>).
///
/// Batang huruf 12pt pada 96 dpi lebih tipis dari satu piksel, jadi tepinya
/// jatuh sebagai cakupan parsial. Bila cakupan itu dicetak apa adanya, huruf
/// terlihat pudar dan kabur. pdfium mengambil cakupan glif dari FreeType lalu
/// melewatkannya lewat tabel 256 entri ini SEBELUM di-blend: nilai kecil
/// dinaikkan (1 → 2, 10 → 13, 64 → 73), nilai penuh dibiarkan (255 → 255),
/// sehingga tepi huruf lebih pekat tanpa memperlebar batang yang sudah penuh.
///
/// Tabel ini BUKAN kekuatan gamma bebas: bentuknya sudah diukur untuk teks,
/// jadi nilai aslinya dipertahankan persis seperti sumber pdfium.
/// </summary>
internal static class PdfTeksGamma
{
    internal static readonly byte[] Tabel =
    {
          0,   2,   3,   4,   6,   7,   8,  10,  11,  12,  13,  15,  16,  17,  18,  19,
         21,  22,  23,  24,  25,  26,  27,  29,  30,  31,  32,  33,  34,  35,  36,  38,
         39,  40,  41,  42,  43,  44,  45,  46,  47,  48,  49,  51,  52,  53,  54,  55,
         56,  57,  58,  59,  60,  61,  62,  63,  64,  65,  66,  67,  68,  69,  71,  72,
         73,  74,  75,  76,  77,  78,  79,  80,  81,  82,  83,  84,  85,  86,  87,  88,
         89,  90,  91,  92,  93,  94,  95,  96,  97,  98,  99, 100, 101, 102, 103, 104,
        105, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 116, 117, 118, 119, 120,
        121, 122, 123, 124, 125, 126, 127, 128, 129, 129, 130, 131, 132, 133, 134, 135,
        136, 137, 138, 139, 140, 141, 142, 143, 144, 145, 146, 147, 148, 149, 150, 151,
        152, 153, 154, 155, 156, 156, 157, 158, 159, 160, 161, 162, 163, 164, 165, 166,
        167, 168, 169, 170, 171, 172, 173, 174, 174, 175, 176, 177, 178, 179, 180, 181,
        182, 183, 184, 185, 186, 187, 188, 189, 190, 190, 191, 192, 193, 194, 195, 196,
        197, 198, 199, 200, 201, 202, 203, 204, 204, 205, 206, 207, 208, 209, 210, 211,
        212, 213, 214, 215, 216, 217, 217, 218, 219, 220, 221, 222, 223, 224, 225, 226,
        227, 228, 228, 229, 230, 231, 232, 233, 234, 235, 236, 237, 238, 239, 239, 240,
        241, 242, 243, 244, 245, 246, 247, 248, 249, 250, 250, 251, 252, 253, 254, 255
    };
}
