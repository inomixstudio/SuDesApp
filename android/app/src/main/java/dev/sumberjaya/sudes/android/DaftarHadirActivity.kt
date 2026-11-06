package dev.sumberjaya.sudes.android

import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Typeface
import android.graphics.pdf.PdfDocument
import android.os.Bundle
import android.print.PrintAttributes
import android.print.PrintDocumentAdapter
import android.print.PrintDocumentInfo
import android.print.PrintManager
import android.view.View
import android.widget.Button
import android.widget.EditText
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import java.io.File
import java.io.FileOutputStream

/**
 * Daftar Hadir: membuat dan mencetak daftar hadir sebagai PDF.
 *
 * Sesuai kesepakatan: fitur ini TIDAK menyimpan data — PDF dibuat, dicetak,
 * dan selesai. Tidak masuk antrean Supabase seperti surat.
 */
class DaftarHadirActivity : AppCompatActivity() {

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_daftar_hadir)

        val acara = findViewById<EditText>(R.id.input_acara)
        val tanggal = findViewById<EditText>(R.id.input_tanggal)
        val tempat = findViewById<EditText>(R.id.input_tempat)
        val pembuat = findViewById<EditText>(R.id.input_pembuat)
        val peserta = findViewById<EditText>(R.id.input_peserta)

        if (tanggal.text.isNullOrBlank()) {
            tanggal.setText(java.text.SimpleDateFormat("yyyy-MM-dd", java.util.Locale.US).format(java.util.Date()))
        }

        findViewById<Button>(R.id.btn_cetak).setOnClickListener {
            if (acara.text.isNullOrBlank()) {
                Toast.makeText(this, "Kegiatan wajib diisi", Toast.LENGTH_SHORT).show()
                return@setOnClickListener
            }

            cetak(
                acara.text.toString().trim(),
                tanggal.text.toString().trim(),
                tempat.text.toString().trim(),
                pembuat.text.toString().trim(),
                peserta.text.toString().trim(),
            )
        }
    }

    private fun cetak(acara: String, tanggal: String, tempat: String, pembuat: String, peserta: String) {
        val dokumen = PdfDocument()
        val halaman = dokumen.startPage(
            PdfDocument.PageInfo.Builder(595, 842, 1).create() // A4 @72dpi
        )

        val canvas = halaman.canvas
        var y = 60f

        val judul = Paint().apply {
            color = Color.BLACK
            textSize = 16f
            typeface = Typeface.create(Typeface.SANS_SERIF, Typeface.BOLD)
            textAlign = Paint.Align.CENTER
        }
        canvas.drawText("DAFTAR HADIR", 297f, y, judul)
        y += 26f

        val biasa = Paint().apply { color = Color.BLACK; textSize = 12f }
        canvas.drawText("Kegiatan: $acara", 48f, y, biasa); y += 18f
        canvas.drawText("Tanggal: $tanggal", 48f, y, biasa); y += 18f
        canvas.drawText("Tempat: $tempat", 48f, y, biasa); y += 26f

        // Kepala tabel
        canvas.drawText("No.", 48f, y, biasa)
        canvas.drawText("Nama", 90f, y, biasa)
        canvas.drawText("Tanda Tangan", 380f, y, biasa)
        y += 8f
        canvas.drawLine(48f, y, 547f, y, biasa)
        y += 18f

        val daftar = peserta.lines().map { it.trim() }.filter { it.isNotEmpty() }
        if (daftar.isEmpty()) {
            canvas.drawText("(tidak ada peserta)", 90f, y, biasa); y += 18f
        } else {
            for ((i, nama) in daftar.withIndex()) {
                canvas.drawText("${i + 1}.", 48f, y, biasa)
                canvas.drawText(nama, 90f, y, biasa)
                canvas.drawText("......................", 420f, y, biasa)
                y += 22f
            }
        }

        y += 24f
        canvas.drawText("Mengetahui,", 380f, y, biasa); y += 16f
        canvas.drawText(pembuat.ifBlank { "......................" }, 360f, y, biasa)

        dokumen.finishPage(halaman)

        val berkas = File(cacheDir, "daftar-hadir-${System.currentTimeMillis()}.pdf")
        try {
            FileOutputStream(berkas).use { dokumen.writeTo(it) }
            cetakMelaluiPrintManager(berkas)
        } catch (e: Exception) {
            Toast.makeText(this, "Gagal membuat PDF: ${e.message}", Toast.LENGTH_LONG).show()
        } finally {
            dokumen.close()
        }
    }

    private fun cetakMelaluiPrintManager(berkas: File) {
        val printManager = getSystemService(Context.PRINT_SERVICE) as PrintManager
        printManager.print(
            "Daftar Hadir",
            object : PrintDocumentAdapter() {
                override fun onLayout(
                    old: PrintAttributes?,
                    baru: PrintAttributes,
                    cancellation: android.os.CancellationSignal?,
                    callback: LayoutResultCallback,
                    extras: android.os.Bundle?,
                ) {
                    if (cancellation?.isCanceled == true) {
                        callback.onLayoutCancelled()
                        return
                    }
                    callback.onLayoutFinished(
                        PrintDocumentInfo.Builder(berkas.name)
                            .setContentType(PrintDocumentInfo.CONTENT_TYPE_DOCUMENT)
                            .build(),
                        true,
                    )
                }

                override fun onWrite(
                    pages: Array<out android.print.PageRange>?,
                    destination: android.os.ParcelFileDescriptor,
                    cancellation: android.os.CancellationSignal?,
                    callback: WriteResultCallback,
                ) {
                    try {
                        berkas.inputStream().use { masuk ->
                            FileOutputStream(destination.fileDescriptor).use { keluar ->
                                masuk.copyTo(keluar)
                            }
                        }
                        callback.onWriteFinished(arrayOf(android.print.PageRange.ALL_PAGES))
                    } catch (e: Exception) {
                        callback.onWriteFailed(e.message)
                    }
                }
            },
            PrintAttributes.Builder().build(),
        )
    }
}
