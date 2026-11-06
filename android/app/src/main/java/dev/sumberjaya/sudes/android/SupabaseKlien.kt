package dev.sumberjaya.sudes.android

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONArray
import org.json.JSONObject
import java.util.concurrent.TimeUnit

/**
 * Klien Supabase tipis: Auth (surel/kata sandi) + PostgREST.
 *
 * Sengaja tanpa SDK Supabase — permintaan yang dipakai sedikit dan bentuknya
 * stabil, dan aplikasi Android ini tidak boleh bergantung pada pustaka yang
 * menyeret dependensi besar hanya untuk tiga endpoint.
 */
class SupabaseKlien(private val sesi: SupabaseSesi) {

    private val http = OkHttpClient.Builder()
        .connectTimeout(20, TimeUnit.SECONDS)
        .readTimeout(30, TimeUnit.SECONDS)
        .build()

    private val jsonTipe = "application/json; charset=utf-8".toMediaType()

    /** Hasil yang membedakan "gagal karena jaringan" dari "gagal karena data". */
    sealed class Hasil<out T> {
        data class Sukses<T>(val nilai: T) : Hasil<T>()
        data class Galat(val kode: Int, val pesan: String) : Hasil<Nothing>()
    }

    /** Masuk dengan surel/kata sandi perangkat. Mengembalikan sesi baru bila berhasil. */
    suspend fun login(email: String, kataSandi: String): SupabaseSesi? = withContext(Dispatchers.IO) {
        val bodi = JSONObject()
            .put("email", email)
            .put("password", kataSandi)
            .toString()
            .toRequestBody(jsonTipe)

        val req = Request.Builder()
            .url("${sesi.url}/auth/v1/token?grant_type=password")
            .header("apikey", sesi.kunciPublik)
            .post(bodi)
            .build()

        try {
            http.newCall(req).execute().use { resp ->
                val teks = resp.body?.string().orEmpty()
                if (!resp.isSuccessful) return@withContext null

                val json = JSONObject(teks)
                val user = json.optJSONObject("user")
                sesi.copy(
                    accessToken = json.optString("access_token"),
                    refreshToken = json.optString("refresh_token"),
                    idPerangkat = user?.optString("id"),
                    tenant = user?.optJSONObject("app_metadata")?.optString("tenant_id"),
                )
            }
        } catch (e: Exception) {
            null
        }
    }

    /** Muat skema formulir desa ini dari Supabase. */
    suspend fun muatSkema(): Map<String, SkemaJenis> = withContext(Dispatchers.IO) {
        val hasil = get("${sesi.url}/rest/v1/skema_formulir?select=id_jenis,skema")
        val peta = mutableMapOf<String, SkemaJenis>()
        val array = JSONArray(hasil)
        for (i in 0 until array.length()) {
            val item = array.getJSONObject(i)
            val id = item.optString("id_jenis")
            val skema = item.optJSONObject("skema") ?: continue
            peta[id] = SkemaJenis.fromJson(id, skema)
        }
        peta
    }

    /** Daftar surat milik perangkat ini yang masih di antrean. */
    suspend fun daftarAntrean(): List<AntreanBaris> = withContext(Dispatchers.IO) {
        val hasil = get(
            "${sesi.url}/rest/v1/antrean_surat" +
                "?select=id,id_jenis,nomor_sementara,nik,status,dibuat_pada,catatan" +
                "&order=dibuat_pada.desc"
        )
        val daftar = mutableListOf<AntreanBaris>()
        val array = JSONArray(hasil)
        for (i in 0 until array.length()) {
            val item = array.getJSONObject(i)
            daftar.add(
                AntreanBaris(
                    id = item.optString("id"),
                    idJenis = item.optString("id_jenis"),
                    nomorSementara = item.optString("nomor_sementara"),
                    nik = item.optString("nik"),
                    status = item.optString("status"),
                    dibuatPada = item.optString("dibuat_pada"),
                    catatan = item.optString("catatan"),
                )
            )
        }
        daftar
    }

    /**
     * Kirim satu surat ke antrean. Nomor dan NIK boleh dobel — desktop yang
     * menormalkan saat impor, jadi Android tidak perlu menghitung nomor.
     */
    suspend fun kirimSurat(
        idJenis: String,
        nomorSementara: String?,
        nik: String?,
        fields: JSONObject,
        tanggal: String,
    ): Hasil<String> = withContext(Dispatchers.IO) {
        val bodi = JSONObject()
            .put("id_jenis", idJenis)
            .put("nomor_sementara", nomorSementara ?: JSONObject.NULL)
            .put("nik", nik ?: JSONObject.NULL)
            .put("data_json", JSONObject().put("tanggal", tanggal).put("fields", fields))
            .toString()
            .toRequestBody(jsonTipe)

        val req = builder()
            .url("${sesi.url}/rest/v1/antrean_surat")
            .header("Prefer", "return=representation")
            .post(bodi)
            .build()

        try {
            http.newCall(req).execute().use { resp ->
                val teks = resp.body?.string().orEmpty()
                if (resp.isSuccessful) {
                    Hasil.Sukses(teks)
                } else {
                    Hasil.Galat(resp.code, teks)
                }
            }
        } catch (e: Exception) {
            Hasil.Galat(-1, e.message ?: " jaringan")
        }
    }

    suspend fun hitungAntrean(): Int? = withContext(Dispatchers.IO) {
        try {
            val req = builder()
                .url("${sesi.url}/rest/v1/antrean_surat?select=id")
                .build()
            http.newCall(req).execute().use { resp ->
                if (!resp.isSuccessful) return@withContext null
                JSONArray(resp.body?.string() ?: "[]").length()
            }
        } catch (e: Exception) {
            null
        }
    }

    private fun builder(): Request.Builder =
        Request.Builder()
            .header("apikey", sesi.kunciPublik)
            .header("Authorization", "Bearer ${sesi.accessToken}")

    private fun get(url: String): String {
        val req = builder().url(url).build()
        http.newCall(req).execute().use { resp ->
            if (!resp.isSuccessful) throw IllegalStateException("HTTP ${resp.code}")
            return resp.body?.string() ?: "[]"
        }
    }
}
