package dev.sumberjaya.sudes.android

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * Pengelompokan bidang menjadi kartu-kartu form (cermin section desktop).
 *
 * Fungsi ini satu-satunya penentu tampilan: kalau ia salah urutan atau
 * menjatuhkan bidang, operator kehilangan kolom isian — jadi aturannya diuji
 * tanpa perlu menjalankan Android.
 */
class SkemaGrupTest {

    private fun bidang(kunci: String, grup: String = "", otomatis: Boolean = false) =
        SkemaBidang(
            key = kunci, label = kunci, type = "text", target = "warga.$kunci",
            required = false, options = emptyList(), otomatis = otomatis,
            children = emptyList(), grup = grup,
        )

    private fun jenis(bidang: List<SkemaBidang>, urutan: List<String> = emptyList()) =
        SkemaJenis(idJenis = "UJI", label = "Uji", fields = bidang, grupUrutan = urutan)

    @Test
    fun grupkan_memakaiUrutanSkemaBukanUrutanField() {
        val j = jenis(
            listOf(bidang("nik", "DATA PELAPOR"), bidang("nama", "DATA PEMOHON")),
            listOf("DATA PEMOHON", "DATA PELAPOR"),
        )
        val hasil = j.grupkan(j.fieldsInput)
        assertEquals(listOf("DATA PEMOHON", "DATA PELAPOR"), hasil.map { it.first })
        assertEquals(listOf("nama"), hasil[0].second.map { it.key })
        assertEquals(listOf("nik"), hasil[1].second.map { it.key })
    }

    @Test
    fun grupkan_bidangTanpaGrupTetakDitampilkanDiKelompokTerakhir() {
        val j = jenis(
            listOf(bidang("nik", "DATA PEMOHON"), bidang("aneh", "GRUP BARU"), bidang("liar")),
            listOf("DATA PEMOHON"),
        )
        val hasil = j.grupkan(j.fieldsInput)
        assertEquals(3, hasil.size)
        assertEquals("DATA PEMOHON", hasil[0].first)
        // Grup asing dan bidang tanpa grup tidak boleh hilang dari layar.
        assertTrue(hasil.flatMap { it.second }.map { it.key }.containsAll(listOf("nik", "aneh", "liar")))
    }

    @Test
    fun grupkan_skemaLamaTetapDatar() {
        val j = jenis(listOf(bidang("nik"), bidang("nama")))
        val hasil = j.grupkan(j.fieldsInput)
        assertEquals(1, hasil.size)
        assertEquals("", hasil[0].first)
        assertEquals(listOf("nik", "nama"), hasil[0].second.map { it.key })
    }

    @Test
    fun grupkan_mengelompokkanBidangSeragamDalamSatuKartu() {
        val j = jenis(
            listOf(bidang("a", "X"), bidang("b", "Y"), bidang("c", "X")),
            listOf("X", "Y"),
        )
        val hasil = j.grupkan(j.fieldsInput)
        assertEquals(listOf("X", "Y"), hasil.map { it.first })
        assertEquals(listOf("a", "c"), hasil[0].second.map { it.key })
    }

    @Test
    fun grupkan_inputKosongMenghasilkanTidakAdaKartu() {
        assertTrue(jenis(emptyList()).grupkan(emptyList()).isEmpty())
    }
}
