using System.Windows;
using System.Windows.Controls;

namespace SuDesApp.Wpf.Views
{
    /// <summary>
    /// Memilih template render pada editor "Isi Surat" wizard sesuai jenis bagian:
    /// teks bebas, data diri, atau kelompok kolom isian.
    /// </summary>
    public class BagianWizardTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? TeksTemplate { get; set; }
        public DataTemplate? DataDiriTemplate { get; set; }
        public DataTemplate? KolomTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if (item is ViewModels.BagianTeksWizardItemViewModel)
            {
                return TeksTemplate!;
            }

            if (item is ViewModels.BagianDataDiriWizardItemViewModel)
            {
                return DataDiriTemplate!;
            }

            if (item is ViewModels.BagianKolomWizardItemViewModel)
            {
                return KolomTemplate!;
            }

            return base.SelectTemplate(item, container);
        }
    }
}