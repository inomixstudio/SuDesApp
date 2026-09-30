using System;
using System.Linq;

namespace SuDesApp.Data.Models
{
    /// <summary>
    /// Daftar jabatan perangkat desa, dikelompokkan sesuai struktur resmi:
    /// perangkat desa (sekretariat, pelaksana teknis, kewilayahan), RT/RW,
    /// Linmas, LPM, PKK, Posyandu, BPD, serta kader (stunting dan KPM).
    ///
    /// Nilai jabatan memakai huruf kapital dan spasi supaya langsung bisa
    /// dipakai sebagai isi kolom <c>Jabatan</c> tanpa kamus tambahan.
    /// </summary>
    public static class JabatanPerangkat
    {
        // ---- Kelompok 1: PERANGKAT DESA (pemerintahan desa) ----
        public const string KepalaDesa = "KEPALA DESA";
        public const string WakilKepalaDesa = "WAKIL KEPALA DESA";
        public const string SekretarisDesa = "SEKRETARIS DESA";
        public const string KaurKeuangan = "KAUR KEUANGAN";
        public const string KaurPerencanaan = "KAUR PERENCANAAN";
        public const string KaurTataUsaha = "KAUR TATA USAHA DAN UMUM";
        public const string KasiPemerintahan = "KASI PEMERINTAHAN";
        public const string KasiPelayanan = "KASI PELAYANAN";
        public const string KasiKesejahteraan = "KASI KESEJAHTERAAN";
        public const string Kadus = "KADUS";
        public const string OperatorDesa = "OPERATOR DESA";
        public const string Staf = "STAF";

        // Sisa daftar lama yang tetap sah supaya data lama tidak ditolak;
        // nama ini tidak lagi muncul di dropdown untuk data baru.
        public const string KepalaSekretariat = "KEPALA SEKRETARIAT";
        public const string KaurPemasaran = "KAUR PEMASARAN";
        public const string KepalaSeksi = "KEPALA SEKSI";

        // ---- Kelompok 2: RT/RW ----
        public const string KetuaRT = "KETUA RT";
        public const string KetuaRW = "KETUA RW";

        // ---- Kelompok 3: LINMAS ----
        public const string Linmas = "LINMAS";

        // ---- Kelompok 4: LPM ----
        public const string KetuaLPM = "KETUA LPM";

        // ---- Kelompok 5: PKK ----
        public const string KetuaPKK = "KETUA PKK";
        public const string PengurusPKK = "PENGURUS PKK";

        // ---- Kelompok 6: POSYANDU ----
        public const string KaderPosyandu = "KADER POSYANDU";

        // ---- Kelompok 7: BPD ----
        public const string KetuaBPD = "KETUA BPD";
        public const string WakilKetuaBPD = "WAKIL KETUA BPD";
        public const string SekretarisBPD = "SEKRETARIS BPD";
        public const string BendaharaBPD = "BENDAHARA BPD";
        public const string AnggotaBPD = "ANGGOTA BPD";

        // ---- Kelompok 8 & 9: kader ----
        public const string KaderStunting = "KADER STUNTING";
        public const string KaderKPM = "KADER KPM";

        // ---- Penampung jabatan khusus desa ----
        public const string Lainnya = "LAINNYA";

        /// <summary>
        /// Nama jabatan lama → nama resmi terbaru (Permendagri 83/2015
        /// sebagaimana diubah Permendagri 67/2017). Dipakai <see cref="Normalisasi"/>
        /// saat menyimpan dan oleh migrasi data di DatabaseInitializer, supaya
        /// baris lama tampil, terfilter, dan terkelompokkan rapi tanpa buka-tutup form.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> NamaLama =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["KAUR UMUM"] = KaurTataUsaha,
                ["KAUR PELAYANAN"] = KasiPelayanan,
                ["STAF SEKRETARIAT"] = Staf,
                ["KEPALA LKD"] = KetuaLPM
            };

        /// <summary>Semua jabatan yang bisa dipilih di form, urut per kelompok.</summary>
        public static readonly IReadOnlyList<string> Semua = new[]
        {
            KepalaDesa, WakilKepalaDesa, SekretarisDesa,
            KaurKeuangan, KaurPerencanaan, KaurTataUsaha,
            KasiPemerintahan, KasiPelayanan, KasiKesejahteraan,
            Kadus, OperatorDesa, Staf,
            KepalaSekretariat, KaurPemasaran, KepalaSeksi,
            KetuaRT, KetuaRW,
            Linmas,
            KetuaLPM,
            KetuaPKK, PengurusPKK,
            KaderPosyandu,
            KetuaBPD, WakilKetuaBPD, SekretarisBPD, BendaharaBPD, AnggotaBPD,
            KaderStunting, KaderKPM,
            Lainnya
        };

