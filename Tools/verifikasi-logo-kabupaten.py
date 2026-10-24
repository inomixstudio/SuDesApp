#!/usr/bin/env python3
"""
Verifikasi + perbaikan lambang kabupaten/kota di Resources/LogosKabupaten.

Masalah yang diselesaikan
-------------------------
Skrip pengunduh lama menebak: judul artikel ditentukan dari nama, lalu gambar
dipilih dari daftar gambar artikel berdasarkan kata "lambang". Cara itu pernah
memasang "Coat of arms of Aceh" (lambang PROVINSI) untuk kabupaten di Aceh, dan
tidak ada cara membuktikannya setelah berkas tersimpan — nama berkas lokal hanya
berisi kode, bukan asal-usulnya. Contoh nyata yang ditemukan alat ini: berkas
32.75 (Kota Bekasi) ternyata logo DPRD Kota Bekasi, bukan lambang kota.

Tiga sumber acuan, semuanya terstruktur (bukan tebakan):

  1. Artikel id.wikipedia ("Kabupaten/Kota X") → `wikibase_item` (QID) dan
     parameter `lambang` di infobox — apa yang benar-benar dipasang artikel.
  2. Wikidata QID → P94 ("coat of arms image"), bila kosong P158 ("seal image").
     Dua properti ini dipakai proyek Wikidata untuk lambang daerah Indonesia.
  3. Commons → imageinfo berkas acuan: SHA-1 isi, URL, dan thumbnail.
     Lebar render HARUS salah satu ukuran baku Wikimedia (20, 40, 60, 120, 250,
     330, 500, 960, 1280, 1920, 3840 — lihat
     mediawiki.org/wiki/Common_thumbnail_sizes). Sejak 2026 permintaan lebar
     lain ditolak: 400 untuk hotlink langsung, dan 429 berulang karena setiap
     ukuran non-baku memaksa render baru. 1024 yang dipakai sebelumnya BUKAN
     ukuran baku — akibatnya lambang besar gagal terunduh sementara yang kecil
     berhasil, sehingga tampak seperti gangguan jaringan. 960 dipilih karena
     paling dekat ke 1024: tetap tajam untuk kop surat, tanpa menambah ukuran
     berkas yang tak perlu.

Jalur unduh gambar, berurutan (dibuktikan dengan mencoba langsung saat menulis
skrip ini):

  1. `commons.wikimedia.org/w/thumb.php?f=<nama>&w=960` — dilayani host wiki,
     bukan CDN `upload.wikimedia.org`;
  2. `thumburl` dari imageinfo (CDN, ukuran baku);
  3. `Special:FilePath/<nama>?width=960` (mengikuti pengalihan);
  4. berkas ASLINYA, hanya bila lebarnya ≤ 960 dan bentuknya raster.

Kenapa begini: CDN `upload.wikimedia.org` bisa memblokir satu IP dengan 429
"Too many requests" setelah unduhan massal — termasuk untuk berkas kecil yang
tidak butuh render sama sekali. Saat itu terjadi, hanya thumb.php yang masih
melayani, jadi ia yang dijadikan jalur utama. Tanpa urutan ini, pengumpulan
ratusan lambang berhenti di berkas pertama dan tampak seperti gangguan jaringan.

Berkas lokal dinilai COCOK bila salah satu terpenuhi:

  * SHA-1 isinya sama dengan berkas acuan (byte identik — bukti terkuat), atau
  * bentuknya mirip dengan render acuan (dHash; untuk versi yang dikompres
    ulang/diperkecil), atau
  * SHA-1-nya dikenali Commons sebagai berkas yang namanya jelas menyebut
    lambang daerah ini (versi lain dari lambang yang sama).

Selain itu SALAH — dan laporannya menyebut berkas apa yang sebenarnya
terpasang, karena SHA-1 lokal dicari balik di Commons (`list=allimages&aisha1`).

Pemakaian
---------
    py -3 Tools/verifikasi-logo-kabupaten.py                 # berkas yang ada
    py -3 Tools/verifikasi-logo-kabupaten.py --semua         # 514 kabupaten/kota
    py -3 Tools/verifikasi-logo-kabupaten.py --perbaiki      # unduh ulang yang salah/hilang
    py -3 Tools/verifikasi-logo-kabupaten.py --kode 32.15,32.75

`--perbaiki` mengganti berkas yang SALAH/HILANG dengan berkas acuan yang SAMA
dengan yang dipakai artikel/Wikidata, lalu menulis sumber.json (asal-usul tiap
berkas, termasuk SHA-256 isi akhir). Uji di SuDesApp.Tests menjaga agar isi
berkas tidak berubah tanpa sumber.json ikut diperbarui.

Kode keluar 0 bila semua cocok, 1 bila ada yang perlu diperbaiki.
"""

import argparse
import datetime
import gzip
import hashlib
import json
import re
import sys
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
from io import BytesIO
from pathlib import Path

try:
    sys.stdout.reconfigure(line_buffering=True)
except (AttributeError, ValueError):
    pass

BASE_DIR = Path(__file__).resolve().parent.parent
WILAYAH_TSV = BASE_DIR / "Data" / "Wilayah" / "wilayah.tsv.gz"
LOGO_DIR = BASE_DIR / "Resources" / "LogosKabupaten"
SUMBER_JSON = LOGO_DIR / "sumber.json"

