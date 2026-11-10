using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Windows;
using SuDesApp.Configuration;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Input;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Utilities;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class PengaturanAplikasiViewModel : ObservableObject
    {
        // ===== Kecepatan animasi antarmuka (Lambat/Normal/Cepat) =====

        /// <summary>
        /// Pilihan kecepatan animasi untuk kartu pengaturan. Urutannya tetap
        /// Lambat → Normal → Cepat (paling halus ke paling gesit).
        /// </summary>
        public static IReadOnlyList<KecepatanAnimasiPilihanVM> DaftarKecepatanAnimasi()
            => new[]
            {
                new KecepatanAnimasiPilihanVM(KecepatanAnimasi.Lambat),
                new KecepatanAnimasiPilihanVM(KecepatanAnimasi.Normal),
                new KecepatanAnimasiPilihanVM(KecepatanAnimasi.Cepat)
            };

        public IReadOnlyList<KecepatanAnimasiPilihanVM> PilihanKecepatanAnimasi { get; }
            = DaftarKecepatanAnimasi();


    }
}
