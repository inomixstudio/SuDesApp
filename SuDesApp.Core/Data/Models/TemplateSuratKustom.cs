using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SuDesApp.Data.Models
{
    /// <summary>Jenis kolom isian yang bisa dipilih pada template surat kustom.</summary>
    public enum TipeKolomTemplate
    {
        /// <summary>Teks satu baris.</summary>
        Teks,
        /// <summary>NIK 16 angka.</summary>
        Nik,
        /// <summary>Angka (mis. nomor, jumlah).</summary>
        Angka,
        /// <summary>Tanggal (diisi DD-MM-YYYY, dicetak sebagai "12 Januari 2026").</summary>
        Tanggal,
        /// <summary>Teks panjang/paragraf.</summary>
        Paragraf,
        /// <summary>Pilihan dari daftar (dropdown).</summary>
        Pilihan
    }

    /// <summary>Perataan satu blok teks bebas pada template.</summary>
    public enum RataBlokTemplate
    {
        Kiri,
        Tengah,
        Kanan,
        Justify
    }

    /// <summary>
    /// Jenis satu bagian (blok) penyusun badan surat kustom. Urutan blok pada
    /// <see cref="TemplateSuratKustom.Bagian"/> itulah yang dicetak dari atas ke bawah.
    /// </summary>
    public enum TipeBagianTemplate
    {
        /// <summary>Teks bebas yang diketik sendiri (kalimat pembuka, keperluan, penutup).</summary>
        Teks,

        /// <summary>Data pribadi satu orang (NIK, Nama, TTL, Alamat, …), diisi di form surat.</summary>
        DataDiri,

        /// <summary>Sekelompok kolom isian; tampil sebagai baris label : nilai atau tabel bergaris.</summary>
        Kolom
    }

    /// <summary>Satu blok teks bebas ("tambah teks kosong untuk diketik sendiri").</summary>
    public class BlokTeksTemplateSurat
    {
        public string Isi { get; set; } = string.Empty;
        public RataBlokTemplate Rata { get; set; } = RataBlokTemplate.Kiri;
        public bool Tebal { get; set; }
        public bool Miring { get; set; }

        /// <summary>Satu baris pertama sebagai judul sub-bagian (opsional).</summary>
        public bool BarisBaru { get; set; }

        public BlokTeksTemplateSurat Clone() => new()
        {
            Isi = Isi,
            Rata = Rata,
            Tebal = Tebal,
            Miring = Miring,
            BarisBaru = BarisBaru
        };
    }

    /// <summary>Satu kolom isian pada template (mis. NIK, Nama, Alamat).</summary>
    public class KolomTemplateSurat
    {
        /// <summary>Kunci teknis unik (slug dari label), dipakai menyimpan nilai.</summary>
        public string Kunci { get; set; } = string.Empty;

        /// <summary>Label yang tercetak di surat, mis. "NIK".</summary>
        public string Label { get; set; } = string.Empty;

        public TipeKolomTemplate Tipe { get; set; } = TipeKolomTemplate.Teks;

        /// <summary>Wajib diisi sebelum surat bisa dicetak.</summary>
        public bool Wajib { get; set; }

        /// <summary>Daftar pilihan untuk tipe <see cref="TipeKolomTemplate.Pilihan"/>.</summary>
        public List<string> Pilihan { get; set; } = new();

        /// <summary>Nilai awal yang sudah terisi saat formulir dibuka.</summary>
        public string NilaiBawaan { get; set; } = string.Empty;

        /// <summary>
        /// Kunci kelompok kolom (misal "datadiri0", "kolom0") yang menautkan kolom
        /// ini kepada satu <see cref="BagianTemplateSurat"/>. Kosong untuk template
        /// lama yang masih memakai daftar kolom datar.
        /// </summary>
        public string Kelompok { get; set; } = string.Empty;

        /// <summary>Label pendek tipe untuk tampilan daftar kolom.</summary>
        public string TipeLabel => Tipe switch
        {
            TipeKolomTemplate.Nik => "NIK (16 angka)",
            TipeKolomTemplate.Angka => "Angka",
            TipeKolomTemplate.Tanggal => "Tanggal",
            TipeKolomTemplate.Paragraf => "Paragraf",
            TipeKolomTemplate.Pilihan => "Pilihan (dropdown)",
            _ => "Teks"
        };

        public KolomTemplateSurat Clone() => new()
        {
            Kunci = Kunci,
            Label = Label,
            Tipe = Tipe,
            Wajib = Wajib,
            Pilihan = new List<string>(Pilihan),
            NilaiBawaan = NilaiBawaan,
            Kelompok = Kelompok
        };
    }

    /// <summary>
    /// Satu bagian (blok) tersusun dari badan surat kustom. Urutan daftar
    /// <see cref="TemplateSuratKustom.Bagian"/> menentukan tata letak dari atas
    /// ke bawah: teks bebas, data diri seseorang, dan sekelompok kolom isian.
    /// </summary>
    public class BagianTemplateSurat
    {
        public TipeBagianTemplate Tipe { get; set; } = TipeBagianTemplate.Teks;

        // ===== Teks bebas =====

        /// <summary>Isi teks (kalimat pembuka, keperluan, penutup, dan lainnya).</summary>
        public string Isi { get; set; } = string.Empty;

        public RataBlokTemplate Rata { get; set; } = RataBlokTemplate.Kiri;
        public bool Tebal { get; set; }
        public bool Miring { get; set; }

        // ===== DataDiri & Kolom =====

        /// <summary>Judul kelompok yang tercetak di atas blok (mis. "Pemohon", "Saksi").</summary>
        public string JudulKelompok { get; set; } = string.Empty;

        /// <summary>
        /// Kunci unik blok (mis. "datadiri0", "kolom0"). Untuk DataDiri, kunci ini
        /// menjadi awalan kunci kolom (<c>[kunci]_nik</c>) agar tidak bentrok bila
        /// dipakai beberapa orang; juga dipakai untuk penandatangan dari data diri.
        /// </summary>
        public string Kunci { get; set; } = string.Empty;

        /// <summary>Kolom milik bagian ini (untuk DataDiri: field baku seseorang).</summary>
        public List<KolomTemplateSurat> Kolom { get; set; } = new();

        /// <summary>Tampilkan kolom bagian sebagai tabel bergaris.</summary>
        public bool Grid { get; set; }

        /// <summary>Judul tampilan jenis bagian untuk daftar di wizard.</summary>
        public string TipeLabel => Tipe switch
        {
            TipeBagianTemplate.DataDiri => "Data Diri",
            TipeBagianTemplate.Kolom => "Kolom Isian",
            _ => "Teks"
        };

        public BagianTemplateSurat Clone() => new()
        {
            Tipe = Tipe,
            Isi = Isi,
            Rata = Rata,
            Tebal = Tebal,
            Miring = Miring,
            JudulKelompok = JudulKelompok,
            Kunci = Kunci,
            Kolom = Kolom?.Select(k => k.Clone()).ToList() ?? new List<KolomTemplateSurat>(),
            Grid = Grid
        };
    }

    /// <summary>
    /// Definisi surat buatan pengguna (menu Template Surat). Dibuat lewat wizard:
    /// memilih elemen surat (kop, judul, nomor), menambah blok teks bebas, menentukan
    /// kolom isian, memilih tampilan grid, lalu mengatur kaki surat (tanda tangan
    /// dan/atau teks penutup).
    ///
    /// Definisi ini disimpan utuh sebagai JSON di database, sehingga penambahan
    /// pilihan baru tidak memerlukan perubahan skema tabel.
    /// </summary>
    public class TemplateSuratKustom
    {
        public int Id { get; set; }

        /// <summary>Nama template yang tampil di daftar (mis. "Surat Keterangan Usaha Baru").</summary>
        public string Nama { get; set; } = string.Empty;

        /// <summary>Keterangan singkat untuk pengguna.</summary>
        public string Deskripsi { get; set; } = string.Empty;

        // ===== Elemen surat =====

        /// <summary>Cetak kop surat desa (logo + lima baris).</summary>
        public bool PakaiKop { get; set; } = true;

        /// <summary>Judul surat (dicetak kapital, rata tengah, bergaris bawah).</summary>
        public string Judul { get; set; } = string.Empty;

        /// <summary>Baris tambahan di bawah judul (opsional).</summary>
        public string SubJudul { get; set; } = string.Empty;

        /// <summary>Cetak baris "NOMOR : …" di bawah judul.</summary>
        public bool PakaiNomor { get; set; } = true;

        /// <summary>Awalan nomor surat (kode klasifikasi), mis. 470 atau 474.3.</summary>
        public string AwalanNomor { get; set; } = "470";

        /// <summary>
        /// Pola nomor surat. Tersedia penanda: {awalan}, {urut}, {tahun}.
        /// Contoh: <c>{awalan}/{urut:000}/Ds/{tahun}</c>.
        /// </summary>
        public string PolaNomor { get; set; } = TemplateSuratNomor.PolaBawaan;

        /// <summary>Cetak baris "Desa, 12 Januari 2026" di atas badan surat (rata kanan).</summary>
        public bool PakaiTempatTanggal { get; set; }

        /// <summary>Blok teks bebas, dicetak berurutan setelah judul.</summary>
        public List<BlokTeksTemplateSurat> Blok { get; set; } = new();

        /// <summary>
        /// Susunan badan surat yang baru (blok berurutan: Teks / DataDiri / Kolom).
        /// Jika kosong, surat memakai susunan lama (<see cref="Blok"/> lalu
        /// <see cref="Kolom"/> dengan satu kelompok).
        /// </summary>
        public List<BagianTemplateSurat> Bagian { get; set; } = new();

        /// <summary>Kolom isian surat (NIK, Nama, dan lainnya).</summary>
        public List<KolomTemplateSurat> Kolom { get; set; } = new();

        /// <summary>Tampilkan kolom isian dalam bentuk tabel bergaris (grid).</summary>
        public bool PakaiGrid { get; set; }

        /// <summary>
        /// Nomor surat diketik manual saat mengisi surat (tanpa dihitung otomatis
        /// dari <see cref="PolaNomor"/>). Berguna untuk nomor tetap tanpa {urut}.
        /// </summary>
        public bool NomorManual { get; set; }

        /// <summary>Penandatangan memakai salah satu orang dari blok Data Diri (bukan Kepala Desa otomatis).</summary>
        public bool PenandatanganDariDataDiri { get; set; }

        /// <summary>Kunci blok Data Diri yang menjadi penandatangan (kosong = Kepala Desa).</summary>
        public string KunciPenandatanganDataDiri { get; set; } = string.Empty;

        // ===== Kaki surat =====

        /// <summary>Cetak blok tanda tangan pejabat desa.</summary>
        public bool PakaiTandaTangan { get; set; } = true;

        /// <summary>
        /// Jabatan penandatangan: kosong = Kepala Desa; boleh "Sekretaris Desa",
        /// "Camat", atau jabatan lain.
        /// </summary>
        public string JabatanPenandatangan { get; set; } = string.Empty;

        /// <summary>Cetak teks penutup tambahan di bawah tanda tangan.</summary>
        public bool PakaiTeksKaki { get; set; }

        /// <summary>Isi teks penutup tambahan.</summary>
        public string TeksKaki { get; set; } = string.Empty;

        // ===== Penomoran & metadata =====

        /// <summary>Nomor urut terakhir yang pernah dipakai (per template).</summary>
        public int NomorTerakhir { get; set; }

        /// <summary>Tahun saat <see cref="NomorTerakhir"/> dicatat; nomor urut mulai dari 1 lagi pada tahun baru.</summary>
        public int TahunNomor { get; set; }

        public DateTime Dibuat { get; set; }
        public DateTime Diubah { get; set; }

        /// <summary>Pengguna yang membuat template (email Google atau admin).</summary>
        public string DibuatOleh { get; set; } = string.Empty;

        // ===== Turunan / bantuan =====

        /// <summary>Judul yang dipakai di daftar bila nama template kosong.</summary>
        public string NamaTampil => string.IsNullOrWhiteSpace(Nama)
            ? (string.IsNullOrWhiteSpace(Judul) ? "(tanpa nama)" : Judul)
            : Nama;

        /// <summary>Ada minimal satu kolom isian.</summary>
        public bool AdaKolom => Kolom != null && Kolom.Count > 0;

        /// <summary>Jumlah kolom isian (untuk kolom daftar di layar).</summary>
        public int JumlahKolom => Kolom?.Count ?? 0;

        /// <summary>Ada minimal satu bagian terurut (susunan baru).</summary>
        public bool AdaBagian => Bagian != null && Bagian.Count > 0;

        /// <summary>
        /// Susunan badan surat yang dipakai. Bila <see cref="Bagian"/> kosong
        /// (template lama), dibentuk dari blok teks lalu satu kelompok kolom.
        /// </summary>
        public List<BagianTemplateSurat> BagianEfektif
        {
            get
            {
                if (AdaBagian) return Bagian;
                var hasil = new List<BagianTemplateSurat>();
                if (Blok != null)
                {
                    foreach (var b in Blok.Where(x => !string.IsNullOrWhiteSpace(x.Isi)))
                    {
                        hasil.Add(new BagianTemplateSurat
                        {
                            Tipe = TipeBagianTemplate.Teks,
                            Isi = b.Isi,
                            Rata = b.Rata,
                            Tebal = b.Tebal,
                            Miring = b.Miring
                        });
                    }
                }
                if (AdaKolom)
                {
                    hasil.Add(new BagianTemplateSurat
                    {
                        Tipe = TipeBagianTemplate.Kolom,
                        Kolom = Kolom,
                        Grid = PakaiGrid
                    });
                }
                return hasil;
            }
        }

        /// <summary>Penanda ringkas penggunaan kop di daftar template.</summary>
        public string LabelKop => PakaiKop ? "Ada" : "Tanpa";

        /// <summary>Penanda ringkas penggunaan nomor surat di daftar template.</summary>
        public string LabelNomor => PakaiNomor ? "Ada" : "Tanpa";

        /// <summary>Contoh nomor surat berikutnya menurut awalan & pola template ini.</summary>
        public string NomorBerikutnyaTampil
        {
            get
            {
                int tahun = DateTime.Now.Year;
                int urut = (TahunNomor == tahun ? NomorTerakhir : 0) + 1;
                return TemplateSuratNomor.Bangun(PolaNomor, AwalanNomor, urut, tahun);
            }
        }

        /// <summary>Ada minimal satu blok teks bebas.</summary>
        public bool AdaBlok => Blok != null && Blok.Count > 0;

        /// <summary>Jumlah elemen yang aktif (untuk ringkasan di kartu daftar).</summary>
        public int JumlahElemen
        {
            get
            {
                int jumlah =
                    (PakaiKop ? 1 : 0) +
                    (string.IsNullOrWhiteSpace(Judul) ? 0 : 1) +
                    (PakaiNomor ? 1 : 0) +
                    (PakaiTempatTanggal ? 1 : 0) +
                    (PakaiTandaTangan ? 1 : 0) +
                    (PakaiTeksKaki && !string.IsNullOrWhiteSpace(TeksKaki) ? 1 : 0);
                foreach (var bagian in BagianEfektif)
                {
                    if (bagian.Tipe == TipeBagianTemplate.Teks && !string.IsNullOrWhiteSpace(bagian.Isi)) jumlah++;
                    else if (bagian.Tipe != TipeBagianTemplate.Teks) jumlah++;
                }
                return jumlah;
            }
        }

        /// <summary>Ringkasan singkat susunan surat untuk ditampilkan pada daftar.</summary>
        public string RingkasanSusunan
        {
            get
            {
                var bagian = new List<string>();
                if (PakaiKop) bagian.Add("kop desa");
                if (!string.IsNullOrWhiteSpace(Judul)) bagian.Add("judul");
                if (PakaiNomor) bagian.Add("nomor");
                if (PakaiTempatTanggal) bagian.Add("tempat & tanggal");

                int teks = 0, dataDiri = 0, kolom = 0;
                foreach (var b in BagianEfektif)
                {
                    if (b.Tipe == TipeBagianTemplate.Teks && !string.IsNullOrWhiteSpace(b.Isi)) teks++;
                    else if (b.Tipe == TipeBagianTemplate.DataDiri) dataDiri++;
                    else if (b.Tipe == TipeBagianTemplate.Kolom) kolom++;
                }
                if (teks > 0) bagian.Add($"{teks} teks");
                if (dataDiri > 0) bagian.Add($"{dataDiri} data diri");
                if (kolom > 0) bagian.Add($"{kolom} kolom{(BagianEfektif.Any(x => x.Tipe == TipeBagianTemplate.Kolom && x.Grid) ? " (grid)" : "")}");

                if (PakaiTandaTangan)
                    bagian.Add(PenandatanganDariDataDiri ? "ttd (data diri)" : "tanda tangan");
                if (PakaiTeksKaki && !string.IsNullOrWhiteSpace(TeksKaki)) bagian.Add("teks kaki");
                return bagian.Count == 0 ? "Belum ada elemen" : string.Join(" • ", bagian);
            }
        }

        /// <summary>Label tampilan untuk penandatangan (dipakai di kartu daftar).</summary>
        public string JabatanTampil =>
            string.IsNullOrWhiteSpace(JabatanPenandatangan) ? "Kepala Desa" : JabatanPenandatangan.Trim();

        /// <summary>
        /// Nomor surat yang disarankan untuk urutan berikutnya. Bila tahun berubah,
        /// urutan dimulai lagi dari 1.
        /// </summary>
        public string NomorBerikutnya(DateTime tanggal, out int urutBaru)
        {
            int tahun = tanggal.Year;
            urutBaru = (TahunNomor == tahun ? NomorTerakhir : 0) + 1;
            return TemplateSuratNomor.Bangun(PolaNomor, AwalanNomor, urutBaru, tahun);
        }

        /// <summary>
        /// Salinan definisi (tanpa identitas database) — dipakai tombol Duplikat dan
        /// saat wizard mengedit supaya perubahan tidak menyentuh objek daftar.
        /// </summary>
        public TemplateSuratKustom Clone(bool termasukIdentitas = false)
        {
            var salinan = new TemplateSuratKustom
            {
                Id = termasukIdentitas ? Id : 0,
                Nama = Nama,
                Deskripsi = Deskripsi,
                PakaiKop = PakaiKop,
                Judul = Judul,
                SubJudul = SubJudul,
                PakaiNomor = PakaiNomor,
                AwalanNomor = AwalanNomor,
                PolaNomor = PolaNomor,
                PakaiTempatTanggal = PakaiTempatTanggal,
                Blok = Blok?.Select(b => b.Clone()).ToList() ?? new List<BlokTeksTemplateSurat>(),
                Bagian = Bagian?.Select(b => b.Clone()).ToList() ?? new List<BagianTemplateSurat>(),
                Kolom = Kolom?.Select(k => k.Clone()).ToList() ?? new List<KolomTemplateSurat>(),
                PakaiGrid = PakaiGrid,
                NomorManual = NomorManual,
                PenandatanganDariDataDiri = PenandatanganDariDataDiri,
                KunciPenandatanganDataDiri = KunciPenandatanganDataDiri,
                PakaiTandaTangan = PakaiTandaTangan,
                JabatanPenandatangan = JabatanPenandatangan,
                PakaiTeksKaki = PakaiTeksKaki,
                TeksKaki = TeksKaki,
                NomorTerakhir = termasukIdentitas ? NomorTerakhir : 0,
                TahunNomor = termasukIdentitas ? TahunNomor : 0,
                Dibuat = Dibuat,
                Diubah = Diubah,
                DibuatOleh = DibuatOleh
            };
            return salinan;
        }

        /// <summary>
        /// Pemakaian nilai kolom untuk pratinjau: contoh isi yang masuk akal supaya
        /// pratinjau tetap enak dibaca walau pengguna belum mengisi apa pun.
        /// </summary>
        public static string ContohNilai(KolomTemplateSurat kolom) => kolom?.Tipe switch
        {
            TipeKolomTemplate.Nik => "3215012345678901",
            TipeKolomTemplate.Tanggal => DateTime.Today.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
            TipeKolomTemplate.Angka => "150",
            TipeKolomTemplate.Paragraf => "Contoh isi paragraf surat yang dapat diganti dengan keterangan sebenarnya.",
            TipeKolomTemplate.Pilihan => (kolom.Pilihan != null && kolom.Pilihan.Count > 0) ? kolom.Pilihan[0] : "Contoh",
            _ => string.IsNullOrWhiteSpace(kolom?.Label) ? "Contoh isi" : "Contoh " + kolom!.Label
        };
    }

    /// <summary>Penyusun nomor surat template kustom dari pola sederhana.</summary>
    public static class TemplateSuratNomor
    {
        /// <summary>Pola bawaan: 470/001/Ds/2026.</summary>
        public const string PolaBawaan = "{awalan}/{urut:000}/Ds/{tahun}";

        /// <summary>Sisipkan awalan, urut, dan tahun ke dalam pola nomor.</summary>
        public static string Bangun(string? pola, string? awalan, int urut, int tahun)
        {
            string bentuk = string.IsNullOrWhiteSpace(pola) ? PolaBawaan : pola.Trim();
            string awalanBersih = (awalan ?? string.Empty).Trim();

            // Urut dengan pad nol: {urut:000} → 007.
            bentuk = Regex.Replace(bentuk, @"\{urut(?::(0+))?\}",
                m => urut.ToString(m.Groups[1].Success ? "D" + m.Groups[1].Value.Length : "D", CultureInfo.InvariantCulture));

            bentuk = bentuk.Replace("{tahun}", tahun.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
            bentuk = bentuk.Replace("{awalan}", awalanBersih, StringComparison.OrdinalIgnoreCase);

            // Rapikan garis miring ganda bila awalan dikosongkan.
            return Regex.Replace(bentuk, "//+", "/").Trim('/').Trim();
        }

        /// <summary>
        /// Pola dianggap sah bila memuat setidaknya satu penanda ({urut}, {awalan},
        /// atau {tahun}) dan tidak memuat penanda yang tidak dikenal. Nomor tetap
        /// tanpa {urut} diperbolehkan (dipakai bersama <see cref="TemplateSuratKustom.NomorManual"/>).
        /// </summary>
        public static bool PolaValid(string? pola, out string pesan)
        {
            pesan = string.Empty;
            string bentuk = (pola ?? string.Empty).Trim();
            if (bentuk.Length == 0)
            {
                pesan = "Pola nomor wajib diisi.";
                return false;
            }
            if (bentuk.Length > 80)
            {
                pesan = "Pola nomor terlalu panjang (maksimal 80 karakter).";
                return false;
            }

            // Setiap {…} harus berupa penanda yang dikenal.
            foreach (Match m in Regex.Matches(bentuk, @"\{([^}]*)\}"))
            {
                string isi = m.Groups[1].Value;
                bool sah = Regex.IsMatch(isi, @"^urut(:0+)?$", RegexOptions.IgnoreCase)
                    || Regex.IsMatch(isi, @"^(awalan|tahun)$", RegexOptions.IgnoreCase);
                if (!sah)
                {
                    pesan = "Pola nomor memuat penanda tidak dikenal: {" + isi + "}.";
                    return false;
                }
            }
            return true;
        }

        /// <summary>Awalan yang sah: huruf, angka, titik, tanda hubung, garis bawah.</summary>
        public static bool AwalanValid(string? awalan, out string pesan)
        {
            pesan = string.Empty;
            string nilai = (awalan ?? string.Empty).Trim();
            if (nilai.Length == 0)
            {
                pesan = "Awalan nomor belum diisi.";
                return false;
            }
            if (nilai.Length > 16)
            {
                pesan = "Awalan nomor maksimal 16 karakter.";
                return false;
            }
            if (!nilai.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_'))
            {
                pesan = "Awalan nomor hanya boleh huruf, angka, titik, tanda hubung, dan garis bawah (tanpa '/' karena dipakai memisah segmen nomor).";
                return false;
            }
            return true;
        }
    }
}
