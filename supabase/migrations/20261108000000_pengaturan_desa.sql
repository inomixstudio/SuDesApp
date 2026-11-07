-- =====================================================================
-- PENGATURAN DESA (KOP + PENOMORAN) UNTUK ANDROID
-- =====================================================================
-- Android tidak menyimpan sendiri identitas kop surat: nilainya diambil dari
-- desktop dan disinkronkan otomatis. Tanpa ini, operator desa harus mengetik
-- ulang nama desa, alamat, dan pola nomor di dua tempat — dan kedua salinan
-- akan menyimpang tanpa jejak.
--
-- Desktop = otoritas: menulis baris ini setiap kali pengaturan berubah.
-- Android = pembaca: menarik baris ini saat dipasang dan saat dibuka, lalu
--           menyimpannya sebagai salinan lokal untuk dipakai saat offline.
--
-- Bentuk kolom:
--   kop   : namaDesa, kecamatan, kabupaten, jenisKabupaten, jenisLembaga,
--           alamat, kodepos, email, kepalaDesa, sekretarisDesa,
--           nipKepalaDesa, ukuranKertas, logo (base64, opsional)
--   nomor : { "format": { "<ID_JENIS>": "<pola>" }, "urutTerakhir": <int> }
--
-- Pola memakai token .NET yang sama dengan desktop: {0:D3} = urutan,
-- {1} = kode jenis, {2:yyyy} = tahun.
-- =====================================================================
create table if not exists public.pengaturan_desa (
    tenant_id       text primary key,
    kop             jsonb not null,
    nomor           jsonb not null,
    versi           integer not null default 1,
    diperbarui_pada timestamptz not null default now()
);

alter table public.pengaturan_desa enable row level security;

-- Android membaca pengaturan desanya; tidak ada jalur tulis dari klien
-- (desktop menulis sebagai pemilik tabel, RLS tidak membatasinya).
drop policy if exists p_pengaturan_desa_baca on public.pengaturan_desa;
create policy p_pengaturan_desa_baca on public.pengaturan_desa
    for select to authenticated
    using (tenant_id = (select public.tenant_dari_jwt()));

grant select on public.pengaturan_desa to authenticated;
