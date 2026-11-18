package dev.sumberjaya.sudes.android

import android.content.Context
import org.json.JSONArray
import org.json.JSONObject
import java.io.File

/** Satu bidang formulir surat di Android. */
data class SkemaBidang(
    val key: String,
    val label: String,
    val type: String,
    val target: String,
    val required: Boolean,
    val options: List<String>,
    val otomatis: Boolean,
    /** Hanya untuk type="list": bidang penyusun tiap baris. */
    val children: List<SkemaBidang>,
    /** Judul kartu tempat bidang ini tampil — sama dengan section form desktop. */
    val grup: String = "",
)

/** Perataan paragraf/blok pada badan surat (kawan Rata di desktop). */
enum class RataBlok { JUSTIFY, KIRI, TENGAH, KANAN }

/**
 * Satu blok badan surat. Bentuknya mengikuti apa yang ditulis generator desktop
 * (`BadanSurat.Paragraf`, `TabelFormulir`, `DaftarBernomor`), sehingga PDF Android
 * bisa meniru tata letak desktop tanpa kode khusus per jenis surat.
 */
sealed class BlokBadan {
    data class Paragraf(
        val teks: String,
        val rata: RataBlok = RataBlok.JUSTIFY,
        val tebal: Boolean = false,
        val miring: Boolean = false,
        val indent: Float = 0f,
        val indentKiri: Float = 0f,
        val jarakAtas: Float = 0f,
        val jarakBawah: Float = 0f,
        /** True = hanya dicetak bila seluruh kuncinya terisi (generator
         *  desktop mencetak blok semacam ini di cabang "if terisi"). */
        val opsional: Boolean = false,
        /** True = hanya dicetak bila kuncinya TIDAK terisi (cabang "else"). */
        val hanyaKosong: Boolean = false,
    ) : BlokBadan()

    data class ParagrafCampur(
        val bagian: List<Pair<String, Boolean>>,
        val rata: RataBlok = RataBlok.JUSTIFY,
        val indent: Float = 0f,
        val indentKiri: Float = 0f,
        val jarakAtas: Float = 0f,
        val jarakBawah: Float = 0f,
        val opsional: Boolean = false,
        val hanyaKosong: Boolean = false,
    ) : BlokBadan()

    data class Tabel(
        val baris: List<Pair<String, String>>,
        val indentKiri: Float = 0f,
        val jarakAtas: Float = 0f,
        val jarakBawah: Float = 0f,
        val tebalkanNama: Boolean = true,
        val opsional: Boolean = false,
        val hanyaKosong: Boolean = false,
    ) : BlokBadan()

    data class Daftar(
        val butir: List<String>,
        val indentKiri: Float = 0f,
        val jarakAtas: Float = 0f,
        val jarakBawah: Float = 0f,
        val opsional: Boolean = false,
        val hanyaKosong: Boolean = false,
    ) : BlokBadan()

    /** Blok yang tidak bisa dibuat generik; dirender lewat id mesin
     *  (lihat SuratPdf) bila skema menyebut [id], atau sebagai catatan bila
     *  tidak dikenali. */
    data class Kustom(val deskripsi: String, val id: String = "") : BlokBadan()
}

