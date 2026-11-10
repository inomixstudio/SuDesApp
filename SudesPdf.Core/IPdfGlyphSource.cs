using System;
using System.Collections.Generic;
using System.Text;

namespace SudesPdf.Core;


/// <summary>Sumber bentuk glif dari program font tersemat.</summary>
public interface IPdfGlyphSource
{
    /// <summary>Satuan per em (1000 pada CFF, 2048/1024 pada TrueType).</summary>
    double UnitsPerEm { get; }

    bool TryGetOutline(int glyphId, out GlyphOutline outline);

    /// <summary>Nomor glif untuk nama glif (dari /Differences pada PDF).</summary>
    bool TryGetGlyphIdByName(string glyphName, out int glyphId);

    /// <summary>Nomor glif untuk titik kode Unicode (font non-simbolik).</summary>
    bool TryGetGlyphIdByUnicode(int codePoint, out int glyphId);
}

