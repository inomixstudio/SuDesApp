#!/usr/bin/env bash
# Ekspor catatan rilis berbahasa Indonesia dari CatatanRilisViewModel.cs
# menjadi Markdown, untuk ditempel pada body GitHub Release.
#
# Mengapa perlu: catatan rilis yang ditulis tangan ada di dalam file C# (dibaca
# pengguna lewat kartu "Catatan Rilis" di aplikasi), sementara workflow rilis
# hanya menghasilkan dua baris ringkasan plus catatan otomatis GitHub
# berbahasa Inggris. Petugas desa tidak pernah membaca commit subject seperti
# "fix(form): textbox Dusun sejajar dengan kolom isian lain di 14 form surat".
#
# Sumber kebenaran tetap CatatanRilisViewModel.cs. Alat ini membacanya, bukan
# membuat ulang, agar tidak ada dua daftar yang bisa melenceng.
#
# Pemakaian: ./Tools/ekspor-catatan-rilis.sh <versi> [keluaran.md]
set -euo pipefail

VERSI="${1:-}"
KELUARAN="${2:-artifacts/CATATAN-RILIS.md}"
SUMBER="SuDesApp.Wpf/ViewModels/CatatanRilisViewModel.cs"

if [ -z "$VERSI" ]; then
  echo "Pemakaian: $0 <versi> [keluaran.md]" >&2
  exit 1
fi
if [ ! -f "$SUMBER" ]; then
  echo "Sumber catatan rilis tidak ditemukan: $SUMBER" >&2
  exit 1
fi

mkdir -p "$(dirname "$KELUARAN")"

# Blok satu entri rilis: dari baris yang memuat "Versi <VERSI> (" sampai baris
# sebelum entri versi berikutnya. Entri selalu dimulai dengan new
# CatatanRilisEntry( pada baris sendiri, jadi batasnya jelas.
awk -v versi="$VERSI" '
  !mulai && index($0, "\"Versi " versi " (") > 0 { mulai = 1 }
  mulai && NR > 1 && /new CatatanRilisEntry\(/ && index($0, "\"Versi " versi " (") == 0 { exit }
  mulai { print }
' "$SUMBER" > /tmp/catatan-blok.txt

if [ ! -s /tmp/catatan-blok.txt ]; then
  echo "Tidak ditemukan entri 'Versi $VERSI' di $SUMBER" >&2
  exit 1
fi

# Ubah C# menjadi Markdown: judul entri -> ###, subjudul -> ####, butir -> -.
{
  grep -oE '"Versi [^"]+"' /tmp/catatan-blok.txt | head -1 | tr -d '"' | sed 's/^/### /'
  echo
  grep -oE 'new CatatanRilisSubSection\("[^"]+"' /tmp/catatan-blok.txt \
    | sed 's/new CatatanRilisSubSection("/#### /' | sed 's/"$//'
  echo
  grep -oE '^[[:space:]]+"[^"]{40,}"[[:space:]]*,?[[:space:]]*$' /tmp/catatan-blok.txt \
    | sed 's/^[[:space:]]*"/- /' | sed 's/",\?$//'
} > "$KELUARAN"

rm -f /tmp/catatan-blok.txt

if [ "$(wc -l < "$KELUARAN")" -le 3 ]; then
  echo "Peringatan: hasil ekspor $KELUARAN nyaris kosong; periksa format $SUMBER." >&2
  exit 1
fi

echo "Catatan rilis $VERSI ditulis ke $KELUARAN ($(wc -l < "$KELUARAN") baris)."