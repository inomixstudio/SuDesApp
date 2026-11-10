using System;
using System.Collections.Generic;
using System.Globalization;

namespace SudesPdf.Core.Layout;

internal sealed partial class LayoutEngine
    {

    // ----------------------------------------------------------------- baris

    private float PlaceRow(LayoutNode node, float left, float yTop, float width, PageContext ctx, bool draw)
    {
        float innerWidth = Math.Max(0f, width - node.PaddingLeft - node.PaddingRight);
        var widths = ResolveRowWidths(node, innerWidth, ctx);

        float boxLeft = left;
        float x = boxLeft + node.PaddingLeft;
        float y = yTop - node.PaddingTop;
        float tinggi = 0f;

        for (int i = 0; i < node.Children.Count; i++)
        {
            float tinggiAnak = MeasureNodeHeight(node.Children[i], widths[i], ctx);
            if (tinggiAnak > tinggi) tinggi = tinggiAnak;
        }

        for (int i = 0; i < node.Children.Count; i++)
        {
            var child = node.Children[i];
            float tinggiAnak = MeasureNodeHeight(child, widths[i], ctx);
            float atas = y;
            if (child.VAlign == VerticalAlign.Middle && tinggi > tinggiAnak) atas -= (tinggi - tinggiAnak) / 2f;
            else if (child.VAlign == VerticalAlign.Bottom && tinggi > tinggiAnak) atas -= tinggi - tinggiAnak;

            PlaceNoSplit(child, x, atas, widths[i], ctx, draw, tinggi);
            x += widths[i] + node.Spacing;
        }

        return node.PaddingTop + tinggi + node.PaddingBottom + node.MarginTop + node.MarginBottom;
    }

    private float[] ResolveRowWidths(LayoutNode node, float innerWidth, PageContext ctx)
    {
        int jumlah = node.Children.Count;
        var widths = new float[jumlah];
        if (jumlah == 0) return widths;

        float spasi = node.Spacing * (jumlah - 1);
        float tersedia = Math.Max(0f, innerWidth - spasi);
        float tetap = 0f;
        float bobot = 0f;

        for (int i = 0; i < jumlah; i++)
        {
            var child = node.Children[i];
            if (child.Width is { } lebar)
            {
                widths[i] = lebar;
                tetap += lebar;
            }
            else if (child.Weight <= 0f)
            {
                float alami = NaturalWidth(child, ctx);
                widths[i] = alami;
                tetap += alami;
            }
            else
            {
                bobot += child.Weight;
            }
        }

        float sisa = Math.Max(0f, tersedia - tetap);
        for (int i = 0; i < jumlah; i++)
        {
            var child = node.Children[i];
            if (child.Width is null && child.Weight > 0f)
            {
                widths[i] = bobot <= 0f ? 0f : sisa * child.Weight / bobot;
            }
        }

        return widths;
    }

    private float NaturalWidth(LayoutNode node, PageContext ctx)
    {
        switch (node.Kind)
        {
            case LayoutKind.Text:
            {
                var block = node.Text ?? new TextBlock();
                var texts = TextLayout.Layout(node, block, float.MaxValue / 4f, ctx.Settings.DefaultStyle, ctx.PageNumber, ctx.TotalPages);
                return texts.Width + node.PaddingLeft + node.PaddingRight;
            }

            case LayoutKind.Column:
            {
                float terlebar = 0f;
                foreach (var child in node.Children) terlebar = Math.Max(terlebar, NaturalWidth(child, ctx));
                return terlebar;
            }

            case LayoutKind.Row:
            {
                float total = 0f;
                foreach (var child in node.Children) total += NaturalWidth(child, ctx) + node.Spacing;
                return total;
            }

            case LayoutKind.Image:
            {
                var (lebar, _) = UkuranGambar(node);
                return lebar;
            }

            default:
                return node.Width ?? 0f;
        }
    }

    }
