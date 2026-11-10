package dev.sumberjaya.sudes.android

import org.junit.Assert.assertEquals
import org.junit.Test

/** Uji pembentukan alamat lengkap penduduk dan komponen RT/RW. */
class AlamatSusunTest {

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

    @Test
    fun formatRtRw_duaKomponen() {
        assertEquals("RT 001/RW 002", AlamatSusun.formatRtRw("001", "002"))
    }

    @Test
    fun formatRtRw_membuangAwalanYangSudahDiketik() {
        assertEquals("RT 001/RW 002", AlamatSusun.formatRtRw("RT 001", "RW 002"))
    }

    @Test
    fun formatRtRw_hanyaRt() {
        assertEquals("RT 003", AlamatSusun.formatRtRw("003", ""))
    }

    @Test
    fun formatRtRw_kosong() {
        assertEquals("", AlamatSusun.formatRtRw("", ""))
    }
}
