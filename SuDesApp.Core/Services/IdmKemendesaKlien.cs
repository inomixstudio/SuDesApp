using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SuDesApp.Services
{
    /// <summary>Hasil satu permintaan data IDM ke situs pusat.</summary>
    public enum StatusMuatIdm
    {
        /// <summary>Data desa untuk tahun itu diterima dan bisa ditampilkan.</summary>
        Berhasil = 0,

        /// <summary>Situs menjawab, tetapi desa/tahun itu tidak ada pada data IDM pusat.</summary>
        TidakDitemukan = 1,

        /// <summary>Situs tidak terjangkau, kode desa tidak sah, atau jawabannya tidak dikenali.</summary>
        Gagal = 2
    }

    /// <summary>Identitas desa sebagaimana dikembalikan situs pusat (bukan dari isian lokal).</summary>
    public sealed record IdentitasIdm(
        string Provinsi,
        string Kabupaten,
        string Kecamatan,
        string Desa,
        string KodeDesa);

    /// <summary>
    /// Satu baris tabel rumusan IDM. Situs pusat mengirim baris indikator dan
    /// baris subtotal (IKS/IKE/IKL/IDM/STATUS) dalam satu urutan; baris subtotal
    /// ditandai <paramref name="Ringkasan"/>.
    /// </summary>
    /// <param name="Nomor">Nomor indikator; null pada baris subtotal.</param>
    /// <param name="Bidang">"IKS", "IKE", "IKL", atau kosong bila tidak terbaca.</param>
    /// <param name="Indikator">Nama indikator persis seperti dari pusat.</param>
    /// <param name="SkorTampil">Skor apa adanya (indikator 0–5; subtotal berupa angka/teks).</param>
    /// <param name="Pihak">Pihak yang berperan (pusat/provinsi/kabupaten/desa/CSR/lainnya).</param>
    public sealed record BarisIdm(
        int? Nomor,
        string Bidang,
        string Indikator,
        string SkorTampil,
        string? Keterangan,
        string? Kegiatan,
        string? Pihak,
        bool Ringkasan);

    /// <summary>
    /// Ringkasan penilaian satu tahun. Nilai IKS/IKE/IKL diambil dari baris
    /// subtotal resmi, bukan dihitung ulang di aplikasi — angka yang tampil
    /// harus sama dengan yang tampil di situs pusat.
    /// </summary>
    public sealed record RingkasanIdm(
        int Tahun,
        double Skor,
        string Status,
        string TargetStatus,
        double? SkorMinimal,
        double? Penambahan,
        double? Iks,
        double? Ike,
        double? Ikl);

    /// <summary>Data IDM satu desa untuk satu tahun.</summary>
    public sealed record HasilIdm(
        RingkasanIdm Ringkasan,
        IdentitasIdm? Identitas,
        IReadOnlyList<BarisIdm> Baris);

    /// <summary>Jawaban lengkap satu permintaan, termasuk alasan bila tidak berhasil.</summary>
    /// <param name="LewatHttp">True bila data terpaksa diambil tanpa enkripsi (sertifikat situs pusat bermasalah).</param>
    public sealed record MuatanIdm(StatusMuatIdm Status, HasilIdm? Hasil, string Pesan, bool LewatHttp = false)
    {
        public static MuatanIdm Galat(string pesan) => new(StatusMuatIdm.Gagal, null, pesan);

        public static MuatanIdm Kosong(string pesan) => new(StatusMuatIdm.TidakDitemukan, null, pesan);
    }

    /// <summary>
    /// Pembacaan jawaban <c>GET /open/api/desa/rumusan/{kode_desa}/{tahun}</c>.
    ///
    /// Dipisah dari klien jaringan supaya bisa diuji dengan contoh jawaban
    /// sungguhan tanpa internet: satu field yang salah baca berarti skor/status
    /// desa yang salah pula di layar.
    /// </summary>
    public static class IdmKemendesaBaca
    {
        private static readonly CultureInfo Budaya = new("id-ID");

        /// <summary>Urai jawaban situs pusat; tidak pernah melempar.</summary>
        public static MuatanIdm Urai(string? json, int tahunDiminta)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return MuatanIdm.Galat("Situs pusat menjawab kosong.");
            }

            JsonDocument dokumen;
            try
            {
                dokumen = JsonDocument.Parse(json);
            }
            catch (JsonException)
            {
                return MuatanIdm.Galat("Jawaban situs pusat tidak dikenali (bukan JSON).");
            }

            using (dokumen)
            {
                var akar = dokumen.RootElement;
                if (akar.ValueKind != JsonValueKind.Object)
                {
                    return MuatanIdm.Galat("Jawaban situs pusat tidak dikenali.");
                }

                string pesan = Teks(akar, "message") ?? "";
                bool galat = akar.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.True;

                if (galat || !akar.TryGetProperty("mapData", out var mapData) ||
                    mapData.ValueKind != JsonValueKind.Object)
                {
                    return MuatanIdm.Kosong(string.IsNullOrWhiteSpace(pesan)
                        ? "Data IDM desa ini tidak ada pada data pusat."
                        : pesan);
                }

                var baris = BacaBaris(mapData);
                var identitas = BacaIdentitas(mapData);
                var ringkasan = BacaRingkasan(mapData, baris, tahunDiminta);

                if (ringkasan == null)
                {
                    return MuatanIdm.Galat("Jawaban situs pusat tidak memuat skor IDM.");
                }

                return new MuatanIdm(
                    StatusMuatIdm.Berhasil,
                    new HasilIdm(ringkasan, identitas, baris),
                    "");
            }
        }

        private static RingkasanIdm? BacaRingkasan(
            JsonElement mapData, IReadOnlyList<BarisIdm> baris, int tahunDiminta)
        {
            double? skor = null;
            string status = "";
            string target = "";
            double? minimal = null;
            double? penambahan = null;
            int tahun = tahunDiminta;

            if (mapData.TryGetProperty("SUMMARIES", out var ringkas) &&
                ringkas.ValueKind == JsonValueKind.Object)
            {
                skor = Angka(ringkas, "SKOR_SAAT_INI");
                status = Teks(ringkas, "STATUS") ?? "";
                target = Teks(ringkas, "TARGET_STATUS") ?? "";
                minimal = Angka(ringkas, "SKOR_MINIMAL");
                penambahan = Angka(ringkas, "PENAMBAHAN");
                var t = Angka(ringkas, "TAHUN");
                if (t.HasValue && t.Value > 0)
                {
                    tahun = (int)Math.Round(t.Value);
                }
            }

            // Baris subtotal membawa angka yang sama (dan IKS/IKE/IKL yang tidak
            // ada di SUMMARIES). Dipakai sebagai cadangan dan pelengkap.
            double? iks = null, ike = null, ikl = null;
            foreach (var b in baris.Where(b => b.Ringkasan))
            {
                var nama = b.Indikator.ToUpperInvariant();
                var nilai = AngkaDari(b.SkorTampil);
                if (nama.StartsWith("IKS", StringComparison.Ordinal)) iks = nilai;
                else if (nama.StartsWith("IKE", StringComparison.Ordinal)) ike = nilai;
                else if (nama.StartsWith("IKL", StringComparison.Ordinal)) ikl = nilai;
                else if (nama.StartsWith("STATUS", StringComparison.Ordinal))
                {
                    if (status.Length == 0) status = b.SkorTampil;
                }
                else if (nama.StartsWith("IDM", StringComparison.Ordinal))
                {
                    skor ??= nilai;
                }
            }

            skor ??= iks.HasValue && ike.HasValue && ikl.HasValue
                ? Math.Round((iks.Value + ike.Value + ikl.Value) / 3.0, 4)
                : null;

            return skor == null
                ? null
                : new RingkasanIdm(tahun, skor.Value, status, target, minimal, penambahan, iks, ike, ikl);
        }

        private static IdentitasIdm? BacaIdentitas(JsonElement mapData)
        {
            if (!mapData.TryGetProperty("IDENTITAS", out var id) ||
                id.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var pertama = id.EnumerateArray().FirstOrDefault();
            if (pertama.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return new IdentitasIdm(
                Teks(pertama, "nama_provinsi") ?? "",
                Teks(pertama, "nama_kab_kota") ?? "",
                Teks(pertama, "nama_kecamatan") ?? "",
                Teks(pertama, "nama_desa") ?? "",
                Teks(pertama, "id_desa") ?? "");
        }

        /// <summary>
        /// Baca baris tabel sekaligus menandai bidangnya. Situs pusat menutup
        /// setiap bidang dengan baris subtotal ("IKS 2024", "IKE 2024", …),
        /// jadi bidang sebuah indikator diketahui dari subtotal sesudahnya.
        /// </summary>
        private static List<BarisIdm> BacaBaris(JsonElement mapData)
        {
            var hasil = new List<BarisIdm>();
            if (!mapData.TryGetProperty("ROW", out var row) || row.ValueKind != JsonValueKind.Array)
            {
                return hasil;
            }

            // Rentang baris indikator yang belum punya bidang.
            int awalBidang = 0;

            foreach (var r in row.EnumerateArray())
            {
                if (r.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var indikator = (Teks(r, "INDIKATOR") ?? "").Trim();
                if (indikator.Length == 0)
                {
                    continue;
                }

                int? nomor = r.TryGetProperty("NO", out var n) && n.ValueKind == JsonValueKind.Number &&
                             n.TryGetInt32(out var nomorBaris)
                    ? nomorBaris
                    : null;

                string skorMentah = Teks(r, "SKOR") ?? "";
                string skorTampil = AngkaDari(skorMentah).HasValue
                    ? AngkaDari(skorMentah)!.Value.ToString("0.####", Budaya)
                    : skorMentah;

                var baris = new BarisIdm(
                    nomor,
                    "",
                    indikator,
                    skorTampil,
                    Teks(r, "KETERANGAN"),
                    Teks(r, "KEGIATAN"),
                    GabungPihak(r),
                    nomor == null);

                if (baris.Ringkasan)
                {
                    string bidang = BidangDariNama(indikator);
                    if (bidang.Length > 0)
                    {
                        for (int i = awalBidang; i < hasil.Count; i++)
                        {
                            if (!hasil[i].Ringkasan && hasil[i].Bidang.Length == 0)
                            {
                                hasil[i] = hasil[i] with { Bidang = bidang };
                            }
                        }
                    }

                    hasil.Add(baris with { Bidang = bidang });
                    awalBidang = hasil.Count;
                }
                else
                {
                    hasil.Add(baris);
                }
            }

            return hasil;
        }

        private static string BidangDariNama(string indikator)
        {
            var nama = indikator.Trim().ToUpperInvariant();
            if (nama.StartsWith("IKS", StringComparison.Ordinal)) return "IKS";
            if (nama.StartsWith("IKE", StringComparison.Ordinal)) return "IKE";
            if (nama.StartsWith("IKL", StringComparison.Ordinal)) return "IKL";
            return "";
        }

        /// <summary>
        /// Pihak penanggung pada satu baris: kolom PUSAT/PROV/KAB/DESA/CSR/
        /// LAINNYA digabung apa adanya ("KAB: Dinkes, PU").
        /// </summary>
        private static string? GabungPihak(JsonElement r)
        {
            var bagian = new List<string>(6);
            foreach (var (kunci, label) in new[]
                     {
                         ("PUSAT", "Pusat"), ("PROV", "Prov"), ("KAB", "Kab"),
                         ("DESA", "Desa"), ("CSR", "CSR"), ("LAINNYA", "Lainnya")
                     })
            {
                var isi = Teks(r, kunci);
                if (!string.IsNullOrWhiteSpace(isi) && isi.Trim() != "-")
                {
                    bagian.Add(label + ": " + isi.Trim());
                }
            }

            return bagian.Count == 0 ? null : string.Join("; ", bagian);
        }

        private static string? Teks(JsonElement elemen, string kunci)
        {
            if (!elemen.TryGetProperty(kunci, out var nilai))
            {
                return null;
            }

            return nilai.ValueKind switch
            {
                JsonValueKind.String => nilai.GetString(),
                JsonValueKind.Number => nilai.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Array => string.Join(", ",
                    nilai.EnumerateArray()
                        .Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.GetRawText())
                        .Where(x => !string.IsNullOrWhiteSpace(x))),
                _ => null
            };
        }

        private static double? Angka(JsonElement elemen, string kunci)
        {
            if (!elemen.TryGetProperty(kunci, out var nilai))
            {
                return null;
            }

            if (nilai.ValueKind == JsonValueKind.Number)
            {
                return nilai.GetDouble();
            }

            return nilai.ValueKind == JsonValueKind.String ? AngkaDari(nilai.GetString()) : null;
        }

        private static double? AngkaDari(string? teks)
        {
            if (string.IsNullOrWhiteSpace(teks))
            {
                return null;
            }

            return double.TryParse(teks.Trim().Replace(',', '.'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var hasil)
                ? hasil
                : null;
        }
    }

    /// <summary>Sumber data IDM yang dipakai halaman IDM (memudahkan pengujian tanpa internet).</summary>
    public interface IIdmKemendesaKlien
    {
        Task<MuatanIdm> MuatAsync(string kodeDesa, int tahun, CancellationToken ct = default);
    }

    /// <summary>
    /// Klien data Indeks Desa Membangun di situs resmi Kemendesa
    /// (<c>idm.kemendesa.go.id</c>) — sumber yang sama dengan yang dipakai situs
    /// pusat untuk menampilkan skor IKS/IKE/IKL, IDM, dan status per desa.
    ///
    /// Aplikasi tidak menghitung ulang skor dan tidak menyimpan salinan angka:
    /// halaman IDM hanya menampilkan jawaban situs pusat untuk kode desa pada
    /// Pengaturan Surat, sehingga angkanya selalu sama dengan yang dilihat
    /// pemerintah pusat.
    ///
    /// Semua permintaan memakai HTTPS. Kegagalan sertifikat tidak boleh diam-diam
    /// diturunkan ke HTTP karena hasil yang dipakai kantor desa dapat dimodifikasi
    /// pada jaringan di antara pengguna dan situs pusat.
    /// </summary>
    public sealed class IdmKemendesaKlien : IIdmKemendesaKlien
    {
        public const string Host = "idm.kemendesa.go.id";

        /// <summary>Situs pusat yang dibuka tombol "Buka situs pusat".</summary>
        public const string AlamatSitus = "https://idm.kemendesa.go.id/";

        private const string AkarHttps = "https://" + Host + "/open/api/desa/rumusan/";

        private readonly HttpClient _http;
        private readonly ILogger<IdmKemendesaKlien> _logger;

        public IdmKemendesaKlien(HttpClient? http = null, ILogger<IdmKemendesaKlien>? logger = null)
        {
            _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
            _logger = logger ?? NullLogger<IdmKemendesaKlien>.Instance;
        }

        public async Task<MuatanIdm> MuatAsync(string kodeDesa, int tahun, CancellationToken ct = default)
        {
            string kode = new((kodeDesa ?? string.Empty).Where(char.IsDigit).ToArray());
            if (kode.Length != 10)
            {
                return MuatanIdm.Galat(
                    "Kode desa belum lengkap. Pilih desa/kelurahan pada Pengaturan Surat lebih dulu.");
            }

            try
            {
                var isi = await BacaAsync(AkarHttps + kode + "/" + tahun, ct)
                    .ConfigureAwait(false);
                var muatan = IdmKemendesaBaca.Urai(isi, tahun);
                return muatan with { LewatHttp = false };
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException)
            {
                return MuatanIdm.Galat("Situs pusat tidak menjawab (waktu habis). Periksa sambungan internet.");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogInformation("IDM pusat tidak terjangkau: {Pesan}", ex.Message);
                return MuatanIdm.Galat("Situs pusat tidak terjangkau. Periksa sambungan internet lalu muat ulang.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal membaca data IDM desa {Kode} tahun {Tahun}", kode, tahun);
                return MuatanIdm.Galat("Gagal membaca data IDM: " + ex.Message);
            }
        }

        private async Task<string> BacaAsync(string alamat, CancellationToken ct)
        {
            using var permintaan = new HttpRequestMessage(HttpMethod.Get, alamat);
            permintaan.Headers.Accept.ParseAdd("application/json");
            // Situs pusat menolak permintaan tanpa agen pengguna yang jelas.
            permintaan.Headers.UserAgent.ParseAdd("SuDesApp/1.0 (aplikasi kantor desa)");

            using var jawaban = await _http.SendAsync(permintaan, ct).ConfigureAwait(false);
            var isi = await jawaban.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            // Situs ini menjawab 400 + {"message":"ID Desa tidak ditemukan"}
            // untuk desa/tahun yang tidak ada — isinya tetap dibaca dan
            // diterjemahkan IdmKemendesaBaca, bukan dianggap kegagalan jaringan.
            if (!jawaban.IsSuccessStatusCode && string.IsNullOrWhiteSpace(isi))
            {
                throw new HttpRequestException(
                    $"Situs pusat menjawab {(int)jawaban.StatusCode} tanpa isi.");
            }

            return isi;
        }
    }
}
