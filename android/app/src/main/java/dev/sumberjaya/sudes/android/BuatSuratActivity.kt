package dev.sumberjaya.sudes.android

import android.os.Bundle
import android.view.View
import android.widget.ArrayAdapter
import android.widget.Button
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.Spinner
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
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
 * dialog cetak. Tidak ada data yang dikirim ke layanan mana pun — surat
 * berpindah ke register desktop lewat PDF yang dicetak, sama seperti surat
 * yang dibuat di desktop lalu di-scan.
 */
class BuatSuratActivity : AppCompatActivity() {

    private val bidang = mutableMapOf<String, View>()
    private val pilihan = mutableMapOf<String, Spinner>()
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

        val grup = intent.getStringExtra(EXTRA_GROUPS) ?: GROUP_SURAT
        daftar = if (grup == GROUP_NTCR) SkemaLokal.ntcr(this) else SkemaLokal.suratBiasa(this)

        title = if (grup == GROUP_NTCR) getString(R.string.label_buat_ntcr) else getString(R.string.label_buat_surat)

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
            android.R.layout.simple_spinner_item,
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
        }

        spinnerJenis.onItemSelectedListener = object : android.widget.AdapterView.OnItemSelectedListener {
            override fun onItemSelected(p: android.widget.AdapterView<*>?, v: View?, pos: Int, id: Long) {
                bangunForm(daftar.getOrNull(pos))
            }

            override fun onNothingSelected(p: android.widget.AdapterView<*>?) = Unit
        }

        findViewById<Button>(R.id.btn_kirim).setOnClickListener { buatPdf() }

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

            wadahForm.addView(TextView(this).apply {
                text = b.label + if (b.required) " *" else ""
                textSize = 14f
                setPadding(0, 12, 0, 4)
            })

            when (b.type) {
                "select" -> {
                    val sp = Spinner(this)
                    sp.adapter = ArrayAdapter(
                        this,
                        android.R.layout.simple_spinner_dropdown_item,
                        b.options.ifEmpty { listOf("-") },
                    )
                    pilihan[b.key] = sp
                    wadahForm.addView(sp)
                }

                "list" -> {
                    val wadah = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
                    val barisList = mutableListOf<BarisDaftar>()
                    daftarBaris[b.key] = barisList

                    fun tambahBaris() {
                        val baris = LinearLayout(this).apply {
                            orientation = LinearLayout.VERTICAL
                            setPadding(0, 8, 0, 8)
                        }
                        val peta = linkedMapOf<String, View>()

                        for (anak in b.children) {
                            baris.addView(TextView(this).apply {
                                text = anak.label
                                textSize = 12f
                            })
                            if (anak.type == "select") {
                                val sp = Spinner(this).apply {
                                    adapter = ArrayAdapter(
                                        this@BuatSuratActivity,
                                        android.R.layout.simple_spinner_dropdown_item,
                                        anak.options.ifEmpty { listOf("-") },
                                    )
                                }
                                peta[anak.key] = sp
                                baris.addView(sp)
                            } else {
                                val et = EditText(this).apply {
                                    hint = anak.label
                                    inputType = when (anak.type) {
                                        "number" -> android.text.InputType.TYPE_CLASS_NUMBER
                                        else -> android.text.InputType.TYPE_CLASS_TEXT
                                    }
                                    setPadding(12, 12, 12, 12)
                                }
                                peta[anak.key] = et
                                baris.addView(et)
                            }
                        }

                        baris.addView(Button(this).apply {
                            text = "Hapus"
                            textSize = 12f
                            setOnClickListener {
                                wadah.removeView(baris)
                                barisList.remove(BarisDaftar(baris, peta))
                            }
                        })

                        wadah.addView(baris)
                        barisList.add(BarisDaftar(baris, peta))
                    }

                    wadah.addView(Button(this).apply {
                        text = "Tambah ${b.label}"
                        setOnClickListener { tambahBaris() }
                    })
                    wadahForm.addView(wadah)
                }

                else -> {
                    val et = EditText(this)
                    et.hint = b.label
                    et.inputType = when (b.type) {
                        "number" -> android.text.InputType.TYPE_CLASS_NUMBER
                        else -> android.text.InputType.TYPE_CLASS_TEXT
                    }
                    et.setPadding(12, 12, 12, 12)
                    bidang[b.key] = et
                    wadahForm.addView(et)
                }
            }
        }
    }

    private fun buatPdf() {
        val jenis = terpilih ?: return

        for (b in jenis.fieldsInput.filter { it.required }) {
            if (b.type == "list") {
                if (daftarBaris[b.key].orEmpty().isEmpty()) {
                    Toast.makeText(this, getString(R.string.wajib_satu_baris, b.label), Toast.LENGTH_SHORT).show()
                    return
                }
                continue
            }
            if (teksBidang(b.key).isBlank()) {
                Toast.makeText(this, getString(R.string.wajib_diisi, b.label), Toast.LENGTH_SHORT).show()
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

        // Keterangan: isian operator bila ada, kalau kosong pakai keterangan
        // bawaan jenis surat — sama seperti desktop saat form dibuka.
        nilai["keterangan"] = listOf("keperluan", "keterangan", "keteranganTambahan")
            .firstNotNullOfOrNull { k -> teksBidang(k).ifBlank { null } }
            ?: if (jenis.keteranganBawaan.isNotBlank()) {
                SuratPdf.urai(jenis.keteranganBawaan, nilai)
            } else {
                ""
            }

        nilai["warga.tempatTanggalLahir"] = listOf(
            teksBidang("tempatLahir").ifBlank { "...." },
            teksBidang("tanggalLahir").ifBlank { "...." },
        ).joinToString(", ")

        nilai["warga.alamat"] = AlamatSusun.susun(nilai)

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

        val tahun = Calendar.getInstance().get(Calendar.YEAR)

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
                val antre = withContext(Dispatchers.IO) {
                    AntreanSurat.kirim(this@BuatSuratActivity, jenis, nomor, fields)
                }

                progres.visibility = View.GONE
                val pesan = if (lokasi != null) getString(R.string.pdf_tersimpan, lokasi)
                else getString(R.string.pdf_gagal, "penyimpanan")
                Toast.makeText(
                    this@BuatSuratActivity,
                    if (antre) "$pesan • antrean desktop" else pesan,
                    Toast.LENGTH_LONG,
                ).show()

                // Langsung tawarkan cetak — operator tidak perlu cari berkasnya.
                PdfAksi.cetak(this@BuatSuratActivity, berkas, jenis.label)
            } catch (e: Exception) {
                progres.visibility = View.GONE
                Toast.makeText(
                    this@BuatSuratActivity,
                    getString(R.string.pdf_gagal, e.message ?: "galat tidak diketahui"),
                    Toast.LENGTH_LONG,
                ).show()
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

    /**
     * Nilai satu bidang. String untuk field biasa, JSONArray untuk type="list" —
     * impor desktop membaca daftar anak sebagai array, bukan teks.
     */
    private fun nilaiBidang(key: String): Any {
        pilihan[key]?.let { return it.selectedItem?.toString() ?: "" }
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
                        is Spinner -> v.selectedItem?.toString() ?: ""
                        is EditText -> v.text.toString().trim()
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
    }
}
