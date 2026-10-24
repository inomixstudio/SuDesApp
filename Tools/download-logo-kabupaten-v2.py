#!/usr/bin/env python3
"""
Unduh lambang kabupaten/kota dari Wikipedia untuk kop surat SuDesApp.

Output: Resources/LogosKabupaten/{kode_kemendagri}.png
        (isi berkas bisa PNG/JPEG/SVG-dirender — Lihat CATATAN FORMAT di bawah.)

Menggantikan skrip pengunduh lama yang gagal di 408/514 berkas.
Tiga penyebab utama versi lama:

  1.  list=search + ambil hasil pertama. Rentan memilih artikel lain, dan
      untuk kota hasil pertama sering artikel provinsi.
  2.  REQUEST_DELAY = 0.3 detik (~3 permintaan/detik) memicu HTTP 429 dari
      id.wikipedia.org.
  3.  Filter gambar hanya mencari kata "lambang"/"coat"/"emblem", sehingga
      "Coat of arms of Aceh" (lambang PROVINSI) ikut terpilih untuk setiap
      kabupaten di Aceh.

Perbaikan: judul artikel deterministik, filter wajib mengandung NAMA
kabupaten/kota, permintaan API dibatch (50 judul sekali jalan), dan jeda
antar-unduhan ditambah.

CATATAN FORMAT: Wikipedia menyimpan sebagian lambang sebagai SVG/JPEG. PIL
tidak tersedia di lingkungan ini, jadi SVG dirender lewat endpoint thumbnail
(kembalikan PNG) dan JPEG dibiarkan apa adanya dengan ekstensi .png. Itu aman:
LogoKabupaten.Cari mengenali .png/.jpg/.jpeg/.bmp/.gif dan WPF membaca gambar
berdasarkan ISI berkas, bukan ekstensinya.

HASIL SKRIP INI BELUM TENTU BENAR. Ia memilih berkas dari daftar gambar artikel
berdasarkan kata "lambang", dan cara itu pernah memasang lambang PROVINSI untuk
kabupaten (Aceh), logo DPRD untuk kota (Bekasi), dan lambang Kota Bekasi untuk
Kabupaten Bekasi. Karena itu setelah mengunduh WAJIB jalankan:

    py -3 Tools/verifikasi-logo-kabupaten.py --perbaiki

Alat itu mencocokkan tiap berkas dengan Wikidata (P94/P158) + infobox artikel +
Commons, mengganti yang salah, dan menulis sumber.json yang dijaga uji.
"""

import argparse
import gzip
import json
import random
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

# stdout dibuat line-buffered: tanpa ini, keluaran ditahan di penyangga Python
# saat dialihkan ke berkas/pipa, sehingga log proses latar belakang muncul
# kosong sampai proses selesai — padahal justru di situ progres paling dibutuhkan.
try:
    sys.stdout.reconfigure(line_buffering=True)
except (AttributeError, ValueError):
    pass

# ── Konfigurasi ──────────────────────────────────────────────────────────
BASE_DIR = Path(__file__).resolve().parent.parent
WILAYAH_TSV = BASE_DIR / "Data" / "Wilayah" / "wilayah.tsv.gz"
OUTPUT_DIR = BASE_DIR / "Resources" / "LogosKabupaten"
WIKI_API = "https://id.wikipedia.org/w/api.php"
THUMB_SIZE = 512

# User-Agent WAJIB memuat identitas & kontak (kebijakan Wikimedia). UA generik
# seperti "SuDesApp-LogoDownloader/1.0" adalah salah satu pemicu 429.
USER_AGENT = (
    "SuDesApp/1.0 (aplikasi administrasi desa; pengunduh lambang kabupaten; "
    "dijalankan manual oleh pengembang)"
)

