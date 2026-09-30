using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SuDesApp.Data.Models
{
    /// <summary>
    /// Jenis tabel yang boleh ditampilkan pada laporan kependudukan. Nilai ini
    /// sekaligus menentukan urutan tabel di laporan — bukan sekadar label di
    /// antarmuka.
    /// </summary>
    public enum JenisTabelLaporan
    {
        RingkasanPenduduk = 0,
        KelompokUsia = 1,
        Pendidikan = 2,
        Agama = 3,
        StatusPerkawinan = 4,
        Rt = 5,
        StatusWarga = 6,
        KartuKeluarga = 7,
        KepalaKeluarga = 8
    }

    /// <summary>Satu tabel laporan: judul, catatan kaki, dan baris datanya.</summary>
    public class LaporanTabel
    {
        /// <summary>Judul tabel, dicetak tebal di atas tabel.</summary>
        public string Judul { get; set; } = string.Empty;

        /// <summary>Kalimat kecil di bawah tabel (mis. sumber data atau catatan).</summary>
        public string? Catatan { get; set; }

        /// <summary>Judul kolom pertama (label kategori). Sisa kolom berisi angka.</summary>
        public string LabelKolom { get; set; } = "Keterangan";

        /// <summary>Judul kolom kedua dan seterusnya.</summary>
        public IReadOnlyList<string> JudulKolom { get; set; } = Array.Empty<string>();

        /// <summary>Baris tabel; setiap baris sejajar dengan <see cref="JudulKolom"/>.</summary>
        public IReadOnlyList<string[]> Baris { get; set; } = Array.Empty<string[]>();

        /// <summary>Baris jumlah, dicetak setelah baris biasa.</summary>
        public string[]? BarisTotal { get; set; }
    }

    /// <summary>
    /// Data siap-cetak untuk Laporan Penduduk. Dipisah dari logika penghitungan
    /// supaya keluaran PDF dan Excel memakai angka yang sama tanpa memanggil
    /// database dua kali.
    /// </summary>
    public class LaporanPendudukData
    {
        /// <summary>Judul besar di kop laporan.</summary>
        public string Judul { get; set; } = "LAPORAN KEPENDUDUKAN";

        /// <summary>Kalimat periode/cakupan, misalnya "Per 31 Desember 2026".</summary>
        public string Periode { get; set; } = string.Empty;

        /// <summary>Data desa untuk kop dan blok tanda tangan.</summary>
        public DesaData Desa { get; set; } = new DesaData();

        /// <summary>Deskripsi filter yang dipakai, dicetak sebagai keterangan sumber.</summary>
        public string? KeteranganFilter { get; set; }

        /// <summary>Angka rekap utama; dipakai kartu KPI dan tabel ringkasan.</summary>
        public WargaStatistikRingkasan Ringkasan { get; set; } = new WargaStatistikRingkasan();

        /// <summary>Urutan tabel yang dicetak. Tabel kosong dilewati.</summary>
        public IReadOnlyList<JenisTabelLaporan> UrutanTabel { get; set; } = new[]
        {
            JenisTabelLaporan.RingkasanPenduduk,
            JenisTabelLaporan.KepalaKeluarga,
            JenisTabelLaporan.KelompokUsia,
            JenisTabelLaporan.Pendidikan,
            JenisTabelLaporan.Agama,
            JenisTabelLaporan.StatusPerkawinan,
            JenisTabelLaporan.Rt,
            JenisTabelLaporan.StatusWarga,
            JenisTabelLaporan.KartuKeluarga
        };

        /// <summary>Tabel hasil susunan, diindeks per jenis.</summary>
        public IDictionary<JenisTabelLaporan, LaporanTabel> Tabel { get; set; } =
            new Dictionary<JenisTabelLaporan, LaporanTabel>();

        /// <summary>Tanggal laporan dicetak, dipakai untuk kop dan nama berkas.</summary>
        public DateTime TanggalCetak { get; set; } = DateTime.Today;

        /// <summary>Nama berkas tanpa ekstensi yang disarankan saat menyimpan.</summary>
        public string NamaBerkas { get; set; } = "Laporan_Penduduk";

        /// <summary>
        /// Tabel yang benar-benar dicetak: sudah berurutan dan tanpa tabel kosong,
        /// supaya laporan tidak memuat tabel "0 orang" yang tidak informatif.
        /// </summary>
        public IReadOnlyList<LaporanTabel> TabelTerisi =>
            UrutanTabel
                .Where(j => Tabel.TryGetValue(j, out var t) && !Kosong(t))
                .Select(j => Tabel[j])
                .ToList();

        /// <summary>Adakah isi laporan yang layak dicetak sama sekali?</summary>
        public bool AdaIsi => Ringkasan.TotalSeluruh > 0 || TabelTerisi.Count > 0;

        /// <summary>
        /// Tabel dianggap kosong bila tidak punya satu pun baris data. Baris
        /// "Jumlah" sendirian tidak membuat tabel layak dicetak — kalau tidak,
        /// laporan menampilkan tabel kosong yang cuma berisi "Jumlah 0 (0,0 %)"
        /// dan membingungkan pembaca.
        /// </summary>
        private static bool Kosong(LaporanTabel tabel) => tabel.Baris.Count == 0;
    }

    /// <summary>
    /// Menyusun <see cref="LaporanPendudukData"/> dari angka statistik yang sudah
    /// dihitung repository. Sengaja tanpa akses database: kalau tabel atau kolom
    /// baru ditambah, cukup ubah pemanggilnya, bukan kelas ini.
    /// </summary>
    public static class LaporanPendudukBuilder
    {
        private static readonly CultureInfo Id = CultureInfo.GetCultureInfo("id-ID");

        public static LaporanPendudukData Susun(
            WargaStatistikRingkasan ringkasan,
            DesaData? desa,
            DateTime? saatCetak = null,
            string? keteranganFilter = null,
            IReadOnlyList<JenisTabelLaporan>? tabelTermasuk = null)
        {
            var hasil = new LaporanPendudukData
            {
                Ringkasan = ringkasan ?? new WargaStatistikRingkasan(),
                Desa = desa ?? new DesaData(),
                TanggalCetak = (saatCetak ?? DateTime.Today).Date,
                KeteranganFilter = Singkapkan(keteranganFilter)
            };

            // tabelTermasuk kosong ≠ null: daftar kosong berarti operator mematikan
            // seluruh tabel (laporan hanya memuat ringkasan), null berarti semua tabel.
            if (tabelTermasuk != null)
            {
                hasil.UrutanTabel = tabelTermasuk.Distinct().ToList();
            }

            foreach (var jenis in hasil.UrutanTabel)
            {
                hasil.Tabel[jenis] = SusunTabel(jenis, hasil.Ringkasan);
            }

            hasil.Periode = $"Per {hasil.TanggalCetak.ToString("dd MMMM yyyy", Id)}";
            hasil.NamaBerkas = $"Laporan_Penduduk_{hasil.TanggalCetak:yyyyMMdd}";

            return hasil;
        }

        private static string? Singkapkan(string? teks)
        {
            if (string.IsNullOrWhiteSpace(teks)) return null;
            string hasil = teks!.Trim();
            return hasil.Length == 0 ? null : hasil;
        }

        private static LaporanTabel SusunTabel(JenisTabelLaporan jenis, WargaStatistikRingkasan r) => jenis switch
        {
            JenisTabelLaporan.RingkasanPenduduk => TabelRingkasan(r),
            JenisTabelLaporan.StatusWarga => TabelStatusWarga(r),
            JenisTabelLaporan.KartuKeluarga => TabelKartuKeluarga(r),
            JenisTabelLaporan.KepalaKeluarga => TabelKepalaKeluarga(r),
            JenisTabelLaporan.KelompokUsia => TabelSederhana(
                "Penduduk Menurut Kelompok Usia", "Kelompok usia", r.PerKelompokUsia, r.TotalSeluruh,
                "Usia dihitung pada tanggal laporan. Warga dengan tanggal lahir tidak terbaca masuk kelompok \"(tidak diketahui)\"."),
            JenisTabelLaporan.Pendidikan => TabelSederhana(
                "Penduduk Menurut Pendidikan Terakhir", "Jenjang pendidikan", r.PerPendidikan, r.TotalSeluruh, null),
            JenisTabelLaporan.Agama => TabelSederhana(
                "Penduduk Menurut Agama", "Agama", r.PerAgama, r.TotalSeluruh, null),
            JenisTabelLaporan.StatusPerkawinan => TabelSederhana(
                "Penduduk Menurut Status Perkawinan", "Status perkawinan", r.PerStatusPerkawinan, r.TotalSeluruh, null),
            JenisTabelLaporan.Rt => TabelSederhana(
                "Penduduk Menurut RT", "RT", r.PerRt, r.TotalSeluruh, null),
            _ => new LaporanTabel { Judul = jenis.ToString() }
        };

        private static LaporanTabel TabelRingkasan(WargaStatistikRingkasan r)
        {
            // "Penduduk" = yang masih tinggal di desa (aktif + baru). Meninggal dan
            // pindah tetap ditampilkan sebagai riwayat, bukan bagian dari penduduk.
            int penduduk = r.TotalAktif + r.TotalBaru;

            // Komposisi jenis kelamin HANYA dari penduduk. Dulu baris L/P memakai
            // LakiLaki/Perempuan (seluruh warga terdata, termasuk pindah & meninggal)
            // tetapi dibagi dengan jumlah penduduk — L + P bisa melebihi baris
            // "Penduduk" dan persentasenya meleset. Penduduk yang jenis kelaminnya
            // belum diisi ditampilkan sebagai baris tersendiri, bukan diam-diam
            // hilang dari rekap.
            int tidakDiketahui = r.PendudukJenisKelaminTidakDiketahui;

            // Tiga kolom: angka dan persentase TERPISAH — kolom persentase yang
            // menyatu di satu sel membuat kolom "Persentase" kosong di pratinjau
            // layar dan menyulitkan pembacaan di Excel.
            var baris = new List<string[]>
            {
                BarisPersen("Penduduk (tinggal di desa)", penduduk, r.TotalSeluruh),
                BarisPersen("Laki-laki", r.LakiLakiPenduduk, penduduk),
                BarisPersen("Perempuan", r.PerempuanPenduduk, penduduk)
            };

            if (tidakDiketahui > 0)
            {
                baris.Add(BarisPersen("Jenis kelamin belum diisi", tidakDiketahui, penduduk));
            }

            baris.Add(BarisPersen("Warga pindah", r.TotalPindah, r.TotalSeluruh));
            baris.Add(BarisPersen("Warga meninggal", r.TotalMeninggal, r.TotalSeluruh));
            baris.Add(BarisPersen("Jumlah seluruh warga terdata", r.TotalSeluruh, r.TotalSeluruh));

            return new LaporanTabel
            {
                Judul = "Ringkasan Penduduk",
                LabelKolom = "Uraian",
                JudulKolom = new[] { "Jumlah", "Persentase" },
                Baris = baris
            };
        }

        /// <summary>
        /// Tabel Kepala Keluarga: total KK, KK berkepala laki-laki, dan KK
        /// berkepala wanita. Dasar perhitungannya kedudukan dalam KK (kolom
        /// StatusKeluarga pada data warga); bila operator belum mengisi
        /// kedudukan, baris yang satu-satunya pemilik nomor KK dihitung sebagai
        /// Kepala Keluarga supaya angka tidak ikut surut pada data lama.
        /// </summary>
        private static LaporanTabel TabelKepalaKeluarga(WargaStatistikRingkasan r)
        {
            int total = r.JumlahKepalaKeluarga;

            return new LaporanTabel
            {
                Judul = "Kepala Keluarga",
                LabelKolom = "Uraian",
                JudulKolom = new[] { "Jumlah", "Persentase" },
                Baris = new[]
                {
                    BarisPersen("Kepala Keluarga laki-laki", r.KepalaKeluargaLakiLaki, total),
                    BarisPersen("Kepala Keluarga wanita", r.KepalaKeluargaPerempuan, total)
                },
                BarisTotal = new[] { "Jumlah Kepala Keluarga", Angka(total), "100,0 %" },
                Catatan = "Dihitung dari kedudukan dalam Kartu Keluarga (Kepala Keluarga, Istri, Anak, Cucu, Family Lain) pada data warga."
            };
        }

        private static LaporanTabel TabelStatusWarga(WargaStatistikRingkasan r) => new()
        {
            Judul = "Warga Menurut Status Tinggal",
            LabelKolom = "Status tinggal",
            JudulKolom = new[] { "Jumlah", "Persentase" },
            Baris = new[]
            {
                BarisPersen(StatusWargaTipe.Aktif, r.TotalAktif, r.TotalSeluruh),
                BarisPersen(StatusWargaTipe.Baru, r.TotalBaru, r.TotalSeluruh),
                BarisPersen(StatusWargaTipe.Pindah, r.TotalPindah, r.TotalSeluruh),
                BarisPersen(StatusWargaTipe.Meninggal, r.TotalMeninggal, r.TotalSeluruh)
            },
            BarisTotal = new[] { "Jumlah", Angka(r.TotalSeluruh), Persen(r.TotalSeluruh, r.TotalSeluruh) }
        };

        private static LaporanTabel TabelKartuKeluarga(WargaStatistikRingkasan r) => new()
        {
            Judul = "Kartu Keluarga",
            LabelKolom = "Uraian",
            JudulKolom = new[] { "Jumlah" },
            Baris = new[]
            {
                new[] { "Kartu Keluarga tercatat", Angka(r.JumlahKartuKeluarga) },
                new[] { "Warga tanpa nomor Kartu Keluarga", Angka(r.TanpaKartuKeluarga) }
            },
            Catatan = "Nomor Kartu Keluarga kosong berarti warga belum dipindahkan ke data Kartu Keluarga desa."
        };

        private static LaporanTabel TabelSederhana(
            string judul,
            string labelKolom,
            IReadOnlyList<WargaStatistikBaris>? sumber,
            int total,
            string? catatan)
        {
            var baris = (sumber ?? Array.Empty<WargaStatistikBaris>())
                .Select(b => BarisPersen(b.Label, b.Jumlah, total))
                .ToList();

            // RT harus urut menurut nomor, bukan abjad: "RT 10" mendahului "RT 2".
            if (string.Equals(labelKolom, "RT", StringComparison.Ordinal))
            {
                baris = baris
                    .OrderBy(b => UrutRT(b[0]))
                    .ThenBy(b => b[0], StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return new LaporanTabel
            {
                Judul = judul,
                LabelKolom = labelKolom,
                JudulKolom = new[] { "Jumlah", "Persentase" },
                Baris = baris,
                BarisTotal = new[] { "Jumlah", Angka(total), Persen(total, total) },
                Catatan = catatan
            };
        }

        /// <summary>Kunci urut RT: "RT 002" menjadi 2. Tanpa angka → diurutkan paling akhir.</summary>
        private static int UrutRT(string? label) =>
            int.TryParse(new string((label ?? string.Empty).Where(char.IsDigit).ToArray()), out int n)
                ? n
                : int.MaxValue;

        /// <summary>
        /// Satu baris tabel dua kolom angka: kolom Jumlah berisi angka saja dan
        /// kolom Persentase berisi persen saja ("12,3 %") — tidak lagi disatukan
        /// dalam satu sel.
        /// </summary>
        private static string[] BarisPersen(string label, int jumlah, int total)
        {
            if (total <= 0)
            {
                return new[] { label, Angka(jumlah), string.Empty };
            }

            double persen = jumlah * 100.0 / total;
            return new[] { label, Angka(jumlah), $"{persen.ToString("0.0", Id)} %" };
        }

        /// <summary>Persentase saja, contoh "12,3 %"; total nol → kosong.</summary>
        private static string Persen(int jumlah, int total)
        {
            if (total <= 0) return string.Empty;
            double persen = jumlah * 100.0 / total;
            return $"{persen.ToString("0.0", Id)} %";
        }

        private static string Angka(int jumlah) => jumlah.ToString("N0", Id);
    }
}
