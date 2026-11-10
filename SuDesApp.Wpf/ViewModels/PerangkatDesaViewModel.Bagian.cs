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
    /// Pemilih bagian halaman (Perangkat Desa, RT/RW, BPD, ...): data tombol, judul/ikon bagian aktif, koleksi tabel per bagian, dan daftar jabatan per bagian.
    /// Pemecahan god-class tanpa mengubah perilaku — bagian dari kelas parsial
    /// <see cref="PerangkatDesaViewModel"/>.
    /// </summary>
    public partial class PerangkatDesaViewModel
    {
        // ---------- pemilih bagian ----------

        /// <summary>
        /// Tombol pemilih bagian halaman, disusun sebagai data supaya XAML cukup
        /// menyalinnya dengan ItemsControl: satu baris data memuat nama, ikon,
        /// perintah, dan keterangan tooltip satu bagian. Dulu ketiganya ditulis
        /// ulang per tombol di XAML, jadi menambah bagian menuntut tiga suntingan.
        /// </summary>
        public sealed class BarisBagian
        {
            internal BarisBagian(BagianPerangkat bagian, string judul, string ikon, string keterangan, int jumlah)
            {
                Bagian = bagian;
                Judul = judul;
                Ikon = ikon;
                Keterangan = keterangan;
                Jumlah = jumlah;
            }

            /// <summary>Bagian yang dibuka tombol ini; juga penanda tombol aktif.</summary>
            public BagianPerangkat Bagian { get; }
            public string Judul { get; }
            public string Ikon { get; }
            public string Keterangan { get; }

            /// <summary>Jumlah orang pada bagian ini, tampil sebagai angka kecil di tombol.</summary>
            public int Jumlah { get; }

            /// <summary>Nama anggota enum — dibandingkan gaya tombol bagian
            /// dengan <see cref="NamaBagianAktifEnum"/> untuk menyalakan tombol aktif.</summary>
            public string NamaBagian => Bagian.ToString();

            /// <summary>Perintah pindah bagian; diisi sekali oleh view model induk.</summary>
            public System.Windows.Input.ICommand? Perintah { get; internal set; }
        }

        /// <summary>Tombol bagian kelompok aparatur pemerintahan desa.</summary>
        public IReadOnlyList<BarisBagian> BarisBagianPemerintahan { get; private set; } = Array.Empty<BarisBagian>();

        /// <summary>Tombol bagian lembaga &amp; kader mitra desa.</summary>
        public IReadOnlyList<BarisBagian> BarisBagianLembaga { get; private set; } = Array.Empty<BarisBagian>();

        private void SusunBarisBagian()
        {
            BarisBagian B(BagianPerangkat bagian, string judul, string ikon, string keterangan)
            {
                // Angka pada tombol memakai daftar jabatan bagian itu, bukan
                // penghitung terpisah per kelompok: satu sumber dengan tabel.
                var jabatan = JabatanBagian(bagian);
                int jumlah = _semua.Count(p => jabatan.Contains(p.JabatanTampil, StringComparer.Ordinal));

                var baris = new BarisBagian(bagian, judul, ikon, keterangan, jumlah)
                {
                    Perintah = bagian switch
                    {
                        BagianPerangkat.PerangkatDesa => TampilkanPerangkatDesaCommand,
                        BagianPerangkat.RtRw => TampilkanRtRwCommand,
                        BagianPerangkat.Bpd => TampilkanBpdCommand,
                        BagianPerangkat.Linmas => TampilkanLinmasCommand,
                        BagianPerangkat.Posyandu => TampilkanPosyanduCommand,
                        BagianPerangkat.KaderKpm => TampilkanKaderKpmCommand,
                        BagianPerangkat.KaderStunting => TampilkanKaderStuntingCommand,
                        BagianPerangkat.StafDesa => TampilkanStafDesaCommand,
                        BagianPerangkat.OperatorDesa => TampilkanOperatorDesaCommand,
                        BagianPerangkat.Pkk => TampilkanPkkCommand,
                        BagianPerangkat.Lpm => TampilkanLpmCommand,
                        _ => TampilkanKarangTarunaCommand
                    }
                };
                return baris;
            }

            // Baris atas: aparatur pemerintahan desa (SK perorangan). Baris
            // bawah: lembaga & kader mitra desa (SK bersama/pengangkatan).
            BarisBagianPemerintahan = new[]
            {
                B(BagianPerangkat.PerangkatDesa, "Perangkat Desa", IkonMenu.Orang,
                    "Tampilkan bagian Perangkat Desa"),
                B(BagianPerangkat.RtRw, "RT/RW", IkonMenu.Warga,
                    "Tampilkan bagian RT/RW"),
                B(BagianPerangkat.Bpd, "BPD", IkonMenu.Peraturan,
                    "Tampilkan BPD"),
                B(BagianPerangkat.StafDesa, "Staf Desa", IkonMenu.Dokumen,
                    "Tampilkan Staf Desa"),
                B(BagianPerangkat.OperatorDesa, "Operator Desa", IkonMenu.Api,
                    "Tampilkan Operator Desa"),
            };

            BarisBagianLembaga = new[]
            {
                B(BagianPerangkat.Linmas, "LINMAS", IkonMenu.Linmas,
                    "Tampilkan LINMAS"),
                B(BagianPerangkat.Posyandu, "Posyandu", IkonMenu.Posyandu,
                    "Tampilkan Posyandu"),
                B(BagianPerangkat.KaderKpm, "Kader KPM", IkonMenu.Pengguna,
                    "Tampilkan Kader KPM"),
                B(BagianPerangkat.KaderStunting, "Kader Stunting", IkonMenu.KaderStunting,
                    "Tampilkan Kader Stunting"),
                B(BagianPerangkat.Pkk, "PKK", IkonMenu.Pkk,
                    "Tampilkan PKK"),
                B(BagianPerangkat.Lpm, "LPM", IkonMenu.Lpm,
                    "Tampilkan LPM — LPM dan LPMD satu lembaga, jadi data LPMD lama tampil di sini"),
                B(BagianPerangkat.KarangTaruna, "Karang Taruna", IkonMenu.KarangTaruna,
                    "Tampilkan Karang Taruna"),
            };

            OnPropertyChanged(nameof(BarisBagianPemerintahan));
            OnPropertyChanged(nameof(BarisBagianLembaga));
        }

        /// <summary>Ikon chip judul halaman: ikon bagian yang sedang tampil, supaya
        /// kepala halaman ikut berganti tanpa satu properti per bagian di XAML.</summary>
        public string IkonBagianAktif => _bagianAktif switch
        {
            BagianPerangkat.PerangkatDesa => IkonMenu.Orang,
            BagianPerangkat.RtRw => IkonMenu.Warga,
            BagianPerangkat.Bpd => IkonMenu.Peraturan,
            BagianPerangkat.Linmas => IkonMenu.Linmas,
            BagianPerangkat.Posyandu => IkonMenu.Posyandu,
            BagianPerangkat.KaderKpm => IkonMenu.Pengguna,
            BagianPerangkat.KaderStunting => IkonMenu.KaderStunting,
            BagianPerangkat.StafDesa => IkonMenu.Dokumen,
            BagianPerangkat.OperatorDesa => IkonMenu.Api,
            BagianPerangkat.Pkk => IkonMenu.Pkk,
            BagianPerangkat.Lpm => IkonMenu.Lpm,
            _ => IkonMenu.KarangTaruna
        };

        // ---------- header ----------

        public string HeaderTitle => "DATA PERANGKAT DESA";

        public string HeaderSubtitle =>
            "Aparatur pemerintahan desa dan lembaga mitra: satu baris per orang yang memegang "
            + "jabatan, dari Kepala Desa sampai kader. Pilih kelompoknya di bawah untuk "
            + "menampilkan data, lalu kelola SK tiap jabatan dari kolom Aksi.";

        // ---------- daftar ----------

        /// <summary>
        /// Baris tabel, selalu berblok per jabatan resmi (Kepala Desa → Kadus).
        /// Jabatan tanpa orang tampil sebagai baris kosong siap isi.
        /// </summary>
        public ObservableCollection<BarisPerangkatViewModel> Items { get; } = new();

        public ObservableCollection<BarisPerangkatViewModel> ItemsRtRw { get; } = new();
        public ObservableCollection<BarisPerangkatViewModel> ItemsBpd { get; } = new();
        public ObservableCollection<BarisPerangkatViewModel> ItemsLinmas { get; } = new();
        public ObservableCollection<BarisPerangkatViewModel> ItemsPosyandu { get; } = new();
        public ObservableCollection<BarisPerangkatViewModel> ItemsKaderKpm { get; } = new();
        public ObservableCollection<BarisPerangkatViewModel> ItemsKaderStunting { get; } = new();
        public ObservableCollection<BarisPerangkatViewModel> ItemsStafDesa { get; } = new();
        public ObservableCollection<BarisPerangkatViewModel> ItemsOperatorDesa { get; } = new();
        public ObservableCollection<BarisPerangkatViewModel> ItemsPkk { get; } = new();
        public ObservableCollection<BarisPerangkatViewModel> ItemsLpm { get; } = new();
        public ObservableCollection<BarisPerangkatViewModel> ItemsKarangTaruna { get; } = new();

        /// <summary>
        /// Nama bagian aktif sebagai teks (mis. "Linmas") — dibandingkan di XAML
        /// dengan penanda <c>u:BagianAktif.Bagian</c> pada tombol pemilih bagian,
        /// supaya tombol bagian yang sedang tampil bisa ditandai tanpa satu
        /// properti <c>Tampilkan…</c> per bagian.
        /// </summary>
        public string NamaBagianAktifEnum => _bagianAktif.ToString();
        public bool TampilkanPerangkatDesa => _bagianAktif == BagianPerangkat.PerangkatDesa;

        /// <summary>
        /// Tabel baris bagian yang sedang tampil. Halaman memakai satu tabel
        /// untuk seluruh bagian, jadi berpindah bagian cukup mengganti isi
        /// koleksi ini.
        /// </summary>
        public ObservableCollection<BarisPerangkatViewModel> ItemsAktif => TabelAktif;

        public string JudulBagianAktif => _bagianAktif switch
        {
            BagianPerangkat.PerangkatDesa => "Perangkat Desa",
            BagianPerangkat.RtRw => "RT/RW",
            BagianPerangkat.Bpd => "BPD",
            BagianPerangkat.Linmas => "LINMAS",
            BagianPerangkat.Posyandu => "POSYANDU",
            BagianPerangkat.KaderKpm => "KADER KPM",
            BagianPerangkat.KaderStunting => "KADER STUNTING",
            BagianPerangkat.StafDesa => "STAF DESA",
            BagianPerangkat.OperatorDesa => "OPERATOR DESA",
            BagianPerangkat.Pkk => "PKK",
            BagianPerangkat.Lpm => "LPM",
            BagianPerangkat.KarangTaruna => "KARANG TARUNA",
            _ => string.Empty
        };

        /// <summary>Nama bagian untuk pesan, huruf kecil semua (mis. "perangkat desa",
        /// "rt/rw", "bpd").</summary>
        private string NamaBagianAktif => _bagianAktif switch
        {
            BagianPerangkat.PerangkatDesa => "perangkat desa",
            BagianPerangkat.RtRw => "RT/RW",
            _ => JudulBagianAktif.ToLowerInvariant()
        };

        /// <summary>Nama bagian untuk judul (mis. "Perangkat Desa", "RT/RW", "BPD").</summary>
        private string NamaBagianTampil => _bagianAktif switch
        {
            BagianPerangkat.PerangkatDesa => "Perangkat Desa",
            BagianPerangkat.RtRw => "RT/RW",
            _ => JudulBagianAktif
        };

        /// <summary>Judul dokumen cetak/ekspor bagian yang sedang tampil. Tiap bagian
        /// dicetak terpisah sebab SK tiap kelompok punya cara penerbitan berbeda,
        /// jadi daftar satu bagian tidak dicampur dengan bagian lain.</summary>
        private string JudulDokumenAktif => _bagianAktif switch
        {
            BagianPerangkat.PerangkatDesa => "DAFTAR PERANGKAT DESA",
            BagianPerangkat.RtRw => "DAFTAR KETUA RT DAN KETUA RW",
            _ => $"DAFTAR {JudulBagianAktif}"
        };

        /// <summary>Lampiran nama berkas per bagian (kosong untuk Perangkat Desa),
        /// memakai nama enum supaya selalu aman untuk nama berkas.</summary>
        private string SufiksNamaBerkas => _bagianAktif == BagianPerangkat.PerangkatDesa
            ? string.Empty
            : "_" + _bagianAktif;

        /// <summary>Teks tombol tambah — diseragamkan "Tambah Orang" di seluruh
        /// halaman, supaya ajakan yang sama terlihat di semua bagian.</summary>
        public string LabelTambahAktif => "Tambah Orang";

        /// <summary>
        /// Petunjuk singkat pada kepala bagian: apa yang bisa dilakukan pada
        /// tabel ini, atau teguran bahwa bagian ini belum berisi orang. Angka
        /// keterisian tidak diulang di sini sebab sudah tampil sebagai chip.
        /// </summary>
        public string StatusBagianAktif => JumlahOrangBagianAktif == 0
            ? "Belum ada orang pada bagian ini. Pilih baris jabatan kosong atau tekan Tambah Orang untuk mulai mengisi."
            : "Klik dua kali baris untuk mengubah data diri; tombol SK di kolom Aksi membuka surat keputusan jabatan itu.";

        /// <summary>Jumlah orang pada bagian yang sedang tampil.</summary>
        public int JumlahOrangBagianAktif => TabelAktif.Count(b => b.AdaData);

        /// <summary>Jumlah jabatan resmi pada bagian yang sedang tampil.</summary>
        public int JumlahJabatanBagianAktif => JabatanBagian(_bagianAktif).Count;

        /// <summary>Jabatan pada bagian ini yang belum ada orangnya.</summary>
        public int JumlahKosongBagianAktif => TabelAktif.Count(b => b.IsBaru);

        /// <summary>Tabel bagian yang sedang tampil; satu tabel dipakai seluruh bagian.</summary>
        private ObservableCollection<BarisPerangkatViewModel> TabelAktif => _bagianAktif switch
        {
            BagianPerangkat.PerangkatDesa => Items,
            BagianPerangkat.RtRw => ItemsRtRw,
            BagianPerangkat.Bpd => ItemsBpd,
            BagianPerangkat.Linmas => ItemsLinmas,
            BagianPerangkat.Posyandu => ItemsPosyandu,
            BagianPerangkat.KaderKpm => ItemsKaderKpm,
            BagianPerangkat.KaderStunting => ItemsKaderStunting,
            BagianPerangkat.StafDesa => ItemsStafDesa,
            BagianPerangkat.OperatorDesa => ItemsOperatorDesa,
            BagianPerangkat.Pkk => ItemsPkk,
            BagianPerangkat.Lpm => ItemsLpm,
            _ => ItemsKarangTaruna
        };

        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    SegarkanPerintah();
                }
            }
        }

        /// <summary>Jabatan untuk form: seluruh jabatan yang tampil di halaman ini —
        /// perangkat desa resmi, RT/RW, dan kelompok lain (BPD, Linmas, Posyandu,
        /// Kader KPM, Kader Stunting, Staf Desa, Operator Desa, PKK, LPM, Karang Taruna) — supaya
        /// orang yang disimpan selalu tampil di salah satu tabel.</summary>
        public static readonly IReadOnlyList<string> DaftarJabatanBpd = new[]
        {
            JabatanPerangkat.KetuaBPD,
            JabatanPerangkat.WakilKetuaBPD,
            JabatanPerangkat.SekretarisBPD,
            JabatanPerangkat.AnggotaBPD
        };
        public static readonly IReadOnlyList<string> DaftarJabatanLinmas = new[] { JabatanPerangkat.Linmas };
        public static readonly IReadOnlyList<string> DaftarJabatanPosyandu = new[] { JabatanPerangkat.KaderPosyandu };
        public static readonly IReadOnlyList<string> DaftarJabatanPkk = new[] { JabatanPerangkat.KetuaPKK, JabatanPerangkat.PengurusPKK };
        public static readonly IReadOnlyList<string> DaftarJabatanLpm = new[] { JabatanPerangkat.KetuaLPM, JabatanPerangkat.PengurusLPM };
        public static readonly IReadOnlyList<string> DaftarJabatanKaderStunting = new[] { JabatanPerangkat.KaderStunting };
        public static readonly IReadOnlyList<string> DaftarJabatanKarangTaruna = new[]
        {
            JabatanPerangkat.KetuaKarangTaruna,
            JabatanPerangkat.WakilKetuaKarangTaruna,
            JabatanPerangkat.SekretarisKarangTaruna,
            JabatanPerangkat.BendaharaKarangTaruna,
            JabatanPerangkat.PengurusKarangTaruna
        };

        public IReadOnlyList<string> DaftarJabatan { get; } =
            JabatanPerangkat.Resmi
                .Concat(new[] { JabatanPerangkat.KetuaRT, JabatanPerangkat.KetuaRW })
                .Concat(DaftarJabatanBpd)
                .Concat(DaftarJabatanLinmas)
                .Concat(DaftarJabatanPosyandu)
                .Concat(new[] { JabatanPerangkat.KaderKPM })
                .Concat(DaftarJabatanKaderStunting)
                .Concat(new[] { JabatanPerangkat.Staf, JabatanPerangkat.OperatorDesa })
                .Concat(DaftarJabatanPkk)
                .Concat(DaftarJabatanLpm)
                .Concat(DaftarJabatanKarangTaruna)
                .ToList();

        /// <summary>Jabatan bagian RT/RW, urut RT lalu RW.</summary>
        public IReadOnlyList<string> DaftarJabatanRtRw { get; } =
            new[] { JabatanPerangkat.KetuaRT, JabatanPerangkat.KetuaRW };

        /// <summary>Daftar jabatan satu bagian halaman ini — sumber pilihan form,
        /// panel SK, dan tombol Tambah tiap bagian. Dibatasi per bagian supaya
        /// pilihan tidak pernah melompat ke bagian lain, sebab tiap kelompok
        /// punya cara penerbitan SK yang berbeda.</summary>
        public IReadOnlyList<string> JabatanBagian(BagianPerangkat bagian) => bagian switch
        {
            BagianPerangkat.PerangkatDesa => JabatanPerangkat.Resmi,
            BagianPerangkat.RtRw => DaftarJabatanRtRw,
            BagianPerangkat.Bpd => DaftarJabatanBpd,
            BagianPerangkat.Linmas => DaftarJabatanLinmas,
            BagianPerangkat.Posyandu => DaftarJabatanPosyandu,
            BagianPerangkat.KaderKpm => new[] { JabatanPerangkat.KaderKPM },
            BagianPerangkat.KaderStunting => DaftarJabatanKaderStunting,
            BagianPerangkat.StafDesa => new[] { JabatanPerangkat.Staf },
            BagianPerangkat.OperatorDesa => new[] { JabatanPerangkat.OperatorDesa },
            BagianPerangkat.Pkk => DaftarJabatanPkk,
            BagianPerangkat.Lpm => DaftarJabatanLpm,
            BagianPerangkat.KarangTaruna => DaftarJabatanKarangTaruna,
            _ => DaftarJabatan
        };

        /// <summary>Daftar jabatan bagian yang memuat satu jabatan — dipakai form yang
        /// dibuka dari baris, supaya pilihan jabatannya tidak keluar dari baris itu.</summary>
        private IReadOnlyList<string> JabatanBagianJabatan(string jabatan)
        {
            foreach (BagianPerangkat bagian in Enum.GetValues<BagianPerangkat>())
            {
                var daftar = JabatanBagian(bagian);
                if (daftar.Contains(jabatan, StringComparer.Ordinal)) return daftar;
            }
            return DaftarJabatan;
        }

        private IReadOnlyList<string>? _daftarJabatanForm;

        /// <summary>Pilihan jabatan pada form — dibatasi bagian tempat form dibuka,
        /// sehingga orang yang disimpan selalu jatuh ke bagian itu.</summary>
        public IReadOnlyList<string> DaftarJabatanForm => _daftarJabatanForm ?? DaftarJabatan;

        private void AturDaftarJabatanForm(IReadOnlyList<string> daftar)
        {
            _daftarJabatanForm = daftar;
            OnPropertyChanged(nameof(DaftarJabatanForm));
        }

        public IReadOnlyList<string> PilihanStatusForm { get; } = StatusPerangkat.Semua;

        public IReadOnlyList<string> PilihanJenisKelamin { get; } = new[] { "Laki-laki", "Perempuan" };

        public IReadOnlyList<string> PilihanPendidikan { get; } = new[]
        {
            "SD", "SMP/MTs", "SMA/MA/SMK", "D1/D2/D3", "S1", "S2", "S3"
        };

        /// <summary>Opsi agama — daftar yang sama dengan form Data Warga.</summary>
        public IReadOnlyList<string> PilihanAgama { get; } = SuratConstants.ValidAgama;


    }
}
