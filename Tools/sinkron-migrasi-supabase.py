#!/usr/bin/env python3
"""Sinkronkan salinan migrasi Supabase CLI dengan SuDesApp.Server/schema.sql.

Server menjalankan ``SuDesApp.Server/schema.sql`` sendiri saat start (berkas
itu ditanam di dalam assembly), sedangkan Supabase CLI menerapkan skema lewat
berkas di ``supabase/migrations/``. Dua jalur itu tidak boleh membawa DDL
yang berbeda, jadi migrasi CLI adalah salinan skemanya.

Berkas ini menulis ulang salinan itu dari sumber kebenaran:

    python Tools/sinkron-migrasi-supabase.py

Uji ``MigrasiSupabaseTests`` di SuDesApp.Tests menagih agar salinan tidak
pernah menyimpang diam-diam; berkas ini adalah cara memperbaikinya.
"""
from __future__ import annotations

import pathlib
import sys

AKAR = pathlib.Path(__file__).resolve().parent.parent
SUMBER = AKAR / "SuDesApp.Server" / "schema.sql"
TUJUAN = AKAR / "supabase" / "migrations" / "20261022000000_skema_lisensi.sql"

PENANDA = "-- ==================== AWAL ISI SALINAN ===================="

KEPALA = f"""-- Migrasi Supabase CLI untuk skema lisensi SuDesApp.
--
-- BERKAS INI ADALAH SALINAN SuDesApp.Server/schema.sql. Server menjalankan
-- berkas itu sendiri saat start (benda tertanam di assembly), sementara CLI
-- menjalankan berkas ini lewat `supabase db push`. Dua jalur itu tidak boleh
-- membawa DDL yang berbeda, jadi isi di bawah penanda WAJIB sama persis
-- dengan schema.sql.
--
-- Bila schema.sql berubah:
--   1. jalankan `python Tools/sinkron-migrasi-supabase.py`,
--   2. terapkan ke Supabase lewat `supabase db push` (atau biarkan server
--      menerapkan skemanya saat start — skemanya idempoten),
--   3. uji MigrasiSupabaseTests akan gagal kalau langkah 1 terlupakan.
--
-- {PENANDA}
"""


def main() -> int:
    if not SUMBER.is_file():
        print(f"Tidak ditemukan: {SUMBER}", file=sys.stderr)
        return 2

    skema = SUMBER.read_text(encoding="utf-8")
    if PENANDA in skema:
        print("schema.sql memuat penanda salinan — sumbernya sudah rusak.", file=sys.stderr)
        return 2

    TUJUAN.parent.mkdir(parents=True, exist_ok=True)
    TUJUAN.write_text(KEPALA + skema, encoding="utf-8", newline="\n")
    print(f"Migrasi ditulis: {TUJUAN.relative_to(AKAR)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
