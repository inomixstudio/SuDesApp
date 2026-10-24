using System;
using System.Collections.Generic;
using System.IO;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Satu naskah contoh Perdes bawaan aplikasi. Contoh hanyalah titik awal:
    /// tombolnya membuka formulir Buku Perdes yang bisa disunting dan disimpan
    /// menurut skema arsip Perdes (nomor, tanggal, tentang, keterangan, dan
    /// berkas lampiran) — bukan membuka berkas Word di luar aplikasi. Saat
    /// dibuka, SALINAN contoh diisi otomatis dengan identitas desa dan kabupaten
    /// dari Pengaturan Surat oleh <see cref="SuDesApp.Utilities.TemplatePerdesFiller"/>,
    /// sehingga naskah yang terbit sudah sesuai desa pengguna.
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
                "Contoh-Perdes-PADes-2023.docx",
                "5",
                "Pungutan-Pungutan Desa Tahun Anggaran 2023",
                "Contoh Perdes PADes — pungutan desa dan tata cara pemungutannya. Sesuaikan nomor, tanggal, dan isi dengan kondisi desa."),
            new(
                "Perdes Kewenangan Berdasarkan Hak Asal Usul",
                "Contoh-Perdes-Kewenangan-Asal-Usul.docx",
                "1",
                "Kewenangan Berdasarkan Hak Asal-Usul",
                "Contoh Perdes kewenangan berdasarkan hak asal-usul. Pastikan dasar hukum daerah berlaku sebelum ditetapkan."),
            new(
                "Perdes Kewenangan Lokal Skala Desa",
                "Contoh-Perdes-Kewenangan-Lokal-Skala-Desa.docx",
                "2",
                "Kewenangan Lokal Berskala Desa",
                "Contoh Perdes kewenangan lokal berskala desa. Periksa daftar kewenangan daerah sebelum disimpan."),
            new(
                "Perdes Kewenangan yang Ditugaskan",
                "Contoh-Perdes-Kewenangan-Ditugaskan.docx",
                "3",
                "Kewenangan yang Ditugaskan",
                "Contoh Perdes kewenangan yang ditugaskan pemerintah atas. Sesuaikan dengan penugasan yang benar-benar diterima desa."),
            new(
                "Perdes tentang APBDes Tahun Anggaran 2026",
                "Contoh-Perdes-APBDes.docx",
                "1",
                "Anggaran Pendapatan dan Belanja Desa Tahun Anggaran 2026",
                "Contoh Perdes APBDes — struktur pendapatan, belanja, dan pembiayaan desa. Ganti seluruh angka rincian dengan hasil musyawarah desa Anda sebelum menyimpan."),
            new(
                "Perdes tentang Badan Permusyawaratan Desa (BPD)",
                "Contoh-Perdes-BPD.docx",
                "2",
                "Badan Permusyawaratan Desa",
                "Contoh Perdes BPD — kedudukan, susunan, tugas, dan wewenang BPD. Periksa ketentuan pembentukan BPD yang berlaku di daerah Anda sebelum ditetapkan.")
        };
    }
}