ID_WIKI = "https://id.wikipedia.org/w/api.php"
WIKIDATA = "https://www.wikidata.org/w/api.php"
COMMONS = "https://commons.wikimedia.org/w/api.php"
COMMONS_FILE = "https://commons.wikimedia.org/wiki/Special:FilePath/"
COMMONS_WIKI = "https://commons.wikimedia.org"

# Cache unduhan di folder sementara: pengumpulan ~514 lambang bisa terputus
# (jaringan, 429, laptop tidur). Dengan cache, menjalankan ulang tidak
# mengunduh ulang berkas yang sudah pernah diambil — kalau tidak, setiap
# percobaan ulang membebani Wikimedia dengan ratusan permintaan yang sama.
CACHE_DIR = Path(tempfile.gettempdir()) / "SuDesApp-lambang-cache"

# UA harus menyebut identitas (kebijakan Wikimedia); UA generik pemicu HTTP 429.
USER_AGENT = (
    "SuDesApp/1.0 (aplikasi administrasi desa; verifikasi lambang kabupaten; "
    "dijalankan manual oleh pengembang)"
)

JEDA = 0.6          # antar permintaan API
# Unduhan berkas gambar lebih banyak jumlahnya daripada panggilan API (satu per
# lambang), jadi jedanya sedikit lebih longgar — Wikimedia membalas 429 bila
# permintaan terlalu rapat, dan jeda yang sopan jauh lebih murah daripada
# mengulang unduhan yang ditolak.
JEDA_UNDUH = 1.1
BATCH = 50          # batas aman API MediaWiki untuk query berjudul
EKSTENSI_GAMBAR = (".png", ".jpg", ".jpeg", ".bmp", ".gif")
EKSTENSI_ACUAN = EKSTENSI_GAMBAR + (".svg", ".tif", ".tiff", ".webp")

# Jarak dHash (0..64). Logo yang sama tapi dikompres ulang biasanya < 10; dua
# lambang berbeda jauh di atasnya. Dipakai sebagai bukti PENDUKUNG: logo dengan
# teks/crop berbeda bisa berjarak 15+ walau lambangnya sama, jadi byte identik
# dan nama berkas Commons tetap lebih menentukan.
AMBANG_HASH = 12

# Lebar render acuan. WAJIB salah satu ukuran baku Wikimedia (20, 40, 60, 120,
# 250, 330, 500, 960, 1280, 1920, 3840), kalau tidak permintaannya ditolak 400
# atau dibalas 429 terus-menerus. 960 px cukup tajam untuk kop surat dan
# menyimpan berkas lebih kecil daripada 1280.
LEBAR_ACUAN = 960


# ── HTTP ─────────────────────────────────────────────────────────────────
def http_ambil(url, maks_coba=5, jeda_tinggi=None):
    """Ambil isi URL; None bila gagal.

    `jeda_tinggi` memendekkan jeda untuk unduhan gambar: satu lambang punya
    sampai empat jalur cadangan, dan menunggu 2+4+8+16+30 detik di setiap jalur
    membuat satu berkas memakan menit. Untuk gambar cukup dua percobaan singkat
    lalu pindah ke jalur berikutnya.
    """
    for percobaan in range(maks_coba):
        try:
            req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
            with urllib.request.urlopen(req, timeout=60) as resp:
                return resp.read()
        except urllib.error.HTTPError as e:
            if e.code in (429, 500, 502, 503, 504):
                tunggu = min(30, 2 * (2 ** percobaan))
                time.sleep(min(tunggu, jeda_tinggi) if jeda_tinggi else tunggu)
                continue
            return None
        except (urllib.error.URLError, TimeoutError, OSError):
            tunggu = min(30, 2 * (2 ** percobaan))
            time.sleep(min(tunggu, jeda_tinggi) if jeda_tinggi else tunggu)
    return None


def api(host, params):
    """Panggil action API; None bila gagal atau balasannya bukan JSON.

    `maxlag` SENGAJA tidak dikirim: di Wikidata nilainya ikut menghitung lag
    query service (rutin ratusan detik), sehingga hampir semua permintaan
    ditolak dengan kode `maxlag` — bukan tanda kita terlalu cepat.
    """
    params = dict(params)
    params["format"] = "json"
    url = host + "?" + urllib.parse.urlencode(params)

    for percobaan in range(5):
        mentah = http_ambil(url)
        time.sleep(JEDA)
        if not mentah:
            return None
        try:
            data = json.loads(mentah.decode("utf-8"))
        except json.JSONDecodeError:
            return None

        if (data.get("error") or {}).get("code") in ("maxlag", "ratelimited"):
            time.sleep(min(20, 3 * (2 ** percobaan)))
            continue
        return data
    return None


# ── Data wilayah ─────────────────────────────────────────────────────────
def baca_kabupaten_kota():
    """{kode: (nama, 'b'|'k')} dari Data/Wilayah/wilayah.tsv.gz."""
    hasil = {}
    with gzip.open(WILAYAH_TSV, "rt", encoding="utf-8") as f:
        for baris in f:
            if baris.startswith("#"):
                continue
            b = baris.rstrip("\n").split("\t")
            if len(b) >= 4 and b[2] in ("b", "k") and b[0].count(".") == 1:
                hasil[b[0]] = (b[1].strip(), b[2].strip())
    return hasil


