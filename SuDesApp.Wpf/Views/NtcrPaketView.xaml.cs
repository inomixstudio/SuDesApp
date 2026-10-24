using System.Windows.Controls;

namespace SuDesApp.Wpf.Views
{
    /// <summary>
    /// Halaman paket NTCR (N1–N6): checklist blanko + form isian bersama.
    /// Seluruh logika ada di NtcrPaketViewModel; formulir isian di dalamnya memakai
    /// kontrol NtcrInputView agar blok isian tetap satu sumber dengan alur per blanko.
    /// </summary>
    public partial class NtcrPaketView : UserControl
    {
        public NtcrPaketView()
        {
            InitializeComponent();
        }
    }
}
