using System;
using System.Threading;
using System.Threading.Tasks;

namespace SuDesApp.Wpf.Mvvm
{
    /// <summary>
    /// Pesan statusbar yang tampil sesaat, lalu kembali ke pesan siap setelah
    /// jeda. Satu instance per ViewModel; panggilan beruntun menggantikan pesan
    /// yang sedang berjalan (timer lama dibatalkan).
    /// </summary>
    public sealed class StatusSesaat
    {
        private static readonly TimeSpan Jeda = TimeSpan.FromSeconds(3.5);

        private readonly Action<string> _tampilkan;
        private readonly string _pesanSiap;
        private CancellationTokenSource? _sumber;

        /// <param name="tampilkan">Penulis pesan ke properti statusbar ViewModel.</param>
        /// <param name="pesanSiap">Pesan tetap saat tidak ada aksi yang baru dikerjakan.</param>
        public StatusSesaat(Action<string> tampilkan, string pesanSiap)
        {
            _tampilkan = tampilkan;
            _pesanSiap = pesanSiap;
        }

        /// <summary>Tampilkan pesan, lalu kembalikan ke pesan siap setelah jeda.</summary>
        public void Tampilkan(string pesan)
        {
            try
            {
                _sumber?.Cancel();
                _sumber?.Dispose();
                var sumber = new CancellationTokenSource();
                _sumber = sumber;

                _tampilkan(pesan);

                // Kembalikan ke pesan siap lewat dispatcher tampilan bila ada,
                // supaya pemberitahuan properti tetap di thread UI.
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                var konteks = SynchronizationContext.Current;
                _ = Task.Delay(Jeda, sumber.Token).ContinueWith(t =>
                {
                    if (t.IsCanceled) return;

                    if (dispatcher != null && !dispatcher.CheckAccess())
                        dispatcher.BeginInvoke(new Action(() => _tampilkan(_pesanSiap)));
                    else if (konteks != null && konteks != SynchronizationContext.Current)
                        konteks.Post(_ => _tampilkan(_pesanSiap), null);
                    else
                        _tampilkan(_pesanSiap);
                }, TaskScheduler.Default);
            }
            catch
            {
                // Statusbar hanya informasi — jangan sampai mengganggu aksi utama.
            }
        }
    }
}