def judul_artikel(nama, jenis):
    return ("Kabupaten " if jenis == "b" else "Kota ") + nama


# ── Acuan 1: QID artikel ─────────────────────────────────────────────────
def qid_artikel(daftar):
    """{kode: QID} lewat properti pageprops artikel 'Kabupaten/Kota X'."""
    judul_ke_kode = {judul_artikel(nama, jenis): kode for kode, (nama, jenis) in daftar.items()}
    hasil = {}
    semuanya = list(judul_ke_kode)
    for mulai in range(0, len(semuanya), BATCH):
        potong = semuanya[mulai:mulai + BATCH]
        data = api(ID_WIKI, {
            "action": "query",
            "prop": "pageprops",
            "ppprop": "wikibase_item",
            "redirects": "1",
            "titles": "|".join(potong),
        })
        if not data:
            continue
        peta = {}
        for r in data.get("query", {}).get("redirects", []):
            peta[r.get("to", "")] = r.get("from", "")
        for r in data.get("query", {}).get("normalized", []):
            peta[r.get("to", "")] = r.get("from", "")
        for halaman in data.get("query", {}).get("pages", {}).values():
            judul = halaman.get("title", "")
            kode = judul_ke_kode.get(peta.get(judul, judul)) or judul_ke_kode.get(judul)
            qid = (halaman.get("pageprops") or {}).get("wikibase_item")
            if kode and qid:
                hasil[kode] = qid
    return hasil


# ── Acuan 2: lambang di Wikidata (P94/P158) ──────────────────────────────
def lambang_wikidata(qid_ke_kode):
    """{kode: (nama_berkas, properti)} dari klaim P94/P158.

    P94 = coat of arms, P158 = seal. Untuk kabupaten/kota Indonesia keduanya
    dipakai campur (Karawang di P94, Aceh Singkil di P158), jadi keduanya
    diperiksa.
    """
    hasil = {}
    qid_unik = sorted(qid_ke_kode)
    for mulai in range(0, len(qid_unik), BATCH):
        data = api(WIKIDATA, {
            "action": "wbgetentities",
            "ids": "|".join(qid_unik[mulai:mulai + BATCH]),
            "props": "claims",
        })
        if not data:
            continue
        for qid, entitas in (data.get("entities") or {}).items():
            kode = qid_ke_kode.get(qid)
            if not kode:
                continue
            klaim_semua = entitas.get("claims") or {}
            for properti in ("P94", "P158"):
                ketemu = None
                for k in klaim_semua.get(properti) or []:
                    nilai = (((k.get("mainsnak") or {}).get("datavalue") or {}).get("value"))
                    if isinstance(nilai, str) and nilai.lower().endswith(EKSTENSI_ACUAN):
                        ketemu = nilai
                        break
                if ketemu:
                    hasil[kode] = (ketemu, properti)
                    break
    return hasil


# ── Acuan 3: parameter `lambang` di infobox artikel ──────────────────────
POLA_LAMBANG = re.compile(r"(?im)^\s*\|\s*(?:lambang|logo|coat[ _]of[ _]arms)\s*=\s*(.+?)\s*$")


def lambang_wikipedia(daftar):
    """{kode: nama_berkas} dari wikitext artikel (parameter infobox)."""
    judul_ke_kode = {judul_artikel(nama, jenis): kode for kode, (nama, jenis) in daftar.items()}
    hasil = {}
    semuanya = list(judul_ke_kode)
    for mulai in range(0, len(semuanya), BATCH):
        potong = semuanya[mulai:mulai + BATCH]
        data = api(ID_WIKI, {
            "action": "query",
            "prop": "revisions",
            "rvprop": "content",
            "rvslots": "main",
            "redirects": "1",
            "titles": "|".join(potong),
        })
        if not data:
            continue
        for halaman in data.get("query", {}).get("pages", {}).values():
            kode = judul_ke_kode.get(halaman.get("title", ""))
            if not kode or halaman.get("missing"):
                continue
            try:
                isi = halaman["revisions"][0]["slots"]["main"]["*"]
            except (KeyError, IndexError):
                continue
            cari = POLA_LAMBANG.search(isi)
            if not cari:
                continue
            nilai = cari.group(1).strip()
            if nilai.startswith("[["):
                # Target tautan SELALU parameter pertama; parameter berikutnya
                # adalah ukuran/keterangan. Mengambil bagian[1] (kesalahan lama)
                # membuat "[[Berkas:X.png|150px]]" terbaca "150px" — bukan
                # berkas — sehingga lambang Sinjai dan Muna dinyatakan
                # "tanpa acuan" padahal artikelnya menyebut acuannya.
                nilai = nilai[2:].split("]]")[0].split("|")[0]
            # Lepaskan awalan ruang nama apa pun (Berkas:, File:, Gambar:, Image:).
            nilai = re.sub(r"(?i)^\s*(berkas|file|gambar|image)\s*:\s*", "", nilai)
            nilai = nilai.strip()
            if nilai.lower().endswith(EKSTENSI_ACUAN):
                hasil[kode] = nilai
    return hasil


# ── Commons ──────────────────────────────────────────────────────────────
def url_acuan_aman(nama_berkas):
    """URL unduhan yang tahan aturan lebar thumbnail Wikimedia.

    `Special:FilePath` mengikuti pengalihan ke ukuran thumbnail yang diizinkan
    (atau berkas aslinya bila lebih kecil), jadi ia tidak pernah kena HTTP 400
    "Use thumbnail sizes listed on …".
    """
    return COMMONS_FILE + urllib.parse.quote(nama_berkas.replace(" ", "_")) + f"?width={LEBAR_ACUAN}"


