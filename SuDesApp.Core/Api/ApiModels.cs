using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using SuDesApp.Data.Models;

namespace SuDesApp.Api
{
    /// <summary>
    /// Tingkat keluasan data sebuah endpoint. Nilai ini menentukan apa yang
    /// boleh keluar lewat API: hanya angka agregat, data perangkat desa, atau
    /// permintaan yang masuk.
    ///
    /// PENTING: tidak ada kategori untuk data pribadi warga. NIK, alamat,
    /// nomor HP, dan tanggal lahir warga TIDAK PERNAH dikeluarkan lewat API.
    /// Angka agregat sudah cukup untuk dashboard kecamatan atau website desa,
    /// sedangkan data pribadi hanya perlu dibaca operator dari dalam aplikasi.
    /// </summary>
    public enum ApiKategori
    {
        /// <summary>Angka agregat: jumlah warga, surat per status, dan sejenisnya.</summary>
        Agregat,

        /// <summary>Data perangkat desa: nama jabatan, nama pejabat, masa jabatan.</summary>
        Perangkat,

        /// <summary>Permintaan surat yang dikirim dari luar dan masuk ke antrean operator.</summary>
        Permintaan,

        /// <summary>
        /// Pemeriksaan keaslian surat dari kode yang tercetak di kaki surat.
        /// Endpoint kategori ini boleh dipanggil tanpa kunci API karena pihak
        /// yang memegang surat (bank, instansi) tidak punya kunci tersebut.
        /// </summary>
        Verifikasi
    }

    /// <summary>
    /// Satu endpoint API yang didaftarkan di tabel rute dan ditampilkan di
    /// halaman API. Sengaja dibuat data (bukan enum) supaya daftar endpoint
    /// bisa dibaca sebagai dokumentasi tanpa mengubah kode.
    /// </summary>
    public sealed class ApiEndpoint
    {
        public string Metode { get; set; } = "GET";

        /// <summary>Jalur relatif terhadap <see cref="ApiRute.Prefix"/>, mis. "/status".</summary>
        public string Jalur { get; set; } = string.Empty;

        public string Nama { get; set; } = string.Empty;

        public string Keterangan { get; set; } = string.Empty;

        public ApiKategori Kategori { get; set; }

        /// <summary>
        /// True untuk endpoint yang boleh dipanggil tanpa kunci API. Hanya
        /// dipakai endpoint verifikasi surat: pemegang surat tidak punya kunci,
        /// dan jawabannya sengaja dibatasi pada data yang sudah tercetak di
        /// surat itu sendiri (tanpa NIK/alamat).
        /// </summary>
        public bool TanpaKunci { get; set; }

        /// <summary>Jalur penuh yang dipanggil pemanggil, mis. "/sudes/api/v1/status".</summary>
        public string JalurPenuh => ApiRute.Prefix + Jalur;

        /// <summary>Baris siap tampil di daftar endpoint ("GET /sudes/api/v1/status").</summary>
        public string BarisTampil => $"{Metode} {JalurPenuh}";

        public string KategoriTampil => Kategori switch
        {
            ApiKategori.Agregat => "Agregat",
            ApiKategori.Perangkat => "Perangkat Desa",
            ApiKategori.Verifikasi => "Verifikasi Surat",
            _ => "Permintaan Masuk"
        };
    }

    /// <summary>
    /// Amplop respons seragam untuk semua endpoint: pemanggil cukup memeriksa
    /// <see cref="Sukses"/>, lalu membaca <see cref="Data"/>. Galat memakai
    /// kode yang stabil (lihat <see cref="ApiKodeGalat"/>) supaya sistem luar
    /// bisa bereaksi tanpa harus membaca pesan dalam bahasa manusia.
    /// </summary>
    public sealed class ApiAmplop
    {
        public bool Sukses { get; set; }

        /// <summary>Kode galat singkat; null pada respons berhasil.</summary>
        public string? Kode { get; set; }

        /// <summary>Pesan untuk dibaca manusia.</summary>
        public string? Pesan { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Data { get; set; }

        public static ApiAmplop Berhasil(object? data, string? pesan = null) => new()
        {
            Sukses = true,
            Pesan = pesan,
            Data = data
        };

        public static ApiAmplop Gagal(string kode, string pesan) => new()
        {
            Sukses = false,
            Kode = kode,
            Pesan = pesan
        };
    }

    /// <summary>Kode galat yang dipakai konsisten di seluruh endpoint.</summary>
    public static class ApiKodeGalat
    {
        public const string ApiKeyKosong = "api_key_kosong";
        public const string ApiKeySalah = "api_key_salah";

