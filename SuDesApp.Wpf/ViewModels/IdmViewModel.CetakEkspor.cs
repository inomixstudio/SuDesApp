using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.GeneratorPdf;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wilayah;
using SuDesApp.Wpf.Controls;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Utilities;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class IdmViewModel : ObservableObject
    {
        // ===================== Cetak / ekspor PDF =====================

        /// <summary>
        /// Susun laporan IDM dari data yang SEDANG tampil (termasuk data cadangan
        /// offline), render PDF, lalu buka pratinjau. Tidak mengambil data baru:
        /// yang dicetak persis yang dilihat pengguna di layar.
        /// </summary>
        public async Task BuatPdfAsync()
        {
            if (_penjaga is { CetakTerkunci: true })
            {
                PesanGalat = SuDesApp.Services.PenjagaLisensi.PesanCetakTerkunci;
                if (_pesan is not null)
                {
                    await _pesan.ShowInfoAsync(
                        SuDesApp.Services.PenjagaLisensi.PesanCetakTerkunci);
                }
                return;
            }

            if (Hasil is not { } hasil) return;

            if (_navigasi == null || _previewFactory == null)
            {
                PesanGalat = "Pencetakan PDF belum tersedia pada halaman ini.";
                return;
            }

            PesanStatus = "Menyiapkan PDF…";
            try
            {
                var desa = await AmbilDesaAsync();
                var data = LaporanIdmBuilder.Susun(
                    hasil, _data?.Seri.Tahun, desa, _data?.WaktuMuat, _jam());

                using var penampung = new MemoryStream();
                await Task.Run(() => new LaporanIdmGenerator().GenerateLaporanPdf(penampung, data))
                    .ConfigureAwait(true);

                string jalur = await PratinjauPdf
                    .SimpanAsync(_appConfig, penampung.ToArray(), data.NamaBerkas)
                    .ConfigureAwait(true);

                string judul = "Laporan IDM — " + data.NamaDesa
                               + (data.Tahun > 0 ? " (" + data.Tahun + ")" : string.Empty);
                _navigasi.Navigate(_previewFactory(judul, jalur));
                PesanStatus = string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat PDF laporan IDM");
                PesanStatus = string.Empty;
                PesanGalat = "Gagal membuat PDF laporan IDM: " + ex.Message;

                if (_pesan != null)
                {
                    await _pesan.ShowErrorAsync(PesanGalat);
                }
            }
        }

        /// <summary>Data desa untuk kop dan blok tanda tangan; gagal baca tidak memblokir cetak.</summary>
        private async Task<DesaData> AmbilDesaAsync()
        {
            try
            {
                return await _pengaturan.GetSettingsAsync() ?? new DesaData();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal membaca data desa untuk laporan IDM; laporan tetap dibuat.");
                return new DesaData();
            }
        }

        private void BukaSitus()
        {
            try
            {
                Process.Start(new ProcessStartInfo(IdmKemendesaKlien.AlamatSitus) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Tidak bisa membuka situs IDM pusat");
                PesanGalat = "Tidak bisa membuka peramban: " + ex.Message;
            }
        }

        private void BukaDokumentasiIndeksDesa()
        {
            const string berkas = "indeks-desa-2025.md";
            var jalur = BerkasDokumentasi.Cari(berkas);
            if (jalur is null)
            {
                PesanGalat = "Dokumentasi sumber Indeks Desa 2025 tidak ditemukan di folder docs aplikasi.";
                return;
            }

            if (_navigasi is null || _dokumenFactory is null)
            {
                PesanGalat = "Pembaca dokumentasi tidak tersedia pada halaman ini.";
                return;
            }

            try
            {
                _navigasi.Navigate(_dokumenFactory(
                    "Sumber resmi Indeks Desa 2025",
                    jalur,
                    () => _navigasi.Navigate(this)));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal membuka dokumentasi sumber Indeks Desa 2025");
                PesanGalat = "Dokumentasi sumber Indeks Desa 2025 tidak bisa dibuka: " + ex.Message;
            }
        }

        /// <summary>Buka halaman resmi terbitan pusat (bukan tautan dataset daerah).</summary>
        private void BukaTerbitan()
        {
            var terbitan = TerbitanBaru;
            if (terbitan == null) return;

            try
            {
                Process.Start(new ProcessStartInfo(terbitan.Alamat) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Tidak bisa membuka terbitan resmi IDM {Alamat}", terbitan.Alamat);
                PesanGalat = "Tidak bisa membuka peramban: " + ex.Message;
            }
        }

        /// <summary>
        /// Buka satu tautan sumber resmi di peramban bawaan. Kegagalan
        /// dilaporkan di halaman, bukan dialog: pesan cukup satu baris.
        /// </summary>
        private void BukaTautan(SumberResmiIdmTampil? sumber)
        {
            if (sumber is null) return;

            var galat = TautanWeb.Buka(sumber.Alamat);
            if (galat is null)
            {
                PesanStatus = "Tautan resmi dibuka di peramban: " + sumber.Judul;
                _logger.LogInformation("Tautan resmi IDM dibuka: {Alamat}", sumber.Alamat);
                return;
            }

            PesanGalat = "Tidak bisa membuka peramban: " + galat;
            _logger.LogWarning("Gagal membuka tautan resmi IDM {Alamat}: {Galat}", sumber.Alamat, galat);
        }

        private static string PesanPencocokan(HasilPencocokanWilayah hasil) => hasil.Status switch
        {
            StatusPencocokanWilayah.TanpaData =>
                "Data wilayah bawaan tidak terbaca, jadi kode desa tidak bisa ditentukan. "
                + "Periksa pemasangan aplikasi (folder Data/Wilayah).",

            StatusPencocokanWilayah.Ambigu =>
                "Nama desa pada Pengaturan Surat cocok dengan " + hasil.JumlahKandidat
                + " wilayah berbeda. Pilih desa dari daftar wilayah (bukan diketik) supaya data IDM "
                + "yang tampil benar-benar milik desa ini.",

            _ =>
                "Desa pada Pengaturan Surat belum dikenali dari data wilayah bawaan. "
                + "Buka Pengaturan Surat lalu pilih provinsi → kabupaten/kota → kecamatan → desa dari daftar."
        };

        /// <summary>"32.15.02.2001" → "3215022001" (bentuk yang dipakai situs pusat).</summary>
        internal static string KodeAngka(string? kode) =>
            new((kode ?? string.Empty).Where(char.IsDigit).ToArray());

        /// <summary>Satu baris tabel yang siap ditampilkan (tanpa logika tambahan).</summary>
        public sealed class BarisTampil
        {
            public int? Nomor { get; init; }
            public string NomorTampil { get; init; } = "";
            public string Bidang { get; init; } = "";
            public string Indikator { get; init; } = "";
            public string Skor { get; init; } = "";
            public string Keterangan { get; init; } = "";
            public string Kegiatan { get; init; } = "";
            public string Pihak { get; init; } = "";
            public bool Ringkasan { get; init; }

            /// <summary>Baris subtotal resmi (IKS/IKE/IKL/IDM) ditebalkan seperti di situs pusat.</summary>
            public FontWeight Ketebalan => Ringkasan ? FontWeights.Bold : FontWeights.Normal;

            public static BarisTampil Dari(BarisIdm baris) => new()
            {
                Nomor = baris.Nomor,
                NomorTampil = baris.Nomor?.ToString(CultureInfo.InvariantCulture) ?? "",
                Bidang = baris.Bidang,
                Indikator = baris.Indikator,
                Skor = baris.SkorTampil,
                Keterangan = baris.Keterangan ?? "",
                Kegiatan = baris.Kegiatan ?? "",
                Pihak = baris.Pihak ?? "",
                Ringkasan = baris.Ringkasan
            };
        }

        /// <summary>
        /// Satu tahun pada daftar chip "tahun yang diterbitkan pusat" — nilai
        /// tampilan belaka, dibangun ulang dari (<see cref="_data"/>,
        /// <see cref="_tahunDipilih"/>). Chip ini dapat diklik: memilih satu tahun
        /// mengganti rincian indikator tanpa memuat ulang dari pusat.
        ///
        /// <see cref="Ditampilkan"/> menandai tahun yang rinciannya sedang tampil;
        /// <see cref="Terbaru"/> menandai tahun terakhir yang terbit — dua hal
        /// berbeda yang tidak boleh saling menggantikan.
        /// </summary>
        public sealed record RiwayatTampil(
            int Tahun,
            string Skor,
            string Status,
            bool Ditampilkan = false,
            bool Terbaru = false);

        /// <summary>
        /// Satu pemuatan yang berhasil: desa, waktu, jalur pengambilan, dan seluruh
        /// tahunnya. Satu-satunya tempat asal-usul data disimpan; memilih tahun lain
        /// hanya memindahkan <see cref="_tahunDipilih"/> dan tidak pernah mengubah
        /// record ini.
        /// </summary>
        private sealed record DataIdm(
            string KodeDesa,
            DateTime? WaktuMuat,
            bool LewatHttp,
            IdmSeriTahun Seri);

        /// <summary>Satu langkah pada dialog progres pemuatan (gaya splash).</summary>
        public sealed class TahapMemuatTampil : ObservableObject
        {
            /// <summary>Nilai <c>Status</c>: langkah belum dimulai.</summary>
            public const string BelumStatus = "Belum";

            /// <summary>Nilai <c>Status</c>: langkah sedang berjalan (titik berdenyut).</summary>
            public const string AktifStatus = "Aktif";

            /// <summary>Nilai <c>Status</c>: langkah selesai (lingkaran terisi + centang).</summary>
            public const string SelesaiStatus = "Selesai";

            private string _teks = string.Empty;
            private string _status = BelumStatus;
            private bool _terakhir;

            public string Teks
            {
                get => _teks;
                set => SetProperty(ref _teks, value);
            }

            /// <summary>Langkah terakhir pada deret — XAML memakai ini untuk
            /// menyembunyikan garis penghubung setelah titik terakhir.</summary>
            public bool Terakhir
            {
                get => _terakhir;
                set => SetProperty(ref _terakhir, value);
            }

            /// <summary>"Belum" / "Aktif" / "Selesai" — dipakai pemicu XAML untuk
            /// titik denyut dan centang (pola sama dengan indikator tahap splash).</summary>
            public string Status
            {
                get => _status;
                set => SetProperty(ref _status, value);
            }
        }

    }
}