def url_thumb_php(nama_berkas):
    """URL render 960 px lewat host wiki (bukan CDN gambar)."""
    return COMMONS_WIKI + "/w/thumb.php?" + urllib.parse.urlencode(
        {"f": nama_berkas, "w": str(LEBAR_ACUAN)})


def _jalur_cache(nama_berkas):
    kunci = hashlib.sha1(nama_berkas.encode("utf-8")).hexdigest()
    return CACHE_DIR / (kunci + ".bin")


def baca_cache(nama_berkas):
    jalur = _jalur_cache(nama_berkas)
    try:
        data = jalur.read_bytes()
    except OSError:
        return None
    return data if len(data) >= 1024 else None


def simpan_cache(nama_berkas, data):
    try:
        CACHE_DIR.mkdir(parents=True, exist_ok=True)
        _jalur_cache(nama_berkas).write_bytes(data)
    except OSError:
        pass


def unduh_acuan(nama_berkas, info):
    """Unduh isi berkas acuan; kembalikan None bila semua jalur gagal.

    Urutan percobaannya sengaja begini (dibuktikan dengan mencoba langsung):

      1. `thumb.php` di host wiki — tetap melayani walau CDN gambar memblokir
         IP kita dengan 429;
      2. `thumburl` dari API — ukurannya sudah dibulatkan ke ukuran baku;
      3. `Special:FilePath?width=…` — mengikuti pengalihan, jalur terakhir
         sebelum menyerah;
      4. berkas ASLINYA — hanya bila lebarnya ≤ LEBAR_ACUAN **dan** bentuknya
         raster, supaya lambang kecil tidak diperbesar, tidak memaksa render
         baru, dan tidak ada SVG yang tersimpan dengan nama .png.
    """
    if not nama_berkas:
        return None

    cache = baca_cache(nama_berkas)
    if cache:
        return cache

    data_berkas = info.get(nama_berkas) or {}
    urls = [url_thumb_php(nama_berkas), data_berkas.get("thumb"), url_acuan_aman(nama_berkas)]

    lebar = data_berkas.get("lebar")
    if (isinstance(lebar, int) and 0 < lebar <= LEBAR_ACUAN
            and nama_berkas.lower().endswith(EKSTENSI_GAMBAR)):
        urls.append(data_berkas.get("url"))

    for url in urls:
        if not url:
            continue
        data = http_ambil(url, maks_coba=2, jeda_tinggi=3)
        if data and len(data) >= 1024 and ekstensi_isi(data):
            simpan_cache(nama_berkas, data)
            return data
    return None


# Tanda awal isi berkas gambar. Ekstensi tujuan ditentukan dari ISI, bukan dari
# nama berkas Commons: `w=960` mengembalikan JPEG untuk sumber JPEG/TIF (tidak
# bisa dibaca kalau disimpan sebagai .png), dan WebP tidak bisa dirender WPF
# sehingga harus diubah ke PNG dulu.
TANDA_GAMBAR = (
    (b"\x89PNG\r\n\x1a\n", ".png"),
    (b"\xff\xd8\xff", ".jpg"),
    (b"GIF87a", ".gif"),
    (b"GIF89a", ".gif"),
    (b"BM", ".bmp"),
)


def ekstensi_isi(data):
    for tanda, ext in TANDA_GAMBAR:
        if data.startswith(tanda):
            return ext
    if data[:4] == b"RIFF" and data[8:12] == b"WEBP":
        return ".webp"
    return None


def info_commons(daftar_berkas):
    """{nama_berkas: {sha1, url, thumb, aman}} untuk berkas di Commons."""
    hasil = {}
    unik = sorted(set(daftar_berkas))
    for mulai in range(0, len(unik), BATCH):
        potong = ["File:" + n for n in unik[mulai:mulai + BATCH]]
        # Satu batch bisa gagal sesaat (429/timeout); tanpa coba ulang, SEMUA
        # berkas di batch itu tampak "tidak ada di Commons" dan tidak pernah
        # dibandingkan — dulu 78 nama hilang begitu saja di satu putaran.
        data = None
        for cobain in range(3):
            data = api(COMMONS, {
                "action": "query",
                "prop": "imageinfo",
                "iiprop": "sha1|url|size|mime",
                "iiurlwidth": str(LEBAR_ACUAN),
                "titles": "|".join(potong),
            })
            if data:
                break
            time.sleep(5 * (cobain + 1))
        if not data:
            print(f"  imageinfo batch {mulai // BATCH + 1} gagal 3 kali", flush=True)
            continue
        for halaman in data.get("query", {}).get("pages", {}).values():
            info = (halaman.get("imageinfo") or [{}])[0]
            if not info or halaman.get("missing"):
                continue
            nama = halaman.get("title", "").removeprefix("File:")
            hasil[nama] = {
                "sha1": info.get("sha1"),
                "url": info.get("url"),
                "thumb": info.get("thumburl") or info.get("url"),
                "aman": url_acuan_aman(nama),
                # Lebar berkas ASLI dipakai untuk memutuskan boleh-tidaknya
                # mengunduh aslinya (tanpa memaksa render thumbnail baru).
                "lebar": info.get("width"),
            }
    return hasil


