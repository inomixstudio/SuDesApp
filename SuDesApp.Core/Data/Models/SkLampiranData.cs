using System;
using System.Collections.Generic;
using System.Linq;

namespace SuDesApp.Data.Models
{
    /// <summary>
    /// Satu baris lampiran daftar nama pada SK perangkat/kelembagaan desa.
    ///
    /// Nama, NIK, peran, dan unit berasal dari data perangkat desa; kolom pribadi
    /// (pekerjaan, agama, golongan darah, status perkawinan, alamat) diambil dari
    /// data Warga lewat NIK oleh <c>SkPerangkatLampiranService</c>. Nilai yang
    /// kosong dicetak tanda hubung oleh generator, bukan kotak kosong.
    /// </summary>
    public sealed class BarisLampiranSk
    {
        public string Nama { get; init; } = string.Empty;

        public string NIK { get; init; } = string.Empty;

        /// <summary>Tempat dan tanggal lahir siap cetak, mis. "Karawang, 07-08-1977".</summary>
        public string TempatTanggalLahir { get; init; } = string.Empty;

        /// <summary>
        /// Peran dalam unitnya (mis. "Ketua", "Sekretaris", "Anggota"); kosong berarti
        /// jabatan orangnya yang dipakai sebagai peran.
        /// </summary>
        public string Peran { get; init; } = string.Empty;

        public string Pendidikan { get; init; } = string.Empty;

        public string Pekerjaan { get; init; } = string.Empty;

        public string Agama { get; init; } = string.Empty;

        public string GolonganDarah { get; init; } = string.Empty;

        public string StatusPerkawinan { get; init; } = string.Empty;

        public string Alamat { get; init; } = string.Empty;

        public string NomorHP { get; init; } = string.Empty;

        /// <summary>
        /// Unit pengelompokan baris (mis. "POSYANDU SAKURA I" atau "POKJANAL").
        /// Kosong berarti baris tersebut tidak masuk blok mana pun — semua baris
        /// kosong dikumpulkan pada satu blok tanpa judul.
        /// </summary>
        public string Unit { get; init; } = string.Empty;
    }

    /// <summary>Satu baris lampiran yang sudah bernomor di dalam bloknya.</summary>
    public sealed class SkLampiranBaris
    {
        public int Nomor { get; init; }

        public BarisLampiranSk Orang { get; init; } = new();
    }

    /// <summary>
    /// Satu blok lampiran: satu unit (mis. satu Posyandu) beserta barisnya.
    /// Blok tanpa judul (<see cref="Unit"/> kosong) adalah daftar biasa, mis.
    /// daftar anggota Linmas yang seluruhnya satu kesatuan.
    /// </summary>
    public sealed class SkLampiranBlok
    {
        public string Unit { get; init; } = string.Empty;

        public IReadOnlyList<SkLampiranBaris> Baris { get; init; } = Array.Empty<SkLampiranBaris>();

        public int Jumlah => Baris.Count;
    }

    /// <summary>
    /// Pengelompokan baris lampiran menurut unitnya, dipakai generator PDF.
    /// Dipisah dari generator supaya urutan blok, penomoran ulang per unit, dan
    /// penyaringan baris kosong bisa diuji tanpa menggambar PDF.
    /// </summary>
    public static class SkPerangkatLampiran
    {
        /// <summary>
        /// Kelompokkan baris menurut unitnya. Urutan unit mengikuti kemunculan
        /// pertamanya — jadi urutan yang diatur operator di panel tidak diacak.
        /// Nomor baris dimulai lagi dari 1 pada setiap unit (setiap Posyandu punya
        /// daftar 1..n sendiri, sama seperti contoh SK Posyandu), dan baris tanpa
        /// nama dibuang supaya tidak mencetak baris kosong.
        /// </summary>
        public static IReadOnlyList<SkLampiranBlok> Kelompokkan(IEnumerable<BarisLampiranSk>? baris)
        {
            var urutan = new List<string>();
            var isi = new Dictionary<string, List<BarisLampiranSk>>(StringComparer.OrdinalIgnoreCase);

            foreach (var orang in baris ?? Enumerable.Empty<BarisLampiranSk>())
            {
                if (orang == null || string.IsNullOrWhiteSpace(orang.Nama))
                {
                    continue;
                }

                string unit = (orang.Unit ?? string.Empty).Trim();
                if (!isi.TryGetValue(unit, out var daftar))
                {
                    daftar = new List<BarisLampiranSk>();
                    isi[unit] = daftar;
                    urutan.Add(unit);
                }

                daftar.Add(orang);
            }

            return urutan
                .Select(unit => new SkLampiranBlok
                {
                    Unit = unit,
                    Baris = isi[unit]
                        .Select((orang, i) => new SkLampiranBaris { Nomor = i + 1, Orang = orang })
                        .ToList()
                })
                .ToList();
        }

        /// <summary>Jumlah baris yang benar-benar akan tercetak.</summary>
        public static int HitungBaris(IEnumerable<BarisLampiranSk>? baris) =>
            Kelompokkan(baris).Sum(blok => blok.Jumlah);

        /// <summary>
        /// True bila lampiran perlu judul blok ("UNIT …"): lebih dari satu unit,
        /// atau satu unit yang memang bernama. Daftar biasa (semua baris tanpa unit)
        /// dicetak sebagai satu tabel tanpa judul.
        /// </summary>
        public static bool PerluJudulUnit(IReadOnlyList<SkLampiranBlok> blok) =>
            blok != null && (blok.Count > 1 || (blok.Count == 1 && !string.IsNullOrWhiteSpace(blok[0].Unit)));
    }
}