        /// <summary>Kunci sah tetapi cakupannya tidak mencakup endpoint diminta (HTTP 403).</summary>
        public const string ApiCakupanKurang = "cakupan_tidak_cukup";
        public const string TerlaluBanyak = "terlalu_banyak_permintaan";
        public const string EndpointTidakAda = "endpoint_tidak_ada";
        public const string MetodeTidakDiizinkan = "metode_tidak_diizinkan";
        public const string PermintaanTidakSah = "permintaan_tidak_sah";
        public const string BadanTerlaluBesar = "badan_terlalu_besar";
        public const string GagalDiServer = "gagal_di_server";

        /// <summary>Kode verifikasi surat tidak ada di arsip desa.</summary>
        public const string KodeTidakDikenal = "kode_tidak_dikenal";
    }

    /// <summary>
    /// Isi permintaan surat yang dikirim dari luar aplikasi. Nama field memakai
    /// snake_case (paham yang lazim di API) dan nama camelCase tetap diterima
    /// karena <c>PropertyNameCaseInsensitive</c> diaktifkan.
    /// </summary>
    public sealed class ApiPermintaanMasuk
    {
        /// <summary>
        /// Kode unik dari sistem pengirim. Bila diisi, pengiriman ulang dengan
        /// referensi sama tidak menghasilkan permintaan ganda — penting karena
        /// pemanggil biasanya mencoba ulang bila jawabannya lambat.
        /// </summary>
        [JsonPropertyName("referensi")]
        public string? Referensi { get; set; }

        [JsonPropertyName("nama_warga")]
        public string? NamaWarga { get; set; }

        [JsonPropertyName("nik")]
        public string? Nik { get; set; }

        [JsonPropertyName("no_hp")]
        public string? NomorHp { get; set; }

        /// <summary>Jenis surat, mis. "SKTM". Boleh dikosongkan bila <see cref="Pesan"/> memuat jenisnya.</summary>
        [JsonPropertyName("jenis_surat")]
        public string? JenisSurat { get; set; }

        /// <summary>
        /// Isi pesan warga dalam format bebas (dipakai parser yang sama dengan
        /// percakapan WhatsApp), mis. "SKTM\nNama: Budi\nNIK: 3204...".
        /// </summary>
        [JsonPropertyName("pesan")]
        public string? Pesan { get; set; }

        /// <summary>Isian terstruktur per jenis surat (opsional; dibaca operator di panel layanan online).</summary>
        [JsonPropertyName("data")]
        public Dictionary<string, string>? Data { get; set; }
    }

    /// <summary>Hasil pembuatan permintaan dari API.</summary>
    public sealed class ApiHasilPermintaan
    {
        public bool Berhasil { get; set; }

        /// <summary>Kode permintaan (mis. PMT-2026-0001) supaya pemanggil bisa melacak statusnya.</summary>
        public string? Kode { get; set; }

        public string? Pesan { get; set; }

        /// <summary>True bila permintaan ini sebenarnya sudah pernah dibuat (referensi sama).</summary>
        public bool SudahAda { get; set; }
    }

    /// <summary>
    /// Status satu permintaan untuk <c>GET /permintaan/{kode}</c>. Sengaja tidak
    /// memuat NIK maupun nama warga: pemanggil luar sudah mengetahui datanya
    /// sendiri, sedangkan log akses tidak perlu menyalin data pribadi.
    /// </summary>
    public sealed class ApiStatusPermintaan
    {
        public string Kode { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Sumber { get; set; } = string.Empty;
        public string JenisSurat { get; set; } = string.Empty;
        public DateTime Tanggal { get; set; }

        /// <summary>Catatan operator (mis. alasan permintaan perlu diperbaiki).</summary>
        public string? Catatan { get; set; }

        public bool AdaSurat { get; set; }

        /// <summary>Nomor surat yang sudah dibuat dari permintaan ini (bila ada).</summary>
        public string? NomorSurat { get; set; }
    }

    /// <summary>Angka agregat untuk <c>GET /statistik</c>.</summary>
    public sealed class ApiRingkasan
    {
        public string VersiAplikasi { get; set; } = string.Empty;
        public DateTime DibuatPada { get; set; }

        public int TotalWarga { get; set; }
        public int WargaAktif { get; set; }
        public int WargaLakiLaki { get; set; }
        public int WargaPerempuan { get; set; }
        public int JumlahKartuKeluarga { get; set; }
        public IReadOnlyList<ApiBarisRingkas> WargaPerRt { get; set; } = Array.Empty<ApiBarisRingkas>();

        public int PerangkatTotal { get; set; }
        public int PerangkatAktif { get; set; }
        public int PerangkatSelesai { get; set; }
        public IReadOnlyList<string> JabatanIntiKosong { get; set; } = Array.Empty<string>();

        public int SuratTotal { get; set; }
        public IReadOnlyDictionary<string, int> SuratPerStatus { get; set; } =
            new Dictionary<string, int>();

