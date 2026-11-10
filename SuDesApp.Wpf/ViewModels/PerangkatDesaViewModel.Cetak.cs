using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SudesExPlus;
using SudesExPlus.Style;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.GeneratorPdf;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Utilities;

namespace SuDesApp.Wpf.ViewModels
{
/// <summary>
    /// Ekspor Excel dan cetak PDF (daftar bagian aktif + formulir pendataan kosong) beserta penyusunan datanya.
    /// Pemecahan god-class tanpa mengubah perilaku — bagian dari kelas parsial
    /// <see cref="PerangkatDesaViewModel"/>.
    /// </summary>
    public partial class PerangkatDesaViewModel
    {
        // ---------- ekspor & cetak ----------

        /// <summary>
        /// Simpan baris berisi orang pada **bagian yang sedang tampil** ke berkas
        /// Excel. Kolomnya mengikuti tampilan layar; tiap bagian diekspor terpisah
        /// sebab SK tiap kelompok punya cara penerbitan berbeda.
        /// </summary>
        private async Task EksporExcelAsync()
        {
            if (_penjaga is { EksporTerkunci: true })
            {
                await _messageService.ShowInfoAsync(
                    SuDesApp.Services.PenjagaLisensi.PesanEksporTerkunci);
                return;
            }

            // Bagian kosong diperiksa lebih dulu, sebelum operator diminta memilih
            // nama berkas untuk data yang tidak akan ditulis.
            var baris = TabelAktif.Where(b => b.AdaData).ToList();
            if (baris.Count == 0)
            {
                await _messageService.ShowInfoAsync(
                    $"Tidak ada data {NamaBagianAktif} yang bisa diekspor, jadi berkas tidak dibuat.");
                return;
            }

            var sfd = new SaveFileDialog
            {
                Title = $"Ekspor Data {NamaBagianTampil}",
                Filter = "Microsoft Excel (*.xlsx)|*.xlsx",
                FileName = $"Perangkat_Desa{SufiksNamaBerkas}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
                DefaultExt = ".xlsx"
            };

            if (sfd.ShowDialog() != true) return;

            try
            {
                IsBusy = true;
                var desa = await _desaRepo.GetInfoDesaAsync().ConfigureAwait(true);
                await TulisExcelAsync(baris, sfd.FileName, JudulDokumenAktif, desa?.NamaDesa ?? "-")
                    .ConfigureAwait(true);

                _activityLog.Log("PERANGKAT", $"{baris.Count} baris {NamaBagianAktif}", "Ekspor Excel", sfd.FileName);
                StatusText = $"{baris.Count} baris {NamaBagianAktif} diekspor ke {Path.GetFileName(sfd.FileName)}.";
                await _messageService.ShowInfoAsync(
                    $"Berhasil mengekspor {baris.Count} baris data {NamaBagianAktif}.\n\n{sfd.FileName}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengekspor data perangkat desa");
                await _messageService.ShowErrorAsync("Gagal mengekspor data perangkat desa.\n\n" + ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Kolom ekspor mengikuti berkas "DATA PERANGKAT" contoh pemakai persis —
        /// 18 kolom dengan urutan yang sama; nilai kosong dicetak "-" seperti
        /// berkasnya, dan kolom NIK/rekening ditulis sebagai teks.
        /// </summary>
        private static readonly string[] HeaderEkspor =
        {
            "DESA", "NAMA PERANGKAT", "JABATAN", "JENIS KELAMIN", "TEMPAT LAHIR",
            "TGL. LAHIR", "NIK", "AGAMA", "NO. TELPON", "NOMOR REKOMENDASI",
            "TGL. REKOMENDASI", "NO SK PENGANGKATAN", "TGL SK PENGANGKATAN",
            "PENDIDIKAN", "TAHUN LULUS", "NO. IJASAH", "NOREK BJB", "BPJS KES"
        };

        /// <summary>
        /// Nomor berurutan per jabatan berbeda, meniru kolom JABATAN pada berkas
        /// contoh (lima Kepala Dusun sama-sama bernomor "8.", dst.).
        /// </summary>
        private static string JabatanBernomor(string jabatan, Dictionary<string, int> nomor)
        {
            if (!nomor.TryGetValue(jabatan, out int n))
            {
                n = nomor.Count + 1;
                nomor[jabatan] = n;
            }

            return $"{n}. {jabatan}";
        }

        private static async Task TulisExcelAsync(
            IReadOnlyList<BarisPerangkatViewModel> baris, string path, string judulLembar, string namaDesa)
        {
            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add(judulLembar);

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
            var nomorJabatan = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var b in baris)
            {
                ws.Cells[rowIdx, 1].Value = namaDesa;
                ws.Cells[rowIdx, 2].Value = b.Nama;
                ws.Cells[rowIdx, 3].Value = JabatanBernomor(b.JabatanTampil, nomorJabatan);
                ws.Cells[rowIdx, 4].Value = b.JenisKelamin;
                ws.Cells[rowIdx, 5].Value = b.TempatLahir;
                ws.Cells[rowIdx, 6].Value = b.TanggalLahir;
                // NIK sebagai teks supaya angka awal nol tidak hilang di Excel.
                ws.Cells[rowIdx, 7].Value = string.IsNullOrWhiteSpace(b.NIK) ? "-" : b.NIK;
                ws.Cells[rowIdx, 8].Value = b.Agama;
                ws.Cells[rowIdx, 9].Value = string.IsNullOrWhiteSpace(b.NomorHP) ? "-" : b.NomorHP;
                ws.Cells[rowIdx, 10].Value = b.NomorRekomendasi;
                ws.Cells[rowIdx, 11].Value = b.TanggalRekomendasi;
                ws.Cells[rowIdx, 12].Value = b.NomorSk;
                ws.Cells[rowIdx, 13].Value = b.TanggalPengangkatan;
                ws.Cells[rowIdx, 14].Value = b.Pendidikan;
                ws.Cells[rowIdx, 15].Value = b.TahunLulus;
                ws.Cells[rowIdx, 16].Value = b.NoIjasah;
                ws.Cells[rowIdx, 17].Value = b.NomorRekening;
                ws.Cells[rowIdx, 18].Value = b.NomorBpjs;

                if (rowIdx % 2 == 1)
                {
                    ws.Cells[rowIdx, 1, rowIdx, HeaderEkspor.Length].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    ws.Cells[rowIdx, 1, rowIdx, HeaderEkspor.Length].Style.Fill.BackgroundColor
                        .SetColor(Color.FromArgb(248, 250, 252));
                }

                rowIdx++;
            }

            ws.Cells[1, 1, rowIdx - 1, HeaderEkspor.Length].AutoFitColumns(10, 45);
            ws.Cells[1, 1, rowIdx - 1, HeaderEkspor.Length].Style.Border.BorderAround(ExcelBorderStyle.Thin);
            await package.SaveAsAsync(new FileInfo(path));
        }

        /// <summary>
        /// Cetak daftar **bagian yang sedang tampil** (sama persis dengan layar,
        /// termasuk baris jabatan yang belum terisi) ke PDF, lalu tampilkan pada
        /// pratinjau SudesPdf. Tiap bagian dicetak terpisah sebab SK tiap kelompok
        /// punya cara penerbitan berbeda.
        /// </summary>
        private async Task CetakPdfAsync()
        {
            if (_penjaga is { CetakTerkunci: true })
            {
                await _messageService.ShowInfoAsync(
                    SuDesApp.Services.PenjagaLisensi.PesanCetakTerkunci);
                return;
            }

            try
            {
                IsBusy = true;

                var desa = await _desaRepo.GetInfoDesaAsync().ConfigureAwait(true);
                var data = SusunDataCetak(desa);

                // Generate PDF di thread_pool — mesin PDF padat CPU membekukan UI bila di UI thread.
                byte[] isi = await Task.Run(() =>
                {
                    using var stream = new MemoryStream();
                    _strukturGenerator.GeneratePdf(stream, data);
                    return stream.ToArray();
                }, CancellationToken.None).ConfigureAwait(true);
                if (isi.Length == 0)
                {
                    await _messageService.ShowErrorAsync("Gagal membuat PDF atau PDF kosong.");
                    return;
                }

                // Pratinjau ditulis ke folder TempPDF supaya ikut dibersihkan
                // TempPdfCleanup seperti keluaran lain; SudesPdf membutuhkan
                // jalur penuh — nama berkas saja membuat dokumen gagal dimuat.
                string jalur = await PratinjauPdf
                    .SimpanAsync(_appConfig, isi, $"PerangkatDesa{SufiksNamaBerkas}")
                    .ConfigureAwait(true);

                _activityLog.Log("PERANGKAT", $"{data.JumlahOrang} orang {NamaBagianAktif}", "Cetak PDF", jalur);
                StatusText = $"Daftar {NamaBagianTampil} ({data.JumlahOrang} orang) dibuka di pratinjau PDF.";

                _navigation.Navigate(_previewFactory($"Daftar {NamaBagianTampil}", jalur));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat PDF daftar perangkat desa");
                await _messageService.ShowErrorAsync("Gagal membuat PDF daftar perangkat desa.\n\n" + ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Cetak formulir pendataan kosong: blanko 18 kolom berkas "DATA PERANGKAT"
        /// (DESA s.d. BPJS KES) yang dipakai menarik data perangkat baru di lapangan,
        /// lalu buka hasilnya di pratinjau SudesPdf agar bisa dicetak/diperbanyak.
        /// </summary>
        private async Task CetakFormulirAsync()
        {
            if (_penjaga is { CetakTerkunci: true })
            {
                await _messageService.ShowInfoAsync(
                    SuDesApp.Services.PenjagaLisensi.PesanCetakTerkunci);
                return;
            }

            try
            {
                IsBusy = true;

                var desa = await _desaRepo.GetInfoDesaAsync().ConfigureAwait(true);
                var data = new FormulirPerangkatData
                {
                    Desa = desa,
                    TanggalCetak = DateTime.Now
                };

                using var stream = new MemoryStream();
                _formulirGenerator.GeneratePdf(stream, data);
                byte[] isi = stream.ToArray();
                if (isi.Length == 0)
                {
                    await _messageService.ShowErrorAsync("Gagal membuat formulir atau PDF kosong.");
                    return;
                }

                // Sama seperti cetak daftar: berkas ditulis ke TempPDF supaya ikut
                // dibersihkan TempPdfCleanup, lalu dibuka di pratinjau SudesPdf.
                string jalur = await PratinjauPdf
                    .SimpanAsync(_appConfig, isi, "FormulirPerangkat")
                    .ConfigureAwait(true);

                _activityLog.Log("PERANGKAT", "Formulir pendataan (18 kolom DATA PERANGKAT)",
                    "Cetak Formulir", jalur);
                StatusText = "Formulir pendataan perangkat desa dibuka di pratinjau PDF.";

                _navigation.Navigate(_previewFactory("Formulir Pendataan Perangkat Desa", jalur));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat formulir pendataan perangkat desa");
                await _messageService.ShowErrorAsync(
                    "Gagal membuat formulir pendataan perangkat desa.\n\n" + ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Baris cetak dari **bagian yang sedang tampil**, mengikuti kolom berkas
        /// "DATA PERANGKAT" contoh: hanya orang yang tercatat (baris jabatan kosong
        /// tidak dicetak sebagai baris — jumlahnya tampil pada ringkasan di atas).
        /// </summary>
        private StrukturPerangkatData SusunDataCetak(DesaData desa)
        {
            var baris = TabelAktif.Where(b => b.AdaData).ToList();
            var nomorJabatan = new Dictionary<string, int>(StringComparer.Ordinal);

            return new StrukturPerangkatData
            {
                Desa = desa,
                Judul = JudulDokumenAktif,
                TanggalCetak = DateTime.Now,
                Baris = baris.Select(b => new StrukturPerangkatBaris
                {
                    Jabatan = JabatanBernomor(b.JabatanTampil, nomorJabatan),
                    Nama = b.Nama,
                    JenisKelamin = b.JenisKelamin,
                    TempatLahir = b.TempatLahir,
                    TanggalLahir = b.TanggalLahir,
                    Nik = string.IsNullOrWhiteSpace(b.NIK) ? "-" : b.NIK,
                    Agama = b.Agama,
                    NoTelp = string.IsNullOrWhiteSpace(b.NomorHP) ? "-" : b.NomorHP,
                    NomorRekomendasi = b.NomorRekomendasi,
                    TanggalRekomendasi = b.TanggalRekomendasi,
                    NomorSk = b.NomorSk,
                    TanggalSk = b.TanggalPengangkatan,
                    Pendidikan = b.Pendidikan,
                    TahunLulus = b.TahunLulus,
                    NoIjasah = b.NoIjasah,
                    NoRekBjb = b.NomorRekening,
                    BpjsKes = b.NomorBpjs
                }).ToList(),
                JumlahOrang = baris.Count,
                JumlahJabatanKosong = TabelAktif.Count(b => b.IsBaru)
            };
        }


    }
}