# Jeda antar-permintaan. API dibatch supaya jumlah permintaan jaringan kecil;
# yang tidak bisa dibatch adalah unduhan berkas, jadi jeda ada di situ.
#
# ANGKA 429: id.wikipedia.org menolak permintaan saat kita mengirim terlalu
# cepat (versi lama skrip mengirim ~3 permintaan/detik). Kedua jeda di bawah
# sengaja lebih longgar daripada kelihatannya perlu: menghormati batas server
# jauh lebih murah daripada menunggu blokir IP berjam-jam.
JEDA_API = 1.5      # antar batch API
JEDA_UNDUH = 0.8    # antar unduhan berkas
BATCH_API = 50

# Percobaan ulang & batas mundur. 429 pada dasarnya server berkata "melambat",
# jadi mundur harus cukup panjang untuk benar-benar meredakan tekanan.
MAKS_COBA = 7
MUNDUR_MAKS = 60

# Batch pemindaian daftar gambar SENGAJA kecil. Properti `images` punya anggaran
# TOTAL per query (bukan per halaman): batch 50 judul membuat ~28 halaman kembali
# tanpa gambar sama sekali. Dengan 10 judul, semua halaman tetap terisi penuh.
BATCH_LIST_GAMBAR = 10

# Kata yang menandakan berkas lambang.
KATA_LAMBANG = ("lambang", "coat of arms", "logo of", "insignia")
# Berkas yang selalu salah pilih walau mengandung kata di atas.
KATA_SAMARAN = (
    "wikisource", "wikipedia", "commons", "wikidata", "favicon", "icon",
    "padlock", "folder", "question book", "ambox", "edit", "red pencil",
)


# ── Baca daftar kabupaten/kota ───────────────────────────────────────────
def baca_kabupaten_kota():
    """Ambil (kode, nama, jenis) untuk semua kabupaten/kota dari wilayah.tsv."""
    hasil = []
    with gzip.open(WILAYAH_TSV, "rt", encoding="utf-8") as f:
        for baris in f:
            baris = baris.rstrip("\n")
            if baris.startswith("#"):
                continue
            bagian = baris.split("\t")
            if len(bagian) < 4:
                continue
            kode, nama, jenis = bagian[0].strip(), bagian[1].strip(), bagian[2].strip()
            if jenis in ("b", "k"):
                hasil.append((kode, nama, jenis))
    return hasil


# ── HTTP ─────────────────────────────────────────────────────────────────
def http_ambil(url, maks_coba=MAKS_COBA):
    """GET dengan percobaan ulang + backoff eksponensial saat 429/5xx."""
    for percobaan in range(maks_coba):
        try:
            req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
            with urllib.request.urlopen(req, timeout=60) as resp:
                return resp.read()
        except urllib.error.HTTPError as e:
            if e.code in (429, 500, 502, 503, 504):
                # 429: server minta melambat. Mundur lebih panjang dari 1 detik
                # supaya tekanan benar-benar turun, bukan sekadar mencoba lagi.
                tunggu = min(MUNDUR_MAKS, 3 * (2 ** percobaan) + random.random() * 3)
                print(f"    [HTTP {e.code}] mundur {tunggu:.0f}s (percobaan {percobaan + 1}/{maks_coba})",
                      flush=True)
                time.sleep(tunggu)
                continue
            raise
        except (urllib.error.URLError, TimeoutError, OSError) as e:
            tunggu = min(MUNDUR_MAKS, 3 * (2 ** percobaan) + random.random() * 3)
            print(f"    [jaringan] {e}; mundur {tunggu:.0f}s", flush=True)
            time.sleep(tunggu)
    return None


def wiki_api(params):
    """Satu pemanggilan Wikipedia API; None bila tetap gagal."""
    params["format"] = "json"
    params["maxlag"] = "5"
    url = WIKI_API + "?" + urllib.parse.urlencode(params)
    mentah = http_ambil(url)
    if mentah is None:
        return None
    try:
        return json.loads(mentah.decode("utf-8"))
    except json.JSONDecodeError:
        return None


