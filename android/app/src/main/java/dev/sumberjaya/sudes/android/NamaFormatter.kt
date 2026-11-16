package dev.sumberjaya.sudes.android

/**
 * Penulisan nama orang pada PDF — salinan perilaku desktop
 * (`SuDesApp.Utilities.NamaFormatter`): NAMA POKOKNYA saja yang kapital,
 * gelar depan (Dr., Hj., Ir., …) dan gelar belakang (S.H., M.M., …) tetap
 * apa adanya. Tanpa ini nama bergelar tercetak KAPITAL SEMUA termasuk
 * gelarnya, berbeda dengan PDF resmi desktop.
 */
object NamaFormatter {

    /// butir 1 — nama di tampilan stabil (daftar warga, surat mutasi)
    private val GELAR_DEPAN = setOf(
        "prof", "profesor", "dr", "drg", "drs", "dra", "ir",
        "h", "hj", "haji", "hajjah", "kh", "kyai", "ust", "ustad", "ustadz", "ustadzah",
        "r", "ra", "rd", "rt", "rm", "rr", "rng", "kr", "kp", "bra", "ba", "aa", "mas", "mb",
    )

    private val GELAR_BELAKANG = setOf(
        "se", "sh", "st", "sp", "ssi", "ssos", "spd", "sag", "si", "skom", "sfarm", "sked", "skep",
        "spsi", "stp", "sip", "shut", "skel", "spt", "sgz", "sikom", "sm", "snm", "skm", "sos",
        "mm", "mh", "msi", "mkom", "mpd", "mag", "mt", "mst", "mkeu", "mkes", "mfarm", "mpsi",
        "mmp", "mmpd", "msos", "mkn", "mip", "mtp", "mhut", "mes", "me", "ma", "mphil", "phd",
        "amd", "apt", "ns", "bd", "nrs", "ners",
    )

    /**
     * Nama dengan KAPITAL di awal tiap kata saja ("AHMAD SURYANA" → "Ahmad
     * Suryana"). Kata bertitik dibiarkan apa adanya (gelar "S.H." tidak
     * jadi "S.h."), huruf setelah tanda hubung/apostrof ikut kapital.
     */
    fun kapitalTiapKata(nama: String?): String {
        if (nama.isNullOrBlank()) return nama ?: ""
        return nama.split(' ', '\t').filter { it.isNotEmpty() }.joinToString(" ") { kata ->
            if (kata.contains('.')) kata else kapitalBagian(kata)
        }
    }

    private fun kapitalBagian(kata: String): String {
        val huruf = kata.toCharArray()
        var kapitalBerikut = true
        for (i in huruf.indices) {
            val c = huruf[i]
            when {
                c == '-' || c == '\'' -> kapitalBerikut = true
                kapitalBerikut -> {
                    huruf[i] = c.uppercaseChar(); kapitalBerikut = false
                }
                else -> huruf[i] = c.lowercaseChar()
            }
        }
        return String(huruf)
    }

    /**
     * Nama orang dengan bagian NAMANYA saja yang dijadikan kapital —
     * "Dr. H. Ahmad Suryana, S.H., M.M." → "Dr. H. AHMAD SURYANA, S.H., M.M.".
     * Gelar depan tetap satu kata minimal sebagai nama, gelar belakang tanpa
     * koma ikut dikenali, gelar setelah koma dibiarkan apa adanya.
     */
    fun toUpperNama(nama: String?): String {
        if (nama.isNullOrBlank()) return nama ?: ""

        val bagianKoma = nama.split(',')
        if (bagianKoma.isEmpty()) return nama
        val depan = bagianKoma[0].trim()
        val gelarSetelahKoma = bagianKoma.drop(1).map { it.trim() }.filter { it.isNotEmpty() }

        val kata = depan.split(' ', '\t').filter { it.isNotEmpty() }
        if (kata.isEmpty()) return nama

        // Gelar depan: sisakan minimal satu kata sebagai nama.
        var awalNama = 0
        while (awalNama < kata.size - 1 && isGelarDepan(kata[awalNama])) awalNama++

        // Gelar belakang tanpa koma: telusuri dari ujung belakang.
        var akhirNama = kata.size
        while (akhirNama - 1 > awalNama && isGelarBelakang(kata[akhirNama - 1], akhirNama - 1 - awalNama)) {
            akhirNama--
        }

        val hasil = mutableListOf<String>()
        hasil.addAll(kata.take(awalNama))
        hasil.add(kata.drop(awalNama).take(akhirNama - awalNama).joinToString(" ").uppercase())
        hasil.addAll(kata.drop(akhirNama))

        val namaKapital = hasil.joinToString(" ")
        return if (gelarSetelahKoma.isNotEmpty()) "$namaKapital, ${gelarSetelahKoma.joinToString(", ")}" else namaKapital
    }

    /** Nama kosong (blanko) → titik-titik, sejajar dengan blok pejabat. */
    fun namaAtauTitik(nama: String?) =
        if (nama.isNullOrBlank()) "..............................." else toUpperNama(nama)

    private fun isGelarDepan(kata: String): Boolean {
        // Perbandingan tak peka huruf besar/kecil — sama seperti HashSet
        // StringComparer.OrdinalIgnoreCase di desktop.
        val b = bersih(kata)
        return GELAR_DEPAN.any { it.equals(b, true) } || GELAR_DEPAN.any { it.equals(kata, true) }
    }

    private fun isGelarBelakang(kata: String, posisiRelatif: Int): Boolean {
        val bersih = bersih(kata)
        if (bersih.isEmpty()) return false
        if (GELAR_BELAKANG.any { it.equals(bersih, true) }) return true
        // Bentuk bertitik ("S.H.", "M.M.") sudah pasti gelar.
        if (kata.contains('.')) return true
        // Singkatan KAPITAL pendek setelah minimal dua kata nama, mis. "SH".
        return posisiRelatif >= 2 && bersih.length <= 5 &&
            bersih.all { !it.isLetter() || it.isUpperCase() } && bersih.any { it.isLetter() }
    }

    private fun bersih(kata: String) = kata.replace(".", "").replace(" ", "")
}
