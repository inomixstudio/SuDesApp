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
        // Pembacaan berkas
        // =====================================================================

        public async Task<WordSuratImpor> BacaAsync(string berkas, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(berkas))
                throw new ArgumentException("Berkas Word belum dipilih.", nameof(berkas));

            string namaBerkas = Path.GetFileName(berkas);
            var hasil = new WordSuratImpor { NamaBerkas = namaBerkas };

            if (!File.Exists(berkas))
                throw new FileNotFoundException("Berkas Word tidak ditemukan.", berkas);

            string ekstensi = Path.GetExtension(berkas).ToUpperInvariant();
            if (ekstensi != ".DOC" && ekstensi != ".DOCX")
            {
                throw new NotSupportedException(
                    "Hanya berkas Word (.docx, atau .doc lama) yang dapat dijadikan template surat.");
            }

            var info = new FileInfo(berkas);
            if (info.Length > UkuranBerkasMaks)
            {
                throw new InvalidOperationException(
                    $"Berkas terlalu besar (lebih dari {UkuranBerkasMaks / (1024 * 1024)} MB). " +
                    "Pisahkan suratnya menjadi beberapa berkas yang lebih kecil.");
            }

            // Berkas .doc lama dibaca lewat konversi otomatis (Microsoft Word) lebih dulu.
            var paragraf = ekstensi == ".DOC"
                ? await BacaDocLamaAsync(berkas, hasil, ct).ConfigureAwait(false)
                : await BacaDocxAsync(berkas, hasil, ct).ConfigureAwait(false);

            if (paragraf.Count == 0)
            {
                hasil.Peringatan.Add("Tidak ada teks yang dapat dibaca dari berkas ini.");
                return hasil;
            }

            bool adaBatasKeras = paragraf.Any(p => p.BatasKerasSetelah || p.Potongan.Any(x => x.BatasKeras));

            var bagianSurat = BagiBagian(paragraf, pakaiBatasLunak: !adaBatasKeras);
            if (!adaBatasKeras)
            {
                hasil.Peringatan.Add(
                    "Batas halaman diperkirakan dari tata letak terakhir berkas. Periksa hasil " +
                    "pemisahan surat sebelum menyimpan.");
            }

            if (bagianSurat.Count == 0)
            {
                hasil.Peringatan.Add("Seluruh isi berkas kosong sehingga tidak ada surat yang bisa dibuat.");
                return hasil;
            }

            if (bagianSurat.Count > MaksBagian)
            {
                hasil.Peringatan.Add(
                    $"Berkas berisi {bagianSurat.Count} bagian; hanya {MaksBagian} bagian pertama yang diproses.");
                bagianSurat = bagianSurat.Take(MaksBagian).ToList();
            }

            int nomor = 0;
            bool adaTandaTanganOtomatis = false;
            foreach (var isi in bagianSurat)
            {
                nomor++;
                var kandidat = SusunKandidat(isi, nomor, namaBerkas);
                if (kandidat == null) continue;

                hasil.Kandidat.Add(kandidat);
                adaTandaTanganOtomatis |= kandidat.Definisi.PakaiTandaTangan;
            }

            if (adaTandaTanganOtomatis)
            {
                hasil.Peringatan.Add(
                    "Blok tanda tangan pada berkas dikenali dan diganti tanda tangan otomatis aplikasi " +
                    "(jabatan + nama pejabat desa dari Setelan Aplikasi).");
            }

            _logger.LogInformation(
                "Berkas Word {Berkas} dibaca: {Bagian} bagian surat, {Kolom} kolom isian.",
                namaBerkas, hasil.Kandidat.Count, hasil.Kandidat.Sum(k => k.JumlahKolom));

            return hasil;
        }

        /// <summary>
        /// Baca berkas .doc (format Word 97-2003). Berkas dikonversi lebih dulu menjadi
        /// .docx memakai Microsoft Word, lalu dibaca seperti berkas .docx supaya susunan,
        /// tabel, dan batas halamannya tetap terbawa. Berkas sementara selalu dihapus.
        /// </summary>
        private async Task<List<ParagrafMentah>> BacaDocLamaAsync(
            string berkas, WordSuratImpor hasil, CancellationToken ct)
        {
            if (_konverterDoc == null || !_konverterDoc.Tersedia)
            {
                throw new NotSupportedException(WordDocConverter.PesanTanpaWord);
            }

            string? sementara = null;
            try
            {
                sementara = await _konverterDoc.KeDocxAsync(berkas, ct).ConfigureAwait(false);
                hasil.Peringatan.Add(
                    "Berkas .doc lama dikonversi otomatis ke .docx, lalu dibaca. Periksa hasil pemisahan " +
                    "surat sebelum menyimpan.");

                return await BacaDocxAsync(sementara, hasil, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not NotSupportedException && ex is not OperationCanceledException)
            {
                throw new InvalidOperationException(
                    $"Berkas .doc \"{Path.GetFileName(berkas)}\" gagal dikonversi otomatis: {ex.Message}", ex);
            }
            finally
            {
                if (!string.IsNullOrEmpty(sementara))
                {
                    try { File.Delete(sementara); }
                    catch { /* berkas sementara gagal dihapus; dibiarkan agar proses tetap jalan */ }
                }
            }
        }

        /// <summary>Baca word/document.xml dari arsip .docx.</summary>
        private async Task<List<ParagrafMentah>> BacaDocxAsync(
            string berkas, WordSuratImpor hasil, CancellationToken ct)
        {
            ZipArchive arsip;
            try
            {
                arsip = ZipFile.OpenRead(berkas);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            {
                throw new InvalidOperationException(
                    "Berkas tidak dapat dibuka sebagai dokumen Word (.docx). Pastikan berkasnya utuh " +
                    "dan bukan dokumen .doc lama.", ex);
            }

            using (arsip)
            {
                var entri = arsip.Entries.FirstOrDefault(e =>
                    e.FullName.Equals("word/document.xml", StringComparison.OrdinalIgnoreCase));
                if (entri == null)
                {
                    throw new InvalidOperationException(
                        "Berkas ini tidak memuat isi dokumen Word (word/document.xml).");
                }

                string xml = await BacaTeksEntriAsync(entri, ct).ConfigureAwait(false);

                XDocument dokumen;
                try
                {
                    dokumen = XDocument.Parse(xml);
                }
                catch (System.Xml.XmlException ex)
                {
                    throw new InvalidOperationException(
                        "Isi berkas Word tidak dapat dibaca karena formatnya rusak.", ex);
                }

                var body = dokumen.Root?.Element(W + "body");
                if (body == null) return new List<ParagrafMentah>();

                bool adaTabel = body.Elements(W + "tbl").Any();
                if (adaTabel)
                {
                    hasil.Peringatan.Add("Tabel pada berkas diubah menjadi baris teks biasa.");
                }

                return BacaParagraf(body);
            }
        }

        private static async Task<string> BacaTeksEntriAsync(ZipArchiveEntry entri, CancellationToken ct)
        {
            if (entri.Length > UkuranXmlMaks)
            {
                throw new InvalidOperationException(
                    $"Isi dokumen terlalu besar untuk dibaca (lebih dari {UkuranXmlMaks / (1024 * 1024)} MB).");
            }

            var sb = new StringBuilder();
            var penyangga = new char[8192];

            using var aliran = entri.Open();
            using var pembaca = new StreamReader(aliran, Encoding.UTF8);
            int dibaca;
            while ((dibaca = await pembaca.ReadAsync(penyangga, ct).ConfigureAwait(false)) > 0)
            {
                if (sb.Length + dibaca > UkuranXmlMaks)
                {
                    throw new InvalidOperationException(
                        $"Isi dokumen terlalu besar untuk dibaca (lebih dari {UkuranXmlMaks / (1024 * 1024)} MB).");
                }
                sb.Append(penyangga, 0, dibaca);
            }

            return sb.ToString();
        }


    }
}
