package dev.sumberjaya.sudes.android

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class ValidasiFormTest {
    @Test
    fun nikValid_16Digit() {
        assertTrue(ValidasiForm.nikValid("1234567890123456"))
        assertFalse(ValidasiForm.nikValid("123"))
        assertFalse(ValidasiForm.nikValid("123456789012345a"))
        assertFalse(ValidasiForm.nikValid(null))
    }

    @Test
    fun tanggalLahirValid_berbagaiKasus() {
        assertTrue(ValidasiForm.tanggalLahirValid("1990-01-01"))
        assertFalse(ValidasiForm.tanggalLahirValid("bukan-tanggal"))
        assertFalse(ValidasiForm.tanggalLahirValid("2099-01-01"))
        assertFalse(ValidasiForm.tanggalLahirValid("1800-01-01"))
    }

    @Test
    fun barisWajibTerisi() {
        val baris = listOf(mapOf("nama" to "A", "nik" to "123"))
        assertTrue(ValidasiForm.barisWajibTerisi(baris, listOf("nama", "nik")))
        assertFalse(ValidasiForm.barisWajibTerisi(listOf(mapOf("nama" to "A")), listOf("nama", "nik")))
        assertTrue(ValidasiForm.barisWajibTerisi(emptyList(), listOf("nama")))
    }
}