# ── Pencarian lambang ────────────────────────────────────────────────────
def normalisasi(teks):
    return " ".join(teks.lower().split())


def judul_artikel(nama, jenis):
    """Judul artikel deterministik untuk satu kabupaten/kota."""
    return ("Kabupaten " if jenis == "b" else "Kota ") + nama


def berkas_lambang_terbaik(nama, daftar_berkas):
    """Pilih berkas lambang paling mungkin untuk kabupaten/kota ini.

    Syarat WAJIB: judul berkas memuat nama kabupaten/kota. Tanpa ini, setiap
    kabupaten di Aceh akan memilih "Coat of arms of Aceh" — lambang provinsi.
    """
    kunci = normalisasi(nama)
    calon = []
    for judul in daftar_berkas:
        j = normalisasi(judul)
        if not any(k in j for k in KATA_LAMBANG):
            continue
        if any(s in j for s in KATA_SAMARAN):
            continue
        if kunci not in j:
            continue
        calon.append(judul)

    if not calon:
        return None

    # Urutan preferensi: nama Indonesia lebih dulu, lalu Inggris, lalu lain.
    def skor(judul):
        j = normalisasi(judul)
        if j.startswith(("berkas:lambang kabupaten", "berkas:lambang kota")):
            return 0
        if "lambang" in j:
            return 1
        if "coat of arms" in j:
            return 2
        return 3

    return sorted(calon, key=skor)[0]


def kumpulkan_judul_artikel(daftar):
    """Judul artikel deterministik — bukan hasil pencarian.

    redirects=1 mengikuti perpiindahan artikel, srsearch tidak dipakai lagi.
    """
    judul = []
    for _kode, nama, jenis in daftar:
        prefiks = "Kabupaten" if jenis == "b" else "Kota"
        judul.append(f"{prefiks} {nama}")
    return judul


def tebak_judul_berkas(nama, jenis):
    """Kandidat judul berkas lambang, paling mungkin lebih dulu.

    Pengenalan lewat daftar gambar artikel ternyata rawan: properti `images`
    punya ANGGARAN TOTAL per query (imlimit), jadi dalam satu batch 50 judul
    sebagian halaman kembali tanpa gambar sama sekali. Menebak judul berkas
    tidak punya masalah itu — satu judul menghasilkan satu hasil.

    Hanya 5 kandidat teratas. Setiap kandidat tambahan menambah permintaan API,
    dan permintaan API itulah yang memicu 429. Sisanya ditangani Fase 2
    (pemindaian daftar gambar), yang memang dirancang untuk kasus tak terduga.
    """
    prefiks = "Kabupaten" if jenis == "b" else "Kota"
    kembar = [
        f"Lambang {prefiks} {nama}.png",
        f"Lambang {prefiks} {nama}.svg",
        f"Coat of arms of {nama}.svg",
        f"Lambang {prefiks} {nama}.jpg",
        f"Coat of arms of {nama}.png",
    ]
    return [f"Berkas:{t}" for t in kembar]


