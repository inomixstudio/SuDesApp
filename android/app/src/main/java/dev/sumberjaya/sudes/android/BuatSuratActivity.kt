package dev.sumberjaya.sudes.android

import android.os.Bundle
import android.view.Gravity
import android.view.View
import android.widget.ArrayAdapter
import android.widget.Button
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.Spinner
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import kotlinx.coroutines.launch
import org.json.JSONArray
import org.json.JSONObject
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/**
 * Buat Surat Baru: formulir dinamis dibangun dari skema yang diterbitkan
 * desktop. Isian dikirim ke antrean Supabase — aplikasi tidak menyimpan apa
 * pun di perangkat.
 */
class BuatSuratActivity : AppCompatActivity() {

    private val bidang = mutableMapOf<String, View>()
    private val pilihan = mutableMapOf<String, Spinner>()
    private val daftarBaris = mutableMapOf<String, MutableList<BarisDaftar>>()

    /** Satu baris pada bidang type="list" beserta field penyusunnya. */
    private data class BarisDaftar(val wadah: View, val bidang: Map<String, View>)
    private lateinit var klien: SupabaseKlien
    private var skema: Map<String, SkemaJenis> = emptyMap()
    private var terpilih: SkemaJenis? = null

    private lateinit var spinnerJenis: Spinner
    private lateinit var wadahForm: LinearLayout
    private lateinit var progres: View

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_buat_surat)

        spinnerJenis = findViewById(R.id.spinner_jenis)
        wadahForm = findViewById(R.id.wadah_form)
        progres = findViewById(R.id.progres)

        klien = SupabaseSesi.klien(this)

        spinnerJenis.onItemSelectedListener = object : android.widget.AdapterView.OnItemSelectedListener {
            override fun onItemSelected(p: android.widget.AdapterView<*>?, v: View?, pos: Int, id: Long) {
                val idJenis = spinnerJenis.getItemAtPosition(pos) as String
                bangunForm(skema[idJenis])
            }

            override fun onNothingSelected(p: android.widget.AdapterView<*>?) = Unit
        }

        findViewById<Button>(R.id.btn_kirim).setOnClickListener { kirim() }

        muatSkema()
    }

    private fun muatSkema() {
        progres.visibility = View.VISIBLE
        lifecycleScope.launch {
            skema = klien.muatSkema()
            if (skema.isEmpty()) {
                Toast.makeText(this@BuatSuratActivity, "Skema formulir belum ada. Terbitkan dulu dari aplikasi desktop.", Toast.LENGTH_LONG).show()
                progres.visibility = View.GONE
                return@launch
            }

            val daftar = skema.values.sortedBy { it.label }
            spinnerJenis.adapter = object : ArrayAdapter<String>(
                this@BuatSuratActivity,
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

            bangunForm(daftar.firstOrNull())
            progres.visibility = View.GONE
        }
    }

    private fun bangunForm(jenis: SkemaJenis?) {
        terpilih = jenis
        wadahForm.removeAllViews()
        bidang.clear()
        pilihan.clear()
        daftarBaris.clear()
        if (jenis == null) return

        for (b in jenis.fieldsInput) {
            val label = TextView(this).apply {
                text = b.label + if (b.required) " *" else ""
                textSize = 14f
                setPadding(0, 12, 0, 4)
            }
            wadahForm.addView(label)

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

    private fun kirim() {
        val jenis = terpilih ?: return

        // Validasi sederhana: field wajib tidak boleh kosong.
        for (b in jenis.fieldsInput.filter { it.required }) {
            if (b.type == "list") {
                if (daftarBaris[b.key].orEmpty().isEmpty()) {
                    Toast.makeText(this, "${b.label} wajib diisi minimal satu baris", Toast.LENGTH_SHORT).show()
                    return
                }
                continue
            }

            if (nilaiBidang(b.key).kosong()) {
                Toast.makeText(this, "${b.label} wajib diisi", Toast.LENGTH_SHORT).show()
                return
            }
        }

        val fields = JSONObject()
        for (b in jenis.fieldsInput) fields.put(b.key, nilaiBidang(b.key))

        val nik = bidang["nik"]?.let { (it as EditText).text.toString().trim() } ?: ""
        val nomor = "AND-" + SimpleDateFormat("yyyyMMdd-HHmmss", Locale.US).format(Date())
        val tanggal = SimpleDateFormat("yyyy-MM-dd", Locale.US).format(Date())

        progres.visibility = View.VISIBLE
        lifecycleScope.launch {
            val hasil = klien.kirimSurat(jenis.idJenis, nomor, nik.ifBlank { null }, fields, tanggal)
            progres.visibility = View.GONE

            when (hasil) {
                is SupabaseKlien.Hasil.Sukses -> {
                    Toast.makeText(this@BuatSuratActivity, "Surat terkirim ke antrean (${jenis.idJenis}).", Toast.LENGTH_LONG).show()
                    finish()
                }

                is SupabaseKlien.Hasil.Galat -> {
                    val pesan = if (hasil.kode == 403 || hasil.pesan.contains("penuh", ignoreCase = true)) {
                        "Antrean penuh (10 surat). Sinkronkan aplikasi desktop dulu."
                    } else {
                        "Gagal mengirim: ${hasil.pesan}"
                    }
                    Toast.makeText(this@BuatSuratActivity, pesan, Toast.LENGTH_LONG).show()
                }
            }
        }
    }

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

    private fun Any.kosong(): Boolean = this is String && isBlank()
}
