namespace SuDesApp.Mobile;

/// <summary>Hasil satu surat yang siap dicetak: arsip yang disimpan dan PDF-nya.</summary>
public sealed record HasilSurat(SuratTersimpan Surat, byte[] Pdf);

/// <summary>
/// Hasil paket NTCR: seluruh blanko yang tersimpan (berurutan N1 → N6) dan satu
/// berkas PDF gabungan, satu blanko per halaman.
/// </summary>
public sealed record PaketNtcrHasil(IReadOnlyList<SuratTersimpan> Surat, byte[] Pdf)
{
    public string DaftarNomor => string.Join(", ", Surat.Select(s => s.Nomor));

    public IReadOnlyList<string> DaftarKode => Surat
        .Select(s => s.Kode.ToUpperInvariant())
        .ToList();
}

/// <summary>
/// Satu pintu untuk keenam menu Android: memvalidasi isian, memberi nomor surat,
/// menyimpan arsip, dan menghasilkan PDF. Layar-layar Blazor hanya mengumpulkan
/// isian lalu memanggil kelas ini.
/// </summary>
public sealed class LayananSuratPonsel
{
    private readonly PenyimpananSurat _simpanan;

    public LayananSuratPonsel(PenyimpananSurat simpanan)
    {
        _simpanan = simpanan ?? throw new ArgumentNullException(nameof(simpanan));
    }

    public PenyimpananSurat Simpanan => _simpanan;

    public ProfilDesa Profil => _simpanan.Profil;

    public IReadOnlyList<DefinisiSurat> SuratBaru => KatalogSurat.Menu(KelompokMenu.SuratBaru);

    public IReadOnlyList<DefinisiSurat> SuratNtcr => KatalogSurat.Menu(KelompokMenu.Ntcr);

    public IReadOnlyList<DefinisiSurat> SuratFormulir => KatalogSurat.Menu(KelompokMenu.Formulir);

    /// <summary>Sambungan desktop terakhir; null bila belum pernah tersambung.</summary>
    public SambunganDesktop? Sambungan => _simpanan.Sambungan;

    /// <summary>
    /// True bila ponsel sedang memakai pengaturan desktop: ada sambungan dan
    /// tokennya masih berlaku. Selama true, profil desa bersifat baca-saja.
    /// </summary>
    public bool TerhubungDesktop => _simpanan.Sambungan?.Aktif == true;

    /// <summary>
    /// True bila ponsel pernah tersambung ke desktop, terlepas dari tokennya
    /// masih berlaku. Menu surat memakai penanda ini: sesi yang kedaluwarsa
    /// tidak boleh menyembunyikan menu, tetapi tetap perlu ditandai agar
    /// pengaturan surat ditarik ulang.
    /// </summary>
    public bool Terpasang => _simpanan.Sambungan != null;

    /// <summary>Masuk memakai akun desktop lalu tarik pengaturan suratnya.</summary>
    /// <remarks>
    /// Urutannya sengaja: login dulu (agar galat sandi tidak tertukar dengan galat
    /// jaringan), baru pengaturan. Profil desa baru ditimpa setelah keduanya berhasil
    /// sehingga kegagalan di tengah tidak meninggalkan kop setengah jadi.
    /// </remarks>
    public async Task<SambunganDesktop> SambungkanAsync(
        string alamat, string kunciApi, string username, string kataSandi,
        CancellationToken ct = default)
    {
        var api = new ApiDesktop(alamat, kunciApi);
        var sesi = await api.MasukAsync(username, kataSandi, ct);
        var pengaturan = await api.AmbilPengaturanAsync(sesi.Token, ct);

        _simpanan.SimpanProfil(ProfilDari(pengaturan.Desa));

        var sambungan = new SambunganDesktop
        {
            Alamat = api.Alamat,
            KunciApi = (kunciApi ?? string.Empty).Trim(),
            Token = sesi.Token,
            Username = sesi.Username,
            Nama = sesi.Nama,
            Peran = sesi.Peran,
            WaktuMasuk = DateTime.Now,
            Berakhir = sesi.BerlakuSampai,
            PengaturanDiambil = DateTime.Now
        };

        _simpanan.SimpanSambungan(sambungan);
        return sambungan;
    }

