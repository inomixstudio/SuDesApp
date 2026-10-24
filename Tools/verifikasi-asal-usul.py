#!/usr/bin/env python3
"""Pembaruan + verifikasi cepat katalog Resources/sumber-aset.json.

Uji `SumberAsetGambarTests` yang menjaga cakupan ini secara harian; skrip ini
untuk momen yang disengaja — regenerasi ikon lewat Tools/generate-app-icon.ps1,
atau penggantian merek — supaya hash katalog tidak tertinggal.

    py -3 Tools/verifikasi-asal-usul.py                 # verifikasi semua aset
    py -3 Tools/verifikasi-asal-usul.py --perbarui      # muat ulang SHA-256 isi

Asal-usul dan jenis sumber TETAP diuji tetap terisi, agar --perbarui tidak
memblesahkan entri yang telanjang. Keluar 0 bila semua sesuai, 1 bila bermasalah.
"""

import argparse
import datetime
import hashlib
import json
import sys
from pathlib import Path

try:
    sys.stdout.reconfigure(line_buffering=True)
except (AttributeError, ValueError):
    pass

BASE_DIR = Path(__file__).resolve().parent.parent
RESOURCES = BASE_DIR / "Resources"
KATALOG = RESOURCES / "sumber-aset.json"

JENIS_SAH = {"generasi-skrip", "aset-perusahaan", "aset-internal"}
# Hanya format yang WPF bisa render sendiri: tanpa webp (WPF tidak punya
# dekoder webp).
TANDA_GAMBAR = (
    (b"\x89PNG\r\n\x1a\n", "png"),
    (b"\xff\xd8\xff", "jpg"),
    (b"BM", "bmp"),
    (b"GIF87a", "gif"),
    (b"GIF89a", "gif"),
)


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def jenis_isi(data: bytes):
    """Tipe yang WPF bisa render, dilihat dari tanda awal isi; 'ico' via header ICONDIR."""
    if data.startswith(b"\x00\x00\x01\x00"):
        return "ico"
    for tanda, nama in TANDA_GAMBAR:
        if data.startswith(tanda):
            return nama
    return None


def baca_katalog():
    try:
        entri = json.loads(KATALOG.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as e:
        print(f"KATALOG TAK DAPAT DIBACA: {KATALOG} — {e}")
        sys.exit(1)
    if not isinstance(entri, list) or not entri:
        print("KATALOG KOSONG — aplikasi tidak boleh berjalan tanpa katalog asal-usul aset.")
        sys.exit(1)
    return entri


def uji_aset(entri, perbarui=False):
    """Verifikasi semua entri; kembalikan (daftar masalah, ada_yang_diubah)."""
    masalah = []
    berubah = False

    for e in entri:
        nama = e.get("berkas", "")
        jalur = RESOURCES / nama
        if not jalur.is_file():
            masalah.append(f"{nama}: berkas tidak ada")
            continue

        data = jalur.read_bytes()
        hash_aktual = sha256(data)
        if e.get("sha256") != hash_aktual:
            if perbarui:
                e["sha256"] = hash_aktual
                e["diperiksa"] = datetime.date.today().isoformat()
                berubah = True
                print(f"  diperbarui  {nama}  sha256={hash_aktual}")
            else:
                masalah.append(f"{nama}: sha256 katalog tidak cocok dengan isi berkas")

        if jenis_isi(data) is None:
            masalah.append(f"{nama}: isi bukan gambar yang dikenali (png/jpg/bmp/gif/ico)")
        if not e.get("sumber"):
            masalah.append(f"{nama}: asal-usul (sumber) kosong")
        if e.get("jenis_sumber") not in JENIS_SAH:
            masalah.append(f"{nama}: jenis_sumber tidak dikenal — {e.get('jenis_sumber')!r}")

    return masalah, berubah


def main() -> int:
    parser = argparse.ArgumentParser(description="Verifikasi + pembaruan asal-usul aset gambar")
    parser.add_argument("--perbarui", action="store_true",
                        help="muat ulang SHA-256 isi berkas ke katalog; asal-usul tetap wajib terisi")
    args = parser.parse_args()

    entri = baca_katalog()
    masalah, berubah = uji_aset(entri, perbarui=args.perbarui)

    if args.perbarui and berubah:
        KATALOG.write_text(
            json.dumps(sorted(entri, key=lambda x: x["berkas"]), indent=2, ensure_ascii=False) + "\n",
            encoding="utf-8")
        print(f"Katalog diperbarui: {KATALOG.name}")

    n = len(entri)
    if masalah:
        print(f"\nHASIL: {n - len(masalah)} sesuai, {len(masalah)} masalah, dari {n}:")
        for m in masalah:
            print(f"  - {m}")
        return 1
    print(f"HASIL: {n} aset sesuai.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
