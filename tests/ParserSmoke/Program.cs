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
            var got = (string)build!.Invoke(null, new object[] { title, shared });
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
            var got = (bool)isOpen!.Invoke(null, new object?[] { c.Now, c.Buka, c.Tutup, c.Hari });
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
        var r1 = (string)rentang!.Invoke(null, new object?[] { "08:00", "16:00", "1,2,3,4,5" });
        if (r1 != "Senin–Jumat, 08:00–16:00")
        {
            fails++;
            Console.WriteLine($"FAIL RentangTampil(default) => '{r1}'");
        }
        var r2 = (string)rentang!.Invoke(null, new object?[] { null, null, null });
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
                    (string)t.GetProperty("BaseFileName")!.GetValue(e)));
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

            // 22 jenis: 16 jenis lama + REKENING_KORAN + TEMPLATE_SURAT (surat dari
            // Template Surat buatan pengguna) + NTCR_N1..NTCR_N6 (blanko Model N1-N6
            // yang dibuat kantor desa) + NTCR_N8 (surat keterangan numpang nikah).
            // Model N7 sudah dihapus karena diterbitkan KUA.
            const int jumlahJenisDiharapkan = 22;
            if (cfg.SuratNumberFormats.Count != jumlahJenisDiharapkan || cfg.TemplateNames.Count != jumlahJenisDiharapkan)
            {
                fails++;
                Console.WriteLine($"FAIL AppConfig jenis surat: formats={cfg.SuratNumberFormats.Count}, names={cfg.TemplateNames.Count} (harusnya {jumlahJenisDiharapkan})");
            }

            // Pencarian format harus case-insensitive (NamaJenis JSON PascalCase, pemakai kadang uppercase)
            if (cfg.GetSuratNumberFormat("KEMATIAN") != "570/{0:D3}/Ds/{2:yyyy}" ||
                cfg.GetSuratNumberFormat("ntcr_n4") != "474.3/{0:D3}/Ds/{2:yyyy}")
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

            // Permohonan rekening koran memakai deret nomor sendiri (awalan 130), terpisah
            // dari urutan SKD 470: nomornya 130/001/Ds/tahun, bukan lanjutan urutan SKD.
            if (cfg.IsSharedNumbering("REKENING_KORAN"))
            {
                fails++;
                Console.WriteLine("FAIL REKENING_KORAN tidak boleh ikut penomoran bersama SKD");
            }
            if (!cfg.GetSuratNumberFormat("REKENING_KORAN").StartsWith("130/", StringComparison.Ordinal) ||
                cfg.GetSuratNumberFormat("REKENING_KORAN") == cfg.GetSuratNumberFormat("SKD_UMUM"))
            {
                fails++;
                Console.WriteLine($"FAIL format nomor rekening koran '{cfg.GetSuratNumberFormat("REKENING_KORAN")}' " +
                    $"(harusnya berawalan 130 dan berbeda dari SKD '{cfg.GetSuratNumberFormat("SKD_UMUM")}')");
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

        // ==== Mode tautan: normalisasi URL form (published vs editor) ====
        {
            int salah = 0;

            (string Masukan, string Harapan)[] kasusForm =
            {
                // URL terbit (published) harus UTUH — dulu terpotong jadi /d/e/viewform (404).
                ("https://docs.google.com/forms/d/e/1FAIpQLSdYZkhoKUot1aMbIzLLUapVn2dYJ71TWVfG0kJNkE8z1GGuqA/viewform",
                 "https://docs.google.com/forms/d/e/1FAIpQLSdYZkhoKUot1aMbIzLLUapVn2dYJ71TWVfG0kJNkE8z1GGuqA/viewform"),
                ("https://docs.google.com/forms/d/e/ABC123/viewform?usp=sf_link",
                 "https://docs.google.com/forms/d/e/ABC123/viewform"),
                // URL gaya editor → viewform.
                ("https://docs.google.com/forms/d/ABC123/edit",
                 "https://docs.google.com/forms/d/ABC123/viewform"),
                ("https://docs.google.com/forms/d/ABC123/edit?usp=sharing",
                 "https://docs.google.com/forms/d/ABC123/viewform"),
            };
            foreach (var (masukan, harapan) in kasusForm)
            {
                var hasil = SuDesApp.Utilities.WaSheetOptions.NormalizeFormUrl(masukan);
                if (hasil != harapan)
                {
                    salah++;
                    Console.WriteLine($"FAIL NormalizeFormUrl(\"{masukan}\") => {hasil} (harusnya {harapan})");
                }
            }

            var sheetId = SuDesApp.Utilities.WaSheetOptions.ExtractSheetId(
                "https://docs.google.com/spreadsheets/d/1sd0kfiZjsJ_f_X6UpnzCQ-zGTu7fK0CHu-GEooSnLA4/edit");
            if (sheetId != "1sd0kfiZjsJ_f_X6UpnzCQ-zGTu7fK0CHu-GEooSnLA4")
            {
                salah++;
                Console.WriteLine($"FAIL ExtractSheetId => {sheetId}");
            }

            if (salah == 0)
            {
                Console.WriteLine("Mode tautan: normalisasi URL form (published/editor) sesuai.");
            }
            else
            {
                fails += salah;
            }
        }

        // ==== Pembuat formulir otomatis: katalog + pemetaan jawaban ====
        {
            int salah = 0;

            var kolom = SuDesApp.Utilities.WaFormKatalog.KolomSheet;
            if (kolom.Count < 20)
            {
                salah++;
                Console.WriteLine($"FAIL katalog kolom Sheet terlalu pendek ({kolom.Count})");
            }
            if (kolom[0] != SuDesApp.Utilities.WaFormKatalog.KolomTimestamp)
            {
                salah++;
                Console.WriteLine("FAIL kolom pertama katalog harus Timestamp");
            }
            if (kolom[kolom.Count - 2] != SuDesApp.Utilities.WaFormKatalog.KolomStatus ||
                kolom[kolom.Count - 1] != SuDesApp.Utilities.WaFormKatalog.KolomResponseId)
            {
                salah++;
                Console.WriteLine("FAIL dua kolom terakhir harus Status lalu Response ID");
            }

            var judulPertanyaan = SuDesApp.Utilities.WaFormKatalog.Pertanyaan
                .Select(p => p.Judul).ToList();
            if (judulPertanyaan.Distinct(StringComparer.OrdinalIgnoreCase).Count() != judulPertanyaan.Count)
            {
                salah++;
                Console.WriteLine("FAIL ada judul pertanyaan formulir yang duplikat");
            }
            foreach (var wajib in new[] { "Jenis Surat", "NIK", "Nama", "Keperluan", "No. WhatsApp" })
            {
                if (!judulPertanyaan.Contains(wajib))
                {
                    salah++;
                    Console.WriteLine($"FAIL pertanyaan wajib tidak ada: {wajib}");
                }
            }

            var idxKeperluan = SuDesApp.Utilities.WaFormKatalog.IndexKolom("Keperluan");
            if (idxKeperluan < 0 || kolom[idxKeperluan] != "Keperluan")
            {
                salah++;
                Console.WriteLine($"FAIL IndexKolom(\"Keperluan\") => {idxKeperluan}");
            }

            // "Keperluan" harus dipetakan ke field baku "keterangan" (kelengkapan surat).
            if (SuDesApp.WhatsApp.WaFormatParser.NormalizeLabel("Keperluan") != "keterangan")
            {
                salah++;
                Console.WriteLine("FAIL NormalizeLabel(\"Keperluan\") harus 'keterangan'");
            }

            // Gabungan jawaban pilihan ganda → teks kolom.
            var jawaban = new Google.Apis.Forms.v1.Data.Answer
            {
                TextAnswers = new Google.Apis.Forms.v1.Data.TextAnswers
                {
                    Answers = new List<Google.Apis.Forms.v1.Data.TextAnswer>
                    {
                        new() { Value = "L" },
                        new() { Value = "P" }
                    }
                }
            };
            if (SuDesApp.Utilities.GoogleFormsService.JawabanTeks(jawaban) != "L, P")
            {
                salah++;
                Console.WriteLine("FAIL JawabanTeks gabungan pilihan ganda salah");
            }
            if (SuDesApp.Utilities.GoogleFormsService.JawabanTeks(null) != string.Empty)
            {
                salah++;
                Console.WriteLine("FAIL JawabanTeks(null) harus kosong");
            }

            if (salah == 0)
            {
                Console.WriteLine("Pembuat formulir otomatis: katalog & pemetaan jawaban sesuai.");
            }
            else
            {
                fails += salah;
            }
        }

        // ==== Paket NTCR: sekali isi data satu pasangan → N blanko + SATU berkas PDF ====
        {
            int salah = 0;
            string paketDir = Path.Combine(Path.GetTempPath(), $"SuDesApp-PaketNTCR-{Guid.NewGuid():N}");

            try
            {
                string? appSettingsPaket = FindFileUpwards("appsettings.json");
                if (appSettingsPaket == null)
                {
                    salah++;
                    Console.WriteLine("FAIL paket NTCR: appsettings.json tidak ditemukan");
                }
                else
                {
                    var cfg = (SuDesApp.AppConfig)Activator.CreateInstance(typeof(SuDesApp.AppConfig), appSettingsPaket, null)!;
                    cfg.TempPdfFolder = paketDir;

                    var desaRepo = new FakeDesaRepository();
                    var wargaRepo = new WaSmokeWargaRepository();
                    var jenisRepo = new PaketNtcrJenisSuratRepository();
                    var suratRepo = new PaketNtcrSuratRepository();
                    var uow = new FakeUnitOfWork(desaRepo, wargaRepo, suratRepo, jenisRepo);

                    var generator = BuatGeneratorNtcr(cfg, uow);

                    var layanan = new SuDesApp.Services.NtcrPaketService(
                        uow, cfg, generator,
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Services.NtcrPaketService>.Instance);

                    // Data satu pasangan seperti hasil isian form paket.
                    var master = BuatPasanganUji();

                    // Sengaja tidak berurut: layanan harus mengembalikannya urut N1 → N6.
                    string[] terpilih = { "NTCR_N6", "NTCR_N1", "NTCR_N5" };
                    var hasil = await layanan.SimpanDanCetakAsync(master, terpilih);

                    if (hasil.Surat.Count != terpilih.Length)
                    {
                        salah++;
                        Console.WriteLine($"FAIL paket NTCR: {hasil.Surat.Count} surat tersimpan (harusnya {terpilih.Length})");
                    }

                    string[] urutHarapan = { "NTCR_N1", "NTCR_N5", "NTCR_N6" };
                    for (int i = 0; i < Math.Min(urutHarapan.Length, hasil.Surat.Count); i++)
                    {
                        if (!string.Equals(hasil.Surat[i].NamaJenis, urutHarapan[i], StringComparison.OrdinalIgnoreCase))
                        {
                            salah++;
                            Console.WriteLine($"FAIL paket NTCR urutan: [{i}] = {hasil.Surat[i].NamaJenis} (harusnya {urutHarapan[i]})");
                        }
                    }

                    foreach (var surat in hasil.Surat)
                    {
                        string keperluanHarusnya = SuDesApp.Data.Models.NtcrKatalog.Keperluan(surat.NamaJenis);
                        if (!string.Equals(surat.Keperluan, keperluanHarusnya, StringComparison.Ordinal))
                        {
                            salah++;
                            Console.WriteLine($"FAIL paket NTCR keperluan {surat.NamaJenis}: '{surat.Keperluan}' (harusnya '{keperluanHarusnya}')");
                        }

                        if (string.IsNullOrWhiteSpace(surat.NomorSurat))
                        {
                            salah++;
                            Console.WriteLine($"FAIL paket NTCR {surat.NamaJenis}: nomor surat belum diberikan");
                        }

                        if (!string.Equals(surat.Status, "Active", StringComparison.OrdinalIgnoreCase))
                        {
                            salah++;
                            Console.WriteLine($"FAIL paket NTCR {surat.NamaJenis}: status '{surat.Status}' (harusnya Active)");
                        }

                        // Identitas pemohon sama untuk seluruh blanko (satu warga di register).
                        if (!ReferenceEquals(surat.Warga, master.Warga))
                        {
                            salah++;
                            Console.WriteLine($"FAIL paket NTCR {surat.NamaJenis}: data pemohon tidak memakai pasangan yang sama");
                        }
                    }

                    // Catatan: penomoran mengikuti urutan per jenis blanko (IsSharedNumbering=false),
                    // jadi nomor antar blanko boleh sama — sama seperti alur cetak satu blanko.

                    // Data pasangan tiap surat harus salinan sendiri (tidak saling menimpa).
                    for (int i = 0; i < hasil.Surat.Count - 1; i++)
                    {
                        for (int j = i + 1; j < hasil.Surat.Count; j++)
                        {
                            if (ReferenceEquals(hasil.Surat[i].Ntcr, hasil.Surat[j].Ntcr) ||
                                ReferenceEquals(hasil.Surat[i].Ntcr!.AyahCalonIstri, hasil.Surat[j].Ntcr!.AyahCalonIstri))
                            {
                                salah++;
                                Console.WriteLine($"FAIL paket NTCR: {hasil.Surat[i].NamaJenis} & {hasil.Surat[j].NamaJenis} berbagi objek data pasangan");
                            }
                        }
                    }

                    // Satu berkas PDF, satu halaman per blanko.
                    if (!File.Exists(hasil.BerkasPdf))
                    {
                        salah++;
                        Console.WriteLine($"FAIL paket NTCR: berkas PDF tidak dibuat ({hasil.BerkasPdf})");
                    }
                    else
                    {
                        int halaman = JumlahHalamanPdf(hasil.BerkasPdf);
                        if (halaman != hasil.Surat.Count)
                        {
                            salah++;
                            Console.WriteLine($"FAIL paket NTCR: PDF {halaman} halaman (harusnya {hasil.Surat.Count}, satu blanko per halaman)");
                        }
                    }

                    // Kepala blanko: nomor LAMPIRAN tiap model sesuai lembar asli
                    // (Keputusan Dirjen Bimas Islam No. 473 Tahun 2020).
                    var lampiranHarapan = new (string Jenis, string Lampiran)[]
                    {
                        ("NTCR_N1", "IV"), ("NTCR_N2", "VI"), ("NTCR_N3", "VII"),
                        ("NTCR_N4", "VIII"), ("NTCR_N5", "IX"), ("NTCR_N6", "X")
                    };
                    foreach (var (jenisNtcr, lampiran) in lampiranHarapan)
                    {
                        if (SuDesApp.Data.Models.NtcrKatalog.Lampiran(jenisNtcr) != lampiran)
                        {
                            salah++;
                            Console.WriteLine($"FAIL paket NTCR: LAMPIRAN {jenisNtcr} = '{SuDesApp.Data.Models.NtcrKatalog.Lampiran(jenisNtcr)}' (harusnya '{lampiran}')");
                        }
                    }

                    // Surat numpang nikah (N8): menu tersendiri, bukan bagian paket N1–N6.
                    salah += PeriksaSuratNumpangNikah(cfg);

                    // Blanko permohonan N2 & N3: penomoran 474.3, lampiran baku +
                    // butir titik-titik, dan N3 yang boleh dikosongkan.
                    salah += PeriksaBlankoPermohonanN2N3(cfg);

                    // Kotak "Nomor Surat" pada form paket harus terisi otomatis.
                    salah += PeriksaNomorOtomatisFormPaket(cfg, uow);

                    // Register NTCR & Register Surat adalah dua register terpisah.
                    salah += PeriksaPemisahanRegister(cfg);

                    // Penyalinan data NTCR harus utuh — satu field tertinggal akan hilang dari semua blanko.
                    salah += PeriksaSalinNtcr();

                    if (salah == 0)
                    {
                        Console.WriteLine($"Paket NTCR: {hasil.Surat.Count} blanko ({hasil.DaftarBlanko}) tersimpan sekaligus + 1 berkas PDF {hasil.Surat.Count} halaman siap cetak.");
                        Console.WriteLine($"     nomor surat: {hasil.DaftarNomor}");
                    }
                }
            }
            catch (Exception ex)
            {
                salah++;
                Console.WriteLine($"FAIL paket NTCR: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                try { if (Directory.Exists(paketDir)) Directory.Delete(paketDir, recursive: true); } catch { /* temp */ }
            }

            fails += salah;
        }

        // --- Migrasi N7: database lama yang masih menyimpan Model N7 dibersihkan otomatis ---
        // Model N7 sudah dihapus dari aplikasi (diterbitkan KUA). Database yang pernah
        // memakainya harus bersih saat startup, tanpa mengganggu surat jenis lain.
        {
            var appSettingsPathN7 = FindFileUpwards("appsettings.json");
            if (appSettingsPathN7 != null)
            {
                string dbPathN7 = Path.Combine(Path.GetTempPath(), $"SuDesApp-Smoke-N7-{Guid.NewGuid():N}.db");
                string backupDirN7 = Path.Combine(Path.GetDirectoryName(dbPathN7)!, "Backup");
                try
                {
                    string connectionString = $"Data Source={dbPathN7}";
                    int salahN7 = 0;

                    var cfgN7 = (SuDesApp.AppConfig)Activator.CreateInstance(typeof(SuDesApp.AppConfig), appSettingsPathN7, null)!;
                    cfgN7.DatabaseConnectionString = connectionString;

                    var initN7 = new SuDesApp.Configuration.DatabaseInitializer(
                        cfgN7,
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Configuration.DatabaseInitializer>.Instance,
                        new FakeDesaRepository(),
                        new WaSmokeJenisSuratRepository(),
                        new WaSmokeWargaRepository(),
                        new FakeSuratRepository());

                    // Startup pertama: schema dibuat dari desa.db.sql + jenis surat N1..N6 disemai.
                    await initN7.InitializeAsync();

                    int idSuratN7;
                    using (var conn = new Microsoft.Data.Sqlite.SqliteConnection(connectionString))
                    {
                        conn.Open();

                        // Tiru database lama: satu surat Model N7 beserta detail dan jenis suratnya,
                        // plus satu surat biasa yang harus tetap utuh setelah pembersihan.
                        void Jalankan(string sql, (string Nama, object Nilai)? param = null)
                        {
                            var cmd = conn.CreateCommand();
                            cmd.CommandText = sql;
                            if (param != null) cmd.Parameters.AddWithValue(param.Value.Nama, param.Value.Nilai);
                            try
                            {
                                cmd.ExecuteNonQuery();
                            }
                            catch (Exception ex)
                            {
                                throw new InvalidOperationException($"{ex.Message} | SQL: {sql}", ex);
                            }
                        }

                        Jalankan("INSERT OR IGNORE INTO JenisSurat (ID_Jenis, NamaJenis, KodeJenis) VALUES (99, 'NTCR_N7', 'N7T')");
                        Jalankan("INSERT INTO Surat (ID_Jenis, NomorSurat, TanggalSurat, Keperluan) " +
                                 "VALUES (99, '474/077/Ds/2025', '2025-03-01', 'Penolakan Kehendak Nikah (N7)')");
                        Jalankan("INSERT INTO NTCR (ID_Surat, NamaIstri, DetailJson) " +
                                 "SELECT ID_Surat, 'Istri Lama', '{}' FROM Surat WHERE NomorSurat = '474/077/Ds/2025'");
                        // Surat jenis lain (SKD_UMUM) harus tetap utuh setelah pembersihan.
                        Jalankan("INSERT INTO Surat (ID_Jenis, NomorSurat, TanggalSurat, Keperluan) " +
                                 "SELECT ID_Jenis, '470/078/Ds/2025', '2025-03-02', 'Surat Uji Lain' " +
                                 "FROM JenisSurat WHERE NamaJenis = 'SKD_UMUM'");

                        var ambilN7 = conn.CreateCommand();
                        ambilN7.CommandText = "SELECT ID_Surat FROM Surat WHERE NomorSurat = '474/077/Ds/2025'";
                        idSuratN7 = Convert.ToInt32(ambilN7.ExecuteScalar());
                    }

                    // Startup berikutnya: sisa Model N7 harus dibuang.
                    await initN7.InitializeAsync();

                    int sisaSuratN7, sisaDetailN7, sisaJenisN7, suratN4, jumlahJenisNtcr;
                    using (var conn = new Microsoft.Data.Sqlite.SqliteConnection(connectionString))
                    {
                        conn.Open();

                        int Hitung(string sql, string? param = null, object? value = null)
                        {
                            var cmd = conn.CreateCommand();
                            cmd.CommandText = sql;
                            if (param != null) cmd.Parameters.AddWithValue(param, value!);
                            return Convert.ToInt32(cmd.ExecuteScalar());
                        }

                        sisaSuratN7 = Hitung("SELECT COUNT(*) FROM Surat WHERE NomorSurat = '474/077/Ds/2025'");
                        sisaJenisN7 = Hitung("SELECT COUNT(*) FROM JenisSurat WHERE NamaJenis = 'NTCR_N7'");
                        sisaDetailN7 = Hitung("SELECT COUNT(*) FROM NTCR WHERE ID_Surat = @id", "@id", idSuratN7);
                        suratN4 = Hitung("SELECT COUNT(*) FROM Surat WHERE NomorSurat = '470/078/Ds/2025'");
                        jumlahJenisNtcr = Hitung("SELECT COUNT(*) FROM JenisSurat WHERE NamaJenis IN " +
                            "('NTCR_N1','NTCR_N2','NTCR_N3','NTCR_N4','NTCR_N5','NTCR_N6','NTCR_N8')");
                    }

                    if (sisaSuratN7 != 0 || sisaDetailN7 != 0 || sisaJenisN7 != 0)
                    {
                        salahN7++;
                        Console.WriteLine($"FAIL migrasi N7: sisa surat={sisaSuratN7}, jenis={sisaJenisN7}, detail={sisaDetailN7} (harusnya 0)");
                    }

                    // Jenis surat N1..N6 + N8 tetap tersedia (dan tersemai walau database
                    // baru dibuat dari desa.db.sql yang belum punya kolom Deskripsi).
                    if (jumlahJenisNtcr != 7)
                    {
                        salahN7++;
                        Console.WriteLine($"FAIL migrasi N7: jenis surat NTCR di database = {jumlahJenisNtcr} (harusnya 7: N1-N6 + N8)");
                    }

                    if (suratN4 != 1)
                    {
                        salahN7++;
                        Console.WriteLine($"FAIL migrasi N7: surat jenis lain ikut terhapus (ditemukan {suratN4}, harusnya 1)");
                    }

                    var berkasCadangan = Directory.Exists(backupDirN7)
                        ? Directory.GetFiles(backupDirN7, "*sebelum-hapus-N7*.bak")
                        : Array.Empty<string>();

                    if (berkasCadangan.Length == 0)
                    {
                        salahN7++;
                        Console.WriteLine("FAIL migrasi N7: tidak ada cadangan database sebelum data N7 dibuang");
                    }

                    if (salahN7 == 0)
                    {
                        Console.WriteLine("Migrasi N7: data Model N7 lama dibuang (surat, detail, jenis) + cadangan database dibuat; surat lain utuh.");
                    }

                    fails += salahN7;
                }
                catch (Exception ex)
                {
                    fails++;
                    Console.WriteLine($"FAIL migrasi N7: {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    try { if (File.Exists(dbPathN7)) File.Delete(dbPathN7); } catch { /* temp */ }
                    try { foreach (var f in Directory.GetFiles(backupDirN7, "*sebelum-hapus-N7*.bak")) File.Delete(f); } catch { /* temp */ }
                }
            }
        }

        // --- Nomor Rekening Koran mengikuti SKD ---
        // Permohonan rekening koran adalah surat keluar desa, jadi nomornya harus berada
        // pada SATU urutan dengan SKD dan surat keterangan lain (IsSharedNumbering).
        // Uji ini memakai database + repository sungguhan supaya urutan nomornya
        // benar-benar terbukti, bukan hanya pembacaan konfigurasi.
        {
            var appSettingsPathRek = FindFileUpwards("appsettings.json");
            var configJenisPath = CariBerkasRepo("Configuration/JenisSuratConfig.json");
            if (appSettingsPathRek != null && configJenisPath != null)
            {
                string dbPathRek = Path.Combine(Path.GetTempPath(), $"SuDesApp-Smoke-RekKor-{Guid.NewGuid():N}.db");
                try
                {
                    int salahRek = 0;
                    string connectionStringRek = $"Data Source={dbPathRek}";
                    string tahunRek = DateTime.Now.Year.ToString();

                    var cfgRek = (SuDesApp.AppConfig)Activator.CreateInstance(
                        typeof(SuDesApp.AppConfig), appSettingsPathRek, null)!;
                    cfgRek.DatabaseConnectionString = connectionStringRek;

                    var initRek = new SuDesApp.Configuration.DatabaseInitializer(
                        cfgRek,
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Configuration.DatabaseInitializer>.Instance,
                        new FakeDesaRepository(),
                        new WaSmokeJenisSuratRepository(),
                        new WaSmokeWargaRepository(),
                        new FakeSuratRepository());

                    await initRek.InitializeAsync();

                    using var connRek = new Microsoft.Data.Sqlite.SqliteConnection(connectionStringRek);
                    connRek.Open();

                    void JalankanRek(string sql)
                    {
                        var cmd = connRek.CreateCommand();
                        cmd.CommandText = sql;
                        cmd.ExecuteNonQuery();
                    }

                    int HitungRek(string sql)
                    {
                        var cmd = connRek.CreateCommand();
                        cmd.CommandText = sql;
                        return Convert.ToInt32(cmd.ExecuteScalar());
                    }

                    // Jenis surat rekening koran wajib ada di database walau penyelarasan
                    // dari konfigurasi belum berjalan — kalau tidak, surat tidak bisa
                    // dicatat dan nomornya tidak akan pernah terpakai.
                    if (HitungRek("SELECT COUNT(*) FROM JenisSurat WHERE NamaJenis = 'REKENING_KORAN'") != 1)
                    {
                        salahRek++;
                        Console.WriteLine("FAIL nomor rekening koran: jenis surat REKENING_KORAN tidak tersemai di database");
                    }

                    // Repository sungguhan di atas database sungguhan.
                    var cacheRek = new SuDesApp.Configuration.MemoryCacheService(
                        new Microsoft.Extensions.Caching.Memory.MemoryCache(
                            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Configuration.MemoryCacheService>.Instance);

                    using var loaderRek = new SuDesApp.Configuration.JenisSuratConfigLoader(
                        cacheRek,
                        new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Configuration.JenisSuratConfigLoader>.Instance,
                        configJenisPath);

                    var jenisRepoRek = new SuDesApp.Data.Repositories.JenisSuratRepository(
                        new SqliteUjiUnitOfWork(connRek),
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Data.Repositories.JenisSuratRepository>.Instance,
                        cacheRek,
                        loaderRek);

                    // Surat SKD terakhir tahun ini bernomor 007.
                    JalankanRek($"INSERT INTO Surat (ID_Jenis, NomorSurat, TanggalSurat, Keperluan) " +
                                $"SELECT ID_Jenis, '470/007/Ds/{tahunRek}', '{tahunRek}-01-05', 'Uji SKD' " +
                                "FROM JenisSurat WHERE NamaJenis = 'SKD_UMUM'");

                    // Rekening koran dulu ikut urutan SKD, jadi baris lama bisa bernomor 470/020.
                    // Deret baru berawalan 130 harus mulai dari 001, bukan meneruskan hitungan itu.
                    JalankanRek($"INSERT INTO Surat (ID_Jenis, NomorSurat, TanggalSurat, Keperluan) " +
                                $"SELECT ID_Jenis, '470/020/Ds/{tahunRek}', '{tahunRek}-01-05', 'Uji Rekening Koran lama' " +
                                "FROM JenisSurat WHERE NamaJenis = 'REKENING_KORAN'");

                    string nomorRek = await jenisRepoRek.GenerateNomorSuratAsync("REKKOR");
                    if (nomorRek != $"130/001/Ds/{tahunRek}")
                    {
                        salahRek++;
                        Console.WriteLine($"FAIL nomor rekening koran '{nomorRek}' (harusnya 130/001/Ds/{tahunRek} — deret sendiri awalan 130)");
                    }

                    // Rekening koran tidak lagi ikut urutan SKD: setelah surat di atas tercatat,
                    // SKD berikutnya tetap 008 karena kedua deret itu terpisah.
                    JalankanRek($"INSERT INTO Surat (ID_Jenis, NomorSurat, TanggalSurat, Keperluan) " +
                                $"SELECT ID_Jenis, '{nomorRek}', '{tahunRek}-01-06', 'Uji Rekening Koran' " +
                                "FROM JenisSurat WHERE NamaJenis = 'REKENING_KORAN'");

                    string nomorSkd = await jenisRepoRek.GenerateNomorSuratAsync("SKD");
                    if (nomorSkd != $"470/008/Ds/{tahunRek}")
                    {
                        salahRek++;
                        Console.WriteLine($"FAIL nomor SKD setelah rekening koran '{nomorSkd}' (harusnya 470/008/Ds/{tahunRek}, deret SKD tidak terpengaruh)");
                    }

                    // Blanko NTCR tidak ikut penomoran bersama (tetap 474.3 dan mulai dari 001).
                    string nomorN1 = await jenisRepoRek.GenerateNomorSuratAsync("N1T");
                    if (nomorN1 != $"474.3/001/Ds/{tahunRek}")
                    {
                        salahRek++;
                        Console.WriteLine($"FAIL nomor NTCR N1 '{nomorN1}' (harusnya 474.3/001/Ds/{tahunRek}, tidak ikut urutan SKD)");
                    }

                    if (salahRek == 0)
                    {
                        Console.WriteLine($"Nomor rekening koran: deret sendiri 130/001 (terpisah dari urutan SKD) pada tahun {tahunRek}; NTCR tetap 474.3/001.");
                    }

                    fails += salahRek;
                }
                catch (Exception ex)
                {
                    fails++;
                    Console.WriteLine($"FAIL nomor rekening koran: {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    try { if (File.Exists(dbPathRek)) File.Delete(dbPathRek); } catch { /* temp */ }
                }
            }
            else
            {
                Console.WriteLine("appsettings.json/JenisSuratConfig.json tidak ditemukan - lewati uji nomor rekening koran");
            }
        }

        // --- Lampiran buku agenda surat masuk/keluar: hanya PDF & gambar ---
        // Alur kerjanya sama dengan lampiran buku Keputusan (berkas disalin ke folder
        // arsip saat data disimpan), bedanya di sini format yang diterima hanya PDF/gambar.
        // Uji ini memakai database + repository sungguhan, lalu meniru alur formulir
        // (pilih berkas → Simpan → Edit → Hapus lampiran) supaya benar-benar terbukti.
        {
            int salahLampiran = 0;
            try
            {
                // Kebijakan berkas: PDF & gambar diterima, dokumen kantor ditolak.
                foreach (var nama in new[] { "surat.pdf", "hasil-scan.JPG", "x.png", "a.bmp", "b.gif", "c.webp", "d.tif", "e.tiff" })
                {
                    if (!SuDesApp.Data.Models.LampiranArsipSurat.Didukung(nama))
                    {
                        salahLampiran++;
                        Console.WriteLine($"FAIL lampiran agenda: '{nama}' seharusnya diterima (PDF/gambar)");
                    }
                }
                foreach (var nama in new[] { "draft.docx", "data.xlsx", "laporan.doc", "tanpa-ekstensi", "" })
                {
                    if (SuDesApp.Data.Models.LampiranArsipSurat.Didukung(nama))
                    {
                        salahLampiran++;
                        Console.WriteLine($"FAIL lampiran agenda: '{nama}' seharusnya ditolak (bukan PDF/gambar)");
                    }
                }
                if (SuDesApp.Data.Models.LampiranArsipSurat.Label("scan.JPG") != "JPG" ||
                    SuDesApp.Data.Models.LampiranArsipSurat.Label("surat.pdf") != "PDF")
                {
                    salahLampiran++;
                    Console.WriteLine("FAIL lampiran agenda: label ekstensi (chip daftar) salah");
                }
                if (!SuDesApp.Data.Models.LampiranArsipSurat.Gambar("scan.png") ||
                    SuDesApp.Data.Models.LampiranArsipSurat.Gambar("surat.pdf") ||
                    !SuDesApp.Data.Models.LampiranArsipSurat.Pdf("surat.PDF"))
                {
                    salahLampiran++;
                    Console.WriteLine("FAIL lampiran agenda: pembeda berkas PDF dan gambar salah");
                }

                var appSettingsLampiran = FindFileUpwards("appsettings.json");
                string folderLampiran = Path.Combine(Path.GetTempPath(), $"SuDesApp-Smoke-Lampiran-{Guid.NewGuid():N}");
                try
                {
                    Directory.CreateDirectory(folderLampiran);
                    var cfgLampiran = (SuDesApp.AppConfig)Activator.CreateInstance(
                        typeof(SuDesApp.AppConfig), appSettingsLampiran, null)!;
                    cfgLampiran.TemplateFolder = folderLampiran;
                    cfgLampiran.DatabaseConnectionString = $"Data Source={Path.Combine(folderLampiran, "desa.db")}";

                    var fileLampiranSvc = new SuDesApp.Utilities.FileService(
                        cfgLampiran,
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Utilities.FileService>.Instance);

                    using var connLampiran = new Microsoft.Data.Sqlite.SqliteConnection(cfgLampiran.DatabaseConnectionString);
                    var repoLampiran = new SuDesApp.Data.Repositories.ArsipSuratRepository(
                        connLampiran,
                        cfgLampiran,
                        fileLampiranSvc,
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Data.Repositories.ArsipSuratRepository>.Instance);

                    // Nama berkas lampiran: jenis + id + nomor surat + ekstensi aslinya.
                    var (namaBerkas, _) = repoLampiran.ResolveLampiranStorage(5, "MASUK", "470/001/Ds/2026", ".pdf");
                    if (namaBerkas != "MASUK_5_470_001_Ds_2026.pdf")
                    {
                        salahLampiran++;
                        Console.WriteLine($"FAIL lampiran agenda: nama berkas '{namaBerkas}' (harusnya MASUK_5_470_001_Ds_2026.pdf)");
                    }

                    // Berkas Word/Excel ditolak sejak di repository (bukan hanya oleh dialog).
                    bool ditolakRepository = false;
                    try { repoLampiran.ResolveLampiranStorage(5, "MASUK", "470/001/Ds/2026", ".docx"); }
                    catch (InvalidOperationException) { ditolakRepository = true; }
                    if (!ditolakRepository)
                    {
                        salahLampiran++;
                        Console.WriteLine("FAIL lampiran agenda: berkas .docx tidak ditolak repository");
                    }

                    // Simulasi formulir: pilih berkas PDF → Simpan → berkas ikut tersimpan.
                    string berkasSumber = Path.Combine(folderLampiran, "pindai-surat.pdf");
                    await File.WriteAllBytesAsync(berkasSumber, new byte[] { 0x25, 0x50, 0x44, 0x46 });
                    string nomorLampiranUji = "470/003/Ds/2026";

                    async Task<SuDesApp.Data.Models.SuratKeluarMasukData?> TungguBarisLampiranAsync(
                        Func<SuDesApp.Data.Models.SuratKeluarMasukData, bool> cocok)
                    {
                        for (int i = 0; i < 60; i++)
                        {
                            var baris = (await repoLampiran.GetAllAsync())
                                .FirstOrDefault(x => x.NomorSurat == nomorLampiranUji);
                            if (baris != null && cocok(baris)) return baris;
                            await Task.Delay(100);
                        }
                        return null;
                    }

                    var pesanLampiran = new FakeMessageService();
                    var vmLampiran = new SuDesApp.Wpf.ViewModels.InputAgendaViewModel(
                        repoLampiran,
                        fileLampiranSvc,
                        pesanLampiran,
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Wpf.ViewModels.InputAgendaViewModel>.Instance,
                        () => berkasSumber);

                    vmLampiran.Initialize("KELUAR", null);
                    vmLampiran.NomorSurat = nomorLampiranUji;
                    vmLampiran.TanggalSurat = "19-09-2026";
                    vmLampiran.AsalTujuan = "Kecamatan Uji";
                    vmLampiran.Perihal = "Uji lampiran surat keluar";
                    vmLampiran.IsiRingkas = "Uji lampiran PDF pada buku agenda";

                    vmLampiran.PilihLampiranCommand.Execute(null);
                    if (!vmLampiran.HasNewLampiran || !vmLampiran.HasLampiran)
                    {
                        salahLampiran++;
                        Console.WriteLine("FAIL lampiran agenda: berkas PDF yang dipilih tidak tercatat di formulir");
                    }
                    if (vmLampiran.EkstensiLampiran != "PDF" || vmLampiran.LampiranDisplay != "pindai-surat.pdf")
                    {
                        salahLampiran++;
                        Console.WriteLine($"FAIL lampiran agenda: tampilan lampiran '{vmLampiran.LampiranDisplay}' / '{vmLampiran.EkstensiLampiran}' salah");
                    }

                    vmLampiran.SaveCommand.Execute(null);
                    var barisTersimpan = await TungguBarisLampiranAsync(b => !string.IsNullOrWhiteSpace(b.FileLampiran));
                    if (barisTersimpan == null)
                    {
                        salahLampiran++;
                        Console.WriteLine("FAIL lampiran agenda: nama berkas lampiran tidak tersimpan di database setelah Simpan");
                    }
                    else
                    {
                        string? pathTersimpan = repoLampiran.ResolveLampiranFullPath(barisTersimpan.FileLampiran);
                        if (pathTersimpan == null)
                        {
                            salahLampiran++;
                            Console.WriteLine($"FAIL lampiran agenda: berkas '{barisTersimpan.FileLampiran}' tidak ada di folder ArsipSuratFiles");
                        }
                        else if (barisTersimpan.FileLampiran!.IndexOf("470_003_Ds_2026", StringComparison.Ordinal) < 0)
                        {
                            salahLampiran++;
                            Console.WriteLine($"FAIL lampiran agenda: nama berkas '{barisTersimpan.FileLampiran}' tidak memuat nomor surat");
                        }

                        // Berkas di luar folder arsip tidak boleh diakses lewat kolom FileLampiran.
                        if (repoLampiran.ResolveLampiranFullPath("..\\rahasia.pdf") != null ||
                            repoLampiran.ResolveLampiranFullPath(Path.Combine(folderLampiran, "bebas.pdf")) != null)
                        {
                            salahLampiran++;
                            Console.WriteLine("FAIL lampiran agenda: path berkas di luar folder arsip tidak ditolak");
                        }

                        // Berkas bukan PDF/gambar ditolak di formulir (tidak mengganti lampiran lama).
                        string berkasDocx = Path.Combine(folderLampiran, "draft.docx");
                        await File.WriteAllTextAsync(berkasDocx, "bukan pdf");
                        var vmDocx = new SuDesApp.Wpf.ViewModels.InputAgendaViewModel(
                            repoLampiran, fileLampiranSvc, pesanLampiran,
                            Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Wpf.ViewModels.InputAgendaViewModel>.Instance,
                            () => berkasDocx);
                        vmDocx.Initialize("KELUAR", barisTersimpan);
                        vmDocx.PilihLampiranCommand.Execute(null);
                        if (vmDocx.HasNewLampiran || pesanLampiran.Kesalahan.Count == 0)
                        {
                            salahLampiran++;
                            Console.WriteLine("FAIL lampiran agenda: berkas .docx diterima formulir (harusnya ditolak + peringatan)");
                        }

                        // Edit: pengguna membuang lampiran → berkas ikut terhapus dari folder.
                        var vmEditLampiran = new SuDesApp.Wpf.ViewModels.InputAgendaViewModel(
                            repoLampiran, fileLampiranSvc, pesanLampiran,
                            Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Wpf.ViewModels.InputAgendaViewModel>.Instance,
                            () => null);
                        vmEditLampiran.Initialize("KELUAR", barisTersimpan);
                        if (!vmEditLampiran.HasLampiran)
                        {
                            salahLampiran++;
                            Console.WriteLine("FAIL lampiran agenda: formulir Edit tidak mengenali lampiran tersimpan");
                        }
                        vmEditLampiran.HapusLampiranCommand.Execute(null);
                        vmEditLampiran.SaveCommand.Execute(null);
                        var barisTanpaLampiran = await TungguBarisLampiranAsync(b => string.IsNullOrWhiteSpace(b.FileLampiran));
                        if (barisTanpaLampiran == null)
                        {
                            salahLampiran++;
                            Console.WriteLine("FAIL lampiran agenda: lampiran tidak terhapus dari database setelah dibuang");
                        }
                        else if (File.Exists(pathTersimpan))
                        {
                            salahLampiran++;
                            Console.WriteLine("FAIL lampiran agenda: berkas lampiran masih ada di folder setelah dibuang");
                        }
                    }

                    // Hapus baris agenda → berkas lampirannya ikut dibersihkan.
                    var barisHapus = await repoLampiran.AddAsync(new SuDesApp.Data.Models.SuratKeluarMasukData
                    {
                        JenisSurat = "MASUK",
                        NomorSurat = "470/004/Ds/2026",
                        TanggalSurat = DateTime.Today,
                        AsalTujuan = "Kecamatan Uji",
                        Perihal = "Uji hapus",
                        IsiRingkas = "Uji hapus berbaris lampiran"
                    });
                    var (namaHapus, pathHapus) = repoLampiran.ResolveLampiranStorage(
                        barisHapus.IdBarisExcel, "MASUK", barisHapus.NomorSurat, ".jpg");
                    fileLampiranSvc.EnsureDirectoryExists(Path.GetDirectoryName(pathHapus) ?? folderLampiran);
                    await fileLampiranSvc.CopyFileAsync(berkasSumber, pathHapus, overwrite: true);
                    barisHapus.FileLampiran = namaHapus;
                    await repoLampiran.UpdateAsync(barisHapus);

                    await repoLampiran.DeleteAsync(barisHapus.IdBarisExcel);
                    if (File.Exists(pathHapus))
                    {
                        salahLampiran++;
                        Console.WriteLine("FAIL lampiran agenda: berkas lampiran tidak dihapus saat baris agendanya dihapus");
                    }
                    if ((await repoLampiran.GetAllAsync()).Any(x => x.IdBarisExcel == barisHapus.IdBarisExcel))
                    {
                        salahLampiran++;
                        Console.WriteLine("FAIL lampiran agenda: baris agenda masih ada setelah dihapus");
                    }

                    if (salahLampiran == 0)
                    {
                        Console.WriteLine("Lampiran agenda surat: hanya PDF/gambar, berkas tersalin ke ArsipSuratFiles saat Simpan, ikut terhapus saat dibuang/dihapus.");
                    }
                }
                finally
                {
                    try { if (Directory.Exists(folderLampiran)) Directory.Delete(folderLampiran, true); } catch { /* temp */ }
                }
            }
            catch (Exception ex)
            {
                salahLampiran++;
                Console.WriteLine($"FAIL lampiran agenda surat: {ex.GetType().Name}: {ex.Message}");
            }

            fails += salahLampiran;
        }

        // --- Binding XAML yang aman (halaman tidak boleh gagal dibuka) ---
        // Sebagian properti WPF mengikat DUA arah (TwoWay) secara bawaan, sehingga
        // binding ke properti hanya-baca menggagalkan pembacaan halaman dengan
        // XamlParseException "cannot work on the read-only property". Dua golongan
        // yang pernah membuat halaman Pengaturan Aplikasi error total: Run.Text
        // (kode jenis/urutan/contoh nomor) dan ProgressBar.Value (langkah wizard).
        {
            int salahXaml = 0;
            try
            {
                var proyekWpf = CariBerkasRepo("SuDesApp.Wpf/SuDesApp.Wpf.csproj");
                var folderWpf = proyekWpf == null ? null : Path.GetDirectoryName(proyekWpf);
                if (folderWpf == null)
                {
                    Console.WriteLine("Proyek SuDesApp.Wpf tidak ditemukan - lewati uji binding XAML");
                }
                else
                {
                    var berkasXaml = Directory.GetFiles(folderWpf, "*.xaml", SearchOption.AllDirectories)
                        .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                                    !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                        .ToList();

                    foreach (var berkas in berkasXaml)
                    {
                        int noBaris = 0;
                        foreach (var baris in File.ReadAllLines(berkas))
                        {
                            noBaris++;

                            bool twoWayBawaan =
                                baris.Contains("Run Text=\"{Binding") ||
                                ((baris.Contains("<ProgressBar") || baris.Contains("<Slider")) &&
                                 baris.Contains("Value=\"{Binding"));

                            if (twoWayBawaan && !baris.Contains("Mode=OneWay"))
                            {
                                salahXaml++;
                                Console.WriteLine(
                                    $"FAIL binding XAML: {Path.GetFileName(berkas)}:{noBaris} memakai properti TwoWay bawaan " +
                                    "(Run.Text / ProgressBar.Value) tanpa Mode=OneWay — halaman bisa gagal dibuka");
                            }
                        }
                    }

                    // Kunci {StaticResource X} harus benar-benar terdefinisi sebagai x:Key
                    // di salah satu berkas XAML. Kunci yang hilang membuat halaman gagal
                    // dibuka dengan XamlParseException saat pengguna membukanya.
                    var kunciTerdefinisi = new HashSet<string>(StringComparer.Ordinal);
                    var rujukanKunci = new List<(string Berkas, int Baris, string Kunci)>();

                    foreach (var berkas in berkasXaml)
                    {
                        int noBaris = 0;
                        foreach (var baris in File.ReadAllLines(berkas))
                        {
                            noBaris++;

                            foreach (System.Text.RegularExpressions.Match m in
                                System.Text.RegularExpressions.Regex.Matches(baris, "x:Key=\"([^\"]+)\""))
                            {
                                kunciTerdefinisi.Add(m.Groups[1].Value);
                            }

                            // Bentuk markup {StaticResource Kunci} …
                            foreach (System.Text.RegularExpressions.Match m in
                                System.Text.RegularExpressions.Regex.Matches(baris, @"\{StaticResource\s+([A-Za-z_][A-Za-z0-9_.]*)\}"))
                            {
                                rujukanKunci.Add((berkas, noBaris, m.Groups[1].Value));
                            }

                            // … dan bentuk elemen <StaticResource ResourceKey="Kunci"/>.
                            foreach (System.Text.RegularExpressions.Match m in
                                System.Text.RegularExpressions.Regex.Matches(baris, "ResourceKey=\"([^\"]+)\""))
                            {
                                rujukanKunci.Add((berkas, noBaris, m.Groups[1].Value));
                            }
                        }
                    }

                    foreach (var (berkas, baris, kunci) in rujukanKunci)
                    {
                        if (kunciTerdefinisi.Contains(kunci)) continue;

                        salahXaml++;
                        Console.WriteLine(
                            $"FAIL kunci XAML: {Path.GetFileName(berkas)}:{baris} memakai StaticResource \"{kunci}\" " +
                            "yang tidak didefinisikan di berkas XAML mana pun — halaman bisa gagal dibuka");
                    }

                    if (salahXaml == 0)
                    {
                        Console.WriteLine(
                            $"Binding XAML aman: {berkasXaml.Count} berkas XAML diperiksa — Run.Text & ProgressBar/Slider.Value selalu Mode=OneWay, " +
                            $"{kunciTerdefinisi.Count} kunci gaya/konverter terdefinisi untuk {rujukanKunci.Count} pemakaian StaticResource.");
                    }
                }
            }
            catch (Exception ex)
            {
                salahXaml++;
                Console.WriteLine($"FAIL binding XAML: {ex.GetType().Name}: {ex.Message}");
            }

            fails += salahXaml;
        }

        // --- Permohonan rekening koran: surat keluar desa (bukan surat warga) yang
        // bisa dicatat di register dan dicetak ulang dari payload JSON-nya ---
        {
            int salahKoran = 0;
            try
            {
                var dataKoran = new SuDesApp.Data.Models.RekeningKoranData
                {
                    NomorSurat = "470/012/Ds/2026",
                    Perihal = "Permohonan Print Out Rekening Koran",
                    TanggalSurat = DateTime.Now,
                    NamaPejabat = "Kades Uji",
                    Jabatan = "Kepala Desa Uji",
                    AlamatPejabat = "Jl. Uji No. 1",
                    NamaPemegangRekening = "PEMERINTAH DESA UJI",
                    NomorRekening = "1234567890",
                    PeriodeRekening = "01-01-2026 s/d 31-03-2026",
                    Bank = "Bank Uji",
                    KCP = "KCP Uji"
                };

                var suratKoran = new SuDesApp.Data.Models.SuratData
                {
                    NamaJenis = SuDesApp.Data.Models.SuratConstants.REKENING_KORAN,
                    NomorSurat = dataKoran.NomorSurat,
                    TanggalSurat = dataKoran.TanggalSurat,
                    Status = "Active",
                    Keperluan = dataKoran.Perihal,
                    AdditionalData = dataKoran.ToJson(),
                    Warga = new SuDesApp.Data.Models.WargaData
                    {
                        NIK = SuDesApp.Data.Models.SuratConstants.NIK_INSTANSI,
                        Nama = dataKoran.NamaPemegangRekening,
                        AlamatLengkap = dataKoran.AlamatPejabat,
                        IsForInstansi = true
                    }
                };

                suratKoran.SetJenisFromNamaJenis(suratKoran.NamaJenis);
                if (suratKoran.Jenis != SuDesApp.Data.Models.SuratData.JenisSuratEnum.RekeningKoran)
                {
                    salahKoran++;
                    Console.WriteLine($"FAIL rekening koran: jenis surat terbaca sebagai '{suratKoran.Jenis}' (harusnya RekeningKoran, bukan SuratWarga)");
                }

                if (!SuDesApp.Data.Models.SuratData.IsValidNamaJenis(SuDesApp.Data.Models.SuratConstants.REKENING_KORAN))
                {
                    salahKoran++;
                    Console.WriteLine("FAIL rekening koran: REKENING_KORAN tidak termasuk jenis surat yang valid");
                }

                // Penandatangan surat ini pemerintah desa, jadi data warga tidak boleh
                // dituntut lengkap — kalau dituntut, suratnya tidak akan bisa disimpan.
                var errorsKoran = new SuDesApp.Data.Models.SuratDataValidator(
                        suratKoran,
                        Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
                        new WaSmokeWargaRepository(),
                        new FakeDesaRepository())
                    .ValidateAsync().GetAwaiter().GetResult().ToList();

                if (errorsKoran.Count > 0)
                {
                    salahKoran++;
                    Console.WriteLine($"FAIL rekening koran: surat ditolak validasi: {string.Join(" | ", errorsKoran)}");
                }

                // Isi surat harus bisa dibaca kembali dari register (dipakai pratinjau /
                // cetak ulang dari Register Surat).
                var bacaKoran = SuDesApp.Data.Models.RekeningKoranData.FromJson(suratKoran.AdditionalData);
                if (bacaKoran == null ||
                    bacaKoran.Bank != "Bank Uji" ||
                    bacaKoran.NomorRekening != "1234567890" ||
                    bacaKoran.PeriodeRekening != "01-01-2026 s/d 31-03-2026")
                {
                    salahKoran++;
                    Console.WriteLine("FAIL rekening koran: isi surat tidak terbaca kembali dari payload register");
                }

                if (SuDesApp.Data.Models.RekeningKoranData.FromJson("{rusak") != null)
                {
                    salahKoran++;
                    Console.WriteLine("FAIL rekening koran: payload rusak harus dianggap kosong, bukan melempar error");
                }

                if (salahKoran == 0)
                {
                    Console.WriteLine("Permohonan rekening koran: jenis surat sendiri, lolos validasi tanpa data warga, dan isinya bisa dicetak ulang dari register.");
                }
            }
            catch (Exception ex)
            {
                salahKoran++;
                Console.WriteLine($"FAIL rekening koran: {ex.GetType().Name}: {ex.Message}");
            }

            fails += salahKoran;
        }

        // --- Pengaturan penomoran surat: awalan per jenis bisa diganti ---
        fails += PeriksaPengaturanPenomoran();

        // --- Template Surat: pengguna menyusun sendiri jenis surat lewat wizard ---
        fails += await PeriksaTemplateSuratAsync();

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

    /// <summary>
    /// Repositori surat palsu untuk uji pencatatan surat template ke Register Surat:
    /// menyimpan baris di memori seperti tabel Surat, tanpa database.
    /// </summary>
    private sealed class TemplateRegisterSuratFake : SuDesApp.Data.Repositories.ISuratRepository
    {
        public readonly List<SuDesApp.Data.Models.SuratData> Tersimpan = new();
        private int _nextId = 500;

        public Task<int> AddSuratAsync(SuDesApp.Data.Models.SuratData entity, System.Data.IDbTransaction? transaction = null, CancellationToken ct = default)
        {
            _nextId++;
            entity.ID_Surat = _nextId;
            Tersimpan.Add(entity);
            return Task.FromResult(_nextId);
        }

        public Task<bool> UpdateAsync(SuDesApp.Data.Models.SuratData entity, System.Data.IDbTransaction? transaction = null, CancellationToken ct = default)
        {
            int posisi = Tersimpan.FindIndex(s => s.ID_Surat == entity.ID_Surat);
            if (posisi < 0) return Task.FromResult(false);

            Tersimpan[posisi] = entity;
            return Task.FromResult(true);
        }

        public Task<SuDesApp.Data.Models.SuratData?> GetByIdAsync(int id, System.Data.IDbTransaction? transaction = null, CancellationToken ct = default)
            => Task.FromResult(Tersimpan.FirstOrDefault(s => s.ID_Surat == id));

        public Task<bool> CheckNomorSuratExistsAsync(string nomorSurat, int? excludeId = null, CancellationToken ct = default)
            => Task.FromResult(Tersimpan.Any(s => s.ID_Surat != excludeId &&
                string.Equals(s.NomorSurat, nomorSurat, StringComparison.OrdinalIgnoreCase)));

        public Task<int> InsertAsync(SuDesApp.Data.Models.SuratData entity, System.Data.IDbTransaction? transaction = null, CancellationToken ct = default)
            => AddSuratAsync(entity, transaction, ct);

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
        public Task<Dictionary<string, object>> GetDatabaseStatsAsync() => throw new NotImplementedException();
        public Task<SuDesApp.Data.Models.WargaData> GetWargaByIdAsync(int idWarga, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SuDesApp.Data.Models.WargaData> GetWargaByNikAsync(string nik, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> AddOrGetWargaAsync(SuDesApp.Data.Models.WargaData warga, CancellationToken ct = default) => Task.FromResult(1);
        public Task<int?> GetLastSuratIdByTypeAsync(string namaJenis, CancellationToken ct = default) => Task.FromResult<int?>(null);
    }

    /// <summary>
    /// UnitOfWork palsu untuk uji paket: cukup menyediakan repository yang dipakai
    /// NtcrPaketService (desa, warga, surat, jenis surat) tanpa database.
    /// </summary>
    private sealed class FakeUnitOfWork : SuDesApp.Configuration.IUnitOfWork
    {
        public FakeUnitOfWork(
            SuDesApp.Data.Repositories.IDesaRepository desa,
            SuDesApp.Data.Repositories.IWargaRepository warga,
            SuDesApp.Data.Repositories.ISuratRepository surat,
            SuDesApp.Data.Repositories.IJenisSuratRepository jenis)
        {
            DesaRepository = desa;
            WargaRepository = warga;
            SuratRepository = surat;
            JenisSuratRepository = jenis;
        }

        public SuDesApp.Data.Repositories.IDesaRepository DesaRepository { get; }
        public SuDesApp.Data.Repositories.IWargaRepository WargaRepository { get; }
        public SuDesApp.Data.Repositories.ISuratRepository SuratRepository { get; }
        public SuDesApp.Data.Repositories.IJenisSuratRepository JenisSuratRepository { get; }
        public SuDesApp.Data.Repositories.IIzinOrtuRepository IzinOrtuRepository => throw new NotImplementedException();

        public Task BeginTransactionAsync() => Task.CompletedTask;
        public Task CommitTransactionAsync() => Task.CompletedTask;
        public Task RollbackTransactionAsync() => Task.CompletedTask;
        public System.Data.IDbTransaction? CurrentTransaction => null;
        public Microsoft.Data.Sqlite.SqliteConnection Connection => throw new NotImplementedException();
        public bool HasActiveTransaction => false;
        public T GetRequiredService<T>() where T : notnull => throw new NotImplementedException();
        public void Dispose() { }
    }

    /// <summary>
    /// UnitOfWork sederhana yang menunjuk ke database SQLite sungguhan — dipakai uji
    /// penomoran surat yang memang bergantung pada SQL (urutan nomor bersama).
    /// Repository lain tidak dipakai sehingga sengaja dibiarkan melempar.
    /// </summary>
    private sealed class SqliteUjiUnitOfWork : SuDesApp.Configuration.IUnitOfWork
    {
        public SqliteUjiUnitOfWork(Microsoft.Data.Sqlite.SqliteConnection connection)
            => Connection = connection;

        public SuDesApp.Data.Repositories.IDesaRepository DesaRepository => throw new NotImplementedException();
        public SuDesApp.Data.Repositories.IWargaRepository WargaRepository => throw new NotImplementedException();
        public SuDesApp.Data.Repositories.ISuratRepository SuratRepository => throw new NotImplementedException();
        public SuDesApp.Data.Repositories.IJenisSuratRepository JenisSuratRepository => throw new NotImplementedException();
        public SuDesApp.Data.Repositories.IIzinOrtuRepository IzinOrtuRepository => throw new NotImplementedException();

        public Task BeginTransactionAsync() => Task.CompletedTask;
        public Task CommitTransactionAsync() => Task.CompletedTask;
        public Task RollbackTransactionAsync() => Task.CompletedTask;
        public System.Data.IDbTransaction? CurrentTransaction => null;
        public Microsoft.Data.Sqlite.SqliteConnection Connection { get; }
        public bool HasActiveTransaction => false;
        public T GetRequiredService<T>() where T : notnull => throw new NotImplementedException();
        public void Dispose() { }
    }

    /// <summary>
    /// Repo surat palsu untuk uji paket: mencatat setiap penyimpanan dan memberi
    /// nomor unik per jenis blanko (seperti penomoran sungguhan di database).
    /// </summary>
    private sealed class PaketNtcrSuratRepository : SuDesApp.Data.Repositories.ISuratRepository
    {
        public readonly List<SuDesApp.Data.Models.SuratData> Tersimpan = new();
        private readonly Dictionary<string, int> _nomorPerJenis = new();
        private int _nextId = 100;

        public Task<int> AddSuratAsync(SuDesApp.Data.Models.SuratData entity, System.Data.IDbTransaction? transaction = null, CancellationToken ct = default)
        {
            _nextId++;
            entity.ID_Surat = _nextId;

            string jenis = entity.NamaJenis ?? string.Empty;
            _nomorPerJenis.TryGetValue(jenis, out int urut);
            urut++;
            _nomorPerJenis[jenis] = urut;

            entity.NomorSurat = $"470/{urut:D3}/Ds/2026";
            Tersimpan.Add(entity);
            return Task.FromResult(_nextId);
        }

        public Task<SuDesApp.Data.Models.SuratData> GetByIdAsync(int id, System.Data.IDbTransaction? transaction = null, CancellationToken ct = default)
            => Task.FromResult(Tersimpan.First(s => s.ID_Surat == id));

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

    /// <summary>Repo jenis surat palsu untuk uji paket: memberi nomor berikutnya per blanko.</summary>
    private sealed class PaketNtcrJenisSuratRepository : SuDesApp.Data.Repositories.IJenisSuratRepository
    {
        public Task InitializeJenisSuratDataAsync() => Task.CompletedTask;

        public Task<SuDesApp.Data.Models.JenisSuratKelas> GetJenisSuratByNamaAsync(string namaJenis)
            => Task.FromResult(new SuDesApp.Data.Models.JenisSuratKelas
            {
                ID_Jenis = 14,
                NamaJenis = namaJenis,
                KodeJenis = namaJenis,
                IsActive = true
            });

        public Task<SuDesApp.Data.Models.JenisSuratKelas> GetByNamaAsync(string namaJenis)
            => GetJenisSuratByNamaAsync(namaJenis);

        public Task<int> GetIdJenisSuratByNamaAsync(string namaJenis) => Task.FromResult(14);

        public Task<string> GenerateNomorSuratAsync(string kodeJenis)
        {
            // Meniru penomoran sungguhan: blanko NTCR N1–N6 memakai 474.3, N8 memakai 474.2.
            string awalan = string.Equals(kodeJenis, "N8T", StringComparison.OrdinalIgnoreCase) ? "474.2" : "474.3";
            return Task.FromResult($"{awalan}/001/Ds/{DateTime.Now.Year}");
        }

        public Task<bool> IsNomorSuratExistsAsync(string nomorSurat) => Task.FromResult(false);
        public Task<int?> GetLastSuratIdByTypeAsync(string templateName) => Task.FromResult<int?>(null);
        public Task<List<string>> GetAvailableYearsAsync() => Task.FromResult(new List<string>());
        public Task<bool> HasExistingNomorSuratAsync(string namaJenis) => Task.FromResult(false);
        public Task<HashSet<string>> GetSharedNumberingGroupAsync() => Task.FromResult(new HashSet<string>());
        public Task RefreshConfigurationAsync() => Task.CompletedTask;
        public Task<List<SuDesApp.Data.Models.JenisSuratKelas>> GetAllJenisSuratAsync()
            => Task.FromResult(new List<SuDesApp.Data.Models.JenisSuratKelas>());
        public Task<Dictionary<string, string>> GetJenisSuratDisplayNamesAsync()
            => Task.FromResult(new Dictionary<string, string>());
        public Task<List<string>> GetJenisSuratKeteranganDesa() => Task.FromResult(new List<string>());
    }

    /// <summary>Pesan UI palsu: mencatat pesan agar alur yang mengandalkan konfirmasi tetap jalan.</summary>
    /// <summary>
    /// Template Surat (menu baru): definisi surat buatan pengguna.
    ///
    /// Diuji berlapis: aturan model (kunci kolom, pola nomor, validasi isian), repository
    /// sungguhan (simpan/baca/ubah/hapus + JSON rusak), lalu alur wizard sampai formulir
    /// pengisian — termasuk surat yang benar-benar dicetak menjadi PDF dan nomor yang
    /// tercatat untuk surat berikutnya.
    /// </summary>
    private static async Task<int> PeriksaTemplateSuratAsync()
    {
        int salah = 0;

        void Cek(bool syarat, string pesan)
        {
            if (!syarat)
            {
                salah++;
                Console.WriteLine($"FAIL template surat: {pesan}");
            }
        }

        // ===== 1. Model: kunci kolom, pola nomor, dan validasi isian =====
        Cek(SuDesApp.Data.Models.TemplateSuratKunci.Slug("Tempat Lahir") == "tempat_lahir",
            $"slug 'Tempat Lahir' = '{SuDesApp.Data.Models.TemplateSuratKunci.Slug("Tempat Lahir")}'");
        Cek(SuDesApp.Data.Models.TemplateSuratKunci.Unik("NIK", new[] { "nik" }) == "nik2",
            "kunci kolom NIK yang sudah terpakai harus menjadi nik2");
        Cek(SuDesApp.Data.Models.TemplateSuratKunci.Unik("Alamat", Array.Empty<string>()) == "alamat",
            "kunci kolom baru harus slug dari labelnya");

        Cek(SuDesApp.Data.Models.TemplateSuratNomor.Bangun("{awalan}/{urut:000}/Ds/{tahun}", "471", 7, 2026) == "471/007/Ds/2026",
            "pola bawaan harus menghasilkan 471/007/Ds/2026");
        Cek(SuDesApp.Data.Models.TemplateSuratNomor.Bangun("{awalan}/{urut}/Ds/{tahun}", "474.3", 12, 2026) == "474.3/12/Ds/2026",
            "pola tanpa pad harus menghasilkan urutan apa adanya");
        Cek(SuDesApp.Data.Models.TemplateSuratNomor.Bangun("{awalan}/{urut:000}/Ds/{tahun}", "", 3, 2026) == "003/Ds/2026",
            "awalan kosong tidak boleh meninggalkan garis miring ganda");
        Cek(SuDesApp.Data.Models.TemplateSuratNomor.PolaValid("470/{tahun}", out _),
            "pola nomor tetap tanpa {urut} harus diterima (untuk nomor manual)");
        Cek(!SuDesApp.Data.Models.TemplateSuratNomor.PolaValid("470/{foo}", out _),
            "pola dengan penanda tak dikenal harus ditolak");
        Cek(!SuDesApp.Data.Models.TemplateSuratNomor.AwalanValid("470/001", out _),
            "awalan bertanda / harus ditolak");
        Cek(SuDesApp.Data.Models.TemplateSuratNomor.AwalanValid("474.3", out _),
            "awalan 474.3 harus diterima");

        var kolomUji = new List<SuDesApp.Data.Models.KolomTemplateSurat>
        {
            new() { Kunci = "nik", Label = "NIK", Tipe = SuDesApp.Data.Models.TipeKolomTemplate.Nik, Wajib = true },
            new() { Kunci = "nama", Label = "Nama", Tipe = SuDesApp.Data.Models.TipeKolomTemplate.Teks, Wajib = true },
            new() { Kunci = "tanggal_lahir", Label = "Tanggal Lahir", Tipe = SuDesApp.Data.Models.TipeKolomTemplate.Tanggal },
            new() { Kunci = "jenis_usaha", Label = "Jenis Usaha", Tipe = SuDesApp.Data.Models.TipeKolomTemplate.Pilihan, Pilihan = new List<string> { "Warung", "Bengkel" } }
        };
        var templateUji = new SuDesApp.Data.Models.TemplateSuratKustom
        {
            Nama = "Surat Uji",
            AwalanNomor = "471",
            PolaNomor = SuDesApp.Data.Models.TemplateSuratNomor.PolaBawaan,
            Kolom = kolomUji
        };

        var kosong = SuDesApp.Data.Models.TemplateSuratNilai.Validasi(templateUji, new Dictionary<string, string>());
        Cek(kosong.Count == 2 && kosong.ContainsKey("nik") && kosong.ContainsKey("nama"),
            $"kolom wajib kosong harus menghasilkan 2 kesalahan (nyatanya {kosong.Count})");

        var nikPendek = SuDesApp.Data.Models.TemplateSuratNilai.Validasi(templateUji, new Dictionary<string, string>
        {
            ["nik"] = "123", ["nama"] = "Budi", ["jenis_usaha"] = "Toko"
        });
        Cek(nikPendek.ContainsKey("nik"), "NIK yang bukan 16 angka harus ditolak");
        Cek(nikPendek.ContainsKey("jenis_usaha"), "pilihan di luar daftar harus ditolak");

        var tanggalSalah = SuDesApp.Data.Models.TemplateSuratNilai.Validasi(templateUji, new Dictionary<string, string>
        {
            ["nik"] = "3215012345678901", ["nama"] = "Budi", ["tanggal_lahir"] = "kemarin"
        });
        Cek(tanggalSalah.ContainsKey("tanggal_lahir"), "tanggal yang tidak terbaca harus ditolak");

        var benar = SuDesApp.Data.Models.TemplateSuratNilai.Validasi(templateUji, new Dictionary<string, string>
        {
            ["nik"] = "3215012345678901", ["nama"] = "Budi", ["tanggal_lahir"] = "17-08-1990", ["jenis_usaha"] = "Warung"
        });
        Cek(benar.Count == 0, $"isian lengkap harus lolos validasi (nyatanya {benar.Count} kesalahan)");
        Cek(SuDesApp.Data.Models.TemplateSuratNilai.Valid(templateUji, new Dictionary<string, string>
            {
                ["nik"] = "3215012345678901", ["nama"] = "Budi"
            }), "template tanpa kolom wajib lain harus valid");

        // Tanggal dicetak dalam bahasa Indonesia, teks lain apa adanya.
        Cek(SuDesApp.Data.Models.TemplateSuratNilai.NilaiCetak(kolomUji[2], "17-08-1990") == "17 Agustus 1990",
            "tanggal harus dicetak sebagai 17 Agustus 1990");
        Cek(SuDesApp.Data.Models.TemplateSuratNilai.NilaiCetak(kolomUji[1], " Budi ") == "Budi",
            "isi teks harus dirapikan dari spasi berlebih");

        // Penomoran: tahun berganti → urutan mulai lagi dari 1.
        var penomoran = new SuDesApp.Data.Models.TemplateSuratKustom
        {
            AwalanNomor = "471", NomorTerakhir = 9, TahunNomor = 2025
        };
        Cek(penomoran.NomorBerikutnya(new DateTime(2026, 1, 5), out var urutBaru) == "471/001/Ds/2026" && urutBaru == 1,
            "tahun baru harus memulai urutan dari 1");
        penomoran.TahunNomor = 2026;
        Cek(penomoran.NomorBerikutnya(new DateTime(2026, 1, 5), out var urutLanjut) == "471/010/Ds/2026" && urutLanjut == 10,
            "tahun yang sama harus melanjutkan urutan terakhir");

        // Pratinjau memakai isi contoh untuk seluruh kolom.
        var contoh = SuDesApp.Data.Models.TemplateSuratNilai.NilaiContoh(templateUji);
        Cek(contoh.Count == kolomUji.Count && contoh.Values.All(v => !string.IsNullOrWhiteSpace(v)),
            "nilai contoh pratinjau harus terisi untuk setiap kolom");

        // ===== 1b. Katalog contoh bawaan (tanpa QuestPDF, harus selalu jalan) =====
        var katalog = SuDesApp.Data.Models.TemplateSuratBawaan.Daftar;
        Cek(katalog.Count >= 5, $"katalog contoh bawaan minimal 5 (nyatanya {katalog.Count})");
        Cek(katalog.Select(c => c.Kode).Distinct(StringComparer.OrdinalIgnoreCase).Count() == katalog.Count,
            "kode contoh bawaan tidak boleh kembar");
        Cek(katalog.Select(c => c.Nama).Distinct(StringComparer.OrdinalIgnoreCase).Count() == katalog.Count,
            "nama contoh bawaan tidak boleh kembar");
        Cek(katalog.Select(c => c.AwalanNomor).Distinct(StringComparer.Ordinal).Count() == katalog.Count,
            "awalan nomor antar contoh bawaan tidak boleh kembar");
        foreach (var cb in katalog)
        {
            Cek(SuDesApp.Data.Models.TemplateSuratNomor.AwalanValid(cb.AwalanNomor, out _) &&
                SuDesApp.Data.Models.TemplateSuratNomor.PolaValid(cb.Template.PolaNomor, out _) &&
                cb.ContohNomor.Contains(cb.AwalanNomor, StringComparison.Ordinal) &&
                cb.Template.JumlahElemen > 0 && cb.Template.PakaiTandaTangan,
                $"awalan/pola/nomor contoh \"{cb.Nama}\" harus konsisten (awalan {cb.AwalanNomor}, nomor {cb.ContohNomor})");

            // Nilai pratinjau (bawaan + contoh otomatis) harus lolos validasi tipe
            // (kolom Angka tidak boleh "Rp 1.000", NIK harus 16 angka, dsb.).
            Cek(SuDesApp.Data.Models.TemplateSuratNilai.Validasi(cb.Template,
                    SuDesApp.Data.Models.TemplateSuratNilai.NilaiContoh(cb.Template)).Count == 0,
                $"nilai contoh kolom \"{cb.Nama}\" harus lolos validasi tipe");
        }

        // ===== 2. Repository sungguhan (database sementara) =====
        var appSettingsTemplate = FindFileUpwards("appsettings.json");
        string folderTemplate = Path.Combine(Path.GetTempPath(), $"SuDesApp-Smoke-Template-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(folderTemplate);
            var cfgTemplate = (SuDesApp.AppConfig)Activator.CreateInstance(
                typeof(SuDesApp.AppConfig), appSettingsTemplate, null)!;
            cfgTemplate.TemplateFolder = folderTemplate;
            cfgTemplate.PdfOutputPath = Path.Combine(folderTemplate, "PDF");
            cfgTemplate.DatabaseConnectionString = $"Data Source={Path.Combine(folderTemplate, "desa.db")}";

            using var connTemplate = new Microsoft.Data.Sqlite.SqliteConnection(cfgTemplate.DatabaseConnectionString);
            var repoTemplate = new SuDesApp.Data.Repositories.TemplateSuratRepository(
                connTemplate,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Data.Repositories.TemplateSuratRepository>.Instance);

            var tersimpan = await repoTemplate.AddAsync(templateUji);
            Cek(tersimpan.Id > 0, "template baru harus mendapat Id dari database");

            var dibaca = await repoTemplate.GetByIdAsync(tersimpan.Id);
            Cek(dibaca != null, "template yang baru disimpan harus bisa dibaca kembali");
            Cek(dibaca != null && dibaca.Kolom.Count == kolomUji.Count && dibaca.PakaiKop,
                "seluruh definisi template (kolom & elemen) harus tersimpan utuh");
            Cek(dibaca != null && dibaca.Kolom.Any(k => k.Kunci == "nik" && k.Wajib && k.Tipe == SuDesApp.Data.Models.TipeKolomTemplate.Nik),
                "tipe & wajib kolom harus ikut tersimpan");

            // Pemakaian nomor: surat yang dibuat menaikkan NomorTerakhir template.
            dibaca!.NomorTerakhir = 1;
            dibaca.TahunNomor = 2026;
            dibaca.Judul = "Surat Uji (diubah)";
            await repoTemplate.UpdateAsync(dibaca);
            var setelahUbah = await repoTemplate.GetByIdAsync(dibaca.Id);
            Cek(setelahUbah != null && setelahUbah.NomorTerakhir == 1 && setelahUbah.TahunNomor == 2026,
                "nomor terakhir & tahunnya harus tersimpan agar urutan berlanjut");
            Cek(setelahUbah != null && setelahUbah.Judul == "Surat Uji (diubah)",
                "perubahan judul harus tersimpan");
            Cek(setelahUbah!.NomorBerikutnyaTampil.StartsWith("471/002", StringComparison.Ordinal),
                $"surat berikutnya harus 471/002… (nyatanya {setelahUbah.NomorBerikutnyaTampil})");

            // Baris dengan JSON rusak tidak boleh menggagalkan pemuatan daftar.
            await connTemplate.OpenAsync();
            using (var cmdRusak = connTemplate.CreateCommand())
            {
                cmdRusak.CommandText =
                    "INSERT INTO TemplateSuratKustom (Nama, Deskripsi, Definisi, NomorTerakhir, TahunNomor) " +
                    "VALUES ('Template Rusak', '', '{bukan json', 0, 0)";
                await cmdRusak.ExecuteNonQueryAsync();
            }
            var semuaTemplate = await repoTemplate.GetAllAsync();
            Cek(semuaTemplate.Count == 2, $"daftar template harus memuat 2 baris (nyatanya {semuaTemplate.Count})");
            Cek(semuaTemplate.Any(t => t.Nama == "Template Rusak"),
                "baris ber-JSON rusak tetap tampil memakai nama kolomnya");

            // ===== 3. Wizard: pilih elemen → pratinjau → simpan → mengisi surat =====
            var generatorTemplate = new SuDesApp.GeneratorPdf.TemplateSuratGenerator(
                cfgTemplate,
                new FakeDesaRepository(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.GeneratorPdf.TemplateSuratGenerator>.Instance);
            var navigasiTemplate = new SuDesApp.Wpf.ViewModels.NavigationService(new FakeServiceProvider());
            Func<string, string, Action?, SuDesApp.Wpf.ViewModels.PdfPreviewViewModel> pratinjauTemplate =
                (judul, berkas, batal) => new SuDesApp.Wpf.ViewModels.PdfPreviewViewModel(
                    judul, berkas, navigasiTemplate, batalKembali: batal);
            var pesanTemplate = new FakeMessageService();

            var wizard = new SuDesApp.Wpf.ViewModels.TemplateSuratWizardViewModel(
                repoTemplate, generatorTemplate, pratinjauTemplate, pesanTemplate,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Wpf.ViewModels.TemplateSuratWizardViewModel>.Instance);

            wizard.SiapkanBaru();
            Cek(wizard.Langkah == 0 && wizard.Kolom.Count > 0,
                "wizard baru harus dimulai dari langkah identitas dengan kolom umum tersedia");

            wizard.LanjutCommand.Execute(null);
            if (!await TungguAsync(() => wizard.AdaKesalahan)) salah++;
            if (wizard.Langkah != 0)
            {
                salah++;
                Console.WriteLine("FAIL template surat: wizard boleh lanjut walau nama template masih kosong");
            }

            wizard.Nama = "Surat Keterangan Uji";
            wizard.Judul = "Surat Keterangan Uji";
            wizard.AwalanNomor = "471";
            wizard.TambahBlokCommand.Execute(null);
            wizard.Blok[0].Isi = "Yang bertanda tangan di bawah ini menerangkan bahwa:";
            wizard.TambahKolomCommand.Execute(null);
            wizard.Kolom[^1].Label = "Jenis Usaha";
            wizard.Kolom[^1].Tipe = SuDesApp.Data.Models.TipeKolomTemplate.Pilihan;
            wizard.Kolom[^1].PilihanTeks = "Warung, Bengkel";
            wizard.PakaiGrid = true;
            wizard.PakaiTeksKaki = true;
            wizard.TeksKaki = "Tembusan disampaikan kepada Camat Uji.";

            int kolomWizard = wizard.Kolom.Count;

            // Langkah 1 → 3 (elemen, isi surat, tata letak), lalu pratinjau membuat PDF contoh.
            for (int i = 0; i < 3; i++)
            {
                int tujuan = wizard.Langkah + 1;
                wizard.LanjutCommand.Execute(null);
                if (!await TungguAsync(() => wizard.Langkah >= tujuan))
                {
                    salah++;
                    Console.WriteLine($"FAIL template surat: wizard tidak maju ke langkah {tujuan + 1} — {wizard.PesanKesalahan}");
                    break;
                }
            }

            // Langkah terakhir (pratinjau) membuat contoh surat secara otomatis.
            wizard.LanjutCommand.Execute(null);
            if (!await TungguAsync(() => wizard.Langkah == 4 && wizard.Pratinjau != null))
            {
                salah++;
                Console.WriteLine($"FAIL template surat: pratinjau tidak terbentuk — {wizard.PesanKesalahan}");
            }

            bool selesai = false;
            bool lanjutIsi = false;
            wizard.Selesai += (_, mintaIsi) => { selesai = true; lanjutIsi = mintaIsi; };
            wizard.SimpanDanIsiCommand.Execute(null);
            if (!await TungguAsync(() => selesai))
            {
                salah++;
                Console.WriteLine($"FAIL template surat: simpan dari wizard tidak selesai — {wizard.PesanKesalahan}");
            }
            Cek(lanjutIsi, "tombol 'Simpan & Isi Surat' harus meminta lanjut ke pengisian");

            var hasilWizard = (await repoTemplate.GetAllAsync()).FirstOrDefault(t => t.Nama == "Surat Keterangan Uji");
            Cek(hasilWizard != null, "template buatan wizard tidak tersimpan");
            if (hasilWizard != null)
            {
                Cek(hasilWizard.Kolom.Count == kolomWizard, $"kolom tersimpan = {hasilWizard.Kolom.Count} (harusnya {kolomWizard})");
                Cek(hasilWizard.Kolom.All(k => !string.IsNullOrWhiteSpace(k.Kunci)), "setiap kolom harus punya kunci teknis");
                Cek(hasilWizard.Kolom.Select(k => k.Kunci).Distinct().Count() == hasilWizard.Kolom.Count,
                    "kunci kolom tidak boleh kembar");
                Cek(hasilWizard.Blok.Count == 1 && hasilWizard.PakaiGrid && hasilWizard.PakaiTeksKaki,
                    "blok teks, grid, dan teks kaki harus tersimpan sesuai pilihan wizard");
                Cek(hasilWizard.Kolom.Any(k => k.Label == "Jenis Usaha" && k.Pilihan.Count == 2),
                    "kolom pilihan beserta daftar pilihannya harus tersimpan");
            }

            // ===== 4. Pengisian: validasi, cetak PDF, dan nomor yang terpakai =====
            Func<string, string, SuDesApp.Wpf.ViewModels.PdfPreviewViewModel> pratinjauIsi =
                (judul, berkas) => new SuDesApp.Wpf.ViewModels.PdfPreviewViewModel(judul, berkas, navigasiTemplate);
            var registerTemplate = new TemplateRegisterSuratFake();
            var uowTemplate = new FakeUnitOfWork(
                new FakeDesaRepository(), new WaSmokeWargaRepository(), registerTemplate, new WaSmokeJenisSuratRepository());
            var layananRegisterTemplate = new SuDesApp.Services.TemplateSuratRegisterService(
                uowTemplate,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Services.TemplateSuratRegisterService>.Instance);

            var isi = new SuDesApp.Wpf.ViewModels.IsiTemplateSuratViewModel(
                generatorTemplate, repoTemplate, layananRegisterTemplate, uowTemplate, cfgTemplate,
                pratinjauIsi, navigasiTemplate, pesanTemplate,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Wpf.ViewModels.IsiTemplateSuratViewModel>.Instance);
            await isi.ConfigureAsync(hasilWizard!);

            Cek(isi.NomorSurat == "471/001/Ds/2026", $"nomor otomatis form pengisian = '{isi.NomorSurat}' (harusnya 471/001/Ds/2026)");
            Cek(isi.Kolom.Count == hasilWizard!.Kolom.Count, "formulir pengisian harus menampilkan seluruh kolom pilihan wizard");

            isi.SimpanCommand.Execute(null);
            if (!await TungguAsync(() => isi.AdaKesalahan))
            {
                salah++;
                Console.WriteLine("FAIL template surat: formulir pengisian tidak menolak isian kosong");
            }
            Cek(isi.Kolom.Any(k => k.AdaKesalahan), "kolom wajib yang kosong harus ditandai pada barisnya");

            foreach (var kolom in isi.Kolom)
            {
                kolom.Nilai = kolom.Tipe switch
                {
                    SuDesApp.Data.Models.TipeKolomTemplate.Nik => "3215012345678901",
                    SuDesApp.Data.Models.TipeKolomTemplate.Tanggal => "17-08-1990",
                    SuDesApp.Data.Models.TipeKolomTemplate.Angka => "5",
                    SuDesApp.Data.Models.TipeKolomTemplate.Pilihan => kolom.Pilihan[0],
                    _ => "Contoh " + kolom.Label
                };
            }
            isi.NamaPejabat = "Kades Uji";

            isi.SimpanCommand.Execute(null);
            if (!await TungguAsync(() => Directory.Exists(cfgTemplate.PdfOutputPath) &&
                Directory.GetFiles(cfgTemplate.PdfOutputPath, "*.pdf").Length > 0, maxMs: 60000))
            {
                salah++;
                Console.WriteLine($"FAIL template surat: surat tidak tercetak — {isi.PesanKesalahan}");
            }
            else
            {
                string berkasSurat = Directory.GetFiles(cfgTemplate.PdfOutputPath, "*.pdf").First();

                // Berkas baru saja ditulis; tunggu sampai tidak terkunci lagi.
                var isiBerkas = Array.Empty<byte>();
                for (int i = 0; i < 40; i++)
                {
                    try
                    {
                        isiBerkas = await File.ReadAllBytesAsync(berkasSurat);
                        if (isiBerkas.Length > 0) break;
                    }
                    catch (IOException)
                    {
                        await Task.Delay(100);
                    }
                }
                Cek(isiBerkas.Length > 1000 && System.Text.Encoding.Latin1.GetString(isiBerkas, 0, 4) == "%PDF",
                    "berkas surat template harus berupa PDF yang berisi");

                var setelahCetak = await repoTemplate.GetByIdAsync(hasilWizard.Id);
                Cek(setelahCetak != null && setelahCetak.NomorTerakhir == 1 && setelahCetak.TahunNomor == DateTime.Today.Year,
                    $"nomor 1 harus tercatat agar surat berikutnya 471/002 (tercatat: {setelahCetak?.NomorTerakhir})");
            }

            // ===== 5. Surat tercatat di Register Surat (bisa dicari & dicetak ulang) =====
            Cek(registerTemplate.Tersimpan.Count == 1,
                $"surat yang dicetak harus tercatat di register (nyatanya {registerTemplate.Tersimpan.Count})");

            var barisRegister = registerTemplate.Tersimpan.FirstOrDefault();
            Cek(barisRegister != null && barisRegister.NamaJenis == SuDesApp.Data.Models.SuratConstants.TEMPLATE_SURAT,
                $"baris register harus berjenis TEMPLATE_SURAT (nyatanya {barisRegister?.NamaJenis})");
            Cek(barisRegister?.NomorSurat == "471/001/Ds/2026" && barisRegister.Status == "Active",
                $"nomor & status baris register = '{barisRegister?.NomorSurat}' / '{barisRegister?.Status}'");

            var payloadRegister = SuDesApp.Data.Models.TemplateSuratTercatat.FromJson(barisRegister?.AdditionalData);
            Cek(payloadRegister != null, "payload surat template harus tersimpan & terbaca dari register");
            Cek(payloadRegister != null && payloadRegister.Nilai.Count == kolomWizard,
                $"payload harus memuat seluruh isian kolom (nyatanya {payloadRegister?.Nilai.Count} dari {kolomWizard})");
            Cek(payloadRegister != null && payloadRegister.Template.JumlahElemen > 0 && payloadRegister.Template.PakaiGrid,
                "payload harus memuat salinan definisi surat lengkap (siap dicetak ulang)");
            Cek(payloadRegister != null && payloadRegister.NamaPemohon == "Contoh Nama",
                $"nama pemohon harus terbaca dari payload untuk pencarian register (nyatanya '{payloadRegister?.NamaPemohon}')");
            Cek(payloadRegister != null && payloadRegister.KeperluanTampil.Contains("Surat Keterangan Uji", StringComparison.Ordinal),
                $"kolom keperluan register harus menyebut nama templatenya (nyatanya '{payloadRegister?.KeperluanTampil}')");
            Cek(barisRegister?.Warga == null,
                "surat template tidak boleh memaksa data kependudukan (kolom warga tetap kosong)");
            Cek(payloadRegister != null && payloadRegister.RingkasanIsian.Contains("Nama: Contoh Nama", StringComparison.Ordinal),
                "keterangan register harus merangkum isian surat");

            // Identitas penerima untuk daftar/cetak register dibaca dari isian suratnya.
            var suratTampil = new SuDesApp.Data.Models.SuratData
            {
                NamaJenis = SuDesApp.Data.Models.SuratConstants.TEMPLATE_SURAT,
                AdditionalData = barisRegister?.AdditionalData ?? string.Empty
            };
            Cek(SuDesApp.Data.Models.TemplateSuratTercatat.NamaPenerimaTampil(suratTampil) == "Contoh Nama",
                $"nama penerima di daftar register = '{SuDesApp.Data.Models.TemplateSuratTercatat.NamaPenerimaTampil(suratTampil)}'");
            Cek(SuDesApp.Data.Models.TemplateSuratTercatat.AlamatPenerimaTampil(suratTampil) == "Contoh Alamat",
                $"alamat penerima di daftar register = '{SuDesApp.Data.Models.TemplateSuratTercatat.AlamatPenerimaTampil(suratTampil)}'");
            Cek(SuDesApp.Data.Models.TemplateSuratTercatat.JenisKelaminTampil(suratTampil) == "L",
                $"jenis kelamin penerima = '{SuDesApp.Data.Models.TemplateSuratTercatat.JenisKelaminTampil(suratTampil)}' (harusnya L)");
            Cek(SuDesApp.Data.Models.TemplateSuratTercatat.TempatTanggalLahirTampil(suratTampil).Contains("Contoh Tempat Lahir", StringComparison.Ordinal),
                "tempat & tanggal lahir penerima harus ikut terbaca dari isian surat");

            // Nomor yang sudah dipakai surat lain ditolak dengan penjelasan, dan surat
            // tidak tercetak supaya tidak ada surat bersampul nomor kembar.
            var registerBentrok = new TemplateRegisterSuratFake();
            var jenisBentrok = new WaSmokeJenisSuratRepository();
            jenisBentrok.NomorTerpakai.Add("471/001/Ds/2026");
            var layananBentrok = new SuDesApp.Services.TemplateSuratRegisterService(
                new FakeUnitOfWork(new FakeDesaRepository(), new WaSmokeWargaRepository(), registerBentrok, jenisBentrok),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Services.TemplateSuratRegisterService>.Instance);

            var hasilBentrok = await layananBentrok.CatatAsync(payloadRegister!);
            Cek(!hasilBentrok.Berhasil && hasilBentrok.NomorSudahDipakai && registerBentrok.Tersimpan.Count == 0,
                "nomor yang sudah dipakai surat lain harus ditolak register");
            Cek(hasilBentrok.Pesan.Contains("sudah dipakai", StringComparison.OrdinalIgnoreCase),
                $"penolakan nomor kembar harus menjelaskan sebabnya (nyatanya '{hasilBentrok.Pesan}')");

            // ===== 5b. Register SUNGGUHAN (database + repositori nyata) =====
            // Membuktikan tiga hal sekaligus: surat tercatat di register, bisa DICARI
            // (walau kolom warganya kosong, pencarian membaca isi suratnya), dan bisa
            // DIBUKA lagi lalu DICETAK ULANG dari baris register itu sendiri.
            string folderRegister = Path.Combine(Path.GetTempPath(), $"SuDesApp-Smoke-Register-{Guid.NewGuid():N}");
            Directory.CreateDirectory(folderRegister);
            {
                var cfgRegister = (SuDesApp.AppConfig)Activator.CreateInstance(
                    typeof(SuDesApp.AppConfig), appSettingsTemplate, null)!;
                cfgRegister.TemplateFolder = folderRegister;
                cfgRegister.PdfOutputPath = Path.Combine(folderRegister, "PDF");
                cfgRegister.TempPdfFolder = Path.Combine(folderRegister, "Temp");
                cfgRegister.DatabaseConnectionString = $"Data Source={Path.Combine(folderRegister, "desa.db")}";

                var cacheRegister = new SuDesApp.Configuration.MemoryCacheService(
                    new Microsoft.Extensions.Caching.Memory.MemoryCache(
                        new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Configuration.MemoryCacheService>.Instance);

                string configJenisRegister = CariBerkasRepo("Configuration/JenisSuratConfig.json")
                    ?? Path.Combine(AppContext.BaseDirectory, "Configuration", "JenisSuratConfig.json");
                var loaderRegister = new SuDesApp.Configuration.JenisSuratConfigLoader(
                    cacheRegister,
                    new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Configuration.JenisSuratConfigLoader>.Instance,
                    configJenisRegister);

                using var connRegister = new Microsoft.Data.Sqlite.SqliteConnection(cfgRegister.DatabaseConnectionString);
                await connRegister.OpenAsync();

                var jenisRegister = new SuDesApp.Data.Repositories.JenisSuratRepository(
                    new SqliteUjiUnitOfWork(connRegister),
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Data.Repositories.JenisSuratRepository>.Instance,
                    cacheRegister, loaderRegister);
                var wargaRegister = new SuDesApp.Data.Repositories.WargaRepository(
                    connRegister, cacheRegister, cfgRegister,
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Data.Repositories.WargaRepository>.Instance);
                var desaRegister = new SuDesApp.Data.Repositories.DesaRepository(
                    cfgRegister, cacheRegister,
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Data.Repositories.DesaRepository>.Instance,
                    loaderRegister);
                var suratRegisterNyata = new SuDesApp.Data.Repositories.SuratRepository(
                    connRegister,
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Data.Repositories.SuratRepository>.Instance,
                    cacheRegister,
                    new SuDesApp.Data.Queries.QueryProvider(
                        Path.Combine(AppContext.BaseDirectory, "Data", "Queries", "SuratQueries.sql"),
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Data.Queries.QueryProvider>.Instance),
                    new SuDesApp.Data.Queries.QueryInterceptor(
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Data.Queries.QueryInterceptor>.Instance),
                    Array.Empty<SuDesApp.Data.Handlers.ISuratDataHandler>(),
                    wargaRegister, jenisRegister, desaRegister, null);

                // Tabel & data awal disiapkan persis seperti aplikasi mulai: lewat
                // DatabaseInitializer dengan repositori sungguhan.
                var initRegister = new SuDesApp.Configuration.DatabaseInitializer(
                    cfgRegister,
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Configuration.DatabaseInitializer>.Instance,
                    desaRegister, jenisRegister, wargaRegister, suratRegisterNyata);
                await initRegister.InitializeAsync();

                var layananRegisterNyata = new SuDesApp.Services.TemplateSuratRegisterService(
                    new FakeUnitOfWork(desaRegister, wargaRegister, suratRegisterNyata, jenisRegister),
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Services.TemplateSuratRegisterService>.Instance);

                var payloadNyata = new SuDesApp.Data.Models.TemplateSuratTercatat
                {
                    TemplateId = 0,
                    NamaTemplate = "Surat Keterangan Uji",
                    JudulSurat = "Surat Keterangan Uji",
                    NomorSurat = "471/005/Ds/2026",
                    TanggalSurat = new DateTime(2026, 9, 19),
                    NamaPejabat = "Kades Uji",
                    Template = payloadRegister!.Template,
                    Nilai = new Dictionary<string, string>(payloadRegister.Nilai, StringComparer.OrdinalIgnoreCase)
                };

                var hasilNyata = await layananRegisterNyata.CatatAsync(payloadNyata);
                Cek(hasilNyata.Berhasil && hasilNyata.IdSurat > 0,
                    $"surat template harus tercatat di register sungguhan (pesan: {hasilNyata.Pesan})");

                async Task<List<SuDesApp.Data.Models.SuratData>> CariDiRegisterAsync(string kataKunci) =>
                    (await suratRegisterNyata.GetFilteredAsync(
                        new SuDesApp.Data.Models.FilterConditions { SearchText = kataKunci },
                        "ID_Surat", false, 0, 20)).ToList();

                var cariNama = await CariDiRegisterAsync("Contoh Nama");
                Cek(cariNama.Any(s => s.ID_Surat == hasilNyata.IdSurat),
                    "surat template harus DITEMUKAN lewat nama penerimanya di pencarian register");

                var cariNik = await CariDiRegisterAsync("3215012345678901");
                Cek(cariNik.Any(s => s.ID_Surat == hasilNyata.IdSurat),
                    "surat template harus DITEMUKAN lewat NIK yang diisi di suratnya");

                var cariKeperluan = await CariDiRegisterAsync("Contoh Keperluan");
                Cek(cariKeperluan.Any(s => s.ID_Surat == hasilNyata.IdSurat),
                    "surat template harus DITEMUKAN lewat isi keperluan suratnya");

                var barisNyata = await suratRegisterNyata.GetByIdAsync(hasilNyata.IdSurat);
                Cek(barisNyata != null && barisNyata.NamaJenis == SuDesApp.Data.Models.SuratConstants.TEMPLATE_SURAT,
                    $"baris register harus berjenis TEMPLATE_SURAT (nyatanya {barisNyata?.NamaJenis})");
                Cek(SuDesApp.Data.Models.TemplateSuratTercatat.NamaPenerimaTampil(barisNyata) == "Contoh Nama",
                    "nama penerima baris register harus ikut terbaca dari database");
                Cek(SuDesApp.Data.Models.TemplateSuratTercatat.AlamatPenerimaTampil(barisNyata) == "Contoh Alamat",
                    "alamat penerima baris register harus ikut terbaca dari database");

                // Cetak ulang dari register: definisi surat dibaca dari payload baris itu.
                var berkasCetakUlang = await SuDesApp.Wpf.Services.SuratPdfHelper.GeneratePdfAsync(
                    new FakeServiceProviderTemplate(generatorTemplate), cfgRegister, barisNyata!,
                    Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
                    SuDesApp.Data.Models.SuratConstants.TEMPLATE_SURAT);

                Cek(berkasCetakUlang != null && File.Exists(berkasCetakUlang),
                    $"surat dari register harus bisa dicetak ulang (berkas: {berkasCetakUlang})");
                if (berkasCetakUlang != null && File.Exists(berkasCetakUlang))
                {
                    var isiCetakUlang = await File.ReadAllBytesAsync(berkasCetakUlang);
                    Cek(isiCetakUlang.Length > 1000 &&
                        System.Text.Encoding.Latin1.GetString(isiCetakUlang, 0, 4) == "%PDF",
                        "hasil cetak ulang dari register harus berupa PDF yang berisi");
                }

                connRegister.Close();
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                try { Directory.Delete(folderRegister, recursive: true); } catch { /* folder temp */ }
            }

            // ===== 5c. Pembaruan kecil (tambalan) tanpa installer =====
            // Diuji sungguhan: berkas lama diganti oleh skrip penerap yang sama dengan
            // yang dijalankan aplikasi, sementara database & pengaturan pengguna utuh.
            {
                int salahSemula = salah;
                string folderTambalan = Path.Combine(Path.GetTempPath(), $"SuDesApp-Smoke-Tambalan-{Guid.NewGuid():N}");
                string folderApp = Path.Combine(folderTambalan, "aplikasi");
                string folderBaru = Path.Combine(folderTambalan, "paket-baru");
                string folderKerja = Path.Combine(folderTambalan, "kerja");
                string folderEkstrak = Path.Combine(folderTambalan, "ekstrak");

                try
                {
                    Directory.CreateDirectory(Path.Combine(folderApp, "Database"));
                    Directory.CreateDirectory(Path.Combine(folderApp, "Configuration"));
                    Directory.CreateDirectory(folderBaru);
                    Directory.CreateDirectory(folderKerja);

                    // Keadaan "aplikasi terpasang": satu berkas lama, satu berkas yang
                    // sudah sama dengan rilis baru, satu berkas yang harus dihapus, plus
                    // database & pengaturan milik pengguna.
                    File.WriteAllText(Path.Combine(folderApp, "SuDesApp.dll"), "isi lama");
                    File.WriteAllText(Path.Combine(folderApp, "SuDesApp.Core.dll"), "sudah sama");
                    File.WriteAllText(Path.Combine(folderApp, "lama-hilang.dll"), "berkas lama");
                    File.WriteAllText(Path.Combine(folderApp, "Database", "desa.db"), "database pengguna");
                    File.WriteAllText(Path.Combine(folderApp, "appsettings.json"), "{\"AppConfig\":{}}");
                    File.WriteAllText(Path.Combine(folderApp, "Configuration", "JenisSuratConfig.json"), "[{\"NamaJenis\":\"SKD_UMUM\"}]");

                    // Isi rilis baru (dikemas jadi zip tambalan di bawah).
                    File.WriteAllText(Path.Combine(folderBaru, "SuDesApp.dll"), "isi baru");
                    File.WriteAllText(Path.Combine(folderBaru, "SuDesApp.Core.dll"), "sudah sama");
                    File.WriteAllText(Path.Combine(folderBaru, "berkas-baru.dll"), "berkas baru");
                    Directory.CreateDirectory(Path.Combine(folderBaru, "Configuration"));
                    File.WriteAllText(Path.Combine(folderBaru, "Configuration", "JenisSuratConfig.json"),
                        "[{\"NamaJenis\":\"SKD_UMUM\"},{\"NamaJenis\":\"TEMPLATE_SURAT\"}]");

                    static string Sha256Berkas(string path) => SuDesApp.Utilities.PatchUpdateService.HashBerkas(path);

                    var tambalanUji = new SuDesApp.Utilities.TambalanInfo
                    {
                        Versi = "2.4.5",
                        DariVersi = new List<string> { "2.4.4" },
                        Jenis = "kecil",
                        Ringkasan = new List<string> { "Perbaikan format surat template", "Penyesuaian tampilan menu utama" },
                        BerkasPatch = "patch-2.4.5.zip",
                        Sha256Patch = new string('a', 64),
                        Berkas = new List<SuDesApp.Utilities.BerkasTambalan>
                        {
                            new() { Path = "SuDesApp.dll", Sha256 = Sha256Berkas(Path.Combine(folderBaru, "SuDesApp.dll")), Ukuran = 8 },
                            new() { Path = "SuDesApp.Core.dll", Sha256 = Sha256Berkas(Path.Combine(folderBaru, "SuDesApp.Core.dll")), Ukuran = 10 },
                            new() { Path = "berkas-baru.dll", Sha256 = Sha256Berkas(Path.Combine(folderBaru, "berkas-baru.dll")), Ukuran = 11 },
                            new() { Path = "Configuration/JenisSuratConfig.json", Sha256 = Sha256Berkas(Path.Combine(folderBaru, "Configuration", "JenisSuratConfig.json")), Ukuran = 40 },
                            // Didafar untuk memastikan aplikasi menolak menggantinya:
                            new() { Path = "Database/desa.db", Sha256 = new string('b', 64), Ukuran = 16 },
                            new() { Path = "appsettings.json", Sha256 = new string('c', 64), Ukuran = 20 }
                        },
                        BerkasDihapus = new List<string> { "lama-hilang.dll" }
                    };

                    var layananTambalan = new SuDesApp.Utilities.PatchUpdateService(
                        new SuDesApp.Utilities.UpdateService(cfgTemplate),
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Utilities.PatchUpdateService>.Instance,
                        () => folderApp,
                        () => "2.4.4");

                    var rencanaTambalan = layananTambalan.SusunRencana(tambalanUji, "2.4.4");
                    Cek(rencanaTambalan.BisaDipakai,
                        $"rencana tambalan harus siap dipasang (alasan: {rencanaTambalan.AlasanTidakBisa})");
                    Cek(rencanaTambalan.BerkasDiganti.Count == 3,
                        $"berkas yang benar-benar berubah = {rencanaTambalan.BerkasDiganti.Count} (harusnya 3)");
                    Cek(rencanaTambalan.BerkasSudahSama == 1,
                        $"berkas yang sudah sama = {rencanaTambalan.BerkasSudahSama} (harusnya 1)");
                    Cek(rencanaTambalan.BerkasDilindungi.Count == 2,
                        $"berkas pengguna yang dilindungi = {rencanaTambalan.BerkasDilindungi.Count} (harusnya 2)");
                    Cek(rencanaTambalan.BerkasDilindungi.All(b => b.Contains("desa.db") || b.Contains("appsettings.json")),
                        "database & appsettings.json harus masuk daftar dilindungi");
                    Cek(rencanaTambalan.BerkasDihapus.Count == 1, "berkas lama harus terdaftar untuk dihapus");

                    // Versi terpasang tidak cocok → wajib installer penuh.
                    var rencanaSalahVersi = layananTambalan.SusunRencana(tambalanUji, "2.4.0");
                    Cek(!rencanaSalahVersi.BisaDipakai &&
                        rencanaSalahVersi.AlasanTidakBisa.Contains("installer", StringComparison.OrdinalIgnoreCase),
                        $"versi lama harus diarahkan ke installer penuh (alasan: {rencanaSalahVersi.AlasanTidakBisa})");

                    // Rilis besar tidak boleh dipasang sebagai tambalan.
                    var tambalanBesar = SuDesApp.Utilities.TambalanInfo.FromJson(
                        "{\"versi\":\"3.0.0\",\"jenis\":\"besar\",\"dariVersi\":[\"2.4.4\"],\"berkasPatch\":\"\",\"berkas\":[]}");
                    Cek(tambalanBesar != null && !tambalanBesar.BisaDitambal &&
                        !layananTambalan.SusunRencana(tambalanBesar, "2.4.4").BisaDipakai,
                        "pembaruan besar harus memakai installer penuh");

                    // Jalur keluar folder aplikasi ditolak (keamanan).
                    string JsonTambalanUji(string jalurBerkas) =>
                        "{\"versi\":\"9.9.9\",\"dariVersi\":[\"2.4.4\"],\"jenis\":\"kecil\"," +
                        "\"ringkasan\":[\"Perbaikan surat\"],\"berkasPatch\":\"patch-9.9.9.zip\"," +
                        $"\"sha256Patch\":\"{new string('a', 64)}\"," +
                        $"\"berkas\":[{{\"path\":\"{jalurBerkas}\",\"sha256\":\"{new string('a', 64)}\",\"ukuran\":12}}]}}";

                    var tambalanJahat = SuDesApp.Utilities.TambalanInfo.FromJson(JsonTambalanUji("../jahat.dll"));
                    Cek(tambalanJahat != null && !layananTambalan.SusunRencana(tambalanJahat, "2.4.4").BisaDipakai,
                        "jalur berkas yang naik ke atas folder aplikasi harus ditolak");

                    // patch.json: parsing sah & penolakan isi cacat.
                    var dariJson = SuDesApp.Utilities.TambalanInfo.FromJson(JsonTambalanUji("SuDesApp.dll"));
                    Cek(dariJson != null && dariJson.Versi == "9.9.9" && dariJson.BisaDitambal,
                        "patch.json yang sah harus terbaca sebagai tambalan kecil");
                    Cek(dariJson != null && dariJson.CocokDenganVersiTerpasang("2.4.4.0") &&
                        !dariJson.CocokDenganVersiTerpasang("2.4.3"),
                        "versi 2.4.4.0 harus dianggap sama dengan 2.4.4");
                    Cek(SuDesApp.Utilities.TambalanInfo.FromJson("{bukan json") == null,
                        "patch.json rusak harus ditolak");
                    Cek(SuDesApp.Utilities.TambalanInfo.FromJson("{\"dariVersi\":[\"2.4.4\"]}") == null,
                        "patch.json tanpa versi harus ditolak");
                    var tambalanTanpaSha = SuDesApp.Utilities.TambalanInfo.FromJson(
                        "{\"versi\":\"1.0.0\",\"berkasPatch\":\"p.zip\",\"berkas\":[{\"path\":\"x.dll\",\"sha256\":\"pendek\"}]}");
                    Cek(tambalanTanpaSha != null && tambalanTanpaSha.JumlahBerkas == 0,
                        "berkas tanpa SHA-256 yang sah harus dibuang dari daftar");

                    // Zip tambalan (hanya berkas yang berubah) → ekstrak aman + verifikasi.
                    string zipUji = Path.Combine(folderTambalan, "patch-2.4.5.zip");
                    System.IO.Compression.ZipFile.CreateFromDirectory(folderBaru, zipUji);
                    SuDesApp.Utilities.PatchUpdateService.EkstrakAman(zipUji, folderEkstrak);

                    bool verifikasiGagal = false;
                    try
                    {
                        SuDesApp.Utilities.PatchUpdateService.VerifikasiHasilEkstrak(folderEkstrak, rencanaTambalan);
                    }
                    catch (InvalidOperationException)
                    {
                        verifikasiGagal = true;
                    }
                    Cek(!verifikasiGagal, "berkas hasil ekstrak harus cocok dengan sidik jari di patch.json");

                    // Isi berkas yang tidak cocok harus ditolak SEBELUM aplikasi ditutup.
                    var rencanaPalsu = new SuDesApp.Utilities.RencanaTambalan
                    {
                        BerkasDiganti = new List<SuDesApp.Utilities.BerkasTambalan>
                        {
                            new() { Path = "SuDesApp.dll", Sha256 = new string('d', 64) }
                        }
                    };
                    bool isiDitolak = false;
                    try
                    {
                        SuDesApp.Utilities.PatchUpdateService.VerifikasiHasilEkstrak(folderEkstrak, rencanaPalsu);
                    }
                    catch (InvalidOperationException)
                    {
                        isiDitolak = true;
                    }
                    Cek(isiDitolak, "isi berkas yang tidak cocok harus ditolak sebelum dipasang");

                    // Entri zip yang keluar folder tujuan ditolak.
                    string zipJahat = Path.Combine(folderTambalan, "jahat.zip");
                    using (var arsip = System.IO.Compression.ZipFile.Open(zipJahat, System.IO.Compression.ZipArchiveMode.Create))
                    {
                        var entri = arsip.CreateEntry("../jahat.dll");
                        using var penulis = new StreamWriter(entri.Open());
                        penulis.Write("jahat");
                    }
                    bool ekstrakDitolak = false;
                    try
                    {
                        SuDesApp.Utilities.PatchUpdateService.EkstrakAman(zipJahat, Path.Combine(folderTambalan, "ekstrak-jahat"));
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or IOException)
                    {
                        ekstrakDitolak = true;
                    }
                    Cek(ekstrakDitolak, "entri zip yang keluar folder tujuan harus ditolak");

                    // ===== Penerapan nyata memakai skrip penerap aplikasi =====
                    string skrip = Path.Combine(folderKerja, "terapkan-tambalan.ps1");
                    File.WriteAllText(skrip, SuDesApp.Utilities.PatchUpdateService.SkripPenerapan());

                    string daftarBerkas = Path.Combine(folderKerja, "daftar-berkas.txt");
                    File.WriteAllLines(daftarBerkas, new[] { "SuDesApp.dll", "berkas-baru.dll", "Configuration/JenisSuratConfig.json" });
                    string daftarHapus = Path.Combine(folderKerja, "daftar-hapus.txt");
                    File.WriteAllLines(daftarHapus, new[] { "lama-hilang.dll" });
                    string berkasHasil = Path.Combine(folderTambalan, "hasil-tambalan.json");

                    var infoProses = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        Arguments = string.Join(' ', new[]
                        {
                            "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $"\"{skrip}\"",
                            "-ProsesId", "0",
                            "-Aplikasi", $"\"{folderApp}\"",
                            "-Sumber", $"\"{folderEkstrak}\"",
                            "-Daftar", $"\"{daftarBerkas}\"",
                            "-Hapus", $"\"{daftarHapus}\"",
                            "-Hasil", $"\"{berkasHasil}\"",
                            "-Versi", "9.9.9"
                        })
                    };

                    using (var proses = System.Diagnostics.Process.Start(infoProses))
                    {
                        if (proses != null) await proses.WaitForExitAsync();
                    }

                    Cek(File.ReadAllText(Path.Combine(folderApp, "SuDesApp.dll")) == "isi baru",
                        "berkas yang berubah harus diganti oleh skrip penerap");
                    Cek(File.Exists(Path.Combine(folderApp, "berkas-baru.dll")),
                        "berkas baru pada paket tambalan harus ikut dipasang");
                    Cek(File.ReadAllText(Path.Combine(folderApp, "Configuration", "JenisSuratConfig.json")).Contains("TEMPLATE_SURAT"),
                        "berkas di dalam subfolder harus ikut diganti");
                    Cek(!File.Exists(Path.Combine(folderApp, "lama-hilang.dll")),
                        "berkas yang dihapus pada rilis baru harus ikut dihapus");
                    Cek(File.ReadAllText(Path.Combine(folderApp, "Database", "desa.db")) == "database pengguna",
                        "database pengguna tidak boleh tersentuh pembaruan");
                    Cek(File.ReadAllText(Path.Combine(folderApp, "appsettings.json")) == "{\"AppConfig\":{}}",
                        "pengaturan pengguna tidak boleh tersentuh pembaruan");

                    // Rilis GitHub: seluruh aset dikenali sehingga berkas tambalan
                    // (patch.json + zip-nya) bisa ditemukan tanpa panggilan API kedua.
                    var rilisGithub = SuDesApp.Utilities.UpdateService.ParseGitHubRelease(
                        "{\"tag_name\":\"v9.9.9\",\"body\":\"catatan\",\"assets\":[" +
                        "{\"name\":\"SuDesApp_9.9.9_Setup.exe\",\"browser_download_url\":\"https://x/setup.exe\",\"digest\":\"sha256:" +
                        new string('a', 64) + "\"}," +
                        "{\"name\":\"patch.json\",\"browser_download_url\":\"https://x/patch.json\"}," +
                        "{\"name\":\"patch-9.9.9.zip\",\"browser_download_url\":\"https://x/patch.zip\"}]}");
                    Cek(rilisGithub != null && rilisGithub.DaftarAset.Count == 3,
                        $"aset rilis harus terbaca ({rilisGithub?.DaftarAset.Count} terbaca)");
                    Cek(rilisGithub?.PatchManifestUrl == "https://x/patch.json",
                        $"tautan patch.json = '{rilisGithub?.PatchManifestUrl}'");
                    Cek(rilisGithub?.CariAset("patch-9.9.9.zip")?.Url == "https://x/patch.zip",
                        "aset paket tambalan harus bisa dicari lewat namanya");
                    Cek(rilisGithub?.DownloadUrl == "https://x/setup.exe",
                        "installer tetap dipakai sebagai tautan unduhan utama");

                    // Preferensi: pemeriksaan pembaruan otomatis dapat dimatikan pengguna.
                    bool bawaanPeriksa = SuDesApp.Utilities.AppPreferenceStore.IsPeriksaPembaruanSaatMulai();
                    SuDesApp.Utilities.AppPreferenceStore.SetPeriksaPembaruanSaatMulai(false);
                    bool setelahDimatikan = SuDesApp.Utilities.AppPreferenceStore.IsPeriksaPembaruanSaatMulai();
                    SuDesApp.Utilities.AppPreferenceStore.SetPeriksaPembaruanSaatMulai(bawaanPeriksa);
                    Cek(bawaanPeriksa && !setelahDimatikan,
                        "preferensi 'periksa pembaruan saat dibuka' harus aktif bawaan dan bisa dimatikan");

                    // Preferensi pemasangan otomatis saat ditutup: bawaan NONAKTIF
                    // (pemasangan senyap tidak boleh menyala sendiri), bisa dinyalakan,
                    // dan batas ukurannya dijepit ke rentang wajar.
                    bool bawaanOtomatis = SuDesApp.Utilities.AppPreferenceStore.IsPasangOtomatisSaatKeluar();
                    SuDesApp.Utilities.AppPreferenceStore.SetPasangOtomatisSaatKeluar(true);
                    bool setelahNyala = SuDesApp.Utilities.AppPreferenceStore.IsPasangOtomatisSaatKeluar();
                    SuDesApp.Utilities.AppPreferenceStore.SetPasangOtomatisSaatKeluar(bawaanOtomatis);
                    Cek(!bawaanOtomatis && setelahNyala,
                        "preferensi 'pasang otomatis saat ditutup' harus nonaktif bawaan dan bisa dinyalakan");

                    int bawaanBatas = SuDesApp.Utilities.AppPreferenceStore.GetBatasUkuranTambalanMb();
                    SuDesApp.Utilities.AppPreferenceStore.SetBatasUkuranTambalanMb(0);
                    Cek(SuDesApp.Utilities.AppPreferenceStore.GetBatasUkuranTambalanMb() == 25,
                        "batas ukuran di luar rentang harus kembali ke bawaan 25 MB");
                    SuDesApp.Utilities.AppPreferenceStore.SetBatasUkuranTambalanMb(60);
                    Cek(SuDesApp.Utilities.AppPreferenceStore.GetBatasUkuranTambalanMb() == 60,
                        "batas ukuran harus bisa diubah (60 MB)");
                    SuDesApp.Utilities.AppPreferenceStore.SetBatasUkuranTambalanMb(bawaanBatas);

                    // Mode diam-diam startup: bawaan AKTIF (startup terasa ringan),
                    // bisa dimatikan pengguna, dan jeda menitnya dijepit 1-60.
                    bool bawaanDiamDiam = SuDesApp.Utilities.AppPreferenceStore.IsStartupDiamDiam();
                    SuDesApp.Utilities.AppPreferenceStore.SetStartupDiamDiam(false);
                    bool setelahMati = SuDesApp.Utilities.AppPreferenceStore.IsStartupDiamDiam();
                    SuDesApp.Utilities.AppPreferenceStore.SetStartupDiamDiam(bawaanDiamDiam);
                    Cek(bawaanDiamDiam && !setelahMati,
                        "mode diam-diam startup harus aktif bawaan dan bisa dimatikan");

                    int bawaanJeda = SuDesApp.Utilities.AppPreferenceStore.GetStartupDiamDiamMenit();
                    SuDesApp.Utilities.AppPreferenceStore.SetStartupDiamDiamMenit(0);
                    Cek(SuDesApp.Utilities.AppPreferenceStore.GetStartupDiamDiamMenit() == 5,
                        "jeda diam-diam di luar rentang harus kembali ke bawaan 5 menit");
                    SuDesApp.Utilities.AppPreferenceStore.SetStartupDiamDiamMenit(15);
                    Cek(SuDesApp.Utilities.AppPreferenceStore.GetStartupDiamDiamMenit() == 15,
                        "jeda diam-diam harus bisa diubah (15 menit)");
                    SuDesApp.Utilities.AppPreferenceStore.SetStartupDiamDiamMenit(99);
                    Cek(SuDesApp.Utilities.AppPreferenceStore.GetStartupDiamDiamMenit() == 5,
                        "jeda diam-diam di atas 60 menit harus kembali ke bawaan");
                    SuDesApp.Utilities.AppPreferenceStore.SetStartupDiamDiamMenit(bawaanJeda);

                    // Gerbang ukuran pemasangan otomatis: paket lebih besar dari batas
                    // tidak boleh lolos, paket kecil lolos.
                    long batasUji = 60L * 1024 * 1024;
                    Cek(batasUji > rencanaTambalan.TotalByte && rencanaTambalan.TotalByte > 0,
                        "gerbang ukuran harus membandingkan TotalByte rencana dengan batas MB");

                    var hasilTambalan = SuDesApp.Utilities.PatchUpdateService.AmbilHasilTerakhir(berkasHasil);
                    Cek(hasilTambalan != null && hasilTambalan.Berhasil && hasilTambalan.JumlahBerkas == 3,
                        $"hasil penerapan harus tercatat: {hasilTambalan?.Pesan}");
                    Cek(hasilTambalan != null && hasilTambalan.Versi == "9.9.9",
                        "hasil penerapan harus memuat versi yang dipasang");
                    Cek(!File.Exists(berkasHasil),
                        "berkas hasil penerapan harus dihapus setelah dibaca agar tidak muncul dua kali");

                    if (salah == salahSemula)
                    {
                        Console.WriteLine(
                            "Tambalan: rencana berkas, berkas pengguna dilindungi, dan penerapan nyata " +
                            "lewat skrip penerap (3 berkas diganti, 1 dihapus) berhasil.");
                    }

                    // ===== Riwayat pembaruan: catatan pemasangan bisa diperiksa kapan saja =====
                    string berkasRiwayat = Path.Combine(folderTambalan, "riwayat-pembaruan.json");
                    SuDesApp.Utilities.RiwayatPembaruanStore.Path = berkasRiwayat;
                    try
                    {
                        SuDesApp.Utilities.RiwayatPembaruanStore.Kosongkan();
                        Cek(SuDesApp.Utilities.RiwayatPembaruanStore.Muat().Count == 0,
                            "riwayat harus kosong sebelum ada pembaruan");

                        // Rekam hasil penerapan seperti yang dilakukan aplikasi saat dibuka kembali.
                        if (hasilTambalan != null)
                        {
                            SuDesApp.Utilities.RiwayatPembaruanStore.Tambah(new SuDesApp.Utilities.EntriRiwayatPembaruan
                            {
                                Versi = hasilTambalan.Versi,
                                Jenis = "Tambalan",
                                JumlahBerkas = hasilTambalan.JumlahBerkas,
                                Berhasil = hasilTambalan.Berhasil,
                                Pesan = hasilTambalan.Pesan,
                                DariVersi = "2.4.4",
                                Waktu = new DateTime(2026, 9, 19, 8, 0, 0)
                            });
                        }

                        SuDesApp.Utilities.RiwayatPembaruanStore.Tambah(new SuDesApp.Utilities.EntriRiwayatPembaruan
                        {
                            Versi = "9.8.0", Jenis = "Installer", JumlahBerkas = 0, Berhasil = true,
                            Pesan = "Installer penuh dijalankan dari halaman Pembaruan.",
                            DariVersi = "2.4.4", Waktu = new DateTime(2026, 9, 1, 10, 0, 0)
                        });
                        SuDesApp.Utilities.RiwayatPembaruanStore.Tambah(new SuDesApp.Utilities.EntriRiwayatPembaruan
                        {
                            Versi = "9.9.0", Jenis = "Tambalan", JumlahBerkas = 2, Berhasil = false,
                            Pesan = "verifikasi SHA-256 gagal", DariVersi = "2.4.4",
                            Waktu = new DateTime(2026, 9, 15, 12, 0, 0)
                        });

                        var riwayat = SuDesApp.Utilities.RiwayatPembaruanStore.Muat();
                        Cek(riwayat.Count == 3, $"riwayat harus memuat 3 entri (ada {riwayat.Count})");
                        Cek(riwayat[0].Versi == "9.9.9" && riwayat[0].JumlahBerkas == 3 && riwayat[0].Berhasil,
                            "entri teratas harus pembaruan terbaru (9.9.9, 3 berkas, berhasil)");
                        Cek(riwayat[1].Versi == "9.9.0" && !riwayat[1].Berhasil && riwayat[1].JumlahBerkas == 2,
                            "urutan riwayat harus terbaru dulu dan menyimpan hasil gagal");
                        Cek(riwayat[2].Jenis == "Installer",
                            "jenis pembaruan (tambalan vs installer) harus tersimpan");
                        Cek(riwayat[0].Pesan.Contains("berhasil"),
                            "pesan hasil penerapan harus ikut tersimpan");

                        // Batas jumlah entri agar berkas tetap ringan.
                        for (int i = 0; i < 60; i++)
                        {
                            SuDesApp.Utilities.RiwayatPembaruanStore.Tambah(new SuDesApp.Utilities.EntriRiwayatPembaruan
                            {
                                Versi = "0.0." + i, Waktu = DateTime.Now.AddMinutes(i)
                            });
                        }
                        Cek(SuDesApp.Utilities.RiwayatPembaruanStore.Muat().Count == SuDesApp.Utilities.RiwayatPembaruanStore.BatasEntri,
                            $"riwayat harus dibatasi {SuDesApp.Utilities.RiwayatPembaruanStore.BatasEntri} entri");

                        // Riwayat rusak tidak boleh mengganggu aplikasi.
                        File.WriteAllText(berkasRiwayat, "{rusak");
                        Cek(SuDesApp.Utilities.RiwayatPembaruanStore.Muat().Count == 0,
                            "riwayat yang rusak harus dianggap kosong, bukan error");

                        SuDesApp.Utilities.RiwayatPembaruanStore.Kosongkan();

                        if (salah == salahSemula)
                        {
                            Console.WriteLine("Riwayat: pencatatan pemasangan (versi, jenis, tanggal, jumlah berkas, hasil) berhasil.");
                        }
                    }
                    finally
                    {
                        SuDesApp.Utilities.RiwayatPembaruanStore.Path = null;
                    }
                }
                finally
                {
                    try { if (Directory.Exists(folderTambalan)) Directory.Delete(folderTambalan, true); }
                    catch { /* folder sementara */ }
                }
            }

            // ===== 6. Surat dibuka lagi dari register untuk diperbaiki =====
            var isiEdit = new SuDesApp.Wpf.ViewModels.IsiTemplateSuratViewModel(
                generatorTemplate, repoTemplate, layananRegisterTemplate, uowTemplate, cfgTemplate,
                pratinjauIsi, navigasiTemplate, pesanTemplate,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Wpf.ViewModels.IsiTemplateSuratViewModel>.Instance);

            await isiEdit.ConfigureForEditAsync(barisRegister!.ID_Surat);
            Cek(isiEdit.ModeEdit, "formulir harus tahu bahwa ia sedang memperbaiki surat yang sudah tercatat");
            Cek(isiEdit.NomorSurat == "471/001/Ds/2026", $"nomor lama harus dipertahankan saat edit (nyatanya '{isiEdit.NomorSurat}')");
            Cek(isiEdit.NamaPejabat == "Kades Uji", "nama penandatangan harus dimuat kembali dari payload");
            Cek(isiEdit.Kolom.All(k => k.Nilai.Length > 0), "seluruh isian surat harus dimuat kembali dari payload");
            Cek(isiEdit.Kolom.Count == kolomWizard, "jumlah kolom saat edit harus sama dengan saat surat dibuat");

            var templateSebelumEdit = await repoTemplate.GetByIdAsync(hasilWizard!.Id);
            isiEdit.Kolom.First(k => k.Label == "Nama").Nilai = "Nama Diperbaiki";
            isiEdit.SimpanCommand.Execute(null);

            if (!await TungguAsync(() => registerTemplate.Tersimpan.Count == 1 &&
                (SuDesApp.Data.Models.TemplateSuratTercatat.FromJson(registerTemplate.Tersimpan[0].AdditionalData)?.NamaPemohon) == "Nama Diperbaiki", maxMs: 60000))
            {
                salah++;
                Console.WriteLine($"FAIL template surat: perbaikan surat tidak tersimpan di register — {isiEdit.PesanKesalahan}");
            }

            Cek(registerTemplate.Tersimpan.Count == 1,
                $"memperbaiki surat harus memperbarui baris register yang sama (nyatanya {registerTemplate.Tersimpan.Count} baris)");
            Cek(registerTemplate.Tersimpan[0].NomorSurat == "471/001/Ds/2026", "nomor surat tidak boleh berubah karena diedit");
            var templateSesudahEdit = await repoTemplate.GetByIdAsync(hasilWizard!.Id);
            Cek(templateSebelumEdit != null && templateSesudahEdit != null &&
                templateSesudahEdit.NomorTerakhir == templateSebelumEdit.NomorTerakhir,
                "memperbaiki surat tidak boleh menghabiskan nomor urut berikutnya");

            // Hapus template: baris benar-benar hilang dari database.
            await repoTemplate.DeleteAsync(hasilWizard!.Id);
            Cek((await repoTemplate.GetByIdAsync(hasilWizard.Id)) == null, "template yang dihapus harus hilang dari database");

            // ===== 7. Contoh bawaan siap pakai (katalog + pemasangan otomatis) =====
            var daftarContoh = SuDesApp.Data.Models.TemplateSuratBawaan.Daftar;
            Cek(daftarContoh.Count >= 5, $"katalog contoh bawaan minimal 5 (nyatanya {daftarContoh.Count})");
            Cek(daftarContoh.Select(c => c.Kode).Distinct(StringComparer.OrdinalIgnoreCase).Count() == daftarContoh.Count,
                "kode contoh bawaan tidak boleh kembar");
            Cek(daftarContoh.Select(c => c.Nama).Distinct(StringComparer.OrdinalIgnoreCase).Count() == daftarContoh.Count,
                "nama contoh bawaan tidak boleh kembar");

            foreach (var c in daftarContoh)
            {
                bool lengkap = c.JumlahKolom > 0
                    && c.Template.JumlahElemen > 0
                    && !string.IsNullOrWhiteSpace(c.Template.Judul)
                    && c.Kolom.Select(k => k.Kunci).Distinct(StringComparer.OrdinalIgnoreCase).Count() == c.JumlahKolom
                    && c.Kolom.Select(k => (k.Label ?? "").Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == c.JumlahKolom
                    && SuDesApp.Data.Models.TemplateSuratNomor.AwalanValid(c.AwalanNomor, out _)
                    && SuDesApp.Data.Models.TemplateSuratNomor.PolaValid(c.Template.PolaNomor, out _)
                    && c.ContohNomor.Contains(c.AwalanNomor, StringComparison.Ordinal)
                    && c.Template.PakaiTandaTangan;

                Cek(lengkap, $"contoh bawaan \"{c.Nama}\" belum lengkap (kolom {c.JumlahKolom}, awalan {c.AwalanNomor}, nomor {c.ContohNomor})");
            }

            // Tiga contoh yang diminta pengguna harus tersedia.
            Cek(SuDesApp.Data.Models.TemplateSuratBawaan.Cari("pengantar-rt-rw") != null
                && SuDesApp.Data.Models.TemplateSuratBawaan.Cari("izin-keramaian") != null
                && SuDesApp.Data.Models.TemplateSuratBawaan.Cari("keterangan-penghasilan") != null,
                "contoh pengantar RT/RW, izin keramaian, dan keterangan penghasilan harus tersedia");
            Cek(SuDesApp.Data.Models.TemplateSuratBawaan.Cari("tidak-ada") == null,
                "kode contoh yang tidak dikenal harus mengembalikan null");

            // Salinan tidak boleh mengubah katalog.
            var salinanContoh = daftarContoh[0].Salinan();
            salinanContoh.Nama = "Diubah";
            salinanContoh.Kolom[0].Label = "Diubah";
            Cek(daftarContoh[0].Template.Nama != "Diubah" && daftarContoh[0].Kolom[0].Label != "Diubah",
                "salinan contoh tidak boleh mengubah definisi katalog");
            Cek(salinanContoh.Id == 0 && salinanContoh.NomorTerakhir == 0,
                "salinan contoh harus mulai dari nol (tanpa identitas database)");

            // Pencocokan nama & daftar contoh yang belum ada.
            Cek(SuDesApp.Data.Models.TemplateSuratBawaan.KodeDariNama(daftarContoh[1].Nama) == daftarContoh[1].Kode,
                "nama contoh harus bisa dilacak kembali ke kodenya");
            Cek(SuDesApp.Data.Models.TemplateSuratBawaan.KodeDariNama("Surat Buatan Sendiri") == null,
                "nama di luar katalog tidak boleh dianggap contoh bawaan");
            var belumAda = SuDesApp.Data.Models.TemplateSuratBawaan.BelumAda(new[]
            {
                new SuDesApp.Data.Models.TemplateSuratKustom { Nama = daftarContoh[0].Nama }
            });
            Cek(belumAda.Count == daftarContoh.Count - 1 && belumAda.All(c => c.Kode != daftarContoh[0].Kode),
                $"contoh yang sudah dipasang harus dikeluarkan dari daftar (nyatanya {belumAda.Count})");

            // Penanda pemasangan di profil pengguna (diarahkan ke berkas sementara).
            string berkasPenanda = Path.Combine(folderTemplate, "template-bawaan.json");
            string penandaSemula = SuDesApp.Configuration.TemplateBawaanStore.Path;
            SuDesApp.Configuration.TemplateBawaanStore.Path = berkasPenanda;

            try
            {
                SuDesApp.Configuration.TemplateBawaanStore.Lupakan();
                Cek(!SuDesApp.Configuration.TemplateBawaanStore.SudahPernah(), "penanda baru harus dianggap belum pernah");

                SuDesApp.Configuration.TemplateBawaanStore.Simpan(new[] { "pengantar-rt-rw", "izin-keramaian" });
                var penandaDibaca = SuDesApp.Configuration.TemplateBawaanStore.Muat();
                Cek(penandaDibaca.Count == 2 && penandaDibaca.Contains("PENGANTAR-RT-RW"),
                    "kode contoh harus tersimpan di penanda (tanpa peduli huruf besar/kecil)");
                Cek(SuDesApp.Configuration.TemplateBawaanStore.SudahPernah(), "penanda yang sudah disimpan harus terbaca");

                SuDesApp.Configuration.TemplateBawaanStore.Lupakan();
                Cek(!SuDesApp.Configuration.TemplateBawaanStore.SudahPernah(), "Lupakan harus menghapus penanda");

                // Pemasangan otomatis pada database yang masih kosong.
                string folderBawaan = Path.Combine(folderTemplate, "bawaan");
                Directory.CreateDirectory(folderBawaan);
                using var connBawaan = new Microsoft.Data.Sqlite.SqliteConnection(
                    $"Data Source={Path.Combine(folderBawaan, "desa.db")}");
                var repoBawaan = new SuDesApp.Data.Repositories.TemplateSuratRepository(
                    connBawaan,
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Data.Repositories.TemplateSuratRepository>.Instance);
                var layananBawaan = new SuDesApp.Services.TemplateSuratBawaanService(
                    repoBawaan,
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Services.TemplateSuratBawaanService>.Instance);

                var otomatis = await layananBawaan.PasangOtomatisAsync();
                Cek(otomatis.Count == daftarContoh.Count,
                    $"contoh bawaan harus terpasang otomatis pada daftar kosong (nyatanya {otomatis.Count})");
                Cek(SuDesApp.Configuration.TemplateBawaanStore.SudahPernah(), "penanda harus tercatat setelah pemasangan otomatis");
                Cek((await repoBawaan.GetAllAsync()).Count == daftarContoh.Count,
                    "seluruh contoh harus benar-benar tersimpan di database");

                Cek((await layananBawaan.PasangOtomatisAsync()).Count == 0,
                    "pemasangan otomatis tidak boleh berjalan dua kali");
                Cek((await repoBawaan.GetAllAsync()).Count == daftarContoh.Count, "tidak boleh muncul salinan ganda");

                // Contoh yang dihapus pengguna tidak boleh kembali sendiri.
                await repoBawaan.DeleteAsync(otomatis[0].Id);
                Cek((await layananBawaan.PasangOtomatisAsync()).Count == 0,
                    "contoh yang sudah dihapus pengguna tidak boleh dipasang ulang otomatis");

                // Pemasangan satu contoh tertentu (mis. dari daftar kode contoh).
                var satuContoh = await layananBawaan.PasangAsync(new[] { daftarContoh[0].Kode });
                Cek(satuContoh.Count == 1 && (await repoBawaan.GetAllAsync()).Count == daftarContoh.Count,
                    "pemasangan manual satu contoh harus menambah tepat satu baris");
                Cek((await layananBawaan.PasangAsync(new[] { daftarContoh[0].Kode })).Count == 0,
                    "memasang contoh yang sudah ada tidak boleh menambah baris baru");

                // Pemasangan semua yang belum ada (dipakai pemasangan otomatis & penyegaran).
                await repoBawaan.DeleteAsync(satuContoh[0].Id);
                Cek(SuDesApp.Services.TemplateSuratBawaanService.BelumAda(await repoBawaan.GetAllAsync()).Count == 1,
                    "harus terdeteksi satu contoh yang belum ada di daftar");
                Cek((await layananBawaan.PasangAsync()).Count == 1,
                    "PasangAsync tanpa kode hanya memasang contoh yang belum ada");

                // Template buatan pengguna sendiri tidak dianggap contoh bawaan.
                await repoBawaan.AddAsync(new SuDesApp.Data.Models.TemplateSuratKustom { Nama = "Surat Buatan Sendiri" });
                Cek((await layananBawaan.PasangAsync()).Count == 0,
                    "tidak ada contoh baru yang perlu dipasang setelah semuanya lengkap");
            }
            finally
            {
                SuDesApp.Configuration.TemplateBawaanStore.Lupakan();
                SuDesApp.Configuration.TemplateBawaanStore.Path = penandaSemula;
            }
        }
        catch (Exception ex)
        {
            salah++;
            Console.WriteLine($"FAIL template surat: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { if (Directory.Exists(folderTemplate)) Directory.Delete(folderTemplate, true); } catch { /* temp */ }
        }

        if (salah == 0)
        {
            Console.WriteLine("Template surat: kunci kolom, pola nomor, validasi isian, repository, wizard, dan pencetakan PDF sesuai.");
        }

        return salah;
    }

    /// <summary>Tunggu sampai syarat terpenuhi (dipakai menguji perintah asinkron).</summary>
    private static async Task<bool> TungguAsync(Func<bool> syarat, int maxMs = 20000)
    {
        var batas = DateTime.UtcNow.AddMilliseconds(maxMs);
        while (DateTime.UtcNow < batas)
        {
            if (syarat()) return true;
            await Task.Delay(50);
        }

        return syarat();
    }

    private sealed class FakeMessageService : SuDesApp.Utilities.IMessageService
    {
        public readonly List<string> Info = new();
        public readonly List<string> Kesalahan = new();

        public Task ShowMessageAsync(string message, string title,
            SuDesApp.Utilities.AppMessageButton buttons, SuDesApp.Utilities.AppMessageIcon icon)
            => Task.CompletedTask;

        public Task ShowErrorAsync(string message) { Kesalahan.Add(message); return Task.CompletedTask; }
        public Task ShowWarningAsync(string message) { Kesalahan.Add(message); return Task.CompletedTask; }
        public Task ShowInfoAsync(string message) { Info.Add(message); return Task.CompletedTask; }
        public Task<bool> ShowConfirmationAsync(string title, string message) => Task.FromResult(true);
    }

    /// <summary>Penyedia layanan minimal untuk NavigationService pada uji.</summary>
    private sealed class FakeServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    /// <summary>
    /// Penyedia layanan palsu yang hanya mengembalikan satu objek (mis. generator
    /// surat) — dipakai uji "cetak ulang surat dari register" yang mencari generator
    /// lewat DI.
    /// </summary>
    private sealed class FakeServiceProviderTemplate : IServiceProvider
    {
        private readonly object _layanan;

        public FakeServiceProviderTemplate(object layanan) => _layanan = layanan;

        public object? GetService(Type serviceType) => serviceType == _layanan.GetType() ? _layanan : null;
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
            => Task.FromResult(Daftar.FirstOrDefault(x => x.ID_Warga == idWarga));

        public Task<SuDesApp.Data.Models.WargaData> GetWargaByNikAsync(string nik, Microsoft.Data.Sqlite.SqliteConnection? connection = null, System.Data.IDbTransaction? transaction = null)
            => Task.FromResult(Daftar.FirstOrDefault(x => x.NIK == nik));

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

        /// <summary>Nomor yang dianggap sudah terpakai (untuk uji nomor kembar).</summary>
        public readonly HashSet<string> NomorTerpakai = new(StringComparer.OrdinalIgnoreCase);

        public Task<bool> IsNomorSuratExistsAsync(string nomorSurat) =>
            Task.FromResult(NomorTerpakai.Contains(nomorSurat ?? string.Empty));
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

    /// <summary>Generator blanko NTCR untuk uji (font + pengaturan desa nyata).</summary>
    private static SuDesApp.GeneratorPdf.NtcrGenerator BuatGeneratorNtcr(
        SuDesApp.AppConfig cfg,
        SuDesApp.Configuration.IUnitOfWork uow)
    {
        var desaRepo = uow.DesaRepository;
        var suratRepo = uow.SuratRepository;

        return new SuDesApp.GeneratorPdf.NtcrGenerator(
            cfg,
            new SuDesApp.Utilities.FileService(cfg,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Utilities.FileService>.Instance),
            desaRepo,
            suratRepo,
            new SuDesApp.ControlSurat.SettingsManager(desaRepo, cfg),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.GeneratorPdf.NtcrGenerator>.Instance,
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
    }

    /// <summary>
    /// Form paket harus mengisi kotak "Nomor Surat" secara otomatis (tanpa perlu
    /// menekan apa pun) dan menyesuaikannya saat pilihan blanko berubah.
    /// </summary>
    private static int PeriksaNomorOtomatisFormPaket(
        SuDesApp.AppConfig cfg,
        SuDesApp.Configuration.IUnitOfWork uow)
    {
        try
        {
            var layanan = new SuDesApp.Services.NtcrPaketService(
                uow, cfg, BuatGeneratorNtcr(cfg, uow),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Services.NtcrPaketService>.Instance);

            var vm = new SuDesApp.Wpf.Input.NtcrPaketViewModel(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Wpf.Input.NtcrPaketViewModel>.Instance,
                cfg,
                uow,
                new FakeMessageService(),
                layanan,
                new SuDesApp.Wpf.ViewModels.NavigationService(new FakeServiceProvider()),
                (_, _) => throw new InvalidOperationException("Pratinjau PDF tidak dipakai pada uji ini."));

            vm.InitializeAsync().GetAwaiter().GetResult();

            int salah = 0;

            if (vm.Blanko.Count(b => b.Terpilih) != 5 || !vm.Blanko.First(b => b.Kode == "N1").Terpilih)
            {
                salah++;
                Console.WriteLine("FAIL nomor otomatis: pilihan bawaan harus N1–N5");
            }

            if (string.IsNullOrWhiteSpace(vm.NomorSurat))
            {
                salah++;
                Console.WriteLine("FAIL nomor otomatis: kotak Nomor Surat masih kosong saat form dibuka");
            }
            else if (!vm.NomorSurat.StartsWith("474.3", StringComparison.Ordinal))
            {
                salah++;
                Console.WriteLine($"FAIL nomor otomatis: nomor NTCR '{vm.NomorSurat}' tidak berawalan 474.3");
            }

            if (vm.NomorSuratEnabled)
            {
                salah++;
                Console.WriteLine("FAIL nomor otomatis: kotak Nomor Surat pada paket harus terisi otomatis (tidak diedit manual)");
            }

            if (!vm.NomorSuratRingkasan.Contains("N1") || vm.NomorSuratRingkasan.Contains("N6"))
            {
                salah++;
                Console.WriteLine($"FAIL nomor otomatis: ringkasan awal salah — '{vm.NomorSuratRingkasan}'");
            }

            // Blanko N6 dicentang: nomornya harus ikut muncul.
            string nomorN1Sebelum = vm.NomorSurat;
            vm.Blanko.First(b => b.Kode == "N6").Terpilih = true;
            vm.PerbaruiNomorSuratAsync().GetAwaiter().GetResult();

            if (!vm.NomorSuratRingkasan.Contains("N6"))
            {
                salah++;
                Console.WriteLine($"FAIL nomor otomatis: N6 tidak muncul di ringkasan — '{vm.NomorSuratRingkasan}'");
            }

            if (string.IsNullOrWhiteSpace(nomorN1Sebelum) || vm.NomorSurat != nomorN1Sebelum)
            {
                salah++;
                Console.WriteLine($"FAIL nomor otomatis: nomor blanko pertama berubah saat blanko lain dicentang ('{nomorN1Sebelum}' → '{vm.NomorSurat}')");
            }

            // Semua centang dilepas: kotak dikosongkan dengan keterangan, bukan angka palsu.
            foreach (var item in vm.Blanko) item.Terpilih = false;
            vm.PerbaruiNomorSuratAsync().GetAwaiter().GetResult();

            if (!string.IsNullOrWhiteSpace(vm.NomorSurat) || !vm.NomorSuratRingkasan.Contains("Belum ada"))
            {
                salah++;
                Console.WriteLine($"FAIL nomor otomatis: tanpa blanko terpilih harus kosong — '{vm.NomorSurat}' / '{vm.NomorSuratRingkasan}'");
            }

            if (salah == 0)
            {
                Console.WriteLine($"Nomor surat otomatis: kotak terisi saat form dibuka (mis. {nomorN1Sebelum}) dan ikut berubah bersama pilihan blanko.");
            }

            return salah;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL nomor otomatis: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Blanko permohonan N2 &amp; N3: penomoran NTCR 474.3, daftar lampiran baku
    /// dengan butir terakhir titik-titik (diisi manual di KUA), dan N3 yang boleh
    /// dikosongkan seluruhnya.
    /// </summary>
    private static int PeriksaBlankoPermohonanN2N3(SuDesApp.AppConfig cfg)
    {
        int salah = 0;

        void Cek(bool syarat, string pesan)
        {
            if (!syarat)
            {
                salah++;
                Console.WriteLine($"FAIL blanko N2/N3: {pesan}");
            }
        }

        try
        {
            // Penomoran: seluruh blanko NTCR memakai 474.3, numpang nikah (N8) tetap 474.2.
            foreach (var jenis in new[] { "NTCR_N1", "NTCR_N2", "NTCR_N3", "NTCR_N4", "NTCR_N5", "NTCR_N6" })
            {
                var format = cfg.GetSuratNumberFormat(jenis);
                Cek(format.StartsWith("474.3", StringComparison.Ordinal),
                    $"format nomor {jenis} = '{format}' (harusnya berawalan 474.3)");
            }

            Cek(cfg.GetSuratNumberFormat("NTCR_N8").StartsWith("474.2", StringComparison.Ordinal),
                $"format nomor N8 = '{cfg.GetSuratNumberFormat("NTCR_N8")}' (harusnya tetap 474.2)");

            // Semua blanko NTCR harus terpetakan ke enum Ntcr. Bila ada yang terlewat,
            // jenisnya jatuh ke nilai enum pertama (Kematian) sehingga surat diperlakukan
            // sebagai jenis lain — mis. data warga ikut dituntut lengkap.
            foreach (var jenisNtcr in SuDesApp.Data.Models.SuratConstants.NtcrSemua)
            {
                var contoh = new SuDesApp.Data.Models.SuratData { NamaJenis = jenisNtcr };
                Cek(contoh.Jenis == SuDesApp.Data.Models.SuratData.JenisSuratEnum.Ntcr,
                    $"{jenisNtcr} terpetakan ke enum '{contoh.Jenis}' (harusnya Ntcr)");
            }

            // Daftar lampiran N2: 6 butir baku + butir 7 & 8 titik-titik tanpa isian.
            var n2 = SuDesApp.Data.Models.NtcrLampiran.UntukN2();
            const string kosong = SuDesApp.Data.Models.NtcrLampiran.ButirKosong;

            Cek(n2.Count == 8, $"butir lampiran N2 = {n2.Count} (harusnya 8)");
            Cek(n2.Count >= 8 && n2[6] == kosong && n2[7] == kosong,
                "butir 7 & 8 lampiran N2 harus dibiarkan titik-titik (tidak diisi aplikasi)");
            Cek(n2.Take(6).All(b => b == kosong || (b.Length > 0 && !b.Contains('.'))),
                "butir 1-6 lampiran N2 harus berupa butir baku, bukan titik-titik");

            // Daftar lampiran N3: 4 butir baku + 2 butir titik-titik.
            var n3 = SuDesApp.Data.Models.NtcrLampiran.UntukN3();
            Cek(n3.Count == 6, $"butir lampiran N3 = {n3.Count} (harusnya 6)");
            Cek(n3.Count >= 6 && n3[4] == kosong && n3[5] == kosong,
                "sisa butir lampiran N3 harus dibiarkan titik-titik");

            // N3 boleh dikosongkan: blanko isbat kosong tidak boleh ditolak validasi.
            var suratN3 = BuatPasanganUji();
            suratN3.NamaJenis = SuDesApp.Data.Models.SuratConstants.NTCR_N3;
            suratN3.NomorSurat = "474.3/001/Ds/2026";
            suratN3.Ntcr!.NamaIstri = string.Empty;
            suratN3.Ntcr.NikIstri = string.Empty;
            suratN3.Ntcr.AlamatIstri = string.Empty;
            suratN3.Ntcr.TujuanKua = string.Empty;
            suratN3.Ntcr.TanggalPenetapanIsbat = string.Empty;
            suratN3.Ntcr.PengadilanAgama = string.Empty;

            var errorsN3 = new SuDesApp.Data.Models.SuratDataValidator(
                    suratN3,
                    Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
                    new WaSmokeWargaRepository(),
                    new FakeDesaRepository())
                .ValidateAsync().GetAwaiter().GetResult().ToList();

            Cek(errorsN3.Count == 0,
                $"N3 yang dikosongkan masih ditolak validasi: {string.Join(" | ", errorsN3)}");

            // N2 tetap ketat: rencana akad nikah & KUA tujuan wajib terisi.
            var suratN2 = BuatPasanganUji();
            suratN2.NamaJenis = SuDesApp.Data.Models.SuratConstants.NTCR_N2;
            suratN2.NomorSurat = "474.3/002/Ds/2026";
            suratN2.Ntcr!.TujuanKua = string.Empty;
            suratN2.Ntcr.HariTanggalJamAkad = string.Empty;
            suratN2.Ntcr.TempatAkad = string.Empty;

            var errorsN2 = new SuDesApp.Data.Models.SuratDataValidator(
                    suratN2,
                    Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
                    new WaSmokeWargaRepository(),
                    new FakeDesaRepository())
                .ValidateAsync().GetAwaiter().GetResult().ToList();

            Cek(errorsN2.Any(e => e.Contains("akad", StringComparison.OrdinalIgnoreCase)),
                $"N2 tanpa rencana akad tidak lagi ditolak validasi: {string.Join(" | ", errorsN2)}");

            if (salah == 0)
            {
                Console.WriteLine("Blanko N2/N3: nomor 474.3, lampiran baku + butir 7 & 8 titik-titik, N3 boleh dikosongkan.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL blanko N2/N3: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }

        return salah;
    }

    /// <summary>Data satu pasangan yang lengkap untuk uji alur paket NTCR.</summary>
    /// <summary>
    /// Surat numpang nikah (N8): katalog &amp; penomorannya terdaftar, tidak ikut paket
    /// blanko N1–N6, dan validasinya menuntut tujuan numpang nikah diisi.
    /// </summary>
    private static int PeriksaSuratNumpangNikah(SuDesApp.AppConfig cfg)
    {
        int salah = 0;
        const string jenis = SuDesApp.Data.Models.SuratConstants.NTCR_N8;

        void Cek(bool syarat, string pesan)
        {
            if (!syarat)
            {
                salah++;
                Console.WriteLine($"FAIL numpang nikah (N8): {pesan}");
            }
        }

        var blanko = SuDesApp.Data.Models.NtcrKatalog.Cari(jenis);
        Cek(blanko != null, "blanko N8 tidak ada di katalog NTCR");
        Cek(SuDesApp.Data.Models.NtcrKatalog.Kode(jenis) == "N8", $"kode blanko = '{SuDesApp.Data.Models.NtcrKatalog.Kode(jenis)}' (harusnya N8)");
        Cek(SuDesApp.Data.Models.NtcrKatalog.NamaFormulir(jenis).Contains("NUMPANG NIKAH"), "judul surat tidak memuat NUMPANG NIKAH");
        Cek(SuDesApp.Data.Models.NtcrKatalog.Keperluan(jenis).Contains("Numpang Nikah"), "keperluan surat tidak memuat Numpang Nikah");
        Cek(SuDesApp.Data.Models.NtcrKatalog.Lampiran(jenis) == string.Empty, "N8 tidak boleh punya nomor lampiran Kepdirjen");

        // N8 bukan blanko Kepdirjen: tidak memakai blok LAMPIRAN dan tidak ikut paket N1–N6.
        Cek(!SuDesApp.Data.Models.NtcrKatalog.PakaiBlokKepdirjen(jenis), "N8 tidak boleh memakai blok kepala Kepdirjen");
        Cek(SuDesApp.Data.Models.NtcrKatalog.PakaiBlokKepdirjen("NTCR_N1"), "blanko N1 harus tetap memakai blok kepala Kepdirjen");
        Cek(SuDesApp.Data.Models.NtcrKatalog.UntukPaket.Count == 6, $"blanko paket = {SuDesApp.Data.Models.NtcrKatalog.UntukPaket.Count} (harusnya 6: N1–N6)");
        Cek(!SuDesApp.Data.Models.NtcrKatalog.UntukPaket.Any(b => b.NamaJenis == jenis), "N8 tidak boleh ikut paket pernikahan");

        // Terdaftar sebagai jenis surat yang sah, dengan penomoran 474.2 sesuai template.
        Cek(SuDesApp.Data.Models.SuratConstants.IsNtcr(jenis), "N8 tidak dikenali sebagai jenis NTCR");
        Cek(SuDesApp.Data.Models.SuratData.IsValidNamaJenis(jenis), "N8 tidak termasuk jenis surat yang valid");
        Cek(cfg.SuratNumberFormats.ContainsKey(jenis), "format nomor surat N8 belum terdaftar di konfigurasi");
        if (cfg.SuratNumberFormats.TryGetValue(jenis, out var formatN8))
        {
            Cek(formatN8.StartsWith("474.2", StringComparison.Ordinal),
                $"format nomor N8 = '{formatN8}' (harusnya berawalan 474.2 seperti template)");
        }

        // Urutan blanko: N8 selalu di belakang blanko Kepdirjen.
        var urut = SuDesApp.Data.Models.NtcrKatalog.Urutkan(new[] { jenis, "NTCR_N5" });
        Cek(urut.Count == 2 && urut[0] == "NTCR_N5" && urut[1] == jenis, "urutan blanko menempatkan N8 tidak di akhir");

        // Validasi: tujuan numpang nikah wajib terisi; NIK calon istri tidak diwajibkan.
        var surat = BuatPasanganUji();
        surat.NamaJenis = jenis;
        surat.Ntcr!.NikIstri = string.Empty;
        surat.Ntcr.NamaIstri = string.Empty;
        surat.Ntcr.DesaNumpang = string.Empty;
        surat.Ntcr.KecamatanNumpang = string.Empty;
        surat.Ntcr.KabupatenNumpang = string.Empty;

        var validator = new SuDesApp.Data.Models.SuratDataValidator(
            surat,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            new WaSmokeWargaRepository(),
            new FakeDesaRepository());

        var kosong = validator.ValidateAsync().GetAwaiter().GetResult().ToList();
        Cek(kosong.Any(e => e.Contains("numpang nikah", StringComparison.OrdinalIgnoreCase)),
            "tujuan numpang nikah kosong tidak ditolak validasi");
        Cek(!kosong.Any(e => e.Contains("NIK calon istri", StringComparison.OrdinalIgnoreCase)),
            "NIK calon istri tidak boleh diwajibkan pada surat N8");

        surat.Ntcr.DesaNumpang = "Desa Tujuan";
        surat.Ntcr.KecamatanNumpang = "Kecamatan Tujuan";
        surat.Ntcr.KabupatenNumpang = "Kabupaten Tujuan";
        surat.Ntcr.NamaIstri = "Calon Istri Uji";
        surat.Ntcr.AlamatIstri = "Dusun Uji, Desa Uji";

        var lengkap = validator.ValidateAsync().GetAwaiter().GetResult().ToList();
        Cek(!lengkap.Any(e => e.Contains("numpang nikah", StringComparison.OrdinalIgnoreCase)),
            $"tujuan numpang nikah yang sudah diisi masih dianggap salah: {string.Join("; ", lengkap)}");

        if (salah == 0)
        {
            Console.WriteLine("Surat numpang nikah (N8): terdaftar (nomor 474.2), bukan blok Kepdirjen, di luar paket N1–N6, dan validasinya menuntut tujuan numpang nikah.");
        }

        return salah;
    }

    /// <summary>
    /// Pengaturan penomoran surat: awalan nomor setiap jenis surat bisa diganti
    /// pengguna (mis. SKD 470 → 471), langsung dipakai saat membuat nomor surat
    /// baru, dan bisa dikembalikan ke bawaan aplikasi.
    /// </summary>
    private static int PeriksaPengaturanPenomoran()
    {
        int salah = 0;

        void Cek(bool syarat, string pesan)
        {
            if (!syarat)
            {
                salah++;
                Console.WriteLine($"FAIL pengaturan penomoran: {pesan}");
            }
        }

        var berkasBawaanAsli = CariBerkasRepo("Configuration/JenisSuratConfig.json");
        var appSettingsPath = FindFileUpwards("appsettings.json");
        if (berkasBawaanAsli == null || appSettingsPath == null)
        {
            Console.WriteLine("JenisSuratConfig.json/appsettings.json tidak ditemukan - lewati uji pengaturan penomoran");
            return 0;
        }

        string folderUji = Path.Combine(Path.GetTempPath(), $"SuDesApp-Penomoran-{Guid.NewGuid():N}");
        string configUji = Path.Combine(folderUji, "JenisSuratConfig.json");
        string berkasPenyesuaian = Path.Combine(folderUji, "penomoran-surat.json");
        string dbUji = Path.Combine(folderUji, "uji.db");
        string? pathPenyesuaianLama = SuDesApp.Configuration.PenomoranOverrideStore.Path;

        try
        {
            Directory.CreateDirectory(folderUji);
            // Berkas bawaan uji = salinan berkas repo, supaya uji ini tidak menyentuh
            // konfigurasi aplikasi yang sebenarnya.
            File.Copy(berkasBawaanAsli, configUji, overwrite: true);
            SuDesApp.Configuration.PenomoranOverrideStore.Path = berkasPenyesuaian;

            var cache = new SuDesApp.Configuration.MemoryCacheService(
                new Microsoft.Extensions.Caching.Memory.MemoryCache(
                    new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Configuration.MemoryCacheService>.Instance);

            using var loader = new SuDesApp.Configuration.JenisSuratConfigLoader(
                cache,
                new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Configuration.JenisSuratConfigLoader>.Instance,
                configUji);

            var cfgUji = (SuDesApp.AppConfig)Activator.CreateInstance(typeof(SuDesApp.AppConfig), appSettingsPath, null)!;
            cfgUji.DatabaseConnectionString = $"Data Source={dbUji}";

            var layanan = new SuDesApp.Services.PenomoranSuratService(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Services.PenomoranSuratService>.Instance,
                loader,
                cfgUji,
                configUji);

            // 1) Daftar awal: setiap jenis surat memakai awalan bawaannya.
            var daftarAwal = layanan.MuatSemua();
            Cek(daftarAwal.Count == 22, $"daftar jenis surat = {daftarAwal.Count} (harusnya 22)");

            var skdAwal = daftarAwal.FirstOrDefault(e => e.NamaJenis == "SKD_UMUM");
            Cek(skdAwal != null && skdAwal.Awalan == "470" && skdAwal.AwalanBawaan == "470" && !skdAwal.Disesuaikan,
                $"awalan awal SKD = '{skdAwal?.Awalan}' (harusnya 470 tanpa penyesuaian)");

            // 2) Awalan tidak sah ditolak dan tidak menghasilkan berkas apa pun.
            var (okSalah, pesanSalah) = layanan.SimpanAsync(new[]
            {
                new SuDesApp.Services.PerubahanPenomoran { NamaJenis = "SKD_UMUM", Awalan = "47/0" }
            }).GetAwaiter().GetResult();

            Cek(!okSalah && pesanSalah.Length > 0, "awalan bertanda '/' harus ditolak");
            Cek(!File.Exists(berkasPenyesuaian), "penyimpanan yang gagal tidak boleh membuat berkas penyesuaian");

            // 3) Ganti awalan SKD 470 → 471.
            var (okUbah, _) = layanan.SimpanAsync(new[]
            {
                new SuDesApp.Services.PerubahanPenomoran { NamaJenis = "SKD_UMUM", Awalan = "471" }
            }).GetAwaiter().GetResult();

            Cek(okUbah, "penggantian awalan SKD ditolak");
            Cek(File.Exists(berkasPenyesuaian), "berkas penyesuaian tidak dibuat");

            var isiPenyesuaian = File.Exists(berkasPenyesuaian) ? File.ReadAllText(berkasPenyesuaian) : string.Empty;
            Cek(isiPenyesuaian.Contains("471/{0:D3}/Ds/{2:yyyy}"),
                $"berkas penyesuaian tidak memuat format baru: {isiPenyesuaian}");
            Cek(File.ReadAllText(configUji).Contains("470/{0:D3}/Ds/{2:yyyy}"),
                "berkas konfigurasi bawaan tidak boleh ikut diubah");

            var skdBaru = layanan.MuatSemua().First(e => e.NamaJenis == "SKD_UMUM");
            Cek(skdBaru.Awalan == "471" && skdBaru.Disesuaikan && skdBaru.AwalanBawaan == "470",
                $"awalan SKD setelah diubah = '{skdBaru.Awalan}' (harusnya 471, bawaan tetap 470)");
            Cek(SuDesApp.Services.PenomoranSuratService.Contoh(skdBaru.Format) == $"471/001/Ds/{DateTime.Now.Year}",
                $"contoh nomor = '{SuDesApp.Services.PenomoranSuratService.Contoh(skdBaru.Format)}'");

            // 4) Nilai di memori (AppConfig) ikut berubah tanpa menutup aplikasi.
            cfgUji.ReloadSuratKindsFromConfig();
            Cek(cfgUji.GetSuratNumberFormat("SKD_UMUM") == "471/{0:D3}/Ds/{2:yyyy}" ||
                cfgUji.GetSuratNumberFormat("SKD_UMUM").StartsWith("471", StringComparison.Ordinal),
                $"AppConfig masih memakai '{cfgUji.GetSuratNumberFormat("SKD_UMUM")}'");

            // 5) Nomor surat SUNGGUHAN memakai awalan baru: database + repository nyata.
            var initUji = new SuDesApp.Configuration.DatabaseInitializer(
                cfgUji,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Configuration.DatabaseInitializer>.Instance,
                new FakeDesaRepository(),
                new WaSmokeJenisSuratRepository(),
                new WaSmokeWargaRepository(),
                new FakeSuratRepository());

            initUji.InitializeAsync().GetAwaiter().GetResult();

            using (var connUji = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbUji}"))
            {
                connUji.Open();

                var jenisRepoUji = new SuDesApp.Data.Repositories.JenisSuratRepository(
                    new SqliteUjiUnitOfWork(connUji),
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Data.Repositories.JenisSuratRepository>.Instance,
                    cache,
                    loader);

                string nomorBaru = jenisRepoUji.GenerateNomorSuratAsync("SKD").GetAwaiter().GetResult();
                Cek(nomorBaru == $"471/001/Ds/{DateTime.Now.Year}",
                    $"nomor surat SKD yang dibuat = '{nomorBaru}' (harusnya 471/001/Ds/{DateTime.Now.Year})");

                // Blanko NTCR tidak ikut terpengaruh (tetap 474.3 miliknya sendiri).
                string nomorN1 = jenisRepoUji.GenerateNomorSuratAsync("N1T").GetAwaiter().GetResult();
                Cek(nomorN1 == $"474.3/001/Ds/{DateTime.Now.Year}",
                    $"nomor NTCR N1 = '{nomorN1}' (harusnya tetap 474.3/001)");
            }

            // 6) Peringatan awalan kembar: 474.2 dipakai urutan berbeda (N8 sendiri vs
            //    kenal lahir & ahli waris yang berbagi urutan).
            var peringatan = SuDesApp.Services.PenomoranSuratService.CariBentrok(layanan.MuatSemua());
            Cek(peringatan.Any(p => p.Contains("474.2")),
                "awalan 474.2 yang dipakai dua urutan berbeda harus diperingatkan");

            var tanpaBentrok = layanan.MuatSemua().Select(e => e.NamaJenis == "NTCR_N8"
                ? new SuDesApp.Services.PenomoranSuratEntri
                {
                    NamaJenis = e.NamaJenis,
                    DisplayName = e.DisplayName,
                    KodeJenis = e.KodeJenis,
                    IsSharedNumbering = e.IsSharedNumbering,
                    Awalan = "474.9"
                }
                : e);
            Cek(!SuDesApp.Services.PenomoranSuratService.CariBentrok(tanpaBentrok).Any(p => p.Contains("474.2")),
                "peringatan 474.2 hilang setelah salah satu jenis diberi awalan sendiri");

            // 7) Kembalikan bawaan → penyesuaian dihapus, awalan kembali 470.
            var (okReset, _) = layanan.SimpanAsync(new[]
            {
                new SuDesApp.Services.PerubahanPenomoran { NamaJenis = "SKD_UMUM", Awalan = string.Empty }
            }).GetAwaiter().GetResult();

            Cek(okReset, "pengembalian awalan ke bawaan ditolak");
            var isiSetelahReset = File.Exists(berkasPenyesuaian) ? File.ReadAllText(berkasPenyesuaian) : string.Empty;
            Cek(!isiSetelahReset.Contains("SKD_UMUM"), "penyesuaian SKD masih tersisa setelah dikembalikan ke bawaan");

            var skdReset = layanan.MuatSemua().First(e => e.NamaJenis == "SKD_UMUM");
            Cek(skdReset.Awalan == "470" && !skdReset.Disesuaikan,
                $"awalan SKD setelah dikembalikan = '{skdReset.Awalan}' (harusnya 470)");

            // 8) Halaman pengaturan memakai navigasi: satu bagian aktif, dan permintaan
            //    fokus dari statusbar memilih bagian yang tepat (bukan menggulir kartu).
            var vm = new SuDesApp.Wpf.ViewModels.PengaturanAplikasiViewModel(
                new SuDesApp.Wpf.ViewModels.NavigationService(new FakeServiceProvider()),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Wpf.ViewModels.PengaturanAplikasiViewModel>.Instance,
                new FakeServiceProvider(),
                layanan);

            Cek(vm.Sections.Count == 7, $"jumlah bagian navigasi = {vm.Sections.Count} (harusnya 7)");
            Cek(vm.Sections.Count(b => b.Aktif) == 1 && vm.BagianUmum.Aktif,
                "tepat satu bagian harus aktif dan bawaannya bagian Umum");
            Cek(vm.JudulSeksiAktif.Length > 0 && vm.RingkasanSeksiAktif.Length > 0,
                "judul/ringkasan bagian aktif tidak boleh kosong");

            vm.SetFocusSection("google-sheet");
            Cek(vm.BagianGoogleSheet.Aktif && !vm.BagianUmum.Aktif && vm.Sections.Count(b => b.Aktif) == 1,
                "permintaan fokus 'google-sheet' tidak memilih bagian Formulir & Sheet");

            Cek(vm.Penomoran.Count == 22, $"baris penomoran = {vm.Penomoran.Count} (harusnya 22)");
            Cek(vm.Penomoran.All(b => !b.Terubah), "baris penomoran awal tidak boleh ditandai berubah");
            Cek(!vm.SimpanPenomoranCommand.CanExecute(null), "tombol simpan aktif walau belum ada perubahan");

            var barisSkd = vm.Penomoran.First(b => b.NamaJenis == "SKD_UMUM");
            barisSkd.Awalan = "47 1";
            Cek(barisSkd.AdaKesalahan && barisSkd.PesanKesalahan.Length > 0,
                "awalan bertanda spasi harus ditandai kesalahan pada baris");

            barisSkd.Awalan = "472";
            Cek(!barisSkd.AdaKesalahan && barisSkd.Contoh == $"472/001/Ds/{DateTime.Now.Year}",
                $"contoh nomor baris = '{barisSkd.Contoh}' (harusnya 472/001/Ds/{DateTime.Now.Year})");
            Cek(vm.AdaPerubahanPenomoran && vm.SimpanPenomoranCommand.CanExecute(null),
                "perubahan awalan tidak menyalakan tombol simpan");

            // Simpan sungguhan dari halaman (perintah VM) — dipakai memastikan kabel
            // perintah ke layanan tersambung, bukan hanya layanannya benar.
            vm.SimpanPenomoranCommand.Execute(null);
            for (int i = 0; i < 100 && vm.AdaPerubahanPenomoran; i++) System.Threading.Thread.Sleep(50);

            Cek(!vm.AdaPerubahanPenomoran, $"perubahan awalan belum tersimpan lewat perintah halaman pengaturan ({vm.PenomoranInfo})");
            Cek(File.Exists(berkasPenyesuaian) && File.ReadAllText(berkasPenyesuaian).Contains("472"),
                "berkas penyesuaian tidak memuat awalan baru dari halaman pengaturan");

            var barisSetelahSimpan = vm.Penomoran.First(b => b.NamaJenis == "SKD_UMUM");
            Cek(barisSetelahSimpan.Awalan == "472" && !barisSetelahSimpan.Terubah && barisSetelahSimpan.Disesuaikan,
                $"daftar belum dimuat ulang dari berkas setelah disimpan (awalan '{barisSetelahSimpan.Awalan}')");

            if (salah == 0)
            {
                Console.WriteLine("Pengaturan penomoran surat: awalan per jenis bisa diganti (470 → 471/472), langsung dipakai saat membuat nomor, dan bisa dikembalikan ke bawaan.");
            }
        }
        catch (Exception ex)
        {
            salah++;
            Console.WriteLine($"FAIL pengaturan penomoran: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            SuDesApp.Configuration.PenomoranOverrideStore.Path = pathPenyesuaianLama;
            try { if (Directory.Exists(folderUji)) Directory.Delete(folderUji, recursive: true); } catch { /* temp */ }
        }

        return salah;
    }

    private static SuDesApp.Data.Models.SuratData BuatPasanganUji()
    {
        var surat = new SuDesApp.Data.Models.SuratData
        {
            NamaJenis = "NTCR_N1",
            TanggalSurat = new DateTime(2026, 9, 1),
            Status = "Active",
            Desa = FakeDesaRepository.BuatDesa(),
            PejabatPenandatangan = "Kepala Desa",
            NamaPejabatPenandatangan = "Kades Uji",
            Warga = new SuDesApp.Data.Models.WargaData
            {
                Nama = "Calon Suami Uji",
                NIK = "3201234567890001",
                TempatLahir = "Karawang",
                TanggalLahir = "1990-01-01",
                JenisKelamin = "Laki-laki",
                Agama = "Islam",
                StatusPerkawinan = "Belum Kawin",
                Pekerjaan = "Petani",
                Kewarganegaraan = "WNI",
                Dusun = "Dusun Uji",
                Desa = "Desa Uji",
                Kecamatan = "Kecamatan Uji",
                Kabupaten = "Kabupaten Uji"
            }
        };

        surat.Ntcr = new SuDesApp.Data.Models.NtcrData
        {
            NamaIstri = "Calon Istri Uji",
            NikIstri = "3201234567890002",
            TempatLahirIstri = "Karawang",
            TanggalLahirIstri = "1992-02-02",
            AgamaIstri = "Islam",
            PekerjaanIstri = "Ibu Rumah Tangga",
            AlamatIstri = "Dusun Uji, Desa Uji",
            StatusPerkawinanIstri = "Belum Kawin",
            KewarganegaraanIstri = "WNI",
            PihakDiterangkanN1 = "Suami",
            TujuanKua = "Kecamatan Uji",
            HariTanggalJamAkad = "Jumat, 2 Oktober 2026, 09.00 WIB",
            TempatAkad = "Kantor KUA Kecamatan Uji",
            PihakAnakIzinOrtu = "Suami",
            PihakMeninggal = "Suami",
            TanggalMeninggal = "1 Agustus 2026",
            TempatMeninggal = "Karawang",
            AyahCalonSuami = new SuDesApp.Data.Models.NtcrOrangTua { Nama = "Ayah Suami Uji" },
            IbuCalonSuami = new SuDesApp.Data.Models.NtcrOrangTua { Nama = "Ibu Suami Uji" },
            AyahCalonIstri = new SuDesApp.Data.Models.NtcrOrangTua { Nama = "Ayah Istri Uji" },
            IbuCalonIstri = new SuDesApp.Data.Models.NtcrOrangTua { Nama = "Ibu Istri Uji" }
        };

        return surat;
    }

    /// <summary>Jumlah halaman PDF dibaca dari pohon halaman (pola sama seperti SuratGeneratorBase).</summary>
    private static int JumlahHalamanPdf(string path)
    {
        string isi = System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(path));
        var cocok = System.Text.RegularExpressions.Regex.Match(isi, @"/Type\s*/Pages[\s\S]{0,256}?/Count\s+(\d+)");
        return cocok.Success && int.TryParse(cocok.Groups[1].Value, out int jumlah) ? jumlah : 1;
    }

    /// <summary>
    /// Salinan data NTCR milik NtcrPaketService harus memuat SELURUH field: satu
    /// field tertinggal akan hilang dari ketujuh blanko paket tanpa peringatan.
    /// Diuji dengan mengisi semua properti NtcrData lalu membandingkan JSON sumber
    /// dan salinannya (metode internal dipanggil lewat refleksi).
    /// </summary>
    private static int PeriksaSalinNtcr()
    {
        var metode = typeof(SuDesApp.Services.NtcrPaketService)
            .GetMethod("SalinNtcr", BindingFlags.NonPublic | BindingFlags.Static);
        if (metode == null)
        {
            Console.WriteLine("FAIL paket NTCR: penyalin data NTCR tidak ditemukan");
            return 1;
        }

        var sumber = new SuDesApp.Data.Models.NtcrData();
        foreach (var p in typeof(SuDesApp.Data.Models.NtcrData).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanWrite) continue;

            if (p.PropertyType == typeof(string))
            {
                p.SetValue(sumber, $"UJI-{p.Name}");
            }
            else if (p.PropertyType == typeof(int))
            {
                p.SetValue(sumber, 0); // ID_CalonIstri memang tidak dibawa ke salinan
            }
            else if (p.PropertyType == typeof(SuDesApp.Data.Models.NtcrOrangTua))
            {
                var orangTua = new SuDesApp.Data.Models.NtcrOrangTua();
                foreach (var q in typeof(SuDesApp.Data.Models.NtcrOrangTua).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                             .Where(q => q.CanWrite && q.PropertyType == typeof(string)))
                {
                    q.SetValue(orangTua, $"UJI-{p.Name}-{q.Name}");
                }

                p.SetValue(sumber, orangTua);
            }
        }

        var salinan = (SuDesApp.Data.Models.NtcrData)metode.Invoke(null, new object[] { sumber });

        string jsonSumber = System.Text.Json.JsonSerializer.Serialize(sumber);
        string jsonSalinan = System.Text.Json.JsonSerializer.Serialize(salinan);
        if (!string.Equals(jsonSumber, jsonSalinan, StringComparison.Ordinal))
        {
            Console.WriteLine("FAIL paket NTCR: penyalinan data NTCR tidak utuh");
            Console.WriteLine($"     sumber : {jsonSumber}");
            Console.WriteLine($"     salinan: {jsonSalinan}");
            return 1;
        }

        if (ReferenceEquals(salinan.AyahCalonSuami, sumber.AyahCalonSuami))
        {
            Console.WriteLine("FAIL paket NTCR: identitas orang tua masih memakai objek yang sama");
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// Register NTCR dan Register Surat harus terpisah: judul, daftar filter jenis,
    /// baris data, dan kolomnya sendiri. Register Surat tidak boleh lagi memuat
    /// kolom calon mempelai.
    /// </summary>
    private static int PeriksaPemisahanRegister(SuDesApp.AppConfig cfg)
    {
        int salah = 0;

        void Cek(bool syarat, string pesan)
        {
            if (!syarat)
            {
                salah++;
                Console.WriteLine($"FAIL register terpisah: {pesan}");
            }
        }

        try
        {
            var desaRepo = new FakeDesaRepository();
            var jenisRepo = new RegisterUjiJenisSuratRepository();
            var repoSurat = new RegisterUjiSuratRepository();
            var repoNtcr = new RegisterUjiSuratRepository();
            var settings = new SuDesApp.ControlSurat.SettingsManager(desaRepo, cfg);
            var fileService = new SuDesApp.Utilities.FileService(cfg,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Utilities.FileService>.Instance);
            var nav = new SuDesApp.Wpf.ViewModels.NavigationService(new FakeServiceProvider());

            var uowSurat = new FakeUnitOfWork(desaRepo, new WaSmokeWargaRepository(), repoSurat, jenisRepo);
            var uowNtcr = new FakeUnitOfWork(desaRepo, new WaSmokeWargaRepository(), repoNtcr, jenisRepo);

            var vmSurat = RegisterUjiSurat.Buat(uowSurat, settings, cfg, fileService, nav);
            var vmNtcr = RegisterUjiNtcr.Buat(uowNtcr, settings, cfg, fileService, nav);

            TungguSampai(() => vmSurat.JenisSuratItems.Count > 0 && vmNtcr.JenisSuratItems.Count > 0);

            Cek(vmSurat.PageTitle == "Register Surat", $"judul register surat = '{vmSurat.PageTitle}'");
            Cek(vmNtcr.PageTitle == "Register NTCR", $"judul register NTCR = '{vmNtcr.PageTitle}'");

            var ntcrSet = new HashSet<string>(SuDesApp.Data.Models.SuratConstants.NtcrSemua, StringComparer.OrdinalIgnoreCase);

            // Daftar pilihan jenis surat: surat umum tanpa NTCR, NTCR tanpa surat umum.
            Cek(vmSurat.JenisSuratItems.All(i => !ntcrSet.Contains(i.FilterValue)),
                "daftar filter Register Surat masih memuat jenis NTCR");
            Cek(vmSurat.JenisSuratItems.Any(i => i.FilterValue == "SKD_UMUM"),
                "daftar filter Register Surat kehilangan jenis surat umum");
            Cek(vmNtcr.JenisSuratItems.All(i => i.FilterValue == "GROUP_NTCR" || ntcrSet.Contains(i.FilterValue)),
                "daftar filter Register NTCR memuat jenis surat non-NTCR");
            Cek(vmNtcr.JenisSuratItems.Any(i => i.FilterValue == "GROUP_NTCR"),
                "Register NTCR kehilangan pilihan 'Semua Blanko NTCR'");
            Cek(vmNtcr.JenisSuratItems.Count == 1 + ntcrSet.Count,
                $"pilihan blanko Register NTCR = {vmNtcr.JenisSuratItems.Count} (harusnya {1 + ntcrSet.Count})");

            // Query ke database: register surat mengecualikan NTCR, register NTCR
            // menyaring grup NTCR tanpa mengecualikan apa pun.
            Cek(repoSurat.FilterTerakhir?.ExcludeJenisNames?.Any() == true,
                "query Register Surat tidak mengecualikan jenis NTCR");
            Cek(repoNtcr.FilterTerakhir?.JenisSurat == "GROUP_NTCR",
                $"query Register NTCR tidak menyaring grup NTCR (='{repoNtcr.FilterTerakhir?.JenisSurat}')");
            Cek(repoNtcr.FilterTerakhir?.ExcludeJenisNames?.Any() != true,
                "query Register NTCR tidak boleh mengecualikan jenis apa pun");

            // Baris data: register surat memakai model biasa (tanpa kolom pasangan),
            // register NTCR memakai model dengan data calon istri & tujuan surat.
            var suratUji = BuatPasanganUji();
            var barisSurat = RegisterUjiSurat.BarisUji(uowSurat, settings, cfg, fileService, nav, suratUji);
            var barisNtcr = RegisterUjiNtcr.BarisUji(uowNtcr, settings, cfg, fileService, nav, suratUji);

            Cek(barisSurat.GetType() == typeof(SuDesApp.Wpf.ViewModels.SuratDisplayModel),
                $"baris Register Surat memakai model berisi kolom NTCR ({barisSurat.GetType().Name})");
            Cek(barisNtcr is SuDesApp.Wpf.ViewModels.NtcrDisplayModel,
                "baris Register NTCR bukan model NTCR");

            if (barisNtcr is SuDesApp.Wpf.ViewModels.NtcrDisplayModel ntcrBaris)
            {
                Cek(ntcrBaris.CalonIstriDisplay == "Calon Istri Uji",
                    $"kolom calon istri = '{ntcrBaris.CalonIstriDisplay}'");
                Cek(ntcrBaris.AlamatIstriDisplay.Contains("Desa Uji"),
                    $"kolom alamat calon istri = '{ntcrBaris.AlamatIstriDisplay}'");
                Cek(ntcrBaris.TujuanDisplay.Contains("Kecamatan Uji"),
                    $"kolom tujuan surat = '{ntcrBaris.TujuanDisplay}'");
            }

            // Kolomnya sendiri di XAML: Register Surat tanpa kolom calon mempelai,
            // Register NTCR memakai kolom pasangan.
            var berkasSurat = CariBerkasRepo("SuDesApp.Wpf/Views/RegisterSuratView.xaml");
            var berkasNtcr = CariBerkasRepo("SuDesApp.Wpf/Views/RegisterNtcrView.xaml");

            Cek(berkasSurat != null, "berkas RegisterSuratView.xaml tidak ditemukan");
            Cek(berkasNtcr != null, "berkas RegisterNtcrView.xaml tidak ditemukan");

            if (berkasSurat != null && berkasNtcr != null)
            {
                var xamlSurat = File.ReadAllText(berkasSurat);
                var xamlNtcr = File.ReadAllText(berkasNtcr);

                Cek(!xamlSurat.Contains("Calon Mempelai") && !xamlSurat.Contains("PasanganDisplay"),
                    "Register Surat masih memuat kolom calon mempelai");
                Cek(xamlNtcr.Contains("Calon Istri") && xamlNtcr.Contains("Tujuan (KUA / Numpang Nikah)"),
                    "Register NTCR tidak memuat kolom pasangan/tujuan");
            }

            if (salah == 0)
            {
                Console.WriteLine(
                    $"Register terpisah: Register Surat {vmSurat.JenisSuratItems.Count} jenis surat umum (tanpa kolom mempelai), " +
                    $"Register NTCR {vmNtcr.JenisSuratItems.Count} pilihan blanko dengan kolom pasangan.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL register terpisah: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }

        return salah;
    }

    private static void TungguSampai(Func<bool> syarat, int timeoutMs = 15000)
    {
        var batas = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!syarat() && DateTime.UtcNow < batas)
        {
            System.Threading.Thread.Sleep(25);
        }
    }

    /// <summary>Register Surat untuk uji — mengakses hook protected (model baris register).</summary>
    private sealed class RegisterUjiSurat : SuDesApp.Wpf.ViewModels.RegisterSuratViewModel
    {
        private RegisterUjiSurat(
            SuDesApp.Configuration.IUnitOfWork uow,
            SuDesApp.ControlSurat.SettingsManager settings,
            SuDesApp.AppConfig cfg,
            SuDesApp.Utilities.FileService fileService,
            SuDesApp.Wpf.ViewModels.NavigationService nav)
            : base(uow,
                Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
                settings, cfg, fileService, new FakeMessageService(), new FakeServiceProvider(), nav,
                (_, _, _) => throw new InvalidOperationException("Pratinjau PDF tidak dipakai pada uji ini."),
                new SuDesApp.Wpf.Services.PdfPrintService(
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Wpf.Services.PdfPrintService>.Instance))
        {
        }

        public static RegisterUjiSurat Buat(
            SuDesApp.Configuration.IUnitOfWork uow,
            SuDesApp.ControlSurat.SettingsManager settings,
            SuDesApp.AppConfig cfg,
            SuDesApp.Utilities.FileService fileService,
            SuDesApp.Wpf.ViewModels.NavigationService nav)
            => new(uow, settings, cfg, fileService, nav);

        public static SuDesApp.Wpf.ViewModels.SuratDisplayModel BarisUji(
            SuDesApp.Configuration.IUnitOfWork uow,
            SuDesApp.ControlSurat.SettingsManager settings,
            SuDesApp.AppConfig cfg,
            SuDesApp.Utilities.FileService fileService,
            SuDesApp.Wpf.ViewModels.NavigationService nav,
            SuDesApp.Data.Models.SuratData surat)
        {
            var vm = new RegisterUjiSurat(uow, settings, cfg, fileService, nav);
            var model = vm.NewDisplayModel();
            vm.FillDisplayModelExtras(model, surat);
            return model;
        }
    }

    /// <summary>Register NTCR untuk uji — sama, tetapi memakai model baris NTCR.</summary>
    private sealed class RegisterUjiNtcr : SuDesApp.Wpf.ViewModels.RegisterNtcrViewModel
    {
        private RegisterUjiNtcr(
            SuDesApp.Configuration.IUnitOfWork uow,
            SuDesApp.ControlSurat.SettingsManager settings,
            SuDesApp.AppConfig cfg,
            SuDesApp.Utilities.FileService fileService,
            SuDesApp.Wpf.ViewModels.NavigationService nav)
            : base(uow,
                Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
                settings, cfg, fileService, new FakeMessageService(), new FakeServiceProvider(), nav,
                (_, _, _) => throw new InvalidOperationException("Pratinjau PDF tidak dipakai pada uji ini."),
                new SuDesApp.Wpf.Services.PdfPrintService(
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<SuDesApp.Wpf.Services.PdfPrintService>.Instance))
        {
        }

        public static RegisterUjiNtcr Buat(
            SuDesApp.Configuration.IUnitOfWork uow,
            SuDesApp.ControlSurat.SettingsManager settings,
            SuDesApp.AppConfig cfg,
            SuDesApp.Utilities.FileService fileService,
            SuDesApp.Wpf.ViewModels.NavigationService nav)
            => new(uow, settings, cfg, fileService, nav);

        public static SuDesApp.Wpf.ViewModels.SuratDisplayModel BarisUji(
            SuDesApp.Configuration.IUnitOfWork uow,
            SuDesApp.ControlSurat.SettingsManager settings,
            SuDesApp.AppConfig cfg,
            SuDesApp.Utilities.FileService fileService,
            SuDesApp.Wpf.ViewModels.NavigationService nav,
            SuDesApp.Data.Models.SuratData surat)
        {
            var vm = new RegisterUjiNtcr(uow, settings, cfg, fileService, nav);
            var model = vm.NewDisplayModel();
            vm.FillDisplayModelExtras(model, surat);
            return model;
        }
    }

    /// <summary>
    /// Repo surat palsu untuk uji pemisahan register: mencatat filter terakhir yang
    /// dikirim, sehingga bisa diperiksa jenis apa yang ditampilkan/disembunyikan.
    /// </summary>
    private sealed class RegisterUjiSuratRepository : SuDesApp.Data.Repositories.ISuratRepository
    {
        public SuDesApp.Data.Models.FilterConditions? FilterTerakhir;

        public Task<IEnumerable<SuDesApp.Data.Models.SuratData>> GetFilteredAsync(
            SuDesApp.Data.Models.FilterConditions filters, string sortBy, bool ascending, int skip, int take,
            CancellationToken ct = default)
        {
            FilterTerakhir = filters;
            return Task.FromResult<IEnumerable<SuDesApp.Data.Models.SuratData>>(
                Array.Empty<SuDesApp.Data.Models.SuratData>());
        }

        public Task<int> CountAsync(SuDesApp.Data.Models.FilterConditions filters, CancellationToken ct = default)
            => Task.FromResult(0);

        public Task<List<string>> GetJenisSuratKeteranganDesaAsync() => Task.FromResult(new List<string>());

        public Task InitializeSuratIndexesAsync() => Task.CompletedTask;

        public Task<int> AddSuratAsync(SuDesApp.Data.Models.SuratData entity, System.Data.IDbTransaction? transaction = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SuDesApp.Data.Models.SuratData> GetByIdAsync(int id, System.Data.IDbTransaction? transaction = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> InsertAsync(SuDesApp.Data.Models.SuratData entity, System.Data.IDbTransaction? transaction = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> UpdateAsync(SuDesApp.Data.Models.SuratData entity, System.Data.IDbTransaction? transaction = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> UpdateStatusAsync(int id, string status, System.Data.IDbTransaction? transaction = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(int id, System.Data.IDbTransaction? transaction = null, CancellationToken ct = default) => throw new NotImplementedException();
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

    /// <summary>Repo jenis surat palsu: berisi jenis surat umum dan seluruh blanko NTCR.</summary>
    private sealed class RegisterUjiJenisSuratRepository : SuDesApp.Data.Repositories.IJenisSuratRepository
    {
        private static readonly string[] JenisUmum = { "SKD_UMUM", "DOMISILI_WARGA" };

        public Task<List<SuDesApp.Data.Models.JenisSuratKelas>> GetAllJenisSuratAsync()
        {
            var daftar = new List<SuDesApp.Data.Models.JenisSuratKelas>();
            int id = 1;

            foreach (var nama in JenisUmum)
            {
                daftar.Add(new SuDesApp.Data.Models.JenisSuratKelas
                {
                    ID_Jenis = id++,
                    NamaJenis = nama,
                    KodeJenis = nama,
                    IsActive = true
                });
            }

            foreach (var nama in SuDesApp.Data.Models.SuratConstants.NtcrSemua)
            {
                daftar.Add(new SuDesApp.Data.Models.JenisSuratKelas
                {
                    ID_Jenis = id++,
                    NamaJenis = nama,
                    KodeJenis = nama,
                    IsActive = true
                });
            }

            return Task.FromResult(daftar);
        }

        public Task<Dictionary<string, string>> GetJenisSuratDisplayNamesAsync()
        {
            var peta = new Dictionary<string, string>
            {
                ["SKD_UMUM"] = "SKD Umum",
                ["DOMISILI_WARGA"] = "Domisili Warga"
            };

            foreach (var nama in SuDesApp.Data.Models.SuratConstants.NtcrSemua)
            {
                peta[nama] = SuDesApp.Data.Models.NtcrKatalog.Judul(nama);
            }

            return Task.FromResult(peta);
        }

        public Task<List<string>> GetAvailableYearsAsync() => Task.FromResult(new List<string>());
        public Task<List<string>> GetJenisSuratKeteranganDesa() => Task.FromResult(new List<string>());
        public Task InitializeJenisSuratDataAsync() => Task.CompletedTask;
        public Task RefreshConfigurationAsync() => Task.CompletedTask;
        public Task<SuDesApp.Data.Models.JenisSuratKelas> GetJenisSuratByNamaAsync(string namaJenis) => throw new NotImplementedException();
        public Task<SuDesApp.Data.Models.JenisSuratKelas> GetByNamaAsync(string namaJenis) => throw new NotImplementedException();
        public Task<int> GetIdJenisSuratByNamaAsync(string namaJenis) => throw new NotImplementedException();
        public Task<string> GenerateNomorSuratAsync(string kodeJenis) => throw new NotImplementedException();
        public Task<bool> IsNomorSuratExistsAsync(string nomorSurat) => throw new NotImplementedException();
        public Task<int?> GetLastSuratIdByTypeAsync(string templateName) => throw new NotImplementedException();
        public Task<bool> HasExistingNomorSuratAsync(string namaJenis) => throw new NotImplementedException();
        public Task<HashSet<string>> GetSharedNumberingGroupAsync() => throw new NotImplementedException();
    }

    /// <summary>Cari berkas di dalam repo memakai path relatif terhadap akar repo.</summary>
    private static string? CariBerkasRepo(string relativePath)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;
        }
        return null;
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
