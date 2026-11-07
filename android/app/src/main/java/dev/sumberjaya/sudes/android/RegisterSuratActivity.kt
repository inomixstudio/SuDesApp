package dev.sumberjaya.sudes.android

import android.content.Intent
import android.os.Bundle
import android.view.View
import android.widget.Button
import android.widget.LinearLayout
import android.widget.ProgressBar
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import kotlinx.coroutines.launch

/**
 * Register Surat: daftar surat yang dibuat di perangkat ini dan masih menunggu
 * aplikasi desktop mengimpornya. Ini menu yang paling penting — operator desa
 * melihat apa yang sudah dibuat dan status sinkronnya tanpa membuka desktop.
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
        val sesi = SupabaseSesi.ambil(this)
        if (!sesi.masuk) {
            Toast.makeText(this, "Belum terhubung. Pasang perangkat dulu.", Toast.LENGTH_LONG).show()
            finish()
            return
        }

        progres.visibility = View.VISIBLE
        wadah.removeAllViews()

        lifecycleScope.launch {
            val daftar = SupabaseSesi.klien(this@RegisterSuratActivity).daftarAntrean()
            progres.visibility = View.GONE

            if (daftar.isEmpty()) {
                val kosong = TextView(this@RegisterSuratActivity).apply {
                    text = "Belum ada surat di antrean perangkat ini."
                    setPadding(0, 24, 0, 0)
                }
                wadah.addView(kosong)
                return@launch
            }

            for (baris in daftar) {
                wadah.addView(baris(baris))
            }
        }
    }

    private fun baris(b: AntreanBaris): View {
        val kartu = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(0, 12, 0, 12)
        }

        kartu.addView(TextView(this).apply {
            text = "${b.idJenis}  ·  ${b.statusLabel}"
            textSize = 15f
            setTypeface(typeface, android.graphics.Typeface.BOLD)
        })

        kartu.addView(TextView(this).apply {
            text = "Nomor: ${b.nomorSementara.ifBlank { "-" }}"
            textSize = 14f
        })

        if (b.nik.isNotBlank()) {
            kartu.addView(TextView(this).apply {
                text = "NIK: ${b.nik}"
                textSize = 13f
            })
        }

        kartu.addView(TextView(this).apply {
            text = b.dibuatPada.replace('T', ' ').take(16) +
                if (b.catatan.isNotBlank()) "  ·  ${b.catatan}" else ""
            textSize = 12f
        })

        val garis = View(this).apply {
            layoutParams = LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT, 1
            )
            setBackgroundColor(0xFFDDDDDD.toInt())
        }
        kartu.addView(garis)

        return kartu
    }
}
