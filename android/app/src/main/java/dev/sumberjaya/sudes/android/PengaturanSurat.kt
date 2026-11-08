package dev.sumberjaya.sudes.android

import android.content.Context

/**
 * Identitas kop surat dan penomoran — kolomnya sama dengan Pengaturan Surat di
 * desktop, supaya kop yang tercetak di Android tidak bergeser.
 *
 * Disimpan di perangkat (bukan di layanan mana pun) karena hanya identitas
 * desa yang memakai aplikasi ini. Nomor urut dimulai dari angka yang diisi
 * operator — cocokkan dengan nomor terakhir di register desktop supaya nomor
 * surat tidak lompat.
 */
data class PengaturanSurat(
    val namaDesa: String = "",
    val kecamatan: String = "",
    val kabupaten: String = "",
    /** "Kabupaten" atau "Kota" — menentukan tulisan "PEMERINTAH KABUPATEN/KOTA". */
    val jenisKabupaten: String = "Kabupaten",
    /** "Desa" atau "Kelurahan" — menentukan "PEMERINTAH DESA" / "KELURAHAN". */
    val jenisLembaga: String = "Desa",
    val alamat: String = "",
    val kodepos: String = "",
    val email: String = "",
    val kepalaDesa: String = "",
    val sekretarisDesa: String = "",
    val nipKepalaDesa: String = "",
    val awalanNomor: String = "470",
    val nomorTerakhir: Int = 0,
    /** "A4" (bawaan) atau "F4". */
    val ukuranKertas: String = "A4",
    /** Jalur gambar logo kop; kosong = kop tanpa logo. */
    val logoPath: String = "",
    /** Pola nomor per id jenis surat, diterbitkan desktop. */
    val formatNomor: Map<String, String> = emptyMap(),
    /** Tahun terbitan dari desktop (diisi PengaturanSinkron). */
    val tahun: Int = 0,
) {
    /**
     * Nomor surat berikutnya dan angka yang tersimpan naik satu.
     * Pola diambil dari [formatNomor] (diterbitkan desktop) supaya bentuk
     * nomornya sama dengan register; bila absent, jatuh ke pola bawaan.
     */
    fun nomorBerikutnya(
        idJenis: String,
        tahun: Int,
        prefs: android.content.SharedPreferences,
    ): String {
        // Nomor terakhir dibaca dari penyimpanan, bukan dari field objek ini:
        // layar formulir memakai satu objek PengaturanSurat untuk beberapa
        // surat, jadi field yang tidak diperbarui membuat dua surat berturut-
        // turut mendapat nomor placeholder yang sama.
        val terakhir = maxOf(nomorTerakhir, prefs.getInt(K_NOMOR, 0))
        val urut = terakhir + 1
        val pola = formatNomor[idJenis]
            ?: formatNomor[idJenis.uppercase()]
            ?: "${awalanNomor.trim()}/{urut.toString().padStart(3, '0')}/Ds/$tahun"
        // Placeholder {1} (kode jenis) diisi kode jenis lowercase dari map;
        // bila pola butuh kode yang belum diketahui, kosong (register desktop
        // yang menormalkan) — tidak dicetak segmen kosong membingungkan.
        val kodeJenis = idJenis.lowercase()
        val nomor = susunNomor(pola, urut, kodeJenis, tahun.toString())
        prefs.edit().putInt(K_NOMOR, urut).apply()
        return nomor
    }

    /** Isi pola .NET sederhana: {0:D3} urutan, {1} kode jenis, {2:yyyy} tahun. */
    fun susunNomor(format: String, urut: Int, kodeJenis: String, tahun: String): String {
        var hasil = Regex("\\{0(?::[^{}]+)?\\}").replace(format) { m ->
            val spec = m.value.removeSurrounding("{", "}")
            when (spec.substringAfter(':', "").uppercase()) {
                "D2" -> urut.toString().padStart(2, '0')
                "D3" -> urut.toString().padStart(3, '0')
                "D4" -> urut.toString().padStart(4, '0')
                else -> urut.toString()
            }
        }
        hasil = Regex("\\{1(?::[^{}]+)?\\}").replace(hasil) { kodeJenis }
        hasil = Regex("\\{2(?::[^{}]+)?\\}").replace(hasil) { tahun }
        return hasil
    }

    companion object {
        private const val PREFS = "sudes_surat"
        private const val K_DESA = "desa"
        private const val K_KECAMATAN = "kecamatan"
        private const val K_KABUPATEN = "kabupaten"
        private const val K_JENIS_KABUPATEN = "jenisKabupaten"
        private const val K_JENIS_LEMBAGA = "jenisLembaga"
        private const val K_ALAMAT = "alamat"
        private const val K_KODEPOS = "kodepos"
        private const val K_EMAIL = "email"
        private const val K_KEPALA_DESA = "kepalaDesa"
        private const val K_SEKRETARIS = "sekretarisDesa"
        private const val K_NIP = "nipKepalaDesa"
        private const val K_AWALAN = "awalan"
        private const val K_NOMOR = "nomor"
        private const val K_UKURAN = "ukuranKertas"
        private const val K_LOGO = "logoPath"
        private const val K_FORMAT = "formatNomor"
        private const val K_TAHUN = "tahun"

        fun ambil(ctx: Context): PengaturanSurat {
            val p = ctx.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
            val format = mutableMapOf<String, String>()
            val teksFormat = p.getString(K_FORMAT, null)
            if (!teksFormat.isNullOrBlank()) {
                try {
                    val obj = org.json.JSONObject(teksFormat)
                    for (k in obj.keys()) format[k] = obj.optString(k, "")
                } catch (e: Exception) {
                    // Pola nomor rusak tidak boleh menjatuhkan aplikasi.
                }
            }
            return PengaturanSurat(
                namaDesa = p.getString(K_DESA, "") ?: "",
                kecamatan = p.getString(K_KECAMATAN, "") ?: "",
                kabupaten = p.getString(K_KABUPATEN, "") ?: "",
                jenisKabupaten = (p.getString(K_JENIS_KABUPATEN, "Kabupaten") ?: "Kabupaten"),
                jenisLembaga = (p.getString(K_JENIS_LEMBAGA, "Desa") ?: "Desa"),
                alamat = p.getString(K_ALAMAT, "") ?: "",
                kodepos = p.getString(K_KODEPOS, "") ?: "",
                email = p.getString(K_EMAIL, "") ?: "",
                kepalaDesa = p.getString(K_KEPALA_DESA, "") ?: "",
                sekretarisDesa = p.getString(K_SEKRETARIS, "") ?: "",
                nipKepalaDesa = p.getString(K_NIP, "") ?: "",
                awalanNomor = (p.getString(K_AWALAN, "470") ?: "470").trim(),
                nomorTerakhir = p.getInt(K_NOMOR, 0),
                ukuranKertas = (p.getString(K_UKURAN, "A4") ?: "A4"),
                logoPath = p.getString(K_LOGO, "") ?: "",
                formatNomor = format,
                tahun = p.getInt(K_TAHUN, 0),
            )
        }

        fun simpan(ctx: Context, p: PengaturanSurat) {
            ctx.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit()
                .putString(K_DESA, p.namaDesa)
                .putString(K_KECAMATAN, p.kecamatan)
                .putString(K_KABUPATEN, p.kabupaten)
                .putString(K_JENIS_KABUPATEN, p.jenisKabupaten)
                .putString(K_JENIS_LEMBAGA, p.jenisLembaga)
                .putString(K_ALAMAT, p.alamat)
                .putString(K_KODEPOS, p.kodepos)
                .putString(K_EMAIL, p.email)
                .putString(K_KEPALA_DESA, p.kepalaDesa)
                .putString(K_SEKRETARIS, p.sekretarisDesa)
                .putString(K_NIP, p.nipKepalaDesa)
                .putString(K_AWALAN, p.awalanNomor)
                .putInt(K_NOMOR, p.nomorTerakhir)
                .putString(K_UKURAN, p.ukuranKertas)
                .putString(K_LOGO, p.logoPath)
                .putString(K_FORMAT, org.json.JSONObject(p.formatNomor as Map<*, *>).toString())
                .putInt(K_TAHUN, p.tahun)
                .apply()
        }

        /** Naikkan nomor urut setelah PDF dibuat. */
        fun simpanNomor(ctx: Context, urut: Int) {
            ctx.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit()
                .putInt(K_NOMOR, urut)
                .apply()
        }
    }
}
