package dev.sumberjaya.sudes.android

import android.content.Context
import java.io.File
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

/**
 * Enkripsi berkas antrean tertunda (berisi NIK & data penduduk).
 *
 * Berkas baru memakai AES/GCM dengan kunci di Android Keystore
 * (alias `sudes_antrean_tertunda`); file berekstensi `.enc` menyimpan
 * IV (12 byte) + ciphertext. Berkas lama plaintext (`{…`) dibaca apa
 * adanya dan dimigrasikan saat `AntreanSurat.kirimTertunda` berjalan,
 * supaya data lama tidak tertinggal plaintext selamanya.
 */
object AntreanTerenkripsi {

    private const val ALIAS = "sudes_antrean_tertunda"
    private const val IV = 12
    private const val TAG_BIT = 128

    /** Baca isi (plaintext) berkas tertunda; null bila rusak/terkunci. */
    fun baca(ctx: Context, berkas: File): String? {
        return try {
            val mentah = berkas.readBytes()
            if (mentah.isEmpty()) return null
            if (mentah.first() == '{'.code.toByte()) {
                String(mentah, Charsets.UTF_8)
            } else {
                String(dekripsi(ctx, mentah), Charsets.UTF_8)
            }
        } catch (e: Exception) {
            null
        }
    }

    /** Tulis plaintext sebagai berkas `.enc` terenkripsi; jatuh ke plaintext bila Keystore gagal. */
    fun tulis(ctx: Context, berkas: File, isi: String) {
        try {
            berkas.writeBytes(enkripsi(ctx, isi.toByteArray(Charsets.UTF_8)))
        } catch (e: Exception) {
            // Beberapa perangkat/OEM menolak kunci AES/GCM Keystore
            // (mis. pad NONE pada API lama). Jangan hilangkan surat diam-diam:
            // simpan plaintext supaya tetap bisa dikirim ulang. Baca() sudah
            // mendukung bentuk `{…` apa adanya.
            try {
                berkas.writeBytes(isi.toByteArray(Charsets.UTF_8))
            } catch (e2: Exception) {
                throw e2
            }
        }
    }

    private fun enkripsi(ctx: Context, teks: ByteArray): ByteArray {
        val kunci = kunci(ctx)
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.ENCRYPT_MODE, kunci)
        val iv = cipher.iv
        return iv + cipher.doFinal(teks)
    }

    private fun dekripsi(ctx: Context, muatan: ByteArray): ByteArray {
        require(muatan.size > IV) { "Muatan terlalu pendek" }
        val iv = muatan.copyOfRange(0, IV)
        val ciph = muatan.copyOfRange(IV, muatan.size)
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.DECRYPT_MODE, kunci(ctx), GCMParameterSpec(TAG_BIT, iv))
        return cipher.doFinal(ciph)
    }

    private fun kunci(ctx: Context): SecretKey {
        val ks = java.security.KeyStore.getInstance("AndroidKeyStore")
        ks.load(null)
        if (ks.containsAlias(ALIAS)) {
            val entri = ks.getEntry(ALIAS, null)
            if (entri is java.security.KeyStore.SecretKeyEntry) {
                return entri.secretKey
            }
        }
        val generator = KeyGenerator.getInstance("AES", "AndroidKeyStore")
        val spec = android.security.keystore.KeyGenParameterSpec.Builder(
            ALIAS,
            android.security.keystore.KeyProperties.PURPOSE_ENCRYPT or
                android.security.keystore.KeyProperties.PURPOSE_DECRYPT,
        )
            .setBlockModes(android.security.keystore.KeyProperties.BLOCK_MODE_GCM)
            .setEncryptionPaddings(android.security.keystore.KeyProperties.ENCRYPTION_PADDING_NONE)
            .setKeySize(256)
            .build()
        generator.init(spec)
        return generator.generateKey()
    }
}
