package dev.sumberjaya.sudes.android

import android.os.Bundle
import android.widget.Button
import android.widget.EditText
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity

/**
 * Pengaturan kop surat dan penomoran.
 *
 * Identitas desa dipakai sebagai kop surat, dan nomor urut dilanjutkan dari
 * angka terakhir di register desktop supaya nomor surat tidak lompat. Semua
 * nilai hanya disimpan di perangkat ini.
 */
class PengaturanActivity : AppCompatActivity() {

    private lateinit var desa: EditText
    private lateinit var kecamatan: EditText
    private lateinit var kabupaten: EditText
    private lateinit var awalan: EditText
    private lateinit var nomor: EditText

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_pengaturan)

        desa = findViewById(R.id.input_desa)
        kecamatan = findViewById(R.id.input_kecamatan)
        kabupaten = findViewById(R.id.input_kabupaten)
        awalan = findViewById(R.id.input_awalan)
        nomor = findViewById(R.id.input_nomor)

        val p = PengaturanSurat.ambil(this)
        desa.setText(p.namaDesa)
        kecamatan.setText(p.kecamatan)
        kabupaten.setText(p.kabupaten)
        awalan.setText(p.awalanNomor)
        nomor.setText(if (p.nomorTerakhir > 0) p.nomorTerakhir.toString() else "")

        findViewById<Button>(R.id.btn_simpan).setOnClickListener { simpan() }
    }

    private fun simpan() {
        val urut = nomor.text.toString().trim().toIntOrNull() ?: 0
        PengaturanSurat.simpan(
            this,
            PengaturanSurat(
                namaDesa = desa.text.toString().trim(),
                kecamatan = kecamatan.text.toString().trim(),
                kabupaten = kabupaten.text.toString().trim(),
                awalanNomor = awalan.text.toString().trim().ifBlank { "470" },
                nomorTerakhir = urut,
            ),
        )
        Toast.makeText(this, R.string.pengaturan_tersimpan, Toast.LENGTH_SHORT).show()
        finish()
    }
}
