using System;
using System.Collections.Generic;
using System.Globalization;

namespace SudesPdf.Core.Layout;

internal sealed partial class LayoutEngine
    {

    // ---------------------------------------------------------------- tabel

    private float PlaceTable(
        LayoutNode node,
        float x,
        float yTop,
        float width,
        PageContext ctx,
        float budget,
        int startRow,
        bool force,
        out Continuation? next,
        bool draw = true)
    {
        var definisi = node.Columns ?? new List<TableColumnDefinition>();
        int jumlahKolom = Math.Max(1, definisi.Count);
        var lebar = ResolveColumnWidths(definisi, Math.Max(0f, width - node.PaddingLeft - node.PaddingRight));

        var barisKepala = BuildRows(node.HeaderCells, jumlahKolom);
        var barisIsi = BuildRows(node.Children, jumlahKolom);

        float left = x + node.PaddingLeft;
        float y = yTop - node.PaddingTop;
        float dipakai = node.PaddingTop;

        // Baris kepala dicetak ulang di setiap halaman, tetapi hanya bila sedikitnya
        // satu baris isi masih muat di bawahnya. Tanpa pemeriksaan ini tabel yang
        // sisanya tidak muat tetap dianggap "tertata" (karena baris kepala dan
        // padding-nya menagih tinggi), halaman tidak pernah berganti, dan baris
        // berikutnya hilang begitu saja — persis kasus daftar hadir dengan peserta
        // banyak yang terpotong di tepi bawah kertas.
        if (startRow < barisIsi.Count && !force)
        {
            float tinggiKepala = 0f;
            foreach (var baris in barisKepala)
            {
                tinggiKepala += MeasureRowHeight(baris, lebar, jumlahKolom, ctx);
            }

            float tinggiBarisPertama = MeasureRowHeight(barisIsi[startRow], lebar, jumlahKolom, ctx);
            if (node.PaddingTop + tinggiKepala + tinggiBarisPertama > budget + Epsilon)
            {
                // Tidak ada yang tertata di halaman ini: minta halaman baru.
                next = new Continuation(node, startRow, null);
                return 0f;
            }
        }

        foreach (var baris in barisKepala)
        {
            float tinggi = MeasureRowHeight(baris, lebar, jumlahKolom, ctx);
            if (draw) DrawRow(baris, lebar, jumlahKolom, left, y, tinggi, ctx);
            y -= tinggi;
            dipakai += tinggi;
        }

        for (int r = startRow; r < barisIsi.Count; r++)
        {
            var baris = barisIsi[r];
            float tinggiBaris = MeasureRowHeight(baris, lebar, jumlahKolom, ctx);

            if (dipakai + tinggiBaris > budget + Epsilon && (r > startRow || !force))
            {
                next = new Continuation(node, r, null);
                return dipakai;
            }

            if (draw) DrawRow(baris, lebar, jumlahKolom, left, y, tinggiBaris, ctx);
            y -= tinggiBaris;
            dipakai += tinggiBaris;
        }

        dipakai += node.PaddingBottom + node.MarginBottom;
        next = null;
        return dipakai;
    }

    private static float[] ResolveColumnWidths(List<TableColumnDefinition> definisi, float innerWidth)
    {
        var lebar = new float[definisi.Count];
        float tetap = 0f;
        float bobot = 0f;
        for (int i = 0; i < definisi.Count; i++)
        {
            if (definisi[i].Constant is { } nilai)
            {
                lebar[i] = nilai;
                tetap += nilai;
            }
            else
            {
                bobot += definisi[i].Weight;
            }
        }

        float sisa = Math.Max(0f, innerWidth - tetap);
        for (int i = 0; i < definisi.Count; i++)
        {
            if (definisi[i].Constant is null)
            {
                lebar[i] = bobot <= 0f ? 0f : sisa * definisi[i].Weight / bobot;
            }
        }

        return lebar;
    }

    /// <summary>Kelompokkan sel menjadi baris menurut jumlah kolom yang digabung.</summary>
    private static List<List<(LayoutNode Cell, int Column, int Span)>> BuildRows(List<LayoutNode>? cells, int jumlahKolom)
    {
        var baris = new List<List<(LayoutNode, int, int)>>();
        if (cells is null || cells.Count == 0) return baris;

        var barisSekarang = new List<(LayoutNode, int, int)>();
        int kolom = 0;
        foreach (var cell in cells)
        {
            int span = (int)Math.Clamp(cell.ColumnSpan, 1, (uint)jumlahKolom);
            if (kolom + span > jumlahKolom && barisSekarang.Count > 0)
            {
                baris.Add(barisSekarang);
                barisSekarang = new List<(LayoutNode, int, int)>();
                kolom = 0;
            }

            barisSekarang.Add((cell, kolom, span));
            kolom += span;
            if (kolom >= jumlahKolom)
            {
                baris.Add(barisSekarang);
                barisSekarang = new List<(LayoutNode, int, int)>();
                kolom = 0;
            }
        }

        if (barisSekarang.Count > 0) baris.Add(barisSekarang);
        return baris;
    }

    private float MeasureRowHeight(List<(LayoutNode Cell, int Column, int Span)> baris, float[] lebar, int jumlahKolom, PageContext ctx)
    {
        float tinggi = 0f;
        foreach (var (cell, kolom, span) in baris)
        {
            float lebarSel = ColumnSpanWidth(lebar, kolom, span, jumlahKolom);
            float tinggiSel = MeasureNodeHeight(cell, lebarSel, ctx);
            if (cell.MinHeight is { } minimal) tinggiSel = Math.Max(tinggiSel, minimal);
            if (tinggiSel > tinggi) tinggi = tinggiSel;
        }

        return tinggi;
    }

    private void DrawRow(
        List<(LayoutNode Cell, int Column, int Span)> baris,
        float[] lebar,
        int jumlahKolom,
        float left,
        float yTop,
        float rowHeight,
        PageContext ctx)
    {
        float x = left;
        foreach (var (cell, kolom, span) in baris)
        {
            float lebarSel = ColumnSpanWidth(lebar, kolom, span, jumlahKolom);
            PlaceNoSplit(cell, x, yTop, lebarSel, ctx, draw: true, rowHeight);
            x += lebarSel;
        }
    }

    private static float ColumnSpanWidth(float[] lebar, int kolom, int span, int jumlahKolom)
    {
        float total = 0f;
        for (int i = kolom; i < kolom + span && i < jumlahKolom; i++) total += lebar[i];
        return total;
    }

    }
