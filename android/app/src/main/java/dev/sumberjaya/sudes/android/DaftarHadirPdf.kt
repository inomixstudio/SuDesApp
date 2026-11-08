package dev.sumberjaya.sudes.android

import android.content.Context
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.pdf.PdfDocument
import java.io.ByteArrayOutputStream
import java.io.File
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/**
 * Rendering Daftar Hadir mengikuti `DaftarHadirDocument` desktop:
 *
 * - Ukuran halaman dari [SuratPdf] (A4/F4 per pengaturan cetak); landscape bila
 *   jumlah kolom terpilih >= 7 (`AmbangLandscape`). Margin atas 50 (potret) /
 *   32 (lanskap), kanan 45, bawah 40/18, kiri 45; font Times 11/10.
 * - Kop surat di halaman pertama saja (opsional), skala kop 0.82 saat landscape.
 * - Judul 15pt tebal bergaris bawah, tiap baris Enter jadi baris sendiri.
 * - Baris info: kolom label 80pt + ":" 9pt, lurus.
 * - Tabel bergaris; lebar kolom No 30, No Hp 88, sisanya relatif
 *   (Nama 4, JK 1.4/1.2, Jabatan 3/2.5, NIP 2/3.5, NIK 3/3.1,
 *   Alamat 3.4/3.1, Keterangan 2.4/2.3, Tanda Tangan 3/2.2).
 * - Semua baris grid dicetak apa adanya; dikengkapi baris kosong sampai
 *   minimal 10 (potret) / 6 (lanskap). Baris tabel min 28/24pt, ruang
 *   tanda tangan 28pt.
 * - Footer Kepala Desa menempel di kanan (lebar 260), rata tengah.
 */
object DaftarHadirPdf {

    private const val MARGIN_SISI = 45f
    private const val LEBAR_LABEL = 80f
    private const val LEBAR_TITIK = 9f
    private const val AMBANG_LANDSKAP = 7
    private const val MIN_POTRET = 10
    private const val MIN_LANSKAP = 6
    private const val UKURAN = 11f
    private const val UKURAN_LANSKAP = 10f
    private const val UKURAN_JUDUL = 15f
    private const val LEBAR_LOGO = 60f

    data class Peserta(
        val nama: String = "",
        val jenisKelamin: String = "",
        val jabatan: String = "",
        val nip: String = "",
        val nik: String = "",
        val noHp: String = "",
        val alamat: String = "",
        val keterangan: String = "",
    )

    data class Data(
        val judul: String = "DAFTAR HADIR",
        val hariTanggal: String = "",
        val pukul: String = "",
        val tempat: String = "",
        val peserta: List<Peserta> = emptyList(),
        val tampilNo: Boolean = true,
        val tampilNama: Boolean = true,
        val tampilJenisKelamin: Boolean = false,
        val tampilJabatan: Boolean = true,
        val tampilTandaTangan: Boolean = true,
        val tampilNip: Boolean = false,
        val tampilNik: Boolean = false,
        val tampilNoHp: Boolean = false,
        val tampilAlamat: Boolean = false,
        val tampilKeterangan: Boolean = false,
        val tampilFooter: Boolean = true,
        val tampilKop: Boolean = true,
    ) {
        val jumlahKolom: Int
            get() = listOf(
                tampilNo, tampilNama, tampilJenisKelamin, tampilJabatan, tampilTandaTangan,
                tampilNip, tampilNik, tampilNoHp, tampilAlamat, tampilKeterangan,
            ).count { it }

        val landscape: Boolean get() = jumlahKolom >= AMBANG_LANDSKAP
    }

    fun buat(
        ctx: Context,
        data: Data,
        pengaturan: PengaturanSurat,
        logo: Bitmap? = null,
    ): File {
        val font = FontSet.muat(ctx)
        var terakhir: ByteArray = ByteArray(0)
        // Kerapatan percobaan: desktop tidak mengulang, Android ratakan sekali.
        val perender = Perender(font, data, pengaturan, logo)
        perender.susun()
        terakhir = perender.hasil()

        val berkas = File(
            File(ctx.cacheDir, "daftar-hadir").apply { mkdirs() },
            "daftar-hadir-${System.currentTimeMillis()}.pdf",
        )
        berkas.writeBytes(terakhir)
        return berkas
    }

