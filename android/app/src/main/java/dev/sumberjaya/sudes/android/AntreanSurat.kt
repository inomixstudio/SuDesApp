package dev.sumberjaya.sudes.android

import android.content.Context
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.io.File
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.UUID

/**
 * Kirim surat yang dibuat di perangkat ke antrean Supabase untuk desktop.
 *
 * Isi kirimannya hanya isian formulir (nomor, NIK, dan kolom) — cukup bagi
 * desktop untuk melanjutkan penomoran resmi dan membuat surat di register.
 * Nomor dan NIK boleh dobel antar-perangkat; desktop yang menormalkan.
 *
 * Bila perangkat belum dipasang atau jaringan gagal, isian **tidak dibuang**:
 * surat disimpan sebagai berkas tertunda di folder aplikasi lalu dicoba lagi
 * saat menu dibuka atau saat operator menekan "Kirim surat tertunda". PDF
 * tetap tersimpan di perangkat, jadi surat tidak pernah hilang karena
 * sinkronisasi.
 */
object AntreanSurat {

    private const val FOLDER = "antrean-tertunda"

    /**
     * Kirim satu surat. Mengembalikan true bila langsung masuk antrean.
     * Kegagalan disimpan sebagai surat tertunda, bukan dilaporkan hilang.
     */
    suspend fun kirim(
        ctx: Context,
        jenis: SkemaJenis,
        nomor: String,
        fields: JSONObject,
    ): Boolean = withContext(Dispatchers.IO) {
        val tanggal = capWaktu()
        val uuid = UUID.randomUUID().toString()

        if (!SupabaseSesi.ambil(ctx).masuk) {
            simpanTertunda(ctx, jenis.idJenis, nomor, fields, tanggal, uuid)
            return@withContext false
        }

        val berhasil = kirimKe(ctx, jenis.idJenis, nomor, fields, tanggal, uuid)
        if (!berhasil) simpanTertunda(ctx, jenis.idJenis, nomor, fields, tanggal, uuid)
        berhasil
    }

    /** Jumlah surat yang tertahan di perangkat karena belum terkirim. */
    fun jumlahTertunda(ctx: Context): Int =
        folder(ctx).listFiles()?.count { it.isFile } ?: 0

    /**
     * Coba kirim semua surat tertunda, terlama lebih dulu. Berhenti pada
     * kegagalan pertama supaya tidak menunggu timeout berkali-kali saat
     * jaringan masih terputus. Mengembalikan jumlah yang berhasil terkirim.
     */
    suspend fun kirimTertunda(ctx: Context): Int = withContext(Dispatchers.IO) {
        if (!SupabaseSesi.ambil(ctx).masuk) return@withContext 0

        var terkirim = 0
        val daftar = folder(ctx).listFiles()
            ?.filter { it.isFile }
            ?.sortedBy { it.name }
            ?: return@withContext 0

        for (berkas in daftar) {
            val isi = try {
                JSONObject(berkas.readText())
            } catch (e: Exception) {
                null
            }

            if (isi == null || isi.optJSONObject("fields") == null) {
                // Jangan hapus diam-diam: pindahkan ke folder gagal supaya
                // bisa diperiksa operator, bukan hilang tanpa jejak.
                try {
                    val folderGagal = File(ctx.filesDir, "$FOLDER-gagal").apply { mkdirs() }
                    berkas.renameTo(File(folderGagal, berkas.name))
                } catch (e: Exception) {
                    // Bila pemindahan gagal, biarkan berkas untuk pemeriksaan manual.
                }
                continue
            }

            val fields = isi.optJSONObject("fields")!!

            val berhasil = try {
                kirimKe(
                    ctx = ctx,
                    idJenis = isi.optString("id_jenis"),
                    nomor = isi.optString("nomor").ifBlank { null },
                    fields = fields,
                    tanggal = isi.optString("tanggal").ifBlank { capWaktu() },
                    clientUuid = isi.optString("client_uuid").ifBlank { null },
                )
            } catch (e: Exception) {
                false
            }

            if (!berhasil) break

            berkas.delete()
            terkirim++
        }

        terkirim
    }

    private suspend fun kirimKe(
        ctx: Context,
        idJenis: String,
        nomor: String?,
        fields: JSONObject,
        tanggal: String,
        clientUuid: String? = null,
    ): Boolean {
        val klien = SupabaseSesi.klien(ctx)
        return when (
            klien.kirimSurat(
                idJenis = idJenis,
                nomorSementara = nomor,
                nik = fields.optString("nik", "").ifBlank { null },
                fields = fields,
                tanggal = tanggal,
                clientUuid = clientUuid,
            )
        ) {
            is SupabaseKlien.Hasil.Sukses -> true
            else -> false
        }
    }

    private fun simpanTertunda(
        ctx: Context,
        idJenis: String,
        nomor: String,
        fields: JSONObject,
        tanggal: String,
        clientUuid: String? = null,
    ) {
        try {
            val isi = JSONObject()
                .put("id_jenis", idJenis)
                .put("nomor", nomor)
                .put("tanggal", tanggal)
                .put("client_uuid", clientUuid ?: UUID.randomUUID().toString())
                .put("fields", fields)

            val berkas = File(folder(ctx), "${System.currentTimeMillis()}-${UUID.randomUUID()}.json")
            berkas.writeText(isi.toString())
        } catch (e: Exception) {
            // Penyimpanan penuh atau ditolak: PDF di Downloads tetap ada, jadi
            // surat masih bisa dicetak dan isiannya diketik ulang bila perlu.
        }
    }

    private fun folder(ctx: Context): File =
        File(ctx.filesDir, FOLDER).apply { mkdirs() }

    private fun capWaktu(): String =
        SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ssZ", Locale.US).format(Date())
}
