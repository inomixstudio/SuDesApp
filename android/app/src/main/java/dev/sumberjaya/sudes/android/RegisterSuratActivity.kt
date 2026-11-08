package dev.sumberjaya.sudes.android

import android.content.Intent
import android.content.res.ColorStateList
import android.os.Bundle
import android.text.TextUtils
import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.ProgressBar
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AlertDialog
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import com.google.android.material.card.MaterialCardView
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/**
 * Register Surat: daftar PDF yang pernah dibuat di perangkat ini.
 *
 * Surat tidak dikirim ke mana pun, jadi "register" di perangkat adalah daftar
 * berkas PDF-nya - operator bisa mencetak ulang atau membagikannya tanpa
 * membuat surat dari awal.
 */
class RegisterSuratActivity : AppCompatActivity() {

    private lateinit var wadah: LinearLayout
    private lateinit var kosong: View
    private lateinit var jumlah: TextView
    private lateinit var progres: ProgressBar

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_register_surat)
        Ui.insetAtas(findViewById(R.id.kepala))
        Ui.insetBawah(findViewById(R.id.konten))

        wadah = findViewById(R.id.wadah_register)
        kosong = findViewById(R.id.kosong)
        jumlah = findViewById(R.id.jumlah_surat)
        progres = findViewById(R.id.progres)

        findViewById<View>(R.id.btn_segarkan).setOnClickListener { muat() }
        muat()
    }

    private fun muat() {
        progres.visibility = View.VISIBLE
        wadah.removeAllViews()

        val daftar = DaftarSurat.ambil(this)
        progres.visibility = View.GONE

        jumlah.text = if (daftar.isEmpty()) {
            getString(R.string.jumlah_surat_kosong)
        } else {
            getString(R.string.jumlah_surat, daftar.size)
        }
        kosong.visibility = if (daftar.isEmpty()) View.VISIBLE else View.GONE

        val format = SimpleDateFormat("dd/MM/yyyy HH:mm", Locale("id", "ID"))
        for (b in daftar) {
            val kartu = MaterialCardView(this).apply {
                radius = Ui.dp(this@RegisterSuratActivity, 16).toFloat()
                strokeWidth = Ui.dp(this@RegisterSuratActivity, 1)
                strokeColor = ContextCompat.getColor(this@RegisterSuratActivity, R.color.garis)
                setCardBackgroundColor(ContextCompat.getColor(this@RegisterSuratActivity, R.color.surface))
                cardElevation = 0f
                rippleColor = ColorStateList.valueOf(
                    ContextCompat.getColor(this@RegisterSuratActivity, R.color.accent_muda)
                )
                layoutParams = LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT,
                    ViewGroup.LayoutParams.WRAP_CONTENT,
                ).apply { bottomMargin = Ui.dp(this@RegisterSuratActivity, 10) }
            }

            val baris = LinearLayout(this).apply {
                orientation = LinearLayout.HORIZONTAL
                gravity = android.view.Gravity.CENTER_VERTICAL
                setPadding(
                    Ui.dp(this@RegisterSuratActivity, 14),
                    Ui.dp(this@RegisterSuratActivity, 13),
                    Ui.dp(this@RegisterSuratActivity, 14),
                    Ui.dp(this@RegisterSuratActivity, 13),
                )
            }

            val wadahIkon = FrameLayout(this).apply {
                layoutParams = LinearLayout.LayoutParams(
                    Ui.dp(this@RegisterSuratActivity, 40),
                    Ui.dp(this@RegisterSuratActivity, 40),
                )
                background = ContextCompat.getDrawable(
                    this@RegisterSuratActivity, R.drawable.bg_ikon
                )
                backgroundTintList = ColorStateList.valueOf(
                    ContextCompat.getColor(this@RegisterSuratActivity, R.color.accent_muda)
                )
            }
            wadahIkon.addView(ImageView(this).apply {
                layoutParams = FrameLayout.LayoutParams(
                    Ui.dp(this@RegisterSuratActivity, 20),
                    Ui.dp(this@RegisterSuratActivity, 20),
                    android.view.Gravity.CENTER,
                )
                setImageResource(R.drawable.ic_surat)
                imageTintList = ColorStateList.valueOf(
                    ContextCompat.getColor(this@RegisterSuratActivity, R.color.accent)
                )
                contentDescription = null
            })

            val teks = LinearLayout(this).apply {
                orientation = LinearLayout.VERTICAL
                layoutParams = LinearLayout.LayoutParams(
                    0,
                    ViewGroup.LayoutParams.WRAP_CONTENT,
                    1f,
                ).apply { marginStart = Ui.dp(this@RegisterSuratActivity, 12) }
            }

            teks.addView(TextView(this).apply {
                text = b.nama
                textSize = 14f
                setTypeface(typeface, android.graphics.Typeface.BOLD)
                setTextColor(ContextCompat.getColor(this@RegisterSuratActivity, R.color.teks))
                maxLines = 2
                ellipsize = TextUtils.TruncateAt.END
            })

            teks.addView(TextView(this).apply {
                text = "${b.jalur}  \u00B7  ${format.format(Date(b.diubah))}"
                textSize = 11f
                setTextColor(ContextCompat.getColor(this@RegisterSuratActivity, R.color.teks_redup))
                maxLines = 1
                ellipsize = TextUtils.TruncateAt.END
                setPadding(0, Ui.dp(this@RegisterSuratActivity, 3), 0, 0)
            })

            val panah = ImageView(this).apply {
                layoutParams = LinearLayout.LayoutParams(
                    Ui.dp(this@RegisterSuratActivity, 18),
                    Ui.dp(this@RegisterSuratActivity, 18),
                )
                setImageResource(R.drawable.ic_panah)
                imageTintList = ColorStateList.valueOf(
                    ContextCompat.getColor(this@RegisterSuratActivity, R.color.teks_redup)
                )
                contentDescription = null
            }

            baris.addView(wadahIkon)
            baris.addView(teks)
            baris.addView(panah)
            kartu.addView(baris)
            kartu.setOnClickListener { pilih(b) }
            wadah.addView(kartu)
        }
    }

    private fun pilih(b: BerkasSurat) {
        val item = arrayOf("Edit", "Cetak ulang", "Bagikan PDF")
        val pilih = AlertDialog.Builder(this)
            .setTitle(b.nama)
            .setItems(item) { _, which ->
                when (which) {
                    0 -> editSurat(b)
                    1 -> if (b.uri != null) PdfAksi.lihatUri(this, b.uri, b.nama)
                    else PdfAksi.lihat(this, b.berkas!!, b.nama)
                    2 -> PdfAksi.bagikan(this, b)
                }
            }
            .setNegativeButton("Tutup", null)
        pilih.show()
        // Edit tidak tersedia untuk surat lama tanpa tangkapan == kolom 0
        // tetap tampil agar operator tahu caranya buat ulang.
    }

    private fun editSurat(b: BerkasSurat) {
        val tangkapan = SimpananEdit.ambil(this, b.nama)
        if (tangkapan == null) {
            Toast.makeText(
                this,
                getString(R.string.edit_tidak_tersedia),
                Toast.LENGTH_LONG,
            ).show()
            return
        }
        val idJenis = tangkapan.optString("id_jenis")
        val fields = tangkapan.optJSONObject("fields")
        if (idJenis.isBlank() || fields == null) {
            Toast.makeText(this, getString(R.string.edit_tidak_tersedia), Toast.LENGTH_LONG).show()
            return
        }
        startActivity(
            Intent(this, BuatSuratActivity::class.java).apply {
                putExtra(BuatSuratActivity.EXTRA_GROUPS, BuatSuratActivity.GROUP_SURAT)
                putExtra(BuatSuratActivity.EXTRA_EDIT_JENIS, idJenis)
                putExtra(BuatSuratActivity.EXTRA_EDIT_FIELDS, fields.toString())
                putExtra(BuatSuratActivity.EXTRA_EDIT_NAMA, b.nama)
            },
        )
    }
}
