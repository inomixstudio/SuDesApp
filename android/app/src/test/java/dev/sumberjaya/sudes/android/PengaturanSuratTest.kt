package dev.sumberjaya.sudes.android

import org.junit.Assert.assertEquals
import org.junit.Test

/**
 * Uji penyusunan nomor surat dari pola .NET dan pembentukan baris kop.
 *
 * Nomor surat adalah identitas resmi: bentuknya harus sama dengan yang
 * diterbitkan desktop, dan kop tidak boleh memuat kata pengulang
 * ("KOTA KOTA BANDUNG", "Email Email ...").
 */
class PengaturanSuratTest {

    @Test
    fun susunNomor_konversiD3() {
        assertEquals(
            "470/007/Ds/2026",
            PengaturanSurat().susunNomor("470/{0:D3}/Ds/{2:yyyy}", 7, "sktm", "2026"),
        )
    }

    @Test
    fun susunNomor_tanpaPresisi() {
        assertEquals(
            "470/7/Ds/2026",
            PengaturanSurat().susunNomor("470/{0}/Ds/{2}", 7, "x", "2026"),
        )
    }

    @Test
    fun susunNomor_kodeJenis() {
        assertEquals(
            "470/001/SKT/2026",
            PengaturanSurat().susunNomor("470/{0:D3}/{1}/{2}", 1, "SKT", "2026"),
        )
    }

    @Test
    fun susunNomor_presisiD4() {
        assertEquals("0007", PengaturanSurat().susunNomor("{0:D4}", 7, "x", "2026"))
    }

    @Test
    fun kop_membuangAwalanKota() {
        val baris = Kop.baris(PengaturanSurat(kabupaten = "Kota Bandung", jenisKabupaten = "Kota"))
        assertEquals("PEMERINTAH KOTA BANDUNG", baris.first().teks)
    }

    @Test
    fun kop_membuangAwalanKabupaten() {
        val baris = Kop.baris(
            PengaturanSurat(kabupaten = "Kabupaten Bandung", jenisKabupaten = "Kabupaten")
        )
        assertEquals("PEMERINTAH KABUPATEN BANDUNG", baris.first().teks)
    }

    @Test
    fun kop_membuangSingkatanKab() {
        val baris = Kop.baris(PengaturanSurat(kabupaten = "Kab. Bandung"))
        assertEquals("PEMERINTAH KABUPATEN BANDUNG", baris.first().teks)
    }

    @Test
    fun kop_membersihkanAwalanSurel() {
        assertEquals("desa@x.id", Kop.SurelBersih("email desa@x.id"))
        assertEquals("desa@x.id", Kop.SurelBersih("mailto:desa@x.id"))
        assertEquals("desa@x.id", Kop.SurelBersih("surel: desa@x.id"))
        assertEquals("", Kop.SurelBersih("   "))
    }

    @Test
    fun kop_tidakMencetakBarisSurelKosong() {
        val baris = Kop.baris(PengaturanSurat(email = "   "))
        assertEquals(5, baris.size)
    }

    @Test
    fun kop_mencetakBarisSurelBilaAda() {
        val baris = Kop.baris(PengaturanSurat(email = "desa@x.id"))
        assertEquals(6, baris.size)
        assertEquals("Email desa@x.id", baris.last().surel)
    }

    @Test
    fun jabatan_membuangAwalanDesa() {
        // Desktop membersihkan nama desa (KopSurat.DesaBersih) sebelum dicetak.
        assertEquals(
            "KEPALA DESA SUMBERJAYA",
            Kop.jabatan(PengaturanSurat(namaDesa = "Desa Sumberjaya"), "Kepala Desa"),
        )
    }

    @Test
    fun jabatan_kelurahanMemakaiLurah() {
        assertEquals(
            "LURAH SUMBERJAYA",
            Kop.jabatan(
                PengaturanSurat(namaDesa = "Sumberjaya", jenisLembaga = "Kelurahan"),
                "Kepala Desa",
            ),
        )
    }

    @Test
    fun jabatan_sekretarisDesa() {
        assertEquals(
            "A/N KEPALA DESA SUMBERJAYA\nSEKRETARIS DESA",
            Kop.jabatan(PengaturanSurat(namaDesa = "Desa Sumberjaya"), "Sekretaris Desa"),
        )
    }
}
