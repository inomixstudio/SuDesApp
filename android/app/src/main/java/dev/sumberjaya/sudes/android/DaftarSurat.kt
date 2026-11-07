package dev.sumberjaya.sudes.android

import android.content.Context
import android.net.Uri
import android.os.Build
import android.os.Environment
import android.provider.MediaStore
import java.io.File

/** Satu berkas surat yang pernah dibuat di perangkat ini. */
data class BerkasSurat(
    val nama: String,
    val jalur: String,
    val diubah: Long,
    val uri: Uri?,
    val berkas: File?,
)

/**
 * Daftar PDF surat yang tersimpan di Downloads/SuDesApp.
 *
 * Dipakai halaman Register Surat supaya operator bisa mencetak ulang atau
# membagikan surat tanpa membuatnya dari awal.
 */
object DaftarSurat {
    private const val FOLDER = "SuDesApp"

    fun ambil(ctx: Context): List<BerkasSurat> {
        val daftar = mutableListOf<BerkasSurat>()

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            val proyeksi = arrayOf(
                MediaStore.Downloads._ID,
                MediaStore.Downloads.DISPLAY_NAME,
                MediaStore.Downloads.DATE_MODIFIED,
            )
            ctx.contentResolver.query(
                MediaStore.Downloads.EXTERNAL_CONTENT_URI,
                proyeksi,
                "${MediaStore.Downloads.RELATIVE_PATH} LIKE ?",
                arrayOf("%$FOLDER%"),
                "${MediaStore.Downloads.DATE_MODIFIED} DESC",
            )?.use { k ->
                val kolomId = k.getColumnIndexOrThrow(MediaStore.Downloads._ID)
                val kolomNama = k.getColumnIndexOrThrow(MediaStore.Downloads.DISPLAY_NAME)
                val kolomTanggal = k.getColumnIndexOrThrow(MediaStore.Downloads.DATE_MODIFIED)

                while (k.moveToNext()) {
                    val id = k.getLong(kolomId)
                    val uri = Uri.withAppendedPath(MediaStore.Downloads.EXTERNAL_CONTENT_URI, id.toString())
                    daftar.add(
                        BerkasSurat(
                            nama = if (k.isNull(kolomNama)) "surat.pdf" else k.getString(kolomNama),
                            jalur = "Downloads/$FOLDER",
                            diubah = k.getLong(kolomTanggal) * 1000L,
                            uri = uri,
                            berkas = null,
                        )
                    )
                }
            }
        } else {
            @Suppress("DEPRECATION")
            val folder = File(
                Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS),
                FOLDER,
            )
            folder.listFiles { f -> f.isFile && f.name.endsWith(".pdf", ignoreCase = true) }
                ?.sortedByDescending { it.lastModified() }
                ?.forEach {
                    daftar.add(
                        BerkasSurat(it.name, it.absolutePath, it.lastModified(), null, it)
                    )
                }
        }

        return daftar
    }
}
