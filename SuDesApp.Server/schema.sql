-- =====================================================================
-- Skema SuDesApp — server lisensi & langganan (Supabase Postgres)
-- =====================================================================
-- CARA PAKAI
--   1. Supabase Dashboard → SQL Editor → New query → tempel seluruh isi
--      berkas ini → Run. Aman dijalankan berulang kali (CREATE/ADD IF NOT
--      EXISTS + DROP POLICY di depan CREATE POLICY), jadi tidak merusak data.
--   2. Setel kata sandi role aplikasi (sekali):
--        ALTER ROLE sudes_server PASSWORD '<kata sandi acak>';
--      lalu pakai role itu di SUPABASE_DB_USER + SUPABASE_DB_PASSWORD.
--      Cukup kata sandinya — mengubah atribut role (LOGIN/BYPASSRLS) bisa
--      ditolak platform. Lakukan SEBELUM start pertama: pooler Supabase butuh
--      waktu sesaat untuk menerima kata sandi baru (bila start pertama masih
--      mengeluh soal login, jalankan lagi sebentar lagi).
--      JANGAN menyajikan server dengan role postgres: pemilik tabel
--      melewati RLS, jadi isolasi antar-desa ikut hilang.
--
-- Server juga menjalankan skema ini sendiri saat start BILA kredensial
-- migrasi diisi (SUPABASE_MIGRATE_PASSWORD) — dan selalu memverifikasi
-- RLS aktif sebelum melayani permintaan.
--
-- ISOLASI ANTAR-DESA (RLS)
--   tenant_id = id desa (pelanggan.id). Kebijakan RLS hanya mengizinkan
--   baris yang tenant_id-nya sama dengan GUC `app.tenant_id` milik sesi.
--   GUC itu TIDAK PERNAH datang dari klien: server menyetelnya per
--   transaksi dari id pelanggan yang sudah diverifikasi (kode aktivasi /
--   order_id yang tanda tangannya sah). Tanpa GUC, predicate-nya NULL →
--   nol baris: gagal tertutup, bukan terbuka.
-- =====================================================================

