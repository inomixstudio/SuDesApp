using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace SuDesApp.Wilayah
{
    /// <summary>
    /// Sumber data wilayah administratif Indonesia (provinsi → kabupaten/kota →
    /// kecamatan → desa/kelurahan + kode pos saran) yang dibundel bersama aplikasi.
    ///
    /// Berkasnya satu berkas TSV terkompresi (lihat Tools/WilayahGen) berisi seluruh
    /// Indonesia, dibaca sekali secara malas lalu diindeks di memori: kantor desa
    /// tidak selalu punya internet, jadi pilihan wilayah tidak boleh bergantung pada
    /// layanan daring. Bila berkasnya hilang atau rusak, <see cref="AdaData"/> false
    /// dan halaman Pengaturan kembali ke isian manual — bukan galat yang menghalangi
    /// pengguna mengisi data desanya.
    /// </summary>
    public sealed class WilayahDataStore
    {
        /// <summary>Kode provinsi Jawa Barat. Aplikasi ini buat kantor desa di Jawa Barat, jadi
        /// daftar provinsi dibuka dari sini (provinsi lain tetap tersedia di bawahnya).</summary>
        public const string KodeJawaBarat = "32";

        /// <summary>Letak berkas data pada hasil build/pemasangan.</summary>
        public static readonly string JalurBawaan = Path.Combine(
            AppContext.BaseDirectory, "Data", "Wilayah", "wilayah.tsv.gz");

        private readonly ILogger<WilayahDataStore>? _logger;
        private readonly string _jalur;
        private readonly Lazy<IsiWilayah> _isi;

        public WilayahDataStore(ILogger<WilayahDataStore>? logger = null, string? jalur = null)
        {
            _logger = logger;
            _jalur = jalur ?? JalurBawaan;
            _isi = new Lazy<IsiWilayah>(Muat, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <summary>Benar bila data wilayah terbaca dan siap dipakai.</summary>
        public bool AdaData => _isi.Value.Berhasil;

        /// <summary>Keterangan singkat untuk pengguna bila data tidak tersedia.</summary>
        public string PesanMasalah => _isi.Value.Masalah;

        /// <summary>Asal-usul data (acuan Kepmendagri, tanggal sumber, jumlah baris).</summary>
        public WilayahInfo Info => _isi.Value.Info;

        /// <summary>
        /// Daftar provinsi diurutkan menurut nama. <paramref name="kodeDiutamakan"/>
        /// (kode Jawa Barat) diletakkan paling atas supaya pengguna di Jawa Barat
        /// tidak perlu menggulir daftar untuk pilihan yang paling sering dipakai.
        /// </summary>
        public IReadOnlyList<WilayahDaerah> DaftarProvinsi(string? kodeDiutamakan = KodeJawaBarat)
        {
            var semua = _isi.Value.Anak.TryGetValue(string.Empty, out var akar)
                ? akar
                : new List<WilayahDaerah>();

            if (string.IsNullOrWhiteSpace(kodeDiutamakan))
            {
                return semua;
            }

            var utama = semua.Where(w => w.Kode == kodeDiutamakan).ToList();
            if (utama.Count == 0)
            {
                return semua;
            }

            return utama.Concat(semua.Where(w => w.Kode != kodeDiutamakan)).ToList();
        }

        /// <summary>Wilayah di bawah satu induk (provinsi → kabupaten/kota, dst), urut nama.</summary>
        public IReadOnlyList<WilayahDaerah> DaftarAnak(string? kodeInduk)
        {
            string kunci = (kodeInduk ?? string.Empty).Trim();
            return _isi.Value.Anak.TryGetValue(kunci, out var anak)
                ? anak
                : new List<WilayahDaerah>();
        }

        /// <summary>Wilayah menurut kodenya, atau null bila tidak ada.</summary>
        public WilayahDaerah? Ambil(string? kode)
        {
            string kunci = (kode ?? string.Empty).Trim();
            return kunci.Length > 0 && _isi.Value.PerKode.TryGetValue(kunci, out var wilayah)
                ? wilayah
                : null;
        }

        /// <summary>
        /// Semua wilayah berjenis tertentu yang namanya sama dengan
        /// <paramref name="namaSudahDinormalisasi"/> (lihat
        /// <see cref="WilayahPencocokan.Normalisasi"/>). Dipakai untuk mencocokkan
        /// isian lama; hasilnya bisa lebih dari satu karena banyak nama wilayah kembar.
        /// </summary>
        public IReadOnlyList<WilayahDaerah> CariBanyak(WilayahJenis jenis, string namaSudahDinormalisasi)
        {
            if (namaSudahDinormalisasi.Length == 0)
            {
                return Array.Empty<WilayahDaerah>();
            }

            return _isi.Value.PerNama.TryGetValue((jenis, namaSudahDinormalisasi), out var hasil)
                ? hasil
                : Array.Empty<WilayahDaerah>();
        }

        /// <summary>Pencarian bebas (dipakai pengujian dan pencarian lanjutan): nama memuat teks.</summary>
        public IReadOnlyList<WilayahDaerah> Cari(string teks, WilayahJenis? jenis = null, string? kodeInduk = null)
        {
            string cari = WilayahPencocokan.Normalisasi(teks);
            if (cari.Length == 0)
            {
                return Array.Empty<WilayahDaerah>();
            }

            IEnumerable<WilayahDaerah> sumber = kodeInduk == null
                ? _isi.Value.Semua
                : DaftarAnak(kodeInduk);

            return sumber
                .Where(w => (!jenis.HasValue || w.Jenis == jenis.Value) &&
                            WilayahPencocokan.Normalisasi(w.Nama).Contains(cari, StringComparison.Ordinal))
                .ToList();
        }

        // =================================================================
        // Pembacaan berkas
        // =================================================================

        private IsiWilayah Muat()
        {
            var perKode = new Dictionary<string, WilayahDaerah>(92000, StringComparer.Ordinal);
            var perNama = new Dictionary<(WilayahJenis, string), List<WilayahDaerah>>();
            var rusak = 0;
            var info = WilayahInfo.Kosong;

            try
            {
                if (!File.Exists(_jalur))
                {
                    _logger?.LogWarning("Berkas data wilayah tidak ditemukan: {Jalur}", _jalur);
                    return IsiWilayah.Gagal(
                        "Data wilayah bawaan aplikasi tidak ditemukan, jadi isian wilayah diketik manual.");
                }

                using var pembaca = BukaPembaca(_jalur);
                string? baris;
                while ((baris = pembaca.ReadLine()) != null)
                {
                    if (baris.Length == 0)
                    {
                        continue;
                    }

                    if (baris[0] == '#')
                    {
                        info = BacaKepala(baris, info);
                        continue;
                    }

                    var wilayah = BacaBaris(baris);
                    if (wilayah == null)
                    {
                        rusak++;
                        continue;
                    }

                    perKode[wilayah.Kode] = wilayah;

                    var kunci = (wilayah.Jenis, WilayahPencocokan.Normalisasi(wilayah.Nama));
                    if (!perNama.TryGetValue(kunci, out var daftar))
                    {
                        daftar = new List<WilayahDaerah>(1);
                        perNama[kunci] = daftar;
                    }

                    daftar.Add(wilayah);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Gagal membaca berkas data wilayah {Jalur}", _jalur);
                return IsiWilayah.Gagal(
                    "Data wilayah bawaan gagal dibaca, jadi isian wilayah diketik manual.");
            }

            if (perKode.Count == 0)
            {
                _logger?.LogWarning("Berkas data wilayah {Jalur} tidak berisi baris yang bisa dipakai.", _jalur);
                return IsiWilayah.Gagal(
                    "Data wilayah bawaan kosong, jadi isian wilayah diketik manual.");
            }

            if (rusak > 0)
            {
                _logger?.LogWarning("{Jumlah} baris data wilayah dilewati karena tidak berbentuk benar.", rusak);
            }

            // Wilayah tanpa induk membuatnya tidak mungkin dipilih pada jenjang mana pun,
            // jadi baris seperti itu dibuang lebih awal daripada mengotori daftar pilihan.
            var yatim = perKode.Values
                .Where(w => w.Induk.Length > 0 && !perKode.ContainsKey(w.Induk))
                .Select(w => w.Kode)
                .ToList();
            foreach (string kode in yatim)
            {
                perKode.Remove(kode);
            }

            if (yatim.Count > 0)
            {
                _logger?.LogWarning("{Jumlah} wilayah dilewati karena induknya tidak ada pada data wilayah.", yatim.Count);
            }

            var anak = new Dictionary<string, List<WilayahDaerah>>(StringComparer.Ordinal)
            {
                [string.Empty] = new List<WilayahDaerah>()
            };

            foreach (var wilayah in perKode.Values)
            {
                if (!anak.TryGetValue(wilayah.Induk, out var daftarAnak))
                {
                    daftarAnak = new List<WilayahDaerah>();
                    anak[wilayah.Induk] = daftarAnak;
                }

                daftarAnak.Add(wilayah);
            }

            var lengkap = perKode.Values.OrderBy(w => w.Kode, StringComparer.Ordinal).ToList();

            // Daftar anak dipakai sebagai sumber pilihan berjenjang, jadi urutannya
            // ditetapkan sekali di sini: menurut nama (mengabaikan besar-kecil huruf),
            // lalu kode sebagai pemutus supaya nama kembar tidak berpindah-pindah posisi.
            foreach (var daftarAnak in anak.Values)
            {
                daftarAnak.Sort((a, b) =>
                {
                    int banding = string.Compare(a.Nama, b.Nama, StringComparison.InvariantCultureIgnoreCase);
                    return banding != 0 ? banding : string.Compare(a.Kode, b.Kode, StringComparison.Ordinal);
                });
            }

            foreach (var daftarNama in perNama.Values)
            {
                daftarNama.Sort((a, b) => string.Compare(a.Kode, b.Kode, StringComparison.Ordinal));
            }

            if (info.JumlahBaris == 0)
            {
                info = info with { JumlahBaris = lengkap.Count };
            }

            _logger?.LogInformation(
                "Data wilayah dimuat: {Jumlah} wilayah dari {Jalur} (acuan {Acuan}).",
                lengkap.Count, _jalur, info.Acuan);

            return new IsiWilayah(true, string.Empty, info, perKode, anak, perNama, lengkap);
        }

        private static TextReader BukaPembaca(string jalur)
        {
            var berkas = File.OpenRead(jalur);

            bool terkompresi;
            using (var pengintip = new BinaryReader(berkas, Encoding.UTF8, leaveOpen: true))
            {
                terkompresi = berkas.Length >= 2 && pengintip.ReadByte() == 0x1F && pengintip.ReadByte() == 0x8B;
            }

            // Pembacaan pengintip memindahkan posisi berkas, jadi posisinya harus
            // dikembalikan ke awal sebelum isinya benar-benar dibaca.
            berkas.Position = 0;

            return terkompresi
                ? new StreamReader(new GZipStream(berkas, CompressionMode.Decompress), Encoding.UTF8)
                // Berkas TSV tanpa kompresi juga diterima supaya data bisa diperiksa manual.
                : new StreamReader(berkas, Encoding.UTF8);
        }

        private static WilayahDaerah? BacaBaris(string baris)
        {
            var kolom = baris.Split('\t');
            if (kolom.Length < 3)
            {
                return null;
            }

            string kode = kolom[0].Trim();
            string nama = kolom[1].Trim();
            if (kode.Length == 0 || nama.Length == 0)
            {
                return null;
            }

            var jenis = Jenis(kolom[2]);
            if (jenis == null)
            {
                return null;
            }

            int pisah = kode.LastIndexOf('.');
            string induk = pisah < 0 ? string.Empty : kode[..pisah];

            return new WilayahDaerah(
                kode,
                nama,
                jenis.Value,
                induk,
                kolom.Length >= 4 ? kolom[3].Trim() : string.Empty);
        }

        private static WilayahJenis? Jenis(string kode) => kode.Trim() switch
        {
            "p" => WilayahJenis.Provinsi,
            "b" => WilayahJenis.Kabupaten,
            "k" => WilayahJenis.Kota,
            "c" => WilayahJenis.Kecamatan,
            "d" => WilayahJenis.Desa,
            "l" => WilayahJenis.Kelurahan,
            _ => null
        };

        private static WilayahInfo BacaKepala(string baris, WilayahInfo sebelumnya)
        {
            int pisah = baris.IndexOf(':');
            if (pisah < 0)
            {
                return sebelumnya;
            }

            string kunci = baris[1..pisah].Trim();
            string nilai = baris[(pisah + 1)..].Trim();

            return kunci switch
            {
                "acuan" => sebelumnya with { Acuan = nilai },
                "sumber-diperbarui" => sebelumnya with { SumberDiperbarui = nilai },
                "jumlah" => sebelumnya with { JumlahBaris = AngkaAwal(nilai) },
                _ => sebelumnya
            };
        }

        private static int AngkaAwal(string teks)
        {
            int akhir = 0;
            while (akhir < teks.Length && char.IsDigit(teks[akhir]))
            {
                akhir++;
            }

            return akhir > 0 && int.TryParse(teks[..akhir], out int hasil) ? hasil : 0;
        }

        private sealed record IsiWilayah(
            bool Berhasil,
            string Masalah,
            WilayahInfo Info,
            Dictionary<string, WilayahDaerah> PerKode,
            Dictionary<string, List<WilayahDaerah>> Anak,
            Dictionary<(WilayahJenis, string), List<WilayahDaerah>> PerNama,
            List<WilayahDaerah> Semua)
        {
            public static IsiWilayah Gagal(string masalah) => new(
                false,
                masalah,
                WilayahInfo.Kosong,
                new Dictionary<string, WilayahDaerah>(StringComparer.Ordinal),
                new Dictionary<string, List<WilayahDaerah>>(StringComparer.Ordinal)
                {
                    [string.Empty] = new List<WilayahDaerah>()
                },
                new Dictionary<(WilayahJenis, string), List<WilayahDaerah>>(),
                new List<WilayahDaerah>());
        }
    }
}
