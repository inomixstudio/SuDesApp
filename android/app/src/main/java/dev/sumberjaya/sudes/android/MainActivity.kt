package dev.sumberjaya.sudes.android

import android.content.Intent
import android.os.Bundle
import android.widget.Button
import android.widget.TextView
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import kotlinx.coroutines.launch

/**
 * Menu utama setelah perangkat terpasang. Tiga menu sesuai kesepakatan:
 * Buat Surat Baru, Register Surat, dan Daftar Hadir.
 */
class MainActivity : AppCompatActivity() {

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        if (!SupabaseSesi.ambil(this).masuk) {
            startActivity(Intent(this, LoginActivity::class.java))
            finish()
            return
        }

        setContentView(R.layout.activity_main)

        val status = findViewById<TextView>(R.id.status_text)
        findViewById<Button>(R.id.btn_buat_surat).setOnClickListener {
            startActivity(Intent(this, BuatSuratActivity::class.java))
        }
        findViewById<Button>(R.id.btn_register_surat).setOnClickListener {
            startActivity(Intent(this, RegisterSuratActivity::class.java))
        }
        findViewById<Button>(R.id.btn_daftar_hadir).setOnClickListener {
            startActivity(Intent(this, DaftarHadirActivity::class.java))
        }
        findViewById<Button>(R.id.btn_keluar).setOnClickListener {
            SupabaseSesi.keluar(this)
            startActivity(Intent(this, LoginActivity::class.java))
            finish()
        }

        segarkanStatus(status)
    }

    override fun onResume() {
        super.onResume()
        val status = findViewById<TextView>(R.id.status_text) ?: return
        segarkanStatus(status)
    }

    private fun segarkanStatus(status: TextView) {
        val sesi = SupabaseSesi.ambil(this)
        status.text = getString(R.string.status_belum_terhubung)

        lifecycleScope.launch {
            val jumlah = SupabaseSesi.klien(this@MainActivity).hitungAntrean()
            status.text = getString(
                R.string.status_terhubung,
                sesi.email,
                jumlah?.toString() ?: "?",
            )
        }
    }
}