-- ---------- Tabel ----------
-- ---------- Pelanggan: satu baris per desa yang memakai aplikasi ----------
CREATE TABLE IF NOT EXISTS pelanggan (
    id             TEXT PRIMARY KEY,
    kode_aktivasi  TEXT NOT NULL UNIQUE,   -- diketik pengguna di aplikasi
    nama_desa      TEXT NOT NULL,
    kontak         TEXT,
    max_perangkat  INT  NOT NULL DEFAULT 2,-- batas PC per langganan
    dibuat         TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- ---------- Langganan: satu baris per pelanggan (1-1 dengan pelanggan) -----
CREATE TABLE IF NOT EXISTS langganan (
    pelanggan_id              TEXT PRIMARY KEY
                              REFERENCES pelanggan(id) ON DELETE CASCADE,
    paket                     TEXT NOT NULL DEFAULT 'bulanan',
    berlaku_sampai           TIMESTAMPTZ,   -- NULL = belum pernah bayar
    order_id_tagihan_terakhir TEXT,
    total_terbayar            INT NOT NULL DEFAULT 0
);

-- ---------- Tagihan: sudah dibuat, belum dibayar ----------
-- Webhook membaca paket + nominal + jumlah hari DARI SINI, bukan dari
-- konfigurasi server. Kalau harga paket berubah setelah tagihan terbit,
-- harga yang dipakai adalah harga yang benar-benar ditagihkan ke pengguna.
CREATE TABLE IF NOT EXISTS tagihan (
    order_id       TEXT PRIMARY KEY,
    pelanggan_id   TEXT NOT NULL
                   REFERENCES pelanggan(id) ON DELETE CASCADE,
    paket          TEXT NOT NULL,   -- bulanan | tahunan | ...
    nominal        BIGINT NOT NULL,
    hari           INT NOT NULL,    -- tambahan masa aktif setelah dibayar
    dibuat         TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- ---------- Pembayaran: jejak audit setiap dana masuk ----------
-- PRIMARY KEY(order_id) sekaligus menjadi penjaga idempotensi webhook.
-- Midtrans mengirim ulang webhook saat tidak menerima jawaban dalam 24 jam;
-- tanpa batasan ini satu transfer bisa menambah hak akses berulang kali.
--   catatan = referensi bukti bayar yang diketik operator pada pembukuan
--             manual ("QRIS 17/10 21:04"); NULL pada pembayaran gateway, yang
--             buktinya adalah notifikasi/status Midtrans itu sendiri. Bebas
--             diisi bersama lampiran gambar: keduanya saling melengkapi.
--   bukti_* = lampiran GAMBAR bukti transfer dari pembukuan manual (JPEG/PNG/
--             WEBP yang diunggah operator — lihat BuktiTransfer.cs). Disimpan
--             di baris pembayarannya sendiri supaya tidak ada pembayaran tanpa
--             lampirannya atau sebaliknya, dan supaya gambar ikut dilindungi
--             RLS per-desa seperti kolom lain di tabel ini.
CREATE TABLE IF NOT EXISTS pembayaran (
    order_id       TEXT PRIMARY KEY,
    pelanggan_id   TEXT NOT NULL
                   REFERENCES pelanggan(id) ON DELETE CASCADE,
    nominal        BIGINT NOT NULL,
    status         TEXT NOT NULL,
    dicatat        TIMESTAMPTZ NOT NULL DEFAULT now(),
    catatan        TEXT
);

-- ---------- Perangkat: PC yang pernah memakai kode aktivasi ----------
CREATE TABLE IF NOT EXISTS perangkat (
    pelanggan_id    TEXT NOT NULL
                    REFERENCES pelanggan(id) ON DELETE CASCADE,
    id_perangkat    TEXT NOT NULL,
    pertama_lihat   TIMESTAMPTZ NOT NULL DEFAULT now(),
    terakhir_lihat  TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (pelanggan_id, id_perangkat)
);

-- ---------- Pengumuman: pemberitahuan broadcast ke SEMUA instalasi desa ---
-- Satu baris = satu itu pengumuman. Aplikasi desa menagih GET /pengumuman
-- (publik) tiap 60 detik dan menampilkannya sebagai popup helaian baru yang
-- belum pernah tampil di perangkat masing-masing.
-- TIDAK ADA tenant_id: ini broadcast lintas desa, bukan data desa.
--   aktif  = on/off	saja matikan tanpa menghapus barisnya.
--   mulai  = tampil mulai waktu ini (NULL = sekarang).
--   sampai = tampil sampai waktu ini (NULL = selamanya sampai dimatikan).
--   jenis  = info (default) | peringatan (popup & lonceng diberi aksen warna).
CREATE TABLE IF NOT EXISTS pengumuman (
    id      TEXT PRIMARY KEY DEFAULT gen_random_uuid()::text,
    judul   TEXT NOT NULL,
    isi     TEXT NOT NULL,
    jenis   TEXT NOT NULL DEFAULT 'info',
    aktif   BOOLEAN NOT NULL DEFAULT true,
    mulai   TIMESTAMPTZ,
    sampai  TIMESTAMPTZ,
    dibuat  TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- ---------- Audit operator admin: jejak durable lintas restart ----------
-- Tidak memiliki FK ke pelanggan karena tindakan admin dapat bersifat global.
-- Role aplikasi hanya boleh menambah baris; tidak ada kebijakan baca/ubah/hapus.
CREATE TABLE IF NOT EXISTS admin_action_audit (
    id            BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    occurred_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
    operator_id   TEXT,
    operator_name TEXT,
    permission    TEXT NOT NULL,
    outcome       TEXT NOT NULL,
    http_method   TEXT NOT NULL,
    route         TEXT NOT NULL,
    status_code   INT NOT NULL,
    trace_id      TEXT NOT NULL
);

-- =====================================================================
-- TENANT_ID — kunci isolasi
-- =====================================================================
-- Ditambahkan lewat ALTER (bukan di CREATE TABLE) supaya instalasi yang
-- sudah berisi data ikut termigrasi dengan jalur yang sama: tambah kolom →
-- isi ulang dari pemilik baris → baru dijadikan NOT NULL.
-- Kolom trial: trial 30 hari hanya boleh sekali per desa; kolom inilah yang
-- menahannya — server yang mengingat, bukan kunci kejujuran klien
-- (lihat LayananAktivasi). Ditambah lewat ALTER supaya instalasi lama ikut
-- termigrasi dengan jalur yang sama seperti tenant_id.
ALTER TABLE pelanggan  ADD COLUMN IF NOT EXISTS trial_sudah BOOLEAN NOT NULL DEFAULT false;
-- Referensi bukti bayar untuk pembukuan manual (lihat LayananPembayaranManual).
-- Ditambah lewat ALTER juga supaya instalasi yang sudah berisi pembayaran ikut
-- punya kolomnya tanpa perlu migrasi terpisah.
ALTER TABLE pembayaran ADD COLUMN IF NOT EXISTS catatan TEXT;
-- Lampiran gambar bukti transfer dari pembukuan manual. bukti_ukuran disimpan
-- terpisah (bukan dihitung octet_length saat membaca) supaya daftar riwayat
-- tidak perlu menyentuh kolom BYTEA-nya sama sekali saat menampilkan tabel.
ALTER TABLE pembayaran ADD COLUMN IF NOT EXISTS bukti_gambar BYTEA;
ALTER TABLE pembayaran ADD COLUMN IF NOT EXISTS bukti_tipe   TEXT;
ALTER TABLE pembayaran ADD COLUMN IF NOT EXISTS bukti_ukuran INT;
ALTER TABLE pembayaran ADD COLUMN IF NOT EXISTS bukti_nama   TEXT;

-- Batas jenis & ukuran lampiran juga dijaga DI PENYIMPANAN, bukan hanya di
-- layanan: nilai di luar aturan (bukan gambar yang diizinkan, lebih dari 2 MB,
-- atau ukuran yang tidak cocok dengan isinya) tidak boleh bisa masuk lewat
-- jalur mana pun — termasuk skrip perbaikan manual atau klien lama.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'pembayaran_bukti_masuk_akal') THEN
        ALTER TABLE pembayaran ADD CONSTRAINT pembayaran_bukti_masuk_akal CHECK (
            (bukti_gambar IS NULL AND bukti_tipe IS NULL AND bukti_ukuran IS NULL)
            OR (bukti_gambar IS NOT NULL
                AND bukti_tipe IN ('image/jpeg', 'image/png', 'image/webp')
                AND bukti_ukuran = octet_length(bukti_gambar)
                AND bukti_ukuran BETWEEN 1 AND 2097152)
        );
    END IF;
END $$;
ALTER TABLE pelanggan  ADD COLUMN IF NOT EXISTS tenant_id TEXT;
ALTER TABLE langganan  ADD COLUMN IF NOT EXISTS tenant_id TEXT;
ALTER TABLE tagihan    ADD COLUMN IF NOT EXISTS tenant_id TEXT;
ALTER TABLE pembayaran ADD COLUMN IF NOT EXISTS tenant_id TEXT;
ALTER TABLE perangkat  ADD COLUMN IF NOT EXISTS tenant_id TEXT;

-- Instalasi lama: tenant setiap baris = desa pemiliknya.
UPDATE pelanggan  SET tenant_id = id            WHERE tenant_id IS NULL;
UPDATE langganan  SET tenant_id = pelanggan_id  WHERE tenant_id IS NULL;
UPDATE tagihan    SET tenant_id = pelanggan_id  WHERE tenant_id IS NULL;
UPDATE pembayaran SET tenant_id = pelanggan_id  WHERE tenant_id IS NULL;
UPDATE perangkat  SET tenant_id = pelanggan_id  WHERE tenant_id IS NULL;

ALTER TABLE pelanggan  ALTER COLUMN tenant_id SET NOT NULL;
ALTER TABLE langganan  ALTER COLUMN tenant_id SET NOT NULL;
ALTER TABLE tagihan    ALTER COLUMN tenant_id SET NOT NULL;
ALTER TABLE pembayaran ALTER COLUMN tenant_id SET NOT NULL;
ALTER TABLE perangkat  ALTER COLUMN tenant_id SET NOT NULL;

-- tenant_id tidak boleh menyimpang dari pemilik barisnya: tanpa jaminan ini
-- satu baris bisa mengaku milik desa lain dan lolos dari kebijakan RLS-nya.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'pelanggan_tenant_sama') THEN
        ALTER TABLE pelanggan ADD CONSTRAINT pelanggan_tenant_sama CHECK (tenant_id = id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'langganan_tenant_sama') THEN
        ALTER TABLE langganan ADD CONSTRAINT langganan_tenant_sama CHECK (tenant_id = pelanggan_id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'tagihan_tenant_sama') THEN
        ALTER TABLE tagihan ADD CONSTRAINT tagihan_tenant_sama CHECK (tenant_id = pelanggan_id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'pembayaran_tenant_sama') THEN
        ALTER TABLE pembayaran ADD CONSTRAINT pembayaran_tenant_sama CHECK (tenant_id = pelanggan_id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'perangkat_tenant_sama') THEN
        ALTER TABLE perangkat ADD CONSTRAINT perangkat_tenant_sama CHECK (tenant_id = pelanggan_id);
    END IF;
END $$;

-- ---------- Index ----------
-- Index tenant dipakai kebijakan RLS; index pelanggan_id dipertahankan
-- karena itu yang melayani ON DELETE CASCADE dari pelanggan.
CREATE INDEX IF NOT EXISTS idx_pembayaran_pelanggan ON pembayaran(pelanggan_id);
CREATE INDEX IF NOT EXISTS idx_tagihan_pelanggan      ON tagihan(pelanggan_id);
CREATE INDEX IF NOT EXISTS idx_langganan_berlaku      ON langganan(berlaku_sampai);
CREATE INDEX IF NOT EXISTS idx_tagihan_tenant         ON tagihan(tenant_id);
CREATE INDEX IF NOT EXISTS idx_pembayaran_tenant      ON pembayaran(tenant_id);
CREATE INDEX IF NOT EXISTS idx_perangkat_tenant       ON perangkat(tenant_id);

-- dir_cari_pelanggan membandingkan lower(kode_aktivasi). UNIQUE pada kolom
-- mentah hanya melayani pencarian persis, jadi tanpa index fungsional ini
-- setiap aktivasi memindai SELURUH tabel pelanggan.
CREATE INDEX IF NOT EXISTS idx_pelanggan_kode_lower   ON pelanggan (lower(kode_aktivasi));

-- Rekonsiliasi memilih tagihan yang belum dibayar dalam rentang waktu lalu
-- mengurutkannya berdasarkan dibuat. Index ini melayani filter sekaligus
-- ORDER BY; tanpa itu seluruh tabel tagihan dipindai tiap putaran.
CREATE INDEX IF NOT EXISTS idx_tagihan_dibuat         ON tagihan(dibuat);

-- =====================================================================
-- ROW LEVEL SECURITY
-- =====================================================================
-- Satu predicate untuk semua tabel. EXECUTE-nya sengaja TIDAK dicabut dari
-- PUBLIC: fungsinya hanya membaca variabel sesi yang harus disetel pemanggil
-- sendiri, jadi tidak memberi kekuatan lintas-tenant apa pun — sementara
-- kebijakan yang gagal dievaluasi justru menyulitkan diagnosa.
CREATE OR REPLACE FUNCTION tenant_aktif() RETURNS text
LANGUAGE sql STABLE AS $$
    SELECT nullif(current_setting('app.tenant_id', true), '')
$$;

ALTER TABLE pelanggan  ENABLE ROW LEVEL SECURITY;
ALTER TABLE langganan  ENABLE ROW LEVEL SECURITY;
ALTER TABLE tagihan    ENABLE ROW LEVEL SECURITY;
ALTER TABLE pembayaran ENABLE ROW LEVEL SECURITY;
ALTER TABLE perangkat  ENABLE ROW LEVEL SECURITY;

-- PENGUMUMAN WAJIB ikut di sini. Kebijakan p_pengumuman_* di bawah hanya
-- berarti bila RLS menyala: tanpa baris ini kebijakannya DIABAIKAN, dan karena
-- Supabase memberi role `anon` hak tabel lewat default privileges, kunci
-- publishable bisa menulis/menghapus pengumuman lewat PostgREST — bertentangan
-- dengan maksud "baca publik, tulis hanya role server".
ALTER TABLE pengumuman ENABLE ROW LEVEL SECURITY;
ALTER TABLE admin_action_audit ENABLE ROW LEVEL SECURITY;

-- Pengumuman punya kebijakan SENDIRI (bukan predicate tenant): kontennya
-- sengaja publik untuk semua reader — endpoint GET /pengumuman dibaca tanpa
-- kunci, sama seperti kode verifikasi surat di aplikasi desa.
--   • Baca: terbuka (USING (true), semua role).
--   • Tulis: TIDAK ada kebijakan untuk anon/authenticated — PostgREST dengan
--     kunci anon TIDAK bisa mengirim/mengubah/menghapus pengumuman. Role
--     sudes_server satu-satunya yang boleh menulis (untuk kapake endpoint
--     admin di masa depan); operator menulis lewat dashboard memakai role
--     pemilik tabel yang berjalan di luar RLS.
DROP POLICY IF EXISTS p_pengumuman_baca ON pengumuman;
CREATE POLICY p_pengumuman_baca ON pengumuman FOR SELECT
    USING (true);

DROP POLICY IF EXISTS p_pengumuman_tulis ON pengumuman;
CREATE POLICY p_pengumuman_tulis ON pengumuman FOR ALL TO sudes_server
    USING (true) WITH CHECK (true);

DROP POLICY IF EXISTS p_admin_audit_insert ON admin_action_audit;
CREATE POLICY p_admin_audit_insert ON admin_action_audit FOR INSERT TO sudes_server
    WITH CHECK (true);

-- FORCE ROW LEVEL SECURITY sengaja TIDAK dipakai: pemilik tabel harus tetap
-- bisa menjalankan dua fungsi pencarian di bawah (kode aktivasi → desa,
-- order_id → tagihan). Isolasi dijaga di sisi lain: server menolak start bila
-- role yang dipakainya adalah pemilik tabel atau punya BYPASSRLS.
-- tenant_aktif() dibungkus (SELECT …) supaya dievaluasi SEKALI per kueri
-- (initplan), bukan sekali per baris. Tanpa pembungkus, tabel 100 ribu baris
-- berarti 100 ribu pembacaan GUC — pola yang dianjurkan Supabase untuk
-- auth.uid() berlaku sama untuk fungsi STABLE milik sendiri.
DROP POLICY IF EXISTS p_tenant ON pelanggan;
CREATE POLICY p_tenant ON pelanggan FOR ALL
    USING (tenant_id = (SELECT tenant_aktif()))
    WITH CHECK (tenant_id = (SELECT tenant_aktif()));

DROP POLICY IF EXISTS p_tenant ON langganan;
CREATE POLICY p_tenant ON langganan FOR ALL
    USING (tenant_id = (SELECT tenant_aktif()))
    WITH CHECK (tenant_id = (SELECT tenant_aktif()));

DROP POLICY IF EXISTS p_tenant ON tagihan;
CREATE POLICY p_tenant ON tagihan FOR ALL
    USING (tenant_id = (SELECT tenant_aktif()))
    WITH CHECK (tenant_id = (SELECT tenant_aktif()));

DROP POLICY IF EXISTS p_tenant ON pembayaran;
CREATE POLICY p_tenant ON pembayaran FOR ALL
    USING (tenant_id = (SELECT tenant_aktif()))
    WITH CHECK (tenant_id = (SELECT tenant_aktif()));

DROP POLICY IF EXISTS p_tenant ON perangkat;
CREATE POLICY p_tenant ON perangkat FOR ALL
    USING (tenant_id = (SELECT tenant_aktif()))
    WITH CHECK (tenant_id = (SELECT tenant_aktif()));

-- =====================================================================
-- ROLE APLIKASI
-- =====================================================================
-- Server memakai role ini, BUKAN postgres. Kata sandinya disetel operator
-- (lihat CARA PAKAI di atas) — sengaja tidak ditulis di berkas ini.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'sudes_server') THEN
        CREATE ROLE sudes_server LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE;
    END IF;
