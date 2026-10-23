#!/usr/bin/env bash
#
# uji-e2e-server-lisensi.sh — seluruh pemeriksaan server lisensi terhadap
# Supabase dan Midtrans SUNGGUHAN, lalu jejak ujinya dibersihkan.
#
# IDEMPOTEN
# ---------
# Kode aktivasi desa uji tetap sama tiap putaran (KODE_UJI), jadi sisa run
# sebelumnya — termasuk run yang terputus di tengah jalan — ditemukan dan
# dihapus lebih dulu sebelum uji dimulai. Pembersihan akhir dijalankan lewat
# trap EXIT/INT/TERM, sehingga tetap terjadi walau pemeriksaan gagal di
# tengah. Setelah putaran selesai, jumlah desa di server sama persis dengan
# jumlah sebelum putaran dimulai.
#
# SERVER HARUS SUDAH BERJALAN
# ---------------------------
# Skrip ini TIDAK menyalakan server; ia menguji server yang sudah berjalan
# dengan Supabase sungguhan dan ServerKey Midtrans sungguhan. Contoh:
#
#   export SUPABASE_HOST=... SUPABASE_DB_PASSWORD=... MIDTRANS_SERVER_KEY=...
#   export SUDES_ADMIN_OPERATORS__0__ID=uji01
#   export SUDES_ADMIN_OPERATORS__0__NAME="Operator Uji E2E"
#   export SUDES_ADMIN_OPERATORS__0__TOKEN="$(openssl rand -hex 32)"
#   export SUDES_ADMIN_OPERATORS__0__PERMISSIONS__0=session.read
#   export SUDES_ADMIN_OPERATORS__0__PERMISSIONS__1=customers.read
#   ... (semua izin yang dipakai uji di bawah)
#   dotnet run --project SuDesApp.Server/SuDesApp.Server.csproj --no-launch-profile
#   Tools/uji-e2e-server-lisensi.sh
#
# Yang DIPERIKSA (semua lewat HTTP; tidak ada kueri SQL langsung):
#   1. endpoint publik    /healthz / /paket /kunci-publik /pengumuman +
#                          halaman /checkout, /admin, /checkout-uji
#   2.  gerbang admin     tanpa token 401, token salah 401, id salah 401,
#                          izin mencukupi 200 (opsional: operator terbatas 403)
#   3.  data desa         daftar dua kali → id sama (Supabase), baca, ubah
#                          nama/kontak, ubah batas perangkat (sah & ditolak),
#                          desa tak dikenal 404
#   4.  aktivasi          kode kosong 400, kode asing 404, desa belum bayar
#                          tanpa token, trial 14 hari + token v1 yang
#                          tanda tangan ES256-nya diverifikasi terhadap
#                          /kunci-publik, batas perangkat (perangkat sama tidak
#                          menghabiskan kuota, perangkat ke-3 ditolak 403,
#                          hapus perangkat membuka kuota, reset semua)
#   5.  pembukuan manual  tandai lunas, lampiran bukti transfer sah (tersimpan
#                          & bisa diambil) dan yang ditolak (400, langganan
#                          TIDAK bertambah), riwayat pembayaran, kuitansi
#   6.  Midtrans          transaksi Snap NYATA, order diverifikasi lewat Core
#                          API merchant, notifikasi: tanpa signature 403,
#                          signature salah 403, nominal salah 400, status
#                          tidak berduit 200 tanpa perpanjangan, settlement
#                          200 + hari bertambah, webhook ulang idempoten,
#                          order tak dikenal 404, body bukan JSON 400, jejak
#                          notifikasi tercatat
#   7.  rekonsiliasi      status ditanyakan ke Midtrans sungguhan; perpanjangan
#                          tidak dobel pada putaran kedua
#   8.  ekspor & pengumuman  CSV desa & pembayaran, siklus buat → tampil →
#                          hapus pengumuman
#   9.  pembersihan       hapus desa dengan jejak uang ditolak 409, hapus
#                          paksa, desa tak bisa dibaca lagi, jumlah desa
#                          kembali seperti sebelum uji
#
# Yang TIDAK bisa dibersihkan: order Snap yang dibuat tetap tercatat di akun
# merchant sampai dihapus manual di dashboard Midtrans (tidak ada API publik
# untuk menghapus transaksi). Skrip ini mencetak daftar ordernya di akhir.
#
# PEMAKAIAN
# ---------
#   Tools/uji-e2e-server-lisensi.sh                          # lokal :5110
#   BASE_URL=https://lisensi.example.com \
#     OPERATOR_TOKEN=... Tools/uji-e2e-server-lisensi.sh      # server deploy
#
# Variabel (semua opsional kecuali OPERATOR_TOKEN):
#   BASE_URL                   dasar server   (bawaan http://127.0.0.1:5110)
#   OPERATOR_ID / OPERATOR_TOKEN  operator uji (bawaan uji01 / dari env deploy)
#   OPERATOR_ID_TERBATAS / OPERATOR_TOKEN_TERBATAS
#                              operator berizin terbatas untuk membuktikan 403;
#                              bila kosong pemeriksaan itu dilewati
#   MIDTRANS_SERVER_KEY        ServerKey; bila kosong dibaca dari
#                              SuDesApp.Server/appsettings.lokal.json (tidak
#                              pernah dicetak)
#   KODE_UJI                   kode aktivasi desa uji (bawaan UJI-E2E-SERVER)
#   TUNEL_PUBLIK               URL publik; bila diisi, notifikasi juga dikirim
#                              lewat jalur publik (membuktikan Midtrans bisa
#                              mencapai server ini)
#   JEDA_GLOBAL / JEDA_ADMIN / JEDA_AKTIVASI / JEDA_TAGIHAN
#                              jeda antarpermintaan (detik). Bawaannya sudah
#                              disetel di bawah rate limit server (global 120,
#                              admin 30, aktivasi 20, tagihan 5 per menit),
#                              sehingga uji tidak pernah kena 429 sendiri.
#
set -u

AKAR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)" || exit 1
cd "$AKAR" || { echo "Gagal masuk root repo: $AKAR" >&2; exit 1; }

if [ -t 1 ]; then
    HIJAU=$'\033[32m'; MERAH=$'\033[31m'; KUNING=$'\033[33m'; RESET=$'\033[0m'
else
    HIJAU=""; MERAH=""; KUNING=""; RESET=""
fi

BASE_URL="${BASE_URL:-http://127.0.0.1:5110}"
BASE_URL="${BASE_URL%/}"
OP_ID="${OPERATOR_ID:-${SUDES_ADMIN_OPERATOR_ID:-uji01}}"
OP_TOKEN="${OPERATOR_TOKEN:-${SUDES_ADMIN_OPERATOR_TOKEN:-}}"
OP_BATAS_ID="${OPERATOR_ID_TERBATAS:-}"
OP_BATAS_TOKEN="${OPERATOR_TOKEN_TERBATAS:-}"
FWD_PROTO="${FORWARDED_PROTO:-https}"
KODE="${KODE_UJI:-UJI-E2E-SERVER}"
NAMA_DESA="Desa Uji E2E Server"
NAMA_DESA_UBAH="Desa Uji E2E (diperbarui)"
JUDUL_UJI="UJI E2E SERVER"
TUNEL="${TUNEL_PUBLIK:-}"

# Jeda antarpermintaan (detik) supaya limiter milik server sendiri tidak
# menggagalkan uji: kebijakan server membatasi 30/menit untuk endpoint admin,
# 20/menit untuk aktivasi, 5/menit untuk pembuatan tagihan & rekonsiliasi, dan
# 120/menit global per IP. Dengan bawaan ini uji berjalan ±5 menit tanpa 429.
JEDA_GLOBAL="${JEDA_GLOBAL:-1}"
JEDA_ADMIN="${JEDA_ADMIN:-3}"
JEDA_AKTIVASI="${JEDA_AKTIVASI:-4}"
JEDA_TAGIHAN="${JEDA_TAGIHAN:-13}"

TMP_DIR="$(mktemp -d 2>/dev/null || echo "/tmp/uji-e2e-$$")"
TMP_BODY="$TMP_DIR/balasan"
TMP_ERR="$TMP_DIR/curl.err"
mkdir -p "$TMP_DIR"

