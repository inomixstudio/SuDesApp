using System;
using System.Collections.Generic;

namespace SuDesApp.Data.Models
{
    /// <summary>
    /// Status warga terhadap desa (nilai kolom <c>Warga.StatusWarga</c>). Ini yang
    /// membedakan "warga menetap" dari "warga sudah tidak tinggal di desa" —
    /// tanpa status, warga yang meninggal atau pindah tidak pernah keluar dari
    /// pencarian sehingga rekapitulasi jumlah penduduk selalu salah.
    /// </summary>
    public static class StatusWargaTipe
    {
        /// <summary>Warga tetap tinggal di desa (nilai bawaan &amp; paling sering dipakai).</summary>
        public const string Aktif = "AKTIF";

        /// <summary>Warga meninggal di desa atau sudah dipindahkan ke tabel kematian.</summary>
        public const string Meninggal = "MENINGGAL";

        /// <summary>Warga pindah keluar dari desa.</summary>
        public const string Pindah = "PINDAH";

        /// <summary>Warga pendatang yang baru didaftarkan ke desa ini.</summary>
        public const string Baru = "BARU";

        public static readonly string[] Semua = { Aktif, Baru, Meninggal, Pindah };

        /// <summary>Status yang masih terhitung dalam jumlah penduduk desa.</summary>
        public static readonly string[] Menghitung = { Aktif, Baru };

        public static bool Valid(string? status) =>
            !string.IsNullOrWhiteSpace(status) && Array.Exists(Semua, s => s.Equals(status, StringComparison.OrdinalIgnoreCase));

        /// <summary>Status yang bisa dipilih pengguna di form Data Warga.</summary>
        public static string Normalisasi(string? status) =>
            Valid(status) ? status!.ToUpperInvariant() : Aktif;
    }

    /// <summary>
    /// Kartu Keluarga. Nomor KK sudah ada di tabel <c>Warga</c> (kolom
    /// <c>NoKK</c>), tabel ini hanya menyimpan kepala keluarga &amp; alamat KK
    /// supaya satu alamat tidak ditulis ulang di setiap anggota.
    /// </summary>
    public class KartuKeluargaData
    {
        public string? NoKK { get; set; }
        public string? NamaKepalaKeluarga { get; set; }
        public string? Alamat { get; set; }
        public string? RT { get; set; }
        public string? RW { get; set; }
        public string? Dusun { get; set; }
        public string? Desa { get; set; }
        public string? Kecamatan { get; set; }
        public string? Kabupaten { get; set; }

        /// <summary>Nomor HP kepala keluarga (sumber tunggal untuk kontak warga).</summary>
        public string? NomorHP { get; set; }

        public string? Catatan { get; set; }
        public int JumlahAnggota { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        /// <summary>Alamat siap cetak: "Dusun ... RT .../RW ... Desa, Kecamatan, Kabupaten".</summary>
        public string AlamatLengkap
        {
            get
            {
                var bagian = new List<string>();
                void Tambah(string? nilai)
                {
                    if (!string.IsNullOrWhiteSpace(nilai) && !bagian.Contains(nilai!.Trim()))
                        bagian.Add(nilai.Trim());
                }

                Tambah(Dusun);
                if (!string.IsNullOrWhiteSpace(RT) || !string.IsNullOrWhiteSpace(RW))
                {
                    var rt = string.IsNullOrWhiteSpace(RT) ? "-" : RT!.Trim();
                    var rw = string.IsNullOrWhiteSpace(RW) ? "-" : RW!.Trim();
                    Tambah($"RT {rt}/RW {rw}");
                }
                Tambah(Alamat);
                Tambah(Desa);
                Tambah(Kecamatan);
                Tambah(Kabupaten);
                return string.Join(", ", bagian);
            }
        }

        /// <summary>Validasi sederhana: No KK wajib 16 digit bila diisi.</summary>
        public bool IsValid(out List<string> errors)
        {
            errors = new List<string>();
            if (string.IsNullOrWhiteSpace(NoKK))
                errors.Add("Nomor Kartu Keluarga wajib diisi");
            else if (NoKK.Trim().Length != 16 || !NoKK.All(char.IsDigit))
                errors.Add("Nomor Kartu Keluarga harus 16 digit angka");

            if (string.IsNullOrWhiteSpace(NamaKepalaKeluarga))
                errors.Add("Nama Kepala Keluarga wajib diisi");
            else if (NamaKepalaKeluarga.Trim().Length < 2)
                errors.Add("Nama Kepala Keluarga minimal 2 karakter");

            return errors.Count == 0;
        }
    }

    /// <summary>
    /// Syarat tampilan tabel Data Warga. Semua filter opsional: nilai kosong
    /// berarti "semua". <see cref="Halaman"/> 1-based.
    /// </summary>
    public class WargaFilter
    {
        /// <summary>Cari bebas di NIK, nama, No KK, alamat, atau nomor HP.</summary>
        public string? Cari { get; set; }

        /// <summary>Penyaring status warga; null/empty = semua status.</summary>
        public string? Status { get; set; }

        /// <summary>Penyaring RT; null/empty = semua RT.</summary>
        public string? RT { get; set; }

        /// <summary>Penyaring RW; null/empty = semua RW.</summary>
        public string? RW { get; set; }

        /// <summary>Penyaring dusun; null/empty = semua dusun.</summary>
        public string? Dusun { get; set; }

        public int Halaman { get; set; } = 1;

        /// <summary>Jumlah baris per halaman; &lt;= 0 berarti 200.</summary>
        public int UkuranHalaman { get; set; } = 200;

