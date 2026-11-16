package dev.sumberjaya.sudes.android

import android.graphics.Bitmap
import android.os.Bundle
import android.view.ViewGroup
import android.widget.Button
import android.widget.LinearLayout
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AlertDialog
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import androidx.lifecycle.lifecycleScope
import com.google.android.material.card.MaterialCardView
import com.google.android.material.checkbox.MaterialCheckBox
import com.google.android.material.textfield.TextInputEditText
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/**
 * Daftar Hadir: membuat dan mencetak daftar hadir kosong sebagai PDF.
 *
 * Baris peserta tidak diisi di aplikasi — PDF dicetak dengan baris kosong
 * bernomor untuk diisi tangan di lembar cetakan. Operator hanya menentukan
 * kegiatan, kolom yang dicetak, dan jumlah baris (bawaan 10, bisa ditambah /
 * dihapus). Jumlah baris ikut tercetak, minimum 10 (potret) / 6 (lanskap)
 * mengikuti `DaftarHadirDocument` desktop. PDF dibuat di `cacheDir`, dicetak
 * lewat pemindai Android, dan tidak masuk antrean Supabase.
 */
class DaftarHadirActivity : AppCompatActivity() {

    /** Satu baris kosong: kartu pinggir + label nomornya. */
    private data class BarisKosong(val kartu: MaterialCardView, val nomor: TextView)

    private val daftarBaris = mutableListOf<BarisKosong>()

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

        for (i in 0 until MIN_POTRET) tambahBaris()
        perbaruiInfo()

        findViewById<Button>(R.id.btn_hari_ini).setOnClickListener { isiHariTanggalHariIni() }
        findViewById<Button>(R.id.btn_tambah).setOnClickListener {
            tambahBaris()
            perbaruiInfo()
        }
        findViewById<Button>(R.id.btn_hapus).setOnClickListener {
            if (daftarBaris.isNotEmpty()) {
                val terakhir = daftarBaris.removeAt(daftarBaris.lastIndex)
                wadahPeserta.removeView(terakhir.kartu)
                pasangNomor()
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
        val teks = SimpleDateFormat("dd/MM/yyyy", Locale("id", "ID")).format(Date())
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
                daftarBaris.clear()
                for (i in 0 until MIN_POTRET) tambahBaris()
                isiHariTanggalHariIni()
                perbaruiInfo()
            }
            .show()
    }

    /** Satu baris kosong baru; dipakai kondisi awal, tombol Tambah, dan Bersihkan. */
    private fun tambahBaris() {
        val kartu = MaterialCardView(this).apply {
            radius = Ui.dp(this@DaftarHadirActivity, 14).toFloat()
            strokeWidth = Ui.dp(this@DaftarHadirActivity, 1)
            strokeColor = ContextCompat.getColor(this@DaftarHadirActivity, R.color.garis)
            setCardBackgroundColor(ContextCompat.getColor(this@DaftarHadirActivity, R.color.surface))
            cardElevation = 0f
            layoutParams = LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.WRAP_CONTENT,
            ).apply { topMargin = Ui.dp(this@DaftarHadirActivity, 8) }
        }
        val nomor = TextView(this).apply {
            textSize = 14f
            setTextColor(ContextCompat.getColor(this@DaftarHadirActivity, R.color.teks_redup))
            setPadding(
                Ui.dp(this@DaftarHadirActivity, 16),
                Ui.dp(this@DaftarHadirActivity, 12),
                Ui.dp(this@DaftarHadirActivity, 16),
                Ui.dp(this@DaftarHadirActivity, 12),
            )
        }
        kartu.addView(nomor)
        wadahPeserta.addView(kartu)
        daftarBaris.add(BarisKosong(kartu, nomor))
        pasangNomor()
    }

    /** Nomor baris selalu berurutan walau baris terakhir baru saja dihapus. */
    private fun pasangNomor() {
        for ((i, b) in daftarBaris.withIndex()) b.nomor.text = "Baris ${i + 1}"
    }

    private fun perbaruiInfo() {
        val jumlahKolom = jumlahKolomTerpilih()
        val landscape = jumlahKolom >= 7
        val minimal = if (landscape) MIN_LANSKAP else MIN_POTRET
        val orientasi = if (landscape) "lanskap" else "potret"
        infoPeserta.text =
            "PDF mencetak ${daftarBaris.size} baris kosong · orientasi $orientasi · minimum $minimal baris."
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
        // Baris kosong: isian daring tidak diperlukan, PDF diisi tangan.
        val peserta = List(daftarBaris.size) { DaftarHadirPdf.Peserta() }
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
                PdfAksi.lihat(this@DaftarHadirActivity, berkas, "Daftar Hadir")
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
