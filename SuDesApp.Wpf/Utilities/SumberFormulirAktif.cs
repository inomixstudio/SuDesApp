using SuDesApp.Data.Models;

namespace SuDesApp.Wpf.Utilities
{
    /// <summary>
    /// Keadaan sumber formulir untuk satu desa: kabupaten mana yang dipilih pada
    /// Pengaturan Surat, apakah formulirnya bisa diunduh otomatis, dan petunjuk
    /// yang tepat untuk keadaan itu. Dipisahkan dari view model agar aturan teks
    /// (terutama “tambahkan manual bila tautan tidak tersedia”) bisa diuji tanpa
    /// menyentuh WPF atau basis data.
    /// </summary>
    public sealed class SumberFormulirAktif
    {
        private SumberFormulirAktif(DesaData? desa, SumberFormulirDaerah? sumber, bool dimuat)
        {
            Desa = desa;
            Sumber = sumber;
            Dimuat = dimuat;
        }

        /// <summary>Keadaan awal sebelum pengaturan desa selesai dibaca.</summary>
        public static SumberFormulirAktif Memuat { get; } = new(null, null, dimuat: false);

        /// <summary>Susun keadaan dari data desa yang tersimpan (boleh null).</summary>
        public static SumberFormulirAktif Dari(DesaData? desa) => new(
            desa,
            KatalogFormulirDaerah.Cari(desa?.KodeKabupaten, desa?.JenisKabupaten, desa?.Kabupaten),
            dimuat: true);

        public DesaData? Desa { get; }

        public SumberFormulirDaerah? Sumber { get; }

        public bool Dimuat { get; }

        /// <summary>“Desa Sumberjaya” atau pengingat bahwa namanya belum diisi.</summary>
        public string NamaDesa => string.IsNullOrWhiteSpace(Desa?.NamaDesa)
            ? "Nama desa belum diisi"
            : "Desa " + Desa!.NamaDesa!.Trim();

        /// <summary>Identitas ringkas untuk chip kepala halaman: desa • kabupaten.</summary>
        public string IdentitasDesa => $"{NamaDesa}  \u2022  {Kabupaten}";

        /// <summary>Sebutan desa yang enak dibaca dalam kalimat (tanpa “Nama desa belum diisi”).</summary>
        private string DesaUntukKalimat => string.IsNullOrWhiteSpace(Desa?.NamaDesa)
            ? "desa ini"
            : NamaDesa;

        /// <summary>
        /// Nama kabupaten/kota yang dipakai. Bila sumbernya dikenal, nama dari
        /// katalog dipakai agar konsisten (“Kota Sukabumi”, bukan “Sukabumi”).
        /// </summary>
        public string Kabupaten
        {
            get
            {
                if (Sumber != null)
                {
                    return Sumber.Nama;
                }

                string nama = Desa?.Kabupaten?.Trim() ?? string.Empty;
                if (nama.Length == 0)
                {
                    return Dimuat ? "Kabupaten belum dipilih" : "Membaca pengaturan desa...";
                }

                string jenis = Desa?.JenisKabupaten?.Trim() ?? string.Empty;
                if (jenis.Length > 0 && !nama.StartsWith(jenis, System.StringComparison.OrdinalIgnoreCase))
                {
                    return jenis + " " + nama;
                }

                return nama;
            }
        }

        public bool UnduhanOtomatis => Sumber?.UnduhanOtomatis == true;

        public bool UnduhManual => Sumber is { UnduhanOtomatis: false };

        /// <summary>Kabupaten belum dipilih sama sekali.</summary>
        public bool BelumDiatur => Sumber is null && string.IsNullOrWhiteSpace(Desa?.Kabupaten);

        /// <summary>Kabupaten sudah dipilih, tetapi belum ada tautan resmi yang dikenal.</summary>
        public bool TidakTersedia => Sumber is null && !string.IsNullOrWhiteSpace(Desa?.Kabupaten);

        public bool AdaSumber => Sumber != null;

        public string StatusTeks => UnduhanOtomatis
            ? "Unduhan otomatis"
            : UnduhManual
                ? "Unduh manual"
                : BelumDiatur
                    ? "Perlu diatur"
                    : "Tanpa tautan resmi";

