package dev.sumberjaya.sudes.android

import android.content.Context
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Typeface
import android.graphics.pdf.PdfDocument
import java.io.ByteArrayOutputStream
import java.io.File
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/**
 * Membuat PDF satu surat dengan tata letak yang SAMA dengan desktop.
 *
 * Seluruh angka di kelas ini dicopy dari sisi desktop supaya hasil cetak Android
 * tidak bergeser walau isiannya sama:
 *   - margin & ukuran kertas  -> TataLetakHalamanSurat + PengaturanCetak
 *   - kerapatan               -> KerapatanSurat (Normal → Paling rapat)
 *   - kop, judul, tabel, kaki -> KopSurat + SuratRenderer
 *   - font                    -> Times New Roman (Resources/Fonts/times*.ttf)
 *
 * PDF ini DRAF: yang resmi tetap dibuat desktop dari register, karena di desktop
 * saja data warga lengkap dan nomornya mengikuti register.
 */
object SuratPdf {

    // ------------------------------------------------------------------
    // Konstanta desktop
    // ------------------------------------------------------------------
    const val MARGIN_SISI = 45f
    const val UKURAN_TEKS = 12f
    const val UKURAN_JUDUL = 14f
    const val JARAK_BARIS_SEL = 15f
    const val TINGGI_BARIS = UKURAN_TEKS * 1.2f          // 14,4 pt
    const val LEBAR_LOGO = 60f
    const val FONT_KABUPATEN = 16f
    const val FONT_KECAMATAN = 18f
    const val FONT_NAMA_DESA = 22f
    const val FONT_ALAMAT = 9f
    const val FONT_KABUPATEN_BAWAH = 9f
    const val GARIS_TIPIS = 0.8f
    const val GARIS_TEBAL = 1.5f
    const val WARNA_SUREL = 0xFF1565C0.toInt()
    const val JARAK_BAWAH_NOMOR = 15f

    /** Lebar kertas A4 dalam point (72 pt = 1 inci). */
    const val A4_LEBAR = 595.28f
    const val A4_TINGGI = 841.89f
    /** F4/Folio — 8,5 x 13 inci. */
    const val F4_LEBAR = 607.5f
    const val F4_TINGGI = 935.4f

    /** Rasio kolom tabel isian desktop: 160 : 10 : 340. */
    const val RASIO_LABEL = 160f
    const val RASIO_TITIK = 10f
    const val RASIO_NILAI = 340f

    /** Satu tingkat kerapatan — angka sama dengan KerapatanSurat di desktop. */
    data class Kerapatan(
        val faktorLeading: Float,
        val leadingSelTabel: Float,
        val paddingSelTabel: Float,
        val barisRuangTandaTangan: Float,
        val faktorJarakBlok: Float,
        val marginAtas: Float,
        val marginBawah: Float,
        val jarakKopJudul: Float,
    )

    private val KERAPATAN = listOf(
        Kerapatan(1.20f, 15f, 1f, 4f, 1.00f, 36f, 30f, 14f),
        Kerapatan(1.12f, 13.8f, 0.5f, 3f, 0.70f, 32f, 24f, 12f),
        Kerapatan(1.07f, 13.2f, 0.35f, 2.75f, 0.55f, 28f, 20f, 10f),
        Kerapatan(1.03f, 12.6f, 0.2f, 2.5f, 0.40f, 24f, 18f, 8f),
    )

    // ------------------------------------------------------------------
    // Masuk
    // ------------------------------------------------------------------

