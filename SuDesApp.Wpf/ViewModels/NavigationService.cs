using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Layanan navigasi untuk content host utama.
    /// ==== Skema migrasi dari WinForms ====
    /// SuDesApp.Services.ControlService.SetMainPanel(IContentHost)
    ///   -&gt; NavigationService.Navigate(viewModel/view)
    /// ControlManager.ShowControl(...) / LoadDokumenAsync / ShowSuratControl
    ///   -&gt; NavigationService menampilkan view-model; ContentControl di MainWindow
    ///       otomatis memuat view melalui DataTemplate.
    /// </summary>
    public class NavigationService
    {
        private readonly IServiceProvider _serviceProvider;
        private object? _currentView;

        public event Action<object?>? CurrentViewChanged;

        public object? CurrentView
        {
            get => _currentView;
            private set
            {
                if (ReferenceEquals(_currentView, value))
                {
                    return;
                }
                _currentView = value;
                CurrentViewChanged?.Invoke(value);
            }
        }

        public NavigationService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        /// <summary>Tampilkan view-model / view yang sudah di-resolve dari DI.</summary>
        public void Navigate<TViewModel>() where TViewModel : notnull
        {
            var vm = _serviceProvider.GetRequiredService<TViewModel>();
            CurrentView = vm;
        }

        /// <summary>Tampilkan object (view-model atau view) secara langsung.</summary>
        public void Navigate(object content)
        {
            CurrentView = content ?? throw new ArgumentNullException(nameof(content));
        }

        /// <summary>Kosongkan content host (analog ControlManager.ShowDefaultControl).</summary>
        public void ShowDefault()
        {
            CurrentView = null;
        }
    }
}