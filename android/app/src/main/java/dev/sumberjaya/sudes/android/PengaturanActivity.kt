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
        periksaVersi()
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
                PengaturanSinkron.HasilTarik.SesiKedaluwarsa -> {
                    // Sama seperti menu utama: bersihkan sesi dan minta
                    // pasang ulang, jangan biarkan layar terjebak.
                    SupabaseSesi.keluar(this@PengaturanActivity)
                    val pesan = getString(R.string.sesi_tidak_berlaku)
                    Toast.makeText(this@PengaturanActivity, pesan, Toast.LENGTH_LONG).show()
                    startActivity(Intent(this@PengaturanActivity, LoginActivity::class.java))
                    finish()
                    return@launch
                }
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
            val gagal = AntreanSurat.jumlahGagal(this@PengaturanActivity)
            status.text = when {
                terkirim > 0 -> getString(R.string.antrean_tertunda_terkirim, terkirim)
                sisa > 0 -> getString(R.string.kirim_tertunda_gagal)
                gagal > 0 -> "Ada $gagal berkas tertunda gagal (rusak) — hubungi desa, jangan hapus manual di aplikasi ini."
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

    private fun periksaVersi() {
        val vDesktop = PengaturanSinkron.versiDesktop(this)
        if (vDesktop.isNotBlank() && vDesktop != BuildConfig.VERSION_NAME) {
            AlertDialog.Builder(this)
                .setTitle("Pembaruan tersedia")
                .setMessage(getString(R.string.versi_tidak_sama, vDesktop, BuildConfig.VERSION_NAME))
                .setPositiveButton(android.R.string.ok, null)
                .show()
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
     *
     * Termasuk peringatan batas antrean (8/10, 10/10), status perangkat
     * aktif, dan daftar per surat (antrean server + tertunda lokal) dengan
     * tombol hapus per baris.
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

        val nilaiBatas = tambahBaris(wadahSinkron, getString(R.string.sinkron_batas_antrean))
        nilaiBatas.text = if (sesi.masuk) {
            getString(R.string.sinkron_memuat)
        } else {
            getString(R.string.sinkron_tidak_terjangkau)
        }

        val nilaiAktif = tambahBaris(wadahSinkron, getString(R.string.sinkron_status_aktif))
        nilaiAktif.text = if (sesi.masuk) {
            getString(R.string.sinkron_memuat)
        } else {
            getString(R.string.sinkron_tidak_terjangkau)
        }

        tambahBaris(wadahSinkron, getString(R.string.sinkron_tertunda_perangkat)).text =
            getString(R.string.sinkron_tertunda_nilai, tertunda)

        btnKirimTertunda.isEnabled = sesi.masuk && tertunda > 0

        if (!sesi.masuk) {
            tampilkanDetailTertundaLokal()
            return
        }

        lifecycleScope.launch {
            val daftar = try {
                SupabaseSesi.klien(this@PengaturanActivity).daftarAntrean()
            } catch (e: SupabaseKlien.SesiKedaluwarsa) {
                null
            } catch (e: Exception) {
                null
            }
            val aktif = try {
                SupabaseSesi.klien(this@PengaturanActivity).perangkatAktif()
            } catch (e: Exception) {
                null
            }

            if (isFinishing || isDestroyed) return@launch

            if (daftar == null) {
                val teks = try {
                    SupabaseSesi.klien(this@PengaturanActivity).daftarAntrean()
                    getString(R.string.sinkron_tidak_terjangkau)
                } catch (e: SupabaseKlien.SesiKedaluwarsa) {
                    getString(R.string.sesi_tidak_berlaku_singkat)
                } catch (e: Exception) {
                    getString(R.string.sinkron_tidak_terjangkau)
                }
                nilaiAntrean.text = teks
                nilaiBatas.text = getString(R.string.sinkron_tidak_terjangkau)
            } else {
                val menunggu = daftar.count { it.status.equals("menunggu", ignoreCase = true) }
                val gagal = daftar.count { it.status.equals("gagal", ignoreCase = true) }
                nilaiAntrean.text = getString(R.string.sinkron_antrean_nilai, menunggu, gagal)
                val total = daftar.size
                nilaiBatas.text = when {
                    total >= 10 -> getString(R.string.sinkron_batas_penuh)
                    total >= 8 -> getString(R.string.sinkron_batas_hampir, total)
                    else -> getString(R.string.sinkron_batas_antrean_nilai, total)
                }
            }
            nilaiAktif.text = when (aktif) {
                true -> getString(R.string.sinkron_aktif_ya)
                false -> getString(R.string.sinkron_aktif_tidak)
                null -> getString(R.string.sinkron_aktif_tidak_dikenal)
            }
            // Perangkat nonaktif: kirim tertunda tidak akan berhasil.
            btnKirimTertunda.isEnabled = sesi.masuk && tertunda > 0 && aktif != false

            tampilkanDetail(daftar.orEmpty())
        }
    }

    /** Daftar per surat: antrean server (dengan hapus) + tertunda lokal. */
    private fun tampilkanDetail(barisServer: List<AntreanBaris>) {
        val daftarLokal = AntreanSurat.daftarTertunda(this)
        if (barisServer.isEmpty() && daftarLokal.isEmpty()) {
            tambahBaris(wadahSinkron, getString(R.string.sinkron_detail_judul)).text =
                getString(R.string.sinkron_detail_kosong)
            return
        }
        for (baris in barisServer) {
            val teks = listOfNotNull(
                baris.idJenis.ifBlank { null },
                baris.nomorSementara.ifBlank { null },
            ).joinToString(" · ").ifBlank { baris.id }
            val judulBaris = "ANTREAN · $teks"
            val nilai = tambahBaris(wadahSinkron, judulBaris)
            val catatan = listOfNotNull(
                baris.statusLabel,
                baris.catatan.ifBlank { null },
            ).joinToString(" · ")
            nilai.text = catatan
            tambahTombolHapus(wadahSinkron, onHapus = {
                konfirmasiHapusServer(baris)
            })
        }
        for (baris in daftarLokal) {
            val teks = listOfNotNull(
                baris.idJenis.ifBlank { null },
                baris.nomor.ifBlank { null },
            ).joinToString(" · ").ifBlank { baris.berkas.name }
            val nilai = tambahBaris(wadahSinkron, "TERTUNDA · $teks")
            nilai.text = baris.tanggal.ifBlank { "Tertunda di perangkat" }
            tambahTombolHapus(wadahSinkron, onHapus = {
                konfirmasiHapusLokal(baris)
            })
        }
    }

    /** Tombol kecil "Hapus" untuk satu baris detail. */
    private fun tambahTombolHapus(wadah: LinearLayout, onHapus: () -> Unit) {
        val tombol = android.widget.Button(this).apply {
            text = getString(R.string.sinkron_detail_hapus)
            textSize = 12f
            isAllCaps = false
            layoutParams = LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.WRAP_CONTENT,
                LinearLayout.LayoutParams.WRAP_CONTENT,
            ).apply {
                gravity = android.view.Gravity.END
                topMargin = Ui.dp(this@PengaturanActivity, 4)
            }
            setOnClickListener { onHapus() }
        }
        wadah.addView(tombol)
    }

    private fun konfirmasiHapusServer(baris: AntreanBaris) {
        AlertDialog.Builder(this)
            .setTitle(R.string.sinkron_detail_hapus_judul)
            .setMessage(
                getString(
                    R.string.sinkron_detail_hapus_pesan,
                    listOfNotNull(
                        baris.idJenis.ifBlank { null },
                        baris.nomorSementara.ifBlank { null },
                    ).joinToString(" · ").ifBlank { baris.id },
                ),
            )
            .setNegativeButton(android.R.string.cancel, null)
            .setPositiveButton(R.string.sinkron_detail_hapus) { _, _ -> hapusServer(baris) }
            .show()
    }

    private fun hapusServer(baris: AntreanBaris) {
        lifecycleScope.launch {
            val ok = try {
                SupabaseSesi.klien(this@PengaturanActivity).hapusAntrean(baris.id)
            } catch (e: Exception) {
                false
            }
            Toast.makeText(
                this@PengaturanActivity,
                if (ok) "Dihapus." else "Tidak bisa menghapus. Periksa koneksi.",
                Toast.LENGTH_SHORT,
            ).show()
            tampilkanSinkron()
        }
    }

    private fun konfirmasiHapusLokal(baris: AntreanSurat.TertundaBaris) {
        AlertDialog.Builder(this)
            .setTitle(R.string.sinkron_detail_hapus_judul)
            .setMessage(
                getString(
                    R.string.sinkron_detail_hapus_pesan,
                    listOfNotNull(
                        baris.idJenis.ifBlank { null },
                        baris.nomor.ifBlank { null },
                    ).joinToString(" · ").ifBlank { baris.berkas.name },
                ),
            )
            .setNegativeButton(android.R.string.cancel, null)
            .setPositiveButton(R.string.sinkron_detail_hapus) { _, _ ->
                baris.berkas.delete()
                tampilkanSinkron()
            }
            .show()
    }

    private fun tampilkanDetailTertundaLokal() {
        // Tampilkan tertunda lokal saja (offline / belum terpasang).
        val daftar = AntreanSurat.daftarTertunda(this)
        if (daftar.isEmpty()) {
            tambahBaris(wadahSinkron, getString(R.string.sinkron_detail_judul)).text =
                getString(R.string.sinkron_detail_kosong)
            return
        }
        for (baris in daftar) {
            val teks = listOfNotNull(
                baris.idJenis.ifBlank { null },
                baris.nomor.ifBlank { null },
            ).joinToString(" · ").ifBlank { baris.berkas.name }
            val nilai = tambahBaris(
                wadahSinkron,
                "TERTUNDA · $teks",
            )
            nilai.text = baris.tanggal.ifBlank { "Tertunda di perangkat" }
            tambahTombolHapus(wadahSinkron, onHapus = {
                konfirmasiHapusLokal(baris)
            })
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
