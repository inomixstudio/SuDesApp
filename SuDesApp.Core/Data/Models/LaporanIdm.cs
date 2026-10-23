using System;
using System.Collections.Generic;

namespace SuDesApp.Data.Models
{
    /// <summary>
    /// Satu baris indikator pada laporan IDM cetak — sudah berupa teks siap
    /// dicetak sehingga generator PDF tidak perlu tahu bentuk data mentah situs
    /// pusat.
    /// </summary>
    /// <param name="Nomor">Nomor indikator; kosong pada baris subtotal.</param>
    /// <param name="Bidang">IKS, IKE, IKL, atau kosong bila pusat tidak mengirimnya.</param>
    /// <param name="Skor">Skor apa adanya (indikator 0–5; subtotal berupa angka).</param>
    /// <param name="Subtotal">True untuk baris subtotal resmi (IKS/IKE/IKL/IDM/STATUS).</param>
    public sealed record BarisLaporanIdm(
        string Nomor,
        string Bidang,
        string Indikator,
        string Skor,
        string Keterangan,
        string Kegiatan,
        string Pihak,
        bool Subtotal);

    /// <summary>Satu tahun pada tabel "tahun yang diterbitkan pusat".</summary>
    public sealed record TahunLaporanIdm(int Tahun, string Skor, string Status);

    /// <summary>
    /// Data siap-cetak Laporan IDM: ringkasan penilaian + rincian indikator,
    /// untuk dilampirkan pada laporan desa.
    ///
    /// SEMUA angka di sini berasal dari data pusat yang sudah diambil halaman IDM
    /// (lihat <c>LaporanIdmBuilder</c>) — aplikasi tidak menghitung ulang skor.
    /// Data mentah dipisahkan dari generator PDF supaya isi laporan bisa diuji
    /// tanpa memuat dokumen, dan supaya format angka di layar dan di kertas
    /// memakai satu sumber yang sama (<c>IdmTeks</c>).
    /// </summary>
    public class LaporanIdmData
    {
        /// <summary>Judul besar di halaman pertama.</summary>
        public string Judul { get; set; } = "LAPORAN INDEKS DESA MEMBANGUN (IDM)";

        /// <summary>Data desa untuk kop, blok tanda tangan, dan nama berkas.</summary>
        public DesaData Desa { get; set; } = new DesaData();

        /// <summary>Nama desa sebagaimana dikirim pusat (cadangan: Pengaturan Surat).</summary>
        public string NamaDesa { get; set; } = string.Empty;

        /// <summary>Kecamatan · kabupaten · provinsi, dipakai pada baris identitas.</summary>
        public string Wilayah { get; set; } = string.Empty;

        /// <summary>Kode desa 10 digit yang dipakai situs pusat (boleh kosong).</summary>
        public string KodeDesa { get; set; } = string.Empty;

        /// <summary>Tahun penilaian yang dicetak; 0 berarti belum ada data.</summary>
        public int Tahun { get; set; }

        public string Skor { get; set; } = "—";
        public string Status { get; set; } = "—";
        public string Iks { get; set; } = "—";
        public string Ike { get; set; } = "—";
        public string Ikl { get; set; } = "—";
        public string Target { get; set; } = "—";
        public string SkorMinimal { get; set; } = "—";

        /// <summary>Kalimat jarak ke status berikutnya (dari pusat, tanpa hitungan aplikasi).</summary>
        public string CatatanTarget { get; set; } = string.Empty;

        /// <summary>"Sumber: idm.kemendesa.go.id · dimuat …" — sama dengan yang tampil di layar.</summary>
        public string Sumber { get; set; } = string.Empty;

        /// <summary>Tanggal laporan dibuat (dipakai pada blok tanda tangan &amp; nama berkas).</summary>
        public DateTime TanggalCetak { get; set; }

        /// <summary>Tahun-tahun lain yang datanya diterbitkan pusat, terbaru dulu.</summary>
        public IReadOnlyList<TahunLaporanIdm> Riwayat { get; set; } = Array.Empty<TahunLaporanIdm>();

        /// <summary>Rincian indikator apa adanya dari pusat, termasuk baris subtotal.</summary>
        public IReadOnlyList<BarisLaporanIdm> Indikator { get; set; } = Array.Empty<BarisLaporanIdm>();

        /// <summary>True bila laporan memuat penilaian (bukan halaman "belum ada data").</summary>
        public bool AdaData => Tahun > 0;

        /// <summary>
        /// Nama berkas tanpa ekstensi: "Laporan_IDM_Sumberjaya_2024_20261018".
        /// Karakter yang tidak sah untuk nama berkas sudah dibuang.
        /// </summary>
        public string NamaBerkas
        {
            get
            {
                string desa = NamaBerkasAman(string.IsNullOrWhiteSpace(NamaDesa) ? "Desa" : NamaDesa);
                string tahun = Tahun > 0 ? Tahun.ToString(System.Globalization.CultureInfo.InvariantCulture) : "tanpa-tahun";
                return $"Laporan_IDM_{desa}_{tahun}_{TanggalCetak:yyyyMMdd}";
            }
        }

        private static string NamaBerkasAman(string teks)
        {
            var sah = new List<char>(teks.Length);
            foreach (char c in teks.Trim())
            {
                sah.Add(char.IsLetterOrDigit(c) ? c : '_');
            }

            // Beberapa garis bawah berurutan dirapatkan supaya nama berkas rapi.
            string hasil = new string(sah.ToArray());
            while (hasil.Contains("__", StringComparison.Ordinal))
            {
                hasil = hasil.Replace("__", "_", StringComparison.Ordinal);
            }

            return hasil.Trim('_');
        }
    }
}
