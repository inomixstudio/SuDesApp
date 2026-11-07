package dev.sumberjaya.sudes.android

import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import java.io.File

/**
 * Muat gambar logo kop dari jalur yang disimpan di Pengaturan.
 * Null bila jalurnya kosong, berkasnya hilang, atau tidak bisa dibaca —
 * kop tetap dicetak tanpa logo, sama seperti desktop.
 */
object MuatLogo {
    fun muat(ctx: Context, jalur: String): Bitmap? {
        if (jalur.isBlank()) return null
        return try {
            val berkas = File(jalur)
            if (!berkas.exists() || !berkas.canRead()) return null
            BitmapFactory.decodeFile(jalur)
        } catch (e: Exception) {
            null
        }
    }
}
