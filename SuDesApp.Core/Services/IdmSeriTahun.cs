using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SuDesApp.Services
{
    /// <summary>
    /// Deret penilaian IDM satu desa: satu entri per tahun, terbaru dulu.
    ///
    /// Halaman IDM mengambil beberapa tahun sekaligus dan membiarkan pengguna
    /// memilih tahun mana yang rinciannya dibaca. Tiga pertanyaan tentang deret
    /// itu — tahun mana saja yang terbit, tahun terbit TERBARU, dan data milik
    /// satu tahun — dijawab di sini, bukan dihitung ulang di halaman. Dengan
    /// begitu "tahun terakhir terbit" tidak bisa berbeda antara chip, ringkasan,
    /// dan laporan.
    /// </summary>
    public sealed class IdmSeriTahun
    {
        /// <summary>Deret tanpa tahun — halaman belum memuat apa pun.</summary>
        public static IdmSeriTahun Kosong { get; } = new(Array.Empty<HasilIdm>());

        private IdmSeriTahun(IReadOnlyList<HasilIdm> tahun)
        {
            Tahun = tahun;
            Terbaru = tahun.Count == 0 ? 0 : tahun[0].Ringkasan.Tahun;
        }

        /// <summary>Data per tahun, terbaru dulu.</summary>
        public IReadOnlyList<HasilIdm> Tahun { get; }

        /// <summary>
        /// Tahun TERBIT terbaru pada deret; 0 bila deret kosong. Bukan tahun yang
        /// sedang dibuka — memilih tahun lama tidak mengubah nilai ini.
        /// </summary>
        public int Terbaru { get; }

        /// <summary>
        /// Susun deret dari jawaban pusat. Situs kadang menjawab dengan tahun yang
        /// SAMA untuk permintaan tahun berbeda (parameter tahun diabaikan), jadi
        /// entri pertama untuk sebuah tahun dipertahankan dan sisanya dibuang:
        /// urutan permintaan selalu dari tahun berjalan mundur, sehingga entri
        /// pertama adalah salinan pertama yang diterima.
        /// </summary>
        public static IdmSeriTahun Dari(IEnumerable<HasilIdm>? semua)
        {
            var tahun = (semua ?? Array.Empty<HasilIdm>())
                .Where(h => h.Ringkasan != null)
                .GroupBy(h => h.Ringkasan.Tahun)
                .Select(g => g.First())
                .OrderByDescending(h => h.Ringkasan.Tahun)
                .ToList();

            return tahun.Count == 0 ? Kosong : new IdmSeriTahun(tahun);
        }

        /// <summary>
        /// Data satu tahun pada deret; null bila tahun itu tidak dimuat. Chip tahun
        /// yang tidak ada di deret tidak pernah dapat dipilih, jadi rincian tidak
        /// mungkin berganti ke tahun kosong.
        /// </summary>
        public HasilIdm? Cari(int tahun) =>
            Tahun.FirstOrDefault(h => h.Ringkasan.Tahun == tahun);

        /// <summary>
        /// Kalimat yang menyebut berapa tahun dimuat, rentangnya, dan tahun terakhir
        /// yang terbit. Dipisah dari halaman supaya bunyinya bisa diuji tanpa
        /// memuat halaman atau menghubungi pusat.
        /// </summary>
        public string RingkasanTahun()
        {
            if (Tahun.Count == 0) return string.Empty;

            string terakhir = Terbaru.ToString(CultureInfo.InvariantCulture);

            if (Tahun.Count == 1)
            {
                return "Seri IDM lama hanya tersedia untuk tahun " + terakhir
                       + " — rincian indikator di halaman ini dari tahun tersebut.";
            }

            string pertama = Tahun[^1].Ringkasan.Tahun.ToString(CultureInfo.InvariantCulture);
            string rentang = pertama + "–" + terakhir;
            return Tahun.Count.ToString(CultureInfo.InvariantCulture) + " tahun dimuat (" + rentang
                   + "). Tahun terakhir terbit " + terakhir
                   + " — pilih tahun lain untuk melihat rinciannya.";
        }
    }
}
