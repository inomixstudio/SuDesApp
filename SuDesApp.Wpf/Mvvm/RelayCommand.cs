using System.Windows.Input;

namespace SuDesApp.Wpf.Mvvm
{
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

        public void Execute(object? parameter) => _execute();

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T?> _execute;
        private readonly Predicate<T?>? _canExecute;

        public RelayCommand(Action<T?> execute, Predicate<T?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => _canExecute?.Invoke((T?)parameter) ?? true;

        public void Execute(object? parameter) => _execute((T?)parameter);

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    public class AsyncRelayCommand : ICommand
    {
        /// <summary>
        /// Penangkap galat perintah, dipasang sekali oleh App saat startup.
        /// Tanpa Initialize(), galat hanya masuk log error.log.
        /// </summary>
        public static Action<Exception>? PenangkapGagal { get; set; }

        /// <summary>
        /// Laporkan galat perintah: catat ke error.log lalu serahkan ke
        /// <see cref="PenangkapGagal"/> bila ada. Kegagalan menulis log maupun
        /// pemanggilan penangkap tidak boleh menjadi galat baru.
        /// </summary>
        internal static void LaporkanGagal(Exception ex)
        {
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(AppContext.BaseDirectory, "error.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] PERINTAH GAGAL:\n{ex}\n\n");
            }
            catch { /* log tidak tersedia — laporkan saja lewat penangkap */ }

            try
            {
                PenangkapGagal?.Invoke(ex);
            }
            catch { /* penangkap gagal — jangan sampai jadi galat baru */ }
        }

        private readonly Func<Task> _execute;
        private readonly Func<bool>? _canExecute;
        private bool _isExecuting;

        public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return !_isExecuting && (_canExecute?.Invoke() ?? true);
        }

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter))
            {
                return;
            }

            _isExecuting = true;
            RaiseCanExecuteChanged();
            try
            {
                await _execute();
            }
            catch (OperationCanceledException)
            {
                // Batal demi sendiri — bukan kegagalan yang perlu dilaporkan.
            }
            catch (Exception ex)
            {
                // DILAPORKAN, bukan diteruskan. Dari async void, galat yang lolos
                // akan dilempar ulang di thread UI dan ditangkap
                // DispatcherUnhandledException, yang memunculkan dialog
                // "Aplikasi mengalami gangguan" FATAL dan menghentikan perintah
                // yang sebenarnya hanya gagal sementara (database terkunci,
                // berkas sedang dipakai, jaringan putus).
                AsyncRelayCommand.LaporkanGagal(ex);
            }
            finally
            {
                _isExecuting = false;
                RaiseCanExecuteChanged();
            }
        }

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    public class AsyncRelayCommand<T> : ICommand
    {
        private readonly Func<T, Task> _execute;
        private readonly Func<T, bool>? _canExecute;
        private bool _isExecuting;

        public AsyncRelayCommand(Func<T, Task> execute, Func<T, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return !_isExecuting && (_canExecute?.Invoke((T)parameter!) ?? true);
        }

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter))
            {
                return;
            }

            _isExecuting = true;
            RaiseCanExecuteChanged();
            try
            {
                await _execute((T)parameter!);
            }
            catch (OperationCanceledException)
            {
                // Batal demi sendiri — bukan kegagalan yang perlu dilaporkan.
            }
            catch (Exception ex)
            {
                // Lihat catatan pada AsyncRelayCommand.Execute: galat yang lolos
                // dari async void memunculkan dialog "gangguan" fatal.
                AsyncRelayCommand.LaporkanGagal(ex);
            }
            finally
            {
                _isExecuting = false;
                RaiseCanExecuteChanged();
            }
        }

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}