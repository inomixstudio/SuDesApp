package dev.sumberjaya.sudes.android

import java.text.SimpleDateFormat
import java.util.Calendar
import java.util.Locale

/**
 * Validasi isian formulir yang sama dengan yang dilakukan desktop
 * (Validator.ValidateNik + ValidateTanggalLahir). Dipisahkan agar bisa
 * diuji tanpa Activity/Android UI.
 */
object ValidasiForm {
    /** NIK harus tepat 16 digit angka. */
    fun nikValid(teks: String?): Boolean =
        !teks.isNullOrBlank() && Regex("^\\d{16}$").matches(teks.trim())

    /** Tanggal lahir harus terbaca, tidak di masa depan, dan tidak sebelum 1900. */
    fun tanggalLahirValid(teks: String?): Boolean {
        val bersih = teks?.trim().orEmpty()
        if (bersih.isEmpty()) return false
        val kalender = uraikanTanggal(bersih) ?: return false
        if (kalender.after(Calendar.getInstance())) return false
        val palingLama = Calendar.getInstance().apply {
            clear()
            set(1900, Calendar.JANUARY, 1)
        }
        if (kalender.before(palingLama)) return false
        return true
    }

    /** Semua bidang wajib di dalam baris daftar harus non-blank. */
    fun barisWajibTerisi(
        baris: List<Map<String, String>>,
        wajib: List<String>,
    ): Boolean = baris.all { row -> wajib.all { key -> !row[key].isNullOrBlank() } }

    private fun uraikanTanggal(teks: String): Calendar? {
        val pola = listOf(
            SimpleDateFormat("yyyy-MM-dd", Locale.US),
            SimpleDateFormat("dd/MM/yyyy", Locale.US),
            SimpleDateFormat("dd-MM-yyyy", Locale.US),
        )
        for (p in pola) {
            try {
                val cal = Calendar.getInstance()
                cal.time = p.parse(teks) ?: continue
                return cal
            } catch (e: Exception) {
                // coba pola berikutnya
            }
        }
        return null
    }
}
