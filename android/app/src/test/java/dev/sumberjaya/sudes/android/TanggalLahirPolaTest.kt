package dev.sumberjaya.sudes.android

import org.junit.Assert.assertEquals
import org.junit.Test

/**
 * Pola tanggal lahir di PDF Android harus sama dengan generator desktop.
 *
 * Desktop tidak seragam: GarapanGenerator dan IzinOrtuGenerator mencetak
 * tanggal lewat ParseTanggalToUiFormat ("dd-MM-yyyy"), sisanya mengikuti
 * SuratGeneratorBase ("dd MMMM yyyy" id-ID). Pemilih tanggal Android
 * menghasilkan dd/MM/yyyy — bentuk yang tidak pernah muncul di PDF desktop —
 * jadi aturan ini diuji supaya tidak kembali bocor ke hasil cetak.
 */
class TanggalLahirPolaTest {

    @Test
    fun polaTanggalLahir_sesuaiGeneratorDesktop() {
        assertEquals("dd-MM-yyyy", TanggalIndonesia.polaTanggalLahir("GARAPAN_SAWAH"))
        assertEquals("dd-MM-yyyy", TanggalIndonesia.polaTanggalLahir("IZIN_ORTU"))
        assertEquals("dd MMMM yyyy", TanggalIndonesia.polaTanggalLahir("SKD_UMUM"))
        assertEquals("dd MMMM yyyy", TanggalIndonesia.polaTanggalLahir("KEMATIAN"))
        assertEquals("dd MMMM yyyy", TanggalIndonesia.polaTanggalLahir("NTCR_N1"))
        //_CASE-nya idJenis dari skema, tetapi pencocokan tidak boleh sensitif.
        assertEquals("dd-MM-yyyy", TanggalIndonesia.polaTanggalLahir("garapan_sawah"))
    }

    @Test
    fun dari_memakaiPolaYangDiminta() {
        assertEquals("17-11-1990", TanggalIndonesia.dari("1990-11-17", "dd-MM-yyyy"))
        assertEquals("17 November 1990", TanggalIndonesia.dari("1990-11-17", "dd MMMM yyyy"))
        // Bentuk lain yang mungkin tersimpan (pemilih tanggal, ketikan lama).
        assertEquals("17-11-1990", TanggalIndonesia.dari("17/11/1990", "dd-MM-yyyy"))
        assertEquals("01-01-2000", TanggalIndonesia.dari("01/01/2000", "dd-MM-yyyy"))
    }

    @Test
    fun dari_bawaanBertahanPanjang() {
        assertEquals("17 November 1990", TanggalIndonesia.dari("1990-11-17"))
    }

    @Test
    fun dari_teksBukanTanggalDisalinApaAdanya() {
        assertEquals("", TanggalIndonesia.dari("", "dd-MM-yyyy"))
        assertEquals("1990", TanggalIndonesia.dari("1990", "dd-MM-yyyy"))
        assertEquals("tidak diketahui", TanggalIndonesia.dari("tidak diketahui", "dd MMMM yyyy"))
        // Sudah panjang tidak boleh dimutasi ulang.
        assertEquals("17 November 1990", TanggalIndonesia.dari("17 November 1990", "dd-MM-yyyy"))
    }
}
