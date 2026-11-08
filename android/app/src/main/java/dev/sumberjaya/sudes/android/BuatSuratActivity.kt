package dev.sumberjaya.sudes.android

import android.content.res.ColorStateList
import android.os.Bundle
import android.text.InputType
import android.view.View
import android.view.ViewGroup
import android.widget.ArrayAdapter
import android.widget.Button
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.Spinner
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import androidx.lifecycle.lifecycleScope
import com.google.android.material.button.MaterialButton
import com.google.android.material.card.MaterialCardView
import com.google.android.material.textfield.MaterialAutoCompleteTextView
import com.google.android.material.textfield.TextInputEditText
import com.google.android.material.textfield.TextInputLayout
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject
import java.util.Calendar
import java.util.Date

/**
 * Buat Surat: formulir dinamis dari skema yang dibawa APK.
 *
 * Isian dijadikan PDF lalu disimpan ke Downloads dan langsung ditawarkan ke
 * dialog cetak. PDF ini draf; isian yang sama ikut ke antrean Supabase dan
 * desktop membangkitkan nomor resmi + PDF resmi dari register.
 */
class BuatSuratActivity : AppCompatActivity() {

    private val bidang = mutableMapOf<String, View>()
    private val pilihan = mutableMapOf<String, MaterialAutoCompleteTextView>()
    private val daftarBaris = mutableMapOf<String, MutableList<BarisDaftar>>()

    /** Satu baris pada bidang type="list" beserta field penyusunnya. */
    private data class BarisDaftar(val wadah: View, val bidang: Map<String, View>)

    private lateinit var spinnerJenis: Spinner
    private lateinit var wadahForm: LinearLayout
    private lateinit var progres: View

