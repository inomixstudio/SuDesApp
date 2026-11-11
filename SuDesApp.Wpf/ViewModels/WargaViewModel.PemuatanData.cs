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
        // Pemuatan data
        // =====================================================================

        /// <summary>Muat daftar warga + rekapitulasi. Dipanggil saat halaman dibuka.</summary>
        public async Task LoadAsync(bool muatUlangRekap = false)
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                var filter = SusunFilter();
                var hasil = await _warga.GetWargaPageAsync(filter);

                Items = new ObservableCollection<WargaBaris>(hasil.Items.Select(w => new WargaBaris
                {
                    Data = w,
                    StatusSapuan = StatusWargaTipe.Normalisasi(w.StatusWarga)
                }));
                OnPropertyChanged(nameof(Items));

                TotalData = hasil.Total;
                TotalHalaman = hasil.TotalHalaman;
                Halaman = hasil.Halaman;
                JumlahDitampilkan = Items.Count;
                StatusText = hasil.Ringkasan;

                // Biarkan daftar yang baru dimuat tampil lebih dulu. Karena
                // LoadAsync bersifat async, UI sempat merender halaman sebelum
                // query pendukung selesai; pemakaian koneksi repository tetap
                // berurutan dan aman.
                await MuatDataPendukungAsync(muatUlangRekap);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memuat data warga");
                StatusText = "Gagal memuat data warga.";
                await _messageService.ShowErrorAsync("Gagal memuat data warga.\n\n" + ex.Message);
            }
            finally
            {
                IsBusy = false;
            }

        }

        private async Task MuatDataPendukungAsync(bool muatRekap)
        {
            await MuatDaftarWilayahAsync();
            if (muatRekap) await MuatRekapAsync();
        }

        private async Task MuatRekapAsync()
        {
            try
            {
                var s = await _warga.GetStatistikWargaAsync();

                KartuStatistik = new ObservableCollection<WargaStatistikKartu>
                {
                    new() { Label = "Warga terdata", Nilai = s.TotalSeluruh.ToString("N0", Budaya), Keterangan = $"{s.TotalAktif} tinggal, {s.TotalMeninggal + s.TotalPindah} sudah tidak tinggal", Tekankan = true },
                    new() { Label = "Laki-laki", Nilai = s.LakiLaki.ToString("N0", Budaya), Keterangan = $"{(s.TotalSeluruh > 0 ? s.LakiLaki * 100.0 / s.TotalSeluruh : 0).ToString("0.#", Budaya)}%" },
                    new() { Label = "Perempuan", Nilai = s.Perempuan.ToString("N0", Budaya), Keterangan = $"{(s.TotalSeluruh > 0 ? s.Perempuan * 100.0 / s.TotalSeluruh : 0).ToString("0.#", Budaya)}%" },
                    new() { Label = "Kepala Keluarga", Nilai = s.JumlahKepalaKeluarga.ToString("N0", Budaya), Keterangan = $"{s.KepalaKeluargaLakiLaki} laki-laki, {s.KepalaKeluargaPerempuan} wanita" },
                    new() { Label = "KK Laki-laki", Nilai = s.KepalaKeluargaLakiLaki.ToString("N0", Budaya), Keterangan = "Berkepala laki-laki" },
                    new() { Label = "KK Wanita", Nilai = s.KepalaKeluargaPerempuan.ToString("N0", Budaya), Keterangan = "Berkepala wanita" },
                    new() { Label = "Kartu Keluarga", Nilai = s.JumlahKartuKeluarga.ToString("N0", Budaya), Keterangan = $"{s.TanpaKartuKeluarga} warga belum ada KK" },
                    new() { Label = "Meninggal / pindah", Nilai = (s.TotalMeninggal + s.TotalPindah).ToString("N0", Budaya), Keterangan = $"{s.TotalMeninggal} meninggal, {s.TotalPindah} pindah" }
                };
                OnPropertyChanged(nameof(KartuStatistik));

                RekapRt = new ObservableCollection<WargaStatistikBaris>(s.PerRt);
                OnPropertyChanged(nameof(RekapRt));

                RekapKelompokUsia = new ObservableCollection<WargaStatistikBaris>(s.PerKelompokUsia);
                OnPropertyChanged(nameof(RekapKelompokUsia));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal memuat rekapitulasi warga");
                // Rekap hanya INFORMASI: kegagalan di sini tidak boleh menutup
                // halaman karena daftar warga tetap bisa dipakai.
            }
        }

        private async Task MuatDaftarWilayahAsync()
        {
            try
            {
                var nilai = await _warga.GetDaftarNilaiWilayahAsync();
                var baru = BuildDaftarWilayah(nilai);
                if (SamaDengan(_daftarWilayah, baru)) return;

                _daftarWilayah.Clear();
                foreach (var pair in baru) _daftarWilayah[pair.Key] = pair.Value;
                _daftarWilayahItem = null;
                OnPropertyChanged(nameof(DaftarWilayah));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal mengambil daftar nilai RT/RW/dusun");
            }
        }

        private static bool SamaDengan(Dictionary<string, string> kiri, Dictionary<string, string> kanan)
        {
            if (kiri.Count != kanan.Count) return false;
            foreach (var pair in kanan)
            {
                if (!kiri.TryGetValue(pair.Key, out var nilai) || nilai != pair.Value) return false;
            }
            return true;
        }

        private static Dictionary<string, string> BuildDaftarWilayah(IEnumerable<string> nilai)
        {
            var hasil = new Dictionary<string, string>(StringComparer.Ordinal);
            var rt = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var rw = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var dusun = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var v in nilai)
            {
                if (string.IsNullOrWhiteSpace(v)) continue;
                var teks = v.Trim();
                if (teks.Length == 0) continue;

                // Nilai lama bisa berupa "Dusun I" atau "RT 001 / RW 002"; dikelompokkan
                // berdasarkan awalan supaya tidak bercampur dengan dusun bernama serupa.
                if (teks.StartsWith("RT", StringComparison.OrdinalIgnoreCase) || IsNumerik(teks))
                    rt.Add(AmbikAngka(teks));
                else if (teks.StartsWith("RW", StringComparison.OrdinalIgnoreCase))
                    rw.Add(AmbikAngka(teks));
                else
                    dusun.Add(teks);
            }

            foreach (var n in rt) hasil[$"RT:{n}"] = n;
            foreach (var n in rw) hasil[$"RW:{n}"] = n;
            foreach (var n in dusun) hasil[$"DUSUN:{n.ToUpperInvariant()}"] = n;
            return hasil;
        }

        private static bool IsNumerik(string teks) => teks.All(char.IsDigit);

        private static string AmbikAngka(string teks)
        {
            var digit = new string(teks.Where(char.IsDigit).ToArray());
            return digit.Length > 0 ? digit : teks;
        }

        /// <summary>Opsi dropdown wilayah; item pertama berarti "semua wilayah".</summary>
        public IEnumerable<System.Windows.Controls.ComboBoxItem> DaftarWilayah
        {
            get
            {
                // Cache identitas stabil antar-perubahan data (WFO2000): daftar
                // dibangun sekali lalu dibangun ulang oleh MuatDaftarWilayahAsync.
                _daftarWilayahItem ??= BangunDaftarWilayah();
                return _daftarWilayahItem;
            }
        }

        private List<System.Windows.Controls.ComboBoxItem>? _daftarWilayahItem;

        private List<System.Windows.Controls.ComboBoxItem> BangunDaftarWilayah()
        {
            var hasil = new List<System.Windows.Controls.ComboBoxItem>
            {
                new() { Content = "Semua wilayah", Tag = string.Empty },
            };
            foreach (var pair in _daftarWilayah.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                hasil.Add(new System.Windows.Controls.ComboBoxItem { Content = pair.Value, Tag = pair.Key });
            }
            return hasil;
        }

        private WargaFilter SusunFilter()
        {
            var filter = new WargaFilter
            {
                Cari = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText,
                Status = string.IsNullOrWhiteSpace(FilterStatus) ? null : FilterStatus,
                Halaman = Halaman,
                UkuranHalaman = 100
            };

            if (!string.IsNullOrWhiteSpace(FilterWilayah) && FilterWilayah.Contains(':'))
            {
                var bagian = FilterWilayah.Split(':', 2);
                switch (bagian[0].ToUpperInvariant())
                {
                    case "RT": filter.RT = bagian[1]; break;
                    case "RW": filter.RW = bagian[1]; break;
                    case "DUSUN": filter.Dusun = bagian[1]; break;
                }
            }

            return filter;
        }

        private void JadwalkanMuatUlang()
        {
            var sebelumnya = _debouncePencarian;
            var baru = new CancellationTokenSource();
            _debouncePencarian = baru;
            sebelumnya?.Cancel();
            sebelumnya?.Dispose();

            _ = MuatTertundaAsync(baru.Token);
        }

        private async Task MuatTertundaAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(300, cancellationToken);
                for (int i = 0; i < 20 && IsBusy; i++)
                {
                    await Task.Delay(100, cancellationToken);
                }

                Halaman = 1;
                await LoadAsync(true);
            }
            catch (OperationCanceledException)
            {
                // Ketikan berikutnya menggantikan jadwal ini.
            }
        }


    }
}
