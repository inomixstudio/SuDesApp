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

/** Skema formulir satu jenis surat. */
data class SkemaJenis(
    val idJenis: String,
    val label: String,
    val fields: List<SkemaBidang>,
) {
    /** Field yang diisi operator, berurutan; field otomatis dilewati. */
    val fieldsInput: List<SkemaBidang> get() = fields.filter { !it.otomatis }

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
            return SkemaJenis(idJenis, label, daftar)
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