END $$;

GRANT USAGE ON SCHEMA public TO sudes_server;
GRANT SELECT, INSERT, UPDATE, DELETE ON
    pelanggan, langganan, tagihan, pembayaran, perangkat, pengumuman TO sudes_server;
GRANT INSERT ON admin_action_audit TO sudes_server;
GRANT USAGE, SELECT ON SEQUENCE admin_action_audit_id_seq TO sudes_server;

-- Audit records are private and append-only even if platform-wide default
-- privileges for the public schema are broadened later.
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'anon') THEN
        EXECUTE 'REVOKE ALL ON admin_action_audit FROM anon';
        EXECUTE 'REVOKE ALL ON SEQUENCE admin_action_audit_id_seq FROM anon';
    END IF;
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'authenticated') THEN
        EXECUTE 'REVOKE ALL ON admin_action_audit FROM authenticated';
        EXECUTE 'REVOKE ALL ON SEQUENCE admin_action_audit_id_seq FROM authenticated';
    END IF;
END $$;

-- PostgREST memakai role anon/authenticated. RLS yang menyala sudah menolak
-- tulisan mereka, tetapi hak tulisnya tetap ada dan menjadi jebakan bila
-- kebijakan kelak dilonggarkan. Cabut sekarang; baca pengumuman tetap publik.
-- Role ini hanya ada di Supabase, jadi dipakai DO + dynamic SQL agar skrip
-- tetap bisa dijalankan pada Postgres biasa.
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'anon') THEN
        EXECUTE 'REVOKE INSERT, UPDATE, DELETE ON pengumuman FROM anon';
        EXECUTE 'GRANT SELECT ON pengumuman TO anon';
    END IF;
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'authenticated') THEN
        EXECUTE 'REVOKE INSERT, UPDATE, DELETE ON pengumuman FROM authenticated';
        EXECUTE 'GRANT SELECT ON pengumuman TO authenticated';
    END IF;
