using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SuDesApp.Data.Models
{
    /// <summary>
    /// Isi surat yang dibuat dari menu Template Surat, disimpan sebagai payload JSON
    /// pada kolom <c>AdditionalData</c> baris Register Surat.
    ///
    /// PENTING: definisi template (kop, judul, blok teks, kolom isian, tanda tangan)
    /// ikut disimpan sebagai SALINAN, bukan hanya identitas template. Dengan begitu
    /// surat bisa dibuka dan dicetak ulang persis seperti saat diterbitkan, walaupun
    /// templatenya kemudian disunting, diganti awalan nomornya, atau dihapus —
    /// syarat mutlak untuk arsip surat yang sudah bernomor.
    /// </summary>
    public class TemplateSuratTercatat
    {
        private static readonly JsonSerializerOptions OpsiJson = new()
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>Id template saat surat dibuat (0 bila template sudah dihapus dari database).</summary>
        public int TemplateId { get; set; }

        /// <summary>Nama template saat surat dibuat, mis. "Surat Keterangan Penghasilan".</summary>
        public string NamaTemplate { get; set; } = string.Empty;

        /// <summary>Judul surat yang tercetak.</summary>
        public string JudulSurat { get; set; } = string.Empty;

        public string NomorSurat { get; set; } = string.Empty;

        public DateTime TanggalSurat { get; set; } = DateTime.Today;

        /// <summary>Nama penandatangan yang dipakai saat surat dibuat (kosong = Kepala Desa).</summary>
        public string NamaPejabat { get; set; } = string.Empty;

        /// <summary>Salinan definisi surat saat dibuat.</summary>
        public TemplateSuratKustom Template { get; set; } = new();

        /// <summary>Isian kolom: kunci kolom → nilai yang diketik pengguna.</summary>
        public Dictionary<string, string> Nilai { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);

        // =====================================================================
        // Turunan untuk tampilan Register Surat
        // =====================================================================

        /// <summary>
        /// Nama penerima/pemohon surat: nilai kolom "Nama" (atau kolom sejenis pada
        /// template, mis. "Ditujukan Kepada"). Dipakai agar surat bisa dicari lewat
        /// nama pada daftar register.
        /// </summary>
        public string NamaPemohon
        {
            get
            {
                foreach (var label in new[]
                {
                    "Nama", "Ditujukan Kepada", "Nama Calon Pasangan",
                    "Penanggung Jawab", "Nama Penerima"
                })
                {
                    string nilai = NilaiKolom(label);
                    if (nilai.Length > 0) return nilai;
                }

                return string.Empty;
            }
        }

        /// <summary>Alamat penerima surat bila template punya kolom alamat.</summary>
        public string AlamatPemohon
        {
            get
            {
                foreach (var label in new[] { "Alamat", "Alamat Lengkap", "Tempat Kegiatan", "Tempat" })
                {
                    string nilai = NilaiKolom(label);
                    if (nilai.Length > 0) return nilai;
                }

                return string.Empty;
            }
        }

        /// <summary>
        /// Ringkasan untuk kolom "Keperluan" pada register: nama template, ditambah
        /// keterangan keperluan/acara bila template memilikinya.
        /// </summary>
        public string KeperluanTampil
        {
            get
            {
                string dasar = string.IsNullOrWhiteSpace(NamaTemplate)
                    ? (JudulSurat ?? string.Empty).Trim()
                    : NamaTemplate.Trim();

                foreach (var label in new[] { "Keperluan", "Perihal", "Acara", "Jenis Kegiatan", "Isi Pengumuman" })
                {
                    string nilai = NilaiKolom(label);
                    if (nilai.Length == 0) continue;

                    return $"{dasar} — {Potong(nilai, 90)}";
                }

                return dasar;
            }
        }

        /// <summary>
        /// Tempat &amp; tanggal lahir penerima, diambil dari kolom template yang
        /// sepadan (kolom gabungan "Tempat/Tanggal Lahir" dipakai apa adanya).
        /// Kosong bila template tidak memuat kolom itu.
        /// </summary>
        public string TempatTanggalLahirPemohon
        {
            get
            {
                foreach (var label in new[] { "Tempat/Tanggal Lahir", "Tempat Tanggal Lahir", "Tempat, Tanggal Lahir" })
                {
                    string gabungan = NilaiKolom(label);
                    if (gabungan.Length > 0) return gabungan;
                }

                string tempat = NilaiKolom("Tempat Lahir");
                string tanggal = NilaiKolom("Tanggal Lahir");
                if (tempat.Length == 0) return tanggal;
                return tanggal.Length == 0 ? tempat : $"{tempat}, {tanggal}";
            }
        }

        /// <summary>Jenis kelamin penerima (L/P) bila template memuat kolomnya.</summary>
        public string JenisKelaminPemohon
        {
            get
            {
                string nilai = NilaiKolom("Jenis Kelamin");
                if (nilai.Length == 0) nilai = NilaiKolom("JK");
                if (nilai.Length == 0) return string.Empty;

                char awal = char.ToUpperInvariant(nilai.Trim()[0]);
                return awal is 'L' or 'P' ? awal.ToString() : nilai;
            }
        }

        /// <summary>Ringkasan seluruh isian (untuk kolom Keterangan register).</summary>
        public string RingkasanIsian
        {
            get
            {
                var bagian = new List<string>();

                foreach (var kolom in Template.Kolom ?? new List<KolomTemplateSurat>())
                {
                    if (kolom == null || string.IsNullOrWhiteSpace(kolom.Label)) continue;

                    string nilai = TemplateSuratNilai.Ambil(Nilai, kolom.Kunci);
                    if (nilai.Length == 0) continue;

                    string cetak = TemplateSuratNilai.NilaiCetak(kolom, nilai);
                    bagian.Add($"{kolom.Label.Trim()}: {Potong(cetak, 60)}");
                }

                string nama = string.IsNullOrWhiteSpace(NamaTemplate) ? "Template Surat" : $"Template {NamaTemplate}";
                return bagian.Count == 0 ? nama : $"{nama} — {string.Join("; ", bagian)}";
            }
        }

        // =====================================================================
        // Tampilan Register Surat
        // =====================================================================

        /// <summary>
        /// Benar bila baris register ini diterbitkan dari menu Template Surat.
        /// Baris seperti ini tidak memakai kolom warga biasa: seluruh identitas
        /// penerima surat tersimpan pada payload suratnya sendiri.
        /// </summary>
        public static bool DariTemplateSurat(SuratData? surat) =>
            string.Equals(surat?.NamaJenis, SuratConstants.TEMPLATE_SURAT, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Nama penerima untuk daftar/cetak register surat template (dibaca dari isian
        /// surat, mis. kolom "Nama"). Kosong bila suratnya bukan surat template atau
        /// payload-nya tidak dapat dibaca lagi.
        /// </summary>
        public static string NamaPenerimaTampil(SuratData? surat)
        {
            var payload = DariTemplateSurat(surat) ? FromJson(surat!.AdditionalData) : null;
            if (payload == null) return string.Empty;

            return payload.NamaPemohon.Length > 0
                ? payload.NamaPemohon
                : (payload.NamaTemplate ?? string.Empty).Trim();
        }

        /// <summary>Alamat penerima untuk daftar/cetak register surat template.</summary>
        public static string AlamatPenerimaTampil(SuratData? surat)
        {
            var payload = DariTemplateSurat(surat) ? FromJson(surat!.AdditionalData) : null;
            if (payload == null) return string.Empty;

            return payload.AlamatPemohon.Replace("\r", " ").Replace("\n", " ").Trim();
        }

        /// <summary>Tempat &amp; tanggal lahir penerima untuk register surat template.</summary>
        public static string TempatTanggalLahirTampil(SuratData? surat)
        {
            var payload = DariTemplateSurat(surat) ? FromJson(surat!.AdditionalData) : null;
            return payload == null ? string.Empty : payload.TempatTanggalLahirPemohon;
        }

        /// <summary>Jenis kelamin penerima untuk register surat template.</summary>
        public static string JenisKelaminTampil(SuratData? surat)
        {
            var payload = DariTemplateSurat(surat) ? FromJson(surat!.AdditionalData) : null;
            return payload == null ? string.Empty : payload.JenisKelaminPemohon;
        }

        // =====================================================================
        // Payload
        // =====================================================================

        public string ToJson() => JsonSerializer.Serialize(this, OpsiJson);

        /// <summary>
        /// Baca payload surat. JSON yang rusak atau tidak memuat definisi template
        /// mengembalikan null supaya pemanggil dapat memberi pesan yang jelas.
        /// </summary>
        public static TemplateSuratTercatat? FromJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            try
            {
                var data = JsonSerializer.Deserialize<TemplateSuratTercatat>(json, OpsiJson);
                if (data == null) return null;

                data.Template ??= new TemplateSuratKustom();

                // Dictionary hasil deserialisasi memakai pembanding bawaan (peka huruf
                // besar/kecil); diseragamkan agar pencarian nilai kolom konsisten.
                data.Nilai = new Dictionary<string, string>(
                    data.Nilai ?? new Dictionary<string, string>(),
                    StringComparer.OrdinalIgnoreCase);
                data.Template.Kolom ??= new List<KolomTemplateSurat>();
                data.Template.Blok ??= new List<BlokTeksTemplateSurat>();

                foreach (var kolom in data.Template.Kolom)
                {
                    kolom.Pilihan ??= new List<string>();
                }

                return data;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>Benar bila payload memuat definisi surat yang bisa dicetak.</summary>
        public bool BisaDicetak => Template != null && Template.JumlahElemen > 0;

        private string NilaiKolom(string label)
        {
            var kolom = (Template.Kolom ?? new List<KolomTemplateSurat>())
                .FirstOrDefault(k => k != null &&
                    string.Equals((k.Label ?? string.Empty).Trim(), label, StringComparison.OrdinalIgnoreCase));

            return kolom == null ? string.Empty : TemplateSuratNilai.Ambil(Nilai, kolom.Kunci);
        }

        private static string Potong(string? teks, int maks)
        {
            string bersih = (teks ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            if (bersih.Length <= maks) return bersih;

            var kata = new StringBuilder();

            // Potong pada batas kata agar ringkasan tetap enak dibaca.
            int batas = bersih.LastIndexOf(' ', Math.Min(maks, bersih.Length - 1));
            if (batas < maks / 2) batas = maks;

            kata.Append(bersih, 0, batas).Append('…');
            return kata.ToString();
        }
    }
}