def cari_berkas_lewat_sha1(sha1):
    """Nama berkas Commons yang isinya persis SHA-1 ini; None bila tak ada."""
    for host in (COMMONS, ID_WIKI):
        data = api(host, {
            "action": "query",
            "list": "allimages",
            "aisha1": sha1,
            "aiprop": "url|size",
            "ailimit": "5",
        })
        if not data:
            continue
        gambar = data.get("query", {}).get("allimages") or []
        if gambar:
            return gambar[0].get("name")
    return None


# ── Perbandingan gambar ──────────────────────────────────────────────────
def gambar_dari_bytes(data):
    """Buka gambar, latar transparan diratakan ke putih (seperti saat dicetak)."""
    from PIL import Image

    Image.MAX_IMAGE_PIXELS = None  # lambang resmi bisa sangat besar (mis. 10.000 px)
    img = Image.open(BytesIO(data))
    img.load()
    if img.mode in ("RGBA", "LA", "P"):
        img = img.convert("RGBA")
        latar = Image.new("RGBA", img.size, (255, 255, 255, 255))
        img = Image.alpha_composite(latar, img).convert("RGB")
    else:
        img = img.convert("RGB")
    return img


def dhash(gambar, sisi=8):
    """dHash 64-bit — tahan kompresi & skala. Sisi 8 supaya hasilnya 64 bit."""
    kecil = gambar.convert("L").resize((sisi + 1, sisi))
    piksel = kecil.tobytes()
    bit = 0
    for y in range(sisi):
        for x in range(sisi):
            kiri = piksel[y * (sisi + 1) + x]
            kanan = piksel[y * (sisi + 1) + x + 1]
            bit = (bit << 1) | (1 if kiri > kanan else 0)
    return bit


def jarak_hash(a, b):
    return bin(a ^ b).count("1")


# ── Penilaian nama berkas ────────────────────────────────────────────────
def normalisasi_berkas(nama):
    return " ".join(nama.lower().replace("_", " ").replace("-", " ").split())


KATA_LAMBANG_BERKAS = ("lambang", "logo", "coat of arms", "seal", "emblem")
# Berkas yang namanya menyebut lambang, tapi bukan lambang daerahnya.
KATA_BUKAN_LAMBANG = ("dprd", "provinsi", "gubernur", "partai", "kecamatan",
                      "kabupaten lain", "peta", "map")


def nama_berkas_cocok(nama_berkas, nama_daerah):
    """True bila nama berkas Commons jelas menyebut lambang daerah ini."""
    j = normalisasi_berkas(nama_berkas)
    if any(k in j for k in KATA_BUKAN_LAMBANG):
        return False
    if not any(k in j for k in KATA_LAMBANG_BERKAS):
        return False
    return " ".join(nama_daerah.lower().split()) in j


def nama_ambigu(nama_berkas):
    """True bila nama berkas tidak menyebut kabupaten/kota.

    "Coat of arms of Bekasi" bisa berarti Kabupaten Bekasi ATAU Kota Bekasi;
    nama seperti itu tidak boleh dianggap bukti tanpa diperiksa kategorinya.
    """
    j = normalisasi_berkas(nama_berkas)
    return not any(k in j for k in ("kabupaten", "kota", "regency", "city"))


def kategori_commons(nama_berkas):
    """Kategori Commons untuk satu berkas (huruf kecil), None bila gagal."""
    data = api(COMMONS, {
        "action": "query",
        "prop": "categories",
        "cllimit": "max",
        "titles": "File:" + nama_berkas,
    })
    if not data:
        return None
    for halaman in data.get("query", {}).get("pages", {}).values():
        return [c.get("title", "").lower() for c in halaman.get("categories") or []]
    return None


def kategori_cocok(kategori, jenis):
    """Apakah kategori Commons tidak menunjuk jenis daerah yang salah.

    Contoh nyata: "Coat of arms of Bekasi.png" berkategori "Bekasi (city)" —
    lambang KOTA Bekasi, bukan kabupatennya (32.16). Tanpa pemeriksaan ini,
    nama yang sama membuat berkas salah kota lolos sebagai "versi lain".
    """
    teks = " | ".join(kategori)
    if jenis == "b":
        return not any(k in teks for k in ("(city)", "cities of indonesia", "city of"))
    return not any(k in teks for k in ("(regency)", "regencies of indonesia"))


# ── Acuan yang belum terisi: coba per kode ───────────────────────────────
def lengkapi_acuan(kode_kurang, daftar):
    """Isi acuan yang gagal pada batch pertama, satu kode sekali jalan.

    Batch besar bisa gagal sebagian (mis. satu QID tidak kembali); tanpa
    percobaan per kode, daerah itu tampak "tanpa acuan" dan berkasnya tidak
    pernah diperiksa — kesalahan yang tidak boleh dilaporkan sebagai SALAH.
    """
    qid_tambahan = {}
    for kode in kode_kurang:
        if kode in daftar:
            qid_tambahan.update(qid_artikel({kode: daftar[kode]}))
    return qid_tambahan


