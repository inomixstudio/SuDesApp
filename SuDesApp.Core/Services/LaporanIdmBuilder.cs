using System;
using System.Collections.Generic;
using System.Linq;
using SuDesApp.Data.Models;

namespace SuDesApp.Services
{
    /// <summary>
    /// Menyusun <see cref="LaporanIdmData"/> dari hasil IDM yang sudah diambil
    /// halaman IDM. Tidak menyentuh jaringan maupun database, jadi isi laporan
    /// bisa diuji langsung dan PDF hanya mencetak apa yang tampil di layar.
    /// </summary>
    public static class LaporanIdmBuilder
    {
        public static LaporanIdmData Susun(
            HasilIdm? utama,
            IEnumerable<HasilIdm>? semuaTahun,
            DesaData? desa,
            DateTime? waktuMuat,
            DateTime? saatCetak = null)
        {
            var data = new LaporanIdmData
            {
                Desa = desa ?? new DesaData(),
                TanggalCetak = (saatCetak ?? DateTime.Today).Date,
                // Pemisah garis tegak (bukan titik tengah) karena ini teks cetak.
                Sumber = IdmTeks.Sumber(waktuMuat, "|")
            };

            if (utama == null)
            {
                // Belum ada data yang dimuat: laporan tetap bisa dicetak sebagai
                // satu halaman keterangan, memakai identitas lokal.
                data.NamaDesa = Bersih(data.Desa.NamaDesa) ?? string.Empty;
                data.Wilayah = WilayahLokal(data.Desa);
                return data;
            }

            var r = utama.Ringkasan;
            var id = utama.Identitas;

            data.NamaDesa = Bersih(id?.Desa) ?? Bersih(data.Desa.NamaDesa) ?? string.Empty;
            data.Wilayah = id != null ? WilayahPusat(id) : WilayahLokal(data.Desa);
            data.KodeDesa = Bersih(id?.KodeDesa) ?? string.Empty;

            data.Tahun = r.Tahun;
            data.Skor = IdmTeks.Angka(r.Skor, "0.0000");
            data.Status = IdmTeks.Status(r.Status);
            data.Iks = IdmTeks.Angka(r.Iks);
            data.Ike = IdmTeks.Angka(r.Ike);
            data.Ikl = IdmTeks.Angka(r.Ikl);
            data.Target = IdmTeks.Status(r.TargetStatus);
            data.SkorMinimal = IdmTeks.Angka(r.SkorMinimal);
            data.CatatanTarget = IdmTeks.PesanTarget(r);

            data.Indikator = utama.Baris
                .Select(b => new BarisLaporanIdm(
                    Nomor: b.Nomor?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                    Bidang: b.Bidang ?? string.Empty,
                    Indikator: b.Indikator ?? string.Empty,
                    Skor: b.SkorTampil ?? string.Empty,
                    Keterangan: b.Keterangan ?? string.Empty,
                    Kegiatan: b.Kegiatan ?? string.Empty,
                    Pihak: b.Pihak ?? string.Empty,
                    Subtotal: b.Ringkasan))
                .ToList();

            // Tabel tren memuat tahun-tahun yang diterbitkan pusat, terbaru dulu,
            // dan selalu menyertakan tahun yang sedang dicetak.
            var sumberRiwayat = (semuaTahun ?? Array.Empty<HasilIdm>())
                .Where(h => h?.Ringkasan != null)
                .GroupBy(h => h.Ringkasan.Tahun)
                .Select(g => g.OrderByDescending(h => h.Ringkasan.Skor).First())
                .ToList();

            if (sumberRiwayat.All(h => h.Ringkasan.Tahun != r.Tahun))
            {
                sumberRiwayat.Add(utama);
            }

            data.Riwayat = sumberRiwayat
                .OrderByDescending(h => h.Ringkasan.Tahun)
                .Select(h => new TahunLaporanIdm(
                    h.Ringkasan.Tahun,
                    IdmTeks.Angka(h.Ringkasan.Skor, "0.0000"),
                    IdmTeks.Status(h.Ringkasan.Status)))
                .ToList();

            return data;
        }

        /// <summary>"Kecamatan X · Kabupaten Y · Provinsi Z" dari identitas pusat.</summary>
        private static string WilayahPusat(IdentitasIdm id)
        {
            var bagian = new List<string>();
            if (!string.IsNullOrWhiteSpace(id.Kecamatan)) bagian.Add("Kec. " + id.Kecamatan.Trim());
            if (!string.IsNullOrWhiteSpace(id.Kabupaten)) bagian.Add(id.Kabupaten.Trim());
            if (!string.IsNullOrWhiteSpace(id.Provinsi)) bagian.Add("Prov. " + id.Provinsi.Trim());
            return string.Join(" | ", bagian);
        }

        /// <summary>Cadangan dari Pengaturan Surat bila pusat tidak mengirim identitas.</summary>
        private static string WilayahLokal(DesaData desa)
        {
            var bagian = new List<string>();
            if (!string.IsNullOrWhiteSpace(desa.Kecamatan)) bagian.Add("Kec. " + desa.Kecamatan.Trim());
            if (!string.IsNullOrWhiteSpace(desa.Kabupaten)) bagian.Add(desa.Kabupaten.Trim());
            return string.Join(" | ", bagian);
        }

        private static string? Bersih(string? teks) =>
            string.IsNullOrWhiteSpace(teks) ? null : teks.Trim();
    }
}