END $$;

-- =====================================================================
-- FUNGSI PENCARIAN LINTAS-TENANT (satu-satunya)
-- =====================================================================
-- Tiga hal yang memang harus dicari sebelum tenant diketahui:
--   1. kode aktivasi → desa (kode ITU kredensialnya, jadi tidak bisa
--      dibatasi per-tenant),
--   2. order_id → tagihan (dipanggil webhook SETELAH signature SHA512
--      Midtrans diverifikasi; order_id tidak bisa ditebak),
--   3. daftar tagihan belum dibayar (rekonsiliasi: server menanyakan status
--      langsung ke Midtrans untuk order yang webhook-nya tidak pernah masuk;
--      server perlu melihat SEMUA desa sekaligus, satu desa tidak relevan).
-- Semuanya SECURITY DEFINER supaya berjalan sebagai pemilik tabel, dan
-- hanya mengembalikan kolom yang diperlukan. EXECUTE-nya dicabut dari
-- PUBLIC lalu diberikan hanya ke sudes_server — jadi tidak ada jalan lain
-- untuk membaca baris desa lain.
-- Versi lama fungsi pencarian dibuang lebih dulu.
--
-- CREATE OR REPLACE hanya bisa mengganti ISI fungsi; begitu daftar kolom
-- RETURNS TABLE berubah sedikit saja, PostgreSQL menolak dengan
-- "42P13: cannot change return type of existing function" dan SELURUH berkas
-- skema gagal. Karena skema ini dijalankan server saat start (dan aman
-- diulang), kegagalan itu membuat server berhenti total: /paket tidak pernah
-- menjawab, aplikasi desa menampilkan "Server lisensi belum terjangkau", dan
-- tagihan/aktivasi tidak bisa dibuat — walau database dan kredensialnya sehat.
-- DROP + CREATE membuat perubahan bentuk hasil aman dijalankan berulang kali.
--
-- Dampak keamanannya nihil: EXECUTE-nya tetap dicabut dari PUBLIC dan hanya
-- diberikan ke sudes_server di bawah, setiap kali berkas ini dijalankan.
DROP FUNCTION IF EXISTS dir_cari_pelanggan(text);
DROP FUNCTION IF EXISTS dir_cari_tagihan(text);
DROP FUNCTION IF EXISTS dir_daftar_pelanggan();