    /// <summary>
    /// Tarik ulang pengaturan surat dari desktop memakai token tersimpan.
    /// Token yang sudah tidak berlaku dibersihkan supaya layar tidak menampilkan
    /// "terhubung" padahal sesinya sudah mati.
    /// </summary>
    public async Task<SambunganDesktop> SegarkanPengaturanAsync(CancellationToken ct = default)
    {
        var sambungan = _simpanan.Sambungan
            ?? throw new ApiDesktopException("Belum tersambung ke desktop. Sambungkan dulu.");

        if (string.IsNullOrWhiteSpace(sambungan.Token))
            throw new ApiDesktopException("Sesi sudah berakhir. Masuk lagi dengan sandi akun desktop.");

        var api = new ApiDesktop(sambungan.Alamat, sambungan.KunciApi);

        PengaturanDesktop pengaturan;
        try
        {
            pengaturan = await api.AmbilPengaturanAsync(sambungan.Token, ct);
        }
        catch (ApiDesktopException ex) when (ex.SesiBerakhir)
        {
            sambungan.Token = string.Empty;
            sambungan.Berakhir = DateTime.MinValue;
            _simpanan.SimpanSambungan(sambungan);
            throw new ApiDesktopException(
                "Sesi desktop sudah berakhir (desktop ditutup atau lewat 12 jam). Masuk lagi dengan sandi akun desktop.",
                ex.Kode, ex);
        }

        _simpanan.SimpanProfil(ProfilDari(pengaturan.Desa));
        sambungan.PengaturanDiambil = DateTime.Now;
        _simpanan.SimpanSambungan(sambungan);
        return sambungan;
    }

    /// <summary>Keluar dari desktop dan lupakan token. Profil hasil tarikan tetap dipakai.</summary>
    public async Task PutuskanAsync(CancellationToken ct = default)
    {
        var sambungan = _simpanan.Sambungan;
        if (sambungan != null && !string.IsNullOrWhiteSpace(sambungan.Token))
        {
            try
            {
                await new ApiDesktop(sambungan.Alamat, sambungan.KunciApi)
                    .KeluarAsync(sambungan.Token, ct);
            }
            catch (Exception)
            {
                // Desktop mungkin sudah ditutup; memutuskan sambungan di ponsel
                // tetap harus berhasil walau pemberitahuan ke desktop gagal.
            }
        }

        _simpanan.HapusSambungan();
    }

    private static ProfilDesa ProfilDari(DesaDesktop desa) => new()
    {
        Kabupaten = (desa.Kabupaten ?? string.Empty).Trim(),
        Kecamatan = (desa.Kecamatan ?? string.Empty).Trim(),
        NamaDesa = (desa.NamaDesa ?? string.Empty).Trim(),
        Alamat = (desa.Alamat ?? string.Empty).Trim(),
        Kodepos = (desa.Kodepos ?? string.Empty).Trim(),
        Email = (desa.Email ?? string.Empty).Trim(),
        NamaKepalaDesa = (desa.KepalaDesa ?? string.Empty).Trim()
    };

    public IReadOnlyList<SuratTersimpan> Arsip => _simpanan.Arsip;

    public IReadOnlyList<SuratTersimpan> ArsipRegister => _simpanan.Arsip
        .Where(s => s.Kelompok == KelompokMenu.SuratBaru ||
                    s.Kelompok == KelompokMenu.Formulir ||
                    s.Kode == "bebas" || s.Kode == "daftar-hadir")
        .ToList();

    public IReadOnlyList<SuratTersimpan> ArsipNtcr => _simpanan.Arsip
        .Where(s => s.Kelompok == KelompokMenu.Ntcr)
        .ToList();

    /// <summary>
    /// Nomor surat berikutnya: awalan/urut/Ds/tahun. Urutan dihitung per awalan
    /// (pola desktop), sehingga semua surat 470 — apa pun jenisnya — berbagi satu
    /// deret nomor dan tidak ada nomor yang sama terbit dua kali.
    /// </summary>
    public string NomorBerikutnya(DefinisiSurat surat, DateTime tanggal)
    {
        int urut = _simpanan.HitungNomorAwalan(surat.AwalanNomor, tanggal.Year) + 1;
        return NomorUrut(surat.AwalanNomor, urut, tanggal.Year);
    }

    private static string NomorUrut(string awalan, int urut, int tahun) =>
        $"{awalan}/{urut:000}/Ds/{tahun}";

