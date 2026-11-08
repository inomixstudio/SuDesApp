-- Keamanan: pasang/cabut perangkat tidak boleh dipanggil pemegang anon key.
-- Fungsi ini security definer; tanpa REVOKE, siapa pun ialah bisa membuat/
-- menghapus perangkat tenant mana pun via PostgREST.
-- Desktop memanggil lewat koneksi postgres (bypass RLS), tidak butuh grant.

REVOKE ALL ON FUNCTION public.pasang_perangkat_android(
    text, text, text, text
) FROM PUBLIC, anon, authenticated;

REVOKE ALL ON FUNCTION public.cabut_perangkat_android(uuid)
FROM PUBLIC, anon, authenticated;
