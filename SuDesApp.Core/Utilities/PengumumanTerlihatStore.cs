using System;
using System.Collections.Generic;
using System.Linq;

namespace SuDesApp.Utilities
{
    /// <summary>
    /// Daftar id pengumuman broadcast yang SUDAH pernah tampil di perangkat
    /// ini. Tersimpan di AppPreferenceStore (teks id dipisah ';') supaya
    /// pengumuman yang sama tidak muncul ulang begitu aplikasi dibuka lagi —
    /// "sekali tampil, tidak mengulang" mengikuti perilaku pemberitahuan
    /// sambutan (PemberitahuanSambutanStore).
    ///
    /// Fungsi baca/tulis bisa ditukar lewat <see cref="MuatOverride"/> /
    /// <see cref="SimpanOverride"/> untuk pengujian (pola yang sama dengan
    /// AppPreferenceStore.LokasiOverride): logika pemanggilan tetap diuji
    /// tanpa menyentuh berkas preferensi nyata.
    /// </summary>
    public static class PengumumanTerlihatStore
    {
        public const string Kunci = "pengumumanTerlihat";

        /// <summary>Batas jumlah id yang dipertahankan: id paling tua dibuang bila terlewati,
        /// supaya preferensi tidak tumbuh tanpa batas selama bertahun-tahun.</summary>
        public const int BatasId = 100;

        /// <summary>Pincang untuk uji: null = memakai AppPreferenceStore sungguhan.</summary>
        public static Func<string[]?>? MuatOverride { get; set; }
        public static Action<string[]?>? SimpanOverride { get; set; }

        private static string[] MuatMentah() =>
            MuatOverride is not null
                ? MuatOverride() ?? []
                : (AppPreferenceStore.GetString(Kunci, string.Empty) ?? string.Empty)
                    .Split(';', StringSplitOptions.RemoveEmptyEntries);

        /// <summary>Id yang sudah pernah tampil (tanpa urutan jaminan).</summary>
        public static HashSet<string> Muat() =>
            new(MuatMentah().Select(s => s.Trim()).Where(s => s.Length > 0), StringComparer.Ordinal);

        /// <summary>
        /// Simpan gabungan id lama + id baru (paling baru dulu, dibatasi
        /// <see cref="BatasId"/>). Penandaan dilakukan SEKETIKA setelah popup
        /// dikirim supaya aplikasi yang ditutup di tengah-tengah tidak
        /// menampilkan pengumuman yang sama dua kali setelah dibuka lagi.
        /// </summary>
        public static void TandaiTerlihat(IEnumerable<string> idBaru)
        {
            if (idBaru == null || !idBaru.Any())
            {
                return;
            }

            var gabungan = idBaru.Concat(MuatMentah())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.Ordinal)
                .Take(BatasId)
                .ToArray();

            if (SimpanOverride is not null)
            {
                SimpanOverride(gabungan);
                return;
            }

            AppPreferenceStore.SetString(Kunci, string.Join(";", gabungan));
        }
    }
}