        public string IkonStatus => UnduhanOtomatis ? "\u2714" : UnduhManual ? "\u2192" : "!";

        /// <summary>Alamat sumber yang berlaku (kosong bila tidak ada).</summary>
        public string SumberTeks => Sumber?.TautanTampil ?? string.Empty;

        /// <summary>
        /// Petunjuk tindakan untuk keadaan sekarang. Keadaan tanpa tautan selalu
        /// menyebut tombol Tambah, sesuai permintaan “beri tahu untuk menambah
        /// formulir secara manual”.
        /// </summary>
        public string Keterangan
        {
            get
            {
                if (!Dimuat)
                {
                    return "Membaca pilihan kabupaten dari Pengaturan Surat...";
                }

                if (UnduhanOtomatis)
                {
                    return $"Formulir adminduk {Kabupaten} diambil langsung dari {Sumber!.TautanTampil}. " +
                           "Formulir yang sudah ada di folder Templates dilewati, jadi tombol Unduh dari " +
                           "Sumber aman ditekan berulang kali.";
                }

                if (UnduhManual)
                {
                    return $"Formulir adminduk {Kabupaten} tersedia di {Sumber!.TautanTampil}, tetapi " +
                           "unduhan otomatis belum didukung untuk daerah ini. Buka situsnya, unduh berkas " +
                           $"PDF yang dibutuhkan untuk {DesaUntukKalimat}, lalu tambahkan lewat tombol Tambah.";
                }

                if (TidakTersedia)
                {
                    return $"Belum ada tautan unduhan resmi yang dikenal untuk {Kabupaten}. Tambahkan " +
                           $"formulir {DesaUntukKalimat} secara manual lewat tombol Tambah — pakai blanko PDF dari " +
                           $"Dukcapil {Kabupaten} sesuai kebutuhan desa pada Pengaturan Surat.";
                }

                return "Pilih kabupaten/kota pada Pengaturan Surat → Data Desa agar aplikasi bisa " +
                       "menunjuk halaman formulir resmi yang tepat. Sementara itu, formulir tetap bisa " +
                       "ditambahkan manual lewat tombol Tambah.";
            }
        }

        /// <summary>Judul keadaan kosong pada daftar formulir.</summary>
        public string JudulKosong => Sumber is null
            ? "Belum ada formulir untuk desa Anda"
            : $"Belum ada formulir {Kabupaten}";

        /// <summary>Petunjuk mengisi folder Templates pada keadaan sekarang.</summary>
        public string PesanKosong
        {
            get
            {
                if (UnduhanOtomatis)
                {
                    return $"Folder Templates masih kosong. Tekan Unduh dari Sumber untuk mengambil " +
                           $"formulir resmi {Kabupaten} dari {Sumber!.TautanTampil}.";
                }

                if (UnduhManual)
                {
                    return $"Folder Templates masih kosong. Unduh formulir {Kabupaten} dari " +
                           $"{Sumber!.TautanTampil}, lalu tambahkan berkas PDF-nya lewat tombol Tambah.";
                }

                return $"Folder Templates masih kosong. Tambahkan berkas PDF formulir untuk {DesaUntukKalimat} " +
                       $"({Kabupaten}) secara manual lewat tombol Tambah — pakai blanko dari Dukcapil " +
                       "sesuai kabupaten pada Pengaturan Surat.";
            }
        }

        /// <summary>
        /// Petunjuk langkah demi langkah untuk keadaan tanpa tautan resmi. Dipakai
        /// kartu bantuan khusus agar ajakan menambah formulir manual tidak tenggelam
        /// di paragraf keterangan.
        /// </summary>
        public string PetunjukManual => TidakTersedia
            ? $"1. Unduh blanko PDF dari Dukcapil {Kabupaten} (atau minta berkasnya ke petugas kecamatan). " +
              $"2. Tekan Tambah Formulir, lalu pilih berkas PDF untuk {DesaUntukKalimat}. " +
              "3. Ulangi untuk formulir lain yang dibutuhkan desa."
            : string.Empty;
    }
}
