#!/usr/bin/env bash
# Pastikan setiap tautan relatif di berkas markdown benar-benar menuju berkas
# yang ada. README pernah menautkan docs/deploy-server.md dan
# docs/langganan-dan-lisensi.md yang belum terlacak -> 404 di GitHub, dan itu
# hanya ketahuan setelah pengguna membukanya.
set -uo pipefail

rusak=0
diperiksa=0

while IFS= read -r -d '' file; do
  # Ambil target tautan relatif: ](target) tanpa http(s), mailto, atau anchor.
  # Buang bagian anchor (#bagian) dan judul tautan ("judul" setelah target).
  while IFS= read -r target; do
    [ -z "$target" ] && continue
    case "$target" in
      http*|mailto:*|/*) continue ;;
    esac
    target="${target%%#*}"
    target="${target%% *}"
    [ -z "$target" ] && continue

    diperiksa=$((diperiksa + 1))
    dir="$(dirname "$file")"
    if [ ! -e "$dir/$target" ]; then
      echo "[GAGAL] $file -> $target (tidak ada)"
      rusak=$((rusak + 1))
    fi
  done < <(grep -oE '\]\([^)]+\)' "$file" \
           | sed -e 's/^](//' -e 's/)$//' || true)
done < <(find . -name '*.md' \
         -not -path './.git/*' -not -path '*/bin/*' -not -path '*/obj/*' \
         -not -path '*/binverify/*' -not -path './artifacts/*' -print0)

echo "Memeriksa ${diperiksa} tautan relatif."
if [ "$rusak" -gt 0 ]; then
  echo "${rusak} tautan mati."
  exit 1
fi
echo "Semua tautan relatif ada."