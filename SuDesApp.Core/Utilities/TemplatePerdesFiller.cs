// TemplatePerdesFiller.cs
//
// Mengisi SALINAN contoh Perdes (.docx) dengan identitas desa dan kabupaten
// dari Pengaturan Surat, supaya naskah yang dibuka dari menu SOTK Desa langsung
// siap dipakai oleh desa pengguna. Berkas contoh bawaan memuat nama desa contoh
// (Sumberjaya), kecamatan (Tempuran), dan kabupaten (Karawang) yang tertanam di
// dalam teksnya — termasuk pada gelar "Bupati" dan kutipan peraturan daerah —
// sehingga tanpa pengisian otomatis naskah yang terbit tidak sesuai desa yang
// memakai aplikasi.
//
// Berkas ASLI tidak pernah diubah: pengisian selalu menulis berkas baru di
// folder sementara, dan salinan itulah yang dilampirkan ke formulir Buku Perdes.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using SuDesApp.Data.Models;
using SuDesApp.GeneratorPdf;

namespace SuDesApp.Utilities
{
    /// <summary>Pengisi contoh Perdes dengan identitas desa/kabupaten pengaturan aplikasi.</summary>
    public static class TemplatePerdesFiller
    {
        /// <summary>
        /// Salin <paramref name="jalurSumber"/> ke <paramref name="folderKeluar"/> sambil
        /// mengganti teks identitas contoh dengan data dari
        /// <paramref name="desaSumber"/> (hasil Pengaturan Surat, sudah dibersihkan
        /// lewat <see cref="KopSurat.DesaBersih"/>). Mengembalikan jalur berkas hasil
        /// pengisian.
        ///
        /// Penggantian berlaku pada seluruh bagian XML di dalam folder <c>word/</c>
        /// (isi utama, kepala, dan kaki halaman). Urutan penggantian penting: frasa
        /// komposit ("Kabupaten Karawang", "Bupati Karawang") diselesaikan lebih dulu
        /// sebelum kata tunggalnya, supaya kabupaten tujuan tidak ikut menimpa frasa.
        /// </summary>
        /// <exception cref="FileNotFoundException">Berkas contoh tidak ditemukan.</exception>
        /// <exception cref="InvalidDataException">Berkas bukan dokumen Word yang dikenali.</exception>
        /// <exception cref="InvalidOperationException">Nama desa/kabupaten belum diisi di Pengaturan Surat.</exception>
        public static string Isi(string jalurSumber, DesaData desaSumber, string folderKeluar)
            => Isi(jalurSumber, desaSumber, folderKeluar, isian: null);

        /// <summary>
        /// Isian naskah dari formulir Buku Perdes yang ikut mengisi SALINAN
        /// contoh: nomor &amp; tahun pada kepala surat, tanggal penetapan dan
        /// pengundangan pada blok penutup, serta tahun anggaran (APBDes/PADes).
        /// Dengan isian ini naskah yang terbit konsisten dengan form, bukan
        /// lagi contoh yang menampilkan nomor/tanggal lama.
        /// </summary>
        public sealed record PerdesTemplateIsian(string Nomor, DateTime Tanggal);

