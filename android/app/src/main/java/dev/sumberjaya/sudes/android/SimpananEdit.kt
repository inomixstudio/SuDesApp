package dev.sumberjaya.sudes.android

import android.content.Context
import org.json.JSONObject
import java.io.File

/**
 * Tangkapan isian surat untuk dibuka kembali lewat Register → Edit.
 *
 * PDF di Downloads hanya menyimpan hasil render; isian aslinya tetap di
 * antrean Supabase atau berkas tertunda. Kalau operator salah ketik, mereka
 * perlu mengubah tanpa membuat surat dari awal. Simpan di sini:
 * `files/register-edit/<nama-pdf-tanpa-ekstensi>.enc` (terenkripsi seperti
 * berkas tertunda karena memuat NIK).
 *
 * Surat lama yang dibuat sebelum fitur ini tidak punya tangkapan, sehingga
 * tombol Edit tidak tersedia (buat ulang lewat Buat Surat).
 */
object SimpananEdit {

    private const val FOLDER = "register-edit"

    fun folder(ctx: Context): File =
        File(ctx.filesDir, FOLDER).apply { mkdirs() }

    /** Simpan tangkapan isian untuk [namaPdf] (mis. `surat-….pdf`). */
    fun simpan(
        ctx: Context,
        namaPdf: String,
        idJenis: String,
        nomor: String,
        bidang: JSONObject,
        pejabat: String,
    ) {
        try {
            val isi = JSONObject()
                .put("id_jenis", idJenis)
                .put("nomor", nomor)
                .put("pejabat", pejabat)
                .put("fields", bidang)
            val berkas = File(folder(ctx), "${tanpaEkstensi(namaPdf)}.enc")
            AntreanTerenkripsi.tulis(ctx, berkas, isi.toString())
        } catch (e: Exception) {
            // PDF & antrean tetap jalan; edit hanya tidak tersedia.
        }
    }

    /** Baca tangkapan untuk [namaPdf]; null bila tidak ada/rusak. */
    fun ambil(ctx: Context, namaPdf: String): JSONObject? {
        return try {
            val berkas = File(folder(ctx), "${tanpaEkstensi(namaPdf)}.enc")
            AntreanTerenkripsi.baca(ctx, berkas)?.let { JSONObject(it) }
        } catch (e: Exception) {
            null
        }
    }

    /** True bila PDF ini punya tangkapan isian yang bisa diedit. */
    fun bisaEdit(ctx: Context, namaPdf: String): Boolean =
        ambil(ctx, namaPdf) != null

    private fun tanpaEkstensi(namaPdf: String): String =
        namaPdf.removeSuffix(".pdf").removeSuffix(".PDF")
}
