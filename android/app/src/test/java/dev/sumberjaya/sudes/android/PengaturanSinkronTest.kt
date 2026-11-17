package dev.sumberjaya.sudes.android

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * Aturan penolak sinkron berdasarkan versi.
 *
 * Sinkron hanya boleh jalan antara Android dan desktop yang versinya sama;
 * versi desktop yang belum diketahui (perangkat baru dipasang, desktop belum
 * menerbitkan kop) tidak boleh memblokir, karena belum ada dasar membandingkan.
 */
class PengaturanSinkronTest {

    @Test
    fun versi_cocok_bila_sama() {
        assertTrue(PengaturanSinkron.versiCocok("2.15.0", "2.15.0"))
    }

    @Test
    fun versi_cocok_bila_desktop_belum_diketahui() {
        assertTrue(PengaturanSinkron.versiCocok("", "2.15.0"))
    }

    @Test
    fun versi_tidak_cocok_bila_berbeda() {
        assertFalse(PengaturanSinkron.versiCocok("2.16.0", "2.15.0"))
    }
}
