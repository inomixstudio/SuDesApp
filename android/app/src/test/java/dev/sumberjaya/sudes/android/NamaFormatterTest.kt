package dev.sumberjaya.sudes.android

import org.junit.Assert.assertEquals
import org.junit.Test

/**
 * NamaFormatter Android harus persis perilaku desktop
 * (SuDesApp.Utilities.NamaFormatter): nama pokok kapital, gelar tetap.
 */
class NamaFormatterTest {

    @Test
    fun gelar_depan_dan_belakang_tetap_apa_adanya() {
        assertEquals(
            "Dr. H. AHMAD SURYANA, S.H., M.M.",
            NamaFormatter.toUpperNama("Dr. H. Ahmad Suryana, S.H., M.M."),
        )
        assertEquals("SITI AMINAH, S.Pd.", NamaFormatter.toUpperNama("Siti Aminah, S.Pd."))
    }

    @Test
    fun nama_tanpa_gelar_kapital_seluruhnya() {
        assertEquals("MUHAMMAD RIZKY RAMADHAN", NamaFormatter.toUpperNama("Muhammad Rizky Ramadhan"))
    }

    @Test
    fun kapital_tiap_kata_tanpa_merusak_gelar_bertitik() {
        assertEquals("Ahmad Suryana", NamaFormatter.kapitalTiapKata("ahmad suryana"))
        assertEquals("Siti Aminah S.H.", NamaFormatter.kapitalTiapKata("siti aminah S.H."))
    }

    @Test
    fun nama_kosong_aman() {
        assertEquals("", NamaFormatter.toUpperNama(""))
        assertEquals("", NamaFormatter.toUpperNama(null))
        assertEquals("...............................", NamaFormatter.namaAtauTitik(""))
    }
}
