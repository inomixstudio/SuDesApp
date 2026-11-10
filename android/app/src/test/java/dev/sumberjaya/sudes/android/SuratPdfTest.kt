package dev.sumberjaya.sudes.android

import org.junit.Assert.assertEquals
import org.junit.Test

/**
 * Uji substitusi placeholder `$kunci` pada badan surat.
 *
 * Perilaku ini menentukan isi surat: kesalahan satu karakter pada templat
 * membuat sel tercetak kosong tanpa gejala di kode, jadi aturannya diuji di
 * sini (termasuk titik di ujung kunci, yang bukan bagian kunci).
 */
class SuratPdfTest {

    @Test
    fun urai_menggantiKunciYangDikenal() {
        assertEquals("Budi", SuratPdf.urai("\$warga.nama", mapOf("warga.nama" to "Budi")))
    }

    @Test
    fun urai_kunciTakDikenalMenjadiKosong() {
        assertEquals("a  b", SuratPdf.urai("a \$warga.takAda b", emptyMap()))
    }

    @Test
    fun urai_titikDiUjungKunciAdalahTandaBaca() {
        assertEquals(
            "atas nama Siti.",
            SuratPdf.urai(
                "atas nama \$ahliwaris.almarhumNama.",
                mapOf("ahliwaris.almarhumNama" to "Siti"),
            ),
        )
    }

    @Test
    fun urai_dolarBergandaMenjadiDolarTunggal() {
        assertEquals("$5", SuratPdf.urai("\$\$5", emptyMap()))
    }

    @Test
    fun urai_kunciBersifatKasusSensitive() {
        assertEquals("", SuratPdf.urai("\$Warga.Nama", mapOf("warga.nama" to "Budi")))
    }

    @Test
    fun urai_dollarSendiriDicetakApaAdanya() {
        assertEquals("harga $ saja", SuratPdf.urai("harga \$ saja", emptyMap()))
    }
}
