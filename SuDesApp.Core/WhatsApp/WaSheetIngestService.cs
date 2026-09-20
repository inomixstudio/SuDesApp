using Google;
using Google.Apis.Forms.v1.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SuDesApp.WhatsApp
{
    /// <summary>
    /// Poller jawaban Google Sheet untuk mode tautan (layanan online): membaca
    /// baris baru dari tab jawaban form, menyimpannya sebagai PermintaanWa
    /// (status BARU → diproses otomatis oleh WaAutoProcessor), menaikkan event
    /// permintaan, lalu menulis status balik ke Sheet sehingga operator dan
    /// warga bisa memantau kemajuan langsung dari Google Sheet.
    /// </summary>
    public class WaSheetIngestService : IDisposable
    {
        private readonly Func<IServiceProvider?> _providerAccessor;
        private readonly WaEngine _engine;
        private readonly ILogger<WaSheetIngestService> _logger;

        private Timer? _timer;
        private int _busy;
        private bool _disposed;

        public WaSheetIngestService(
            Func<IServiceProvider?> providerAccessor,
            WaEngine engine,
            ILogger<WaSheetIngestService> logger)
        {
            _providerAccessor = providerAccessor ?? throw new ArgumentNullException(nameof(providerAccessor));
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>Jeda polling jawaban Sheet (default 30 detik).</summary>
        public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);

        public void Start()
        {
            if (_timer != null) return;
            _timer = new Timer(
                _ => { _ = Task.Run(() => TickAsync()); },
                null,
                TimeSpan.FromSeconds(20), // jeda awal: biarkan aplikasi selesai startup
                PollInterval);
            _logger.LogInformation("Poller Google Sheet dimulai ({Interval}).", PollInterval);
        }

        public void Stop()
        {
            _timer?.Dispose();
            _timer = null;
            _logger.LogInformation("Poller Google Sheet dihentikan.");
        }

        /// <summary>Satu putaran polling. Aman dipanggil manual (mis. dari panel uji).</summary>
        public async Task TickAsync()
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            try
            {
                if (!WaSheetOptions.IsLinkModeEnabled()) return;
                var sheetId = WaSheetOptions.ExtractSheetId(WaSheetOptions.GetSheetUrl());
                if (string.IsNullOrWhiteSpace(sheetId)) return;

                // Hormati jam layanan otomatis (sama seperti auto-processor).
                if (_engine.ServiceHoursOpenQuery?.Invoke() == false) return;

                var provider = _providerAccessor.Invoke();
                if (provider == null) return;

                using var scope = provider.CreateScope();
                var sp = scope.ServiceProvider;

                var drive = sp.GetRequiredService<GoogleDriveService>();
                if (!drive.IsOAuthEnabled || !drive.HasStoredToken()) return; // Google belum terhubung

                var sheets = sp.GetRequiredService<GoogleSheetsService>();
                var repo = sp.GetRequiredService<IPermintaanWaRepository>();
                var wargaRepo = sp.GetRequiredService<IWargaRepository>();
                var desaRepo = sp.GetRequiredService<IDesaRepository>();

                var tab = WaSheetOptions.GetTabName();
                var ct = CancellationToken.None;

                try
                {
                    var forms = sp.GetRequiredService<GoogleFormsService>();
                    await SinkronFormAsync(forms, sheets, sheetId, tab, ct).ConfigureAwait(false);
                }
                catch (Exception exSync)
                {
                    _logger.LogWarning(exSync, "Sinkronisasi jawaban Forms ke Sheet gagal — lanjut membaca Sheet yang ada");
                }

                var rows = await sheets.GetRowsAsync(sheetId, tab, ct).ConfigureAwait(false);
                if (rows.Count == 0) return;

                // Pastikan kolom Status ada di header (dibuat otomatis bila belum).
                var statusCol = await sheets.EnsureStatusColumnAsync(sheetId, tab, "Status", ct).ConfigureAwait(false);

                foreach (var row in rows)
                {
                    var statusSaatIni = (row.Get("Status") ?? string.Empty).Trim();
                    if (statusSaatIni.Length > 0) continue; // sudah pernah diproses / diisi manual

                    try
                    {
                        await ProsesBarisAsync(row, sheetId, tab, statusCol, sheets, repo, wargaRepo, desaRepo, ct)
                            .ConfigureAwait(false);
                    }
                    catch (GoogleApiException gex)
                    {
                        _logger.LogWarning(gex, "Google API menolak pemrosesan baris Sheet {Row}", row.RowNumber);
                        await TulisAmanAsync(sheets, sheetId, tab, row.RowNumber, statusCol,
                            "GAGAL: " + Potong(gex.Message, 80), ct).ConfigureAwait(false);
                    }
                    catch (Exception exRow)
                    {
                        _logger.LogWarning(exRow, "Gagal memproses baris Sheet {Row}", row.RowNumber);
                        await TulisAmanAsync(sheets, sheetId, tab, row.RowNumber, statusCol,
                            "GAGAL: " + Potong(exRow.Message, 80), ct).ConfigureAwait(false);
                    }
                }
            }
            catch (GoogleApiException gex) when (
                gex.HttpStatusCode == HttpStatusCode.Forbidden ||
                gex.HttpStatusCode == HttpStatusCode.ServiceUnavailable)
            {
                _logger.LogWarning(gex,
                    "Google Sheets API tidak tersedia (aktifkan Google Sheets API di Cloud Console) — putaran polling dilewati.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Kesalahan poller jawaban Google Sheet");
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        }

        private async Task ProsesBarisAsync(
            WaSheetRow row,
            string sheetId,
            string tab,
            string statusCol,
            GoogleSheetsService sheets,
            IPermintaanWaRepository repo,
            IWargaRepository wargaRepo,
            IDesaRepository desaRepo,
            CancellationToken ct)
        {
            var fields = BuildFields(row);

            var jenis = WaFormatParser.DetectJenisSurat(
                row.Get("Jenis Surat") ?? row.Get("Jenis") ?? fields.GetValueOrDefault("keperluan") ?? string.Empty);
            if (jenis == null || WaFormatParser.IsOfflineOnly(jenis))
            {
                await sheets.WriteCellAsync(sheetId, tab, row.RowNumber, statusCol,
                    "JENIS_TAK_DIKENAL — isi kolom Jenis Surat, contoh: SKTM", ct).ConfigureAwait(false);
                return;
            }

            var nikRaw = row.Get("NIK") ?? string.Empty;
            var nik = new string(nikRaw.Where(char.IsDigit).ToArray());

            var nomorWa = NormalisasiNomor(
                row.Get("No. WhatsApp") ?? row.Get("Nomor WhatsApp") ?? row.Get("No WA") ??
                row.Get("WhatsApp") ?? row.Get("NomorWA") ?? string.Empty);
            if (nomorWa.Length < 9)
            {
                await sheets.WriteCellAsync(sheetId, tab, row.RowNumber, statusCol,
                    "PERLU_DATA: Nomor WhatsApp valid untuk pengiriman surat", ct).ConfigureAwait(false);
                return;
            }

            var token = (row.Get("Token") ?? string.Empty).Trim();
            var keperluanTeks = row.Get("Keperluan") ?? fields.GetValueOrDefault("keperluan") ?? string.Empty;

            // Kelengkapan sesuai form generator — aturan sama dengan percakapan WhatsApp.
            var missing = WaFormatParser.CekKelengkapan(jenis, fields, nik.Length == 16 ? nik : null);

            WargaData? warga = null;
            if (nik.Length == 16)
            {
                var dbWarga = await wargaRepo.GetWargaByNikAsync(nik).ConfigureAwait(false);
                if (dbWarga != null)
                {
                    warga = dbWarga;
                }
                else
                {
                    var (baru, missingWarga) = BuildWargaFromFields(fields, nik, desaRepo);
                    if (baru == null || missingWarga.Count > 0)
                        missing.AddRange(missingWarga);
                    warga = baru;
                }
            }

            if (missing.Count > 0 || warga == null)
            {
                var daftar = string.Join(", ", missing.Distinct().Take(4));
                await sheets.WriteCellAsync(sheetId, tab, row.RowNumber, statusCol,
                    "PERLU_DATA: " + (daftar.Length > 0 ? daftar : "data warga tidak lengkap"), ct).ConfigureAwait(false);
                return;
            }

            // Idempoten: jangan buat permintaan ganda untuk baris/token yang sama.
            // CATATAN: permintaan MENUNGGU_FORM (placeholder tautan) diabaikan —
            // justru baris inilah kelanjutannya.
            PermintaanWa? existing = null;
            if (token.Length >= 6)
                existing = await repo.GetBySheetTokenAsync(token, ct).ConfigureAwait(false);
            existing ??= await repo.GetBySheetRowAsync(sheetId, row.RowNumber, ct).ConfigureAwait(false);
            if (existing != null && existing.Status != WaRequestStatus.MENUNGGU_FORM)
            {
                await sheets.WriteCellAsync(sheetId, tab, row.RowNumber, statusCol,
                    existing.Status + (string.IsNullOrWhiteSpace(existing.PesanBalasan) ? "" : " — " + Potong(existing.PesanBalasan, 60)),
                    ct).ConfigureAwait(false);
                return;
            }

            var kode = await repo.NextKodePermintaanAsync(DateTime.Now.Year, ct).ConfigureAwait(false);

            var data = new WaRequestData
            {
                NamaJenis = jenis,
                Keperluan = keperluanTeks.Length > 0 ? keperluanTeks : "Permohonan " + WaFormatParser.TampilanJenis(jenis),
                NomorWA = nomorWa,
                Warga = warga,
                Fields = fields,
                SheetId = sheetId,
                SheetRowNumber = row.RowNumber
            };

            var permintaan = new PermintaanWa
            {
                KodePermintaan = kode,
                NomorWA = nomorWa,
                NamaWarga = warga.Nama,
                NIK = warga.NIK,
                NamaJenis = jenis,
                PesanMentah = $"Google Sheet baris {row.RowNumber}" + (token.Length > 0 ? $" (Token: {token})" : string.Empty),
                DataJson = JsonSerializer.Serialize(data),
                Status = WaRequestStatus.BARU,
                IsRead = false,
                TanggalPermintaan = DateTime.Now,
                Sumber = WaRequestStatus.SumberGoogleSheet,
                SheetToken = token.Length > 0 ? token : null,
                SheetRowId = row.RowNumber
            };

            // Tutup placeholder MENUNGGU_FORM milik nomor ini (warga sudah
            // mengisi form) supaya pesan berikutnya tidak mengulang tautan lama.
            try
            {
                var menunggu = await repo.GetAllAsync(WaRequestStatus.MENUNGGU_FORM, ct).ConfigureAwait(false);
                foreach (var ph in menunggu.Where(p => p.NomorWA == nomorWa))
                {
                    await repo.UpdateStatusAsync(ph.ID_Permintaan, WaRequestStatus.DIPROSES,
                        "Dilanjutkan oleh " + kode, null, null, ct).ConfigureAwait(false);
                }
            }
            catch (Exception exPh)
            {
                _logger.LogWarning(exPh, "Gagal menutup placeholder MENUNGGU_FORM untuk {Nomor}", nomorWa);
            }

            await repo.InsertAsync(permintaan, ct).ConfigureAwait(false);
            _logger.LogInformation("Permintaan Sheet baru {Kode} (baris {Row}) dari {Nomor}: {Jenis}",
                kode, row.RowNumber, nomorWa, jenis);

            // Tulis status dulu, baru naikkan event — agar status akhir dari
            // auto-processor (SELESAI/GAGAL) tidak tertimpa tulisan DITERIMA.
            await sheets.WriteCellAsync(sheetId, tab, row.RowNumber, statusCol,
                "DIPROSES (" + kode + ")", ct).ConfigureAwait(false);

            // Satu pintu event: WaAutoProcessor (PDF + link unduh) & panel operator.
            _engine.RaiseRequestCreated(permintaan);
        }

        /// <summary>
        /// Menyalin jawaban Google Form (dibaca via Forms API) ke Sheet mirror.
        /// Hanya berlaku untuk form yang dibuat aplikasi (Forms API tidak bisa
        /// menautkan form ke Sheet). Idempoten lewat kolom "Response ID".
        /// </summary>
        private async Task SinkronFormAsync(
            GoogleFormsService forms, GoogleSheetsService sheets,
            string sheetId, string tab, CancellationToken ct)
        {
            if (!WaSheetOptions.IsAutoForm()) return;
            var formId = WaSheetOptions.GetFormId();
            if (string.IsNullOrWhiteSpace(formId)) return;

            var definisi = await forms.GetFormAsync(formId, ct).ConfigureAwait(false);
            var peta = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var item in definisi.Items ?? new List<Item>())
            {
                var qid = item.QuestionItem?.Question?.QuestionId;
                if (!string.IsNullOrEmpty(qid) && !string.IsNullOrWhiteSpace(item.Title))
                    peta[qid!] = item.Title!;
            }

            var responses = await forms.ListResponsesAsync(formId, ct).ConfigureAwait(false);
            if (responses.Count == 0) return;

            var rows = await sheets.GetRowsAsync(sheetId, tab, ct).ConfigureAwait(false);
            var dikenal = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in rows)
            {
                var id = r.Get(WaFormKatalog.KolomResponseId)?.Trim();
                if (!string.IsNullOrEmpty(id)) dikenal.Add(id!);
            }

            var kolom = WaFormKatalog.KolomSheet;
            var tsIdx = WaFormKatalog.IndexKolom(WaFormKatalog.KolomTimestamp);
            var statusIdx = WaFormKatalog.IndexKolom(WaFormKatalog.KolomStatus);
            var idIdx = WaFormKatalog.IndexKolom(WaFormKatalog.KolomResponseId);
            var baru = new List<IList<object>>();

            foreach (var resp in responses)
            {
                var rid = resp.ResponseId;
                if (string.IsNullOrWhiteSpace(rid) || dikenal.Contains(rid!)) continue;

                var nilai = new string[kolom.Count];
                if (tsIdx >= 0 && !string.IsNullOrWhiteSpace(resp.CreateTimeRaw))
                    nilai[tsIdx] = resp.CreateTimeRaw!;

                foreach (var kv in resp.Answers ?? new Dictionary<string, Answer>())
                {
                    if (!peta.TryGetValue(kv.Key, out var judul)) continue;
                    var idx = WaFormKatalog.IndexKolom(judul);
                    if (idx < 0) continue;
                    var teks = GoogleFormsService.JawabanTeks(kv.Value);
                    if (!string.IsNullOrEmpty(teks)) nilai[idx] = teks;
                }

                if (statusIdx >= 0) nilai[statusIdx] = string.Empty;
                if (idIdx >= 0) nilai[idIdx] = rid!;
                baru.Add(nilai.Cast<object>().ToList());
                dikenal.Add(rid!);
            }

            if (baru.Count > 0)
            {
                await sheets.AppendRowsAsync(sheetId, tab, baru, ct).ConfigureAwait(false);
                _logger.LogInformation("Formulir: {Jumlah} jawaban baru disalin ke Sheet", baru.Count);
            }
        }

        /// <summary>Menulis status ke Sheet tanpa pernah melempar (pemrosesan utama jangan gagal karena status).</summary>
        private async Task TulisAmanAsync(GoogleSheetsService sheets, string sheetId, string tab,
            int rowNumber, string statusCol, string teks, CancellationToken ct)
        {
            try { await sheets.WriteCellAsync(sheetId, tab, rowNumber, statusCol, teks, ct).ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogWarning(ex, "Gagal menulis status Sheet baris {Row}", rowNumber); }
        }

        /// <summary>Memetakan seluruh kolom baris menjadi field format WA (label ternormalisasi).</summary>
        private static Dictionary<string, string> BuildFields(WaSheetRow row)
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "status", "token", "timestamp", "jenis", "jenissurat", "nowa", "nomorwhatsapp", "whatsapp", "responseid" };

            for (int i = 0; i < row.Headers.Length; i++)
            {
                var header = WaSheetRow.Normalize(row.Headers[i]);
                if (header.Length == 0 || skip.Contains(header)) continue;

                var key = WaFormatParser.NormalizeLabel(header);
                if (key.Length == 0 || skip.Contains(key)) continue;

                var value = i < row.Values.Length ? row.Values[i]?.Trim() : null;
                if (string.IsNullOrWhiteSpace(value)) continue;

                if (key == "ttl")
                {
                    fields["ttl"] = value;
                    fields["tempatlahir"] = WaFormatParser.TtlPisahTempat(value);
                    fields["tanggallahir"] = WaFormatParser.TtlPisahTanggal(value);
                }
                else
                {
                    fields[key] = value;
                }
            }

            return fields;
        }

        /// <summary>Membangun WargaData baru dari field (aturan sama dengan percakapan WhatsApp).</summary>
        private (WargaData? Warga, List<string> Missing) BuildWargaFromFields(
            Dictionary<string, string> fields, string nik, IDesaRepository desaRepo)
        {
            var tgLahir = WaFormatParser.NormalkanTanggal(fields.GetValueOrDefault("tanggallahir"));
            var jk = WaFormatParser.NormalkanJenisKelamin(fields.GetValueOrDefault("jk"));
            var agama = WaFormatParser.NormalkanAgama(fields.GetValueOrDefault("agama"));
            var status = WaFormatParser.NormalkanStatusPerkawinan(fields.GetValueOrDefault("statusperkawinan"));
            var pekerjaan = fields.GetValueOrDefault("pekerjaan");
            var nama = fields.GetValueOrDefault("nama");
            var alamat = fields.GetValueOrDefault("alamat") ?? fields.GetValueOrDefault("alamatlengkap");
            var tempatLahir = fields.GetValueOrDefault("tempatlahir");

            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(nama)) missing.Add("Nama");
            if (string.IsNullOrWhiteSpace(alamat)) missing.Add("Alamat");
            if (string.IsNullOrWhiteSpace(tempatLahir)) missing.Add("TTL (tempat lahir)");
            if (string.IsNullOrWhiteSpace(tgLahir)) missing.Add("Tanggal lahir (dd-mm-yyyy)");
            if (string.IsNullOrWhiteSpace(jk)) missing.Add("JK (L/P)");
            if (string.IsNullOrWhiteSpace(agama)) missing.Add("Agama");
            if (string.IsNullOrWhiteSpace(status)) missing.Add("Status perkawinan");
            if (string.IsNullOrWhiteSpace(pekerjaan)) missing.Add("Pekerjaan");

            if (missing.Count > 0) return (null, missing);

            DesaData? infoDesa = null;
            try { infoDesa = desaRepo.GetInfoDesaAsync().GetAwaiter().GetResult(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Gagal memuat InfoDesa saat mendaftarkan warga baru dari Sheet"); }

            var warga = new WargaData
            {
                NIK = nik,
                Nama = nama!.Trim(),
                AlamatLengkap = alamat!.Trim(),
                TempatLahir = tempatLahir!.Trim(),
                TanggalLahir = tgLahir!,
                JenisKelamin = jk!,
                Agama = agama!,
                StatusPerkawinan = status!,
                Pekerjaan = pekerjaan!.Trim(),
                Dusun = fields.GetValueOrDefault("dusun")?.Trim(),
                Desa = infoDesa?.NamaDesa ?? string.Empty,
                Kecamatan = infoDesa?.Kecamatan ?? string.Empty,
                Kabupaten = infoDesa?.Kabupaten ?? string.Empty,
                Pendidikan = WaFormatParser.NormalkanPendidikan(fields.GetValueOrDefault("pendidikan")) ?? string.Empty,
                Kewarganegaraan = WaFormatParser.NormalkanKewarganegaraan(fields.GetValueOrDefault("kewarganegaraan")) ?? "WNI"
            };

            return (warga, missing);
        }

        /// <summary>Menormalkan nomor WhatsApp dari Sheet: hanya digit, 62… → 0….</summary>
        private static string NormalisasiNomor(string nomor)
        {
            var digits = new string((nomor ?? string.Empty).Where(char.IsDigit).ToArray());
            if (digits.StartsWith("62") && digits.Length > 10) return "0" + digits.Substring(2);
            return digits;
        }

        private static string Potong(string? s, int max)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var clean = s.Replace('\n', ' ').Replace('\r', ' ').Trim();
            return clean.Length <= max ? clean : clean[..max] + "…";
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