SK="${MIDTRANS_SERVER_KEY:-}"
if [ -z "$SK" ]; then
    SK="$(sed -n 's/.*"ServerKey"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' \
        SuDesApp.Server/appsettings.lokal.json 2>/dev/null | head -1)"
fi

# Interpreter Python yang BENAR-BENAR bisa dijalankan. Di Windows ada shim
# "python3.exe" milik Microsoft Store: `command -v python3` menemukannya, tapi
# menjalankannya hanya memuntahkan "Python was not found" — jadi kandidatnya
# diuji dengan benar-benar mengimport hashlib sebelum dipakai.
PYTHON=""
for _kandidat in python3 python py; do
    if command -v "$_kandidat" >/dev/null 2>&1 &&
       "$_kandidat" -c "import hashlib" >/dev/null 2>&1; then
        PYTHON="$_kandidat"
        break
    fi
done

LULUS=0; GAGAL=0; LEWAT=0
SIAP=0                       # 1 setelah prasyarat lewat → baru boleh menyentuh data
ORDER_UJI=()                 # order Snap yang dibuat, untuk dicetak di akhir
DAFTAR_DESA=""               # salinan terakhir GET /admin/pelanggan
DAFTAR_PENGUMUMAN=""         # salinan terakhir GET /admin/pengumuman

# =====================================================================
# Laporan
# =====================================================================
lulus()    { LULUS=$((LULUS + 1)); printf '    %sOK%s    %s\n' "$HIJAU" "$RESET" "$1"; }
gagal()    { GAGAL=$((GAGAL + 1)); printf '    %sGAGAL%s  %s\n' "$MERAH" "$RESET" "$1"
             [ -n "${2:-}" ] && printf '          %s\n' "$2"; return 0; }
dilewati() { LEWAT=$((LEWAT + 1)); printf '    %sLEWAT%s  %s\n' "$KUNING" "$RESET" "$1"; }
ringkas()  { printf '%s' "$1" | tr -d '\r\n' | head -c 200; }

cek() { # cek <kondisi:0=aman> <deskripsi> [detail galat]
    if [ "$1" = "0" ]; then lulus "$2"; else gagal "$2" "${3:-}"; fi
}

cek_kode() { # cek_kode <kode_http_diharapkan> <deskripsi>
    if [ "$HTTP_CODE" = "$1" ]; then
        lulus "$2"
    else
        gagal "$2" "HTTP $HTTP_CODE (diharapkan $1) — $(ringkas "$HTTP_BODY")"
    fi
}

cek_isi() { # cek_isi <teks_yang_harus_ada> <deskripsi>
    case "$HTTP_BODY" in
        *"$1"*) lulus "$2" ;;
        *) gagal "$2" "tidak menemukan '$1' dalam balasan: $(ringkas "$HTTP_BODY")" ;;
    esac
}

# =====================================================================
# JSON (server membalas JSON camelCase, tanpa spasi)
# =====================================================================
jstr()  { printf '%s' "$1" | grep -o "\"$2\":\"[^\"]*\"" | head -1 | sed "s/^\"$2\":\"//; s/\"$//"; }
jnum()  { printf '%s' "$1" | grep -o "\"$2\":[0-9][0-9]*" | head -1 | sed "s/^\"$2\"://"; }
jbool() { printf '%s' "$1" | grep -o "\"$2\":\(true\|false\)" | head -1 | sed "s/^\"$2\"://"; }

# WAJIB memakai pembungkus ADMIN: memanggil req() langsung berarti tanpa
# X-Forwarded-Proto & tanpa token, sehingga server membalas 426 (gerbang TLS)
# dan daftarnya kosong — kegagalan yang diam-diam membuat seluruh pemeriksaan
# daftar terlihat "desa tidak muncul".
baca_daftar_desa()      { ADMIN GET /admin/pelanggan; DAFTAR_DESA="$HTTP_BODY"; }
baca_daftar_pengumuman(){ ADMIN GET /admin/pengumuman; DAFTAR_PENGUMUMAN="$HTTP_BODY"; }
baris_desa()            { printf '%s' "$DAFTAR_DESA" | tr '{' '\n' | grep "\"kodeAktivasi\":\"$1\"" | head -1; }
jumlah_desa()           { jnum "$DAFTAR_DESA" "jumlah"; }

# =====================================================================
# HTTP — dengan jeda sesuai kebijakan rate limit + ulang bila kena 429
# =====================================================================
T_GLOBAL=0; T_ADMIN=0; T_AKTIVASI=0; T_TAGIHAN=0; T_WEBHOOK=0
HTTP_CODE=""; HTTP_BODY=""

tunggu() { # tunggu <policy>
    local min last now butuh=0 sisa
    case "$1" in
        admin)    min=$JEDA_ADMIN ;;
        aktivasi) min=$JEDA_AKTIVASI ;;
        tagihan)  min=$JEDA_TAGIHAN ;;
        *)        min=$JEDA_GLOBAL ;;
    esac
    last=$(case "$1" in
        admin) echo "$T_ADMIN" ;; aktivasi) echo "$T_AKTIVASI" ;;
        tagihan) echo "$T_TAGIHAN" ;; webhook) echo "$T_WEBHOOK" ;; *) echo "$T_GLOBAL" ;; esac)
    now=$(date +%s)
    if [ $((now - last)) -lt "$min" ]; then butuh=$((min - (now - last))); fi

    # Setiap permintaan — apa pun kebijakannya — juga ikut jeda global.
    now=$(date +%s)
    if [ $((now - T_GLOBAL)) -lt "$JEDA_GLOBAL" ]; then
        sisa=$((JEDA_GLOBAL - (now - T_GLOBAL)))
        [ "$sisa" -gt "$butuh" ] && butuh=$sisa
    fi

    [ "$butuh" -gt 0 ] && sleep "$butuh"
    return 0
}

catat_waktu() {
    local now; now=$(date +%s)
    case "$1" in
        admin) T_ADMIN=$now ;; aktivasi) T_AKTIVASI=$now ;;
        tagihan) T_TAGIHAN=$now ;; webhook) T_WEBHOOK=$now ;;
    esac
    T_GLOBAL=$now
}

req() { # req <policy> <method> <path|url-lengkap> [body-json] [argumen curl...]
    local policy="$1" method="$2" path="$3"; shift 3
    local body="" attempt=0 code="" url
    if [ "$#" -gt 0 ] && [ "${1#-}" = "$1" ]; then body="$1"; shift; fi

    case "$path" in
        http://*|https://*) url="$path" ;;
        *) url="$BASE_URL$path" ;;
    esac

    tunggu "$policy"
    while [ "$attempt" -lt 6 ]; do
        attempt=$((attempt + 1))
        if [ -n "$body" ]; then
            code=$(curl -sS -m 60 -o "$TMP_BODY" -w '%{http_code}' -X "$method" "$url" \
                -H "Content-Type: application/json" --data-binary "$body" "$@" 2>>"$TMP_ERR") || code="000"
        else
            code=$(curl -sS -m 60 -o "$TMP_BODY" -w '%{http_code}' -X "$method" "$url" \
                "$@" 2>>"$TMP_ERR") || code="000"
        fi
        # Cadangan bila jeda di atas tidak cukup (ada pemanggil lain dari IP
        # yang sama): tunggu lalu ulangi, jangan langsung menghitung gagal.
        if [ "$code" = "429" ] && [ "$attempt" -lt 6 ]; then
            printf '      %s429%s limiter server — tunggu %ss, ulangi (%s %s)\n' \
                "$KUNING" "$RESET" "$((attempt * 5))" "$method" "$path"
            sleep $((attempt * 5)); continue
        fi
        break
    done
    catat_waktu "$policy"
    HTTP_CODE="$code"
    # Nol byte dibuang: balasan biner (gambar bukti transfer) membuat
    # command-substitution memuntahkan peringatan yang menutupi hasil uji.
    HTTP_BODY="$(tr -d '\0' < "$TMP_BODY" 2>/dev/null)"
    return 0
}

