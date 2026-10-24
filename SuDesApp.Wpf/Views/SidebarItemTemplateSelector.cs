using System.Windows;
using System.Windows.Controls;
using SuDesApp.Wpf.ViewModels;

namespace SuDesApp.Wpf.Views
{
    /// <summary>
    /// Memilih template render untuk item menu sidebar di MainWindow.
    /// WinForms Utama.cs membedakan antara: label section (BUAT SURAT / FORMULIR /
    /// PENGATURAN / BANTUAN), accordion dengan children, dan tombol langsung.
    /// </summary>
    public class SidebarItemTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? SectionHeaderTemplate { get; set; }
        public DataTemplate? AccordionTemplate { get; set; }
        public DataTemplate? ButtonTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if (item is NavItem navItem)
            {
                if (navItem.IsSectionHeader)
                {
                    return SectionHeaderTemplate!;
                }

                if (navItem.HasChildren)
                {
                    return AccordionTemplate!;
                }

                return ButtonTemplate!;
            }

            return base.SelectTemplate(item, container);
        }
    }
}