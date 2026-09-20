using System;
using System.IO;
using System.Linq;

namespace SuDesApp.Data.Models
{
    /// <summary>
    /// Kebijakan berkas lampiran buku agenda Surat Masuk/Keluar.
    ///
    /// Berbeda dengan arsip Keputusan (yang boleh Word karena isinya dibaca
    /// otomatis), agenda surat masuk/keluar hanya menyimpan berkas siap pakai:
    /// <b>PDF dan gambar</b> — hasil pindai/tangkapan surat. Isi berkas tidak
    /// pernah dibaca aplikasi, hanya disalin ke folder arsip saat data disimpan.
    /// Satu tempat ini dipakai repository, formulir input, dan pengujian.
    /// </summary>
    public static class LampiranArsipSurat
    {
        /// <summary>Ekstensi yang diterima (huruf kecil, termasuk titik).</summary>
        public static readonly string[] EkstensiDidukung =
        {
            ".pdf",
            ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".tif", ".tiff"
        };

        /// <summary>Filter dialog pemilih berkas.</summary>
        public const string FilterDialog =
            "Berkas PDF / Gambar|*.pdf;*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp;*.tif;*.tiff" +
            "|PDF (*.pdf)|*.pdf" +
            "|Gambar (*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp;*.tif;*.tiff)|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp;*.tif;*.tiff" +
            "|Semua berkas (*.*)|*.*";

        /// <summary>Keterangan singkat untuk pengguna.</summary>
        public const string Keterangan = "Berkas PDF atau gambar (hasil pindai/tangkapan surat)";

        /// <summary>Ekstensi berkas yang didukung (huruf kecil), atau kosong bila bukan PDF/gambar.</summary>
        public static string EkstensiTerdukung(string? pathOrName)
        {
            if (string.IsNullOrWhiteSpace(pathOrName)) return string.Empty;
            string ext;
            try
            {
                ext = Path.GetExtension(pathOrName.Trim());
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
            if (string.IsNullOrEmpty(ext)) return string.Empty;
            ext = ext.ToLowerInvariant();
            return EkstensiDidukung.Contains(ext) ? ext : string.Empty;
        }

        /// <summary>Berkas (path atau nama) merupakan PDF/gambar yang boleh disimpan.</summary>
        public static bool Didukung(string? pathOrName) => EkstensiTerdukung(pathOrName).Length > 0;

        /// <summary>Berkas PDF (bukan gambar).</summary>
        public static bool Pdf(string? pathOrName) =>
            string.Equals(EkstensiTerdukung(pathOrName), ".pdf", StringComparison.OrdinalIgnoreCase);

        /// <summary>Berkas gambar (bisa dilihat langsung di penampil bawaan Windows).</summary>
        public static bool Gambar(string? pathOrName)
        {
            string ext = EkstensiTerdukung(pathOrName);
            return ext.Length > 0 && !string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Label pendek untuk chip di daftar (mis. PDF, JPG), atau kosong.</summary>
        public static string Label(string? pathOrName)
        {
            string ext = EkstensiTerdukung(pathOrName);
            return ext.Length == 0 ? string.Empty : ext.TrimStart('.').ToUpperInvariant();
        }

        /// <summary>Pesan baku saat berkas yang dipilih bukan PDF/gambar.</summary>
        public static string PesanTidakDidukung(string? pathOrName)
        {
            string nama = string.IsNullOrWhiteSpace(pathOrName)
                ? "(tanpa nama)"
                : Path.GetFileName(pathOrName.Trim());
            string ext = string.IsNullOrWhiteSpace(pathOrName) ? "" : Path.GetExtension(pathOrName.Trim());
            return $"Berkas '{nama}'{(string.IsNullOrEmpty(ext) ? "" : $" ({ext})")} tidak dapat disimpan.\n\n" +
                   "Buku agenda surat masuk/keluar hanya menyimpan berkas PDF atau gambar " +
                   "(*.pdf, *.jpg, *.jpeg, *.png, *.bmp, *.gif, *.webp, *.tif, *.tiff).\n\n" +
                   "Ubah dulu berkasnya ke PDF atau gambar, lalu lampirkan kembali.";
        }
    }
}