# ── Alur utama ───────────────────────────────────────────────────────────
def main():
    p = argparse.ArgumentParser(description="Verifikasi + perbaikan lambang kabupaten/kota")
    p.add_argument("--kode", default="", help="hanya kode tertentu, dipisah koma")
    p.add_argument("--semua", action="store_true",
                   help="periksa semua kabupaten/kota (termasuk berkas yang belum ada)")
    p.add_argument("--perbaiki", action="store_true",
                   help="unduh ulang berkas yang salah/hilang dari acuan, lalu tulis sumber.json")
    p.add_argument("--json", default="", help="simpan laporan terperinci ke berkas JSON")
    args = p.parse_args()

    nama_kode = baca_kabupaten_kota()

    # Berkas lokal per kode (satu kode bisa punya beberapa ekstensi).
    lokal = {}
    for f in sorted(LOGO_DIR.iterdir()):
        if f.suffix.lower() in EKSTENSI_GAMBAR and f.stem in nama_kode:
            lokal.setdefault(f.stem, []).append(f)

    if args.kode:
        ingin = {k.strip() for k in args.kode.split(",") if k.strip()}
        kode_scope = {k: nama_kode[k] for k in ingin if k in nama_kode}
        if not kode_scope:
            print("Kode tidak dikenal:", ", ".join(sorted(ingin)))
            return 1
    elif args.semua:
        kode_scope = dict(nama_kode)
    else:
        kode_scope = {k: nama_kode[k] for k in lokal}
        if not kode_scope:
            print("Tidak ada berkas untuk diperiksa.")
            return 0

    print(f"Memeriksa {len(kode_scope)} kabupaten/kota...\n", flush=True)
    qid = qid_artikel(kode_scope)
    kurang = [k for k in kode_scope if k not in qid]
    if kurang and len(kurang) <= 60:
        qid.update(lengkapi_acuan(kurang, kode_scope))
    print(f"  artikel → QID: {len(qid)}/{len(kode_scope)}", flush=True)

    wikidata = lambang_wikidata({q: k for k, q in qid.items()})
    wiki = lambang_wikipedia(kode_scope)
    print(f"  acuan lambang: wikidata {len(wikidata)}, infobox {len(wiki)}", flush=True)

    semua_acuan = [n for n, _ in wikidata.values()] + list(wiki.values())
    info = info_commons(semua_acuan)
    print(f"  berkas acuan di Commons: {len(info)}/{len(set(semua_acuan))}\n", flush=True)

    laporan = []
    cocok = 0
    for kode, (nama, jenis) in sorted(kode_scope.items()):
        berkas_kode = lokal.get(kode, [])
        acuan_nama, acuan_properti = wikidata.get(kode, (None, None))
        wiki_nama = wiki.get(kode)
        acuan_semua = [n for n in (acuan_nama, wiki_nama) if n]

        entri = {
            "kode": kode,
            "nama": nama,
            "jenis": jenis,
            "berkas": berkas_kode[0].name if berkas_kode else None,
            "acuan_berkas": acuan_nama,
            "acuan_properti": acuan_properti,
            "wiki_berkas": wiki_nama,
            "acuan_url": (info.get(acuan_nama or "") or {}).get("url"),
            "wiki_url": (info.get(wiki_nama or "") or {}).get("url"),
            "status": "",
            "jarak": {},
            "sebenarnya": None,
        }

        if not berkas_kode:
            entri["status"] = "HILANG" if acuan_semua else "TANPA_ACUAN"
            laporan.append(entri)
            print(f"{kode} {nama:24} {entri['status']}", flush=True)
            continue

        jalur = berkas_kode[0]
        mentah = jalur.read_bytes()
        entri["sha1_lokal"] = hashlib.sha1(mentah).hexdigest()

        if not acuan_semua:
            entri["status"] = "TANPA_ACUAN"
            laporan.append(entri)
            print(f"{kode} {nama:24} TANPA_ACUAN — tidak ada P94/P158 maupun `lambang`", flush=True)
            continue

        # 1. Byte identik dengan salah satu acuan — bukti terkuat.
        identik = next((n for n in acuan_semua if (info.get(n) or {}).get("sha1") == entri["sha1_lokal"]), None)
        if identik:
            entri["status"] = "COCOK"
            entri["bukti"] = "byte identik dengan " + identik
            cocok += 1
            laporan.append(entri)
            print(f"{kode} {nama:24} COCOK — byte identik ({identik})", flush=True)
            continue

        # 2. Bentuk mirip dengan render acuan.
        try:
            lokal_hash = dhash(gambar_dari_bytes(mentah))
        except Exception as e:
            entri["status"] = "RUSAK"
            entri["pesan"] = f"{type(e).__name__}: {e}"
            laporan.append(entri)
            print(f"{kode} {nama:24} RUSAK — {entri['pesan']}", flush=True)
            continue

        for label, n in (("wikidata", acuan_nama), ("wikipedia", wiki_nama)):
            if not n or n not in info:
                continue
            data_acuan = unduh_acuan(n, info)
            if not data_acuan:
                continue
            try:
                entri["jarak"][label] = jarak_hash(lokal_hash, dhash(gambar_dari_bytes(data_acuan)))
            except Exception:
                continue

        if entri["jarak"] and min(entri["jarak"].values()) <= AMBANG_HASH:
            entri["status"] = "COCOK"
            entri["bukti"] = "bentuk sama " + str(entri["jarak"])
            cocok += 1
            laporan.append(entri)
            print(f"{kode} {nama:24} COCOK — bentuk sama {entri['jarak']}", flush=True)
            continue

        # 3. Tidak mirip: cari tahu berkas apa yang sebenarnya terpasang.
        sebenarnya = cari_berkas_lewat_sha1(entri["sha1_lokal"])
        entri["sebenarnya"] = sebenarnya
        nama_cocok = bool(sebenarnya) and nama_berkas_cocok(sebenarnya, nama)
        if nama_cocok and nama_ambigu(sebenarnya or ""):
            # Nama tidak menyebut kabupaten/kota: periksa kategori Commons
            # sebelum mempercayainya (mis. "Coat of arms of Bekasi").
            kat = kategori_commons(sebenarnya or "")
            entri["kategori"] = kat
            nama_cocok = bool(kat) and kategori_cocok(kat, jenis)
            if not nama_cocok:
                entri["bukti_kategori"] = "kategori Commons menunjuk jenis daerah lain: " + str(kat)

        if nama_cocok:
            entri["status"] = "COCOK"
            entri["bukti"] = "versi lain; berkas Commons bernama " + sebenarnya
            cocok += 1
            laporan.append(entri)
            print(f"{kode} {nama:24} COCOK — versi lain ({sebenarnya})", flush=True)
            continue

        entri["status"] = "SALAH"
        entri["bukti"] = ("jarak " + str(entri["jarak"]) + "; terpasang "
                          + (sebenarnya or "(tidak dikenal Commons)"))
        laporan.append(entri)
        print(f"{kode} {nama:24} SALAH — {entri['bukti']}", flush=True)

    print(f"\nHASIL: {cocok} cocok, {len(laporan) - cocok} perlu diperbaiki, "
          f"dari {len(laporan)}", flush=True)

    if args.perbaiki:
        diperbaiki = perbaiki(laporan, info)
        print(f"\nDiperbaiki: {len(diperbaiki)} berkas", flush=True)
        for kode in diperbaiki:
            print(f"  {kode}", flush=True)
        tulis_sumber(laporan, info)

    if args.semua:
        tulis_daftar_hilang(laporan)

    if args.json:
        Path(args.json).write_text(json.dumps(laporan, indent=2, ensure_ascii=False), encoding="utf-8")
        print(f"Laporan: {args.json}", flush=True)

    perlu = [e for e in laporan if e["status"] not in ("COCOK",)]
    return 0 if not perlu else 1


