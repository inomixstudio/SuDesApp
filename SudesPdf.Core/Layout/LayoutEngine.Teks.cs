using System;
using System.Collections.Generic;
using System.Globalization;

namespace SudesPdf.Core.Layout;

internal sealed partial class LayoutEngine
    {
    // ------------------------------------------------------------------ teks

    private float PlaceText(
        LayoutNode node,
        float x,
        float yTop,
        float width,
        PageContext ctx,
        float budget,
        int startLine,
        bool force,
        out Continuation? next,
        bool draw = true)
    {
        var block = node.Text ?? new TextBlock();
        float left = x + node.MarginLeft;
        float boxWidth = node.Width is { } fixedWidth
            ? fixedWidth + node.PaddingLeft + node.PaddingRight
            : Math.Max(0f, width - node.MarginLeft - node.MarginRight);
        float innerWidth = Math.Max(0f, boxWidth - node.PaddingLeft - node.PaddingRight);

        var texts = TextLayout.Layout(node, block, innerWidth, ctx.Settings.DefaultStyle, ctx.PageNumber, ctx.TotalPages);

        float y = yTop - node.MarginTop - node.PaddingTop;
        float dipakai = node.MarginTop + node.PaddingTop;
        int index = startLine;

        for (; index < texts.Lines.Count; index++)
        {
            var line = texts.Lines[index];
            bool terakhir = index == texts.Lines.Count - 1;
            if (dipakai + line.Height > budget + Epsilon && (index > 0 || !force))
            {
                next = new Continuation(node, index, null);
                return dipakai;
            }

            if (draw)
            {
                DrawTextLine(node, block, line, left + node.PaddingLeft, y, innerWidth, ctx, terakhir);
            }

            y -= line.Height;
            dipakai += line.Height;
        }

        dipakai += node.PaddingBottom + node.MarginBottom;
        next = null;
        return dipakai;
    }

    /// <summary>Gambar satu baris teks beserta garis bawahnya.</summary>
    private static void DrawTextLine(
        LayoutNode node,
        TextBlock block,
        LaidOutLine line,
        float left,
        float yTop,
        float innerWidth,
        PageContext ctx,
        bool barisTerakhir)
    {
        var align = block.Align ?? (node.HAlign switch
        {
            HorizontalAlign.Center => TextAlign.Center,
            HorizontalAlign.Right => TextAlign.Right,
            _ => TextAlign.Left,
        });

        float offset = align switch
        {
            TextAlign.Center => Math.Max(0f, (innerWidth - line.Width) / 2f),
            TextAlign.Right => Math.Max(0f, innerWidth - line.Width),
            _ => 0f,
        };

        float wordSpacing = 0f;
        if (align == TextAlign.Justify && !barisTerakhir && line.Spaces > 0 && line.Width < innerWidth)
        {
            float perlu = (innerWidth - line.Width) / line.Spaces;

            // Batas peregangan. Spasi normal Times selebar 0,25 em, jadi batas
            // 0,75 em per spasi masih menyisakan celah paling lebar tiga kali
            // spasi biasa — bentuk yang lazim pada surat yang diratakan penuh.
            //
            // Batas ini dulu 0,25 em dan akibatnya banyak baris TENGAH paragraf
            // tidak diratakan sama sekali: satu paragraf jadi bercampur baris
            // rata-kiri dan rata-kanan sehingga tepi kanannya bergerigi. Batas
            // yang lebih longgar membuat seluruh baris (kecuali baris terakhir)
            // benar-benar rata, dan hanya baris yang benar-benar ekstrem yang
            // dibiarkan menggantung.
            float em = 0f;
            foreach (var bagian in line.Parts)
            {
                if (bagian.Size > em) em = bagian.Size;
            }

            if (em > 0f && perlu <= em * 0.75f)
            {
                wordSpacing = perlu;
            }
        }

        float baseline = yTop - line.Baseline;
        float tambahanSpasi = 0f;
        foreach (var part in line.Parts)
        {
            if (part.Text.Length > 0)
            {
                float x = left + offset + part.X + tambahanSpasi;
                ctx.DrawText(part, x, baseline, 0f);

                if (part.Underline)
                {
                    float thickness = Math.Max(0.5f, part.Size * 0.06f);
                    float underlineY = baseline - part.Size * 0.12f;
                    ctx.DrawLine(x, underlineY, x + part.Width, underlineY, thickness, part.Color);
                }
            }

            if (wordSpacing > 0f && part.IsSpace) tambahanSpasi += wordSpacing;
        }
    }

    }