/** Skema formulir satu jenis surat. */
data class SkemaJenis(
    val idJenis: String,
    val label: String,
    val fields: List<SkemaBidang>,
    val judul: String = "",
    val badan: List<BlokBadan> = emptyList(),
    val pakaiLogo: Boolean = false,
    val pemohonDiTandaTangan: Boolean = false,
    /** Teks bawaan bila kolom keterangan kosong (SKTM dan beberapa jenis lain). */
    val catatan: String = "",
    /**
     * Keterangan bawaan jenis surat — teks yang dicetak sebelum kalimat penutup.
     * Berbeda tiap jenis, dibangkitkan desktop saat form dibuka.
     */
    val keteranganBawaan: String = "",
    /**
     * True bila badan sudah mencakup seluruh halaman (kop/judul/tanda tangan)
     * karena generator desktop menimpa ComposeHalaman. Android tidak boleh
     * menambahkan judul lagi — itu akan terduplikasi.
     */
    val halamanPenuh: Boolean = false,
    /**
     * Urutan kartu seperti yang dilihat operator di desktop. Skema lama (cache
     * server sebelum grup diterbitkan) list kosong → formulir tetap datar.
     */
    val grupUrutan: List<String> = emptyList(),
) {
    /** Field yang diisi operator, berurutan; field otomatis dilewati. */
    val fieldsInput: List<SkemaBidang> get() = fields.filter { !it.otomatis }

    /**
     * Bagi bidang menjadi kartu-kartu (judul, bidang) mengikuti [grupUrutan];
     * bidang tanpa grup terkumpul di kelompok terakhir berjudul kosong sehingga
     * tidak ada isian yang hilang. Judul kosong = satu kelompok datar.
     */
    fun grupkan(input: List<SkemaBidang>): List<Pair<String, List<SkemaBidang>>> {
        if (input.isEmpty()) return emptyList()
        if (grupUrutan.isEmpty() && input.none { it.grup.isNotBlank() }) {
            return listOf("" to input)
        }
        val hasil = mutableListOf<Pair<String, MutableList<SkemaBidang>>>()
        for (judul in grupUrutan) {
            val anggota = input.filter { it.grup == judul }
            if (anggota.isNotEmpty()) hasil += judul to anggota.toMutableList()
        }
        val terpakai = hasil.flatMap { it.second }.toSet()
        for (b in input.filterNot { terpakai.contains(it) }) {
            val idx = hasil.indexOfFirst { it.first == b.grup }
            if (idx >= 0) hasil[idx].second.add(b) else hasil += b.grup to mutableListOf(b)
        }
        return hasil.map { it.first to it.second.toList() }
    }

    /** Judul yang dicetak; jatuh ke label bila skema tidak menyebutkannya. */
    val judulCetak: String get() = judul.ifBlank { label }

    /** True untuk kelompok NTCR (N1–N6, N8) — dipisah di menu utama. */
    val ntcr: Boolean get() = idJenis.startsWith("NTCR_", ignoreCase = true)

    companion object {
        fun fromJson(idJenis: String, json: JSONObject): SkemaJenis {
            val label = json.optString("label", idJenis)
            val daftar = mutableListOf<SkemaBidang>()
            val arr = json.optJSONArray("fields") ?: JSONArray()
            for (i in 0 until arr.length()) {
                daftar.add(BidangDariJson(arr.getJSONObject(i)))
            }
            val urutanArr = json.optJSONArray("grupUrutan")
            val urutan = mutableListOf<String>()
            if (urutanArr != null) {
                for (i in 0 until urutanArr.length()) {
                    val teks = urutanArr.optString(i)
                    if (teks.isNotBlank()) urutan.add(teks)
                }
            }
            return SkemaJenis(
                idJenis = idJenis,
                label = label,
                fields = daftar,
                judul = json.optString("judul", ""),
                badan = BlokDariJson(json.optJSONArray("badan")),
                pakaiLogo = json.optBoolean("pakaiLogo", false),
                pemohonDiTandaTangan = json.optBoolean("pemohonDiTandaTangan", false),
                catatan = json.optString("catatan", ""),
                keteranganBawaan = json.optString("keteranganBawaan", ""),
                halamanPenuh = json.optBoolean("halamanPenuh", false),
                grupUrutan = urutan,
            )
        }

        /** Blok badan surat; urutan array dipertahankan persis seperti desktop. */
        private fun BlokDariJson(arr: JSONArray?): List<BlokBadan> {
            if (arr == null) return emptyList()
            val hasil = mutableListOf<BlokBadan>()
            for (i in 0 until arr.length()) {
                val b = arr.optJSONObject(i) ?: continue
                val jarakAtas = b.optDouble("jarakAtas", 0.0).toFloat()
                val jarakBawah = b.optDouble("jarakBawah", 0.0).toFloat()
                val indent = b.optDouble("indent", 0.0).toFloat()
                val indentKiri = b.optDouble("indentKiri", 0.0).toFloat()
                // Kondisi percabangan sama seperti generator desktop: blok
                // "opsional" hanya dicetak bila seluruh kuncinya terisi, blok
                // "hanyaKosong" hanya bila kuncinya kosong (cabang else).
                val opsional = b.optBoolean("opsional", false)
                val hanyaKosong = b.optBoolean("hanyaKosong", false)

                val campur = b.optJSONArray("paragrafCampur")
                val tabel = b.optJSONArray("tabel")
                val daftar = b.optJSONArray("daftar")
                val kustom = b.optString("kustom", "")

                when {
                    campur != null -> {
                        val bagian = mutableListOf<Pair<String, Boolean>>()
                        for (j in 0 until campur.length()) {
                            val potongan = campur.getJSONArray(j)
                            bagian.add(potongan.getString(0) to potongan.optBoolean(1, false))
                        }
                        hasil.add(BlokBadan.ParagrafCampur(
                            bagian = bagian,
                            rata = RataDariJson(b.optString("rata", "justify")),
                            indent = indent,
                            indentKiri = indentKiri,
                            jarakAtas = jarakAtas,
                            jarakBawah = jarakBawah,
                            opsional = opsional,
                            hanyaKosong = hanyaKosong,
                        ))
                    }

                    tabel != null -> {
                        val baris = mutableListOf<Pair<String, String>>()
                        for (j in 0 until tabel.length()) {
                            val barisJson = tabel.getJSONArray(j)
                            baris.add(barisJson.getString(0) to barisJson.optString(1, ""))
                        }
                        hasil.add(BlokBadan.Tabel(
                            baris = baris,
                            indentKiri = indentKiri,
                            jarakAtas = jarakAtas,
                            jarakBawah = jarakBawah,
                            tebalkanNama = b.optBoolean("tebalkanNama", true),
                            opsional = opsional,
                            hanyaKosong = hanyaKosong,
                        ))
                    }

                    daftar != null -> {
                        val butir = mutableListOf<String>()
                        for (j in 0 until daftar.length()) butir.add(daftar.getString(j))
                        hasil.add(BlokBadan.Daftar(
                            butir = butir,
                            indentKiri = indentKiri,
                            jarakAtas = jarakAtas,
                            jarakBawah = jarakBawah,
                            opsional = opsional,
                            hanyaKosong = hanyaKosong,
                        ))
                    }

                    kustom.isNotBlank() -> hasil.add(BlokBadan.Kustom(
                        deskripsi = kustom,
                        id = b.optString("kustumId", ""),
                    ))

                    else -> hasil.add(BlokBadan.Paragraf(
                        teks = b.optString("paragraf", ""),
                        rata = RataDariJson(b.optString("rata", "justify")),
                        tebal = b.optBoolean("tebal", false),
                        miring = b.optBoolean("miring", false),
                        indent = indent,
                        indentKiri = indentKiri,
                        jarakAtas = jarakAtas,
                        jarakBawah = jarakBawah,
                        opsional = opsional,
                        hanyaKosong = hanyaKosong,
                    ))
                }
            }
            return hasil
        }

        private fun RataDariJson(nilai: String): RataBlok = when (nilai.lowercase()) {
            "kiri" -> RataBlok.KIRI
            "tengah" -> RataBlok.TENGAH
            "kanan" -> RataBlok.KANAN
            else -> RataBlok.JUSTIFY
        }

        private fun BidangDariJson(b: JSONObject): SkemaBidang {
            val key = b.optString("key")
            val opsi = mutableListOf<String>()
            val arrOpsi = b.optJSONArray("options")
            if (arrOpsi != null) {
                for (j in 0 until arrOpsi.length()) opsi.add(arrOpsi.optString(j))
            }

            val anak = mutableListOf<SkemaBidang>()
            val arrAnak = b.optJSONArray("children")
            if (arrAnak != null) {
                for (j in 0 until arrAnak.length()) anak.add(BidangDariJson(arrAnak.getJSONObject(j)))
            }

            return SkemaBidang(
                key = key,
                label = b.optString("label", key),
                type = b.optString("type", "text"),
                target = b.optString("target", key),
                required = b.optBoolean("required", false),
                options = opsi,
                otomatis = b.optBoolean("otomatis", false),
                children = anak,
                grup = b.optString("grup", ""),
            )
        }
    }
}

