using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SuDesApp.Wpf.ViewModels;

namespace SuDesApp.Wpf.Views
{
    public partial class LoginWindow : Window
    {
        private readonly LoginViewModel _viewModel;

        public LoginWindow(LoginViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;

            viewModel.LoginSucceeded += () =>
            {
                DialogResult = true;
                Close();
            };
            viewModel.LoginCancelled += () =>
            {
                DialogResult = false;
                Close();
            };

            // Auto-login Google diperiksa setelah window tampil agar overlay
            // "Masuk sebagai ..." terlihat, bukan layar kosong tanpa UI.
            Loaded += (_, _) => _viewModel.InitializeGoogleAutoLogin();
        }

        private void Login_Click(object sender, RoutedEventArgs e)
        {
            SyncToViewModel();
            _viewModel.LoginCommand.Execute(null);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.CancelCommand.Execute(null);
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (ShowPassCheck.IsChecked != true)
            {
                _viewModel.Password = PasswordBox.Password;
            }
        }

        private void RevealBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (ShowPassCheck.IsChecked == true)
            {
                _viewModel.Password = RevealBox.Text;
                if (!string.IsNullOrEmpty(RevealBox.Text))
                {
                    PasswordBox.Password = RevealBox.Text;
                }
            }
        }

        private void ShowPass_Changed(object sender, RoutedEventArgs e)
        {
            bool show = ShowPassCheck.IsChecked == true;
            RevealBox.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            PasswordBox.Visibility = show ? Visibility.Collapsed : Visibility.Visible;

            if (show)
            {
                RevealBox.Text = _viewModel.Password;
                RevealBox.Focus();
            }
            else
            {
                PasswordBox.Password = _viewModel.Password;
                PasswordBox.Focus();
            }
        }

        private void HeaderBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
            {
                try { DragMove(); } catch { /* jendela sedang dimodali */ }
            }
        }

        private void SyncToViewModel()
        {
            _viewModel.Username = UsernameBox.Text;
            _viewModel.Password = ShowPassCheck.IsChecked == true ? RevealBox.Text : PasswordBox.Password;
        }
    }
}
