# -*- coding: utf-8 -*-
"""Standarkan seluruh literal FontSize ke skala ukuran huruf baku.

Skala baku (diadaptasi dari skala tipografi antarmuka modern —
caption 10, label 11, body 13, pesan 14, judul-kecil 15, judul 20,
angka besar 24, tampilan 28):

    10  11  13  14  15  20  24  28

Pemetaan nilai lama → nilai skala (terdekat; seri naik ke atas):
    9, 9.5 → 10        13, 13.5 → 13, 14
    10.5, 11, 11.5 → 11              15, 16, 17 → 15
    12, 12.5 → 13                   18, 19, 20, 21, 22 → 20
    24, 25, 26 → 24                 28 → 28

Nilai LEBIH BESAR dari 28 (mis. 32/34/48 untuk emoji/piagam) dan nilai
kecil khusus (mis. 1 untuk pemisah hairline) sengaja tidak disentuh.

Dua bentuk penulisan ikut diseragamkan, karena nilai yang ditulis di dalam
Style TIDAK terlihat oleh pembaca atribut biasa:

    FontSize="12"
    <Setter Property="FontSize" Value="12"/>
    <Setter Value="12" Property="FontSize"/>

Jalankan:  py Tools/standarkan-ukuran-huruf.py
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

AKAR = Path(__file__).resolve().parent.parent

PEMETAAN = {
    "9": "10", "9.5": "10", "10": "10",
    "10.5": "11", "11": "11", "11.5": "11",
    "12": "13", "12.5": "13",
    "13": "13", "13.5": "14", "14": "14",
    "15": "15", "16": "15", "17": "15",
    "18": "20", "19": "20", "20": "20", "21": "20", "22": "20",
    "24": "24", "25": "24", "26": "24",
    "28": "28",
}

# XAML:  FontSize="12.5"      C#:  FontSize = 12.5
POLA_XAML = re.compile(r'(FontSize=")(\d+(?:\.\d+)?)(")')
POLA_CS = re.compile(r'(FontSize\s*=\s*)(\d+(?:\.\d+)?)(?![\d.])')

# Nilai di dalam Style: <Setter Property="FontSize" Value="12"/> — urutan
# atribut boleh terbalik, dan spasi/newline di antaranya boleh berbeda.
POLA_SETTER = re.compile(
    r'(Property="FontSize"\s+Value=")(\d+(?:\.\d+)?)(")')
POLA_SETTER_TERBALIK = re.compile(
    r'(Value=")(\d+(?:\.\d+)?)("\s+Property="FontSize")')


def proses(berkas: Path) -> int:
    # newline="" supaya akhir baris CRLF/LF berkas asli tidak ikut berubah —
    # tanpa ini, satu angka yang diganti akan tampil sebagai perubahan
    # seluruh berkas di diff.
    with berkas.open(encoding="utf-8", newline="") as f:
        isi = f.read()
    pola = (POLA_XAML, POLA_SETTER, POLA_SETTER_TERBALIK) \
        if berkas.suffix == ".xaml" else (POLA_CS,)

    jumlah = 0

    for satu in pola:
        def ganti(m: re.Match) -> str:
            nonlocal jumlah
            nilai = m.group(2)
            baru = PEMETAAN.get(nilai)
            if baru is None or baru == nilai:
                return m.group(0)
            jumlah += 1
            ekor = m.group(3) if (m.lastindex or 0) >= 3 else ""
            return m.group(1) + baru + ekor

        isi = satu.sub(ganti, isi)

    if jumlah:
        with berkas.open("w", encoding="utf-8", newline="") as f:
            f.write(isi)
    return jumlah


def main() -> int:
    wpf = AKAR / "SuDesApp.Wpf"
    total = 0
    berkas_diubah = 0
    for berkas in sorted(wpf.rglob("*")):
        if berkas.suffix not in (".xaml", ".cs") or "obj" in berkas.parts or "bin" in berkas.parts:
            continue
        n = proses(berkas)
        if n:
            berkas_diubah += 1
            total += n
            print(f"{n:4d}  {berkas.relative_to(AKAR)}")
    print(f"total {total} literal pada {berkas_diubah} berkas")
    return 0


if __name__ == "__main__":
    sys.exit(main())