/**
 * Sumber definisi formulir di perangkat.
 *
 * Urutannya: salinan lokal hasil tarikan dari desktop
 * (`filesDir/skema-cache.json`) lebih dulu, lalu `assets/skema-formulir.json`
 * yang ikut APK. Dengan begitu skema yang diterbitkan desktop ikut dipakai
 * tanpa menunggu APK baru, sementara perangkat yang belum pernah tersambung —
 * atau sedang offline — tetap bisa membuat surat dari salinan terakhir.
 */
object SkemaLokal {
    private const val BERKAS = "skema-formulir.json"
    private const val CACHE = "skema-cache.json"
    private const val PREFS = "sudes_skema"
    private const val K_HASH_ASET = "hash_aset"
    private const val K_HASH_CACHE = "hash_cache"
    private const val K_VERSI_APK = "versi_apk"
    private const val K_TERBIT_MILLIS = "terbit_millis"

    @Volatile
    private var cache: Map<String, SkemaJenis>? = null

    @Volatile
    private var asetDiCache: Map<String, SkemaJenis>? = null

    /** Sidik jari skema yang dibekukan di APK ini; null bila aset tidak terbaca. */
    @Volatile
    private var hashAset: String? = null

    /**
     * Skema yang dipakai aplikasi.
     *
     * Cache <c>skema-cache.json</c> adalah terbitan desktop, jadi ia dipakai
     * hanya kalau ditulis oleh APK yang sama (sidik jari aset + versionCode
     * cocok) DAN diterbitkan tidak sebelum skema di dalam APK ini dibekukan.
     * Tanpa dua pemeriksaan itu, memasang APK baru tidak ada gunanya: formulir
     * tetap dibangun dari salinan lama — kolom yang desktop buang masih muncul,
     * bidang daftar tambahan kehilangan kolom inputnya, dan operator melapor
     * "APK-nya belum berubah" padahal berkas di dalam APK sudah benar.
     */
    @Synchronized
    fun muat(ctx: Context): Map<String, SkemaJenis> {
        cache?.let { return it }

        val asetPeta = aset(ctx)
        val p = ctx.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
        val cocok = cacheBolehDipakai(
            p.getString(K_HASH_ASET, null),
            p.getInt(K_VERSI_APK, -1),
            p.getLong(K_TERBIT_MILLIS, 0L),
            hashAset,
            BuildConfig.VERSION_CODE,
            BuildConfig.SKEMA_SNAPSHOT_MILLIS,
        )
        val milikApkIni = hashAset != null &&
            p.getString(K_HASH_ASET, null) == hashAset &&
            p.getInt(K_VERSI_APK, -1) == BuildConfig.VERSION_CODE

        val pakai = when {
            cocok -> bacaBerkas(File(ctx.filesDir, CACHE)) ?: asetPeta ?: emptyMap()
            // APK berbeda dari yang menulis cache (atau cache warisan versi lama
            // tanpa stamp): salinan APK inilah yang terbaru, berkas lama dibuang.
            !milikApkIni -> {
                buangCache(ctx)
                asetPeta ?: emptyMap()
            }
            // Terbitan desktop memang lebih lama dari skema APK ini: berkasnya
            // dibiarkan (sinkron berikutnya menggantikannya), tapi tidak dipakai.
            else -> asetPeta ?: emptyMap()
        }

        cache = lengkapiGrup(pakai, asetPeta)
        return cache!!
    }

