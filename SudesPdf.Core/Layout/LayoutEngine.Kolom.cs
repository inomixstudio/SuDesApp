using System;
using System.Collections.Generic;
using System.Globalization;

namespace SudesPdf.Core.Layout;

internal sealed partial class LayoutEngine
    {

    // ----------------------------------------------------------------- kolom

    private float PlaceColumn(
        LayoutNode node,
        float x,
        float yTop,
        float width,
        PageContext ctx,
        float budget,
        int startIndex,
        Continuation? inner,
        bool force,
        out Continuation? next,
        bool draw = true)
    {
        float innerWidth = Math.Max(0f, width - node.MarginLeft - node.MarginRight - node.PaddingLeft - node.PaddingRight);
        float left = x + node.MarginLeft + node.PaddingLeft;
        float y = yTop - node.MarginTop - node.PaddingTop;
        float dipakai = node.MarginTop + node.PaddingTop;
        float dipakaiAwal = dipakai;

        // Anak yang terpotong halaman sebelumnya dilanjutkan lebih dulu.
        bool sedangLanjutan = inner is not null;
        int index = startIndex;
        LayoutNode? child = sedangLanjutan
            ? inner!.Node
            : (index < node.Children.Count ? node.Children[index] : null);

        while (child is not null)
        {
            if (!sedangLanjutan && child.Kind == LayoutKind.PageBreak)
            {
                if (index > startIndex)
                {
                    // Tandai sebagai pemisah halaman supaya PlacePage memulai
                    // lembar baru, bukan menaruh isi berikutnya di sisa ruang.
                    next = new Continuation(node, index + 1, null, hentikanHalaman: true);
                    return dipakai + node.PaddingBottom;
                }

                // Pemisah halaman di awal halaman: tidak perlu apa-apa lagi.
                index++;
                child = index < node.Children.Count ? node.Children[index] : null;
                continue;
            }

            var childState = sedangLanjutan ? inner! : new Continuation(child, 0, null);
            // Izin "cetak walau melebihi halaman" hanya untuk anak PERTAMA pada
            // halaman baru yang belum memakai ruang apa pun. Dulu syaratnya cuma
            // index == startIndex; saat melanjutkan tabel, index tidak bertambah,
            // sehingga blok tanda tangan SESUDAH tabel ikut dipaksa cetak di sisa
            // ruang beberapa titik dan terpotong di tepi bawah kertas.
            bool paksaAnak = !sedangLanjutan && force && index == startIndex
                && dipakai <= dipakaiAwal + Epsilon;
            float tinggiAnak = PlaceNode(child, left, y, budget - dipakai, innerWidth, ctx, childState, out var lanjutan, paksaAnak, draw);
            dipakai += tinggiAnak;
            y -= tinggiAnak;

            if (lanjutan is not null)
            {
                next = new Continuation(node, sedangLanjutan ? index : index + 1, lanjutan);
                return dipakai;
            }

            if (!sedangLanjutan) index++;
            sedangLanjutan = false;

            if (index < node.Children.Count)
            {
                dipakai += node.Spacing;
                y -= node.Spacing;
            }

            child = index < node.Children.Count ? node.Children[index] : null;
        }

        dipakai += node.PaddingBottom + node.MarginBottom;
        next = null;
        return dipakai;
    }

    }
