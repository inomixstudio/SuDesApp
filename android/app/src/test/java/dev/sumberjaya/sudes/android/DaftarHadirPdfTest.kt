package dev.sumberjaya.sudes.android

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * Uji penentuan kolom & orientasi daftar hadir.
 *
 * Ambang lanskap (7 kolom) mengubah ukuran halaman, minimal baris, dan ukuran
 * font — salah hitung membuat tabel terpotong atau tata letak berubah saat
 * kotak centang diganti.
 *
 * Bawaan: No, Nama, Jabatan, Tanda Tangan = 4 kolom (Jabatan memang dicentang
 * di UI). No/Nama/Tanda Tangan tidak bisa dimatikan.
 */
class DaftarHadirPdfTest {

    @Test
    fun empatKolomBawaan_potret() {
        val data = DaftarHadirPdf.Data()
        assertEquals(4, data.jumlahKolom)
        assertFalse(data.landscape)
    }

    @Test
    fun enamKolom_masihPotret() {
        val data = DaftarHadirPdf.Data(
            tampilJenisKelamin = true,
            tampilNip = true,
        )
        assertEquals(6, data.jumlahKolom)
        assertFalse(data.landscape)
    }

    @Test
    fun tujuhKolom_lanskap() {
        val data = DaftarHadirPdf.Data(
            tampilJenisKelamin = true,
            tampilNip = true,
            tampilNik = true,
        )
        assertEquals(7, data.jumlahKolom)
        assertTrue(data.landscape)
    }

    @Test
    fun kolomWajibTetapDihitungWalauDimatikan() {
        // No, Nama, dan Tanda Tangan terkunci di UI; data class tetap
        // menghitungnya, jadi mematikannya mengurangi jumlah kolom.
        val data = DaftarHadirPdf.Data(
            tampilNo = false,
            tampilNama = false,
            tampilTandaTangan = false,
        )
        assertEquals(1, data.jumlahKolom) // hanya Jabatan
    }
}
