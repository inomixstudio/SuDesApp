using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SuDesApp.Services
{
    /// <summary>
    /// Satu pengumuman broadcast yang diterima aplikasi dari server lisensi
    /// (<c>GET /pengumuman</c> — lihat SuDesApp.Server/LayananPengumuman.cs).
    /// Bentuknya sengaja sederhana: id + teks + waktu, tanpa kolom yang bisa
    /// menunjuk desa tertentu — pengumuman sifatnya broadcast.
    /// </summary>
    /// <param name="Id">Id unik pengumuman dari tabel <c>pengumuman</c> Supabase.</param>
    /// <param name="Judul">Judul singkat yang tampil di popup dan lonceng.</param>
    /// <param name="Isi">Isi pesan; boleh menjelaskan jawab-sudah.</param>
    /// <param name="Jenis">"info" (bawaan) atau "peringatan" (aksen warna). Nilai lain memakai Info.</param>
    /// <param name="Dibuat">Waktu operator menerbitkan pengumuman (UTC, ISO-8601).</param>
    /// <param name="Sampai">Batas tampil sesuai tabel (null = selamanya); sudah disaring server.</param>
    public sealed record Pengumuman(
        string Id, string Judul, string Isi, string Jenis, DateTime Dibuat, DateTime? Sampai);

    /// <summary>
    /// Klien endpoint pengumuman publik di server lisensi. Mengikuti pola yang
    /// sama dengan <see cref="LanggananKlien"/>: urusan alamat server milik
    /// <see cref="LisensiManager"/> melalui <see cref="LisensiManager.AlamatEndpoint"/>,
    /// parser JSON hidup di sini sehingga perubahan bentuk balasan server tidak
    /// menyentuh layanan pemantauan maupun tampilan.
    ///
    /// Bukan operasi sensitif: endpoint publik, tanpa kunci, tanpa data desa.
    /// Server tidak terjangkau adalah keadaan NORMAL (instalasi offline) —
    /// kegagalan dikembalikan sebagai daftar kosong, tidak pernah melempar.
    /// </summary>
    public sealed class PengumumanKlien
    {
        private readonly LisensiManager _lisensi;
        private readonly HttpClient _http;
        private readonly ILogger<PengumumanKlien> _logger;

        private bool _gagalPernahDibicarakan;

        public PengumumanKlien(LisensiManager lisensi, HttpClient http, ILogger<PengumumanKlien> logger)
        {
            _lisensi = lisensi ?? throw new ArgumentNullException(nameof(lisensi));
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Muat pengumuman aktif dari server. Kosong (bukan null) bila alamat
        /// server belum diatur, server menjawab mengejutakan, maupun offline —
        /// pemanggil cukup memproses daftar apa adanya.
        /// </summary>
        public async Task<List<Pengumuman>> MuatAsync(CancellationToken ct = default)
        {
            var alamat = _lisensi.AlamatEndpoint("pengumuman");
            if (string.IsNullOrWhiteSpace(alamat))
            {
                return [];
            }

            try
            {
                // _http adalah HttpClient bersama dari DI (sama dengan yang
                // dipakai LisensiManager) — JANGAN di-Dispose di sini.
                var resp = await _http.GetAsync(alamat, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("GET /pengumuman menjawab {Status} — daftar dibiarkan kosong.", (int)resp.StatusCode);
                    return [];
                }

                var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);

                // Bentuk balasan: array pengumuman aktif (lihat endpoint server).
                // Array kosong = tidak ada pengumuman — bukan galat.
                var mentah = doc.RootElement;
                if (mentah.ValueKind != JsonValueKind.Array)
                {
                    _logger.LogWarning("GET /pengumuman menjawab bukan array ({Kind}) — diabaikan.", mentah.ValueKind);
                    return [];
                }

                var hasil = new List<Pengumuman>();
                foreach (var item in mentah.EnumerateArray())
                {
                    var id = item.TryGetProperty("id", out var i) ? i.GetString() : null;
                    var judul = item.TryGetProperty("judul", out var j) ? j.GetString() : null;
                    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(judul))
                    {
                        continue; // pengumuman tanpa id/judul tidak bisa dide-duplikasi
                    }

                    var isi = item.TryGetProperty("isi", out var s) ? s.GetString() ?? "" : "";
                    var jenis = item.TryGetProperty("jenis", out var t) ? t.GetString() ?? "info" : "info";
                    var dibuat = item.TryGetProperty("dibuat", out var d) && d.ValueKind == JsonValueKind.String
                        ? BacaWaktu(d.GetString())
                        : DateTime.UtcNow;
                    DateTime? sampai = item.TryGetProperty("sampai", out var p) && p.ValueKind == JsonValueKind.String
                        ? BacaWaktu(p.GetString())
                        : null;

                    hasil.Add(new Pengumuman(id!, judul!, isi, jenis, dibuat, sampai));
                }

                _gagalPernahDibicarakan = false;
                return hasil;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return [];
            }
            catch (Exception ex)
            {
                // Offline adalah keadaan normal; jangan banjiri log dengan
                // paket kesalahan yang sama setiap 60 detik.
                if (!_gagalPernahDibicarakan)
                {
                    _logger.LogInformation("Server pengumuman tidak terjangkau: {Pesan}", ex.Message);
                    _gagalPernahDibicarakan = true;
                }
                return [];
            }
        }

        /// <summary>Parse waktu ISO-8601; gagal jatuh ke UtcNow agar tidak mengunci antrean.</summary>
        internal static DateTime BacaWaktu(string? teks) =>
            string.IsNullOrWhiteSpace(teks) || !DateTime.TryParse(
                teks, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var waktu)
                ? DateTime.UtcNow
                : waktu;
    }

    /// <summary>
    /// Aturan "mana yang perlu ditampilkan": yang id-nya belum pernah tampil
    /// di perangkat ini. Murni (tanpa I/O) supaya bisa diuji unit — pemanggil
    /// (PengumumanLatar) yang menyimpul ke pembandingan pref.
    /// </summary>
    public static class PengumumanPemilih
    {
        /// <summary>
        /// Urutan tetap: terbaru dulu. Pengumuman yang sudah terlihat, null,
        /// atau id/judulnya kosong dibuang di sini — satu tempat, jadi popup,
        /// lonceng, dan penandaan "terlihat" tidak pernah berbeda.
        /// </summary>
        public static List<Pengumuman> SaringBelumDitampilkan(
            IEnumerable<Pengumuman> daftar, IReadOnlyCollection<string> sudahTerlihat)
        {
            var terlihat = new HashSet<string>(sudahTerlihat ?? Array.Empty<string>(), StringComparer.Ordinal);
            return (daftar ?? [])
                .Where(p => p is not null)
                .Where(p => !string.IsNullOrWhiteSpace(p.Id) && !string.IsNullOrWhiteSpace(p.Judul))
                .Where(p => !terlihat.Contains(p.Id))
                .OrderByDescending(p => p.Dibuat)
                .ToList();
        }
    }
}