    /**
     * Aturan kapan cache terbitan desktop boleh dipakai. Murni supaya bisa
     * diuji tanpa Android.
     *
     * [apkSnapshotMillis] 0 berarti waktu pembekuan skema APK tidak diketahui
     * (build tanpa riwayat git): dalam keadaan itu server tetap yang dipegang,
     * supaya penerbitan desktop tidak pernah diam-diam dikalahkan APK buta.
     */
    fun cacheBolehDipakai(
        stampHashAset: String?,
        stampVersiApk: Int,
        stampTerbitMillis: Long,
        hashAsetSekarang: String?,
        versiApkSekarang: Int,
        apkSnapshotMillis: Long,
    ): Boolean {
        if (hashAsetSekarang == null) return false
        if (stampHashAset != hashAsetSekarang) return false
        if (stampVersiApk != versiApkSekarang) return false
        if (apkSnapshotMillis <= 0L) return true
        return stampTerbitMillis >= apkSnapshotMillis
    }

    private fun buangCache(ctx: Context) {
        try {
            val lama = File(ctx.filesDir, CACHE)
            if (lama.isFile) lama.delete()
            val sementara = File(ctx.filesDir, "$CACHE.tmp")
            if (sementara.isFile) sementara.delete()
        } catch (e: Exception) {
            // Tidak bisa menghapus tidak berbahaya: selama stamp-nya tidak cocok,
            // berkas itu tetap tidak dipakai.
        }
    }

