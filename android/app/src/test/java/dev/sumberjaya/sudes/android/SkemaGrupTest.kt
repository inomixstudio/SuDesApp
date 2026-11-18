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

    // --- skema cache lama disempurnakan dengan kartu dari aset APK -----------
    // Inilah jalan kembali ke "satu daftar panjang": perangkat yang belum
    // sinkron dengan terbitan desktop terbaru tetap memakai cache lama tanpa
    // grup, walaupun APK-nya sudah benar.

    private val asetBaru = mapOf(
        "UJI" to jenis(
            listOf(bidang("nik", "DATA PEMOHON"), bidang("nama", "DATA PEMOHON"), bidang("luas", "DETAIL TANAH")),
            listOf("DATA PEMOHON", "DETAIL TANAH"),
        ),
    )

    @Test
    fun lengkapiGrup_memakaiKartuAsetBilaCacheBelumPunyaGrup() {
        val cacheLama = mapOf("UJI" to jenis(listOf(bidang("nik"), bidang("nama"), bidang("luas"))))

        val hasil = SkemaLokal.lengkapiGrup(cacheLama, asetBaru)
        val jenisHasil = hasil["UJI"]!!

        assertEquals(listOf("DATA PEMOHON", "DETAIL TANAH"), jenisHasil.grupUrutan)
        assertEquals(
            mapOf("nik" to "DATA PEMOHON", "nama" to "DATA PEMOHON", "luas" to "DETAIL TANAH"),
            jenisHasil.fields.associate { it.key to it.grup },
        )
        // Buktinya satu: formulir tidak lagi jadi satu daftar datar.
        assertEquals(2, jenisHasil.grupkan(jenisHasil.fieldsInput).size)
    }

    @Test
    fun lengkapiGrup_tidakMenimpaGrupDariServer() {
        val cacheServer = mapOf(
            "UJI" to jenis(
                listOf(bidang("nik", "DARI SERVER"), bidang("nama", "DARI SERVER")),
                listOf("DARI SERVER"),
            ),
        )

        val hasil = SkemaLokal.lengkapiGrup(cacheServer, asetBaru)

        assertEquals(listOf("DARI SERVER"), hasil["UJI"]!!.grupUrutan)
        assertEquals(listOf("DARI SERVER"), hasil["UJI"]!!.fields.map { it.grup }.distinct())
    }

    @Test
    fun lengkapiGrup_hanyaMengisiBidangYangDikenalAset() {
        val kolomBaruDesktop = listOf(bidang("nik"), bidang("nama"), bidang("luas"), bidang("panen"))
        val cacheServer = mapOf("UJI" to jenis(kolomBaruDesktop))

        val hasil = SkemaLokal.lengkapiGrup(cacheServer, asetBaru)

        // Bidang yang belum ada di APK ikut terbawa tanpa grup: ia tetap tampil
        // (dikelompokkan terakhir berjudul kosong), tidak sampai hilang.
        assertEquals("", hasil["UJI"]!!.fields.first { it.key == "panen" }.grup)
        assertEquals(4, hasil["UJI"]!!.fields.size)
    }

    @Test
    fun lengkapiGrup_mempertahankanIsiServerLainnya() {
        val cacheLama = mapOf("UJI" to jenis(listOf(bidang("nik"), bidang("nama"), bidang("luas"))))

        val hasil = SkemaLokal.lengkapiGrup(cacheLama, asetBaru)

        // Hanya pengelompokan yang berubah: jumlah dan urutan field tetap.
        assertEquals(listOf("nik", "nama", "luas"), hasil["UJI"]!!.fields.map { it.key })
        assertEquals("Uji", hasil["UJI"]!!.label)
    }

    @Test
    fun lengkapiGrup_amanBilaAsetTidakAdaAtauJenisAsing() {
        val cuma = mapOf("UJI" to jenis(listOf(bidang("nik"))))

        assertTrue(SkemaLokal.lengkapiGrup(cuma, null).isEmpty() == false)
        assertEquals(cuma["UJI"]!!.grupUrutan, SkemaLokal.lengkapiGrup(cuma, null)["UJI"]!!.grupUrutan)
        assertEquals(emptyList<String>(), SkemaLokal.lengkapiGrup(cuma, emptyMap())["UJI"]!!.grupUrutan)
        assertTrue(SkemaLokal.lengkapiGrup(cuma, mapOf("LAIN" to jenis(listOf(bidang("nik", "X"))))).let {
            it["UJI"]!!.grupUrutan.isEmpty()
        })
        assertTrue(SkemaLokal.lengkapiGrup(emptyMap(), asetBaru).isEmpty())
    }
}
