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

    /** Hasil kirim satu surat, dibedakan supaya UI bisa memberi tahu yang benar. */
    enum class HasilKirim {
        TERKIRIM,
        TERTUNDA,
        ANTREAN_PENUH,
        PERANGKAT_NONAKTIF,
        SESI_MATI,
    }

    /** Satu baris tertunda lokal untuk daftar status di layar Pengaturan. */
    data class TertundaBaris(
        val berkas: File,
        val idJenis: String,
        val nomor: String,
        val tanggal: String,
    )

    /**
     * Kirim satu surat. Mengembalikan true bila langsung masuk antrean.
     * Kegagalan disimpan sebagai surat tertunda (terenkripsi), bukan
     * dilaporkan hilang.
     */
    suspend fun kirim(
        ctx: Context,
        jenis: SkemaJenis,
        nomor: String,
        fields: JSONObject,
    ): Boolean = kirimHasil(ctx, jenis, nomor, fields) == HasilKirim.TERKIRIM

    /** Sama seperti [kirim] tetapi membedakan sebabnya untuk UI. */
    suspend fun kirimHasil(
        ctx: Context,
        jenis: SkemaJenis,
        nomor: String,
        fields: JSONObject,
    ): HasilKirim = withContext(Dispatchers.IO) {
        val tanggal = capWaktu()
        val uuid = UUID.randomUUID().toString()

        if (!SupabaseSesi.ambil(ctx).masuk) {
            simpanTertunda(ctx, jenis.idJenis, nomor, fields, tanggal, uuid)
            return@withContext HasilKirim.TERTUNDA
        }

        try {
            when (val hasil = kirimKeHasil(ctx, jenis.idJenis, nomor, fields, tanggal, uuid)) {
                is SupabaseKlien.Hasil.Sukses -> HasilKirim.TERKIRIM
                is SupabaseKlien.Hasil.Galat -> {
                    val hasilKirim = petakanGalat(ctx, hasil)
                    // Semua galat tetap disimpan tertunda (terenkripsi) supaya
                    // isian tidak hilang; sebabnya hanya ubah pesan ke operator.
                    simpanTertunda(ctx, jenis.idJenis, nomor, fields, tanggal, uuid)
                    hasilKirim
                }
            }
        } catch (e: SupabaseKlien.SesiKedaluwarsa) {
            simpanTertunda(ctx, jenis.idJenis, nomor, fields, tanggal, uuid)
            HasilKirim.SESI_MATI
        }
    }

    /** Bedakan galat kirim menjadi penuh / perangkat nonaktif / tertunda biasa. */
    private suspend fun petakanGalat(
        ctx: Context,
        galat: SupabaseKlien.Hasil.Galat,
    ): HasilKirim {
        val pesan = galat.pesan.lowercase()
        if ("penuh" in pesan) return HasilKirim.ANTREAN_PENUH
        if (galat.kode == 403) {
            try {
                if (SupabaseSesi.klien(ctx).perangkatAktif() == false) {
                    return HasilKirim.PERANGKAT_NONAKTIF
                }
            } catch (e: Exception) {
                // Biarkan tertunda biasa bila status perangkat tak diketahui.
            }
        }
        return HasilKirim.TERTUNDA
    }

    /** Jumlah surat yang tertahan di perangkat karena belum terkirim. */
    fun jumlahTertunda(ctx: Context): Int =
        folder(ctx).listFiles()?.count { it.isFile } ?: 0

    /** Jumlah berkas gagal yang dipindah (rusak / format tak dikenal) — untuk UI terlihat. */
    fun jumlahGagal(ctx: Context): Int =
        File(ctx.filesDir, "$FOLDER-gagal").listFiles()?.count { it.isFile } ?: 0

    /**
     * Coba kirim semua surat tertunda, terlama lebih dulu. Berhenti pada
     * kegagalan pertama supaya tidak menunggu timeout berkali-kali saat
     * jaringan masih terputus. Mengembalikan jumlah yang berhasil terkirim.
     * Berkas lama plaintext dimigrasikan ke terenkripsi lebih dulu.
     */
    suspend fun kirimTertunda(ctx: Context): Int = withContext(Dispatchers.IO) {
        if (!SupabaseSesi.ambil(ctx).masuk) return@withContext 0
        migrasikanLama(ctx)

        var terkirim = 0
        val daftar = folder(ctx).listFiles()
            ?.filter { it.isFile }
            ?.sortedBy { it.name }
            ?: return@withContext 0

        for (berkas in daftar) {
            val isi = try {
                AntreanTerenkripsi.baca(ctx, berkas)?.let { JSONObject(it) }
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

    /**
     * Daftar surat tertunda untuk tampilan (jenis, nomor, tanggal).
     * Berkas rusak dilewati — dihitung lewat [jumlahGagal].
     */
    fun daftarTertunda(ctx: Context): List<TertundaBaris> {
        val hasil = mutableListOf<TertundaBaris>()
        for (berkas in folder(ctx).listFiles()?.filter { it.isFile }?.sortedBy { it.name }.orEmpty()) {
            val isi = try {
                AntreanTerenkripsi.baca(ctx, berkas)?.let { JSONObject(it) }
            } catch (e: Exception) {
                null
            } ?: continue
            hasil.add(
                TertundaBaris(
                    berkas = berkas,
                    idJenis = isi.optString("id_jenis"),
                    nomor = isi.optString("nomor"),
                    tanggal = isi.optString("tanggal"),
                ),
            )
        }
        return hasil
    }

    /**
     * Ubah berkas lama plaintext (`*.json` diawali `{`) menjadi `.enc`
     * terenkripsi. Best-effort: berkas yang gagal tetap dibaca apa adanya.
     */
    private fun migrasikanLama(ctx: Context) {
        for (berkas in folder(ctx).listFiles()?.filter { it.isFile && it.name.endsWith(".json") }.orEmpty()) {
            try {
                val teks = berkas.readText().trim()
                if (!teks.startsWith("{")) continue
                JSONObject(teks).optJSONObject("fields") ?: continue
                val tujuan = File(berkas.parent, berkas.nameWithoutExtension + ".enc")
                AntreanTerenkripsi.tulis(ctx, tujuan, teks)
                berkas.delete()
            } catch (e: Exception) {
                // Biarkan apa adanya; baca masih mendukung plaintext lama.
            }
        }
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

    private suspend fun kirimKeHasil(
        ctx: Context,
        idJenis: String,
        nomor: String?,
        fields: JSONObject,
        tanggal: String,
        clientUuid: String? = null,
    ): SupabaseKlien.Hasil<String> {
        val klien = SupabaseSesi.klien(ctx)
        return klien.kirimSurat(
            idJenis = idJenis,
            nomorSementara = nomor,
            nik = fields.optString("nik", "").ifBlank { null },
            fields = fields,
            tanggal = tanggal,
            clientUuid = clientUuid,
        )
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

            val berkas = File(folder(ctx), "${System.currentTimeMillis()}-${UUID.randomUUID()}.enc")
            AntreanTerenkripsi.tulis(ctx, berkas, isi.toString())
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
