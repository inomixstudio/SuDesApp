-- =====================================================================
-- BATAS PERCOBAAN KODE PASANGAN PERANGKAT
-- =====================================================================
-- Kode pasangan hanya empat angka, jadi hanya ada 10.000 kemungkinan.
-- Fungsi public.ambil_pasangan dapat dipanggil siapa pun yang memegang kunci
-- anon — dan kunci itu ikut terpasang di dalam APK. Tanpa batas percobaan,
-- kode yang berlaku sepuluh menit bisa ditebak berulang sampai dapat; tebakan
-- yang benar mengembalikan surel dan kata sandi perangkat, sehingga penebak
-- bisa membaca antrean surat berisi NIK warga.
--
-- Batas di sini: 10 percobaan gagal per 15 menit per alamat IP klien.
-- Penghitung disimpan di tabel tersendiri dengan RLS menyala tanpa policy,
-- sehingga klien tidak bisa membacanya (dan tidak bisa mengosongkannya).
--
-- Catatan: alamat IP dibaca dari header yang diteruskan PostgREST
-- (x-forwarded-for / x-real-ip). Bila header tidak ada (mis. dari SQL editor),
-- semua pemanggil berbagi satu kunci "tanpa-ip" — tetap dibatasi.
-- =====================================================================

create table if not exists public.pasangan_percobaan (
    kunci         text primary key,
    jendela_mulai timestamptz not null default now(),
    jumlah        integer not null default 0
);

alter table public.pasangan_percobaan enable row level security;
revoke all on public.pasangan_percobaan from anon, authenticated;

-- Kunci percobaan: satu alamat IP klien, dipotong supaya tetap pendek.
create or replace function public.kunci_percobaan_pasangan() returns text
language plpgsql stable as $$
declare
    v_headers jsonb;
    v_ip text;
begin
    begin
        v_headers := coalesce(current_setting('request.headers', true), '{}')::jsonb;
    exception when others then
        v_headers := '{}'::jsonb;
    end;

    v_ip := coalesce(
        nullif(trim(split_part(coalesce(v_headers ->> 'x-forwarded-for', ''), ',', 1)), ''),
        nullif(trim(coalesce(v_headers ->> 'x-real-ip', '')), ''),
        'tanpa-ip');

    return left(v_ip, 64);
end $$;

revoke all on function public.kunci_percobaan_pasangan() from public;

-- Tukar kode 4 angka dengan surel + kata sandi perangkat, dengan batas
-- percobaan. Perilaku lain sama dengan versi sebelumnya: kode berlaku 10
-- menit, sekali pakai, dan hanya bisa diambil lewat security definer.
create or replace function public.ambil_pasangan(p_kode text)
returns table(tenant_id text, email text, kata_sandi text)
language plpgsql security definer set search_path = public AS $$
declare
    v record;
    v_kunci text := public.kunci_percobaan_pasangan();
    v_jumlah integer;
begin
    if p_kode is null or trim(p_kode) !~ '^[0-9]{4}$' then
        raise exception 'Kode harus 4 angka.' using errcode = 'P0001';
    end if;

    -- Mulai/setel jendela 15 menit untuk IP ini.
    insert into public.pasangan_percobaan as p (kunci, jendela_mulai, jumlah)
    values (v_kunci, now(), 0)
    on conflict (kunci) do update
       set jumlah = case
                        when p.jendela_mulai < now() - interval '15 minutes' then 0
                        else p.jumlah
                    end,
           jendela_mulai = case
                               when p.jendela_mulai < now() - interval '15 minutes' then now()
                               else p.jendela_mulai
                           end;

    select p.jumlah into v_jumlah
      from public.pasangan_percobaan p
     where p.kunci = v_kunci;

    if v_jumlah >= 10 then
        raise exception 'Terlalu banyak percobaan kode. Tunggu 15 menit, lalu minta kode baru di aplikasi desktop.'
            using errcode = 'P0001';
    end if;

    select * into v
      from public.pasangan_sementara
     where kode = upper(trim(p_kode))
       and kedaluwarsa_pada > now()
       and dipakai_pada is null
       for update skip locked;

    if not found then
        update public.pasangan_percobaan
           set jumlah = jumlah + 1
         where kunci = v_kunci;
        raise exception 'Kode tidak dikenal atau sudah kedaluwarsa. Minta kode baru di aplikasi desktop.'
            using errcode = 'P0001';
    end if;

    update public.pasangan_sementara
       set dipakai_pada = now()
     where kode = v.kode;

    -- Tebakan benar: hitungan dihapus supaya pemasangan berikutnya dari
    -- jaringan kantor yang sama tidak langsung terkena batas.
    delete from public.pasangan_percobaan where kunci = v_kunci;

    return query select v.tenant_id, v.email, v.kata_sandi;
end $$;

revoke all on function public.ambil_pasangan(text) from public;
grant execute on function public.ambil_pasangan(text) to anon, authenticated;

-- Pembersihan kode kedaluwarsa juga membersihkan penghitung lama, supaya
-- tabel percobaan tidak tumbuh selamanya.
create or replace function public.bersihkan_pasangan_kedaluwarsa()
returns integer
language plpgsql security definer set search_path = public AS $$
declare
    n integer;
begin
    delete from public.pasangan_sementara where kedaluwarsa_pada <= now();
    get diagnostics n = row_count;

    delete from public.pasangan_percobaan
     where jendela_mulai < now() - interval '1 day';

    return n;
end $$;

revoke all on function public.bersihkan_pasangan_kedaluwarsa() from public;
grant execute on function public.bersihkan_pasangan_kedaluwarsa() to anon, authenticated;
