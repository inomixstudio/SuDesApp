package dev.sumberjaya.sudes.android

import android.content.Context
import android.content.Intent
import android.graphics.Bitmap
import android.graphics.Matrix
import android.graphics.pdf.PdfRenderer
import android.os.Bundle
import android.os.ParcelFileDescriptor
import android.view.MotionEvent
import android.view.ScaleGestureDetector
import android.view.View
import android.widget.Button
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import java.io.File
import java.io.FileInputStream
import kotlin.math.max
import kotlin.math.min

/**
 * Penampil PDF zoomable sebelum cetak: operator meneliti hasil isian
 * (pinch untuk zoom, satu jari untuk menggeser, tombol ‹ › untuk halaman)
 * lalu menekan Cetak untuk memakai dialog cetak Android.
 */
class PdfZoomActivity : AppCompatActivity() {

    private lateinit var tampilan: GambarZoom
    private lateinit var labelHalaman: TextView
    private lateinit var tombolSebelumnya: Button
    private lateinit var tombolBerikutnya: Button

    private var berkasPath: String = ""
    private var judul: String = "PDF"

    private var perender: PdfRenderer? = null
    private var deskriptor: ParcelFileDescriptor? = null
    private var jumlahHalaman = 0
    private var halaman = 0

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        berkasPath = intent.getStringExtra(EXTRA_BERKAS).orEmpty()
        judul = intent.getStringExtra(EXTRA_JUDUL).orEmpty().ifBlank { "PDF" }
        title = judul

        val akar = FrameLayout(this)
        tampilan = GambarZoom(this)
        akar.addView(tampilan, FrameLayout.LayoutParams(
            FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT))

        val bilahBawah = android.widget.LinearLayout(this).apply {
            orientation = android.widget.LinearLayout.HORIZONTAL
            setPadding(Ui.dp(this@PdfZoomActivity, 12), Ui.dp(this@PdfZoomActivity, 8),
                Ui.dp(this@PdfZoomActivity, 12), Ui.dp(this@PdfZoomActivity, 8))
            background = null
        }
        tombolSebelumnya = Button(this).apply {
            text = "‹"
            textSize = 20f
            isAllCaps = false
            setOnClickListener { tampilkanHalaman(halaman - 1) }
        }
        tombolBerikutnya = Button(this).apply {
            text = "›"
            textSize = 20f
            isAllCaps = false
            setOnClickListener { tampilkanHalaman(halaman + 1) }
        }
        labelHalaman = TextView(this).apply { textSize = 13f; gravity = android.view.Gravity.CENTER }
        val tombolCetak = Button(this).apply {
            text = getString(R.string.pdf_cetak_sekarang)
            textSize = 14f
            isAllCaps = false
            setOnClickListener { cetakSekarang() }
        }
        bilahBawah.addView(tombolSebelumnya)
        bilahBawah.addView(labelHalaman, android.widget.LinearLayout.LayoutParams(
            0, android.widget.LinearLayout.LayoutParams.WRAP_CONTENT, 1f))
        bilahBawah.addView(tombolBerikutnya)
        bilahBawah.addView(tombolCetak)

        val induk = android.widget.LinearLayout(this).apply {
            orientation = android.widget.LinearLayout.VERTICAL
            addView(akar, android.widget.LinearLayout.LayoutParams(
                android.widget.LinearLayout.LayoutParams.MATCH_PARENT,
                android.widget.LinearLayout.LayoutParams.WRAP_CONTENT, 1f))
            addView(bilahBawah)
        }
        setContentView(induk)

