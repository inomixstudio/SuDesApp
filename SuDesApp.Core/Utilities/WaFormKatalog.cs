using SuDesApp.WhatsApp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SuDesApp.Utilities
{
    public enum WaFormTipe { TeksPendek, Paragraf, Pilihan }

    public sealed class WaFormPertanyaan
    {
        public string Judul { get; }
        public WaFormTipe Tipe { get; }
        public bool Wajib { get; }
        public string? Bantuan { get; }
        public IReadOnlyList<string> Pilihan { get; }

        public WaFormPertanyaan(string judul, WaFormTipe tipe, bool wajib,
            string? bantuan = null, params string[] pilihan)
        {
            Judul = judul;
            Tipe = tipe;
            Wajib = wajib;
            Bantuan = bantuan;
            Pilihan = pilihan ?? Array.Empty<string>();
        }
    }

    public static class WaFormKatalog
    {
        public const string KolomTimestamp = "Timestamp";
        public const string KolomStatus = "Status";
        public const string KolomResponseId = "Response ID";
        public const string TabJawabanDefault = "Form Responses 1";

        public const string JudulFormDefault = "Formulir Layanan Surat Online Desa";
        public const string JudulSheetDefault = "Jawaban Formulir Layanan Surat Desa";
        public const string DeskripsiFormDefault =
            "Pilih jenis surat, lalu isi data Anda dengan lengkap. Kolom bertanda bintang wajib diisi. " +
            "Isi hanya kolom yang berhubungan dengan jenis surat yang Anda pilih.";

        public static IReadOnlyList<string> DaftarJenis { get; } = new[]
        {
            "SKTM",
            "SKD Umum",
            "Domisili Warga",
            "Pengantar SKCK",
            "SKU (Keterangan Usaha)",
            "Izin Orang Tua",
            "Surat Instansi",
        };

        private static readonly string[] PilihanJk = { "L", "P" };
        private static readonly string[] PilihanAgama =
            { "Islam", "Kristen", "Katolik", "Hindu", "Buddha", "Konghucu" };
        private static readonly string[] PilihanStatus =
            { "Belum Kawin", "Kawin", "Cerai Hidup", "Cerai Mati" };

        public static IReadOnlyList<WaFormPertanyaan> Pertanyaan { get; } = new[]
        {
            new WaFormPertanyaan("Jenis Surat", WaFormTipe.Pilihan, true,
                "Pilih jenis surat yang ingin dibuat", DaftarJenis.ToArray()),

            new WaFormPertanyaan("NIK", WaFormTipe.TeksPendek, true, "16 digit NIK"),
            new WaFormPertanyaan("Nama", WaFormTipe.TeksPendek, true, "Nama lengkap sesuai KTP"),
            new WaFormPertanyaan("Tempat Lahir", WaFormTipe.TeksPendek, true, "Contoh: Karawang"),
            new WaFormPertanyaan("Tanggal Lahir", WaFormTipe.TeksPendek, true, "Format dd-mm-yyyy (contoh: 01-01-1990)"),
            new WaFormPertanyaan("JK", WaFormTipe.Pilihan, true, null, PilihanJk),
            new WaFormPertanyaan("Agama", WaFormTipe.Pilihan, true, null, PilihanAgama),
            new WaFormPertanyaan("Status Perkawinan", WaFormTipe.Pilihan, true, null, PilihanStatus),
            new WaFormPertanyaan("Pekerjaan", WaFormTipe.TeksPendek, true, "Contoh: Petani"),
            new WaFormPertanyaan("Alamat", WaFormTipe.Paragraf, true, "Dusun/Jalan, RT/RW, Desa, Kecamatan, Kabupaten"),
            new WaFormPertanyaan("Keperluan", WaFormTipe.Paragraf, true, "Alasan / kebutuhan surat"),
            new WaFormPertanyaan("No. WhatsApp", WaFormTipe.TeksPendek, true, "Contoh: 081234567890"),
            new WaFormPertanyaan("Token", WaFormTipe.TeksPendek, false, "Jangan diubah bila sudah terisi dari tautan"),

            new WaFormPertanyaan("Pendidikan", WaFormTipe.TeksPendek, false, "Untuk Pengantar SKCK — contoh: SMA"),
            new WaFormPertanyaan("Kewarganegaraan", WaFormTipe.TeksPendek, false, "Untuk Pengantar SKCK — contoh: WNI"),
            new WaFormPertanyaan("Bidang Usaha", WaFormTipe.TeksPendek, false, "Untuk SKU — contoh: Warung Kelontong"),
            new WaFormPertanyaan("Sejak Tahun", WaFormTipe.TeksPendek, false, "Untuk SKU — contoh: 2015"),

            new WaFormPertanyaan("NIK Anak", WaFormTipe.TeksPendek, false, "Untuk Izin Orang Tua"),
            new WaFormPertanyaan("Nama Anak", WaFormTipe.TeksPendek, false, "Untuk Izin Orang Tua"),
            new WaFormPertanyaan("Tempat Lahir Anak", WaFormTipe.TeksPendek, false, "Untuk Izin Orang Tua"),
            new WaFormPertanyaan("Tanggal Lahir Anak", WaFormTipe.TeksPendek, false, "Untuk Izin Orang Tua — dd-mm-yyyy"),
            new WaFormPertanyaan("JK Anak", WaFormTipe.Pilihan, false, null, PilihanJk),
            new WaFormPertanyaan("Agama Anak", WaFormTipe.Pilihan, false, null, PilihanAgama),
            new WaFormPertanyaan("Status Anak", WaFormTipe.Pilihan, false, null, PilihanStatus),
            new WaFormPertanyaan("Pekerjaan Anak", WaFormTipe.TeksPendek, false, "Untuk Izin Orang Tua — contoh: Pelajar"),
            new WaFormPertanyaan("Alamat Anak", WaFormTipe.Paragraf, false, "Untuk Izin Orang Tua"),
            new WaFormPertanyaan("Negara Tujuan", WaFormTipe.TeksPendek, false, "Untuk Izin Orang Tua — contoh: Malaysia"),
            new WaFormPertanyaan("Nama PT", WaFormTipe.TeksPendek, false, "Untuk Izin Orang Tua (opsional)"),

            new WaFormPertanyaan("Nama Instansi", WaFormTipe.TeksPendek, false, "Untuk Surat Instansi / lembaga"),
            new WaFormPertanyaan("Alamat Instansi", WaFormTipe.Paragraf, false, "Untuk Surat Instansi / lembaga"),
        };

        public static IReadOnlyList<string> KolomSheet { get; } = BuildKolomSheet();

        private static string[] BuildKolomSheet()
        {
            var kolom = new List<string> { KolomTimestamp };
            kolom.AddRange(Pertanyaan.Select(p => p.Judul));
            kolom.Add(KolomStatus);
            kolom.Add(KolomResponseId);
            return kolom.ToArray();
        }

        public static int IndexKolom(string judul)
        {
            var key = HeaderKey(judul);
            for (int i = 0; i < KolomSheet.Count; i++)
            {
                if (HeaderKey(KolomSheet[i]) == key) return i;
            }
            return -1;
        }

        private static string HeaderKey(string? s) => WaSheetRow.Normalize(s);
    }
}
