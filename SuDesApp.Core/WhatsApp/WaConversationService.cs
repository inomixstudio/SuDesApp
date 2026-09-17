using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace SuDesApp.WhatsApp
{
    /// <summary>Hasil pemrosesan satu pesan WhatsApp masuk.</summary>
    public class WaConversationResult
    {
        public string ReplyText { get; set; } = string.Empty;
        public PermintaanWa? CreatedRequest { get; set; }
    }

    /// <summary>
    /// Mesin percakapan WhatsApp untuk warga. Mengingatkan format, membaca data,
    /// memvalidasi, lalu menyimpan permintaan setelah konfirmasi "YA".
    /// Dipakai oleh WaEngine; dibuat per-scope agar repository aman.
    /// </summary>
    public class WaConversationService
    {
        private readonly WaSessionStore _sessions;
        private readonly IWargaRepository _wargaRepository;
        private readonly IDesaRepository _desaRepository;
        private readonly IPermintaanWaRepository _permintaanRepository;
        private readonly ILogger<WaConversationService> _logger;

        public WaConversationService(
            WaSessionStore sessions,
            IWargaRepository wargaRepository,
            IDesaRepository desaRepository,
            IPermintaanWaRepository permintaanRepository,
            ILogger<WaConversationService> logger)
        {
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            _wargaRepository = wargaRepository ?? throw new ArgumentNullException(nameof(wargaRepository));
            _desaRepository = desaRepository ?? throw new ArgumentNullException(nameof(desaRepository));
            _permintaanRepository = permintaanRepository ?? throw new ArgumentNullException(nameof(permintaanRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<WaConversationResult> ProcessAsync(string nomorWA, string text)
        {
            var nomor = CleanNumber(nomorWA);

            if (string.IsNullOrWhiteSpace(text))
            {
                return new WaConversationResult { ReplyText = WaFormatParser.InstruksiFormat };
            }

            var lower = text.Trim().ToLowerInvariant();

            // ================================================================
            // MODE TAUTAN (Google Form/Sheet) — pesan apa pun dari warga
            // dibalas tautan form untuk diisi sendiri. Tidak ada parse format
            // dan tidak ada konfirmasi YA/TIDAK di jalur ini.
            // ================================================================
            if (WaSheetOptions.IsLinkModeEnabled() && WaSheetOptions.IsConfigured())
            {
                var link = WaLinkBuilder.BuildFormLink();
                if (!string.IsNullOrWhiteSpace(link))
                {
                    return new WaConversationResult { ReplyText = await BuildLinkModeReplyAsync(nomor), CreatedRequest = null };
                }

                // Sheet/Form belum bisa membangun tautan (konfigurasi hilang saat
                // runtime) — jatuh ke mode percakapan agar warga tetap terlayani.
                _logger.LogWarning("Mode tautan aktif tetapi tautan form tidak dapat dibangun; jatuh ke mode percakapan.");
            }

            // Perintah FORMAT <jenis>: contoh format spesifik per jenis surat.
            if (lower.StartsWith("format ") || lower.StartsWith("/format "))
            {
                var kolom = text.IndexOf(' ');
                var cari = kolom >= 0 ? text[(kolom + 1)..] : string.Empty;
                var jenisFormat = WaFormatParser.DetectJenisSurat(cari);

                if (jenisFormat == null)
                {
                    return new WaConversationResult
                    {
                        ReplyText = "Jenis surat tidak dikenali.\n\n" + WaFormatParser.InstruksiFormat
                    };
                }

                if (WaFormatParser.IsOfflineOnly(jenisFormat))
                {
                    return new WaConversationResult
                    {
                        ReplyText = $"Jenis surat {WaFormatParser.TampilanJenis(jenisFormat)} belum bisa diproses online. Silakan datang ke kantor desa."
                    };
                }

                return new WaConversationResult { ReplyText = WaFormatParser.FormatTemplate(jenisFormat) };
            }

            // Perintah bantuan / menu
            if (lower is "halo" or "hallo" or "assalamualaikum" or "menu" or "/menu"
                or "bantuan" or "/bantuan" or "format" or "/format")
            {
                return new WaConversationResult { ReplyText = WaFormatParser.InstruksiFormat };
            }

            // Menunggu konfirmasi dari permintaan sebelumnya
            var pending = _sessions.Get(nomor);
            if (pending != null)
            {
                if (lower is "ya" or "y" or "iya" or "oke" or "ok" or "sip")
                {
                    _sessions.Remove(nomor);
                    return await FinalizeAsync(nomor, pending.Data, pending.LastRawText);
                }

                if (lower is "tidak" or "t" or "batal" or "nggak" or "no")
                {
                    _sessions.Remove(nomor);
                    _logger.LogInformation("Permintaan dibatalkan oleh {Nomor}", nomor);
                    return new WaConversationResult
                    {
                        ReplyText = "Permohonan dibatalkan.\n\n" + WaFormatParser.InstruksiFormat
                    };
                }

                return new WaConversationResult
                {
                    ReplyText = "Permohonan Anda belum dikirim. Balas YA untuk mengirim, atau TIDAK untuk membatalkan.",
                };
            }

            // Pesan baru: deteksi jenis surat
            var parsed = WaFormatParser.Parse(text);
            if (parsed.NamaJenis == null)
            {
                return new WaConversationResult
                {
                    ReplyText = "Jenis surat tidak dikenali. Pastikan baris pertama berisi nama surat.\n\n" + WaFormatParser.InstruksiFormat
                };
            }

            if (WaFormatParser.IsOfflineOnly(parsed.NamaJenis))
            {
                return new WaConversationResult
                {
                    ReplyText = $"Jenis surat {WaFormatParser.TampilanJenis(parsed.NamaJenis)} belum bisa diproses online. Silakan datang ke kantor desa dengan membawa data yang diperlukan."
                };
            }

            // Resolusi warga
            WargaData warga;
            bool isInstansi = parsed.NamaJenis.Equals(SuratConstants.INSTANSI, StringComparison.OrdinalIgnoreCase);

            // Kelengkapan sesuai form generator — SEMUA field wajib (termasuk
            // data pribadi), berlaku juga saat NIK sudah terdaftar.
            var missingFmt = WaFormatParser.CekKelengkapan(parsed.NamaJenis, parsed.Fields, parsed.NIK);
            if (missingFmt.Count > 0)
            {
                var daftarMissing = string.Join("\n", missingFmt.Select(m => "• " + m));
                return new WaConversationResult
                {
                    ReplyText = $"Format untuk {WaFormatParser.TampilanJenis(parsed.NamaJenis)} belum lengkap.\n" +
                                "Lengkapi:\n" + daftarMissing + ".\n\n" + WaFormatParser.FormatTemplate(parsed.NamaJenis)
                };
            }

            if (isInstansi)
            {
                warga = new WargaData
                {
                    NIK = SuratConstants.NIK_INSTANSI,
                    Nama = parsed.Fields.GetValueOrDefault("nama") ?? string.Empty,
                    Alamat = parsed.Fields.GetValueOrDefault("alamat") ?? string.Empty,
                    TempatLahir = "N/A",
                    TanggalLahir = "1900-01-01",
                    JenisKelamin = "N/A",
                    Agama = "N/A",
                    StatusPerkawinan = "N/A",
                    Pekerjaan = string.Empty,
                    Pendidikan = "N/A",
                    Kewarganegaraan = "N/A",
                    IsForInstansi = true
                };
            }
            else
            {
                var nik = parsed.NIK ?? string.Empty;
                var dbWarga = await _wargaRepository.GetWargaByNikAsync(nik);
                if (dbWarga != null)
                {
                    warga = dbWarga;
                }
                else
                {
                    var (wargaBaru, missing) = await BuildWargaFromFieldsAsync(parsed.Fields, nik);
                    if (missing.Count > 0)
                    {
                        return new WaConversationResult
                        {
                            ReplyText = "Data NIK belum terdaftar. Mohon lengkapi format dengan:\n" +
                                        string.Join(", ", missing) + ".\n\n" + WaFormatParser.FormatTemplate(parsed.NamaJenis)
                        };
                    }
                    warga = wargaBaru!;
                }
            }

            var keperluan = parsed.Fields.GetValueOrDefault("keperluan")
                            ?? DefaultKeperluan(parsed.NamaJenis);

            var requestData = new WaRequestData
            {
                NamaJenis = parsed.NamaJenis,
                Keperluan = keperluan,
                NomorWA = nomor,
                Warga = warga,
                Fields = parsed.Fields
            };

            _sessions.Set(nomor, new PendingWaRequest { Data = requestData, LastRawText = text });

            return new WaConversationResult
            {
                ReplyText = "Data permohonan:\n" + BuildSummary(requestData) +
                            "\n\nBalas:  YA  untuk mengirim permohonan, atau  TIDAK  untuk membatalkan."
            };
        }

        /// <summary>
        /// Mode tautan (Google Form/Sheet): membangun balasan untuk PESAN PERTAMA
        /// warga berisi tautan form + token penghubung, dan menyimpan permintaan
        /// placeholder MENUNGGU_FORM agar balasan PDF bisa dikirim ke nomor yang sama.
        /// Pesan lanjutan (warga sudah punya token) hanya mengulang tautan + token.
        /// </summary>
        private async Task<string> BuildLinkModeReplyAsync(string nomor)
        {
            try
            {
                var link = WaLinkBuilder.BuildFormLink() ?? string.Empty;

                // Cari permintaan MENUNGGU_FORM yang masih terbuka untuk nomor ini.
                var repo = _permintaanRepository;
                PermintaanWa? placeholder = null;
                try
                {
                    var semua = await repo.GetAllAsync(WaRequestStatus.MENUNGGU_FORM);
                    placeholder = semua.FirstOrDefault(p => p.NomorWA == nomor);
                }
                catch (Exception exLookup)
                {
                    _logger.LogWarning(exLookup, "Gagal mencari permintaan MENUNGGU_FORM untuk {Nomor}", nomor);
                }

                if (placeholder != null)
                {
                    var tokenLama = placeholder.SheetToken ?? WaLinkBuilder.NewToken();
                    var linkLama = WaLinkBuilder.BuildPrefilledLink(tokenLama) ?? link;
                    return "🏛️ LAYANAN SURAT ONLINE DESA\n\n" +
                           "Anda punya pengajuan yang belum selesai. Silakan lanjutkan pengisian form:\n" +
                           linkLama + "\n\n" + WaLinkBuilder.TokenReminder(tokenLama) +
                           "Setelah form tersimpan, surat PDF otomatis dibuat dan tautan unduh " +
                           "dikirim kembali ke WhatsApp ini.";
                }

                // Belum ada → buat token + permintaan placeholder MENUNGGU_FORM.
                var token = WaLinkBuilder.NewToken();

                try
                {
                    var kode = await _permintaanRepository.NextKodePermintaanAsync(DateTime.Now.Year);

                    var data = new WaRequestData
                    {
                        NamaJenis = string.Empty, // diisi saat form tersimpan (ingest Sheet)
                        NomorWA = nomor,
                        Fields = { ["token"] = token }
                    };

                    var placeholderBaru = new PermintaanWa
                    {
                        KodePermintaan = kode,
                        NomorWA = nomor,
                        NamaJenis = "(menunggu form)",
                        PesanMentah = "Tautan form dikirim ke warga (mode Google Sheet).",
                        DataJson = JsonSerializer.Serialize(data),
                        Status = WaRequestStatus.MENUNGGU_FORM,
                        // Tidak menaikkan lonceng operator — placeholder bukan
                        // permintaan yang butuh tindakan; otomatis ditutup saat
                        // warga menyelesaikan form (ingest Sheet).
                        IsRead = true,
                        TanggalPermintaan = DateTime.Now,
                        Sumber = WaRequestStatus.SumberGoogleSheet,
                        SheetToken = token
                    };

                    await _permintaanRepository.InsertAsync(placeholderBaru);
                    _logger.LogInformation("Placeholder MENUNGGU_FORM {Kode} dibuat untuk {Nomor} (token {Token})",
                        kode, nomor, token);
                }
                catch (Exception exInsert)
                {
                    // Warga tetap menerima tautan + token walau penyimpanan gagal;
                    // ingest Sheet bisa tetap memproses via kolom No. WhatsApp di form.
                    _logger.LogWarning(exInsert, "Gagal menyimpan placeholder MENUNGGU_FORM untuk {Nomor}", nomor);
                }

                return "🏛️ LAYANAN SURAT ONLINE DESA\n\n" +
                       "Silakan isi form permohonan surat melalui tautan berikut:\n" +
                       WaLinkBuilder.BuildPrefilledLink(token) + "\n\n" +
                       WaLinkBuilder.TokenReminder(token) +
                       "Setelah form tersimpan, surat PDF otomatis dibuat dan tautan unduh " +
                       "dikirim kembali ke WhatsApp ini.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membangun balasan mode tautan untuk {Nomor}", nomor);
                return "Maaf, terjadi kendala menyiapkan form. Silakan coba beberapa saat lagi.";
            }
        }

        private async Task<WaConversationResult> FinalizeAsync(string nomor, WaRequestData data, string rawText)
        {
            try
            {
                var kode = await _permintaanRepository.NextKodePermintaanAsync(DateTime.Now.Year);

                var permintaan = new PermintaanWa
                {
                    KodePermintaan = kode,
                    NomorWA = nomor,
                    NamaWarga = data.Warga?.Nama,
                    NIK = data.Warga?.NIK,
                    NamaJenis = data.NamaJenis,
                    PesanMentah = rawText,
                    DataJson = JsonSerializer.Serialize(data),
                    Status = WaRequestStatus.BARU,
                    IsRead = false,
                    TanggalPermintaan = DateTime.Now,
                    Sumber = WaRequestStatus.SumberWhatsApp
                };

                await _permintaanRepository.InsertAsync(permintaan);
                _logger.LogInformation("Permintaan baru {Kode} dari {Nomor}: {Jenis}", kode, nomor, data.NamaJenis);

                // Balasan menyesuaikan jam layanan otomatis: di luar jam kerja
                // pemohon diberi tahu kapan pemrosesan otomatis dimulai kembali.
                var buka = AppPreferenceStore.GetWaServiceOpen();
                var tutup = AppPreferenceStore.GetWaServiceClose();
                var hari = AppPreferenceStore.GetWaServiceDays();
                var menitSampaiBuka = WaServiceHours.MenitSampaiBuka(DateTime.Now, buka, tutup, hari);

                string infoWaktu = menitSampaiBuka == null
                    ? "Permohonan Anda sedang diproses otomatis dan surat siap akan dikirim " +
                      "kembali melalui WhatsApp ini."
                    : $"Layanan otomatis sedang tutup (jam layanan {WaServiceHours.RentangTampil(buka, tutup, hari)}). " +
                      $"Permohonan Anda tercatat dan akan diproses otomatis pada jam layanan " +
                      $"(± {FormatDurasi(menitSampaiBuka.Value)} lagi).";

                return new WaConversationResult
                {
                    ReplyText = $"✅ Permohonan {WaFormatParser.TampilanJenis(data.NamaJenis)} telah diterima.\n" +
                                $"Kode: {kode}\n\n" + infoWaktu,
                    CreatedRequest = permintaan
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyimpan permintaan WhatsApp dari {Nomor}", nomor);
                return new WaConversationResult
                {
                    ReplyText = "Maaf, terjadi kendala sistem saat menerima permohonan. Silakan coba beberapa saat lagi."
                };
            }
        }

        /// <summary>Membangun WargaData baru dari kolom format.</summary>
        private async Task<(WargaData? Warga, List<string> Missing)> BuildWargaFromFieldsAsync(
            Dictionary<string, string> fields, string nik)
        {
            var tgLahir = WaFormatParser.NormalkanTanggal(
                fields.TryGetValue("tanggallahir", out var tgl) ? tgl : null);
            var jk = WaFormatParser.NormalkanJenisKelamin(fields.GetValueOrDefault("jk"));
            var agama = WaFormatParser.NormalkanAgama(fields.GetValueOrDefault("agama"));
            var status = WaFormatParser.NormalkanStatusPerkawinan(fields.GetValueOrDefault("statusperkawinan"));
            var pekerjaan = fields.GetValueOrDefault("pekerjaan");
            var nama = fields.GetValueOrDefault("nama");
            var alamat = fields.GetValueOrDefault("alamat") ?? fields.GetValueOrDefault("alamatlengkap");
            var tempatLahir = fields.GetValueOrDefault("tempatlahir");
            var dusun = fields.GetValueOrDefault("dusun");

            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(nama)) missing.Add("Nama");
            if (string.IsNullOrWhiteSpace(alamat)) missing.Add("Alamat");
            if (string.IsNullOrWhiteSpace(tempatLahir)) missing.Add("TTL (tempat & tanggal lahir)");
            if (string.IsNullOrWhiteSpace(tgLahir)) missing.Add("TTL (tanggal lahir dd-mm-yyyy)");
            if (string.IsNullOrWhiteSpace(jk)) missing.Add("JK (L/P)");
            if (string.IsNullOrWhiteSpace(agama)) missing.Add("Agama");
            if (string.IsNullOrWhiteSpace(status)) missing.Add("Status perkawinan");
            if (string.IsNullOrWhiteSpace(pekerjaan)) missing.Add("Pekerjaan");

            if (missing.Count > 0) return (null, missing);

            DesaData? infoDesa = null;
            try { infoDesa = await _desaRepository.GetInfoDesaAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Gagal memuat InfoDesa saat mendaftarkan warga baru"); }

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
                Dusun = dusun?.Trim(),
                Desa = infoDesa?.NamaDesa ?? string.Empty,
                Kecamatan = infoDesa?.Kecamatan ?? string.Empty,
                Kabupaten = infoDesa?.Kabupaten ?? string.Empty,
                Pendidikan = WaFormatParser.NormalkanPendidikan(fields.GetValueOrDefault("pendidikan")) ?? string.Empty,
                Kewarganegaraan = WaFormatParser.NormalkanKewarganegaraan(fields.GetValueOrDefault("kewarganegaraan")) ?? "WNI"
            };

            return (warga, missing);
        }

        private static string FormatDurasi(int totalMenit)
        {
            var jam = totalMenit / 60;
            var menit = totalMenit % 60;
            if (jam > 0 && menit > 0) return $"{jam} jam {menit} menit";
            if (jam > 0) return $"{jam} jam";
            return $"{menit} menit";
        }

        private static string DefaultKeperluan(string namaJenis)
            => namaJenis.ToUpperInvariant() switch
            {
                SuratConstants.SKTM => "Keterangan Tidak Mampu",
                SuratConstants.PENGANTAR_SKCK => "Persyaratan",
                SuratConstants.SKU => "Persyaratan",
                SuratConstants.IZIN_ORTU => "Izin Bekerja ke Luar Negeri",
                SuratConstants.SKD_UMUM => "Surat Keterangan Desa",
                SuratConstants.DOMISILI_WARGA => "Surat Keterangan Domisili",
                SuratConstants.INSTANSI => "Persyaratan",
                _ => "Permohonan " + WaFormatParser.TampilanJenis(namaJenis)
            };

        private string BuildSummary(WaRequestData d)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("• Jenis: ").Append(WaFormatParser.TampilanJenis(d.NamaJenis)).AppendLine();
            sb.Append("• Keperluan: ").Append(d.Keperluan).AppendLine();

            if (d.NamaJenis.Equals(SuratConstants.INSTANSI, StringComparison.OrdinalIgnoreCase))
            {
                sb.Append("• Nama Instansi: ").Append(d.Fields.GetValueOrDefault("nama")).AppendLine();
                sb.Append("• Alamat Instansi: ").Append(d.Fields.GetValueOrDefault("alamat")).AppendLine();
                return sb.ToString();
            }

            var w = d.Warga;
            sb.Append("• NIK: ").Append(w?.NIK).AppendLine();
            sb.Append("• Nama: ").Append(w?.Nama).AppendLine();
            sb.Append("• Alamat: ").Append(w?.AlamatLengkap).AppendLine();
            sb.Append("• TTL: ").Append(w?.TempatLahir).Append(", ")
              .Append(FormatTglTampil(w?.TanggalLahir)).AppendLine();
            sb.Append("• JK: ").Append(w?.JenisKelamin).Append("   • Agama: ").Append(w?.Agama).AppendLine();
            sb.Append("• Status: ").Append(w?.StatusPerkawinan).Append("   • Pekerjaan: ").Append(w?.Pekerjaan).AppendLine();

            foreach (var item in d.Fields)
            {
                if (item.Key is "nama" or "alamat" or "alamatlengkap" or "tanggallahir" or "tempatlahir"
                    or "jk" or "agama" or "statusperkawinan" or "pekerjaan" or "nik" or "ttl" or "keperluan"
                    or "dusun" or "pendidikan" or "kewarganegaraan") continue;
                sb.Append("• ").Append(HumanLabel(item.Key)).Append(": ").Append(item.Value).AppendLine();
            }

            return sb.ToString();
        }

        private static string HumanLabel(string key)
            => key switch
            {
                "negaratujuan" => "Negara Tujuan",
                "namapt" => "Nama Perusahaan",
                "bidangusaha" => "Bidang Usaha",
                "sejaktahun" => "Sejak Tahun",
                "keterangan" => "Keterangan",
                _ => key
            };

        private static string FormatTglTampil(string? tglDb)
        {
            if (DateTime.TryParse(tglDb, out var dt))
                return dt.ToString("dd-MM-yyyy");
            return tglDb ?? string.Empty;
        }

        private static string CleanNumber(string nomor)
        {
            var digits = new string((nomor ?? string.Empty).Where(char.IsDigit).ToArray());
            if (digits.StartsWith("62") && digits.Length > 10) return "0" + digits.Substring(2);
            return digits;
        }
    }
}