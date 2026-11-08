package dev.sumberjaya.sudes.android

import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.util.Base64
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.io.File

/**
 * Menarik identitas kop surat dan pola nomor dari desktop lewat Supabase.
 *
 * Android tidak menyimpan sendiri identitas desa — nilainya datang dari desktop
 * supaya tidak ada dua salinan yang menyimpang. Logo dikirim sebagai base64 dan
 * disimpan sebagai berkas sementara di perangkat.
 */
object PengaturanSinkron {

    /** Hasil tarikan — agar layar bisa membedakan "belum terbit" dari "gagal". */
    sealed class HasilTarik {
        data class Sukses(val pengaturan: PengaturanSurat) : HasilTarik()
        object BelumTerbit : HasilTarik()
        object SesiKedaluwarsa : HasilTarik()
        object Gagal : HasilTarik()
    }

    /**
     * Tarik pengaturan terbitan desktop dan simpan sebagai salinan lokal.
     * [HasilTarik.BelumTerbit] berarti desktop belum pernah menerbitkan kop;
     * [HasilTarik.Gagal] berarti perangkat belum dipasang atau jaringan gagal.
     */
    suspend fun tarik(ctx: Context): HasilTarik = withContext(Dispatchers.IO) {
        val sesi = SupabaseSesi.ambil(ctx)
        if (!sesi.masuk) return@withContext HasilTarik.Gagal

        val klien = SupabaseSesi.klien(ctx)

        // Skema formulir ikut ditarik: desktop adalah sumbernya, dan tanpa
        // langkah ini tombol "Terbitkan skema" di desktop tidak berpengaruh
        // apa pun ke perangkat. Kegagalannya tidak membatalkan pengaturan.
        try {
            val skema = klien.muatSkemaJson()
            if (skema != null) SkemaLokal.simpan(ctx, skema)
        } catch (e: Exception) {
            // Skema lama (salinan lokal/aset APK) tetap dipakai.
        }

        val muatan = try {
            klien.muatPengaturan()
        } catch (e: SupabaseKlien.SesiKedaluwarsa) {
            return@withContext HasilTarik.SesiKedaluwarsa
        } catch (e: Exception) {
            return@withContext HasilTarik.Gagal
        } ?: return@withContext HasilTarik.BelumTerbit

        val kop = muatan.optJSONObject("kop") ?: JSONObject()
        val nomor = muatan.optJSONObject("nomor") ?: JSONObject()

        val format = mutableMapOf<String, String>()
        val formatJson = nomor.optJSONObject("format")
        if (formatJson != null) {
            for (k in formatJson.keys()) {
                val v = formatJson.optString(k, "")
                if (v.isNotBlank()) format[k] = v
            }
        }

        val logoPath = simpanLogo(ctx, kop.optString("logo", ""))

        val lama = PengaturanSurat.ambil(ctx)
        val hasil = PengaturanSurat(
            namaDesa = kop.optString("namaDesa", ""),
            kecamatan = kop.optString("kecamatan", ""),
            kabupaten = kop.optString("kabupaten", ""),
            jenisKabupaten = kop.optString("jenisKabupaten", "Kabupaten").ifBlank { "Kabupaten" },
            jenisLembaga = kop.optString("jenisLembaga", "Desa").ifBlank { "Desa" },
            alamat = kop.optString("alamat", ""),
            kodepos = kop.optString("kodepos", ""),
            email = kop.optString("email", ""),
            kepalaDesa = kop.optString("kepalaDesa", ""),
            sekretarisDesa = kop.optString("sekretarisDesa", ""),
            nipKepalaDesa = kop.optString("nipKepalaDesa", ""),
            awalanNomor = lama.awalanNomor,
            // Nomor urut lokal yang lebih tinggi MENANG: sinkron ulang tiap kali
            // aplikasi dibuka tidak boleh membuat nomor surat mundur.
            nomorTerakhir = maxOf(lama.nomorTerakhir, nomor.optInt("urutTerakhir", 0)),
            ukuranKertas = kop.optString("ukuranKertas", "A4").ifBlank { "A4" },
            logoPath = logoPath,
            formatNomor = format,
        )

        PengaturanSurat.simpan(ctx, hasil)
        HasilTarik.Sukses(hasil)
    }

    /** Simpan logo base64 ke berkas lokal; kosong bila tidak ada/rusak. */
    private fun simpanLogo(ctx: Context, base64: String): String {
        if (base64.isBlank()) return ""
        return try {
            val bytes = Base64.decode(base64, Base64.DEFAULT)
            val bitmap = BitmapFactory.decodeByteArray(bytes, 0, bytes.size) ?: return ""
            val berkas = File(File(ctx.cacheDir, "kop").apply { mkdirs() }, "logo.png")
            berkas.outputStream().use { bitmap.compress(Bitmap.CompressFormat.PNG, 100, it) }
            berkas.absolutePath
        } catch (e: Exception) {
            ""
        }
    }
}