def resolusi_lewat_tebakan(daftar):
    """FASE 1: tebak judul berkas lalu resolve lewat prop=imageinfo.

    Dipakai lebih dulu karena tak bergantung pada anggaran imlimit. Mengembalikan
    {kode: judul_berkas}. Judul yang tidak ada diabaikan.
    """
    kode_ke_judul = {}
    berkas_ke_kode = {}

    for kode, nama, jenis in daftar:
        for judul in tebak_judul_berkas(nama, jenis):
            # Judul yang sama boleh muncul untuk beberapa kode (mis. beberapa
            # "Lambang Kabupaten Aceh" tebakan) — kemunculan pertama menang.
            berkas_ke_kode.setdefault(judul, kode)

    semuanya = list(berkas_ke_kode.keys())
    print(f"  Menebak {len(semuanya)} judul berkas ({len(daftar)} kabupaten/kota)...", flush=True)
    for mulai in range(0, len(semuanya), BATCH_API):
        potong = semuanya[mulai:mulai + BATCH_API]
        data = wiki_api({
            "action": "query",
            "prop": "imageinfo",
            "iiprop": "url|mime",
            "titles": "|".join(potong),
        })
        if not data:
            continue
        time.sleep(JEDA_API)

        for halaman in data.get("query", {}).get("pages", {}).values():
            judul = halaman.get("title", "")
            if halaman.get("missing") or "imageinfo" not in halaman:
                continue
            info = halaman["imageinfo"][0]
            # Hanya gambar; jangan ambil PDF/berkas apa pun yang kebetulan ada.
            if not (info.get("mime") or "").startswith("image/"):
                continue
            kode = berkas_ke_kode.get(judul)
            if kode:
                kode_ke_judul[kode] = judul

    return kode_ke_judul


def resolusi_lewat_daftar(daftar, sudah_dapat):
    """FASE 2: untuk sisa yang gagal ditebak, baca daftar gambar artikelnya.

    Batch SENGAJA kecil (10): imlimit adalah anggaran total seluruh halaman
    dalam satu query, jadi batch besar membuat halaman sebagian besar kembali
    tanpa gambar. Lebih banyak permintaan, tapi tidak ada yang hilang.
    """
    sisa = [d for d in daftar if d[0] not in sudah_dapat]
    if not sisa:
        return {}

    print(f"  Memindai daftar gambar artikel untuk {len(sisa)} sisa (batch {BATCH_LIST_GAMBAR})...", flush=True)
    judul_ke_kode = {}
    for kode, nama, jenis in sisa:
        judul_ke_kode.setdefault(judul_artikel(nama, jenis), []).append((kode, nama, jenis))

    semuanya = list(judul_ke_kode.keys())
    hasil = {}

    for mulai in range(0, len(semuanya), BATCH_LIST_GAMBAR):
        potong = semuanya[mulai:mulai + BATCH_LIST_GAMBAR]
        data = wiki_api({
            "action": "query",
            "prop": "images",
            "titles": "|".join(potong),
            "imlimit": "max",
            "redirects": "1",
        })
        if not data:
            continue
        time.sleep(JEDA_API)

        halaman = {p["title"]: p for p in data.get("query", {}).get("pages", {}).values()}
        for judul_tertulis, pasangan in judul_ke_kode.items():
            if judul_tertulis not in potong:
                continue
            info = halaman.get(judul_tertulis)
            if not info:
                continue
            gambar = [g.get("title", "") for g in info.get("images", [])]
            if not gambar:
                continue
            kode, nama, _ = pasangan[0]
            berkas = berkas_lambang_terbaik(nama, gambar)
            if berkas:
                hasil[kode] = berkas

    return hasil


def unduh_berkas(nama_berkas, tujuan):
    """Ambil URL asli lalu unduh (SVG dirender menjadi PNG via thumbnail)."""
    data = wiki_api({
        "action": "query",
        "prop": "imageinfo",
        "iiprop": "url|size|mime",
        "iiurlwidth": str(THUMB_SIZE),
        "titles": nama_berkas,
    })
    if not data:
        return False, "tidak bisa resolve URL"

    halaman = list(data.get("query", {}).get("pages", {}).values())
    if not halaman or "imageinfo" not in halaman[0]:
        return False, "tidak ada imageinfo"

    info = halaman[0]["imageinfo"][0]
    mime = info.get("mime", "")
    asli = info.get("url")

    if not asli:
        return False, "URL kosong"

    # SVG: selalu lewat thumbnail supaya jadi PNG (WPF tak bisa render SVG).
    if mime == "image/svg+xml":
        mentah = http_ambil(
            "https://id.wikipedia.org/w/index.php?title=Special:Redirect/file/"
            + urllib.parse.quote(nama_berkas.split(":", 1)[-1])
            + f"&width={THUMB_SIZE}"
        )
    else:
        mentah = http_ambil(asli)

    if not mentah:
        return False, f"unduhan gagal ({mime or 'mime?'})"

    if not GambarValid(mentah):
        return False, f"bukan gambar (mime={mime}, {len(mentah)} byte)"

    # Minimal 1 KB: di bawah itu hampir pasti halaman galat, bukan gambar.
    if len(mentah) < 1024:
        return False, f"terlalu kecil ({len(mentah)} byte)"

    tujuan.write_bytes(mentah)
    return True, f"{mime}, {len(mentah)} byte"


