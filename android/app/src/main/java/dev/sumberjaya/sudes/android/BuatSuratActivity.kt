package dev.sumberjaya.sudes.android

import android.content.res.ColorStateList
import android.os.Bundle
import android.graphics.Typeface
import android.text.InputType
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.ArrayAdapter
import android.widget.Button
import android.widget.EditText
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.TextView
import android.widget.Toast
import androidx.activity.OnBackPressedCallback
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
import java.util.Locale

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
    private val wadahBaris = mutableMapOf<String, LinearLayout>()

    /** Satu baris pada bidang type="list" beserta field penyusunnya. */
    private data class BarisDaftar(val wadah: View, val bidang: Map<String, View>)

    private lateinit var judulHalaman: TextView
    private lateinit var wadahPilih: View
    private lateinit var wadahKartuJenis: LinearLayout
    private lateinit var kartuForm: View
    private lateinit var tombolKirim: Button
    private lateinit var wadahForm: LinearLayout
    private lateinit var progres: View

    private var daftar: List<SkemaJenis> = emptyList()
    private var terpilih: SkemaJenis? = null
    private var modeEdit = false
    private var ntcrGrup = false

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_buat_surat)
        Ui.insetAtas(findViewById(R.id.kepala))
        Ui.insetBawah(findViewById(R.id.konten))

        val grup = intent.getStringExtra(EXTRA_GROUPS) ?: GROUP_SURAT
        ntcrGrup = grup == GROUP_NTCR
        daftar = if (ntcrGrup) SkemaLokal.ntcr(this) else SkemaLokal.suratBiasa(this)

        judulHalaman = findViewById(R.id.judul_halaman)
        wadahPilih = findViewById(R.id.wadah_pilih)
        wadahKartuJenis = findViewById(R.id.wadah_kartu_jenis)
        kartuForm = findViewById(R.id.kartu_form)
        tombolKirim = findViewById(R.id.btn_kirim)
        wadahForm = findViewById(R.id.wadah_form)
        progres = findViewById(R.id.progres)

        // Mode Edit (dari Register): jenis terkunci, formulir diisi tangkapannya.
        modeEdit = intent.hasExtra(EXTRA_EDIT_JENIS)
        if (modeEdit) {
            // Cari di kedua grup: Register selalu mengirim GROUP_SURAT, padahal
            // surat NTCR termasuk kelompok lain — filter di kelompok SURAT saja
            // membuat surat NTCR tidak ketemu dan form terlihat kosong.
            val semua = SkemaLokal.suratBiasa(this) + SkemaLokal.ntcr(this)
            daftar = semua.filter { it.idJenis == intent.getStringExtra(EXTRA_EDIT_JENIS) }
            if (daftar.isEmpty()) {
                Toast.makeText(this, getString(R.string.edit_jenis_tidak_dikenal), Toast.LENGTH_LONG).show()
                finish()
                return
            }
        }
        if (daftar.isEmpty()) {
            Toast.makeText(this, R.string.register_kosong, Toast.LENGTH_LONG).show()
            finish()
            return
        }

        tombolKirim.setOnClickListener {
            // Cegah dobel klik: satu pembuatan = satu PDF + satu antrean.
            tombolKirim.isEnabled = false
            buatPdf { tombolKirim.isEnabled = true }
        }

        // Di langkah formulir, tombol kembali dipakai memilih jenis lain.
        onBackPressedDispatcher.addCallback(this, object : OnBackPressedCallback(true) {
            override fun handleOnBackPressed() {
                if (!modeEdit && kartuForm.visibility == View.VISIBLE) {
                    kembaliKePilihan()
                } else {
                    isEnabled = false
                    onBackPressedDispatcher.onBackPressed()
                }
            }
        })

        if (modeEdit) {
            title = getString(R.string.edit_surat_judul, daftar.first().label)
            aturKepala(daftar.first().label, daftar.first().ntcr)
            tampilkanForm(daftar.first())
            intent.getStringExtra(EXTRA_EDIT_FIELDS)?.let { json -> isiDariEdit(json) }
        } else {
            val judul = if (ntcrGrup) getString(R.string.label_buat_ntcr)
            else getString(R.string.label_buat_surat)
            title = judul
            aturKepala(judul, ntcrGrup)
            bangunPilihan()
        }
    }

    /**
     * Langkah pertama: semua jenis surat ditawarkan sebagai kartu. Memilih
     * satu kartu langsung membuka formulirnya, jadi operator tidak perlu
     * membaca daftar dropdown.
     */
    private fun bangunPilihan() {
        wadahKartuJenis.removeAllViews()
        for (jenis in daftar) {
            val kartu = layoutInflater.inflate(R.layout.item_menu, wadahKartuJenis, false)
                as MaterialCardView
            kartu.findViewById<ImageView>(R.id.ikon).setImageResource(
                if (jenis.ntcr) R.drawable.ic_ntcr else R.drawable.ic_surat,
            )
            kartu.findViewById<TextView>(R.id.judul).text = jenis.label
            kartu.findViewById<TextView>(R.id.ket).text = jenis.idJenis
            kartu.setOnClickListener { tampilkanForm(jenis) }
            wadahKartuJenis.addView(kartu)
        }
    }

    /** Langkah kedua: pilihan disembunyikan, formulir jenis terpilih tampil. */
    private fun tampilkanForm(jenis: SkemaJenis) {
        wadahPilih.visibility = View.GONE
        kartuForm.visibility = View.VISIBLE
        tombolKirim.visibility = View.VISIBLE
        title = jenis.label
        aturKepala(jenis.label, jenis.ntcr)
        bangunForm(jenis)
    }

    /** Kembali ke daftar kartu; isian formulir dibuang karena jenis berubah. */
    private fun kembaliKePilihan() {
        bangunForm(null)
        kartuForm.visibility = View.GONE
        tombolKirim.visibility = View.GONE
        wadahPilih.visibility = View.VISIBLE
        val judul = if (ntcrGrup) getString(R.string.label_buat_ntcr)
        else getString(R.string.label_buat_surat)
        title = judul
        aturKepala(judul, ntcrGrup)
    }

    /** Judul halaman mengikuti langkah: kelompok saat memilih, jenis saat mengisi. */
    private fun aturKepala(judul: String, ntcr: Boolean) {
        judulHalaman.text = judul
        findViewById<TextView>(R.id.subjudul_halaman).text =
            getString(if (ntcr) R.string.ket_buat_ntcr else R.string.ket_buat_surat)
    }

    /**
     * Isi formulir dari tangkapan edit (Register → Edit).
     * Teks langsung ditulis ke bidang; select memilih opsi yang cocok;
     * list diisi ulang baris per baris dari array tersimpan.
     */
    private fun isiDariEdit(json: String) {
        try {
            val fields = JSONObject(json)
            val jenis = terpilih ?: return
            for (b in jenis.fieldsInput) {
                if (b.key.equals("nomorSurat", ignoreCase = true)) continue
                if (!fields.has(b.key)) continue
                when (b.type) {
                    "select" -> {
                        val nilai = fields.optString(b.key)
                        val posisi = b.options.indexOf(nilai)
                        if (posisi >= 0) pilihan[b.key]?.setText(nilai, false)
                        else pilihan[b.key]?.setText(nilai, false)
                    }
                    "list" -> {
                        val arr = fields.optJSONArray(b.key) ?: continue
                        val barisList = daftarBaris[b.key] ?: continue
                        val wadahTarget = wadahBaris[b.key] ?: continue
                        for (i in 0 until arr.length()) {
                            buatBarisList(b, wadahTarget, barisList)
                            val objek = arr.optJSONObject(i) ?: continue
                            val baris = barisList.lastOrNull() ?: continue
                            for (anak in b.children) {
                                if (!objek.has(anak.key)) continue
                                val v = baris.bidang[anak.key] ?: continue
                                when (v) {
                                    is EditText -> v.setText(objek.optString(anak.key))
                                    is MaterialAutoCompleteTextView -> v.setText(objek.optString(anak.key), false)
                                    else -> {}
                                }
                            }
                        }
                    }
                    else -> {
                        val v = bidang[b.key] ?: continue
                        (v as? EditText)?.setText(fields.optString(b.key))
                    }
                }
            }
        } catch (e: Exception) {
            Toast.makeText(this, "Data edit tidak bisa dibaca", Toast.LENGTH_LONG).show()
        }
    }

    private fun bangunForm(jenis: SkemaJenis?) {
        terpilih = jenis
        wadahForm.removeAllViews()
        bidang.clear()
        pilihan.clear()
        daftarBaris.clear()
        wadahBaris.clear()
        if (jenis == null) return

        // Nomor surat dibuat aplikasi, bukan diketik operator.
        val input = jenis.fieldsInput.filterNot { it.key.equals("nomorSurat", ignoreCase = true) }

        // Satu kartu per grup skema — judul dan urutannya sama dengan section
        // kartu pada form desktop. Grup kosong (skema lama dari cache server)
        // berarti satu wadah datar seperti sebelumnya, jadi perangkat yang
        // belum tersinkron tidak kehilangan isian.
        for ((judul, anggota) in jenis.grupkan(input)) {
            val wadahGrup = if (judul.isBlank()) wadahForm else kartuGrup(judul)
            for (b in anggota) {
                when (b.type) {
                    "select" -> {
                        val wadahPilih = bidangPilih(b.label + wajib(b.required), b.options)
                        pilihan[b.key] = wadahPilih.editText as MaterialAutoCompleteTextView
                        wadahGrup.addView(wadahPilih)
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
                        wadahBaris[b.key] = wadah

                        // Tombol dan petunjuk memakai bentuk singkat seperti di
                        // desktop ("Tambah Bidang" / "Tambah Anak"), bukan judul
                        // blok skema yang panjang.
                        val (tombolTambah, petunjuk) = when (b.key) {
                            "rincianTambahan" -> "Tambah Bidang" to
                                "Kosongkan bila garapan hanya satu. Baris kosong otomatis dilewati saat disimpan:"
                            "anak" -> "Tambah Anak" to
                                "Baris tanpa data otomatis dilewati saat disimpan:"
                            else -> "Tambah ${b.label}" to ""
                        }
                        if (petunjuk.isNotEmpty()) {
                            wadah.addView(TextView(this).apply {
                                text = petunjuk
                                textSize = 12f
                                setTextColor(
                                    ContextCompat.getColor(this@BuatSuratActivity, R.color.teks_redup)
                                )
                                setPadding(
                                    Ui.dp(this@BuatSuratActivity, 2),
                                    Ui.dp(this@BuatSuratActivity, 4),
                                    Ui.dp(this@BuatSuratActivity, 2),
                                    Ui.dp(this@BuatSuratActivity, 2),
                                )
                            })
                        }
                        wadah.addView(tombolKecil(tombolTambah) { buatBarisList(b, wadah, barisList) })
                        wadahGrup.addView(wadah)
                    }

                    else -> {
                        val wadahTeks = bidangTeks(b.label + wajib(b.required), b.type)
                        val isian = wadahTeks.editText as EditText
                        bidang[b.key] = isian
                        praisiWilayah(b.key, isian)
                        wadahGrup.addView(wadahTeks)
                    }
                }
            }
        }
    }

    /**
     * Kartu satu grup isian dengan kepala bergaya sama seperti desktop
     * (bar aksen hijau + judul kapital). Mengembalikan wadah tempat bidang
     * ditambahkan.
     */
    private fun kartuGrup(judul: String): LinearLayout {
        val kartu = MaterialCardView(this).apply {
            radius = Ui.dp(this@BuatSuratActivity, 18).toFloat()
            strokeWidth = Ui.dp(this@BuatSuratActivity, 1)
            strokeColor = ContextCompat.getColor(this@BuatSuratActivity, R.color.garis)
            setCardBackgroundColor(ContextCompat.getColor(this@BuatSuratActivity, R.color.surface))
            cardElevation = 0f
            isClickable = false
            layoutParams = LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.WRAP_CONTENT,
            ).apply { topMargin = Ui.dp(this@BuatSuratActivity, 12) }
        }
        val isi = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(
                Ui.dp(this@BuatSuratActivity, 16),
                Ui.dp(this@BuatSuratActivity, 14),
                Ui.dp(this@BuatSuratActivity, 16),
                Ui.dp(this@BuatSuratActivity, 14),
            )
        }

        val kepala = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        kepala.addView(View(this).apply {
            setBackgroundColor(ContextCompat.getColor(this@BuatSuratActivity, R.color.accent))
            layoutParams = LinearLayout.LayoutParams(
                Ui.dp(this@BuatSuratActivity, 4),
                Ui.dp(this@BuatSuratActivity, 15),
            ).apply { marginEnd = Ui.dp(this@BuatSuratActivity, 8) }
        })
        kepala.addView(TextView(this).apply {
            text = judul.uppercase(Locale.getDefault())
            textSize = 12f
            letterSpacing = 0.06f
            setTypeface(typeface, Typeface.BOLD)
            setTextColor(ContextCompat.getColor(this@BuatSuratActivity, R.color.accent_dark))
        })
        isi.addView(kepala)

        kartu.addView(isi)
        wadahForm.addView(kartu)
        return isi
    }

    /**
     * Desa/Kecamatan/Kabupaten diisi dari pengaturan kop: identitas itu sama
     * di semua surat, operator tidak perlu mengetiknya berulang kali. Kolom
     * turunan (Desa Anak, Desa Pelapor, Desa Tujuan, dan sebagainya) tidak
     * diisi — itu alamat pihak lain yang bisa berbeda.
     */
    private fun praisiWilayah(key: String, isian: EditText) {
        val p = PengaturanSurat.ambil(this)
        val nilai = when (key) {
            "desa" -> p.namaDesa
            "kecamatan" -> p.kecamatan
            "kabupaten" -> p.kabupaten
            else -> return
        }
        if (nilai.isNotBlank()) isian.setText(nilai)
    }

    /** Buat satu baris kartu daftar (dipakai tombol Tambah & isi Edit). */
    private fun buatBarisList(
        b: SkemaBidang,
        wadah: LinearLayout,
        barisList: MutableList<BarisDaftar>,
    ) {
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

    /** Pemilih tanggal: hasil dd/MM/yyyy (format Indonesia, diterima impor desktop). */
    private fun pilihTanggal(et: TextInputEditText) {
        val kalender = Calendar.getInstance()
        android.app.DatePickerDialog(
            this,
            { _, tahun, bulan, hari ->
                et.setText(
                    "%02d/%02d/%04d".format(hari, bulan + 1, tahun),
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

        // Desktop mencetak tanggal LAHIR dengan pola tetap per jenis surat
        // (lihat TanggalIndonesia.polaTanggalLahir). Pemilih tanggal Android
        // menghasilkan dd/MM/yyyy, bentuk yang tidak pernah muncul di PDF
        // desktop, jadi semua kunci tanggal lahir dinormalkan lebih dulu.
        val polaLahir = TanggalIndonesia.polaTanggalLahir(jenis.idJenis)
        for ((k, v) in nilai.toList()) {
            val ekor = k.substringAfterLast('.').lowercase(Locale.getDefault())
            if (ekor.contains("tanggallahir") || ekor.contains("tgllahir")) {
                nilai[k] = TanggalIndonesia.dari(v, polaLahir)
            }
        }

        // Keterangan: isian operator bila ada, kalau kosong pakai keterangan
        // bawaan jenis surat — sama seperti desktop saat form dibuka.
        val isianKeterangan = listOf("keperluan", "keterangan", "keteranganTambahan")
            .firstNotNullOfOrNull { k -> teksBidang(k).ifBlank { null } }

        nilai["warga.tempatTanggalLahir"] = listOf(
            teksBidang("tempatLahir").ifBlank { "...." },
            TanggalIndonesia.dari(teksBidang("tanggalLahir"), polaLahir).ifBlank { "...." },
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
        nilai["kematian.tanggal"] = tanggalPanjangIndo(teksBidang("tanggalKematian"))
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

        // Tanggal surat (dipakai sebagian templat badan). Bentuk sama dengan
        // desktop (SuratGeneratorBase): "dd MMMM yyyy" bahasa Indonesia,
        // mis. "01 Januari 1990" — bukan dd/MM/yyyy.
        nilai["surat.tanggal"] = TanggalIndonesia.panjang()

        // Baru sekarang keterangan disusun: isian operator menang, kalau kosong
        // pakai bawaan jenis surat. Harus SETELAH $desa.* terisi.
        nilai["keterangan"] = isianKeterangan
            ?: if (jenis.keteranganBawaan.isNotBlank()) {
                SuratPdf.urai(jenis.keteranganBawaan, nilai)
            } else {
                ""
            }

        // Rincian garapan tambahan (bidang ke-2 dst.): setiap baris list
        // rincianTambahan Dipetakan ke kunci garapanTambahan[i].* yang dibaca
        // blok tabel PDF ("garapan.tabel"), nomor melanjutkan bidang pertama.
        fields.optJSONArray("rincianTambahan")?.let { arrayTambahan ->
            for (j in 0 until arrayTambahan.length()) {
                val baris = arrayTambahan.optJSONObject(j) ?: continue
                val awalanBaris = "garapanTambahan[${j + 2}]."
                for (kunci in listOf("lokasi", "nomorPersil", "keteranganGarapan")) {
                    nilai[awalanBaris + kunci] = baris.optString(kunci, "")
                }
                nilai[awalanBaris + "luas"] = baris.opt("luas")?.toString() ?: ""
            }
        }

        // Surat Ahli Waris: kunci turunan yang di generator desktop dihitung
        // dari objek AhliWarisData/KematianData — Android menghitung dari
        // isian formulir dengan bentuk teks yang sama.
        if (jenis.idJenis.equals("AHLI_WARIS", ignoreCase = true)) {
            val namaAlmarhum = teksBidang("nama").ifBlank { "..." }
            val almarhumPerempuan = teksBidang("jenisKelamin")
                .contains("Perempuan", ignoreCase = true)
            // Gelar tidak ikut kapital — sama seperti NamaFormatter desktop.
            val namaKapital = NamaFormatter.kapitalTiapKata(namaAlmarhum)
            nilai["ahliwaris.almarhumNama"] =
                (if (almarhumPerempuan) "Almarhumah" else "Almarhum") + " " + namaKapital
            val rtRw = AlamatSusun.formatRtRw(
                nilai["warga.rt"]?.ifBlank { null }, nilai["warga.rw"]?.ifBlank { null },
            )
            nilai["ahliwaris.almarhumAlamat"] = listOf(
                (nilai["warga.dusun"] ?: ""),
                rtRw,
                "Desa " + (nilai["warga.desa"] ?: ""),
                "Kecamatan " + (nilai["warga.kecamatan"] ?: ""),
                "Kabupaten " + (nilai["warga.kabupaten"] ?: ""),
            ).filter { it.isNotBlank() }.joinToString(" ")

            val namaPasangan = teksBidang("namaPasangan")
            if (namaPasangan.isNotBlank()) {
                nilai["ahliwaris.pasangan.nama"] = NamaFormatter.kapitalTiapKata(namaPasangan)
                nilai["ahliwaris.pasangan.tempatTanggalLahir"] = listOf(
                    teksBidang("tempatLahirPasangan").ifBlank { "...." },
                    tanggalPanjangIndo(teksBidang("tanggalLahirPasangan")).ifBlank { "...." },
                ).joinToString(", ")
                nilai["ahliwaris.pasangan.jenisKelamin"] = teksBidang("jenisKelaminPasangan")
                nilai["ahliwaris.pasangan.alamat"] = teksBidang("alamatPasangan")
            }
            // Sebutan kata pasangan menyesuaikan jenis kelamin yang dipilih.
            nilai["ahliwaris.jenisKelaminPasanganKata"] =
                if (teksBidang("jenisKelaminPasangan").contains("Perempuan", true)) "perempuan"
                else "laku-laki".let { "laki-laki" }

            val barisAnak = daftarBaris["anak"].orEmpty()
            val anakTerisi = barisAnak.filter { isiBaris(it, "nama").isNotBlank() }
            for ((idx, anak) in anakTerisi.withIndex()) {
                val awalan = "ahliwaris.anak[$idx]."
                nilai[awalan + "nama"] = NamaFormatter.kapitalTiapKata(isiBaris(anak, "nama"))
                nilai[awalan + "tempatTanggalLahir"] = listOf(
                    isiBaris(anak, "tempatLahir").ifBlank { "...." },
                    tanggalPanjangIndo(isiBaris(anak, "tanggalLahir")).ifBlank { "...." },
                ).joinToString(", ")
                nilai[awalan + "jenisKelamin"] = isiBaris(anak, "jenisKelamin")
                nilai[awalan + "alamat"] = isiBaris(anak, "alamat")
            }
            if (anakTerisi.isNotEmpty()) {
                nilai["ahliwaris.jumlahAnak"] = anakTerisi.size.toString()
                nilai["ahliwaris.jumlahAnakTerbilang"] = terbilangIndo(anakTerisi.size)
            }
            // Penanda tangan pernyataan & pemberi kuasa: pasangan lalu tiap anak.
            val penandatangan = buildList {
                if (namaPasangan.isNotBlank()) add(NamaFormatter.kapitalTiapKata(namaPasangan))
                for (anak in anakTerisi) {
                    val nama = isiBaris(anak, "nama").trim()
                    if (nama.isNotBlank()) add(NamaFormatter.kapitalTiapKata(nama))
                }
            }
            nilai["ahliwaris.penandatangan"] = penandatangan.joinToString("\n")
        }

        // Rekening Koran: badan surat memakai $pejabat.alamat dan $rekeningkoran.*
        // yang tidak punya kolom sendiri, jadi disusun di sini dari kolom alamat
        // dan kolom periode — bentuknya sama dengan RekeningKoranGenerator desktop
        // ("01 Januari 2026 s/d 31 Maret 2026").
        if (jenis.idJenis.equals("REKENING_KORAN", ignoreCase = true)) {
            nilai["pejabat.alamat"] = AlamatSusun.susun(
                dusunJalan = teksBidang("alamatDusun"),
                desa = teksBidang("alamatDesa"),
                kecamatan = teksBidang("alamatKecamatan"),
                kabupaten = teksBidang("alamatKabupaten"),
                rt = teksBidang("alamatRt"),
                rw = teksBidang("alamatRw"),
            )
            nilai["rekeningkoran.namaPemegang"] = teksBidang("namaRekening")
            nilai["rekeningkoran.periode"] = listOf(
                tanggalPanjangIndo(teksBidang("tanggalMulai")),
                tanggalPanjangIndo(teksBidang("tanggalSelesai")),
            ).filter { it.isNotBlank() }.joinToString(" s/d ").ifBlank { "N/A" }
        }

        // Blanko NTCR: desktop membaca objek orang tua (`NtcrOrangTua`) dan
        // memformat selnya lewat Isi()/NamaAtauTitik()/TempatTanggalLahir().
        // Skema menuliskan target dengan huruf besar di depan
        // ("ntcr.AyahCalonSuami.nama") sementara badan memakai ejaan properti
        // C# ("ntcr.ayahCalonSuami.nama") — tanpa jembatan ini seluas blok
        // orang tua tercetak kosong di Android walau desktopnya terisi penuh.
        val titikNtcR = "................................"
        fun isianNtcR(kunci: String) {
            val v = nilai[kunci]
            nilai[kunci] = if (v.isNullOrBlank()) titikNtcR else v.trim()
        }
        if ("N1" in jenis.idJenis.uppercase() || "N2" in jenis.idJenis.uppercase() ||
            "N4" in jenis.idJenis.uppercase() || "N6" in jenis.idJenis.uppercase()
        ) {
            for ((prop, prefiks) in listOf(
                    "AyahCalonSuami" to "ayahCalonSuami", "IbuCalonSuami" to "ibuCalonSuami",
                    "AyahCalonIstri" to "ayahCalonIstri", "IbuCalonIstri" to "ibuCalonIstri",
                )) {
                val akar = "ntcr.$prop."
                for (anak in listOf("nama", "nik", "tempatLahir", "tanggalLahir", "kewarganegaraan",
                                    "agama", "pekerjaan", "alamat", "binBinti")) {
                    val v = nilai[akar + anak]
                    if (!v.isNullOrBlank()) nilai["ntcr.$prefiks.$anak"] = v
                }
            }
            // Gaya blanko desktop (BarisOrangTua): nama kapital + titik, leaf
            // kosong menjadi titik, TTL digabung "dd MMMM yyyy".
            for (prefiks in listOf("ayahCalonSuami", "ibuCalonSuami", "ayahCalonIstri", "ibuCalonIstri")) {
                for (kosong in listOf("nik", "kewarganegaraan", "agama", "pekerjaan", "alamat", "binBinti"))
                    isianNtcR("ntcr.$prefiks.$kosong")
                nilai["ntcr.$prefiks.nama"] =
                    if (nilai["ntcr.$prefiks.nama"].isNullOrBlank()) titikNtcR
                    else NamaFormatter.kapitalTiapKata(nilai["ntcr.$prefiks.nama"]!!)
                nilai["ntcr.$prefiks.tempatTanggalLahir"] = ntcRTempatTanggal(
                    nilai["ntcr.$prefiks.tempatLahir"], nilai["ntcr.$prefiks.tanggalLahir"],
                )
            }
        }
        if (jenis.idJenis.uppercase().startsWith("NTCR_N5")) {
            // Baris orang tua pada N5 dipilih dari pihakAnakIzinOrtu; leaf yang
            // tidak punya kolom (nik/agama/pekerjaan/binBinti orang tua calon)
            // tercetak titik seperti Isi(null) di desktop.
            val (propAyah, propIbu) = if (nilai["ntcr.pihakAnakIzinOrtu"]
                    .equals("Istri", ignoreCase = true)
            ) "AyahCalonIstri" to "IbuCalonIstri" else "AyahCalonSuami" to "IbuCalonSuami"
            for ((prefiks, prop) in listOf("ayah" to propAyah, "ibu" to propIbu)) {
                for (anak in listOf("nama", "alamat", "tempatLahir", "tanggalLahir", "kewarganegaraan",
                                    "binBinti", "nik", "agama", "pekerjaan")) {
                    val v = nilai["ntcr.$prop.$anak"]
                    if (!v.isNullOrBlank()) nilai["ntcr.$prefiks.$anak"] = v
                }
                for (kosong in listOf("binBinti", "nik", "agama", "pekerjaan", "kewarganegaraan", "alamat"))
                    isianNtcR("ntcr.$prefiks.$kosong")
                nilai["ntcr.$prefiks.nama"] =
                    if (nilai["ntcr.$prefiks.nama"].isNullOrBlank()) titikNtcR
                    else NamaFormatter.kapitalTiapKata(nilai["ntcr.$prefiks.nama"]!!)
                nilai["ntcr.$prefiks.tempatTanggalLahir"] = ntcRTempatTanggal(
                    nilai["ntcr.$prefiks.tempatLahir"], nilai["ntcr.$prefiks.tanggalLahir"],
                )
                // BarisOrangTua(pakaiBin=true): Bin/Binti = nama ayah kandung,
                // kapital ala NamaAtauTitik — kolomnya bukan orang tua ini.
                val binAsal = nilai["ntcr.$prop.nama"]
                nilai["ntcr.$prefiks.binBinti"] =
                    if (binAsal.isNullOrBlank()) titikNtcR else NamaFormatter.kapitalTiapKata(binAsal)
            }
            // Baris identitas calon = BarisIdentitasCalon desktop: anak dari
            // warga (pihak = Suami) atau dari istri (pihak = Istri), pasangan
            // adalah kebalikannya. Kunci badan memakai ejaan turunan sendiri.
            val anakIstri = nilai["ntcr.pihakAnakIzinOrtu"].equals("Istri", ignoreCase = true)
            val namaAnakKey = if (anakIstri) "ntcr.namaIstri" else "warga.nama"
            val ayahAnakKey = if (anakIstri) "ntcr.ayahCalonIstri.nama" else "ntcr.ayahCalonSuami.nama"
            val namaPasanganKey = if (anakIstri) "warga.nama" else "ntcr.namaIstri"
            val ayahPasanganKey = if (anakIstri) "ntcr.ayahCalonSuami.nama" else "ntcr.ayahCalonIstri.nama"
            val ttlAnak = if (anakIstri)
                ntcRTempatTanggal(nilai["ntcr.tempatLahirIstri"], nilai["ntcr.tanggalLahirIstri"])
            else ntcRTempatTanggal(nilai["warga.tempatLahir"], nilai["warga.tanggalLahir"])
            val ttlPasangan = if (anakIstri)
                ntcRTempatTanggal(nilai["warga.tempatLahir"], nilai["warga.tanggalLahir"])
            else ntcRTempatTanggal(nilai["ntcr.tempatLahirIstri"], nilai["ntcr.tanggalLahirIstri"])
            fun blanko(v: String?) = if (v.isNullOrBlank()) titikNtcR else NamaFormatter.kapitalTiapKata(v)
            nilai["ntcr.namaAnak"] = blanko(nilai[namaAnakKey])
            nilai["ntcr.ayahCalonAnak.nama"] = blanko(nilai[ayahAnakKey])
            nilai["ntcr.nikAnak"] = if (anakIstri) nilai["ntcr.nikIstri"] ?: "" else nilai["warga.nik"] ?: ""
            nilai["ntcr.tempatTanggalLahirAnak"] = ttlAnak
            nilai["ntcr.kewarganegaraanAnak"] =
                (if (anakIstri) nilai["ntcr.kewarganegaraanIstri"] else nilai["warga.kewarganegaraan"])
                    ?.ifBlank { null } ?: "WNI"
            nilai["ntcr.agamaAnak"] = if (anakIstri) nilai["ntcr.agamaIstri"] ?: "" else nilai["warga.agama"] ?: ""
            nilai["ntcr.pekerjaanAnak"] = if (anakIstri) nilai["ntcr.pekerjaanIstri"] ?: "" else nilai["warga.pekerjaan"] ?: ""
            nilai["ntcr.alamatAnak"] = if (anakIstri) nilai["ntcr.alamatIstri"] ?: "" else nilai["warga.alamat"] ?: ""
            nilai["ntcr.namaPasanganAnak"] = blanko(nilai[namaPasanganKey])
            nilai["ntcr.ayahCalonPasangan.nama"] = blanko(nilai[ayahPasanganKey])
            nilai["ntcr.nikPasanganAnak"] = if (anakIstri) nilai["warga.nik"] ?: "" else nilai["ntcr.nikIstri"] ?: ""
            nilai["ntcr.tempatTanggalLahirPasanganAnak"] = ttlPasangan
            nilai["ntcr.kewarganegaraanPasanganAnak"] =
                (if (anakIstri) nilai["warga.kewarganegaraan"] else nilai["ntcr.kewarganegaraanIstri"])
                    ?.ifBlank { null } ?: "WNI"
            nilai["ntcr.agamaPasanganAnak"] = if (anakIstri) nilai["warga.agama"] ?: "" else nilai["ntcr.agamaIstri"] ?: ""
            nilai["ntcr.pekerjaanPasanganAnak"] = if (anakIstri) nilai["warga.pekerjaan"] ?: "" else nilai["ntcr.pekerjaanIstri"] ?: ""
            nilai["ntcr.alamatPasanganAnak"] = if (anakIstri) nilai["warga.alamat"] ?: "" else nilai["ntcr.alamatIstri"] ?: ""
        }
        // Identitas calon perempuan (blanko N1/N2/N4/N5/N6) dicetak gaya
        // blanko: kapital + titik bila kosong — sama seperti NamaAtauTitik().
        nilai["ntcr.namaIstri"] =
            if (nilai["ntcr.namaIstri"].isNullOrBlank()) titikNtcR
            else NamaFormatter.kapitalTiapKata(nilai["ntcr.namaIstri"]!!)
        nilai["ntcr.tempatTanggalLahirIstri"] = ntcRTempatTanggal(
            nilai["ntcr.tempatLahirIstri"], nilai["ntcr.tanggalLahirIstri"],
        )

        // KUNCI BADAN DILAPORKAN lewat `nilai` yang dibaca SuratPdf. Dulu
        // badan SKEMA memakai ejaan "kenallahir."/"surat.dataSource" yang tidak
        // pernah ditulis siapa pun, sehingga selnya tercetak KOSONG walau
        // desktop mencetak isinya. Skema sekarang memakai kunci yang dihitung
        // di bawah — rumus "N Tahun"/"dd MMMM yyyy" sama dengan
        // KematianGenerator/BedaNamaGenerator/KenalLahirGenerator desktop.
        val tanggalHariIni = java.text.SimpleDateFormat("yyyy-MM-dd",
            java.util.Locale.US).format(java.util.Date())
        if (jenis.idJenis.equals("KEMATIAN", ignoreCase = true)) {
            nilai["kematian.umurTampil"] = umurPada(
                teksBidang("tanggalLahir"), teksBidang("tanggalKematian"),
            )
            // Baris "Umur" jenazah pada blanko = umur PADA tanggal kematian
            // (HitungUmurPadaTanggal desktop), bukan umur hari ini.
            nilai["warga.umur"] = nilai["kematian.umurTampil"]!!
            nilai["kematian.umurPelaporTampil"] = umurPada(
                teksBidang("umurPelapor"), tanggalHariIni,
            )
            nilai["kematian.alamatPelaporTampil"] =
                alamatPelaporTampil(nilai["kematian.alamatPelapor"].orEmpty())
        }

        if (jenis.idJenis.equals("BEDANAMA", ignoreCase = true)) {
            nilai["wargaKk.nama"] = NamaFormatter.kapitalTiapKata(teksBidang("nama2"))
            nilai["wargaKk.tempatTanggalLahir"] = gabungTempatTanggal(
                teksBidang("tempatLahir2"), tanggalPanjangIndo(teksBidang("tanggalLahir2")),
            )
            nilai["wargaKk.alamat"] = AlamatSusun.susun(
                dusunJalan = teksBidang("dusun2"),
                desa = teksBidang("desa2"),
                kecamatan = teksBidang("kecamatan2"),
                kabupaten = teksBidang("kabupaten2"),
                fallback = "[Alamat Warga]",
            )
        }

        if (jenis.idJenis.equals("KENAL_LAHIR", ignoreCase = true)) {
            // Desktop: kartu pertama adalah AYAH sekaligus PEMOHON (warga.*),
            // jadi data ayah di Android dibaca dari kolom warga — persis
            // pemetaan KenalLahirInputViewModel. Ibu memakai kolom sendiri.
            nilai["ayah.nama"] = teksBidang("nama")
            nilai["ayah.nik"] = teksBidang("nik")
            nilai["ayah.tempatLahir"] = teksBidang("tempatLahir")
            nilai["ayah.tanggalLahir"] = TanggalIndonesia.dari(teksBidang("tanggalLahir"), polaLahir)
            nilai["ayah.agama"] = teksBidang("agama")
            nilai["ayah.pekerjaan"] = teksBidang("pekerjaan")
            nilai["ayah.tempatTanggalLahir"] = gabungTempatTanggal(
                teksBidang("tempatLahir"), tanggalPanjangIndo(teksBidang("tanggalLahir")),
            )
            nilai["ayah.alamat"] = nilai["warga.alamat"].orEmpty()
            nilai["ibu.tempatTanggalLahir"] = gabungTempatTanggal(
                teksBidang("ibuTempatLahir"), tanggalPanjangIndo(teksBidang("ibuTanggalLahir")),
            )
            nilai["ibu.alamat"] = AlamatSusun.susun(
                dusunJalan = teksBidang("ibuDusun"),
                desa = teksBidang("ibuDesa"),
                kecamatan = teksBidang("ibuKecamatan"),
                kabupaten = teksBidang("ibuKabupaten"),
                fallback = "[Alamat]",
            )
            nilai["anak.tempatTanggalLahir"] = gabungTempatTanggal(
                teksBidang("tempatLahirAnak"), tanggalPanjangIndo(teksBidang("tanggalLahirAnak")),
            )
            nilai["anak.ke"] = teksBidang("anakKe")
            nilai["anak.keTerbilang"] = anakKeTerbilang(teksBidang("anakKe"))
            nilai["anak.alamat"] = AlamatSusun.susun(
                dusunJalan = teksBidang("dusunAnak"),
                desa = teksBidang("desaAnak"),
                kecamatan = teksBidang("kecamatanAnak"),
                kabupaten = teksBidang("kabupatenAnak"),
                fallback = "[Alamat]",
            )
        }

        if (jenis.idJenis.equals("IJIN_TINGGAL", ignoreCase = true)) {
            nilai["penanggungjawab.nama"] = NamaFormatter.kapitalTiapKata(teksBidang("namaPenanggungJawab"))
            nilai["penanggungjawab.tanggalLahir"] = tanggalPanjangIndo(teksBidang("tglLahirPenanggungJawab"))
        }

        // Ahli Waris: tanggal kematian dalam kata — rumus FormatTanggalTerbilang
        // desktop: "<terbilang hari> bulan <nama bulan> tahun <terbilang tahun>".
        if (jenis.idJenis.equals("AHLI_WARIS", ignoreCase = true)) {
            nilai["kematian.tanggalTerbilang"] = tanggalKematianTerbilang(teksBidang("tanggalKematian"))
        }

        val tahunTarik = PengaturanSinkron.tahunTarik(this@BuatSuratActivity)
        val tahun = tahunTarik

        val namaEdit = intent.getStringExtra(EXTRA_EDIT_NAMA)
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
                    if (modeEdit && !namaEdit.isNullOrBlank()) {
                        PdfAksi.simpanKeDownloadsEdit(this@BuatSuratActivity, berkas, namaEdit)
                    } else {
                        PdfAksi.simpanKeDownloads(this@BuatSuratActivity, berkas)
                    }
                }

                // Simpan tangkapan isian supaya operator bisa memperbaiki lewat
                // Register → Edit tanpa membuat surat dari awal. Hanya bila
                // PDF benar-benar tersimpan ke Downloads (hindari yatim).
                if (lokasi != null) {
                    withContext(Dispatchers.IO) {
                        val namaBerkas = if (modeEdit && !namaEdit.isNullOrBlank()) namaEdit else berkas.name
                        SimpananEdit.simpan(
                            this@BuatSuratActivity, namaBerkas, jenis.idJenis, nomor, fields, pejabat,
                        )
                    }
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
                PdfAksi.lihat(this@BuatSuratActivity, berkas, jenis.label)
                // Surat sudah selesai (PDF tersimpan + isian masuk antrean):
                // tutup formulir supaya operator tidak menekan Back berkali-kali.
                // Bila ini layar Edit, yang di bawah adalah Register Surat.
                finish()
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
    /** Isi satu anak pada baris list ("anak") sebagai teks. */
    private fun isiBaris(baris: BarisDaftar, kunci: String): String {
        val v = baris.bidang[kunci] ?: return ""
        return when (v) {
            is EditText -> v.text.toString().trim()
            is MaterialAutoCompleteTextView -> v.text?.toString()?.trim().orEmpty()
            else -> ""
        }
    }

    /** Tanggal panjang Indonesia ("17 September 2026") dari tanggal bebas. */
    private fun tanggalPanjangIndo(iso: String): String {
        val cal = uraikanTanggal(iso) ?: return ""
        return java.text.SimpleDateFormat("dd MMMM yyyy", java.util.Locale("id", "ID")).format(cal.time)
    }

    /** Angka ke kata Indonesia untuk jumlah anak kecil (1..99). */
    private fun terbilangIndo(n: Int): String {
        val satuan = listOf("", "satu", "dua", "tiga", "empat", "lima",
            "enam", "tujuh", "delapan", "sembilan", "sepuluh", "sebelas")
        return when {
            n < 0 || n > 99 -> n.toString()
            n < 12 -> satuan[n]
            n == 12 -> "dua belas"
            n < 20 -> satuan[n - 10] + " belas"
            else -> {
                val puluh = n / 10
                val sisa = n % 10
                (if (puluh == 1) "sepuluh" else satuan[puluh]) + " puluh" +
                    (if (sisa > 0) " " + satuan[sisa] else "")
            }
        }
    }

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

    /**
     * Umur pada tanggal referensi — rumus HitungUmurPadaTanggal desktop:
     * `"<n> Tahun"`, titik-titik bila salah satu tanggal kosong, dan teks galat
     * khusus bila tanggal lahir menyusul referensi.
     */
    private fun umurPada(tanggalLahir: String, tanggalReferensi: String): String {
        val lahir = uraikanTanggal(tanggalLahir)
        val referensi = uraikanTanggal(tanggalReferensi)
        if (lahir == null || referensi == null) return "..............................."
        if (lahir.timeInMillis > referensi.timeInMillis) return "(Data Tanggal Tidak Valid)"
        var tahun = referensi.get(Calendar.YEAR) - lahir.get(Calendar.YEAR)
        if (lahir.get(Calendar.MONTH) > referensi.get(Calendar.MONTH) ||
            (lahir.get(Calendar.MONTH) == referensi.get(Calendar.MONTH) &&
                lahir.get(Calendar.DAY_OF_MONTH) > referensi.get(Calendar.DAY_OF_MONTH))
        ) tahun -= 1
        if (tahun < 0) tahun = 0
        return "$tahun Tahun"
    }

    /** "Tempat, 08 November 2026"; kosong hanya bila keduanya kosong. */
    private fun gabungTempatTanggal(tempat: String, tanggal: String): String {
        if (tempat.isBlank() && tanggal.isBlank()) return ""
        val t = tempat.ifBlank { "[Tempat Lahir]" }
        val g = tanggal.ifBlank { "[Tanggal Lahir]" }
        return "$t, $g"
    }

    /** Alamat pelapor kematian: "Dusun : x\nDesa : y\nKecamatan : z" gaya desktop. */
    private fun alamatPelaporTampil(alamatLengkap: String): String {
        if (alamatLengkap.isBlank()) return ""
        val bagian = alamatLengkap.split("Desa", "Kec.", "Kab.")
            .map { it.trim() }
            .filter { it.isNotEmpty() }
        if (bagian.size < 3) return alamatLengkap
        fun titik(v: String) = if (v.isBlank()) "................" else v
        return "Dusun : ${titik(bagian[0])}\nDesa : ${titik(bagian[1])}\nKecamatan : ${titik(bagian[2])}"
    }

    /** "Tempat, 08 November 1990" ala TempatTanggalLahir() desktop: titik bila
     *  keduanya kosong, komponen yang ada saja yang tercetak selain itu. */
    private fun ntcRTempatTanggal(tempat: String?, tanggal: String?): String {
        val t = tempat?.trim().orEmpty()
        val g = tanggal?.let { tanggalPanjangIndo(it).ifBlank { it.trim() } }.orEmpty()
        val titik = "................................"
        return when {
            t.isEmpty() && g.isEmpty() -> titik
            t.isEmpty() -> g.ifBlank { titik }
            g.isEmpty() -> t
            else -> "$t, $g"
        }
    }

    /** "Keempat" dari nomor urut — tabel ConvertAnakKeToText desktop. */
    private fun anakKeTerbilang(nilai: String): String {
        val n = nilai.trim().toIntOrNull() ?: return ""
        return when (n) {
            1 -> "Kesatu"; 2 -> "Kedua"; 3 -> "Ketiga"; 4 -> "Keempat"; 5 -> "Kelima"
            6 -> "Keenam"; 7 -> "Ketujuh"; 8 -> "Kedelapan"; 9 -> "Kesembilan"
            10 -> "Kesepuluh"; 11 -> "Kesebelas"; 12 -> "Kedua Belas"; 13 -> "Ketiga Belas"
            14 -> "Keempat Belas"; 15 -> "Kelima Belas"; 16 -> "Keenam Belas"
            17 -> "Ketujuh Belas"; 18 -> "Kedelapan Belas"; 19 -> "Kesembilan Belas"
            else -> if (n in 20..30) "Ke$n" else n.toString()
        }
    }

    /**
     * "delapan bulan November tahun dua ribu dua puluh enam" — rumus
     * FormatTanggalTerbilang desktop (AhliWarisGenerator), termasuk terbilang
     * kapital-pertama untuk hari dan tahun.
     */
    private fun tanggalKematianTerbilang(iso: String): String {
        val bersih = iso.trim()
        if (bersih.isEmpty()) return "-"
        val cal = uraikanTanggal(bersih) ?: return bersih
        val budaya = java.util.Locale("id", "ID")
        val bulan = java.text.SimpleDateFormat("MMMM", budaya).format(cal.time).lowercase()
        return "${terbilangBesar(cal.get(Calendar.DAY_OF_MONTH))} bulan $bulan tahun " +
            terbilangBesar(cal.get(Calendar.YEAR))
    }

    private fun terbilangBesar(n: Int): String =
        terbilangIndo(n).replaceFirstChar { it.uppercaseChar() }


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
        const val EXTRA_EDIT_JENIS = "edit_jenis"
        const val EXTRA_EDIT_FIELDS = "edit_fields"
        const val EXTRA_EDIT_NAMA = "edit_nama"

        /**
         * Jembatan ejaan antara templat badan (ditulis) dan prefiks 'target'
         * skema (dibaca). Nilai dari target ikut dipublikasikan di bawah nama
         * yang dipakai templat, supaya kolom tidak tercetak kosong.
         */
        private val ALIAS_KUNCI = mapOf(
            "wargaKk" to "wargaKK",
            "ahliwaris" to "ahliwarisdata",
            "ayah" to "kenalLahir.ayah",
            "ibu" to "kenalLahir.ibu",
            "anak" to "kenalLahir.anak",
            "rekeningkoran" to "additionalData",
        )

        /** Alias kunci penuh (ditulis → dibaca) untuk badan surat. */
        private val ALIAS_PENUH = mapOf(
            "instansi.nama" to "instansi.namaInstansi",
            "instansi.alamat" to "instansi.alamatInstansi",
            "instansi.nomorSk" to "instansi.nomorSkIzinUsaha",
            "instansi.pimpinan" to "instansi.pimpinanInstansi",
            "instansi.nikPimpinan" to "instansi.nikPimpinan",
            // Penanggung jawab Tinggal Sementara: target skema datar, badan
            // menyebutnya berprefiks — sama seperti Instansi di atas.
            "penanggungjawab.nik" to "nikPenanggungJawab",
            "penanggungjawab.nama" to "namaPenanggungJawab",
            "penanggungjawab.pekerjaan" to "pekerjaanPenanggungJawab",
            "penanggungjawab.tanggalLahir" to "tglLahirPenanggungJawab",
        )
    }
}
