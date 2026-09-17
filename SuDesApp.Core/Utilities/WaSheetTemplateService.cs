using Microsoft.Extensions.Logging;
using OfficeOpenXml;
using SuDesApp.Data.Models;
using SuDesApp.WhatsApp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SuDesApp.Utilities
{
    /// <summary>
    /// Generator template Google Sheet untuk mode layanan online (jawaban form).
    /// Membuat berkas .xlsx per jenis surat yang berisi:
    ///   • baris header = kolom yang PERSIS dipahami poller aplikasi (label sama
    ///     dengan form generator surat — lihat WaFormatParser.FieldDefs),
    ///   • 2 baris CONTOH (tertulis, dengan nilai contoh per kolom),
    ///   • validasi dropdown "Jenis Surat" terisi daftar jenis yang didukung.
    /// Operator tinggal mengunggah berkas ini ke Google Drive → "Open with →
    /// Google Sheets", atau memakai tombol unggah otomatis di aplikasi.
    /// </summary>
    public static class WaSheetTemplateService
    {
        /// <summary>Kolom yang selalu ada di semua template (uratan sama).</summary>
        private static readonly string[] KolomUmum =
        {
            "Timestamp", "Jenis Surat", "NIK", "Nama", "Tempat Lahir", "Tanggal Lahir",
            "JK", "Agama", "Status Perkawinan", "Pekerjaan", "Alamat", "Keperluan",
            "No. WhatsApp", "Token", "Status"
        };

        /// <summary>
        /// Kolom tambahan per jenis surat (di samping kolom umum), mengikuti
        /// form generator masing-masing surat.
        /// </summary>
        private static readonly Dictionary<string, (string NamaTampil, string[] Ekstra)> EkstraPerJenis =
            new(StringComparer.OrdinalIgnoreCase)
            {
                [SuratConstants.SKTM] = ("SKTM", Array.Empty<string>()),
                [SuratConstants.SKD_UMUM] = ("SKD Umum", Array.Empty<string>()),
                [SuratConstants.DOMISILI_WARGA] = ("Domisili Warga", Array.Empty<string>()),
                [SuratConstants.PENGANTAR_SKCK] = ("Pengantar SKCK", new[] { "Pendidikan", "Kewarganegaraan" }),
                [SuratConstants.SKU] = ("SKU (Keterangan Usaha)", new[] { "Bidang Usaha", "Sejak Tahun" }),
                [SuratConstants.IZIN_ORTU] = ("Izin Orang Tua", new[]
                {
                    "NIK Anak", "Nama Anak", "Tempat Lahir Anak", "Tanggal Lahir Anak",
                    "JK Anak", "Agama Anak", "Status Anak", "Pekerjaan Anak", "Alamat Anak",
                    "Negara Tujuan", "Nama PT"
                }),
                [SuratConstants.INSTANSI] = ("Surat Instansi", new[] { "Nama Instansi", "Alamat Instansi" }),
            };

        /// <summary>Daftar nilai dropdown validasi kolom "Jenis Surat".</summary>
        private static readonly string[] DaftarJenis =
            EkstraPerJenis.Values.Select(v => v.NamaTampil).ToArray();

        /// <summary>
        /// Buat berkas template .xlsx untuk satu jenis surat (atau "SEMUA" =
        /// satu berkas berisi satu tab per jenis). Mengembalikan path berkas
        /// sementara yang sudah ditulis.
        /// </summary>
        public static string BuatTemplateFile(string? jenisKey, string folderOutput)
        {
            ExcelPackage.License.SetNonCommercialPersonal("ARIE INO");

            Directory.CreateDirectory(folderOutput);
            var namaFile = string.IsNullOrWhiteSpace(jenisKey) || jenisKey == "SEMUA"
                ? $"Template-Sheet-Surat-Semua_{DateTime.Now:yyyyMMdd-HHmm}.xlsx"
                : $"Template-Sheet-{WaFormatParser.TampilanJenis(jenisKey).Replace(' ', '-')}_{DateTime.Now:yyyyMMdd-HHmm}.xlsx";
            var path = Path.Combine(folderOutput, namaFile);

            using var package = new ExcelPackage();

            if (string.IsNullOrWhiteSpace(jenisKey) || jenisKey == "SEMUA")
            {
                // Satu tab per jenis surat — cukup satu unggahan untuk semuanya.
                foreach (var kvp in EkstraPerJenis)
                    IsiTab(package, jenisKey: kvp.Key, kvp.Value.NamaTampil, kvp.Value.Ekstra);
            }
            else
            {
                if (!EkstraPerJenis.ContainsKey(jenisKey))
                    throw new ArgumentException("Jenis surat tidak didukung untuk template Sheet: " + jenisKey);
                var (namaTampil, ekstra) = EkstraPerJenis[jenisKey];
                IsiTab(package, jenisKey, namaTampil, ekstra);
            }

            package.SaveAs(new FileInfo(path));
            return path;
        }

        /// <summary>Isi satu worksheet dengan header + contoh + dropdown.</summary>
        private static void IsiTab(ExcelPackage package, string jenisKey, string namaTampil, string[] ekstra)
        {
            // Nama tab maks 31 karakter (batas Excel/Sheets).
            var tabName = namaTampil.Length > 31 ? namaTampil[..31] : namaTampil;
            var ws = package.Workbook.Worksheets.Add(tabName);

            // ── Header: kolom umum + ekstra jenis ini ──
            var headers = KolomUmum.Take(KolomUmum.Length - 1)      // semua kecuali "Status"
                .Concat(ekstra)                                      // kolom khusus jenis
                .Concat(new[] { "Status" })                          // Status selalu terakhir
                .ToArray();

            for (int c = 0; c < headers.Length; c++)
            {
                var cell = ws.Cells[1, c + 1];
                cell.Value = headers[c];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(31, 111, 78)); // hijau tua Google Sheets
                cell.Style.Font.Color.SetColor(System.Drawing.Color.White);
            }
            ws.Row(1).Height = 22;

            // ── Baris 2: CONTOH terisi (Status terisi agar poller melewatinya) ──
            IsiBarisContoh(ws, jenisKey, namaTampil, row: 2, headers);
            // ── Baris 3: CONTOH kosong (petunjuk isian per kolom) ──
            IsiBarisContoh(ws, jenisKey, namaTampil, row: 3, headers);

            // ── Dropdown validasi "Jenis Surat" (kolom B, baris data) ──
            var jenisCol = Array.IndexOf(headers, "Jenis Surat") + 1;
            if (jenisCol > 0)
            {
                var list = string.Join(",", DaftarJenis);
                var validation = ws.DataValidations.AddListValidation(
                    ws.Cells[2, jenisCol, 200, jenisCol].Address);
                validation.Formula.Values.Add(list);
            }

            // ── Dropdown ringan untuk kolom bernilai baku (JK, Agama, Status Perkawinan) ──
            TambahDropdown(ws, headers, "JK", "L,P");
            TambahDropdown(ws, headers, "JK Anak", "L,P");
            TambahDropdown(ws, headers, "Agama", "Islam,Kristen,Katolik,Hindu,Buddha,Konghucu");
            TambahDropdown(ws, headers, "Agama Anak", "Islam,Kristen,Katolik,Hindu,Buddha,Konghucu");
            TambahDropdown(ws, headers, "Status Perkawinan", "Belum Kawin,Kawin,Cerai Hidup,Cerai Mati");
            TambahDropdown(ws, headers, "Status Anak", "Belum Kawin,Kawin,Cerai Hidup,Cerai Mati");

            // Lebar kolom wajar + freeze header.
            ws.Cells[ws.Dimension.Address].AutoFitColumns(12, 30);
            ws.View.FreezePanes(2, 1);
        }

        private static void TambahDropdown(ExcelWorksheet ws, string[] headers, string header, string pilihan)
        {
            var col = Array.IndexOf(headers, header) + 1;
            if (col <= 0) return;
            var validation = ws.DataValidations.AddListValidation(
                ws.Cells[2, col, 200, col].Address);
            foreach (var p in pilihan.Split(','))
                validation.Formula.Values.Add(p);
        }

        /// <summary>
        /// Isi baris CONTOH. Baris 2 = contoh terisi penuh (data fiktif jelas);
        /// baris 3 = petunjuk per kolom. Keduanya berisi Status agar tidak
        /// pernah diproses poller.
        /// </summary>
        private static void IsiBarisContoh(ExcelWorksheet ws, string jenisKey, string namaTampil, int row, string[] headers)
        {
            string[] umum = row == 2
                ? new[]
                {
                    DateTime.Now.ToString("dd-MM-yyyy HH:mm"),
                    namaTampil,
                    "3273010101010001",
                    "[CONTOH] Budi Santoso",
                    "[CONTOH] Karawang",
                    "01-01-1990",
                    "L",
                    "Islam",
                    "Kawin",
                    "[CONTOH] Petani",
                    "[CONTOH] Dusun I RT 001/RW 001, Desa …, Kec. …, Kab. …",
                    "Untuk keperluan [sebutkan kebutuhan surat]",
                    "6281234567890",
                    "(kosongkan)"
                }
                : new[]
                {
                    "(otomatis oleh Google Form)",
                    "pilih: " + namaTampil,
                    "16 digit NIK",
                    "Nama lengkap",
                    "tempat lahir",
                    "tanggal lahir dd-mm-yyyy",
                    "L / P",
                    "agama",
                    "status perkawinan",
                    "pekerjaan",
                    "alamat lengkap (dusun, RT/RW, desa, kec, kab)",
                    "alasan / kebutuhan surat",
                    "62xxx (No. WhatsApp pengirim tautan)",
                    "(kosongkan)"
                };

            for (int c = 0; c < umum.Length && c < headers.Length; c++)
                ws.Cells[row, c + 1].Value = umum[c];

            // Kolom ekstra per jenis.
            var ekstraMap = jenisKey == SuratConstants.SKU
                ? new[] { "[CONTOH] Warung Kelontong", "2015" }
                : jenisKey == SuratConstants.PENGANTAR_SKCK
                    ? new[] { "SMA", "WNI" }
                    : jenisKey == SuratConstants.IZIN_ORTU
                        ? new[]
                        {
                            "3273010101010002", "[CONTOH] Anak Contoh", "[CONTOH] Karawang", "01-01-2010",
                            "P", "Islam", "Belum Kawin", "Pelajar", "[CONTOH] alamat anak", "Malaysia", "[NAMA PT/PERUSAHAAN]"
                        }
                        : jenisKey == SuratConstants.INSTANSI
                            ? new[] { "[CONTOH] Nama Instansi", "[CONTOH] alamat instansi" }
                            : Array.Empty<string>();

            var idxUmum = KolomUmum.Length - 1; // jumlah kolom sebelum ekstra (Timestamp..Token)
            for (int i = 0; i < ekstraMap.Length; i++)
            {
                var val = row == 2 ? ekstraMap[i] : ekstraMap[i].StartsWith('[') ? ekstraMap[i].Trim('[', ']') : ekstraMap[i];
                ws.Cells[row, idxUmum + i + 1].Value = val;
            }

            // Status: selalu terisi di baris contoh → poller melewati baris ini.
            ws.Cells[row, headers.Length].Value =
                row == 2 ? "CONTOH — hapus baris ini sebelum dipakai" : "CONTOH";
            ws.Cells[row, 1, row, headers.Length].Style.Font.Italic = true;
            ws.Cells[row, 1, row, headers.Length].Style.Font.Color.SetColor(System.Drawing.Color.Gray);
        }
    }
}
