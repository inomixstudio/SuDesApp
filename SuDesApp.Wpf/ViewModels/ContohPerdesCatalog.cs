using System;
using System.Collections.Generic;
using System.IO;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Satu naskah contoh Perdes bawaan aplikasi. Contoh hanyalah titik awal:
    /// tombolnya membuka formulir Buku Perdes dengan halaman naskah yang bisa
    /// disunting. Isi contoh adalah data naskah (JSON), bukan berkas Word, dan
    /// dirender generator PDF saat pratinjau maupun simpan. Nama desa/kabupaten
    /// pada teks diganti identitas Pengaturan Surat saat dimuat.
    /// </summary>
    public sealed class ContohPerdesDokumen
    {
        public ContohPerdesDokumen(
            string judul, string namaFile, string nomor, string tentang, string keterangan)
        {
            Judul = judul;
            NamaFile = namaFile;
            Nomor = nomor;
            Tentang = tentang;
            Keterangan = keterangan;
        }

        public string Judul { get; }
        public string NamaFile { get; }
        public string Nomor { get; }
        public string Tentang { get; }
        public string Keterangan { get; }

        /// <summary>Folder berkas contoh di samping aplikasi.</summary>
        public static string FolderContoh =>
            Path.Combine(AppContext.BaseDirectory, "Templates", "Perdes");

        public string JalurBerkas => Path.Combine(FolderContoh, NamaFile);

        public bool BerkasAda => File.Exists(JalurBerkas);

        /// <summary>Nama berkas naskah (JSON) yang dibuka oleh pemuat contoh.</summary>
        public string Kunci => NamaFile;
    }

    /// <summary>
    /// Katalog contoh Perdes. Daftar ini dipakai halaman PERDES di menu Surat
    /// Keputusan; setiap contoh dibuka di formulir Perdes supaya pengguna dapat
    /// mengubah isian lalu menyimpannya ke Buku Perdes.
    /// </summary>
    public static class ContohPerdesCatalog
    {
        public static IReadOnlyList<ContohPerdesDokumen> Semua { get; } = new List<ContohPerdesDokumen>
        {
            new(
                "Perdes tentang Pendapatan Asli Desa (PADes)",
                "Contoh-Perdes-PADes-2023.json",
                "5",
                "Pungutan-Pungutan Desa Tahun Anggaran 2023",
                "Contoh Perdes PADes — pungutan desa dan tata cara pemungutannya. Sesuaikan nomor, tanggal, dan isi dengan kondisi desa."),
            new(
                "Perdes Kewenangan Berdasarkan Hak Asal Usul",
                "Contoh-Perdes-Kewenangan-Asal-Usul.json",
                "1",
                "Kewenangan Berdasarkan Hak Asal-Usul",
                "Contoh Perdes kewenangan berdasarkan hak asal-usul. Pastikan dasar hukum daerah berlaku sebelum ditetapkan."),
            new(
                "Perdes Kewenangan Lokal Skala Desa",
                "Contoh-Perdes-Kewenangan-Lokal-Skala-Desa.json",
                "2",
                "Kewenangan Lokal Berskala Desa",
                "Contoh Perdes kewenangan lokal berskala desa. Periksa daftar kewenangan daerah sebelum disimpan."),
            new(
                "Perdes Kewenangan yang Ditugaskan",
                "Contoh-Perdes-Kewenangan-Ditugaskan.json",
                "3",
                "Kewenangan yang Ditugaskan",
                "Contoh Perdes kewenangan yang ditugaskan pemerintah atas. Sesuaikan dengan penugasan yang benar-benar diterima desa."),
            new(
                "Perdes tentang APBDes Tahun Anggaran 2026",
                "Contoh-Perdes-APBDes.json",
                "1",
                "Anggaran Pendapatan dan Belanja Desa Tahun Anggaran 2026",
                "Contoh Perdes APBDes — struktur pendapatan, belanja, dan pembiayaan desa. Ganti seluruh angka rincian dengan hasil musyawarah desa Anda sebelum menyimpan."),
            new(
                "Perdes tentang Badan Permusyawaratan Desa (BPD)",
                "Contoh-Perdes-BPD.json",
                "2",
                "Badan Permusyawaratan Desa",
                "Contoh Perdes BPD — kedudukan, susunan, tugas, dan wewenang BPD. Periksa ketentuan pembentukan BPD yang berlaku di daerah Anda sebelum ditetapkan.")
        };
    }
}
