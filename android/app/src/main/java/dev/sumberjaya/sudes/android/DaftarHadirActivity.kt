package dev.sumberjaya.sudes.android

import android.content.Context
import android.graphics.Bitmap
import android.os.Bundle
import android.view.View
import android.view.ViewGroup
import android.widget.AdapterView
import android.widget.ArrayAdapter
import android.widget.Button
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.ProgressBar
import android.widget.Spinner
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AlertDialog
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import androidx.lifecycle.lifecycleScope
import com.google.android.material.card.MaterialCardView
import com.google.android.material.checkbox.MaterialCheckBox
import com.google.android.material.textfield.TextInputEditText
import com.google.android.material.textfield.TextInputLayout
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/**
 * Daftar Hadir: membuat, mencetak, dan membagikan daftar hadir sebagai PDF.
 * Sepenuhnya juga mengakui pengaturan kolom, jumlah baris peserta, dan orientasi
 * sesuai DaftarHadirViewModel & DaftarHadirGenerator di desktop. PDF dibuat di
 * `cacheDir`, dicetak lewat pemindai Android, dan tidak masuk antrean Supabase
 * (fitur cetak saja, sesuai kesepakatan).
 */
class DaftarHadirActivity : AppCompatActivity() {

    private data class BarisPeserta(
        val kartu: MaterialCardView,
        val nama: TextInputEditText,
        val jk: Spinner,
        val jabatan: TextInputEditText,
        val nip: TextInputEditText,
        val nik: TextInputEditText,
        val noHp: TextInputEditText,
        val alamat: TextInputEditText,
        val keterangan: TextInputEditText,
    )

    private val daftarPeserta = mutableListOf<BarisPeserta>()

    private lateinit var inputJudul: TextInputEditText
    private lateinit var inputHariTanggal: TextInputEditText
    private lateinit var inputPukul: TextInputEditText
    private lateinit var inputTempat: TextInputEditText
    private lateinit var wadahPeserta: LinearLayout
    private lateinit var infoPeserta: TextView
    private lateinit var checkJK: MaterialCheckBox
    private lateinit var checkJabatan: MaterialCheckBox
    private lateinit var checkNip: MaterialCheckBox
    private lateinit var checkNik: MaterialCheckBox
    private lateinit var checkNohp: MaterialCheckBox
    private lateinit var checkAlamat: MaterialCheckBox
    private lateinit var checkKet: MaterialCheckBox
    private lateinit var checkKop: MaterialCheckBox
    private lateinit var checkFooter: MaterialCheckBox

