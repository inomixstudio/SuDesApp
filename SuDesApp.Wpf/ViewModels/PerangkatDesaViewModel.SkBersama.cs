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
    /// Mode SK bersama: lampiran daftar nama, pemuatan dari data jabatan + Data Warga, dan segarkan turunannya.
    /// Pemecahan god-class tanpa mengubah perilaku — bagian dari kelas parsial
    /// <see cref="PerangkatDesaViewModel"/>.
    /// </summary>
    public partial class PerangkatDesaViewModel
    {
        // ---------- SK bersama (lampiran daftar nama) ----------

        /// <summary>
        /// Benar bila kelompok jabatan terpilih memang diterbitkan sebagai satu SK
        /// bersama (Linmas, Posyandu, Kader KPM — lihat
        /// <see cref="SkPerangkatKatalog.LazimBersama"/>), bukan SK per orang.
        /// Panel menyalakan mode bersama otomatis untuk kelompok ini.
        /// </summary>
        public bool SkWajibBersama =>
            SkPerangkatKatalog.LazimBersama(JabatanPerangkat.Kelompok(_skJabatan));

        /// <summary>Benar bila operator boleh mengubah mode SK bersama.</summary>
        public bool SkBersamaDiubah => !SkWajibBersama;

        /// <summary>
        /// Mode SK bersama: satu SK dan satu nomor untuk semua orang pada jabatan
        /// terpilih, dengan nama-namanya dicetak pada halaman Lampiran seperti SK
        /// Linmas/Posyandu. Dinyalakan operator dari panel — kecuali kelompok yang
        /// SK-nya memang selalu bersama, yang dinyalakan otomatis dan tidak bisa
        /// dimatikan; daftar namanya dimuat saat mode menyala dan dimuat ulang
        /// lewat tombolnya.
        /// </summary>
        public bool SkBersama
        {
            get => _skBersama;
            set
            {
                // Kelompok SK bersama (Linmas, Posyandu, Kader KPM) tidak boleh
                // keluar dari mode bersama: dokumennya memang satu untuk seluruh
                // anggota, bukan SK per orang.
                if (!value && SkWajibBersama)
                {
                    OnPropertyChanged(nameof(SkBersama));
                    return;
                }

                if (!SetProperty(ref _skBersama, value)) return;

                if (value)
                {
                    // Pemuatannya sengaja tidak ditunggui setter; panel menampilkan
                    // keadaan "memuat" sampai daftarnya siap.
                    _ = MuatLampiranBersamaAsync();
                }
                else
                {
                    // Versi dinaikkan supaya hasil pemuatan yang masih berjalan
                    // tidak lagi dipakai setelah mode dimatikan.
                    _skLampiranVersi++;
                    _skLampiranCadangan = false;
                    SkLampiranMemuat = false;
                    SkLampiran.Clear();
                    SegarkanTurunanLampiran();
                }
            }
        }

        /// <summary>Nama-nama yang ikut pada lampiran SK bersama, urut seperti akan tercetak.</summary>
        public ObservableCollection<LampiranBarisViewModel> SkLampiran { get; } = new();

        /// <summary>Daftar nama lampiran sedang dibaca/dilengkapi dari Data Warga.</summary>
        public bool SkLampiranMemuat
        {
            get => _skLampiranMemuat;
            private set
            {
                if (SetProperty(ref _skLampiranMemuat, value))
                    OnPropertyChanged(nameof(SkPesanLampiran));
            }
        }

        public int SkJumlahLampiran => SkLampiran.Count;

        /// <summary>Benar bila jabatan terpilih punya orang yang bisa masuk ke lampiran.</summary>
        public bool SkBisaBersama => CalonLampiran(_skJabatan).Orang.Count > 0;

        /// <summary>Penjelasan mode SK bersama, tampil di panel di bawah tombolnya.</summary>
        public string SkCatatanBersama
        {
            get
            {
                const string penjelasan =
                    "Satu SK dan satu nomor untuk semua orang pada jabatan ini; nama mereka tidak dicetak di badan SK, "
                    + "melainkan pada halaman Lampiran daftar nama seperti SK Linmas/Posyandu. Kolom pribadi (pendidikan, "
                    + "pekerjaan, agama, golongan darah, status kawin, alamat) diambil dari Data Warga lewat NIK.";

                if (!SkWajibBersama) return penjelasan;

                return $"Kelompok {NamaJabatanTampil(_skJabatan)} pada contoh SK desanya diterbitkan sebagai satu SK "
                    + "bersama, jadi pilihan ini menyala tetap dan tidak bisa dimatikan. " + penjelasan;
            }
        }

        /// <summary>Keterangan asal-usul nama pada lampiran (termasuk pemakaian cadangan).</summary>
        public string SkLampiranCatatan => !SkBersama
            ? string.Empty
            : _skLampiranCadangan
                ? "Jabatan ini tidak punya orang berstatus AKTIF atau MENUNGGU SK, jadi seluruh orang pada jabatan "
                  + "ini dimasukkan — hapus baris yang tidak perlu."
                : SkJumlahLampiran > 0
                    ? $"Nama diambil dari data jabatan {NamaJabatanTampil(_skJabatan)}. Peran dan unitnya boleh "
                      + "disunting, dan baris yang tidak ikut dihapus."
                    : string.Empty;

        /// <summary>Ringkas jumlah nama pada lampiran, tampil di atas daftar.</summary>
        public string SkLampiranRingkasan => SkJumlahLampiran == 0
            ? string.Empty
            : $"{SkJumlahLampiran} nama akan tercetak pada lampiran daftar nama.";

        /// <summary>Pesan keadaan daftar lampiran: memuat, kosong, atau jabatan tanpa orang.</summary>
        public string SkPesanLampiran
        {
            get
            {
                if (!SkBersama) return string.Empty;
                if (SkLampiranMemuat) return "Memuat data pribadi dari Data Warga…";
                if (SkLampiran.Count > 0) return string.Empty;
                return SkBisaBersama
                    ? "Daftar nama masih kosong. Tekan Muat Ulang Daftar untuk mengisi nama dari data jabatan ini."
                    : $"Jabatan {NamaJabatanTampil(_skJabatan)} belum punya orang, jadi lampirannya belum bisa "
                      + "disusun. Isi datanya lebih dulu lewat tombol Tambah Orang.";
            }
        }

        /// <summary>Teks tombol pratinjau, menyesuaikan mode yang sedang dipakai.</summary>
        public string SkTombolPratinjau => SkBersama ? "Pratinjau SK Bersama" : "Pratinjau SK";


        private (List<PerangkatDesa> Orang, bool Cadangan) CalonLampiran(string jabatan)
        {
            if (string.IsNullOrWhiteSpace(jabatan))
            {
                return (new List<PerangkatDesa>(), false);
            }

            var semua = _semua
                .Where(p => string.Equals(p.JabatanTampil, jabatan, StringComparison.Ordinal))
                .ToList();
            var pemegang = semua.Where(p => p.MasihMemegangJabatan).ToList();
            bool cadangan = pemegang.Count == 0 && semua.Count > 0;

            var orang = (cadangan ? semua : pemegang)
                .OrderByDescending(p => p.MasihMemegangJabatan)
                .ThenBy(p => p.Nama, StringComparer.CurrentCulture)
                .ToList();
            return (orang, cadangan);
        }

        /// <summary>
        /// Susun daftar nama lampiran SK bersama untuk jabatan terpilih: orangnya
        /// diambil dari data halaman (bukan dari database), sedangkan kolom pribadi
        /// dilengkapi <see cref="SkPerangkatLampiranService"/> lewat NIK sekali baca.
        /// </summary>
        private async Task MuatLampiranBersamaAsync()
        {
            int versi = ++_skLampiranVersi;
            string jabatan = _skJabatan;
            var calon = CalonLampiran(jabatan);
            _skLampiranCadangan = calon.Cadangan;

            SkLampiran.Clear();
            SegarkanTurunanLampiran();
            if (calon.Orang.Count == 0) return;

            try
            {
                SkLampiranMemuat = true;
                string kelompok = JabatanPerangkat.Kelompok(jabatan);
                var sumber = calon.Orang
                    .Select(o => new LampiranSumber
                    {
                        Orang = o,
                        Peran = PeranLampiran(kelompok),
                        // Unit disusun sama dengan SK jadi saat menyimpan, supaya
                        // blok lampiran di panel dan di dokumen selalu sama.
                        Unit = UnitLampiran(kelompok, o)
                    })
                    .ToList();

                var baris = await _lampiranService.SusunAsync(sumber).ConfigureAwait(true);

                // Hasil dibuang bila operator sudah mematikan mode bersama atau
                // berpindah jabatan saat data warga masih dibaca.
                if (versi != _skLampiranVersi || !SkBersama
                    || !string.Equals(_skJabatan, jabatan, StringComparison.Ordinal))
                {
                    return;
                }

                // SusunAsync mempertahankan urutan sumbernya, jadi status orangnya
                // bisa dipasangkan pada baris yang sama untuk ditampilkan di panel.
                for (int i = 0; i < baris.Count && i < sumber.Count; i++)
                {
                    SkLampiran.Add(new LampiranBarisViewModel(
                        baris[i], HapusBarisLampiran, sumber[i].Orang.StatusTampil));
                }

                _activityLog.Log("PERANGKAT", jabatan, "Daftar lampiran SK bersama",
                    $"{SkLampiran.Count} nama");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyusun lampiran SK bersama {Jabatan}", jabatan);
                SkGalat = "Gagal menyusun daftar nama lampiran. Penyebabnya: " + ex.Message;
            }
            finally
            {
                // Hanya pemuatan terbaru yang boleh menutup penanda "memuat";
                // pemuatan lama yang kalah balapan tidak menyentuhnya.
                if (versi == _skLampiranVersi)
                {
                    SkLampiranMemuat = false;
                    SegarkanTurunanLampiran();
                }
            }
        }

        /// <summary>Buang satu baris dari lampiran tanpa mengubah data perangkatnya.</summary>
        private void HapusBarisLampiran(LampiranBarisViewModel baris)
        {
            if (baris is null) return;
            SkLampiran.Remove(baris);
            SegarkanTurunanLampiran();
        }

        /// <summary>Segarkan seluruh turunan daftar lampiran SK bersama.</summary>
        private void SegarkanTurunanLampiran()
        {
            OnPropertyChanged(nameof(SkJumlahLampiran));
            OnPropertyChanged(nameof(SkBisaBersama));
            OnPropertyChanged(nameof(SkLampiranRingkasan));
            OnPropertyChanged(nameof(SkLampiranCatatan));
            OnPropertyChanged(nameof(SkPesanLampiran));
            OnPropertyChanged(nameof(SkTombolPratinjau));
            SegarkanPerintah();
        }

        /// <summary>
        /// Cetak SK jabatan terpilih ke PDF lalu tampilkan di pratinjau SudesPdf.
        /// Isian yang kosong (atau baris tanpa orang) dicetak bergaris titik-titik
        /// oleh generator, jadi dokumen tetap terbaca.
        /// </summary>

    }
}
