-- =====================================================================
-- HAPUS BARIS ANTREAN SENDIRI (dialek Supabase)
-- =====================================================================
-- Android butuh menghapus satu baris antrean miliknya sendiri dari halaman
-- Pengaturan (mis. isian salah ketik). Policy ini menyertakan tenant +
-- id_perangkat JWT yang sama seperti policy baca/tulis.
-- Idempoten: dijalankan ulang aman.
-- =====================================================================
drop policy if exists p_antrean_surat_hapus on public.antrean_surat;
create policy p_antrean_surat_hapus on public.antrean_surat
    for delete to authenticated
    using (
        tenant_id = (select public.tenant_dari_jwt())
        and id_perangkat::text = (select public.perangkat_dari_jwt())
    );

grant delete on public.antrean_surat to authenticated;
