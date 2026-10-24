# -*- coding: utf-8 -*-
"""Buat berkas font Google Sans yang bisa ditanam ke aplikasi WPF.

Sumber: subset WOFF2 Google Sans (lisensi SIL OFL 1.1) dari folder font
antarmuka aplikasi sumber yang memasangnya.

Yang dihasilkan di SuDesApp.Wpf/Fonts/:
  - GoogleSans-Regular.ttf  — instans statis wght=400 (subset latin)
  - GoogleSans-Bold.ttf      — instans statis wght=700 (subset latin)
  - GoogleSans.ttc           — koleksi keduanya dengan nama keluarga
                               "Google Sans" sehingga WPF menemukan muka
                               Regular DAN Bold yang asli (bukan faux bold)
  - OFL.txt                  — lisensi font (wajib ikut saat font ditanam)

Jalankan dengan Python yang punya fonttools+brotli, mis.:
    py -3.14 Tools/embed-google-sans.py
"""

from __future__ import annotations

import shutil
import struct
import sys
from pathlib import Path

from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

AKAR = Path(__file__).resolve().parent.parent

# Lokasi instalasi aplikasi sumber (default) — bisa ditimpa lewat argumen CLI.
# Dicari lewat pola agar tidak bergantung pada nama folder pemasangan tertentu.
def folder_sumber_bawaan() -> Path:
    program = Path.home() / "AppData/Local/Programs"
    for kandidat in program.glob("*/resources/orchestrator/ui/fonts/google-sans"):
        return kandidat
    return program / "desktop/resources/orchestrator/ui/fonts/google-sans"

KELUARGA = "Google Sans"


def instans_static(woff2: Path, bobot: int) -> TTFont:
    """Buka subset variable dan kunci satu nilai bobot (400/700)."""
    font = TTFont(str(woff2))
    if "fvar" in font:
        font = instancer.instantiateVariableFont(font, {"wght": bobot}, updateFontNames=False)
    return font


def rapikan_nama(font: TTFont, bobot: int, subfamily: str) -> None:
    """Samakan nama keluarga di seluruh tabel nama + tandai gaya tebal."""
    bold = bobot >= 700
    nama = font["name"]
    for rec in nama.names:
        teks = rec.toUnicode()
        if teks in ("Google Sans", "Google Sans Regular", "Google Sans Bold", "Variable",
                    "Google Sans Variable") or teks.startswith("Google Sans "):
            if rec.nameID in (1, 16):   # family / typographic family
                rec.string = KELUARGA
            elif rec.nameID == 4:       # full name
                rec.string = f"{KELUARGA} {subfamily}"
            elif rec.nameID == 6:       # postscript name
                rec.string = f"{KELUARGA}-{subfamily}"
    # Tambah nama standar bila salah satu absen.
    for pid, eid, lid in ((3, 1, 0x409), (1, 0, 0)):
        if nama.getName(1, pid, eid, lid) is None:
            nama.setName(KELUARGA, 1, pid, eid, lid)
        if nama.getName(2, pid, eid, lid) is None:
            nama.setName(subfamily, 2, pid, eid, lid)
        if nama.getName(4, pid, eid, lid) is None:
            nama.setName(f"{KELUARGA} {subfamily}", 4, pid, eid, lid)
        if nama.getName(6, pid, eid, lid) is None:
            nama.setName(f"{KELUARGA}-{subfamily}", 6, pid, eid, lid)

    os2 = font["OS/2"]
    os2.usWeightClass = bobot
    # fsSelection: bit 5 = BOLD, bit 6 = REGULAR (jangan keduanya).
    fs = os2.fsSelection & ~(1 << 5 | 1 << 6)
    os2.fsSelection = fs | ((1 << 5) if bold else (1 << 6))

    head = font["head"]
    head.macStyle = (head.macStyle & ~1) | (1 if bold else 0)


def buat_ttc(fonts: list[TTFont], keluar: Path) -> None:
    """Susun beberapa sfnt menjadi satu koleksi .ttc (header ttcf v1.0).

    Setiap font ditempatkan penuh di dalam berkas; tabel directory-nya
    ditulis ulang dengan offset absolut relatif awal berkas .ttc.
    """
    # Simpan tiap font ke blob sementara, lalu susun ulang.
    blobs = []
    for font in fonts:
        font.flavor = None
        import io
        buf = io.BytesIO()
        font.save(buf)
        blobs.append(buf.getvalue())

    jumlah = len(blobs)
    ukuran_header = 12 + 4 * jumlah

    # Susun: header, lalu tiap font sejajar 512 byte (kebiasaan layout TTC).
    offsets = []
    potongan = bytearray(b'\x00' * ukuran_header)
    kursor = ukuran_header
    for blob in blobs:
        selaras = (kursor + 511) // 512 * 512
        potongan.extend(b'\x00' * (selaras - len(potongan)))
        offsets.append(selaras)
        potongan.extend(blob)
        kursor = len(potongan)

    # Perbaiki offset tabel di direktori tiap font (menjadi absolut).
    for i, dasar in enumerate(offsets):
        num_tables = struct.unpack(">H", potongan[dasar + 4:dasar + 6])[0]
        for t in range(num_tables):
            rec = dasar + 12 + t * 16
            ofs = struct.unpack(">I", potongan[rec + 8:rec + 12])[0]
            struct.pack_into(">I", potongan, rec + 8, ofs + dasar)

    # Tulis header ttcf.
    potongan[0:4] = b"ttcf"
    struct.pack_into(">HH", potongan, 4, 1, 0)
    struct.pack_into(">I", potongan, 8, jumlah)
    for i, ofs in enumerate(offsets):
        struct.pack_into(">I", potongan, 12 + i * 4, ofs)

    keluar.write_bytes(bytes(potongan))


def main() -> int:
    sumber = Path(sys.argv[1]) if len(sys.argv) > 1 else folder_sumber_bawaan()
    latin = sumber / "google-sans-latin-normal.woff2"
    if not latin.exists():
        print(f"SUMBER TIDAK DITEMUKAN: {latin}", file=sys.stderr)
        print("Berikan jalur folder fonts/google-sans aplikasi sumber sebagai argumen.", file=sys.stderr)
        return 1

    tujuan = AKAR / "SuDesApp.Wpf" / "Fonts"
    tujuan.mkdir(parents=True, exist_ok=True)

    reg = instans_static(latin, 400)
    bold = instans_static(latin, 700)
    rapikan_nama(reg, 400, "Regular")
    rapikan_nama(bold, 700, "Bold")

    reg.save(str(tujuan / "GoogleSans-Regular.ttf"))
    bold.save(str(tujuan / "GoogleSans-Bold.ttf"))
    buat_ttc([reg, bold], tujuan / "GoogleSans.ttc")

    ofl = sumber / "OFL.txt"
    if ofl.exists():
        shutil.copyfile(ofl, tujuan / "OFL.txt")

    for f in sorted(tujuan.iterdir()):
        print("tulis", f)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