CREATE OR REPLACE FUNCTION dir_cari_pelanggan(p_kode text)
RETURNS TABLE (
    id text, kode_aktivasi text, nama_desa text, kontak text, dibuat timestamptz,
    max_perangkat int, paket text, berlaku_sampai timestamptz,
    order_id_tagihan_terakhir text, total_terbayar int, trial_sudah boolean)
LANGUAGE sql SECURITY DEFINER SET search_path = public, pg_temp AS $$
    SELECT p.id, p.kode_aktivasi, p.nama_desa, p.kontak, p.dibuat, p.max_perangkat,
           l.paket, l.berlaku_sampai, l.order_id_tagihan_terakhir, l.total_terbayar,
           p.trial_sudah
      FROM pelanggan p
      LEFT JOIN langganan l ON l.pelanggan_id = p.id
     WHERE lower(p.kode_aktivasi) = lower(p_kode)
$$;

CREATE OR REPLACE FUNCTION dir_cari_tagihan(p_order text)
RETURNS TABLE (pelanggan_id text, paket text, nominal bigint, hari int)
LANGUAGE sql SECURITY DEFINER SET search_path = public, pg_temp AS $$
    SELECT t.pelanggan_id, t.paket, t.nominal, t.hari
      FROM tagihan t
     WHERE t.order_id = p_order