# Pembungkus sesuai kebijakan rate limit endpointnya — jeda yang dipakai harus
# mengikuti kebijakan yang sebenarnya, bukan satu jeda untuk semua.
KIRIM()  { req global   "$1" "$2" "${3:-}" -H "X-Forwarded-Proto: $FWD_PROTO"; }          # publik
ADMIN()  { req admin    "$1" "$2" "${3:-}" -H "X-Admin-Operator: $OP_ID" \
                            -H "Authorization: Bearer $OP_TOKEN" -H "X-Forwarded-Proto: $FWD_PROTO"; }
TAGIHAN(){ req tagihan  "$1" "$2" "${3:-}" -H "X-Admin-Operator: $OP_ID" \
                            -H "Authorization: Bearer $OP_TOKEN" -H "X-Forwarded-Proto: $FWD_PROTO"; }
AKTIVASI(){ req aktivasi "$1" "$2" "${3:-}" -H "X-Forwarded-Proto: $FWD_PROTO"; }
TANPA_TOKEN() { req admin "$1" "$2" "${3:-}" -H "X-Forwarded-Proto: $FWD_PROTO"; }
TOKEN_SALAH() { req admin "$1" "$2" "${3:-}" -H "X-Admin-Operator: $OP_ID" \
                        -H "Authorization: Bearer 000000000000000000000000000000000000000000000000" \
                        -H "X-Forwarded-Proto: $FWD_PROTO"; }
ID_SALAH() { req admin "$1" "$2" "${3:-}" -H "X-Admin-Operator: bukan-operator" \
                        -H "Authorization: Bearer $OP_TOKEN" -H "X-Forwarded-Proto: $FWD_PROTO"; }
OPERATOR_BATAS() { req admin "$1" "$2" "${3:-}" -H "X-Admin-Operator: $OP_BATAS_ID" \
                        -H "Authorization: Bearer $OP_BATAS_TOKEN" -H "X-Forwarded-Proto: $FWD_PROTO"; }

# Signature Midtrans: SHA512(order_id + status_code + gross_amount + ServerKey).
# Kuncinya DIOPERKAN sebagai argumen keempat supaya uji "signature salah"
# benar-benar menghitung dengan kunci yang berbeda — bukan diam-diam memakai
# ServerKey yang sah dan menghasilkan signature yang justru valid.
sig512() { # sig512 <order_id> <status_code> <gross_amount> <server_key>
    if command -v openssl >/dev/null 2>&1; then
        printf '%s' "$1$2$3$4" | openssl dgst -sha512 -hex | awk '{print $NF}'
    elif [ -n "$PYTHON" ]; then
        "$PYTHON" -c "import hashlib,sys;print(hashlib.sha512(''.join(sys.argv[1:]).encode()).hexdigest())" \
            "$1" "$2" "$3" "$4"
    fi
}

# =====================================================================
# Pembersihan — selalu jalan (trap), idempoten, boleh dipanggil dua kali
# =====================================================================
BERSIH_SUDAH_JALAN=0
bersihkan() {
    [ "$BERSIH_SUDAH_JALAN" = "1" ] && return 0
    BERSIH_SUDAH_JALAN=1

    printf '\n%s== PEMBERSIHAN JEJAK UJI ==%s\n' "$KUNING" "$RESET"

    if [ "$SIAP" != "1" ]; then
        printf '  prasyarat belum lewat — tidak menyentuh data apa pun.\n'
        rm -rf "$TMP_DIR" 2>/dev/null
        return 0
    fi

    # 1. Pengumuman uji (judul berawalan JUDUL_UJI), apa pun tersisa.
    baca_daftar_pengumuman
    local id
    for id in $(printf '%s' "$DAFTAR_PENGUMUMAN" \
            | grep -o "\"id\":\"[^\"]*\",\"judul\":\"$JUDUL_UJI[^\"]*\"" \
            | sed 's/^"id":"//; s/","judul".*$//'); do
        ADMIN DELETE "/admin/pengumuman/$id"
        printf '  pengumuman %s → HTTP %s\n' "$id" "$HTTP_CODE"
    done

    # 2. Desa uji beserta tagihan, pembayaran, perangkat & langganannya.
    ADMIN DELETE "/admin/pelanggan/$KODE?konfirmasi=$KODE"
    printf '  desa uji %s → HTTP %s %s\n' "$KODE" "$HTTP_CODE" "$(ringkas "$HTTP_BODY")"

    # 3. Pastikan tidak ada sisa yang terbaca lagi (perlu token: tanpa token
    #    endpointnya menjawab 401, bukan 404).
    ADMIN GET "/admin/pelanggan/$KODE"
    if [ "$HTTP_CODE" = "404" ]; then
        printf '  %sverifikasi%s desa uji hilang (GET → 404)\n' "$HIJAU" "$RESET"
    elif [ "$HTTP_CODE" = "200" ]; then
        printf '  %sPERHATIAN%s desa uji %s MASIH ADA setelah penghapusan.\n' "$MERAH" "$RESET" "$KODE"
    else
        printf '  verifikasi desa uji: HTTP %s (diharapkan 404)\n' "$HTTP_CODE"
    fi

    rm -rf "$TMP_DIR" 2>/dev/null
    return 0
}
trap 'bersihkan' EXIT
trap 'printf "\nDibatalkan pengguna.\n"; bersihkan; exit 130' INT TERM

# =====================================================================
# 0) PRASYARAT
# =====================================================================
printf '%s== PRAKONDISI ==%s\n' "$KUNING" "$RESET"

if [ -z "$OP_TOKEN" ]; then
    gagal "operator token tersedia" \
        "setel OPERATOR_TOKEN (atau SUDES_ADMIN_OPERATOR_TOKEN) — token operator dengan izin yang dipakai uji."
    printf '\n%d lulus, %d gagal, %d dilewati\n' "$LULUS" "$GAGAL" "$LEWAT"
    exit 1
fi

if [ -z "$SK" ]; then
    gagal "ServerKey Midtrans tersedia" \
        "setel MIDTRANS_SERVER_KEY atau isi SuDesApp.Server/appsettings.lokal.json"
    printf '\n%d lulus, %d gagal, %d dilewati\n' "$LULUS" "$GAGAL" "$LEWAT"
    exit 1
fi

KIRIM GET /healthz
if [ "$HTTP_CODE" != "200" ]; then
    gagal "server hidup di $BASE_URL" \
        "GET /healthz → HTTP $HTTP_CODE. Skrip ini tidak menyalakan server; nyalakan dulu (lihat header berkas ini)."
    printf '\n%d lulus, %d gagal, %d dilewati\n' "$LULUS" "$GAGAL" "$LEWAT"
    exit 1
fi
lulus "server hidup di $BASE_URL (GET /healthz → 200)"

ADMIN GET /admin/uji-pembayaran
if [ "$HTTP_CODE" != "200" ]; then
    gagal "operator $OP_ID sah dan berizin payments.test" \
        "GET /admin/uji-pembayaran → HTTP $HTTP_CODE: $(ringkas "$HTTP_BODY")"
    printf '\n%d lulus, %d gagal, %d dilewati\n' "$LULUS" "$GAGAL" "$LEWAT"
    exit 1
fi
lulus "operator $OP_ID sah dan berizin payments.test"

LINGKUNGAN="$(jstr "$HTTP_BODY" "lingkungan")"
printf '  gateway: %s, notifikasi transaksi uji → %s\n' \
    "$LINGKUNGAN" "$(jstr "$HTTP_BODY" "urlNotifikasi")"
[ "$(jbool "$HTTP_BODY" "gatewayAktif")" = "true" ] || {
    gagal "gateway Midtrans aktif" "ServerKey belum terbaca server — tagihan uji tidak akan dibuat."
    printf '\n%d lulus, %d gagal, %d dilewati\n' "$LULUS" "$GAGAL" "$LEWAT"
    exit 1
}

CORE_API="https://api.midtrans.com"
[ "$LINGKUNGAN" = "sandbox" ] && CORE_API="https://api.sandbox.midtrans.com"

