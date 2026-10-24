using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace SuDesApp.Wpf.Utilities
{
    /// <summary>
    /// Satu daerah yang punya halaman unduhan formulir administrasi kependudukan
    /// resmi. Katalog ini menjadi sumber tunggal untuk halaman Pengaturan Formulir
    /// (menyesuaikan pilihan kabupaten di Pengaturan Surat) dan halaman Tentang.
    /// </summary>
    public sealed class SumberFormulirDaerah
    {
        public SumberFormulirDaerah(
            string nama, string kodeKabupaten, string url, bool unduhanOtomatis, string catatan)
        {
            Nama = nama;
            KodeKabupaten = kodeKabupaten;
            Url = url;
            UnduhanOtomatis = unduhanOtomatis;
            Catatan = catatan;
        }

        /// <summary>Nama daerah lengkap, mis. “Kabupaten Karawang”.</summary>
        public string Nama { get; }

        /// <summary>Kode Kemendagri tingkat kabupaten/kota, mis. “32.15”.</summary>
        public string KodeKabupaten { get; }

        /// <summary>Halaman resmi tempat formulir diunduh.</summary>
        public string Url { get; }

        /// <summary>
        /// Benar bila aplikasi bisa mengambil daftar formulir sendiri dari
        /// halaman ini. Hanya INSAN Karawang yang punya pembaca halaman khusus;
        /// daerah lain masih diunduh manual lalu ditambahkan lewat tombol Tambah.
        /// </summary>
        public bool UnduhanOtomatis { get; }

        /// <summary>Keterangan singkat isi halaman sumber.</summary>
        public string Catatan { get; }

        /// <summary>Alamat siap tampil (tanpa skema) untuk kartu dan tooltip.</summary>
        public string TautanTampil => new Uri(Url).Host;

        /// <summary>Jenis wilayah: “Kabupaten”, “Kota”, atau kosong.</summary>
        internal string Jenis => NamaWilayahDaerah.DariNama(Nama).Jenis;

        /// <summary>Nama daerah tanpa awalan “Kabupaten”/“Kota”.</summary>
        internal string NamaBersih => NamaWilayahDaerah.DariNama(Nama).Nama;

        /// <summary>Kunci pencocokan nama: “kabupatenkarawang”, “kotasukabumi”, …</summary>
        internal string NamaNormal => NamaWilayahDaerah.Normalisasi(Jenis, NamaBersih);
    }

    /// <summary>
    /// Katalog sumber formulir adminduk. Daftarnya dibaca dari berkas konfigurasi
    /// <see cref="Path"/> di profil pengguna, sehingga tautan bisa diperbarui
    /// tanpa merilis ulang aplikasi. Bila berkas belum ada, dibuat otomatis dari
    /// daftar <see cref="Bawaan"/>; bila berkasnya rusak, daftar bawaan dipakai
    /// agar halaman tidak pernah kosong.
    /// </summary>
    public static class KatalogFormulirDaerah
    {
        /// <summary>Host yang benar-benar punya pembaca halaman di aplikasi ini.</summary>
        public const string HostUnduhanOtomatis = "infosadaradmindukkarawang.id";

        private const string NamaFolder = "SuDesApp";
        private const string NamaBerkas = "sumber-formulir-daerah.json";
        private const string NamaFolderBawaan = "Bawaan";

        /// <summary>
        /// Lokasi berkas bawaan yang ikut terpasang bersama aplikasi. Berkas ini
        /// milik aplikasi (bukan data desa) dan bisa diganti lewat tambalan tanpa
        /// merilis ulang program.
        /// </summary>
        public static string PathBawaanAplikasi => System.IO.Path.Combine(
            AppContext.BaseDirectory, NamaFolderBawaan, NamaBerkas);

        private static IReadOnlyList<SumberFormulirDaerah>? _bawaan;
        private static int _versiBawaan = 1;

        /// <summary>
        /// Versi daftar bawaan yang terakhir dibaca. Dipakai untuk menambahkan
        /// daerah baru dari bawaan ke berkas aktif lama tanpa merilis ulang.
        /// </summary>
        public static int VersiBawaan
        {
            get
            {
                _ = Bawaan; // pastikan berkas bawaan sudah dibaca
                return _versiBawaan;
            }
        }

        /// <summary>
        /// Daftar bawaan — dibaca dari <c>Bawaan/sumber-formulir-daerah.json</c>
        /// di folder aplikasi. Bila berkas hilang atau tidak terbaca, daftar
        /// darurat (Karawang) dipakai agar halaman tidak pernah kosong.
        /// </summary>
        public static IReadOnlyList<SumberFormulirDaerah> Bawaan
        {
            get
            {
                if (_bawaan is null)
                {
                    _bawaan = MuatBawaan(out _);
                }

                return _bawaan;
            }
        }

        /// <summary>
        /// Daftar darurat: satu-satunya sumber yang punya pembaca halaman khusus.
        /// Dipakai hanya bila berkas bawaan tidak ada/rusak, supaya unduhan
        /// otomatis Karawang tetap bisa ditawarkan.
        /// </summary>
        private static IReadOnlyList<SumberFormulirDaerah> DaftarDarurat { get; } =
            new List<SumberFormulirDaerah>
            {
                new(
                    "Kabupaten Karawang", "32.15",
                    "https://infosadaradmindukkarawang.id", unduhanOtomatis: true,
                    "INSAN Karawang — daftar formulir F-1.01 dan blanko surat desa, bisa diunduh langsung dari aplikasi."),
            };

        /// <summary>
        /// Baca daftar bawaan dari folder aplikasi tanpa mengubah daftar yang
        /// sedang berlaku. Catatan hasil (berhasil/gagal) berguna untuk log start.
        /// </summary>
        public static IReadOnlyList<SumberFormulirDaerah> MuatBawaan(out string catatan)
        {
            string berkas = PathBawaanAplikasi;

            try
            {
                if (!File.Exists(berkas))
                {
                    catatan = $"berkas bawaan {berkas} tidak ditemukan; daftar darurat (Karawang) dipakai.";
                    return DaftarDarurat;
                }

                string json = File.ReadAllText(berkas);
                var hasil = Baca(json, out var masalah, out int versi);

                if (hasil.Count == 0)
                {
                    catatan = $"berkas bawaan {berkas} tidak memuat sumber yang sah; daftar darurat (Karawang) dipakai." +
                              (masalah.Count > 0 ? " " + string.Join(" | ", masalah) : string.Empty);
                    return DaftarDarurat;
                }

                _versiBawaan = versi > 0 ? versi : 1;
                catatan = $"{hasil.Count} sumber bawaan dibaca dari {berkas} (versi {_versiBawaan})." +
                          (masalah.Count > 0 ? $" Dilewati: {string.Join(" | ", masalah)}" : string.Empty);
                return hasil;
            }
            catch (Exception ex)
            {
                catatan = $"gagal membaca berkas bawaan {berkas} ({ex.Message}); daftar darurat (Karawang) dipakai.";
                return DaftarDarurat;
            }
        }

        private static IReadOnlyList<SumberFormulirDaerah> _semua = Bawaan;

        /// <summary>
        /// Lokasi berkas konfigurasi bawaan: profil pengguna Windows, bukan folder
        /// instalasi — supaya suntingan operator tidak tertimpa saat aplikasi
        /// diperbarui.
        /// </summary>
        public static string PathBawaan => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            NamaFolder,
            NamaBerkas);

        private static string? _path;

        /// <summary>Lokasi berkas konfigurasi yang dipakai (dapat diarahkan saat pengujian).</summary>
        public static string Path
        {
            get => _path ?? PathBawaan;
            set => _path = string.IsNullOrWhiteSpace(value) ? null : value;
        }

        /// <summary>Daftar yang sedang berlaku — sumber bagi kedua halaman.</summary>
        public static IReadOnlyList<SumberFormulirDaerah> Semua => _semua;

        /// <summary>
        /// Muat ulang katalog dari berkas konfigurasi. Dipanggil saat aplikasi
        /// start dan setiap halaman Pengaturan Formulir disegarkan, sehingga
        /// suntingan berkas langsung terpakai tanpa rilis baru.
        /// </summary>
        public static void Muat(ILogger? logger = null)
        {
            var daftar = MuatDaftar(Path, out string catatan);
            _semua = daftar;

            if (catatan.Length > 0)
            {
                logger?.LogInformation("Katalog sumber formulir: {Catatan}", catatan);
            }
        }

        /// <summary>
        /// Baca daftar dari berkas (tanpa mengubah katalog yang sedang berlaku).
        /// Berkas yang belum ada dibuat dari daftar bawaan; berkas rusak/kosong
        /// menghasilkan daftar bawaan agar halaman tetap bisa dipakai.
        /// </summary>
        public static IReadOnlyList<SumberFormulirDaerah> MuatDaftar(string? path, out string catatan)
        {
            string berkas = string.IsNullOrWhiteSpace(path) ? Path : path;

            try
            {
                if (!File.Exists(berkas))
                {
                    bool tertulis = TulisBawaan(berkas);
                    catatan = tertulis
                        ? $"berkas belum ada — dibuat di {berkas}; {Bawaan.Count} sumber bawaan dipakai."
                        : $"berkas {berkas} belum ada dan tidak bisa dibuat; {Bawaan.Count} sumber bawaan dipakai.";
                    return Bawaan;
                }

                string json = File.ReadAllText(berkas);
                var hasil = Baca(json, out var masalah, out int versiAktif);

                if (hasil.Count == 0)
                {
                    catatan = $"berkas {berkas} tidak memuat sumber yang sah; daftar bawaan dipakai." +
                              (masalah.Count > 0 ? " " + string.Join(" | ", masalah) : string.Empty);
                    return Bawaan;
                }

                string catatanVersi = string.Empty;

                // Berkas aktif dari versi lama tetap dihormati, tetapi daerah baru
                // yang ditambahkan pada berkas bawaan ikut disisipkan. Suntingan
                // operator (mis. URL yang diperbaiki) tidak ditimpa karena hanya
                // entri yang belum ada yang ditambahkan.
                if (versiAktif < VersiBawaan)
                {
                    var kodeAda = hasil.Select(s => NormalisasiKode(s.KodeKabupaten))
                        .ToHashSet(StringComparer.Ordinal);
                    var namaAda = hasil.Select(s => s.NamaNormal).ToHashSet(StringComparer.Ordinal);

                    var tambahan = Bawaan
                        .Where(s => !kodeAda.Contains(NormalisasiKode(s.KodeKabupaten))
                                 && !namaAda.Contains(s.NamaNormal))
                        .ToList();

                    var gabungan = hasil.Concat(tambahan).ToList();
                    bool tertulis = TulisDaftar(berkas, gabungan, VersiBawaan);

                    if (tambahan.Count > 0)
                    {
                        catatanVersi = $" {tambahan.Count} sumber baru dari bawaan versi {VersiBawaan} ditambahkan" +
                                       (tertulis ? " dan disimpan ke berkas aktif." : " (berkas aktif tidak bisa ditulis ulang).");
                    }
                    else if (tertulis)
                    {
                        catatanVersi = $" Versi berkas aktif dinaikkan ke {VersiBawaan}.";
                    }

                    hasil = gabungan;
                }

                catatan = $"{hasil.Count} sumber dibaca dari {berkas}." +
                          (masalah.Count > 0 ? $" Dilewati: {string.Join(" | ", masalah)}" : string.Empty) +
                          catatanVersi;
                return hasil;
            }
            catch (Exception ex)
            {
                catatan = $"gagal membaca {berkas} ({ex.Message}); daftar bawaan dipakai.";
                return Bawaan;
            }
        }

        /// <summary>
        /// Baca isi JSON konfigurasi. Entri tanpa nama/kode/URL sah dilewati,
        /// kode ganda hanya diambil yang pertama, dan unduhan otomatis ditolak
        /// untuk host yang belum punya pembaca halaman di aplikasi.
        /// </summary>
        public static IReadOnlyList<SumberFormulirDaerah> Baca(string? json, out IReadOnlyList<string> masalah)
            => Baca(json, out masalah, out _);

        /// <summary>Seperti <see cref="Baca(string?, out IReadOnlyList{string})"/>, sekaligus membaca versi berkas.</summary>
        public static IReadOnlyList<SumberFormulirDaerah> Baca(
            string? json, out IReadOnlyList<string> masalah, out int versi)
        {
            var daftar = new List<SumberFormulirDaerah>();
            var keluhan = new List<string>();
            masalah = keluhan;
            versi = 0;

            if (string.IsNullOrWhiteSpace(json))
            {
                keluhan.Add("isi berkas kosong.");
                return daftar;
            }

            BerkasSumberFormulir? berkas;
            try
            {
                berkas = JsonSerializer.Deserialize<BerkasSumberFormulir>(json, OpsiJson);
            }
            catch (JsonException ex)
            {
                keluhan.Add("JSON tidak sah: " + ex.Message);
                return daftar;
            }

            if (berkas?.Sumber is null || berkas.Sumber.Count == 0)
            {
                keluhan.Add("tidak ada kunci \"Sumber\" yang berisi daftar daerah.");
                return daftar;
            }

            versi = berkas.Versi > 0 ? berkas.Versi : 1;

            var kodeTerpakai = new HashSet<string>(StringComparer.Ordinal);
            var namaTerpakai = new HashSet<string>(StringComparer.Ordinal);

            foreach (var entri in berkas.Sumber)
            {
                if (entri is null) continue;

                string nama = (entri.Nama ?? string.Empty).Trim();
                string kode = (entri.KodeKabupaten ?? string.Empty).Trim();
                string url = (entri.Url ?? string.Empty).Trim();

                if (nama.Length == 0 || kode.Length == 0 || url.Length == 0)
                {
                    keluhan.Add("ada entri tanpa Nama/KodeKabupaten/Url.");
                    continue;
                }

                if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
                    (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
                {
                    keluhan.Add($"{nama}: Url bukan alamat http/https yang sah.");
                    continue;
                }

                string kunciKode = NormalisasiKode(kode);
                string kunciNama = NamaWilayahDaerah.Normalisasi(
                    NamaWilayahDaerah.DariNama(nama).Jenis, NamaWilayahDaerah.DariNama(nama).Nama);

                if (!kodeTerpakai.Add(kunciKode) || !namaTerpakai.Add(kunciNama))
                {
                    keluhan.Add($"{nama}: kode/nama ganda, entri setelah yang pertama dilewati.");
                    continue;
                }

                bool otomatis = entri.UnduhanOtomatis;
                if (otomatis && !string.Equals(uri.Host, HostUnduhanOtomatis, StringComparison.OrdinalIgnoreCase))
                {
                    otomatis = false;
                    keluhan.Add($"{nama}: UnduhanOtomatis diabaikan karena {uri.Host} belum punya pembaca halaman.");
                }

                daftar.Add(new SumberFormulirDaerah(
                    nama, kode, url, otomatis, (entri.Catatan ?? string.Empty).Trim()));
            }

            return daftar;
        }

        /// <summary>
        /// Cari sumber yang cocok dengan pilihan wilayah pada Pengaturan Surat.
        /// Kode Kemendagri dipakai lebih dulu; nama hanya cadangan untuk data
        /// desa lama yang belum menyimpan kode.
        /// </summary>
        public static SumberFormulirDaerah? Cari(
            string? kodeKabupaten, string? jenisKabupaten, string? namaKabupaten)
        {
            var semua = Semua;

            string kode = NormalisasiKode(kodeKabupaten);
            if (kode.Length > 0)
            {
                var lewatKode = semua.FirstOrDefault(s => NormalisasiKode(s.KodeKabupaten) == kode);
                if (lewatKode != null)
                {
                    return lewatKode;
                }
            }

            var wilayah = NamaWilayahDaerah.DariPilihan(jenisKabupaten, namaKabupaten);
            if (wilayah.Nama.Length == 0)
            {
                return null;
            }

            // Jenis wilayah diketahui → kunci lengkap, mis. “kabupatenkarawang”.
            if (wilayah.Jenis.Length > 0)
            {
                string kunci = NamaWilayahDaerah.Normalisasi(wilayah.Jenis, wilayah.Nama);
                return semua.FirstOrDefault(s => s.NamaNormal == kunci);
            }

            // Jenis tidak tersimpan (data lama): cocokkan nama saja, tetapi hanya
            // bila tunggal — “Sukabumi” bisa berarti Kabupaten atau Kota.
            string namaSaja = NamaWilayahDaerah.Normalisasi(string.Empty, wilayah.Nama);
            var kandidat = semua
                .Where(s => NamaWilayahDaerah.Normalisasi(string.Empty, s.NamaBersih) == namaSaja)
                .ToList();

            return kandidat.Count == 1 ? kandidat[0] : null;
        }

        // =====================================================================
        // Berkas konfigurasi
        // =====================================================================

        private static readonly JsonSerializerOptions OpsiJson = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        private sealed class BerkasSumberFormulir
        {
            [JsonPropertyName("Versi")]
            public int Versi { get; set; } = 1;

            [JsonPropertyName("Keterangan")]
            public string? Keterangan { get; set; }

            [JsonPropertyName("Sumber")]
            public List<EntriSumberFormulir>? Sumber { get; set; }
        }

        private sealed class EntriSumberFormulir
        {
            [JsonPropertyName("Nama")]
            public string? Nama { get; set; }

            [JsonPropertyName("KodeKabupaten")]
            public string? KodeKabupaten { get; set; }

            [JsonPropertyName("Url")]
            public string? Url { get; set; }

            [JsonPropertyName("UnduhanOtomatis")]
            public bool UnduhanOtomatis { get; set; }

            [JsonPropertyName("Catatan")]
            public string? Catatan { get; set; }
        }

        private const string KeteranganBerkas =
            "Daftar halaman unduhan formulir administrasi kependudukan per kabupaten/kota. " +
            "Berkas ini disalin dari Bawaan\\sumber-formulir-daerah.json di folder aplikasi saat pertama dibuka. " +
            "Ubah berkas ini lalu tekan Segarkan di halaman Pengaturan Formulir — perubahan langsung " +
            "dipakai tanpa merilis ulang aplikasi. UnduhanOtomatis hanya berlaku untuk host " +
            HostUnduhanOtomatis + "; daerah lain diunduh manual lalu ditambahkan lewat tombol Tambah.";

        /// <summary>Tulis berkas bawaan saat berkas aktif belum ada.</summary>
        private static bool TulisBawaan(string berkas)
            => TulisDaftar(berkas, Bawaan, VersiBawaan);

        /// <summary>
        /// Tulis daftar sumber ke berkas JSON. Dipakai untuk membuat berkas aktif
        /// pertama kali dan saat menyisipkan daerah baru dari bawaan versi lebih
        /// baru. Kegagalan menulis tidak menghentikan aplikasi — daftar di memori
        /// tetap dipakai.
        /// </summary>
        private static bool TulisDaftar(
            string berkas, IEnumerable<SumberFormulirDaerah> daftar, int versi)
        {
            try
            {
                string? folder = System.IO.Path.GetDirectoryName(berkas);
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                var isi = new BerkasSumberFormulir
                {
                    Versi = versi,
                    Keterangan = KeteranganBerkas,
                    Sumber = daftar.Select(s => new EntriSumberFormulir
                    {
                        Nama = s.Nama,
                        KodeKabupaten = s.KodeKabupaten,
                        Url = s.Url,
                        UnduhanOtomatis = s.UnduhanOtomatis,
                        Catatan = s.Catatan,
                    }).ToList(),
                };

                File.WriteAllText(berkas, JsonSerializer.Serialize(isi, OpsiJson), new UTF8Encoding(false));
                return true;
            }
            catch
            {
                // Tidak bisa menulis profil pengguna bukan alasan menghentikan
                // aplikasi — daftar bawaan tetap dipakai.
                return false;
            }
        }

        /// <summary>Kode “32.15” dan “3215” dianggap sama; selain angka dibuang.</summary>
        internal static string NormalisasiKode(string? kode)
        {
            if (string.IsNullOrWhiteSpace(kode))
            {
                return string.Empty;
            }

            var hasil = new StringBuilder(kode.Length);
            foreach (char huruf in kode.Trim())
            {
                if (char.IsDigit(huruf))
                {
                    hasil.Append(huruf);
                }
            }

            return hasil.ToString();
        }
    }

    /// <summary>Pemisah awalan wilayah dan pencocokan nama yang seragam.</summary>
    internal static class NamaWilayahDaerah
    {
        /// <summary>Pecah “Kabupaten Karawang” menjadi jenis + nama bersih.</summary>
        public static (string Jenis, string Nama) DariNama(string? nama)
        {
            string teks = (nama ?? string.Empty).Trim();
            foreach (string awalan in new[] { "Kabupaten", "Kota" })
            {
                if (teks.StartsWith(awalan + " ", StringComparison.OrdinalIgnoreCase))
                {
                    return (awalan, teks[(awalan.Length + 1)..].Trim());
                }
            }

            return (string.Empty, teks);
        }

        /// <summary>Gabungkan pilihan Pengaturan Surat (jenis terpisah dari nama).</summary>
        public static (string Jenis, string Nama) DariPilihan(string? jenis, string? nama)
        {
            var dariNama = DariNama(nama);

            // Jenis eksplisit menang; nama yang masih berawalan “Kabupaten …”
            // sudah dipecah oleh DariNama.
            string jenisBersih = string.IsNullOrWhiteSpace(jenis)
                ? dariNama.Jenis
                : jenis.Trim();

            return (jenisBersih, dariNama.Nama);
        }

        /// <summary>“kabupatenkarawang” — huruf kecil tanpa spasi/tanda baca.</summary>
        public static string Normalisasi(string? jenis, string? nama)
        {
            string gabung = (jenis ?? string.Empty) + (nama ?? string.Empty);
            var hasil = new StringBuilder(gabung.Length);
            foreach (char huruf in gabung.Normalize(NormalizationForm.FormC))
            {
                if (char.IsLetterOrDigit(huruf))
                {
                    hasil.Append(char.ToLowerInvariant(huruf));
                }
            }

            return hasil.ToString();
        }
    }
}
