using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SuDesApp.Wpf.ViewModels;

namespace SuDesApp.Wpf.Views.About
{
    public partial class BagianTeknologiView : UserControl
    {
        public BagianTeknologiView()
        {
            InitializeComponent();
        }

        private void ChipTeknologi_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Border { Tag: TeknologiItem item } chip)
            {
                return;
            }

            // Chip berada DI DALAM ItemsControl: DataContext-nya TeknologiItem,
            // bukan VM halaman — dulu karena itu klik chip tidak membuka apa pun.
            // VM dicari dari leluhur visual (AboutView / Grid bagian).
            for (DependencyObject? simpul = chip; simpul != null; simpul = VisualTreeHelper.GetParent(simpul))
            {
                if (simpul is FrameworkElement { DataContext: AboutViewModel vm })
                {
                    vm.OpenUrlCommand.Execute(item.Url);
                    return;
                }
            }
        }
    }
}
