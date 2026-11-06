package dev.sumberjaya.sudes.android

import android.content.Context
import android.content.SharedPreferences
import java.util.concurrent.TimeUnit

/**
 * Sesi Supabase perangkat ini.
 *
 * Aplikasi tidak menyimpan data desa — hanya identitas perangkat untuk
 * membuktikan diri ke Supabase. Bila token kedaluwarsa, [SupabaseKlien]
 * menyegarkannya dengan refresh token; bila gagal, masuk ulang dengan surel
 * dan kata sandi yang disimpan.
 */
data class SupabaseSesi(
    val url: String,
    val kunciPublik: String,
    val email: String,
    val kataSandi: String,
    val accessToken: String,
    val refreshToken: String,
    val idPerangkat: String?,
    val tenant: String?,
) {
    val masuk: Boolean get() = accessToken.isNotBlank()

    companion object {
        private const val PREFS = "sudes_sesi"
        private const val K_URL = "url"
        private const val K_KUNCI = "kunci"
        private const val K_EMAIL = "email"
        private const val K_SANDI = "sandi"
        private const val K_ACCESS = "access"
        private const val K_REFRESH = "refresh"
        private const val K_PERANGKAT = "perangkat"
        private const val K_TENANT = "tenant"

        fun ambil(ctx: Context): SupabaseSesi {
            val p: SharedPreferences = ctx.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
            return SupabaseSesi(
                url = p.getString(K_URL, "") ?: "",
                kunciPublik = p.getString(K_KUNCI, "") ?: "",
                email = p.getString(K_EMAIL, "") ?: "",
                kataSandi = p.getString(K_SANDI, "") ?: "",
                accessToken = p.getString(K_ACCESS, "") ?: "",
                refreshToken = p.getString(K_REFRESH, "") ?: "",
                idPerangkat = p.getString(K_PERANGKAT, null),
                tenant = p.getString(K_TENANT, null),
            )
        }

        fun simpan(ctx: Context, sesi: SupabaseSesi) {
            ctx.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit()
                .putString(K_URL, sesi.url)
                .putString(K_KUNCI, sesi.kunciPublik)
                .putString(K_EMAIL, sesi.email)
                .putString(K_SANDI, sesi.kataSandi)
                .putString(K_ACCESS, sesi.accessToken)
                .putString(K_REFRESH, sesi.refreshToken)
                .putString(K_PERANGKAT, sesi.idPerangkat)
                .putString(K_TENANT, sesi.tenant)
                .apply()
        }

        fun keluar(ctx: Context) {
            ctx.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit().clear().apply()
        }

        /**
         * Susun sesi dari isi QR pairing: URL, kunci publik, surel, kata sandi.
         * QR sengaja memakai empat baris teks biasa supaya Android tidak butuh
         * parser khusus. Fungsi ini suspending karena di dalamnya ada login
         * jaringan — jangan blokir thread utama.
         */
        suspend fun dariQr(ctx: Context, payload: String): SupabaseSesi? {
            val baris = payload.lines().map { it.trim() }.filter { it.isNotEmpty() }
            if (baris.size < 4) return null

            val url = baris[0].trimEnd('/')
            val kunci = baris[1]
            val email = baris[2]
            val sandi = baris[3]

            val dasar = SupabaseSesi.ambil(ctx).copy(
                url = url,
                kunciPublik = kunci,
                email = email,
                kataSandi = sandi,
                accessToken = "",
                refreshToken = "",
            )

            return SupabaseKlien(dasar).login(email, sandi)
        }
    }
}
