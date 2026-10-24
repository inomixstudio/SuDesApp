using System;
using System.Collections.Generic;

namespace SuDesApp.Api
{
    /// <summary>
    /// Pembatas laju sederhana per alamat pengirim: maksimal N permintaan dalam
    /// jendela waktu tertentu. Tujuannya bukan anti-DDoS, melainkan mencegah
    /// satu klien yang salah (loop tanpa henti, skrip yang mengulang permintaan)
    /// membebani aplikasi desktop yang juga sedang dipakai operator.
    ///
    /// Seluruh catatan dijaga dengan satu kunci, karena volumennya rendah
    /// (batas di atas 60 permintaan per menit per klien) sehingga mengunci satu
    /// kali jauh lebih sederhana dan bebas risiko deadlock dibanding mengunci
    /// tiap antrean secara terpisah. Catatan waktu yang sudah keluar dari
    /// jendela dibuang saat klien itu datang lagi, jadi tidak ada pembersih
    /// berkala.
    /// </summary>
    public sealed class ApiPembatas
    {
        private const int MaksAntreanPerKunci = 64;

        private readonly object _gapu = new();
        private readonly Dictionary<string, Queue<DateTime>> _antrean = new(StringComparer.Ordinal);
        private readonly int _batas;
        private readonly TimeSpan _jendela;
        private readonly Func<DateTime> _sekarang;

        /// <param name="batas">Jumlah permintaan maksimum dalam satu jendela.</param>
        /// <param name="jendela">Panjang jendela (mis. 1 menit).</param>
        /// <param name="sekarang">Sumber waktu; disuntikkan agar pengujian tidak bergantung jam sistem.</param>
        public ApiPembatas(int batas, TimeSpan jendela, Func<DateTime>? sekarang = null)
        {
            if (batas < 1) throw new ArgumentOutOfRangeException(nameof(batas), batas, "Batas minimal 1.");
            if (jendela <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(jendela), jendela, "Jendela harus di atas nol.");

            _batas = batas;
            _jendela = jendela;
            _sekarang = sekarang ?? (() => DateTime.Now);
        }

        /// <summary>
        /// True bila permintaan dari <paramref name="kunci"/> masih boleh lewat.
        /// </summary>
        public bool Izinkan(string kunci)
        {
            if (string.IsNullOrEmpty(kunci)) kunci = "?";

            lock (_gapu)
            {
                var sekarang = _sekarang();

                if (!_antrean.TryGetValue(kunci, out var antrean))
                {
                    antrean = new Queue<DateTime>();
                    _antrean[kunci] = antrean;
                }

                BuangKedaluwarsa(antrean, sekarang);

                if (antrean.Count >= _batas)
                {
                    BersihkanDiam(sekarang);
                    return false;
                }

                antrean.Enqueue(sekarang);
                return true;
            }
        }

        /// <summary>
        /// Sisa waktu sampai permintaan berikutnya boleh lewat; nol bila boleh
        /// sekarang. Dipakai untuk mengisi header Retry-After.
        ///
        /// Catatan tertua keluar dari jendela tepat setelah selisih waktunya
        /// mencapai panjang jendela, jadi sisa = jendela - selisih tertua. Kalau
        /// dihitung sebagai (sekarang - tertua) - jendela, hasilnya nol/tebal
        /// negatif pada saat klien justru sedang ditolak, dan Retry-After
        /// menyuruh klien mencoba lagi padahal permintaan itu pasti ditolak.
        /// </summary>
        public TimeSpan TungguBerikutnya(string kunci)
        {
            if (string.IsNullOrEmpty(kunci)) kunci = "?";

            lock (_gapu)
            {
                if (!_antrean.TryGetValue(kunci, out var antrean)) return TimeSpan.Zero;
                if (antrean.Count < _batas) return TimeSpan.Zero;

                var tertua = antrean.Peek();
                var sisa = _jendela - (_sekarang() - tertua);
                return sisa > TimeSpan.Zero ? sisa : TimeSpan.Zero;
            }
        }

        /// <summary>Jumlah permintaan yang tercatat untuk satu klien.</summary>
        public int JumlahTercatat(string kunci)
        {
            lock (_gapu)
            {
                return _antrean.TryGetValue(kunci, out var antrean) ? antrean.Count : 0;
            }
        }

        /// <summary>Lupakan semua catatan (dipakai saat listener dinyalakan ulang).</summary>
        public void Bersihkan()
        {
            lock (_gapu)
            {
                _antrean.Clear();
            }
        }

        private void BuangKedaluwarsa(Queue<DateTime> antrean, DateTime sekarang)
        {
            var batasAwal = sekarang - _jendela;
            while (antrean.Count > 0 && antrean.Peek() <= batasAwal)
                antrean.Dequeue();
        }

        /// <summary>
        /// Buang klien yang sudah lama tidak mengirim apa pun (dan antrean yang
        /// membengkak). Dipanggil saat sudah menolak permintaan supaya tidak
        /// menambah biaya di jalur yang berhasil.
        /// </summary>
        private void BersihkanDiam(DateTime sekarang)
        {
            if (_antrean.Count <= MaksAntreanPerKunci) return;

            foreach (var kunci in new List<string>(_antrean.Keys))
            {
                var antrean = _antrean[kunci];
                BuangKedaluwarsa(antrean, sekarang);
                if (antrean.Count == 0)
                    _antrean.Remove(kunci);
            }
        }
    }
}