$$;

-- Daftar SEMUA desa + ringkasan langganan & jumlah perangkatnya. Dipakai
-- "pusat administer" (endpoint /admin/pelanggan) supaya operator bisa melihat
-- desa mana yang sedang mentok kuota perangkat, tanpa menebak kode aktivasi.
-- Lintas-tenant secara sengaja — karena itu EXECUTE-nya hanya untuk
-- sudes_server DAN endpoint pemanggilnya wajib bertoken admin.
CREATE OR REPLACE FUNCTION dir_daftar_pelanggan()
RETURNS TABLE (id text, kode_aktivasi text, nama_desa text, max_perangkat int,
               paket text, berlaku_sampai timestamptz, jumlah_perangkat bigint)
LANGUAGE sql SECURITY DEFINER SET search_path = public, pg_temp AS $$
    SELECT p.id, p.kode_aktivasi, p.nama_desa, p.max_perangkat,
           l.paket, l.berlaku_sampai,
           (SELECT count(*) FROM perangkat d WHERE d.pelanggan_id = p.id)
      FROM pelanggan p
      LEFT JOIN langganan l ON l.pelanggan_id = p.id
     ORDER BY p.nama_desa, p.kode_aktivasi
$$;

-- Versi lama (dua parameter) dibuang lebih dulu: CREATE OR REPLACE tidak bisa
-- mengubah daftar parameter, jadi tanpa DROP ini akan ada dua fungsi berbeda
-- dan panggilan tiga-parameter dari server gagal pada skema lama.
DROP FUNCTION IF EXISTS dir_tagihan_belum_dibayar(int, timestamptz);

