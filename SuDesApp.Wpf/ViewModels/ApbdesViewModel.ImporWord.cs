using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using SuDesApp.Data.Repositories;
using SuDesApp.Services;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class ApbdesViewModel : ObservableObject, IDisposable
    {
        // ===================== Impor Word =====================

        /// <summary>
        /// Impor berkas dokumen APBDes (Word): berkasnya disalin ke arsip, teksnya
        /// ditampilkan, dan baris anggaran yang dikenali mengisi tabel rincian.
        /// Semua hasilnya masih bisa disunting sebelum disimpan.
        /// </summary>
        private async Task ImporWordAsync()
        {
            if (_terpilih == null)
            {
                PesanGalat = "Pilih dulu dokumennya (atau tekan Tambah Dokumen) supaya berkas bisa ditempelkan ke dokumen itu.";
                return;
            }

            var dialog = new OpenFileDialog
            {
                Title = "Pilih berkas dokumen APBDes (Word)",
                Filter = "Dokumen Word|*.docx;*.doc|Word (.docx)|*.docx|Word 97-2003 (.doc)|*.doc|Semua Berkas (*.*)|*.*",
                CheckFileExists = true,
            };

            if (dialog.ShowDialog() != true) return;

            SedangMemuat = true;
            Pesan = string.Empty;
            PesanGalat = string.Empty;
            PesanPeringatan = string.Empty;

            try
            {
                var hasil = await _wordImpor.BacaAsync(dialog.FileName);
                TerapkanImpor(dialog.FileName, hasil);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengimpor berkas APBDes");
                PesanGalat = "Gagal membaca berkas: " + ex.Message;
            }
            finally
            {
                SedangMemuat = false;
            }
        }

        /// <summary>
        /// Lampirkan berkas dokumen APBDes apa adanya (PDF/Excel/Word) ke arsip:
        /// berkasnya disalin dan tidak dibaca isinya. Ini jalur utama untuk hasil
        /// cetak Siskeudes; jalur Word dipakai bila rinciannya ingin dikenali otomatis.
        /// </summary>
        private void PilihBerkasUntukDilampirkan()
        {
            if (_terpilih == null)
            {
                PesanGalat = "Pilih dulu dokumennya (atau tekan Tambah Dokumen) supaya berkas bisa ditempelkan ke dokumen itu.";
                return;
            }

            var dialog = new OpenFileDialog
            {
                Title = "Pilih berkas dokumen APBDes",
                Filter = "Dokumen Siskeudes|*.pdf;*.xlsx;*.xls;*.csv;*.docx;*.doc"
                         + "|PDF (*.pdf)|*.pdf|Excel (*.xlsx;*.xls)|*.xlsx;*.xls|Word (*.docx;*.doc)|*.docx;*.doc"
                         + "|Semua Berkas (*.*)|*.*",
                CheckFileExists = true,
            };

            if (dialog.ShowDialog() != true) return;

            Pesan = string.Empty;
            PesanGalat = string.Empty;
            PesanPeringatan = string.Empty;

            try
            {
                LampirkanBerkas(dialog.FileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal melampirkan berkas APBDes");
                PesanGalat = "Gagal melampirkan berkas: " + ex.Message;
            }
        }

        /// <summary>
        /// Salin berkas sumber ke arsip tanpa membaca isinya. Dipisah dari dialog
        /// berkas supaya bisa diuji tanpa jendela.
        /// </summary>
        internal bool LampirkanBerkas(string jalurBerkas)
        {
            var dok = _terpilih;
            if (dok == null) return false;

            string lama = dok.Model.Berkas;
            string nama = ApbdesStore.SimpanBerkas(jalurBerkas);
            dok.Model.Berkas = nama;
            dok.Model.BerkasAsli = Path.GetFileName(jalurBerkas);

            // Isi yang dibaca dari berkas lama tidak berlaku lagi untuk berkas baru.
            dok.Model.Teks = string.Empty;
            dok.PerbaruiBerkas();
            if (!string.IsNullOrEmpty(lama) && !string.Equals(lama, nama, StringComparison.OrdinalIgnoreCase))
            {
                ApbdesStore.HapusBerkas(lama);
            }

            OnPropertyChanged(nameof(BerkasTampil));
            OnPropertyChanged(nameof(TeksTampil));
            OnPropertyChanged(nameof(AdaTeks));
            PerbaruiTombol();
            Pesan = "Berkas \"" + dok.Model.BerkasAsli + "\" disimpan ke arsip. "
                    + "Tekan Simpan supaya tercatat di daftar dokumen.";
            return true;
        }

        /// <summary>
        /// Terapkan hasil pembacaan berkas ke dokumen yang sedang dibuka: berkasnya
        /// disalin ke arsip, identitas yang kosong diisi dari berkas, dan tabel
        /// rincian diganti baris anggaran hasil pembacaan.
        ///
        /// Dipisah dari pemilihan berkas (dialog) supaya bisa diuji tanpa jendela.
        /// </summary>
        internal bool TerapkanImpor(string jalurBerkas, HasilImporApbdes hasil)
        {
            var dok = _terpilih;
            if (dok == null) return false;

            string lama = dok.Model.Berkas;
            string nama = ApbdesStore.SimpanBerkas(jalurBerkas);
            dok.Model.Berkas = nama;
            dok.Model.BerkasAsli = hasil.NamaBerkas;
            dok.Model.Teks = hasil.Teks;
            dok.PerbaruiBerkas();

            // Berkas lama pada dokumen yang sama tidak perlu disimpan dua kali.
            if (!string.IsNullOrEmpty(lama) && !string.Equals(lama, nama, StringComparison.OrdinalIgnoreCase))
            {
                ApbdesStore.HapusBerkas(lama);
            }

            if (string.IsNullOrWhiteSpace(dok.Nomor) && hasil.Nomor.Length > 0) dok.Nomor = hasil.Nomor;
            if (hasil.Tahun.HasValue && hasil.Tahun.Value > 2000) dok.Tahun = hasil.Tahun.Value;
            if (!string.IsNullOrWhiteSpace(hasil.Jenis) && JenisDaftar.Contains(hasil.Jenis!))
            {
                dok.Jenis = hasil.Jenis!;
            }

            if (dok.Tanggal == null)
            {
                // Tanggal dokumen tidak selalu bisa dibaca dari berkas; diisi tanggal
                // hari ini supaya ada titik awal yang mudah diperbaiki pengguna.
                dok.Tanggal = _jam().Date;
            }

            if (hasil.Anggaran.Count > 0)
            {
                foreach (var item in Pos.ToList()) item.PropertyChanged -= PosBerubah;
                Pos.Clear();
                foreach (var baris in hasil.Anggaran)
                {
                    Pos.Add(BuatPos(new ApbdesStore.Pos
                    {
                        Kode = baris.Kode,
                        Uraian = baris.Uraian,
                        Anggaran = baris.Anggaran,
                        Realisasi = baris.Realisasi,
                    }));
                }

                RingkasanBerubah();
            }

            // Tahun dokumen bisa berubah dari berkas; daftar tahun ikut menyesuaikan.
            SegarkanPilihanTahun();
            if (dok.Tahun != Tahun)
            {
                Pesan = "Berkas \"" + hasil.NamaBerkas + "\" disimpan, tetapi tahun dokumen bergeser ke "
                        + dok.Tahun + " — pindah ke tahun itu untuk melihatnya.";
                SimpanKeModel();
                SegarkanDaftar();
            }
            else
            {
                Pesan = "Berkas \"" + hasil.NamaBerkas + "\" disimpan ke arsip: "
                        + hasil.Anggaran.Count + " baris anggaran terbaca. Periksa tabelnya, lalu tekan Simpan.";
            }

            PesanPeringatan = hasil.Peringatan.Count > 0 ? string.Join(" ", hasil.Peringatan) : string.Empty;

            _logger.LogInformation(
                "Impor APBDes {Berkas}: {Baris} baris anggaran untuk dokumen {Dokumen}.",
                hasil.NamaBerkas, hasil.Anggaran.Count, dok.Judul);

            return true;
        }

        private void PerbaruiTombol()
        {
            foreach (var perintah in new[]
                     {
                         HapusDokumenCommand, HapusBerkasCommand, BukaBerkasCommand, PratinjauBerkasCommand,
                         TambahPosCommand, LampirkanBerkasCommand
                     })
            {
                (perintah as RelayCommand)?.RaiseCanExecuteChanged();
            }

            (ImporWordCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (MuatDariPusatCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(BisaPratinjau));
            OnPropertyChanged(nameof(KeteranganPratinjau));
            OnPropertyChanged(nameof(JudulTampilan));
        }


    }
}
