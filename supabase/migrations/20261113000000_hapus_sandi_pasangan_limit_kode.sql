-- Keamanan: hapus sesi pasangan terpakai segera setelah ambil_pasangan
-- mengembalikan kredensial. Kata sandi perangkat (plaintext di pasangan_sementara)
-- tidak lagi bergantayangan 10 menit sampai kedaluwarsa.
-- Juga: batasi percobaan per KODE (bukan hanya per IP dari header klien)
-- supaya tebak 10.000 kode tidak lolos lewat X-Forwarded-For palsu.

-- 1. Hapus kata_sandi dari pasangan_sementara setelah dipakai.
-- Relaksasi NOT NULL supaya NULL dapat diterima setelah dipakai:
ALTER TABLE public.pasangan_sementara ALTER COLUMN kata_sandi DROP NOT NULL;

CREATE OR REPLACE FUNCTION public.ambil_pasangan(p_kode text)
returns table(tenant_id text, email text, kata_sandi text)
language plpgsql security definer set search_path = public AS $$
declare
    v record;
    v_kunci_ip text := public.kunci_percobaan_pasangan();
    v_kunci_kode text := 'kode:' || upper(trim(coalesce(p_kode, '')));
    v_jumlah integer;
begin
    if p_kode is null or trim(p_kode) !~ '^[0-9]{4}$' then
        raise exception 'Kode harus 4 angka.' using errcode = 'P0001';
    end if;

    -- Batas GABUNGAN: per IP DAN per kode. Kunci efektif = kunci IP untuk pesan umum;
    -- baris per-kode dicek terpisah di bawah.
    insert into public.pasangan_percobaan as p (kunci, jendela_mulai, jumlah)
    values (v_kunci_ip, now(), 0)
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
     where p.kunci = v_kunci_ip;

    if v_jumlah >= 10 then
        raise exception 'Terlalu banyak percobaan kode. Tunggu 15 menit, lalu minta kode baru di aplikasi desktop.'
            using errcode = 'P0001';
    end if;

    -- Batas per KODE: 10 tebakan gagal per jendela untuk kode yang sama,
    -- terlepas dari IP pengirim (header XFF bisa dipalsu).
    insert into public.pasangan_percobaan as p (kunci, jendela_mulai, jumlah)
    values (v_kunci_kode, now(), 0)
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
     where p.kunci = v_kunci_kode;

    if v_jumlah >= 10 then
        raise exception 'Kode ini terlalu banyak ditebak. Tunggu 15 menit, lalu minta kode baru di aplikasi desktop.'
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
         where kunci in (v_kunci_ip, v_kunci_kode);
        raise exception 'Kode tidak dikenal atau sudah kedaluwarsa. Minta kode baru di aplikasi desktop.'
            using errcode = 'P0001';
    end if;

    update public.pasangan_sementara
       set dipakai_pada = now(), kata_sandi = NULL
     where kode = v.kode;

    -- Tebakan benar: hapus hitungan IP supaya pemasangan berikutnya dari
    -- jaringan kantor yang sama tidak langsung terkena batas.
    delete from public.pasangan_percobaan where kunci in (v_kunci_ip, v_kunci_kode);

    -- Kembalikan kredensial DULU; kata_sandi sudah di-NULL-kan di atas,
    -- sehingga salinan plaintext tidak bertahan di DB.
    return query select v.tenant_id, v.email, v.kata_sandi;
end $$;

revoke all on function public.ambil_pasangan(text) from public;
grant execute on function public.ambil_pasangan(text) to anon, authenticated;
