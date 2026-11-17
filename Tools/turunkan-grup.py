"""Turunkan pengelompokan kartu (`grup` + `grupUrutan`) skema formulir dari
section kartu XAML desktop.

Desktop memecah isian ke beberapa kartu berjudul (FormCard + FormSectionTitle);
Android membangun formulirnya dari skema. Agar tampilan Android "sama seperti
desktop", judul dan urutan kartu yang sama ditulis ke skema — satu sumber
kebenaran, dijaga ParitasBuatSuratAndroidDesktopTests.

Cara kerja:
 1. Baca section tiap view input desktop (judul + {Binding ...} di dalamnya).
 2. Cocokkan binding dengan kunci skema (case-insensitive; binding bertingkat
    seperti AyahCalonSuami.Nama dipetakan ke kunci blanko yang sebenarnya).
 3. Kunci yang tidak ter-claim mendapat judul lewat aturan deterministik.
 4. Urutan kartu = urutan section di desktop, kartu sisa di belakang,
    PEJABAT PENANDATANGAN selalu paling akhir.

Pemakaian: python Tools/turunkan-grup.py [--tulis]
"""
import io, json, os, re, sys

AKAR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
VIEWS = os.path.join(AKAR, "SuDesApp.Wpf", "Views")
SKEMA_DESKTOP = os.path.join(AKAR, "SuDesApp.Core", "Configuration", "SkemaFormulir.json")
SKEMA_ANDROID = os.path.join(AKAR, "android", "app", "src", "main", "assets", "skema-formulir.json")
MANIFEST = os.path.join(AKAR, "SuDesApp.Core", "Configuration", "BidangFormDesktop.json")

# Kolom yang wajar tidak punya binding di XAML: nomor dibangkitkan aplikasi,
# keterangan/keperluan diambil dari bilah bawah, dan bidang ber-otomatis=true
# memang tidak ditampilkan Android.
DIIZINKAN_TANPA_XAML = {"nomorSurat", "keterangan", "keperluan"}

VIEW_JENIS = {
    "SKD_UMUM": "SkdInputView.xaml", "KEMATIAN": "KematianInputView.xaml",
    "KENAL_LAHIR": "KenalLahirInputView.xaml", "PENGANTAR_SKCK": "SkckInputView.xaml",
    "SKU": "SkuInputView.xaml", "SKTM": "SktmInputView.xaml",
    "PENGHASILAN": "PenghasilanInputView.xaml", "SK_BELUM_TERBIT": "KeteranganTerbitInputView.xaml",
    "DOMISILI_WARGA": "DomisiliWargaInputView.xaml", "INSTANSI": "DomisiliInstansiInputView.xaml",
    "BEDANAMA": "BedaNamaInputView.xaml", "IZIN_ORTU": "IzinOrtuInputView.xaml",
    "IJIN_TINGGAL": "IjinTinggalInputView.xaml", "GARAPAN_SAWAH": "GarapanInputView.xaml",
    "AHLI_WARIS": "AhliWarisInputView.xaml", "PENGANTAR_ADMINDUK": "PengantarAdmindukInputView.xaml",
    "SK_PINDAH": "PindahInputView.xaml", "SK_TIDAK_PUNYA_RUMAH": "TidakPunyaRumahInputView.xaml",
    "IZIN_KERAMAIAN": "KeramaianInputView.xaml",
}
NTCR_VIEW = "NtcrInputView.xaml"

# Binding bukan kolom isian (opsi dropdown, penanda visibilitas, radio pejabat).
ABAI = re.compile(r"(options|isse|butuh|ism)\w*$", re.I)

EKOR = ("KETERANGAN / MAKSUD", "KETERANGAN SURAT", "KEPERLUAN SURAT", "PEJABAT PENANDATANGAN")


def area_baris(x):
    """Hapus {Binding} di dalam blok DataGrid.

    Binding pada kolom DataGrid (Nama/TempatLahir per anak, Lokasi per bidang
    garapan) dihitung terhadap objek baris, bukan property view model. Kalau
    tidak dibuang, kolom tingkat atas surat (mis. Tempat Lahir almarhum pada
    Ahli Waris) salah diklaim kartu daftar anak. Tag <DataGrid> sendiri TIDAK
    dibuang: ItemsSource/SelectedItem di nyalah daftar itu ke view model
    (RincianItems, AnakItems) dan justru diperlukan sebagai bukti kolomnya ada.
    """
    out, i = [], 0
    while True:
        mula = x.find("<DataGrid.Columns>", i)
        if mula < 0:
            out.append(x[i:]); break
        akhir = x.find("</DataGrid.Columns>", mula)
        akhir = akhir + len("</DataGrid.Columns>") if akhir > 0 else len(x)
        out.append(x[i:mula]); out.append(" " * (akhir - mula)); i = akhir
    return "".join(out)


