package dev.sumberjaya.sudes.android

import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Typeface
import android.graphics.pdf.PdfDocument
import java.io.File
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/**
 * Membuat PDF satu surat.
 *
 * Tata letak mengikuti adat surat desa: kop tiga baris, nomor di kanan,
 * judul di tengah, isian berlabel, lalu penutup dan blok tanda tangan.
 * PDF ini DRAF — yang resmi tetap dibuat desktop dari register, karena di
 * desktop saja data warga lengkap dan nomornya mengikuti register.
 */
object SuratPdf {
    private const val LEBAR = 595 // A4 @72dpi
    private const val TINGGI = 842
    private const val MARGIN = 48f
    private const val BATAS_BAWAH = TINGGI - MARGIN

    private fun catatan(ukuran: Float, tebal: Boolean = false, rati: Paint.Align = Paint.Align.LEFT) =
        Paint().apply {
            color = Color.BLACK
            textSize = ukuran
            typeface = if (tebal) Typeface.create(Typeface.SANS_SERIF, Typeface.BOLD) else Typeface.SANS_SERIF
            textAlign = rati
            isAntiAlias = true
        }

    /**
     * @param isi daftar pasangan label→nilai untuk isian surat. Baris bernilai
     *           kosong dilewati supaya surat tidak penuh kolom kosong.
     */
    fun buat(
        ctx: Context,
        jenis: SkemaJenis,
        nomor: String,
        isi: List<Pair<String, String>>,
        pengaturan: PengaturanSurat,
        pejabat: String = "",
    ): File {
        val dokumen = PdfDocument()
        val p = Penulis(dokumen, pengaturan)
        val tanggal = SimpleDateFormat("d MMMM yyyy", Locale("id", "ID")).format(Date())

        p.kop()
        p.nomor(nomor)
        p.judul(jenis.label)
        p.lewati(10f)
        p.garis("Yang bertanda tangan di bawah ini menerangkan dengan sesungguhnya bahwa:", p.body)
        p.lewati(6f)

        for ((label, nilai) in isi) {
            if (nilai.isBlank()) continue

            if (p.y > BATAS_BAWAH - 70f) p.halamanBaru()

            p.lewati(8f)
            val labelLebar = p.label.measureText("$label:") + 10f
            p.canvas.drawText("$label:", MARGIN, p.y, p.label)
            p.garis(nilai, p.body, xKiri = MARGIN + labelLebar)
        }

        p.lewati(12f)
        if (p.y > BATAS_BAWAH - 90f) p.halamanBaru()
        p.garis("Demikian surat keterangan ini dibuat dengan sesungguhnya untuk dapat dipergunakan seperlunya.", p.body)
        p.lewati(8f)
        p.garis("Tanggal terbit: $tanggal", p.body)

        // Blok tanda tangan: tempat+ tanggal, jabatan, lalu titik-titik untuk
        // nama yang diisi tangan — nama pejabat tidak tersimpan di perangkat.
        if (p.y > BATAS_BAWAH - 90f) p.halamanBaru()
        p.lewati(24f)
        p.garis("${pengaturan.namaDesa.ifBlank { "...." }}, $tanggal", p.body, Paint.Align.RIGHT)
        p.lewati(16f)
        p.garis(pejabat.ifBlank { "Kepala Desa" }, p.body, Paint.Align.RIGHT)
        p.lewati(46f)
        p.garis("( ........................................... )", p.body, Paint.Align.RIGHT)

        p.selesai()

        val berkas = File(File(ctx.cacheDir, "surat").apply { mkdirs() }, "surat-${System.currentTimeMillis()}.pdf")
        berkas.outputStream().use { dokumen.writeTo(it) }
        dokumen.close()
        return berkas
    }

    private class Penulis(
        private val dokumen: PdfDocument,
        private val pengaturan: PengaturanSurat,
    ) {
        private var halamanIni = dokumen.startPage(
            PdfDocument.PageInfo.Builder(LEBAR, TINGGI, 1).create()
        )
        var canvas: Canvas = halamanIni.canvas
        var y = MARGIN
        private var nomorHalaman = 1

        val kop = catatan(13f, tebal = true, rati = Paint.Align.CENTER)
        val judulPaint = catatan(14f, tebal = true, rati = Paint.Align.CENTER)
        val body = catatan(12f)
        val label = catatan(12f, tebal = true)

        fun kop() {
            for (baris in pengaturan.barisKop()) {
                canvas.drawText(baris.uppercase(Locale("id", "ID")), LEBAR / 2f, y, kop)
                y += 16f
            }
            y += 6f
            canvas.drawLine(MARGIN, y, LEBAR - MARGIN, y, Paint().apply {
                color = Color.BLACK; strokeWidth = 2f
            })
            y += 20f
        }

        fun nomor(nomor: String) {
            val teks = "Nomor: $nomor"
            canvas.drawText(teks, LEBAR - MARGIN - body.measureText(teks), y, body)
            y += 18f
        }

        fun judul(judul: String) {
            val teks = judul.uppercase(Locale("id", "ID"))
            canvas.drawText(teks, LEBAR / 2f, y, judulPaint)
            y += 20f
            canvas.drawLine(LEBAR / 2f - 60f, y, LEBAR / 2f + 60f, y, Paint().apply {
                color = Color.BLACK; strokeWidth = 1f
            })
            y += 16f
        }

        /** Gambar teks dengan lipat baris otomatis, mulai dari [xKiri]. */
        fun garis(teks: String, paint: Paint, rati: Paint.Align = Paint.Align.LEFT, xKiri: Float = MARGIN) {
            val kanan = LEBAR - MARGIN
            val lebarMaks = kanan - xKiri

            val baris = bagi(teks, paint, lebarMaks)
            var yy = y
            for (b in baris) {
                val x = when (rati) {
                    Paint.Align.RIGHT -> kanan - paint.measureText(b)
                    Paint.Align.CENTER -> (xKiri + kanan - paint.measureText(b)) / 2f
                    else -> xKiri
                }
                canvas.drawText(b, x, yy, paint)
                yy += paint.textSize * 1.35f
            }
            y = yy
        }

        private fun bagi(teks: String, paint: Paint, lebarMaks: Float): List<String> {
            val hasil = mutableListOf<String>()
            var baris = StringBuilder()

            for (kata in teks.split(' ')) {
                val uji = if (baris.isEmpty()) kata else "$baris $kata"
                if (paint.measureText(uji) > lebarMaks && baris.isNotEmpty()) {
                    hasil.add(baris.toString())
                    baris = StringBuilder(kata)
                } else {
                    baris = StringBuilder(uji)
                }
            }
            if (baris.isNotEmpty()) hasil.add(baris.toString())
            return hasil
        }

        fun lewati(jumlah: Float) { y += jumlah }

        fun halamanBaru() {
            dokumen.finishPage(halamanIni)
            nomorHalaman += 1
            halamanIni = dokumen.startPage(
                PdfDocument.PageInfo.Builder(LEBAR, TINGGI, nomorHalaman).create()
            )
            canvas = halamanIni.canvas
            y = MARGIN
        }

        fun selesai() {
            dokumen.finishPage(halamanIni)
        }
    }
}
