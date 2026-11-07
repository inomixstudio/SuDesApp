-- =====================================================================
-- KODE VERIFIKASI 4 ANGKA UNTUK MEMASANG PERANGKAT
-- =====================================================================
-- Operator Android tidak lagi mengetik URL, kunci, surel, dan kata sandi.
-- Desktop menunjukkan kode 4 angka (atau QR yang memuatnya); Android
-- menukar kode itu dengan surel + kata sandi lewat fungsi di bawah.
--
-- Keamanan:
--   * Kode hanya berlaku SEPULUH menit dan hanya bisa dipakai SEKALI
--     (dipakai_pada diisi saat berhasil diambil).
--   * Tabel tidak bisa dibaca klien (RLS menyala, tanpa policy select);
--     satu-satunya jalur masuk adalah fungsi ambil_pasangan.
--   * Fungsi berjalan security definer supaya bisa menyetel penanda pakai.
-- =====================================================================
create table if not exists public.pasangan_sementara (
    kode            text primary key,
    tenant_id       text not null,
    email           text not null,
    kata_sandi      text not null,
    kedaluwarsa_pada timestamptz not null,
    dipakai_pada    timestamptz
);

create index if not exists ix_pasangan_sementara_kedaluwarsa
    on public.pasangan_sementara (kedaluwarsa_pada);

alter table public.pasangan_sementara enable row level security;

-- Tidak ada policy SELECT/INSERT untuk peran klien: tabel ini hanya boleh
-- disentuh desktop (pemilik tabel) dan fungsi security definer di bawah.
create or replace function public.ambil_pasangan(p_kode text)
returns table(tenant_id text, email text, kata_sandi text)
language plpgsql security definer set search_path = public AS $$
declare
    v record;
begin
    if p_kode is null or length(trim(p_kode)) <> 4 then
        raise exception 'Kode harus 4 angka.' using errcode = 'P0001';
    end if;

    select * into v
      from public.pasangan_sementara
     where kode = upper(trim(p_kode))
       and kedaluwarsa_pada > now()
       and dipakai_pada is null
       for update skip locked;

    if not found then
        raise exception 'Kode tidak dikenal atau sudah kedaluwarsa. Minta kode baru di aplikasi desktop.'
            using errcode = 'P0001';
    end if;

    update public.pasangan_sementara
       set dipakai_pada = now()
     where kode = v.kode;

    return query select v.tenant_id, v.email, v.kata_sandi;
end $$;

revoke all on function public.ambil_pasangan(text) from public;
grant execute on function public.ambil_pasangan(text) to anon, authenticated;

-- Bersihkan kode yang sudah lama kedaluwarsa (dipanggil desktop saat pairing).
create or replace function public.bersihkan_pasangan_kedaluwarsa()
returns integer
language plpgsql security definer set search_path = public AS $$
declare
    n integer;
begin
    delete from public.pasangan_sementara where kedaluwarsa_pada <= now();
    get diagnostics n = row_count;
    return n;
end $$;

revoke all on function public.bersihkan_pasangan_kedaluwarsa() from public;
grant execute on function public.bersihkan_pasangan_kedaluwarsa() to anon, authenticated;
