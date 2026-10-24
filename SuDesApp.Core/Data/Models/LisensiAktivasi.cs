using System;

namespace SuDesApp.Data.Models
{
    /// <summary>
    /// Bukti langganan desa yang tersimpan di database lokal.
    ///
    /// PENTING: tabel ini sengaja TIDAK menyimpan status langganan sebagai
    /// angka/teks biasa (mis. "aktif" atau tanggal jatuh tempo) karena
    /// kolom seperti itu bisa disunting bebas di komputer desa untuk
    /// mendapat akses selamanya. Yang disimpan hanyalah TOKEN BERTANDATANGAN
    /// yang sudah ditandatangani server — sama dengan berkas cache
    /// <c>lisensi-cache.json</c>, hanya saja ikut tersimpan di database desa
    /// sehingga ikut terbawa saat aplikasi mencadangkan datanya.
    ///
    /// Kolom <c>KodeAktivasi</c>, <c>NamaDesa</c>, dan <c>Paket</c> hanya
    /// untuk tampilan di halaman Langganan; kebenarannya tetap diputuskan
    /// dari verifikasi tanda tangan token, bukan dari kolom-kolom ini.
    /// </summary>
    public sealed class LisensiAktivasi
    {
        /// <summary>Kode aktivasi yang diketik pengguna pada halaman Langganan.</summary>
        public string KodeAktivasi { get; set; } = string.Empty;

        /// <summary>Nama desa pemilik langganan (dari klaim token, untuk tampilan).</summary>
        public string? NamaDesa { get; set; }

        /// <summary>Nama paket langganan (dari klaim token, untuk tampilan).</summary>
        public string? Paket { get; set; }

        /// <summary>
        /// Token lisensi bertanda tangan. Satu-satunya sumber kebenaran
        /// kelayakan — status dihitung ulang setiap kali dibaca dengan
        /// memverifikasi tanda tangannya terhadap kunci publik.
        /// </summary>
        public string? Token { get; set; }

        /// <summary>Id perangkat yang terakhir memakai kode ini (dari server).</summary>
        public string? IdPerangkat { get; set; }

        /// <summary>Waktu terakhir server mengonfirmasi (isi klaim token, ISO-8601 UTC).</summary>
        public string? CekTerakhir { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }
}