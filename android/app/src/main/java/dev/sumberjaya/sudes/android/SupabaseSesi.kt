package dev.sumberjaya.sudes.android

import android.content.Context
import android.content.SharedPreferences
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKey

/**
 * Sesi Supabase — EncryptedSharedPreferences (kunci di Android Keystore).
 * Kata sandi dan token tidak lagi plaintext di SharedPreferences biasa.
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

        private fun prefs(ctx: Context): SharedPreferences {
            val masterKey = MasterKey.Builder(ctx).setKeyScheme(MasterKey.KeyScheme.AES256_GCM).build()
            return EncryptedSharedPreferences.create(
                ctx, PREFS, masterKey,
                EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
                EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM,
            )
        }

        fun ambil(ctx: Context): SupabaseSesi {
            val p = prefs(ctx)
            if (p.getString(K_EMAIL, null) == null) {
                val lama = ctx.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
                val emailLama = lama.getString(K_EMAIL, null)
                if (emailLama != null) {
                    p.edit()
                        .putString(K_URL, lama.getString(K_URL, ""))
                        .putString(K_KUNCI, lama.getString(K_KUNCI, ""))
                        .putString(K_EMAIL, emailLama)
                        .putString(K_SANDI, lama.getString(K_SANDI, ""))
                        .putString(K_ACCESS, lama.getString(K_ACCESS, ""))
                        .putString(K_REFRESH, lama.getString(K_REFRESH, ""))
                        .putString(K_PERANGKAT, lama.getString(K_PERANGKAT, null))
                        .putString(K_TENANT, lama.getString(K_TENANT, null))
                        .apply()
                    lama.edit().clear().apply()
                }
            }
            return SupabaseSesi(
                // URL dan kunci publik sudah terpasang di dalam APK — operator
                // tidak perlu mengetiknya. Bila QR memuat keduanya, nilai itu
                // yang dipakai (disimpan saat perangkat dipasang).
                url = (p.getString(K_URL, "") ?: "").ifBlank {
                    dev.sumberjaya.sudes.android.BuildConfig.SUPABASE_URL
                },
                kunciPublik = (p.getString(K_KUNCI, "") ?: "").ifBlank {
                    dev.sumberjaya.sudes.android.BuildConfig.SUPABASE_ANON_KEY
                },
                email = p.getString(K_EMAIL, "") ?: "",
                kataSandi = p.getString(K_SANDI, "") ?: "",
                accessToken = p.getString(K_ACCESS, "") ?: "",
                refreshToken = p.getString(K_REFRESH, "") ?: "",
                idPerangkat = p.getString(K_PERANGKAT, null),
                tenant = p.getString(K_TENANT, null),
            )
        }

        fun simpan(ctx: Context, sesi: SupabaseSesi) {
            prefs(ctx).edit()
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
            prefs(ctx).edit().clear().apply()
        }

        /**
         * Klien siap pakai untuk aktivitas: sesi dibaca dari penyimpanan, dan
         * bila klien menyegarkan token, sesi baru langsung disimpan kembali.
         */
        fun klien(ctx: Context): SupabaseKlien =
            SupabaseKlien(ambil(ctx)) { sesiBaru -> simpan(ctx, sesiBaru) }
    }
}
