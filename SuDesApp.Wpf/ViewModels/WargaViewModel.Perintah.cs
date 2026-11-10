using System;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SudesExPlus;
using SudesExPlus.Style;
using System.Drawing;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.GeneratorPdf;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class WargaViewModel : ObservableObject
    {
        // ----- Perintah -----
        public ICommand MuatUlangCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand CariCommand { get; }
        public ICommand TambahCommand { get; }
        public ICommand UbahCommand { get; }
        public ICommand BatalFormCommand { get; }
        public ICommand SimpanCommand { get; }
        public ICommand UbahStatusCommand { get; }
        public ICommand EksporCommand { get; }
        public ICommand CetakPdfCommand { get; }
        public ICommand BuatTemplateCommand { get; }
        public ICommand BukaImporCommand { get; }
        public ICommand TutupImporCommand { get; }
        public ICommand PeriksaImporCommand { get; }
        public ICommand JalankanImporCommand { get; }
        public ICommand HalamanSebelumnyaCommand { get; }
        public ICommand HalamanBerikutnyaCommand { get; }

        private void SegarkanPerintah()
        {
            (MuatUlangCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (TambahCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (SimpanCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (EksporCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (CetakPdfCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (BuatTemplateCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (BukaImporCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (PeriksaImporCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (JalankanImporCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (HalamanSebelumnyaCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (HalamanBerikutnyaCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }


    }
}
