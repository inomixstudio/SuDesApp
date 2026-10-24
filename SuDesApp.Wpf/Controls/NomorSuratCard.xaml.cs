using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace SuDesApp.Wpf.Controls;

/// <summary>
/// Card nomor surat bersama. Mengikuti DataContext formulir tanpa mengambil
/// kepemilikan model; kontrol pembungkus tidak menjadi node UIA atau tab stop.
/// </summary>
public partial class NomorSuratCard : UserControl
{
    public static readonly DependencyProperty TooltipNomorSuratProperty =
        DependencyProperty.Register(
            nameof(TooltipNomorSurat), typeof(string), typeof(NomorSuratCard),
            new PropertyMetadata("Nomor surat (otomatis jika dikelola register)"));

    public NomorSuratCard() => InitializeComponent();

    protected override AutomationPeer OnCreateAutomationPeer() =>
        new NomorSuratCardAutomationPeer(this);

    public string TooltipNomorSurat
    {
        get => (string)GetValue(TooltipNomorSuratProperty);
        set => SetValue(TooltipNomorSuratProperty, value);
    }

    private sealed class NomorSuratCardAutomationPeer : UserControlAutomationPeer
    {
        public NomorSuratCardAutomationPeer(NomorSuratCard owner) : base(owner) { }

        protected override bool IsControlElementCore() => false;
        protected override bool IsContentElementCore() => false;
    }
}
