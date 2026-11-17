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
        val ukuranFontLampiran: Float,
        val marginAtas: Float,
        val marginBawah: Float,
        val jarakKopJudul: Float,
    )

    private val KERAPATAN = listOf(
        Kerapatan(1.20f, 15f, 1f, 4f, 1.00f, 10f, 36f, 30f, 14f),
        Kerapatan(1.12f, 13.8f, 0.5f, 3f, 0.70f, 8.5f, 32f, 24f, 12f),
        Kerapatan(1.07f, 13.2f, 0.35f, 2.75f, 0.55f, 7.5f, 28f, 20f, 10f),
        Kerapatan(1.03f, 12.6f, 0.2f, 2.5f, 0.40f, 6.5f, 24f, 18f, 8f),
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

        /** Hasil penilaian blok opsional terakhir (dipakai blok hanyaKosong). */
        private var opsionalTerisi: Boolean = true

        fun susun(
            jenis: SkemaJenis,
            nomor: String,
            nilai: Map<String, String>,
            pengaturan: PengaturanSurat,
            pejabat: String,
            logo: Bitmap?,
        ) {
            // KEPALA halaman: kop surat desa. Jenis halaman-penuh (blanko NTCR,
            // Kematian, Ahli Waris, Rekening Koran) TIDAK memakai kop desktop —
            // yang memang perlu kop meminta lewat kustomId "surat.kop" di badan,
            // jadi kop di sini dilewati supaya tidak tercetak dobel.
            if (!jenis.halamanPenuh) {
                kop(pengaturan, if (jenis.pakaiLogo) logo else null)
                lewati(kerapatan.jarakKopJudul * kerapatan.faktorJarakBlok)
                judul(jenis.judulCetak, nomor)
            }

            // Reset setiap surat: blok opsional pertama selalu dinilai.
            opsionalTerisi = true

            for (blok in jenis.badan) {
                // Kondisi percabangan ala generator desktop: blok "opsional"
                // dicetak hanya bila seluruh kuncinya terisi; blok "hanyaKosong"
                // (pasangan else-nya) hanya bila blok opsional di depannya tidak
                // tercetak.
                when (blok) {
                    is BlokBadan.Paragraf -> if (blok.opsional) opsionalTerisi = kondisiTerisi(listOf(blok.teks), nilai)
                    is BlokBadan.ParagrafCampur -> if (blok.opsional) opsionalTerisi = kondisiTerisi(blok.bagian.map { it.first }, nilai)
                    is BlokBadan.Tabel -> if (blok.opsional) opsionalTerisi = kondisiTerisi(blok.baris.map { it.second }, nilai)
                    is BlokBadan.Daftar -> if (blok.opsional) opsionalTerisi = kondisiTerisi(blok.butir, nilai)
                    else -> {}
                }
                when (blok) {
                    is BlokBadan.Paragraf -> paragraf(blok, nilai)
                    is BlokBadan.ParagrafCampur -> paragrafCampur(blok, nilai)
                    is BlokBadan.Tabel -> tabel(blok, nilai)
                    is BlokBadan.Daftar -> daftar(blok, nilai)
                    is BlokBadan.Kustom -> {
                        // Blok bespoke generator desktop dirender lewat id mesin;
                        // id yang belum ada penrendernya tetap dicetak sebagai
                        // catatan supaya operator melihat ada blok yang hilang.
                        val kelola = kustum(blok.id, jenis, nilai, nomor, pengaturan, pejabat, logo)
                        if (!kelola) {
                            paragraf(
                                BlokBadan.Paragraf(
                                    teks = "[${blok.deskripsi}]",
                                    rata = RataBlok.KIRI,
                                    miring = true,
                                ),
                                nilai,
                            )
                        }
                    }
                }
            }

            // Blok tanda tangan: pemohon di kiri (bila template menghendaki),
            // pejabat di kanan — sama seperti SuratRenderer.TandaTangan.
            // Jenis halaman-penuh sudah menyertakannya di 'badan'.
            if (!jenis.halamanPenuh) {
                lewati(10f * kerapatan.faktorJarakBlok)
                tandaTangan(jenis, nilai, pengaturan, pejabat)
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
            // Blok pasangan else: hanya dicetak bila blok opsional
            // sebelumnya tidak tercetak.
            if (blok.hanyaKosong && opsionalTerisi) return
            if (blok.jarakAtas > 0f) lewati(blok.jarakAtas * kerapatan.faktorJarakBlok)
            val teks = urai(blok.teks, nilai)
            if (teks.isNotBlank()) {
                gambarParagraf(teks, font.paint(UKURAN_TEKS, blok.tebal, blok.miring), blok.rata, blok.indent, blok.indentKiri)
            }
            if (blok.jarakBawah > 0f) lewati(blok.jarakBawah * kerapatan.faktorJarakBlok)
        }

        private fun paragrafCampur(blok: BlokBadan.ParagrafCampur, nilai: Map<String, String>) {
            // Blok pasangan else: hanya dicetak bila blok opsional
            // sebelumnya tidak tercetak.
            if (blok.hanyaKosong && opsionalTerisi) return
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
            // Blok pasangan else: hanya dicetak bila blok opsional
            // sebelumnya tidak tercetak.
            if (blok.hanyaKosong && opsionalTerisi) return
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
                // Baris "Nama" mengikuti template resmi: NAMA POKOKNYA kapital
                // dan tebal; gelar (Dr., S.H., …) tetap apa adanya — perilaku
                // NamaFormatter.ToUpperNama di desktop.
                val teksIsi = if (tebalkan) NamaFormatter.toUpperNama(isi) else isi
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
            // Blok pasangan else: hanya dicetak bila blok opsional
            // sebelumnya tidak tercetak.
            if (blok.hanyaKosong && opsionalTerisi) return
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
        ) {
            val namaDesa = p.namaDesa.ifBlank { "...." }
            val jabatan = Kop.jabatan(p, pejabat)
            val namaPejabat = NamaFormatter.toUpperNama(
                nilai["pejabat.nama"].orBlank().ifBlank { p.kepalaDesa }
                    .ifBlank { "NAMA" })
            // Desktop: nama pejabat kosong tetap dicetak "NAMA" di blok tanda
            // tangan (SuratRenderer.TandaTangan), bukan garis bawah.
            val nip = p.nipKepalaDesa.orBlank()

            val xKiri = MARGIN_SISI
            val xKanan = MARGIN_SISI + setengahHalaman()
            val lebarKolom = setengahHalaman()
            val paint = font.paint(UKURAN_TEKS)
            val paintTebal = font.paint(UKURAN_TEKS, tebal = true)

            // Blok tanda tangan seperti desktop (SuratRenderer.TandaTangan):
            // pemohon di KIRI, Kepala Desa di KANAN, keduanya mulai di y yang
            // sama; nama pemohon disejajarkan dengan nama Kepala Desa.
            val yAwal = y
            var yAkhir = y
            // Desktop (SuratRenderer.TandaTangan): blok pemohon TIDAK hilang bila
            // namanya kosong — dicetak titik-titik supaya kolom kiri tetap sejajar
            // blok pejabat (pratinjau blanko/draft belum terisi).
            val tampilPemohon = jenis.pemohonDiTandaTangan

            // Kolom kanan dulu di yAwal: letakkan tempat+tanggal, jabatan,
            // ruang tanda tangan, nama, NIP — catat posisi nama supaya
            // setelah itu kolom kiri disejajarkan.
            // Bentuk tanggal sama dengan desktop (SuratGeneratorBase):
            // "dd MMMM yyyy" bahasa Indonesia, mis. "01 Januari 1990".
            teksTengah("${namaDesa}, ${TanggalIndonesia.panjang()}", paint, xKanan, lebarKolom)
            for (baris in jabatan.split('\n')) teksTengah(baris, paint, xKanan, lebarKolom)
            lewati(TINGGI_BARIS * kerapatan.barisRuangTandaTangan)
            teksTengah(namaPejabat, paintTebal, xKanan, lebarKolom)
            if (nip.isNotBlank()) teksTengah("NIP $nip", paint, xKanan, lebarKolom)
            yAkhir = y

            if (tampilPemohon) {
                y = yAwal
                teksTengah("Pemohon :", paint, xKiri, lebarKolom)
                // Pad ruang + satu baris penyeimbang (desktop: RuangTandaTangan
                // + UkuranTeks × leading) supaya nama pemohon duduk SEJAJAR nama
                // kades. TinggiBaris tetap meleset pada kerapatan rapat.
                lewati(TINGGI_BARIS * kerapatan.barisRuangTandaTangan + UKURAN_TEKS * kerapatan.faktorLeading)
                teksTengah(
                    NamaFormatter.namaAtauTitik(nilai["warga.nama"]),
                    paintTebal, xKiri, lebarKolom,
                )
                if (yAkhir > y) y = yAkhir
            }
        }

        private fun teksTengah(teks: String, paint: Paint, xKiri: Float, lebar: Float) {
            kanvas.drawText(teks, xKiri + (lebar - paint.measureText(teks)) / 2f, y + UKURAN_TEKS, paint)
            y += UKURAN_TEKS * kerapatan.faktorLeading
        }

        // ------------------------------------------------------------------
        // Blok kustom generator desktop. Setiap angka diambil dari generator
        // desktop yang bersangkutan (NtcrGenerator, KematianGenerator, dst.)
        // supaya PDF Android sepadan dengan PDF resmi desktop; id datang dari
        // skema (kolom "kustumId") yang diterbitkan desktop.
        // ------------------------------------------------------------------

        private fun kustum(
            id: String,
            jenis: SkemaJenis,
            nilai: Map<String, String>,
            nomor: String,
            p: PengaturanSurat,
            pejabat: String,
            logo: Bitmap?,
        ): Boolean = when (id) {
            "ntcr.lampiran" -> { blokLampiran(jenis); true }
            "ntcr.kepalaDesa" -> { blokKepalaDesa(p); true }
            "ntcr.judulFormulir" -> { judul(NtcrKatalog.namaFormulir(jenis.idJenis).ifBlank { jenis.judulCetak }, nomor); true }
            "ntcr.judulTengah" -> {
                // N6 memakai JudulDanNomor (dengan baris NOMOR); N4/N5 murni judul.
                judul(NtcrKatalog.namaFormulir(jenis.idJenis).ifBlank { jenis.judulCetak },
                    if (jenis.idJenis.equals("NTCR_N6", true)) nomor else "")
                true
            }
            "ntcr.judulPermohonan" -> { blokJudulPermohonan(jenis, nomor, p); true }
            "ntcr.ttdKanan" -> { blokTtdKanan(nilai, p, pejabat); true }
            "ntcr.ttdKua" -> { blokTtdKua(nilai); true }
            "ntcr.ttdDuaPihak" -> { blokTtdDuaPihak(jenis, nilai, p); true }
            "surat.kop" -> { kop(p, logo); true }
            "kematian.kode" -> { blokKotakKode(); true }
            "kematian.judul" -> { blokJudulKematian(jenis.judulCetak, nomor); true }
            "kematian.ttd" -> { blokTtdKematian(nilai, p, pejabat); true }
            "garapan.tabel" -> { blokTabelGarapan(nilai); true }
            // Surat Ahli Waris: tiga halaman berurutan (Keterangan + Pernyataan
            // + Kuasa), kop/kaki hanya di halaman pertama — sama seperti
            // AhliWarisGenerator desktop.
            "aw.kop" -> {
                kop(p, if (jenis.pakaiLogo) logo else null)
                lewati(kerapatan.jarakKopJudul * kerapatan.faktorJarakBlok)
                judul(jenis.judulCetak, nomor)
                true
            }
            "aw.kakiH1" -> {
                // Camat di kolom kiri, Kepala Desa di kanan (halaman keterangan).
                val yMulai = y
                tulisKolomTengah(MARGIN_SISI + setengahHalaman(), setengahHalaman(),
                    listOf("Menguatkan;", "Nomor : ...................................", "Camat ${p.kecamatan.ifBlank { "..........." }}"),
                    emptyList(), "..........................", "")
                val yKiri = y; y = yMulai
                tulisKolomTengah(MARGIN_SISI + setengahHalaman(), setengahHalaman(),
                    listOf("${p.namaDesa}, ${TanggalIndonesia.panjang()}"),
                    listOf("${p.jenisLembaga.uppercase()} ${p.namaDesa.uppercase()}"),
                    NamaFormatter.toUpperNama(p.kepalaDesa), nipKades(p))
                y = maxOf(yKiri, y)
                true
            }
            "aw.halaman2" -> { halamanBaru(); true }
            "aw.judul" -> { judulTengahSaja("SURAT PERNYATAAN AHLI WARIS"); true }
            "aw.daftarTtd" -> { blokDaftarTtdAhliWaris(nilai); true }
            "aw.halaman3" -> {
                // Halaman kuasa hanya ada bila ada pasangan DAN anak — sama
                // seperti kondisi generator desktop.
                val pasangan = nilai["ahliwaris.pasangan.nama"].orBlank()
                val anakPertama = nilai["ahliwaris.anak[0].nama"].orBlank()
                if (pasangan.isNotBlank() && anakPertama.isNotBlank()) { halamanBaru(); true } else false
            }
            "aw.judulKuasa" -> { judulTengahSaja("SURAT KUASA AHLI WARIS"); true }
            "aw.ttdKuasa" -> { blokTtdKuasa(nilai); true }
            "aw.kakiH3" -> {
                // Kepala Desa kiri, Camat kanan (halaman kuasa).
                val yMulai = y
                tulisKolomTengah(MARGIN_SISI + setengahHalaman(), setengahHalaman(),
                    listOf("Mengetahui;"),
                    listOf("${p.jenisLembaga.uppercase()} ${p.namaDesa.uppercase()}"),
                    NamaFormatter.toUpperNama(p.kepalaDesa), nipKades(p))
                val yKiri = y; y = yMulai
                tulisKolomTengah(MARGIN_SISI + setengahHalaman(), setengahHalaman(),
                    listOf("Menguatkan;", "Nomor : ...................................", "Camat ${p.kecamatan.ifBlank { "..........." }}"),
                    emptyList(), "..........................", "")
                y = maxOf(yKiri, y)
                true
            }
            "rk.baris" -> { blokRekening(nomor, nilai, p); true }
            "rk.ttd" -> { blokTtdRekening(nilai, p); true }
            else -> false
        }

        private fun teksTengahTeks(teks: String, paint: Paint, xKiri: Float, lebar: Float) {
            kanvas.drawText(teks, xKiri + (lebar - paint.measureText(teks)) / 2f, y + UKURAN_TEKS, paint)
            y += UKURAN_TEKS * kerapatan.faktorLeading
        }

        private fun beriHalaman(tinggiButuh: Float) {
            if (y + tinggiButuh > tinggi - kerapatan.marginBawah) halamanBaru()
        }

        /** LAMPIRAN IV/VI... — tabel 70/30, ukuran font ikut kerapatan. */
        private fun blokLampiran(jenis: SkemaJenis) {
            val uk = kerapatan.ukuranFontLampiran
            val katalog = NtcrKatalog.cari(jenis.idJenis)
            val lampiran = katalog?.lampiran.orEmpty()
            val baris = (if (lampiran.isBlank()) emptyList() else listOf("LAMPIRAN $lampiran")) + listOf(
                "KEPUTUSAN DIREKTUR JENDERAL BIMBINGAN MASYARAKAT ISLAM",
                "NOMOR 473 TAHUN 2020",
                "TENTANG",
                "PETUNJUK TEKNIS PELAKSANAAN PENCATATAN PERNIKAHAN",
            )
            val paint = font.paint(uk)
            var yKiri = y
            for ((i, t) in baris.withIndex()) {
                kanvas.drawText(t, MARGIN_SISI, yKiri + uk, paint)
                if (i == 0) {
                    val model = "Model ${katalog?.kode ?: "N1"}"
                    kanvas.drawText(model, lebar - MARGIN_SISI - paint.measureText(model), yKiri + uk, paint)
                }
                yKiri += uk * 1.2f
            }
            y = yKiri
            lewati(14f * kerapatan.faktorJarakBlok)
        }

        /** KANTOR DESA/KELURAHAN : ... — tiga baris berlabel, label kolom 205pt. */
        private fun blokKepalaDesa(p: PengaturanSurat) {
            val data = listOf(
                Triple("KANTOR DESA/KELURAHAN", (p.namaDesa.ifBlank { "..............." }).uppercase(), 3f),
                Triple("KECAMATAN", (p.kecamatan.ifBlank { "..............." }).uppercase(), 3f),
                Triple("KABUPATEN/KOTA", (p.kabupaten.ifBlank { "..............." }).uppercase(), 0f),
            )
            val paint = font.paint(UKURAN_TEKS)
            for ((label, nilaiTeks, jarak) in data) {
                kanvas.drawText("$label :", MARGIN_SISI, y + UKURAN_TEKS, paint)
                kanvas.drawText(nilaiTeks, MARGIN_SISI + 205f, y + UKURAN_TEKS, paint)
                y += JARAK_BARIS_SEL
                if (jarak > 0f) y += jarak
            }
            lewati(4f)
        }

        /** Judul permohonan N2/N3: judul tebal tengah + Nomor/Perihal ~ tanggal. */
        private fun blokJudulPermohonan(jenis: SkemaJenis, nomor: String, p: PengaturanSurat) {
            lewati(12f * kerapatan.faktorJarakBlok)
            judulTengahSaja(NtcrKatalog.namaFormulir(jenis.idJenis).ifBlank { jenis.judulCetak })
            lewati(14f)
            val lebarIsi = lebar - MARGIN_SISI * 2
            val lebarKiri = lebarIsi * 0.6f
            val xKanan = MARGIN_SISI + lebarKiri
            val lebarKanan = lebarIsi * 0.4f
            val paint = font.paint(UKURAN_TEKS)
            kanvas.drawText("Nomor : ${nomor.ifBlank { "......................." }}", MARGIN_SISI, y + UKURAN_TEKS, paint)
            val yNomor = y
            y += JARAK_BARIS_SEL
            kanvas.drawText("Perihal : ${NtcrKatalog.perihal(jenis.idJenis).ifBlank { "......................." }}", MARGIN_SISI, y + UKURAN_TEKS, paint)
            val tempatTanggal = "${p.namaDesa}, ${TanggalIndonesia.panjang()}"
            kanvas.drawText(tempatTanggal, xKanan + lebarKanan - paint.measureText(tempatTanggal), yNomor + UKURAN_TEKS, paint)
            y += JARAK_BARIS_SEL
        }

        private fun judulTengahSaja(judulTeks: String) {
            val teks = judulTeks.uppercase(Locale("id", "ID"))
            val paint = font.paint(UKURAN_JUDUL, tebal = true)
            kanvas.drawText(teks, (lebar - paint.measureText(teks)) / 2f, y + UKURAN_JUDUL, paint)
            val lebarTeks = paint.measureText(teks)
            garisHorisontal((lebar - lebarTeks) / 2f, (lebar + lebarTeks) / 2f, y + UKURAN_JUDUL + 2f, 1f)
            y += UKURAN_JUDUL + 2f
        }

        /** Tanda tangan desa di kolom kanan (N1, N6, N8) — baris atas tanggal. */
        private fun blokTtdKanan(nilai: Map<String, String>, p: PengaturanSurat, pejabat: String) {
            lewati(20f * kerapatan.faktorJarakBlok)
            val setengah = (lebar - MARGIN_SISI * 2) / 2f
            val xKanan = MARGIN_SISI + setengah
            val desa = p.namaDesa.ifBlank { "..............." }
            val sekdes = pejabat.equals("Sekretaris Desa", ignoreCase = true)
            val jabatan = if (sekdes)
                "A/N KEPALA DESA ${desa.uppercase()}\nSEKRETARIS DESA"
            else Kop.jabatan(p, pejabat)
            val nama = NamaFormatter.namaAtauTitik(
                nilai["pejabat.nama"].orBlank().ifBlank {
                    if (sekdes) p.sekretarisDesa else p.kepalaDesa
                })
            val nip = if (!sekdes && p.jenisLembaga.equals("Kelurahan", ignoreCase = true))
                p.nipKepalaDesa.trim() else ""
            tulisKolomTengah(xKanan, setengah, listOf(desa, TanggalIndonesia.panjang()), jabatan.split('\n'), nama, nip)
        }

        /** N2/N3: Diterima/Kepala KUA di kiri, Wassalam/Pemohon di kanan. */
        private fun blokTtdKua(nilai: Map<String, String>) {
            lewati(20f * kerapatan.faktorJarakBlok)
            val setengah = (lebar - MARGIN_SISI * 2) / 2f
            val xKiri = MARGIN_SISI
            val xKanan = MARGIN_SISI + setengah
            val diterima = nilai["ntcr.tanggalDiterima"].orBlank()
            val teksDiterima = if (diterima.isBlank())
                "Diterima Tanggal ..............." else "Diterima Tanggal " + TanggalIndonesia.dari(diterima)
            val paint = font.paint(UKURAN_TEKS)
            val paintTebal = font.paint(UKURAN_TEKS, tebal = true)

            val yAwal = y
            var yKiri = yAwal
            fun kiri(teks: String, teksPaint: Paint) {
                kanvas.drawText(teks, xKiri + (setengah - teksPaint.measureText(teks)) / 2f, yKiri + UKURAN_TEKS, teksPaint)
                yKiri += UKURAN_TEKS * kerapatan.faktorLeading
            }
            kiri(teksDiterima, paint)
            kiri("Yang menerima,", paint)
            kiri("Kepala KUA/PPN Luar Negeri", paint)
            yKiri += TINGGI_BARIS * kerapatan.barisRuangTandaTangan
            kiri("...................................", paintTebal)

            var yKanan = yAwal
            fun kanan(teks: String, teksPaint: Paint) {
                kanvas.drawText(teks, xKanan + (setengah - teksPaint.measureText(teks)) / 2f, yKanan + UKURAN_TEKS, teksPaint)
                yKanan += UKURAN_TEKS * kerapatan.faktorLeading
            }
            // Pemisah SATU baris (desktop TinggiBarisTeks) supaya nama pemohon
            // sejajar dengan Kepala KUA.
            yKanan += UKURAN_TEKS * kerapatan.faktorLeading
            kanan("Wassalam,", paint)
            kanan("Pemohon", paint)
            yKanan += TINGGI_BARIS * kerapatan.barisRuangTandaTangan
            kanan(NamaFormatter.namaAtauTitik(nilai["warga.nama"]), paintTebal)

            y = maxOf(yKiri, yKanan)
        }

        /** N4/N5: dua penandatangan sejajar (Calon Suami/Istri atau Ayah/Ibu). */
        private fun blokTtdDuaPihak(jenis: SkemaJenis, nilai: Map<String, String>, p: PengaturanSurat) {
            lewati(10f * kerapatan.faktorJarakBlok)
            val setengah = (lebar - MARGIN_SISI * 2) / 2f
            val xKiri = MARGIN_SISI
            val xKanan = MARGIN_SISI + setengah
            val paint = font.paint(UKURAN_TEKS)
            val paintTebal = font.paint(UKURAN_TEKS, tebal = true)

            var labelKiri: String
            var namaKiri: String?
            var labelKanan: String
            var namaKanan: String?
            if (jenis.idJenis.equals("NTCR_N5", true)) {
                // Ayah & ibu/wali dari SISI ANAK yang diberi izin.
                val istri = nilai["ntcr.pihakAnakIzinOrtu"].equals("Istri", ignoreCase = true)
                val prefiksAyah = if (istri) "ntcr.AyahCalonIstri.nama" else "ntcr.AyahCalonSuami.nama"
                val prefiksIbu = if (istri) "ntcr.IbuCalonIstri.nama" else "ntcr.IbuCalonSuami.nama"
                labelKiri = "Ayah/wali/pengampu"
                namaKiri = nilai[prefiksAyah]
                labelKanan = "Ibu/wali/pengampu"
                namaKanan = nilai[prefiksIbu]
            } else {
                labelKiri = "Calon Suami"
                namaKiri = nilai["warga.nama"]
                labelKanan = "Calon Istri"
                namaKanan = nilai["ntcr.namaIstri"]
            }

            fun baris(teks: String, teksPaint: Paint, kiri: Boolean, target: Float): Float {
                val x = if (kiri) xKiri else xKanan
                kanvas.drawText(teks, x + (setengah - teksPaint.measureText(teks)) / 2f, target + UKURAN_TEKS, teksPaint)
                return target + UKURAN_TEKS * kerapatan.faktorLeading
            }
            // Kiri: pemisah SATU baris (desktop TinggiBarisTeks) supaya jabatan
            // kiri sejajar baris tempat/tanggal kanan; nama kiri sejajar nama kanan.
            var yKiri = y
            yKiri += UKURAN_TEKS * kerapatan.faktorLeading
            yKiri = baris(labelKiri, paint, true, yKiri)
            yKiri += TINGGI_BARIS * kerapatan.barisRuangTandaTangan
            yKiri = baris(NamaFormatter.namaAtauTitik(namaKiri), paintTebal, true, yKiri)

            var yKananX = y
            yKananX = baris("${p.namaDesa}, ${TanggalIndonesia.panjang()}", paint, false, yKananX)
            yKananX = baris(labelKanan, paint, false, yKananX)
            yKananX += TINGGI_BARIS * kerapatan.barisRuangTandaTangan
            yKananX = baris(NamaFormatter.namaAtauTitik(namaKanan), paintTebal, false, yKananX)

            y = maxOf(yKiri, yKananX)
        }

        /** Kolom tanda tangan kanan umum: [barisAtas..], jabatan, ruang, nama, NIP. */
        private fun tulisKolomTengah(
            xKanan: Float,
            lebarKolom: Float,
            barisAtas: List<String>,
            jabatan: List<String>,
            nama: String,
            nip: String,
        ) {
            val paint = font.paint(UKURAN_TEKS)
            val paintTebal = font.paint(UKURAN_TEKS, tebal = true)
            var yy = y
            fun tengah(teks: String, teksPaint: Paint) {
                kanvas.drawText(teks, xKanan + (lebarKolom - teksPaint.measureText(teks)) / 2f, yy + UKURAN_TEKS, teksPaint)
                yy += UKURAN_TEKS * kerapatan.faktorLeading
            }
            for (b in barisAtas) tengah(b, paint)
            for (b in jabatan) tengah(b, paint)
            yy += TINGGI_BARIS * kerapatan.barisRuangTandaTangan
            tengah(nama, paintTebal)
            if (nip.isNotBlank()) tengah("NIP $nip", paint)
            y = yy
        }

        /** Kotak "Kode : F-2.17" kanan atas pada formulir resmi Kematian. */
        private fun blokKotakKode() {
            val lebarKotak = 89f
            val tinggiKotak = UKURAN_TEKS + 6f
            val xKotak = lebar - MARGIN_SISI - lebarKotak
            val paintTebal = font.paint(UKURAN_TEKS, tebal = true)
            beriHalaman(tinggiKotak + 3f)
            kanvas.drawRect(xKotak, y, xKotak + lebarKotak, y + tinggiKotak, Paint().apply {
                style = Paint.Style.STROKE
                color = Color.BLACK
                strokeWidth = 0.75f
                isAntiAlias = true
            })
            kanvas.drawText("Kode : F-2.17", xKotak + (lebarKotak - paintTebal.measureText("Kode : F-2.17")) / 2f, y + tinggiKotak - 3f, paintTebal)
            y += tinggiKotak + 3f
        }

        /** Judul kematian: 14pt tebal tengah + baris "Nomor : ..." di bawahnya. */
        private fun blokJudulKematian(judulTeks: String, nomor: String) {
            judulTengahSaja(judulTeks)
            val nomorTampil = if (nomor.isBlank() || nomor.contains("..."))
                "Nomor : 570 / ${"".padEnd(17)} / Ds. ${java.util.Calendar.getInstance().get(java.util.Calendar.YEAR)}"
            else "Nomor : $nomor"
            val paint = font.paint(UKURAN_TEKS)
            kanvas.drawText(nomorTampil, (lebar - paint.measureText(nomorTampil)) / 2f, y + UKURAN_TEKS, paint)
            y += JARAK_BARIS_SEL
            lewati(15f)
        }

        /** Blok tanda tangan Kematian: kolom kanan, ruang tetap 45pt. */
        private fun blokTtdKematian(nilai: Map<String, String>, p: PengaturanSurat, pejabat: String) {
            lewati(30f)
            val setengah = (lebar - MARGIN_SISI * 2) / 2f
            val xKanan = MARGIN_SISI + setengah
            val desa = p.namaDesa.ifBlank { "..............." }
            val jabatan = Kop.jabatan(p, pejabat)
            val nama = NamaFormatter.namaAtauTitik(
                nilai["pejabat.nama"].orBlank().ifBlank { p.kepalaDesa })
            val nip = if (p.jenisLembaga.equals("Kelurahan", ignoreCase = true)) p.nipKepalaDesa.trim() else ""
            val paint = font.paint(UKURAN_TEKS)
            val paintTebal = font.paint(UKURAN_TEKS, tebal = true)
            var yy = y
            fun tengah(teks: String, teksPaint: Paint) {
                kanvas.drawText(teks, xKanan + (setengah - teksPaint.measureText(teks)) / 2f, yy + UKURAN_TEKS, teksPaint)
                yy += UKURAN_TEKS * kerapatan.faktorLeading
            }
            tengah("$desa, ${TanggalIndonesia.panjang()}", paint)
            for (b in jabatan.split('\n')) tengah(b, paint)
            yy += 45f
            tengah(nama, paintTebal)
            if (nip.isNotBlank()) tengah("NIP $nip", font.paint(UKURAN_TEKS - 1f))
            y = yy
        }

        /** Tabel rincian garapan (6 kolom berbingkai) + baris Jumlah. */
        private fun blokTabelGarapan(nilai: Map<String, String>) {
            lewati(10f * kerapatan.faktorJarakBlok)
            val ukTabel = 11f
            val paint = font.paint(ukTabel)
            val tinggiSel = ukTabel * 1.25f + 6f
            val kolom = listOf(
                Triple("No", 4f, "tengah"),
                Triple("Nama Pemilik", 22f, "kiri"),
                Triple("Letak", 28f, "kiri"),
                Triple("No. Persil", 16f, "kiri"),
                Triple("Luas", 12f, "kanan"),
                Triple("Keterangan", 18f, "kiri"),
            )
            val totalRasio = kolom.sumOf { it.second.toDouble() }.toFloat()
            val lebarTabel = lebar - MARGIN_SISI * 2
            val xMulai = mutableMapOf<String, Float>()
            val lebarKol = mutableMapOf<String, Float>()
            var xx = MARGIN_SISI
            for ((namaKol, rasio, _) in kolom) {
                xMulai[namaKol] = xx
                lebarKol[namaKol] = lebarTabel * rasio / totalRasio
                xx += lebarKol[namaKol]!!
            }

            // Header berbingkai berlatar abu terang, urut desktop.
            val tinggiHeader = ukTabel * 1.25f + 8f
            for ((namaKol, _, _) in kolom) {
                val x1 = xMulai[namaKol]!!
                val x2 = x1 + lebarKol[namaKol]!!
                kanvas.drawRect(x1, y, x2, y + tinggiHeader, Paint().apply {
                    style = Paint.Style.FILL
                    color = Color.parseColor("#EEEEEE")
                    isAntiAlias = true
                })
                garis(x1, x2, y, 0.75f)
                garis(x1, x2, y + tinggiHeader, 0.75f)
                val pj = font.paint(ukTabel, tebal = true)
                kanvas.drawText(namaKol, x1 + (x2 - x1 - pj.measureText(namaKol)) / 2f, y + tinggiHeader - 4f, pj)
            }
            y += tinggiHeader

            // Rincian: bidang pertama dari isian Android; bidang ke-2 dst. dari
            // kunci garapanTambahan[i].* (dihitung BuatSuratActivity); dua baris
            // kosong tetap dicetak seperti desktop (formulir selalu ada supaya
            // operator bisa menulis manual).
            fun barisGarapan(no: String, pemilik: String, lokasi: String, persil: String, luasTeks: String, ket: String) =
                listOf(no, pemilik, lokasi, persil, luasTeks, ket)
            val pemilikBawaan = nilai["warga.nama"].orBlank()
            val barisIsi = mutableListOf<List<String>>()
            var totalLuas = 0.0
            val luas0 = nilai["rincianGarapans[0].luas"].orBlank().toDoubleOrNull() ?: 0.0
            if (luas0 > 0) totalLuas += luas0
            barisIsi.add(barisGarapan("1",
                nilai["rincianGarapans[0].pemilikTanah"].orBlank().ifBlank { pemilikBawaan },
                nilai["rincianGarapans[0].lokasi"].orBlank(),
                nilai["rincianGarapans[0].nomorPersil"].orBlank(),
                if (luas0 > 0) formatRibuan(luas0) + " M2" else "",
                nilai["rincianGarapans[0].keteranganGarapan"].orBlank()))
            var tambahan = 2
            while (true) {
                val awalan = "garapanTambahan[$tambahan]."
                val lokasi = nilai[awalan + "lokasi"].orBlank()
                val persil = nilai[awalan + "nomorPersil"].orBlank()
                val ket = nilai[awalan + "keteranganGarapan"].orBlank()
                val luasTambahan = nilai[awalan + "luas"].orBlank().toDoubleOrNull() ?: 0.0
                if (lokasi.isBlank() && persil.isBlank() && ket.isBlank() && luasTambahan <= 0) break
                if (luasTambahan > 0) totalLuas += luasTambahan
                barisIsi.add(barisGarapan(tambahan.toString(),
                    nilai[awalan + "pemilikTanah"].orBlank().ifBlank { pemilikBawaan },
                    lokasi, persil,
                    if (luasTambahan > 0) formatRibuan(luasTambahan) + " M2" else "", ket))
                tambahan += 1
            }
            // Dua baris kosong berikutnya, nomor urut melanjutkan.
            for (i in 0 until 2) {
                barisIsi.add(barisGarapan((tambahan + i).toString(), "", "", "", "", ""))
            }
            for (isi in barisIsi) {
                beriHalaman(tinggiSel)
                val tengahY = y + (tinggiSel - ukTabel * 1.25f) / 2f + ukTabel
                kolom.forEachIndexed { j, (namaKol, _, atur) ->
                    val x1 = xMulai[namaKol]!!
                    val x2 = x1 + lebarKol[namaKol]!!
                    val teks = isi[j]
                    val posisi = when (atur) {
                        "tengah" -> x1 + (x2 - x1 - paint.measureText(teks)) / 2f
                        "kanan" -> x2 - 4f - paint.measureText(teks)
                        else -> x1 + 4f
                    }
                    kanvas.drawText(teks, posisi, tengahY, paint)
                }
                // Bingkai seluruh isi baris + pemisah antar kolom.
                garis(MARGIN_SISI, lebar - MARGIN_SISI, y, 0.75f)
                garis(MARGIN_SISI, lebar - MARGIN_SISI, y + tinggiSel, 0.75f)
                for ((namaKol, _, _) in kolom) {
                    val x1 = xMulai[namaKol]!!
                    garis(x1, x1, y, y + tinggiSel, 0.75f)
                }
                garis(lebar - MARGIN_SISI, lebar - MARGIN_SISI, y, y + tinggiSel, 0.75f)
                y += tinggiSel
            }

            // Baris Jumlah: empat kolom pertama digabung.
            beriHalaman(tinggiSel)
            val teksJumlah = formatRibuan(totalLuas) + " M2"
            val pj = font.paint(ukTabel, tebal = true)
            val tengahY = y + (tinggiSel - ukTabel * 1.25f) / 2f + ukTabel
            val xJoin1 = MARGIN_SISI
            val xJoin2 = xMulai["No. Persil"]!!
            kanvas.drawText("Jumlah", xJoin1 + (xJoin2 - xJoin1 - pj.measureText("Jumlah")) / 2f, tengahY, pj)
            val xl = xMulai["Luas"]!!
            val xLuasAkhir = xl + lebarKol["Luas"]!!
            kanvas.drawText(teksJumlah, xLuasAkhir - 4f - pj.measureText(teksJumlah), tengahY, pj)
            garis(xJoin2, xLuasAkhir, y, y + tinggiSel, 0.75f)
            garis(xJoin2, xLuasAkhir, y + tinggiSel, y + tinggiSel, 0.75f)
            garis(xLuasAkhir, lebar - MARGIN_SISI, y, y + tinggiSel, 0.75f)
            garis(xLuasAkhir, lebar - MARGIN_SISI, y + tinggiSel, y + tinggiSel, 0.75f)
            y += tinggiSel
            lewati(10f * kerapatan.faktorJarakBlok)
        }

        private fun garis(x1: Float, x2: Float, yy: Float, tebal: Float) {
            kanvas.drawLine(x1, yy, x2, yy, Paint().apply {
                color = Color.BLACK
                strokeWidth = tebal
                isAntiAlias = true
            })
        }

        private fun garis(x1: Float, y1: Float, x2: Float, y2: Float, tebal: Float) {
            kanvas.drawLine(x1, y1, x2, y2, Paint().apply {
                color = Color.BLACK
                strokeWidth = tebal
                isAntiAlias = true
            })
        }

        private fun formatRibuan(nilai: Double): String {
            val simbol = java.text.DecimalFormat("#,##0",
                java.text.DecimalFormatSymbols().apply { groupingSeparator = '.' })
            return simbol.format(nilai)
        }

        /** Rekening Koran: Nomor/Perihal di lajur kiri, tujuan surat di kanan. */
        private fun blokRekening(nomor: String, nilai: Map<String, String>, p: PengaturanSurat) {
            lewati(14f * kerapatan.faktorJarakBlok)
            val paint = font.paint(UKURAN_TEKS)
            val teks = "${p.namaDesa}, ${TanggalIndonesia.panjang()}"
            kanvas.drawText(teks, lebar - MARGIN_SISI - paint.measureText(teks), y + UKURAN_TEKS, paint)
            y += UKURAN_TEKS * kerapatan.faktorLeading
            lewati(12f * kerapatan.faktorJarakBlok)
            val setengah = (lebar - MARGIN_SISI * 2) / 2f
            val xKiri = MARGIN_SISI
            val xKanan = MARGIN_SISI + setengah + 40f
            val dots = "..................................."
            val kiri = listOf(
                "Nomor" to nomor.ifBlank { dots },
                "Perihal" to nilai["keperluan"].orBlank().ifBlank { dots },
            )
            var yKiri = y
            for ((label, isi) in kiri) {
                kanvas.drawText(label, xKiri, yKiri + UKURAN_TEKS, paint)
                kanvas.drawText(":", xKiri + 110f, yKiri + UKURAN_TEKS, paint)
                kanvas.drawText(isi, xKiri + 119f, yKiri + UKURAN_TEKS, paint)
                yKiri += JARAK_BARIS_SEL
            }
            var yKanan = y
            val kanan = listOf(
                "Kepada Yth," to true,
                "Customer Service" to false,
                "Bank ${nilai["additionalData.bank"].orBlank().ifBlank { dots }}" to false,
                "KCP ${nilai["additionalData.kcp"].orBlank().ifBlank { dots }}" to false,
                "Di -" to false,
            )
            for ((t, tebal) in kanan) {
                kanvas.drawText(t, xKanan, yKanan + UKURAN_TEKS, font.paint(UKURAN_TEKS, tebal))
                yKanan += JARAK_BARIS_SEL
            }
            kanvas.drawText("Tempat", xKanan + 20f, yKanan + UKURAN_TEKS, paint)
            y = maxOf(yKiri, yKanan + JARAK_BARIS_SEL)
        }

        private fun blokTtdRekening(nilai: Map<String, String>, p: PengaturanSurat) {
            lewati(12f * kerapatan.faktorJarakBlok)
            val lebarBlok = 250f
            val xKiri = lebar - MARGIN_SISI - lebarBlok
            val jabatan = nilai["additionalData.jabatan"].orBlank().ifBlank { "Kepala Desa" }
            val nama = NamaFormatter.namaAtauTitik(
                nilai["additionalData.namaPejabat"].orBlank().ifBlank { p.kepalaDesa })
            val nip = if (p.jenisLembaga.equals("Kelurahan", ignoreCase = true)) p.nipKepalaDesa.trim() else ""
            val paint = font.paint(UKURAN_TEKS)
            val paintTebal = font.paint(UKURAN_TEKS, tebal = true)
            fun tengah(teks: String, teksPaint: Paint) {
                kanvas.drawText(teks, xKiri + (lebarBlok - teksPaint.measureText(teks)) / 2f, y + UKURAN_TEKS, teksPaint)
                y += UKURAN_TEKS * kerapatan.faktorLeading
            }
            tengah(jabatan, paintTebal)
            y += TINGGI_BARIS * kerapatan.barisRuangTandaTangan
            tengah(nama, paintTebal)
            if (nip.isNotBlank()) tengah("NIP $nip", paint)
        }

        /** Lebar setengah halaman isian (dipakai blok dua kolom). */
        private fun setengahHalaman(): Float = (lebar - MARGIN_SISI * 2) / 2f

        /** NIP Kepala Desa hanya untuk kelurahan — sama seperti KopSurat desktop. */
        private fun nipKades(p: PengaturanSurat): String =
            if (p.jenisLembaga.equals("Kelurahan", true)) p.nipKepalaDesa.trim() else ""

        /** True bila SEMUA $kunci pada teks-teks terisi (tidak kosong). */
        private fun kondisiTerisi(templat: List<String>, nilai: Map<String, String>): Boolean {
            val gabung = templat.joinToString("\n")
            val cocok = Regex("\\$[A-Za-z0-9_.\\[\\]]+").findAll(gabung)
            return cocok.all { (nilai[it.value.substring(1)] ?: "").isNotBlank() }
        }

        /** Daftar nama penanda tangan pernyataan: pasangan lalu tiap anak. */
        private fun blokDaftarTtdAhliWaris(nilai: Map<String, String>) {
            val daftar = nilai["ahliwaris.penandatangan"].orBlank().split("\n").filter { it.isNotBlank() }
            lewati(15f * kerapatan.faktorJarakBlok)
            val paint = font.paint(UKURAN_TEKS)
            for (nama in daftar) {
                teksTengahTeks("  $nama  ................................", paint, MARGIN_SISI, lebar - MARGIN_SISI * 2)
                lewati(15f * kerapatan.faktorJarakBlok)
            }
        }

        /**
         * Tanda tangan surat kuasa: kolom kiri "Yang Menerima Kuasa" (anak
         * pertama), kolom kanan "Yang Memberi Kuasa" (pasangan lalu tiap anak).
         */
        private fun blokTtdKuasa(nilai: Map<String, String>) {
            lewati(30f * kerapatan.faktorJarakBlok)
            val setengah = (lebar - MARGIN_SISI * 2) / 2f
            val xKiri = MARGIN_SISI
            val xKanan = MARGIN_SISI + setengah
            val penerima = nilai["ahliwaris.anak[0].nama"].orBlank().ifBlank { ".........................." }
            val paintTebal = font.paint(UKURAN_TEKS, tebal = true)

            var yKiri = y
            fun tulisKiri(teks: String, teksPaint: Paint) {
                kanvas.drawText(teks, xKiri + (setengahHalaman() - teksPaint.measureText(teks)) / 2f, yKiri + UKURAN_TEKS, teksPaint)
                yKiri += UKURAN_TEKS * kerapatan.faktorLeading
            }
            tulisKiri("Yang Menerima Kuasa", paintTebal)
            yKiri += TINGGI_BARIS * kerapatan.barisRuangTandaTangan
            tulisKiri(penerima, paintTebal)

            var yKanan = y
            fun tulisKanan(teks: String, teksPaint: Paint) {
                kanvas.drawText(teks, xKanan + (setengahHalaman() - teksPaint.measureText(teks)) / 2f, yKanan + UKURAN_TEKS, teksPaint)
                yKanan += UKURAN_TEKS * kerapatan.faktorLeading
            }
            tulisKanan("Yang Memberi Kuasa", paintTebal)
            val daftarPemberi = nilai["ahliwaris.penandatangan"].orBlank().split("\n").filter { it.isNotBlank() }
            for (nama in daftarPemberi) {
                tulisKanan("$nama   ...............................",
                    font.paint(UKURAN_TEKS))
                yKanan += 15f * kerapatan.faktorJarakBlok
            }

            y = maxOf(yKiri, yKanan)
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
                // ' [' dan ']' ikut masuk kunci supaya referensi daftar seperti
                // $ahliwaris.anak[0].nama bisa dipecahkan.
                !ch.isLetterOrDigit() && ch != '.' && ch != '_' && ch != '[' && ch != ']'
            } ?: templat.length
            if (akhir == i + 1) {
                hasil.append('$')
                i += 1
                continue
            }
            val kunciMentah = templat.substring(i + 1, akhir)
            val kunci = kunciMentah.trimEnd('.')
            hasil.append(nilai[kunci] ?: "")
            i = i + 1 + kunci.length
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
        val kabupaten = bersihkan(p.kabupaten, jenisKab, if (jenisKab.equals("Kabupaten", ignoreCase = true)) "Kab." else null)
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
        val surel = SurelBersih(p.email)
        if (surel.isNotBlank()) hasil.add(Baris("", SuratPdf.FONT_ALAMAT, false, "Email $surel"))
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
            val desa = bersihkan(p.namaDesa, "Desa").trim()
            return if (kata.equals("Sekretaris Desa", ignoreCase = true)) {
                "A/N KEPALA DESA ${desa.uppercase()}\nSEKRETARIS DESA"
            } else {
                "${kata} ${desa}".uppercase()
            }
        }

    /** Jabatan untuk baris "Jabatan" di badan: huruf awal besar, tanpa kapital semua. */
    fun jabatanBadan(p: PengaturanSurat, pejabat: String): String = kataPejabat(p, pejabat)

    fun SurelBersih(teks: String): String {
        val t = teks.trim()
        if (t.isEmpty()) return ""
        val regex = Regex("[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\\.[A-Za-z]{2,}")
        return regex.find(t)?.value ?: t
    }

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

/**
 * Katalog blanko NTCR — salinan ringkas NtcrKatalog desktop. Dipakai blok
 * kustom untuk menuliskan lampiran Kepdirjen, nama formulir, dan perihal.
 */
object NtcrKatalog {
    data class Blanko(val kode: String, val lampiran: String, val namaFormulir: String, val perihal: String)

    private val daftar = mapOf(
        "NTCR_N1" to Blanko("N1", "IV", "FORMULIR PENGANTAR NIKAH", "Pengantar nikah"),
        "NTCR_N2" to Blanko("N2", "VI", "FORMULIR PERMOHONAN KEHENDAK NIKAH", "Permohonan kehendak nikah"),
        "NTCR_N3" to Blanko("N3", "VII", "FORMULIR PERMOHONAN PENCATATAN ISBAT", "Permohonan pencatatan isbat"),
        "NTCR_N4" to Blanko("N4", "VIII", "SURAT PERSETUJUAN PENGANTIN", ""),
        "NTCR_N5" to Blanko("N5", "IX", "SURAT IZIN ORANG TUA", ""),
        "NTCR_N6" to Blanko("N6", "X", "SURAT KETERANGAN KEMATIAN SUAMI/ISTRI", ""),
        "NTCR_N8" to Blanko("N8", "", "SURAT KETERANGAN NUMPANG NIKAH", ""),
    )

    fun cari(idJenis: String): Blanko? =
        daftar.entries.firstOrNull { it.key.equals(idJenis, ignoreCase = true) }?.value

    fun kode(idJenis: String): String = cari(idJenis)?.kode ?: ""
    fun lampiran(idJenis: String): String = cari(idJenis)?.lampiran ?: ""
    fun namaFormulir(idJenis: String): String = cari(idJenis)?.namaFormulir ?: ""
    fun perihal(idJenis: String): String = cari(idJenis)?.perihal ?: ""
}

/**
 * Format tanggal panjang bahasa Indonesia ("17 September 2026") — bentuk yang
 * dipakai baris tempat/tanggal blanko desktop (Budaya id-ID). Nilai isian
 * Android (yyyy-MM-dd / dd-MM-yyyy) ikut diterima supaya tanggal diterima KUA
 * tidak ditulis apa adanya bila operator memilih dari pemilih tanggal.
 */
object TanggalIndonesia {
    private val budaya = Locale("id", "ID")

    fun panjang(): String = SimpleDateFormat("dd MMMM yyyy", budaya).format(Date())

    /** Nilai isian Android menjadi "dd MMMM yyyy"; pola tak dikenal disalin apa adanya. */
    fun dari(nilai: String): String = dari(nilai, "dd MMMM yyyy")

    /**
     * Pola tampilan tanggal LAHIR yang dipakai generator desktop untuk jenis surat
     * ini. Desktop tidak seragam: GarapanGenerator dan IzinOrtuGenerator mencetaknya
     * lewat ParseTanggalToUiFormat yang berpola "dd-MM-yyyy", sedangkan
     * SuratGeneratorBase dan sisanya memakai "dd MMMM yyyy" (id-ID).
     */
    fun polaTanggalLahir(idJenis: String): String = when {
        idJenis.equals("GARAPAN_SAWAH", ignoreCase = true) -> "dd-MM-yyyy"
        idJenis.equals("IZIN_ORTU", ignoreCase = true) -> "dd-MM-yyyy"
        else -> "dd MMMM yyyy"
    }

    /** Nilai isian Android (ISO / dd-MM-yyyy / dd/MM/yyyy) ke pola tampilan desktop. */
    fun dari(nilai: String, polaTampil: String): String {
        val teks = nilai.trim()
        val pola = listOf("yyyy-MM-dd", "dd-MM-yyyy", "dd/MM/yyyy", "d/M/yyyy")
        for (polaTeks in pola) {
            try {
                val parser = SimpleDateFormat(polaTeks, Locale.US)
                parser.isLenient = false
                val terbaca = parser.parse(teks)
                if (terbaca != null) return SimpleDateFormat(polaTampil, budaya).format(terbaca)
            } catch (_: Exception) {
                // pola berikutnya
            }
        }
        return teks
    }
}