CREATE OR REPLACE FUNCTION dir_tagihan_belum_dibayar(
    p_batas int,
    p_lebih_tua_dari timestamptz DEFAULT NULL,
    p_lebih_baru_dari timestamptz DEFAULT NULL)
RETURNS TABLE (order_id text, pelanggan_id text, paket text,
               nominal bigint, hari int, dibuat timestamptz)
LANGUAGE sql SECURITY DEFINER SET search_path = public, pg_temp AS $$
    SELECT t.order_id, t.pelanggan_id, t.paket, t.nominal, t.hari, t.dibuat
      FROM tagihan t
     WHERE NOT EXISTS (SELECT 1 FROM pembayaran p WHERE p.order_id = t.order_id)
       AND (p_lebih_tua_dari IS NULL OR t.dibuat <= p_lebih_tua_dari)
       AND (p_lebih_baru_dari IS NULL OR t.dibuat >= p_lebih_baru_dari)
     ORDER BY t.dibuat
     LIMIT GREATEST(p_batas, 0)
$$;

REVOKE ALL ON FUNCTION dir_cari_pelanggan(text) FROM PUBLIC;
REVOKE ALL ON FUNCTION dir_cari_tagihan(text) FROM PUBLIC;
REVOKE ALL ON FUNCTION dir_tagihan_belum_dibayar(int, timestamptz, timestamptz) FROM PUBLIC;
REVOKE ALL ON FUNCTION dir_daftar_pelanggan() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION dir_cari_pelanggan(text) TO sudes_server;
GRANT EXECUTE ON FUNCTION dir_cari_tagihan(text) TO sudes_server;
GRANT EXECUTE ON FUNCTION dir_tagihan_belum_dibayar(int, timestamptz, timestamptz) TO sudes_server;
GRANT EXECUTE ON FUNCTION dir_daftar_pelanggan() TO sudes_server;

