package dev.sumberjaya.sudes.android

import android.content.ContentValues
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.Environment
import android.print.PageRange
import android.print.PrintAttributes
import android.print.PrintDocumentAdapter
import android.print.PrintDocumentInfo
import android.print.PrintManager
import android.provider.MediaStore
import java.io.File
import java.io.FileOutputStream
import java.io.InputStream
import java.io.OutputStream

/**
 * Menyimpan dan mencetak PDF.
 *
 * PDF surat disimpan ke folder Downloads/SuDesApp supaya operator bisa
 * membukanya atau membagikannya dari pengelola berkas. Tidak ada data yang
 * dikirim ke layanan mana pun — salinan lokal ini satu-satunya keluaran.
 */
object PdfAksi {

    /**
     * Salin [sumber] ke Downloads/SuDesApp. Mengembalikan jalur tampilan untuk
     * pesan, atau null bila gagal (penyimpanan penuh, izin ditolak).
     */
    fun simpanKeDownloads(ctx: Context, sumber: File): String? {
        val nama = sumber.name

        return try {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                val nilai = ContentValues().apply {
                    put(MediaStore.Downloads.DISPLAY_NAME, nama)
                    put(MediaStore.Downloads.MIME_TYPE, "application/pdf")
                    put(MediaStore.Downloads.RELATIVE_PATH, Environment.DIRECTORY_DOWNLOADS + "/SuDesApp")
                }
                val uri = ctx.contentResolver.insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, nilai)
                    ?: return null
                ctx.contentResolver.openOutputStream(uri)?.use { keluar ->
                    sumber.inputStream().use { it.copyTo(keluar) }
                } ?: return null
                "Downloads/SuDesApp/$nama"
            } else {
                @Suppress("DEPRECATION")
                val folder = File(
                    Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS),
                    "SuDesApp",
                )
                if (!folder.exists() && !folder.mkdirs()) return null
                val tujuan = File(folder, nama)
                sumber.inputStream().use { masuk ->
                    tujuan.outputStream().use { keluar -> masuk.copyTo(keluar) }
                }
                tujuan.absolutePath
            }
        } catch (e: Exception) {
            null
        }
    }

    /** Cetak [berkas] lewat dialog cetak Android. */
    fun cetak(ctx: Context, berkas: File, judul: String) {
        cetakStream(ctx, judul, { berkas.inputStream() }, { callback, aliran, sinyal, tujuan ->
            aliran.use { masuk ->
                FileOutputStream(tujuan.fileDescriptor).use { keluar -> masuk.copyTo(keluar) }
            }
        })
    }

    /** Cetak berkas yang hanya tersedia sebagai URI (MediaStore di API 29+). */
    fun cetakUri(ctx: Context, uri: Uri, judul: String) {
        cetakStream(ctx, judul, { ctx.contentResolver.openInputStream(uri) }, { callback, aliran, sinyal, tujuan ->
            aliran.use { masuk ->
                FileOutputStream(tujuan.fileDescriptor).use { keluar -> masuk.copyTo(keluar) }
            }
        })
    }

    private inline fun cetakStream(
        ctx: Context,
        judul: String,
        crossinline buka: () -> InputStream?,
        crossinline tulis: (
            callback: PrintDocumentAdapter.WriteResultCallback,
            aliran: InputStream,
            sinyal: android.os.CancellationSignal,
            tujuan: android.os.ParcelFileDescriptor,
        ) -> Unit,
    ) {
        val manajer = ctx.getSystemService(Context.PRINT_SERVICE) as PrintManager
        manajer.print(
            judul,
            object : PrintDocumentAdapter() {
                override fun onLayout(
                    lama: PrintAttributes?,
                    baru: PrintAttributes,
                    pembatalan: android.os.CancellationSignal?,
                    callback: PrintDocumentAdapter.LayoutResultCallback,
                    tambahan: android.os.Bundle?,
                ) {
                    if (pembatalan?.isCanceled == true) {
                        callback.onLayoutCancelled()
                        return
                    }
                    callback.onLayoutFinished(
                        PrintDocumentInfo.Builder(judul)
                            .setContentType(PrintDocumentInfo.CONTENT_TYPE_DOCUMENT)
                            .setPageCount(PrintDocumentInfo.PAGE_COUNT_UNKNOWN)
                            .build(),
                        true,
                    )
                }

                override fun onWrite(
                    halaman: Array<out PageRange>?,
                    tujuan: android.os.ParcelFileDescriptor,
                    pembatalan: android.os.CancellationSignal?,
                    callback: PrintDocumentAdapter.WriteResultCallback,
                ) {
                    val aliran = try {
                        buka() ?: run {
                            callback.onWriteFailed("Berkas tidak bisa dibuka")
                            return
                        }
                    } catch (e: Exception) {
                        callback.onWriteFailed(e.message)
                        return
                    }

                    try {
                        tulis(callback, aliran, pembatalan!!, tujuan)
                        callback.onWriteFinished(arrayOf(PageRange.ALL_PAGES))
                    } catch (e: Exception) {
                        callback.onWriteFailed(e.message)
                    }
                }
            },
            PrintAttributes.Builder().build(),
        )
    }

    /** Bagikan PDF lewat aplikasi lain (WhatsApp, email, drive). */
    fun bagikan(ctx: Context, berkas: BerkasSurat) {
        val uri = berkas.uri ?: berkas.berkas?.let {
            androidx.core.content.FileProvider.getUriForFile(
                ctx, "${ctx.packageName}.berkas", it
            )
        } ?: return

        val intent = Intent(Intent.ACTION_SEND).apply {
            type = "application/pdf"
            putExtra(Intent.EXTRA_STREAM, uri)
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
        ctx.startActivity(Intent.createChooser(intent, "Bagikan surat"))
    }
}
