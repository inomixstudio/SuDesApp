using System;
using System.Collections.Generic;
using System.Linq;

namespace SuDesApp.Data.Models
{
    /// Isian satu dokumen SK yang dikirim ke generator PDF. Bila sebuah nilai
    /// kosong, generator mencetak garis titik-titik (untuk contoh yang belum
    /// terisi orang).
    /// </summary>
    public sealed class SkPerangkatIsi
    {
        public SkJenisPerangkat Jenis { get; init; } = SkJenisPerangkat.Pengangkatan;

        /// <summary>Kelompok jabatan (lihat <see cref="JabatanPerangkat.UrutanKelompok"/>).</summary>
        public string Kelompok { get; init; } = string.Empty;

        /// <summary>Nomor SK; kosong → digariskan.</summary>
        public string Nomor { get; init; } = string.Empty;

        /// <summary>Tanggal penandatanganan SK; null → hari ini.</summary>
        public DateTime? Tanggal { get; init; }

        public string Nama { get; init; } = string.Empty;
        public string NIK { get; init; } = string.Empty;
        public string Jabatan { get; init; } = string.Empty;

        /// <summary>Keterangan wilayah, mis. "RT 01/RW 02" atau dusun; boleh kosong.</summary>
        public string Wilayah { get; init; } = string.Empty;

        public DateTime? Mulai { get; init; }
        public DateTime? Selesai { get; init; }

        /// <summary>Alasan pemberhentian, mis. "karena masa jabatan telah berakhir".</summary>
        public string Alasan { get; init; } = string.Empty;

        /// <summary>
        /// Penanda diktum pertama. Contoh SK tidak seragam: Posyandu memakai
        /// "KESATU", sedangkan Linmas memakai "PERTAMA". Kosong memakai bawaan
        /// template kelompok, lalu "KESATU".
        /// </summary>
        public string PenandaDiktum { get; init; } = string.Empty;

        /// <summary>
        /// Butir blok "Memperhatikan". Kosong memakai butir bawaan kelompok
        /// (hasil kesepakatan/musyawarah), lalu bawaan umum katalog.
        /// </summary>
        public IReadOnlyList<string> Memperhatikan { get; init; } = Array.Empty<string>();

        /// <summary>
        /// Isi diktum ketiga. Kosong memakai kalimat bawaan katalog. Diktum ini
        /// menampilkan "{PENANDA}", diganti penanda diktum pertama.
        /// </summary>
        public string KetentuanKetiga { get; init; } = string.Empty;

        /// <summary>
        /// Penerima tembusan, satu butir per baris. Kosong memakai daftar bawaan
        /// katalog; butir yang dikosongkan operator berarti SK itu memang tidak
        /// punya tembusan sama sekali.
        /// </summary>
        public IReadOnlyList<string>? Tembusan { get; init; }

        /// <summary>
        /// Daftar nama pada lampiran SK. Kosong berarti SK satu orang (diktum
        /// KESATU menyebut namanya langsung); berisi berarti SK banyak orang
        /// sekaligus — diktum KESATU merujuk lampiran dan tabelnya dicetak di
        /// halaman berikutnya, dikelompokkan per unit (mis. Posyandu).
        /// </summary>
        public IReadOnlyList<BarisLampiranSk> Lampiran { get; init; } = Array.Empty<BarisLampiranSk>();

        /// <summary>Benar bila SK memakai diktum lampiran (banyak orang).</summary>
        public bool AdaLampiran => Lampiran.Count > 0;

        /// <summary>True bila ini contoh kosong (tanpa orang tertentu).</summary>
        public bool Contoh { get; init; }
    }

    /// <summary>
    /// Template SK untuk satu kelompok jabatan: judul, kalimat keputusan,
    /// serta daftar pertimbangan dan dasar hukum. Teks memuat placeholder
    /// {JABATAN}, {NAMA}, {NIK}, {DESA}, {WILAYAH}, {MULAI}, {SELESAI},
    /// {TANGGAL} yang diganti generator.
    ///
    /// Dasar hukum disusun dari regulasi yang berlaku (UU, PP, dan Permendagri
    /// pengangkatan/pemberhentian perangkat desa) mengikuti format naskah dinas
    /// Peraturan Bupati tentang Tata Naskah Dinas Desa. Operator tetap disarankan
    /// menyesuaikan dengan Perbup/Perdes setempat sebelum menetapkan.
    /// </summary>
    /// <summary>
    /// Kategori sumber SK per kelompok jabatan, mengikuti praktik nyata penerbitan SK:
    /// 
    /// - <b>KepalaDesa</b>: SK per orang yang ditandatangani Kepala Desa (perangkat desa,
    ///   kadus, RT/RW, kader, dll. — perilaku lama aplikasi).
    /// - <b>KepalaDesaBersama</b>: satu SK Kepala Desa untuk banyak orang sekaligus
    ///   dengan lampiran daftar nama (Linmas, kader Posyandu/Pokjanal) — bentuk SK-nya
    ///   tetap milik Kepala Desa.
    /// - <b>Bupati</b>: SK diterbitkan Bupati (SK Kepala Desa, SK anggota BPD).
    ///   Aplikasi tidak menerbitkan SK ini; yang dibutuhkan adalah mengarsipkan berkas
    ///   PDF-nya dan menautkannya dari data perangkat.
    /// </summary>
    public enum SumberSkPerangkat
    {
        KepalaDesa = 0,
        KepalaDesaBersama = 1,
        Bupati = 2
    }

}
