package dev.sumberjaya.sudes.android

import android.content.Context

/**
 * Identitas kop surat dan penomoran.
 *
 * Disimpan di perangkat (bukan di layanan mana pun) karena hanya identitas
 * desa yang memakai aplikasi ini. Nomor urut dimulai dari angka yang diisi
 * operator — cocokkan dengan nomor terakhir di register desktop supaya nomor
 * surat tidak lompat.
 */
data class PengaturanSurat(
    val namaDesa: String = "",
    val kecamatan: String = "",
    val kabupaten: String = "",
    val awalanNomor: String = "470",
    val nomorTerakhir: Int = 0,
) {
    /** Kop tiga baris; baris kosong dilewati supaya kop tidak bolong-bolong. */
    fun barisKop(): List<String> = listOf(namaDesa, kecamatan, kabupaten)
        .map { it.trim() }
        .filter { it.isNotEmpty() }

    /**
     * Nomor surat berikutnya dan angka yang tersimpan naik satu.
     * Format mengikuti kebiasaan register: 470/001/Ds/2026.
     */
    fun nomorBerikutnya(tahun: Int, prefs:android.content.SharedPreferences): String {
        val urut = nomorTerakhir + 1
        val nomor = "${awalanNomor.trim()}/${urut.toString().padStart(3, '0')}/Ds/$tahun"
        prefs.edit().putInt(K_NOMOR, urut).apply()
        return nomor
    }

    companion object {
        private const val PREFS = "sudes_surat"
        private const val K_DESA = "desa"
        private const val K_KECAMATAN = "kecamatan"
        private const val K_KABUPATEN = "kabupaten"
        private const val K_AWALAN = "awalan"
        private const val K_NOMOR = "nomor"

        fun ambil(ctx: Context): PengaturanSurat {
            val p = ctx.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
            return PengaturanSurat(
                namaDesa = p.getString(K_DESA, "") ?: "",
                kecamatan = p.getString(K_KECAMATAN, "") ?: "",
                kabupaten = p.getString(K_KABUPATEN, "") ?: "",
                awalanNomor = (p.getString(K_AWALAN, "470") ?: "470").trim(),
                nomorTerakhir = p.getInt(K_NOMOR, 0),
            )
        }

        fun simpan(ctx: Context, p: PengaturanSurat) {
            ctx.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit()
                .putString(K_DESA, p.namaDesa)
                .putString(K_KECAMATAN, p.kecamatan)
                .putString(K_KABUPATEN, p.kabupaten)
                .putString(K_AWALAN, p.awalanNomor)
                .putInt(K_NOMOR, p.nomorTerakhir)
                .apply()
        }

        /** Naikkan nomor urut setelah PDF dibuat; dipanggil lewat [nomorBerikutnya]. */
        fun simpanNomor(ctx: Context, urut: Int) {
            ctx.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit()
                .putInt(K_NOMOR, urut)
                .apply()
        }
    }
}
