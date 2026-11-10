using System;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SudesExPlus;
using SudesExPlus.Style;
using System.Drawing;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.GeneratorPdf;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class WargaViewModel : ObservableObject
    {
        // =====================================================================
        // Ekspor
        // =====================================================================

        private async Task EksporAsync()
        {
            if (_penjaga is { EksporTerkunci: true })
            {
                await _messageService.ShowInfoAsync(PenjagaLisensi.PesanEksporTerkunci);
                return;
            }

            var sfd = new SaveFileDialog
            {
                Title = "Ekspor Data Warga",
                Filter = "Microsoft Excel (*.xlsx)|*.xlsx|CSV UTF-8 (*.csv)|*.csv|JSON Data (*.json)|*.json",
                FileName = $"Data_Warga_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
                DefaultExt = ".xlsx"
            };

            if (sfd.ShowDialog() != true) return;

            IsBusy = true;
            try
            {
                var data = await _warga.GetWargaForExportAsync(SusunFilter());
                if (data.Count == 0)
                {
                    await _messageService.ShowInfoAsync("Tidak ada warga yang sesuai filter, jadi berkas ekspor tidak dibuat.");
                    return;
                }

                var jumlahSurat = await _warga.GetJumlahSuratSemuaWargaAsync();

                var extension = Path.GetExtension(sfd.FileName).ToLowerInvariant();
                switch (extension)
                {
                    case ".csv": await TulisCsvAsync(data, jumlahSurat, sfd.FileName); break;
                    case ".json": await TulisJsonAsync(data, jumlahSurat, sfd.FileName); break;
                    default: await TulisExcelAsync(data, jumlahSurat, sfd.FileName); break;
                }

                _activityLog.Log("WARGA", $"{data.Count} baris", "Ekspor", sfd.FileName);
                StatusText = $"{data.Count} warga diekspor ke {Path.GetFileName(sfd.FileName)}.";
                await _messageService.ShowInfoAsync($"Berhasil mengekspor {data.Count} baris warga.\n\n{sfd.FileName}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengekspor data warga");
                await _messageService.ShowErrorAsync("Gagal mengekspor data warga.\n\n" + ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private static readonly string[] HeaderEkspor =
        {
            "No", "NIK", "Nama", "No Kartu Keluarga", "Jenis Kelamin", "Tempat Lahir",
            "Tanggal Lahir", "Agama", "Golongan Darah", "Status Perkawinan", "Pekerjaan",
            "Pendidikan", "Nama Ayah", "Nama Ibu", "Nomor HP", "RT", "RW", "Dusun",
            "Alamat (jalan/kampung)", "Desa", "Kecamatan", "Kabupaten", "Status Warga",
            "Tanggal Status", "Keterangan", "Jumlah Surat"
        };

        /// <summary>
        /// Kolom template impor: judul kolom ekspor yang dikenali layanan impor,
        /// tanpa "No" dan "Jumlah Surat" (dua kolom itu memang diabaikan saat impor).
        /// </summary>
        private static readonly string[] HeaderTemplate = HeaderEkspor
            .Where(h => h != "No" && h != "Jumlah Surat")
            .ToArray();

        private static object?[] BarisEkspor(WargaData w, int nomor, Dictionary<int, int> jumlahSurat)
        {
            jumlahSurat.TryGetValue(w.ID_Warga, out var surat);
            return new object?[]
            {
                nomor, w.NIK, w.Nama, w.NoKK, WargaBaris.NormalisasiJenisKelamin(w.JenisKelamin),
                w.TempatLahir, WargaBaris.FormatTanggalPendek(w.TanggalLahir), w.Agama, w.GolonganDarah,
                w.StatusPerkawinan, w.Pekerjaan, w.Pendidikan, w.NamaAyah, w.NamaIbu, w.NomorHP,
                w.RT, w.RW, w.Dusun, w.AlamatDetail, w.Desa, w.Kecamatan, w.Kabupaten,
                StatusWargaTipe.Normalisasi(w.StatusWarga), w.TanggalStatus, w.KeteranganWarga, surat
            };
        }

        private static async Task TulisExcelAsync(List<WargaData> data, Dictionary<int, int> jumlahSurat, string path)
        {
            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Data Warga");

            for (int col = 0; col < HeaderEkspor.Length; col++)
            {
                var cell = ws.Cells[1, col + 1];
                cell.Value = HeaderEkspor[col];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                cell.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(37, 99, 235));
                cell.Style.Font.Color.SetColor(Color.White);
                cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            }
            ws.Row(1).Height = 26;

            int rowIdx = 2;
            int nomor = 1;
            foreach (var w in data)
            {
                var isi = BarisEkspor(w, nomor++, jumlahSurat);
                for (int c = 0; c < isi.Length; c++)
                {
                    ws.Cells[rowIdx, c + 1].Value = isi[c];
                }

                if (rowIdx % 2 == 1)
                {
                    ws.Cells[rowIdx, 1, rowIdx, HeaderEkspor.Length].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    ws.Cells[rowIdx, 1, rowIdx, HeaderEkspor.Length].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(248, 250, 252));
                }

                rowIdx++;
            }

            ws.Cells[1, 1, rowIdx - 1, HeaderEkspor.Length].AutoFitColumns(10, 45);
            ws.Cells[1, 1, rowIdx - 1, HeaderEkspor.Length].Style.Border.BorderAround(ExcelBorderStyle.Thin);
            await package.SaveAsAsync(new FileInfo(path));
        }

        private static async Task TulisCsvAsync(List<WargaData> data, Dictionary<int, int> jumlahSurat, string path)
        {
            // BOM UTF-8: tanpa itu Excel di Windows salah membaca karakter Indonesia.
            await using var writer = new StreamWriter(path, false, new System.Text.UTF8Encoding(true));
            await writer.WriteLineAsync(string.Join(",", HeaderEkspor.Select(KutipCsv)));
            int nomor = 1;
            foreach (var w in data)
            {
                var isi = BarisEkspor(w, nomor++, jumlahSurat);
                await writer.WriteLineAsync(string.Join(",", isi.Select(v => KutipCsv(Convert.ToString(v) ?? string.Empty))));
            }
        }

        private static async Task TulisJsonAsync(List<WargaData> data, Dictionary<int, int> jumlahSurat, string path)
        {
            var items = data.Select((w, i) => new
            {
                No = i + 1,
                w.NIK,
                w.Nama,
                NoKartuKeluarga = w.NoKK,
                JenisKelamin = WargaBaris.NormalisasiJenisKelamin(w.JenisKelamin),
                w.TempatLahir,
                w.TanggalLahir,
                w.Agama,
                w.GolonganDarah,
                w.StatusPerkawinan,
                w.Pekerjaan,
                w.Pendidikan,
                w.NamaAyah,
                w.NamaIbu,
                w.NomorHP,
                w.RT,
                w.RW,
                w.Dusun,
                w.AlamatDetail,
                w.Desa,
                w.Kecamatan,
                w.Kabupaten,
                StatusWarga = StatusWargaTipe.Normalisasi(w.StatusWarga),
                w.TanggalStatus,
                w.KeteranganWarga,
                JumlahSurat = jumlahSurat.TryGetValue(w.ID_Warga, out var surat) ? surat : 0
            });
            await File.WriteAllTextAsync(path,
                System.Text.Json.JsonSerializer.Serialize(items, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }

        /// <summary>Bungkus nilai CSV bila mengandung koma, kutip, atau baris baru.</summary>
        private static string KutipCsv(string nilai)
        {
            if (string.IsNullOrEmpty(nilai)) return string.Empty;
            var perlu = nilai.Contains(',') || nilai.Contains('"') || nilai.Contains('\n') || nilai.Contains('\r');
            if (!perlu) return nilai;
            return "\"" + nilai.Replace("\"", "\"\"") + "\"";
        }


    }
}