    /**
     * Isi pengelompokan kartu dari aset APK bila skema yang dipakai tidak
     * memilikinya.
     *
     * Skema di <c>skema-cache.json</c> adalah terbitan desktop. Perangkat yang
     * terakhir sinkron sebelum grup kartu diterbitkan punya skema tanpa
     * <grup>/<grupUrutan>, dan formulirnya kembali menjadi satu daftar panjang —
     * gejala yang terlihat seperti "APK-nya belum berubah" padahal APK-nya sudah
     * benar. Kartu diturunkan dari XAML desktop dan ikut dibekukan di APK, jadi
     * judul dan urutannya aman dipakai sebagai penyempurna: yang diisi hanya
     * pengelompokan, tidak ada label, field, opsi, atau badan surat server yang
     * diubah, dan skema server yang sudah menyebut grup tidak pernah ditimpa.
     */
    fun lengkapiGrup(
        dipakai: Map<String, SkemaJenis>,
        aset: Map<String, SkemaJenis>?,
    ): Map<String, SkemaJenis> {
        if (aset.isNullOrEmpty() || dipakai.isEmpty()) return dipakai
        return dipakai.mapValues { (idJenis, jenis) ->
            val sumber = aset[idJenis] ?: return@mapValues jenis
            val sudahPunyaGrup = jenis.grupUrutan.isNotEmpty() ||
                jenis.fields.any { it.grup.isNotBlank() }
            if (sudahPunyaGrup || sumber.grupUrutan.isEmpty()) return@mapValues jenis
            val peta = sumber.fields.asSequence()
                .filter { it.grup.isNotBlank() }
                .associate { it.key to it.grup }
            if (peta.isEmpty()) return@mapValues jenis
            jenis.copy(
                grupUrutan = sumber.grupUrutan,
                fields = jenis.fields.map { b -> b.copy(grup = peta[b.key] ?: b.grup) },
            )
        }
    }

    private fun aset(ctx: Context): Map<String, SkemaJenis>? {
        asetDiCache?.let { return it }
        val teks = try {
            ctx.assets.open(BERKAS).bufferedReader().use { it.readText() }
        } catch (e: Exception) {
            null
        } ?: return null
        hashAset = sha256(teks)
        return uraikan(teks)?.also { asetDiCache = it }
    }

