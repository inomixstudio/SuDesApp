-- =====================================================================
-- SKEMA SINKRONISASI ANDROID -> DESKTOP
-- =====================================================================
-- Surat yang dibuat aplikasi Android disimpan di sini sambil menunggu
-- aplikasi desktop mengunduh dan mengimpornya ke database lokal.
--
-- Model:
--   * Android = klien tipis: menyisipkan baris ke antrean_surat, membaca
--               antreannya sendiri, membaca skema formulir desanya.
--   * Desktop = otoritas: membaca antrean, mengimpor ke SQLite lewat
--               SuratRepository.AddSuratAsync, menghapus baris yang sudah
--               sinkron.
--   * tenant_id = id desa (sama dengan pelanggan.id pada skema lisensi).
--
-- BERKAS INI DITERAPKAN lewat `supabase db push`. Untuk uji lokal berkas ini
-- juga bisa dijalankan langsung; semua pernyataannya idempoten.
--
-- Keamanan:
--   * RLS menyala di semua tabel. Klien Android memakai JWT Supabase Auth
--     dengan klaim app_metadata: tenant_id, id_pengguna, id_perangkat.
--   * Desktop masuk sebagai role postgres ( pemilik tabel), jadi RLS tidak
--     membatasinya.
--   * Batas 10 surat ditegakkan trigger (security definer) supaya tidak bisa
--     diloloskan klien.

-- =====================================================================
-- PERANGKAT ANDROID YANG DIPASANGKAN
-- =====================================================================
create table if not exists public.perangkat_android (
    id               uuid primary key default gen_random_uuid(),
    tenant_id        text not null,
    supabase_user_id uuid,
    id_pengguna      text not null,
    nama             text not null,
    aktif            boolean not null default true,
    dipasang_pada    timestamptz not null default now(),
    terakhir_online  timestamptz,
    catatan          text
);

create index if not exists ix_perangkat_android_tenant
    on public.perangkat_android (tenant_id);
create index if not exists ix_perangkat_android_pengguna
    on public.perangkat_android (tenant_id, id_pengguna);
create index if not exists ix_perangkat_android_user
    on public.perangkat_android (supabase_user_id);

-- =====================================================================
-- ANTREAN SURAT
-- =====================================================================
-- data_json memuat muatan surat: {"tanggal": "...", "fields": {...}}.
-- Kolom id_jenis, nomor_sementara, dan nik dipisah supaya bisa diindeks.
create table if not exists public.antrean_surat (
    id               uuid primary key default gen_random_uuid(),
    tenant_id        text not null,
    id_pengguna      text not null,
    id_perangkat     uuid,
    id_jenis         text not null,
    nomor_sementara  text,
    nik              text,
    data_json        jsonb not null,
    status           text not null default 'menunggu',
    batas_coba       smallint not null default 0,
    catatan          text,
    dibuat_pada      timestamptz not null default now(),
    diubah_pada      timestamptz not null default now()
);

create index if not exists ix_antrean_surat_antrean
    on public.antrean_surat (tenant_id, status, dibuat_pada);
create index if not exists ix_antrean_surat_perangkat
    on public.antrean_surat (id_perangkat);
create index if not exists ix_antrean_surat_nik
    on public.antrean_surat (tenant_id, nik);

-- =====================================================================
-- LOG SINKRONISASI
-- =====================================================================
create table if not exists public.sinkron_log (
    id           bigint generated always as identity primary key,
    tenant_id    text not null,
    id_antrean   uuid not null,
    nomor_resmi  text,
    status       text not null,
    catatan      text,
    diimpor_pada timestamptz not null default now()
);

create index if not exists ix_sinkron_log_tenant
    on public.sinkron_log (tenant_id, diimpor_pada desc);
create index if not exists ix_sinkron_log_antrean
    on public.sinkron_log (id_antrean);

-- =====================================================================
-- SKEMA FORMULIR
-- =====================================================================
-- Definisi bidang formulir per jenis surat, diterbitkan desktop untuk
-- Android. Bentuknya dibaca aplikasi Android saat membangun formulir.
create table if not exists public.skema_formulir (
    tenant_id       text not null,
    id_jenis        text not null,
    versi           integer not null default 1,
    skema           jsonb not null,
    diperbarui_pada timestamptz not null default now(),
    primary key (tenant_id, id_jenis)
);

-- =====================================================================
-- BANTUAN KLaim JWT
-- =====================================================================
create or replace function public.tenant_dari_jwt() returns text
language sql stable as $$
    select nullif(auth.jwt() -> 'app_metadata' ->> 'tenant_id', '')
$$;

create or replace function public.perangkat_dari_jwt() returns text
language sql stable as $$
    select nullif(auth.jwt() -> 'app_metadata' ->> 'id_perangkat', '')
$$;

-- =====================================================================
-- BATAS 10 SURAT DALAM ANTREAN
-- =====================================================================
create or replace function public.batasi_antrean_surat() returns trigger
language plpgsql security definer set search_path = public, extensions AS $$
declare
    jumlah integer;
begin
    select count(*) into jumlah
      from public.antrean_surat
     where tenant_id = new.tenant_id;

    if jumlah >= 10 then
        raise exception
            'Antrean sinkronisasi penuh (10 surat). Sinkronkan aplikasi desktop dulu.'
            using errcode = 'P0001';
    end if;

    return new;
