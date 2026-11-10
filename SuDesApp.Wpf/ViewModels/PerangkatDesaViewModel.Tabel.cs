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
    /// Pemuatan data (LoadAsync), penyusunan baris tabel per bagian, pemindah bagian, helper tanggal, dan HapusAsync.
    /// Pemecahan god-class tanpa mengubah perilaku — bagian dari kelas parsial
    /// <see cref="PerangkatDesaViewModel"/>.
    /// </summary>
    public partial class PerangkatDesaViewModel
    {
        // ---------- aksi ----------

        // ---------- tanggal teks dd-mm-yyyy ----------
        // Aturan bacanya dipusatkan di <see cref="TanggalIsian"/> supaya seluruh
        // form yang tanggalnya diketik memakai pola dan pesan galat yang sama.

        private static string TanggalKeTeks(DateTime? tanggal) => TanggalIsian.KeTeks(tanggal);

        private static bool BacaTanggal(string? teks, string label, out DateTime? tanggal, out string pesan) =>
            TanggalIsian.Baca(teks, label, out tanggal, out pesan);

        /// <summary>
        /// Periksa seluruh isian tanggal form sekaligus, supaya semua kesalahan
        /// ditampilkan bersamaan. Mengembalikan false disertai pesan gabungan bila
        /// ada yang tidak sah.
        /// </summary>
        private bool BacaTanggalForm(out DateTime? tanggalLahir, out DateTime? tanggalSk,
            out DateTime? tanggalRekomendasi, out DateTime? mulai, out DateTime? selesai, out string pesan)
        {
            var kekurangan = new List<string>();

            if (!BacaTanggal(_formTanggalLahir, "Tanggal Lahir", out tanggalLahir, out string p1))
                kekurangan.Add(p1);
            if (!BacaTanggal(_formTanggalSk, "Tanggal SK", out tanggalSk, out string p2))
                kekurangan.Add(p2);
            if (!BacaTanggal(_formTanggalRekomendasi, "Tgl. Rekomendasi", out tanggalRekomendasi, out string p3))
                kekurangan.Add(p3);
            if (!BacaTanggal(_formMasaJabatanMulai, "Masa Jabatan Mulai", out mulai, out string p4))
                kekurangan.Add(p4);
            if (!BacaTanggal(_formMasaJabatanSelesai, "Masa Jabatan Selesai", out selesai, out string p5))
                kekurangan.Add(p5);

            pesan = string.Join(Environment.NewLine, kekurangan);
            return kekurangan.Count == 0;
        }

        /// <summary>
        /// Muat daftar. Dipanggil setelah halaman tampil, jadi
        /// tidak perlu menunggu pekerjaan berat sebelum menavigasi.
        /// </summary>
        public async Task LoadAsync()
        {
            try
            {
                IsBusy = true;

                // Sekali baca: seluruh data dipanggil ke memori, lalu penyusunan
                // baris dijalankan di sini. Halaman ini tidak memakai pencarian
                // maupun filter; seluruh data selalu tampil apa adanya.
                _semua = await _layanan.AmbilSemuaAsync().ConfigureAwait(true);

                SusunBaris();
                // Angka pada tombol pemilih bagian ikut berubah setelah data dimuat.
                SusunBarisBagian();
                SegarkanRingkasanBagian();
                StatusText = string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memuat data perangkat desa");
                StatusText = "Gagal memuat data perangkat desa.";
                await _messageService.ShowErrorAsync("Gagal memuat data perangkat desa.\n\n" + ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Susun baris seluruh tabel halaman: satu blok per jabatan menurut
        /// bagiannya — perangkat desa urut Kepala Desa sampai Kadus, bagian lain
        /// menurut daftar jabatnya — dan di dalam bloknya orang yang masih
        /// memegang jabatan didahulukan. Jabatan tanpa orang tetap mendapat
        /// baris kosong siap isi.
        /// </summary>
        /// <summary>
        /// Susun baris tabel yang sedang aktif saja (lazy). Dulu semua 12 tabel
        /// dibangun tiap muat (~13× pemindaian + Clear/Add per baris) walau
        /// hanya satu yang tampil — kini bangun saat bagian dibuka.
        /// </summary>
        private void SusunBaris()
        {
            SusunBarisBagianAktif();
        }

        private void SusunBarisBagianAktif()
        {
            var resmi = _semua.Where(p => JabatanPerangkat.AdalahResmi(p.Jabatan)).ToList();
            var rtrw = _semua.Where(p => JabatanPerangkat.Kelompok(p.Jabatan) == "RT/RW").ToList();
            var bpd = _semua.Where(p => JabatanPerangkat.Kelompok(p.Jabatan) == "BPD").ToList();
            var linmas = _semua.Where(p => JabatanPerangkat.Kelompok(p.Jabatan) == "LINMAS").ToList();
            var posyandu = _semua.Where(p => JabatanPerangkat.Kelompok(p.Jabatan) == "POSYANDU").ToList();
            var kaderkpm = _semua.Where(p => JabatanPerangkat.Kelompok(p.Jabatan) == "KADER KPM").ToList();
            var kaderstunting = _semua.Where(p => JabatanPerangkat.Kelompok(p.Jabatan) == "KADER STUNTING").ToList();
            var staf = _semua.Where(p => JabatanPerangkat.Kelompok(p.Jabatan) == "STAF DESA").ToList();
            var op = _semua.Where(p => JabatanPerangkat.Kelompok(p.Jabatan) == "OPERATOR DESA").ToList();
            var pkk = _semua.Where(p => JabatanPerangkat.Kelompok(p.Jabatan) == "PKK").ToList();
            var lpm = _semua.Where(p => JabatanPerangkat.Kelompok(p.Jabatan) == "LPM").ToList();
            var karang = _semua.Where(p => JabatanPerangkat.Kelompok(p.Jabatan) == "KARANG TARUNA").ToList();

            switch (_bagianAktif)
            {
                case BagianPerangkat.RtRw: IsiTabel(ItemsRtRw, DaftarJabatanRtRw, rtrw); break;
                case BagianPerangkat.Bpd: IsiTabel(ItemsBpd, DaftarJabatanBpd, bpd); break;
                case BagianPerangkat.Linmas: IsiTabel(ItemsLinmas, DaftarJabatanLinmas, linmas); break;
                case BagianPerangkat.Posyandu: IsiTabel(ItemsPosyandu, DaftarJabatanPosyandu, posyandu); break;
                case BagianPerangkat.KaderKpm: IsiTabel(ItemsKaderKpm, new[] { JabatanPerangkat.KaderKPM }, kaderkpm); break;
                case BagianPerangkat.KaderStunting: IsiTabel(ItemsKaderStunting, new[] { JabatanPerangkat.KaderStunting }, kaderstunting); break;
                case BagianPerangkat.StafDesa: IsiTabel(ItemsStafDesa, new[] { JabatanPerangkat.Staf }, staf); break;
                case BagianPerangkat.OperatorDesa: IsiTabel(ItemsOperatorDesa, new[] { JabatanPerangkat.OperatorDesa }, op); break;
                case BagianPerangkat.Pkk: IsiTabel(ItemsPkk, DaftarJabatanPkk, pkk); break;
                case BagianPerangkat.Lpm: IsiTabel(ItemsLpm, DaftarJabatanLpm, lpm); break;
                case BagianPerangkat.KarangTaruna: IsiTabel(ItemsKarangTaruna, DaftarJabatanKarangTaruna, karang); break;
                default: IsiTabel(Items, JabatanPerangkat.Resmi, resmi); break;
            }
        }

        /// <summary>
        /// Isi satu tabel halaman ini: satu blok per jabatan menurut
        /// <paramref name="urutan"/>, orang yang masih memegang jabatan
        /// didahulukan, dan jabatan tanpa orang tetap mendapat baris kosong siap
        /// isi. Dipakai seluruh bagian halaman, termasuk Perangkat Desa dan RT/RW.
        ///
        /// Penghuni sebenarnya menentukan baris penanda: jabatan yang belum ada
        /// orangnya tetap mendapat baris kosong siap isi.
        /// </summary>
        private void IsiTabel(
            ObservableCollection<BarisPerangkatViewModel> tabel,
            IReadOnlyList<string> urutan,
            IReadOnlyList<PerangkatDesa> orang)
        {
            var semuaPerJabatan = Kelompokkan(orang);

            tabel.Clear();
            foreach (var jabatan in urutan)
            {
                if (semuaPerJabatan.TryGetValue(jabatan, out var isi))
                {
                    foreach (var p in isi) tabel.Add(new BarisPerangkatViewModel(p));
                }

                if (!semuaPerJabatan.ContainsKey(jabatan))
                {
                    tabel.Add(new BarisPerangkatViewModel(jabatan));
                }
            }

            // Jabatan di luar daftar bagian tetap ditampilkan sebagai barisnya
            // sendiri. IsiTabel mencari memakai nama konstanta, sedangkan kamus
            // dikunci oleh JabatanTampil orang itu. Nilai warisan yang tidak punya
            // padanan di JabatanPerangkat.NamaLama (mis. "KEPALA DUSUN" untuk
            // KADUS) tidak akan pernah cocok, sehingga orang tersebut HILANG dari
            // tabel, kartu angka, ekspor Excel, dan cetak PDF — padahal barisnya
            // ada di database. Baris sisa di bawah menutup kebocoran itu.
            var sudahTampil = new HashSet<string>(urutan, StringComparer.Ordinal);
            foreach (var sisa in semuaPerJabatan.Keys
                .Where(k => !sudahTampil.Contains(k))
                .OrderBy(k => k, StringComparer.CurrentCulture))
            {
                foreach (var p in semuaPerJabatan[sisa])
                {
                    tabel.Add(new BarisPerangkatViewModel(p));
                }
            }
        }

        /// <summary>Kelompokkan orang per jabatan; urut: yang masih memegang
        /// jabatan didahulukan, lalu menurut nama. Dipakai seluruh tabel halaman ini.</summary>
        private static Dictionary<string, List<PerangkatDesa>> Kelompokkan(IEnumerable<PerangkatDesa> orang) =>
            orang
                .GroupBy(p => p.JabatanTampil, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(p => p.MasihMemegangJabatan)
                          .ThenBy(p => p.Nama, StringComparer.CurrentCulture)
                          .ToList(),
                    StringComparer.Ordinal);

        /// <summary>
        /// Segarkan seluruh turunan kepala bagian: petunjuk, jumlah orang,
        /// jumlah jabatan, dan jabatan yang belum terisi.
        /// </summary>
        private void SegarkanRingkasanBagian()
        {
            OnPropertyChanged(nameof(StatusBagianAktif));
            OnPropertyChanged(nameof(JumlahOrangBagianAktif));
            OnPropertyChanged(nameof(JumlahJabatanBagianAktif));
            OnPropertyChanged(nameof(JumlahKosongBagianAktif));
        }

        /// <summary>
        /// Pindah bagian halaman. Semua tabel sudah ada di memori, jadi tidak
        /// ada pembacaan database ulang.
        /// </summary>
        private void PilihBagian(BagianPerangkat bagian)
        {
            if (_bagianAktif == bagian) return;

            _bagianAktif = bagian;
            SusunBarisBagianAktif();
            OnPropertyChanged(nameof(NamaBagianAktifEnum));
            OnPropertyChanged(nameof(IkonBagianAktif));
            OnPropertyChanged(nameof(TampilkanPerangkatDesa));
            OnPropertyChanged(nameof(ItemsAktif));
            OnPropertyChanged(nameof(JudulBagianAktif));
            OnPropertyChanged(nameof(JudulForm));
            SegarkanRingkasanBagian();

            // Pesan hasil aksi bagian sebelumnya tidak relevan lagi di sini.
            StatusText = string.Empty;
            SegarkanPerintah();
        }

        // ---------- baris tabel ----------

        private async Task HapusAsync(BarisPerangkatViewModel? baris)
        {
            var data = baris?.Sumber;
            if (data is null) return;

            bool lanjut = await _messageService.ShowConfirmationAsync(
                "Hapus Data Perangkat",
                $"Hapus data \"{data.Nama}\" ({data.JabatanTampil})?\n\n"
                + "Data surat yang sudah pernah dibuat tidak ikut berubah.");

            if (!lanjut) return;

            try
            {
                IsBusy = true;
                bool terhapus = await _layanan.HapusAsync(data.ID).ConfigureAwait(true);
                if (!terhapus)
                {
                    await _messageService.ShowWarningAsync("Data itu sudah tidak ada. Daftar akan dimuat ulang.");
                    await LoadAsync().ConfigureAwait(true);
                    return;
                }

                _activityLog.Log("PERANGKAT", $"{data.Nama} ({data.JabatanTampil})", "Hapus", $"ID {data.ID}");

                // Berkas SK terarsip ikut dibersihkan agar tidak tertinggal sebagai
                // berkas yatim di folder setelah baris datanya hilang. Hapus aman
                // dipanggil walau berkasnya sudah tidak ada atau sedang dibuka.
                BerkasSkPerangkat.Hapus(data.BerkasSK);

                await LoadAsync().ConfigureAwait(true);
                StatusText = $"Data {data.Nama} dihapus.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menghapus perangkat desa #{ID}", data.ID);
                await _messageService.ShowErrorAsync("Gagal menghapus data perangkat.\n\n" + ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }


    }
}
