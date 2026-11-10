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
        // ===================== Data pusat =====================

        /// <summary>
        /// Data tahun yang rinciannya sedang tampil — satu-satunya pembacaan
        /// "tahun aktif" di halaman ini. Null bila belum ada data atau tidak ada
        /// tahun yang dipilih (mis. desa ini hanya punya penilaian Indeks Desa 2025).
        /// </summary>
        private HasilIdm? Hasil => _data?.Seri.Cari(_tahunDipilih);

        public bool AdaData => Hasil != null || _indeks2025 != null;

        public bool AdaDataIdm => Hasil != null;

        public bool AdaIndeksDesa2025 => _indeks2025 != null;

        public string SkorTotalIndeksDesa2025 => _indeks2025?.TotalSkor.ToString(CultureInfo.InvariantCulture) ?? "—";

        public string NilaiIndeksDesa2025 => _indeks2025 == null
            ? "—"
            : _indeks2025.NilaiIndeks.ToString("0.00", CultureInfo.GetCultureInfo("id-ID")) + "%";

        public string StatusIndeksDesa2025 => _indeks2025?.Status ?? "—";

        /// <summary>
        /// Judul bagian riwayat IDM lama pada ringkasan. Tampil hanya ketika
        /// penilaian 2025 (Indeks Desa) sudah ada, supaya keping IKS/IKE/IKL di
        /// bawahnya tidak terbaca sebagai penilaian tahun 2025.
        /// </summary>
        public string JudulSeriLamaTampil => Hasil == null
            ? "RIWAYAT IDM LAMA"
            : "RIWAYAT IDM LAMA · S/D " + TahunTerbitTerbaru.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Catatan pada kartu tren: seri Indeks Desa 2025 tidak digabungkan ke
        /// grafik IDM lama, jadi penilaian 2025 disebut terpisah. Kosong bila
        /// dataset 2025 tidak memuat desa ini.
        /// </summary>
        public string CatatanTren2025Tampil => _indeks2025 == null
            ? string.Empty
            : "Penilaian 2025 memakai Indeks Desa (enam dimensi) dan ditampilkan pada ringkasan — seri IDM lama"
              + (Hasil == null
                  ? " tidak tersedia untuk desa ini."
                  : " berakhir di tahun " + TahunTerbitTerbaru.ToString(CultureInfo.InvariantCulture) + ".");

        /// <summary>Tanggal perubahan dataset menurut header Last-Modified dari sumber.</summary>
        public string TanggalPembaruanSumberIndeksDesa2025 => _indeks2025?.TanggalPembaruanSumber is { } tanggal
            ? "Pembaruan sumber: " + tanggal.ToLocalTime().ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("id-ID"))
            : "Tanggal pembaruan sumber tidak tersedia.";

        public IReadOnlyList<DimensiIndeksDesa2025> DimensiIndeksDesa2025 => _indeks2025 is not { } data
            ? Array.Empty<DimensiIndeksDesa2025>()
            : new[]
            {
                new DimensiIndeksDesa2025("Layanan dasar", data.LayananDasar),
                new DimensiIndeksDesa2025("Sosial", data.Sosial),
                new DimensiIndeksDesa2025("Ekonomi", data.Ekonomi),
                new DimensiIndeksDesa2025("Lingkungan", data.Lingkungan),
                new DimensiIndeksDesa2025("Aksesibilitas", data.Aksesibilitas),
                new DimensiIndeksDesa2025("Tata kelola pemerintahan", data.TataKelola),
            };

        /// <summary>Skor 13 sub-dimensi sebagaimana diterbitkan dataset resmi 2025.</summary>
        public IReadOnlyList<SubDimensiIndeksDesa2025> RincianSubDimensiIndeksDesa2025 =>
            _indeks2025?.SubDimensi ?? System.Array.Empty<SubDimensiIndeksDesa2025>();

        public string SumberIndeksDesa2025 => "Sumber: Dataset Indeks Desa 2025 — Kementerian Desa PDT (Satu Data Indonesia).";

        public string NamaDesaTampil => Hasil?.Identitas?.Desa is { Length: > 0 } nama
            ? nama
            : _namaDesa.Length > 0 ? _namaDesa : "—";

        /// <summary>Kecamatan, kabupaten, provinsi — dari situs pusat bila ada.</summary>
        public string WilayahTampil
        {
            get
            {
                if (Hasil?.Identitas is { } id)
                {
                    var bagian = new[] { id.Kecamatan, id.Kabupaten, id.Provinsi }
                        .Where(x => !string.IsNullOrWhiteSpace(x));
                    return string.Join(" · ", bagian);
                }

                var lokal = new[] { _kecamatan, _kabupaten, _provinsi }
                    .Where(x => !string.IsNullOrWhiteSpace(x));
                return string.Join(" · ", lokal);
            }
        }

        public string KodeDesaTampil => string.IsNullOrEmpty(_kodeDesa)
            ? "—"
            : "Kode desa " + _kodeDesa;

        /// <summary>
        /// Tahun penilaian yang rinciannya sedang tampil. Mengikuti chip tahun yang
        /// dipilih; setelah pemuatan, tahun terbaru yang terpilih. Bila desa ini
        /// juga punya penilaian Indeks Desa 2025, tahun seri lama ditandai supaya
        /// tidak terbaca sebagai penilaian 2025 (kartu Indeks Desa terpisah).
        /// </summary>
        public string TahunTampil
        {
            get
            {
                if (Hasil is not { } hasil)
                {
                    return _indeks2025 != null ? "Penilaian tahun 2025 · Indeks Desa" : "—";
                }

                string teks = "Penilaian tahun "
                              + hasil.Ringkasan.Tahun.ToString(CultureInfo.InvariantCulture);
                return _indeks2025 != null ? teks + " · seri IDM lama" : teks;
            }
        }

        /// <summary>Banyaknya tahun seri IDM yang berhasil dimuat pada sesi ini.</summary>
        public int JumlahTahunDimuat => _data?.Seri.Tahun.Count ?? 0;

        /// <summary>
        /// Tahun TERAKHIR data IDM yang datanya benar-benar terbit untuk desa ini
        /// — bukan tahun berjalan. Dipakai sebagai penanda "tahun terakhir" pada
        /// ringkasan tahun yang dimuat.
        /// </summary>
        public string TahunTerakhirIdmTampil => Hasil == null
            ? IdmTeks.Kosong
            : TahunTerbitTerbaru.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Kalimat yang menyebut berapa tahun IDM yang dimuat, rentangnya, dan
        /// tahun terakhir yang rinciannya sedang tampil. Halaman mengambil beberapa
        /// tahun sekaligus (tahun terbaru untuk rincian, sisanya untuk tren), jadi
        /// tanpa kalimat ini pembaca tidak tahu bahwa tahun-tahun lain ikut dimuat
        /// dan tahun mana yang sedang dibaca.
        /// </summary>
        public string RingkasanTahunDimuat
        {
            get
            {
                if (Hasil == null || _data is not { } data) return string.Empty;

                return data.Seri.RingkasanTahun();
            }
        }

        /// <summary>True bila ada daftar tahun untuk ditampilkan di bawah ringkasan.</summary>
        public bool AdaRiwayatTahun => Riwayat.Count > 0;

        /// <summary>
        /// Indikator "data terbaru yang terbit": dari catatan pemeriksaan portal
        /// terakhir (halaman atau pemantau latar), dan bila belum ada catatan
        /// dipakai tahun terbit terbaru pada deret yang dimuat. Patokannya selalu
        /// tahun TERBIT terakhir, bukan tahun yang sedang dibuka: memilih tahun
        /// lama bukan tanda halaman memakai data cadangan. Bila catatan justru
        /// menunjukkan tahun yang lebih baru daripada yang dimuat (mis. data
        /// cadangan), kalimatnya mengajak muat ulang.
        /// </summary>
        public string IndikatorTerbitTampil
        {
            get
            {
                int dimuat = TahunTerbitTerbaru;
                int? terbit = _pemantauan?.TahunTerbit ?? (dimuat > 0 ? dimuat : null);
                if (terbit == null) return string.Empty;

                // Penilaian 2025 tampil lewat Indeks Desa → label bagian ini
                // disebut "seri IDM lama" supaya tidak terbaca sebagai anggapan
                // bahwa 2024 adalah penilaian terbaru yang ada.
                string teks = (_indeks2025 != null
                        ? "Data terbaru seri IDM lama: "
                        : "Data terbaru yang terbit: ")
                               + terbit.Value.ToString(CultureInfo.InvariantCulture);
                if (dimuat > 0 && terbit.Value > dimuat)
                {
                    teks += " — halaman ini masih memuat tahun "
                            + dimuat.ToString(CultureInfo.InvariantCulture)
                            + " (data cadangan), muat ulang untuk memperbarui";
                }

                return teks;
            }
        }

        /// <summary>Tanggal pemeriksaan portal: catatan pemantauan, atau waktu data dimuat.</summary>
        public string PemeriksaanTampil
        {
            get
            {
                DateTime? kapan = _pemantauan?.WaktuPeriksa ?? _data?.WaktuMuat;
                return kapan == null
                    ? string.Empty
                    : "Diperiksa: " + kapan.Value.ToString(
                        "dd/MM/yyyy HH.mm", CultureInfo.GetCultureInfo("id-ID"));
            }
        }

        /// <summary>Indikator tampil hanya bila ada data untuk dijelaskan.</summary>
        public bool AdaIndikatorTerbit => Hasil != null && IndikatorTerbitTampil.Length > 0;

        /// <summary>Bagian tanggal pemeriksaan hanya tampil bila waktunya diketahui.</summary>
        public bool AdaPemeriksaan => PemeriksaanTampil.Length > 0;

        /// <summary>
        /// Tahun-tahun setelah tahun data yang tampil sampai tahun berjalan
        /// belum diterbitkan pusat (permintaan dimulai dari tahun berjalan dan
        /// mundur per tahun), jadi yang tampil adalah penilaian tahun sebelumnya
        /// — beri tahu SEMUA tahun yang kosong supaya pengguna tidak mengira
        /// hanya tahun berjalan yang tertinggal. Juga berlaku untuk cadangan
        /// offline yang membawa tahun lebih lama. Pengecualiannya tahun 2025:
        /// bila dataset Indeks Desa 2025 memuat desa ini, tahun itu sudah tampil
        /// lewat seri baru sehingga tidak dihitung sebagai tahun yang kosong
        /// (lihat <see cref="PesanTahun"/>).
        /// </summary>
        public bool AdaPesanTahun => Hasil != null && TahunTerbitTerbaru < _jam().Year;

        public string PesanTahun
        {
            get
            {
                if (!AdaPesanTahun) return string.Empty;

                // Patokannya tahun TERBIT terbaru, bukan tahun yang sedang dibuka:
                // memilih tahun lama tidak boleh membuat tahun-tahun sesudahnya
                // terbaca sebagai "belum diterbitkan".
                int tampil = TahunTerbitTerbaru;
                var belum = new List<string>();
                for (int tahun = tampil + 1; tahun <= _jam().Year; tahun++)
                {
                    // Penilaian 2025 sudah tampil lewat dataset resmi Indeks Desa
                    // → tahun itu jangan disebut "belum diterbitkan pusat".
                    if (_indeks2025 != null && tahun == 2025) continue;
                    belum.Add(tahun.ToString(CultureInfo.InvariantCulture));
                }

                if (_indeks2025 != null)
                {
                    return "Seri IDM lama (IKS/IKE/IKL) terakhir terbit tahun "
                           + tampil.ToString(CultureInfo.InvariantCulture)
                           + "; penilaian 2025 tampil sebagai Indeks Desa enam dimensi"
                           + (belum.Count == 0
                               ? "."
                               : ". Tahun " + DaftarTahun(belum) + " belum diterbitkan pusat.");
                }

                return "Tahun " + DaftarTahun(belum)
                    + " belum diterbitkan pusat — menampilkan penilaian tahun "
                    + tampil.ToString(CultureInfo.InvariantCulture) + ".";
            }
        }

        /// <summary>"2025" / "2025 dan 2026" / "2025, 2026, dan 2027".</summary>
        private static string DaftarTahun(IReadOnlyList<string> tahun) => tahun.Count switch
        {
            0 => string.Empty,
            1 => tahun[0],
            _ => string.Join(", ", tahun.Take(tahun.Count - 1)) + " dan " + tahun[^1],
        };

        /// <summary>
        /// Terbitan resmi pusat yang lebih baru dari tahun data yang tampil
        /// (mis. Kepmendesa 2025 ketika portal baru memuat 2024). Null bila tidak ada.
        /// Angkanya TIDAK dipakai sebagai pengganti skor — hanya tautan dan keterangan.
        /// </summary>
        public TerbitanIdmResmi? TerbitanBaru => Hasil is { } hasil
            ? IdmTerbitanResmi.LebihBaruDari(hasil.Ringkasan.Tahun, _jam().Year)
            : null;

        /// <summary>Benar bila ada terbitan resmi pusat untuk tahun setelah tahun data.</summary>
        public bool AdaTerbitanBaru => TerbitanBaru != null;

        /// <summary>Judul dokumen resmi yang ditampilkan di kartu kabar data.</summary>
        public string JudulTerbitanBaru => TerbitanBaru?.Judul ?? string.Empty;

        /// <summary>Penjelasan tahun resmi vs tahun yang tampil, tanpa angka karangan.</summary>
        public string KeteranganTerbitanBaru
        {
            get
            {
                if (Hasil == null) return string.Empty;
                if (_indeks2025 != null)
                    return "Kepmendesa PDT menetapkan status desa tahun 2025. Penilaian Indeks Desa enam dimensi tersedia di bagian atas; riwayat IDM lama tetap ditampilkan terpisah.";
                if (!_indeks2025BerhasilDiperiksa)
                    return "Kepmendesa PDT menetapkan status desa tahun 2025. Dataset Indeks Desa nasional belum berhasil dimuat; muat ulang saat tersambung ke internet.";
                return "Kepmendesa PDT menetapkan status desa tahun 2025. Dataset nasional telah diperiksa, tetapi tidak memuat kode desa ini.";
            }
        }

        public string SkorTampil => Hasil is { } hasil
            ? IdmTeks.Angka(hasil.Ringkasan.Skor, "0.0000")
            : IdmTeks.Kosong;

        public string StatusTampil => Hasil is { } hasil
            ? IdmTeks.Status(hasil.Ringkasan.Status)
            : IdmTeks.Kosong;

        public string IksTampil => IdmTeks.Angka(Hasil?.Ringkasan.Iks);

        public string IkeTampil => IdmTeks.Angka(Hasil?.Ringkasan.Ike);

        public string IklTampil => IdmTeks.Angka(Hasil?.Ringkasan.Ikl);

        public string TargetTampil => Hasil is { } hasil
            ? IdmTeks.Status(hasil.Ringkasan.TargetStatus)
            : IdmTeks.Kosong;

        public string SkorMinimalTampil => IdmTeks.Angka(Hasil?.Ringkasan.SkorMinimal);

        /// <summary>
        /// Kalimat jarak ke status berikutnya — hanya memakai angka dari pusat.
        /// Teksnya dipusatkan di <see cref="IdmTeks.PesanTarget"/> supaya PDF
        /// mencetak kalimat yang persis sama.
        /// </summary>
        public string PesanTarget => Hasil is { } hasil ? IdmTeks.PesanTarget(hasil.Ringkasan) : string.Empty;

        public string SumberTampil => IdmTeks.Sumber(_data?.WaktuMuat);

        /// <summary>Rincian indikator apa adanya dari pusat, termasuk baris subtotal.</summary>
        public ObservableCollection<BarisTampil> Indikator { get; }

        /// <summary>Tahun-tahun lain yang datanya diterbitkan pusat (terbaru dulu).</summary>
        public ObservableCollection<RiwayatTampil> Riwayat { get; }

        /// <summary>
        /// Deret grafik tren: satu titik per tahun terbit, urut menaik. Angkanya
        /// berasal dari daftar tahun yang SAMA dengan chip "Tahun lain" di atas
        /// — skor IDM dan IKS/IKE/IKL apa adanya dari pusat, tanpa menghitung
        /// ulang — supaya grafik tidak pernah menampilkan angka berbeda dari
        /// baris ringkasan.
        /// </summary>
        public IReadOnlyList<TitikTren> DeretTren { get; private set; } = Array.Empty<TitikTren>();

        public ICommand MuatCommand { get; }
        public ICommand BatalCommand { get; }

        public ICommand BukaSitusCommand { get; }

        public ICommand BukaDokumentasiIndeksDesaCommand { get; }

        /// <summary>Buka halaman resmi terbitan pusat (JDIH Kemendesa PDT).</summary>
        public ICommand BukaTerbitanCommand { get; }

        /// <summary>
        /// Tampilkan rincian indikator untuk satu tahun yang sudah dimuat.
        /// Parameter berisi tahun penilaian; tahun yang tidak ada pada daftar
        /// diabaikan sehingga chip tahun lain tidak pernah menampilkan data kosong.
        /// </summary>
        public ICommand PilihTahunCommand { get; }

        /// <summary>
        /// Tahun penilaian yang rinciannya sedang tampil. Sama dengan tahun
        /// terbaru setelah pemuatan, lalu berubah bila pengguna memilih chip
        /// tahun lain.
        /// </summary>
        public int TahunDipilih => _tahunDipilih;

        /// <summary>
        /// Ganti tahun yang ditampilkan tanpa memuat ulang dari pusat: seluruh
        /// tahun sudah ada di memori, jadi cukup pindahkan pilihan lalu bangun
        /// ulang tampilan yang bergantung padanya. Data yang dimuat (kode desa,
        /// waktu, seluruh tahun) tidak ikut berubah. Tahun yang tidak dimuat
        /// diabaikan sehingga chip tidak pernah membuka rincian kosong.
        /// </summary>
        private void PilihTahun(int tahun)
        {
            // Tahun yang sudah terbuka dan tahun yang tidak dimuat diabaikan,
            // sehingga chip tidak pernah membangun ulang rincian tanpa sebab.
            if (tahun == _tahunDipilih) return;
            if (_data?.Seri.Cari(tahun) is null) return;

            _tahunDipilih = tahun;
            TampilkanPilihan();
        }

        /// <summary>
        /// Bangun ulang seluruh tampilan yang bergantung pada tahun terpilih:
        /// tabel indikator dan penanda chip. Angka ringkasan ikut disegarkan
        /// lewat <see cref="Segarkan"/>.
        /// </summary>
        private void TampilkanPilihan()
        {
            IsiIndikator();
            BangunRiwayat();
            Segarkan();
        }

        /// <summary>
        /// Buka tautan sumber resmi (dataset, peraturan, portal pusat) di peramban
        /// bawaan. Dipakai kartu "Sumber resmi 2025" pada halaman ini.
        /// </summary>
        public ICommand BukaTautanCommand { get; }

        /// <summary>
        /// Cetak atau simpan ringkasan + rincian indikator sebagai PDF — untuk
        /// dilampirkan pada laporan desa. Tersedia hanya setelah data pusat tampil;
        /// hasilnya dibuka di halaman pratinjau PDF yang sama dengan dokumen lain,
        /// sehingga tombol Cetak dan Ekspor di sana langsung bisa dipakai.
        /// </summary>
        public ICommand CetakCommand { get; }

        /// <summary>
        /// Keterangan tombol Cetak / PDF, termasuk saat tombolnya NONAKTIF.
        /// Tombol nonaktif tidak menampilkan tooltip biasa (lihat
        /// ToolTipService.ShowOnDisabled di IdmView), jadi tanpa teks ini pengguna
        /// desa yang hanya punya penilaian Indeks Desa 2025 melihat tombol mati
        /// tanpa tahu sebabnya — laporan IDM memang hanya memuat rincian indikator
        /// seri IDM lama.
        /// </summary>
        public string TooltipCetak
        {
            get
            {
                if (Hasil != null)
                {
                    return "Cetak atau simpan ringkasan penilaian dan rincian indikator sebagai PDF "
                           + "untuk dilampirkan pada laporan desa";
                }

                if (_indeks2025 != null)
                {
                    return "Desa ini hanya punya penilaian Indeks Desa 2025 (enam dimensi), "
                           + "sedangkan laporan PDF memuat ringkasan dan rincian indikator seri IDM lama "
                           + "— belum ada rincian indikator yang bisa dicetak.";
                }

                return "Muat data dari situs pusat terlebih dahulu untuk bisa mencetak laporan";
            }
        }


    }
}