        public static string Isi(string jalurSumber, DesaData desaSumber, string folderKeluar, PerdesTemplateIsian? isian)
        {
            if (string.IsNullOrWhiteSpace(jalurSumber))
                throw new ArgumentException("Jalur berkas contoh Perdes tidak boleh kosong.", nameof(jalurSumber));
            if (!File.Exists(jalurSumber))
                throw new FileNotFoundException("Berkas contoh Perdes tidak ditemukan.", jalurSumber);
            if (string.IsNullOrWhiteSpace(folderKeluar))
                throw new ArgumentException("Folder keluaran tidak boleh kosong.", nameof(folderKeluar));

            var desa = KopSurat.DesaBersih(desaSumber);
            string namaDesa = (desa.NamaDesa ?? string.Empty).Trim();
            string kecamatan = (desa.Kecamatan ?? string.Empty).Trim();
            string kabupaten = (desa.Kabupaten ?? string.Empty).Trim();

            if (namaDesa.Length == 0 || kabupaten.Length == 0)
            {
                throw new InvalidOperationException(
                    "Nama desa atau kabupaten belum diisi pada Pengaturan Surat, " +
                    "sehingga contoh Perdes belum bisa diisi identitasnya.");
            }

            string jenis = WilayahLabel.Jenis(desa.JenisKabupaten);
            string identitasWilayah = WilayahLabel.IdentitasWilayah(jenis, kabupaten);

            var pasangan = new List<(string Lama, string Baru)>();
            void Tambah(string lama, string? baru)
            {
                if (!string.IsNullOrWhiteSpace(baru))
                    pasangan.Add((lama, Xl(baru)));
            }

            // Frasa komposit lebih dulu, lalu kata tunggal per bagian identitas.
            Tambah("Kabupaten Karawang", identitasWilayah);
            Tambah("KABUPATEN KARAWANG", identitasWilayah.ToUpperInvariant());
            Tambah("Bupati Karawang", $"{WilayahLabel.Kepala(jenis)} {kabupaten}");
            Tambah("Karawang", kabupaten);
            Tambah("KARAWANG", kabupaten.ToUpperInvariant());
            Tambah("Sumberjaya", namaDesa);
            Tambah("SUMBERJAYA", namaDesa.ToUpperInvariant());
            Tambah("Tempuran", kecamatan);
            Tambah("TEMPURAN", kecamatan.ToUpperInvariant());

            return SalinDanUbahXml(jalurSumber, folderKeluar, xml =>
            {
                foreach (var (lama, baru) in pasangan)
                    xml = xml.Replace(lama, baru);

                return isian == null ? xml : IsiIsianNaskah(xml, isian, kabupaten);
            });
        }

        /// <summary>
        /// Isi ulang SALINAN contoh Perdes (identitas desa sudah terisi) dengan
        /// nomor &amp; tahun TERBARU dari formulir Buku Perdes. Dipanggil saat
        /// pengguna menekan Simpan, supaya baris "NOMOR x TAHUN y" dan tanggal
        /// penutup pada naskah selalu cocok dengan isian yang disimpan — walau
        /// nomor atau tanggal diubah setelah contoh dibuka. Aturannya idempoten:
        /// hasil isi ulang bisa diisi ulang lagi tanpa merusak isi naskah, dan
        /// berkas sumber tidak pernah diubah.
        /// </summary>
        /// <exception cref="FileNotFoundException">Berkas sumber tidak ditemukan.</exception>
        /// <exception cref="InvalidDataException">Berkas bukan dokumen Word yang dikenali.</exception>
        public static string IsiUlangNaskahPerdes(string jalurSumber, string folderKeluar, PerdesTemplateIsian isian)
        {
            if (isian == null)
                throw new ArgumentNullException(nameof(isian));

            // Kabupaten sengaja kosong: aturan pelepasan nomor Perda kabupaten
            // hanya berlaku pada pengisian awal, saat identitas kabupaten baru
            // diketahui dari Pengaturan Surat.
            return SalinDanUbahXml(jalurSumber, folderKeluar,
                xml => IsiIsianNaskah(xml, isian, kabupaten: string.Empty));
        }