    private class Perender(
        private val font: FontSet,
        private val data: Data,
        private val pengaturan: PengaturanSurat,
        private val logo: Bitmap?,
    ) {
        private val dokumen = PdfDocument()
        private val landscape = data.landscape
        private val ukuran = if (landscape) UKURAN_LANSKAP else UKURAN
        private val marginAtas = if (landscape) 32f else 50f
        private val marginBawah = if (landscape) 18f else 40f
        private val minimalBaris = if (landscape) MIN_LANSKAP else MIN_POTRET
        private val minSel = if (landscape) 24f else 28f

        var lebar: Float = 0f
            private set
        var tinggi: Float = 0f
            private set

        private var halamanIni: PdfDocument.Page
        private var kanvas: Canvas
        private var y: Float
        private var nomorHalaman = 1

        init {
            val (l, t) = dimensi()
            lebar = l
            tinggi = t
            halamanIni = dokumen.startPage(
                PdfDocument.PageInfo.Builder(l.toInt(), t.toInt(), 1).create(),
            )
            kanvas = halamanIni.canvas
            y = marginAtas
        }

        fun hasil(): ByteArray {
            dokumen.finishPage(halamanIni)
            val keluar = ByteArrayOutputStream()
            dokumen.writeTo(keluar)
            dokumen.close()
            return keluar.toByteArray()
        }

        private fun dimensi(): Pair<Float, Float> {
            val f4 = pengaturan.ukuranKertas.equals("F4", ignoreCase = true)
            val (l, t) = if (f4) SuratPdf.F4_LEBAR to SuratPdf.F4_TINGGI
            else SuratPdf.A4_LEBAR to SuratPdf.A4_TINGGI
            return if (landscape) t to l else l to t
        }

        fun susun() {
            if (data.tampilKop) {
                kop()
                y += 14f
            } else {
                y += 6f
            }
            judul()
            y += 6f
            barisInfo()
            y += 12f
            tabel()
            if (data.tampilFooter) {
                y += if (landscape) 12f else 20f
                // ShowEntire: blok footer tidak boleh terbelah halaman.
                if (y + 150f > tinggi - marginBawah) halamanBaru()
                footer()
            }
        }

        private fun kop() {
            val skala = if (landscape) 0.82f else 1f
            val baris = Kop.baris(pengaturan)
            val kolomTeks = lebar - MARGIN_SISI * 2 - LEBAR_LOGO
            var yTeks = y
            for (b in baris) {
                val uk = b.ukuran * skala
                if (b.teks.isBlank() && b.surel.isBlank()) {
                    yTeks += uk * 1.15f
                    continue
                }
                val paint = font.paint(uk, b.tebal)
                if (b.teks.isNotBlank()) {
                    val xx = MARGIN_SISI + LEBAR_LOGO + (kolomTeks - paint.measureText(b.teks)) / 2f
                    kanvas.drawText(b.teks, xx, yTeks + uk, paint)
                }
                if (b.surel.isNotBlank()) {
                    val pSurel = font.paint(uk, b.tebal, warna = SuratPdf.WARNA_SUREL)
                    val xx = MARGIN_SISI + LEBAR_LOGO + (kolomTeks - pSurel.measureText(b.surel)) / 2f
                    kanvas.drawText(b.surel, xx, yTeks + uk, pSurel)
                }
                yTeks += uk * 1.15f
            }
            if (logo != null) {
                val tinggiLogo = (yTeks - y).coerceAtMost(LEBAR_LOGO * 1.3f)
                kanvas.drawBitmap(
                    logo, null,
                    android.graphics.RectF(MARGIN_SISI, y, MARGIN_SISI + LEBAR_LOGO, y + tinggiLogo),
                    null,
                )
            }
            y = yTeks + 3f
            garis(MARGIN_SISI, lebar - MARGIN_SISI, y, 0.8f)
            y += 1f
            garis(MARGIN_SISI, lebar - MARGIN_SISI, y, 1.5f)
        }

        private fun judul() {
            val baris = normalisasiBaris(data.judul.ifBlank { "DAFTAR HADIR" })
            val paint = font.paint(UKURAN_JUDUL, tebal = true)
            for (b in baris) {
                val w = paint.measureText(b)
                val x = (lebar - w) / 2f
                kanvas.drawText(b, x, y + UKURAN_JUDUL, paint)
                garis(x, x + w, y + UKURAN_JUDUL + 2f, 1f)
                y += UKURAN_JUDUL + 4f
            }
        }

        private fun barisInfo() {
            val dasar = font.paint(ukuran)
            fun baris(label: String, nilai: String) {
                // Label kolom tetap 80pt + ":" 9pt — lurus seperti desktop.
                kanvas.drawText(label, MARGIN_SISI, y + ukuran, dasar)
                kanvas.drawText(":", MARGIN_SISI + LEBAR_LABEL, y + ukuran, dasar)
                // Nilai dilipat bila panjang.
                val sisa = lebar - MARGIN_SISI - (MARGIN_SISI + LEBAR_LABEL + LEBAR_TITIK)
                val potong = lipat(nilai, dasar, sisa)
                for ((i, b) in potong.withIndex()) {
                    kanvas.drawText(b, MARGIN_SISI + LEBAR_LABEL + LEBAR_TITIK, y + ukuran, dasar)
                    y += ukuran * 1.2f
                    if (i != potong.lastIndex) {
                        if (y + ukuran > tinggi - marginBawah) halamanBaru()
                    }
                }
            }
            if (data.hariTanggal.isNotBlank()) baris("Hari/Tanggal", data.hariTanggal.trim())
            if (data.pukul.isNotBlank()) baris("Pukul", data.pukul.trim())
            if (data.tempat.isNotBlank()) baris("Tempat", data.tempat.trim())
        }

        private fun tabel() {
            val kolom = kolomAktif()
            val lebarKolom = hitungLebar(kolom)
            val xKolom = kolomKolom(kolom, lebarKolom)

            // Header (berulang tiap halaman).
            gambarHeader(kolom, xKolom)

            val peserta = data.peserta
            for (i in peserta.indices) {
                gambarBaris(peserta[i], i + 1, kolom, xKolom)
            }
            val total = maxOf(peserta.size, minimalBaris)
            for (i in peserta.size until total) {
                gambarBaris(null, i + 1, kolom, xKolom)
            }
        }

        private fun kolomAktif(): List<String> {
            val daftar = mutableListOf<String>()
            if (data.tampilNo) daftar.add("no")
            if (data.tampilNama) daftar.add("nama")
            if (data.tampilJenisKelamin) daftar.add("jk")
            if (data.tampilJabatan) daftar.add("jabatan")
            if (data.tampilNip) daftar.add("nip")
            if (data.tampilNik) daftar.add("nik")
            if (data.tampilNoHp) daftar.add("nohp")
            if (data.tampilAlamat) daftar.add("alamat")
            if (data.tampilKeterangan) daftar.add("ket")
            if (data.tampilTandaTangan) daftar.add("ttd")
            return daftar
        }

        private fun lebarDasar(kolom: String): Float = when (kolom) {
            "no" -> 30f
            "nohp" -> 88f
            else -> 0f
        }

        private fun lebarRelatif(kolom: String): Float = when (kolom) {
            "nama" -> 4f
            "jk" -> if (landscape) 1.2f else 1.4f
            "jabatan" -> if (landscape) 2.5f else 3f
            "nip" -> if (landscape) 3.5f else 2f
            "nik" -> if (landscape) 3.1f else 3f
            "alamat" -> if (landscape) 3.1f else 3.4f
            "ket" -> if (landscape) 2.3f else 2.4f
            "ttd" -> if (landscape) 2.2f else 3f
            else -> 0f
        }

        private fun hitungLebar(kolom: List<String>): Map<String, Float> {
            val tetap = kolom.sumOf { lebarDasar(it).toDouble() }
            val relatif = kolom.sumOf { lebarRelatif(it).toDouble() }
            val lebarTabel = lebar - MARGIN_SISI * 2
            // Bila lebar tersisa negatif (kolom tetap berlebih), berikan porsi
            // minimal per kolom relatif agar tidak ada lebar negatif.
            val sisa = maxOf(lebarTabel - tetap.toFloat(), kolom.count { lebarDasar(it) == 0f } * 20f)
            val sisaGanda = sisa.toDouble()
            val hasil = mutableMapOf<String, Float>()
            for (k in kolom) {
                val dasar = lebarDasar(k)
                hasil[k] = if (dasar > 0f) dasar else (sisaGanda * lebarRelatif(k) / relatif).toFloat()
            }
            return hasil
        }

        private fun kolomKolom(kolom: List<String>, lebarKolom: Map<String, Float>): Map<String, Pair<Float, Float>> {
            val hasil = mutableMapOf<String, Pair<Float, Float>>()
            var x = MARGIN_SISI
            for (k in kolom) {
                val w = lebarKolom[k] ?: 0f
                hasil[k] = x to (x + w)
                x += w
            }
            return hasil
        }

        private fun gambarHeader(kolom: List<String>, xKolom: Map<String, Pair<Float, Float>>) {
            val label = mapOf(
                "no" to "No.", "nama" to "Nama", "jk" to "L/P", "jabatan" to "Jabatan",
                "nip" to "NIP", "nik" to "NIK", "nohp" to "No. Handphone",
                "alamat" to "Alamat", "ket" to "Keterangan", "ttd" to "Tanda Tangan",
            )
            val paint = font.paint(ukuran, tebal = true)
            val tinggiHeader = ukuran + 8f
            for (k in kolom) {
                val (x1, x2) = xKolom[k] ?: continue
                kanvas.drawRect(x1, y, x2, y + tinggiHeader, paint.apply { style = Paint.Style.FILL; color = Color.parseColor("#EEEEEE") })
                rect(x1, y, x2, y + tinggiHeader)
                val teks = label[k] ?: ""
                val xx = x1 + (x2 - x1 - paint.measureText(teks)) / 2f
                kanvas.drawText(teks, xx, y + tinggiHeader - 3f, paint)
            }
            y += tinggiHeader
        }

        private fun gambarBaris(p: Peserta?, no: Int, kolom: List<String>, xKolom: Map<String, Pair<Float, Float>>) {
            val paint = font.paint(ukuran)
            val values = mutableMapOf<String, String>()
            values["no"] = no.toString()
            values["nama"] = p?.nama.orEmpty()
            values["jk"] = p?.jenisKelamin.orEmpty()
            values["jabatan"] = p?.jabatan.orEmpty()
            values["nip"] = p?.nip.orEmpty()
            values["nik"] = p?.nik.orEmpty()
            values["nohp"] = p?.noHp.orEmpty()
            values["alamat"] = p?.alamat.orEmpty()
            values["ket"] = p?.keterangan.orEmpty()
            values["ttd"] = ""

            // Hitung tinggi baris = maks sel (min sel), sel panjang dilipat.
            val selTeks = mutableMapOf<String, List<String>>()
            var tinggiBaris = minSel
            for (k in kolom) {
                if (k == "ttd") continue
                val (x1, x2) = xKolom[k] ?: continue
                val lebarSel = (x2 - x1) - 8f
                val lines = lipat(values[k] ?: "", paint, lebarSel)
                selTeks[k] = lines
                tinggiBaris = maxOf(tinggiBaris, lines.size * ukuran * 1.2f + 8f)
            }

            if (y + tinggiBaris > tinggi - marginBawah) {
                halamanBaru()
                gambarHeader(kolom, xKolom)
            }

            for (k in kolom) {
                val (x1, x2) = xKolom[k] ?: continue
                rect(x1, y, x2, y + tinggiBaris)
                val lines = selTeks[k] ?: emptyList()
                val tengah = k == "no" || k == "jk"
                var yy = y + 4f
                for (line in lines) {
                    val w = paint.measureText(line)
                    val xx = if (tengah) x1 + ((x2 - x1) - w) / 2f else x1 + 4f
                    kanvas.drawText(line, xx, yy + ukuran, paint)
                    yy += ukuran * 1.2f
                }
            }
            y += tinggiBaris
        }

        private fun rect(x1: Float, y1: Float, x2: Float, y2: Float) {
            kanvas.drawRect(x1, y1, x2, y2, Paint().apply { style = Paint.Style.STROKE; color = Color.BLACK; strokeWidth = 1f })
        }

        private fun footer() {
            val desa = pengaturan.namaDesa
            val namaJudul = if (desa.isBlank()) "" else titleCase(desa.lowercase())
            val tanggal = tanggalDariHariTanggal(data.hariTanggal) ?: Date()
            val tanggalTeks = SimpleDateFormat("dd/MM/yyyy", Locale("id", "ID")).format(tanggal)
            val tempatTanggal = if (namaJudul.isBlank()) tanggalTeks else "$namaJudul, $tanggalTeks"
            val jabatan = Kop.jabatan(pengaturan, "Kepala Desa")
            val namaPejabat = pengaturan.kepalaDesa.ifBlank { "..........................." }

            val lebarBlok = 260f
            val xKiri = lebar - MARGIN_SISI - lebarBlok
            val paint = font.paint(ukuran)
            fun tengah(teks: String) {
                val w = paint.measureText(teks)
                kanvas.drawText(teks, xKiri + (lebarBlok - w) / 2f, y + ukuran, paint)
                y += ukuran * 1.2f
            }
            tengah(tempatTanggal)
            for (b in jabatan.split('\n')) tengah(b.uppercase(Locale("id", "ID")))
            y += if (landscape) 30f else 55f - ukuran * 1.2f
            val paintTebal = font.paint(ukuran, tebal = true)
            val wNama = paintTebal.measureText(namaPejabat.uppercase(Locale("id", "ID")))
            val xx = xKiri + (lebarBlok - wNama) / 2f
            kanvas.drawText(namaPejabat.uppercase(Locale("id", "ID")), xx, y + ukuran, paintTebal)
            garis(xx, xx + wNama, y + ukuran + 2f, 1f)
            y += ukuran * 1.2f
            if (pengaturan.jenisLembaga.equals("Kelurahan", ignoreCase = true) &&
                pengaturan.nipKepalaDesa.isNotBlank()
            ) {
                tengah("NIP ${pengaturan.nipKepalaDesa}")
            }
        }

        private fun titleCase(teks: String): String =
            teks.split(' ').joinToString(" ") { it.replaceFirstChar { c -> c.uppercase() } }

        private fun tanggalDariHariTanggal(teks: String): Date? {
            if (teks.isBlank()) return null
            val bersih = teks.trim()
            val tanpaHari = bersih.substringAfter(",", bersih).trim()
            val pola = listOf(
                SimpleDateFormat("dd/MM/yyyy", Locale("id", "ID")),
                SimpleDateFormat("d MMMM yyyy", Locale("id", "ID")),
                SimpleDateFormat("yyyy-MM-dd", Locale.US),
                SimpleDateFormat("dd-MM-yyyy", Locale.US),
            )
            for (p in pola) {
                try {
                    p.parse(bersih)?.let { return it }
                    p.parse(tanpaHari)?.let { return it }
                } catch (e: Exception) {
                    // pola berikutnya
                }
            }
            return null
        }

        private fun normalisasiBaris(teks: String): List<String> {
            val baris = teks.replace("\r\n", "\n").replace('\r', '\n')
                .split('\n').map { it.trim() }.toMutableList()
            while (baris.firstOrNull()?.isEmpty() == true) baris.removeAt(0)
            while (baris.lastOrNull()?.isEmpty() == true) baris.removeAt(baris.lastIndex)
            if (baris.isEmpty()) baris.add("DAFTAR HADIR")
            return baris
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

        private fun garis(x1: Float, x2: Float, yy: Float, tebal: Float) {
            kanvas.drawLine(x1, yy, x2, yy, Paint().apply {
                color = Color.BLACK
                strokeWidth = tebal
                isAntiAlias = true
            })
        }

        private fun halamanBaru() {
            dokumen.finishPage(halamanIni)
            nomorHalaman += 1
            halamanIni = dokumen.startPage(
                PdfDocument.PageInfo.Builder(lebar.toInt(), tinggi.toInt(), nomorHalaman).create(),
            )
            kanvas = halamanIni.canvas
            y = marginAtas
        }
    }

}
