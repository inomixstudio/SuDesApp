package dev.sumberjaya.sudes.android

import android.content.Context
import org.json.JSONArray
import org.json.JSONObject

/** Satu bidang formulir surat di Android. */
data class SkemaBidang(
    val key: String,
    val label: String,
    val type: String,
    val target: String,
    val required: Boolean,
    val options: List<String>,
    val otomatis: Boolean,
    /** Hanya untuk type="list": bidang penyusun tiap baris. */
    val children: List<SkemaBidang>,
)

/** Perataan paragraf/blok pada badan surat (kawan Rata di desktop). */
enum class RataBlok { JUSTIFY, KIRI, TENGAH, KANAN }

/**
 * Satu blok badan surat. Bentuknya mengikuti apa yang ditulis generator desktop
 * (`BadanSurat.Paragraf`, `TabelFormulir`, `DaftarBernomor`), sehingga PDF Android
 * bisa meniru tata letak desktop tanpa kode khusus per jenis surat.
 */
sealed class BlokBadan {
    data class Paragraf(
        val teks: String,
        val rata: RataBlok = RataBlok.JUSTIFY,
        val tebal: Boolean = false,
        val miring: Boolean = false,
        val indent: Float = 0f,
        val indentKiri: Float = 0f,
        val jarakAtas: Float = 0f,
        val jarakBawah: Float = 0f,
    ) : BlokBadan()

    data class ParagrafCampur(
        val bagian: List<Pair<String, Boolean>>,
        val rata: RataBlok = RataBlok.JUSTIFY,
        val indent: Float = 0f,
        val indentKiri: Float = 0f,
        val jarakAtas: Float = 0f,
        val jarakBawah: Float = 0f,
    ) : BlokBadan()

    data class Tabel(
        val baris: List<Pair<String, String>>,
        val indentKiri: Float = 0f,
        val jarakAtas: Float = 0f,
        val jarakBawah: Float = 0f,
        val tebalkanNama: Boolean = true,
    ) : BlokBadan()

    data class Daftar(
        val butir: List<String>,
        val indentKiri: Float = 0f,
        val jarakAtas: Float = 0f,
        val jarakBawah: Float = 0f,
    ) : BlokBadan()

    /** Blok yang tidak bisa dibuat generik; dicetak sebagai catatan. */
    data class Kustom(val deskripsi: String) : BlokBadan()
}

