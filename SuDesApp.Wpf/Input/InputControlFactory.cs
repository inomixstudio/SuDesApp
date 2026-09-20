using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SuDesApp.Interfaces;
using SuDesApp.Data.Models;

namespace SuDesApp.Wpf.Input
{
    /// <summary>
    /// Factory pembuat View+ViewModel input surat — setara SuratInputFactory (WinForms).
    /// Gunakan dalam scope DI agar layanan scoped (IUnitOfWork, repository) ikut ter-resolve.
    /// </summary>
    public class InputControlFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public InputControlFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public (ISuratInput ViewModel, FrameworkElement View) Create(string templateName)
        {
            switch (templateName.ToUpperInvariant())
            {
                case SuratConstants.SKD_UMUM:
                    return CreateView<SkdInputViewModel, Views.SkdInputView>();

                case SuratConstants.KEMATIAN:
                    return CreateView<KematianInputViewModel, Views.KematianInputView>();

                case SuratConstants.KENAL_LAHIR:
                    return CreateView<KenalLahirInputViewModel, Views.KenalLahirInputView>();

                case SuratConstants.PENGANTAR_SKCK:
                    return CreateView<SkckInputViewModel, Views.SkckInputView>();

                case SuratConstants.SKU:
                    return CreateView<SkuInputViewModel, Views.SkuInputView>();

                case SuratConstants.SKTM:
                    return CreateView<SktmInputViewModel, Views.SktmInputView>();

                case SuratConstants.DOMISILI_WARGA:
                    return CreateView<DomisiliWargaInputViewModel, Views.DomisiliWargaInputView>();

                case SuratConstants.INSTANSI:
                    return CreateView<DomisiliInstansiInputViewModel, Views.DomisiliInstansiInputView>();

                case SuratConstants.BEDANAMA:
                    return CreateView<BedaNamaInputViewModel, Views.BedaNamaInputView>();

                case SuratConstants.IZIN_ORTU:
                    return CreateView<IzinOrtuInputViewModel, Views.IzinOrtuInputView>();

                case SuratConstants.IJIN_TINGGAL:
                    return CreateView<IjinTinggalInputViewModel, Views.IjinTinggalInputView>();

                case SuratConstants.GARAPAN_SAWAH:
                    return CreateView<GarapanInputViewModel, Views.GarapanInputView>();

                case SuratConstants.AHLI_WARIS:
                    return CreateView<AhliWarisInputViewModel, Views.AhliWarisInputView>();

                default:
                    // Seluruh formulir NTCR (blanko N1-N6 + surat numpang nikah N8)
                    // memakai satu View + ViewModel yang
                    // menampilkan kolom sesuai blanko terpilih (NamaJenis di-set di bawah).
                    if (SuratConstants.IsNtcr(templateName))
                        return CreateNtcrView(templateName.ToUpperInvariant());

                    throw new NotSupportedException(
                        $"Template '{templateName}' belum dikenal sistem.");
            }
        }

        private (ISuratInput ViewModel, FrameworkElement View) CreateNtcrView(string templateName)
        {
            var vm = _serviceProvider.GetRequiredService<NtcrInputViewModel>();
            vm.NamaJenis = templateName;
            var view = new Views.NtcrInputView { DataContext = vm };
            return (vm, view);
        }

        private (ISuratInput ViewModel, FrameworkElement View) CreateView<TVm, TView>()
            where TVm : BaseSuratInputViewModel
            where TView : FrameworkElement, new()
        {
            var vm = _serviceProvider.GetRequiredService<TVm>();
            var view = new TView { DataContext = vm };
            return (vm, view);
        }
    }
}