#!/usr/bin/env bash
# uji-e2e-trial-server.sh — Uji E2E trial SERVER (alat permanen, sebelum rilis).
#
# Membangun dan menyalakan server lisensi lokal (Development, penyimpanan
# MEMORI — tanpa Supabase, tanpa Midtrans), lalu menjalankan harness klien
# Tools/UjiTrialServerE2E yang memakai klien lisensi yang SAMA dengan aplikasi:
# mendaftar desa via admin, meminta trial, membaca token bertanda tangan,
# memeriksa keadaan & chip status, lalu membuktikan trial kedua dari perangkat
# lain ditolak TANPA cadangan trial lokal.
#
# Pemakaian:
#   Tools/uji-e2e-trial-server.sh                 # port bawaan 5199
#   UJI_PORT=5391 Tools/uji-e2e-trial-server.sh
#
# Variabel yang bisa diganti: UJI_PORT, UJI_KODE (kode desa uji),
# UJI_OP_ID / UJI_OP_TOKEN (kredensial operator admin di server uji).
#
# Artefak (log server) disimpan di folder %TEMP% per putaran dan path-nya
# dicetak — folder sementara ini tidak menyentuh repo atau data pengguna.
set -u
cd "$(dirname "$0")/.." || exit 1

TMP_RUN="$(mktemp -d -t sudesapp-uji-trial-XXXXXX)"
trap 'rm -rf "$TMP_RUN"' EXIT

PORT="${UJI_PORT:-5199}"
KODE="${UJI_KODE:-UJI-TRIAL-E2E}"
OP_TOKEN="${UJI_OP_TOKEN:-0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef}"

export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="http://127.0.0.1:$PORT"
# Paksa mode MEMORI: appsettings.lokal.json di mesin ini memuat kredensial
# Supabase sungguhan, dan env yang diisi kosong menang atas berkas itu.
export SUPABASE_HOST=""
export SUPABASE_DB_PASSWORD=""
export LISENSI_KEY_PATH="$(cygpath -m "$TMP_RUN/kunci.pem" 2>/dev/null || echo "$TMP_RUN/kunci.pem")"

export SUDES_ADMIN_OPERATORS__0__ID="uji01"
export SUDES_ADMIN_OPERATORS__0__NAME="Operator Uji Trial"
export SUDES_ADMIN_OPERATORS__0__TOKEN="$OP_TOKEN"
export SUDES_ADMIN_OPERATORS__0__PERMISSIONS__0="session.read"
export SUDES_ADMIN_OPERATORS__0__PERMISSIONS__1="customers.read"
export SUDES_ADMIN_OPERATORS__0__PERMISSIONS__2="customers.create"
export SUDES_ADMIN_OPERATORS__0__PERMISSIONS__3="devices.read"

echo "== Menyiapkan server lisensi lokal (Development/memori) di port $PORT =="
dotnet build SuDesApp.Server/SuDesApp.Server.csproj -v q --nologo || exit 1
dotnet build Tools/UjiTrialServerE2E/UjiTrialServerE2E.csproj -v q --nologo || exit 1

dotnet run --project SuDesApp.Server/SuDesApp.Server.csproj --no-build --no-launch-profile \
    > "$TMP_RUN/server.log" 2>&1 &
PID=$!
trap 'kill $PID 2>/dev/null; wait $PID 2>/dev/null; rm -rf "$TMP_RUN"' EXIT

SIAP=0
for _ in $(seq 1 60); do
    if curl -fsS "http://127.0.0.1:$PORT/healthz" >/dev/null 2>&1; then SIAP=1; break; fi
    if ! kill -0 "$PID" 2>/dev/null; then break; fi
    sleep 1
done

if [ "$SIAP" != "1" ]; then
    echo "SERVER GAGAL SIAP — 40 baris log terakhir:"
    tail -40 "$TMP_RUN/server.log"
    exit 1
fi

echo "== Server siap; menjalankan harness klien =="
UJI_BASE_URL="http://127.0.0.1:$PORT" \
UJI_KODE="$KODE" \
UJI_OP_ID="${UJI_OP_ID:-uji01}" \
UJI_OP_TOKEN="$OP_TOKEN" \
    dotnet run --project Tools/UjiTrialServerE2E/UjiTrialServerE2E.csproj --no-build
STATUS=$?

echo
echo "== Log server (30 baris terakhir) =="
tail -30 "$TMP_RUN/server.log"

exit $STATUS
