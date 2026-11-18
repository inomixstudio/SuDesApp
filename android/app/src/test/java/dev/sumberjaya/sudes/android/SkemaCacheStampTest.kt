package dev.sumberjaya.sudes.android

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * Aturan kapan cache skema terbitan desktop boleh dipakai.
 *
 * Gejalanya terlihat sebagai "APK yang dipasang tidak berubah": formulir
 * dibangun dari <c>skema-cache.json</c> warisan sinkronisasi lama, padahal APK
 * baru membawa skema yang lebih lengkap — kolom yang desktop buang masih
 * muncul dan bidang daftar tambahan kehilangan kolom inputnya. Cache hanya
 * boleh menang kalau ia ditulis oleh APK yang sama DAN diterbitkan tidak sebelum
 * skema APK ini dibekukan. Aturannya murni, jadi bisa diuji tanpa Android.
 */
class SkemaCacheStampTest {

    private val hashApk = "aa11".repeat(16)
    private val hashApkLain = "bb22".repeat(16)
    private val versi = 21500
    private val bekukan = 1_700_000_000_000L

    private fun boleh(
        stampHash: String?,
        stampVersi: Int,
        stampTerbit: Long,
        hashSekarang: String? = hashApk,
        versiSekarang: Int = versi,
        bekukanApk: Long = bekukan,
    ) = SkemaLokal.cacheBolehDipakai(stampHash, stampVersi, stampTerbit, hashSekarang, versiSekarang, bekukanApk)

    @Test
    fun terbitanSetelahApkDibekukanBolehDipakai() {
        assertTrue(boleh(hashApk, versi, bekukan))
        assertTrue(boleh(hashApk, versi, bekukan + 60_000L))
    }

    @Test
    fun terbitanDesktopLamaKalahOlehApkBaru() {
        // Inilah kasus "handphone masih baca file lama": desktop yang menerbitkan
        // belum ikut rilis terbaru, jadi terbitannya lebih tua dari skema APK.
        assertFalse(boleh(hashApk, versi, bekukan - 1L))
        assertFalse(boleh(hashApk, versi, 0L))
    }

    @Test
    fun apkDenganSkemaBaruMengalahkanCacheLama() {
        // Build APK berbeda (versionCode sama, isi skema berbeda).
        assertFalse(boleh(hashApkLain, versi, bekukan + 1L))
    }

    @Test
    fun cacheDariVersiApkLainTidakDipakai() {
        assertFalse(boleh(hashApk, 21400, bekukan + 1L))
    }

    @Test
    fun cacheWarisanTanpaStampTidakDipakai() {
        assertFalse(boleh(null, versi, bekukan + 1L))
        assertFalse(boleh(hashApk, -1, bekukan + 1L))
    }

    @Test
    fun asetTidakTerbacaTidakMengambilAlih() {
        // Tanpa sidik jari aset, server tetap yang dipegang.
        assertFalse(boleh(hashApk, versi, bekukan + 1L, hashSekarang = null))
    }

    @Test
    fun buildTanpaWaktuSkemaTidakMenangDariServer() {
        // apkSnapshotMillis = 0 berarti waktu pembekuan tidak diketahui (build
        // tanpa riwayat git): jangan sampai APK buta menolak terbitan desktop.
        assertTrue(boleh(hashApk, versi, 0L, bekukanApk = 0L))
        assertTrue(boleh(hashApk, versi, bekukan, bekukanApk = 0L))
    }
}
