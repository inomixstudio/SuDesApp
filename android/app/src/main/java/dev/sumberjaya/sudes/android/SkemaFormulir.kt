package dev.sumberjaya.sudes.android

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
)

/** Skema formulir satu jenis surat. */
data class SkemaJenis(
    val idJenis: String,
    val label: String,
    val fields: List<SkemaBidang>,
) {
    /** Field yang diisi operator, berurutan; field otomatis dilewati. */
    val fieldsInput: List<SkemaBidang> get() = fields.filter { !it.otomatis }

    companion object {
        fun fromJson(idJenis: String, json: JSONObject): SkemaJenis {
            val label = json.optString("label", idJenis)
            val daftar = mutableListOf<SkemaBidang>()
            val arr = json.optJSONArray("fields") ?: JSONArray()
            for (i in 0 until arr.length()) {
                val b = arr.getJSONObject(i)
                val key = b.optString("key")
                if (key.isEmpty()) continue
                val opsi = mutableListOf<String>()
                val arrOpsi = b.optJSONArray("options")
                if (arrOpsi != null) {
                    for (j in 0 until arrOpsi.length()) opsi.add(arrOpsi.optString(j))
                }
                daftar.add(
                    SkemaBidang(
                        key = key,
                        label = b.optString("label", key),
                        type = b.optString("type", "text"),
                        target = b.optString("target", key),
                        required = b.optBoolean("required", false),
                        options = opsi,
                        otomatis = b.optBoolean("otomatis", false),
                    )
                )
            }
            return SkemaJenis(idJenis, label, daftar)
        }
    }
}
