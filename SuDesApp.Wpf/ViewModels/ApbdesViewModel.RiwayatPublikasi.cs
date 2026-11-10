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
        // ===================== Riwayat & data publikasi =====================

        /// <summary>Baris perbandingan satu tahun anggaran: arsip dokumen berdamping dengan angka publikasi yang diimpor.</summary>
        public sealed class RiwayatItem
        {
            public int Tahun { get; init; }
            public string AcuanLabel { get; init; } = "";
            public long Anggaran { get; init; }
            public long Realisasi { get; init; }
            public bool AdaArsip { get; init; }
            public bool AdaPublikasi { get; init; }
            public long PublikasiAnggaran { get; init; }
            public long PublikasiRealisasi { get; init; }

            public string TahunLabel => Tahun.ToString(CultureInfo.InvariantCulture);
            public string AcuanTampil => AdaArsip ? AcuanLabel : "—";
            public string AnggaranArsipTampil => AdaArsip ? Rupiah(Anggaran) : "—";
            public string RealisasiArsipTampil => AdaArsip ? Rupiah(Realisasi) : "—";
            public string PublikasiAnggaranTampil => AdaPublikasi ? Rupiah(PublikasiAnggaran) : "—";
            public string PublikasiRealisasiTampil => AdaPublikasi ? Rupiah(PublikasiRealisasi) : "—";
            public string SelisihTampil => AdaArsip && AdaPublikasi
                ? Rupiah(Anggaran - PublikasiAnggaran)
                : "—";
        }

        /// <summary>Daftar baris riwayat (tahun turun) untuk panel perbandingan.</summary>
        public ObservableCollection<RiwayatItem> Riwayat { get; } = new();

        public bool AdaRiwayat => Riwayat.Count > 0;

        public string RiwayatRingkas => Riwayat.Count == 0
            ? "Belum ada data riwayat."
            : $"Perbandingan {Riwayat.Count} tahun anggaran (tertua masuk turun).";

        /// <summary>
        /// Hitung riwayat dari arsip: per tahun, dokumen acuan arsip (Perubahan
        /// terbaru, lalu Awal, lalu dokumen dengan rincian terbanyak) berdamping
        /// dengan dokumen "Data Publikasi Pusat" bila ada. Metode ini murni —
        /// dipanggil SegarkanRiwayat dan diuji langsung dari proyek uji.
        /// </summary>
        internal static List<RiwayatItem> HitungRiwayat(IReadOnlyList<ApbdesStore.Dokumen> semua)
        {
            var hasil = new List<RiwayatItem>();
            foreach (var grup in semua.GroupBy(d => d.Tahun).OrderByDescending(g => g.Key))
            {
                var kandidat = grup
                    .Where(d => d.Pos.Count > 0 && !d.Jenis.Equals(ApbdesStore.JenisPublikasi, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var acuan = kandidat
                    .Where(d => d.Jenis.Equals("APBDes Perubahan", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(d => d.PerubahanKe)
                    .FirstOrDefault()
                    ?? kandidat.FirstOrDefault(d => d.Jenis.Equals("APBDes Awal", StringComparison.OrdinalIgnoreCase))
                    ?? kandidat.OrderByDescending(d => d.Pos.Count).FirstOrDefault();
                var pub = grup.FirstOrDefault(d =>
                    d.Pos.Count > 0 && d.Jenis.Equals(ApbdesStore.JenisPublikasi, StringComparison.OrdinalIgnoreCase));

                if (acuan == null && pub == null) continue;

                hasil.Add(new RiwayatItem
                {
                    Tahun = grup.Key,
                    AcuanLabel = acuan == null ? "" : ApbdesStore.LabelJenis(acuan.Jenis, acuan.PerubahanKe),
                    Anggaran = acuan?.Pos.Sum(p => p.Anggaran) ?? 0,
                    Realisasi = acuan?.Pos.Sum(p => p.Realisasi) ?? 0,
                    AdaArsip = acuan != null,
                    AdaPublikasi = pub != null,
                    PublikasiAnggaran = pub?.Pos.Sum(p => p.Anggaran) ?? 0,
                    PublikasiRealisasi = pub?.Pos.Sum(p => p.Realisasi) ?? 0,
                });
            }
            return hasil;
        }

        private void SegarkanRiwayat()
        {
            var hitung = HitungRiwayat(_semua);
            Riwayat.Clear();
            foreach (var baris in hitung) Riwayat.Add(baris);
            OnPropertyChanged(nameof(AdaRiwayat));
            OnPropertyChanged(nameof(RiwayatRingkas));
        }

        /// <summary>Rangkuman satu tahun anggaran dari baris publikasi yang diimpor.</summary>
        internal sealed record RangkumanPublikasi(
            int Tahun,
            string NamaDesa,
            long? AngPendapatan, long? RealPendapatan,
            long? AngBelanja, long? RealBelanja,
            long? AngPembiayaan, long? RealPembiayaan,
            long? DanaDesa)
        {
            public bool Ada => AngPendapatan.HasValue || RealPendapatan.HasValue
                || AngBelanja.HasValue || RealBelanja.HasValue
                || AngPembiayaan.HasValue || RealPembiayaan.HasValue
                || DanaDesa.HasValue;
        }

        /// <summary>
        /// Kumpulkan baris publikasi per tahun. Bila berkas memuat beberapa desa,
        /// hanya baris desa yang terpilih (Pengaturan Surat, atau desa berbaris
        /// terbanyak bila Pengaturan Surat tidak cocok) yang dipakai, dan
        /// keputusannya dicatat supaya pengguna tahu apa yang terjadi. Bila tahun
        /// yang sama muncul beberapa baris, angka baris pertama yang berangka
        /// dipakai (baris teratas biasanya versi resmi).
        /// </summary>
        internal static List<RangkumanPublikasi> RangkaiTahunan(
            HasilBacaPublikasi hasil, string? namaDesaPilihan, out List<string> catatan)
        {
            catatan = new List<string>();
            var desaBerbeda = hasil.Baris
                .Select(b => b.NamaDesa.Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            string? desaTerpilih = null;
            IEnumerable<BarisPublikasi> baris = hasil.Baris;
            if (desaBerbeda.Count > 1)
            {
                desaTerpilih = namaDesaPilihan != null && desaBerbeda.Contains(namaDesaPilihan, StringComparer.OrdinalIgnoreCase)
                    ? namaDesaPilihan
                    : desaBerbeda
                        .OrderByDescending(besi => hasil.Baris.Count(x => x.NamaDesa.Equals(besi, StringComparison.OrdinalIgnoreCase)))
                        .First();
                baris = hasil.Baris.Where(b =>
                    string.IsNullOrWhiteSpace(b.NamaDesa) ||
                    b.NamaDesa.Equals(desaTerpilih, StringComparison.OrdinalIgnoreCase));
                catatan.Add(desaTerpilih.Equals(namaDesaPilihan, StringComparison.OrdinalIgnoreCase)
                    ? $"berkas memuat {desaBerbeda.Count} desa — hanya desa \"{desaTerpilih}\" (Pengaturan Surat) yang diimpor."
                    : $"berkas memuat {desaBerbeda.Count} desa dan tidak cocok dengan Pengaturan Surat — desa \"{desaTerpilih}\" yang diimpor.");
            }
            else if (desaBerbeda.Count == 1)
            {
                desaTerpilih = desaBerbeda[0];
            }

            var hasilTahunan = new List<RangkumanPublikasi>();
            foreach (var grup in baris.GroupBy(b => b.Tahun).OrderByDescending(g => g.Key))
            {
                long? Terakhir(Func<BarisPublikasi, long?> ambil)
                {
                    foreach (var bari in grup)
                    {
                        if (ambil(bari).HasValue) return ambil(bari);
                    }
                    return null;
                }
                var rangkum = new RangkumanPublikasi(
                    grup.Key,
                    desaTerpilih ?? "",
                    Terakhir(b => b.AnggaranPendapatan), Terakhir(b => b.RealisasiPendapatan),
                    Terakhir(b => b.AnggaranBelanja), Terakhir(b => b.RealisasiBelanja),
                    Terakhir(b => b.AnggaranPembiayaan), Terakhir(b => b.RealisasiPembiayaan),
                    Terakhir(b => b.DanaDesa));
                if (rangkum.Ada) hasilTahunan.Add(rangkum);
            }
            return hasilTahunan;
        }

        /// <summary>
        /// Impor berkas data publikasi pemerintah (Excel/CSV hasil unduhan portal
        /// resmi — PELITA, SIKD Dana Desa, PPID, atau pemda) ke panel riwayat.
        /// Jalur UTAMA anggaran terpublikasi adalah <see cref="MuatDariPusatAsync"/>
        /// (otomatis dari situs pusat); impor ini tetap disediakan sebagai cadangan
        /// bila situs pusat tak terjangkau atau bentuk halamannya berubah.
        /// </summary>
        private async Task ImporPublikasiAsync()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Pilih berkas data publikasi APBDes (Excel/CSV)",
                Filter = "Data publikasi (Excel/CSV)|*.xlsx;*.csv"
                         + "|Excel (.xlsx)|*.xlsx|CSV (.csv)|*.csv",
                CheckFileExists = true,
            };
            if (dialog.ShowDialog() != true) return;

            await TerimaDataPublikasiAsync(dialog.FileName);
        }

        internal async Task TerimaDataPublikasiAsync(string jalur)
        {
            SedangMemuat = true;
            Pesan = string.Empty;
            PesanGalat = string.Empty;
            PesanPeringatan = string.Empty;
            try
            {
                var hasil = await Task.Run(() => ApbdesPublikasiReader.Baca(jalur));

                string? namaDesa = null;
                if (_desaRepo != null)
                {
                    try { namaDesa = (await _desaRepo.GetInfoDesaAsync()).NamaDesa?.Trim(); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Gagal membaca nama desa saat impor publikasi"); }
                }

                var tahunan = RangkaiTahunan(hasil, namaDesa, out var catatan);
                int diimpor = 0;
                var tahunTersentuh = new List<int>();
                foreach (var rangkum in tahunan)
                {
                    if (RangkaiDokumenPublikasi(rangkum, "berkas publikasi \"" + hasil.NamaBerkas + "\""))
                    {
                        diimpor++;
                        tahunTersentuh.Add(rangkum.Tahun);
                    }
                }

                if (diimpor == 0)
                {
                    PesanPeringatan = "Berkas terbaca tetapi tidak ada angka APBDes yang bisa dipetakan — "
                        + "periksa kolom anggaran/realisasi di berkas publikasinya.";
                    return;
                }

                if (!ApbdesStore.Simpan(_semua))
                {
                    PesanGalat = "Data publikasi tertata di halaman tetapi gagal disimpan ke arsip komputer — "
                        + "periksa disk/hak akses, lalu tekan Simpan.";
                    return;
                }

                SegarkanDaftar();
                SegarkanDaftarDokumenPublikasi();

                var tahunTeks = string.Join(", ", tahunTersentuh.Select(t => t.ToString(CultureInfo.InvariantCulture)));
                Pesan = $"Data publikasi untuk tahun {tahunTeks} diimpor dari \"{hasil.NamaBerkas}\" "
                    + $"({hasil.Baris.Count} baris terbaca) — lihat bagian Riwayat untuk perbandingannya.";
                if (catatan.Count > 0) PesanPeringatan = string.Join(" ", catatan);
            }
            catch (NotSupportedException ex)
            {
                PesanGalat = ex.Message;
            }
            catch (InvalidDataException ex)
            {
                _logger.LogWarning(ex, "Berkas publikasi tidak dikenali");
                PesanGalat = ex.Message + "\n\nBerkasnya tidak diubah — dokumen APBDes yang tersimpan aman.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal mengimpor berkas publikasi APBDes");
                PesanGalat = "Gagal mengimpor berkas publikasi: " + ex.Message;
            }
            finally
            {
                SedangMemuat = false;
            }
        }

        /// <summary>Buat/segarkan satu dokumen "Data Publikasi Pusat" dari rangkuman satu tahun.</summary>
        /// <param name="sumber">Asal angka untuk keterangan dokumen — mis.
        /// <c>berkas publikasi "…xlsx"</c> (impor manual) atau <see cref="SumberProdeskel"/> (tarik otomatis).</param>
        private bool RangkaiDokumenPublikasi(RangkumanPublikasi r, string sumber)
        {
            if (!r.Ada) return false;

            var dok = _semua.FirstOrDefault(d => d.Tahun == r.Tahun
                && d.Jenis.Equals(ApbdesStore.JenisPublikasi, StringComparison.OrdinalIgnoreCase));
            bool baru = dok == null;
            if (dok == null)
            {
                dok = new ApbdesStore.Dokumen { Tahun = r.Tahun, Jenis = ApbdesStore.JenisPublikasi };
                _semua.Add(dok);
            }

            dok.Pos.Clear();
            void Tambah(long? anggaran, long? realisasi, string kode, string uraian, string keterangan)
            {
                if (anggaran.HasValue || realisasi.HasValue)
                {
                    dok.Pos.Add(new ApbdesStore.Pos
                    {
                        Kode = kode,
                        Uraian = uraian,
                        Anggaran = anggaran ?? 0,
                        Realisasi = realisasi ?? 0,
                        Keterangan = keterangan,
                    });
                }
            }

            Tambah(r.AngPendapatan, r.RealPendapatan, "4", "Pendapatan", "angka publikasi pemerintah");
            Tambah(r.AngBelanja, r.RealBelanja, "5", "Belanja", "angka publikasi pemerintah");
            Tambah(r.AngPembiayaan, r.RealPembiayaan, "6", "Pembiayaan", "angka publikasi pemerintah");
            if (r.DanaDesa.HasValue)
            {
                dok.Pos.Add(new ApbdesStore.Pos
                {
                    Kode = "",
                    Uraian = "Dana Desa",
                    Anggaran = r.DanaDesa.Value,
                    Keterangan = "angka alokasi dari publikasi pemerintah",
                });
            }

            dok.Judul = string.IsNullOrWhiteSpace(r.NamaDesa)
                ? "Data publikasi TA " + r.Tahun.ToString(CultureInfo.InvariantCulture)
                : "Data publikasi TA " + r.Tahun.ToString(CultureInfo.InvariantCulture) + " — " + r.NamaDesa;
            dok.Keterangan = "Diimpor dari " + sumber + " "
                + "lewat panel Riwayat APBDES sebagai pembanding tahun-tahun sebelumnya.";
            dok.Tanggal = _jam().ToString("yyyy-MM-dd");
            dok.Diubah = _jam().ToString("s");
            return baru;
        }

        /// <summary>Hasil satu tahun dari publikasi pusat → rangkuman tahunan yang
        /// sama dengan jalur impor Excel (pemetaan tetap satu salinan).</summary>
        internal static RangkumanPublikasi RangkaiPusat(HasilApbdesProdeskel h, string namaDesa) =>
            new(h.Tahun, namaDesa,
                h.AngPendapatan, h.RealPendapatan,
                h.AngBelanja, h.RealBelanja,
                h.AngPembiayaan, h.RealPembiayaan,
                h.DanaDesa);

        /// <summary>
        /// JALUR UTAMA panel Riwayat: tarik anggaran & realisasi yang SUDAH
        /// dipublikasikan pemerintah pusat (Prodeskel Kemendagri) untuk desa pada
        /// Pengaturan Surat — lima tahun terakhir, berhenti setelah dua tahun
        /// berturut-turut tidak ditemukan atau format halaman tak dikenali.
        /// Angka disimpan sebagai dokumen "Data Publikasi Pusat" per tahun lewat
        /// jalur yang sama dengan impor Excel, sehingga panel Riwayat langsung
        /// membandingkan arsip vs publikasi. Aman dipanggil berkali-kali (tombol),
        /// tidak pernah melempar; keadaan tampil di <see cref="StatusPusat"/>.
        /// </summary>
        public async Task MuatDariPusatAsync()
        {
            if (SedangMemuat || SedangMuatPusat) return;

            if (_klienPusat == null)
            {
                StatusPusat = "Sumber data pusat tidak terpasang — pakai Impor Publikasi (Excel/CSV) sebagai cadangan.";
                return;
            }

            SedangMuatPusat = true;
            StatusPusat = "Menghubungi situs publikasi pusat (Prodeskel)…";
            try
            {
                string namaDesa = "", kecamatan = "", kabupaten = "";
                if (_desaRepo != null)
                {
                    try
                    {
                        var info = await _desaRepo.GetInfoDesaAsync();
                        namaDesa = info.NamaDesa?.Trim() ?? string.Empty;
                        kecamatan = info.Kecamatan?.Trim() ?? string.Empty;
                        kabupaten = info.Kabupaten?.Trim() ?? string.Empty;
                    }
                    catch (Exception ex) { _logger.LogWarning(ex, "Gagal membaca data desa saat menarik publikasi pusat"); }
                }

                if (namaDesa.Length == 0 || kecamatan.Length == 0 || kabupaten.Length == 0)
                {
                    StatusPusat = "Data desa belum lengkap. Lengkapi nama desa, kecamatan, dan kabupaten pada "
                        + "Pengaturan Surat — kode desa pusat ditentukan dari sana.";
                    return;
                }

                if (_wilayah == null)
                {
                    StatusPusat = "Data wilayah bawaan tidak tersedia, jadi kode desa tidak bisa ditentukan.";
                    return;
                }

                var pencocokan = SuDesApp.Wilayah.WilayahPencocokan.CariRantai(_wilayah, namaDesa, kecamatan, kabupaten);
                if (pencocokan.Status != SuDesApp.Wilayah.StatusPencocokanWilayah.Ditemukan || pencocokan.Rantai == null)
                {
                    StatusPusat = "Desa pada Pengaturan Surat belum dikenali dari data wilayah — pilih "
                        + "provinsi → kabupaten → kecamatan → desa dari daftar wilayah supaya kode desa terbaca.";
                    return;
                }

                var kode = IdmViewModel.KodeAngka(pencocokan.Rantai.Desa.Kode);
                if (kode.Length != 10)
                {
                    StatusPusat = "Kode desa \"" + pencocokan.Rantai.Desa.Kode + "\" pada data wilayah tidak "
                        + "berbentuk 10 digit, jadi tidak bisa dipakai mencari publikasi pusat.";
                    return;
                }

                int tahunAwal = TahunBerjalan - 1;
                int beruntunKosong = 0, diambil = 0;
                var tahunTersentuh = new List<int>();
                string? pesanHenti = null;

                for (int i = 0; i < 5 && beruntunKosong < 2; i++)
                {
                    int tahun = tahunAwal - i;
                    var muatan = await _klienPusat.MuatAsync(kode, tahun);
                    if (muatan.Status == StatusMuatApbdesPusat.Berhasil && muatan.Hasil != null)
                    {
                        beruntunKosong = 0;
                        if (RangkaiDokumenPublikasi(RangkaiPusat(muatan.Hasil, namaDesa), SumberProdeskel))
                        {
                            diambil++;
                            tahunTersentuh.Add(tahun);
                        }
                        continue;
                    }

                    if (muatan.Status == StatusMuatApbdesPusat.TidakDitemukan)
                    {
                        beruntunKosong++;
                        continue;
                    }

                    // Format tak dikenali / galat jaringan: berhenti — jangan terus
                    // menebak tahun-tahun lain dengan halaman yang sama.
                    pesanHenti = muatan.Pesan;
                    break;
                }

                if (diambil == 0)
                {
                    StatusPusat = pesanHenti
                        ?? "Situs pusat belum memuat anggaran desa ini (5 tahun terakhir) — "
                         + "gunakan Impor Publikasi (Excel/CSV) sebagai cadangan.";
                    return;
                }

                if (!ApbdesStore.Simpan(_semua))
                {
                    StatusPusat = "Anggaran pusat terbaca tetapi gagal disimpan ke arsip komputer — periksa disk/hak akses.";
                    return;
                }

                SegarkanDaftar();
                SegarkanDaftarDokumenPublikasi();
                var tahunTeks = string.Join(", ", tahunTersentuh.Select(t => t.ToString(CultureInfo.InvariantCulture)));
                StatusPusat = "Anggaran publikasi pusat dimuat untuk tahun " + tahunTeks + " — sumber: " + SumberProdeskel + ".";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menarik publikasi APBDes dari situs pusat");
                StatusPusat = "Gagal menghubungi situs publikasi pusat: " + ex.Message;
            }
            finally
            {
                SedangMuatPusat = false;
            }
        }

        private void SegarkanDaftarDokumenPublikasi()
        {
            foreach (var item in Dokumen)
            {
                item.PerbaruiRingkasan();
                item.PerbaruiBerkas();
            }
            OnPropertyChanged(nameof(TidakAdaDokumen));
        }


    }
}
