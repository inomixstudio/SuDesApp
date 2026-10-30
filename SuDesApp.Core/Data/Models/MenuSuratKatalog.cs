using System.Collections.Generic;

namespace SuDesApp.Data.Models
{
    /// <summary>Satu pilihan pada menu "Buat Surat Baru".</summary>
    public sealed class PilihanSurat
    {
        /// <summary>Label yang dibaca pengguna, mis. "SKD Belum Menikah".</summary>
        public string Label { get; init; } = string.Empty;

        /// <summary>Nilai <see cref="SuratConstants"/> untuk jenis surat ini.</summary>
        public string Jenis { get; init; } = string.Empty;
    }

    /// <summary>
    /// Katalog pilihan surat pada menu "Buat Surat Baru" — satu sumber untuk
    /// aplikasi desktop dan halaman web, sehingga keduanya menampilkan daftar
    /// yang sama persis: label, jenis surat, dan urutannya.
    ///
    /// Urutan di sini adalah urutan yang dilihat pengguna di sidebar. Jenis
    /// surat tetap diambil dari <see cref="SuratConstants"/> supaya tidak ada
    /// kode jenis yang ditulis ulang sebagai teks bebas.
    /// </summary>
    public static class MenuSuratKatalog
    {
        public static readonly IReadOnlyList<PilihanSurat> Semua = new PilihanSurat[]
        {
            new() { Label = "SKD Belum Menikah", Jenis = SuratConstants.SKD_UMUM },
            new() { Label = "Domisili Warga", Jenis = SuratConstants.DOMISILI_WARGA },
            new() { Label = "Domisili Instansi", Jenis = SuratConstants.INSTANSI },
            new() { Label = "SKU", Jenis = SuratConstants.SKU },
            new() { Label = "Pengantar SKCK", Jenis = SuratConstants.PENGANTAR_SKCK },
            new() { Label = "Izin Suami / Orang Tua", Jenis = SuratConstants.IZIN_ORTU },
            new() { Label = "SKTM", Jenis = SuratConstants.SKTM },
            new() { Label = "Belum Terbit", Jenis = SuratConstants.SK_BELUM_TERBIT },
            new() { Label = "Garapan Sawah", Jenis = SuratConstants.GARAPAN_SAWAH },
            new() { Label = "Kematian", Jenis = SuratConstants.KEMATIAN },
            new() { Label = "Beda Nama", Jenis = SuratConstants.BEDANAMA },
            new() { Label = "Kenal Lahir", Jenis = SuratConstants.KENAL_LAHIR },
            new() { Label = "Tinggal Sementara", Jenis = SuratConstants.IJIN_TINGGAL },
            new() { Label = "Ahli Waris", Jenis = SuratConstants.AHLI_WARIS },
            new() { Label = "Pengantar Adminduk", Jenis = SuratConstants.PENGANTAR_ADMINDUK },
            new() { Label = "Surat Keterangan Pindah", Jenis = SuratConstants.SK_PINDAH },
            new() { Label = "SK Tidak Punya Rumah", Jenis = SuratConstants.SK_TIDAK_PUNYA_RUMAH },
            new() { Label = "Izin Keramaian", Jenis = SuratConstants.IZIN_KERAMAIAN },
        };
    }
}