    private var hariTanggalDisentuh = false

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_daftar_hadir)
        Ui.insetAtas(findViewById(R.id.kepala))
        Ui.insetBawah(findViewById(R.id.konten))

        inputJudul = findViewById(R.id.input_judul)
        inputHariTanggal = findViewById(R.id.input_hari_tanggal)
        inputPukul = findViewById(R.id.input_pukul)
        inputTempat = findViewById(R.id.input_tempat)
        wadahPeserta = findViewById(R.id.wadah_peserta)
        infoPeserta = findViewById(R.id.info_peserta)

        checkJK = findViewById(R.id.check_jk)
        checkJabatan = findViewById(R.id.check_jabatan)
        checkNip = findViewById(R.id.check_nip)
        checkNik = findViewById(R.id.check_nik)
        checkNohp = findViewById(R.id.check_nohp)
        checkAlamat = findViewById(R.id.check_alamat)
        checkKet = findViewById(R.id.check_ket)
        checkKop = findViewById(R.id.check_kop)
        checkFooter = findViewById(R.id.check_footer)

        val pengaturan = PengaturanSurat.ambil(this)
        inputJudul.setText("DAFTAR HADIR")
        inputPukul.setText("09.00 WIB s/d Selesai")
        if (pengaturan.namaDesa.isNotBlank()) {
            inputTempat.setText("Aula Kantor Desa ${pengaturan.namaDesa}")
        }
        isiHariTanggalHariIni()
        inputHariTanggal.addTextChangedListener(object : android.text.TextWatcher {
            override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) = Unit
            override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) = Unit
            override fun afterTextChanged(s: android.text.Editable?) {
                hariTanggalDisentuh = true
            }
        })

        for (i in 0 until MIN_POTRET) tambahBaris(null)
        perbaruiInfo()

        findViewById<Button>(R.id.btn_hari_ini).setOnClickListener { isiHariTanggalHariIni() }
        findViewById<Button>(R.id.btn_tambah).setOnClickListener {
            tambahBaris(null)
            perbaruiInfo()
        }
        findViewById<Button>(R.id.btn_hapus).setOnClickListener {
            if (daftarPeserta.isNotEmpty()) {
                val terakhir = daftarPeserta.removeAt(daftarPeserta.lastIndex)
                wadahPeserta.removeView(terakhir.kartu)
                perbaruiInfo()
            }
        }
        findViewById<Button>(R.id.btn_bersihkan).setOnClickListener { bersihkan() }

        findViewById<Button>(R.id.btn_cetak).setOnClickListener {
            if (inputJudul.text.isNullOrBlank()) {
                Toast.makeText(this, "Judul wajib diisi", Toast.LENGTH_SHORT).show()
                return@setOnClickListener
            }
            cetak()
        }
    }

    private fun isiHariTanggalHariIni() {
        hariTanggalDisentuh = false
        val teks = SimpleDateFormat("EEEE, d MMMM yyyy", Locale("id", "ID")).format(Date())
        inputHariTanggal.setText(teks)
    }

    private fun bersihkan() {
        AlertDialog.Builder(this)
            .setTitle("Bersihkan Daftar Hadir")
            .setMessage("Hapus seluruh isian dan kembali ke tampilan awal (10 baris kosong, kolom bawaan)?")
            .setNegativeButton(android.R.string.cancel, null)
            .setPositiveButton("Bersihkan") { _, _ ->
                inputJudul.setText("DAFTAR HADIR")
                inputPukul.setText("09.00 WIB s/d Selesai")
                inputTempat.setText("")
                checkJK.isChecked = false
                checkJabatan.isChecked = true
                checkNip.isChecked = false
                checkNik.isChecked = false
                checkNohp.isChecked = false
                checkAlamat.isChecked = false
                checkKet.isChecked = false
                checkKop.isChecked = true
                checkFooter.isChecked = true
                wadahPeserta.removeAllViews()
                daftarPeserta.clear()
                for (i in 0 until MIN_POTRET) tambahBaris(null)
                isiHariTanggalHariIni()
                perbaruiInfo()
            }
            .show()
    }

    private fun tambahBaris(values: DaftarHadirPdf.Peserta?) {
        val kartu = MaterialCardView(this).apply {
            radius = Ui.dp(this@DaftarHadirActivity, 14).toFloat()
            strokeWidth = Ui.dp(this@DaftarHadirActivity, 1)
            strokeColor = ContextCompat.getColor(this@DaftarHadirActivity, R.color.garis)
            setCardBackgroundColor(ContextCompat.getColor(this@DaftarHadirActivity, R.color.surface))
            cardElevation = 0f
            layoutParams = LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.WRAP_CONTENT,
            ).apply { topMargin = Ui.dp(this@DaftarHadirActivity, 10) }
        }
        val isi = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(Ui.dp(this@DaftarHadirActivity, 14), Ui.dp(this@DaftarHadirActivity, 6),
                Ui.dp(this@DaftarHadirActivity, 14), Ui.dp(this@DaftarHadirActivity, 12))
        }
        kartu.addView(isi)

        fun bidang(label: String, nilai: String) = TextInputEditText(this).apply {
            hint = label
            setText(nilai)
            textSize = 15f
            inputType = android.text.InputType.TYPE_CLASS_TEXT
            isSingleLine = true
        }

        val nama = TextInputEditText(this).apply { hint = "Nama"; setText(values?.nama.orEmpty()); textSize = 15f }
        val jk = Spinner(this).apply {
            adapter = ArrayAdapter(this@DaftarHadirActivity,
                R.layout.item_pilihan, listOf("", "L", "P")).also {
                it.setDropDownViewResource(R.layout.item_pilihan_dropdown)
            }
            val pos = when (values?.jenisKelamin) {
                "L" -> 1
                "P" -> 2
                else -> 0
            }
            setSelection(pos)
        }
        val jabatan = TextInputEditText(this).apply { hint = "Jabatan"; setText(values?.jabatan.orEmpty()); textSize = 15f }
        val nip = TextInputEditText(this).apply { hint = "NIP"; setText(values?.nip.orEmpty()); textSize = 15f }
        val nik = TextInputEditText(this).apply { hint = "NIK"; setText(values?.nik.orEmpty()); textSize = 15f }
        val noHp = TextInputEditText(this).apply {
            hint = "No. Handphone"; setText(values?.noHp.orEmpty()); textSize = 15f
            inputType = android.text.InputType.TYPE_CLASS_PHONE
        }
        val alamat = TextInputEditText(this).apply { hint = "Alamat"; setText(values?.alamat.orEmpty()); textSize = 15f }
        val keterangan = TextInputEditText(this).apply { hint = "Keterangan"; setText(values?.keterangan.orEmpty()); textSize = 15f }

        isi.addView(nama)
        isi.addView(TextView(this).apply {
            text = "Jenis Kelamin (L/P)"
            textSize = 12f
            setTextColor(ContextCompat.getColor(this@DaftarHadirActivity, R.color.teks_redup))
            setPadding(0, Ui.dp(this@DaftarHadirActivity, 8), 0, Ui.dp(this@DaftarHadirActivity, 4))
        })
        isi.addView(jk)
        isi.addView(jabatan)
        isi.addView(nip)
        isi.addView(nik)
        isi.addView(noHp)
        isi.addView(alamat)
        isi.addView(keterangan)

        wadahPeserta.addView(kartu)
        daftarPeserta.add(BarisPeserta(kartu, nama, jk, jabatan, nip, nik, noHp, alamat, keterangan))
    }

    private fun perbaruiInfo() {
        val jumlahKolom = jumlahKolomTerpilih()
        val landscape = jumlahKolom >= 7
        val minimal = if (landscape) MIN_LANSKAP else MIN_POTRET
        infoPeserta.text = "Baris grid: ${daftarPeserta.size} baris · orientasi " +
            if (landscape) "lanskap" else "potret" +
            " · semua baris ikut dicetak, kekurangan diisi baris kosong hingga minimal $minimal baris."
    }

    private fun jumlahKolomTerpilih(): Int {
        var total = 3 // No, Nama, Tanda Tangan wajib
        if (checkJK.isChecked) total += 1
        if (checkJabatan.isChecked) total += 1
        if (checkNip.isChecked) total += 1
        if (checkNik.isChecked) total += 1
        if (checkNohp.isChecked) total += 1
        if (checkAlamat.isChecked) total += 1
        if (checkKet.isChecked) total += 1
        return total
    }

    private fun cetak() {
        // Hari/Tanggal belum pernah diedit? Segarkan seperti desktop.
        if (!hariTanggalDisentuh) isiHariTanggalHariIni()

        val pengaturan = PengaturanSurat.ambil(this)
        val peserta = daftarPeserta.map {
            DaftarHadirPdf.Peserta(
                nama = it.nama.text.toString().trim(),
                jenisKelamin = it.jk.selectedItem?.toString().orEmpty().trim(),
                jabatan = it.jabatan.text.toString().trim(),
                nip = it.nip.text.toString().trim(),
                nik = it.nik.text.toString().trim(),
                noHp = it.noHp.text.toString().trim(),
                alamat = it.alamat.text.toString().trim(),
                keterangan = it.keterangan.text.toString().trim(),
            )
        }
        val data = DaftarHadirPdf.Data(
            judul = inputJudul.text.toString().trim().ifBlank { "DAFTAR HADIR" },
            hariTanggal = inputHariTanggal.text.toString().trim(),
            pukul = inputPukul.text.toString().trim(),
            tempat = inputTempat.text.toString().trim(),
            peserta = peserta,
            tampilJenisKelamin = checkJK.isChecked,
            tampilJabatan = checkJabatan.isChecked,
            tampilNip = checkNip.isChecked,
            tampilNik = checkNik.isChecked,
            tampilNoHp = checkNohp.isChecked,
            tampilAlamat = checkAlamat.isChecked,
            tampilKeterangan = checkKet.isChecked,
            tampilFooter = checkFooter.isChecked,
            tampilKop = checkKop.isChecked,
        )

        lifecycleScope.launch {
            try {
                val berkas = withContext(Dispatchers.IO) {
                    val logo: Bitmap? = MuatLogo.muat(this@DaftarHadirActivity, pengaturan.logoPath)
                    DaftarHadirPdf.buat(this@DaftarHadirActivity, data, pengaturan, logo)
                }
                PdfAksi.cetak(this@DaftarHadirActivity, berkas, "Daftar Hadir")
            } catch (e: Exception) {
                Toast.makeText(
                    this@DaftarHadirActivity,
                    "Gagal membuat PDF: ${e.message ?: "galat tidak diketahui"}",
                    Toast.LENGTH_LONG,
                ).show()
            }
        }
    }

    companion object {
        private const val MIN_POTRET = 10
        private const val MIN_LANSKAP = 6
    }
}
