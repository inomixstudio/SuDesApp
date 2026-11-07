package dev.sumberjaya.sudes.android

import android.content.Intent
import android.os.Bundle
import android.widget.Button
import android.widget.TextView
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import com.journeyapps.barcodescanner.ScanContract
import com.journeyapps.barcodescanner.ScanOptions
import kotlinx.coroutines.launch

/**
 * Layar sambung perangkat — satu-satunya yang membuka aplikasi.
 *
 * Cukup memindai kode QR dari halaman Sinkron Android di desktop. Kamera
 * langsung terbuka saat layar ini muncul, jadi tidak ada kolom yang perlu
 * diketik: URL, kunci, surel, dan kata sandi perangkat semuanya dari QR.
 */
class LoginActivity : AppCompatActivity() {

    private lateinit var status: TextView
    private var sudahMencoba = false

    private val pindai = registerForActivityResult(ScanContract()) { hasil ->
        val teks = hasil.contents
        if (teks.isNullOrBlank()) {
            // Operator menutup pemindai — beri jalan keluar, jangan macet.
            status.text = getString(R.string.status_batal_pindai)
            return@registerForActivityResult
        }
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
        status = findViewById(R.id.status_text)
        findViewById<Button>(R.id.btn_pindai).setOnClickListener { mulaiPindai() }
    }

    override fun onResume() {
        super.onResume()
        // Pindai otomatis saat layar muncul, tetapi hanya sekali: setelah
        // operator menutup pemindai tanpa memindai, tombol tersedia.
        if (!sudahMencoba && !SupabaseSesi.ambil(this).masuk) {
            sudahMencoba = true
            mulaiPindai()
        }
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
            status.text = "Kode QR tidak terbaca. Coba pindai ulang."
            return
        }
        hubungkan(kode)
    }

    private fun hubungkan(kode: String) {
        status.text = getString(R.string.status_menghubungkan)
        lifecycleScope.launch {
            val sesi = try {
                SupabaseSesi.klien(this@LoginActivity).pakaiKode(kode)
            } catch (e: Exception) {
                null
            }

            if (sesi == null || !sesi.masuk) {
                status.text = "Sambungan gagal. Periksa kode QR, lalu pindai ulang."
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
