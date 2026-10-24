using System;
using System.Threading;

namespace SuDesApp.GeneratorPdf
{
    /// <summary>
    /// Penanda bahwa PDF sedang dibuat untuk <b>pratinjau</b> form input surat,
    /// bukan untuk diterbitkan atau dicetak.
    ///
    /// Alasannya: saat form baru dibuka, hampir semua kolom masih kosong. Tanpa
    /// penanda ini generator menolak membuat dokumen ("Data warga tidak lengkap")
    /// sehingga pengguna tidak bisa melihat kerangka surat sebelum mengisi apa pun.
    /// Selama pratinjau:
    ///   - kolom kosong dicetak sebagai <see cref="TitikKosong"/> — kerangka
    ///     surat tetap terbaca dan jelas mana yang harus diisi;
    ///   - pemeriksaan kelengkapan warga dilewati (lihat
    ///     <c>SuratGeneratorBase.ValidateSuratData</c>), karena justru kekosongan
    ///     itulah yang sedang diperiksa pengguna.
    ///
    /// Penanda ini TIDAK boleh bocor ke jalur simpan/cetak: nilainya disimpan
    /// per alur async (<see cref="AsyncLocal{T}"/>), jadi pratinjau dan
    /// penerbitan surat yang berjalan bersamaan tidak saling memengaruhi.
    /// </summary>
    public static class PratinjauIsian
    {
        /// <summary>Garis isian untuk kolom yang belum diisi pada pratinjau.</summary>
        public const string TitikKosong = "...............................";

        private static readonly AsyncLocal<bool> _aktif = new();

        /// <summary>Benar bila pembuatan PDF saat ini adalah pratinjau form.</summary>
        public static bool Aktif => _aktif.Value;

        /// <summary>
        /// Nyalakan mode pratinjau sampai objek hasilnya dibuang. Nilai sebelumnya
        /// dipulihkan saat dibuang supaya pemanggilan bersarang tetap aman.
        /// </summary>
        public static IDisposable Pakai() => new Penanda(_aktif.Value);

        /// <summary>
        /// Nilai yang dicetak untuk satu sel: teks asli bila ada, titik-titik bila
        /// kosong DAN sedang pratinjau, atau string kosong pada pencetakan biasa
        /// (perilaku lama dipertahankan agar surat resmi tidak berubah).
        /// </summary>
        public static string Tampil(string? nilai) =>
            !string.IsNullOrWhiteSpace(nilai)
                ? nilai!
                : Aktif ? TitikKosong : string.Empty;

        private sealed class Penanda : IDisposable
        {
            private readonly bool _sebelumnya;
            private bool _selesai;

            public Penanda(bool sebelumnya)
            {
                _sebelumnya = sebelumnya;
                _aktif.Value = true;
            }

            public void Dispose()
            {
                if (_selesai) return;
                _selesai = true;
                _aktif.Value = _sebelumnya;
            }
        }
    }
}
