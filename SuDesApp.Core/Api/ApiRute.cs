using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SuDesApp.Api
{
    /// <summary>Hasil pencocokan permintaan terhadap tabel rute.</summary>
    public sealed class ApiCocok
    {
        internal ApiCocok(ApiEndpoint? endpoint, string? nilaiParameter, bool metodeTidakDiizinkan)
        {
            Endpoint = endpoint;
            NilaiParameter = nilaiParameter;
            MetodeTidakDiizinkan = metodeTidakDiizinkan;
        }

        /// <summary>Endpoint yang cocok, atau null bila tidak ada.</summary>
        public ApiEndpoint? Endpoint { get; }

        /// <summary>Nilai bagian jalur bertanda kurung, mis. kode permintaan.</summary>
        public string? NilaiParameter { get; }

        /// <summary>
        /// True bila jalur-nya dikenal tetapi metodenya berbeda. Pemanggil
        /// dibalas 405 (bukan 404) supaya integrator tahu endpoint-nya ada,
        /// cuma salah perintah.
        /// </summary>
        public bool MetodeTidakDiizinkan { get; }

        /// <summary>
        /// True bila permintaan boleh dijalankan: jalur dikenal dan metodenya
        /// cocok. Method salah tetap menyimpan <see cref="Endpoint"/> (supaya
        /// pemanggil bisa isi header Allow) tetapi nilai ini false, supaya
        /// pemanggil tidak keliru menjalankan handler.
        /// </summary>
        public bool Ditemukan => Endpoint != null && !MetodeTidakDiizinkan;

        internal static ApiCocok TidakDitemukan() => new(null, null, false);

        internal static ApiCocok SalahMetode(ApiEndpoint endpoint) => new(endpoint, null, true);

        internal static ApiCocok Cocokkan(ApiEndpoint endpoint, string? nilaiParameter)
            => new(endpoint, nilaiParameter, false);
    }

    /// <summary>
    /// Tabel rute API: satu-satunya tempat endpoint didaftarkan, sehingga
    /// dokumentasi di halaman API dan listener tidak mungkin berbeda.
    ///
    /// Prefix memakai versi ("/sudes/api/v1") supaya kalau nanti ada perubahan
    /// yang merusak, jalur versi baru bisa berdampingan dengan yang lama.
    /// </summary>
    public static class ApiRute
    {
        public const string Prefix = "/sudes/api/v1";

        /// <summary>Semua endpoint yang tersedia, sesuai urutan dokumentasi.</summary>
        public static IReadOnlyList<ApiEndpoint> Semua { get; } = new[]
        {
            new ApiEndpoint
            {
                Metode = "GET",
                Jalur = "/status",
                Nama = "Status layanan",
                Keterangan = "Cek versi API dan jumlah antrean yang menunggu operator. Endpoint paling ringan untuk uji koneksi.",
                Kategori = ApiKategori.Agregat
            },
            new ApiEndpoint
            {
                Metode = "GET",
                Jalur = "/statistik",
                Nama = "Statistik desa",
                Keterangan = "Angka agregat: jumlah warga, kartu keluarga, perangkat desa, surat per status, dan permintaan per status. Tidak memuat NIK, alamat, atau nomor HP warga.",
                Kategori = ApiKategori.Agregat
            },
            new ApiEndpoint
            {
                Metode = "GET",
                Jalur = "/perangkat-desa",
                Nama = "Daftar perangkat desa",
                Keterangan = "Nama jabatan, nama pejabat, wilayah, dan masa jabatan. Berguna untuk menyelaraskan kop surat di sistem luar.",
                Kategori = ApiKategori.Perangkat
            },
            new ApiEndpoint
            {
                Metode = "GET",
                Jalur = "/jenis-surat",
                Nama = "Jenis surat online",
                Keterangan = "Daftar jenis surat yang bisa diminta lewat API, supaya sistem luar tidak perlu menebak nama jenis.",
                Kategori = ApiKategori.Agregat
            },
            new ApiEndpoint
            {
                Metode = "POST",
                Jalur = "/permintaan",
                Nama = "Kirim permintaan surat",
                Keterangan = "Menerima permintaan dari luar dan menaruhnya di antrean operator (menu Layanan Online) dengan status BARU. Isi field referensi agar pengiriman ulang tidak tercatat ganda.",
                Kategori = ApiKategori.Permintaan
            },
            new ApiEndpoint
            {
                Metode = "GET",
                Jalur = "/permintaan/{kode}",
                Nama = "Status permintaan",
                Keterangan = "Status satu permintaan berdasarkan kode (mis. PMT-2026-0001): BARU, DIPROSES, SELESAI, DITOLAK, atau PERLU_PERBAIKAN.",
                Kategori = ApiKategori.Permintaan
            },
            new ApiEndpoint
            {
                Metode = "GET",
                Jalur = "/verifikasi/{kode}",
                Nama = "Verifikasi surat",
                Keterangan = "Periksa keaslian surat dari kode yang tercetak di kaki surat (mis. SD-7K3M-9QX2). Satu-satunya endpoint yang tidak memerlukan kunci API — dipakai bank/instansi yang memegang surat, dan jawabannya hanya memuat data yang sudah tercetak di surat.",
                Kategori = ApiKategori.Verifikasi,
                TanpaKunci = true
            },
            new ApiEndpoint
            {
                Metode = "GET",
                Jalur = "/rekap-bulanan",
                Nama = "Rekap laporan bulanan",
                Keterangan = "Rekap surat per bulan untuk satu tahun (parameter ?tahun=YYYY, bawaan tahun berjalan): jumlah surat dan rinciannya per status persetujuan (DIAJUKAN, DIVERIFIKASI, TERBIT, DITOLAK, TANPA_ALUR). Disediakan 12 baris Januari–Desember walau nol, supaya dashboard sistem luar bisa langsung membaca tren setahun penuh.",
                Kategori = ApiKategori.Agregat
            }
        };

        private sealed class Pola
        {
            public ApiEndpoint Endpoint { get; init; } = null!;
            public Regex Regex { get; init; } = null!;
            public bool AdaParameter { get; init; }
        }

        /// <summary>Tanda kurung kurawal pada jalur yang diganti kelompok tangkap.</summary>
        private const string PlaceholderParameter = "{kode}";

        /// <summary>Nama kelompok tangkap untuk bagian parameter jalur.</summary>
        private const string NamaKelompok = "kode";

        private static readonly List<Pola> TabelPola = SusunPola();

        private static List<Pola> SusunPola()
        {
            var hasil = new List<Pola>();
            foreach (var endpoint in Semua)
            {
                var jalur = Prefix + endpoint.Jalur;
                var regex = SusunRegex(jalur, out var adaParameter);
                hasil.Add(new Pola
                {
                    Endpoint = endpoint,
                    Regex = regex,
                    AdaParameter = adaParameter
                });
            }
            return hasil;
        }

        private static Regex SusunRegex(string jalur, out bool adaParameter)
        {
            // Placeholder dipecah lebih dulu, bukan lewat Regex.Escape: "}" tidak
            // di-escape oleh Regex.Escape sehingga pola "{kode}" akan menghasilkan
            // kelompok tangkap yang tidak berpasangan.
            var potong = jalur.Split(
                new[] { PlaceholderParameter }, 2, StringSplitOptions.None);
            adaParameter = potong.Length > 1;

            // Parameter dibatasi satu segmen jalur supaya "/permintaan/a/b" tidak
            // ikut cocok ke "/permintaan/{kode}".
            var pola = "^" + Regex.Escape(potong[0])
                + (adaParameter
                    ? "(?<" + NamaKelompok + ">[^/]+)" + Regex.Escape(potong[1])
                    : string.Empty)
                + "$";

            return new Regex(pola, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        /// <summary>
        /// Cocokkan metode dan jalur penuh. Jalur dinormalisasi lebih dulu: garis
        /// miring akhir diabaikan dan huruf besar-kecil tidak berpengaruh.
        /// </summary>
        public static ApiCocok Cocok(string metode, string jalurPenuh)
        {
            if (string.IsNullOrWhiteSpace(jalurPenuh)) return ApiCocok.TidakDitemukan();

            var jalur = NormalisasiJalur(jalurPenuh);
            ApiEndpoint? jalurDikenalDenganMetodeLain = null;

            foreach (var pola in TabelPola)
            {
                var cocok = pola.Regex.Match(jalur);
                if (!cocok.Success) continue;

                if (string.Equals(pola.Endpoint.Metode, metode, StringComparison.OrdinalIgnoreCase))
                {
                    var nilai = pola.AdaParameter
                        ? Uri.UnescapeDataString(cocok.Groups[NamaKelompok].Value)
                        : null;
                    return ApiCocok.Cocokkan(pola.Endpoint, nilai);
                }

                // Jalur dikenali, tetapi metodenya belum cocok.
                jalurDikenalDenganMetodeLain ??= pola.Endpoint;
            }

            return jalurDikenalDenganMetodeLain != null
                ? ApiCocok.SalahMetode(jalurDikenalDenganMetodeLain)
                : ApiCocok.TidakDitemukan();
        }

        /// <summary>Buang garis miring akhir dan pastikan diawali satu garis miring.</summary>
        public static string NormalisasiJalur(string? jalur)
        {
            var hasil = (jalur ?? string.Empty).Trim().TrimEnd('/');
            if (hasil.Length == 0) return "/";
            return hasil[0] == '/' ? hasil : "/" + hasil;
        }
    }
}
