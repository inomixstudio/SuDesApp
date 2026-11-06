#!/usr/bin/env bash
# Tolak atribusi agen pada pesan commit dalam sebuah rentang rev.
#
# Dipanggil manual untuk memeriksa sejarah commit (mis. sebelum membagikan
# branch). Pola yang dicek: baris "Co-Authored-By" bernama Codebuff, teks
# "Generated with Codebuff", dan alamat "noreply@codebuff.com".
#
# Pemakaian:
#   Tools/check-atribusi-commit.sh <rentang-rev>
# Contoh:
#   Tools/check-atribusi-commit.sh "origin/master..HEAD"

set -uo pipefail

rentang="${1:-}"
if [ -z "$rentang" ]; then
    echo "pemakaian: $0 <rentang-rev> (mis. origin/master..HEAD)" >&2
    exit 2
fi

if ! git rev-list --count "$rentang" >/dev/null 2>&1; then
    echo "rentang rev tidak valid: $rentang" >&2
    exit 2
fi

pola='Co-Authored-By:.*Codebuff|Generated with Codebuff|noreply@codebuff.com'

sementara=$(mktemp -d)
trap 'rm -rf "$sementara"' EXIT

jumlah=0
gagal=0
while read -r sha; do
    [ -n "$sha" ] || continue
    jumlah=$((jumlah + 1))
    git log -1 --format=%B "$sha" > "$sementara/pesan"
    if grep -Ein "$pola" "$sementara/pesan" >/dev/null; then
        printf '::error::%s %s\n' "$(git rev-parse --short "$sha")" "$(git log -1 --format=%s "$sha")"
        grep -Ein "$pola" "$sementara/pesan" | sed 's/^/    /'
        gagal=$((gagal + 1))
    fi
done < <(git rev-list "$rentang")

echo "Diperiksa $jumlah commit pada rentang $rentang; $gagal melanggar."

if [ "$gagal" -gt 0 ]; then
    echo "Hapus baris atribusi pada commit di atas (amend/rebase), lalu push ulang." >&2
    exit 1
fi
