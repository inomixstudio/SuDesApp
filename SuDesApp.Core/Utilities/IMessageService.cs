using System.Threading.Tasks;

namespace SuDesApp.Utilities
{
    /// <summary>
    /// Framework-agnostic message service. Implementasi WPF mengganti
    /// WinForms MessageBox tanpa mengubah seluruh call site.
    /// </summary>
    public enum AppMessageIcon
    {
        None,
        Error,
        Warning,
        Info,
        Question
    }

    public enum AppMessageButton
    {
        Ok,
        OkCancel,
        YesNo,
        YesNoCancel
    }

    public interface IMessageService
    {
        Task ShowMessageAsync(string message, string title, AppMessageButton buttons, AppMessageIcon icon);
        Task ShowErrorAsync(string message);
        Task ShowWarningAsync(string message);
        Task ShowInfoAsync(string message);
        Task<bool> ShowConfirmationAsync(string title, string message);
    }
}