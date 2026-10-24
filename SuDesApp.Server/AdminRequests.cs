namespace SuDesApp.Server;

/// <summary>Permintaan pendaftaran desa baru dari endpoint admin.</summary>
public sealed record DaftarkanPelangganRequest(
    string KodeAktivasi, string NamaDesa, string? Kontak = null, int MaxPerangkat = 2);

/// <summary>
/// Permintaan daftarkan desa baru dengan kode aktivasi yang DIBUATKAN SERVER
/// (generator <see cref="GeneratorKodeAktivasi"/>) — operator tidak mengirim
/// kode apa pun; kode hasil generator yang dikembalikan.
/// </summary>
public sealed record BuatKodeAktivasiRequest(
    string NamaDesa, string? Kontak = null, int MaxPerangkat = 2);

/// <summary>Permintaan ubah batas perangkat desa dari pusat administer.</summary>
public sealed record UbahBatasRequest(int Batas);

/// <summary>Permintaan ubah nama/kontak desa dari pusat administer (kode tetap).</summary>
public sealed record UbahDesaRequest(string NamaDesa, string? Kontak = null);

/// <summary>
/// Permintaan terbitkan pengumuman dari halaman admin. Mulai/Sampai opsional
/// (ISO-8601 UTC dari peramban); jenis dibaca dari select formulir.
/// </summary>
public sealed record BuatPengumumanRequest(
    string? Judul, string? Isi, string? Jenis, DateTime? Mulai, DateTime? Sampai);

/// <summary>
/// Permintaan penandaan lunas manual (QRIS statis/transfer). Semua opsional:
/// paket null → paket bawaan katalog, nominal null → harga katalog paket itu,
/// catatan hanya untuk log operator.
///
/// <c>BuktiBase64</c> adalah lampiran gambar bukti transfer (base64 atau data URL
/// hasil pembaca berkas di peramban) dan <c>BuktiNama</c> nama berkasnya untuk
/// tampilan. Keduanya opsional: pembukuan manual dengan catatan teks saja tetap
/// sah seperti sebelumnya. Penilaian isinya ada di <see cref="BuktiTransfer"/>.
/// </summary>
public sealed record TandaiLunasRequest(
    string? Paket, long? Nominal, string? Catatan, string? BuktiBase64 = null, string? BuktiNama = null);
