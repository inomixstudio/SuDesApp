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
class SupabaseKlien(
    private var sesi: SupabaseSesi,
    private val onSesiDiperbarui: ((SupabaseSesi) -> Unit)? = null,
) {

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

    /**
     * Tukar kode verifikasi 4 angka dengan surel + kata sandi perangkat.
     * Kode hanya berlaku 10 menit dan hanya sekali pakai (lihat skema
     * pasangan_sementara). Mengembalikan sesi yang sudah masuk, atau null.
     */
    suspend fun pakaiKode(kode: String): SupabaseSesi? = withContext(Dispatchers.IO) {
        val bersih = kode.trim()
        if (bersih.length != 4 || !bersih.all { it.isDigit() }) return@withContext null

        val bodi = JSONObject().put("p_kode", bersih).toString().toRequestBody(jsonTipe)
        val req = Request.Builder()
            .url("${sesi.url}/rest/v1/rpc/ambil_pasangan")
            .header("apikey", sesi.kunciPublik)
            .post(bodi)
            .build()

        try {
            http.newCall(req).execute().use { resp ->
                if (!resp.isSuccessful) return@withContext null
                val arr = JSONArray(resp.body?.string().orEmpty())
                if (arr.length() == 0) return@withContext null
                val o = arr.getJSONObject(0)
                val s = sesi.copy(
                    email = o.optString("email"),
                    kataSandi = o.optString("kata_sandi"),
                    accessToken = "",
                    refreshToken = "",
                )
                SupabaseKlien(s).login(s.email, s.kataSandi)
            }
        } catch (e: Exception) {
            null
        }
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
                val meta = user?.optJSONObject("app_metadata")
                sesi.copy(
                    accessToken = json.optString("access_token"),
                    refreshToken = json.optString("refresh_token"),
                    // user.id adalah ID akun auth, BUKAN ID perangkat. RLS
                    // membandingkan id_perangkat dengan klaim JWT, jadi salah
                    // sumber di sini membuat seluruh kiriman ditolak 403.
                    idPerangkat = meta?.optString("id_perangkat")?.ifBlank { null },
                    tenant = meta?.optString("tenant_id")?.ifBlank { null },
                )
            }
        } catch (e: Exception) {
            null
        }
    }

    /**
     * Muat identitas kop surat dan pola nomor yang diterbitkan desktop.
     * Bentuk kembalian sama dengan yang ditulis PenerbitPengaturanDesa.
     */
    suspend fun muatPengaturan(): JSONObject? = withContext(Dispatchers.IO) {
        pastikanTokenSegar()
        val hasil = try {
            get("${sesi.url}/rest/v1/pengaturan_desa?select=kop,nomor&limit=1")
        } catch (e: Exception) {
            return@withContext null
        }
        val array = JSONArray(hasil)
        if (array.length() == 0) return@withContext null
        array.optJSONObject(0)
    }

    /** Muat skema formulir desa ini dari Supabase. */
    suspend fun muatSkema(): Map<String, SkemaJenis> = withContext(Dispatchers.IO) {
        pastikanTokenSegar()
        // get() melempar pada non-2xx; tanpa tangkapan, pengecualian keluar dari
        // coroutine dan menjatuhkan aplikasi saat token sudah tidak berlaku.
        val hasil = try {
            get("${sesi.url}/rest/v1/skema_formulir?select=id_jenis,skema")
        } catch (e: Exception) {
            return@withContext emptyMap()
        }
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
        pastikanTokenSegar()
        val hasil = try {
            get(
                "${sesi.url}/rest/v1/antrean_surat" +
                    "?select=id,id_jenis,nomor_sementara,nik,status,dibuat_pada,catatan" +
                    "&order=dibuat_pada.desc"
            )
        } catch (e: Exception) {
            return@withContext emptyList()
        }
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
        pastikanTokenSegar()
        val bodi = JSONObject()
            .put("id_jenis", idJenis)
            .put("nomor_sementara", nomorSementara ?: JSONObject.NULL)
            .put("nik", nik ?: JSONObject.NULL)
            // RLS mencentang tenant_id dan id_perangkat terhadap klaim JWT,
            // jadi klien wajib mengirim keduanya (RLS menolak yang tidak
            // cocok). id_pengguna hanya metadata.
            .put("tenant_id", sesi.tenant ?: JSONObject.NULL)
            .put("id_pengguna", sesi.email)
            .put("id_perangkat", sesi.idPerangkat ?: JSONObject.NULL)
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
        pastikanTokenSegar()
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

    /**
     * Token Supabase kedaluwarsa dalam ~1 jam. Tanpa penyegaran, aplikasi
     * berhenti berfungsi setelah itu dan operator harus memasang ulang
     * perangkat — tidak bisa dibiarkan pada aplikasi yang dipakai sehari-hari.
     */
    private suspend fun pastikanTokenSegar() {
        if (!tokenKedaluwarsa()) return
        muatUlangToken()
    }

    /** True bila token tidak terbaca atau sisa berlakunya kurang dari 5 menit. */
    private fun tokenKedaluwarsa(): Boolean {
        if (sesi.accessToken.isBlank()) return true
        val bagian = sesi.accessToken.split(".")
        if (bagian.size != 3) return true
        return try {
            val payload = String(
                android.util.Base64.decode(
                    bagian[1],
                    android.util.Base64.URL_SAFE or android.util.Base64.NO_WRAP,
                )
            )
            val exp = JSONObject(payload).optLong("exp", 0)
            exp <= System.currentTimeMillis() / 1000 + 300
        } catch (e: Exception) {
            true
        }
    }

    /** Tukar refresh token dengan access token baru. False bila gagal. */
    private suspend fun muatUlangToken(): Boolean {
        if (sesi.refreshToken.isBlank()) return false
        val bodi = JSONObject()
            .put("refresh_token", sesi.refreshToken)
            .toString()
            .toRequestBody(jsonTipe)

        val req = Request.Builder()
            .url("${sesi.url}/auth/v1/token?grant_type=refresh_token")
            .header("apikey", sesi.kunciPublik)
            .post(bodi)
            .build()

        return try {
            http.newCall(req).execute().use { resp ->
                if (!resp.isSuccessful) return false
                val json = JSONObject(resp.body?.string().orEmpty())
                val akses = json.optString("access_token")
                val segarkan = json.optString("refresh_token")
                if (akses.isBlank()) return false

                sesi = sesi.copy(
                    accessToken = akses,
                    refreshToken = if (segarkan.isBlank()) sesi.refreshToken else segarkan,
                )
                onSesiDiperbarui?.invoke(sesi)
                true
            }
        } catch (e: Exception) {
            false
        }
    }

    private fun get(url: String): String {
        val req = builder().url(url).build()
        http.newCall(req).execute().use { resp ->
            if (!resp.isSuccessful) throw IllegalStateException("HTTP ${resp.code}")
            return resp.body?.string() ?: "[]"
        }
    }
}