    public HasilSurat Buat(DefinisiSurat surat, IReadOnlyDictionary<string, string> isian, DateTime tanggal)
    {
        ArgumentNullException.ThrowIfNull(surat);
        PastikanProfilLengkap();

        var nilai = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var kurang = new List<string>();

        foreach (var kolom in surat.Kolom)
        {
            isian.TryGetValue(kolom.Kunci, out var isi);
            string bersih = FormatSurat.Bersih(isi);
            nilai[kolom.Kunci] = bersih;

            if (kolom.Wajib && bersih.Length == 0)
            {
                kurang.Add(kolom.Label);
            }
        }

        if (kurang.Count > 0)
        {
            throw new InvalidOperationException("Lengkapi isian wajib: " + string.Join(", ", kurang) + ".");
        }

        string nomor = NomorBerikutnya(surat, tanggal);
        byte[] pdf = PencetakSurat.BuatSurat(Profil, surat, nilai, nomor, tanggal);

        var arsip = new SuratTersimpan
        {
            Kode = surat.Kode,
            NamaJenis = surat.Nama,
            Kelompok = surat.Kelompok,
            Judul = surat.Judul,
            Nomor = nomor,
            Waktu = tanggal,
            Isian = nilai
        };

        _simpanan.Tambah(arsip);
        return new HasilSurat(arsip, pdf);
    }

    /// <summary>Menu "Daftar Hadir": tabel peserta dan blok tanda tangan.</summary>
    public HasilSurat BuatDaftarHadir(
        string judul,
        string hariTanggal,
        string pukul,
        string tempat,
        IReadOnlyList<PesertaHadir> peserta,
        DateTime tanggal)
    {
        PastikanProfilLengkap();

        var hadir = peserta
            .Where(p => !string.IsNullOrWhiteSpace(p.Nama))
            .Select(p => new PesertaHadir
            {
                Nama = FormatSurat.Bersih(p.Nama),
                Jabatan = FormatSurat.Bersih(p.Jabatan),
                Keterangan = FormatSurat.Bersih(p.Keterangan)
            })
            .ToList();

        if (hadir.Count == 0)
        {
            throw new InvalidOperationException("Isi minimal satu nama peserta.");
        }

        string judulBersih = string.IsNullOrWhiteSpace(judul) ? "Daftar Hadir" : FormatSurat.Bersih(judul);
        string nomor = $"DH/{_simpanan.HitungNomor("daftar-hadir", tanggal.Year) + 1:000}/{tanggal.Year}";

        byte[] pdf = PencetakSurat.BuatDaftarHadir(
            Profil, judulBersih, hariTanggal, pukul, tempat, hadir, tanggal);

        var arsip = new SuratTersimpan
        {
            Kode = "daftar-hadir",
            NamaJenis = "Daftar Hadir",
            Kelompok = KelompokMenu.SuratBaru,
            Judul = judulBersih,
            Nomor = nomor,
            Waktu = tanggal,
            Isian = new Dictionary<string, string>
            {
                ["nama"] = judulBersih,
                ["peserta"] = string.Join(", ", hadir.Select(p => p.Nama))
            }
        };

        _simpanan.Tambah(arsip);
        return new HasilSurat(arsip, pdf);
    }

    /// <summary>Urutan blanko paket NTCR, sama dengan urutan blanko di desktop.</summary>
    public static IReadOnlyList<string> UrutanPaket { get; } = new[] { "n1", "n2", "n3", "n4", "n5", "n6" };

