package dev.sumberjaya.sudes.android

/** Satu baris antrean surat milik perangkat ini. */
data class AntreanBaris(
    val id: String,
    val idJenis: String,
    val nomorSementara: String,
    val nik: String,
    val status: String,
    val dibuatPada: String,
    val catatan: String,
) {
    /** Label status yang ramah dibaca operator. */
    val statusLabel: String
        get() = when (status.lowercase()) {
            "menunggu" -> "Menunggu sinkron"
            "sinkron" -> "Sudah sinkron"
            "gagal" -> "Gagal"
            else -> status
        }
}