def section_view(nama_file):
    """[(judul, [binding...])] urut seperti di XAML desktop."""
    x = area_baris(io.open(os.path.join(VIEWS, nama_file), encoding="utf-8").read())
    titik = [m.start() for m in re.finditer(r"StaticResource FormCard", x)]
    hasil = []
    for n, st in enumerate(titik):
        seg = x[st:titik[n + 1] if n + 1 < len(titik) else len(x)]
        t = re.search(r"<TextBlock[^>]*FormSectionTitle[^>]*", seg)
        if not t:
            continue
        judul = re.search(r'Text="([^"]+)"', t.group(0)).group(1).replace("&amp;", "&")
        binds = []
        for b in re.findall(r"\{Binding ([A-Za-z0-9_.]+)", seg):
            if ABAI.search(b) or b in binds:
                continue
            binds.append(b)
        hasil.append((judul, binds))
    return hasil


# Penanda pilihan pejabat: sebagian besar view memakai radio/checkbox, bukan
# ComboBox SelectedPejabat. ItemsSource daftar juga dipetakan ke kunci skema.
PETA_BINDING_KHUSUS = {
    "selectedpejabat": "pejabatPenandatangan",
    "iskadesselected": "pejabatPenandatangan",
    "issekdesselected": "pejabatPenandatangan",
    "iscamatselected": "pejabatPenandatangan",
    "iskapolsekselected": "pejabatPenandatangan",
    "isdanramilselected": "pejabatPenandatangan",
    "rincianitems": "rincianTambahan",
    "anakitems": "anak",
    "alamatpasangansamaalmarhum": "alamatPasangan",
}


# Objek yang diedit lewat ContentControl + DataTemplate (orang tua pada
# blanko NTCR): binding di dalam template dihitung terhadap objek itu, jadi
# seluruh keluarga kunci dengan awalan inilah yang terikat.
PETA_AWALAN_OBJEK = {
    "ayahcalonsuami": "ayahcalon",
    "ibucalonsuami": "ibucalon",
    "ayahcalonistri": "ayahcalonistri",
    "ibucalonistri": "ibucalonistri",
}


def binding_ke_kunci(b, kunci):
    peta = {k.lower(): k for k in kunci}
    if b.lower() in PETA_BINDING_KHUSUS:
        return peta.get(PETA_BINDING_KHUSUS[b.lower()].lower())
    if b.lower() in peta:
        return peta[b.lower()]
    # Bertingkat: AyahCalonSuami.Nama -> ayahCalonNama (sisi suami tidak memakai
    # "Suami" di nama kunci), AyahCalonIstri.Nama -> ayahCalonIstriNama.
    if "." in b:
        induk, anak = b.split(".", 1)
        for alias, ganti in (("ayahCalonSuami", "ayahCalon"), ("ibuCalonSuami", "ibuCalon")):
            if induk.lower() == alias:
                calon = (ganti + anak[0].upper() + anak[1:]).lower()
                return peta.get(calon)
    return None


def judul_sisa(id_jenis, kunci, grup_sudah, judul_pertama):
    """Aturan deterministik untuk kunci yang tidak ter-claim oleh XAML."""
    out = {}
    for k in kunci:
        if k in grup_sudah:
            continue
        kl = k.lower()
        if kl == "nomorsurat":
            out[k] = judul_pertama
        elif kl.startswith("pejabat") or kl.endswith("pejabatpenandatangan"):
            out[k] = "PEJABAT PENANDATANGAN"
        elif kl in ("keterangan", "keteranganTambahan", "keperluan"):
            out[k] = "KETERANGAN SURAT" if kl != "keperluan" else "KEPERLUAN SURAT"
        elif id_jenis.startswith("NTCR_"):
            if kl.startswith(("ayahcalon", "ibucalon")):
                out[k] = "DATA ORANG TUA / WALI"
            elif "istri" in kl:
                out[k] = "CALON MEMPELAI WANITA"
            else:
                out[k] = "KOLOM KHUSUS BLANKO TERPILIH"
        elif id_jenis == "REKENING_KORAN":
            out[k] = ("DATA PEMOHON" if kl.startswith(
                ("nik", "nama", "tempat", "tanggal", "jenis", "agama", "pekerjaan",
                 "dusun", "rt", "rw", "desa", "kec", "kab")) else "DATA REKENING")
        elif kl.endswith("penerimakuasa") or kl.startswith("penerima"):
            out[k] = "PENERIMA KUASA"
        elif kl == "anak" and id_jenis == "AHLI_WARIS":
            out[k] = "DATA ANAK (AHLI WARIS)"
        elif "pasangan" in kl and id_jenis == "AHLI_WARIS":
            out[k] = "DATA PASANGAN"
        elif k == "rincianTambahan":
            out[k] = "TANAH GARAPAN TAMBAHAN (BIDANG KE-2 DST.)"
        elif kl == "anak":
            out[k] = "DATA ANAK"
        else:
            out[k] = judul_pertama
    return out


def susun_urutan(urutan_xaml, semua_grup):
    """Urutan kartu: section desktop dulu, sisanya di ekor yangKnown."""
    urutan = [g for g in urutan_xaml if g in semua_grup]
    sisa = [g for g in dict.fromkeys(semua_grup) if g not in urutan]
    ekor = [g for g in EKOR if g in (urutan + sisa)]
    badan = [g for g in urutan + sisa if g not in EKOR]
    return badan + ekor