def perbaiki(laporan, info):
    """Unduh berkas acuan untuk entri SALAH/HILANG/RUSAK; kembalikan kode yang diganti.

    Acuan yang dipilih adalah berkas yang dipakai artikel/Wikidata — bukan hasil
    pencarian bebas — sehingga yang terpasang selalu lambang yang sama dengan
    yang dilihat pembaca Wikipedia. Render 960 px dipakai supaya SVG pun
    tersimpan sebagai PNG tajam (WPF tidak bisa merender SVG).

    Unduhan yang gagal dikumpulkan lalu dicoba SEKALI lagi: kegagalan biasanya
    sesaat (429/timeout), dan satu putaran ulang jauh lebih murah daripada
    meninggalkan lubang yang tidak terlihat di laporan.
    """
    pending = [e for e in laporan if e["status"] in ("SALAH", "HILANG", "RUSAK")]
    diganti = []

    for putaran in (1, 2):
        sisa = []
        for entri in pending:
            if putaran == 2:
                time.sleep(JEDA_UNDUH * 4)
            if _perbaiki_satu(entri, info, diganti, putaran):
                continue
            sisa.append(entri)
        pending = sisa
        if not pending:
            break

    return diganti


def _perbaiki_satu(entri, info, diganti, putaran):
    """Unduh + verifikasi satu berkas. True bila berhasil."""
    # Acuan yang dipakai adalah berkas yang BENAR-BENAR ada di Commons:
    # berkas yang disebut Wikidata bisa saja sudah dihapus/diganti nama,
    # sedangkan infobox artikel menunjuk berkas lain yang masih ada.
    nama_acuan = next((n for n in (entri.get("acuan_berkas"), entri.get("wiki_berkas"))
                       if n and n in info), None)
    if not nama_acuan:
        print(f"  {entri['kode']} — tidak bisa diperbaiki (acuan tidak ada di Commons)", flush=True)
        return False

    mentah = unduh_acuan(nama_acuan, info)
    if not mentah:
        print(f"  {entri['kode']} — unduhan acuan gagal ({nama_acuan})"
              + (" — dicoba lagi" if putaran == 1 else ""), flush=True)
        return False

    # Bukti sebelum disimpan: SHA-1 isi dibandingkan dengan SHA-1 berkas acuan di
    # Commons. Sama = byte identik (tidak ada tebakan). Beda = ini render 960 px
    # dari acuan yang sama, dan ukurannya diperiksa agar tidak ada berkas rusak.
    sha1_acuan = (info.get(nama_acuan) or {}).get("sha1")
    sha1_isi = hashlib.sha1(mentah).hexdigest()
    entri["dipakai_berkas"] = nama_acuan
    entri["sha1_acuan"] = sha1_acuan
    entri["acuan_sama_byte"] = bool(sha1_acuan) and sha1_acuan == sha1_isi

    ext = ekstensi_isi(mentah)
    if ext == ".webp":
        # WPF tidak punya dekoder WebP; simpan sebagai PNG supaya lambangnya
        # benar-benar bisa dipakai di kop surat.
        try:
            buf = BytesIO()
            gambar_dari_bytes(mentah).save(buf, "PNG")
            mentah = buf.getvalue()
            ext = ".png"
        except Exception as e:
            print(f"  {entri['kode']} — WebP gagal diubah ke PNG ({type(e).__name__})", flush=True)
            return False

    if ext is None:
        print(f"  {entri['kode']} — isi acuan bukan gambar yang dikenali ({nama_acuan})", flush=True)
        return False

    # Berkas lama dengan ekstensi lain dihapus supaya hanya satu berkas per
    # kode — pencarian aplikasi tidak lagi bergantung urutan ekstensi.
    for lama in LOGO_DIR.glob(f"{entri['kode']}.*"):
        if lama.suffix.lower() in EKSTENSI_GAMBAR:
            lama.unlink()

    tujuan = LOGO_DIR / f"{entri['kode']}{ext}"
    tujuan.write_bytes(mentah)
    entri["berkas"] = tujuan.name
    entri["sha256_lokal"] = hashlib.sha256(mentah).hexdigest()
    entri["status"] = "DIPERBAIKI"
    diganti.append(entri["kode"])
    print(f"  {entri['kode']} {entri['nama']:24} {tujuan.name} "
          + ("byte identik" if entri["acuan_sama_byte"] else "render 960 px")
          + f" ({nama_acuan})", flush=True)
    time.sleep(JEDA_UNDUH)
    return True