        /// <summary>
        /// Salin seluruh isi arsip ZIP sumber ke berkas baru di
        /// <paramref name="folderKeluar"/>; setiap berkas teks XML di folder
        /// <c>word/</c> diolah lewat <paramref name="ubahXml"/> (null = tanpa
        /// ubahan). Berkas sumber tidak pernah diubah.
        /// </summary>
        private static string SalinDanUbahXml(string jalurSumber, string folderKeluar, Func<string, string>? ubahXml)
        {
            if (string.IsNullOrWhiteSpace(jalurSumber))
                throw new ArgumentException("Jalur berkas contoh Perdes tidak boleh kosong.", nameof(jalurSumber));
            if (!File.Exists(jalurSumber))
                throw new FileNotFoundException("Berkas contoh Perdes tidak ditemukan.", jalurSumber);
            if (string.IsNullOrWhiteSpace(folderKeluar))
                throw new ArgumentException("Folder keluaran tidak boleh kosong.", nameof(folderKeluar));

            using var sumber = File.OpenRead(jalurSumber);
            using var zipSumber = new ZipArchive(sumber, ZipArchiveMode.Read);

            if (zipSumber.GetEntry("word/document.xml") == null)
                throw new InvalidDataException(
                    "Berkas bukan dokumen contoh Perdes yang dikenali (word/document.xml tidak ada).");

            Directory.CreateDirectory(folderKeluar);
            string namaKeluar =
                $"{Path.GetFileNameWithoutExtension(jalurSumber)}_{DateTime.Now:yyyyMMddHHmmssfff}" +
                Path.GetExtension(jalurSumber);
            string jalurKeluar = Path.Combine(folderKeluar, namaKeluar);

            using (var tujuan = File.Create(jalurKeluar))
            using (var zipTujuan = new ZipArchive(tujuan, ZipArchiveMode.Create))
            {
                foreach (var entri in zipSumber.Entries)
                {
                    var entriBaru = zipTujuan.CreateEntry(entri.FullName, CompressionLevel.Optimal);
                    bool xmlTeks = entri.FullName.StartsWith("word/", StringComparison.OrdinalIgnoreCase)
                                && entri.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);

                    if (xmlTeks)
                    {
                        string xml;
                        using (var baca = new StreamReader(entri.Open()))
                            xml = baca.ReadToEnd();

                        if (ubahXml != null)
                            xml = ubahXml(xml);

                        using var tulis = new StreamWriter(entriBaru.Open());
                        tulis.Write(xml);
                    }
                    else
                    {
                        using var asal = entri.Open();
                        using var hasil = entriBaru.Open();
                        asal.CopyTo(hasil);
                    }
                }
            }

            return jalurKeluar;
        }

        /// <summary>
        /// Isi naskah dari formulir Buku Perdes ke dalam XML yang identitas
        /// desanya sudah terganti. Urutan penggantian: kop nomor/tahun, tahun
        /// anggaran, lalu tanggal pada blok penutup naskah. Pola tanggal sengaja
        /// disandingkan dengan kata “Ditetapkan di…”, “Diundangkan…”, atau
        /// “Pada tanggal:” sehingga tanggal riwayat di badan naskah (mis. hasil
        /// musyawarah desa) TIDAK ikut terganti.
        /// </summary>
        private static string IsiIsianNaskah(string xml, PerdesTemplateIsian isian, string kabupaten)
        {
            string tgl = isian.Tanggal.ToString("d MMMM yyyy", new CultureInfo("id-ID"));
            int tahun = isian.Tanggal.Year;
            string nomor = (isian.Nomor ?? string.Empty).Trim();
            if (nomor.Length == 0) nomor = ".........";

            // Kepala surat: “NOMOR 5 TAHUN 2023” → nomor & tahun dari form.
            xml = Regex.Replace(xml, @"NOMOR \d+ TAHUN \d{4}", $"NOMOR {nomor} TAHUN {tahun}");

            // Tahun anggaran (APBDes/PADes) mengikuti tahun tanggal perdes.
            xml = Regex.Replace(xml, @"TAHUN ANGGARAN \d{4}", $"TAHUN ANGGARAN {tahun}");

            // Tanggal penetapan & pengundangan pada blok penutup naskah.
            xml = Regex.Replace(xml, @"(?<=Diundangkan pada tanggal )\d{1,2} \w+ \d{4}", tgl);
            xml = Regex.Replace(xml, @"(?<=Pada tanggal: )\d{1,2} \w+ \d{4}", tgl);
            xml = Regex.Replace(xml, @"(?<=Ditetapkan di[^\n]{0,80}?pada tanggal )\d{1,2} \w+ \d{4}", tgl);

            // Nomor Perda kabupaten soal Desa khas tiap kabupaten — angkanya
            // dilepas supaya naskah tidak salah menyebut perda daerah lain;
            // rujukan lengkap yang berlaku diisi pengguna pada isian Rujukan
            // Daerah halaman SOTK.
            string kab = Xl(kabupaten);
            xml = Regex.Replace(xml,
                $@"Peraturan Daerah Kabupaten {Regex.Escape(kab)} Nomor \d+ Tahun \d{{4}} tentang Desa",
                $"Peraturan Daerah Kabupaten {kab} tentang Desa");

            return xml;
        }

        /// <summary>
        /// Amankan nilai untuk disisipkan ke dalam XML: nama desa boleh memuat
        /// karakter khusus (&amp;, &lt;) tanpa merusak berkas Word.
        /// </summary>
        private static string Xl(string teks)
            => teks.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
