package dev.sumberjaya.sudes.android

import android.os.Bundle
import android.widget.Button
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import kotlinx.coroutines.launch

/**
 * Pengaturan kop surat — BUKAN tempat mengetik.
 *
 * Identitas desa (nama, kecamatan, kabupaten, alamat, pejabat, pola nomor)
 * diambil dari aplikasi desktop dan disinkronkan otomatis. Halaman ini hanya
 * menampilkan salinan yang sedang dipakai perangkat, plus tombol menarik ulang.
 */
class PengaturanActivity : AppCompatActivity() {

    private lateinit var isi: TextView
    private lateinit var status: TextView

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_pengaturan)

        isi = findViewById(R.id.isi_pengaturan)
        status = findViewById(R.id.status_pengaturan)

        findViewById<Button>(R.id.btn_sinkronkan).setOnClickListener { sinkronkan() }

        tampilkan()
    }

    private fun sinkronkan() {
        status.text = getString(R.string.pengaturan_sinkron_proses)
        lifecycleScope.launch {
            val hasil = try {
                PengaturanSinkron.tarik(this@PengaturanActivity)
            } catch (e: Exception) {
                null
            }
            tampilkan()
            status.text = when {
                hasil == null -> getString(R.string.pengaturan_sinkron_gagal)
                !SupabaseSesi.ambil(this@PengaturanActivity).masuk ->
                    getString(R.string.pengaturan_belum_terpasang)
                else -> getString(R.string.pengaturan_sinkron_berhasil)
            }
            Toast.makeText(
                this@PengaturanActivity,
                status.text,
                Toast.LENGTH_SHORT,
            ).show()
        }
    }

    private fun tampilkan() {
        val p = PengaturanSurat.ambil(this)
        isi.text = listOf(
            "Nama desa" to p.namaDesa,
            "Kecamatan" to p.kecamatan,
            "Kabupaten" to p.kabupaten,
            "Jenis kabupaten" to p.jenisKabupaten,
            "Jenis lembaga" to p.jenisLembaga,
            "Alamat" to p.alamat,
            "Kodepos" to p.kodepos,
            "Surel" to p.email,
            "Kepala Desa" to p.kepalaDesa,
            "Sekretaris Desa" to p.sekretarisDesa,
            "NIP" to p.nipKepalaDesa,
            "Ukuran kertas" to p.ukuranKertas,
            "Pola nomor" to if (p.formatNomor.isEmpty()) "(bawaan)" else "${p.formatNomor.size} jenis",
            "Nomor urut terakhir" to if (p.nomorTerakhir > 0) p.nomorTerakhir.toString() else "-",
        ).joinToString("\n") { (label, nilai) ->
            "• $label: ${nilai.ifBlank { "-" }}"
        }
    }
}
