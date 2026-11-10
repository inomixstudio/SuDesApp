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
        // ===================== Berkas =====================

        private void HapusBerkas()
        {
            if (_terpilih == null || !_terpilih.AdaBerkas) return;

            ApbdesStore.HapusBerkas(_terpilih.Model.Berkas);
            _terpilih.Model.Berkas = string.Empty;
            _terpilih.Model.BerkasAsli = string.Empty;
            _terpilih.Model.Teks = string.Empty;
            _terpilih.PerbaruiBerkas();

            OnPropertyChanged(nameof(BerkasTampil));
            OnPropertyChanged(nameof(TeksTampil));
            OnPropertyChanged(nameof(AdaTeks));
            PerbaruiTombol();
            Pesan = "Berkas salinan dihapus — tekan Simpan supaya perubahan daftar dokumen tetap tersimpan.";
        }

        private void BukaBerkas()
        {
            if (_terpilih == null) return;

            var jalur = ApbdesStore.JalurBerkas(_terpilih.Model.Berkas);
            if (jalur == null || !File.Exists(jalur))
            {
                PesanGalat = "Berkas salinan tidak ada di arsip. Impor berkasnya lagi lewat tombol Impor dari Word.";
                return;
            }

            BukaDiAplikasiLuar(jalur);
        }

        /// <summary>
        /// Pratinjau berkas arsip DI DALAM aplikasi — ditempatkan di halaman
        /// pratinjau PDF yang sama dengan IDM, Agenda Surat, dan Daftar Hadir.
        /// Berkas diambil dari folder arsip APBDes (bukan folder PDF sementara),
        /// jadi tidak ikut terhapus pembersih dokumen temp. Word/Excel tidak
        /// dirender; tombolnya nonaktif dan tooltip mengarahkan ke Buka berkas.
        /// </summary>
        private void PratinjauBerkas()
        {
            if (_terpilih == null) return;

            var jalur = ApbdesStore.JalurBerkas(_terpilih.Model.Berkas);
            if (jalur == null || !File.Exists(jalur))
            {
                PesanGalat = "Berkas salinan tidak ada di arsip. Impor berkasnya lagi lewat tombol Impor dari Word.";
                return;
            }

            if (!BisaPratinjau)
            {
                // Command sudah nonaktif untuk format ini; penjaga ini hanya untuk
                // pemanggilan langsung (mis. dari uji) supaya pesannya tetap jelas.
                Pesan = "Pratinjau di aplikasi hanya untuk berkas PDF — berkas ini dibuka "
                        + "dengan aplikasi luar lewat tombol Buka berkas.";
                PesanGalat = string.Empty;
                return;
            }

            if (_navigasi == null || _previewFactory == null)
            {
                // Tanpa layanan navigasi (mis. halaman dirakit manual) jangan diam:
                // berkasnya tetap dibuka dengan aplikasi luar.
                BukaDiAplikasiLuar(jalur);
                return;
            }

            string judul = _terpilih.BerkasAsli.Length > 0 ? _terpilih.BerkasAsli : _terpilih.Berkas;
            Pesan = string.Empty;
            PesanGalat = string.Empty;
            _navigasi.Navigate(_previewFactory("Pratinjau " + judul, jalur));
        }

        private void BukaDiAplikasiLuar(string jalur)
        {
            try
            {
                Process.Start(new ProcessStartInfo(jalur) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Tidak bisa membuka berkas APBDes {Jalur}", jalur);
                PesanGalat = "Tidak bisa membuka berkas: " + ex.Message;
            }
        }


    }
}
