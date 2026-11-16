#!/usr/bin/env python3
"""Kemas lambang kabupaten/kota menjadi SATU berkas arsip: Resources/LambangKabupaten.zip.

Latar: 514 berkas lambang (PNG/JPG/GIF) di Resources/LogosKabupaten/ berukuran
total ±214 MB dan seluruhnya ikut tersalin ke folder instalasi (csproj glob).
Padahal kop surat menggambar logo dalam kotak 60 pt (±250 px pada 300 dpi),
jadi ukuran sumber yang besar tidak menambah mutu cetak apa pun.

Skrip ini:
  1. membaca setiap lambang dari Resources/LogosKabupaten (sumber asal-usul
     tetap di sana — tidak diubah, tidak dipindah);
  2. menyesuaikan sisi terpanjang menjadi maksimum 512 px (LANCZOS);
  3. mengkuantisasi menjadi maksimum 256 warna (heraldry = warna datar,
     dithering Floyd-Steinberg menjaga gradasi);
  4. menulis entri <kode>.png ke arsip ZIP dengan penanda waktu tetap supaya
     hasilnya deterministik (hash arsip sama bila sumbernya sama).

Jalankan ulang setiap kali isi Resources/LogosKabupaten berubah (mis. hasil
Tools/verifikasi-logo-kabupaten.py --perbaiki), lalu commit arsipnya:

    py -3 Tools/siapkan-lambang-kabupaten.py

Berkas lama di Resources/LambangKabupaten.zip ditimpa utuh.
"""

import io
import os
import sys
import zipfile
from pathlib import Path

try:
    from PIL import Image
except ImportError:
    print("Butuh Pillow: py -3 -m pip install pillow")
    sys.exit(1)

BASE = Path(__file__).resolve().parent.parent
SUMBER = BASE / "Resources" / "LogosKabupaten"
ARSIP = BASE / "Resources" / "LambangKabupaten.zip"

SISI_MAKS = 512
WARNA_MAKS = 256
# Penanda waktu tetap (UTC): zip deterministik — hash tidak bergeser karena
# jam pembuat, sehingga diff git hanya muncul bila isinya benar-benar berubah.
STempel = (2026, 1, 1, 0, 0, 0)
EKSTENSI = {".png", ".jpg", ".jpeg", ".bmp", ".gif"}


def siapkan_gambar(jalur: Path) -> bytes:
    """Satu lambang → PNG ≤512 px, ≤256 warna, teroptimal (bytes)."""
    im = Image.open(jalur)

    # GIF animasi: ambil bingkai pertama (kop statis; WPF juga merender frame pertama).
    getattr(im, "seek", lambda i: None)(0)

    # Mode transparansi eksplisit: P dengan info transparansi → RGBA supaya
    # latar tidak menghitam saat diquantize.
    if im.mode == "P" and "transparency" in im.info:
        im = im.convert("RGBA")
    elif im.mode not in ("RGBA", "RGB", "L"):
        im = im.convert("RGBA")

    w, h = im.size
    skala = min(1.0, SISI_MAKS / max(w, h))
    if skala < 1.0:
        im = im.resize((round(w * skala), round(h * skala)), Image.LANCZOS)

    if im.mode not in ("P", "L"):
        # FASTOCTREE satu-satunya metode yang mendukung RGBA sekaligus RGB.
        im = im.quantize(colors=WARNA_MAKS, method=Image.FASTOCTREE,
                         dither=Image.FLOYDSTEINBERG)

    buf = io.BytesIO()
    im.save(buf, format="PNG", optimize=True)
    return buf.getvalue()


def main() -> int:
    if not SUMBER.is_dir():
        print(f"Folder sumber tidak ada: {SUMBER}")
        return 1

    kode_seen = {}
    berkas = sorted(
        f for f in SUMBER.iterdir()
        if f.is_file() and f.suffix.lower() in EKSTENSI
    )
    if not berkas:
        print(f"Tidak ada gambar di {SUMBER}")
        return 1

    ukuran_sebelum = 0
    ukuran_isi = 0
    entri = []
    for f in berkas:
        kode = f.stem
        if kode in kode_seen:
            print(f"DILEWATI — kode {kode} lebih dari satu berkas: {f.name}")
            continue
        kode_seen[kode] = f.name

        data = siapkan_gambar(f)
        ukuran_sebelum += f.stat().st_size
        ukuran_isi += len(data)
        entri.append((kode, data))

    entri.sort(key=lambda x: x[0])
    ukuran_sebelum += sum(
        f.stat().st_size for f in SUMBER.iterdir()
        if f.is_file() and f.suffix.lower() in EKSTENSI and f.stem not in kode_seen
    )

    if ARSIP.exists():
        ukuran_sebelum += 0  # arsip lama dilaporkan terpisah
    lama = ARSIP.stat().st_size if ARSIP.exists() else 0

    with zipfile.ZipFile(ARSIP, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for kode, data in entri:
            info = zipfile.ZipInfo(f"{kode}.png", date_time=STempel)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16
            z.writestr(info, data)

    ukuran_arsip = ARSIP.stat().st_size
    print(f"Lambang  : {len(entri)} kode")
    print(f"Sumber   : {ukuran_sebelum/1e6:7.1f} MB (folder LogosKabupaten)")
    print(f"Isi arsip: {ukuran_isi/1e6:7.1f} MB (PNG ≤512 px, ≤256 warna)")
    print(f"Arsip    : {ARSIP.name} = {ukuran_arsip/1e6:7.1f} MB"
          + (f"  (sebelumnya {lama/1e6:.1f} MB)" if lama else ""))
    print(f"Berkas   : {ARSIP}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
