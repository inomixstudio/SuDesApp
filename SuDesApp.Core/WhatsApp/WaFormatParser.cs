using SuDesApp.Data.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SuDesApp.WhatsApp
{
    /// <summary>Hasil parsing pesan berformat dari warga.</summary>
    public class ParsedWaFormat
    {
        public string? NamaJenis { get; set; }
        public string? NIK { get; set; }
        public Dictionary<string, string> Fields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Warnings { get; } = new();
    }

    /// <summary>Satu kolom format pesan WhatsApp yang meniru field form generator.</summary>
    public sealed class WaFieldDef
    {
        public string Label { get; }
        public string Key { get; }
        public bool Required { get; }
        public string? Contoh { get; }

        public WaFieldDef(string label, string key, bool required, string? contoh = null)
        {
            Label = label;
            Key = key;
            Required = required;
            Contoh = contoh;
        }
    }

    /// <summary>
    /// Mem-parse pesan WhatsApp warga yang mengikuti format:
    ///
    ///     SKTM
    ///     NIK: 3273010101010001
    ///     Keperluan: biaya pengobatan
    ///
    /// serta format data pribadi untuk warga yang belum terdaftar:
    ///
    ///     NIK: ...
    ///     Nama: ...
    ///     Alamat: ...
    ///     TTL: Bandung, 01-01-1990
    ///     JK: L/P
    ///     Agama: Islam
    ///     Status: Kawin
    ///     Pekerjaan: Petani
    /// </summary>
    public static class WaFormatParser
    {
        // Jenis surat yang bisa diproses online via WhatsApp.
        public static readonly IReadOnlyList<(string[] KataKunci, string NamaJenis)> KatalogSurat = new (string[], string)[]
        {
            (new[] { "sktm", "tidak mampu", "kurang mampu" }, SuratConstants.SKTM),
            (new[] { "skd umum", "surat keterangan umum", "skd" }, SuratConstants.SKD_UMUM),
            (new[] { "domisili warga", "domisili" }, SuratConstants.DOMISILI_WARGA),
            (new[] { "pengantar skck", "skck" }, SuratConstants.PENGANTAR_SKCK),
            (new[] { "keterangan usaha", "surat usaha", "sku" }, SuratConstants.SKU),
            (new[] { "izin orang tua", "izin suami", "izin ortu", "izin" }, SuratConstants.IZIN_ORTU),
            (new[] { "instansi", "lembaga" }, SuratConstants.INSTANSI)
        };

        /// <summary>
        /// Kata kunci jenis surat yang TIDAK bisa diproses online (butuh isi
        /// banyak data / datang ke kantor). Ikut dideteksi supaya warga menerima
        /// jawaban "silakan datang ke kantor", BUKAN jawaban "jenis surat tidak
        /// dikenali" atau — lebih buruk — diproses sebagai surat lain. Contoh:
        /// tanpa daftar ini, "IZIN TINGGAL" cocok dengan kata kunci "izin" milik
        /// IZIN ORTU sehingga permohonan warga salah jenis.
        /// </summary>
        private static readonly IReadOnlyList<(string[] KataKunci, string NamaJenis)> KatalogOffline = new (string[], string)[]
        {
            (new[] { "keterangan kematian", "surat kematian", "kematian" }, SuratConstants.KEMATIAN),
            (new[] { "garapan sawah", "garapan" }, SuratConstants.GARAPAN_SAWAH),
            (new[] { "beda nama", "bedanama", "beda data", "perbedaan data" }, SuratConstants.BEDANAMA),
            (new[] { "kenal lahir", "kenallahir" }, SuratConstants.KENAL_LAHIR),
            (new[] { "ahli waris" }, SuratConstants.AHLI_WARIS),
            (new[] { "izin tinggal", "ijin tinggal", "izin tinggal terbatas" }, SuratConstants.IJIN_TINGGAL),
            (new[] { "ntcr n1", "ntcr 1", "formulir n1" }, SuratConstants.NTCR_N1),
            (new[] { "ntcr n2", "ntcr 2", "formulir n2" }, SuratConstants.NTCR_N2),
            (new[] { "ntcr n3", "ntcr 3", "formulir n3" }, SuratConstants.NTCR_N3),
            (new[] { "ntcr n4", "ntcr 4", "formulir n4" }, SuratConstants.NTCR_N4),
            (new[] { "ntcr n5", "ntcr 5", "formulir n5" }, SuratConstants.NTCR_N5),
            (new[] { "ntcr n6", "ntcr 6", "formulir n6" }, SuratConstants.NTCR_N6),
            // N1..N6 saja: Model N7 sudah dihapus karena diterbitkan KUA.
            (new[] { "ntcr", "surat pernikahan", "persyaratan pernikahan" }, SuratConstants.NTCR_N1)
        };

        // Jenis surat yang TIDAK bisa online pada versi ini (butuh isi banyak
        // data / datang ke kantor).
        private static readonly string[] OfflineOnly =
        {
            SuratConstants.KEMATIAN,
            SuratConstants.GARAPAN_SAWAH,
            SuratConstants.BEDANAMA,
            SuratConstants.KENAL_LAHIR,
            SuratConstants.AHLI_WARIS,
            SuratConstants.IJIN_TINGGAL,
            SuratConstants.NTCR_N1,
            SuratConstants.NTCR_N2,
            SuratConstants.NTCR_N3,
            SuratConstants.NTCR_N4,
            SuratConstants.NTCR_N5,
            SuratConstants.NTCR_N6
        };

        /// <summary>
        /// Menu utama. Format pengisian per jenis surat mengikuti field pada
        /// generator (form) masing-masing surat, jadi warga harus melihat
        /// contoh spesifik per jenis melalui perintah FORMAT &lt;jenis&gt;.
        /// </summary>
        public static string InstruksiFormat
        {
            get
            {
                var daftar = string.Join("\n", KatalogSurat.Select(k => "• " + TampilanJenis(k.NamaJenis)));
                return "🏛️ LAYANAN SURAT ONLINE DESA\n\n" +
                       "Format pengisian mengikuti form generator masing-masing surat.\n\n" +
                       "Ketik perintah:\n" +
                       "  FORMAT SKTM\n" +
                       "  FORMAT SKU\n" +
                       "  FORMAT SKCK\n" +
                       "  FORMAT IZIN\n" +
                       "untuk melihat contoh format setiap jenis.\n\n" +
                       "Jenis surat yang tersedia:\n" + daftar + "\n\n" +
                       "⚠️ SEMUA data pada format wajib diisi, termasuk data pribadi " +
                       "(meskipun NIK sudah terdaftar).";
            }
        }

        // ─── Template format per jenis (mengikuti form generator) ───

        private static readonly WaFieldDef F_NIK = new("NIK", "nik", true, "3273010101010001");
        private static readonly WaFieldDef F_Nama = new("Nama", "nama", true, "[NAMA LENGKAP]");
        private static readonly WaFieldDef F_TempatLahir = new("Tempat Lahir", "tempatlahir", true, "[TEMPAT LAHIR]");
        private static readonly WaFieldDef F_TanggalLahir = new("Tanggal Lahir", "tanggallahir", true, "01-01-1990");
        private static readonly WaFieldDef F_Jk = new("JK", "jk", true, "L");
        private static readonly WaFieldDef F_Agama = new("Agama", "agama", true, "Islam");
        private static readonly WaFieldDef F_Status = new("Status", "statusperkawinan", true, "Kawin");
        private static readonly WaFieldDef F_Pekerjaan = new("Pekerjaan", "pekerjaan", true, "[PEKERJAAN]");
        private static readonly WaFieldDef F_Alamat = new("Alamat", "alamat", true, "[DUSUN/JALAN, RT/RW, DESA, KEC, KAB]");
        private static readonly WaFieldDef F_Keterangan = new("Keterangan", "keterangan", true, "[ALASAN/KEBUTUHAN SURAT]");

        private static readonly WaFieldDef F_BidangUsaha = new("Bidang Usaha", "bidangusaha", true, "[BIDANG USAHA]");
        private static readonly WaFieldDef F_SejakTahun = new("Sejak Tahun", "sejaktahun", true, "2015");
        private static readonly WaFieldDef F_Pendidikan = new("Pendidikan", "pendidikan", true, "SMA");
        private static readonly WaFieldDef F_Kewarganegaraan = new("Kewarganegaraan", "kewarganegaraan", true, "WNI");

        private static readonly WaFieldDef F_NikAnak = new("NIK Anak", "nikanak", true, "3273010101010002");
        private static readonly WaFieldDef F_NamaAnak = new("Nama Anak", "namaanak", true, "[NAMA ANAK]");
        private static readonly WaFieldDef F_TempatLahirAnak = new("Tempat Lahir Anak", "tempatlahiranak", true, "[TEMPAT LAHIR ANAK]");
        private static readonly WaFieldDef F_TanggalLahirAnak = new("Tanggal Lahir Anak", "tanggallahiranak", true, "01-01-2010");
        private static readonly WaFieldDef F_JkAnak = new("JK Anak", "jkanak", true, "P");
        private static readonly WaFieldDef F_AgamaAnak = new("Agama Anak", "agamaanak", true, "Islam");
        private static readonly WaFieldDef F_StatusAnak = new("Status Anak", "statusanak", true, "Belum Kawin");
        private static readonly WaFieldDef F_PekerjaanAnak = new("Pekerjaan Anak", "pekerjaananak", false, "Pelajar");
        private static readonly WaFieldDef F_AlamatAnak = new("Alamat Anak", "alamatanak", true, "[ALAMAT ANAK]");

        private static readonly WaFieldDef F_NegaraTujuan = new("Negara Tujuan", "negaratujuan", true, "Malaysia");
        private static readonly WaFieldDef F_NamaPt = new("Nama PT", "namapt", false, "[NAMA PT/PERUSAHAAN]");

        private static readonly WaFieldDef F_NamaInstansi = new("Nama Instansi", "nama", true, "[NAMA INSTANSI/LEMBAGA]");
        private static readonly WaFieldDef F_AlamatInstansi = new("Alamat Instansi", "alamat", true, "[ALAMAT INSTANSI]");

        private static IEnumerable<WaFieldDef> DasarPribadi()
        {
            yield return F_NIK;
            yield return F_Nama;
            yield return F_TempatLahir;
            yield return F_TanggalLahir;
            yield return F_Jk;
            yield return F_Agama;
            yield return F_Status;
            yield return F_Pekerjaan;
            yield return F_Alamat;
        }

        /// <summary>Field yang dibutuhkan untuk satu jenis surat, sesuai form generator.</summary>
        public static IReadOnlyList<WaFieldDef> FieldDefs(string namaJenis)
        {
            var list = new List<WaFieldDef>();
            var j = (namaJenis ?? string.Empty).ToUpperInvariant();

            if (j == SuratConstants.INSTANSI)
            {
                list.Add(F_NamaInstansi);
                list.Add(F_AlamatInstansi);
                list.Add(F_Keterangan);
                return list;
            }

            list.AddRange(DasarPribadi());

            if (j == SuratConstants.SKTM || j == SuratConstants.SKD_UMUM ||
                j == SuratConstants.DOMISILI_WARGA || j == SuratConstants.SKU ||
                j == SuratConstants.PENGANTAR_SKCK)
                list.Add(F_Keterangan);

            if (j == SuratConstants.SKU)
            {
                list.Add(F_BidangUsaha);
                list.Add(F_SejakTahun);
            }

            if (j == SuratConstants.PENGANTAR_SKCK)
            {
                list.Add(F_Pendidikan);
                list.Add(F_Kewarganegaraan);
            }

            if (j == SuratConstants.IZIN_ORTU)
            {
                list.AddRange(new[] { F_NikAnak, F_NamaAnak, F_TempatLahirAnak, F_TanggalLahirAnak, F_JkAnak, F_AgamaAnak, F_StatusAnak, F_PekerjaanAnak, F_AlamatAnak });
                list.Add(F_NegaraTujuan);
                list.Add(F_NamaPt);
            }

            return list;
        }

        /// <summary>Apakah nilai kolom yang diberikan valid/lengkap (termasuk bentuk baku).</summary>
        private static bool NilaiTidakValid(string key, string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return true;
            return key switch
            {
                "nik" => raw.Trim().Length != 16,
                "tanggallahir" or "tanggallahiranak" => NormalkanTanggal(raw) == null,
                "jk" or "jkanak" => NormalkanJenisKelamin(raw) == null,
                "agama" or "agamaanak" => NormalkanAgama(raw) == null,
                "statusperkawinan" or "statusanak" => NormalkanStatusPerkawinan(raw) == null,
                "sejaktahun" => !(int.TryParse(raw.Trim(), out var y) && y > 1900 && y <= DateTime.Now.Year),
                _ => false
            };
        }

        /// <summary>
        /// Mengecek seluruh field wajib untuk jenis surat (mengikuti form generator).
        /// Berlaku selalu — termasuk saat NIK sudah terdaftar.
        /// Mengembalikan daftar label yang kurang.
        /// </summary>
        public static List<string> CekKelengkapan(string namaJenis, Dictionary<string, string> fields, string? nik = null)
        {
            var missing = new List<string>();
            var defs = FieldDefs(namaJenis);

            foreach (var def in defs)
            {
                if (!def.Required) continue;
                var value = def.Key == "nik" ? nik : fields.GetValueOrDefault(def.Key);
                if (NilaiTidakValid(def.Key, value))
                    missing.Add(def.Label);
            }

            return missing;
        }

        /// <summary>Contoh format lengkap untuk satu jenis surat (mengikuti field form generator).</summary>
        public static string FormatTemplate(string namaJenis)
        {
            var defs = FieldDefs(namaJenis);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"📋 FORMAT {TampilanJenis(namaJenis).ToUpperInvariant()}");
            sb.AppendLine("Salin contoh di bawah, ganti isinya dengan data Anda:");
            sb.AppendLine();
            sb.AppendLine(TampilanJenis(namaJenis).ToUpperInvariant());
            foreach (var def in defs)
            {
                sb.Append(def.Label).Append(": ").Append(def.Contoh ?? "…");
                if (!def.Required) sb.Append(" (opsional)");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        /// <summary>Mendeteksi jenis surat dari teks pesan (baris pertama / kata kunci).</summary>
        public static string? DetectJenisSurat(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            var lower = text.ToLowerInvariant();

            // Pencocokan kata kunci pada satu potongan teks. Mengembalikan
            // katalog dengan kata kunci terpanjang yang cocok.
            static string? MatchKatalog(string? lower)
            {
                if (lower == null) return null;
                return KatalogSurat.Concat(KatalogOffline)
                    .Where(item => item.KataKunci.Any(k => lower.Contains(k)))
                    .OrderByDescending(item => item.KataKunci.Max(k => k.Length))
                    .Select(item => item.NamaJenis)
                    .FirstOrDefault();
            }

            // 1. Prioritaskan baris pertama (di situlah warga menulis jenis
            //    surat) — mencegah kata kunci di isi field (mis. alamat
            //    "dekat SKCK") salah menangkap jenis lain.
            var barisPertama = lower.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                    .FirstOrDefault();
            var dariBarisPertama = MatchKatalog(barisPertama);
            if (dariBarisPertama != null) return dariBarisPertama;

            // 2. Fallback: pindai seluruh pesan (mis. warga menulis jenis di
            //    tengah pesan tanpa baris judul).
            return MatchKatalog(lower);
        }

        /// <summary>Mengecek apakah jenis surat hanya bisa via kantor (offline).</summary>
        public static bool IsOfflineOnly(string namaJenis)
            => OfflineOnly.Contains(namaJenis, StringComparer.OrdinalIgnoreCase);

        /// <summary>Daftar jenis yang didukung online (untuk ditampilkan ke warga).</summary>
        public static string DaftarSuratOnline()
            => string.Join(", ", KatalogSurat.Select(k => "• " + TampilanJenis(k.NamaJenis)));

        public static string TampilanJenis(string? namaJenis)
            => string.IsNullOrWhiteSpace(namaJenis) ? string.Empty : namaJenis.Replace("_", " ");

        /// <summary>
        /// Mem-parse pesan berformat menjadi data terstruktur.
        /// Tidak melakukan validasi kelengkapan — hanya ekstraksi.
        /// </summary>
        public static ParsedWaFormat Parse(string text)
        {
            var result = new ParsedWaFormat();
            if (string.IsNullOrWhiteSpace(text)) return result;

            result.NamaJenis = DetectJenisSurat(text);

            foreach (var rawLine in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var line = rawLine.Trim();
                if (line.Length == 0) continue;

                var colonIndex = line.IndexOf(':');
                if (colonIndex <= 0)
                {
                    // Bisa jadi baris nama jenis saja (mis. "SKTM")
                    continue;
                }

                var label = NormalizeLabel(line.Substring(0, colonIndex));
                var value = line.Substring(colonIndex + 1).Trim();
                if (string.IsNullOrWhiteSpace(value)) continue;

                if (label == "nik")
                {
                    var digits = new string(value.Where(char.IsDigit).ToArray());
                    result.NIK = digits;
                    if (digits.Length != 16)
                        result.Warnings.Add("NIK bukan 16 digit angka.");
                }
                else if (label == "ttl")
                {
                    result.Fields["ttl"] = value;
                    result.Fields["tempatlahir"] = TtlPisahTempat(value);
                    result.Fields["tanggallahir"] = TtlPisahTanggal(value);
                }
                else
                {
                    result.Fields[label] = value;
                }
            }

            return result;
        }

        /// <summary>Mengubah label kolom menjadi kunci baku (huruf kecil, tanpa spasi/garis bawah).</summary>
        public static string NormalizeLabel(string label)
        {
            var clean = new string(label
                .ToLowerInvariant()
                .Where(c => !char.IsWhiteSpace(c) && c != '_' && c != '-' && c != '.' && c != ',')
                .ToArray());

            return clean switch
            {
                "jk" => "jk",
                "jeniskelamin" => "jk",
                "tgl" or "tgllahir" or "tanggall" => "tanggallahir",
                "tglanak" or "tgllahiranak" or "tanggallanak" => "tanggallahiranak",
                "tpl" or "tempatlah" => "tempatlahir",
                "statuskawin" or "status" => "statusperkawinan",
                "namainstansi" or "instansi" or "lembaga" => "nama",
                "alamatinstansi" => "alamat",
                "ttl" => "ttl",
                "bidangusaha" => "bidangusaha",
                "sejak" or "sejaktahun" => "sejaktahun",
                "negaratujuan" or "tujuan" => "negaratujuan",
                "namapt" or "pt" or "perusahaan" => "namapt",
                "keperluan" => "keterangan",
                "tujuantinggal" => "tujuantinggal",
                "alamatasal" => "alamatasal",
                "sumberkoreksi" or "sumberdatakoreksi" => "sumberkoreksi",
                "sumberkeliru" or "sumberdatakeliru" => "sumberkeliru",
                "alasan" or "alasanperbedaan" => "alasanperbedaan",
                _ => clean
            };
        }        public static string TtlPisahTempat(string ttl)
        {
            var parts = ttl.Split(',');
            return parts.Length > 0 ? parts[0].Trim() : string.Empty;
        }

        public static string TtlPisahTanggal(string ttl)
        {
            var parts = ttl.Split(',');
            return parts.Length > 1 ? parts[1].Trim() : string.Empty;
        }

        /// <summary>Menormalkan tanggal menjadi yyyy-MM-dd (null jika tidak valid).</summary>
        public static string? NormalkanTanggal(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;

            var s = input.Trim();
            if (DateTime.TryParseExact(s, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d1))
                return d1.ToString("yyyy-MM-dd");
            if (DateTime.TryParseExact(s, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d2))
                return d2.ToString("yyyy-MM-dd");
            if (DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d3))
                return d3.ToString("yyyy-MM-dd");

            return null;
        }

        public static string? NormalkanJenisKelamin(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            var s = input.Trim().ToLowerInvariant();
            if (s is "1" or "l" or "laki" or "laki-laki" or "laki laki") return "Laki-laki";
            if (s is "2" or "p" or "perempuan" or "wanita") return "Perempuan";
            return null;
        }

        public static string? NormalkanAgama(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            var s = input.Trim().ToLowerInvariant();
            return s switch
            {
                "1" or "islam" => "Islam",
                "2" or "kristen" or "protestan" => "Kristen",
                "3" or "katolik" => "Katolik",
                "4" or "hindu" => "Hindu",
                "5" or "buddha" => "Buddha",
                "6" or "konghucu" => "Konghucu",
                _ => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(input.Trim().ToLowerInvariant())
            };
        }

        public static string? NormalkanStatusPerkawinan(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            var s = input.Trim().ToLowerInvariant();
            return s switch
            {
                "1" or "belum kawin" or "single" or "lajang" => "Belum Kawin",
                "2" or "kawin" or "menikah" => "Kawin",
                "3" or "cerai hidup" or "cerai" => "Cerai Hidup",
                "4" or "cerai mati" or "janda" or "duda" => "Cerai Mati",
                _ => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(input.Trim().ToLowerInvariant())
            };
        }

        public static string? NormalkanPendidikan(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            var s = input.Trim().ToLowerInvariant();
            return s switch
            {
                "1" or "sd" => "SD",
                "2" or "smp" => "SMP",
                "3" or "sma" or "slta" => "SMA",
                "4" or "d3" => "D3",
                "5" or "s1" => "S1",
                "6" or "s2" => "S2",
                "7" or "s3" => "S3",
                _ => input.Trim()
            };
        }

        public static string? NormalkanKewarganegaraan(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            var s = input.Trim().ToLowerInvariant();
            return s is "1" or "wni" ? "WNI" : s is "2" or "wna" ? "WNA" : input.Trim();
        }
    }
}