def gambar_ada_dan_valid(kode):
    """True bila berkas sudah ada DAN isinya benar-benar gambar.

    Sikap "sudah ada = sudah selesai" tidak aman di sini: skrip versi pertama
    menulis berkas walau isinya bukan PNG (ia hanya memberi peringatan), jadi
    ada berkas .png berisi SVG mentah 5 MB. Tanpa pemeriksaan ini, berkas rusak
    seperti itu akan dilewati selamanya.
    """
    berkas = OUTPUT_DIR / f"{kode}.png"
    if not berkas.exists():
        return False
    try:
        with open(berkas, "rb") as f:
            kepala = f.read(16)
    except OSError:
        return False
    return GambarValid(kepala)


def GambarValid(data):
    """True bila byte ini PNG atau JPEG — ekstensi berkasnya .png boleh berisi JPEG."""
    return (
        data[:8] == b"\x89PNG\r\n\x1a\n"
        or data[:3] == b"\xff\xd8\xff"
        or data[:4] == b"GIF8"
        or data[:2] == b"BM"
    )


# ── Main ────────────────────────────────────────────────────────────────
def main():
    p = argparse.ArgumentParser(description="Unduh lambang kabupaten/kota dari Wikipedia")
    p.add_argument("--batas", type=int, default=0,
                   help="batasi jumlah unduhan (untuk uji coba)")
    p.add_argument("--kode", type=str, default="",
                   help="hanya kode tertentu, dipisah koma (mis. 11.01,11.71)")
    p.add_argument("--ulang", action="store_true",
                   help="hanya ulang yang tercatat gagal di LOGO_YANG_HILANG.txt")
    args = p.parse_args()

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    print("Membaca data wilayah...", flush=True)
    daftar = baca_kabupaten_kota()
    print(f"Ditemukan {len(daftar)} kabupaten/kota", flush=True)
    # --- Siapa yang perlu diunduh ---
    if args.kode:
        ingin = {k.strip() for k in args.kode.split(",") if k.strip()}
        daftar = [d for d in daftar if d[0] in ingin]
    elif args.ulang:
        ingin = baca_log_gagal()
        daftar = [d for d in daftar if d[0] in ingin]

    # Berkas yang ada TETAP diperiksa isinya: skrip versi pertama sempat menulis
    # berkas .png berisi SVG mentah, jadi "sudah ada" tidak boleh berarti "sudah
    # benar". Yang rusak diunduh ulang, bukan dilewati selamanya.
    rusak = [d for d in daftar
             if (OUTPUT_DIR / f"{d[0]}.png").exists() and not gambar_ada_dan_valid(d[0])]
    if rusak:
        print(f"\n[!!] {len(rusak)} berkas ada tapi isinya bukan gambar — akan diunduh ulang:", flush=True)
        for kode, nama, _ in rusak:
            print(f"     {kode} {nama}", flush=True)

    perlu = [d for d in daftar if not gambar_ada_dan_valid(d[0])]
    sudah = len(daftar) - len(perlu)
    print(f"Perlu diunduh: {len(perlu)} (sudah ada & valid: {sudah})", flush=True)
    if args.batas > 0:
        perlu = perlu[:args.batas]
        print(f"Dibatasi uji coba: {len(perlu)}");

    if not perlu:
        print("Tidak ada yang perlu diunduh.", flush=True)
        return 0

    # --- Tahap 1: kumpulkan judul berkas lambang ---
    print("\n[Tahap 1] Menentukan judul berkas lambang...", flush=True)
    print("  Fase 1/2 — tebak judul berkas (tanpa masalah anggaran imlimit):", flush=True)
    url_kode = resolusi_lewat_tebakan(perlu)
    print(f"  Tertebak: {len(url_kode)} dari {len(perlu)}", flush=True)
    if len(url_kode) < len(perlu):
        print(f"  Fase 2/2 — pindai daftar gambar artikel untuk {len(perlu) - len(url_kode)} sisa:", flush=True)
        url_kode.update(resolusi_lewat_daftar(perlu, url_kode))
    print(f"  Total lambang ditemukan: {len(url_kode)} dari {len(perlu)}", flush=True)
    # --- Tahap 2: unduh ---
    print(f"\n[Tahap 2] Mengunduh (jeda {JEDA_UNDUH}s)...", flush=True)
    sukses, gagal = 0, []

    for i, (kode, nama, jenis) in enumerate(perlu, 1):
        tujuan = OUTPUT_DIR / f"{kode}.png"
        nama_berkas = url_kode.get(kode)

        if not nama_berkas:
            gagal.append((kode, nama, "lambang tidak ditemukan di artikel"))
            print(f"[{i}/{len(perlu)}] {kode} {nama} — TIDAK DITEMUKAN", flush=True)
            continue

        print(f"[{i}/{len(perlu)}] {kode} {nama} — {nama_berkas}", flush=True)
        ok, ket = unduh_berkas(nama_berkas, tujuan)
        if ok:
            sukses += 1
            print(f"    tersimpan: {ket}", flush=True)
        else:
            gagal.append((kode, nama, ket))
            tujuan.unlink(missing_ok=True)
            print(f"    GAGAL: {ket}", flush=True)
        time.sleep(JEDA_UNDUH)

    # --- Ringkasan ---
    print(f"\n{'=' * 60}", flush=True)
    print(f"HASIL: berhasil {sukses}, gagal {len(gagal)}, dilewati {sudah}", flush=True)
    print(f"Total berkas di folder: {len(list(OUTPUT_DIR.glob('*.png')))} / {len(daftar and baca_kabupaten_kota())}", flush=True)
    print("\nJangan lupa: py -3 Tools/verifikasi-logo-kabupaten.py --perbaiki", flush=True)

    if gagal:
        print(f"\n{'=' * 60}", flush=True)
        print("DAFTAR GAGAL:", flush=True)
        for kode, nama, alasan in gagal:
            print(f"  {kode:8} {nama:38} ({alasan})", flush=True)
        log = OUTPUT_DIR / "LOGO_YANG_HILANG.txt"
        with open(log, "w", encoding="utf-8") as f:
            f.write("Logo lambang yang tidak ditemukan otomatis\n")
            f.write(f"Total gagal: {len(gagal)} dari 514 kabupaten/kota\n\n")
            f.write("Digambar ulang oleh download-logo-kabupaten-v2.py\n\n")
            for kode, nama, alasan in gagal:
                f.write(f"{kode}\t{nama}\t{alasan}\n")

    return 0 if not gagal else 1


def baca_log_gagal():
    """Ambil kode dari LOGO_YANG_HILANG.txt untuk mode --ulang."""
    log = OUTPUT_DIR / "LOGO_YANG_HILANG.txt"
    kode = set()
    if not log.exists():
        return kode
    for baris in log.read_text(encoding="utf-8").splitlines():
        bagian = baris.split("\t")
        if bagian and len(bagian[0].split(".")) == 2 and bagian[0][0].isdigit():
            kode.add(bagian[0].strip())
    return kode


if __name__ == "__main__":
    sys.exit(main())
