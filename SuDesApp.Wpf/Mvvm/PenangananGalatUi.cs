using System;
using System.Threading.Tasks;

namespace SuDesApp.Wpf.Mvvm
{
    /// <summary>
    /// Pembungkus handler kejadian WPF yang harus asinkron (async void).
    /// Semua galat masuk ke saluran yang sama dengan perintah MVVM supaya
    /// pengguna melihat pesan operasi yang gagal, bukan dialog "gangguan
    /// tidak terduga" dari dispatcher.
    /// </summary>
    public static class PenangananGalatUi
    {
        public static async void Jalankan(Func<Task> aksi, string konteks)
        {
            try
            {
                await aksi();
            }
            catch (OperationCanceledException)
            {
                // Dibatalkan pengguna — bukan kegagalan.
            }
            catch (Exception ex)
            {
                AsyncRelayCommand.LaporkanGagal(ex, konteks);
            }
        }
    }
}
