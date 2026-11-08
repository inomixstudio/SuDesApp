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

    /** Akhiri sesi di server (auth/v1/logout) agar refresh token dicabut. */
    suspend fun logoutServer(): Boolean = withContext(Dispatchers.IO) {
        try {
            val permintaan = Request.Builder()
                .url("${sesi.url}/auth/v1/logout")
                .post("".toRequestBody(null))
                .addHeader("apikey", sesi.kunciPublik)
                .addHeader("Authorization", "Bearer ${sesi.accessToken}")
                .build()
            http.newCall(permintaan).execute().use { it.isSuccessful }
        } catch (e: Exception) {
            false
        }
    }

    private val jsonTipe = "application/json; charset=utf-8".toMediaType()

    /** Hasil yang membedakan "gagal karena jaringan" dari "gagal karena data". */
    sealed class Hasil<out T> {
        data class Sukses<T>(val nilai: T) : Hasil<T>()
        data class Galat(val kode: Int, val pesan: String) : Hasil<Nothing>()
    }

    /**
     * Sesi perangkat tidak berlaku lagi (token kedaluwarsa dan penyegaran
     * gagal, atau server menolak dengan 401). Dibedakan dari galat jaringan
     * supaya operator diminta memasang ulang perangkat, bukan menunggu
     * koneksi.
     */
    class SesiKedaluwarsa(pesan: String = "Sesi perangkat tidak berlaku.") : Exception(pesan)

    /** Hasil penukaran kode pasangan — pesannya ditampilkan ke operator. */
    sealed class HasilKode {
        data class Sukses(val sesi: SupabaseSesi) : HasilKode()
        data class Galat(val pesan: String) : HasilKode()
    }

    /**
     * Tukar kode verifikasi 4 angka dengan surel + kata sandi perangkat.
     * Kode hanya berlaku 10 menit, hanya sekali pakai, dan dibatasi 10
     * percobaan per 15 menit per alamat IP (lihat migrasi
     * 20261110000000_batas_percobaan_pasangan.sql). Pesan galat dari server
     * diteruskan apa adanya supaya operator tahu apakah harus menunggu atau
     * minta kode baru.
     */
    suspend fun pakaiKode(kode: String): HasilKode = withContext(Dispatchers.IO) {
        val bersih = kode.trim()
        if (bersih.length != 4 || !bersih.all { it.isDigit() }) {
            return@withContext HasilKode.Galat("Kode harus 4 angka.")
        }

        val bodi = JSONObject().put("p_kode", bersih).toString().toRequestBody(jsonTipe)
        val req = Request.Builder()
            .url("${sesi.url}/rest/v1/rpc/ambil_pasangan")
            .header("apikey", sesi.kunciPublik)
            .post(bodi)
            .build()

        try {
            http.newCall(req).execute().use { resp ->
                val teks = resp.body?.string().orEmpty()
                if (!resp.isSuccessful) {
                    return@withContext HasilKode.Galat(pesanGalat(teks, resp.code))
                }

                val arr = JSONArray(teks)
                if (arr.length() == 0) {
                    return@withContext HasilKode.Galat(
                        "Kode tidak dikenal atau sudah kedaluwarsa. Minta kode baru di aplikasi desktop."
                    )
                }

                val o = arr.getJSONObject(0)
                val s = sesi.copy(
                    email = o.optString("email"),
                    kataSandi = o.optString("kata_sandi"),
                    accessToken = "",
                    refreshToken = "",
                )
                val sesiBaru = SupabaseKlien(s).login(s.email, s.kataSandi)
                if (sesiBaru == null) {
                    HasilKode.Galat("Kode benar, tetapi masuk ke Supabase gagal. Periksa koneksi lalu coba lagi.")
                } else {
                    HasilKode.Sukses(sesiBaru)
                }
            }
        } catch (e: Exception) {
            HasilKode.Galat("Tidak bisa menghubungi server: ${e.message ?: "jaringan"}")
        }
    }

    /** Ambil pesan galat PostgREST ({"message": ...}) atau jatuh ke kode HTTP. */
    private fun pesanGalat(teks: String, kode: Int): String = try {
        val o = JSONObject(teks)
        o.optString("message").ifBlank { o.optString("msg") }.ifBlank { "HTTP $kode" }
    } catch (e: Exception) {
        "HTTP $kode"
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
     * Null berarti desktop belum menerbitkan; kegagalan jaringan dilempar
     * sebagai pengecualian supaya pemanggil bisa membedakan keduanya.
     */
    suspend fun muatPengaturan(): JSONObject? = withContext(Dispatchers.IO) {
        pastikanTokenSegar()
        val array = JSONArray(get("${sesi.url}/rest/v1/pengaturan_desa?select=kop,nomor&limit=1"))
        if (array.length() == 0) return@withContext null
        array.optJSONObject(0)
    }

    /**
     * Muat skema formulir terbitan desktop dalam bentuk JSON mentah
     * ({"ID_JENIS": {...}}), siap disimpan sebagai salinan lokal oleh
     * [SkemaLokal]. Null bila perangkat belum dipasang, jaringan gagal, atau
     * desktop belum pernah menerbitkan skema.
     */
    suspend fun muatSkemaJson(): String? = withContext(Dispatchers.IO) {
        pastikanTokenSegar()
        // get() melempar pada non-2xx; tanpa tangkapan, pengecualian keluar dari
        // coroutine dan menjatuhkan aplikasi saat token sudah tidak berlaku.
        // SesiKedaluwarsa dilempar ulang supaya pemanggil bisa meminta
        // pemasangan ulang (bukan mengira skema kosong).
        val hasil = try {
            get("${sesi.url}/rest/v1/skema_formulir?select=id_jenis,skema")
        } catch (e: SesiKedaluwarsa) {
            throw e
        } catch (e: Exception) {
            return@withContext null
        }

        val array = JSONArray(hasil)
        if (array.length() == 0) return@withContext null

        val akar = JSONObject()
        for (i in 0 until array.length()) {
            val item = array.optJSONObject(i) ?: continue
            val id = item.optString("id_jenis")
            val skema = item.optJSONObject("skema") ?: continue
            if (id.isBlank()) continue
            akar.put(id, skema)
        }

        if (akar.length() == 0) null else akar.toString()
    }

    /**
     * Daftar surat milik perangkat ini yang masih di antrean. Null bila
     * daftar tidak bisa diambil — tampilan harus membedakan "kosong" dari
     * "tidak terjangkau", bukan menampilkan 0 seolah antrean benar-benar
     * kosong.
     */
    suspend fun daftarAntrean(): List<AntreanBaris>? = withContext(Dispatchers.IO) {
        pastikanTokenSegar()
        val hasil = try {
            get(
                "${sesi.url}/rest/v1/antrean_surat" +
                    "?select=id,id_jenis,nomor_sementara,nik,status,dibuat_pada,catatan" +
                    "&order=dibuat_pada.desc"
            )
        } catch (e: SesiKedaluwarsa) {
            // Bukan soal jaringan — tampilan perlu meminta pemasangan ulang.
            throw e
        } catch (e: Exception) {
            return@withContext null
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
        clientUuid: String? = null,
    ): Hasil<String> = withContext(Dispatchers.IO) {
        pastikanTokenSegar()
        val bodi = JSONObject()
            .put("id_jenis", idJenis)
            .put("nomor_sementara", nomorSementara ?: JSONObject.NULL)
            .put("nik", nik ?: JSONObject.NULL)
            // RLS mencentang tenant_id dan id_perangkat terhadap klaim JWT,
            // jadi klien wajib mengirim keduanya (RLS menolak yang tidak
            // cocok). id_pengguna hanya metadata.
            // client_uuid adalah kunci idempotensi: dibuat sekali per surat,
            // dipakai ulang saat retry agar timeout tidak menduplikasi surat.
            .put("client_uuid", clientUuid ?: java.util.UUID.randomUUID().toString())
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
                } else if (resp.code == 409) {
                    // Duplikat client_uuid: sudah ada di server. Anggap sukses
                    // lokal agar tidak memblokir antrean tertunda selamanya.
                    Hasil.Sukses(teks)
                } else {
                    // Pesan galat trigger (mis. antrean penuh) diurai supaya
                    // operator mendapat tahu penyebabnya, bukan "HTTP 400".
                    Hasil.Galat(resp.code, pesanGalat(teks, resp.code))
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

    /**
     * True bila perangkat ini masih aktif di desktop; false bila sudah
     * dinonaktifkan (surat baru akan ditolak RLS 403); null bila tidak
     * diketahui (jaringan / belum dipasang).
     */
    suspend fun perangkatAktif(): Boolean? = withContext(Dispatchers.IO) {
        pastikanTokenSegar()
        val id = sesi.idPerangkat ?: return@withContext null
        try {
            val req = builder()
                .url("${sesi.url}/rest/v1/perangkat_android?select=aktif&id=eq.$id&limit=1")
                .build()
            http.newCall(req).execute().use { resp ->
                if (!resp.isSuccessful) return@withContext null
                val array = JSONArray(resp.body?.string() ?: "[]")
                if (array.length() == 0) return@withContext null
                array.optJSONObject(0)?.optBoolean("aktif", false)
            }
        } catch (e: Exception) {
            null
        }
    }

    /** Hapus satu baris antrean milik perangkat ini (butuh policy hapus sederhana). */
    suspend fun hapusAntrean(id: String): Boolean = withContext(Dispatchers.IO) {
        pastikanTokenSegar()
        try {
            val req = builder()
                .url("${sesi.url}/rest/v1/antrean_surat?id=eq.$id")
                .delete()
                .build()
            http.newCall(req).execute().use { it.isSuccessful }
        } catch (e: Exception) {
            false
        }
    }

    /**
     * Laporkan bahwa perangkat ini sedang dipakai. Desktop memakai
     * `terakhir_online` untuk menampilkan "Android terhubung" di status bar;
     * RPC hanya memperbarui baris perangkat pemilik token (lihat migrasi
     * 20261110000001_perangkat_tunggal_dan_status_online.sql).
     */
    suspend fun tandaiOnline(): Boolean = withContext(Dispatchers.IO) {
        try {
            pastikanTokenSegar()
            val req = builder()
                .url("${sesi.url}/rest/v1/rpc/tandai_perangkat_online")
                .post("{}".toRequestBody(jsonTipe))
                .build()
            http.newCall(req).execute().use { it.isSuccessful }
        } catch (e: Exception) {
            false
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

        // Token sudah tidak layak pakai dan tidak bisa disegarkan: kirim galat
        // yang jelas, jangan lanjut dengan token mati (hasilnya 401 yang
        // tertelan menjadi "data kosong").
        if (!muatUlangToken() && tokenKedaluwarsa()) {
            throw SesiKedaluwarsa()
        }
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
            if (resp.code == 401) throw SesiKedaluwarsa()
            if (!resp.isSuccessful) throw IllegalStateException("HTTP ${resp.code}")
            return resp.body?.string() ?: "[]"
        }
    }
}