        bukaBerkas()
    }

    private fun bukaBerkas() {
        try {
            val berkas = File(berkasPath)
            if (!berkas.exists()) {
                Toast.makeText(this, getString(R.string.pdf_tidak_ditemukan), Toast.LENGTH_LONG).show()
                finish()
                return
            }
            deskriptor = ParcelFileDescriptor.open(berkas, ParcelFileDescriptor.MODE_READ_ONLY)
            perender = PdfRenderer(deskriptor!!)
            jumlahHalaman = perender!!.pageCount
            tampilkanHalaman(0)
        } catch (e: Exception) {
            Toast.makeText(
                this,
                getString(R.string.pdf_gagal, e.message ?: "PdfRenderer"),
                Toast.LENGTH_LONG,
            ).show()
            finish()
        }
    }

    private fun tampilkanHalaman(halamanBaru: Int) {
        val renderer = perender ?: return
        val tujuan = halamanBaru.coerceIn(0, jumlahHalaman - 1)
        val page = renderer.openPage(tujuan)
        try {
            val skala = 2.0f
            val bitmap = Bitmap.createBitmap(
                (page.width * skala).toInt(), (page.height * skala).toInt(),
                Bitmap.Config.ARGB_8888)
            // Mode render HARUS konstanta resmi (1 = FOR_DISPLAY, 2 = FOR_PRINT).
            // Nilai mentah 0 melempar IllegalArgumentException("Unsupported
            // render mode") — itulah error yang muncul saat membuka/cetak PDF.
            page.render(bitmap, null, null, PdfRenderer.Page.RENDER_MODE_FOR_DISPLAY)
            tampilan.setBitmap(bitmap, page.width.toFloat(), page.height.toFloat())
            halaman = tujuan
            labelHalaman.text = getString(R.string.pdf_halaman_dari, halaman + 1, jumlahHalaman)
            tombolSebelumnya.isEnabled = halaman > 0
            tombolBerikutnya.isEnabled = halaman < jumlahHalaman - 1
        } finally {
            page.close()
        }
    }

    private fun cetakSekarang() {
        val berkas = File(berkasPath)
        if (berkas.exists()) PdfAksi.cetak(this, berkas, judul)
    }

    override fun onDestroy() {
        try { perender?.close() } catch (e: Exception) {
            // sudah ditutup
        }
        try { deskriptor?.close() } catch (e: Exception) {
            // sudah ditutup
        }
        super.onDestroy()
    }

    companion object {
        const val EXTRA_BERKAS = "pdf_berkas"
        const val EXTRA_JUDUL = "pdf_judul"

        fun mulai(ctx: Context, berkas: File, judul: String) {
            ctx.startActivity(Intent(ctx, PdfZoomActivity::class.java).apply {
                putExtra(EXTRA_BERKAS, berkas.absolutePath)
                putExtra(EXTRA_JUDUL, judul)
            })
        }
    }
}

/** ImageView dengan pinch-zoom (1..5x) dan geser satu jari. */
private class GambarZoom(ctx: Context) : ImageView(ctx) {
    private var skala = 1f
    private var dx = 0f
    private var dy = 0f
    private var layarW = 0f
    private var layarH = 0f
    private var bitmapTampil: Bitmap? = null

    private val detektor = ScaleGestureDetector(ctx, object : ScaleGestureDetector.SimpleOnScaleGestureListener() {
        override fun onScale(detektor: ScaleGestureDetector): Boolean {
            val faktor = detektor.scaleFactor
            val fokusX = detektor.focusX
            val fokusY = detektor.focusY
            val skalaBaru = (skala * faktor).coerceIn(1f, 5f)
            if (skalaBaru == skala) return false
            // Geser agar titik fokus tetap di tempat.
            dx = fokusX - (fokusX - dx) * (skalaBaru / skala)
            dy = fokusY - (fokusY - dy) * (skalaBaru / skala)
            skala = skalaBaru
            terapkan()
            return true
        }
    })

    private var turunX = 0f
    private var turunY = 0f
    private var awalDx = 0f
    private var awalDy = 0f

    fun setBitmap(bitmap: Bitmap, lebarAsli: Float, tinggiAsli: Float) {
        val lama = bitmapTampil
        bitmapTampil = bitmap
        setImageBitmap(bitmap)
        lama?.recycle()
        skala = 1f
        dx = 0f
        dy = 0f
        terapkan()
    }

    override fun onSizeChanged(w: Int, h: Int, oldw: Int, oldh: Int) {
        super.onSizeChanged(w, h, oldw, oldh)
        layarW = w.toFloat()
        layarH = h.toFloat()
        terapkan()
    }

    override fun onTouchEvent(event: MotionEvent): Boolean {
        detektor.onTouchEvent(event)
        when (event?.action) {
            MotionEvent.ACTION_DOWN -> {
                turunX = event.x
                turunY = event.y
                awalDx = dx
                awalDy = dy
            }
            MotionEvent.ACTION_MOVE -> {
                if (event.pointerCount == 1) {
                    dx = awalDx + (event.x - turunX)
                    dy = awalDy + (event.y - turunY)
                    terapkan()
                }
            }
        }
        return true
    }

    private fun terapkan() {
        val bitmap = bitmapTampil ?: return
        // Skala pas layar dulu, lalu zoom di atasnya.
        val skalaLayar = min(layarW / bitmap.width, layarH / bitmap.height)
        val w = bitmap.width * skalaLayar * skala
        val h = bitmap.height * skalaLayar * skala
        dx = dx.coerceIn(min(0f, layarW - w), max(0f, layarW - w))
        dy = dy.coerceIn(min(0f, layarH - h), max(0f, layarH - h))
        imageMatrix = Matrix().apply { setScale(skalaLayar * skala, skalaLayar * skala) }
        imageMatrix.postTranslate(dx, dy)
    }
}
