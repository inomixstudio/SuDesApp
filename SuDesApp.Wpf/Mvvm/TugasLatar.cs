using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using SuDesApp.Wpf.Services;

namespace SuDesApp.Wpf.Mvvm
{
    /// <summary>
    /// Jalur aman untuk pekerjaan yang tidak ditunggu pemanggil (fire-and-forget).
    /// Tanpa ini, galat dari tugas latar tidak pernah tercatat dan tidak pernah
    /// sampai ke pengguna — aplikasi tampak diam padahal operasinya gagal.
    /// </summary>
    public static class TugasLatar
    {
        private static readonly ConcurrentBag<Task> _tugasAktif = new();
        private static readonly CancellationTokenSource _cts = new();

        /// <summary>Token pembatalan yang dibatalkan saat aplikasi keluar.</summary>
        public static CancellationToken Token => _cts.Token;


        /// <summary>Jalankan tugas tanpa ditunggu; galat dicatat ke error.log.</summary>
        public static void Jalankan(Func<Task> tugas, string konteks)
        {
            var task = JalankanAsync(tugas, konteks);
            _tugasAktif.Add(task);
        }

        /// <summary>Versi yang bisa ditunggu — dipakai uji.</summary>
        public static async Task JalankanAsync(Func<Task> tugas, string konteks)
        {
            try
            {
                await tugas().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Pembatalan oleh pemanggil bukan kegagalan.
            }
            catch (Exception ex)
            {
                LogGalat.Tulis("TUGAS LATAR: " + konteks, ex);
            }
        }
    }
}
