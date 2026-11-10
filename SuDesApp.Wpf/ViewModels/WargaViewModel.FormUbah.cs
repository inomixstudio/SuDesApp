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
        // Form tambah / ubah
        // =====================================================================

        private async Task BukaFormTambahAsync()
        {
            if (_penjaga is { BacaSaja: true })
            {
                await _messageService.ShowInfoAsync(PenjagaLisensi.PesanAksesDikunci);
                return;
            }

            _formIdWarga = 0;
            _alamatDetailFormTersimpan = null;
            IsModeUbah = false;
            FormGalat = string.Empty;

            var desa = await _desa.GetInfoDesaFromCacheAsync();

            FormNoKK = string.Empty;
            FormNik = string.Empty;
            FormNama = string.Empty;
            FormTempatLahir = string.Empty;
            FormTanggalLahir = string.Empty;
            FormJenisKelamin = string.Empty;
            FormAgama = "Islam";
            FormStatusPerkawinan = "Belum Kawin";
            FormStatusKeluarga = StatusKeluargaTipe.KepalaKeluarga;
            FormGolonganDarah = string.Empty;
            FormPekerjaan = string.Empty;
            FormPendidikan = string.Empty;
            FormNamaAyah = string.Empty;
            FormNamaIbu = string.Empty;
            FormNomorHP = string.Empty;
            FormDusun = string.Empty;
            FormRT = string.Empty;
            FormRW = string.Empty;
            FormStatusWarga = StatusWargaTipe.Aktif;
            FormTanggalStatus = DateTime.Today.ToString("yyyy-MM-dd");
            FormKeterangan = string.Empty;

            IsFormTerbuka = true;
            OnPropertyChanged(nameof(JudulForm));

            // Desa/kecamatan/kabupaten mengikuti data desa supaya operator tidak
            // perlu mengetik ulang untuk setiap warga.
            _desaBawaan = desa;
        }

        private DesaData _desaBawaan = new();

        private async Task BukaFormUbahAsync(WargaBaris? baris)
        {
            if (baris == null) return;

            try
            {
                var warga = await _warga.GetWargaByIdAsync(baris.ID_Warga);
                if (warga == null)
                {
                    await _messageService.ShowInfoAsync("Data warga tidak ditemukan. Mungkin sudah dihapus.");
                    return;
                }

                _desaBawaan = await _desa.GetInfoDesaFromCacheAsync();

                _formIdWarga = warga.ID_Warga;
                _alamatDetailFormTersimpan = warga.AlamatDetail;
                IsModeUbah = true;
                FormGalat = string.Empty;

                FormNoKK = warga.NoKK ?? string.Empty;
                FormNik = warga.NIK ?? string.Empty;
                FormNama = warga.Nama ?? string.Empty;
                FormTempatLahir = warga.TempatLahir ?? string.Empty;
                FormTanggalLahir = warga.TanggalLahir ?? string.Empty;
                FormJenisKelamin = WargaBaris.NormalisasiJenisKelamin(warga.JenisKelamin) == "-" ? "" : WargaBaris.NormalisasiJenisKelamin(warga.JenisKelamin);
                FormAgama = warga.Agama ?? string.Empty;
                FormStatusPerkawinan = warga.StatusPerkawinan ?? string.Empty;
                FormStatusKeluarga = warga.StatusKeluarga ?? string.Empty;
                FormGolonganDarah = warga.GolonganDarah ?? string.Empty;
                FormPekerjaan = warga.Pekerjaan ?? string.Empty;
                FormPendidikan = warga.Pendidikan ?? string.Empty;
                FormNamaAyah = warga.NamaAyah ?? string.Empty;
                FormNamaIbu = warga.NamaIbu ?? string.Empty;
                FormNomorHP = warga.NomorHP ?? string.Empty;
                FormDusun = warga.Dusun ?? string.Empty;
                FormRT = warga.RT ?? string.Empty;
                FormRW = warga.RW ?? string.Empty;
                FormStatusWarga = StatusWargaTipe.Normalisasi(warga.StatusWarga);
                FormTanggalStatus = warga.TanggalStatus ?? string.Empty;
                FormKeterangan = warga.KeteranganWarga ?? string.Empty;

                IsFormTerbuka = true;
                OnPropertyChanged(nameof(JudulForm));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuka form ubah warga");
                await _messageService.ShowErrorAsync("Gagal membuka data warga.\n\n" + ex.Message);
            }
        }

        private void TutupForm()
        {
            IsFormTerbuka = false;
            FormGalat = string.Empty;
            _formIdWarga = 0;
            _alamatDetailFormTersimpan = null;
        }

        private async Task SimpanAsync()
        {
            // Mode baca-saja: menambah warga baru diblokir, mengubah status warga
            // yang sudah ada tetap boleh.
            if (!IsModeUbah && _penjaga is { BacaSaja: true })
            {
                await _messageService.ShowInfoAsync(PenjagaLisensi.PesanAksesDikunci);
                return;
            }

            FormGalat = string.Empty;

            var nik = (FormNik ?? string.Empty).Trim();
            if (nik.Length != 16 || !nik.All(char.IsDigit))
            {
                FormGalat = "NIK harus 16 digit angka.";
                return;
            }

            if (string.IsNullOrWhiteSpace(FormNama) || FormNama.Trim().Length < 2)
            {
                FormGalat = "Nama wajib diisi (minimal 2 karakter).";
                return;
            }

            var noKK = (FormNoKK ?? string.Empty).Trim();
            if (noKK.Length > 0 && (noKK.Length != 16 || !noKK.All(char.IsDigit)))
            {
                FormGalat = "Nomor Kartu Keluarga harus 16 digit angka (boleh dikosongkan).";
                return;
            }

            // NIK yang sudah dipakai warga lain harus ditolak SEBELUM menyimpan.
            // WargaRepository.AddOrUpdateWargaAsync memperbarui baris yang NIK-nya
            // sama (jalur impor), jadi tanpa pemeriksaan ini form "Tambah Warga"
            // dengan NIK Budi MENIMPA baris Budi — nama, tanggal lahir, agama,
            // alamat, RT semuanya diganti — lalu tetap melaporkan "berhasil
            // ditambahkan" dan jumlah warga tidak berubah. Satu warga hilang
            // senyap tanpa jalan untuk memulihkannya dari aplikasi.
            if (!IsModeUbah)
            {
                var sudahAda = await _warga.GetWargaByNikAsync(nik).ConfigureAwait(true);
                if (sudahAda != null)
                {
                    FormGalat = $"NIK {nik} sudah dipakai warga \"{sudahAda.Nama}\". " +
                                $"Pilih baris warga tersebut lalu tekan Ubah, atau pakai NIK yang lain.";
                    return;
                }
            }

            IsBusy = true;
            try
            {
                var model = new WargaData
                {
                    ID_Warga = _formIdWarga,
                    NIK = nik,
                    Nama = FormNama?.Trim(),
                    TempatLahir = FormTempatLahir?.Trim(),
                    TanggalLahir = NormalisasiTanggalLahir(FormTanggalLahir),
                    JenisKelamin = FormJenisKelamin,
                    Agama = KosongkanJikaTidakDipilih(FormAgama),
                    StatusPerkawinan = KosongkanJikaTidakDipilih(FormStatusPerkawinan),
                    StatusKeluarga = KosongkanJikaTidakDipilih(FormStatusKeluarga),
                    GolonganDarah = KosongkanJikaTidakDipilih(FormGolonganDarah),
                    Pekerjaan = FormPekerjaan?.Trim(),
                    Pendidikan = FormPendidikan?.Trim(),
                    NamaAyah = FormNamaAyah?.Trim(),
                    NamaIbu = FormNamaIbu?.Trim(),
                    NomorHP = BersihkanNomorHP(FormNomorHP),
                    NoKK = noKK.Length == 0 ? null : noKK,
                    Dusun = FormDusun?.Trim(),
                    RT = FormRT?.Trim(),
                    RW = FormRW?.Trim(),
                    // Alamat detail lama dipertahankan saat edit agar data historis
                    // tidak terhapus walau kolomnya tidak lagi ditampilkan di form.
                    AlamatDetail = _alamatDetailFormTersimpan,
                    // Desa/kecamatan/kabupaten selalu mengikuti data desa: surat
                    // resmi desa tidak boleh mencantumkan desa lain.
                    Desa = _desaBawaan?.NamaDesa,
                    Kecamatan = _desaBawaan?.Kecamatan,
                    Kabupaten = _desaBawaan?.Kabupaten,
                    StatusWarga = StatusWargaTipe.Normalisasi(FormStatusWarga),
                    TanggalStatus = FormTanggalStatus?.Trim(),
                    KeteranganWarga = FormKeterangan?.Trim(),
                    Kewarganegaraan = "WNI"
                };

                int id = await _warga.AddOrUpdateWargaAndGetIdAsync(model);

                if (noKK.Length == 16)
                {
                    await SimpanKartuKeluargaJikaBelumAdaAsync(model, noKK);
                }

                _activityLog.Log("WARGA", $"{model.Nama} ({nik})", IsModeUbah ? "Ubah" : "Tambah",
                    IsModeUbah ? $"ID {id} diperbarui" : $"ID {id} ditambahkan");

                TutupForm();
                await LoadAsync(true);
                StatusText = IsModeUbah ? "Data warga diperbarui." : "Warga baru ditambahkan.";
                await _messageService.ShowInfoAsync(IsModeUbah
                    ? "Data warga berhasil diperbarui."
                    : "Warga baru berhasil ditambahkan.");
            }
            catch (ValidationException vex)
            {
                FormGalat = vex.Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyimpan data warga");
                FormGalat = ex.Message;
                await _messageService.ShowErrorAsync("Gagal menyimpan data warga.\n\n" + ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Buat KK untuk warga baru yang NoKK-nya belum terdaftar. KK yang sudah ada
        /// tidak ditimpa supaya alamat yang sudah dikoreksi tidak hilang.
        /// </summary>
        private async Task SimpanKartuKeluargaJikaBelumAdaAsync(WargaData warga, string noKK)
        {
            try
            {
                var ada = await _warga.GetKartuKeluargaAsync(noKK);
                if (ada != null) return;

                await _warga.SaveKartuKeluargaAsync(new KartuKeluargaData
                {
                    NoKK = noKK,
                    NamaKepalaKeluarga = warga.Nama,
                    Alamat = warga.AlamatDetail,
                    RT = warga.RT,
                    RW = warga.RW,
                    Dusun = warga.Dusun,
                    Desa = warga.Desa,
                    Kecamatan = warga.Kecamatan,
                    Kabupaten = warga.Kabupaten,
                    NomorHP = warga.NomorHP
                });
            }
            catch (Exception ex)
            {
                // KK adalah turunan: kegagalan menulis KK tidak boleh membatalkan
                // warga yang sudah tersimpan.
                _logger.LogWarning(ex, "Gagal membuat kartu keluarga otomatis untuk No KK {NoKK}", noKK);
            }
        }

        private static string KosongkanJikaTidakDipilih(string? nilai) =>
            string.IsNullOrWhiteSpace(nilai) || nilai!.Trim() == "-" ? string.Empty : nilai.Trim();

        private static string? BersihkanNomorHP(string? nomor)
        {
            if (string.IsNullOrWhiteSpace(nomor)) return null;
            var digit = new string(nomor.Where(char.IsDigit).ToArray());
            if (digit.Length == 0) return null;
            // Nomor Indonesia: 62xxx jadi 0xxx agar format baku.
            if (digit.StartsWith("62", StringComparison.Ordinal) && digit.Length >= 11)
                digit = "0" + digit[2..];
            return digit;
        }

        private static string? NormalisasiTanggalLahir(string? tanggal)
        {
            if (string.IsNullOrWhiteSpace(tanggal)) return null;
            var teks = tanggal!.Trim();

            // HARUS TryParseExact dengan daftar format eksplisit. DateTime.TryParse
            // memilih pola budaya lebih dulu (MM/dd/yyyy) dan menerima '-' sebagai
            // pemisah, sehingga tanggal yang diketik mengikuti petunjuk form
            // ("Format: dd-MM-YYYY") tersimpan TERBALIK: "05-06-1990" yang dimaksud
            // 5 Juni 1990 tersimpan 1990-05-06 (6 Mei), dan umur serta tanggal lahir
            // pada setiap surat/ekspor ikut salah selamanya. Format-format di bawah
            // sama dengan yang dipakai WargaRepository saat membaca balik.
            string[] format =
            {
                "yyyy-MM-dd", "dd-MM-yyyy", "dd-MM-yy", "dd/MM/yyyy", "dd/MM/yy",
                "d-M-yyyy", "d/M/yyyy", "dd MMMM yyyy", "dd MMM yyyy", "d MMMM yyyy"
            };
            if (DateTime.TryParseExact(teks, format, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var persis))
            {
                return persis.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            }

            // Bentuk yang tidak dikenali dibiarkan apa adanya supaya operator bisa
            // melihat dan memperbaikinya sendiri (perilaku yang sudah ada).
            return teks;
        }


    }
}
