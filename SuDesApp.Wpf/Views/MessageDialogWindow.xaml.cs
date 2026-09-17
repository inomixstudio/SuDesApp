using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Controls;

namespace SuDesApp.Wpf.Views
{
    /// <summary>
    /// Dialog pesan bertema aplikasi — pengganti MessageBox bawaan Windows agar
    /// warna, tombol, dan tipografi mengikuti tema aktif. Dipakai lewat helper
    /// statis <see cref="Show"/> oleh <c>WpfMessageService</c>.
    /// </summary>
    public partial class MessageDialogWindow : Window
    {
        /// <summary>Hasil dialog: true bila tombol konfirmasi (Ya/OK) ditekan.</summary>
        public bool Confirmed { get; private set; }

        /// <summary>Hasil 3-tombol: null (batal/ditutup), true (Ya), false (Tidak).</summary>
        public bool? ConfirmedNullable => Confirmed ? true : _noPressed ? false : null;

        private bool _noPressed;

        public MessageDialogWindow()
        {
            InitializeComponent();

            // Chrome bertema (tombol tutup title bar memakai AppChromeCommands.Close).
            ChromeWindowBehavior.Attach(this);

            BtnYes.Click += (s, e) => { Confirmed = true; DialogResult = true; };
            BtnNo.Click += (s, e) => { _noPressed = true; DialogResult = true; };
            BtnCancel.Click += (s, e) => { DialogResult = false; };
            BtnOk.Click += (s, e) => { Confirmed = true; DialogResult = true; };
        }

        /// <summary>
        /// Tampilkan dialog pesan bertema. Kombinasi tombol &amp; ikon dipetakan
        /// dari enum <see cref="AppMessageButton"/>/<see cref="AppMessageIcon"/>.
        /// Owner otomatis jendela aktif agar selalu modal di depan aplikasi.
        /// </summary>
        public static bool? Show(string title, string message,
            AppMessageButton buttons = AppMessageButton.Ok,
            AppMessageIcon icon = AppMessageIcon.Info)
        {
            var owner = Application.Current?.Windows.OfType<Window>()
                            .FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;

            var dlg = new MessageDialogWindow { Title = string.IsNullOrWhiteSpace(title) ? "Pesan" : title };
            if (owner != null && owner != dlg)
            {
                dlg.Owner = owner;
            }

            dlg.TitleText.Text = title;
            dlg.MessageText.Text = message;
            dlg.ApplyIcon(icon);
            dlg.ApplyButtons(buttons);

            // Suara sistem sesuai jenis pesan (konsisten dengan MessageBox lama).
            switch (icon)
            {
                case AppMessageIcon.Error: System.Media.SystemSounds.Hand.Play(); break;
                case AppMessageIcon.Warning: System.Media.SystemSounds.Exclamation.Play(); break;
                case AppMessageIcon.Question: System.Media.SystemSounds.Question.Play(); break;
                default: System.Media.SystemSounds.Asterisk.Play(); break;
            }

            bool? result = dlg.ShowDialog();
            if (result == true)
            {
                return dlg.ConfirmedNullable;
            }
            return null; // ditutup lewat tombol X / Esc
        }

        private void ApplyIcon(AppMessageIcon icon)
        {
            switch (icon)
            {
                case AppMessageIcon.Error:
                    SetIconBadge("\u2715", "ErrorBrush");    // ✕
                    break;
                case AppMessageIcon.Warning:
                    SetIconBadge("\u26A0", "WarningBrush");  // ⚠
                    break;
                case AppMessageIcon.Question:
                    SetIconBadge("?", "AccentBrush");        // ?
                    break;
                default:
                    SetIconBadge("i", "AccentBrush");        // i
                    break;
            }
        }

        private void SetIconBadge(string glyph, string brushKey)
        {
            IconText.Text = glyph;
            IconBadge.Background = Application.Current?.TryFindResource(brushKey)
                                   as System.Windows.Media.Brush
                                   ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.DodgerBlue);
        }

        private void ApplyButtons(AppMessageButton buttons)
        {
            BtnYes.Visibility = Visibility.Collapsed;
            BtnNo.Visibility = Visibility.Collapsed;
            BtnCancel.Visibility = Visibility.Collapsed;
            BtnOk.Visibility = Visibility.Collapsed;
            BtnYes.IsDefault = false;
            BtnOk.IsDefault = false;
            BtnOk.IsCancel = false;
            BtnNo.IsCancel = false;

            switch (buttons)
            {
                case AppMessageButton.YesNo:
                    BtnYes.Visibility = Visibility.Visible;
                    BtnNo.Visibility = Visibility.Visible;
                    BtnYes.IsDefault = true;  // Enter = Ya
                    BtnNo.IsCancel = true;    // Esc = Tidak
                    break;
                case AppMessageButton.OkCancel:
                    BtnOk.Visibility = Visibility.Visible;
                    BtnCancel.Visibility = Visibility.Visible;
                    BtnOk.IsDefault = true;   // Enter = OK
                    BtnCancel.IsCancel = true; // Esc = Batal
                    break;
                case AppMessageButton.YesNoCancel:
                    BtnYes.Visibility = Visibility.Visible;
                    BtnNo.Visibility = Visibility.Visible;
                    BtnCancel.Visibility = Visibility.Visible;
                    BtnYes.IsDefault = true;
                    BtnCancel.IsCancel = true;
                    break;
                default: // Ok
                    BtnOk.Visibility = Visibility.Visible;
                    BtnOk.IsDefault = true;   // Enter = OK
                    BtnOk.IsCancel = true;    // Esc = tutup
                    break;
            }
        }

        /// <summary>Seret area judul untuk memindah dialog.</summary>
        private void CaptionBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try { DragMove(); } catch { /* jendela sedang dimodali */ }
            }
        }
    }
}
