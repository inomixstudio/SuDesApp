namespace SuDesApp.Server;

/// <summary>
/// Pembuat kode aktivasi otomatis untuk pendaftaran desa baru dari pusat
/// administer — operator tidak perlu mengarang kode sendiri.
///
/// Aturan formatnya:
/// <list type="bullet">
/// <item>Awalan <see cref="Awalan"/> membedakannya dari kode yang diketik
/// manual dan membuatnya mudah dikenali di riwayat;</item>
/// <item>alfabet <see cref="Alfabet"/> TANPA karakter rancu (tanpa 0/O dan
/// 1/I/L) supaya kode yang dibacakan lewat WhatsApp tidak salah ketik —
/// kesalahan satu huruf berarti desa tidak bisa aktivasi dan harus
/// menghubungi admin lagi;</item>
/// <item>keunikan TIDAK dijamin kelas ini: yang memastikan adalah pemanggil
/// (<see cref="LayananAdmin"/>) dengan mengecek ulang ke penyimpanan, karena
/// keunikan hanya bermakna terhadap sumber data yang benar.</item>
/// </list>
/// Sumber acak bisa disuntikkan supaya hasilnya dapat diprediksi pada uji.
/// </summary>
public static class GeneratorKodeAktivasi
{
    /// <summary>Awalan semua kode hasil generator.</summary>
    public const string Awalan = "SUDES";

    /// <summary>
    /// Alfabet tanpa karakter rancu: tanpa 0 (nol), O, 1 (satu), I, dan L —
    /// 31 karakter. Karakter yang tersisa tidak mudah tertukar saat dibaca
    /// atau dibacakan.
    /// </summary>
    public const string Alfabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    /// <summary>Panjang badan kode: dua segmen empat karakter dipisah tanda hubung.</summary>
    public const int PanjangBadan = 8;

    /// <summary>
    /// Bentuk kode hasil generator: <c>SUDES-XXXX-XXXX</c> (contoh:
    /// <c>SUDES-K7Q2-4831</c>). Hanya untuk penjelasan di halaman admin.
    /// </summary>
    public static string PolaTampil => $"{Awalan}-XXXX-XXXX";

    /// <summary>
    /// Buat satu kode acak. <paramref name="acak"/> bisa disuntikkan pada uji;
    /// bila null, dipakai <see cref="Random.Shared"/>. Hasilnya selalu
    /// <see cref="PolaTampil"/>: huruf besar, dua segmen, satu tanda hubung.
    /// </summary>
    public static string Buat(Random? acak = null)
    {
        var sumber = acak ?? Random.Shared;
        var badan = new char[PanjangBadan];
        for (int i = 0; i < PanjangBadan; i++)
        {
            badan[i] = Alfabet[sumber.Next(Alfabet.Length)];
        }

        return $"{Awalan}-{new string(badan, 0, 4)}-{new string(badan, 4, 4)}";
    }
}
