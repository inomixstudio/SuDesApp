namespace SuDesApp.Server;

/// <summary>
/// Satu paket langganan: nama yang dilihat pengguna, harga, dan berapa hari
/// akses yang ditambahkan setelah satu pembayaran lunas.
/// </summary>
/// <param name="Kode">Kunci paket, mis. "bulanan" / "tahunan".</param>
/// <param name="Nama">Nama untuk ditampilkan di aplikasi.</param>
/// <param name="Harga">Nominal satu siklus, dalam rupiah penuh.</param>
/// <param name="Hari">Tambahan masa aktif setelah satu siklus dibayar.</param>
public sealed record PaketLangganan(string Kode, string Nama, long Harga, int Hari);

/// <summary>
/// Katalog paket langganan — SATU-SUMBER-KEBENARAN harga.
///
/// PENTING: harga hanya boleh datang dari sini, tidak pernah dari permintaan
/// klien. Bila klien boleh menentukan nominal, orang bisa meminta tagihan Rp1
/// lalu membayar Rp1 untuk masa akses penuh sebulan (atau setahun).
///
/// Dua tempat memakainya, dan keduanya wajib konsisten:
///  - <c>/buat-tagihan</c> — saat membuat tagihan, harga paket disimpan ke
///    tabel <c>tagihan</c>.
///  - <c>/webhook-pembayaran</c> — saat dana masuk, nominal yang ditandatangani
///    dibandingkan terhadap harga yang tersimpan di <c>tagihan</c>, dan
///    perpanjangan memakai jumlah hari dari paket yang sama.
/// </summary>
public sealed class KatalogPaket
{
    private readonly Dictionary<string, PaketLangganan> _paket;

    public KatalogPaket(IConfiguration config)
    {
        _paket = new Dictionary<string, PaketLangganan>(StringComparer.OrdinalIgnoreCase);

        // Bawaan dipakai bila konfigurasi kosong — server tetap bisa jalan
        // tanpa appsettings, dan harga yang sama berlaku di mana pun.
        //
        // Harga resmi (Oktober 2026): tiga paket berbayar, dan harga per hari
        // TURUN seiring panjang komitmennya — itulah alasan orang membayar di
        // muka, bukan sekadar angka yang kebetulan lebih besar:
        //   Bulanan   Rp150.000 / 30 hari   = Rp5.000/hari
        //   6 Bulan   Rp750.000 / 180 hari  = Rp4.167/hari  (hemat ±17%)
        //   Tahunan   Rp1.200.000 / 365 hari = Rp3.288/hari (hemat ±34%)
        //
        // Trial 30 hari TIDAK lewat katalog ini: harganya 0, dan paket Rp0 di
        // katalog tagihan berarti tagihan Rp0 yang bisa dikali ulang. Trial
        // punya jalurnya sendiri (LayananAktivasi.HariTrial + /aktifkan-trial).
        //
        // Tagihan lama yang belum dibayar tetap sah walau harganya berubah:
        // harga & durasi disimpan di tabel tagihan, bukan dibaca ulang dari sini.
        Muat("bulanan", "Bulanan", 150_000, 30);
        Muat("enambulan", "6 Bulan", 750_000, 180);
        Muat("tahunan", "Tahunan", 1_200_000, 365);

        foreach (var (kode, nilai) in config.GetSection("Langganan:Paket")
                     .GetChildren()
                     .ToDictionary(c => c.Key, c => c))
        {
            var nama = nilai["Nama"];
            var harga = nilai.GetValue<long?>("Harga");
            var hari = nilai.GetValue<int?>("Hari");
            if (nama is null || harga is null || hari is null) continue;
            Muat(kode, nama, harga.Value, hari.Value);
        }
    }

    private void Muat(string kode, string nama, long harga, int hari)
    {
        if (string.IsNullOrWhiteSpace(kode)) return;
        if (harga <= 0 || hari <= 0)
            throw new InvalidOperationException(
                $"Paket '{kode}' tidak valid: harga dan hari harus lebih dari nol.");

        _paket[kode.Trim()] = new PaketLangganan(kode.Trim(), nama, harga, hari);
    }

    /// <summary>Semua paket, untuk dikirim ke aplikasi agar UI bisa menampilkan pilihan.</summary>
    public IReadOnlyList<PaketLangganan> Semua =>
        _paket.Values.OrderBy(p => p.Harga).ToArray();

    /// <summary>Ambil paket by kode; null bila tidak dikenal.</summary>
    public PaketLangganan? Cari(string? kode) =>
        kode is not null && _paket.TryGetValue(kode.Trim(), out var p) ? p : null;

    /// <summary>Kode paket bawaan (paling murah) — dipakai saat kode tidak disebutkan.</summary>
    public PaketLangganan Bawaan =>
        _paket.Values.OrderBy(p => p.Harga).FirstOrDefault()
        ?? new PaketLangganan("bulanan", "Bulanan", 150_000, 30);
}