-- Cabut hak EXECUTE dari role PLATFORM (anon, authenticated).
--
-- REVOKE ... FROM PUBLIC saja TIDAK cukup: Supabase memberi role
-- anon/authenticated hak EXECUTE default pada fungsi baru di schema public
-- sebagai grant LANGSUNG (bukan lewat PUBLIC), jadi hak itu tetap ada
-- setelah REVOKE dari PUBLIC. Akibatnya KUNCI PUBLISHABLE — yang ikut
-- terbawa di dalam aplikasi desa dan tidak pernah rahasia — bisa memanggil
-- fungsi-fungsi ini lewat PostgREST RPC:
--
--   POST /rest/v1/rpc/dir_daftar_pelanggan  -> daftar SEMUA desa +
--                                              kode aktivasi + masa langganan
--   POST /rest/v1/rpc/dir_cari_pelanggan    -> kontak, order id, status trial
--
-- RLS TIDAK menolong di sini: fungsi-fungsi itu SECURITY DEFINER, jadi
--Policy RLS tidak dievaluasi sama sekali saat dipanggil. Tabelnya sendiri
--tetap aman (RLS menutupnya), tapi bocornya lewat fungsi.
--
-- Role server (sudes_server) tetap memegang hak EXECUTE seperti di atas.
-- Role service_role dibiarkan: itu role sisi server (kunci service, tidak
-- pernah dibagikan ke klien).
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'anon') THEN
        EXECUTE 'REVOKE ALL ON FUNCTION dir_cari_pelanggan(text) FROM anon';
        EXECUTE 'REVOKE ALL ON FUNCTION dir_cari_tagihan(text) FROM anon';
        EXECUTE 'REVOKE ALL ON FUNCTION dir_tagihan_belum_dibayar(int, timestamptz, timestamptz) FROM anon';
        EXECUTE 'REVOKE ALL ON FUNCTION dir_daftar_pelanggan() FROM anon';
    END IF;
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'authenticated') THEN
        EXECUTE 'REVOKE ALL ON FUNCTION dir_cari_pelanggan(text) FROM authenticated';
        EXECUTE 'REVOKE ALL ON FUNCTION dir_cari_tagihan(text) FROM authenticated';
        EXECUTE 'REVOKE ALL ON FUNCTION dir_tagihan_belum_dibayar(int, timestamptz, timestamptz) FROM authenticated';
        EXECUTE 'REVOKE ALL ON FUNCTION dir_daftar_pelanggan() FROM authenticated';
    END IF;
END $$;

-- Tabel tenant juga dicabut dari role platform. RLS sudah menutupnya
-- (GUC `app.tenant_id` tidak bisa disetel lewat PostgREST), tapi hak
-- tulis yang tersisa jadi jebakan: begitu kebijakan RLS dilonggarkan
-- atau GUC bisa disetel, kunci publishable langsung bisa menyisipkan
-- atau mengubah baris desa mana pun. Server memakai role sudes_server
-- — hak tabel untuk role itu sudah diberikan di atas.
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'anon') THEN
        EXECUTE 'REVOKE ALL ON pelanggan, langganan, tagihan, pembayaran, perangkat FROM anon';
    END IF;
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'authenticated') THEN
        EXECUTE 'REVOKE ALL ON pelanggan, langganan, tagihan, pembayaran, perangkat FROM authenticated';
    END IF;
END $$;

-- =====================================================================
-- CATATAN KEAMANAN
-- =====================================================================
-- Sebelum RLS dinyalakan, tabel-tabel ini bisa dibaca lewat PostgREST dengan
-- publishable key (`anon`): Supabase memberi default privilege pada tabel
-- baru di schema public, dan tanpa RLS tidak ada yang menyaringnya.
-- Mulai sekarang predicate `tenant_id = (SELECT tenant_aktif())` menutup itu:
-- `anon` tidak punya cara menyetel `app.tenant_id`, jadi hasilnya selalu nol
-- baris. Pengumuman sengaja terbuka untuk dibaca, tetapi RLS-nya juga aktif
-- (sebelumnya terlewat) dan hak tulis `anon`/`authenticated` dicabut.
--
-- Yang menjaga isolasi, berurutan:
--   1. RLS aktif di keenam tabel (lima tabel tenant + pengumuman) dengan
--      predicate yang gagal-tertutup; pengumuman hanya mengizinkan baca publik.
--   2. Server memakai role `sudes_server` (bukan pemilik, tanpa BYPASSRLS)
--      dan MENOLAK START bila role koneksinya istimewa.
--   3. Hanya tiga fungsi di atas yang bisa membaca lintas-tenant: dua
--      berdasarkan kredensial (kode aktivasi / order_id tervalidasi), satu
--      (dir_tagihan_belum_dibayar) hanya mengembalikan kolom tagihan yang
--      belum lunas untuk rekonsiliasi — tanpa kontak, kode aktivasi, atau
--      detail desa — dan hanya bisa dipanggil role server. Hak EXECUTE-nya
--      dicabut dari PUBLIC DAN dari role platform (anon/authenticated),
--      karena kunci publishable tidak rahasia: tanpa cabutan itu, PostgREST
--      bisa memanggil fungsi SECURITY DEFINER ini dan membaca semua desa
--      tanpa filter RLS sama sekali.
--   4. CHECK `tenant_id = pemilik` mencegah baris mengaku milik desa lain.
