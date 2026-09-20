// PdfGallery: alat diagnostik visual untuk generator surat.
// Menghasilkan satu PDF per jenis surat (data uji lengkap), lalu merasterisasi
// setiap PDF ke PNG via PDFium agar tata letak bisa diperiksa secara visual.
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PdfiumViewer;
using SuDesApp;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.GeneratorPdf;
using SuDesApp.Interfaces;
using SuDesApp.Utilities;

internal static class Program
{
    private static async Task<int> Main()
    {
        var root = FindRoot();
        Console.WriteLine($"Root repo: {root}");
        var appSettingsPath = Path.Combine(root, "appsettings.json");
        var cfg = (AppConfig)Activator.CreateInstance(typeof(AppConfig), appSettingsPath, null)!;
        var outDir = Path.Combine(root, "pdf-gallery");
        Directory.CreateDirectory(outDir);

        var desaRepo = new FakeDesaRepository();
        var suratRepo = new FakeSuratRepository();
        var settingsManager = new SettingsManager(desaRepo, cfg);
        var fileSvc = new FileService(cfg, NullLogger<FileService>.Instance);
        var loggerFactory = NullLoggerFactory.Instance;

        ISuratGenerator Make(Type t) => (ISuratGenerator)Activator.CreateInstance(
            t, cfg, fileSvc, desaRepo, suratRepo, settingsManager,
            NullLoggerOf(t),
            loggerFactory)!;

        var map = new Dictionary<string, Type>
        {
            ["SKD_UMUM"] = typeof(SKDGenerator),
            ["DOMISILI_WARGA"] = typeof(DomisiliWargaGenerator),
            ["DOMISILI_INSTANSI"] = typeof(DomisiliInstansiGenerator),
            ["PENGANTAR_SKCK"] = typeof(SKCKGenerator),
            ["SKTM"] = typeof(SKTMGenerator),
            ["KEMATIAN"] = typeof(KematianGenerator),
            ["AHLI_WARIS"] = typeof(AhliWarisGenerator),
            ["GARAPAN_SAWAH"] = typeof(GarapanGenerator),
            ["IZIN_ORTU"] = typeof(IzinOrtuGenerator),
            ["KENAL_LAHIR"] = typeof(KenalLahirGenerator),
            ["IJIN_TINGGAL"] = typeof(IjinTinggalGenerator),
            ["SKU"] = typeof(SKUGenerator),
            ["BEDANAMA"] = typeof(BedaNamaGenerator),
            ["NTCR_N1"] = typeof(NtcrGenerator),
            ["NTCR_N2"] = typeof(NtcrGenerator),
            ["NTCR_N3"] = typeof(NtcrGenerator),
            ["NTCR_N4"] = typeof(NtcrGenerator),
            ["NTCR_N5"] = typeof(NtcrGenerator),
            ["NTCR_N6"] = typeof(NtcrGenerator),
            ["NTCR_N8"] = typeof(NtcrGenerator),
            // N3 yang dikosongkan: tetap blanko N3, hanya nama berkasnya dibedakan.
            ["NTCR_N3_KOSONG"] = typeof(NtcrGenerator),
            // Kasus gelar: nama jenis tetap jenis aslinya, hanya nama berkasnya dibedakan.
            ["GELAR_SKU"] = typeof(SKUGenerator),
            ["GELAR_KEMATIAN"] = typeof(KematianGenerator),
            ["GELAR_KENAL_LAHIR"] = typeof(KenalLahirGenerator),
        };

        var cases = BuatKasus();
        int fails = 0;
        var hasil = new List<(string Name, int Pages, string Error)>();

        foreach (var (name, surat) in cases)
        {
            try
            {
                var generator = Make(map[name]);
                var pdfPath = Path.Combine(outDir, $"{name}.pdf");
                using (var fs = File.Create(pdfPath))
                    await generator.GeneratePdfAsync(fs, surat, null);

                int pages;
                using (var doc = PdfDocument.Load(pdfPath))
                {
                    pages = doc.PageCount;
                    for (int p = 0; p < Math.Min(2, pages); p++)
                    {
                        using var img = doc.Render(p, 115f, 115f, false);
                        img.Save(Path.Combine(outDir, $"{name}_p{p + 1}.png"), ImageFormat.Png);
                    }
                }
                // Blanko NTCR wajib satu halaman: blok LAMPIRAN di atasnya tidak boleh
                // mendorong isi ke halaman kedua (lihat KerapatanSurat).
                if (name.StartsWith("NTCR_N", StringComparison.Ordinal) && pages != 1)
                {
                    fails++;
                    hasil.Add((name, pages, "Blanko NTCR harus muat 1 halaman"));
                    Console.WriteLine($"FAIL {name}: {pages} halaman (blanko NTCR harus 1 halaman)");
                }
                else
                {
                    hasil.Add((name, pages, ""));
                    Console.WriteLine($"OK   {name} ({pages} hal.)");
                }

                // Blanko permohonan N2 & N3: daftar "surat yang diperlukan untuk
                // diperiksa" tercetak baku, dan butir di luar daftar itu hanya titik-titik
                // (diisi tangan di KUA) — lampiran tambahan yang dulu diketik di form
                // tidak boleh ikut tercetak.
                if (name is "NTCR_N2" or "NTCR_N3" or "NTCR_N3_KOSONG")
                {
                    using var docN = PdfDocument.Load(pdfPath);
                    string teksN = docN.GetPdfText(0) ?? string.Empty;
                    var masalahN = new List<string>();

                    foreach (var terlarang in new[] { "Surat keterangan sehat", "Akta cerai" })
                    {
                        if (teksN.Contains(terlarang, StringComparison.OrdinalIgnoreCase))
                            masalahN.Add($"'{terlarang}' seharusnya tidak tercetak");
                    }

                    if (name == "NTCR_N2" &&
                        !teksN.Contains("Surat pengantar nikah dari Desa/Kelurahan", StringComparison.OrdinalIgnoreCase))
                    {
                        masalahN.Add("butir baku lampiran N2 hilang");
                    }

                    if (name != "NTCR_N2" &&
                        !teksN.Contains("Putusan Isbat", StringComparison.OrdinalIgnoreCase))
                    {
                        masalahN.Add("butir baku lampiran N3 hilang");
                    }

                    // Butir yang dibiarkan kosong harus tercetak sebagai titik-titik.
                    if (!teksN.Contains(".............."))
                    {
                        masalahN.Add("baris titik-titik butir kosong tidak tercetak");
                    }

                    if (name == "NTCR_N3_KOSONG" &&
                        (!teksN.Contains("PENCATATAN ISBAT", StringComparison.OrdinalIgnoreCase) ||
                         !teksN.Contains("Model N3", StringComparison.OrdinalIgnoreCase)))
                    {
                        masalahN.Add("blanko N3 kosong tidak tercetak utuh");
                    }

                    if (masalahN.Count > 0)
                    {
                        fails++;
                        Console.WriteLine($"FAIL {name}: {string.Join("; ", masalahN)}");
                    }
                    else
                    {
                        Console.WriteLine($"     {name}: lampiran baku + butir titik-titik tercetak, tanpa isian dari form.");
                    }
                }

                // Surat numpang nikah (N8): surat keterangan desa biasa — memuat tujuan
                // numpang nikah dan calon istri, tanpa blok LAMPIRAN Kepdirjen.
                if (name == "NTCR_N8")
                {
                    using var docN8 = PdfDocument.Load(pdfPath);
                    string teksN8 = docN8.GetPdfText(0) ?? string.Empty;

                    var wajibN8 = new[]
                    {
                        "SURAT KETERANGAN NUMPANG NIKAH",
                        "numpang nikah di",
                        "Desa Mekarsari",
                        "Pakis Jaya",
                        "Calon Istri Uji"
                    };

                    var hilangN8 = wajibN8.Where(k => !teksN8.Contains(k, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (teksN8.Contains("LAMPIRAN", StringComparison.OrdinalIgnoreCase))
                    {
                        hilangN8.Add("blok LAMPIRAN Kepdirjen tidak boleh tercetak");
                    }

                    if (hilangN8.Count > 0)
                    {
                        fails++;
                        Console.WriteLine($"FAIL NTCR_N8: {string.Join("; ", hilangN8)}");
                    }
                    else
                    {
                        Console.WriteLine("     N8: kop desa + tujuan numpang nikah tercetak, tanpa blok LAMPIRAN.");
                    }
                }
            }
            catch (Exception ex)
            {
                fails++;
                hasil.Add((name, 0, $"{ex.GetType().Name}: {ex.Message}"));
                Console.WriteLine($"FAIL {name}: {ex}");
            }
        }

        // === Paket NTCR: SATU berkas PDF berisi seluruh blanko, satu blanko per halaman ===
        try
        {
            // Paket pernikahan hanya memuat blanko Kepdirjen N1–N6; surat numpang nikah
            // (N8) berbentuk surat desa tersendiri sehingga tidak ikut berkas gabungan.
            var blankoPaket = cases
                .Where(c => c.Name.StartsWith("NTCR_", StringComparison.Ordinal) &&
                            NtcrKatalog.PakaiBlokKepdirjen(c.Surat.NamaJenis))
                .Select(c => c.Surat)
                .ToList();

            var generatorPaket = new NtcrGenerator(cfg, fileSvc, desaRepo, suratRepo, settingsManager,
                NullLogger<NtcrGenerator>.Instance, loggerFactory);

            var pdfPathPaket = Path.Combine(outDir, "NTCR_PAKET.pdf");
            using (var fs = File.Create(pdfPathPaket))
                await generatorPaket.GeneratePaketPdfAsync(fs, blankoPaket, null);

            int pagesPaket;
            using (var doc = PdfDocument.Load(pdfPathPaket))
            {
                pagesPaket = doc.PageCount;
                for (int p = 0; p < pagesPaket; p++)
                {
                    using var img = doc.Render(p, 115f, 115f, false);
                    img.Save(Path.Combine(outDir, $"NTCR_PAKET_p{p + 1}.png"), ImageFormat.Png);
                }
            }

            // Halaman pratinjau mandiri (gambar disematkan) agar hasil paket mudah diperiksa.
            TulisHtmlPaketNtcr(outDir, blankoPaket, pagesPaket);

            // Isi tiap blanko harus tetap utuh di halamannya sendiri.
            bool halamanSesuai = pagesPaket == blankoPaket.Count;
            if (!halamanSesuai)
            {
                fails++;
                hasil.Add(("NTCR_PAKET", pagesPaket, $"Harusnya {blankoPaket.Count} halaman (satu blanko per halaman)"));
                Console.WriteLine($"FAIL NTCR_PAKET: {pagesPaket} halaman, harusnya {blankoPaket.Count}");
            }

            // Tiap halaman wajib memuat kepala "LAMPIRAN <nomor>" dan label "Model Nx"
            // sesuai blankonya (Kepdirjen Bimas Islam No. 473 Tahun 2020).
            int lampiranSalah = 0;
            using (var doc = PdfDocument.Load(pdfPathPaket))
            {
                for (int p = 0; p < Math.Min(doc.PageCount, blankoPaket.Count); p++)
                {
                    string teksHalaman = doc.GetPdfText(p) ?? string.Empty;
                    string kode = NtcrKatalog.Kode(blankoPaket[p].NamaJenis);
                    string lampiran = NtcrKatalog.Lampiran(blankoPaket[p].NamaJenis);

                    if (!teksHalaman.Contains("LAMPIRAN " + lampiran) ||
                        !teksHalaman.Contains("KEPUTUSAN DIREKTUR JENDERAL") ||
                        !teksHalaman.Contains("PETUNJUK TEKNIS PELAKSANAAN PENCATATAN PERNIKAHAN") ||
                        !teksHalaman.Contains("Model " + kode))
                    {
                        lampiranSalah++;
                        Console.WriteLine($"FAIL NTCR_PAKET hal. {p + 1} ({kode}): kepala LAMPIRAN {lampiran} / Model {kode} tidak terbaca");
                    }
                }
            }

            fails += lampiranSalah;

            if (halamanSesuai && lampiranSalah == 0)
            {
                hasil.Add(("NTCR_PAKET", pagesPaket, ""));
                Console.WriteLine($"OK   NTCR_PAKET ({pagesPaket} hal. dari {blankoPaket.Count} blanko; kepala LAMPIRAN & Model tercetak)");
            }
        }
        catch (Exception ex)
        {
            fails++;
            hasil.Add(("NTCR_PAKET", 0, $"{ex.GetType().Name}: {ex.Message}"));
            Console.WriteLine($"FAIL NTCR_PAKET: {ex}");
        }

        // === Daftar Hadir: halaman form baru (kolom checkbox dinamis + kop surat) ===
        var daftarHadirGenerator = new DaftarHadirGenerator(cfg, fileSvc, NullLogger<DaftarHadirGenerator>.Instance);
        foreach (var (suffix, data) in BuatKasusDaftarHadir())
        {
            string name = "DAFTAR_HADIR" + suffix;
            try
            {
                var pdfPath = Path.Combine(outDir, $"{name}.pdf");
                using (var fs = File.Create(pdfPath))
                    daftarHadirGenerator.GeneratePdf(fs, data);

                int pages;
                using (var doc = PdfDocument.Load(pdfPath))
                {
                    pages = doc.PageCount;
                    for (int p = 0; p < Math.Min(2, pages); p++)
                    {
                        using var img = doc.Render(p, 115f, 115f, false);
                        img.Save(Path.Combine(outDir, $"{name}_p{p + 1}.png"), ImageFormat.Png);
                    }
                }
                hasil.Add((name, pages, ""));
                Console.WriteLine($"OK   {name} ({pages} hal.)");
            }
            catch (Exception ex)
            {
                fails++;
                hasil.Add((name, 0, $"{ex.GetType().Name}: {ex.Message}"));
                Console.WriteLine($"FAIL {name}: {ex}");
            }
        }

        // === Template Surat: surat buatan pengguna sendiri (definisi dari wizard) ===
        fails += await PeriksaTemplateSuratAsync(cfg, desaRepo, outDir, hasil);

        // === Permohonan Rekening Koran: kop lengkap + alamat empat komponen ===
        fails += await PeriksaRekeningKoranAsync(cfg, desaRepo, suratRepo, settingsManager, fileSvc, loggerFactory, outDir, hasil);

        // === Mode perbandingan: arsip PDF lama di folder TempPDF aplikasi ===
        var folderArsip = CariFolderArsip(root);
        var (arsip, arsipTanpaPasangan) = KumpulkanArsip(folderArsip, map.Keys);
        var arsipRendered = RenderArsip(outDir, arsip);
        Console.WriteLine(arsipRendered.Count == 0
            ? "Tidak ada arsip PDF lama yang cocok di folder TempPDF."
            : $"{arsipRendered.Count} arsip PDF lama dirasterisasi untuk perbandingan.");

        TulisIndexHtml(outDir, hasil);
        TulisCompareHtml(outDir, hasil, arsipRendered, arsipTanpaPasangan);
        Console.WriteLine(fails == 0 ? "SEMUA PDF BERHASIL" : $"{fails} PDF GAGAL");
        Console.WriteLine($"Galeri: {Path.Combine(outDir, "index.html")}");
        Console.WriteLine($"Perbandingan: {Path.Combine(outDir, "compare.html")}");
        return fails == 0 ? 0 : 1;
    }

    /// <summary>
    /// Permohonan Rekening Koran: surat ini dulu menyusun data desanya sendiri dari
    /// jabatan penandatangan, sehingga kopnya kehilangan kecamatan, kabupaten, alamat
    /// kantor desa, dan kodepos. Pemeriksaan ini memastikan kop terisi lengkap dari
    /// pengaturan desa dan alamat pejabat tercetak sebagai empat komponen (dusun/jalan
    /// &amp; desa pada baris pertama, kecamatan &amp; kabupaten pada baris kedua), sama
    /// seperti hasil menu input surat.
    /// </summary>
    private static async Task<int> PeriksaRekeningKoranAsync(
        AppConfig cfg,
        IDesaRepository desaRepo,
        ISuratRepository suratRepo,
        SettingsManager settingsManager,
        FileService fileSvc,
        ILoggerFactory loggerFactory,
        string outDir,
        List<(string Nama, int Halaman, string Masalah)> hasil)
    {
        const string nama = "PERMOHONAN_REKENING_KORAN";
        try
        {
            var generator = new RekeningKoranGenerator(
                cfg, fileSvc, desaRepo, suratRepo, settingsManager,
                NullLogger<RekeningKoranGenerator>.Instance, loggerFactory);

            var desa = FakeDesaRepository.BuatDesa();
            var data = new RekeningKoranData
            {
                NomorSurat = "130/001/Ds/2026",
                Perihal = "Permohonan Print Out Rekening Koran",
                TanggalSurat = new DateTime(2026, 9, 19),
                NamaPejabat = "H. SURYANA",
                Jabatan = "Kepala Desa Sumberjaya",

                // Empat komponen alamat seperti kolom di menu input surat.
                AlamatDusun = "Krajan RT 001 RW 002",
                AlamatDesa = desa.NamaDesa,
                AlamatKecamatan = desa.Kecamatan,
                AlamatKabupaten = desa.Kabupaten,

                NamaPemegangRekening = "PEMERINTAH DESA SUMBERJAYA",
                NomorRekening = "0012345678901",
                PeriodeRekening = "01-06-2026 s/d 31-08-2026",
                Bank = "BANK JABAR",
                KCP = "KCP TEMPURAN",

                // Data desa sengaja TIDAK lengkap (seperti yang dikirim form lama:
                // hanya nama desa dari jabatan) — generator wajib melengkapinya dari
                // pengaturan desa.
                Desa = new DesaData
                {
                    NamaDesa = desa.NamaDesa,
                    KepalaDesa = desa.KepalaDesa
                }
            };

            var pdfPath = Path.Combine(outDir, $"{nama}.pdf");
            using (var stream = File.Create(pdfPath))
            {
                await generator.GeneratePdfAsync(stream, data);
            }

            int pages;
            using (var doc = PdfDocument.Load(pdfPath))
            {
                pages = doc.PageCount;
                using var img = doc.Render(0, 115f, 115f, false);
                img.Save(Path.Combine(outDir, $"{nama}_p1.png"), ImageFormat.Png);
            }

            var masalah = new List<string>();
            using (var doc = PdfDocument.Load(pdfPath))
            {
                string teks = doc.GetPdfText(0) ?? string.Empty;

                if (pages != 1) masalah.Add($"{pages} halaman (harusnya 1)");

                // Kop wajib lengkap — inilah yang dulu hilang.
                foreach (var bagian in new[]
                {
                    "PEMERINTAH KABUPATEN KARAWANG",
                    "KECAMATAN TEMPURAN",
                    "PEMERINTAH DESA SUMBERJAYA",
                    "Jl. Raya Sumberjaya No. 1",
                    "41361"
                })
                {
                    if (!teks.Contains(bagian, StringComparison.OrdinalIgnoreCase))
                        masalah.Add($"kop kehilangan '{bagian}'");
                }

                // Alamat pejabat: format empat komponen yang sama dengan surat lain.
                if (!teks.Contains("Dusun Krajan RT 001 RW 002 Desa Sumberjaya", StringComparison.OrdinalIgnoreCase))
                    masalah.Add("baris pertama alamat (Dusun + Desa) tidak tercetak sesuai format surat");
                if (!teks.Contains("Kecamatan Tempuran Kabupaten Karawang", StringComparison.OrdinalIgnoreCase))
                    masalah.Add("baris kedua alamat (Kecamatan + Kabupaten) tidak tercetak sesuai format surat");

                if (!teks.Contains("KEPALA DESA SUMBERJAYA", StringComparison.OrdinalIgnoreCase))
                    masalah.Add("jabatan penandatangan tidak tercetak");
            }

            if (masalah.Count > 0)
            {
                hasil.Add((nama, pages, string.Join("; ", masalah)));
                Console.WriteLine($"FAIL {nama}: {string.Join("; ", masalah)}");
                return 1;
            }

            hasil.Add((nama, pages, ""));
            Console.WriteLine($"OK   {nama} ({pages} hal.; kop lengkap + alamat empat komponen)");
            return 0;
        }
        catch (Exception ex)
        {
            hasil.Add((nama, 0, $"{ex.GetType().Name}: {ex.Message}"));
            Console.WriteLine($"FAIL {nama}: {ex}");
            return 1;
        }
    }

    /// <summary>
    /// Template Surat (menu baru): surat disusun pengguna lewat wizard — kop, judul,
    /// nomor, blok teks, kolom isian (dengan/tanpa grid), tanda tangan, dan teks kaki.
    /// Diperiksa dua bentuk: lengkap dan minimal (tanpa kop, grid, maupun tanda tangan).
    /// </summary>
    private static async Task<int> PeriksaTemplateSuratAsync(
        AppConfig cfg, IDesaRepository desaRepo, string outDir,
        List<(string Name, int Pages, string Error)> hasil)
    {
        int fails = 0;
        var generator = new TemplateSuratGenerator(cfg, desaRepo, NullLogger<TemplateSuratGenerator>.Instance);

        var lengkap = new TemplateSuratKustom
        {
            Nama = "Surat Keterangan Usaha Baru",
            Deskripsi = "Uji galeri template surat",
            PakaiKop = true,
            Judul = "Surat Keterangan Usaha",
            SubJudul = "Nomor: 471/001/Ds/2026",
            PakaiNomor = true,
            AwalanNomor = "471",
            PakaiTempatTanggal = false,
            PakaiGrid = true,
            PakaiTandaTangan = true,
            PakaiTeksKaki = true,
            TeksKaki = "Tembusan disampaikan kepada Camat Tempuran.",
            Blok = new List<BlokTeksTemplateSurat>
            {
                new() { Isi = "Yang bertanda tangan di bawah ini menerangkan bahwa:", Rata = RataBlokTemplate.Kiri }
            },
            Kolom = new List<KolomTemplateSurat>
            {
                new() { Kunci = "nik", Label = "NIK", Tipe = TipeKolomTemplate.Nik },
                new() { Kunci = "nama", Label = "Nama", Tipe = TipeKolomTemplate.Teks },
                new() { Kunci = "tanggal_lahir", Label = "Tanggal Lahir", Tipe = TipeKolomTemplate.Tanggal },
                new() { Kunci = "alamat", Label = "Alamat", Tipe = TipeKolomTemplate.Paragraf },
                new() { Kunci = "jenis_usaha", Label = "Jenis Usaha", Tipe = TipeKolomTemplate.Pilihan, Pilihan = new List<string> { "Warung", "Bengkel" } }
            }
        };

        var nilaiLengkap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["nik"] = "3215012345678901",
            ["nama"] = "Budi Santoso",
            ["tanggal_lahir"] = "17-08-1990",
            ["alamat"] = "Jl. Raya Sumberjaya No. 12",
            ["jenis_usaha"] = "Warung"
        };

        var minimal = new TemplateSuratKustom
        {
            Nama = "Surat Pengantar Sederhana",
            PakaiKop = false,
            Judul = "Surat Pengantar",
            PakaiNomor = true,
            AwalanNomor = "471.2",
            PakaiGrid = false,
            PakaiTandaTangan = false,
            PakaiTeksKaki = false,
            PakaiTempatTanggal = true,
            Blok = new List<BlokTeksTemplateSurat>(),
            Kolom = new List<KolomTemplateSurat>
            {
                new() { Kunci = "nama", Label = "Nama", Tipe = TipeKolomTemplate.Teks }
            }
        };

        var kasus = new (string Nama, TemplateSuratKustom Template, Dictionary<string, string> Nilai, string Nomor)[]
        {
            ("TEMPLATE_SURAT", lengkap, nilaiLengkap, "471/001/Ds/2026"),
            ("TEMPLATE_SURAT_MINIMAL", minimal, new Dictionary<string, string> { ["nama"] = "Siti Aminah" }, "471.2/003/Ds/2026")
        };

        foreach (var (nama, template, nilai, nomor) in kasus)
        {
            try
            {
                var pdfPath = Path.Combine(outDir, $"{nama}.pdf");
                var pdf = await generator.BuatPdfAsync(template, nilai, nomor, new DateTime(2026, 9, 19));
                await File.WriteAllBytesAsync(pdfPath, pdf);

                int pages;
                using (var doc = PdfDocument.Load(pdfPath))
                {
                    pages = doc.PageCount;
                    for (int p = 0; p < Math.Min(2, pages); p++)
                    {
                        using var img = doc.Render(p, 115f, 115f, false);
                        img.Save(Path.Combine(outDir, $"{nama}_p{p + 1}.png"), ImageFormat.Png);
                    }
                }

                var masalah = new List<string>();
                using (var doc = PdfDocument.Load(pdfPath))
                {
                    string teks = doc.GetPdfText(0) ?? string.Empty;

                    if (pages != 1) masalah.Add($"{pages} halaman (harusnya 1)");
                    if (teks.Length == 0) masalah.Add("teks PDF kosong");

                    if (!teks.Contains(template.Judul.ToUpperInvariant(), StringComparison.Ordinal))
                        masalah.Add("judul surat tidak tercetak");
                    if (template.PakaiNomor && !teks.Contains(nomor, StringComparison.Ordinal))
                        masalah.Add("nomor surat tidak tercetak");

                    if (nama == "TEMPLATE_SURAT")
                    {
                        foreach (var wajib in new[]
                        {
                            "NIK", "3215012345678901", "Budi Santoso", "17 Agustus 1990",
                            "Jl. Raya Sumberjaya No. 12", "Warung", "YANG BERTANDA TANGAN",
                            "KEPALA DESA SUMBERJAYA", "Tembusan disampaikan kepada Camat Tempuran"
                        })
                        {
                            if (!teks.Contains(wajib, StringComparison.OrdinalIgnoreCase))
                                masalah.Add($"'{wajib}' tidak tercetak");
                        }

                        // Kop surat desa hanya boleh ada bila PakaiKop true.
                        if (!teks.Contains("PEMERINTAH KABUPATEN", StringComparison.OrdinalIgnoreCase))
                            masalah.Add("kop surat tidak tercetak");
                    }
                    else
                    {
                        if (teks.Contains("KEPALA DESA", StringComparison.OrdinalIgnoreCase))
                            masalah.Add("tanda tangan tercetak padahal tidak dipilih");
                        if (teks.Contains("PEMERINTAH KABUPATEN", StringComparison.OrdinalIgnoreCase))
                            masalah.Add("kop surat tercetak padahal tidak dipilih");
                        if (!teks.Contains("Siti Aminah", StringComparison.OrdinalIgnoreCase))
                            masalah.Add("kolom isian tidak tercetak");
                        if (!teks.Contains("Sumberjaya, 19 September 2026", StringComparison.OrdinalIgnoreCase))
                            masalah.Add("baris tempat & tanggal tidak tercetak");
                    }
                }

                if (masalah.Count > 0)
                {
                    fails++;
                    hasil.Add((nama, pages, string.Join("; ", masalah)));
                    Console.WriteLine($"FAIL {nama}: {string.Join("; ", masalah)}");
                }
                else
                {
                    hasil.Add((nama, pages, ""));
                    Console.WriteLine($"OK   {nama} ({pages} hal.; template surat buatan pengguna)");
                }
            }
            catch (Exception ex)
            {
                fails++;
                hasil.Add((nama, 0, $"{ex.GetType().Name}: {ex.Message}"));
                Console.WriteLine($"FAIL {nama}: {ex}");
            }
        }

        // === Contoh bawaan siap pakai: setiap contoh harus benar-benar bisa dicetak ===
        foreach (var contoh in TemplateSuratBawaan.Daftar)
        {
            string nama = "CONTOH_" + contoh.Kode.ToUpperInvariant().Replace('-', '_');
            try
            {
                var template = contoh.Salinan();
                var nilai = TemplateSuratNilai.NilaiContoh(template);
                int urut;
                string nomor = template.NomorBerikutnya(new DateTime(2026, 9, 19), out urut);

                var pdfPath = Path.Combine(outDir, $"{nama}.pdf");
                var pdf = await generator.BuatPdfAsync(template, nilai, nomor, new DateTime(2026, 9, 19));
                await File.WriteAllBytesAsync(pdfPath, pdf);

                int pages;
                using (var doc = PdfDocument.Load(pdfPath))
                {
                    pages = doc.PageCount;
                    using var img = doc.Render(0, 115f, 115f, false);
                    img.Save(Path.Combine(outDir, $"{nama}_p1.png"), ImageFormat.Png);
                }

                var masalah = new List<string>();
                using (var doc = PdfDocument.Load(pdfPath))
                {
                    string teks = doc.GetPdfText(0) ?? string.Empty;

                    if (pages != 1) masalah.Add($"{pages} halaman (harusnya 1)");
                    if (teks.Length == 0) masalah.Add("teks PDF kosong");
                    if (!teks.Contains(template.Judul.ToUpperInvariant(), StringComparison.Ordinal))
                        masalah.Add($"judul '{template.Judul}' tidak tercetak");
                    if (!teks.Contains(nomor, StringComparison.Ordinal))
                        masalah.Add($"nomor '{nomor}' tidak tercetak");
                    if (!teks.Contains("PEMERINTAH KABUPATEN", StringComparison.OrdinalIgnoreCase))
                        masalah.Add("kop surat tidak tercetak");
                    if (!teks.Contains("KEPALA DESA", StringComparison.OrdinalIgnoreCase))
                        masalah.Add("tanda tangan tidak tercetak");

                    // Blok teks & isi kolom bisa terpotong baris, jadi diperiksa
                    // lewat awalannya saja.
                    foreach (var blok in template.Blok)
                    {
                        if (!teks.Contains(Petikan(blok.Isi), StringComparison.OrdinalIgnoreCase))
                            masalah.Add($"blok teks '{Petikan(blok.Isi)}' tidak tercetak");
                    }

                    foreach (var kolom in template.Kolom)
                    {
                        if (!teks.Contains(kolom.Label, StringComparison.OrdinalIgnoreCase))
                            masalah.Add($"label kolom '{kolom.Label}' tidak tercetak");

                        string isi = TemplateSuratNilai.NilaiCetak(kolom, TemplateSuratNilai.Ambil(nilai, kolom.Kunci));
                        if (isi.Length > 0 && !teks.Contains(Petikan(isi), StringComparison.OrdinalIgnoreCase))
                            masalah.Add($"isi kolom '{kolom.Label}' tidak tercetak");
                    }

                    // Aturan tata letak surat: tanda tangan adalah elemen paling bawah.
                    // Kalimat penutup "Demikian …" harus berada DI ATAS tanda tangan, dan
                    // tidak boleh ada teks kaki yang tercetak setelahnya.
                    if (template.PakaiTeksKaki && !string.IsNullOrWhiteSpace(template.TeksKaki))
                        masalah.Add("contoh bawaan tidak boleh memakai teks kaki (akan tercetak setelah tanda tangan)");

                    int posisiTtd = teks.IndexOf("KEPALA DESA", StringComparison.OrdinalIgnoreCase);
                    int posisiPenutup = teks.LastIndexOf("Demikian", StringComparison.OrdinalIgnoreCase);
                    if (posisiPenutup < 0)
                        masalah.Add("kalimat penutup 'Demikian …' tidak tercetak");
                    else if (posisiTtd >= 0 && posisiPenutup > posisiTtd)
                        masalah.Add("kalimat penutup 'Demikian …' tercetak setelah tanda tangan");
                }

                if (masalah.Count > 0)
                {
                    fails++;
                    hasil.Add((nama, pages, string.Join("; ", masalah)));
                    Console.WriteLine($"FAIL {nama}: {string.Join("; ", masalah)}");
                }
                else
                {
                    hasil.Add((nama, pages, ""));
                    Console.WriteLine($"OK   {nama} ({pages} hal.; contoh siap pakai — {contoh.Nama})");
                }
            }
            catch (Exception ex)
            {
                fails++;
                hasil.Add((nama, 0, $"{ex.GetType().Name}: {ex.Message}"));
                Console.WriteLine($"FAIL {nama}: {ex}");
            }
        }

        // === Cetak ulang dari Register Surat ===
        // Surat yang sudah terbit bisa dibuka & dicetak ulang dari daftarnya: PDF
        // disusun ulang dari payload surat (salinan definisi template + isiannya),
        // bukan dari template hidup — sehingga hasilnya tetap sama walau templatenya
        // sudah disunting setelah surat terbit.
        {
            const string nama = "CETAK_ULANG_REGISTER";
            string? tempSemula = cfg.TempPdfFolder;

            try
            {
                cfg.TempPdfFolder = Path.Combine(outDir, "temp-register");

                var payload = new TemplateSuratTercatat
                {
                    TemplateId = 7,
                    NamaTemplate = lengkap.Nama,
                    JudulSurat = lengkap.Judul,
                    NomorSurat = "471/001/Ds/2026",
                    TanggalSurat = new DateTime(2026, 9, 19),
                    NamaPejabat = "Kades Uji",
                    Template = lengkap,
                    Nilai = new Dictionary<string, string>(nilaiLengkap, StringComparer.OrdinalIgnoreCase)
                };

                var suratRegister = new SuratData
                {
                    ID_Surat = 91,
                    NamaJenis = SuratConstants.TEMPLATE_SURAT,
                    NomorSurat = payload.NomorSurat,
                    TanggalSurat = payload.TanggalSurat,
                    Keperluan = payload.KeperluanTampil,
                    Keterangan = payload.RingkasanIsian,
                    AdditionalData = payload.ToJson()
                };

                var provider = new GeneratorSuratTemplateProvider(generator);

                string? pdfPath = await SuDesApp.Wpf.Services.SuratPdfHelper.GeneratePdfAsync(
                    provider, cfg, suratRegister, NullLogger.Instance, SuratConstants.TEMPLATE_SURAT);

                var masalah = new List<string>();

                if (pdfPath == null || !File.Exists(pdfPath))
                {
                    masalah.Add("PDF cetak ulang tidak terbentuk");
                }
                else
                {
                    int pages;
                    using (var doc = PdfDocument.Load(pdfPath))
                    {
                        pages = doc.PageCount;
                        string teks = doc.GetPdfText(0) ?? string.Empty;

                        if (pages != 1) masalah.Add($"{pages} halaman (harusnya 1)");
                        if (!teks.Contains("SURAT KETERANGAN USAHA", StringComparison.Ordinal))
                            masalah.Add("judul surat tidak tercetak dari payload");
                        if (!teks.Contains("471/001/Ds/2026", StringComparison.Ordinal))
                            masalah.Add("nomor surat tidak tercetak dari payload");
                        if (!teks.Contains("Budi Santoso", StringComparison.OrdinalIgnoreCase))
                            masalah.Add("isian surat tidak tercetak dari payload");
                        if (!teks.Contains("3215012345678901", StringComparison.Ordinal))
                            masalah.Add("NIK isian tidak tercetak dari payload");
                        if (!teks.Contains("KEPALA DESA SUMBERJAYA", StringComparison.OrdinalIgnoreCase))
                            masalah.Add("tanda tangan tidak tercetak dari payload");
                    }

                    // Template hidup diubah setelah surat terbit: cetak ulang harus tetap
                    // memakai definisi surat yang tersimpan di payload.
                    string judulAsli = lengkap.Judul;
                    lengkap.Judul = "Judul Sudah Diubah Setelah Terbit";
                    try
                    {
                        string? pdfPath2 = await SuDesApp.Wpf.Services.SuratPdfHelper.GeneratePdfAsync(
                            provider, cfg, suratRegister, NullLogger.Instance, SuratConstants.TEMPLATE_SURAT);

                        if (pdfPath2 == null || !File.Exists(pdfPath2))
                        {
                            masalah.Add("cetak ulang sesudah template disunting gagal");
                        }
                        else
                        {
                            using var doc2 = PdfDocument.Load(pdfPath2);
                            string teks2 = doc2.GetPdfText(0) ?? string.Empty;
                            if (!teks2.Contains(judulAsli.ToUpperInvariant(), StringComparison.Ordinal))
                                masalah.Add("cetak ulang kehilangan judul asli (payload tidak dipakai)");
                            if (teks2.Contains("DIUBAH SETELAH TERBIT", StringComparison.OrdinalIgnoreCase))
                                masalah.Add("cetak ulang memakai template hidup, bukan payload surat");
                        }
                    }
                    finally
                    {
                        lengkap.Judul = judulAsli;
                    }
                }

                if (masalah.Count > 0)
                {
                    fails++;
                    hasil.Add((nama, 0, string.Join("; ", masalah)));
                    Console.WriteLine($"FAIL {nama}: {string.Join("; ", masalah)}");
                }
                else
                {
                    hasil.Add((nama, 1, ""));
                    Console.WriteLine($"OK   {nama} (1 hal.; cetak ulang dari Register Surat memakai payload surat)");
                }
            }
            catch (Exception ex)
            {
                fails++;
                hasil.Add((nama, 0, $"{ex.GetType().Name}: {ex.Message}"));
                Console.WriteLine($"FAIL {nama}: {ex}");
            }
            finally
            {
                cfg.TempPdfFolder = tempSemula;
            }
        }

        // === Buku Register Surat (halaman cetak daftar surat) ===
        // Surat dari Template Surat tidak memakai data kependudukan: buku register harus
        // menampilkan nama & alamat dari isi suratnya sendiri, bukan nama baris warga
        // penanda milik aplikasi ("INSTANSI DUMMY").
        {
            const string nama = "BUKU_REGISTER_SURAT";
            var masalah = new List<string>();

            try
            {
                var payloadRegister = new TemplateSuratTercatat
                {
                    TemplateId = 7,
                    NamaTemplate = lengkap.Nama,
                    JudulSurat = lengkap.Judul,
                    NomorSurat = "471/002/Ds/2026",
                    TanggalSurat = new DateTime(2026, 9, 19),
                    Template = lengkap,
                    Nilai = new Dictionary<string, string>(nilaiLengkap, StringComparer.OrdinalIgnoreCase)
                };

                // Baris surat template: kolom warga-nya hanya baris penanda aplikasi.
                var barisTemplate = new SuratData
                {
                    ID_Surat = 92,
                    NamaJenis = SuratConstants.TEMPLATE_SURAT,
                    NomorSurat = payloadRegister.NomorSurat,
                    TanggalSurat = payloadRegister.TanggalSurat,
                    Keperluan = payloadRegister.KeperluanTampil,
                    Keterangan = payloadRegister.RingkasanIsian,
                    AdditionalData = payloadRegister.ToJson(),
                    Warga = new WargaData
                    {
                        ID_Warga = 1, NIK = "9999999999999999", Nama = "INSTANSI DUMMY",
                        TempatLahir = "N/A", TanggalLahir = "1900-01-01", JenisKelamin = "N/A",
                        Dusun = "N/A", Desa = "N/A", Kecamatan = "N/A", Kabupaten = "N/A"
                    }
                };

                // Baris surat warga biasa: harus tetap tampil seperti semula.
                var barisWarga = new SuratData
                {
                    ID_Surat = 93,
                    NamaJenis = SuratConstants.SKD_UMUM,
                    NomorSurat = "470/007/Ds/2026",
                    TanggalSurat = new DateTime(2026, 9, 18),
                    Keperluan = "Persyaratan bank",
                    Warga = new WargaData
                    {
                        ID_Warga = 2, NIK = "3215012345678999", Nama = "Siti Aminah",
                        TempatLahir = "Karawang", TanggalLahir = "1985-03-02", JenisKelamin = "Perempuan",
                        Dusun = "Dusun Satu", Desa = "Sumberjaya", Kecamatan = "Tempuran", Kabupaten = "Karawang"
                    }
                };

                var generatorRegister = new SuratRegisterGenerator(
                    cfg,
                    new FileService(cfg, NullLogger<FileService>.Instance),
                    desaRepo,
                    new FakeSuratRepository(),
                    new SettingsManager(desaRepo, cfg),
                    NullLogger<SuratRegisterGenerator>.Instance,
                    NullLoggerFactory.Instance);
                string pdfRegister = Path.Combine(outDir, $"{nama}.pdf");

                using (var ms = new MemoryStream())
                {
                    await generatorRegister.GenerateRegisterPdfAsync(
                        ms, new List<SuratData> { barisTemplate, barisWarga });
                    await File.WriteAllBytesAsync(pdfRegister, ms.ToArray());
                }

                using var docRegister = PdfDocument.Load(pdfRegister);
                string teksRegister = docRegister.GetPdfText(0) ?? string.Empty;

                // Teks panjang terpotong baris di kolom sempit, jadi dibandingkan tanpa
                // spasi/baris baru.
                static string Rapat(string? teks) =>
                    new string((teks ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray());

                string isi = Rapat(teksRegister);

                if (!isi.Contains(Rapat("Budi Santoso"), StringComparison.OrdinalIgnoreCase))
                    masalah.Add("nama penerima surat template tidak tercetak di buku register");
                if (!isi.Contains(Rapat("Jl. Raya Sumberjaya No. 12"), StringComparison.OrdinalIgnoreCase))
                    masalah.Add("alamat penerima surat template tidak tercetak di buku register");
                if (!isi.Contains(Rapat($"Template: {lengkap.Nama}"), StringComparison.OrdinalIgnoreCase))
                    masalah.Add("kolom jenis surat tidak menyebut nama templatenya");
                if (isi.Contains(Rapat("INSTANSI DUMMY"), StringComparison.OrdinalIgnoreCase))
                    masalah.Add("buku register memakai nama baris warga penanda, bukan isi surat");
                if (!isi.Contains(Rapat("Siti Aminah"), StringComparison.OrdinalIgnoreCase))
                    masalah.Add("surat warga biasa hilang dari buku register");

                if (masalah.Count > 0)
                {
                    fails++;
                    hasil.Add((nama, 0, string.Join("; ", masalah)));
                    Console.WriteLine($"FAIL {nama}: {string.Join("; ", masalah)}");
                }
                else
                {
                    hasil.Add((nama, 1, ""));
                    Console.WriteLine($"OK   {nama} (1 hal.; identitas surat template dibaca dari isi suratnya)"             );
                }
            }
            catch (Exception ex)
            {
                fails++;
                hasil.Add((nama, 0, $"{ex.GetType().Name}: {ex.Message}"));
                Console.WriteLine($"FAIL {nama}: {ex}");
            }
        }

        return fails;
    }

    /// <summary>
    /// Penyedia layanan minimal untuk SuratPdfHelper pada uji cetak ulang: hanya
    /// generator Template Surat yang tersedia.
    /// </summary>
    private sealed class GeneratorSuratTemplateProvider : IServiceProvider
    {
        private readonly TemplateSuratGenerator _generator;

        public GeneratorSuratTemplateProvider(TemplateSuratGenerator generator) => _generator = generator;

        public object? GetService(Type serviceType) =>
            serviceType == typeof(TemplateSuratGenerator) ? _generator : null;
    }

    /// <summary>Awal teks yang aman dicari di PDF (teks panjang terpotong baris).</summary>
    private static string Petikan(string teks)
    {
        string bersih = (teks ?? string.Empty).Trim().Replace("\r", " ").Replace("\n", " ");
        return bersih.Length <= 24 ? bersih : bersih.Substring(0, 24);
    }

    private static object NullLoggerOf(Type t) =>
        typeof(Program).GetMethod(nameof(NullLoggerOfGeneric), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(t).Invoke(null, null);

    private static object NullLoggerOfGeneric<T>() => NullLogger<T>.Instance;

    /// <summary>
    /// Naik dari folder output sampai menemukan root repo: harus berisi appsettings.json
    /// DAN folder proyek SuDesApp.Wpf (folder output juga ikut menerima salinan
    /// appsettings.json, jadi penanda folder proyek wajib agar tidak salah root).
    /// </summary>
    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "appsettings.json"))
                && Directory.Exists(Path.Combine(dir.FullName, "SuDesApp.Wpf")))
                return dir.FullName;
        return AppContext.BaseDirectory;
    }

    private static SuratData Base(string jenis) => new SuratData
    {
        ID_Surat = 1,
        ID_Jenis = 1,
        NamaJenis = jenis,
        NomorSurat = "470/001/Ds/2026",
        TanggalSurat = new DateTime(2026, 9, 17),
        Warga = new WargaData
        {
            Nama = "Warga Uji",
            NIK = "3201234567890001",
            TempatLahir = "Uji",
            TanggalLahir = "1990-01-01",
            JenisKelamin = "Laki-laki",
            Agama = "Islam",
            StatusPerkawinan = "Belum Kawin",
            Pekerjaan = "Petani",
            Pendidikan = "SLTA",
            Kewarganegaraan = "Indonesia",
            Dusun = "Dusun Uji",
            Desa = "Sumberjaya",
            Kecamatan = "Tempuran",
            Kabupaten = "Karawang"
        },
        Desa = FakeDesaRepository.BuatDesa(),
        PejabatPenandatangan = "Kepala Desa",
        NamaPejabatPenandatangan = "Kades Uji",
        Keterangan = "Surat ini dibuat untuk keperluan pengujian tata letak PDF pada aplikasi SuDesApp."
    };

    /// <summary>
    /// Kasus uji Daftar Hadir: kolom default dengan judul multibaris, kombinasi
    /// kolom tanpa Jabatan + tanpa footer Kepala Desa, kolom lengkap (termasuk
    /// NIK/No. Hp), dan daftar panjang untuk menguji perpindahan halaman.
    /// </summary>
    private static List<(string Suffix, DaftarHadirData Data)> BuatKasusDaftarHadir()
    {
        var desa = FakeDesaRepository.BuatDesa();

        // Desa dengan kata pengulang di dalam nilainya ("Desa Sumberjaya"):
        // membuktikan kop tetap tercetak sekali — bukan "PEMERINTAH DESA DESA SUMBERJAYA".
        var desaBerprefiks = new DesaData
        {
            NamaDesa = "Desa Sumberjaya",
            Kecamatan = "Kecamatan Tempuran",
            Kabupaten = "Kabupaten Karawang",
            Alamat = "Jl. Raya Sumberjaya No. 1",
            Kodepos = "41361",
            KepalaDesa = "H. Suryana",
            SekretarisDesa = "Dedi Mulyadi",
            NamaCamat = "Camat Tempuran"
        };

        static DaftarHadirPeserta Orang(string nama, string jabatan, string nip, string nik, string noHp, string alamat, string ket, string jenisKelamin = "L") => new()
        {
            Nama = nama,
            JenisKelamin = jenisKelamin,
            Jabatan = jabatan,
            Nip = nip,
            Nik = nik,
            NoHp = noHp,
            Alamat = alamat,
            Keterangan = ket
        };

        var peserta = new List<DaftarHadirPeserta>
        {
            Orang("Budi Santoso", "Kepala Dusun Krajan", "197001011990031001", "3201234567890001", "0812-1111-2222", "Dusun Krajan", "Hadir"),
            Orang("Siti Aminah", "Ketua Tim Penggerak PKK Desa", "198202022005012002", "3201234567890002", "0812-3333-4444", "Dusun Krajan", "Hadir", "P"),
            Orang("Ahmad Fauzi", "Ketua BPD", "197503032000031003", "3201234567890003", "0857-5555-6666", "Dusun Tegalsari", "Hadir"),
            Orang("Dewi Lestari", "Bidan Desa", "198804042010012004", "3201234567890004", "0813-7777-8888", "Dusun Tegalsari", "Hadir", "P"),
            Orang("Rudi Hartono", "Ketua Karang Taruna", "-", "3201234567890005", "0821-9999-0000", "Dusun Sumber", "Hadir"),
            Orang("Sulastri", "Kader Posyandu", "-", "3201234567890006", "-", "Dusun Sumber", "Izin", "P"),
            Orang("Joko Widodo", "Tokoh Masyarakat", "-", "3201234567890007", "0838-1234-5678", "Dusun Krajan", "Hadir"),
            Orang("Ratna Sari", "Guru Desa", "199005052015012005", "3201234567890008", "0851-2345-6789", "Dusun Sumber", "Hadir", "P")
        };

        var banyak = new List<DaftarHadirPeserta>(peserta);
        for (int i = peserta.Count + 1; i <= 24; i++)
        {
            banyak.Add(Orang($"Peserta Uji {i}", "Anggota", $"1990{i:00}01", $"320123456789{i:00}0", $"0812-0000-{i:00}00", "Dusun Uji", "Hadir", i % 2 == 0 ? "L" : "P"));
        }

        return new List<(string, DaftarHadirData)>
        {
            // Kolom default + judul dua baris (hasil tombol Enter) + Pukul/Tempat.
            ("", new DaftarHadirData
            {
                Judul = "DAFTAR HADIR\nRAPAT MUSYAWARAH DESA",
                HariTanggal = "Jumat, 18 September 2026",
                Pukul = "09.00 WIB s/d Selesai",
                Tempat = "Aula Kantor Desa Sumberjaya",
                Peserta = peserta,
                Desa = desa
            }),
            // Hari/Tanggal diganti manual (mis. untuk kegiatan yang tanggalnya berbeda)
            // — baris kota di atas tanda tangan ikut memakai tanggal itu.
            ("_TANGGAL_EDIT", new DaftarHadirData
            {
                Judul = "DAFTAR HADIR RAPAT EVALUASI",
                HariTanggal = "Sabtu, 3 Oktober 2026",
                Pukul = "10.00 WIB s/d Selesai",
                Tempat = "Aula Kantor Desa Sumberjaya",
                Peserta = peserta,
                Desa = desa
            }),
            // Kombinasi checkbox ala pengguna: No. & Jabatan dimatikan, Pukul dikosongkan,
            // dan footer tanpa tanda tangan Kepala Desa.
            ("_MINIMAL", new DaftarHadirData
            {
                Judul = "DAFTAR HADIR POSYANDU",
                HariTanggal = string.Empty,
                Pukul = string.Empty,
                Tempat = "Balai Desa Sumberjaya",
                TampilkanNo = false,
                TampilkanNama = true,
                TampilkanJabatan = false,
                TampilkanTandaTangan = true,
                TampilkanFooterKepalaDesa = false,
                Peserta = peserta,
                Desa = desaBerprefiks
            }),
            // Semua kolom opsional dinyalakan, termasuk NIK dan No. Hp.
            ("_LENGKAP", new DaftarHadirData
            {
                Judul = "DAFTAR HADIR BIMBINGAN TEKNIS APARATUR DESA",
                HariTanggal = "Jumat, 18 September 2026",
                Pukul = "08.30 WIB",
                Tempat = "Aula Kantor Desa Sumberjaya",
                TampilkanNip = true,
                TampilkanNik = true,
                TampilkanNoHp = true,
                TampilkanJenisKelamin = true,
                TampilkanAlamat = true,
                TampilkanKeterangan = true,
                Peserta = peserta,
                Desa = desa
            }),
            // Teks panjang yang tidak cukup satu baris harus turun ke baris kedua,
            // bukan terpotong — diuji dengan nama/jabatan/alamat yang panjang.
            ("_PANJANG", new DaftarHadirData
            {
                Judul = "DAFTAR HADIR RAPAT KOORDINASI",
                HariTanggal = "Jumat, 18 September 2026",
                Pukul = "13.30 WIB s/d Selesai",
                Tempat = "Pendopo Kantor Desa Sumberjaya",
                TampilkanAlamat = true,
                Peserta = new List<DaftarHadirPeserta>
                {
                    Orang("Muhammad Rizky Ramadhan Putra", "Koordinator Bidang Pembangunan dan Pemberdayaan Masyarakat Desa", "198503152010011005", "3201234567890009", "0812-3456-7890", "Dusun Krajan RT 02 RW 03 Desa Uji Kecamatan Uji Kabupaten Uji", "Hadir"),
                    Orang("Siti Nurhaliza", "Sekretaris", "-", "3201234567890010", "-", "Dusun Tegalsari RT 01 RW 02", "Hadir", "P"),
                    Orang("Ahmad", "Anggota", "-", "3201234567890011", "0857-1111-2222", "Dusun Sumber", "Hadir")
                },
                Desa = desa
            }),
            // 24 peserta → uji perpindahan halaman + kop yang berulang.
            ("_BANYAK", new DaftarHadirData
            {
                Judul = "DAFTAR HADIR\nMUSYAWARAH PERENCANAAN PEMBANGUNAN DESA",
                HariTanggal = "Kamis, 17 September 2026",
                Pukul = "13.00 WIB",
                Tempat = "Aula Kantor Desa Sumberjaya",
                Peserta = banyak,
                Desa = desa,
                TanggalCetak = new DateTime(2026, 9, 17)
            })
        };
    }

    private static List<(string Name, SuratData Surat)> BuatKasus()
    {
        var list = new List<(string, SuratData)>
        {
            ("SKD_UMUM", Base("SKD_UMUM")),
            ("DOMISILI_WARGA", Base("DOMISILI_WARGA")),
            ("PENGANTAR_SKCK", Base("PENGANTAR_SKCK")),
            ("SKTM", Base("SKTM")),
        };

        var instansi = Base("DOMISILI_INSTANSI");
        instansi.Instansi = new Instansi { NamaInstansi = "SDN Uji", AlamatInstansi = "Jl. Instansi No. 2" };
        list.Add(("DOMISILI_INSTANSI", instansi));

        var kematian = Base("KEMATIAN");
        kematian.Kematian = new KematianData
        {
            HariKematian = "Kamis",
            TanggalKematian = "2026-09-10",
            PukulKematian = "09.30",
            PenyebabKematian = "Sakit",
            NIKPelapor = "3201234567890002",
            NamaPelapor = "Pelapor Uji",
            AgamaPelapor = "Islam",
            UmurPelapor = "1965-05-05",
            PekerjaanPelapor = "Petani",
            AlamatPelapor = "Dusun Uji Desa Uji Kecamatan Uji",
            HubunganPelapor = "Anak kandung"
        };
        list.Add(("KEMATIAN", kematian));

        var ahliWaris = Base("AHLI_WARIS");
        ahliWaris.AhliWarisData = new AhliWarisData
        {
            NamaPasangan = "Pasangan Uji",
            JenisKelaminPasangan = "Perempuan",
            TempatLahirPasangan = "Uji",
            TanggalLahirPasangan = new DateTime(1962, 3, 3),
            AlamatPasangan = "Dusun Uji, Kecamatan Uji, Kabupaten Uji",
            Anak = new List<AnakAhliWarisData>
            {
                new() { Nama = "Anak Satu", TempatLahir = "Uji", TanggalLahir = new DateTime(1985, 7, 7), JenisKelamin = "Laki-laki", Alamat = "Dusun Uji, Kecamatan Uji, Kabupaten Uji" },
                new() { Nama = "Anak Dua", TempatLahir = "Uji", TanggalLahir = new DateTime(1988, 9, 9), JenisKelamin = "Perempuan", Alamat = "Dusun Uji, Kecamatan Uji, Kabupaten Uji" }
            }
        };
        list.Add(("AHLI_WARIS", ahliWaris));

        var garapan = Base("GARAPAN_SAWAH");
        garapan.RincianGarapans = new List<GarapanData>
        {
            new() { PemilikTanah = "Warga Uji", Lokasi = "Blok A Desa Uji", NomorPersil = "123", Luas = 1500, KeteranganGarapan = "Sawah irigasi" },
            new() { PemilikTanah = "Warga Uji", Lokasi = "Blok B Desa Uji", NomorPersil = "124", Luas = 750, KeteranganGarapan = "Sawah tadah hujan" }
        };
        list.Add(("GARAPAN_SAWAH", garapan));

        var izinOrtu = Base("IZIN_ORTU");
        izinOrtu.IzinOrtu = new IzinOrtuData
        {
            ID_Anak = 1,
            NamaAnak = "Anak Uji",
            NIKAnak = "3201234567890004",
            TempatLahirAnak = "Uji",
            TanggalLahirAnak = "1995-06-15",
            JenisKelaminAnak = "Perempuan",
            AgamaAnak = "Islam",
            StatusPerkawinanAnak = "Belum Kawin",
            AlamatAnak = "Dusun Uji Desa Uji",
            PekerjaanAnak = "Mahasiswa",
            NegaraTujuan = "Malaysia",
            NamaPT = "Universitas Uji Malaysia"
        };
        izinOrtu.Jenis = SuratData.JenisSuratEnum.IzinOrtu;
        list.Add(("IZIN_ORTU", izinOrtu));

        var kenalLahir = Base("KENAL_LAHIR");
        kenalLahir.KenalLahir = new KenalLahirData
        {
            Ayah = new WargaData { Nama = "Ayah Uji", NIK = "3201234567890005" },
            Ibu = new WargaData { Nama = "Ibu Uji", NIK = "3201234567890006" },
            NamaAnak = "Bayi Uji",
            TanggalLahirAnak = new DateTime(2026, 8, 1),
            TempatLahirAnak = "Puskesmas Uji",
            AnakKe = 1,
            LahirDi = "Rumah Sakit"
        };
        list.Add(("KENAL_LAHIR", kenalLahir));

        var ijinTinggal = Base("IJIN_TINGGAL");
        ijinTinggal.DesaTujuan = "Desa Tujuan Uji";
        ijinTinggal.KecTujuan = "Kecamatan Tujuan";
        ijinTinggal.KabTujuan = "Kabupaten Tujuan";
        ijinTinggal.DusunTujuan = "Dusun Tujuan";
        list.Add(("IJIN_TINGGAL", ijinTinggal));

        var sku = Base("SKU");
        sku.SKU = new SKUData();
        list.Add(("SKU", sku));

        var bedaNama = Base("BEDANAMA");
        bedaNama.WargaKK = new WargaData
        {
            Nama = "Warga Uji",
            NIK = "3201234567890099",
            TempatLahir = "Uji",
            TanggalLahir = "1990-01-01",
            JenisKelamin = "Laki-laki",
            Dusun = "Dusun Uji",
            Desa = "Sumberjaya",
            Kecamatan = "Tempuran",
            Kabupaten = "Karawang"
        };
        bedaNama.DataSource1 = "KTP-el";
        bedaNama.DataSource2 = "Kartu Keluarga";
        list.Add(("BEDANAMA", bedaNama));

        // Seluruh blanko NTCR yang dibuat kantor desa (Model N1-N6) diuji dengan data
        // lengkap sesuai kolom yang dicetak masing-masing formulir. Model N7 sudah
        // dihapus dari aplikasi karena diterbitkan KUA, bukan kantor desa.
        foreach (var n in new[] { "NTCR_N1", "NTCR_N2", "NTCR_N3", "NTCR_N4", "NTCR_N5", "NTCR_N6" })
        {
            var d = Base(n);
            d.Ntcr = new NtcrData
            {
                NamaIstri = "Calon Istri Uji",
                NikIstri = "3201234567890003",
                TempatLahirIstri = "Uji",
                TanggalLahirIstri = "1992-02-02",
                KewarganegaraanIstri = "WNI",
                AgamaIstri = "Islam",
                StatusPerkawinanIstri = "Belum Kawin",
                PekerjaanIstri = "Ibu Rumah Tangga",
                AlamatIstri = "Dusun Uji, Desa Sumberjaya, Kecamatan Tempuran, Kabupaten Karawang",
                PihakDiterangkanN1 = "Suami",
                TujuanKua = "Tempuran",
                HariTanggalJamAkad = "Jumat, 12-10-2026 / 09.00 WIB",
                TempatAkad = "Kantor Urusan Agama Kecamatan Tempuran",
                TanggalPenetapanIsbat = "2026-03-04",
                PengadilanAgama = "Pengadilan Agama Karawang",
                LampiranTambahan = "Surat keterangan sehat\nAkta cerai",
                TanggalDiterima = "2026-09-18",
                PihakAnakIzinOrtu = "Suami",
                PihakMeninggal = "Suami",
                TanggalMeninggal = "2026-01-05",
                TempatMeninggal = "RSUD Karawang",
                AyahCalonSuami = new NtcrOrangTua
                {
                    Nama = "Ayah Pria Uji",
                    BinBinti = "Kakek Pria Uji",
                    Nik = "3201234567890005",
                    TempatLahir = "Karawang",
                    TanggalLahir = "1965-04-04",
                    Kewarganegaraan = "WNI",
                    Agama = "Islam",
                    Pekerjaan = "Petani",
                    Alamat = "Dusun Uji, Desa Sumberjaya, Kecamatan Tempuran"
                },
                IbuCalonSuami = new NtcrOrangTua
                {
                    Nama = "Ibu Pria Uji",
                    BinBinti = "Kakek Ibu Uji",
                    Nik = "3201234567890006",
                    TempatLahir = "Karawang",
                    TanggalLahir = "1968-06-06",
                    Kewarganegaraan = "WNI",
                    Agama = "Islam",
                    Pekerjaan = "Ibu Rumah Tangga",
                    Alamat = "Dusun Uji, Desa Sumberjaya, Kecamatan Tempuran"
                },
                AyahCalonIstri = new NtcrOrangTua
                {
                    Nama = "Ayah Istri Uji",
                    BinBinti = "Kakek Istri Uji",
                    Nik = "3201234567890007",
                    TempatLahir = "Karawang",
                    TanggalLahir = "1966-07-07",
                    Kewarganegaraan = "WNI",
                    Agama = "Islam",
                    Pekerjaan = "Buruh",
                    Alamat = "Dusun Uji, Desa Sumberjaya, Kecamatan Tempuran"
                },
                IbuCalonIstri = new NtcrOrangTua
                {
                    Nama = "Ibu Istri Uji",
                    BinBinti = "Kakek Ibu Istri Uji",
                    Nik = "3201234567890008",
                    TempatLahir = "Karawang",
                    TanggalLahir = "1969-08-08",
                    Kewarganegaraan = "WNI",
                    Agama = "Islam",
                    Pekerjaan = "Ibu Rumah Tangga",
                    Alamat = "Dusun Uji, Desa Sumberjaya, Kecamatan Tempuran"
                }
            };
            list.Add((n, d));
        }

        // N3 yang sama sekali tidak diisi: harus tetap tercetak seperti template
        // (baris titik-titik) dan tidak ditolak validasi.
        var n3Kosong = Base("NTCR_N3_KOSONG");
        n3Kosong.NamaJenis = "NTCR_N3";
        n3Kosong.Ntcr = new NtcrData();
        list.Add(("NTCR_N3_KOSONG", n3Kosong));

        // Surat keterangan numpang nikah (N8) — surat desa tersendiri: kop surat desa,
        // tanpa blok LAMPIRAN Kepdirjen, dan memuat tujuan numpang nikah.
        var numpangNikah = Base("NTCR_N8");
        numpangNikah.Ntcr = new NtcrData
        {
            NamaIstri = "Calon Istri Uji",
            TempatLahirIstri = "Karawang",
            TanggalLahirIstri = "1992-02-02",
            AlamatIstri = "Dusun Uji, Desa Mekarsari",
            KecamatanIstri = "Pakis Jaya",
            KabupatenIstri = "Karawang",
            DesaNumpang = "Desa Mekarsari",
            KecamatanNumpang = "Pakis Jaya",
            KabupatenNumpang = "Karawang"
        };
        list.Add(("NTCR_N8", numpangNikah));

        // === Kasus gelar: hanya NAMA yang dikapitalkan, gelar dibiarkan apa adanya ===
        var skuGelar = Base("SKU");
        skuGelar.SKU = new SKUData();
        skuGelar.Warga!.Nama = "Dr. H. Ahmad Suryana, S.H., M.H.";
        skuGelar.NamaPejabatPenandatangan = "Drs. H. Suryana Kusuma, M.M.";
        list.Add(("GELAR_SKU", skuGelar));

        var kematianGelar = Base("KEMATIAN");
        kematianGelar.Warga!.Nama = "Hj. Siti Aminah, S.Pd.";
        kematianGelar.Kematian = new KematianData
        {
            HariKematian = "Kamis",
            TanggalKematian = "2026-09-10",
            PukulKematian = "09.30",
            PenyebabKematian = "Sakit",
            NIKPelapor = "3201234567890002",
            NamaPelapor = "Ir. Bambang Wijaya, M.T.",
            AgamaPelapor = "Islam",
            UmurPelapor = "1965-05-05",
            PekerjaanPelapor = "Petani",
            AlamatPelapor = "Dusun Uji Desa Uji Kecamatan Uji",
            HubunganPelapor = "Anak kandung"
        };
        list.Add(("GELAR_KEMATIAN", kematianGelar));

        var kenalLahirGelar = Base("KENAL_LAHIR");
        kenalLahirGelar.KenalLahir = new KenalLahirData
        {
            Ayah = new WargaData { Nama = "dr. Agus Salim, Sp.PD.", NIK = "3201234567890005" },
            Ibu = new WargaData { Nama = "Siti Rahayu, S.Sos.", NIK = "3201234567890006" },
            NamaAnak = "Muhammad Rizky Ramadhan",
            TanggalLahirAnak = new DateTime(2026, 8, 1),
            TempatLahirAnak = "Puskesmas Uji",
            AnakKe = 1,
            LahirDi = "Rumah Sakit"
        };
        list.Add(("GELAR_KENAL_LAHIR", kenalLahirGelar));

        return list;
    }

    /// <summary>
    /// Halaman pratinjau paket NTCR: seluruh halaman berkas GABUNGAN disematkan sebagai
    /// data URI, sehingga tetap tampil walau dibuka tanpa akses ke berkas gambar lain.
    /// </summary>
    private static void TulisHtmlPaketNtcr(
        string outDir,
        IReadOnlyList<SuratData> blanko,
        int jumlahHalaman)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang=\"id\"><head><meta charset=\"utf-8\">");
        sb.AppendLine("<title>Paket NTCR — satu berkas PDF</title>");
        sb.AppendLine("<style>body{background:#20242c;color:#e8eaf0;font-family:Segoe UI,sans-serif;margin:16px}" +
                      "h1{font-size:18px}h2{font-size:14px;color:#9fd0ff;margin:18px 0 6px}" +
                      ".row{display:flex;gap:12px;margin-bottom:18px;align-items:flex-start}" +
                      ".row img{width:440px;border-radius:6px;box-shadow:0 2px 10px rgba(0,0,0,.5);background:#fff}" +
                      ".label{width:120px;font-size:13px;font-weight:600;padding-top:6px}" +
                      ".note{font-size:12px;color:#a9b1c1;max-width:900px;line-height:1.5}</style></head><body>");
        sb.AppendLine("<h1>Paket pernikahan NTCR (N1–N6) — satu berkas PDF (satu blanko per halaman)</h1>");
        sb.AppendLine("<p class=\"note\">Alur paket: sekali isi data satu pasangan, seluruh blanko N1–N6 yang dipilih " +
                      "disimpan sekaligus ke Register NTCR lalu dicetak menjadi <strong>satu</strong> berkas PDF gabungan urut N1 → N6.</p>");

        for (int i = 0; i < blanko.Count; i++)
        {
            string png = Path.Combine(outDir, $"NTCR_PAKET_p{i + 1}.png");
            if (!File.Exists(png)) continue;

            string dataUri = "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(png));
            string kode = NtcrKatalog.Kode(blanko[i].NamaJenis);

            sb.AppendLine($"<div class=\"row\"><div class=\"label\">Halaman {i + 1}<br>Model {kode}</div>" +
                          $"<img src=\"{dataUri}\"></div>");
        }

        sb.AppendLine($"<p class=\"note\">{jumlahHalaman} halaman dari {blanko.Count} blanko terpilih.</p>");

        // Pembanding: cetak satu blanko (alur lama) agar kerapatan tiap blanko
        // bisa dibandingkan dengan halamannya di berkas gabungan.
        sb.AppendLine("<h2>Cetak satu blanko (menu NTCR per blanko)</h2>");
        foreach (var surat in blanko)
        {
            string kode = NtcrKatalog.Kode(surat.NamaJenis);
            string png = Path.Combine(outDir, $"{surat.NamaJenis}_p1.png");
            if (!File.Exists(png)) continue;

            string dataUri = "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(png));
            sb.AppendLine($"<div class=\"row\"><div class=\"label\">{kode}</div><img src=\"{dataUri}\"></div>");
        }

        // Surat keterangan numpang nikah (N8): menu tersendiri di sidebar NTCR,
        // berbentuk surat desa biasa (kop desa, tanpa blok LAMPIRAN Kepdirjen).
        string pngN8 = Path.Combine(outDir, "NTCR_N8_p1.png");
        if (File.Exists(pngN8))
        {
            sb.AppendLine("<h2>Surat Numpang Nikah (N8) — menu sendiri di sidebar NTCR</h2>");
            sb.AppendLine("<p class=\"note\">Bukan blanko Kepdirjen: memakai kop surat desa, menerangkan bahwa warga " +
                          "berdomisili di desa ini akan melangsungkan akad nikah (numpang nikah) di wilayah lain, " +
                          "dan tidak ikut dalam berkas paket N1–N6.</p>");
            sb.AppendLine("<div class=\"row\"><div class=\"label\">N8</div>" +
                          $"<img src=\"data:image/png;base64,{Convert.ToBase64String(File.ReadAllBytes(pngN8))}\"></div>");
        }

        sb.AppendLine("</body></html>");

        File.WriteAllText(Path.Combine(outDir, "ntcr-paket.html"), sb.ToString());
        Console.WriteLine($"Pratinjau paket NTCR: {Path.Combine(outDir, "ntcr-paket.html")}");
    }

    private static void TulisIndexHtml(string outDir, List<(string Name, int Pages, string Error)> hasil)
    {
        var sb = new StringBuilder();
        // PNG ditanam sebagai data URI base64 agar galeri berupa SATU file mandiri —
        // bisa dibuka langsung tanpa server gambar terpisah.
        string B64(string file) =>
            File.Exists(file) ? Convert.ToBase64String(File.ReadAllBytes(file)) : null;

        sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'><title>Galeri PDF SuDesApp</title>");
        sb.AppendLine("<style>body{font-family:Segoe UI,sans-serif;background:#1e2430;color:#e8eaf0;margin:24px}");
        sb.AppendLine("h1{font-size:20px}h2{font-size:14px;color:#9fb3d1;margin:28px 0 8px}");
        sb.AppendLine("img{width:420px;margin:6px;border-radius:6px;box-shadow:0 2px 10px rgba(0,0,0,.5);background:#fff}");
        sb.AppendLine(".fail{color:#ff8a80}a{color:#82b1ff}</style></head><body>");
        sb.AppendLine("<h1>Galeri Uji Visual Generator Surat (QuestPDF)</h1>");
        sb.AppendLine("<p><a href='compare.html'>→ Mode Perbandingan: arsip lama (TempPDF) vs QuestPDF berdampingan</a></p>");

        foreach (var h in hasil)
        {
            sb.AppendLine($"<h2>{h.Name} {(h.Pages > 0 ? $"({h.Pages} hal.)" : "")}</h2>");
            if (h.Pages > 0)
            {
                for (int p = 1; p <= Math.Min(2, h.Pages); p++)
                {
                    var b64 = B64(Path.Combine(outDir, $"{h.Name}_p{p}.png"));
                    if (b64 != null)
                        sb.AppendLine($"<img src='data:image/png;base64,{b64}' alt='{h.Name} hal {p}'>");
                }
                sb.AppendLine($"<div><a href='{h.Name}.pdf'>Buka PDF {h.Name}</a></div>");
            }
            else
            {
                sb.AppendLine($"<div class='fail'>GAGAL: {System.Net.WebUtility.HtmlEncode(h.Error)}</div>");
            }
        }
        sb.AppendLine("</body></html>");
        File.WriteAllText(Path.Combine(outDir, "index.html"), sb.ToString());
    }

    // =====================================================================
    // Mode perbandingan: arsip lama dari folder TempPDF aplikasi
    // =====================================================================

    /// <summary>Cari semua folder TempPDF milik build aplikasi (Debug/Release).</summary>
    private static List<string> CariFolderArsip(string root)
    {
        var hasil = new List<string>();
        var binRoot = Path.Combine(root, "SuDesApp.Wpf", "bin");
        if (Directory.Exists(binRoot))
            foreach (var cfgDir in Directory.GetDirectories(binRoot))
                foreach (var tfm in Directory.GetDirectories(cfgDir))
                {
                    var tp = Path.Combine(tfm, "TempPDF");
                    if (Directory.Exists(tp)) hasil.Add(tp);
                }
        var fallback = Path.Combine(root, "TempPDF");
        if (Directory.Exists(fallback)) hasil.Add(fallback);
        return hasil;
    }

    /// <summary>
    /// Kelompokkan arsip PDF per jenis surat dengan pencocokan awalan terpanjang
    /// terhadap daftar jenis galeri (mis. "SKD_UMUM_470005Ds2026_7.pdf" → SKD_UMUM).
    /// File yang tidak cocok (mis. RegisterSurat_*) dikembalikan terpisah.
    /// </summary>
    private static (List<(string Jenis, int Idx, string File)> Matched, List<string> TanpaPasangan)
        KumpulkanArsip(IEnumerable<string> folders, IEnumerable<string> jenisKeys)
    {
        var keys = jenisKeys.OrderByDescending(k => k.Length).ToList();
        var matched = new List<(string, int, string)>();
        var tanpa = new List<string>();
        var files = folders.SelectMany(f => Directory.EnumerateFiles(f, "*.pdf"))
                           .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                           .ToList();
        int idx = 0;
        foreach (var f in files)
        {
            var nama = Path.GetFileNameWithoutExtension(f);
            var key = keys.FirstOrDefault(k => nama.Equals(k, StringComparison.OrdinalIgnoreCase)
                                            || nama.StartsWith(k + "_", StringComparison.OrdinalIgnoreCase));
            if (key == null) { tanpa.Add(Path.GetFileName(f)); continue; }
            idx++;
            matched.Add((key, idx, f));
        }
        return (matched, tanpa);
    }

    /// <summary>Rasterisasi halaman arsip lama ke PNG (maks. 2 halaman, DPI sama dengan galeri baru).
    /// Byte PDF disalin ke memori dulu supaya file asli di TempPDF tidak terkunci.</summary>
    private static List<(string Jenis, int Idx, string FileName, DateTime Modified, int Pages)>
        RenderArsip(string outDir, List<(string Jenis, int Idx, string File)> arsip)
    {
        var hasil = new List<(string, int, string, DateTime, int)>();
        foreach (var (jenis, idx, file) in arsip)
        {
            try
            {
                int pages;
                using (var ms = new MemoryStream(File.ReadAllBytes(file)))
                using (var doc = PdfDocument.Load(ms))
                {
                    pages = doc.PageCount;
                    for (int p = 0; p < Math.Min(2, pages); p++)
                    {
                        using var img = doc.Render(p, 115f, 115f, false);
                        img.Save(Path.Combine(outDir, $"arsip_{jenis}_{idx}_p{p + 1}.png"), ImageFormat.Png);
                    }
                }
                // Salin PDF arsip ke folder galeri agar tautan "Buka PDF" tetap bekerja.
                File.Copy(file, Path.Combine(outDir, $"arsip_{jenis}_{idx}.pdf"), overwrite: true);
                hasil.Add((jenis, idx, Path.GetFileName(file), File.GetLastWriteTime(file), pages));
                Console.WriteLine($"ARSIP {jenis} ← {Path.GetFileName(file)} ({pages} hal.)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SKIP arsip {Path.GetFileName(file)}: {ex.Message}");
            }
        }
        return hasil;
    }

    /// <summary>HTML mandiri berdampingan: kolom kiri arsip lama, kolom kanan QuestPDF.</summary>
    private static void TulisCompareHtml(
        string outDir,
        List<(string Name, int Pages, string Error)> hasil,
        List<(string Jenis, int Idx, string FileName, DateTime Modified, int Pages)> arsip,
        List<string> arsipTanpaPasangan)
    {
        var sb = new StringBuilder();
        string B64(string file) =>
            File.Exists(file) ? Convert.ToBase64String(File.ReadAllBytes(file)) : null;
        string Img(string file, string alt)
        {
            var b = B64(file);
            return b == null ? "" : $"<img src='data:image/png;base64,{b}' alt='{System.Net.WebUtility.HtmlEncode(alt)}'>";
        }

        sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'><title>Perbandingan PDF — Arsip Lama vs QuestPDF</title>");
        sb.AppendLine("<style>body{font-family:Segoe UI,sans-serif;background:#161b26;color:#e8eaf0;margin:24px}");
        sb.AppendLine("h1{font-size:20px}h2{font-size:15px;color:#cfe0f5;margin:34px 0 6px;border-bottom:1px solid #2c3547;padding-bottom:6px}");
        sb.AppendLine(".meta{font-size:12px;color:#9fb3d1;margin:0 0 10px}");
        sb.AppendLine(".grid{display:grid;grid-template-columns:1fr 1fr;gap:16px;align-items:start}");
        sb.AppendLine(".col{min-width:0}");
        sb.AppendLine(".col h3{font-size:12px;margin:0 0 8px;font-weight:600;letter-spacing:.4px}");
        sb.AppendLine(".lama h3{color:#f0b37e}.baru h3{color:#8fd694}");
        sb.AppendLine(".imgwrap{margin:0 0 12px}");
        sb.AppendLine("img{width:100%;max-width:560px;display:block;border-radius:6px;box-shadow:0 2px 10px rgba(0,0,0,.5);background:#fff}");
        sb.AppendLine(".cap{font-size:10.5px;color:#8fa3c0;margin:3px 0 0;overflow-wrap:anywhere}");
        sb.AppendLine(".kosong{font-size:12px;color:#7d8ba1;font-style:italic;border:1px dashed #33405a;border-radius:6px;padding:12px}");
        sb.AppendLine(".tanpa{color:#f0b37e;font-size:12px}");
        sb.AppendLine("a{color:#82b1ff}.nav{font-size:13px}");
        sb.AppendLine(".toc{font-size:13px;line-height:1.8;margin:10px 0 0}");
        sb.AppendLine("</style></head><body>");
        sb.AppendLine("<p class='nav'><a href='index.html'>← Galeri QuestPDF saja</a></p>");
        sb.AppendLine("<h1>Perbandingan Berdampingan: Arsip Lama (TempPDF) vs QuestPDF</h1>");
        sb.AppendLine("<p class='meta'>Kolom kiri = hasil render arsip lama di folder TempPDF aplikasi; kolom kanan = hasil QuestPDF dengan data uji galeri. " +
                      "Perhatikan bahwa data uji galeri sengaja dummy — bandingkan <b>struktur &amp; tata letak</b>, bukan isinya.</p>");
        sb.AppendLine("<p class='toc'>" + string.Join(" · ", hasil.Select(h => $"<a href='#{h.Name}'>{h.Name}</a>")) + "</p>");

        foreach (var h in hasil)
        {
            var jenis = h.Name;
            var arcs = arsip.Where(a => a.Jenis == jenis).OrderBy(a => a.Idx).ToList();
            sb.AppendLine($"<h2 id='{jenis}'>{jenis}</h2>");
            sb.AppendLine($"<p class='meta'>{arcs.Count} arsip lama · PDF baru {h.Pages} halaman</p>");
            if (arcs.Count == 0)
                sb.AppendLine("<p class='tanpa'>⚠ Tidak ada arsip lama untuk jenis ini di folder TempPDF — bandingkan manual bila ada contoh cetak lama.</p>");

            sb.AppendLine("<div class='grid'>");

            // Kolom kiri: arsip lama
            sb.AppendLine("<div class='col lama'><h3>◀ ARSIP LAMA (TempPDF)</h3>");
            if (arcs.Count == 0)
                sb.AppendLine("<div class='kosong'>Tidak ada arsip untuk dibandingkan.</div>");
            else
                foreach (var a in arcs)
                {
                    for (int p = 1; p <= Math.Min(2, a.Pages); p++)
                    {
                        sb.AppendLine($"<div class='imgwrap'>{Img(Path.Combine(outDir, $"arsip_{a.Jenis}_{a.Idx}_p{p}.png"), $"{a.FileName} hal {p}")}");
                        sb.AppendLine($"<p class='cap'>{System.Net.WebUtility.HtmlEncode(a.FileName)} — hal. {p} · disimpan {a.Modified:dd MMM yyyy HH:mm}</p></div>");
                    }
                    sb.AppendLine($"<p class='cap'><a href='arsip_{a.Jenis}_{a.Idx}.pdf'>Buka PDF arsip</a></p>");
                }
            sb.AppendLine("</div>");

            // Kolom kanan: QuestPDF baru
            sb.AppendLine("<div class='col baru'><h3>▶ QUESTPDF (HASIL BARU)</h3>");
            if (h.Pages > 0)
            {
                for (int p = 1; p <= Math.Min(2, h.Pages); p++)
                {
                    sb.AppendLine($"<div class='imgwrap'>{Img(Path.Combine(outDir, $"{jenis}_p{p}.png"), $"{jenis} baru hal {p}")}");
                    sb.AppendLine($"<p class='cap'>Data uji galeri — hal. {p}</p></div>");
                }
                sb.AppendLine($"<p class='cap'><a href='{jenis}.pdf'>Buka PDF baru {jenis}</a></p>");
            }
            else
                sb.AppendLine($"<div class='kosong'>GAGAL generate: {System.Net.WebUtility.HtmlEncode(h.Error)}</div>");
            sb.AppendLine("</div>");

            sb.AppendLine("</div>");
        }

        if (arsipTanpaPasangan.Count > 0)
        {
            sb.AppendLine("<h2 id='LAIN'>Arsip lain di TempPDF (tanpa pasangan galeri)</h2>");
            sb.AppendLine("<p class='meta'>Termasuk RegisterSurat_* (register, bukan keluaran generator surat per jenis).</p>");
            sb.AppendLine("<ul style='font-size:12px;color:#9fb3d1'>");
            foreach (var f in arsipTanpaPasangan)
                sb.AppendLine($"<li>{System.Net.WebUtility.HtmlEncode(f)}</li>");
            sb.AppendLine("</ul>");
        }

        sb.AppendLine("</body></html>");
        File.WriteAllText(Path.Combine(outDir, "compare.html"), sb.ToString());
    }

    // =====================================================================
    // Test double repository (data tetap, tanpa database)
    // =====================================================================
    private sealed class FakeDesaRepository : IDesaRepository
    {
        public static DesaData BuatDesa() => new DesaData
        {
            // Nama wilayah sengaja ditulis apa adanya (tanpa kata "Desa/Kecamatan/
            // Kabupaten" di dalam nilainya), persis seperti isian di Setelan Aplikasi,
            // sehingga kop pada galeri mencerminkan surat yang sebenarnya.
            NamaDesa = "Sumberjaya",
            Kecamatan = "Tempuran",
            Kabupaten = "Karawang",
            Alamat = "Jl. Raya Sumberjaya No. 1",
            Kodepos = "41361",
            KepalaDesa = "H. Suryana",
            SekretarisDesa = "Dedi Mulyadi",
            NamaCamat = "Camat Tempuran"
        };

        public Task InitializeAsync() => Task.CompletedTask;
        public Task<DesaData> GetInfoDesaAsync(CancellationToken cancellationToken = default) => Task.FromResult(BuatDesa());
        public Task UpdateInfoDesaAsync(DesaData desaData) => Task.CompletedTask;
        public Task SaveInfoDesaAsync(DesaData desaData) => Task.CompletedTask;
        public Task<DesaData> GetInfoDesaFromCacheAsync() => Task.FromResult(BuatDesa());
        public Task InvalidateCacheAsync() => Task.CompletedTask;
    }

    private sealed class FakeSuratRepository : ISuratRepository
    {
        public Task<SuratData> GetByIdAsync(int id, System.Data.IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new SuratData { ID_Surat = id });

        public Task<int> InsertAsync(SuratData entity, System.Data.IDbTransaction? transaction = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> UpdateAsync(SuratData entity, System.Data.IDbTransaction? transaction = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> UpdateStatusAsync(int id, string status, System.Data.IDbTransaction? transaction = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(int id, System.Data.IDbTransaction? transaction = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<SuratData>> GetFilteredAsync(FilterConditions filters, string sortBy, bool ascending, int skip, int take, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> CountAsync(FilterConditions filters, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task InitializeSuratIndexesAsync() => Task.CompletedTask;
        public Task<List<string>> GetJenisSuratKeteranganDesaAsync() => Task.FromResult(new List<string>());
        public Task RefreshJenisSuratConfigurationAsync() => Task.CompletedTask;
        public Task<IEnumerable<SuratData>> GetAllSuratDataAsync(string sortBy = "ID_Surat", bool ascending = true, int skip = 0, int take = 100, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> AddSuratAsync(SuratData entity, System.Data.IDbTransaction? transaction = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> CountSuratByJenisAndYearAsync(string namaJenis, string year, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<SuratData>> GetSuratByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Dictionary<string, int>> GetSuratStatisticsByStatusAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> CheckNomorSuratExistsAsync(string nomorSurat, int? excludeId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Dictionary<string, object>> GetDatabaseStatsAsync() => throw new NotImplementedException();
        public Task<WargaData> GetWargaByIdAsync(int idWarga, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WargaData> GetWargaByNikAsync(string nik, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> AddOrGetWargaAsync(WargaData wargaData, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int?> GetLastSuratIdByTypeAsync(string templateName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
