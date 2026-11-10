using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace SuDesApp.Data.Repositories
{
    public partial class WargaRepository : IWargaRepository
    {

        /// <summary>
        /// Satu query agregat untuk seluruh angka ringkasan kependudukan:
        /// total/status/jenis kelamin/KK/per RT. Angka "penduduk" hanya
        /// menghitung yang masih tinggal di desa (AKTIF + BARU); seluruh warga
        /// terdata tetap dilaporkan terpisah supaya laporan tidak menyesatkan.
        /// </summary>
        public async Task<WargaStatistikRingkasan> GetStatistikWargaAsync(CancellationToken cancellationToken = default)
        {
            const string cacheKey = "Warga_Statistik";
            var cached = await _cacheService.GetAsync<WargaStatistikRingkasan>(cacheKey, cancellationToken);
            if (cached != null) return cached;

            try
            {
                if (_connection.State != ConnectionState.Open)
                    await _connection.OpenAsync(cancellationToken);

                var hasil = new WargaStatistikRingkasan();

                var baris = await _connection.QueryAsync<(
                    string? Status, string? Jk, string? Rt, string? NoKk,
                    string? TanggalLahir, string? Pendidikan, string? Agama, string? StatusPerkawin,
                    string? StatusKeluarga)>(
                    @"SELECT COALESCE(NULLIF(StatusWarga,''), 'AKTIF'), JenisKelamin, RT, NoKK,
                             TanggalLahir, Pendidikan, Agama, StatusPerkawinan, StatusKeluarga
                      FROM Warga WHERE " + SyaratBukanDummy,
                    commandTimeout: 30);

                var perRt = new Dictionary<string, (int Jumlah, int L, int P)>(StringComparer.OrdinalIgnoreCase);
                var perPendidikan = new Dictionary<string, int>(StringComparer.Ordinal);
                var perAgama = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var perPerkawinan = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var perKelompokUsia = new Dictionary<string, int>(StringComparer.Ordinal);
                var kkUnik = new HashSet<string>(StringComparer.Ordinal);
                int tanpaKk = 0;

                // Kantong usia 5 tahunan gaya BPS; kantong "(tidak diketahui)"
                // SELALU ada supaya pembaca laporan tidak perlu null-check.
                string KantongUsia(string? tanggalLahir)
                {
                    if (!CobaParseTanggalLahir(tanggalLahir, out var lahir))
                    {
                        hasil.UsiaTidakDiketahui++;
                        return "(tidak diketahui)";
                    }

                    int usia = DateTime.Today.Year - lahir.Year;
                    if (lahir.Date > DateTime.Today.AddYears(-usia)) usia--;

                    // Tanggal lahir di masa depan (mis. salah ketik 2027 untuk
                    // 1927) tidak boleh jatuh ke kantong "0-4": orang berusia
                    // 99 tahun akan dilaporkan sebagai balita dan jumlah kantong
                    // 0-4 membengkak. Perlakukan sebagai tidak diketahui, sama
                    // seperti tanggal yang tidak bisa dibaca.
                    if (usia < 0)
                    {
                        hasil.UsiaTidakDiketahui++;
                        return "(tidak diketahui)";
                    }

                    return usia >= 65 ? "65+" : $"{usia / 5 * 5}-{usia / 5 * 5 + 4}";
                }

                // Nomor KK yang hanya dipegang SATU orang: dipakai sebagai
                // Kepala Keluarga bila operator belum mengisi kedudukan dalam
                // keluarga, sehingga KK berisi orang tua + anak tetap terhitung
                // 1 kepala keluarga, bukan 0.
                var kkSendiri = new HashSet<string>(
                    baris.Where(b => !string.IsNullOrWhiteSpace(b.NoKk))
                         .GroupBy(b => b.NoKk!.Trim(), StringComparer.Ordinal)
                         .Where(g => g.Count() == 1)
                         .Select(g => g.Key),
                    StringComparer.Ordinal);

                foreach (var (status, jk, rt, noKk, tanggalLahir, pendidikan, agama, perkawinan, statusKeluarga) in baris)
                {
                    var statusRapi = StatusWargaTipe.Normalisasi(status);
                    // Jenis kelamin menerima bentuk lengkap maupun singkat (L/P)
                    // karena data lama dan impor menyimpan keduanya.
                    bool laki = string.Equals(jk?.Trim(), "Laki-laki", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(jk?.Trim(), "L", StringComparison.OrdinalIgnoreCase);
                    bool perempuan = string.Equals(jk?.Trim(), "Perempuan", StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(jk?.Trim(), "P", StringComparison.OrdinalIgnoreCase);

                    hasil.TotalSeluruh++;
                    if (laki) hasil.LakiLaki++;
                    if (perempuan) hasil.Perempuan++;

                    switch (statusRapi)
                    {
                        case StatusWargaTipe.Aktif:
                            hasil.TotalAktif++;
                            break;
                        case StatusWargaTipe.Baru:
                            hasil.TotalBaru++;
                            break;
                        case StatusWargaTipe.Pindah:
                            hasil.TotalPindah++;
                            break;
                        case StatusWargaTipe.Meninggal:
                            hasil.TotalMeninggal++;
                            break;
                    }

                    // Penduduk = yang masih tinggal di desa (aktif/baru).
                    if (StatusWargaTipe.Menghitung.Contains(statusRapi, StringComparer.Ordinal))
                    {
                        if (laki) hasil.LakiLakiPenduduk++;
                        else if (perempuan) hasil.PerempuanPenduduk++;
                        else hasil.PendudukJenisKelaminTidakDiketahui++;
                    }

                    // Kepala Keluarga dihitung dari kedudukan dalam keluarga, bukan
                    // dari nomor KK — kolom StatusKeluarga selamanya tidak ikut
                    // SELECT sehingga angka ini selalu 0 di layar, Excel, dan PDF
                    // ("Jumlah Kepala Keluarga 0 — 100,0 %"). Bila operator belum
                    // mengisi kedudukan, pemilik tunggal satu no. KK diperlakukan
                    // sebagai Kepala Keluarga (aturan yang sudah tertulis di
                    // WargaKeluarga tetapi belum pernah dijalankan).
                    if (StatusKeluargaTipe.AdalahKepalaKeluarga(statusKeluarga)
                        || kkSendiri.Contains((noKk ?? string.Empty).Trim()))
                    {
                        hasil.JumlahKepalaKeluarga++;
                        if (laki) hasil.KepalaKeluargaLakiLaki++;
                        else if (perempuan) hasil.KepalaKeluargaPerempuan++;
                    }

                    // RT kosong tetap dihitung, pada kantong "(tidak diisi)" —
                    // pola yang sama dipakai kantong usia. Tanpa ini warga tanpa
                    // RT hilang dari tabel tetapi totalnya tetap ikut dihitung,
                    // sehingga baris "Jumlah" tidak pernah sama dengan jumlah
                    // baris tabelnya (mis. 8 baris, total 10) dan tidak ada
                    // penjelasan ke mana 2 orang itu pergi.
                    var kunciRt = string.IsNullOrWhiteSpace(rt) ? "(tidak diisi)" : rt!.Trim();
                    var (jmlRt, lRt, pRt) = perRt.TryGetValue(kunciRt, out var vRt) ? vRt : (0, 0, 0);
                    perRt[kunciRt] = (jmlRt + 1, lRt + (laki ? 1 : 0), pRt + (perempuan ? 1 : 0));

                    // KK dihitung unik dari kolom NoKK warga; baris tanpa NoKK
                    // dihitung sebagai "tanpa KK".
                    if (string.IsNullOrWhiteSpace(noKk))
                        tanpaKk++;
                    else
                        kkUnik.Add(noKk.Trim());

                    // Kelompok laporan: pendidikan ke jenjang BPS, agama & status
                    // perkawinan apa adanya, usia ke kantong 5 tahunan.
                    var jenjang = JenjangPendidikanBps.Tentukan(pendidikan);
                    perPendidikan[jenjang] = perPendidikan.GetValueOrDefault(jenjang) + 1;

                    if (!string.IsNullOrWhiteSpace(agama))
                    {
                        var kunciAgama = agama!.Trim();
                        perAgama[kunciAgama] = perAgama.GetValueOrDefault(kunciAgama) + 1;
                    }

                    if (!string.IsNullOrWhiteSpace(perkawinan))
                    {
                        var kunciKawin = perkawinan!.Trim();
                        perPerkawinan[kunciKawin] = perPerkawinan.GetValueOrDefault(kunciKawin) + 1;
                    }

                    var kantong = KantongUsia(tanggalLahir);
                    perKelompokUsia[kantong] = perKelompokUsia.GetValueOrDefault(kantong) + 1;
                }

                // Kantong "(tidak diketahui)" SELALU ada — walau kosong — supaya
                // pembaca laporan tidak perlu berurusan dengan baris hilang.
                perKelompokUsia.TryAdd("(tidak diketahui)", 0);

                foreach (var (rt, v) in perRt)
                {
                    hasil.PerRt = hasil.PerRt.Append(new WargaStatistikBaris
                    {
                        Kunci = rt,
                        Jumlah = v.Jumlah,
                        LakiLaki = v.L,
                        Perempuan = v.P
                    }).ToList();
                }

                hasil.PerPendidikan = UrutkanJenjang(perPendidikan);
                hasil.PerAgama = perAgama
                    .Select(kv => new WargaStatistikBaris { Kunci = kv.Key, Jumlah = kv.Value })
                    .OrderBy(b => b.Kunci, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                hasil.PerStatusPerkawinan = perPerkawinan
                    .Select(kv => new WargaStatistikBaris { Kunci = kv.Key, Jumlah = kv.Value })
                    .OrderBy(b => b.Kunci, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // Kantong usia diurutkan numerik; kantong tak diketahui selalu
                // diletakkan paling akhir agar tabel laporan terbaca berurut.
                hasil.PerKelompokUsia = perKelompokUsia
                    .Select(kv => new WargaStatistikBaris { Kunci = kv.Key, Jumlah = kv.Value })
                    .OrderBy(b => b.Kunci == "(tidak diketahui)" ? 1 : 0)
                    .ThenBy(b => int.TryParse((b.Kunci ?? "0").Split('-')[0], out var bawah) ? bawah : int.MaxValue)
                    .ToList();

                hasil.JumlahKartuKeluarga = kkUnik.Count;
                hasil.TanpaKartuKeluarga = tanpaKk;

                await _cacheService.SetAsync(cacheKey, hasil, new MemoryCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromMinutes(30),
                    AbsoluteExpirationRelativeToNow = _cacheExpiration
                }, cancellationToken);

                return hasil;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyusun statistik warga");
                throw new DataRetrievalException("Gagal menyusun statistik warga", ex);
            }
        }

        private static IReadOnlyList<WargaStatistikBaris> UrutkanJenjang(Dictionary<string, int> perJenjang)
        {
            var daftar = new List<WargaStatistikBaris>();
            foreach (var jenjang in JenjangPendidikanBps.Lista)
            {
                if (perJenjang.TryGetValue(jenjang, out var jumlah))
                {
                    daftar.Add(new WargaStatistikBaris { Kunci = jenjang, Jumlah = jumlah });
                }
            }

            // Jenjang di luar daftar baku tetap dilaporkan di akhir.
            foreach (var (jenjang, jumlah) in perJenjang)
            {
                if (!JenjangPendidikanBps.Lista.Contains(jenjang))
                {
                    daftar.Add(new WargaStatistikBaris { Kunci = jenjang, Jumlah = jumlah });
                }
            }

            return daftar;
        }

        /// <summary>Baca tanggal lahir dalam format umum (ISO, dd-MM-yyyy, titik, garis miring).</summary>
        private static bool CobaParseTanggalLahir(string? teks, out DateTime tanggal)
        {
            tanggal = default;
            if (string.IsNullOrWhiteSpace(teks)) return false;

            string[] format = { "yyyy-MM-dd", "dd-MM-yyyy", "yyyy/MM/dd", "dd/MM/yyyy", "dd.MM.yyyy", "yyyy-MM-dd HH:mm:ss" };
            return DateTime.TryParseExact(
                teks!.Trim(), format, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out tanggal);
        }

    }
}
