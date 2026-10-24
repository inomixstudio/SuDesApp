using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SuDesApp.Services
{
    /// <summary>Data APBDes satu desa untuk satu tahun seperti yang dipublikasikan
    /// pemerintah. Angka setiap metrik boleh hilang (null) bila publikasinya tidak
    /// memuatnya — tidak pernah dibuat nol supaya angka tidak salah tampil.</summary>
    public sealed record HasilApbdesProdeskel(
        int Tahun,
        long? AngPendapatan, long? RealPendapatan,
        long? AngBelanja, long? RealBelanja,
        long? AngPembiayaan, long? RealPembiayaan,
        long? DanaDesa)
    {
        /// <summary>Jumlah metrik (pendapatan/belanja/pembiayaan/dana desa) yang benar-benar berangka.</summary>
        public int MetrikTerisi => new[]
        {
            AngPendapatan.HasValue || RealPendapatan.HasValue,
            AngBelanja.HasValue || RealBelanja.HasValue,
            AngPembiayaan.HasValue || RealPembiayaan.HasValue,
            DanaDesa.HasValue,
        }.Count(b => b);

        public bool PunyaAngka => MetrikTerisi > 0;
    }

    public enum StatusMuatApbdesPusat
    {
        /// <summary>Publikasi terbaca dan angkanya dikenali.</summary>
        Berhasil,

        /// <summary>Situs menjawab, tetapi tidak ada publikasi untuk desa/tahun itu.</summary>
        TidakDitemukan,

        /// <summary>Halaman terbaca tetapi bentuk tabel anggarannya belum dikenali —
        /// tidak ada angka yang boleh dipakai darinya, supaya halaman tidak pernah
        /// menampilkan anggaran yang salah.</summary>
        FormatTidakDikenal,

        /// <summary>Situs pusat tidak terjangkau atau menjawab galat.</summary>
        Gagal,
    }

    /// <summary>Jawaban lengkap satu permintaan, termasuk alasannya bila tidak berhasil.</summary>
    public sealed record MuatanApbdesPusat(StatusMuatApbdesPusat Status, HasilApbdesProdeskel? Hasil, string Pesan)
    {
        public static MuatanApbdesPusat Galat(string pesan) => new(StatusMuatApbdesPusat.Gagal, null, pesan);
        public static MuatanApbdesPusat Kosong(string pesan) => new(StatusMuatApbdesPusat.TidakDitemukan, null, pesan);
        public static MuatanApbdesPusat FormatTidakDikenal(string pesan) => new(StatusMuatApbdesPusat.FormatTidakDikenal, null, pesan);
    }

    public interface IKlienApbdesPusat
    {
        Task<MuatanApbdesPusat> MuatAsync(string kodeDesa, int tahun, CancellationToken ct = default);
    }

    /// <summary>
    /// Klien data APBDes yang sudah dipublikasikan pemerintah pusat — sumber yang
    /// sama dengan yang dipakai publikasi Prodeskel Kemendagri:
    /// <c>prodeskel.binapemdes.kemendagri.go.id</c>, diakses dengan kode desa
    /// Kemendagri (10 digit) per tahun anggaran, tanpa kredensial.
    ///
    /// Aplikasi tidak menghitung ulang keuangan desa: halaman APBDES hanya
    /// menampilkan anggaran &amp; realisasi yang dipublikasikan pusat, sehingga
    /// angkanya selalu sama dengan yang dilihat publik.
    ///
    /// PENGURAIAN YANG KETAT: bentuk halaman publikasi pusat bisa berubah, jadi
    /// teksnya diurai <see cref="ApbdesProdeskelBaca"/> yang hanya menerima baris
    /// label yang dikenali (Pendapatan/Belanja/Pembiayaan/Dana Desa). Bila
    /// formatnya tidak dikenali, hasilnya status
    /// <see cref="StatusMuatApbdesPusat.FormatTidakDikenal"/> — halaman menampilkan
    /// pesan, bukan angka tebakan. Parser dipisah supaya bisa diuji dengan contoh
    /// respons tanpa internet.
    /// </summary>
    public sealed class ApbdesProdeskelKlien : IKlienApbdesPusat
    {
        public const string Host = "prodeskel.binapemdes.kemendagri.go.id";

        /// <summary>Situs pusat yang dibuka tombol "Buka situs pusat".</summary>
        public const string AlamatSitus = "https://" + Host + "/";

        /// <summary>
        /// Halaman laporan publik Prodeskel per desa per tahun — pola tautan publik
        /// yang sama dengan yang dirujuk dokumen profil desa resmi:
        /// <c>.../laporan-terkini-tingkat/laporan-terkini-tingkat.php?tahun=…&amp;kodesa=…</c>
        /// </summary>
        private const string AkarLaporan =
            "https://" + Host + "/laporan-terkini-tingkat/laporan-terkini-tingkat.php?tahun=";

        private readonly HttpClient _http;
        private readonly ILogger<ApbdesProdeskelKlien> _logger;

        public ApbdesProdeskelKlien(HttpClient? http = null, ILogger<ApbdesProdeskelKlien>? logger = null)
        {
            _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
            _logger = logger ?? NullLogger<ApbdesProdeskelKlien>.Instance;
        }

        public async Task<MuatanApbdesPusat> MuatAsync(string kodeDesa, int tahun, CancellationToken ct = default)
        {
            string kode = new((kodeDesa ?? string.Empty).Where(char.IsDigit).ToArray());
            if (kode.Length != 10)
            {
                return MuatanApbdesPusat.Galat(
                    "Kode desa belum lengkap. Pilih desa/kelurahan pada Pengaturan Surat lebih dulu.");
            }

            try
            {
                var isi = await BacaAsync(AkarLaporan + tahun + "&kodesa=" + kode, ct).ConfigureAwait(false);
                return ApbdesProdeskelBaca.Urai(isi, tahun);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException)
            {
                return MuatanApbdesPusat.Galat("Situs pusat tidak menjawab (waktu habis). Periksa sambungan internet.");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogInformation("Situs publikasi pusat tidak terjangkau: {Pesan}", ex.Message);
                return MuatanApbdesPusat.Galat("Situs pusat tidak terjangkau. Periksa sambungan internet lalu muat ulang.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal membaca publikasi APBDes desa {Kode} tahun {Tahun}", kode, tahun);
                return MuatanApbdesPusat.Galat("Gagal membaca publikasi pusat: " + ex.Message);
            }
        }

        private async Task<string> BacaAsync(string alamat, CancellationToken ct)
        {
            using var permintaan = new HttpRequestMessage(HttpMethod.Get, alamat);
            permintaan.Headers.Accept.ParseAdd("text/html,application/json");
            // Situs pusat menolak permintaan tanpa agen pengguna yang jelas.
            permintaan.Headers.UserAgent.ParseAdd("SuDesApp/1.0 (aplikasi kantor desa)");

            using var jawaban = await _http.SendAsync(permintaan, ct).ConfigureAwait(false);
            var isi = await jawaban.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!jawaban.IsSuccessStatusCode && string.IsNullOrWhiteSpace(isi))
            {
                throw new HttpRequestException(
                    $"Situs pusat menjawab {(int)jawaban.StatusCode} tanpa isi.");
            }

            return isi;
        }
    }

    /// <summary>
    /// Penguraian halaman publikasi APBDes pusat → angka metrik standar.
    /// DIPISAH dari klien jaringan supaya bisa diuji tanpa internet, dan TIDAK
    /// PERNAH melempar: sembarang teks menghasilkan status + pesan, bukan angka
    /// karangan. Satu field yang salah baca berarti anggaran desa yang salah di
    /// layar, jadi pendekatannya menolak, bukan menebak.
    /// </summary>
    public static class ApbdesProdeskelBaca
    {
        /// <summary>Baris yang labelnya nyaris alias tetapi sebenarnya rincian,
        /// bukan jumlah total — jangan dipakai sebagai angka ringkasan.</summary>
        private static readonly string[] PenandaRincian =
        {
            "transfer",
            "bagi",      // pendapatan bagi hasil
            "hibah",
            "bantuan",
            "pajak",
            "retribusi",
            "eceran",
            "rincian",
            "pendapatan asli",
            "penerimaan",  // pembiayaan: penerimaan dari dsb.
            "pengeluaran", // pembiayaan: pengeluaran untuk dsb.
        };

        // Urutan alias berpengaruh: alias yang lebih spesifik dipasangkan lebih dulu,
        // dan masing-masing slot punya penanda penolak tambahan (item belanja dsb.).
        private static readonly (string Slot, string[] Alias, string[] PenandaTambahan)[] Peta =
        {
            ("danaDesa", new[] { "dana desa", "alokasi dana desa" }, Array.Empty<string>()),
            ("pembiayaan", new[] { "pembiayaan neto", "pembiayaan desa", "pembiayaan" }, new[] { "dalam negeri", "luar negeri" }),
            ("pendapatan", new[] { "pendapatan desa", "total pendapatan", "jumlah pendapatan", "pendapatan" }, new[] { "asli" }),
            ("belanja", new[] { "belanja desa", "total belanja", "jumlah belanja", "belanja" }, new[] { "belanja pegawai", "belanja barang", "belanja modal", "belanja langsung", "tidak langsung", "operasi" }),
        };

        private static readonly Regex RegexTag = new(@"<[^>]+>", RegexOptions.Compiled);

        /// <summary>
        /// Nilai rupiah: berformat titik-ribuan (1.234.567.890) atau polos
        /// minimal 6 digit, boleh diawali "Rp" dan diapit tanda kurung
        /// (negatif, khas pembiayaan). Angka 4–5 digit (tahun, kode, hitungan)
        /// tidak dianggap nilai rupiah.
        /// </summary>
        private static readonly Regex RegexAngka = new(
            @"(?<!\d)\(?\s*(?:Rp\.?\s*)?(\d{1,3}(?:\.\d{3})+|\d{6,})(,\d{1,2})?\s*\)?",
            RegexOptions.Compiled);

        /// <summary>Urai isi halaman publikasi; tidak pernah melempar.</summary>
        public static MuatanApbdesPusat Urai(string? isi, int tahunDiminta)
        {
            if (string.IsNullOrWhiteSpace(isi))
            {
                return MuatanApbdesPusat.Galat("Situs pusat menjawab kosong.");
            }

            var kandidat = KandidatBaris(isi);

            if (kandidat.Count == 0)
            {
                // Penanda halaman wajar tanpa anggaran: minta login, atau dijawab
                // "tidak ditemukan" — publikasi desa ini memang belum terbit.
                var rendah = isi.ToLowerInvariant();
                if (rendah.Contains("tidak ditemukan") || rendah.Contains("not found"))
                {
                    return MuatanApbdesPusat.Kosong(
                        "Publikasi APBDes " + tahunDiminta + " tidak ditemukan pada situs pusat.");
                }
                if (rendah.Contains("kode registrasi") || rendah.Contains("login"))
                {
                    return MuatanApbdesPusat.FormatTidakDikenal(
                        "Halaman publikasi pusat menuntut masuk (login), jadi isinya tidak bisa dibaca otomatis.");
                }
                return MuatanApbdesPusat.FormatTidakDikenal(
                    "Halaman publikasi pusat terbaca tetapi tidak memuat tabel anggaran yang dikenali — "
                    + "tidak ada angka yang diambil darinya demi keamanan angka.");
            }

            var slots = new Dictionary<string, (long? Ang, long? Real)>();
            foreach (var (slot, alias, tambahan) in Peta)
            {
                // Baris ringkasan resmi: prefer label berisi total/jumlah (skor 0),
                // lalu label paling pendek (paling dekat alias murni).
                var kandidatSlot = kandidat
                    .Where(k => alias.Any(a => k.Label.Contains(a, StringComparison.Ordinal)))
                    .Where(k => !PenandaRincian.Any(p => k.Label.Contains(p, StringComparison.Ordinal))
                                && !tambahan.Any(p => k.Label.Contains(p, StringComparison.Ordinal)))
                    .OrderBy(k => (k.Label.Contains("total") || k.Label.Contains("jumlah") ? 0 : 100)
                                  + k.Label.Length)
                    .ToList();

                if (kandidatSlot.Count > 0 && kandidatSlot[0].Angka.Length > 0)
                {
                    var angka = kandidatSlot[0].Angka;
                    var labelTerpilih = kandidatSlot[0].Label;
                    if (slot == "danaDesa")
                    {
                        slots[slot] = (angka[0], null);
                    }
                    else
                    {
                        // Baris bergaris "Realisasi …" memuat nilai realisasi saja.
                        var realisasiMurni = labelTerpilih.Contains("realisasi")
                            && !labelTerpilih.Contains("anggaran");
                        slots[slot] = realisasiMurni
                            ? (null, angka[0])
                            : (angka[0], angka.Length > 1 ? angka[1] : null);
                    }
                }
            }

            slots.TryGetValue("pendapatan", out var pd);
            slots.TryGetValue("belanja", out var bl);
            slots.TryGetValue("pembiayaan", out var pm);
            slots.TryGetValue("danaDesa", out var dd);

            if ((pd.Ang == null && pd.Real == null) || (bl.Ang == null && bl.Real == null))
            {
                return MuatanApbdesPusat.FormatTidakDikenal(
                    "Halaman terbaca tetapi ringkasan pendapatan/belanja tidak dikenali — "
                    + "angka tidak diambil darinya supaya tidak ada anggaran yang salah tampil. "
                    + "Sebenarnya terbaca: "
                    + string.Join(", ", slots.Where(s => s.Value.Ang != null || s.Value.Real != null)
                        .Select(s => s.Key))
                    + ".");
            }

            return new MuatanApbdesPusat(
                StatusMuatApbdesPusat.Berhasil,
                new HasilApbdesProdeskel(
                    tahunDiminta,
                    pd.Ang, pd.Real,
                    bl.Ang, bl.Real,
                    pm.Ang, pm.Real,
                    dd.Ang),
                "Publikasi terbaca.");
        }

        /// <summary>Baris yang berangka: (label sebelum angka pertama, daftar nilai rupiah).</summary>
        internal static List<(string Label, long[] Angka)> KandidatBaris(string isi)
        {
            var hasil = new List<(string, long[])>();
            foreach (var baris in BarisTeks(isi))
            {
                var angka = AmbilAngka(baris);
                if (angka.Count == 0) continue;

                int potong = RegexAngka.Match(baris).Index;
                var label = Regex.Replace(baris[..potong], @"\s+", " ").Trim().ToLowerInvariant();
                if (label.Length == 0) continue;

                hasil.Add((label, angka.ToArray()));
            }
            return hasil;
        }

        internal static IEnumerable<string> BarisTeks(string isi)
        {
            // Entitas dasar, lalu tag HTML: tag pindah baris menjadi \n, tag lain
            // dihapus. Isi tabel jadi baris teks yang hubungan label→angka tetap sebaris
            // seperti yang dilihat publik.
            var teks = isi
                .Replace("&nbsp;", " ")
                .Replace("&amp;", "&")
                .Replace("&lt;", "<")
                .Replace("&gt;", ">")
                .Replace("<br", "\n<br")
                .Replace("<tr", "\n<tr")
                .Replace("</tr", "\n</tr")
                .Replace("</p", "\n</p");
            var tanpaTag = RegexTag.Replace(teks, string.Empty);
            return tanpaTag
                .Split('\n')
                .Select(b => Regex.Replace(b, @"\s+", " ").Trim())
                .Where(b => b.Length > 0);
        }

        internal static List<long> AmbilAngka(string baris)
        {
            var hasil = new List<long>();
            foreach (Match m in RegexAngka.Matches(baris))
            {
                if (hasil.Count >= 4) break;
                if (!long.TryParse(
                        m.Groups[1].Value.Replace(".", string.Empty),
                        NumberStyles.Integer, CultureInfo.InvariantCulture, out var nilai))
                {
                    continue;
                }
                if (nilai < 999_999)
                {
                    continue; // angka kecil (tahun? kode? hitungan) bukan nilai rupiah
                }
                bool negatif = m.Value.TrimStart().StartsWith('(') && m.Value.Contains(')');
                hasil.Add(negatif ? -nilai : nilai);
            }
            return hasil;
        }
    }
}