# Bersihkan sisa run sebelumnya supaya putaran ini dimulai dari keadaan awal
# yang sama (inilah yang membuat skrip idempoten).
baca_daftar_desa
JUMLAH_AWAL="$(jumlah_desa)"
baca_daftar_pengumuman
for id in $(printf '%s' "$DAFTAR_PENGUMUMAN" \
        | grep -o "\"id\":\"[^\"]*\",\"judul\":\"$JUDUL_UJI[^\"]*\"" \
        | sed 's/^"id":"//; s/","judul".*$//'); do
    ADMIN DELETE "/admin/pengumuman/$id"
    printf '  pengumuman sisa run lalu %s → HTTP %s\n' "$id" "$HTTP_CODE"
done
if [ "$(baris_desa "$KODE")" != "" ]; then
    ADMIN DELETE "/admin/pelanggan/$KODE?konfirmasi=$KODE"
    printf '  desa sisa run lalu %s → HTTP %s %s\n' "$KODE" "$HTTP_CODE" "$(ringkas "$HTTP_BODY")"
    baca_daftar_desa
    JUMLAH_AWAL="$(jumlah_desa)"
fi
lulus "prasyarat lengkap; jumlah desa sebelum uji: $JUMLAH_AWAL"
SIAP=1

# Harga & durasi paket SELALU dibaca dari katalog server (satu-satunya sumber
# kebenaran) — uji tidak pernah menaruh angka rupiahnya sendiri.
KIRIM GET /paket
cek_kode 200 "GET /paket"
HARGA="$(printf '%s' "$HTTP_BODY" | tr '{' '\n' | grep '"kode":"bulanan"' | head -1 | grep -o '"harga":[0-9]*' | cut -d: -f2)"
HARI="$(printf '%s' "$HTTP_BODY" | tr '{' '\n' | grep '"kode":"bulanan"' | head -1 | grep -o '"hari":[0-9]*' | cut -d: -f2)"
cek "$(if [ -n "$HARGA" ] && [ "$HARGA" -gt 0 ] && [ -n "$HARI" ] && [ "$HARI" -gt 0 ]; then echo 0; else echo 1; fi)" \
    "katalog 'bulanan' terbaca (harga $HARGA, $HARI hari)"
GROSS="${HARGA}.00"

# =====================================================================
# 1) ENDPOINT PUBLIK
# =====================================================================
printf '\n%s== 1) ENDPOINT PUBLIK ==%s\n' "$KUNING" "$RESET"

