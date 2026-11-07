package dev.sumberjaya.sudes.android

import android.os.Bundle
import android.view.View
import android.widget.Button
import android.widget.LinearLayout
import android.widget.ProgressBar
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AlertDialog
import androidx.appcompat.app.AppCompatActivity
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/**
 * Register Surat: daftar PDF yang pernah dibuat di perangkat ini.
 *
 * Surat tidak dikirim ke mana pun, jadi "register" di perangkat adalah daftar
 * berkas PDF-nya — operator bisa mencetak ulang atau membagikannya tanpa
 * membuat surat dari awal.
 */
class RegisterSuratActivity : AppCompatActivity() {

    private lateinit var wadah: LinearLayout
    private lateinit var progres: ProgressBar

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_register_surat)

        wadah = findViewById(R.id.wadah_register)
        progres = findViewById(R.id.progres)

        findViewById<Button>(R.id.btn_segarkan).setOnClickListener { muat() }
        muat()
    }

    private fun muat() {
        progres.visibility = View.VISIBLE
        wadah.removeAllViews()

        val daftar = DaftarSurat.ambil(this)
        progres.visibility = View.GONE

        if (daftar.isEmpty()) {
            wadah.addView(TextView(this).apply {
                text = getString(R.string.register_kosong)
                setPadding(0, 24, 0, 0)
            })
            return
        }

        val format = SimpleDateFormat("d MMM yyyy HH:mm", Locale("id", "ID"))
        for (b in daftar) {
            val kartu = LinearLayout(this).apply {
                orientation = LinearLayout.VERTICAL
                setPadding(0, 14, 0, 14)
            }

            kartu.addView(TextView(this).apply {
                text = b.nama
                textSize = 15f
                setTypeface(typeface, android.graphics.Typeface.BOLD)
            })

            kartu.addView(TextView(this).apply {
                text = "${b.jalur}  •  ${format.format(Date(b.diubah))}"
                textSize = 12f
            })

            kartu.addView(View(this).apply {
                layoutParams = LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, 1)
                setBackgroundColor(0xFFDDDDDD.toInt())
            })

            kartu.setOnClickListener { pilih(b) }
            wadah.addView(kartu)
        }
    }

    private fun pilih(b: BerkasSurat) {
        AlertDialog.Builder(this)
            .setTitle(b.nama)
            .setItems(arrayOf("Cetak ulang", "Bagikan PDF")) { _, which ->
                when (which) {
                    0 -> if (b.uri != null) PdfAksi.cetakUri(this, b.uri, b.nama)
                    else PdfAksi.cetak(this, b.berkas!!, b.nama)
                    1 -> PdfAksi.bagikan(this, b)
                }
            }
            .setNegativeButton("Tutup", null)
            .show()
    }
}
