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
        // Cetak PDF & template impor
        // =====================================================================

        /// <summary>
        /// Cetak daftar warga yang sedang tersaring — seluruh baris yang cocok
        /// filter, sama seperti ekspor — ke PDF berkop desa, lalu tampilkan pada
        /// pratinjau SudesPdf (pola halaman Data Perangkat Desa).
        /// </summary>
        private async Task CetakPdfAsync()
        {
            if (_penjaga is { CetakTerkunci: true })
            {
                await _messageService.ShowInfoAsync(PenjagaLisensi.PesanCetakTerkunci);
                return;
            }

            try
            {
                IsBusy = true;

                var desa = await _desa.GetInfoDesaAsync();
                var data = await SusunDataCetakAsync(desa);
                if (data.Baris.Count == 0)
                {
                    await _messageService.ShowInfoAsync(
                        "Tidak ada warga yang sesuai filter, jadi berkas cetak tidak dibuat.");
                    return;
                }

                using var stream = new MemoryStream();
                _daftarGenerator.GeneratePdf(stream, data);
                byte[] isi = stream.ToArray();
                if (isi.Length == 0)
                {
                    await _messageService.ShowErrorAsync("Gagal membuat PDF atau PDF kosong.");
                    return;
                }

                // Pratinjau ditulis ke folder TempPDF supaya ikut dibersihkan
                // TempPdfCleanup seperti keluaran lain; SudesPdf membutuhkan
                // jalur penuh — nama berkas saja membuat dokumen gagal dimuat.
                string jalur = await PratinjauPdf
                    .SimpanAsync(_appConfig, isi, $"DaftarWarga_{DateTime.Now:yyyyMMdd_HHmmss}");

                _activityLog.Log("WARGA", $"{data.Baris.Count} baris", "Cetak PDF", jalur);
                StatusText = $"Daftar {data.Baris.Count} warga dibuka di pratinjau PDF.";

                _navigation.Navigate(_previewFactory("Daftar Data Warga", jalur));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat PDF daftar data warga");
                await _messageService.ShowErrorAsync("Gagal membuat PDF daftar data warga.\n\n" + ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Susun isi cetak: seluruh baris tersaring, memakai tampilan yang sama dengan tabel layar.</summary>
        private async Task<DaftarWargaData> SusunDataCetakAsync(DesaData desa)
        {
            var data = await _warga.GetWargaForExportAsync(SusunFilter());

            var baris = new List<DaftarWargaBaris>(data.Count);
            int nomor = 1;
            foreach (var w in data)
            {
                var tampil = new WargaBaris { Data = w };
                baris.Add(new DaftarWargaBaris
                {
                    Nomor = nomor++,
                    Nik = string.IsNullOrWhiteSpace(w.NIK) ? "-" : w.NIK!.Trim(),
                    Nama = string.IsNullOrWhiteSpace(w.Nama) ? "-" : w.Nama!.Trim(),
                    NoKk = string.IsNullOrWhiteSpace(w.NoKK) ? "-" : w.NoKK!.Trim(),
                    JenisKelamin = tampil.JenisKelaminRingkas,
                    Ttl = tampil.TTL,
                    Umur = tampil.Umur,
                    Alamat = tampil.AlamatRingkas,
                    NomorHp = string.IsNullOrWhiteSpace(w.NomorHP) ? "-" : w.NomorHP!.Trim(),
                    Status = tampil.StatusTampil
                });
            }

            return new DaftarWargaData
            {
                Desa = desa,
                Judul = "DAFTAR DATA WARGA",
                TanggalCetak = DateTime.Now,
                FilterTeks = SusunTeksFilter(),
                Baris = baris,
                JumlahWarga = baris.Count
            };
        }

        /// <summary>Keterangan filter yang sedang aktif, dicetak di bawah judul supaya pembaca tahu cakupan daftar.</summary>
        private string SusunTeksFilter()
        {
            var bagian = new List<string>();
            if (!string.IsNullOrWhiteSpace(SearchText)) bagian.Add($"kata kunci \"{SearchText.Trim()}\"");
            if (!string.IsNullOrWhiteSpace(FilterStatus))
                bagian.Add($"status {StatusWargaTipe.Normalisasi(FilterStatus)}");
            if (!string.IsNullOrWhiteSpace(FilterWilayah) && FilterWilayah.Contains(':'))
            {
                var b = FilterWilayah.Split(':', 2);
                string jenis = b[0].ToUpperInvariant() switch
                {
                    "RT" => "RT",
                    "RW" => "RW",
                    "DUSUN" => "Dusun",
                    _ => b[0]
                };
                bagian.Add($"{jenis} {b[1]}");
            }
            return bagian.Count == 0 ? "Seluruh warga" : string.Join(", ", bagian);
        }

        /// <summary>
        /// Buat template Excel kosong untuk diisi manual lalu diimpor kembali:
        /// baris judulnya persis kolom yang dikenali layanan impor, kolom nomor
        /// disetel teks agar nol di depan tidak hilang, plus sheet Petunjuk.
        /// </summary>
        private async Task BuatTemplateAsync()
        {
            var sfd = new SaveFileDialog
            {
                Title = "Buat Template Impor Data Warga",
                Filter = "Microsoft Excel (*.xlsx)|*.xlsx",
                FileName = "Template_Impor_Data_Warga.xlsx",
                DefaultExt = ".xlsx"
            };
            if (sfd.ShowDialog() != true) return;

            IsBusy = true;
            try
            {
                TulisTemplate(sfd.FileName);
                _activityLog.Log("WARGA", "Template kosong", "Buat Template", sfd.FileName);
                StatusText = $"Template impor warga dibuat di {Path.GetFileName(sfd.FileName)}.";
                await _messageService.ShowInfoAsync(
                    "Template kosong berhasil dibuat.\n\n" +
                    "Isi satu warga per baris di bawah judul kolom pada sheet \"Data Warga\", " +
                    "lalu masukkan kembali lewat tombol Impor. Sheet \"Petunjuk\" menjelaskan " +
                    "format kolom yang wajib diperhatikan.\n\n" + sfd.FileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat template impor warga");
                await _messageService.ShowErrorAsync("Gagal membuat template impor warga.\n\n" + ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Tulis berkas template: sheet Data Warga (judul kolom) + sheet Petunjuk.</summary>
        private static void TulisTemplate(string path)
        {
            using var package = new ExcelPackage();

            // Sheet pertama dibaca layanan impor: judul kolom harus tepat sama
            // dengan yang dipetakan ImporWargaService, tanpa baris contoh —
            // template ini sengaja kosong untuk diisi manual.
            var ws = package.Workbook.Worksheets.Add("Data Warga");
            for (int col = 0; col < HeaderTemplate.Length; col++)
            {
                var cell = ws.Cells[1, col + 1];
                cell.Value = HeaderTemplate[col];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                cell.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(37, 99, 235));
                cell.Style.Font.Color.SetColor(Color.White);
                cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            }
            ws.Row(1).Height = 26;

            // NIK, No KK, Nomor HP, RT, dan RW disetel TEKS: bila disimpan sebagai
            // angka, nol di depan hilang dan impor menolak barisnya (NIK bukan
            // 16 digit) — jebakan paling sering pada template buatan sendiri.
            foreach (var judul in new[] { "NIK", "No Kartu Keluarga", "Nomor HP", "RT", "RW" })
            {
                int idx = Array.IndexOf(HeaderTemplate, judul);
                if (idx < 0) continue;
                ws.Cells[2, idx + 1, 500, idx + 1].Style.Numberformat.Format = "@";
            }

            ws.Cells[1, 1, 1, HeaderTemplate.Length].AutoFitColumns(10, 45);
            ws.Cells[1, 1, 1, HeaderTemplate.Length].Style.Border.BorderAround(ExcelBorderStyle.Thin);
            ws.View.FreezePanes(2, 1);

            // Sheet kedua: petunjuk singkat. Impor hanya membaca sheet pertama,
            // jadi teks ini tidak ikut terbaca sebagai data.
            var panduan = package.Workbook.Worksheets.Add("Petunjuk");
            string[] kalimat =
            {
                "PETUNJUK PENGGUNAAN TEMPLATE IMPOR DATA WARGA",
                "",
                "1. Isi satu warga per baris pada sheet \"Data Warga\", di bawah judul kolom. Jangan mengubah atau menghapus judul kolom.",
                "2. NIK wajib diisi: 16 digit angka, unik untuk tiap warga, tanpa spasi. Nol di depan harus tetap ada (kolom sudah berformat teks).",
                "3. Nomor Kartu Keluarga 16 digit, boleh dikosongkan. Satu KK sebaiknya berisi satu Kepala Keluarga beserta anggota keluarganya.",
                "4. Tanggal lahir ditulis dd-MM-yyyy atau dd/MM/yyyy, misalnya 17-08-1990.",
                "5. Jenis Kelamin diisi L atau P (boleh juga Laki-laki / Perempuan).",
                "6. Kolom kosong dibiarkan kosong — kolom di luar judul di atas akan diabaikan saat impor.",
                "7. Simpan berkas sebagai .xlsx, lalu buka menu Data Warga dan tekan tombol Impor. Setiap baris diperiksa lebih dulu beserta alasannya sebelum ada yang disimpan.",
                "8. Warga yang NIK-nya sudah ada tidak ditimpa kecuali kotak \"Perbarui warga yang NIK-nya sudah ada\" dicentang saat impor."
            };
            for (int i = 0; i < kalimat.Length; i++)
            {
                panduan.Cells[i + 1, 1].Value = kalimat[i];
                if (i == 0) panduan.Cells[i + 1, 1].Style.Font.Bold = true;
            }
            panduan.Column(1).Width = 120;

            package.SaveAs(new FileInfo(path));
        }

    }
}
