// TemplateSuratWordImporService.cs
// Membaca berkas Word (.docx) lalu menyusunnya menjadi satu atau beberapa
// Template Surat (menu Template Surat).
//
// Berkas dibaca tanpa library tambahan: .docx adalah arsip ZIP berisi
// word/document.xml, sehingga teks, perataan, tebal/miring, dan batas halaman
// (page break / section break) dapat diambil langsung dari XML-nya.
//
// Berkas .doc (format Word 97-2003) dikonversi otomatis lebih dulu menjadi .docx
// memakai Microsoft Word yang terpasang (WordDocConverter), sehingga pengguna tidak
// perlu menyimpan ulang berkasnya secara manual.
//
// Satu halaman/bagian pada berkas dianggap satu bentuk surat. Berkas yang berisi
// beberapa blanko sekaligus tetap bisa disimpan sebagai beberapa template, dan
// bagian yang salah terpisah dapat diabaikan pengguna lewat dialog pemilihan.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;

namespace SuDesApp.Services
{
    public partial class TemplateSuratWordImporService : ITemplateSuratWordImpor
    {
        // =====================================================================
        // Bantuan nama
        // =====================================================================

        /// <summary>
        /// Nama template yang belum dipakai: \"Surat Keterangan\" → \"Surat Keterangan 2\"
        /// bila namanya sudah ada pada daftar template pengguna.
        /// </summary>
        public static string NamaUnik(string? dasar, IEnumerable<string>? terpakai)
        {
            string bersih = (dasar ?? string.Empty).Trim();
            if (bersih.Length == 0) bersih = "Template dari berkas Word";
            if (bersih.Length > 120) bersih = bersih.Substring(0, 120).Trim();

            var dipakai = terpakai == null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(terpakai
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(n => n.Trim()), StringComparer.OrdinalIgnoreCase);

            if (!dipakai.Contains(bersih)) return bersih;

            for (int i = 2; i < 1000; i++)
            {
                string kandidat = $"{bersih} {i}";
                if (!dipakai.Contains(kandidat)) return kandidat;
            }
            return bersih;
        }

    }
}
