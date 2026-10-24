using System;
using System.Threading.Tasks;
using System.Windows;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Views;

namespace SuDesApp.Wpf.Services
{
    /// <summary>
    /// Implementasi <see cref="SuDesApp.Utilities.IMessageService"/> untuk WPF.
    /// Pesan ditampilkan dengan dialog BERTEMA aplikasi (MessageDialogWindow) —
    /// bukan MessageBox bawaan Windows — sehingga warna, tombol, dan tipografi
    /// mengikuti tema aktif. Menjalankan dialog di thread UI melalui Dispatcher.
    /// </summary>
    public class WpfMessageService : IMessageService
    {
        public Task ShowMessageAsync(string message, string title, AppMessageButton buttons, AppMessageIcon icon)
        {
            return Dispatch(() => MessageDialogWindow.Show(title, message, buttons, icon));
        }

        public Task ShowErrorAsync(string message)
            => Dispatch(() => MessageDialogWindow.Show(
                "Error", message, AppMessageButton.Ok, AppMessageIcon.Error));

        public Task ShowWarningAsync(string message)
            => Dispatch(() => MessageDialogWindow.Show(
                "Peringatan", message, AppMessageButton.Ok, AppMessageIcon.Warning));

        public Task ShowInfoAsync(string message)
            => Dispatch(() => MessageDialogWindow.Show(
                "Informasi", message, AppMessageButton.Ok, AppMessageIcon.Info));

        public Task<bool> ShowConfirmationAsync(string title, string message)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var dispatcher = Application.Current?.Dispatcher;
            Action show = () =>
            {
                try
                {
                    var result = MessageDialogWindow.Show(
                        title, message, AppMessageButton.YesNo, AppMessageIcon.Question);
                    tcs.SetResult(result == true);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            };

            if (dispatcher == null || dispatcher.CheckAccess())
            {
                show();
            }
            else
            {
                _ = dispatcher.InvokeAsync(show).Task;
            }

            return tcs.Task;
        }

        /// <summary>
        /// Jalankan dialog di thread UI dan tunggu hasilnya. Dari thread non-UI
        /// memakai InvokeAsync (tidak memblokir thread pemanggil di luar await).
        /// </summary>
        private static Task Dispatch(Func<bool?> dialog)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                dialog();
                return Task.CompletedTask;
            }

            return dispatcher.InvokeAsync(dialog).Task;
        }
    }
}