def tulis_daftar_hilang(laporan):
    """Tulis LOGO_YANG_HILANG.txt — daftar kode yang belum punya berkas.

    Ditulis HANYA saat cakupannya seluruh kabupaten/kota (`--semua`): daftar ini
    menyebut total nasional, jadi menjalankannya untuk beberapa kode akan
    menghasilkan angka yang menyesatkan. Isinya dibuat dari hasil pemeriksaan
    yang baru saja berjalan — bukan dari sisa log unduhan, supaya tidak bisa
    lagi mengklaim "belum ada" untuk berkas yang sebenarnya sudah terpasang.
    """
    kurang = [e for e in laporan if not e.get("berkas")]
    baris = [
        "Logo lambang yang belum tersedia",
        f"Total belum ada: {len(kurang)} dari {len(laporan)} kabupaten/kota",
        "",
        "Diperbarui oleh verifikasi-logo-kabupaten.py (bukan hasil tebakan skrip unduh)",
        "",
    ]
    for e in sorted(kurang, key=lambda x: x["kode"]):
        if e["status"] == "TANPA_ACUAN":
            baris.append(f"{e['kode']}\t{e['nama']}\ttanpa acuan di Wikidata/Wikipedia")
        else:
            baris.append(f"{e['kode']}\t{e['nama']}\tbelum ada berkas")

    (LOGO_DIR / "LOGO_YANG_HILANG.txt").write_text("\n".join(baris) + "\n", encoding="utf-8")
    print(f"Daftar yang belum ada: {len(kurang)} kode → LOGO_YANG_HILANG.txt", flush=True)


def tulis_sumber(laporan, info):
    """Tulis sumber.json: asal-usul tiap berkas yang ada di folder."""
    entri = []
    for e in laporan:
        if not e.get("berkas"):
            continue
        jalur = LOGO_DIR / e["berkas"]
        if not jalur.exists():
            continue
        # Berkas acuan yang dicatat adalah yang benar-benar ada di Commons —
        # sama dengan yang dipakai saat mengunduh/menilai, supaya sumber.json
        # tidak pernah menunjuk berkas yang tidak ada.
        nama_acuan = next((n for n in (e.get("dipakai_berkas"), e.get("acuan_berkas"), e.get("wiki_berkas"))
                           if n and n in info), None)
        acuan = info.get(nama_acuan or "") or {}
        isi = jalur.read_bytes()
        entri.append({
            "kode": e["kode"],
            "nama": e["nama"],
            "berkas": e["berkas"],
            "sha256": hashlib.sha256(isi).hexdigest(),
            "sumber": acuan.get("url") or (url_acuan_aman(nama_acuan) if nama_acuan else None),
            "sumber_berkas": nama_acuan,
            # SHA-1 berkas acuan di Commons dan hasil perbandingannya dengan isi
            # yang tersimpan. `cocok_byte` true = berkas ini PERSIS berkas acuan,
            # bukan tafsiran. False berarti render 960 px / versi lain, dan itu
            # dinyatakan apa adanya — bukan dibulatkan menjadi "identik".
            "sumber_sha1": acuan.get("sha1"),
            "cocok_byte": bool(acuan.get("sha1")) and acuan.get("sha1") == hashlib.sha1(isi).hexdigest(),
            "diperiksa": datetime.date.today().isoformat(),
        })

    lama = {}
    if SUMBER_JSON.exists():
        try:
            lama = {e["kode"]: e for e in json.loads(SUMBER_JSON.read_text(encoding="utf-8"))}
        except (json.JSONDecodeError, KeyError):
            lama = {}
    for e in entri:
        lama[e["kode"]] = e

    SUMBER_JSON.write_text(
        json.dumps(sorted(lama.values(), key=lambda x: x["kode"]), indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8")
    print(f"Sumber: {SUMBER_JSON.name} ({len(lama)} berkas)", flush=True)


if __name__ == "__main__":
    sys.exit(main())