    /**
     * @param nilai peta kunci→teks. Kunci yang dipakai badan surat:
     *        `warga.<key bidang>`, `pejabat.nama`, `pejabat.jabatan`,
     *        `keterangan`, `warga.alamat`, `warga.tempatTanggalLahir`.
     * @param logo gambar logo kop (null = kop tanpa logo).
     */
    fun buat(
        ctx: Context,
        jenis: SkemaJenis,
        nomor: String,
        nilai: Map<String, String>,
        pengaturan: PengaturanSurat,
        pejabat: String,
        logo: Bitmap? = null,
    ): File {
        val font = FontSet.muat(ctx)
        val lebar: Float
        val tinggi: Float
        if (pengaturan.ukuranKertas.equals("F4", ignoreCase = true)) {
            lebar = F4_LEBAR
            tinggi = F4_TINGGI
        } else {
            lebar = A4_LEBAR
            tinggi = A4_TINGGI
        }

        // Coba tampilan lapang dulu; rapatkan hanya bila isinya melimpah.
        var terakhir: ByteArray = ByteArray(0)
        for (kerapatan in KERAPATAN) {
            val perender = Perender(kerapatan, lebar, tinggi, font)
            perender.susun(jenis, nomor, nilai, pengaturan, pejabat, logo)
            terakhir = perender.hasil()
            if (perender.halaman <= 1) break
        }

        val berkas = File(
            File(ctx.cacheDir, "surat").apply { mkdirs() },
            "surat-${System.currentTimeMillis()}.pdf",
        )
        berkas.writeBytes(terakhir)
        return berkas
    }

    // ------------------------------------------------------------------
    // Penyusunan
    // ------------------------------------------------------------------

