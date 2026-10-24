using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Utilities;

namespace SuDesApp.Wpf.Services
{
    /// <summary>
    /// Keadaan satu dokumen hukum pada instalasi ini: berkas yang terpasang,
    /// versi yang tertulis di dalamnya, dan persetujuan yang sudah tercatat.
    /// </summary>
    public sealed record StatusPersetujuan(
        DokumenHukum Dokumen,
        string? JalurBerkas,
        VersiDokumen? VersiTerpasang,
        PersetujuanTercatat? Tercatat)
    {
        /// <summary>Dokumen ini perlu disetujui (belum pernah, atau versinya berubah).</summary>
        public bool PerluPersetujuan => PersetujuanDokumen.PerluPersetujuan(Dokumen, VersiTerpasang);

        /// <summary>
        /// Dokumen ini menuntut persetujuan SAAT INI: versinya terbaca dan belum
        /// (atau tidak lagi) disetujui. Dokumen yang berkasnya hilang tidak pernah
        /// menahan pengguna — aplikasi tidak boleh terkunci karena folder
        /// <c>docs</c> tidak ikut terpasang.
        /// </summary>
        public bool WajibDisetujui => VersiTerpasang is not null && PerluPersetujuan;

        /// <summary>Berkas dokumen tidak ditemukan di folder aplikasi.</summary>
        public bool BerkasHilang => string.IsNullOrWhiteSpace(JalurBerkas);

        /// <summary>Kebalikan <see cref="BerkasHilang"/> — untuk mengaktifkan tombol baca.</summary>
        public bool BisaDibaca => !BerkasHilang;

        /// <summary>Versi dokumen yang terpasang siap ditampilkan; null bila tak terbaca.</summary>
        public string VersiTerpasangTampil => VersiTerpasang is null
            ? "versi belum terbaca"
            : $"Versi {VersiTerpasang.Versi}";

        /// <summary>Tanggal berlaku dokumen terpasang, apa adanya dari dokumennya.</summary>
        public string TanggalBerlakuTampil => VersiTerpasang is null
            || string.IsNullOrWhiteSpace(VersiTerpasang.TanggalBerlaku)
            ? string.Empty
            : $"berlaku sejak {VersiTerpasang.TanggalBerlaku}";

        /// <summary>Persetujuan versi yang lebih lama — dokumennya sudah diperbarui.</summary>
        public bool VersiBerubah => Tercatat is not null
            && VersiTerpasang is not null
            && !string.Equals(Tercatat.Versi, VersiTerpasang.Versi, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Hasil pencatatan persetujuan, siap ditampilkan bila gagal.</summary>
    public sealed record HasilCatatPersetujuan(bool Berhasil, string Pesan)
    {
        public static HasilCatatPersetujuan Sukses() => new(true, string.Empty);

        public static HasilCatatPersetujuan Gagal(string pesan) => new(false, pesan);
    }

    /// <summary>
    /// Jembatan antara dokumen hukum yang terpasang (folder <c>docs</c>) dan
    /// persetujuan di preferensi aplikasi.
    ///
    /// Dua hal penting yang dijaga di sini:
    /// 1. <b>Versi dibaca dari dokumen yang benar-benar ada di komputer pengguna</b>,
    ///    bukan konstanta di kode. Dokumen yang diperbarui dan dinaikkan versinya
    ///    otomatis memicu dialog persetujuan lagi di pemakaian berikutnya.
    /// 2. <b>Penulisan diverifikasi.</b> <see cref="AppPreferenceStore.SetString"/>
    ///    menelan kegagalan tulis (preferensi dianggap non-kritis). Untuk
    ///    persetujuan, gagal tulis berarti pengguna akan ditanya lagi setiap
    ///    kali membuka aplikasi — jadi hasilnya dibaca ulang dan dilaporkan,
    ///    bukan diasumsikan berhasil.
    /// </summary>
    public static class PersetujuanDokumenService
    {
        /// <summary>Keadaan seluruh dokumen hukum yang wajib disetujui, berurutan.</summary>
        public static IReadOnlyList<StatusPersetujuan> MuatStatus()
        {
            return PersetujuanDokumen.Daftar.Select(dokumen =>
            {
                var jalur = BerkasDokumentasi.Cari(dokumen.BerkasRelatif);
                VersiDokumen? versi = null;

                if (jalur is not null)
                {
                    try
                    {
                        versi = PersetujuanDokumen.BacaVersi(File.ReadAllText(jalur));
                    }
                    catch
                    {
                        // Berkas ada tetapi tidak terbaca: sama seperti tidak ada
                        // versi — jangan sampai aplikasi menahan pengguna karena itu.
                        versi = null;
                    }
                }

                return new StatusPersetujuan(
                    dokumen, jalur, versi, PersetujuanDokumen.BacaPersetujuan(dokumen));
            }).ToList();
        }

        /// <summary>True bila minimal satu dokumen terbaca dan belum disetujui pada versinya.</summary>
        public static bool PerluPersetujuan() => MuatStatus().Any(s => s.WajibDisetujui);

        /// <summary>
        /// Catat persetujuan untuk semua dokumen yang versinya terbaca, lalu
        /// verifikasi tiap catatan dengan membacanya kembali.
        /// </summary>
        public static HasilCatatPersetujuan CatatSemua(DateTime waktu)
        {
            var status = MuatStatus();
            var gagal = new List<string>();

            foreach (var satu in status)
            {
                if (satu.VersiTerpasang is null)
                {
                    // Dokumen tanpa versi tidak bisa dicatat dengan jujur. Bukan
                    // kegagalan yang menahan pengguna: dokumen seperti ini juga
                    // tidak pernah memicu dialog (lihat WajibDisetujui).
                    continue;
                }

                PersetujuanDokumen.Catat(satu.Dokumen, satu.VersiTerpasang, waktu);

                var terbaca = PersetujuanDokumen.BacaPersetujuan(satu.Dokumen);
                bool tersimpan = terbaca is not null
                    && string.Equals(terbaca.Versi, satu.VersiTerpasang.Versi, StringComparison.OrdinalIgnoreCase)
                    && Math.Abs((terbaca.DisetujuiPada - waktu.ToLocalTime()).TotalSeconds) < 120;

                if (!tersimpan)
                {
                    gagal.Add($"{satu.Dokumen.Judul} (gagal menulis preferensi)");
                }
            }

            return gagal.Count == 0
                ? HasilCatatPersetujuan.Sukses()
                : HasilCatatPersetujuan.Gagal(
                    "Persetujuan belum tersimpan untuk: " + string.Join("; ", gagal) + ".");
        }

        /// <summary>
        /// Catat persetujuan HANYA bila sama sekali belum ada catatan — dipakai
        /// saat aktivasi langganan sebagai jaring pengaman bila preferensi
        /// pengguna hilang/terhapus. Versi yang berubah tidak pernah dicatat
        /// diam-diam: pengguna harus melihat dokumen barunya lebih dulu.
        /// </summary>
        public static HasilCatatPersetujuan CatatBilaBelumAda(DateTime waktu)
        {
            var belum = MuatStatus().Where(s => s.Tercatat is null).ToList();
            if (belum.Count == 0)
            {
                return HasilCatatPersetujuan.Sukses();
            }

            var gagal = new List<string>();
            foreach (var satu in belum)
            {
                if (satu.VersiTerpasang is null)
                {
                    gagal.Add(satu.Dokumen.Judul);
                    continue;
                }

                PersetujuanDokumen.Catat(satu.Dokumen, satu.VersiTerpasang, waktu);
                if (PersetujuanDokumen.BacaPersetujuan(satu.Dokumen) is null)
                {
                    gagal.Add(satu.Dokumen.Judul);
                }
            }

            return gagal.Count == 0
                ? HasilCatatPersetujuan.Sukses()
                : HasilCatatPersetujuan.Gagal(
                    "Persetujuan belum tersimpan untuk: " + string.Join("; ", gagal) + ".");
        }
    }
}