        public int UkuranHalamanEfektif => UkuranHalaman <= 0 ? 200 : UkuranHalaman;

        public int HalamanEfektif => Halaman < 1 ? 1 : Halaman;
    }

    /// <summary>Hasil satu halaman daftar warga + jumlah total untuk paginasi.</summary>
    public class WargaPagedResult
    {
        public IReadOnlyList<WargaData> Items { get; set; } = Array.Empty<WargaData>();
        public int Total { get; set; }

        public int Halaman { get; set; } = 1;
        public int UkuranHalaman { get; set; } = 200;

        public int TotalHalaman => UkuranHalaman <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(Total / (double)UkuranHalaman));
        public bool AdaHalamanSebelum => Halaman > 1;
        public bool AdaHalamanSesudah => Halaman < TotalHalaman;

        public string Ringkasan =>
            Total == 0
                ? "Tidak ada warga yang sesuai filter."
                : $"Menampilkan {(Halaman - 1) * UkuranHalaman + 1}–{Math.Min(Halaman * UkuranHalaman, Total)} dari {Total} warga.";
    }

    /// <summary>Statistik jumlah warga pada satu kelompok (RT/RW/dusun/status).</summary>
    public class WargaStatistikBaris
    {
        public string? Kunci { get; set; }
        public int Jumlah { get; set; }
        public int LakiLaki { get; set; }
        public int Perempuan { get; set; }

        public string Label => string.IsNullOrWhiteSpace(Kunci) ? "(tidak diisi)" : Kunci!;
    }

    /// <summary>Ringkasan angka kependudukan untuk kartu statistik &amp; laporan.</summary>
    public class WargaStatistikRingkasan
    {
        public int TotalSeluruh { get; set; }
        public int TotalAktif { get; set; }
        public int TotalBaru { get; set; }
        public int TotalMeninggal { get; set; }
        public int TotalPindah { get; set; }

        /// <summary>
        /// Warga laki-laki dari SELURUH warga terdata — termasuk yang sudah pindah
        /// atau meninggal. Dipakai halaman Data Warga; laporan kependudukan memakai
        /// <see cref="LakiLakiPenduduk"/> supaya persentasenya dihitung terhadap
        /// penduduk yang benar.
        /// </summary>
        public int LakiLaki { get; set; }

        /// <summary>Warga perempuan dari seluruh warga terdata (lihat <see cref="LakiLaki"/>).</summary>
        public int Perempuan { get; set; }

        /// <summary>
        /// Penduduk laki-laki: hanya yang masih tinggal di desa (status aktif atau
        /// baru). Angka inilah yang dibandingkan dengan "Penduduk (tinggal di desa)"
        /// pada laporan — dulu laporan memakai <see cref="LakiLaki"/> (seluruh warga)
        /// tetapi membaginya dengan jumlah penduduk, sehingga L + P bisa melebihi
        /// jumlah penduduk dan persentasenya meleset.
        /// </summary>
        public int LakiLakiPenduduk { get; set; }

        /// <summary>Penduduk perempuan yang masih tinggal di desa (lihat <see cref="LakiLakiPenduduk"/>).</summary>
        public int PerempuanPenduduk { get; set; }

        /// <summary>
        /// Penduduk yang jenis kelaminnya kosong/tidak dikenal. Tercatat supaya
        /// LakiLakiPenduduk + PerempuanPenduduk + nilai ini selalu sama dengan
        /// TotalAktif + TotalBaru — laporan bisa menunjukkan berapa data yang
        /// belum lengkap alih-alih diam-diam kehilangan orang.
        /// </summary>
        public int PendudukJenisKelaminTidakDiketahui { get; set; }

        public int JumlahKartuKeluarga { get; set; }
        public int TanpaKartuKeluarga { get; set; }

        /// <summary>Jumlah baris berkedudukan Kepala Keluarga (semua jenis kelamin).</summary>
        public int JumlahKepalaKeluarga { get; set; }

        /// <summary>Kepala Keluarga berjenis kelamin laki-laki.</summary>
        public int KepalaKeluargaLakiLaki { get; set; }

        /// <summary>Kepala Keluarga berjenis kelamin perempuan (KK wanita).</summary>
        public int KepalaKeluargaPerempuan { get; set; }

        public IReadOnlyList<WargaStatistikBaris> PerRt { get; set; } = Array.Empty<WargaStatistikBaris>();
        public IReadOnlyList<WargaStatistikBaris> PerStatusPerkawinan { get; set; } = Array.Empty<WargaStatistikBaris>();
        public IReadOnlyList<WargaStatistikBaris> PerAgama { get; set; } = Array.Empty<WargaStatistikBaris>();
        public IReadOnlyList<WargaStatistikBaris> PerKelompokUsia { get; set; } = Array.Empty<WargaStatistikBaris>();

        /// <summary>
        /// Pendidikan terkelompok ke jenjang baku BPS supaya tabel laporan tidak
        /// berderet banyak baris "SD/SMP/SMA/…" yang isinya sama. Baris berlabel
        /// <c>(tidak diisi)</c> muncul bila kolom kosong pada warga lama.
        /// </summary>
        public IReadOnlyList<WargaStatistikBaris> PerPendidikan { get; set; } = Array.Empty<WargaStatistikBaris>();

        /// <summary>Usia berapa pun yang tidak bisa dihitung (tanggal lahir tidak terbaca).</summary>
        public int UsiaTidakDiketahui { get; set; }
    }
}
