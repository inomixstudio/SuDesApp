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
 * Layar ini TIDAK membuka kamera sendiri: operator memilih memindai QR atau
 * mengetik kode 4 angka. QR memuat URL proyek Supabase serta kode tersebut.
 * Setelah tersambung, pengaturan desa langsung diambil dari desktop sebelum
 * menu dibuka.
 */
class LoginActivity : AppCompatActivity() {

    private lateinit var status: TextView
    private lateinit var kode: EditText
    private lateinit var tombolKode: Button
    private var sedangMenghubungkan = false

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
        Ui.insetAtas(findViewById(R.id.kepala))
        Ui.insetBawah(findViewById(R.id.konten))
        Ui.statusBarIkutGulir(this, findViewById(R.id.gulir), findViewById(R.id.kepala))
        status = findViewById(R.id.status_text)
        kode = findViewById(R.id.kode_manual)
        tombolKode = findViewById(R.id.btn_kode)

        findViewById<Button>(R.id.btn_pindai).setOnClickListener { mulaiPindai() }
        tombolKode.setOnClickListener { hubungkanKodeManual() }
    }

    private fun mulaiPindai() {
        pindai.launch(
            ScanOptions()
                .setDesiredBarcodeFormats(ScanOptions.QR_CODE)
                .setPrompt("Arahkan ke kode QR di aplikasi desktop")
                .setBeepEnabled(false)
                .setOrientationLocked(true)
        )
    }

    /** Kode yang diketik manual: hanya 4 angka, sama seperti isi QR. */
    private fun hubungkanKodeManual() {
        val teks = kode.text.toString().trim()
        if (!Regex("^[0-9]{4}$").matches(teks)) {
            status.text = getString(R.string.status_kode_tidak_valid)
            return
        }
        hubungkan(teks, null)
    }

    /** Isi QR: URL proyek Supabase (opsional) di baris pertama, kode 4 angka di baris kedua. */
    private fun dariQr(payload: String) {
        val baris = payload.lines().map { it.trim() }.filter { it.isNotEmpty() }
        val isiKode = baris.lastOrNull { it.length == 4 && it.all { c -> c.isDigit() } }
        if (isiKode == null) {
            status.text = getString(R.string.status_kode_tidak_terbaca)
            return
        }

        // URL dari QR diterima HANYA bila skema https dan host berakhir
        // .supabase.co (atau sama dengan nilai bawaan APK). Nilai lain
        // (http://, IP, domain asing) ditolak demi mencegah phishing kredensial.
        val bawaan = dev.sumberjaya.sudes.android.BuildConfig.SUPABASE_URL.trimEnd('/')
        val urlMentah = baris.firstOrNull {
            it.startsWith("https://", ignoreCase = true)
        }?.trimEnd('/')
        val url = urlMentah?.takeIf { u ->
            u == bawaan || u.endsWith(".supabase.co")
        }
        if (urlMentah != null && url == null) {
            status.text = getString(R.string.status_kode_tidak_valid)
            return
        }
        hubungkan(isiKode, url)
    }

    private fun hubungkan(kodeMasuk: String, url: String?) {
        if (sedangMenghubungkan) return
        sedangMenghubungkan = true
        tombolKode.isEnabled = false
        status.text = getString(R.string.status_menghubungkan)

        lifecycleScope.launch {
            val dasar = SupabaseSesi.ambil(this@LoginActivity).let {
                if (url.isNullOrBlank()) it else it.copy(url = url)
            }

            val hasil = try {
                SupabaseKlien(dasar).pakaiKode(kodeMasuk)
            } catch (e: Exception) {
                SupabaseKlien.HasilKode.Galat(
                    "Tidak bisa menghubungi server: ${e.message ?: "jaringan"}"
                )
            }

            when (hasil) {
                is SupabaseKlien.HasilKode.Galat -> {
                    status.text = hasil.pesan
                    sedangMenghubungkan = false
                    tombolKode.isEnabled = true
                }

                is SupabaseKlien.HasilKode.Sukses -> {
                    SupabaseSesi.simpan(this@LoginActivity, hasil.sesi)

                    // Pengaturan desa diambil SEKARANG, sebelum menu dibuka:
                    // perangkat yang baru dipasang langsung memakai kop dan pola
                    // nomor dari desktop, bukan data contoh bawaan APK.
                    status.text = getString(R.string.status_mengambil_pengaturan)
                    try {
                        PengaturanSinkron.tarik(this@LoginActivity)
                    } catch (e: Exception) {
                        // Gagal menarik tidak membatalkan pemasangan; dicoba lagi
                        // saat menu dibuka.
                    }

                    // Kabari desktop bahwa perangkat sudah tersambung supaya
                    // status bar di sana menampilkan "Android terhubung".
                    try {
                        SupabaseKlien(SupabaseSesi.ambil(this@LoginActivity)).tandaiOnline()
                    } catch (e: Exception) {
                        // Laporan status boleh gagal; bukan penghalang pemakaian.
                    }

                    bukaMenu()
                }
            }
        }
    }

    private fun bukaMenu() {
        startActivity(Intent(this, MainActivity::class.java))
        finish()
    }
}