        /// <summary>
        /// Jumlah surat per status alur persetujuan (field
        /// <c>suratPerStatusPersetujuan</c>). Kunci selalu terisi penuh:
        /// DIAJUKAN, DIVERIFIKASI, TERBIT, DITOLAK, dan TANPA_ALUR (surat yang
        /// tidak melewati alur — termasuk draf), supaya dashboard sistem luar
        /// tidak perlu menangani kunci yang muncul-hilang.
        /// </summary>
        public IReadOnlyDictionary<string, int> SuratPerStatusPersetujuan { get; set; } =
            new Dictionary<string, int>();

        public int PermintaanTotal { get; set; }
        public int PermintaanBelumDibaca { get; set; }
        public IReadOnlyDictionary<string, int> PermintaanPerStatus { get; set; } =
            new Dictionary<string, int>();
    }

    /// <summary>
    /// Hasil pemeriksaan <c>GET /verifikasi/{kode}</c>. Memuat kolom yang sudah
    /// tercetak di surat (nomor, jenis, tanggal, pemohon tersamar) — tidak ada
    /// NIK, alamat, atau nomor HP, sehingga aman dipanggil tanpa kunci API.
    /// </summary>
    public sealed class ApiHasilVerifikasi
    {
        public string Kode { get; set; } = string.Empty;

        /// <summary>True bila kode terdaftar dan isi surat masih cocok dengan arsip.</summary>
        public bool Sah { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? NomorSurat { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? JenisSurat { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DateTime? TanggalSurat { get; set; }

        /// <summary>Nama pemohon dalam bentuk tersamar, mis. "BUDI S.".</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? PemohonTersamar { get; set; }

        /// <summary>True bila data surat berubah setelah diterbitkan.</summary>
        public bool DataBerubah { get; set; }

        /// <summary>
        /// Status alur persetujuan surat: DIAJUKAN, DIVERIFIKASI, TERBIT,
        /// DITOLAK, atau TANPA_ALUR (tidak melewati alur). Selalu terisi —
        /// sistem luar cukup membandingkan nilai ini tanpa membedakan
        /// kunci yang tidak dikirim. Untuk surat dalam alur, status ini juga
        /// sama dengan label yang tercetak di kertas surat.
        /// </summary>
        public string StatusPersetujuan { get; set; } = StatusPersetujuanSurat.TanpaAlur;
    }

    /// <summary>Satu baris angka agregat (per RT, per agama, dan sejenisnya).</summary>
    public sealed class ApiBarisRingkas
    {
        public string Kunci { get; set; } = string.Empty;
        public int Jumlah { get; set; }
        public int LakiLaki { get; set; }
        public int Perempuan { get; set; }
    }

    /// <summary>
    /// Satu perangkat desa untuk <c>GET /perangkat-desa</c>. Hanya memuat kolom
    /// yang relevan untuk kop surat — NIP/NIK pejabat, alamat,
    /// dan nomor HP sengaja tidak ikut supaya data ini aman dibuka lewat jaringan.
    /// </summary>
    public sealed class ApiPerangkatRingkas
    {
        public string Nama { get; set; } = string.Empty;
        public string Jabatan { get; set; } = string.Empty;
        public string Kelompok { get; set; } = string.Empty;
        public string Wilayah { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DateTime? MasaJabatanMulai { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DateTime? MasaJabatanSelesai { get; set; }
    }

    /// <summary>
    /// Rekap laporan bulanan untuk <c>GET /rekap-bulanan</c>: jumlah surat per
    /// bulan untuk satu tahun, dirinci per status persetujuan. Hanya angka
    /// agregat — tidak ada nomor surat, nama, maupun data warga — sehingga
    /// aman dipantau sistem luar lewat jaringan desa.
    /// </summary>
    public sealed class ApiRekapBulanan
    {
        public int Tahun { get; set; }
        public DateTime DibuatPada { get; set; }

        /// <summary>Jumlah seluruh surat pada tahun ini (semua status persetujuan).</summary>
        public int JumlahSuratTotal { get; set; }

        /// <summary>Total setahun per status persetujuan, kunci selalu penuh.</summary>
        public IReadOnlyDictionary<string, int> SuratPerStatusPersetujuan { get; set; } =
            new Dictionary<string, int>();

        /// <summary>12 baris (Januari–Desember); bulan tanpa surat tetap muncul dengan nol.</summary>
        public IReadOnlyList<ApiBarisRekapBulanan> Bulan { get; set; } = Array.Empty<ApiBarisRekapBulanan>();
    }

    /// <summary>Satu bulan dalam rekap laporan bulanan.</summary>
    public sealed class ApiBarisRekapBulanan
    {
        /// <summary>Bulan "2026-01" s.d. "2026-12".</summary>
        public string Bulan { get; set; } = string.Empty;

        public int JumlahSurat { get; set; }

        /// <summary>Rincian bulan ini per status persetujuan, kunci selalu penuh.</summary>
        public IReadOnlyDictionary<string, int> PerStatusPersetujuan { get; set; } =
            new Dictionary<string, int>();
    }
}
