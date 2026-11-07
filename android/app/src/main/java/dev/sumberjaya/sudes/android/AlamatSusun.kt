package dev.sumberjaya.sudes.android

/**
 * Menyusun alamat lengkap penduduk dari komponen alamat — sama dengan
 * AlamatFormatter di desktop, termasuk pemisahnya yang SPASI (tanpa koma).
 *
 * Baris 1: Dusun/Jalan + RT/RW + Desa — Baris 2: Kecamatan + Kabupaten.
 */
object AlamatSusun {

    fun susun(nilai: Map<String, String>): String =
        susun(
            dusunJalan = nilai["warga.dusun"] ?: nilai["warga.dusunJalan"] ?: "",
            desa = nilai["warga.desa"] ?: "",
            kecamatan = nilai["warga.kecamatan"] ?: "",
            kabupaten = nilai["warga.kabupaten"] ?: "",
            rt = nilai["warga.rt"],
            rw = nilai["warga.rw"],
        )

    fun susun(
        dusunJalan: String,
        desa: String,
        kecamatan: String,
        kabupaten: String,
        rt: String? = null,
        rw: String? = null,
        fallback: String = "[Alamat Warga]",
    ): String {
        val baris1 = mutableListOf<String>()
        tambahkan(baris1, normalDusunJalan(dusunJalan))
        tambahkan(baris1, formatRtRw(rt, rw))
        tambahkan(baris1, desa, "Desa")

        val baris2 = mutableListOf<String>()
        tambahkan(baris2, kecamatan, "Kecamatan")
        tambahkan(baris2, kabupaten, "Kabupaten")

        return when {
            baris1.isEmpty() && baris2.isEmpty() -> fallback
            baris2.isEmpty() -> baris1.joinToString(" ")
            baris1.isEmpty() -> baris2.joinToString(" ")
            else -> baris1.joinToString(" ") + "\n" + baris2.joinToString(" ")
        }
    }

    private fun tambahkan(bagian: MutableList<String>, nilai: String?, awalan: String? = null) {
        val teks = (nilai ?: "").trim()
        if (teks.isEmpty()) return
        bagian.add(if (awalan != null) "$awalan $teks" else teks)
    }

    /** "RT 001/RW 002", "RT 001", atau "RW 002"; kosong bila keduanya kosong. */
    fun formatRtRw(rt: String?, rw: String?): String {
        val a = normalWilayah(rt, "RT")
        val b = normalWilayah(rw, "RW")
        return when {
            a.isNotEmpty() && b.isNotEmpty() -> "RT $a/RW $b"
            a.isNotEmpty() -> "RT $a"
            b.isNotEmpty() -> "RW $b"
            else -> ""
        }
    }

    /** Buang awalan "RT"/"RW" bila pengguna sudah mengetiknya sendiri. */
    private fun normalWilayah(nilai: String?, label: String): String {
        var teks = (nilai ?: "").trim()
        if (teks.startsWith(label, ignoreCase = true)) {
            teks = teks.substring(label.length).trimStart(' ', '.', '-', '/')
        }
        return teks
    }

    /**
     * Nilai Dusun/Jalan dibiarkan apa adanya bila sudah berlabel sendiri
     * ("Dusun Krajan", "Jl. Merdeka"); bila belum, diawali "Dusun ".
     */
    private fun normalDusunJalan(nilai: String?): String {
        val teks = (nilai ?: "").trim()
        if (teks.isEmpty()) return ""
        val lower = teks.lowercase()
        val sudahBerlabel = lower.startsWith("dusun ") || lower.startsWith("jl.") ||
            lower.startsWith("jl ") || lower.startsWith("jalan ")
        return if (sudahBerlabel) teks else "Dusun $teks"
    }
}
