using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;
using SuDesApp.WhatsApp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    ///
    /// Jalur otomatis ini juga DITAHAN bila data desa masih memakai contoh bawaan
    /// aplikasi: surat tidak dibuat dan tidak dikirim, lalu permintaannya ditandai
    /// PERLU_PERBAIKAN beserta alasannya — di sini tidak ada operator yang bisa
    /// ditanyai seperti di form input, jadi mengirim surat berkop contoh ke warga
    /// tidak boleh terjadi tanpa disadari.
    ///
    /// Penahanan itu <b>tidak permanen</b>: begitu data desa disimpan (atau ketika
    /// aplikasi dibuka kembali dengan data desa yang sudah lengkap), daftar permintaan
    /// yang tertahan dibaca ulang lalu diproses lagi sendiri — operator tidak perlu
    /// menyentuhnya (lihat <see cref="LanjutkanPermintaanTertahanAsync"/>).
    /// </summary>
    public class WaAutoProcessor
    {
        private readonly IServiceProvider _provider;
        private readonly AppConfig _config;
        private readonly ILogger<WaAutoProcessor> _logger;
        private readonly NotificationService _notifications;

        /// <summary>Penjaga agar pembukaan penahanan tidak berjalan dua kali sekaligus.</summary>
        private readonly SemaphoreSlim _kunciLanjut = new(1, 1);

        /// <summary>Judul kabar lonceng saat surat warga ditahan karena data desa contoh.</summary>
        public const string JudulNotifDitahan = "Surat Warga Ditahan";

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
        /// Buka kembali permintaan yang tadi ditahan karena data desa masih contoh, lalu
        /// proses ulang sekarang juga — tanpa perlu disentuh operator. Dipanggil begitu
        /// data desa disimpan (lihat <c>SettingsManager.PengaturanDesaTersimpan</c>) dan
        /// sekali saat aplikasi dibuka, supaya penahanan dari sesi sebelumnya pun lepas
        /// walau data desanya diisi lewat pemulihan/impor database.
        ///
        /// Yang TIDAK ikut diutak-atik: permintaan yang gagal karena sebab lain
        /// (generator, pengiriman) — penahanannya bukan karena data desa. Bila data desa
        /// masih contoh, tidak ada yang dibuka sama sekali. Di luar jam layanan, hanya
        /// penahanannya yang dilepas (status kembali BARU); catch-up akan memprosesnya
        /// saat jam layanan dibuka.
        /// </summary>
        /// <returns>Jumlah permintaan yang penahanannya dibuka.</returns>
        public async Task<int> LanjutkanPermintaanTertahanAsync(CancellationToken ct = default)
        {
            if (!AppPreferenceStore.IsWaAutoProcessEnabled())
            {
                _logger.LogInformation(
                    "Lanjut permintaan tertahan dilewati — proses otomatis WA sedang dimatikan.");
                return 0;
            }

            // Dua penyimpanan data desa yang berdekatan (atau simpan + pemrosesan saat aplikasi
            // dibuka) tidak boleh memproses permintaan yang sama dua kali.
            if (!await _kunciLanjut.WaitAsync(0))
            {
                _logger.LogInformation("Lanjut permintaan tertahan sedang berjalan — panggilan ini dilewati.");
                return 0;
            }

            try
            {
                using var scope = _provider.CreateScope();
                var sp = scope.ServiceProvider;
                var repo = sp.GetRequiredService<IPermintaanWaRepository>();

                var penjagaDesa = sp.GetRequiredService<IPeringatanDataDesaContoh>();
                var keadaanDesa = await penjagaDesa.PeriksaAsync(ct);
                if (keadaanDesa.MasihContoh)
                {
                    _logger.LogInformation(
                        "Lanjut permintaan tertahan ditunda — data desa masih contoh ({Field}).",
                        keadaanDesa.RingkasField);
                    return 0;
                }

                var perluPerbaikan = await repo.GetAllAsync(WaRequestStatus.PERLU_PERBAIKAN, ct);
                var tertahan = perluPerbaikan
                    .Where(p => PenahananDataDesaContoh.Ditahan(p.Status, p.Catatan))
                    .ToList();

                if (tertahan.Count == 0)
                {
                    return 0;
                }

                _logger.LogInformation(
                    "Data desa sudah diisi: {Count} permintaan yang tadi ditahan dilanjutkan otomatis.",
                    tertahan.Count);

                int dibuka = 0, terkirim = 0, perluDiperiksa = 0, dijadwalkan = 0;

                foreach (var calon in tertahan)
                {
                    if (ct.IsCancellationRequested) break;

                    // Baca ulang tepat sebelum dibuka: bila penahanannya sudah ditangani
                    // proses lain, permintaan ini tidak boleh diproses dua kali.
                    var permintaan = await repo.GetByIdAsync(calon.ID_Permintaan, ct);
                    if (!PenahananDataDesaContoh.Ditahan(permintaan))
                    {
                        continue;
                    }

                    await repo.UpdateStatusAsync(calon.ID_Permintaan, WaRequestStatus.BARU,
                        PenahananDataDesaContoh.CatatanDibukaKembali, null, null, ct);
                    dibuka++;

                    // Kabar lama "surat ditahan" untuk permintaan ini tidak berlaku lagi:
                    // lonceng tidak boleh menyuruh operator memproses ulang hal yang
                    // sedang dikerjakan sendiri oleh aplikasi.
                    _notifications.HapusPesanLama(JudulNotifDitahan, permintaan!.KodePermintaan);

                    if (!DalamJamLayanan())
                    {
                        dijadwalkan++;
                        await TulisStatusSheetAsync(sp, permintaan!,
                            "MENUNGGU: data desa sudah diisi — surat diproses otomatis saat jam layanan dibuka", ct);
                        continue;
                    }

                    if (await ProcessAsync(calon.ID_Permintaan, ct))
                    {
                        terkirim++;
                    }
                    else
                    {
                        perluDiperiksa++;
                    }
                }

                if (dibuka == 0)
                {
                    return 0; // semuanya sudah ditangani proses lain
                }

                KabariHasilLanjut(dibuka, terkirim, perluDiperiksa, dijadwalkan);
                return dibuka;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Melanjutkan permintaan yang tertahan gagal.");
                return 0;
            }
            finally
            {
                _kunciLanjut.Release();
            }
        }

        /// <summary>
        /// Kabarkan hasil pembukaan penahanan lewat lonceng: apa yang sudah terkirim,
        /// apa yang masih perlu diperiksa operator, dan apa yang menunggu jam layanan.
        /// </summary>
        private void KabariHasilLanjut(int jumlah, int terkirim, int perluDiperiksa, int dijadwalkan)
        {
            var rincian = new List<string>();
            if (terkirim > 0) rincian.Add($"{terkirim} surat terkirim ke warga");
            if (perluDiperiksa > 0) rincian.Add($"{perluDiperiksa} masih perlu diperiksa di panel Layanan Online");
            if (dijadwalkan > 0) rincian.Add($"{dijadwalkan} diproses otomatis saat jam layanan dibuka");

            string pesan = $"{jumlah} permintaan warga yang tadi ditahan karena data desa masih contoh " +
                           "dilanjutkan sendiri setelah data desa disimpan." +
                           (rincian.Count == 0 ? string.Empty : "\n" + string.Join(" • ", rincian) + ".");

            if (perluDiperiksa > 0)
            {
                _notifications.Warning("Surat Warga Dilanjutkan — sebagian perlu diperiksa", pesan, "layanan-online");
            }
            else if (terkirim == 0 && dijadwalkan > 0)
            {
                _notifications.Info("Surat Warga Dijadwalkan", pesan, "layanan-online");
            }
            else
            {
                _notifications.Success("Surat Warga Dilanjutkan", pesan, "layanan-online");
            }
        }

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

                // 0. Penjaga data desa contoh. Di jalur otomatis TIDAK ada orang yang bisa
                //    ditanyai seperti di form input, jadi surat untuk warga tidak diteruskan:
                //    permintaan ditahan dan ditandai PERLU_PERBAIKAN beserta alasannya, supaya
                //    operator melengkapinya setelah data desa diisi. Suratnya pun belum dibuat,
                //    sehingga tidak ada dokumen berkop contoh yang tersimpan di register.
                var penjagaDesa = sp.GetRequiredService<IPeringatanDataDesaContoh>();
                var keadaanDesa = await penjagaDesa.PeriksaAsync(ct);
                var alasanTahan = AlasanTahanDataDesaContoh(keadaanDesa);

                if (alasanTahan.Length > 0)
                {
                    _logger.LogWarning(
                        "Auto-proses {Kode} ditahan: data desa masih contoh ({Field}).",
                        permintaan.KodePermintaan, keadaanDesa.RingkasField);

                    await repo.UpdateStatusAsync(idPermintaan, WaRequestStatus.PERLU_PERBAIKAN,
                        alasanTahan, null, null, ct);
                    await TulisStatusSheetAsync(sp, permintaan,
                        "DITAHAN: data desa masih contoh — surat belum dikirim ke warga", ct);

                    _notifications.Warning(
                        JudulNotifDitahan,
                        permintaan.KodePermintaan + " — surat belum dibuat/dikirim ke warga karena data desa " +
                        "masih memakai contoh bawaan aplikasi (" + keadaanDesa.RingkasField + ").\n" +
                        "Lengkapi Pengaturan Surat → Data Desa, lalu proses ulang permintaan ini " +
                        "dari panel Layanan Online.",
                        "layanan-online");
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
                            ") tetapi gagal dikirim.\nSilakan kirim ulang dari panel Layanan Online.",
                            "layanan-online");
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
                        " ke " + permintaan.NomorWA + ".",
                        "layanan-online");
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
                    " sudah ada tetapi PDF gagal dibuat.\nBisa dicetak manual dari Register Surat.",
                    "layanan-online");
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
                    "Menunggu perbaikan operator di panel Layanan Online.",
                    "layanan-online");
                return false;
            }
        }

        /// <summary>
        /// Alasan penahanan surat otomatis bila data desa masih contoh — kosong bila aman.
        ///
        /// Dipisah sebagai fungsi murni supaya aturan "surat warga tidak boleh keluar
        /// memakai data desa contoh" bisa diperiksa uji tanpa menjalankan seluruh pipeline
        /// WhatsApp (database, generator PDF, Drive, dan gateway).
        /// </summary>
        public static string AlasanTahanDataDesaContoh(KeadaanDataDesaContoh? keadaan)
            => PenahananDataDesaContoh.Alasan(keadaan);

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