end $$;

drop trigger if exists trg_batasi_antrean_surat on public.antrean_surat;
create trigger trg_batasi_antrean_surat
    before insert on public.antrean_surat
    for each row execute function public.batasi_antrean_surat();

-- =====================================================================
-- RLS
-- =====================================================================
alter table public.perangkat_android enable row level security;
alter table public.antrean_surat enable row level security;
alter table public.sinkron_log enable row level security;
alter table public.skema_formulir enable row level security;

-- Perangkat: Android hanya membaca perangkatnya sendiri.
drop policy if exists p_perangkat_android_baca on public.perangkat_android;
create policy p_perangkat_android_baca on public.perangkat_android
    for select to authenticated
    using (
        tenant_id = (select public.tenant_dari_jwt())
        and id::text = (select public.perangkat_dari_jwt())
    );

-- Antrean: Android menyisipkan dan membaca antreannya sendiri.
drop policy if exists p_antrean_surat_tulis on public.antrean_surat;
create policy p_antrean_surat_tulis on public.antrean_surat
    for insert to authenticated
    with check (
        tenant_id = (select public.tenant_dari_jwt())
        and id_perangkat::text = (select public.perangkat_dari_jwt())
    );

drop policy if exists p_antrean_surat_baca on public.antrean_surat;
create policy p_antrean_surat_baca on public.antrean_surat
    for select to authenticated
    using (
        tenant_id = (select public.tenant_dari_jwt())
        and id_perangkat::text = (select public.perangkat_dari_jwt())
    );

-- Skema formulir: Android membaca skema desanya.
drop policy if exists p_skema_formulir_baca on public.skema_formulir;
create policy p_skema_formulir_baca on public.skema_formulir
    for select to authenticated
    using (tenant_id = (select public.tenant_dari_jwt()));

-- =====================================================================
-- HAK AKSES ROLE KLIEN
-- =====================================================================
grant usage on schema public to authenticated;
grant select on public.perangkat_android, public.antrean_surat, public.skema_formulir to authenticated;
grant insert on public.antrean_surat to authenticated;

-- =====================================================================
-- PASANG PERANGKAT (dipanggil desktop saat pairing)
-- =====================================================================
-- Membuat akun Supabase Auth untuk perangkat, memasang klaim tenant, lalu
-- mencatat perangkatnya. Mengembalikan supabase_user_id.
create or replace function public.pasang_perangkat_android(
    p_tenant_id   text,
    p_id_pengguna text,
    p_nama        text,
    p_kata_sandi  text
) returns uuid
language plpgsql security definer set search_path = public, extensions AS $$
declare
    v_perangkat_id uuid := gen_random_uuid();
    v_user_id      uuid := gen_random_uuid();
    v_email        text := 'android-' || replace(v_user_id::text, '-', '') || '@perangkat.local';
begin
    insert into auth.users (
        id, instance_id, aud, role, email, encrypted_password,
        email_confirmed_at, raw_app_meta_data, raw_user_meta_data,
        created_at, updated_at, is_sso_user, is_anonymous,
        email_change,
        confirmation_token, recovery_token, email_change_token_new,
        email_change_token_current, reauthentication_token, phone_change_token
    ) values (
        v_user_id,
        '00000000-0000-0000-0000-000000000000',
        'authenticated',
        'authenticated',
        v_email,
        extensions.crypt(p_kata_sandi, extensions.gen_salt('bf', 10)),
        now(),
        jsonb_build_object(
            'provider', 'email',
            'providers', jsonb_build_array('email'),
            'tenant_id', p_tenant_id,
            'id_pengguna', p_id_pengguna,
            'id_perangkat', v_perangkat_id::text,
            'peran', 'android'
        ),
        '{}'::jsonb,
        now(),
        now(),
        false,
        false,
        '',
        '', '', '', '', '', ''
    );

    insert into auth.identities (
        id, user_id, provider_id, identity_data, provider,
        last_sign_in_at, created_at, updated_at
    ) values (
        v_user_id,
        v_user_id,
        v_user_id::text,
        jsonb_build_object('sub', v_user_id::text, 'email', v_email),
        'email',
        now(),
        now(),
        now()
    );

    insert into public.perangkat_android (id, tenant_id, supabase_user_id, id_pengguna, nama)
    values (v_perangkat_id, p_tenant_id, v_user_id, p_id_pengguna, p_nama);

    return v_perangkat_id;
end $$;

-- =====================================================================
-- HAPUS PERANGKAT (dipanggil desktop saat perangkat dicabut)
-- =====================================================================
create or replace function public.cabut_perangkat_android(p_id uuid)
returns void
language plpgsql security definer set search_path = public, extensions AS $$
declare
    v_user_id uuid;
begin
    select supabase_user_id into v_user_id
      from public.perangkat_android where id = p_id;

    delete from public.antrean_surat where id_perangkat = p_id;
    delete from public.perangkat_android where id = p_id;

    if v_user_id is not null then
        delete from auth.identities where user_id = v_user_id;
        delete from auth.users where id = v_user_id;
    end if;
end $$;

-- =====================================================================
-- LAYANAN REST API (PostgREST) UNTUK DESKTOP
-- =====================================================================
-- Desktop membaca antrean lewat role postgres (bypass RLS), jadi tidak
-- memerlukan kebijakan khusus.
