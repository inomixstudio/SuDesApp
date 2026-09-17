using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using SuDesApp.Wpf.ViewModels;

internal static class Program
{
    private static async Task<int> Main()
    {
        var vmType = typeof(FormulirViewModel);

        int fails = 0;

        // --- GetFormCode ---
        var getFormCode = vmType.GetMethod("GetFormCode", BindingFlags.Public | BindingFlags.Static);
        object?[] codeCases =
        {
            "F-1.01, Formulir Biodata Keluarga", // F-1.01
            "Formulir F-1.02, digunakan untuk ...", // F-1.02
            "Formulir F-1.15 Penduduk Nonpermanen", // F-1.15
            "Formulir Tambahan Lainnya",            // null
        };
        string?[] codeExpected = { "F-1.01", "F-1.02", "F-1.15", null };
        for (int i = 0; i < codeCases.Length; i++)
        {
            var got = (string?)getFormCode!.Invoke(null, new[] { codeCases[i] });
            if (!string.Equals(got, codeExpected[i], StringComparison.Ordinal))
            {
                fails++;
                Console.WriteLine($"FAIL GetFormCode(\"{codeCases[i]}\") => {got ?? "null"} (harusnya {codeExpected[i] ?? "null"})");
            }
        }

        // --- BuildTemplateBaseName (judul asli halaman sumber) ---
        var build = vmType.GetMethod("BuildTemplateBaseName", BindingFlags.Public | BindingFlags.Static);
        (string Title, bool Shared, string Expected)[] baseCases =
        {
            ("F-1.01, Formulir Biodata Keluarga", false, "F-1.01"),
            ("Formulir F-1.02, digunakan untuk melengkapi pengajuan dokumen Kartu Keluarga & KTP-el", false, "F-1.02"),
            ("Formulir F-1.03, digunakan untuk melengkapi pengajuan Pindah Alamat", false, "F-1.03"),
            ("Formulir F-1.04, Surat Pernyataan Tidak Memiliki Dokumen Kependudukan", false, "F-1.04"),
            ("Formulir F-1.05, digunakan untuk ...", false, "F-1.05"),
            ("Formulir F-1.06, digunakan untuk ...", false, "F-1.06"),
            ("Formulir F-1.15 Penduduk Nonpermanen", false, "F-1.15"),
            ("F-2.01 Akta Kelahiran, digunakan untuk ...", true, "F-2.01_AKTA_KELAHIRAN"),
            ("F-2.01 Akta Kematian, digunakan untuk ...", true, "F-2.01_AKTA_KEMATIAN"),
            ("F-2.01 Akta Perkawinan (Non-Muslim)", true, "F-2.01_AKTA_PERKAWINAN_NON-MUSLIM"),
            ("F-2.01 Perceraian (Non-Muslim)", true, "F-2.01_PERCERAIAN_NON-MUSLIM"),
            ("F-2.01 Perubahan/ Pembetulan Akta Kelahiran", true, "F-2.01_PERUBAHAN_PEMBETULAN_AKTA_KELAHIRAN"),
            ("Formulir SPTJM Kebenaran Data Kematian", false, "FORMULIR_SPTJM_KEBENARAN_DATA_KEMATIAN"),
        };
        foreach (var (title, shared, want) in baseCases)
        {
            var got = (string)build!.Invoke(null, new object[] { title, shared })!;
            if (!string.Equals(got, want, StringComparison.Ordinal))
            {
                fails++;
                Console.WriteLine($"FAIL BuildTemplateBaseName(\"{title}\", {shared}) => {got} (harusnya {want})");
            }
        }

        // --- DetectJenisSurat (layanan online WhatsApp) ---
        var detect = typeof(SuDesApp.WhatsApp.WaFormatParser).GetMethod("DetectJenisSurat");
        (string Pesan, string? Expected)[] detectCases =
        {
            ("SKTM\nNIK: 3273010101010001\nAlamat: perumahan dekat SKCK", "SKTM"),
            ("format SKU\nNIK: 3273010101010001\nAlamat: Jalan Sudirman", "SKU"),
            (" halo", null),                          // bukan pesan berformat
            ("Saya mau buat surat keterangan usaha\nNIK: 3273010101010001", "SKU"),
            ("PENGANTAR SKCK\nNIK: 3273010101010001\nPendidikan: SMA\nKewarganegaraan: WNI", "PENGANTAR_SKCK"),
        };
        foreach (var (pesan, want) in detectCases)
        {
            var got = (string?)detect!.Invoke(null, new object[] { pesan });
            if (!string.Equals(got, want, StringComparison.Ordinal))
            {
                fails++;
                Console.WriteLine($"FAIL DetectJenisSurat(\"{pesan.Split('\n')[0]}…\") => {got ?? "null"} (harusnya {want ?? "null"})");
            }
        }

        // --- WaServiceHours (jam + hari layanan) ---
        var svc = typeof(SuDesApp.WhatsApp.WaServiceHours);
        var isOpen = svc.GetMethod("IsOpen");
        var rentang = svc.GetMethod("RentangTampil");
        var menit = svc.GetMethod("MenitSampaiBuka");

        // Kalender Sept 2026: Rab 16-09, Kam 17-09, Jum 18-09, Sab 19-09, Min 20-09, Sen 21-09.
        (DateTime Now, string? Buka, string? Tutup, string? Hari, bool Want)[] openCases =
        {
            (new DateTime(2026, 9, 16, 10, 0, 0), "08:00", "16:00", "1,2,3,4,5", true),   // Rabu 10:00
            (new DateTime(2026, 9, 16, 17, 0, 0), "08:00", "16:00", "1,2,3,4,5", false),  // Rabu 17:00
            (new DateTime(2026, 9, 19, 10, 0, 0), "08:00", "16:00", "1,2,3,4,5", false),  // Sabtu 10:00 (libur)
            (new DateTime(2026, 9, 20, 10, 0, 0), "08:00", "16:00", "1,2,3,4,5", false),  // Minggu (libur)
            (new DateTime(2026, 9, 21, 10, 0, 0), "08:00", "16:00", "1,2,3,4,5", true),   // Senin 10:00
            (new DateTime(2026, 9, 19, 10, 0, 0), null, null, null, true),                 // 24 jam
            (new DateTime(2026, 9, 18, 23, 0, 0), "20:00", "06:00", null, true),          // lintas tengah malam
        };
        foreach (var c in openCases)
        {
            var got = (bool)isOpen!.Invoke(null, new object?[] { c.Now, c.Buka, c.Tutup, c.Hari })!;
            if (got != c.Want)
            {
                fails++;
                Console.WriteLine($"FAIL IsOpen({c.Now:ddd HH:mm}, {c.Buka}-{c.Tutup}, hari={c.Hari}) => {got} (harusnya {c.Want})");
            }
        }

        // MenitSampaiBuka: Kamis (17-09) 17:00 → Jumat 08:00 = 900 menit.
        var m1 = (int?)menit!.Invoke(null, new object?[]
            { new DateTime(2026, 9, 17, 17, 0, 0), "08:00", "16:00", "1,2,3,4,5" });
        if (m1 != 900)
        {
            fails++;
            Console.WriteLine($"FAIL MenitSampaiBuka(Kamis 17:00) => {m1} (harusnya 900)");
        }

        // Jumat (18-09) 17:00 → Senin 08:00 = 3780 menit (melewati Sabtu & Minggu libur).
        var m2 = (int?)menit!.Invoke(null, new object?[]
            { new DateTime(2026, 9, 18, 17, 0, 0), "08:00", "16:00", "1,2,3,4,5" });
        if (m2 != 3780)
        {
            fails++;
            Console.WriteLine($"FAIL MenitSampaiBuka(Jumat 17:00) => {m2} (harusnya 3780)");
        }

        // Tampilan: hari default + rentang jam; dan 24 jam penuh.
        var r1 = (string)rentang!.Invoke(null, new object?[] { "08:00", "16:00", "1,2,3,4,5" })!;
        if (r1 != "Senin–Jumat, 08:00–16:00")
        {
            fails++;
            Console.WriteLine($"FAIL RentangTampil(default) => '{r1}'");
        }
        var r2 = (string)rentang!.Invoke(null, new object?[] { null, null, null })!;
        if (r2 != "24 jam")
        {
            fails++;
            Console.WriteLine($"FAIL RentangTampil(24jam) => '{r2}'");
        }

        // --- ParseFormulirEntries terhadap HTML asli (jika file tersedia) ---
        var htmlPath = FindFileUpwards("jenis-formulir.html");
        if (htmlPath != null)
        {
            var parse = vmType.GetMethod("ParseFormulirEntries", BindingFlags.NonPublic | BindingFlags.Static);
            var entries = (System.Collections.IEnumerable)parse!.Invoke(null, new object[] { File.ReadAllText(htmlPath) })!;
            var list = new List<(string Name, string Url, string Base)>();
            foreach (var e in entries)
            {
                var t = e.GetType();
                list.Add((
                    (string)t.GetProperty("NamaFormulir")!.GetValue(e)!,
                    (string)t.GetProperty("LinkDownload")!.GetValue(e)!,
                    (string)t.GetProperty("BaseFileName")!.GetValue(e)!));
            }

            Console.WriteLine($"Parsed {list.Count} entri dari HTML asli");
            if (list.Count != 20)
            {
                fails++;
                Console.WriteLine($"FAIL jumlah entri = {list.Count} (harusnya 20)");
            }
            if (list.Any(e => !e.Url.Contains("export=download")))
            {
                fails++;
                Console.WriteLine("FAIL ada URL unduhan yang bukan format direct-download Drive");
            }
            if (list.Select(e => e.Base).Distinct(StringComparer.OrdinalIgnoreCase).Count() != list.Count)
            {
                fails++;
                Console.WriteLine("FAIL ada nama dasar file yang duplikat");
            }

            foreach (var e in list)
            {
                Console.WriteLine($"  [{e.Base}] {e.Name} -> {e.Url}");
            }
        }
        else
        {
            Console.WriteLine("jenis-formulir.html tidak ditemukan - lewati uji parsing HTML asli");
        }

        // --- AppConfig: jenis surat dimuat dari JenisSuratConfig.json (satu sumber kebenaran) ---
        var appSettingsPath = FindFileUpwards("appsettings.json");
        if (appSettingsPath != null)
        {
            var cfg = (SuDesApp.AppConfig)Activator.CreateInstance(
                typeof(SuDesApp.AppConfig), appSettingsPath, null)!;

            if (cfg.SuratNumberFormats.Count != 17 || cfg.TemplateNames.Count != 17)
            {
                fails++;
                Console.WriteLine($"FAIL AppConfig jenis surat: formats={cfg.SuratNumberFormats.Count}, names={cfg.TemplateNames.Count} (harusnya 17)");
            }

            // Pencarian format harus case-insensitive (NamaJenis JSON PascalCase, pemakai kadang uppercase)
            if (cfg.GetSuratNumberFormat("KEMATIAN") != "570/{0:D3}/Ds/{2:yyyy}" ||
                cfg.GetSuratNumberFormat("ntcr_n4") != "474.1/{0:D3}/Ds/{2:yyyy}")
            {
                fails++;
                Console.WriteLine("FAIL GetSuratNumberFormat case-insensitive");
            }

            if (!cfg.IsValidTemplate("SKD_UMUM") || cfg.IsValidTemplate("JENIS_PALSU"))
            {
                fails++;
                Console.WriteLine("FAIL IsValidTemplate");
            }

            // Penomoran bersama (IsSharedNumbering) dari JenisSuratConfig.json
            if (cfg.IsSharedNumbering("SKD_UMUM") != true || cfg.IsSharedNumbering("kematian") != false)
            {
                fails++;
                Console.WriteLine("FAIL IsSharedNumbering (SKD_UMUM=true, KEMATIAN=false)");
            }

            var shared = cfg.GetSharedNumberingNames().ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (shared.Count != 9)
            {
                fails++;
                Console.WriteLine($"FAIL anggota penomoran bersama = {shared.Count} (harusnya 9)");
            }
            foreach (var anggota in new[] { "SKD_UMUM", "DOMISILI_WARGA", "INSTANSI", "PENGANTAR_SKCK",
                                            "SKTM", "BEDANAMA", "KENAL_LAHIR", "AHLI_WARIS", "IJIN_TINGGAL" })
            {
                if (!shared.Contains(anggota))
                {
                    fails++;
                    Console.WriteLine($"FAIL anggota penomoran bersama tidak ada: {anggota}");
                }
            }
            if (shared.Contains("GARAPAN_SAWAH"))
            {
                fails++;
                Console.WriteLine("FAIL GARAPAN_SAWAH tidak boleh anggota penomoran bersama (IsSharedNumbering=false)");
            }

            Console.WriteLine($"AppConfig: penomoran bersama {shared.Count} jenis (dari JenisSuratConfig.json)");

            Console.WriteLine($"AppConfig: {cfg.TemplateNames.Count} jenis surat dimuat dari JenisSuratConfig.json");
        }
        else
        {
            Console.WriteLine("appsettings.json tidak ditemukan - lewati uji AppConfig");
        }

        // CATATAN: TIDAK ADA tes yang menulis ke data nyata di luar repo
        // (AppData, kredensial Google, database). Dulu ada tes roundtrip
        // GoogleClientCredentials di sini — dihapus permanen karena Save()
        // menimpa kredensial asli aplikasi dan mematahkan login Google.

        // --- Generator surat: PDF dirender langsung dengan QuestPDF ---
        {
            var appSettingsPath2 = FindFileUpwards("appsettings.json");
            if (appSettingsPath2 != null)
            {
                try
                {
                    var cfg = (SuDesApp.AppConfig)Activator.CreateInstance(
                        typeof(SuDesApp.AppConfig), appSettingsPath2, null)!;

                    var desaRepo = new FakeDesaRepository();
                    var suratRepo = new FakeSuratRepository();
                    var settingsManager = new SuDesApp.ControlSurat.SettingsManager(desaRepo, cfg);

                    var generator = new SuDesApp.GeneratorPdf.SKDGenerator(
                        cfg,
                        new SuDesApp.Utilities.FileService(cfg, Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Utilities.FileService>.Instance),
                        desaRepo,
                        suratRepo,
                        settingsManager,
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.GeneratorPdf.SKDGenerator>.Instance,
                        Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);

                    using var pdfStream = new MemoryStream();
                    await generator.GeneratePdfAsync(pdfStream, suratRepo.SampleSurat, "Uji migrasi QuestPDF");

                    var bytes = pdfStream.ToArray();
                    var magic = System.Text.Encoding.ASCII.GetString(bytes.Take(4).ToArray());
                    if (pdfStream.Length < 1000 || magic != "%PDF")
                    {
                        fails++;
                        Console.WriteLine($"FAIL generator surat (QuestPDF): {pdfStream.Length} bytes, magic='{magic}'");
                    }
                    else
                    {
                        Console.WriteLine($"Generator surat (QuestPDF): PDF valid, {pdfStream.Length} bytes, kertas " +
                            $"{SuDesApp.Utilities.PengaturanCetak.LabelUkuranKertas(SuDesApp.Utilities.PengaturanCetak.GetUkuranKertas())}");
                    }
                }
                catch (Exception ex)
                {
                    fails++;
                    Console.WriteLine($"FAIL generator surat (QuestPDF): {ex.GetType().Name}: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine("appsettings.json tidak ditemukan - lewati uji generator");
            }
        }

        // --- Template Google Sheet per jenis surat (menulis ke folder temp saja) ---
        {
            try
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "SuDesApp-Smoke-Template");
                var allPath = SuDesApp.Utilities.WaSheetTemplateService.BuatTemplateFile("SEMUA", tempDir);
                var info = new FileInfo(allPath);
                var valid = info.Exists && info.Length > 4000;

                var skuPath = SuDesApp.Utilities.WaSheetTemplateService.BuatTemplateFile("SKU", tempDir);
                var skuInfo = new FileInfo(skuPath);

                // Header tab pertama berkas SEMUA harus memuat kolom inti.
                using var pkg = new OfficeOpenXml.ExcelPackage(skuInfo);
                var ws = pkg.Workbook.Worksheets[0];
                var headers = new List<string>();
                for (int c = 1; c <= ws.Dimension.End.Column; c++)
                    headers.Add(ws.Cells[1, c].Text.Trim());
                string[] wajib = { "Jenis Surat", "NIK", "Nama", "Tanggal Lahir", "No. WhatsApp", "Status", "Bidang Usaha" };
                var kurang = wajib.Where(w => !headers.Contains(w)).ToList();

                if (!valid || !skuInfo.Exists || kurang.Count > 0)
                {
                    fails++;
                    Console.WriteLine($"FAIL template Sheet: file={(valid ? "ok" : "tidak valid")}, kolom kurang: {string.Join(", ", kurang)}");
                }
                else
                {
                    Console.WriteLine($"Template Google Sheet: berkas SEMUA {info.Length / 1024} KB + tab SKU kolom lengkap ({headers.Count} kolom)");
                }

                try { Directory.Delete(tempDir, recursive: true); } catch { /* temp */ }
            }
            catch (Exception ex)
            {
                fails++;
                Console.WriteLine($"FAIL template Sheet: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // --- Pengaturan desa: tersimpan ke database DAN cache desa langsung dibersihkan ---
        // Tanpa pembersihan cache, halaman lain masih memakai nama desa lama sampai 1 jam
        // (mis. "Aula Kantor Desa …" di Daftar Hadir dan kop seluruh surat).
        {
            var appSettingsPath3 = FindFileUpwards("appsettings.json");
            if (appSettingsPath3 != null)
            {
                string dbPath = Path.Combine(Path.GetTempPath(), $"SuDesApp-Smoke-Desa-{Guid.NewGuid():N}.db");
                try
                {
                    string connectionString = $"Data Source={dbPath}";
                    using (var conn = new Microsoft.Data.Sqlite.SqliteConnection(connectionString))
                    {
                        conn.Open();
                        var buatTabel = conn.CreateCommand();
                        buatTabel.CommandText = "CREATE TABLE InfoDesa (NamaDesa TEXT, Kecamatan TEXT, Kabupaten TEXT, " +
                                                "Alamat TEXT, Kodepos TEXT, KepalaDesa TEXT, SekretarisDesa TEXT, " +
                                                "NamaCamat TEXT, NipCamat TEXT, GolCamat TEXT)";
                        buatTabel.ExecuteNonQuery();
                    }

                    var cfg3 = (SuDesApp.AppConfig)Activator.CreateInstance(typeof(SuDesApp.AppConfig), appSettingsPath3, null)!;
                    cfg3.DatabaseConnectionString = connectionString;

                    var repo3 = new FakeDesaRepository();
                    var settingsManager3 = new SuDesApp.ControlSurat.SettingsManager(repo3, cfg3);
                    settingsManager3.SaveSettingsAsync(new SuDesApp.Data.Models.DesaData
                    {
                        NamaDesa = "Sumberjaya",
                        Kecamatan = "Tempuran",
                        Kabupaten = "Karawang",
                        Alamat = "Jl. Raya Sumberjaya No. 1",
                        Kodepos = "41361",
                        KepalaDesa = "H. Suryana"
                    }).GetAwaiter().GetResult();

                    string tersimpan;
                    using (var conn = new Microsoft.Data.Sqlite.SqliteConnection(connectionString))
                    {
                        conn.Open();
                        var ambil = conn.CreateCommand();
                        ambil.CommandText = "SELECT NamaDesa FROM InfoDesa LIMIT 1";
                        tersimpan = Convert.ToString(ambil.ExecuteScalar()) ?? string.Empty;
                    }

                    if (tersimpan != "Sumberjaya" || repo3.InvalidateCount == 0)
                    {
                        fails++;
                        Console.WriteLine($"FAIL pengaturan desa: tersimpan='{tersimpan}', cache dibersihkan {repo3.InvalidateCount}× (harusnya minimal 1×)");
                    }
                    else
                    {
                        Console.WriteLine($"Pengaturan desa: tersimpan '{tersimpan}' dan cache desa dibersihkan.");
                    }
                }
                catch (Exception ex)
                {
                    fails++;
                    Console.WriteLine($"FAIL pengaturan desa: {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { /* abaikan */ }
                }
            }
        }

        // --- Daftar Hadir: hari/tanggal bisa diedit, baris kota ikut menyesuaikan ---
        {
            int salah = 0;

            string bawaan = SuDesApp.Data.Models.DaftarHadirData.HariTanggalIndo(new DateTime(2026, 9, 18));
            if (bawaan != "Jumat, 18 September 2026")
            {
                salah++;
                Console.WriteLine($"FAIL hari/tanggal bawaan: '{bawaan}'");
            }

            var contoh = new (string HariTanggal, string? TanggalHarapan)[]
            {
                ("Sabtu, 3 Oktober 2026", "2026-10-03"),
                ("3 Oktober 2026", "2026-10-03"),
                ("", null),
                ("Hari pertama kerja bakti", null),
            };

            foreach (var (hariTanggal, harapan) in contoh)
            {
                var data = new SuDesApp.Data.Models.DaftarHadirData { HariTanggal = hariTanggal };
                var tanggal = data.TanggalDariHariTanggal();
                string hasil = tanggal.HasValue ? tanggal.Value.ToString("yyyy-MM-dd") : "(tidak dibaca)";
                string harusnya = harapan ?? "(tidak dibaca)";

                if (hasil != harusnya)
                {
                    salah++;
                    Console.WriteLine($"FAIL tanggal dari '{hariTanggal}': {hasil} (harusnya {harusnya})");
                }
            }

            if (salah == 0)
            {
                Console.WriteLine("Daftar Hadir: hari/tanggal bisa diedit dan baris kota mengikutinya.");
            }
            else
            {
                fails += salah;
            }
        }

        // --- Penulisan nama pada surat: nama kapital, gelar tetap ---
        {
            var contoh = new (string Masukan, string Harapan)[]
            {
                ("Muhammad Rizky Ramadhan", "MUHAMMAD RIZKY RAMADHAN"),
                ("Dr. H. Ahmad Suryana, S.H., M.H.", "Dr. H. AHMAD SURYANA, S.H., M.H."),
                ("Siti Aminah, S.Pd.", "SITI AMINAH, S.Pd."),
                ("dr. Agus Salim, Sp.PD.", "dr. AGUS SALIM, Sp.PD."),
                ("Hj. Siti Aminah, S.Pd.", "Hj. SITI AMINAH, S.Pd."),
                ("Ir. Bambang Wijaya, M.T.", "Ir. BAMBANG WIJAYA, M.T."),
                ("Drs. H. Suryana Kusuma, M.M.", "Drs. H. SURYANA KUSUMA, M.M."),
                ("Ahmad Suryana S.H.", "AHMAD SURYANA S.H."),
                (", S.H.", ", S.H."),
                ("   ", "   "),
            };

            int salah = 0;
            foreach (var (masukan, harapan) in contoh)
            {
                string hasil = SuDesApp.Utilities.NamaFormatter.ToUpperNama(masukan);
                if (hasil != harapan)
                {
                    salah++;
                    Console.WriteLine($"FAIL nama '{masukan}' → '{hasil}' (harusnya '{harapan}')");
                }
            }

            if (salah == 0)
            {
                Console.WriteLine($"Nama/gelar: {contoh.Length} bentuk uji sesuai.");
            }
            else
            {
                fails += salah;
            }
        }

        // ==== Layanan Online (WhatsApp): alur percakapan → simpan → proses surat ====
        {
            int salah = 0;

            bool linkSebelum = SuDesApp.Utilities.WaSheetOptions.IsLinkModeEnabled();
            SuDesApp.Utilities.WaSheetOptions.SetLinkModeEnabled(false);
            try
            {
                var waRepo = new FakeWaRepository();
                var wargaRepo = new WaSmokeWargaRepository();
                var desaRepo = new FakeDesaRepository();
                var jenisRepo = new WaSmokeJenisSuratRepository();
                var suratRepo = new WaSmokeSuratRepository();

                var sesi = new SuDesApp.WhatsApp.WaSessionStore();
                var percakapan = new SuDesApp.WhatsApp.WaConversationService(
                    sesi,
                    wargaRepo,
                    desaRepo,
                    waRepo,
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.WhatsApp.WaConversationService>.Instance);

                string pesanSktm =
                    "SKTM\n" +
                    "NIK: 3273010101010001\n" +
                    "Nama: Siti Aminah\n" +
                    "TTL: Karawang, 01-01-1990\n" +
                    "JK: P\n" +
                    "Agama: Islam\n" +
                    "Status: Kawin\n" +
                    "Pekerjaan: Ibu Rumah Tangga\n" +
                    "Alamat: Dusun Sukamaju RT 01/02, Desa Sumberjaya\n" +
                    "Keterangan: Keluarga kurang mampu";
                var w1 = await percakapan.ProcessAsync("6281212345678", pesanSktm);
                if (!w1.ReplyText.Contains("Data permohonan:") || !w1.ReplyText.Contains("Balas:  YA"))
                {
                    salah++;
                    Console.WriteLine("FAIL ringkasan permohonan WA tidak muncul");
                }
                if (!w1.ReplyText.Contains("Siti Aminah") || !w1.ReplyText.Contains("SKTM"))
                {
                    salah++;
                    Console.WriteLine("FAIL ringkasan WA tidak memuat nama/jenis");
                }
                if (waRepo.Semua.Count != 0)
                {
                    salah++;
                    Console.WriteLine("FAIL permintaan tersimpan sebelum konfirmasi YA");
                }

                var w2 = await percakapan.ProcessAsync("6281212345678", "YA");
                if (!w2.ReplyText.Contains("telah diterima") || !w2.ReplyText.Contains("Kode: PMT-"))
                {
                    salah++;
                    Console.WriteLine("FAIL balasan konfirmasi YA (terima/kode)");
                }
                if (waRepo.Semua.Count != 1 || w2.CreatedRequest == null)
                {
                    salah++;
                    Console.WriteLine("FAIL permintaan tidak tersimpan setelah YA");
                }
                else
                {
                    var p = waRepo.Semua[0];
                    if (p.Status != "BARU" || p.NamaJenis != "SKTM" || p.Sumber != "WA")
                    {
                        salah++;
                        Console.WriteLine($"FAIL metadata permintaan WA (status={p.Status} jenis={p.NamaJenis} sumber={p.Sumber})");
                    }
                    if (p.IsRead || p.NamaWarga != "Siti Aminah" || !p.NomorWA.StartsWith("0"))
                    {
                        salah++;
                        Console.WriteLine("FAIL kolom permintaan WA (baca/nama/nomor)");
                    }
                    if (p.DataJson == null || !p.DataJson.Contains("Keluarga kurang mampu"))
                    {
                        salah++;
                        Console.WriteLine("FAIL DataJson permintaan tidak memuat field");
                    }
                }

                var w3 = await percakapan.ProcessAsync("6281299990000",
                    "SKU\nNIK: 3273010101010002\nNama: Budi\nTTL: Karawang, 02-02-1992\nJK: L\nAgama: Islam\n" +
                    "Status: Belum Kawin\nPekerjaan: Pedagang\nAlamat: Jl. Sudirman No. 5");
                if (!w3.ReplyText.Contains("belum lengkap") || !w3.ReplyText.Contains("Bidang Usaha"))
                {
                    salah++;
                    Console.WriteLine("FAIL SKU kurang field tidak terdeteksi");
                }

                var w4 = await percakapan.ProcessAsync("6281288887777",
                    "KEMATIAN\nNIK: 3200000000000002\nNama: Tono\nTTL: Bogor, 03-03-1970\nJK: L\nAgama: Islam\n" +
                    "Status: Cerai Mati\nPekerjaan: Petani\nAlamat: Cikampek");
                if (!w4.ReplyText.Contains("datang ke kantor"))
                {
                    salah++;
                    Console.WriteLine("FAIL jenis offline harus diarahkan ke kantor");
                }
                if (waRepo.Semua.Count != 1)
                {
                    salah++;
                    Console.WriteLine("FAIL jenis offline tidak boleh disimpan sebagai permintaan");
                }

                string pesanIzin =
                    "IZIN ORTU\n" +
                    "NIK: 3273010101010001\n" +
                    "Nama: Siti Aminah\n" +
                    "TTL: Karawang, 01-01-1990\n" +
                    "JK: P\n" +
                    "Agama: Islam\n" +
                    "Status: Kawin\n" +
                    "Pekerjaan: Ibu Rumah Tangga\n" +
                    "Alamat: Dusun Sukamaju, Desa Sumberjaya\n" +
                    "NIK Anak: 3273010101010002\n" +
                    "Nama Anak: Dedi Setiawan\n" +
                    "Tempat Lahir Anak: Karawang\n" +
                    "Tanggal Lahir Anak: 12-05-2010\n" +
                    "JK Anak: L\n" +
                    "Agama Anak: Islam\n" +
                    "Status Anak: Belum Kawin\n" +
                    "Pekerjaan Anak: Pelajar\n" +
                    "Alamat Anak: Dusun Sukamaju, Desa Sumberjaya\n" +
                    "Negara Tujuan: Malaysia\n" +
                    "Nama PT: PT Sejahtera Abadi";
                var wi = await percakapan.ProcessAsync("62813 1111 2222", pesanIzin);
                if (!wi.ReplyText.Contains("Data permohonan:") || !wi.ReplyText.Contains("Negara Tujuan"))
                {
                    salah++;
                    Console.WriteLine("FAIL ringkasan IZIN ORTU");
                }
                var wk = await percakapan.ProcessAsync("62813 1111 2222", "ya");
                if (waRepo.Semua.Count != 2 || waRepo.Semua[^1].NamaJenis != "IZIN_ORTU")
                {
                    salah++;
                    Console.WriteLine("FAIL permintaan IZIN ORTU tidak tersimpan");
                }
                else
                {
                    var izin = waRepo.Semua[^1];
                    if (izin.DataJson == null || !izin.DataJson.Contains("3273010101010002"))
                    {
                        salah++;
                        Console.WriteLine("FAIL DataJson IZIN ORTU tidak memuat NIK anak");
                    }

                    var proc = new SuDesApp.WhatsApp.WaSuratProcessor(
                        waRepo, wargaRepo, desaRepo, jenisRepo, suratRepo,
                        Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);

                    var sktm = waRepo.Semua[0];
                    var hs = await proc.ProsesAsync(sktm.ID_Permintaan);
                    if (hs.IdSurat != 77 || hs.NomorSurat != "470/001/Ds/2026")
                    {
                        salah++;
                        Console.WriteLine($"FAIL proses SKTM => id={hs.IdSurat} nomor={hs.NomorSurat}");
                    }
                    var pSktm = waRepo.Semua[0];
                    if (pSktm.Status != "SELESAI" || pSktm.IdSurat != 77)
                    {
                        salah++;
                        Console.WriteLine("FAIL status SKTM tidak SELESAI setelah diproses");
                    }
                    if (suratRepo.Terakhir?.SKTM.KeteranganKemiskinan != "Keluarga kurang mampu")
                    {
                        salah++;
                        Console.WriteLine("FAIL field keterangan tidak masuk surat SKTM");
                    }

                    var hi = await proc.ProsesAsync(izin.ID_Permintaan);
                    var pIzin = waRepo.Semua[^1];
                    if (pIzin.Status != "SELESAI")
                    {
                        salah++;
                        Console.WriteLine("FAIL status IZIN tidak SELESAI setelah diproses");
                    }
                    var sIzin = suratRepo.Terakhir;
                    if (sIzin == null || sIzin.IzinOrtu.NIKAnak != "3273010101010002" ||
                        sIzin.IzinOrtu.NamaAnak != "Dedi Setiawan" ||
                        sIzin.IzinOrtu.NegaraTujuan != "Malaysia" ||
                        sIzin.IzinOrtu.NamaPT != "PT Sejahtera Abadi")
                    {
                        salah++;
                        Console.WriteLine("FAIL pemetaan data anak/PT pada IZIN ORTU");
                    }
                }
            }
            catch (Exception exWa)
            {
                salah++;
                Console.WriteLine($"FAIL uji WhatsApp: {exWa.GetType().Name}: {exWa.Message}");
            }
            finally
            {
                SuDesApp.Utilities.WaSheetOptions.SetLinkModeEnabled(linkSebelum);
            }

            if (salah == 0)
            {
                Console.WriteLine("Layanan Online WhatsApp: alur percakapan → simpan → proses surat sesuai.");
            }
            else
            {
                fails += salah;
            }
        }

        // ==== Google: parsing tautan Drive & invalidasi klien saat ganti akun ====
        {
            int salah = 0;

            (string Url, string? Id)[] kasusDrive =
            {
                ("https://drive.google.com/file/d/ABC123/view?usp=sharing", "ABC123"),
                ("https://drive.google.com/uc?export=download&id=XYZ789", "XYZ789"),
                ("https://drive.google.com/open?id=OPEN456", "OPEN456"),
                ("https://drive.usercontent.google.com/download?id=USER321&export=download&confirm=t", "USER321"),
                ("https://docs.google.com/spreadsheets/d/SHEET99/edit", null),
                ("https://example.com/file/d/NOPE/view", null),
                ("", null),
            };
            foreach (var k in kasusDrive)
            {
                var ok = SuDesApp.Utilities.UpdateService.TryExtractDriveFileId(k.Url, out var id);
                if (ok != (k.Id != null) || (k.Id != null && id != k.Id))
                {
                    salah++;
                    Console.WriteLine($"FAIL TryExtractDriveFileId(\"{k.Url}\") => {id}");
                }
            }

            var cfg = Microsoft.Extensions.Configuration.MemoryConfigurationBuilderExtensions.AddInMemoryCollection(
                new Microsoft.Extensions.Configuration.ConfigurationBuilder(),
                new Dictionary<string, string?>
                {
                    ["GoogleDrive:authMode"] = "OAuth",
                    ["GoogleDrive:clientId"] = "dummy-id",
                    ["GoogleDrive:clientSecret"] = "dummy-secret",
                    ["GoogleDrive:tokenFolder"] = Path.Combine(Path.GetTempPath(), "SuDesApp-GoogleTest-" + Guid.NewGuid().ToString("N"))
                })
                .Build();
            var drive = new SuDesApp.Utilities.GoogleDriveService(
                cfg, Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Utilities.GoogleDriveService>.Instance);

            bool invalidated = false;
            drive.ClientInvalidated += () => invalidated = true;

            var sheets = new SuDesApp.Utilities.GoogleSheetsService(
                drive, Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Utilities.GoogleSheetsService>.Instance);
            var sheetsField = typeof(SuDesApp.Utilities.GoogleSheetsService)
                .GetField("_service", BindingFlags.NonPublic | BindingFlags.Instance);
            sheetsField!.SetValue(sheets, new Google.Apis.Sheets.v4.SheetsService(new Google.Apis.Services.BaseClientService.Initializer()));

            await drive.ClearTokenAsync();

            if (!invalidated)
            {
                salah++;
                Console.WriteLine("FAIL ClientInvalidated tidak dipicu saat token dihapus");
            }
            if (sheetsField.GetValue(sheets) != null)
            {
                salah++;
                Console.WriteLine("FAIL cache klien Google Sheets tidak dibuang saat token dihapus");
            }

            if (salah == 0)
            {
                Console.WriteLine("Google: parsing tautan Drive & invalidasi klien saat ganti akun sesuai.");
            }
            else
            {
                fails += salah;
            }
        }

        // ==== UpdateService: verifikasi SHA-256 berkas pembaruan ====
        {
            int salah = 0;
            var tmp = Path.Combine(Path.GetTempPath(), "SuDesApp-HashTest-" + Guid.NewGuid().ToString("N") + ".bin");
            try
            {
                File.WriteAllBytes(tmp, new byte[] { 1, 2, 3, 4, 5 });
                string good;
                using (var sha = System.Security.Cryptography.SHA256.Create())
                using (var fs = File.OpenRead(tmp))
                {
                    good = Convert.ToHexString(sha.ComputeHash(fs));
                }

                if (!SuDesApp.Utilities.UpdateService.VerifySha256(tmp, good))
                {
                    salah++;
                    Console.WriteLine("FAIL VerifySha256 hash benar ditolak");
                }
                if (!SuDesApp.Utilities.UpdateService.VerifySha256(tmp, good.ToLowerInvariant()))
                {
                    salah++;
                    Console.WriteLine("FAIL VerifySha256 huruf kecil ditolak");
                }
                if (!SuDesApp.Utilities.UpdateService.VerifySha256(tmp, good.Insert(8, " ")))
                {
                    salah++;
                    Console.WriteLine("FAIL VerifySha256 dengan spasi ditolak");
                }
                if (SuDesApp.Utilities.UpdateService.VerifySha256(tmp, new string('0', 64)))
                {
                    salah++;
                    Console.WriteLine("FAIL VerifySha256 hash salah diterima");
                }
                if (SuDesApp.Utilities.UpdateService.VerifySha256(tmp, "abc"))
                {
                    salah++;
                    Console.WriteLine("FAIL VerifySha256 panjang ganjil diterima");
                }
                if (SuDesApp.Utilities.UpdateService.VerifySha256(tmp + ".tidak-ada", good))
                {
                    salah++;
                    Console.WriteLine("FAIL VerifySha256 berkas tidak ada diterima");
                }

                var info = System.Text.Json.JsonSerializer.Deserialize<SuDesApp.Utilities.UpdateInfo>(
                    "{\"version\":\"9.9.9\",\"downloadUrl\":\"https://example.com/a.exe\",\"sha256\":\"" + new string('a', 64) + "\"}");
                if (info == null || info.Version != "9.9.9" || info.Sha256?.Length != 64)
                {
                    salah++;
                    Console.WriteLine("FAIL UpdateInfo.sha256 tidak terparse");
                }

                var gh = SuDesApp.Utilities.UpdateService.ParseGitHubRelease(
                    "{\"tag_name\":\"v2.3.0\",\"body\":\"catatan\",\"assets\":[" +
                    "{\"name\":\"SuDesApp_2.3.0.exe\",\"browser_download_url\":\"https://github.com/u/r/releases/download/v2.3.0/SuDesApp_2.3.0.exe\"," +
                    "\"url\":\"https://api.github.com/repos/u/r/releases/assets/1\",\"digest\":\"sha256:" + new string('a', 64) + "\"}]}");
                if (gh == null || gh.Version != "2.3.0" || gh.Sha256?.Length != 64 ||
                    gh.DownloadUrl?.Contains("releases/download") != true ||
                    gh.GitHubApiAssetUrl != "https://api.github.com/repos/u/r/releases/assets/1")
                {
                    salah++;
                    Console.WriteLine("FAIL ParseGitHubRelease rilis normal tidak sesuai");
                }

                var ghPre = SuDesApp.Utilities.UpdateService.ParseGitHubRelease(
                    "{\"tag_name\":\"v2.3.1-rc1\",\"assets\":[{\"name\":\"x.exe\",\"browser_download_url\":\"https://x/y.exe\"}]}");
                if (ghPre?.Version != "2.3.1" || ghPre.Sha256 != null)
                {
                    salah++;
                    Console.WriteLine("FAIL ParseGitHubRelease pre-release tidak dinormalkan");
                }

                var ghNoExe = SuDesApp.Utilities.UpdateService.ParseGitHubRelease(
                    "{\"tag_name\":\"3.0.0\",\"assets\":[{\"name\":\"data.zip\",\"browser_download_url\":\"https://x/d.zip\"}]}");
                if (ghNoExe?.DownloadUrl != "https://x/d.zip")
                {
                    salah++;
                    Console.WriteLine("FAIL ParseGitHubRelease fallback aset non-exe gagal");
                }

                if (SuDesApp.Utilities.UpdateService.ParseGitHubRelease("not json") != null ||
                    SuDesApp.Utilities.UpdateService.ParseGitHubRelease("{\"tag_name\":\"\",\"assets\":[]}") != null)
                {
                    salah++;
                    Console.WriteLine("FAIL ParseGitHubRelease JSON tak valid seharusnya null");
                }
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }

            if (salah == 0)
            {
                Console.WriteLine("UpdateService: verifikasi SHA-256, parsing version.json & rilis GitHub sesuai.");
            }
            else
            {
                fails += salah;
            }
        }

        if (fails == 0)
        {
            Console.WriteLine("SEMUA TES LULUS");
        }
        else
        {
            Console.WriteLine($"{fails} tes GAGAL");
            return 1;
        }

        // --- Probe unduhan: replika cara aplikasi mengunduh via HttpClient ---
        if (Environment.GetEnvironmentVariable("PROBE_DOWNLOAD") == "1")
        {
            await ProbeDownloadAsync();
        }
        return 0;
    }

    private static async Task ProbeDownloadAsync()
    {
        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        string[] ids =
        {
            "1paTPiCuoSEMMTjRaPdQrp_8_2DJN1HcM", // F-1.01
            "14PuMyNH3jRQBMBPhRNNq9jbPnd-xtqKe", // F-1.02
            "1oqmPIOfsWvu3veWPfT7XdmEET_jBdd0g", // F-2.01 Akta Kelahiran
        };

        foreach (var id in ids)
        {
            foreach (var (label, url) in new[]
            {
                ("uc          ", $"https://drive.google.com/uc?export=download&id={id}"),
                ("usercontent ", $"https://drive.usercontent.google.com/download?id={id}&export=download&confirm=t"),
            })
            {
                try
                {
                    using var resp = await http.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                    await using var ms = new MemoryStream();
                    await resp.Content.CopyToAsync(ms);
                    var bytes = ms.ToArray();
                    var magic = System.Text.Encoding.ASCII.GetString(bytes.Take(4).ToArray());
                    var ctype = resp.Content.Headers.ContentType?.ToString() ?? "-";
                    Console.WriteLine($"{label} id={id[..8]}.. status={resp.StatusCode} type={ctype} bytes={bytes.Length} magic='{magic}'");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{label} id={id[..8]}.. GAGAL: {ex.Message}");
                }
            }
        }
    }

    private sealed class FakeDesaRepository : SuDesApp.Data.Repositories.IDesaRepository
    {
        public static SuDesApp.Data.Models.DesaData BuatDesa() => new SuDesApp.Data.Models.DesaData
        {
            NamaDesa = "Desa Uji",
            Kecamatan = "Kecamatan Uji",
            Kabupaten = "Kabupaten Uji",
            Alamat = "Jl. Uji No. 1",
            Kodepos = "41300",
            KepalaDesa = "Kades Uji",
            SekretarisDesa = "Sekdes Uji",
            NamaCamat = "Camat Uji"
        };

        /// <summary>Berapa kali cache desa diminta dibersihkan (dipakai uji pengaturan desa).</summary>
        public int InvalidateCount;

        public Task InitializeAsync() => Task.CompletedTask;
        public Task<SuDesApp.Data.Models.DesaData> GetInfoDesaAsync(System.Threading.CancellationToken cancellationToken = default)
            => Task.FromResult(BuatDesa());
        public Task UpdateInfoDesaAsync(SuDesApp.Data.Models.DesaData desaData) => Task.CompletedTask;
        public Task SaveInfoDesaAsync(SuDesApp.Data.Models.DesaData desaData) => Task.CompletedTask;
        public Task<SuDesApp.Data.Models.DesaData> GetInfoDesaFromCacheAsync() => Task.FromResult(BuatDesa());
        public Task InvalidateCacheAsync()
        {
            InvalidateCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSuratRepository : SuDesApp.Data.Repositories.ISuratRepository
    {
        public readonly SuDesApp.Data.Models.SuratData SampleSurat = new SuDesApp.Data.Models.SuratData
        {
            ID_Surat = 1,
            ID_Jenis = 2,
            NamaJenis = "SKD_UMUM",
            NomorSurat = "470/001/Ds/2026",
            TanggalSurat = new DateTime(2026, 9, 17),
            Warga = new SuDesApp.Data.Models.WargaData
            {
                Nama = "Warga Uji",
                NIK = "3200000000000001",
                TempatLahir = "Uji",
                TanggalLahir = "1990-01-01",
                JenisKelamin = "Laki-laki",
                Agama = "Islam",
                StatusPerkawinan = "Belum Kawin",
                Pekerjaan = "Petani",
                Dusun = "Dusun Uji",
                Desa = "Desa Uji",
                Kecamatan = "Kecamatan Uji",
                Kabupaten = "Kabupaten Uji",
            },
            Desa = FakeDesaRepository.BuatDesa(),
            PejabatPenandatangan = "Kepala Desa",
            NamaPejabatPenandatangan = "Kades Uji",
        };

        public Task<SuDesApp.Data.Models.SuratData> GetByIdAsync(int id, System.Data.IDbTransaction? transaction = null, System.Threading.CancellationToken cancellationToken = default)
            => Task.FromResult(SampleSurat);

        public Task<int> InsertAsync(SuDesApp.Data.Models.SuratData entity, System.Data.IDbTransaction? transaction = null, System.Threading.CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> UpdateAsync(SuDesApp.Data.Models.SuratData entity, System.Data.IDbTransaction? transaction = null, System.Threading.CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> UpdateStatusAsync(int id, string status, System.Data.IDbTransaction? transaction = null, System.Threading.CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(int id, System.Data.IDbTransaction? transaction = null, System.Threading.CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<System.Collections.Generic.IEnumerable<SuDesApp.Data.Models.SuratData>> GetFilteredAsync(SuDesApp.Data.Models.FilterConditions filters, string sortBy, bool ascending, int skip, int take, System.Threading.CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> CountAsync(SuDesApp.Data.Models.FilterConditions filters, System.Threading.CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task InitializeSuratIndexesAsync() => Task.CompletedTask;
        public Task<System.Collections.Generic.List<string>> GetJenisSuratKeteranganDesaAsync() => Task.FromResult(new System.Collections.Generic.List<string>());
        public Task RefreshJenisSuratConfigurationAsync() => Task.CompletedTask;
        public Task<System.Collections.Generic.IEnumerable<SuDesApp.Data.Models.SuratData>> GetAllSuratDataAsync(string sortBy = "ID_Surat", bool ascending = true, int skip = 0, int take = 100, System.Threading.CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> AddSuratAsync(SuDesApp.Data.Models.SuratData entity, System.Data.IDbTransaction? transaction = null, System.Threading.CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> CountSuratByJenisAndYearAsync(string namaJenis, string year, System.Threading.CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<System.Collections.Generic.IEnumerable<SuDesApp.Data.Models.SuratData>> GetSuratByDateRangeAsync(DateTime startDate, DateTime endDate, System.Threading.CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<System.Collections.Generic.Dictionary<string, int>> GetSuratStatisticsByStatusAsync(System.Threading.CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> CheckNomorSuratExistsAsync(string nomorSurat, int? excludeId = null, System.Threading.CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<System.Collections.Generic.Dictionary<string, object>> GetDatabaseStatsAsync() => throw new NotImplementedException();
        public Task<SuDesApp.Data.Models.WargaData> GetWargaByIdAsync(int idWarga, System.Threading.CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SuDesApp.Data.Models.WargaData> GetWargaByNikAsync(string nik, System.Threading.CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> AddOrGetWargaAsync(SuDesApp.Data.Models.WargaData wargaData, System.Threading.CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int?> GetLastSuratIdByTypeAsync(string templateName, System.Threading.CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class FakeWaRepository : SuDesApp.Data.Repositories.IPermintaanWaRepository
    {
        public readonly List<SuDesApp.Data.Models.PermintaanWa> Semua = new();
        private int _nextId = 1;

        public Task<int> InsertAsync(SuDesApp.Data.Models.PermintaanWa p, CancellationToken ct = default)
        {
            p.ID_Permintaan = _nextId++;
            Semua.Add(p);
            return Task.FromResult(p.ID_Permintaan);
        }

        public Task<bool> UpdateAsync(SuDesApp.Data.Models.PermintaanWa p, CancellationToken ct = default)
        {
            var i = Semua.FindIndex(x => x.ID_Permintaan == p.ID_Permintaan);
            if (i < 0) return Task.FromResult(false);
            Semua[i] = p;
            return Task.FromResult(true);
        }

        public Task<bool> UpdateStatusAsync(int id, string status, string? catatan, int? idSurat, string? pesanBalasan, CancellationToken ct = default)
        {
            var p = Semua.FirstOrDefault(x => x.ID_Permintaan == id);
            if (p == null) return Task.FromResult(false);
            p.Status = status;
            p.Catatan = catatan;
            p.IdSurat = idSurat;
            p.PesanBalasan = pesanBalasan;
            p.TanggalDiproses = DateTime.Now;
            p.IsRead = true;
            return Task.FromResult(true);
        }

        public Task<bool> MarkReadAsync(int id, CancellationToken ct = default)
        {
            var p = Semua.FirstOrDefault(x => x.ID_Permintaan == id);
            if (p == null) return Task.FromResult(false);
            p.IsRead = true;
            return Task.FromResult(true);
        }

        public Task<bool> MarkReadAllAsync(CancellationToken ct = default)
        {
            foreach (var p in Semua) p.IsRead = true;
            return Task.FromResult(true);
        }

        public Task<SuDesApp.Data.Models.PermintaanWa?> GetByIdAsync(int id, CancellationToken ct = default)
            => Task.FromResult(Semua.FirstOrDefault(x => x.ID_Permintaan == id));

        public Task<List<SuDesApp.Data.Models.PermintaanWa>> GetAllAsync(string? statusFilter = null, CancellationToken ct = default)
            => Task.FromResult(string.IsNullOrWhiteSpace(statusFilter)
                ? Semua.OrderByDescending(x => x.TanggalPermintaan).ToList()
                : Semua.Where(x => x.Status == statusFilter).OrderByDescending(x => x.TanggalPermintaan).ToList());

        public Task<List<SuDesApp.Data.Models.PermintaanWa>> GetUnreadAsync(CancellationToken ct = default)
            => Task.FromResult(Semua.Where(x => !x.IsRead).ToList());

        public Task<int> CountByStatusAsync(string status, CancellationToken ct = default)
            => Task.FromResult(Semua.Count(x => x.Status == status));

        public Task<int> CountUnreadAsync(CancellationToken ct = default)
            => Task.FromResult(Semua.Count(x => !x.IsRead));

        public Task<int> CountByKodeYearAsync(int year, CancellationToken ct = default)
            => Task.FromResult(Semua.Count(x => x.KodePermintaan.StartsWith($"PMT-{year}-", StringComparison.Ordinal)));

        public Task<string> NextKodePermintaanAsync(int year, CancellationToken ct = default)
        {
            var prefix = $"PMT-{year}-";
            int maks = 0;
            foreach (var kode in Semua.Select(x => x.KodePermintaan))
            {
                if (kode.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(kode[prefix.Length..], out var n) && n > maks)
                    maks = n;
            }
            return Task.FromResult($"{prefix}{(maks + 1):D4}");
        }

        public Task InitializeTableAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task MigrateSheetColumnsAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<SuDesApp.Data.Models.PermintaanWa?> GetBySheetTokenAsync(string token, CancellationToken ct = default)
            => Task.FromResult(Semua.OrderByDescending(x => x.TanggalPermintaan).FirstOrDefault(x => x.SheetToken == token));

        public Task<SuDesApp.Data.Models.PermintaanWa?> GetBySheetRowAsync(string sheetId, int rowNumber, CancellationToken ct = default)
            => Task.FromResult(Semua.OrderByDescending(x => x.TanggalPermintaan)
                .FirstOrDefault(x => x.SheetRowId == rowNumber && (x.DataJson?.Contains($"\"SheetId\":\"{sheetId}\"") ?? false)));

        public Task<bool> UpdateSheetLinkAsync(int id, string sheetToken, int? sheetRowId, CancellationToken ct = default)
        {
            var p = Semua.FirstOrDefault(x => x.ID_Permintaan == id);
            if (p == null) return Task.FromResult(false);
            p.SheetToken = sheetToken;
            p.SheetRowId = sheetRowId;
            return Task.FromResult(true);
        }
    }

    private sealed class WaSmokeWargaRepository : SuDesApp.Data.Repositories.IWargaRepository
    {
        public readonly List<SuDesApp.Data.Models.WargaData> Daftar = new();
        private int _nextId = 1;

        public Task InitializeWargaTableAsync() => Task.CompletedTask;

        public Task<int> AddOrUpdateWargaAsync(SuDesApp.Data.Models.WargaData w, Microsoft.Data.Sqlite.SqliteConnection? existingConnection = null, System.Data.IDbTransaction? existingTransaction = null)
        {
            if (w.ID_Warga <= 0) w.ID_Warga = _nextId++;
            var i = Daftar.FindIndex(x => x.ID_Warga == w.ID_Warga);
            if (i < 0) Daftar.Add(w);
            else Daftar[i] = w;
            return Task.FromResult(w.ID_Warga);
        }

        public Task<SuDesApp.Data.Models.WargaData> GetWargaByIdAsync(int idWarga)
            => Task.FromResult(Daftar.FirstOrDefault(x => x.ID_Warga == idWarga)!);

        public Task<SuDesApp.Data.Models.WargaData> GetWargaByNikAsync(string nik, Microsoft.Data.Sqlite.SqliteConnection? connection = null, System.Data.IDbTransaction? transaction = null)
            => Task.FromResult(Daftar.FirstOrDefault(x => x.NIK == nik)!);

        public Task<IEnumerable<SuDesApp.Data.Models.WargaData>> GetAllWargaAsync()
            => Task.FromResult((IEnumerable<SuDesApp.Data.Models.WargaData>)Daftar.ToList());

        public Task<IEnumerable<SuDesApp.Data.Models.WargaData>> SearchWargaAsync(string searchTerm)
            => Task.FromResult((IEnumerable<SuDesApp.Data.Models.WargaData>)Daftar
                .Where(w => (w.Nama ?? string.Empty).Contains(searchTerm) || (w.NIK ?? string.Empty).Contains(searchTerm)).ToList());

        public Task<bool> DeleteWargaAsync(int id)
        {
            var i = Daftar.FindIndex(x => x.ID_Warga == id);
            if (i < 0) return Task.FromResult(false);
            Daftar.RemoveAt(i);
            return Task.FromResult(true);
        }

        public Task<string> GetAlamatByNikAsync(string nik)
            => Task.FromResult(Daftar.FirstOrDefault(x => x.NIK == nik)?.AlamatLengkap ?? string.Empty);

        public Task<int> AddOrUpdateWargaAndGetIdAsync(SuDesApp.Data.Models.WargaData w)
            => AddOrUpdateWargaAsync(w);

        public Task<int> GetOrCreateDummyWargaAsync(Microsoft.Data.Sqlite.SqliteConnection connection, System.Data.IDbTransaction transaction)
            => Task.FromResult(99);

        public Task<int> AddOrUpdateWargaAndGetIdAsync(SuDesApp.Data.Models.WargaData w, Microsoft.Data.Sqlite.SqliteConnection connection, System.Data.IDbTransaction transaction)
            => AddOrUpdateWargaAsync(w);
    }

    private sealed class WaSmokeJenisSuratRepository : SuDesApp.Data.Repositories.IJenisSuratRepository
    {
        public Task InitializeJenisSuratDataAsync() => Task.CompletedTask;

        public Task<SuDesApp.Data.Models.JenisSuratKelas> GetJenisSuratByNamaAsync(string namaJenis)
            => Task.FromResult(new SuDesApp.Data.Models.JenisSuratKelas
            {
                ID_Jenis = 1,
                NamaJenis = namaJenis,
                KodeJenis = namaJenis,
                IsActive = true
            });

        public Task<SuDesApp.Data.Models.JenisSuratKelas> GetByNamaAsync(string namaJenis)
            => GetJenisSuratByNamaAsync(namaJenis);

        public Task<int> GetIdJenisSuratByNamaAsync(string namaJenis) => Task.FromResult(1);

        public Task<List<SuDesApp.Data.Models.JenisSuratKelas>> GetAllJenisSuratAsync()
            => Task.FromResult(new List<SuDesApp.Data.Models.JenisSuratKelas>());

        public Task<Dictionary<string, string>> GetJenisSuratDisplayNamesAsync()
            => Task.FromResult(new Dictionary<string, string>());

        public Task<List<string>> GetJenisSuratKeteranganDesa()
            => Task.FromResult(new List<string>());

        public Task<string> GenerateNomorSuratAsync(string kodeJenis) => Task.FromResult(string.Empty);
        public Task<bool> IsNomorSuratExistsAsync(string nomorSurat) => Task.FromResult(false);
        public Task<int?> GetLastSuratIdByTypeAsync(string templateName) => Task.FromResult<int?>(null);
        public Task<List<string>> GetAvailableYearsAsync() => Task.FromResult(new List<string>());
        public Task<bool> HasExistingNomorSuratAsync(string namaJenis) => Task.FromResult(false);
        public Task<HashSet<string>> GetSharedNumberingGroupAsync() => Task.FromResult(new HashSet<string>());
        public Task RefreshConfigurationAsync() => Task.CompletedTask;
    }

    private sealed class WaSmokeSuratRepository : SuDesApp.Data.Repositories.ISuratRepository
    {
        public SuDesApp.Data.Models.SuratData? Terakhir;

        public Task<int> AddSuratAsync(SuDesApp.Data.Models.SuratData entity, System.Data.IDbTransaction? transaction = null, CancellationToken ct = default)
        {
            entity.ID_Surat = 77;
            entity.NomorSurat = "470/001/Ds/2026";
            entity.Warga.ID_Warga = 7;
            Terakhir = entity;
            return Task.FromResult(77);
        }

        public Task<SuDesApp.Data.Models.SuratData> GetByIdAsync(int id, System.Data.IDbTransaction? transaction = null, CancellationToken ct = default)
            => Task.FromResult(Terakhir ?? throw new InvalidOperationException("Belum ada surat."));

        public Task<int> InsertAsync(SuDesApp.Data.Models.SuratData entity, System.Data.IDbTransaction? transaction = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> UpdateAsync(SuDesApp.Data.Models.SuratData entity, System.Data.IDbTransaction? transaction = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> UpdateStatusAsync(int id, string status, System.Data.IDbTransaction? transaction = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(int id, System.Data.IDbTransaction? transaction = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<SuDesApp.Data.Models.SuratData>> GetFilteredAsync(SuDesApp.Data.Models.FilterConditions filters, string sortBy, bool ascending, int skip, int take, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> CountAsync(SuDesApp.Data.Models.FilterConditions filters, CancellationToken ct = default) => throw new NotImplementedException();
        public Task InitializeSuratIndexesAsync() => throw new NotImplementedException();
        public Task<List<string>> GetJenisSuratKeteranganDesaAsync() => throw new NotImplementedException();
        public Task RefreshJenisSuratConfigurationAsync() => throw new NotImplementedException();
        public Task<IEnumerable<SuDesApp.Data.Models.SuratData>> GetAllSuratDataAsync(string sortBy = "ID_Surat", bool ascending = true, int skip = 0, int take = 100, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> CountSuratByJenisAndYearAsync(string namaJenis, string year, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<SuDesApp.Data.Models.SuratData>> GetSuratByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Dictionary<string, int>> GetSuratStatisticsByStatusAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> CheckNomorSuratExistsAsync(string nomorSurat, int? excludeId = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Dictionary<string, object>> GetDatabaseStatsAsync() => throw new NotImplementedException();
        public Task<SuDesApp.Data.Models.WargaData> GetWargaByIdAsync(int idWarga, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SuDesApp.Data.Models.WargaData> GetWargaByNikAsync(string nik, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> AddOrGetWargaAsync(SuDesApp.Data.Models.WargaData wargaData, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int?> GetLastSuratIdByTypeAsync(string templateName, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private static string? FindFileUpwards(string fileName)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
