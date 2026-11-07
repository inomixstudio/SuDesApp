package dev.sumberjaya.sudes.android

import android.content.Intent
import android.os.Bundle
import android.widget.Button
import android.widget.EditText
import android.widget.TextView
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import com.journeyapps.barcodescanner.ScanContract
import com.journeyapps.barcodescanner.ScanOptions
import kotlinx.coroutines.launch

/**
 * Layar sambung perangkat — satu-satunya yang membuka aplikasi.
 *
 * Operator cukup memindai QR dari halaman Sinkron Android di desktop, atau
 * mengetik kode verifikasi 4 angka yang tampil di sana. URL, kunci publik,
 * surel, dan kata sandi perangkat tidak pernah diketik di sini.
 */
class LoginActivity : AppCompatActivity() {

    private lateinit var inputKode: EditText
    private lateinit var status: TextView

    private val pindai = registerForActivityResult(ScanContract()) { hasil ->
        val teks = hasil.contents ?: return@registerForActivityResult
        dariQr(teks)
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        // Sudah tersambung? Langsung ke menu.
        if (SupabaseSesi.ambil(this).masuk) {
            bukaMenu()
            return
        }

        setContentView(R.layout.activity_login)
        inputKode = findViewById(R.id.input_kode)
        status = findViewById(R.id.status_text)

        findViewById<Button>(R.id.btn_pindai).setOnClickListener { mulaiPindai() }
        findViewById<Button>(R.id.btn_hubungkan).setOnClickListener { hubungkan() }
    }

    private fun mulaiPindai() {
        pindai.launch(
            ScanOptions()
                .setDesiredBarcodeFormats(ScanOptions.QR_CODE)
                .setPrompt("Arahkan ke kode QR di aplikasi desktop")
                .setBeepEnabled(false)
                .setOrientationLocked(false)
        )
    }

    /** Isi QR: URL Supabase di baris pertama, kode 4 angka di baris kedua. */
    private fun dariQr(payload: String) {
        val baris = payload.lines().map { it.trim() }.filter { it.isNotEmpty() }
        val kode = baris.lastOrNull { it.length == 4 && it.all { c -> c.isDigit() } }
        if (kode == null) {
            status.text = "Kode QR tidak terbaca. Coba pindai ulang, atau ketik kode 4 angka."
            return
        }
        hubungkan(kode)
    }

    private fun hubungkan(otomatis: String? = null) {
        val kode = (otomatis ?: inputKode.text.toString()).trim()
        if (kode.length != 4 || !kode.all { it.isDigit() }) {
            status.text = "Kode harus 4 angka. Lihat kode di aplikasi desktop."
            return
        }

        status.text = "Menghubungkan…"
        findViewById<Button>(R.id.btn_hubungkan).isEnabled = false
        lifecycleScope.launch {
            val sesi = try {
                SupabaseSesi.klien(this@LoginActivity).pakaiKode(kode)
            } catch (e: Exception) {
                null
            }

            if (sesi == null || !sesi.masuk) {
                status.text = "Kode tidak dikenal atau sudah kedaluwarsa. Minta kode baru di aplikasi desktop."
                findViewById<Button>(R.id.btn_hubungkan).isEnabled = true
                return@launch
            }

            SupabaseSesi.simpan(this@LoginActivity, sesi)
            tarikPengaturan()
            bukaMenu()
        }
    }

    /** Tarik kop surat dan pola nomor dari desktop setelah perangkat dipasang. */
    private fun tarikPengaturan() {
        lifecycleScope.launch {
            try {
                PengaturanSinkron.tarik(this@LoginActivity)
            } catch (e: Exception) {
                // Gagal menarik tidak menghalangi pemasangan perangkat.
            }
        }
    }

    private fun bukaMenu() {
        startActivity(Intent(this, MainActivity::class.java))
        finish()
    }
}
