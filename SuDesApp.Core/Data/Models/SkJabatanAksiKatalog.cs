using System;
using System.Collections.Generic;
using System.Linq;

namespace SuDesApp.Data.Models
{
    /// <summary>
    /// Satu kartu aksi jabatan pada panel "SK per Jabatan" di halaman Data
    /// Perangkat Desa: jabatannya, berapa orang yang memegangnya, siapa yang
    /// menerbitkan SK-nya, dan teks tombol aksinya.
    ///
    /// Kelas ini sengaja polos (tanpa notifikasi perubahan): kartu disusun ulang
    /// utuh setiap daftar dimuat, jadi angkanya selalu segar tanpa keadaan yang
    /// harus disinkronkan.
    /// </summary>
    public class JabatanAksiItem
    {
        /// <summary>Nilai jabatan (huruf kapital, nilai <see cref="JabatanPerangkat"/>).</summary>
        public string NamaJabatan { get; init; } = string.Empty;

        /// <summary>Nama jabatan siap tampil, mis. "Kaur Keuangan".</summary>
        public string NamaTampil { get; init; } = string.Empty;

        /// <summary>Kelompok jabatan — penentu template SK (lihat <see cref="SkPerangkatKatalog"/>).</summary>
        public string Kelompok { get; init; } = string.Empty;

        /// <summary>Siapa yang menerbitkan SK jabatan ini (Kepala Desa atau Bupati).</summary>
        public SumberSkPerangkat Sumber { get; init; } = SumberSkPerangkat.KepalaDesa;

        /// <summary>Kelompoknya punya template SK bawaan (LAINNYA tidak punya).</summary>
        public bool PunyaTemplate { get; init; }

        public int JumlahOrang { get; set; }
        public int JumlahAktif { get; set; }
        public int JumlahMenungguSk { get; set; }

        /// <summary>Sudah ada berkas PDF SK Bupati yang diarsipkan untuk jabatan ini.</summary>
        public bool AdaArsipBupati { get; set; }

        public bool DariBupati => Sumber == SumberSkPerangkat.Bupati;

        /// <summary>Angka ringkas di bawah nama jabatan, mis. "3 orang — 2 aktif, 1 menunggu SK".</summary>
        public string Ringkas
        {
            get
            {
                if (JumlahOrang == 0) return "Belum ada orang";
                var potongan = new List<string>(3);
                if (JumlahAktif > 0) potongan.Add($"{JumlahAktif} aktif");
                if (JumlahMenungguSk > 0) potongan.Add($"{JumlahMenungguSk} menunggu SK");
                var sisa = JumlahOrang - JumlahAktif - JumlahMenungguSk;
                if (sisa > 0) potongan.Add($"{sisa} lainnya");
                return $"{JumlahOrang} orang — " + string.Join(", ", potongan);
            }
        }

        /// <summary>Teks aksi pada kartu — sekaligus menjelaskan mode panel yang terbuka.</summary>
        public string LabelAksi => DariBupati
            ? (AdaArsipBupati ? "Arsip SK Bupati tersedia — buka" : "Arsipkan SK Bupati")
            : (PunyaTemplate ? "Buat SK & Pratinjau" : "Tanpa template SK bawaan");

        /// <summary>Tooltip kartu: apa yang terjadi kalau diklik.</summary>
        public string Keterangan => DariBupati
            ? "SK jabatan ini diterbitkan Bupati (SKD), bukan oleh Kepala Desa. Klik untuk membuka tempat " +
              "mengarsipkan berkas PDF SK Bupati beserta nomor dan tanggalnya."
            : PunyaTemplate
                ? "Klik untuk menampilkan jabatan ini pada daftar dan langsung membuka panel Surat Keputusan " +
                  "yang siap dicetak atau dilihat pratinjaunya."
                : "Kelompok jabatan ini belum punya template SK bawaan. Klik untuk melihat penjelasannya " +
                  "beserta daftar kelompok yang sudah punya template.";
    }

