using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SuDesApp;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.GeneratorPdf;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class InputKeputusanViewModel : ObservableObject, IJudulHalaman
    {
        public void Initialize(string jenisKeputusan, DataKeputusan? editData, DataKeputusan? prefill = null)
        {
            _jenisKeputusan = (jenisKeputusan ?? "SK").ToUpperInvariant();
            _jenisKeputusanAwal = _jenisKeputusan;
            _editData = editData;
            _naskahSalinan = null;
            bool isEdit = _editData != null;
            Title = (isEdit ? "Edit " : "Input ") + LabelJenis(_jenisKeputusan);

            if (isEdit)
            {
                Nomor = _editData!.Nomor;
                Tanggal = _editData.Tanggal.ToString("dd-MM-yyyy");
                Tentang = _editData.Tentang;
                Keterangan = _editData.Keterangan;
                _storedFileName = _editData.FileWord;
            }
            else if (prefill != null)
            {
                Nomor = prefill.Nomor;
                Tanggal = prefill.Tanggal.ToString("dd-MM-yyyy");
                Tentang = prefill.Tentang;
                Keterangan = prefill.Keterangan;
                _storedFileName = null;
                // Salin membawa PDF sumber agar naskah Perdes ikut tersalin;
                // berkas sumber tidak disentuh — PDF baru ditulis saat simpan.
                _naskahSalinan = prefill.FileWord;
            }
            else
            {
                Nomor = string.Empty;
                Tanggal = string.Empty;
                Tentang = string.Empty;
                Keterangan = string.Empty;
                _storedFileName = null;
            }

            _wordFilePath = string.Empty;
            _pdfFilePath = string.Empty;
            OnPropertyChanged(nameof(IsPerdes));
            OnPropertyChanged(nameof(IsBukanPerdes));
            OnPropertyChanged(nameof(WordFileDisplay));
            OnPropertyChanged(nameof(HasStoredOrPickedFile));
            ((AsyncRelayCommand)OpenWordFileCommand).RaiseCanExecuteChanged();
            MuatNaskahAwal();
        }

        /// <summary>
        /// Muat naskah contoh PERDES sebagai isi halaman. Nama desa pada teks
        /// contoh diganti identitas Pengaturan Surat; nomor dan tanggal tetap
        /// diisi saat pratinjau/simpan dari kolom formulir.
        /// </summary>
        public async Task GunakanNaskahContohAsync(string namaBerkas)
        {
            if (string.IsNullOrWhiteSpace(namaBerkas)) return;
            var desa = await _desaRepository.GetInfoDesaAsync().ConfigureAwait(true);
            var naskah = NaskahPerdesContoh.Muat(namaBerkas, desa);
            IsiHalamanDariModel(naskah.Halaman);
            InfoNaskah = $"{naskah.Halaman.Count} halaman naskah contoh dimuat. Sunting isi halaman lalu periksa pratinjau PDF.";
        }


    }
}
