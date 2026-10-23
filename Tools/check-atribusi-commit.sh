#!/usr/bin/env bash
# Jalankan hook commit-msg pada SETIAP pesan commit dalam sebuah rentang rev.
#
# Dipakai job CI "Audit atribusi commit" supaya aturan yang sama dengan hook
# lokal juga menggerbang pull request. Pola yang dicek tidak diduplikasi di
# sini: skrip ini hanya memanggil .githooks/commit-msg, jadi hook lokal dan
# gerbang CI tidak bisa berbeda diam-diam.
#
# Pemakaian:
#   Tools/check-atribusi-commit.sh <rentang-rev>
# Contoh:
#   Tools/check-atribusi-commit.sh "origin/main..HEAD"

set -uo pipefail

rentang="${1:-}"
if [ -z "$rentang" ]; then
    echo "pemakaian: $0 <rentang-rev> (mis. origin/main..HEAD)" >&2
    exit 2
fi

if ! git rev-list --count "$rentang" >/dev/null 2>&1; then
    echo "rentang rev tidak valid: $rentang" >&2
    exit 2
fi

akar=$(git rev-parse --show-toplevel)
hook="$akar/.githooks/commit-msg"
if [ ! -f "$hook" ]; then
    echo "hook tidak ditemukan: $hook" >&2
    exit 2
fi

sementara=$(mktemp -d)
trap 'rm -rf "$sementara"' EXIT

jumlah=0
gagal=0
while read -r sha; do
    [ -n "$sha" ] || continue
    jumlah=$((jumlah + 1))
    git log -1 --format=%B "$sha" > "$sementara/pesan"
    if ! sh "$hook" "$sementara/pesan" >/dev/null 2> "$sementara/galat"; then
        printf '::error::%s %s\n' "$(git rev-parse --short "$sha")" "$(git log -1 --format=%s "$sha")"
        sed 's/^/    /' "$sementara/galat"
        gagal=$((gagal + 1))
    fi
done < <(git rev-list "$rentang")

echo "Diperiksa $jumlah commit pada rentang $rentang; $gagal melanggar."

if [ "$gagal" -gt 0 ]; then
    echo "Hapus baris atribusi pada commit di atas (amend/rebase), lalu push ulang." >&2
    exit 1
fi
