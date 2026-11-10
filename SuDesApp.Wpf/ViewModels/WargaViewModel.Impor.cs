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
        // Impor warga massal dari Excel/CSV
        // =====================================================================

        private bool _isImporTerbuka;
        private string _jalurBerkasImpor = string.Empty;
        private bool _imporPerbaruiYangAda;
        private bool _isProsesImpor;
        private int _progresImpor;
        private string _ringkasanImpor = string.Empty;
        private string _infoKolomAsing = string.Empty;
        private HasilImporWarga? _hasilImpor;

        /// <summary>Panel impor terbuka di atas daftar (pola sama dengan form warga).</summary>
        public bool IsImporTerbuka
        {
            get => _isImporTerbuka;
            private set => SetProperty(ref _isImporTerbuka, value);
        }

        public string JalurBerkasImpor
        {
            get => _jalurBerkasImpor;
            private set
            {
                if (!SetProperty(ref _jalurBerkasImpor, value)) return;

                // Ganti berkas berarti hasil pemeriksaan lama tidak berlaku lagi;
                // menyimpan tanpa memeriksa ulang bisa menulis data yang salah.
                if (HasilImpor != null)
                {
                    HasilImpor = null;
                    PratinjauImpor.Clear();
                    RingkasanImpor = string.Empty;
                }

                SegarkanPerintah();
            }
        }

        /// <summary>Perbarui warga yang NIK-nya sudah ada; tanpa ini baris itu dilewati.</summary>
        public bool ImporPerbaruiYangAda
        {
            get => _imporPerbaruiYangAda;
            set
            {
                if (!SetProperty(ref _imporPerbaruiYangAda, value)) return;

                // Memilih mode perbarui mengubah keputusan baris duplikat, jadi
                // pemeriksaan harus diulang.
                if (HasilImpor != null)
                {
                    HasilImpor = null;
                    PratinjauImpor.Clear();
                    RingkasanImpor = "Mode diubah. Periksa lagi berkas untuk melihat dampaknya.";
                }
            }
        }

        public bool IsProsesImpor
        {
            get => _isProsesImpor;
            private set => SetProperty(ref _isProsesImpor, value);
        }

        public int ProgresImpor
        {
            get => _progresImpor;
            private set => SetProperty(ref _progresImpor, value);
        }

        public string RingkasanImpor
        {
            get => _ringkasanImpor;
            private set => SetProperty(ref _ringkasanImpor, value);
        }

        /// <summary>Judul kolom yang diabaikan, supaya operator tahu isinya tidak terbaca.</summary>
        public string InfoKolomAsing
        {
            get => _infoKolomAsing;
            private set => SetProperty(ref _infoKolomAsing, value);
        }

        public ObservableCollection<BarisImporWarga> PratinjauImpor { get; } = new();

        public HasilImporWarga? HasilImpor
        {
            get => _hasilImpor;
            private set
            {
                if (!SetProperty(ref _hasilImpor, value)) return;
                OnPropertyChanged(nameof(BisaJalankanImpor));
                SegarkanPerintah();
            }
        }

        public bool BisaJalankanImpor =>
            HasilImpor != null && HasilImpor.JumlahBisaDisimpan > 0;

        private void TutupImpor()
        {
            IsImporTerbuka = false;
            IsProsesImpor = false;
            ProgresImpor = 0;
        }

        private async Task BukaImporAsync()
        {
            var ofd = new OpenFileDialog
            {
                Title = "Impor Data Warga",
                Filter = "Microsoft Excel (*.xlsx)|*.xlsx|CSV UTF-8 (*.csv)|*.csv",
                DefaultExt = ".xlsx"
            };

            if (ofd.ShowDialog() != true) return;

            TutupImpor();
            RingkasanImpor = string.Empty;
            InfoKolomAsing = string.Empty;
            PratinjauImpor.Clear();
            HasilImpor = null;

            // SetProperty pada JalurBerkasImpor juga membersihkan hasil lama.
            JalurBerkasImpor = ofd.FileName;
            IsImporTerbuka = true;

            // Langsung periksa: operator tidak perlu menekan tombol lain dulu.
            await PeriksaImporAsync();
        }

        /// <summary>Periksa berkas tanpa menyimpan apa pun, lalu tampilkan per baris.</summary>
        private async Task PeriksaImporAsync()
        {
            if (string.IsNullOrWhiteSpace(JalurBerkasImpor)) return;

            IsProsesImpor = true;
            try
            {
                var hasil = await _impor.ValidasiAsync(JalurBerkasImpor, ImporPerbaruiYangAda);

                if (hasil.AdaKesalahanBerkas)
                {
                    RingkasanImpor = hasil.KesalahanBerkas!;
                    InfoKolomAsing = string.Empty;
                    PratinjauImpor.Clear();
                    HasilImpor = null;
                    await _messageService.ShowWarningAsync(hasil.KesalahanBerkas!);
                    return;
                }

                HasilImpor = hasil;
                RingkasanImpor = hasil.Ringkasan;
                InfoKolomAsing = hasil.KolomTidakDikenali.Count > 0
                    ? "Kolom diabaikan: " + string.Join(", ", hasil.KolomTidakDikenali)
                    : string.Empty;

                PratinjauImpor.Clear();
                foreach (var b in hasil.Baris)
                    PratinjauImpor.Add(b);

                StatusText = hasil.Ringkasan;

                if (hasil.JumlahBisaDisimpan == 0)
                {
                    await _messageService.ShowWarningAsync(
                        "Tidak ada baris yang bisa disimpan.\n\n" + hasil.Ringkasan +
                        "\n\nPerbaiki berkas lebih dulu, lalu periksa lagi.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memeriksa berkas impor warga");
                RingkasanImpor = "Gagal membaca berkas: " + ex.Message;
                HasilImpor = null;
                PratinjauImpor.Clear();
                await _messageService.ShowErrorAsync("Gagal memeriksa berkas impor.\n\n" + ex.Message);
            }
            finally
            {
                IsProsesImpor = false;
            }
        }

        private async Task JalankanImporAsync()
        {
            var hasil = HasilImpor;
            if (hasil == null || hasil.JumlahBisaDisimpan == 0) return;

            string pertanyaan =
                $"{hasil.JumlahAkanDisimpan} warga baru dan {hasil.JumlahAkanDiperbarui} pembaruan" +
                (hasil.JumlahGagal > 0 ? $", {hasil.JumlahGagal} baris dilewati" : "") +
                "\n\nData yang sudah ada tidak dihapus. Lanjutkan?";

            if (!await _messageService.ShowConfirmationAsync("Konfirmasi Impor", pertanyaan))
                return;

            IsProsesImpor = true;
            ProgresImpor = 0;
            try
            {
                var progres = new Progress<int>(n => ProgresImpor = n);
                int tersimpan = await _impor.JalankanAsync(hasil, progres);

                _activityLog.Log("WARGA", $"{tersimpan} baris", "Impor", JalurBerkasImpor);

                int gagalTersimpan = hasil.JumlahBisaDisimpan - tersimpan;
                string pesan = gagalTersimpan > 0
                    ? $"{tersimpan} warga tersimpan, {gagalTersimpan} baris gagal disimpan (lihat daftar).\n\n" +
                      $"{JalurBerkasImpor}"
                    : $"{tersimpan} warga berhasil diimpor dari {Path.GetFileName(JalurBerkasImpor)}.\n\n{JalurBerkasImpor}";

                StatusText = $"{tersimpan} warga diimpor.";
                TutupImpor();
                await LoadAsync(true);
                await _messageService.ShowInfoAsync(pesan);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menjalankan impor warga");
                await _messageService.ShowErrorAsync("Gagal mengimpor warga.\n\n" + ex.Message);
            }
            finally
            {
                IsProsesImpor = false;
                ProgresImpor = 0;
            }
        }


    }
}
