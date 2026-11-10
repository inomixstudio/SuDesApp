using System;
using System.Collections.Generic;
using System.Text;

namespace SudesPdf.Core;


/// <summary>Font halaman: lebar, pemetaan teks, dan bentuk glif.</summary>
public abstract class PdfFont
{
    private readonly Dictionary<int, GlyphOutline> _outlineCache = new();
    private readonly HashSet<int> _missingOutlines = new();

    public abstract string Name { get; }

    public abstract bool IsComposite { get; }

    public virtual bool IsVertical => false;

    public virtual bool IsSymbolic => false;

    /// <summary>Latar belakang serba satu em (1000 unit) — dipakai penataan huruf.</summary>
    public virtual double Ascent => 800;

    public virtual double Descent => -200;

    /// <summary>Bentuk glif tersedia (font tersemat dengan program yang didukung).</summary>
    public virtual bool HasOutlines => false;

    /// <summary>Satuan per em pada program glif (1000 untuk CFF, 2048 umumnya TrueType).</summary>
    public virtual double GlyphUnitsPerEm => 1000;

    /// <summary>Baca satu kode dari data string dan hitung panjangnya.</summary>
    public abstract int ReadCode(byte[] data, int position, int end, out int code);

    /// <summary>Lebar kode dalam satuan 1/1000 em.</summary>
    public abstract double GetWidth(int code);

    /// <summary>Teks Unicode untuk kode tersebut, atau null bila tidak dikenal.</summary>
    public abstract string? Decode(int code);

    public abstract bool TryGetGlyph(int code, out int glyphId);

    public virtual bool TryGetOutline(int glyphId, out GlyphOutline outline)
    {
        outline = GlyphOutline.Empty;
        return false;
    }

    protected bool TryGetCachedOutline(int glyphId, out GlyphOutline outline)
    {
        if (_outlineCache.TryGetValue(glyphId, out var cached))
        {
            outline = cached;
            return true;
        }

        if (_missingOutlines.Contains(glyphId))
        {
            outline = GlyphOutline.Empty;
            return false;
        }

        outline = GlyphOutline.Empty;
        return false;
    }

    protected void CacheOutline(int glyphId, GlyphOutline? outline)
    {
        if (outline is null || outline.IsEmpty)
        {
            _missingOutlines.Add(glyphId);
            return;
        }

        _outlineCache[glyphId] = outline;
    }
}

