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

        val isi = mutableListOf<Pair<String, String>>()
        for (b in jenis.fieldsInput) {
            if (b.key.equals("nomorSurat", ignoreCase = true)) continue
            isi.add(b.label to teksNilai(b))
        }

        val tahun = Calendar.getInstance().get(Calendar.YEAR)

        progres.visibility = View.VISIBLE
        lifecycleScope.launch {
            try {
                val berkas = withContext(Dispatchers.IO) {
                    val p = PengaturanSurat.ambil(this@BuatSuratActivity)
                    val nomor = p.nomorBerikutnya(tahun, getSharedPreferences("sudes_surat", MODE_PRIVATE))
                    SuratPdf.buat(
                        this@BuatSuratActivity,
                        jenis,
                        nomor,
                        isi,
                        p,
                        teksBidang("pejabatPenandatangan"),
                    )
                }

                val lokasi = withContext(Dispatchers.IO) {
                    PdfAksi.simpanKeDownloads(this@BuatSuratActivity, berkas)
                }

                progres.visibility = View.GONE
                Toast.makeText(
                    this@BuatSuratActivity,
                    if (lokasi != null) getString(R.string.pdf_tersimpan, lokasi)
                    else getString(R.string.pdf_gagal, "penyimpanan"),
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
    }
}
