package dev.sumberjaya.sudes.android

import android.content.Intent
import android.os.Bundle
import android.view.View
import android.widget.Button
import android.widget.EditText
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import com.journeyapps.barcodescanner.ScanContract
import com.journeyapps.barcodescanner.ScanOptions
import kotlinx.coroutines.launch

/**
 * Pasang perangkat: pindai QR dari halaman Sinkron Android aplikasi desktop,
 * atau ketik manual URL/kunci/surel/kata sandi.
 *
 * QR berisi URL Supabase, kunci publik, surel, dan kata sandi perangkat —
 * disusun desktop saat perangkat dipasang.
 */
class LoginActivity : AppCompatActivity() {

    private lateinit var inputUrl: EditText
    private lateinit var inputKunci: EditText
    private lateinit var inputEmail: EditText
    private lateinit var inputSandi: EditText
    private lateinit var status: TextView

    private val pindai = registerForActivityResult(ScanContract()) { hasil ->
        val teks = hasil.contents ?: return@registerForActivityResult
        masukDariQr(teks)
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        // Sudah terhubung? Langsung ke menu.
        if (SupabaseSesi.ambil(this).masuk) {
            bukaMenu()
            return
        }

        setContentView(R.layout.activity_login)
        inputUrl = findViewById(R.id.input_url)
        inputKunci = findViewById(R.id.input_kunci)
        inputEmail = findViewById(R.id.input_email)
        inputSandi = findViewById(R.id.input_sandi)
        status = findViewById(R.id.status_text)

        findViewById<Button>(R.id.btn_pindai).setOnClickListener { mulaiPindai() }
        findViewById<Button>(R.id.btn_masuk).setOnClickListener { masukManual() }
    }

    private fun mulaiPindai() {
        pindai.launch(
            ScanOptions()
                .setDesiredBarcodeFormats(ScanOptions.QR_CODE)
                .setPrompt("Arahkan ke QR perangkat di aplikasi desktop")
                .setBeepEnabled(false)
                .setOrientationLocked(false)
        )
    }

    private fun masukDariQr(payload: String) {
        status.text = "Memasang perangkat…"
        lifecycleScope.launch {
            val sesi = SupabaseSesi.dariQr(this@LoginActivity, payload)
            if (sesi == null || !sesi.masuk) {
                status.text = "QR tidak terbaca atau login gagal. Periksa QR dan coba lagi."
                return@launch
            }

            SupabaseSesi.simpan(this@LoginActivity, sesi)
            bukaMenu()
        }
    }

    private fun masukManual() {
        val url = inputUrl.text.toString().trim().trimEnd('/')
        val kunci = inputKunci.text.toString().trim()
        val email = inputEmail.text.toString().trim()
        val sandi = inputSandi.text.toString()

        if (url.isEmpty() || kunci.isEmpty() || email.isEmpty() || sandi.isEmpty()) {
            status.text = "Semua kolom wajib diisi."
            return
        }

        status.text = "Memasang perangkat…"
        findViewById<Button>(R.id.btn_masuk).isEnabled = false

        lifecycleScope.launch {
            val saran = SupabaseSesi.ambil(this@LoginActivity).copy(
                url = url,
                kunciPublik = kunci,
                email = email,
                kataSandi = sandi,
            )
            val sesi = SupabaseKlien(saran).login(email, sandi)

            if (sesi == null || !sesi.masuk) {
                status.text = "Login gagal. Periksa surel dan kata sandi perangkat."
                findViewById<Button>(R.id.btn_masuk).isEnabled = true
                return@launch
            }

            SupabaseSesi.simpan(this@LoginActivity, sesi)
            bukaMenu()
        }
    }

    private fun bukaMenu() {
        startActivity(Intent(this, MainActivity::class.java))
        finish()
    }
}
