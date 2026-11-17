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

    Binding pada baris DataGrid (Nama/TempatLahir/TanggalLahir per anak atau
    per bidang garapan) dihitung terhadap objek baris, bukan property view
    model. Kalau tidak dibuang, kolom tingkat atas surat (mis. Tempat Lahir
    almarhum pada Ahli Waris) salah diklaim kartu daftar anak.
    """
    out, i = [], 0
    while True:
        mula = x.find("<DataGrid ", i)
        if mula < 0:
            out.append(x[i:]); break
        akhir = x.find("</DataGrid>", mula)
        akhir = akhir + len("</DataGrid>") if akhir > 0 else len(x)
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


def binding_ke_kunci(b, kunci):
    peta = {k.lower(): k for k in kunci}
    if b == "SelectedPejabat":
        return peta.get("pejabatpenandatangan")
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


def proses(id_jenis, v, laporan):
    kunci = [f["key"] for f in v["fields"]]
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
    laporan[id_jenis] = v["grupUrutan"]


def main():
    d = json.loads(io.open(SKEMA_DESKTOP, encoding="utf-8-sig").read())
    laporan = {}
    for k, v in d.items():
        proses(k, v, laporan)
    for k, u in laporan.items():
        print(f"{k:22} {u}")
    if "--tulis" in sys.argv:
        teks = json.dumps(d, ensure_ascii=False, indent=2)
        io.open(SKEMA_DESKTOP, "w", encoding="utf-8").write(teks)
        io.open(SKEMA_ANDROID, "w", encoding="utf-8").write(teks)
        print("\nschema ditulis ke kedua salinan")


if __name__ == "__main__":
    main()
