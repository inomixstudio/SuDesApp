using System;
using System.Collections.Generic;
using System.Linq;

namespace SuDesApp.Data.Models
{
    /// <summary>Jenis Surat Keputusan yang disediakan template-nya.</summary>
    public enum SkJenisPerangkat
    {
        Pengangkatan,
        Pemberhentian,

        /// <summary>
        /// Penetapan (bukan pengangkatan) — SK KPM pada contoh dokumen memakai
        /// bentuk ini, misalnya "PENETAPAN KADER PEMBANGUNAN MANUSIA (KPM)".
        /// </summary>
        Penetapan
    }

    /// <summary>

}
