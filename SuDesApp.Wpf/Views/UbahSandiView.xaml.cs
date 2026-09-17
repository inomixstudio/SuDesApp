using System.Windows.Controls;
using SuDesApp.Wpf.ViewModels;

namespace SuDesApp.Wpf.Views
{
    public partial class UbahSandiView : UserControl
    {
        public UbahSandiView(UbahSandiViewModel? viewModel = null)
        {
            InitializeComponent();
            DataContext = viewModel;

            // PasswordBox tidak mendukung TwoWay binding (§ keamanan), jadi sinkronkan
            // nilainya ke ViewModel secara manual pada setiap perubahan karakter.
            txtLama.PasswordChanged += (_, _) => SyncToViewModel();
            txtBaru.PasswordChanged += (_, _) => SyncToViewModel();
            txtKonfirmasi.PasswordChanged += (_, _) => SyncToViewModel();
        }

        private void SyncToViewModel()
        {
            if (DataContext is not UbahSandiViewModel vm)
            {
                return;
            }

            vm.PasswordLama = txtLama.Password;
            vm.PasswordBaru = txtBaru.Password;
            vm.KonfirmasiPassword = txtKonfirmasi.Password;
        }
    }
}