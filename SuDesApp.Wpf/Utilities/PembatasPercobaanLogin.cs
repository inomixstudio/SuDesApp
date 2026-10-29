using System;
using SuDesApp.Services;

namespace SuDesApp.Wpf.Utilities
{
    /// <summary>
    /// Batas percobaan untuk jalur login yang TIDAK dicatat
    /// <see cref="PenggunaService"/>: akun pusat penyedia (tanpa baris database)
    /// dan cadangan Login.dat. Akun database sudah punya kunci menetap di
    /// database, jadi sengaja tidak dihitung di sini — kalau ikut dihitung, satu
    /// warga salah sandi bisa mengunci akun pusat penyedia.
    ///
    /// Tanpa penyimpanan, keadaan hanya hidup di memori. Akun pusat memakai
    /// penyimpanan preferensi agar kuncinya tetap berlaku setelah aplikasi
    /// dibuka ulang; yang disimpan hanya hitungan dan waktu gagal terakhir.
    /// </summary>
    public sealed class PembatasPercobaanLogin
    {
        private readonly Func<DateTime> _waktu;
        private readonly IPenyimpananPercobaanLogin? _penyimpanan;
        private int _gagal;
        private DateTime _terakhir;

        public PembatasPercobaanLogin(Func<DateTime>? waktu = null)
            : this(waktu, null)
        {
        }

        internal PembatasPercobaanLogin(
            Func<DateTime>? waktu,
            IPenyimpananPercobaanLogin? penyimpanan)
        {
            _waktu = waktu ?? (() => DateTime.Now);
            _penyimpanan = penyimpanan;

            if (penyimpanan?.Baca() is { Gagal: > 0 } tersimpan)
            {
                _gagal = tersimpan.Gagal;
                _terakhir = tersimpan.Terakhir;
            }
        }

        /// <summary>Sisa masa tunggu; nol bila percobaan boleh dilakukan.</summary>
        public TimeSpan SisaTunggu
        {
            get
            {
                if (_gagal < PenggunaService.MaksPercobaanGagal) return TimeSpan.Zero;

                var sisa = PenggunaService.DurasiKunci - (_waktu() - _terakhir);
                return sisa > TimeSpan.Zero ? sisa : TimeSpan.Zero;
            }
        }

        public bool BolehMencoba => SisaTunggu == TimeSpan.Zero;

        public void CatatGagal()
        {
            _gagal++;
            _terakhir = _waktu();
            _penyimpanan?.Simpan(_gagal, _terakhir);
        }

        public void CatatBerhasil()
        {
            _gagal = 0;
            _terakhir = default;
            _penyimpanan?.Hapus();
        }
    }
}