    private class Perender(
        private val kerapatan: Kerapatan,
        private val lebar: Float,
        private val tinggi: Float,
        private val font: FontSet,
    ) {
        private val dokumen = PdfDocument()
        private var halamanIni = dokumen.startPage(
            PdfDocument.PageInfo.Builder(lebar.toInt(), tinggi.toInt(), 1).create()
        )
        private var kanvas: Canvas = halamanIni.canvas
        private var y = kerapatan.marginAtas
        private var nomorHalaman = 1

        val halaman: Int get() = nomorHalaman

        fun hasil(): ByteArray {
            dokumen.finishPage(halamanIni)
            val keluaran = ByteArrayOutputStream()
            dokumen.writeTo(keluaran)
            dokumen.close()
            return keluaran.toByteArray()
        }

        // ---- potongan surat ----

        fun susun(
            jenis: SkemaJenis,
            nomor: String,
            nilai: Map<String, String>,
            pengaturan: PengaturanSurat,
            pejabat: String,
            logo: Bitmap?,
        ) {
            val tanggal = SimpleDateFormat("dd/MM/yyyy", Locale("id", "ID")).format(Date())

            kop(pengaturan, if (jenis.pakaiLogo) logo else null)
            if (!jenis.halamanPenuh) {
                lewati(kerapatan.jarakKopJudul * kerapatan.faktorJarakBlok)
                judul(jenis.judulCetak, nomor)
            }

            for (blok in jenis.badan) {
                when (blok) {
                    is BlokBadan.Paragraf -> paragraf(blok, nilai)
                    is BlokBadan.ParagrafCampur -> paragrafCampur(blok, nilai)
                    is BlokBadan.Tabel -> tabel(blok, nilai)
                    is BlokBadan.Daftar -> daftar(blok, nilai)
                    is BlokBadan.Kustom -> paragraf(
                        BlokBadan.Paragraf(
                            teks = "[${blok.deskripsi}]",
                            rata = RataBlok.KIRI,
                            miring = true,
                        ),
                        nilai,
                    )
                }
            }

            // Blok tanda tangan: pemohon di kiri (bila template menghendaki),
            // pejabat di kanan — sama seperti SuratRenderer.TandaTangan.
            // Jenis halaman-penuh sudah menyertakannya di 'badan'.
            if (!jenis.halamanPenuh) {
                lewati(10f * kerapatan.faktorJarakBlok)
                tandaTangan(jenis, nilai, pengaturan, pejabat, tanggal)
            }
        }

        // ---- kop ----

        private fun kop(p: PengaturanSurat, logo: Bitmap?) {
            val baris = Kop.baris(p)
            val kolomTeks = lebar - MARGIN_SISI * 2 - LEBAR_LOGO
            val xTeks = MARGIN_SISI + LEBAR_LOGO
            val yMulai = y
            var yTeks = y

            for (barisKop in baris) {
                val paint = font.paint(barisKop.ukuran, barisKop.tebal)
                val x = xTeks + (kolomTeks - paint.measureText(barisKop.teks)) / 2f
                kanvas.drawText(barisKop.teks, x, yTeks + barisKop.ukuran, paint)
                if (barisKop.surel.isNotBlank()) {
                    val paintSurel = font.paint(barisKop.ukuran, barisKop.tebal, warna = WARNA_SUREL)
                    val xSurel = xTeks + (kolomTeks - paintSurel.measureText(barisKop.surel)) / 2f
                    kanvas.drawText(barisKop.surel, xSurel, yTeks + barisKop.ukuran, paintSurel)
                }
                yTeks += barisKop.ukuran * 1.15f
            }

            if (logo != null) {
                val tinggiLogo = (yTeks - yMulai).coerceAtMost(LEBAR_LOGO * 1.3f)
                kanvas.drawBitmap(
                    logo, null,
                    android.graphics.RectF(MARGIN_SISI, yMulai, MARGIN_SISI + LEBAR_LOGO, yMulai + tinggiLogo),
                    null,
                )
            }

            y = yTeks + 3f
            garisHorisontal(MARGIN_SISI, lebar - MARGIN_SISI, y, GARIS_TIPIS)
            y += 1f
            garisHorisontal(MARGIN_SISI, lebar - MARGIN_SISI, y, GARIS_TEBAL)
        }

        private fun garisHorisontal(x1: Float, x2: Float, yy: Float, tebal: Float) {
            kanvas.drawLine(x1, yy, x2, yy, Paint().apply {
                color = Color.BLACK
                strokeWidth = tebal
                isAntiAlias = true
            })
        }

        // ---- judul + nomor ----

        private fun judul(judul: String, nomor: String) {
            val teks = judul.uppercase(Locale("id", "ID"))
            val paint = font.paint(UKURAN_JUDUL, tebal = true)
            kanvas.drawText(teks, (lebar - paint.measureText(teks)) / 2f, y + UKURAN_JUDUL, paint)

            // Garis bawah judul mengikuti lebar teksnya.
            val lebarTeks = paint.measureText(teks)
            garisHorisontal((lebar - lebarTeks) / 2f, (lebar + lebarTeks) / 2f, y + UKURAN_JUDUL + 2f, 1f)
            y += UKURAN_JUDUL + 4f

            if (nomor.isNotBlank()) {
                val barisNomor = "NOMOR : $nomor"
                val paintNomor = font.paint(UKURAN_TEKS)
                kanvas.drawText(barisNomor, (lebar - paintNomor.measureText(barisNomor)) / 2f, y + UKURAN_TEKS, paintNomor)
                y += UKURAN_TEKS + 2f
            }

            lewati(JARAK_BAWAH_NOMOR * kerapatan.faktorJarakBlok)
        }

        // ---- paragraf ----

        private fun paragraf(blok: BlokBadan.Paragraf, nilai: Map<String, String>) {
            if (blok.jarakAtas > 0f) lewati(blok.jarakAtas * kerapatan.faktorJarakBlok)
            val teks = urai(blok.teks, nilai)
            if (teks.isNotBlank()) {
                gambarParagraf(teks, font.paint(UKURAN_TEKS, blok.tebal, blok.miring), blok.rata, blok.indent, blok.indentKiri)
            }
            if (blok.jarakBawah > 0f) lewati(blok.jarakBawah * kerapatan.faktorJarakBlok)
        }

        private fun paragrafCampur(blok: BlokBadan.ParagrafCampur, nilai: Map<String, String>) {
            if (blok.jarakAtas > 0f) lewati(blok.jarakAtas * kerapatan.faktorJarakBlok)

            // Setiap potongan bisa tebal sendiri; lipat per baris seperti paragraf biasa.
            val kata = blok.bagian.flatMap { (teks, tebal) ->
                urai(teks, nilai).split(' ').filter { it.isNotEmpty() }.map { it to tebal }
            }
            // Lebar dasar harus sama dengan paragraf biasa: dikurangi indentKiri
            // saja; indent baris pertama dikurangi per baris (lebarMaksBaris).
            val lebarMaks = lebar - MARGIN_SISI * 2 - blok.indentKiri
            var barisSekarang = mutableListOf<Pair<String, Boolean>>()
            var lebarSekarang = 0f
            var barisKe = 0

            for (k in kata) {
                val paint = font.paint(UKURAN_TEKS, k.second)
                val tambah = if (barisSekarang.isEmpty()) paint.measureText(k.first)
                else paint.measureText(" ${k.first}")
                val batas = lebarMaksBaris(barisKe, lebarMaks, blok.indent)
                if (lebarSekarang + tambah > batas && barisSekarang.isNotEmpty()) {
                    gambarBarisCampur(barisSekarang, blok.rata, blok.indent, blok.indentKiri, barisKe == 0, terakhir = false)
                    barisKe += 1
                    barisSekarang = mutableListOf()
                    lebarSekarang = 0f
                }
                barisSekarang.add(k)
                lebarSekarang += tambah
            }
            if (barisSekarang.isNotEmpty()) {
                gambarBarisCampur(barisSekarang, blok.rata, blok.indent, blok.indentKiri, barisKe == 0, terakhir = true)
            }

            if (blok.jarakBawah > 0f) lewati(blok.jarakBawah * kerapatan.faktorJarakBlok)
        }

        private fun gambarBarisCampur(
            kata: List<Pair<String, Boolean>>,
            rata: RataBlok,
            indent: Float,
            indentKiri: Float,
            barisPertama: Boolean,
            terakhir: Boolean,
        ) {
            val spasi = font.paint(UKURAN_TEKS).measureText(" ")
            var total = 0f
            for ((k, tebal) in kata) total += font.paint(UKURAN_TEKS, tebal).measureText(k)
            total += spasi * (kata.size - 1).coerceAtLeast(0)
            // Baris pertama dikurangi indent pertama supaya tidak menjulur ke
            // margin kanan; baris lainnya memakai lebar penuh.
            val lebarMaks = lebarMaksBaris(if (barisPertama) 0 else 1, lebar - MARGIN_SISI * 2 - indentKiri, indent)
            val xAwal = MARGIN_SISI + indentKiri + if (barisPertama) indent else 0f

            // Baris terakhir tidak di-rata-kanankan (sesuai desktop Justify:
            // baris pendek penutup tetap rata kiri).
            if (rata == RataBlok.JUSTIFY && kata.size > 1 && !terakhir) {
                val sisa = (lebarMaks - total) / (kata.size - 1)
                var xx = xAwal
                for ((k, tebal) in kata) {
                    val paint = font.paint(UKURAN_TEKS, tebal)
                    kanvas.drawText(k, xx, y + UKURAN_TEKS, paint)
                    xx += paint.measureText(k) + spasi + sisa
                }
            } else {
                val xMulai = when (rata) {
                    RataBlok.KANAN -> lebar - MARGIN_SISI - total
                    RataBlok.TENGAH -> MARGIN_SISI + indentKiri + (lebarMaks - total) / 2f
                    else -> MARGIN_SISI + indentKiri + indent
                }
                var xx = xMulai
                for ((k, tebal) in kata) {
                    val paint = font.paint(UKURAN_TEKS, tebal)
                    kanvas.drawText(k, xx, y + UKURAN_TEKS, paint)
                    xx += paint.measureText(k) + spasi
                }
            }
            y += UKURAN_TEKS * kerapatan.faktorLeading
        }

        /** Gambar paragraf dengan lipat baris; baris pertama boleh menjorok. Baris JUSTIFY kecuali terakhir di-rata-kanankan. */
        private fun gambarParagraf(
            teks: String,
            paint: Paint,
            rata: RataBlok,
            indent: Float,
            indentKiri: Float,
        ) {
            // Baris pertama memakai lebar tercetak dikurangi indent pertama;
            // baris berikutnya lebar penuh. Ini menjaga baris pertama (mis.
            // keterangan indent 50pt) tetap di dalam margin kanan 45pt.
            val lebarPenuh = lebar - MARGIN_SISI * 2 - indentKiri
            val baris = lipatIndentasi(teks, paint, lebarPenuh, indent)
            for ((i, b) in baris.withIndex()) {
                if (y + UKURAN_TEKS > tinggi - kerapatan.marginBawah) halamanBaru()
                val kata = b.split(' ').filter { it.isNotEmpty() }
                val xMulai = MARGIN_SISI + indentKiri + if (i == 0) indent else 0f
                if (rata == RataBlok.JUSTIFY && i != baris.lastIndex && kata.size > 1) {
                    // Rata kanan: sebar spasi antar-kata sampai tepi teks penuh.
                    var total = 0f
                    for (k in kata) total += paint.measureText(k)
                    val spasi = paint.measureText(" ")
                    total += spasi * (kata.size - 1)
                    val sisa = (lebarMaksBaris(i, lebarPenuh, indent) - total) / (kata.size - 1)
                    var xx = xMulai + if (i == 0) 0f else 0f
                    for (k in kata) {
                        kanvas.drawText(k, xx, y + UKURAN_TEKS, paint)
                        xx += paint.measureText(k) + spasi + sisa
                    }
                } else {
                    val lebarBaris = paint.measureText(b)
                    val x = when (rata) {
                        RataBlok.KANAN -> lebar - MARGIN_SISI - lebarBaris
                        RataBlok.TENGAH -> MARGIN_SISI + indentKiri + (lebarMaksBaris(i, lebarPenuh, indent) - lebarBaris) / 2f
                        else -> xMulai
                    }
                    kanvas.drawText(b, x, y + UKURAN_TEKS, paint)
                }
                y += UKURAN_TEKS * kerapatan.faktorLeading
            }
        }

        /** Lebar teks baris ke-[i]: baris pertama dikurangi indent pertama. */
        private fun lebarMaksBaris(i: Int, lebarPenuh: Float, indent: Float): Float =
            if (i == 0) (lebarPenuh - indent).coerceAtLeast(40f) else lebarPenuh

        private fun lipatIndentasi(teks: String, paint: Paint, lebarPenuh: Float, indent: Float): List<String> {
            val hasil = mutableListOf<String>()
            var baris = StringBuilder()
            for (kata in teks.split(' ')) {
                val uji = if (baris.isEmpty()) kata else "$baris $kata"
                val batas = lebarMaksBaris(hasil.size, lebarPenuh, indent)
                if (paint.measureText(uji) > batas && baris.isNotEmpty()) {
                    hasil.add(baris.toString())
                    baris = StringBuilder(kata)
                } else {
                    baris = StringBuilder(uji)
                }
            }
            if (baris.isNotEmpty()) hasil.add(baris.toString())
            // Baris tunggal yang panjangnya melebihi batas tetap dipecah di sini;
            // baris pendek dibiarkan apa adanya supaya lisip tetap persis.
            return hasil
        }

        private fun lipat(teks: String, paint: Paint, lebarMaks: Float): List<String> {
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

        // ---- tabel isian ----

        private fun tabel(blok: BlokBadan.Tabel, nilai: Map<String, String>) {
            if (blok.jarakAtas > 0f) lewati(blok.jarakAtas * kerapatan.faktorJarakBlok)

            val lebarTabel = lebar - MARGIN_SISI * 2 - blok.indentKiri
            val totalRasio = RASIO_LABEL + RASIO_TITIK + RASIO_NILAI
            val lebarLabel = lebarTabel * RASIO_LABEL / totalRasio
            val lebarTitik = lebarTabel * RASIO_TITIK / totalRasio
            val xLabel = MARGIN_SISI + blok.indentKiri
            val xTitik = xLabel + lebarLabel
            val xNilai = xTitik + lebarTitik
            val lebarNilai = lebarTabel - lebarLabel - lebarTitik
            val paintLabel = font.paint(UKURAN_TEKS)

            for ((label, templatNilai) in blok.baris) {
                val isi = urai(templatNilai, nilai)
                val tebalkan = blok.tebalkanNama && label.equals("Nama", ignoreCase = true)
                // Baris "Nama" mengikuti template resmi: isinya KAPITAL dan tebal.
                val teksIsi = if (tebalkan) isi.uppercase(Locale("id", "ID")) else isi
                val paintNilai = font.paint(UKURAN_TEKS, tebalkan)
                val baris = lipat(teksIsi.ifBlank { "...................." }, paintNilai, lebarNilai)

                for ((i, b) in baris.withIndex()) {
                    if (y + kerapatan.leadingSelTabel > tinggi - kerapatan.marginBawah) halamanBaru()
                    if (i == 0) {
                        kanvas.drawText(label, xLabel, y + UKURAN_TEKS, paintLabel)
                        kanvas.drawText(":", xTitik, y + UKURAN_TEKS, paintLabel)
                    }
                    kanvas.drawText(b, xNilai, y + UKURAN_TEKS, paintNilai)
                    y += kerapatan.leadingSelTabel
                }
            }

            if (blok.jarakBawah > 0f) lewati(blok.jarakBawah * kerapatan.faktorJarakBlok)
        }

        // ---- daftar bernomor ----

        private fun daftar(blok: BlokBadan.Daftar, nilai: Map<String, String>) {
            if (blok.jarakAtas > 0f) lewati(blok.jarakAtas * kerapatan.faktorJarakBlok)
            val paint = font.paint(UKURAN_TEKS)
            val lebarNomor = 20f
            val xKiri = MARGIN_SISI + blok.indentKiri

            for ((i, butir) in blok.butir.withIndex()) {
                val teks = urai(butir, nilai)
                val baris = lipat(teks, paint, lebar - xKiri - lebarNomor - MARGIN_SISI)
                for ((j, b) in baris.withIndex()) {
                    if (j == 0) kanvas.drawText("${i + 1}.", xKiri, y + UKURAN_TEKS, paint)
                    kanvas.drawText(b, xKiri + lebarNomor, y + UKURAN_TEKS, paint)
                    y += UKURAN_TEKS * kerapatan.faktorLeading
                }
                lewati(3f * kerapatan.faktorJarakBlok)
            }
            if (blok.jarakBawah > 0f) lewati(blok.jarakBawah * kerapatan.faktorJarakBlok)
        }

        // ---- tanda tangan ----

        private fun tandaTangan(
            jenis: SkemaJenis,
            nilai: Map<String, String>,
            p: PengaturanSurat,
            pejabat: String,
            tanggal: String,
        ) {
            val namaDesa = p.namaDesa.ifBlank { "...." }
            val jabatan = Kop.jabatan(p, pejabat)
            val namaPejabat = nilai["pejabat.nama"].orBlank().ifBlank {
                p.kepalaDesa.ifBlank { "_______________________" }
            }
            val nip = p.nipKepalaDesa.orBlank()

            val setengah = (lebar - MARGIN_SISI * 2) / 2f
            val xKiri = MARGIN_SISI
            val xKanan = MARGIN_SISI + setengah
            val lebarKolom = setengah
            val paint = font.paint(UKURAN_TEKS)
            val paintTebal = font.paint(UKURAN_TEKS, tebal = true)

            // Blok tanda tangan seperti desktop (SuratRenderer.TandaTangan):
            // pemohon di KIRI, Kepala Desa di KANAN, keduanya mulai di y yang
            // sama; nama pemohon disejajarkan dengan nama Kepala Desa.
            val yAwal = y
            var yAkhir = y
            val tampilPemohon = jenis.pemohonDiTandaTangan &&
                !nilai["warga.nama"].isNullOrBlank()

            // Kolom kanan dulu di yAwal: letakkan tempat+tanggal, jabatan,
            // ruang tanda tangan, nama, NIP — catat posisi nama supaya
            // setelah itu kolom kiri disejajarkan.
            teksTengah("$namaDesa, $tanggal", paint, xKanan, lebarKolom)
            for (baris in jabatan.split('\n')) teksTengah(baris, paint, xKanan, lebarKolom)
            lewati(TINGGI_BARIS * kerapatan.barisRuangTandaTangan)
            teksTengah(namaPejabat.uppercase(Locale("id", "ID")), paintTebal, xKanan, lebarKolom)
            if (nip.isNotBlank()) teksTengah("NIP $nip", paint, xKanan, lebarKolom)
            yAkhir = y

            if (tampilPemohon) {
                y = yAwal
                teksTengah("Pemohon :", paint, xKiri, lebarKolom)
                // Pad ruang + satu baris penyeimbang (desktop: RuangTandaTangan
                // + TinggiBaris) supaya nama pemohon duduk SEJAJAR nama kades.
                lewati(TINGGI_BARIS * kerapatan.barisRuangTandaTangan + TINGGI_BARIS)
                teksTengah(
                    nilai["warga.nama"].orBlank().uppercase(Locale("id", "ID")),
                    paintTebal, xKiri, lebarKolom,
                )
                if (yAkhir > y) y = yAkhir
            }
        }

        private fun teksTengah(teks: String, paint: Paint, xKiri: Float, lebar: Float) {
            kanvas.drawText(teks, xKiri + (lebar - paint.measureText(teks)) / 2f, y + UKURAN_TEKS, paint)
            y += UKURAN_TEKS * kerapatan.faktorLeading
        }

        // ---- util ----

        private fun lewati(jumlah: Float) { y += jumlah }

        private fun halamanBaru() {
            dokumen.finishPage(halamanIni)
            nomorHalaman += 1
            halamanIni = dokumen.startPage(
                PdfDocument.PageInfo.Builder(lebar.toInt(), tinggi.toInt(), nomorHalaman).create()
            )
            kanvas = halamanIni.canvas
            y = kerapatan.marginAtas
        }
    }

    // ------------------------------------------------------------------
    // Penyelesaian kunci $kunci
    // ------------------------------------------------------------------

    /** Ganti `$kunci` dengan isi [nilai]; `$$` menjadi `$`. */
    fun urai(templat: String, nilai: Map<String, String>): String {
        val hasil = StringBuilder()
        var i = 0
        while (i < templat.length) {
            val c = templat[i]
            if (c != '$') {
                hasil.append(c)
                i += 1
                continue
            }
            if (i + 1 < templat.length && templat[i + 1] == '$') {
                hasil.append('$')
                i += 2
                continue
            }
            val akhir = (i + 1 until templat.length).firstOrNull { idx ->
                val ch = templat[idx]
                !ch.isLetterOrDigit() && ch != '.' && ch != '_'
            } ?: templat.length
            if (akhir == i + 1) {
                hasil.append('$')
                i += 1
                continue
            }
            hasil.append(nilai[templat.substring(i + 1, akhir)] ?: "")
            i = akhir
        }
        return hasil.toString()
    }
}

/** Times New Roman: reguler, tebal, miring — dimuat sekali dari aset. */
class FontSet private constructor(
    private val reguler: Typeface,
    private val tebal: Typeface,
    private val miring: Typeface,
) {
    fun paint(
        ukuran: Float,
        tebal: Boolean = false,
        miring: Boolean = false,
        warna: Int = Color.BLACK,
    ): Paint = Paint().apply {
        color = warna
        textSize = ukuran
        typeface = when {
            tebal -> this@FontSet.tebal
            miring -> this@FontSet.miring
            else -> this@FontSet.reguler
        }
        isAntiAlias = true
    }

    companion object {
        @Volatile
        private var cache: FontSet? = null

        fun muat(ctx: Context): FontSet {
            cache?.let { return it }
            val reguler = Typeface.createFromAsset(ctx.assets, "fonts/times.ttf")
            val tebal = Typeface.createFromAsset(ctx.assets, "fonts/timesbd.ttf")
            val miring = Typeface.createFromAsset(ctx.assets, "fonts/timesi.ttf")
            return FontSet(reguler, tebal, miring).also { cache = it }
        }
    }
}

/**
 * Isi baris kop — angka dan bentuk sama dengan KopSurat di desktop.
 */
object Kop {
    data class Baris(
        val teks: String,
        val ukuran: Float,
        val tebal: Boolean,
        val surel: String = "",
    )

