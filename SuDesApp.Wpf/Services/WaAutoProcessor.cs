using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;
using SuDesApp.WhatsApp;
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SuDesApp.Wpf.Services
{
    /// <summary>
    /// Pipeline otomatis permintaan surat WhatsApp: setelah permintaan masuk
    /// (status BARU — dari percakapan atau jawaban Google Sheet), komponen ini
    /// melanjutkannya TANPA operator — memproses surat (WaSuratProcessor),
    /// meng-generate PDF dengan generator resmi (data tersimpan sama seperti
    /// input form biasa), lalu mengirim tautan unduh Google Drive ke nomor
    /// WhatsApp pemohon (teks/link saja — aplikasi tidak mengirim berkas),
    /// serta menulis status balik ke Google Sheet (mode Sheet). Keadaan akhir:
    ///   SELESAI          → PDF terkirim ke warga.
    ///   PERLU_PERBAIKAN  → gagal (data tidak valid/generator error); menunggu
    ///                      operator di panel Layanan Online.
    /// </summary>
    public class WaAutoProcessor
    {
        private readonly IServiceProvider _provider;
        private readonly AppConfig _config;
        private readonly ILogger<WaAutoProcessor> _logger;
        private readonly NotificationService _notifications;

        public WaAutoProcessor(
            IServiceProvider provider,
            AppConfig config,
            NotificationService notifications,
            ILogger<WaAutoProcessor> logger)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Dipanggil dari WaEngine pada event RequestCreated. Jalur otomatis hanya
        /// jalan bila preferensi aktif dan permintaan masih BARU (belum disentuh
        /// operator) — klik manual "Setujui & Buat Surat" tidak digandakan.
        /// Di luar jam layanan, permintaan dibiarkan BARU untuk diproses saat
        /// jam layanan dibuka kembali (catch-up).
        /// </summary>
        public void HandleRequestCreated(object? sender, PermintaanWa permintaan)
        {
            if (permintaan == null || permintaan.ID_Permintaan <= 0) return;
            if (!AppPreferenceStore.IsWaAutoProcessEnabled())
            {
                _logger.LogInformation(
                    "Auto-proses WA dilewati (preferensi mati) untuk {Kode}", permintaan.KodePermintaan);
                return;
            }

            if (!DalamJamLayanan())
            {
                _logger.LogInformation(
                    "Auto-proses WA ditunda untuk {Kode} — di luar jam layanan ({Rentang}).",
                    permintaan.KodePermintaan, WaServiceHours.RentangTampil(
                        AppPreferenceStore.GetWaServiceOpen(), AppPreferenceStore.GetWaServiceClose(),
                        AppPreferenceStore.GetWaServiceDays()));
                return; // tetap BARU; catch-up akan mengambilnya nanti.
            }

            // Fire-and-forget: jangan blokir loop polling / thread UI.
            _ = Task.Run(() => ProcessAsync(permintaan.ID_Permintaan));
        }

        /// <summary>Apakah layanan otomatis sedang buka (jam + hari layanan).</summary>
        private static bool DalamJamLayanan()
            => WaServiceHours.IsOpen(DateTime.Now,
                AppPreferenceStore.GetWaServiceOpen(), AppPreferenceStore.GetWaServiceClose(),
                AppPreferenceStore.GetWaServiceDays());

        /// <summary>
        /// Catch-up: proses semua permintaan BARU yang tertunda — dipanggil saat
        /// jam layanan baru saja dibuka (event ServiceHoursOpened dari engine).
        /// </summary>
        public async Task CatchUpAsync(CancellationToken ct = default)
        {
            if (!AppPreferenceStore.IsWaAutoProcessEnabled()) return;
            if (!DalamJamLayanan()) return;

            try
            {
                using var scope = _provider.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IPermintaanWaRepository>();
                var tertunda = await repo.GetAllAsync(WaRequestStatus.BARU, ct);

                foreach (var p in tertunda)
                {
                    if (ct.IsCancellationRequested) break;
                    await ProcessAsync(p.ID_Permintaan, ct);
                }

                if (tertunda.Count > 0)
                    _logger.LogInformation(
                        "Catch-up selesai: {Count} permintaan tertunda diproses.", tertunda.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Catch-up permintaan tertunda gagal.");
            }
        }

        /// <summary>Dipanggil dari engine saat jam layanan bertransisi tutup → buka.</summary>
        public void HandleServiceHoursOpened(object? sender, EventArgs e)
            => _ = Task.Run(() => CatchUpAsync());

        /// <summary>
        /// Memproses satu permintaan sampai PDF terkirim. Mengembalikan true bila
        /// berhasil (status SELESAI), false bila gagal (PERLU_PERBAIKAN).
        /// </summary>
        public async Task<bool> ProcessAsync(int idPermintaan, CancellationToken ct = default)
        {
            // Di luar jam layanan: jangan mulai pemrosesan baru (kecuali yang
            // sudah lewat tahap DIPROSES — biarkan selesai agar tidak tergantung).
            if (DalamJamLayanan() == false)
            {
                _logger.LogInformation(
                    "Auto-proses permintaan #{Id} ditunda — di luar jam layanan.", idPermintaan);
                return false;
            }

            try
            {
                using var scope = _provider.CreateScope();
                var sp = scope.ServiceProvider;
                var repo = sp.GetRequiredService<IPermintaanWaRepository>();

                var permintaan = await repo.GetByIdAsync(idPermintaan, ct);
                if (permintaan == null)
                {
                    _logger.LogWarning("Auto-proses: permintaan #{Id} tidak ditemukan.", idPermintaan);
                    return false;
                }

                // Hanya proses permintaan BARU — hindari memproses ulang yang sudah
                // disetujui manual (DIPROSES/SELESAI) atau sudah ditolak.
                if (permintaan.Status != WaRequestStatus.BARU)
                {
                    _logger.LogInformation(
                        "Auto-proses dilewati untuk {Kode} (status {Status}).",
                        permintaan.KodePermintaan, permintaan.Status);
                    return false;
                }

                // 1. Tandai DIPROSES (sama seperti alur manual).
                await repo.UpdateStatusAsync(idPermintaan, WaRequestStatus.DIPROSES,
                    "Diproses otomatis", null, null, ct);

                // 2. Buat surat + simpan ke DB (jalur sama dengan input form biasa).
                var processor = sp.GetRequiredService<WaSuratProcessor>();
                var hasil = await processor.ProsesAsync(idPermintaan);

                // 3. Generate PDF dari data yang tersimpan (sumber kebenaran = DB).
                var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
                var suratData = await unitOfWork.SuratRepository.GetByIdAsync(hasil.IdSurat)
                    ?? throw new InvalidOperationException(
                        $"Surat #{hasil.IdSurat} tidak ditemukan setelah dibuat.");

                var pdfPath = await SuratPdfHelper.GeneratePdfAsync(
                    sp, _config, suratData, _logger, suratData.NamaJenis ?? permintaan.NamaJenis);

                // 4. Kirim surat balik ke nomor WhatsApp pemohon: unggah PDF ke
                //    Google Drive lalu kirim TAUTAN UNDUH (teks saja). Gagal
                //    unggah/kirim = permintaan ditandai untuk operator.
                if (!string.IsNullOrEmpty(pdfPath) && File.Exists(pdfPath))
                {
                    var (terkirim, cara, gagalKirim) = await KirimSuratKeWargaAsync(
                        sp, permintaan, pdfPath, hasil.NomorSurat, ct);

                    if (!terkirim)
                    {
                        _logger.LogWarning(
                            "Gagal kirim surat {Kode}: {Status}",
                            permintaan.KodePermintaan, gagalKirim);
                        await repo.UpdateStatusAsync(idPermintaan, WaRequestStatus.PERLU_PERBAIKAN,
                            $"PDF dibuat ({hasil.NomorSurat}) tetapi gagal dikirim: {gagalKirim}",
                            hasil.IdSurat, null, ct);
                        await TulisStatusSheetAsync(sp, permintaan,
                            "GAGAL KIRIM: " + Potong(gagalKirim ?? "tidak diketahui"), ct);

                        _notifications.Warning(
                            "Gagal Kirim PDF Otomatis",
                            permintaan.KodePermintaan + " — PDF dibuat (" + hasil.NomorSurat +
                            ") tetapi gagal dikirim.\nSilakan kirim ulang dari panel Layanan Online.");
                        return false;
                    }

                    await repo.UpdateStatusAsync(idPermintaan, WaRequestStatus.SELESAI,
                        $"Selesai otomatis ({cara}; Nomor: {hasil.NomorSurat})",
                        hasil.IdSurat,
                        $"Surat siap dan {cara}. Nomor: {hasil.NomorSurat}", ct);

                    await TulisStatusSheetAsync(sp, permintaan,
                        $"SELESAI — surat {hasil.NomorSurat} {cara}", ct);

                    _logger.LogInformation(
                        "Auto-proses {Kode} selesai: Surat {Nomor} → {Cara} ke {NomorWA}.",
                        permintaan.KodePermintaan, hasil.NomorSurat, cara, permintaan.NomorWA);

                    _notifications.Success(
                        "Surat Terkirim Otomatis",
                        permintaan.KodePermintaan + " — " +
                        WaFormatParser.TampilanJenis(permintaan.NamaJenis) +
                        ".\nNomor surat: " + hasil.NomorSurat +
                        "\n" + char.ToUpperInvariant(cara[0]) + cara[1..] +
                        " ke " + permintaan.NomorWA + ".");
                    return true;
                }

                // PDF gagal dibuat — surat sudah ada di DB (bisa dicetak manual),
                // tapi warga belum dapat file. Tandai untuk operator.
                _logger.LogWarning(
                    "Auto-proses {Kode}: PDF gagal dibuat untuk surat #{IdSurat}.",
                    permintaan.KodePermintaan, hasil.IdSurat);
                await repo.UpdateStatusAsync(idPermintaan, WaRequestStatus.PERLU_PERBAIKAN,
                    $"Surat dibuat ({hasil.NomorSurat}) tetapi PDF gagal dibuat.",
                    hasil.IdSurat, null, ct);
                await TulisStatusSheetAsync(sp, permintaan,
                    "PERLU_PERBAIKAN: PDF gagal dibuat — surat sudah ada di aplikasi", ct);

                _notifications.Warning(
                    "PDF Gagal Dibuat",
                    permintaan.KodePermintaan + " — surat " + hasil.NomorSurat +
                    " sudah ada tetapi PDF gagal dibuat.\nBisa dicetak manual dari Register Surat.");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Auto-proses permintaan #{Id} gagal.", idPermintaan);
                try
                {
                using var scope = _provider.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IPermintaanWaRepository>();
                await repo.UpdateStatusAsync(idPermintaan, WaRequestStatus.PERLU_PERBAIKAN,
                    "Gagal diproses otomatis: " + ex.Message, null, null, ct);
                var p = await repo.GetByIdAsync(idPermintaan, ct);
                if (p != null)
                    await TulisStatusSheetAsync(scope.ServiceProvider, p,
                        "GAGAL: " + Potong(ex.Message), ct);
            }
            catch (Exception exStatus)
                {
                    _logger.LogWarning(exStatus,
                        "Gagal menandai PERLU_PERBAIKAN untuk permintaan #{Id}.", idPermintaan);
                }

                _notifications.Error(
                    "Proses Otomatis Gagal",
                    "Permintaan gagal diproses otomatis.\n" +
                    "Menunggu perbaikan operator di panel Layanan Online.");
                return false;
            }
        }

        /// <summary>
        /// Mengirim surat selesai ke warga: unggah PDF ke folder "Surat Online"
        /// di Google Drive, lalu kirim TAUTAN UNDUH via WhatsApp (teks saja —
        /// aplikasi tidak mengirim berkas apa pun). Bila Google belum terhubung
        /// atau unggahan gagal, pengiriman dianggap gagal agar operator
        /// menanganinya di panel Layanan Online.
        /// </summary>
        private async Task<(bool Terkirim, string Cara, string? Gagal)> KirimSuratKeWargaAsync(
            IServiceProvider sp, PermintaanWa permintaan, string pdfPath, string nomorSurat, CancellationToken ct)
        {
            var gateway = sp.GetRequiredService<IWhatsAppGateway>();
            var drive = sp.GetRequiredService<GoogleDriveService>();

            if (!drive.IsOAuthEnabled || !drive.HasStoredToken())
                return (false, string.Empty,
                    "Akun Google belum terhubung — tautan unduh tidak bisa dibuat (login Google dulu).");

            try
            {
                var folderId = await drive.FindOrCreateFolderAsync("Surat Online", null, ct);
                var uploaded = await drive.UploadFileAsync(pdfPath, folderId, Path.GetFileName(pdfPath), null, ct);
                var link = drive.ToWebViewLink(uploaded.Id);

                var pesan = "✅ Surat Anda sudah siap.\n" +
                            $"Jenis   : {WaFormatParser.TampilanJenis(permintaan.NamaJenis)}\n" +
                            $"Nomor   : {nomorSurat}\n\n" +
                            "Unduh PDF surat Anda di tautan berikut:\n" +
                            link + "\n\n" +
                            "Terima kasih.";

                var send = await gateway.SendTextAsync(permintaan.NomorWA, pesan, ct);
                if (send.Success)
                    return (true, "tautan unduh Google Drive dikirim", null);

                return (false, string.Empty, Potong(send.Status + " — " + (send.Detail ?? string.Empty), 160));
            }
            catch (Exception exDrive)
            {
                _logger.LogWarning(exDrive,
                    "Unggah PDF ke Drive gagal untuk {Kode}.", permintaan.KodePermintaan);
                return (false, string.Empty, "Unggah ke Google Drive gagal: " + exDrive.Message);
            }
        }

        /// <summary>
        /// Menulis status pemrosesan balik ke Google Sheet (mode Sheet) berdasar
        /// SheetId/SheetRowNumber yang tersimpan pada permintaan. Tidak pernah melempar.
        /// </summary>
        private async Task TulisStatusSheetAsync(IServiceProvider sp, PermintaanWa permintaan, string status, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(permintaan.DataJson)) return;
                var data = JsonSerializer.Deserialize<WaRequestData>(permintaan.DataJson);
                if (string.IsNullOrWhiteSpace(data?.SheetId) || data.SheetRowNumber is not int row || row <= 0) return;

                var sheets = sp.GetRequiredService<GoogleSheetsService>();
                var tab = WaSheetOptions.GetTabName();
                var col = await sheets.EnsureStatusColumnAsync(data.SheetId!, tab, "Status", ct);
                await sheets.WriteCellAsync(data.SheetId!, tab, row, col, status, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal menulis status Sheet untuk {Kode}", permintaan.KodePermintaan);
            }
        }

        private static string Potong(string? s, int max = 80)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var clean = s.Replace('\n', ' ').Replace('\r', ' ').Trim();
            return clean.Length <= max ? clean : clean[..max] + "…";
        }
    }
}
