package dev.sumberjaya.sudes.android

import android.content.Intent
import android.os.Bundle
import android.widget.Button
import android.widget.TextView
import androidx.appcompat.app.AppCompatActivity

/**
 * Menu utama.
 *
 * Dua jalur pembuatan surat dipisah sesuai pembagian di aplikasi desktop:
 * surat biasa di "Buat Surat", persyaratan pernikahan (N1–N6, N8) di
 * "Buat NTCR". Aplikasi tidak punia masuk akun — tidak ada data yang dikirim
 * ke mana pun, jadi perangkat bisa dipakai langsung di lapangan.
 */
class MainActivity : AppCompatActivity() {

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)

        findViewById<Button>(R.id.btn_buat_surat).setOnClickListener {
            startActivity(
                Intent(this, BuatSuratActivity::class.java)
                    .putExtra(BuatSuratActivity.EXTRA_GROUPS, BuatSuratActivity.GROUP_SURAT)
            )
        }

        findViewById<Button>(R.id.btn_buat_ntcr).setOnClickListener {
            startActivity(
                Intent(this, BuatSuratActivity::class.java)
                    .putExtra(BuatSuratActivity.EXTRA_GROUPS, BuatSuratActivity.GROUP_NTCR)
            )
        }

        findViewById<Button>(R.id.btn_register_surat).setOnClickListener {
            startActivity(Intent(this, RegisterSuratActivity::class.java))
        }

        findViewById<Button>(R.id.btn_daftar_hadir).setOnClickListener {
            startActivity(Intent(this, DaftarHadirActivity::class.java))
        }

        findViewById<Button>(R.id.btn_pengaturan).setOnClickListener {
            startActivity(Intent(this, PengaturanActivity::class.java))
        }

        tampilkanIdentitas()
    }

    override fun onResume() {
        super.onResume()
        tampilkanIdentitas()
    }

    private fun tampilkanIdentitas() {
        val p = PengaturanSurat.ambil(this)
        val judul = findViewById<TextView>(R.id.nama_desa)
        judul.text = listOf(p.namaDesa, p.kecamatan, p.kabupaten)
            .map { it.trim() }
            .filter { it.isNotEmpty() }
            .joinToString(" • ")
            .ifBlank { getString(R.string.catatan_privasi) }
    }
}
