using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SudesExPlus;
using SudesExPlus.Style;
using SuDesApp.Configuration;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Views;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class RegisterSuratViewModel : ObservableObject
    {
        private async Task ExportDatabaseAsync()
        {
            // Kunci ekspor Excel saat trial habis — hanya baca, tidak keluarkan berkas.
            if (_penjaga is { EksporTerkunci: true })
            {
                await _messageService.ShowInfoAsync(
                    SuDesApp.Services.PenjagaLisensi.PesanEksporTerkunci);
                return;
            }

            try
            {
                var sfd = new SaveFileDialog
                {
                    Title = $"Ekspor Data {PageTitle}",
                    Filter = "Microsoft Excel (*.xlsx)|*.xlsx|CSV UTF-8 (*.csv)|*.csv|JSON Data (*.json)|*.json",
                    FileName = $"{RegisterFileBaseName}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
                    DefaultExt = ".xlsx"
                };

                if (sfd.ShowDialog() != true) return;

                IsLoading = true;
                var filter = BuildFilterConditions();
                var exportData = (await _unitOfWork.SuratRepository.GetFilteredAsync(filter, "TanggalSurat", false, 0, 50000))?.ToList() ?? new List<SuratData>();

                if (!exportData.Any())
                {
                    Pesan.Info("Tidak ada data untuk diekspor",
                        "Tidak ada surat yang sesuai filter sekarang, jadi berkas ekspor tidak dibuat.",
                        "ekspor-register");
                    return;
                }

                var extension = Path.GetExtension(sfd.FileName).ToLowerInvariant();

                if (extension == ".xlsx")
                {
                    using var package = new ExcelPackage();
                    var ws = package.Workbook.Worksheets.Add(PageTitle);

                    string[] headers = { "No", "ID Surat", "Tanggal Surat", "Nomor Surat", "Nama Pemohon / Instansi", "TTL", "JK", "Alamat", "Keperluan", "Jenis Surat", "Status", "Waktu Dibuat" };
                    for (int col = 0; col < headers.Length; col++)
                    {
                        var cell = ws.Cells[1, col + 1];
                        cell.Value = headers[col];
                        cell.Style.Font.Bold = true;
                        cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                        cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(37, 99, 235));
                        cell.Style.Font.Color.SetColor(System.Drawing.Color.White);
                        cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                        cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
                    }
                    ws.Row(1).Height = 26;

                    int rowIdx = 2;
                    int num = 1;
                    foreach (var s in exportData)
                    {
                        ws.Cells[rowIdx, 1].Value = num++;
                        ws.Cells[rowIdx, 2].Value = s.ID_Surat;
                        ws.Cells[rowIdx, 3].Value = s.TanggalSurat.ToString("dd-MM-yyyy");
                        ws.Cells[rowIdx, 4].Value = s.NomorSurat ?? "-";
                        ws.Cells[rowIdx, 5].Value = GetNamaDisplay(s);
                        ws.Cells[rowIdx, 6].Value = GetTTLDisplay(s);
                        ws.Cells[rowIdx, 7].Value = GetJenisKelaminDisplay(s);
                        ws.Cells[rowIdx, 8].Value = GetAlamatDisplay(s);
                        ws.Cells[rowIdx, 9].Value = s.Keperluan ?? "-";
                        ws.Cells[rowIdx, 10].Value = s.NamaJenis ?? "-";
                        ws.Cells[rowIdx, 11].Value = GetStatusDisplayName(s.Status ?? "Draft");
                        ws.Cells[rowIdx, 12].Value = s.CreatedAt.ToString("dd-MM-yyyy HH:mm");

                        if (rowIdx % 2 == 1)
                        {
                            ws.Cells[rowIdx, 1, rowIdx, headers.Length].Style.Fill.PatternType = ExcelFillStyle.Solid;
                            ws.Cells[rowIdx, 1, rowIdx, headers.Length].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(248, 250, 252));
                        }

                        rowIdx++;
                    }

                    ws.Cells[1, 1, rowIdx - 1, headers.Length].AutoFitColumns(10, 45);
                    await package.SaveAsAsync(new FileInfo(sfd.FileName));
                }
                else if (extension == ".json")
                {
                    var jsonItems = exportData.Select((s, i) => new
                    {
                        No = i + 1,
                        s.ID_Surat,
                        TanggalSurat = s.TanggalSurat.ToString("yyyy-MM-dd"),
                        s.NomorSurat,
                        NamaLengkap = GetNamaDisplay(s),
                        TTL = GetTTLDisplay(s),
                        JenisKelamin = GetJenisKelaminDisplay(s),
                        Alamat = GetAlamatDisplay(s),
                        s.Keperluan,
                        s.NamaJenis,
                        Status = GetStatusDisplayName(s.Status ?? "Draft"),
                        CreatedAt = s.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
                    });
                    var jsonStr = System.Text.Json.JsonSerializer.Serialize(jsonItems, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(sfd.FileName, jsonStr);
                }
                else
                {
                    using var sw = new StreamWriter(sfd.FileName, false, System.Text.Encoding.UTF8);
                    await sw.WriteLineAsync("No,ID_Surat,TanggalSurat,NomorSurat,NamaLengkap,TTL,JK,Alamat,Keperluan,JenisSurat,Status,CreatedAt");
                    int i = 1;
                    foreach (var s in exportData)
                    {
                        string Escape(string? val) => $"\"{(val ?? "-").Replace("\"", "\"\"")}\"";
                        await sw.WriteLineAsync(string.Join(",",
                            i++,
                            s.ID_Surat,
                            Escape(s.TanggalSurat.ToString("dd-MM-yyyy")),
                            Escape(s.NomorSurat),
                            Escape(GetNamaDisplay(s)),
                            Escape(GetTTLDisplay(s)),
                            Escape(GetJenisKelaminDisplay(s)),
                            Escape(GetAlamatDisplay(s)),
                            Escape(s.Keperluan),
                            Escape(s.NamaJenis),
                            Escape(GetStatusDisplayName(s.Status ?? "Draft")),
                            Escape(s.CreatedAt.ToString("dd-MM-yyyy HH:mm"))));
                    }
                }

                Pesan.Sukses("Register diekspor",
                    $"Berhasil mengekspor {exportData.Count} surat ke berkas:\n{sfd.FileName}",
                    "ekspor-register");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengekspor register surat");
                Pesan.Galat("Gagal mengekspor register",
                    $"Berkas ekspor tidak jadi dibuat. Penyebabnya: {ex.Message}",
                    "ekspor-register");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task ShowStatisticsAsync()
        {
            try
            {
                var stats = await _unitOfWork.SuratRepository.GetDatabaseStatsAsync();
                if (stats == null || stats.Count == 0)
                {
                    Pesan.Info("Belum ada statistik",
                        "Database surat masih kosong, jadi belum ada angka yang bisa ditampilkan.",
                        "statistik");
                    return;
                }

                var lines = stats.Select(kvp => $"{kvp.Key}: {kvp.Value}");
                Pesan.Info("Statistik database surat",
                    string.Join(Environment.NewLine, lines), "statistik");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to show statistics");
                Pesan.Galat("Gagal memuat statistik",
                    $"Angka statistik tidak dapat dibaca. Penyebabnya: {ex.Message}", "statistik");
            }
        }

        private async Task PrintRegisterAsync()
        {
            if (_penjaga is { CetakTerkunci: true })
            {
                await _messageService.ShowInfoAsync(
                    SuDesApp.Services.PenjagaLisensi.PesanCetakTerkunci);
                return;
            }

            try
            {
                IsLoading = true;
                var filter = BuildFilterConditions();
                var allFilteredData = await _unitOfWork.SuratRepository.GetFilteredAsync(filter, "TanggalSurat", false, 0, 2000);
                var suratList = allFilteredData?.ToList() ?? new List<SuratData>();
                if (!suratList.Any())
                {
                    Pesan.Info("Tidak ada data untuk dicetak",
                        "Tidak ada surat yang cocok dengan filter sekarang, jadi buku register tidak dibuat.",
                        "cetak-register");
                    return;
                }

                var generator = _serviceProvider.GetRequiredService<SuDesApp.GeneratorPdf.SuratRegisterGenerator>();
                using var penampung = new MemoryStream();
                await generator.GenerateRegisterPdfAsync(penampung, suratList);
                string tempPdfPath = await PratinjauPdf
                    .SimpanAsync(_appConfig, penampung.ToArray(), RegisterFileBaseName)
                    .ConfigureAwait(true);

                // Cetak menjadi PDF: navigasi ke halaman pratinjau PDF agar pengguna
                // dapat menyimpan (Ekspor PDF) atau mencetaknya dari pratinjau.
                var spPreview = _navigation.BukaHalaman();
                var vmFactory = spPreview.GetRequiredService<Func<string, string, int?, PdfPreviewViewModel>>();
                var previewVm = vmFactory($"Buku {PageTitle} ({suratList.Count} Dokumen)", tempPdfPath, null);
                _navigation.Navigate(new PdfPreviewView { DataContext = previewVm });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to print register");
                Pesan.Galat("Gagal mencetak register",
                    $"Buku register tidak dapat dibuat. Penyebabnya: {ex.Message}", "cetak-register");
            }
            finally
            {
                IsLoading = false;
            }
        }


    }
}