    private var daftar: List<SkemaJenis> = emptyList()
    private var terpilih: SkemaJenis? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_buat_surat)
        Ui.insetAtas(findViewById(R.id.kepala))
        Ui.insetBawah(findViewById(R.id.konten))

        val grup = intent.getStringExtra(EXTRA_GROUPS) ?: GROUP_SURAT
        daftar = if (grup == GROUP_NTCR) SkemaLokal.ntcr(this) else SkemaLokal.suratBiasa(this)

        title = if (grup == GROUP_NTCR) getString(R.string.label_buat_ntcr) else getString(R.string.label_buat_surat)
        findViewById<TextView>(R.id.subjudul_halaman).text = getString(
            if (grup == GROUP_NTCR) R.string.ket_buat_ntcr else R.string.ket_buat_surat
        )

        spinnerJenis = findViewById(R.id.spinner_jenis)
        wadahForm = findViewById(R.id.wadah_form)
        progres = findViewById(R.id.progres)

        if (daftar.isEmpty()) {
            Toast.makeText(this, R.string.register_kosong, Toast.LENGTH_LONG).show()
            finish()
            return
        }

        spinnerJenis.adapter = object : ArrayAdapter<String>(
            this,
            R.layout.item_pilihan,
            daftar.map { it.idJenis },
        ) {
            override fun getView(pos: Int, convert: View?, parent: android.view.ViewGroup): View {
                val t = super.getView(pos, convert, parent) as TextView
                t.text = daftar[pos].label
                return t
            }

            override fun getDropDownView(pos: Int, convert: View?, parent: android.view.ViewGroup): View {
                val t = super.getDropDownView(pos, convert, parent) as TextView
                t.text = daftar[pos].label
                return t
            }
        }.also { it.setDropDownViewResource(R.layout.item_pilihan_dropdown) }

        spinnerJenis.onItemSelectedListener = object : android.widget.AdapterView.OnItemSelectedListener {
            override fun onItemSelected(p: android.widget.AdapterView<*>?, v: View?, pos: Int, id: Long) {
                bangunForm(daftar.getOrNull(pos))
            }

            override fun onNothingSelected(p: android.widget.AdapterView<*>?) = Unit
        }

        val tombolKirim = findViewById<Button>(R.id.btn_kirim)
        tombolKirim.setOnClickListener {
            // Cegah dobel klik: satu pembuatan = satu PDF + satu antrean.
            tombolKirim.isEnabled = false
            buatPdf { tombolKirim.isEnabled = true }
        }

        bangunForm(daftar.firstOrNull())
    }

    private fun bangunForm(jenis: SkemaJenis?) {
        terpilih = jenis
        wadahForm.removeAllViews()
        bidang.clear()
        pilihan.clear()
        daftarBaris.clear()
        if (jenis == null) return

        for (b in jenis.fieldsInput) {
            // Nomor surat dibuat aplikasi, bukan diketik operator.
            if (b.key.equals("nomorSurat", ignoreCase = true)) continue

            when (b.type) {
                "select" -> {
                    val wadahPilih = bidangPilih(b.label + wajib(b.required), b.options)
                    pilihan[b.key] = wadahPilih.editText as MaterialAutoCompleteTextView
                    wadahForm.addView(wadahPilih)
                }

                "list" -> {
                    val wadah = LinearLayout(this).apply {
                        orientation = LinearLayout.VERTICAL
                        layoutParams = LinearLayout.LayoutParams(
                            ViewGroup.LayoutParams.MATCH_PARENT,
                            ViewGroup.LayoutParams.WRAP_CONTENT,
                        ).apply { topMargin = Ui.dp(this@BuatSuratActivity, 12) }
                    }
                    val barisList = mutableListOf<BarisDaftar>()
                    daftarBaris[b.key] = barisList

                    fun tambahBaris() {
                        val kartu = MaterialCardView(this).apply {
                            radius = Ui.dp(this@BuatSuratActivity, 14).toFloat()
                            strokeWidth = Ui.dp(this@BuatSuratActivity, 1)
                            strokeColor = ContextCompat.getColor(this@BuatSuratActivity, R.color.garis)
                            setCardBackgroundColor(
                                ContextCompat.getColor(this@BuatSuratActivity, R.color.surface)
                            )
                            cardElevation = 0f
                            layoutParams = LinearLayout.LayoutParams(
                                ViewGroup.LayoutParams.MATCH_PARENT,
                                ViewGroup.LayoutParams.WRAP_CONTENT,
                            ).apply { topMargin = Ui.dp(this@BuatSuratActivity, 10) }
                        }
                        val isi = LinearLayout(this@BuatSuratActivity).apply {
                            orientation = LinearLayout.VERTICAL
                            setPadding(
                                Ui.dp(this@BuatSuratActivity, 14),
                                Ui.dp(this@BuatSuratActivity, 6),
                                Ui.dp(this@BuatSuratActivity, 14),
                                Ui.dp(this@BuatSuratActivity, 12),
                            )
                        }
                        kartu.addView(isi)

                        val peta = linkedMapOf<String, View>()
                        for (anak in b.children) {
                            if (anak.type == "select") {
                                val wadahPilih = bidangPilih(anak.label, anak.options)
                                val pilih = wadahPilih.editText as MaterialAutoCompleteTextView
                                peta[anak.key] = pilih
                                isi.addView(wadahPilih)
                            } else {
                                val wadahTeks = bidangTeks(anak.label, anak.type)
                                peta[anak.key] = wadahTeks.editText as EditText
                                isi.addView(wadahTeks)
                            }
                        }

                        isi.addView(tombolKecil("Hapus") {
                            wadah.removeView(kartu)
                            barisList.removeAll { it.wadah === kartu }
                        })

                        wadah.addView(kartu)
                        barisList.add(BarisDaftar(kartu, peta))
                    }

                    wadah.addView(tombolKecil("Tambah ${b.label}") { tambahBaris() })
                    wadahForm.addView(wadah)
                }

                else -> {
                    val wadahTeks = bidangTeks(b.label + wajib(b.required), b.type)
                    bidang[b.key] = wadahTeks.editText as EditText
                    wadahForm.addView(wadahTeks)
                }
            }
        }
    }

    /** Label bintang untuk bidang wajib. */
    private fun wajib(required: Boolean): String = if (required) "  *" else ""

    /** Kolom teks bergaya outlined Material 3. */
    private fun bidangTeks(label: String, tipe: String): TextInputLayout {
        val et = TextInputEditText(this).apply {
            inputType = when (tipe) {
                "number" -> InputType.TYPE_CLASS_NUMBER
                // Tanggal tidak mengetik bebas: pemilih tanggal di bawah
                // menulis ISO yyyy-MM-dd supaya impor desktop selalu bisa
                // membacanya (bentuk lain sering gagal parse dan hilang).
                "date" -> InputType.TYPE_NULL
                else -> InputType.TYPE_CLASS_TEXT
            }
            if (tipe == "date") {
                isFocusable = false
                isFocusableInTouchMode = false
                keyListener = null
                setOnClickListener { pilihTanggal(this) }
            }
            isSingleLine = tipe != "textarea"
            textSize = 15f
            setPadding(
                Ui.dp(this@BuatSuratActivity, 16),
                Ui.dp(this@BuatSuratActivity, 12),
                Ui.dp(this@BuatSuratActivity, 16),
                Ui.dp(this@BuatSuratActivity, 12),
            )
        }
        return bidangLayout(label).apply {
            addView(
                et,
                LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT,
                    ViewGroup.LayoutParams.WRAP_CONTENT,
                ),
            )
        }
    }

    /** Pemilih tanggal: hasil ISO yyyy-MM-dd (dipahami impor desktop). */
    private fun pilihTanggal(et: TextInputEditText) {
        val kalender = Calendar.getInstance()
        android.app.DatePickerDialog(
            this,
            { _, tahun, bulan, hari ->
                et.setText(
                    "%04d-%02d-%02d".format(tahun, bulan + 1, hari),
                )
            },
            kalender.get(Calendar.YEAR),
            kalender.get(Calendar.MONTH),
            kalender.get(Calendar.DAY_OF_MONTH),
        ).show()
    }

    /** Pilihan bergaya dropdown Material 3. */
    private fun bidangPilih(label: String, opsiAsli: List<String>): TextInputLayout {
        val opsi = opsiAsli.ifEmpty { listOf("-") }
        val pilih = MaterialAutoCompleteTextView(this).apply {
            inputType = InputType.TYPE_NULL
            threshold = 0
            textSize = 15f
            setAdapter(
                ArrayAdapter(
                    this@BuatSuratActivity,
                    R.layout.item_pilihan,
                    opsi,
                ).also { it.setDropDownViewResource(R.layout.item_pilihan_dropdown) }
            )
            setText(opsi.first(), false)
        }
        return bidangLayout(label).apply {
            endIconMode = TextInputLayout.END_ICON_DROPDOWN_MENU
            setEndIconTintList(warna(R.color.accent))
            addView(
                pilih,
                LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT,
                    ViewGroup.LayoutParams.WRAP_CONTENT,
                ),
            )
        }
    }

    /** Kerangka umum TextInputLayout supaya semua bidang seragam. */
    private fun bidangLayout(label: String): TextInputLayout =
        TextInputLayout(this).apply {
            hint = label
            boxBackgroundMode = TextInputLayout.BOX_BACKGROUND_OUTLINE
            boxBackgroundColor = ContextCompat.getColor(this@BuatSuratActivity, R.color.surface)
            boxStrokeColor = ContextCompat.getColor(this@BuatSuratActivity, R.color.accent)
            setDefaultHintTextColor(warna(R.color.teks_redup))
            setBoxCornerRadii(
                Ui.dp(this@BuatSuratActivity, 14).toFloat(),
                Ui.dp(this@BuatSuratActivity, 14).toFloat(),
                Ui.dp(this@BuatSuratActivity, 14).toFloat(),
                Ui.dp(this@BuatSuratActivity, 14).toFloat(),
            )
            layoutParams = LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.WRAP_CONTENT,
            ).apply { topMargin = Ui.dp(this@BuatSuratActivity, 12) }
        }

    /** Tombol kecil untuk menambah / menghapus baris daftar. */
    private fun tombolKecil(teks: String, aksi: () -> Unit): MaterialButton =
        MaterialButton(this).apply {
            text = teks
            textSize = 12f
            isAllCaps = false
            cornerRadius = Ui.dp(this@BuatSuratActivity, 12)
            backgroundTintList = warna(R.color.accent_muda)
            setTextColor(ContextCompat.getColor(this@BuatSuratActivity, R.color.accent_dark))
            minHeight = Ui.dp(this@BuatSuratActivity, 40)
            setPadding(
                Ui.dp(this@BuatSuratActivity, 14),
                0,
                Ui.dp(this@BuatSuratActivity, 14),
                0,
            )
            layoutParams = LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT,
                ViewGroup.LayoutParams.WRAP_CONTENT,
            ).apply { topMargin = Ui.dp(this@BuatSuratActivity, 10) }
            setOnClickListener { aksi() }
        }

    private fun warna(id: Int): ColorStateList =
        ColorStateList.valueOf(ContextCompat.getColor(this, id))

    private fun buatPdf(selesai: () -> Unit = {}) {
        val jenis = terpilih ?: run { selesai(); return }

        for (b in jenis.fieldsInput.filter { it.required }) {
            if (b.type == "list") {
                if (daftarBaris[b.key].orEmpty().isEmpty()) {
                    Toast.makeText(this, getString(R.string.wajib_satu_baris, b.label), Toast.LENGTH_SHORT).show()
                    selesai()
                    return
                }
                continue
            }
            if (teksBidang(b.key).isBlank()) {
                Toast.makeText(this, getString(R.string.wajib_diisi, b.label), Toast.LENGTH_SHORT).show()
                selesai()
                return
            }
        }

        val p = PengaturanSurat.ambil(this)
        val pejabat = teksBidang("pejabatPenandatangan")

        // Kunci yang dipakai badan surat — bentuknya sama dengan yang ditulis
        // generator desktop ($warga.*, $pejabat.*, $keterangan).
        val fields = JSONObject()
        for (b in jenis.fieldsInput) {
            if (b.key.equals("nomorSurat", ignoreCase = true)) continue
            fields.put(b.key, nilaiBidang(b.key))
        }

        // Kunci badan surat diambil dari 'target' skema (mis. warga.nama,
        // instansi.namaInstansi, ntcr.namaIstri) supaya semua prefiks terisi —
        // bukan hanya warga.*. Nama prefiks skema desktop berbeda ejaannya
        // dengan yang dipakai templat badan, jadi diberi jembatan di bawah.
        val nilai = mutableMapOf<String, String>()
        for (b in jenis.fieldsInput) {
            if (b.key.equals("nomorSurat", ignoreCase = true)) continue
            if (b.target.isNotBlank()) nilai[b.target] = teksNilai(b)
        }
        for ((ditulis, dibaca) in ALIAS_KUNCI) {
            val prefiks = "$dibaca."
            for ((k, v) in nilai.toList()) {
                if (k.startsWith(prefiks)) nilai[ditulis + k.removePrefix(prefiks)] = v
            }
        }
        // Alias kunci penuh: awalan target skema vs nama yang dipakai badan
        // (mis. instansi.namaInstansi → $instansi.nama).
        for ((ditulis, dibaca) in ALIAS_PENUH) {
            nilai[dibaca]?.let { nilai[ditulis] = it }
        }

        // Keterangan: isian operator bila ada, kalau kosong pakai keterangan
        // bawaan jenis surat — sama seperti desktop saat form dibuka.
        val isianKeterangan = listOf("keperluan", "keterangan", "keteranganTambahan")
            .firstNotNullOfOrNull { k -> teksBidang(k).ifBlank { null } }

        nilai["warga.tempatTanggalLahir"] = listOf(
            teksBidang("tempatLahir").ifBlank { "...." },
            teksBidang("tanggalLahir").ifBlank { "...." },
        ).joinToString(", ")

        nilai["warga.alamat"] = AlamatSusun.susun(nilai)

        // Turunan yang dipakai badan tetapi tidak punya kolom sendiri —
        // batasan ini sama dengan generator desktop (NtcrGenerator,
        // KematianInputViewModel).
        nilai["warga.kewarganegaraan"] =
            nilai["warga.kewarganegaraan"]?.ifBlank { null } ?: "WNI"
        nilai["warga.umur"] = umurDari(teksBidang("tanggalLahir"))
        nilai["warga.rtRw"] = AlamatSusun.formatRtRw(
            nilai["warga.rt"]?.ifBlank { null }, nilai["warga.rw"]?.ifBlank { null },
        )
        nilai["kematian.hari"] = hariDariTanggal(teksBidang("tanggalKematian"))
        nilai["kematian.tanggal"] = tanggalPanjang(teksBidang("tanggalKematian"))
        nilai["warga.statusPerkawinanSebutan"] =
            sebutanStatus(perempuan = false, nilai["warga.statusPerkawinan"])
        nilai["ntcr.statusPerkawinanIstriSebutan"] =
            sebutanStatus(perempuan = true, nilai["ntcr.statusPerkawinanIstri"])

        nilai["pejabat.nama"] = listOf("namaPejabat", "namaPejabatPenandatangan")
            .firstNotNullOfOrNull { k -> teksBidang(k).ifBlank { null } }
            ?: if (pejabat.equals("Sekretaris Desa", ignoreCase = true)) p.sekretarisDesa else p.kepalaDesa
        // Baris "Jabatan" di badan memakai huruf awal besar + nama desa (skema
        // menambahkan "$desa.nama"), bukan kapital semua seperti blok tanda tangan.
        nilai["pejabat.jabatan"] = Kop.jabatanBadan(p, pejabat)

        // Data desa untuk templat badan ($desa.*).
        nilai["desa.nama"] = p.namaDesa
        nilai["desa.kecamatan"] = p.kecamatan
        nilai["desa.kabupaten"] = p.kabupaten
        nilai["desa.jenisKabupaten"] = p.jenisKabupaten
        nilai["desa.lembaga"] = p.jenisLembaga
        nilai["desa.kodepos"] = p.kodepos

        // Tanggal surat (dipakai sebagian templat badan).
        nilai["surat.tanggal"] = java.text.SimpleDateFormat(
            "dd MMMM yyyy", java.util.Locale("id", "ID"),
        ).format(java.util.Date())

        // Baru sekarang keterangan disusun: isian operator menang, kalau kosong
        // pakai bawaan jenis surat. Harus SETELAH $desa.* terisi.
        nilai["keterangan"] = isianKeterangan
            ?: if (jenis.keteranganBawaan.isNotBlank()) {
                SuratPdf.urai(jenis.keteranganBawaan, nilai)
            } else {
                ""
            }

        val tahunTarik = PengaturanSinkron.tahunTarik(this@BuatSuratActivity)
        val tahun = tahunTarik

        progres.visibility = View.VISIBLE
        lifecycleScope.launch {
            try {
                var nomor = ""
                val berkas = withContext(Dispatchers.IO) {
                    nomor = p.nomorBerikutnya(jenis.idJenis, tahun, getSharedPreferences("sudes_surat", MODE_PRIVATE))
                    val logo = MuatLogo.muat(this@BuatSuratActivity, p.logoPath)
                    SuratPdf.buat(
                        this@BuatSuratActivity,
                        jenis,
                        nomor,
                        nilai,
                        p,
                        pejabat,
                        logo,
                    )
                }

                val lokasi = withContext(Dispatchers.IO) {
                    PdfAksi.simpanKeDownloads(this@BuatSuratActivity, berkas)
                }

                // Surat yang baru dibuat ikut antrean sinkron desktop.
                val hasilKirim = withContext(Dispatchers.IO) {
                    val hasil = AntreanSurat.kirimHasil(this@BuatSuratActivity, jenis, nomor, fields)
                    // Jadwalkan pengiriman tertunda segera — tidak menunggu periodik 15 mnt.
                    try { PengirimWorker.sekali(this@BuatSuratActivity) } catch (e: Exception) { /* jadwal boleh gagal */ }
                    hasil
                }

                progres.visibility = View.GONE
                val pesan = if (lokasi != null) getString(R.string.pdf_tersimpan, lokasi)
                else getString(R.string.pdf_gagal, "penyimpanan")
                val gabungan = when (hasilKirim) {
                    AntreanSurat.HasilKirim.TERKIRIM ->
                        getString(R.string.toast_antrean_terkirim, pesan)
                    AntreanSurat.HasilKirim.ANTREAN_PENUH ->
                        getString(R.string.toast_antrean_penuh, pesan)
                    AntreanSurat.HasilKirim.PERANGKAT_NONAKTIF ->
                        getString(R.string.toast_perangkat_nonaktif, pesan)
                    AntreanSurat.HasilKirim.SESI_MATI ->
                        getString(R.string.toast_sesi_mati, pesan)
                    AntreanSurat.HasilKirim.TERTUNDA ->
                        getString(R.string.toast_antrean_tertunda, pesan)
                }
                Toast.makeText(this@BuatSuratActivity, gabungan, Toast.LENGTH_LONG).show()

                // Langsung tawarkan cetak — operator tidak perlu cari berkasnya.
                PdfAksi.cetak(this@BuatSuratActivity, berkas, jenis.label)
                selesai()
            } catch (e: Exception) {
                progres.visibility = View.GONE
                Toast.makeText(
                    this@BuatSuratActivity,
                    getString(R.string.pdf_gagal, e.message ?: "galat tidak diketahui"),
                    Toast.LENGTH_LONG,
                ).show()
                selesai()
            }
        }
    }

    /** Nilai satu bidang sebagai teks; daftar (type="list") digabung per baris. */
    private fun teksNilai(b: SkemaBidang): String {
        val nilai = nilaiBidang(b.key)
        return when (nilai) {
            is JSONArray -> {
                if (nilai.length() == 0) ""
                else (0 until nilai.length()).joinToString("; ") { i ->
                    val o = nilai.optJSONObject(i) ?: return@joinToString ""
                    o.keys().asSequence()
                        .mapNotNull { k -> o.optString(k).ifBlank { null } }
                        .joinToString(", ")
                }
            }
            is String -> nilai
            else -> ""
        }
    }

    private fun teksBidang(key: String): String = nilaiBidang(key).let { if (it is String) it else "" }

    /** Umur tahun dari tanggal lahir (ISO yyyy-MM-dd); kosong bila tak terbaca. */
    private fun umurDari(iso: String): String {
        val lahir = uraikanTanggal(iso) ?: return ""
        val sekarang = Calendar.getInstance()
        var umur = sekarang.get(Calendar.YEAR) - lahir.get(Calendar.YEAR)
        val bulan = sekarang.get(Calendar.MONTH)
        val hari = sekarang.get(Calendar.DAY_OF_MONTH)
        val bulanLahir = lahir.get(Calendar.MONTH)
        val hariLahir = lahir.get(Calendar.DAY_OF_MONTH)
        if (bulan < bulanLahir || (bulan == bulanLahir && hari < hariLahir)) umur -= 1
        return if (umur < 0) "" else umur.toString()
    }

    /** Nama hari Indonesia ("Senin"…) dari tanggal ISO; kosong bila tak terbaca. */
    private fun hariDariTanggal(iso: String): String {
        val cal = uraikanTanggal(iso) ?: return ""
        return java.text.SimpleDateFormat("EEEE", java.util.Locale("id", "ID")).format(cal.time)
    }

    /** Tanggal panjang ("12 Januari 2026") dari tanggal ISO; kosong bila tak terbaca. */
    private fun tanggalPanjang(iso: String): String {
        val cal = uraikanTanggal(iso) ?: return ""
        return java.text.SimpleDateFormat("d MMMM yyyy", java.util.Locale("id", "ID")).format(cal.time)
    }

    /** Uraikan ISO yyyy-MM-dd (atau dd/MM/yyyy lama) menjadi Calendar. */
    private fun uraikanTanggal(teks: String): Calendar? {
        val bersih = teks.trim()
        if (bersih.isEmpty()) return null
        val pola = listOf(
            java.text.SimpleDateFormat("yyyy-MM-dd", java.util.Locale.US),
            java.text.SimpleDateFormat("dd/MM/yyyy", java.util.Locale.US),
            java.text.SimpleDateFormat("dd-MM-yyyy", java.util.Locale.US),
        )
        for (p in pola) {
            try {
                val cal = Calendar.getInstance()
                cal.setTime(p.parse(bersih) ?: continue)
                return cal
            } catch (e: Exception) {
                // Coba pola berikutnya.
            }
        }
        return null
    }

    /** Sebutan N1: "Belum kawin" → Jejaka/Perawan; cerai/janda/duda → Duda/Janda. */
    private fun sebutanStatus(perempuan: Boolean, status: String?): String {
        val bersih = status?.trim().orEmpty()
        if (bersih.isEmpty()) return ""
        return when {
            bersih.contains("Belum", ignoreCase = true) ->
                if (perempuan) "Perawan" else "Jejaka"
            bersih.contains("Cerai", ignoreCase = true) ||
                bersih.contains("Janda", ignoreCase = true) ||
                bersih.contains("Duda", ignoreCase = true) ->
                if (perempuan) "Janda" else "Duda"
            else -> bersih
        }
    }

    /**
     * Nilai satu bidang. String untuk field biasa, JSONArray untuk type="list" —
     * impor desktop membaca daftar anak sebagai array, bukan teks.
     */
    private fun nilaiBidang(key: String): Any {
        pilihan[key]?.let { return it.text?.toString().orEmpty() }
        bidang[key]?.let { return (it as EditText).text.toString().trim() }

        val baris = daftarBaris[key] ?: return ""
        if (baris.isEmpty()) return ""

        val arr = JSONArray()
        for (b in baris) {
            val objek = JSONObject()
            for ((k, v) in b.bidang) {
                objek.put(
                    k,
                    when (v) {
                        is EditText -> v.text.toString().trim()
                        is MaterialAutoCompleteTextView -> v.text?.toString()?.trim().orEmpty()
                        else -> ""
                    }
                )
            }
            arr.put(objek)
        }
        return arr
    }

    companion object {
        const val EXTRA_GROUPS = "grup"
        const val GROUP_SURAT = "surat"
        const val GROUP_NTCR = "ntcr"

        /**
         * Jembatan ejaan antara templat badan (ditulis) dan prefiks 'target'
         * skema (dibaca). Nilai dari target ikut dipublikasikan di bawah nama
         * yang dipakai templat, supaya kolom tidak tercetak kosong.
         */
        private val ALIAS_KUNCI = mapOf(
            "wargaKk" to "wargaKK",
            "ahliwaris" to "ahliwarisdata",
            "ayah" to "kenalahir.ayah",
            "ibu" to "kenalahir.ibu",
            "anak" to "kenallahir.anak",
            "rekeningkoran" to "additionalData",
        )

        /** Alias kunci penuh (ditulis → dibaca) untuk badan surat. */
        private val ALIAS_PENUH = mapOf(
            "instansi.nama" to "instansi.namaInstansi",
            "instansi.alamat" to "instansi.alamatInstansi",
            "instansi.nomorSk" to "instansi.nomorSkIzinUsaha",
            "instansi.pimpinan" to "instansi.pimpinanInstansi",
            "instansi.nikPimpinan" to "instansi.nikPimpinan",
        )
    }
}
