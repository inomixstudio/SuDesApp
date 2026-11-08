package dev.sumberjaya.sudes.android

import android.content.Intent
import android.os.Bundle
import android.view.View
import android.view.ViewGroup
import android.widget.Button
import android.widget.LinearLayout
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AlertDialog
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import androidx.lifecycle.lifecycleScope
import kotlinx.coroutines.launch
import java.util.Locale

/**
 * Pengaturan kop surat - BUKAN tempat mengetik.
 *
 * Identitas desa (nama, kecamatan, kabupaten, alamat, pejabat, pola nomor)
 * diambil dari aplikasi desktop dan disinkronkan otomatis. Halaman ini hanya
 * menampilkan salinan yang sedang dipakai perangkat, plus tombol menarik ulang.
 *
 * Bagian bawah halaman menunjukkan keadaan sinkronisasi: perangkat terpasang
 * atau belum, berapa surat menunggu di antrean Supabase, berapa yang masih
 * tertunda di perangkat, serta tombol mengirim ulang dan keluar perangkat.
 */
class PengaturanActivity : AppCompatActivity() {

    private lateinit var wadah: LinearLayout
    private lateinit var wadahSinkron: LinearLayout
    private lateinit var status: TextView
    private lateinit var btnKirimTertunda: Button

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_pengaturan)
        Ui.insetAtas(findViewById(R.id.kepala))
        Ui.insetBawah(findViewById(R.id.konten))

        wadah = findViewById(R.id.wadah_pengaturan)
        wadahSinkron = findViewById(R.id.wadah_sinkron)
        status = findViewById(R.id.status_pengaturan)
        btnKirimTertunda = findViewById(R.id.btn_kirim_tertunda)

        findViewById<Button>(R.id.btn_sinkronkan).setOnClickListener { sinkronkan() }
        btnKirimTertunda.setOnClickListener { kirimTertunda() }
        findViewById<Button>(R.id.btn_keluar_perangkat).setOnClickListener { konfirmasiKeluar() }

        tampilkan()
        tampilkanSinkron()
    }

    override fun onResume() {
        super.onResume()
        // Jumlah surat tertunda bisa berubah setelah membuat surat di layar lain.
        tampilkanSinkron()
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
            status.text = when (hasil) {
                is PengaturanSinkron.HasilTarik.Sukses ->
                    getString(R.string.pengaturan_sinkron_berhasil)
                PengaturanSinkron.HasilTarik.BelumTerbit ->
                    getString(R.string.pengaturan_belum_terbit)
                PengaturanSinkron.HasilTarik.SesiKedaluwarsa ->
                    getString(R.string.sesi_tidak_berlaku)
                else -> getString(R.string.pengaturan_sinkron_gagal)
            }
            Toast.makeText(
                this@PengaturanActivity,
                status.text,
                Toast.LENGTH_SHORT,
            ).show()
            tampilkanSinkron()
        }
    }

    /** Kirim ulang surat yang tertahan di perangkat karena jaringan/perangkat. */
    private fun kirimTertunda() {
        status.text = getString(R.string.kirim_tertunda_proses)
        lifecycleScope.launch {
            val terkirim = try {
                AntreanSurat.kirimTertunda(this@PengaturanActivity)
            } catch (e: Exception) {
                0
            }

            val sisa = AntreanSurat.jumlahTertunda(this@PengaturanActivity)
            status.text = when {
                terkirim > 0 -> getString(R.string.antrean_tertunda_terkirim, terkirim)
                sisa > 0 -> getString(R.string.kirim_tertunda_gagal)
                else -> getString(R.string.kirim_tertunda_kosong)
            }

            Toast.makeText(this@PengaturanActivity, status.text, Toast.LENGTH_SHORT).show()
            tampilkanSinkron()
        }
    }

    private fun konfirmasiKeluar() {
        if (!SupabaseSesi.ambil(this).masuk) {
            Toast.makeText(
                this,
                getString(R.string.pengaturan_belum_terpasang),
                Toast.LENGTH_SHORT,
            ).show()
            return
        }

        AlertDialog.Builder(this)
            .setTitle(R.string.keluar_judul)
            .setMessage(R.string.keluar_pesan)
            .setNegativeButton(android.R.string.cancel, null)
            .setPositiveButton(R.string.btn_keluar_perangkat) { _, _ -> keluar() }
            .show()
    }

    private fun keluar() {
        lifecycleScope.launch {
            try {
                SupabaseSesi.klien(this@PengaturanActivity).logoutServer()
            } catch (e: Exception) {
                // Sesi lokal tetap dibersihkan walau server tidak terjangkau.
            }
            SupabaseSesi.keluar(this@PengaturanActivity)
            Toast.makeText(this@PengaturanActivity, R.string.keluar_berhasil, Toast.LENGTH_LONG).show()
            startActivity(Intent(this@PengaturanActivity, LoginActivity::class.java))
            finish()
        }
    }

    private fun tampilkan() {
        val p = PengaturanSurat.ambil(this)
        val baris = listOf(
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
        )

        wadah.removeAllViews()
        baris.forEach { (label, nilai) ->
            tambahBaris(wadah, label).text = nilai.ifBlank { "-" }
        }
    }

    /**
     * Status sinkronisasi. Jumlah antrean di Supabase butuh jaringan, jadi
     * ditampilkan "memuat" dulu lalu diperbarui; kegagalan jaringan
     * ditampilkan apa adanya, bukan dianggap nol.
     */
    private fun tampilkanSinkron() {
        val sesi = SupabaseSesi.ambil(this)
        val tertunda = AntreanSurat.jumlahTertunda(this)

        wadahSinkron.removeAllViews()
        tambahBaris(wadahSinkron, getString(R.string.sinkron_status_perangkat)).text =
            getString(if (sesi.masuk) R.string.sinkron_terpasang else R.string.sinkron_belum_terpasang)

        val nilaiAntrean = tambahBaris(wadahSinkron, getString(R.string.sinkron_antrean_supabase))
        nilaiAntrean.text = if (sesi.masuk) {
            getString(R.string.sinkron_memuat)
        } else {
            getString(R.string.sinkron_tidak_terjangkau)
        }

        tambahBaris(wadahSinkron, getString(R.string.sinkron_tertunda_perangkat)).text =
            getString(R.string.sinkron_tertunda_nilai, tertunda)

        btnKirimTertunda.isEnabled = sesi.masuk && tertunda > 0

        if (!sesi.masuk) return

        lifecycleScope.launch {
            val teks = try {
                val daftar = SupabaseSesi.klien(this@PengaturanActivity).daftarAntrean()
                if (daftar == null) {
                    getString(R.string.sinkron_tidak_terjangkau)
                } else {
                    getString(
                        R.string.sinkron_antrean_nilai,
                        daftar.count { it.status.equals("menunggu", ignoreCase = true) },
                        daftar.count { it.status.equals("gagal", ignoreCase = true) },
                    )
                }
            } catch (e: SupabaseKlien.SesiKedaluwarsa) {
                getString(R.string.sesi_tidak_berlaku_singkat)
            } catch (e: Exception) {
                getString(R.string.sinkron_tidak_terjangkau)
            }

            if (isFinishing || isDestroyed) return@launch
            nilaiAntrean.text = teks
        }
    }

    /** Satu baris kartu (label redup + nilai). Mengembalikan TextView nilainya. */
    private fun tambahBaris(wadah: LinearLayout, label: String): TextView {
        if (wadah.childCount > 0) {
            wadah.addView(View(this).apply {
                layoutParams = LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT,
                    Ui.dp(this@PengaturanActivity, 1),
                )
                setBackgroundColor(ContextCompat.getColor(this@PengaturanActivity, R.color.garis))
            })
        }

        val barisView = layoutInflater.inflate(R.layout.baris_pengaturan, wadah, false)
        barisView.findViewById<TextView>(R.id.info_label).text =
            label.uppercase(Locale("id", "ID"))
        val nilai = barisView.findViewById<TextView>(R.id.info_nilai)
        wadah.addView(barisView)
        return nilai
    }
}
