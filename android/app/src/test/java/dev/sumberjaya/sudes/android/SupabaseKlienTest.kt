package dev.sumberjaya.sudes.android

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** Uji logika murni Android: tanpa Activity, tanpa jaringan. */
class SupabaseKlienTest {

    @Test
    fun alamatSusun_menggabungkanKomponen() {
        assertEquals(
            "Dusun A RT 1/RW 2 Desa B\nKecamatan C Kabupaten D",
            AlamatSusun.susun(
                dusunJalan = "A",
                desa = "B",
                kecamatan = "C",
                kabupaten = "D",
                rt = "1",
                rw = "2",
            ),
        )
    }

    @Test
    fun alamatSusun_kosong_mengembalikanFallback() {
        assertEquals("[Alamat Warga]", AlamatSusun.susun("", "", "", ""))
    }
}
