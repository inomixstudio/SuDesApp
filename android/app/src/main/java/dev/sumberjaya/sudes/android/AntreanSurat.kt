package dev.sumberjaya.sudes.android

import android.content.Context
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/**
 * Kirim satu surat yang baru dibuat ke antrean Supabase.
 *
 * Isi kirimannya hanya isian formulir (nomor, NIK, dan kolom) — cukup bagi
 * desktop untuk melanjutkan penomoran resmi dan membuat surat di register.
 * Nomor dan NIK boleh dobel antar-perangkat; desktop yang menormalkan.
 *
 * Mengembalikan false bila perangkat belum dipasang atau jaringan gagal —
 * PDF tetap tersimpan, jadi kegagalan sinkron tidak menghilangkan surat.
 */
object AntreanSurat {

    suspend fun kirim(
        ctx: Context,
        jenis: SkemaJenis,
        nomor: String,
        fields: JSONObject,
    ): Boolean = withContext(Dispatchers.IO) {
        val sesi = SupabaseSesi.ambil(ctx)
        if (!sesi.masuk) return@withContext false

        val tanggal = SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ssZ", Locale.US).format(Date())
        val klien = SupabaseSesi.klien(ctx)
        when (
            klien.kirimSurat(
                idJenis = jenis.idJenis,
                nomorSementara = nomor,
                nik = fields.optString("nik", "").ifBlank { null },
                fields = fields,
                tanggal = tanggal,
            )
        ) {
            is SupabaseKlien.Hasil.Sukses -> true
            else -> false
        }
    }
}