    /// <summary>
    /// Menu "Paket N1-N6": satu data pasangan menjadi seluruh blanko terpilih,
    /// disimpan ke Register NTCR lalu dicetak sebagai satu PDF gabungan — satu
    /// blanko per halaman, sama seperti paket pernikahan di desktop.
    /// </summary>
    public PaketNtcrHasil BuatPaketNtcr(
        IReadOnlyDictionary<string, string> isian,
        IReadOnlyList<string> kodeTerpilih,
        DateTime tanggal)
    {
        ArgumentNullException.ThrowIfNull(isian);
        PastikanProfilLengkap();

        var terpilih = UrutanPaket
            .Where(kode => kodeTerpilih.Contains(kode, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (terpilih.Count == 0)
        {
            throw new InvalidOperationException("Pilih minimal satu blanko yang akan dibuat pada paket.");
        }

        var kurang = new List<string>();
        var isiBlanko = new List<(DefinisiSurat Definisi, Dictionary<string, string> Nilai)>(terpilih.Count);

        foreach (var kode in terpilih)
        {
            var definisi = KatalogSurat.Cari(kode)
                ?? throw new InvalidOperationException($"Blanko '{kode}' tidak dikenal.");

            var nilai = PetakanNilaiPaket(kode, isian);
            var kosong = definisi.Kolom
                .Where(k => k.Wajib && FormatSurat.Bersih(nilai.GetValueOrDefault(k.Kunci)).Length == 0)
                .Select(k => k.Label)
                .ToList();

            if (kosong.Count > 0)
            {
                kurang.Add($"{kode.ToUpperInvariant()}: {string.Join(", ", kosong)}");
            }

            isiBlanko.Add((definisi, nilai));
        }

        if (kurang.Count > 0)
        {
            throw new InvalidOperationException("Lengkapi isian wajib — " + string.Join("; ", kurang) + ".");
        }

        // Seluruh blanko paket memakai awalan yang sama (474.3), jadi nomornya
        // berurutan langsung dalam satu deret — sama seperti simpan dalam satu
        // transaksi di desktop.
        string awalan = isiBlanko[0].Definisi.AwalanNomor;
        int urut = _simpanan.HitungNomorAwalan(awalan, tanggal.Year);

        var halaman = new List<HalamanSurat>(isiBlanko.Count);
        foreach (var (definisi, nilai) in isiBlanko)
        {
            urut++;
            halaman.Add(new HalamanSurat(definisi, nilai, NomorUrut(awalan, urut, tanggal.Year)));
        }

        byte[] pdf = PencetakSurat.BuatBerkasBanyak(Profil, halaman, tanggal);

        var arsip = new List<SuratTersimpan>(halaman.Count);
        foreach (var lembar in halaman)
        {
            var catatan = new SuratTersimpan
            {
                Kode = lembar.Surat.Kode,
                NamaJenis = lembar.Surat.Nama,
                Kelompok = KelompokMenu.Ntcr,
                Judul = lembar.Surat.Judul,
                Nomor = lembar.Nomor,
                Waktu = tanggal,
                Isian = new Dictionary<string, string>(lembar.Nilai, StringComparer.OrdinalIgnoreCase)
            };

            _simpanan.Tambah(catatan);
            arsip.Add(catatan);
        }

        return new PaketNtcrHasil(arsip, pdf);
    }

    /// <summary>
    /// Isi satu blanko paket dari data pasangan. Isian form paket memakai awalan
    /// <c>pria-</c>, <c>istri-</c>, <c>ayah-</c>, <c>ibu-</c>, dan <c>kematian-</c>;
    /// tiap blanko hanya menerima bagian yang relevan.
    /// </summary>
    private static Dictionary<string, string> PetakanNilaiPaket(
        string kode, IReadOnlyDictionary<string, string> isian)
    {
        string A(string kunci) => isian.TryGetValue(kunci, out var nilai) ? FormatSurat.Bersih(nilai) : string.Empty;

        string Ttl(string kunciTempat, string kunciTanggal)
        {
            string tempat = A(kunciTempat);
            string tanggal = FormatSurat.BacaTanggal(A(kunciTanggal)) is { } t
                ? FormatSurat.TanggalPanjang(t)
                : A(kunciTanggal);

            if (tempat.Length == 0) return tanggal;
            if (tanggal.Length == 0) return tempat;
            return $"{tempat}, {tanggal}";
        }

        var nilai = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        switch (kode.ToLowerInvariant())
        {
            case "n1":
                nilai["nama"] = A("pria-nama");
                nilai["nik"] = A("pria-nik");
                nilai["tempat-lahir"] = A("pria-tempat-lahir");
                nilai["tanggal-lahir"] = A("pria-tanggal-lahir");
                nilai["agama"] = A("pria-agama");
                nilai["pekerjaan"] = A("pria-pekerjaan");
                nilai["alamat"] = A("pria-alamat");
                break;

            case "n2":
                nilai["nama"] = A("istri-nama");
                nilai["nik"] = A("istri-nik");
                nilai["tempat-lahir"] = A("istri-tempat-lahir");
                nilai["tanggal-lahir"] = A("istri-tanggal-lahir");
                nilai["alamat"] = A("istri-alamat");
                nilai["nomor-surat-rt"] = string.Empty;
                break;

            case "n3":
                nilai["nama-pria"] = A("pria-nama");
                nilai["nik-pria"] = A("pria-nik");
                nilai["tempat-lahir-pria"] = Ttl("pria-tempat-lahir", "pria-tanggal-lahir");
                nilai["alamat-pria"] = A("pria-alamat");
                nilai["nama-wanita"] = A("istri-nama");
                nilai["nik-wanita"] = A("istri-nik");
                nilai["tempat-lahir-wanita"] = Ttl("istri-tempat-lahir", "istri-tanggal-lahir");
                nilai["alamat-wanita"] = A("istri-alamat");
                nilai["tanggal-akad"] = A("akad-tanggal");
                break;

            case "n4":
                nilai["nama-pria"] = A("pria-nama");
                nilai["nik-pria"] = A("pria-nik");
                nilai["nama-wanita"] = A("istri-nama");
                nilai["nik-wanita"] = A("istri-nik");
                nilai["tanggal-akad"] = A("akad-tanggal");
                break;

            case "n5":
            {
                var orangTua = new[] { A("ayah-pria"), A("ibu-pria") }
                    .Where(nama => nama.Length > 0)
                    .ToList();

                nilai["nama-anak"] = A("pria-nama");
                nilai["nik-anak"] = A("pria-nik");
                nilai["nama-pasangan"] = A("istri-nama");
                nilai["nama-orang-tua"] = string.Join(" dan ", orangTua);
                nilai["hubungan"] = orangTua.Count switch
                {
                    0 => string.Empty,
                    1 => "Orang Tua Kandung",
                    _ => "Ayah dan Ibu Kandung"
                };
                nilai["alamat"] = A("pria-alamat");
                break;
            }

            case "n6":
                nilai["nama"] = A("kematian-nama");
                nilai["nik"] = A("kematian-nik");
                nilai["tanggal-meninggal"] = A("kematian-tanggal");
                nilai["sebab"] = A("kematian-sebab");
                nilai["hubungan"] = A("kematian-hubungan");
                break;

            default:
                throw new InvalidOperationException($"Blanko '{kode}' tidak termasuk paket NTCR.");
        }

        return nilai;
    }

    /// <summary>Cetak ulang surat dari arsip, termasuk surat bebas dan daftar hadir.</summary>
    public byte[] CetakUlang(SuratTersimpan surat)
    {
        ArgumentNullException.ThrowIfNull(surat);
        PastikanProfilLengkap();

        if (surat.Kode == "bebas")
        {
            return PencetakSurat.BuatSuratBebas(
                Profil, surat.Judul, surat.Nomor, surat.ParagrafBebas, surat.Waktu);
        }

        if (surat.Kode == "daftar-hadir")
        {
            var peserta = (surat.Isian.TryGetValue("peserta", out var daftar) ? daftar : string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(nama => new PesertaHadir { Nama = nama })
                .ToList();

            return PencetakSurat.BuatDaftarHadir(
                Profil, surat.Judul, string.Empty, string.Empty, string.Empty, peserta, surat.Waktu);
        }

        var definisi = KatalogSurat.Cari(surat.Kode)
            ?? throw new InvalidOperationException($"Jenis surat '{surat.Kode}' tidak dikenal.");

        return PencetakSurat.BuatSurat(Profil, definisi, surat.Isian, surat.Nomor, surat.Waktu);
    }

    /// <summary>PDF register untuk dicetak atau diarsipkan.</summary>
    public byte[] CetakRegister(bool ntcr, DateTime tanggal)
    {
        PastikanProfilLengkap();

        var daftar = ntcr ? ArsipNtcr : ArsipRegister;
        string judul = ntcr ? "Register NTCR" : "Register Surat";
        return PencetakSurat.BuatRegister(Profil, judul, daftar, tanggal);
    }

    private void PastikanProfilLengkap()
    {
        if (Profil.Lengkap) return;

        throw new InvalidOperationException(
            "Pengaturan surat belum lengkap. Sambungkan ke desktop lalu tarik ulang pengaturan, " +
            "atau lengkapi nama desa, kecamatan, kabupaten, dan Kepala Desa di desktop.");
    }
}
