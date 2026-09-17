using Microsoft.Extensions.DependencyInjection;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SuDesApp.WhatsApp
{
    /// <summary>
    /// Gateway WhatsApp aktif: WhatsApp Cloud API resmi dari Meta.
    /// Dibungkus kelas terpisah agar WaEngine tidak perlu tahu detail provider —
    /// konfigurasi dibaca ulang pada setiap panggilan sehingga perubahan
    /// pengaturan langsung berlaku tanpa restart aplikasi.
    /// </summary>
    public class WaGatewaySelector : IWhatsAppGateway
    {
        private readonly IServiceProvider _provider;

        public WaGatewaySelector(IServiceProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        private IWhatsAppGateway Active => _provider.GetRequiredService<CloudApiWhatsAppGateway>();

        public string Name => Active.Name;
        public bool IsConfigured => Active.IsConfigured;

        public Task<IReadOnlyList<WaInboundMessage>> FetchInboundAsync(CancellationToken cancellationToken = default)
            => Active.FetchInboundAsync(cancellationToken);

        public Task<WaSendResult> SendTextAsync(string toNumber, string text, CancellationToken cancellationToken = default)
            => Active.SendTextAsync(toNumber, text, cancellationToken);
    }
}
