using System;

namespace SuDesApp.Wpf.ViewModels
{
    public sealed class FilterPilihan
    {
        public FilterPilihan(string nilai, string label)
        {
            Nilai = nilai ?? string.Empty;
            Label = label ?? string.Empty;
        }

        public string Nilai { get; }
        public string Label { get; }
    }
}
