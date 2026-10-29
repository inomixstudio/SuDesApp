using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;

namespace SuDesApp.GeneratorPdf
{
    /// <summary>
    /// Membaca naskah contoh Perdes dari folder <c>Templates/Perdes</c>. Contoh
    /// disimpan sebagai data naskah (JSON), bukan dokumen Word, sehingga isinya
    /// dapat langsung dibuka di penyunting halaman dan dirender generator PDF.
    /// </summary>
    public static class NaskahPerdesContoh
    {
        public static string FolderBawaan =>
            Path.Combine(AppContext.BaseDirectory, "Templates", "Perdes");

        /// <summary>
        /// Muat satu naskah contoh lalu ganti identitas desa/kabupaten contoh
        /// dengan data Pengaturan Surat. Berkas contoh tidak pernah diubah.
        /// </summary>
        /// <exception cref="ArgumentException">Nama berkas kosong atau memuat jalur.</exception>
        /// <exception cref="FileNotFoundException">Berkas contoh tidak ditemukan.</exception>
        /// <exception cref="InvalidDataException">Isi berkas bukan naskah yang dikenali.</exception>
        public static NaskahPerdes Muat(string namaBerkas, DesaData? desa, string? folder = null)
        {
            if (string.IsNullOrWhiteSpace(namaBerkas) ||
                !string.Equals(Path.GetFileName(namaBerkas), namaBerkas, StringComparison.Ordinal))
            {
                throw new ArgumentException("Nama berkas naskah contoh tidak valid.", nameof(namaBerkas));
            }

            string jalur = Path.Combine(folder ?? FolderBawaan, namaBerkas);
            if (!File.Exists(jalur))
            {
                throw new FileNotFoundException("Naskah contoh Perdes tidak ditemukan.", jalur);
            }

            var naskah = NaskahPerdesJson.Baca(jalur);
            return TerapkanIdentitas(naskah, desa);
        }

        /// <summary>Salinan naskah dengan nama desa/kecamatan/kabupaten contoh diganti.</summary>
        public static NaskahPerdes TerapkanIdentitas(NaskahPerdes naskah, DesaData? desa)
        {
            ArgumentNullException.ThrowIfNull(naskah);

            var hasil = naskah.Salin();
            if (desa == null) return hasil;

            var bersih = KopSurat.DesaBersih(desa);
            string namaDesa = (bersih.NamaDesa ?? string.Empty).Trim();
            string kecamatan = (bersih.Kecamatan ?? string.Empty).Trim();
            string kabupaten = (bersih.Kabupaten ?? string.Empty).Trim();
            if (namaDesa.Length == 0 || kabupaten.Length == 0) return hasil;

            string identitasKabupaten = WilayahLabel.IdentitasWilayah(bersih.JenisKabupaten, kabupaten);
            var pengganti = new (string Lama, string Baru)[]
            {
                // Frasa komposit lebih dulu agar kata tunggalnya tidak menimpa lebih awal.
                ("Kabupaten Karawang", identitasKabupaten),
                ("KABUPATEN KARAWANG", identitasKabupaten.ToUpperInvariant()),
                ("Bupati Karawang", $"{WilayahLabel.Kepala(bersih.JenisKabupaten)} {kabupaten}"),
                ("KARAWANG", kabupaten.ToUpperInvariant()),
                ("Karawang", kabupaten),
                ("SUMBERJAYA", namaDesa.ToUpperInvariant()),
                ("Sumberjaya", namaDesa),
                (kecamatan.Length > 0 ? "TEMPURAN" : string.Empty,
                    kecamatan.Length > 0 ? kecamatan.ToUpperInvariant() : string.Empty),
                (kecamatan.Length > 0 ? "Tempuran" : string.Empty, kecamatan)
            };

            hasil.Judul = Ganti(hasil.Judul, pengganti);
            foreach (var halaman in hasil.Halaman)
            {
                halaman.Judul = Ganti(halaman.Judul, pengganti);
                halaman.Isi = Ganti(halaman.Isi, pengganti);
            }

            return hasil;
        }

        private static string Ganti(string teks, (string Lama, string Baru)[] pengganti)
        {
            foreach (var (lama, baru) in pengganti)
            {
                if (lama.Length == 0 || baru.Length == 0) continue;
                teks = teks.Replace(lama, baru, StringComparison.Ordinal);
            }

            return teks;
        }
    }

    /// <summary>
    /// Berkas naskah yang menyertai PDF Perdes di arsip. Nama berkas diturunkan
    /// dari PDF (<c>...pdf</c> → <c>...naskah.json</c>) supaya tidak perlu kolom
    /// database baru; menghapus data lama juga mudah menemukan berkasnya.
    /// </summary>
    public static class NaskahPerdesStore
    {
        /// <summary>Berkas sumber naskah untuk sebuah PDF arsip.</summary>
        public static string JalurSampingan(string jalurPdf)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(jalurPdf);
            return Path.ChangeExtension(jalurPdf, ".naskah.json");
        }

        /// <summary>Baca naskah sampingan; null bila berkas belum ada.</summary>
        /// <exception cref="InvalidDataException">Berkas ada tetapi isinya rusak.</exception>
        public static NaskahPerdes? Muat(string jalurPdf)
        {
            string jalur = JalurSampingan(jalurPdf);
            if (!File.Exists(jalur)) return null;
            return NaskahPerdesJson.Baca(jalur);
        }

        public static void Simpan(string jalurPdf, NaskahPerdes naskah)
        {
            ArgumentNullException.ThrowIfNull(naskah);
            string jalur = JalurSampingan(jalurPdf);
            string? folder = Path.GetDirectoryName(jalur);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            string sementara = jalur + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(sementara, NaskahPerdesJson.Tulis(naskah));
                File.Move(sementara, jalur, overwrite: true);
            }
            finally
            {
                try
                {
                    if (File.Exists(sementara)) File.Delete(sementara);
                }
                catch (IOException)
                {
                    // Jangan menutupi kegagalan penulisan naskah yang asli.
                }
                catch (UnauthorizedAccessException)
                {
                    // Jangan menutupi kegagalan penulisan naskah yang asli.
                }
            }
        }

        /// <summary>Hapus naskah sampingan bila ada. Berkas yang tidak ada diabaikan.</summary>
        public static void Hapus(string jalurPdf)
        {
            string jalur = JalurSampingan(jalurPdf);
            if (File.Exists(jalur)) File.Delete(jalur);
        }
    }

    internal static class NaskahPerdesJson
    {
        private static readonly JsonSerializerOptions Opsi = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static NaskahPerdes Baca(string jalur)
        {
            try
            {
                var naskah = JsonSerializer.Deserialize<NaskahPerdes>(File.ReadAllText(jalur), Opsi);
                if (naskah == null || naskah.Halaman == null || naskah.Halaman.Count == 0)
                {
                    throw new InvalidDataException("Berkas naskah tidak memuat satu halaman pun.");
                }

                return naskah;
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("Isi berkas naskah tidak dapat dibaca.", ex);
            }
        }

        public static string Tulis(NaskahPerdes naskah) => JsonSerializer.Serialize(naskah, Opsi);
    }
}