    /**
     * Simpan skema terbitan desktop sebagai salinan lokal. False bila isinya
     * kosong/rusak — salinan lama (atau aset APK) tetap dipakai.
     *
     * [terbitMillis] adalah <c>diperbarui_pada</c> paling tua di antara baris yang
     * ditarik; 0 bila server tidak menyebutkannya.
     */
    fun simpan(ctx: Context, json: String, terbitMillis: Long = 0L): Boolean {
        val peta = try {
            uraikan(json)
        } catch (e: Exception) {
            null
        } ?: return false

        return try {
            val tujuan = File(ctx.filesDir, CACHE)
            val sementara = File(ctx.filesDir, "$CACHE.tmp")
            sementara.writeText(json)
            if (!sementara.renameTo(tujuan)) {
                sementara.copyTo(tujuan, overwrite = true)
                sementara.delete()
            }
            // Stamp: cache ini sekarang milik APK ini. Setelah APK diganti,
            // sidik jarinya tidak cocok lagi dan skema APK yang dipakai lebih dulu.
            ctx.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit()
                .putString(K_HASH_ASET, hashAset ?: "")
                .putString(K_HASH_CACHE, sha256(json) ?: "")
                .putInt(K_VERSI_APK, BuildConfig.VERSION_CODE)
                .putLong(K_TERBIT_MILLIS, terbitMillis)
                .apply()
            // Terbitan yang lebih tua dari skema APK ini tidak boleh menutupinya
            // (mis. desktop lama yang belum ikut rilis terbaru), tetapi juga tidak
            // dibuang: begitu desktop menerbitkan yang baru, sinkron berikutnya
            // memakainya. Grup kartu tetap disempurnakan dari aset APK.
            val boleh = cacheBolehDipakai(
                hashAset, BuildConfig.VERSION_CODE, terbitMillis,
                hashAset, BuildConfig.VERSION_CODE, BuildConfig.SKEMA_SNAPSHOT_MILLIS,
            )
            cache = if (boleh) {
                lengkapiGrup(peta, aset(ctx))
            } else {
                lengkapiGrup(aset(ctx) ?: peta, aset(ctx))
            }
            true
        } catch (e: Exception) {
            false
        }
    }

    private fun sha256(teks: String): String? = try {
        val digest = java.security.MessageDigest.getInstance("SHA-256")
            .digest(teks.toByteArray(Charsets.UTF_8))
        digest.joinToString("") { "%02x".format(it) }
    } catch (e: Exception) {
        null
    }

    private fun bacaBerkas(berkas: File): Map<String, SkemaJenis>? {
        if (!berkas.isFile) return null
        return try {
            uraikan(berkas.readText())
        } catch (e: Exception) {
            null
        }
    }

    private fun uraikan(teks: String): Map<String, SkemaJenis>? {
        val akar = JSONObject(teks)
        val peta = mutableMapOf<String, SkemaJenis>()
        for (kunci in akar.keys()) {
            val obj = akar.optJSONObject(kunci) ?: continue
            peta[kunci] = SkemaJenis.fromJson(kunci, obj)
        }

        return if (peta.isEmpty()) null else peta
    }

    /** Jenis surat biasa (bukan NTCR), diurutkan untuk tampilan menu. */
    /** Jenis surat biasa (bukan NTCR), diurutkan untuk tampilan menu.
     *
     * TEMPLATE_SURAT tidak ikut: di desktop jenis itu tidak ada di menu
     * Buat Surat Baru (badannya datang dari payload template tersimpan),
     * jadi Android tidak menawarkan jenis yang tidak bisa diisi di sini.
     */
    fun suratBiasa(ctx: Context): List<SkemaJenis> =
        muat(ctx).values.filter { !it.ntcr && !it.idJenis.equals("TEMPLATE_SURAT", true) }.sortedBy { it.label }

    /** Jenis NTCR (N1–N6, N8), diurutkan untuk tampilan menu. */
    fun ntcr(ctx: Context): List<SkemaJenis> =
        muat(ctx).values.filter { it.ntcr }.sortedBy { it.label }
}