def proses(id_jenis, v, laporan, manifest=None):
    kunci = [f["key"] for f in v["fields"]]
    terikat = set()       # kunci yang benar-benar punya binding di view desktop
    nama_view = VIEW_JENIS.get(id_jenis) or (NTCR_VIEW if id_jenis.startswith("NTCR_") else None)
    grup = {}
    urutan_xaml = []      # semua judul kartu, urut seperti di desktop
    judul_terpakai = []   # yang benar-benar mengklaim minimal satu bidang
    judul_pertama = "DATA PEMOHON"
    if nama_view and os.path.exists(os.path.join(VIEWS, nama_view)):
        for judul, binds in section_view(nama_view):
            klaim = []
            for b in binds:
                kk = binding_ke_kunci(b, kunci)
                if kk and kk not in grup:
                    grup[kk] = judul
                    klaim.append(kk)
                    terikat.add(kk)
            if judul not in urutan_xaml:
                urutan_xaml.append(judul)
            if klaim:
                if judul not in judul_terpakai:
                    judul_terpakai.append(judul)
                if judul_pertama == "DATA PEMOHON" and len(judul_terpakai) == 1:
                    judul_pertama = judul
    grup.update(judul_sisa(id_jenis, kunci, grup, judul_pertama))
    for f in v["fields"]:
        f["grup"] = grup[f["key"]]
    v["grupUrutan"] = susun_urutan(urutan_xaml, list(dict.fromkeys(grup.values())))
    if nama_view and os.path.exists(os.path.join(VIEWS, nama_view)):
        x = area_baris(io.open(os.path.join(VIEWS, nama_view), encoding="utf-8").read())
        for b in re.findall(r"\{Binding ([A-Za-z0-9_.]+)", x):
            if ABAI.search(b):
                continue
            kk = binding_ke_kunci(b, kunci)
            if kk:
                terikat.add(kk)
            awalan = PETA_AWALAN_OBJEK.get(b.lower())
            if awalan:
                terikat.update(k for k in kunci if k.lower().startswith(awalan))

    laporan[id_jenis] = v["grupUrutan"]
    if manifest is not None:
        manifest[id_jenis] = {"view": nama_view or "", "bidang": sorted(terikat)}
    return terikat


def kunci_badan(v):
    """Semua $kunci yang dipakai blok badan (PDF) jenis ini."""
    teks = json.dumps(v.get("badan", []), ensure_ascii=False)
    return set(re.findall(r"$([A-Za-z0-9_.\[\]]+)", teks))


def audit(d, terikat):
    """Daftar kolom skema yang tidak ada di form desktop.

    Inilah sumber gejala "Android menampilkan kolom yang desktop tidak punya"
    (mis. Status Perkawinan pada Garapan Sawah). Keluaran bukan nol bila ada
    kolom asing, supaya bisa dipakai sebagai gerbang sebelum menerbitkan skema.
    """
    buruk = []
    for id_jenis, v in d.items():
        punya = terikat.get(id_jenis) or set()
        if not punya:
            continue  # jenis tanpa view desktop (template) tidak bisa diaudit
        for f in v["fields"]:
            if f.get("otomatis") or f["key"] in DIIZINKAN_TANPA_XAML:
                continue
            if f["key"] in punya:
                continue
            # Kolom yang tidak ada di XAML tetapi tercetak di PDF desktop tetap
            # sah: desktop membacanya dari kartu warga, Android harus mengetiknya.
            dicetak = kunci_badan(v)
            if f["key"] in dicetak or (f.get("target") or "") in dicetak:
                continue
            buruk.append(f"{id_jenis}.{f['key']} ({f['label']})")
    if not buruk:
        print("\naudit kolom: semua kolom skema terikat di form desktop")
        return 0
    print("\nKOLOM SKEMA TIDAK ADA DI FORM DESKTOP:")
    for b in buruk:
        print("  -", b)
    return 1


def main():
    d = json.loads(io.open(SKEMA_DESKTOP, encoding="utf-8-sig").read())
    laporan = {}
    manifest = {}
    terikat = {}
    for k, v in d.items():
        terikat[k] = proses(k, v, laporan, manifest) or set()
    for k, u in laporan.items():
        print(f"{k:22} {u}")
    if "--audit" in sys.argv:
        sys.exit(audit(d, terikat))
    if "--tulis" in sys.argv:
        io.open(MANIFEST, "w", encoding="utf-8").write(
            json.dumps(manifest, ensure_ascii=False, indent=2) + "\n")
        print("manifest kolom form desktop ditulis")
        teks = json.dumps(d, ensure_ascii=False, indent=2)
        io.open(SKEMA_DESKTOP, "w", encoding="utf-8").write(teks)
        io.open(SKEMA_ANDROID, "w", encoding="utf-8").write(teks)
        print("\nschema ditulis ke kedua salinan")


if __name__ == "__main__":
    main()
