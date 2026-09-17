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
using SuDesApp.Interface;
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

    private static object NullLoggerOf(Type t) =>
        typeof(Program).GetMethod(nameof(NullLoggerOfGeneric), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(t).Invoke(null, null)!;

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

        foreach (var n in new[] { "NTCR_N1", "NTCR_N2", "NTCR_N3", "NTCR_N4" })
        {
            var d = Base(n);
            d.Ntcr = new NtcrData
            {
                NamaIstri = "Calon Istri Uji",
                NikIstri = "3201234567890003",
                TempatLahirIstri = "Uji",
                TanggalLahirIstri = "1992-02-02",
                AgamaIstri = "Islam",
                StatusPerkawinanIstri = "Belum Kawin",
                PekerjaanIstri = "Ibu Rumah Tangga",
                AlamatIstri = "Dusun Uji Desa Uji",
                NamaAyahCalonSuami = "Ayah Pria Uji",
                NamaIbuCalonSuami = "Ibu Pria Uji",
                NamaAyahCalonIstri = "Ayah Istri Uji",
                NamaIbuCalonIstri = "Ibu Istri Uji",
                TujuanSurat = "pendaftaran keperluan pernikahan pada KUA Kecamatan Uji"
            };
            list.Add((n, d));
        }

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