    fun baris(p: PengaturanSurat): List<Baris> {
        val jenisKab = p.jenisKabupaten.ifBlank { "Kabupaten" }
        val kabupaten = bersihkan(p.kabupaten, "Kabupaten", "Kab.")
        val kecamatan = bersihkan(p.kecamatan, "Kecamatan", "Kec.")
        val desa = bersihkan(p.namaDesa, "Desa")
        val alamat = p.alamat.ifBlank { "[Alamat Kantor Desa]" }
        val kata = p.jenisLembaga.ifBlank { "Desa" }

        val hasil = mutableListOf(
            Baris("PEMERINTAH ${jenisKab.uppercase()} ${kabupaten.uppercase()}", SuratPdf.FONT_KABUPATEN, true),
            Baris("KECAMATAN ${kecamatan.uppercase()}", SuratPdf.FONT_KECAMATAN, true),
            Baris(labelLembaga(p, desa), SuratPdf.FONT_NAMA_DESA, true),
            Baris("Alamat $alamat $kata $desa Kec. $kecamatan Kodepos ${p.kodepos}".trimEnd(), SuratPdf.FONT_ALAMAT, false),
            Baris(kabupaten.uppercase(), SuratPdf.FONT_KABUPATEN_BAWAH, true),
        )
        if (p.email.isNotBlank()) hasil.add(Baris("", SuratPdf.FONT_ALAMAT, false, "Email ${p.email}"))
        return hasil
    }