KIRIM GET /
cek_kode 200 "GET / menyapa"
KIRIM GET /kunci-publik
cek_kode 200 "GET /kunci-publik"
KUNCI_PUBLIK="$(jstr "$HTTP_BODY" "kunci")"
cek_isi "ES256" "kunci publik menyebut algoritma ES256"
cek "$(if [ -n "$KUNCI_PUBLIK" ] && [ "${#KUNCI_PUBLIK}" -gt 40 ]; then echo 0; else echo 1; fi)" \
    "kunci publik berisi materi kunci (${#KUNCI_PUBLIK} huruf)"
KIRIM GET /pengumuman
cek_kode 200 "GET /pengumuman"
KIRIM GET /checkout
cek_kode 200 "GET /checkout (halaman)"
KIRIM GET /admin
cek_kode 200 "GET /admin (halaman)"
KIRIM GET /checkout-uji
cek_kode 200 "GET /checkout-uji (halaman)"

# =====================================================================
# 2) GERBANG ADMIN
# =====================================================================
printf '\n%s== 2) GERBANG ADMIN ==%s\n' "$KUNING" "$RESET"

TANPA_TOKEN GET /admin/pelanggan
cek_kode 401 "tanpa token → 401"
TOKEN_SALAH GET /admin/pelanggan
cek_kode 401 "token salah → 401"
ID_SALAH GET /admin/pelanggan
cek_kode 401 "id operator salah → 401"

ADMIN GET /admin/sesi
cek_kode 200 "GET /admin/sesi dengan token sah"
cek "$(if [ "$(jstr "$HTTP_BODY" "id")" = "$OP_ID" ]; then echo 0; else echo 1; fi)" \
    "identitas sesi sama dengan operator uji"
cek_isi "payments.test" "sesi memuat izin payments.test"

if [ -n "$OP_BATAS_TOKEN" ]; then
    OPERATOR_BATAS GET /admin/ekspor/desa.csv
    if [ "$HTTP_CODE" = "403" ]; then
        lulus "operator tanpa izin customers.export → 403"
    else
        gagal "operator tanpa izin customers.export → 403" "HTTP $HTTP_CODE"
    fi
else
    dilewati "pemeriksaan 403 izin (setel OPERATOR_ID_TERBATAS / OPERATOR_TOKEN_TERBATAS)"
fi

# =====================================================================
# 3) DATA DESA — Supabase sungguhan
# =====================================================================
printf '\n%s== 3) DATA DESA (Supabase) ==%s\n' "$KUNING" "$RESET"

ADMIN POST /admin/pelanggan "{\"kodeAktivasi\":\"$KODE\",\"namaDesa\":\"$NAMA_DESA\",\"kontak\":\"0812-0000-0000\",\"maxPerangkat\":2}"
cek_kode 200 "pendaftaran desa uji $KODE"
ID_PELANGGAN="$(jstr "$HTTP_BODY" "id")"
cek "$(if [ -n "$ID_PELANGGAN" ]; then echo 0; else echo 1; fi)" "pendaftaran mengembalikan id desa"

ADMIN POST /admin/pelanggan "{\"kodeAktivasi\":\"$KODE\",\"namaDesa\":\"$NAMA_DESA\",\"kontak\":\"0812-0000-0000\",\"maxPerangkat\":2}"
cek_kode 200 "pendaftaran ulang tidak ditolak (idempoten)"
cek "$(if [ "$(jstr "$HTTP_BODY" "id")" = "$ID_PELANGGAN" ]; then echo 0; else echo 1; fi)" \
    "pendaftaran ulang memakai id desa yang sama"

baca_daftar_desa
BARIS="$(baris_desa "$KODE")"
cek "$(if [ -n "$BARIS" ]; then echo 0; else echo 1; fi)" "desa uji muncul di daftar"
cek "$(if [ "$(jnum "$BARIS" "maxPerangkat")" = "2" ]; then echo 0; else echo 1; fi)" \
    "batas perangkat terbaca 2"

ADMIN GET "/admin/pelanggan/$KODE"
cek_kode 200 "GET /admin/pelanggan/{kode}"
cek "$(if [ "$(jstr "$HTTP_BODY" "kodeAktivasi")" = "$KODE" ]; then echo 0; else echo 1; fi)" \
    "kode aktivasi terbaca persis"
ADMIN GET /admin/pelanggan/UJI-YANG-TIDAK-ADA
cek_kode 404 "desa tak dikenal → 404"

ADMIN PUT "/admin/pelanggan/$KODE" "{\"namaDesa\":\"$NAMA_DESA_UBAH\",\"kontak\":\"0800-111-2222\"}"
cek_kode 200 "ubah nama & kontak desa"
ADMIN GET "/admin/pelanggan/$KODE"
cek "$(if [ "$(jstr "$HTTP_BODY" "namaDesa")" = "$NAMA_DESA_UBAH" ]; then echo 0; else echo 1; fi)" \
    "perubahan nama tersimpan (kode tidak berubah)"

ADMIN POST "/admin/pelanggan/$KODE/batas" '{"batas":2}'
cek_kode 200 "ubah batas perangkat"
ADMIN POST "/admin/pelanggan/$KODE/batas" '{"batas":0}'
cek_kode 400 "batas 0 ditolak"
ADMIN POST "/admin/pelanggan/$KODE/batas" '{"batas":101}'
cek_kode 400 "batas 101 ditolak"

# =====================================================================
# 4) AKTIVASI & PERANGKAT
# =====================================================================
printf '\n%s== 4) AKTIVASI & PERANGKAT ==%s\n' "$KUNING" "$RESET"

AKTIVASI POST /aktivasi '{"kodeAktivasi":"","idPerangkat":"uji-1"}'
cek_kode 400 "kode kosong → 400"
AKTIVASI POST /aktivasi '{"kodeAktivasi":"UJI-YANG-TIDAK-ADA","idPerangkat":"uji-1"}'
cek_kode 404 "kode asing → 404"

AKTIVASI POST /aktivasi "{\"kodeAktivasi\":\"$KODE\",\"idPerangkat\":\"uji-1\"}"
cek_kode 200 "aktivasi desa yang belum bayar"
cek "$(if [ "$(jbool "$HTTP_BODY" "aktif")" = "false" ]; then echo 0; else echo 1; fi)" \
    "aktivasi tanpa pembayaran TIDAK mengeluarkan token (aktif=false)"
cek "$(if [ -z "$(jstr "$HTTP_BODY" "token")" ]; then echo 0; else echo 1; fi)" \
    "balasan tidak memuat token apa pun"

AKTIVASI POST /aktifkan-trial "{\"kodeAktivasi\":\"$KODE\",\"idPerangkat\":\"uji-1\"}"
cek_kode 200 "aktifkan trial"
TOKEN_TRIAL="$(jstr "$HTTP_BODY" "token")"
cek "$(if [ -n "$TOKEN_TRIAL" ]; then echo 0; else echo 1; fi)" "trial menerbitkan token"
AKTIVASI POST /aktifkan-trial "{\"kodeAktivasi\":\"$KODE\",\"idPerangkat\":\"uji-1\"}"
cek_kode 403 "trial kedua ditolak (sekali per desa)"

# --- Token: struktur + klaim + tanda tangan ES256 terhadap /kunci-publik ---
if [ -n "$TOKEN_TRIAL" ]; then
    IFS='.' read -r T_VERSI T_PAYLOAD T_TTD <<EOF
$TOKEN_TRIAL
EOF
    cek "$(if [ "$T_VERSI" = "v1" ]; then echo 0; else echo 1; fi)" "token berversi v1"
    SEKARANG=$(date +%s)
    if [ -n "$PYTHON" ] && command -v openssl >/dev/null 2>&1; then
        cat > "$TMP_DIR/verifikasi_token.py" <<'PYEOF'
import base64, json, os, subprocess, sys, tempfile

def b64u(s):
    b = s.replace("-", "+").replace("_", "/")
    return base64.b64decode(b + "=" * (-len(b) % 4))

def der_int(b):
    b = b.lstrip(b"\x00") or b"\x00"
    if b[0] & 0x80:
        b = b"\x00" + b
    return b"\x02" + bytes([len(b)]) + b

token, pub = sys.argv[1], sys.argv[2]
bagian = token.split(".")
if len(bagian) != 3 or bagian[0] != "v1":
    print(json.dumps({"sah": False, "alasan": "bukan token v1 tiga bagian"}))
    raise SystemExit(0)
payload, ttd = b64u(bagian[1]), b64u(bagian[2])
if len(ttd) != 64:
    print(json.dumps({"sah": False, "alasan": "tanda tangan %d byte, bukan 64" % len(ttd)}))
    raise SystemExit(0)
try:
    klaim = json.loads(payload.decode("utf-8"))
except Exception as exc:
    print(json.dumps({"sah": False, "alasan": "payload bukan JSON: %s" % exc}))
    raise SystemExit(0)
der = der_int(ttd[:32]) + der_int(ttd[32:])
der = b"\x30" + bytes([len(der)]) + der
with tempfile.TemporaryDirectory() as d:
    open(os.path.join(d, "pub.der"), "wb").write(b64u(pub))
    open(os.path.join(d, "sig.der"), "wb").write(der)
    open(os.path.join(d, "payload.bin"), "wb").write(payload)
    # -pubin SENGAJA tidak dipakai: build OpenSSL 3 (Windows/Git Bash)
    # menolaknya dengan "Multiple digest or unknown options". -verify sudah
    # membaca kuncinya sebagai kunci publik, jadi -keyform DER cukup.
    r = subprocess.run(
        ["openssl", "dgst", "-sha256", "-keyform", "DER",
         "-verify", os.path.join(d, "pub.der"),
         "-signature", os.path.join(d, "sig.der"),
         os.path.join(d, "payload.bin")],
        capture_output=True, text=True)
# Kompak (tanpa spasi) supaya pembaca JSON skripnya bisa memakai grep.
print(json.dumps({"sah": r.returncode == 0, "klaim": klaim,
                  "alasan": None if r.returncode == 0 else (r.stderr or r.stdout).strip()},
                 separators=(",", ":")))
PYEOF
        if "$PYTHON" "$TMP_DIR/verifikasi_token.py" "$TOKEN_TRIAL" "$KUNCI_PUBLIK" \
                >"$TMP_DIR/token.json" 2>"$TMP_DIR/token.err"; then
            HASIL_TOKEN="$(cat "$TMP_DIR/token.json")"
            cek "$(if [ "$(jbool "$HASIL_TOKEN" "sah")" = "true" ]; then echo 0; else echo 1; fi)" \
                "tanda tangan token TERVERIFIKASI terhadap /kunci-publik" \
                "$(ringkas "$HASIL_TOKEN")"
            KODE_KLAIM="$(jstr "$HASIL_TOKEN" "kode")"
            PAKE2="$(jstr "$HASIL_TOKEN" "paket")"
            EKSP="$(jnum "$HASIL_TOKEN" "exp")"
            cek "$(if [ "$KODE_KLAIM" = "$KODE" ] && [ "$PAKE2" = "trial" ]; then echo 0; else echo 1; fi)" \
                "klaim token menyebut kode uji & paket trial (kode=$KODE_KLAIM, paket=$PAKE2)"
            cek "$(if [ -n "$EKSP" ] && [ "$EKSP" -gt "$SEKARANG" ] && [ "$EKSP" -le $((SEKARANG + 15 * 86400)) ]; then echo 0; else echo 1; fi)" \
                "exp token jatuh tempo dalam ≤15 hari (exp=$EKSP, sekarang=$SEKARANG)"
            CEK_TOKEN="$(jnum "$HASIL_TOKEN" "cek")"
            cek "$(if [ -n "$CEK_TOKEN" ] && [ "$CEK_TOKEN" -le $((SEKARANG + 60)) ]; then echo 0; else echo 1; fi)" \
                "klaim cek (ancang masa tenggang) ikut ditandatangani"
        else
            gagal "verifikasi tanda tangan token" \
                "$PYTHON/openssl gagal: $(ringkas "$(cat "$TMP_DIR/token.err" 2>/dev/null)")"
        fi
    else
        dilewati "verifikasi tanda tangan token (butuh Python + openssl; struktur & klaimnya tetap diperiksa di atas)"
    fi
else
    dilewati "pemeriksaan struktur & tanda tangan token (token tidak terbit)"
fi

# --- Batas perangkat ---
AKTIVASI POST /aktivasi "{\"kodeAktivasi\":\"$KODE\",\"idPerangkat\":\"uji-1\"}"
cek_kode 200 "aktivasi ulang perangkat yang sama"
AKTIVASI POST /aktivasi "{\"kodeAktivasi\":\"$KODE\",\"idPerangkat\":\"uji-2\"}"
cek_kode 200 "perangkat kedua (batas 2)"
AKTIVASI POST /aktivasi "{\"kodeAktivasi\":\"$KODE\",\"idPerangkat\":\"uji-3\"}"
cek_kode 403 "perangkat ketiga melewati batas → 403"
cek_isi "pusat administer" "penolakan menyebut jalan pemulihan di pusat administer"

ADMIN GET "/admin/pelanggan/$KODE/perangkat"
cek_kode 200 "daftar perangkat"
cek "$(if [ "$(jnum "$HTTP_BODY" "jumlah")" = "2" ]; then echo 0; else echo 1; fi)" \
    "perangkat yang DITOLAK tidak tercatat (jumlah tetap 2)"

ADMIN DELETE "/admin/pelanggan/$KODE/perangkat/uji-1"
cek_kode 200 "hapus satu perangkat"
AKTIVASI POST /aktivasi "{\"kodeAktivasi\":\"$KODE\",\"idPerangkat\":\"uji-3\"}"
cek_kode 200 "kuota terbuka setelah perangkat dihapus"
ADMIN DELETE "/admin/pelanggan/$KODE/perangkat"
cek_kode 200 "reset semua perangkat"

# =====================================================================
# 5) PEMBUKUAN PEMBAYARAN MANUAL
# =====================================================================
printf '\n%s== 5) PEMBUKUAN MANUAL ==%s\n' "$KUNING" "$RESET"

PNG_1PX="iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="

ADMIN POST "/admin/pelanggan/$KODE/tandai-lunas" \
    "{\"paket\":\"bulanan\",\"nominal\":$HARGA,\"catatan\":\"Uji E2E manual\"}"
cek_kode 200 "tandai lunas (tanpa lampiran)"
ORDER_MANUAL_1="$(jstr "$HTTP_BODY" "orderId")"
cek "$(if [ "${ORDER_MANUAL_1#MANUAL-}" != "$ORDER_MANUAL_1" ]; then echo 0; else echo 1; fi)" \
    "order id pembukuan manual berawalan MANUAL- (jejak terlihat di riwayat)"
cek "$(if [ "$(jnum "$HTTP_BODY" "nominal")" = "$HARGA" ]; then echo 0; else echo 1; fi)" \
    "nominal yang dicatat = harga katalog ($HARGA)"

ADMIN GET "/admin/pelanggan/$KODE/pembayaran"
cek_kode 200 "riwayat pembayaran"
cek "$(if [ "$(jnum "$HTTP_BODY" "jumlah")" = "1" ]; then echo 0; else echo 1; fi)" \
    "satu pembayaran tercatat"
cek_isi "$ORDER_MANUAL_1" "order id muncul di riwayat"

ADMIN GET "/admin/pelanggan/$KODE/pembayaran/$ORDER_MANUAL_1/kuitansi"
cek_kode 200 "kuitansi siap cetak"
cek_isi "$KODE" "kuitansi memuat kode aktivasi desa"
ADMIN GET "/admin/pelanggan/$KODE/pembayaran/$ORDER_MANUAL_1/bukti"
cek_kode 404 "pembayaran tanpa lampiran → 404 di endpoint bukti"

ADMIN POST "/admin/pelanggan/$KODE/tandai-lunas" \
    "{\"paket\":\"bulanan\",\"catatan\":\"Uji E2E lampiran\",\"buktiBase64\":\"data:image/png;base64,$PNG_1PX\",\"buktiNama\":\"bukti.png\"}"
cek_kode 200 "tandai lunas dengan lampiran bukti transfer"
ORDER_MANUAL_2="$(jstr "$HTTP_BODY" "orderId")"
cek "$(if [ "$(jstr "$HTTP_BODY" "tipeBukti")" = "image/png" ]; then echo 0; else echo 1; fi)" \
    "jenis lampiran terbaca image/png dari magic bytes"
ADMIN GET "/admin/pelanggan/$KODE/pembayaran/$ORDER_MANUAL_2/bukti"
cek_kode 200 "lampiran bukti bisa diambil kembali"
# Bandingkan ISI berkasnya (base64), bukan teks balasan: badan berisi byte biner.
ISI_BUKTI="$(base64 -w0 < "$TMP_BODY" 2>/dev/null || base64 < "$TMP_BODY" | tr -d '\n')"
cek "$(if [ "$ISI_BUKTI" = "$PNG_1PX" ]; then echo 0; else echo 1; fi)" \
    "isi lampiran persis PNG yang dikirim" \
    "${ISI_BUKTI:0:40}… ≠ ${PNG_1PX:0:40}…"

baca_daftar_desa
BERLAKU_SEBELUM_MANUAL="$(jstr "$(baris_desa "$KODE")" "berlakuSampai")"
ADMIN POST "/admin/pelanggan/$KODE/tandai-lunas" \
    '{"paket":"bulanan","catatan":"Uji E2E bukti rusak","buktiBase64":"aGFsIGJ1bG9oIGJ1a2FuIGdhbWJhcg==","buktiNama":"nota.txt"}'
cek_kode 400 "lampiran bukan gambar → seluruh penandaan dibatalkan"
baca_daftar_desa
cek "$(if [ "$(jstr "$(baris_desa "$KODE")" "berlakuSampai")" = "$BERLAKU_SEBELUM_MANUAL" ]; then echo 0; else echo 1; fi)" \
    "penandaan yang ditolak TIDAK memperpanjang langganan"
ADMIN GET "/admin/pelanggan/$KODE/pembayaran"
cek "$(if [ "$(jnum "$HTTP_BODY" "jumlah")" = "2" ]; then echo 0; else echo 1; fi)" \
    "hanya 2 pembayaran tercatat (yang ditolak tidak meninggalkan jejak)"

ADMIN GET "/admin/ekspor/desa.csv"
cek_kode 200 "ekspor CSV daftar desa"
cek_isi "$KODE" "CSV daftar desa memuat desa uji"
ADMIN GET "/admin/ekspor/pembayaran/$KODE.csv"
cek_kode 200 "ekspor CSV pembayaran"
cek_isi "$ORDER_MANUAL_1" "CSV pembayaran memuat order manual"

# =====================================================================
# 6) MIDTRANS SUNGGUHAN
# =====================================================================
printf '\n%s== 6) MIDTRANS SUNGGUHAN (%s) ==%s\n' "$KUNING" "$LINGKUNGAN" "$RESET"

TAGIHAN POST /admin/uji-pembayaran "{\"kodeAktivasi\":\"$KODE\",\"paket\":\"bulanan\",\"overrideNotifikasi\":false}"
cek_kode 200 "buat transaksi Snap NYATA"
ORDER="$(jstr "$HTTP_BODY" "orderId")"
LINK="$(jstr "$HTTP_BODY" "linkPembayaran")"
TOKEN_SNAP="$(jstr "$HTTP_BODY" "token")"
ORDER_UJI+=("$ORDER")
cek "$(if [ -n "$ORDER" ]; then echo 0; else echo 1; fi)" "order id terbit ($ORDER)"
cek "$(if [ -n "$TOKEN_SNAP" ] && [ -n "$LINK" ]; then echo 0; else echo 1; fi)" \
    "token & tautan Snap terbit (${LINK:-(kosong)})"
cek "$(if [ "$(jnum "$HTTP_BODY" "nominal")" = "$HARGA" ]; then echo 0; else echo 1; fi)" \
    "nominal transaksi uji = harga katalog ($HARGA)"
cek "$(if [ "$(jbool "$HTTP_BODY" "desaBaru")" = "false" ]; then echo 0; else echo 1; fi)" \
    "desa uji dipakai yang sudah ada, bukan dibuat lagi"

# Transaksi kedua: TIDAK akan dibayar, jadi menjadi bahan pemeriksaan
# rekonsiliasi (tagihan nyata yang statusnya ditanya ke Midtrans).
TAGIHAN POST /admin/uji-pembayaran "{\"kodeAktivasi\":\"$KODE\",\"paket\":\"bulanan\",\"overrideNotifikasi\":false}"
ORDER_BELUM_BAYAR="$(jstr "$HTTP_BODY" "orderId")"
ORDER_UJI+=("$ORDER_BELUM_BAYAR")
cek_kode 200 "buat transaksi kedua (tidak dibayar)"
cek "$(if [ -n "$ORDER_BELUM_BAYAR" ] && [ "$ORDER_BELUM_BAYAR" != "$ORDER" ]; then echo 0; else echo 1; fi)" \
    "order kedua berbeda ($ORDER_BELUM_BAYAR)"

# Order benar-benar ada di akun merchant? Ditanyakan langsung ke Core API
# Midtrans memakai ServerKey — bukan lewat server yang sedang diuji.
#
# CATATAN Perilaku Midtrans: order Snap yang BELUM dibayar belum muncul sebagai
# transaksi, jadi Core API menjawab HTTP 200 dengan body
# {"status_code":"404","status_message":"Transaction doesn't exist."}.
# Kunci yang salah justru dijawab 401, jadi HTTP 200 sudah membuktikan ServerKey
# diterima akun merchant; status "belum ada" itu sendiri sudah diharapkan
# (transaksi ini memang belum dibayar).
if command -v curl >/dev/null 2>&1; then
    CORE_CODE=$(curl -sS -m 40 -o "$TMP_BODY" -w '%{http_code}' -u "$SK:" \
        "$CORE_API/v2/$ORDER/status" 2>>"$TMP_ERR") || CORE_CODE="000"
    CORE_BODY="$(cat "$TMP_BODY" 2>/dev/null)"
    CORE_STATUS="$(printf '%s' "$CORE_BODY" | grep -o '"transaction_status":"[^"]*"' | head -1)"
    if [ "$CORE_CODE" = "401" ]; then
        gagal "ServerKey diterima akun merchant Midtrans $LINGKUNGAN" \
            "$CORE_API menolak kredensial (401) — periksa Midtrans:ServerKey / MIDTRANS_SERVER_KEY"
    elif [ "$CORE_CODE" != "200" ]; then
        gagal "Core API Midtrans $LINGKUNGAN terjangkau" \
            "$CORE_API/v2/$ORDER/status → HTTP $CORE_CODE: $(ringkas "$CORE_BODY")"
    elif [ -n "$CORE_STATUS" ]; then
        lulus "ServerKey diterima merchant; order $ORDER tercatat di Midtrans ($CORE_STATUS)"
    elif printf '%s' "$CORE_BODY" | grep -q "Transaction doesn't exist"; then
        lulus "ServerKey diterima merchant; order Snap $ORDER belum punya transaksi (wajar — belum dibayar)"
    else
        gagal "jawaban Core API terbaca" "HTTP $CORE_CODE: $(ringkas "$CORE_BODY")"
    fi
else
    dilewati "pemeriksaan Core API merchant (curl tidak ada)"
fi

ADMIN GET "/admin/uji-pembayaran/$ORDER"
cek_kode 200 "status transaksi uji terbaca"
# Order Snap yang belum dibayar BELUM menjadi transaksi di Midtrans (Core API
# menjawab "Transaction doesn't exist"), jadi server yang jujur melaporkan
# ditemukan=false — bukan "ditemukan" dengan status kosong seperti yang pernah
# terlihat sebelum perbaikan MidtransService.
cek "$(if [ "$(jbool "$HTTP_BODY" "ditemukan")" = "false" ]; then echo 0; else echo 1; fi)" \
    "status gateway jujur untuk order belum dibayar (ditemukan=false, bukan 'ditemukan' tanpa status)"

# --- Notifikasi: semua pintu penolakan ---
WEBHOOK='{"order_id":"'"$ORDER"'","transaction_status":"settlement","status_code":"200","gross_amount":"'"$GROSS"'"}'
req webhook POST /webhook-pembayaran "$WEBHOOK" -H "X-Forwarded-Proto: $FWD_PROTO"
cek_kode 403 "notifikasi tanpa signature → 403"

SIG_SALAH="$(sig512 "$ORDER" "200" "$GROSS" "SERVER-KEY-SALAH")"
req webhook POST /webhook-pembayaran \
    "{\"order_id\":\"$ORDER\",\"transaction_status\":\"settlement\",\"status_code\":\"200\",\"gross_amount\":\"$GROSS\",\"signature_key\":\"$SIG_SALAH\"}" \
    -H "X-Forwarded-Proto: $FWD_PROTO"
cek_kode 403 "signature dihitung dengan kunci salah → 403"

# Signature sah menurut kunci, tapi dihitung atas nilai yang BERBEDA dari isi
# body (gross 1.00) — persis penyuntingan payload yang harus tertolak.
SIG_BEDA_ISI="$(sig512 "$ORDER" "200" "1.00" "$SK")"
req webhook POST /webhook-pembayaran \
    "{\"order_id\":\"$ORDER\",\"transaction_status\":\"settlement\",\"status_code\":\"200\",\"gross_amount\":\"$GROSS\",\"signature_key\":\"$SIG_BEDA_ISI\"}" \
    -H "X-Forwarded-Proto: $FWD_PROTO"
cek_kode 403 "signature tidak cocok dengan isi body → 403"

SIG_1RUPIAH="$(sig512 "$ORDER" "200" "1.00" "$SK")"
req webhook POST /webhook-pembayaran \
    "{\"order_id\":\"$ORDER\",\"transaction_status\":\"settlement\",\"status_code\":\"200\",\"gross_amount\":\"1.00\",\"signature_key\":\"$SIG_1RUPIAH\"}" \
    -H "X-Forwarded-Proto: $FWD_PROTO"
cek_kode 400 "nominal Rp1 → 400 (bayar Rp1 tidak membeli langganan)"

req webhook POST /webhook-pembayaran 'bukan-json' -H "X-Forwarded-Proto: $FWD_PROTO"
cek_kode 400 "body bukan JSON → 400"

SIG_PENDING="$(sig512 "$ORDER_BELUM_BAYAR" "200" "$GROSS" "$SK")"
baca_daftar_desa
BERLAKU_SEBELUM_BAYAR="$(jstr "$(baris_desa "$KODE")" "berlakuSampai")"
req webhook POST /webhook-pembayaran \
    "{\"order_id\":\"$ORDER_BELUM_BAYAR\",\"transaction_status\":\"pending\",\"status_code\":\"200\",\"gross_amount\":\"$GROSS\",\"signature_key\":\"$SIG_PENDING\"}" \
    -H "X-Forwarded-Proto: $FWD_PROTO"
cek_kode 200 "notifikasi ber-status 'pending' diterima tapi tidak memberi hari"
cek_isi "tidak memperpanjang" "server menyatakan status tidak berduit"
baca_daftar_desa
cek "$(if [ "$(jstr "$(baris_desa "$KODE")" "berlakuSampai")" = "$BERLAKU_SEBELUM_BAYAR" ]; then echo 0; else echo 1; fi)" \
    "status tidak berduit tidak mengubah jatuh tempo"

SIG_404="$(sig512 "UJI-TIDAK-ADA-000" "200" "$GROSS" "$SK")"
req webhook POST /webhook-pembayaran \
    "{\"order_id\":\"UJI-TIDAK-ADA-000\",\"transaction_status\":\"settlement\",\"status_code\":\"200\",\"gross_amount\":\"$GROSS\",\"signature_key\":\"$SIG_404\"}" \
    -H "X-Forwarded-Proto: $FWD_PROTO"
cek_kode 404 "order tak dikenal → 404"

# --- Yang sah: settlement ---
SIG_SAHIH="$(sig512 "$ORDER" "200" "$GROSS" "$SK")"
req webhook POST /webhook-pembayaran \
    "{\"order_id\":\"$ORDER\",\"transaction_status\":\"settlement\",\"status_code\":\"200\",\"gross_amount\":\"$GROSS\",\"payment_type\":\"other_qris\",\"fraud_status\":\"accept\",\"signature_key\":\"$SIG_SAHIH\",\"user_agent\":\"Midtrans-Sandbox/1.0\"}" \
    -H "X-Forwarded-Proto: $FWD_PROTO"
cek_kode 200 "notifikasi settlement bertanda tangan sah → 200"
cek "$(if [ "$(jbool "$HTTP_BODY" "diterima")" = "true" ]; then echo 0; else echo 1; fi)" "server menerima notifikasi"
cek "$(if [ "$(jnum "$HTTP_BODY" "hariDitambah")" = "$HARI" ]; then echo 0; else echo 1; fi)" \
    "$HARI hari ditambahkan (durasi dari tagihan, bukan dari notifikasi)"

req webhook POST /webhook-pembayaran \
    "{\"order_id\":\"$ORDER\",\"transaction_status\":\"settlement\",\"status_code\":\"200\",\"gross_amount\":\"$GROSS\",\"payment_type\":\"other_qris\",\"fraud_status\":\"accept\",\"signature_key\":\"$SIG_SAHIH\"}" \
    -H "X-Forwarded-Proto: $FWD_PROTO"
cek_kode 200 "webhook ulang diterima (idempoten)"
cek "$(if [ "$(jbool "$HTTP_BODY" "duplikat")" = "true" ]; then echo 0; else echo 1; fi)" \
    "webhook ulang dilaporkan duplikat"
cek "$(if [ -z "$(jnum "$HTTP_BODY" "hariDitambah")" ]; then echo 0; else echo 1; fi)" \
    "webhook ulang tidak menambah hari"

baca_daftar_desa
BERLAKU_SETELAH_BAYAR="$(jstr "$(baris_desa "$KODE")" "berlakuSampai")"
cek "$(if [ -n "$BERLAKU_SETELAH_BAYAR" ] && [ "$BERLAKU_SETELAH_BAYAR" != "$BERLAKU_SEBELUM_BAYAR" ]; then echo 0; else echo 1; fi)" \
    "jatuh tempo desa bertambah (dari ${BERLAKU_SEBELUM_BAYAR:-kosong} menjadi $BERLAKU_SETELAH_BAYAR)"

ADMIN GET "/admin/uji-pembayaran/$ORDER"
cek_kode 200 "jejak notifikasi terbaca"
cek "$(if [ "$(jbool "$HTTP_BODY" "webhookDiterima")" = "true" ]; then echo 0; else echo 1; fi)" \
    "server mencatat notifikasi diterima"
NOTIF_SAHIH="$(printf '%s' "$HTTP_BODY" | grep -o '"signatureValid":true' | head -1)"
cek "$(if [ -n "$NOTIF_SAHIH" ]; then echo 0; else echo 1; fi)" \
    "jejak mencatat signature sah + balasan HTTP 200"

ADMIN GET /admin/uji-pembayaran/notifikasi
cek_kode 200 "daftar notifikasi masuk"
cek_isi "$ORDER" "notifikasi untuk order uji tercatat"

# Opsional: buktikan jalur PUBLIK (Midtrans → tunnel → server ini).
if [ -n "$TUNEL" ]; then
    req webhook POST "$TUNEL/webhook-pembayaran" \
        "{\"order_id\":\"$ORDER\",\"transaction_status\":\"settlement\",\"status_code\":\"200\",\"gross_amount\":\"$GROSS\",\"payment_type\":\"other_qris\",\"fraud_status\":\"accept\",\"signature_key\":\"$SIG_SAHIH\"}" \
        -H "X-Forwarded-Proto: $FWD_PROTO"
    cek_kode 200 "notifikasi terkirim lewat URL publik ($TUNEL)"
else
    dilewati "notifikasi lewat URL publik (setel TUNEL_PUBLIK bila ingin lewat tunnel)"
fi

# =====================================================================
# 7) REKONSILIASI
# =====================================================================
printf '\n%s== 7) REKONSILIASI ==%s\n' "$KUNING" "$RESET"

TAGIHAN POST /admin/rekonsiliasi '{}'
cek_kode 200 "PUTARAN 1 rekonsiliasi (status ditanyakan ke Midtrans sungguhan)"
DIPERIKSA_1="$(jnum "$HTTP_BODY" "diperiksa")"
SELESAI_1="$(jnum "$HTTP_BODY" "diselesaikan")"
SUDAH_1="$(jnum "$HTTP_BODY" "sudahDiselesaikan")"
if [ "${DIPERIKSA_1:-0}" = "0" ]; then
    dilewati "tagihan uji ikut diperiksa (server memakai minimal umur bawaan 10 menit; setel REKONSILIASI_MINIMAL_UMUR_MENIT=0 untuk memeriksanya di putaran yang sama)"
else
    lulus "$DIPERIKSA_1 tagihan masuk putaran rekonsiliasi (selesai: $SELESAI_1, sudah diproses: $SUDAH_1)"
fi

TAGIHAN POST /admin/rekonsiliasi '{}'
cek_kode 200 "PUTARAN 2 rekonsiliasi"
baca_daftar_desa
cek "$(if [ "$(jstr "$(baris_desa "$KODE")" "berlakuSampai")" = "$BERLAKU_SETELAH_BAYAR" ]; then echo 0; else echo 1; fi)" \
    "putaran kedua tidak menambah hari lagi (idempoten)"

# =====================================================================
# 8) PENGUMUMAN
# =====================================================================
printf '\n%s== 8) PENGUMUMAN ==%s\n' "$KUNING" "$RESET"

ADMIN POST /admin/pengumuman "{\"judul\":\"$JUDUL_UJI\",\"isi\":\"Uji end-to-end server lisensi.\",\"jenis\":\"info\"}"
cek_kode 200 "terbitkan pengumuman"
ID_PENGUMUMAN="$(jstr "$HTTP_BODY" "id")"
KIRIM GET /pengumuman
cek_kode 200 "GET /pengumuman publik"
cek_isi "$JUDUL_UJI" "pengumuman tampil di kanal publik"
ADMIN GET /admin/pengumuman
cek_kode 200 "GET /admin/pengumuman"
cek_isi "$ID_PENGUMUMAN" "pengumuman terdaftar di daftar admin"
ADMIN DELETE "/admin/pengumuman/$ID_PENGUMUMAN"
cek_kode 200 "hapus pengumuman"
KIRIM GET /pengumuman
case "$HTTP_BODY" in
    *"$JUDUL_UJI"*) gagal "pengumuman hilang dari kanal publik" "masih ada: $(ringkas "$HTTP_BODY")" ;;
    *) lulus "pengumuman hilang dari kanal publik" ;;
esac

# =====================================================================
# 9) PEMBERSIHAN & VERIFIKASI AKHIR
# =====================================================================
printf '\n%s== 9) PEMBERSIHAN ==%s\n' "$KUNING" "$RESET"

ADMIN DELETE "/admin/pelanggan/$KODE"
cek_kode 409 "penghapusan normal ditolak selama masih ada jejak uang (409)"
ADMIN DELETE "/admin/pelanggan/$KODE?konfirmasi=$KODE"
cek_kode 200 "hapus paksa desa uji beserta jejak ujinya"
cek_isi "dihapus" "balasan menyebut desa terhapus"

ADMIN GET "/admin/pelanggan/$KODE"
cek_kode 404 "desa uji tidak bisa dibaca lagi"
ADMIN GET "/admin/pelanggan/$KODE/pembayaran"
cek_kode 404 "riwayat pembayaran desa uji ikut hilang"

baca_daftar_desa
JUMLAH_AKHIR="$(jumlah_desa)"
cek "$(if [ "$JUMLAH_AKHIR" = "$JUMLAH_AWAL" ]; then echo 0; else echo 1; fi)" \
    "jumlah desa kembali seperti sebelum uji ($JUMLAH_AWAL → $JUMLAH_AKHIR)"
baca_daftar_pengumuman
for id in $(printf '%s' "$DAFTAR_PENGUMUMAN" \
        | grep -o "\"id\":\"[^\"]*\",\"judul\":\"$JUDUL_UJI[^\"]*\"" \
        | sed 's/^"id":"//; s/","judul".*$//'); do
    ADMIN DELETE "/admin/pengumuman/$id"
    printf '  pengumuman sisa %s → HTTP %s\n' "$id" "$HTTP_CODE"
done
printf '  %sverifikasi%s jejak uji di Supabase tidak tersisa (desa uji 404, jumlah desa sama)\n' \
    "$HIJAU" "$RESET"
printf '  transaksi Snap yang belum dibayar tetap ada di dashboard Midtrans %s: %s\n' \
    "$LINGKUNGAN" "${ORDER_UJI[*]:-(tidak ada)}"

# =====================================================================
# RINGKASAN
# =====================================================================
printf '\n%s== RINGKASAN ==%s\n' "$KUNING" "$RESET"
printf '  server      : %s\n' "$BASE_URL"
printf '  gateway     : %s\n' "$LINGKUNGAN"
printf '  desa uji    : %s (dihapus di akhir putaran)\n' "$KODE"
printf '  order Snap  : %s\n' "${ORDER_UJI[*]:-(tidak ada)}"
printf '  %s%d lulus%s, %s%d gagal%s, %d dilewati\n' \
    "$HIJAU" "$LULUS" "$RESET" "$MERAH" "$GAGAL" "$RESET" "$LEWAT"

if [ "${#ORDER_UJI[@]}" -gt 0 ]; then
    printf '\n  Catatan: transaksi Snap di atas tetap tercatat di dashboard Midtrans %s\n' "$LINGKUNGAN"
    printf '  sebagai order BELUM DIBAYAR; hapus manual di sana bila tidak diinginkan.\n'
fi

[ "$GAGAL" = "0" ] || exit 1
exit 0
