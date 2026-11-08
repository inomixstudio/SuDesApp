-- =====================================================================
-- SATU PERANGKAT PER DESA + STATUS ONLINE
-- =====================================================================
-- Desktop hanya mengizinkan satu perangkat Android aktif saat administrator
-- masuk. Perangkat lama dinonaktifkan (bukan dihapus) supaya antreannya masih
-- bisa ditarik desktop sebelum benar-benar ditinggalkan.
create or replace function public.nonaktifkan_perangkat_lain(
    p_tenant_id text,
    p_kecuali    uuid
) returns integer
language plpgsql security definer set search_path = public AS $$
declare
    v_jumlah integer;
begin
    update public.perangkat_android
       set aktif = false
     where tenant_id = p_tenant_id
       and id <> p_kecuali
       and aktif;

    get diagnostics v_jumlah = row_count;
    return v_jumlah;
end $$;

revoke all on function public.nonaktifkan_perangkat_lain(text, uuid)
    from public, anon, authenticated;

-- =====================================================================
-- LAPORAN ONLINE DARI PERANGKAT
-- =====================================================================
-- Desktop membaca terakhir_online untuk menampilkan "Android terhubung" di
-- status bar. Perangkat hanya boleh memperbarui barisnya sendiri: id dan
-- tenant diambil dari klaim JWT, bukan dari isi permintaan.
create or replace function public.tandai_perangkat_online()
returns void
language sql security definer set search_path = public, auth AS $$
    update public.perangkat_android
       set terakhir_online = now()
     where id::text = coalesce(auth.jwt() -> 'app_metadata' ->> 'id_perangkat', '')
       and tenant_id = coalesce(auth.jwt() -> 'app_metadata' ->> 'tenant_id', '')
       and aktif;
$$;

revoke all on function public.tandai_perangkat_online() from public, anon;
grant execute on function public.tandai_perangkat_online() to authenticated;

-- =====================================================================
-- PERANGKAT NONAKTIF TIDAK BOLEH MENULIS ANTREAN BARU
-- =====================================================================
create or replace function public.perangkat_saya_aktif()
returns boolean
language sql stable security definer set search_path = public, auth AS $$
    select coalesce((
        select p.aktif
          from public.perangkat_android p
         where p.id::text = coalesce(auth.jwt() -> 'app_metadata' ->> 'id_perangkat', '')
           and p.tenant_id = coalesce(auth.jwt() -> 'app_metadata' ->> 'tenant_id', '')
    ), false);
$$;

revoke all on function public.perangkat_saya_aktif() from public, anon;
grant execute on function public.perangkat_saya_aktif() to authenticated;

-- Policy RESTRICTIVE bergabung dengan AND terhadap policy permissive yang
-- sudah ada: perangkat yang dinonaktifkan tetap bisa membaca, tetapi tidak
-- bisa menambah surat ke antrean.
drop policy if exists perangkat_aktif_menulis on public.antrean_surat;
create policy perangkat_aktif_menulis on public.antrean_surat
    as restrictive for insert to authenticated
    with check (public.perangkat_saya_aktif());