        /// <summary>Jabatan yang minimal harus ada di setiap desa.</summary>
        public static readonly IReadOnlyList<string> Inti = new[]
        {
            KepalaDesa, SekretarisDesa, KetuaBPD
        };

        /// <summary>Kelompok jabatan, dipakai mengelompokkan daftar di halaman.</summary>
        public static string Kelompok(string? jabatan) => Normalisasi(jabatan) switch
        {
            KepalaDesa or WakilKepalaDesa or SekretarisDesa
                or KaurKeuangan or KaurPerencanaan or KaurTataUsaha
                or KasiPemerintahan or KasiPelayanan or KasiKesejahteraan
                or Kadus or OperatorDesa or Staf
                or KepalaSekretariat or KaurPemasaran or KepalaSeksi => "PERANGKAT DESA",
            KetuaRT or KetuaRW => "RT/RW",
            Linmas => "LINMAS",
            KetuaLPM => "LPM",
            KetuaPKK or PengurusPKK => "PKK",
            KaderPosyandu => "POSYANDU",
            KetuaBPD or WakilKetuaBPD or SekretarisBPD or BendaharaBPD or AnggotaBPD => "BPD",
            KaderStunting => "KADER STUNTING",
            KaderKPM => "KADER KPM",
            _ => "LAINNYA"
        };

        /// <summary>Urutan kelompok untuk tampilan, sesuai urutan jabatan.</summary>
        public static readonly IReadOnlyList<string> UrutanKelompok = new[]
        {
            "PERANGKAT DESA", "RT/RW", "LINMAS", "LPM", "PKK", "POSYANDU",
            "BPD", "KADER STUNTING", "KADER KPM", "LAINNYA"
        };