    fun labelLembaga(p: PengaturanSurat, desa: String): String =
        if (p.jenisLembaga.equals("Kelurahan", ignoreCase = true)) {
            "KELURAHAN ${desa.uppercase()}"
        } else {
            "PEMERINTAH DESA ${desa.uppercase()}"
        }

    /**
     * Jabatan pada blok tanda tangan, huruf besar — sama dengan desktop.
     * Baris "Jabatan" di badan surat TIDAK memakai ini: di sana jabatannya
     * disusun memakai [jabatanBadan] (huruf awal besar, nama desa menyusul).
     */
    fun jabatan(p: PengaturanSurat, pejabat: String): String {
        val kata = kataPejabat(p, pejabat)
        val desa = p.namaDesa.trim()
        return if (kata.equals("Sekretaris Desa", ignoreCase = true)) {
            "A/N KEPALA DESA ${desa.uppercase()}\nSEKRETARIS DESA"
        } else {
            "${kata} ${desa}".uppercase()
        }
    }

    /** Jabatan untuk baris "Jabatan" di badan: huruf awal besar, tanpa kapital semua. */
    fun jabatanBadan(p: PengaturanSurat, pejabat: String): String = kataPejabat(p, pejabat)

    private fun kataPejabat(p: PengaturanSurat, pejabat: String): String = when {
        pejabat.isBlank() || pejabat.equals("Kepala Desa", ignoreCase = true) ->
            if (p.jenisLembaga.equals("Kelurahan", ignoreCase = true)) "Lurah" else "Kepala Desa"
        else -> pejabat.trim()
    }

    private fun bersihkan(nilai: String, kata: String, singkatan: String? = null): String {
        val teks = nilai.trim()
        if (teks.isEmpty()) return ""
        for (awalan in listOfNotNull(kata, singkatan)) {
            if (teks.length <= awalan.length) continue
            if (!teks.startsWith(awalan, ignoreCase = true)) continue
            if (!teks[awalan.length].isWhitespace()) continue
            val sisa = teks.substring(awalan.length + 1).trim()
            if (sisa.isNotEmpty()) return sisa
        }
        return teks
    }
}

private fun String?.orBlank(): String = this ?: ""