    /// <summary>
    /// Susunan panel aksi SK per jabatan pada halaman Data Perangkat Desa.
    ///
    /// Panel ini memisahkan akses SK menurut JABATAN (bukan kelompok): setiap
    /// jabatan struktural desa mendapat kartu berisi jumlah orangnya dan tombol
    /// aksi yang langsung membuka SK yang siap dicetak/dipratinjau. SK Kepala
    /// Desa adalah pengecualian: dokumennya diterbitkan Bupati (SKD), sehingga
    /// kartunya membuka mode arsip berkas PDF — bukan penerbitan dokumen.
    /// </summary>
    public static class SkJabatanAksiKatalog
    {
        /// <summary>
        /// Jabatan yang SK-nya diterbitkan Bupati. Pada praktik pemerintahan desa
        /// hanya Kepala Desa yang dilantik lewat SK Bupati (SKD); seluruh
        /// perangkat desa lainnya diterbitkan Kepala Desa. Anggota BPD juga dari
        /// Bupati, tetapi mereka dikelola lewat kelompok BPD, bukan panel jabatan.
        /// </summary>
        private static readonly IReadOnlyList<string> JabatanSumberBupati = new[]
        {
            JabatanPerangkat.KepalaDesa
        };

        /// <summary>
        /// Urutan jabatan pada panel, dari pimpinan sampai staf — sesuai permintaan
        /// pemelihara. LAINNYA ditutup di urutan terakhir sebagai wadah jabatan
        /// khusus desa di luar daftar.
        /// </summary>
        public static IReadOnlyList<string> UrutanJabatanPanel() => new[]
        {
            JabatanPerangkat.KepalaDesa,
            JabatanPerangkat.SekretarisDesa,
            JabatanPerangkat.KaurKeuangan,
            JabatanPerangkat.KaurPerencanaan,
            JabatanPerangkat.KaurTataUsaha,
            JabatanPerangkat.KasiPelayanan,
            JabatanPerangkat.KasiPemerintahan,
            JabatanPerangkat.KasiKesejahteraan,
            JabatanPerangkat.OperatorDesa,
            JabatanPerangkat.Staf,
            JabatanPerangkat.Lainnya
        };

        /// <summary>Siapa yang menerbitkan SK jabatan ini.</summary>
        public static SumberSkPerangkat SumberJabatan(string? jabatan) =>
            JabatanSumberBupati.Contains(JabatanPerangkat.Normalisasi(jabatan), StringComparer.Ordinal)
                ? SumberSkPerangkat.Bupati
                : SumberSkPerangkat.KepalaDesa;

        /// <summary>
        /// Susun kartu aksi per jabatan dari seluruh orang perangkat desa. Daftar
        /// orang boleh kosong: kartunya tetap dibuat supaya operator tetap bisa
        /// membuka panel SK per jabatan pada pemasangan baru.
        /// </summary>
        public static IReadOnlyList<JabatanAksiItem> Susun(
            IEnumerable<PerangkatDesa>? orang,
            IReadOnlyList<string>? urutan = null)
        {
            var daftar = (orang ?? Enumerable.Empty<PerangkatDesa>()).ToList();
            var urutanJabatan = urutan ?? UrutanJabatanPanel();

            var hasil = new List<JabatanAksiItem>(urutanJabatan.Count);
            foreach (var jabatan in urutanJabatan)
            {
                var normal = JabatanPerangkat.Normalisasi(jabatan);
                var kelompok = JabatanPerangkat.Kelompok(normal);
                var anggota = daftar
                    .Where(p => string.Equals(p.JabatanTampil, normal, StringComparison.Ordinal))
                    .ToList();

                hasil.Add(new JabatanAksiItem
                {
                    NamaJabatan = normal,
                    NamaTampil = NamaTampil(normal),
                    Kelompok = kelompok,
                    Sumber = SumberJabatan(normal),
                    PunyaTemplate = SkPerangkatKatalog.Ada(kelompok),
                    JumlahOrang = anggota.Count,
                    JumlahAktif = anggota.Count(p => p.StatusTampil == StatusPerangkat.Aktif),
                    JumlahMenungguSk = anggota.Count(p => p.StatusTampil == StatusPerangkat.MenungguSK),
                    AdaArsipBupati = anggota.Any(p => !string.IsNullOrWhiteSpace(p.BerkasSK))
                });
            }

            return hasil;
        }

        /// <summary>"KAUR KEUANGAN" → "Kaur Keuangan" — kapital semua terlalu berat untuk kartu.</summary>
        private static string NamaTampil(string jabatanKapital) =>
            string.Join(" ", jabatanKapital
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(kata => char.ToUpperInvariant(kata[0]) + kata[1..].ToLowerInvariant()));
    }
}
