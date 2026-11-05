using System;
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
        /// <summary>Jalankan tugas tanpa ditunggu; galat dicatat ke error.log.</summary>
        public static void Jalankan(Func<Task> tugas, string konteks)
        {
            _ = JalankanAsync(tugas, konteks);
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
