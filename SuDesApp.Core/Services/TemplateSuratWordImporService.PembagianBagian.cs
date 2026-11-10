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
        // Pembagian berkas menjadi bagian surat
        // =====================================================================

        /// <summary>
        /// Bagi paragraf menjadi bagian-bagian surat. Batas yang dipakai adalah batas
        /// halaman/bagian Word; bila berkas tidak punya batas keras, posisi halaman
        /// terakhir yang direkam Word dipakai sebagai perkiraan.
        /// </summary>
        private static List<List<WordSuratParagraf>> BagiBagian(List<ParagrafMentah> paragraf, bool pakaiBatasLunak)
        {
            var hasil = new List<List<WordSuratParagraf>>();
            var sekarang = new List<WordSuratParagraf>();

            void Tutup()
            {
                if (sekarang.Count > 0)
                {
                    hasil.Add(sekarang);
                    sekarang = new List<WordSuratParagraf>();
                }
            }

            foreach (var p in paragraf)
            {
                if (p.Pemisah)
                {
                    Tutup();
                    continue;
                }

                foreach (var potongan in p.Potongan)
                {
                    bool batas = pakaiBatasLunak ? potongan.BatasLunak : potongan.BatasKeras;
                    if (batas) Tutup();

                    if (potongan.Teks.Length > 0)
                    {
                        sekarang.Add(new WordSuratParagraf
                        {
                            Teks = potongan.Teks,
                            Rata = p.Rata,
                            Tebal = p.Tebal,
                            Miring = p.Miring
                        });
                    }
                }

                bool batasSetelah = pakaiBatasLunak ? p.BatasLunakSetelah : p.BatasKerasSetelah;
                if (batasSetelah) Tutup();
            }

            Tutup();

            // Buang bagian yang tidak memuat huruf/angka (sisa batas halaman).
            return hasil
                .Where(bagian => bagian.Any(x => x.Teks.Any(char.IsLetterOrDigit)))
                .ToList();
        }


    }
}