/** Skema formulir satu jenis surat. */
data class SkemaJenis(
    val idJenis: String,
    val label: String,
    val fields: List<SkemaBidang>,
    val judul: String = "",
    val badan: List<BlokBadan> = emptyList(),
    val pakaiLogo: Boolean = false,
    val pemohonDiTandaTangan: Boolean = false,
    /** Teks bawaan bila kolom keterangan kosong (SKTM dan beberapa jenis lain). */
    val catatan: String = "",
) {
    /** Field yang diisi operator, berurutan; field otomatis dilewati. */
    val fieldsInput: List<SkemaBidang> get() = fields.filter { !it.otomatis }

    /** Judul yang dicetak; jatuh ke label bila skema tidak menyebutkannya. */
    val judulCetak: String get() = judul.ifBlank { label }

    /** True untuk kelompok NTCR (N1–N6, N8) — dipisah di menu utama. */
    val ntcr: Boolean get() = idJenis.startsWith("NTCR_", ignoreCase = true)

    companion object {
        fun fromJson(idJenis: String, json: JSONObject): SkemaJenis {
            val label = json.optString("label", idJenis)
            val daftar = mutableListOf<SkemaBidang>()
            val arr = json.optJSONArray("fields") ?: JSONArray()
            for (i in 0 until arr.length()) {
                daftar.add(BidangDariJson(arr.getJSONObject(i)))
            }
            return SkemaJenis(
                idJenis = idJenis,
                label = label,
                fields = daftar,
                judul = json.optString("judul", ""),
                badan = BlokDariJson(json.optJSONArray("badan")),
                pakaiLogo = json.optBoolean("pakaiLogo", false),
                pemohonDiTandaTangan = json.optBoolean("pemohonDiTandaTangan", false),
                catatan = json.optString("catatan", ""),
            )
        }

        /** Blok badan surat; urutan array dipertahankan persis seperti desktop. */
        private fun BlokDariJson(arr: JSONArray?): List<BlokBadan> {
            if (arr == null) return emptyList()
            val hasil = mutableListOf<BlokBadan>()
            for (i in 0 until arr.length()) {
                val b = arr.optJSONObject(i) ?: continue
                val jarakAtas = b.optDouble("jarakAtas", 0.0).toFloat()
                val jarakBawah = b.optDouble("jarakBawah", 0.0).toFloat()
                val indent = b.optDouble("indent", 0.0).toFloat()
                val indentKiri = b.optDouble("indentKiri", 0.0).toFloat()

                val campur = b.optJSONArray("paragrafCampur")
                val tabel = b.optJSONArray("tabel")
                val daftar = b.optJSONArray("daftar")
                val kustom = b.optString("kustom", "")

                when {
                    campur != null -> {
                        val bagian = mutableListOf<Pair<String, Boolean>>()
                        for (j in 0 until campur.length()) {
                            val potongan = campur.getJSONArray(j)
                            bagian.add(potongan.getString(0) to potongan.optBoolean(1, false))
                        }
                        hasil.add(BlokBadan.ParagrafCampur(
                            bagian = bagian,
                            rata = RataDariJson(b.optString("rata", "justify")),
                            indent = indent,
                            indentKiri = indentKiri,
                            jarakAtas = jarakAtas,
                            jarakBawah = jarakBawah,
                        ))
                    }

                    tabel != null -> {
                        val baris = mutableListOf<Pair<String, String>>()
                        for (j in 0 until tabel.length()) {
                            val barisJson = tabel.getJSONArray(j)
                            baris.add(barisJson.getString(0) to barisJson.optString(1, ""))
                        }
                        hasil.add(BlokBadan.Tabel(
                            baris = baris,
                            indentKiri = indentKiri,
                            jarakAtas = jarakAtas,
                            jarakBawah = jarakBawah,
                            tebalkanNama = b.optBoolean("tebalkanNama", true),
                        ))
                    }

                    daftar != null -> {
                        val butir = mutableListOf<String>()
                        for (j in 0 until daftar.length()) butir.add(daftar.getString(j))
                        hasil.add(BlokBadan.Daftar(
                            butir = butir,
                            indentKiri = indentKiri,
                            jarakAtas = jarakAtas,
                            jarakBawah = jarakBawah,
                        ))
                    }

                    kustom.isNotBlank() -> hasil.add(BlokBadan.Kustom(kustom))

                    else -> hasil.add(BlokBadan.Paragraf(
                        teks = b.optString("paragraf", ""),
                        rata = RataDariJson(b.optString("rata", "justify")),
                        tebal = b.optBoolean("tebal", false),
                        miring = b.optBoolean("miring", false),
                        indent = indent,
                        indentKiri = indentKiri,
                        jarakAtas = jarakAtas,
                        jarakBawah = jarakBawah,
                    ))
                }
            }
            return hasil
        }

        private fun RataDariJson(nilai: String): RataBlok = when (nilai.lowercase()) {
            "kiri" -> RataBlok.KIRI
            "tengah" -> RataBlok.TENGAH
            "kanan" -> RataBlok.KANAN
            else -> RataBlok.JUSTIFY
        }

        private fun BidangDariJson(b: JSONObject): SkemaBidang {
            val key = b.optString("key")
            val opsi = mutableListOf<String>()
            val arrOpsi = b.optJSONArray("options")
            if (arrOpsi != null) {
                for (j in 0 until arrOpsi.length()) opsi.add(arrOpsi.optString(j))
            }

            val anak = mutableListOf<SkemaBidang>()
            val arrAnak = b.optJSONArray("children")
            if (arrAnak != null) {
                for (j in 0 until arrAnak.length()) anak.add(BidangDariJson(arrAnak.getJSONObject(j)))
            }

            return SkemaBidang(
                key = key,
                label = b.optString("label", key),
                type = b.optString("type", "text"),
                target = b.optString("target", key),
                required = b.optBoolean("required", false),
                options = opsi,
                otomatis = b.optBoolean("otomatis", false),
                children = anak,
            )
        }
    }
}

/**
 * Skema formulir dibawa di dalam APK, bukan diunduh.
 *
 * Aplikasi ini sengaja tidak mengirim data apa pun: definisi formulir ikut
 * terpasang bersama APK, jadi perangkat tidak perlu terhubung ke layanan mana
 * pun untuk bekerja. Bila skema di desktop berubah, APK harus diperbarui —
 * konsekuensi yang diambil demi data warga tidak keluar perangkat.
 */
object SkemaLokal {
    private const val BERKAS = "skema-formulir.json"

    @Volatile
    private var cache: Map<String, SkemaJenis>? = null

    @Synchronized
    fun muat(ctx: Context): Map<String, SkemaJenis> {
        cache?.let { return it }

        val teks = ctx.assets.open(BERKAS).bufferedReader().use { it.readText() }
        val akar = JSONObject(teks)
        val peta = mutableMapOf<String, SkemaJenis>()
        for (kunci in akar.keys()) {
            peta[kunci] = SkemaJenis.fromJson(kunci, akar.getJSONObject(kunci))
        }

        cache = peta
        return peta
    }

    /** Jenis surat biasa (bukan NTCR), diurutkan untuk tampilan menu. */
    fun suratBiasa(ctx: Context): List<SkemaJenis> =
        muat(ctx).values.filter { !it.ntcr }.sortedBy { it.label }

    /** Jenis NTCR (N1–N6, N8), diurutkan untuk tampilan menu. */
    fun ntcr(ctx: Context): List<SkemaJenis> =
        muat(ctx).values.filter { it.ntcr }.sortedBy { it.label }
}