        /// <summary>
        /// Jabatan-jabatan yang termasuk satu kelompok — kebalikan <see cref="Kelompok"/>,
        /// urut sesuai <see cref="Semua"/>. Dipakai penyaringan daftar per kelompok
        /// (dan menu sidebar yang dipisah per kelompok), sebab kelompok dihitung dari
        /// jabatan dan tidak tersimpan sebagai kolom database.
        /// Daftar kosong berarti nama kelompoknya tidak dikenal.
        /// </summary>
        public static IReadOnlyList<string> DaftarKelompok(string? kelompok)
        {
            if (string.IsNullOrWhiteSpace(kelompok))
            {
                return Array.Empty<string>();
            }

            var nama = kelompok.Trim();
            return Semua
                .Where(j => string.Equals(Kelompok(j), nama, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        /// <summary>Nama kelompok untuk menu dan filter — huruf besar semua hanya
        /// pas untuk kolom tabel, tidak untuk daftar menu.</summary>
        public static string TampilanKelompok(string? kelompok) => Normalisasi(kelompok) switch
        {
            "PERANGKAT DESA" => "Perangkat Desa",
            "RT/RW" => "RT/RW",
            "LINMAS" => "Linmas",
            "LPM" => "LPM",
            "PKK" => "PKK",
            "POSYANDU" => "Posyandu",
            "BPD" => "BPD",
            "KADER STUNTING" => "Kader Stunting",
            "KADER KPM" => "Kader KPM",
            _ => "Lainnya"
        };

        public static bool Valid(string? jabatan) =>
            !string.IsNullOrWhiteSpace(jabatan)
            && Semua.Contains(Normalisasi(jabatan), StringComparer.Ordinal);

        /// <summary>
        /// Rapikan jabatan dari form: huruf kapital, spasi berlebih dibuang, dan
        /// nama lama (mis. "KAUR UMUM") dipetakan ke nama resmi terbaru.
        /// </summary>
        public static string Normalisasi(string? jabatan)
        {
            if (string.IsNullOrWhiteSpace(jabatan)) return string.Empty;
            var rapi = RapiTeks(jabatan);
            return NamaLama.TryGetValue(rapi, out var baru) ? baru : rapi;
        }

        private static string RapiTeks(string jabatan) =>
            string.Join(" ", jabatan.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .ToUpperInvariant();
    }

    /// <summary>
    /// Status jabatan perangkat desa. <c>MENUNGGU SK</c> berarti sudah
    /// dilantik tetapi SK-nya belum turun, <c>SELESAI</c> berarti masa
    /// jabatan habis, dan <c>BERHENTI</c> berarti berhenti sebelum masa
    /// jabatan selesai.
    /// </summary>
    public static class StatusPerangkat
    {
        public const string Aktif = "AKTIF";
        public const string MenungguSK = "MENUNGGU SK";
        public const string Selesai = "SELESAI";
        public const string Berhenti = "BERHENTI";

        public static readonly IReadOnlyList<string> Semua = new[] { Aktif, MenungguSK, Selesai, Berhenti };

        /// <summary>Status yang masih memegang jabatan saat ini.</summary>
        public static readonly IReadOnlyList<string> MemegangJabatan = new[] { Aktif, MenungguSK };

        public static bool Valid(string? status) =>
            !string.IsNullOrWhiteSpace(status)
            && Semua.Contains(Normalisasi(status), StringComparer.Ordinal);

        public static string Normalisasi(string? status)
        {
            if (string.IsNullOrWhiteSpace(status)) return Aktif;
            return Semua.Contains(status.Trim().ToUpperInvariant(), StringComparer.Ordinal)
                ? status.Trim().ToUpperInvariant()
                : Aktif;
        }
    }

    /// <summary>
    /// Satu orang perangkat desa. Isi tabel ini adalah "siapa yang
    /// memegang jabatan", bukan ketentuan jabatannya; daftar jabatan ada di
    /// <see cref="JabatanPerangkat"/>.
    ///
    /// Dusun, RT, dan RW sengaja disimpan sebagai teks bebas, sama seperti di tabel
    /// <c>Warga</c>, supaya tidak perlu memindahkan data warga lama.
    /// </summary>
    public class PerangkatDesa
    {
        public int ID { get; set; }

        /// <summary>Nama lengkap, wajib diisi.</summary>
        public string Nama { get; set; } = string.Empty;

        /// <summary>Jabatan, harus salah satu nilai <see cref="JabatanPerangkat"/>.</summary>
        public string Jabatan { get; set; } = string.Empty;

        public string? NIP { get; set; }
        public string? NIK { get; set; }
        public string? JenisKelamin { get; set; }
        public string? TempatLahir { get; set; }
        public DateTime? TanggalLahir { get; set; }
        public string? Pendidikan { get; set; }
        public string? Alamat { get; set; }
        public string? Dusun { get; set; }
        public string? RT { get; set; }
        public string? RW { get; set; }
        public string? NomorHP { get; set; }
        public string? WhatsApp { get; set; }

        /// <summary>
        /// Unit kerja di dalam kelompoknya, mis. "POSYANDU SAKURA I" atau
        /// "POKJANAL" untuk kader Posyandu, atau nama dusun untuk Linmas.
        /// Dipakai mengelompokkan nama pada lampiran SK yang memuat banyak orang.
        /// </summary>
        public string? Unit { get; set; }

        /// <summary>Nomor SK pelantikan/pemberhentian.</summary>
        public string? NomorSK { get; set; }

        public DateTime? TanggalSK { get; set; }

        /// <summary>
        /// Nama berkas PDF SK yang diarsipkan (tanpa jalur), berlaku untuk SK yang
        /// TIDAK diterbitkan aplikasi — SK Bupati untuk Kepala Desa dan anggota BPD.
        /// Berkasnya disimpan di folder berkas SK (lihat <c>BerkasSkPerangkat</c>);
        /// SK yang dicetak aplikasi tidak perlu diarsipkan ulang karena PDF-nya sudah
        /// tersimpan di folder keluaran.
        /// </summary>
        public string? BerkasSK { get; set; }

        /// <summary>Masa jabatan mulai, biasanya sama dengan TanggalSK.</summary>
        public DateTime? MasaJabatanMulai { get; set; }

        public DateTime? MasaJabatanSelesai { get; set; }

        public string Status { get; set; } = StatusPerangkat.Aktif;

        public string? Catatan { get; set; }

        public string DibuatOleh { get; set; } = string.Empty;
        public string DiperbaruiOleh { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        // ---------- properties turunan untuk tampilan ----------

        public string JabatanTampil => JabatanPerangkat.Normalisasi(Jabatan);

        public string Kelompok => JabatanPerangkat.Kelompok(Jabatan);

        public string StatusTampil => StatusPerangkat.Normalisasi(Status);

        public string JenisKelaminTampil => JenisKelaminRingkas(JenisKelamin);

        public static string JenisKelaminRingkas(string? jenisKelamin) =>
            (jenisKelamin ?? string.Empty).Trim().ToUpperInvariant() switch
            {
                "L" or "LAKI-LAKI" or "PRIA" => "Laki-laki",
                "P" or "PEREMPUAN" or "WANITA" => "Perempuan",
                "" => "-",
                _ => jenisKelamin!.Trim()
            };

        /// <summary>Wilayah-wewenang, contoh "Dusun Cibogo, RT 01/RW 02".</summary>
        public string WilayahRingkas
        {
            get
            {
                var bagian = new List<string>();
                if (!string.IsNullOrWhiteSpace(Dusun)) bagian.Add(Dusun!.Trim());
                if (!string.IsNullOrWhiteSpace(RT) || !string.IsNullOrWhiteSpace(RW))
                    bagian.Add($"RT {Ringkas(RT)}/RW {Ringkas(RW)}");
                return bagian.Count == 0 ? "-" : string.Join(", ", bagian);
            }
        }

        private static string Ringkas(string? nilai) =>
            string.IsNullOrWhiteSpace(nilai) ? "-" : nilai!.Trim();

        /// <summary>Masa jabatan siap tampil, contoh "2021-05-10 s/d 2026-05-09".</summary>
        public string MasaJabatanTampil => MasaJabatanSelesai.HasValue
            ? $"{FormatTanggal(MasaJabatanMulai)} s/d {FormatTanggal(MasaJabatanSelesai)}"
            : FormatTanggal(MasaJabatanMulai);

        private static string FormatTanggal(DateTime? tanggal) =>
            tanggal?.ToString("dd-MM-yyyy") ?? "-";

        /// <summary>Berkas SK terarsip tersedia untuk baris ini.</summary>
        public bool AdaBerkasSK => !string.IsNullOrWhiteSpace(BerkasSK);

        /// <summary>Nomor SK dan tanggalnya, untuk keperluan rujukan.</summary>
        public string LatarBelakangTampil => string.IsNullOrWhiteSpace(NomorSK)
            ? "-"
            : $"{NomorSK!.Trim()} / {FormatTanggal(TanggalSK)}";

        public int Umur => TanggalLahir.HasValue
            ? UmurPada(TanggalLahir.Value, DateTime.Today)
            : 0;

        /// <summary>Umur pada tanggal tertentu; dipakai juga oleh pengujian.</summary>
        public static int UmurPada(DateTime lahir, DateTime pada)
        {
            int umur = pada.Year - lahir.Year;
            if (lahir.Date > pada.Date.AddYears(-umur)) umur--;
            return umur;
        }

        public bool MasihMemegangJabatan => StatusPerangkat.MemegangJabatan.Contains(StatusTampil, StringComparer.Ordinal);
    }

    /// <summary>Saring daftar perangkat desa.</summary>
    public class PerangkatDesaFilter
    {
        /// <summary>Bebas: nama, jabatan, NIP, NIK, nomor HP, atau nomor SK.</summary>
        public string? Cari { get; set; }

        public string? Jabatan { get; set; }

        public string? Status { get; set; }

        /// <summary>Nilai wilayah bebas (dusun, RT, atau RW).</summary>
        public string? Wilayah { get; set; }

        /// <summary>
        /// Kelompok jabatan (mis. <c>RT/RW</c> atau <c>BPD</c>). Kosong berarti semua
        /// kelompok. Dipakai menu sidebar yang dipisah per kelompok, sehingga menunya
        /// bisa membuka daftar yang sudah tersaring.
        /// </summary>
        public string? Kelompok { get; set; }

        /// <summary>Filter disalin dengan kondisi lain, dipakai tombol "belum lengkap".</summary>
        public PerangkatDesaFilter Clone(string? status = null) => new()
        {
            Cari = Cari,
            Jabatan = Jabatan,
            Status = status ?? Status,
            Wilayah = Wilayah,
            Kelompok = Kelompok
        };
    }

    /// <summary>Angka ringkas untuk kartu rekapitulasi di atas daftar.</summary>
    public class PerangkatDesaStatistik
    {
        public int Total { get; set; }
        public int Aktif { get; set; }
        public int MenungguSK { get; set; }
        public int Selesai { get; set; }
        public int Berhenti { get; set; }

        /// <summary>Jabatan inti yang belum ada orangnya (mis. Kepala Desa).</summary>
        public IReadOnlyList<string> JabatanIntiKosong { get; set; } = Array.Empty<string>();

        public int JumlahKelompok => JabatanIntiKosong.Count;
    }
}
