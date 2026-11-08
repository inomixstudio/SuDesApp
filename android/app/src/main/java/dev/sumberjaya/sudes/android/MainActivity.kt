package dev.sumberjaya.sudes.android

import android.content.Intent
import android.content.res.ColorStateList
import android.graphics.Color
import android.os.Bundle
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import androidx.lifecycle.lifecycleScope
import com.google.android.material.card.MaterialCardView
import kotlinx.coroutines.launch

/**
 * Menu utama.
 *
 * Dua jalur pembuatan surat dipisah sesuai pembagian di aplikasi desktop:
 * surat biasa di "Buat Surat", persyaratan pernikahan (N1-N6, N8) di
 * "Buat NTCR".
 *
 * PDF dibuat dan disimpan di perangkat; isian surat ikut antrean Supabase
 * supaya register desktop bisa memberi nomor resmi. Bila perangkat belum
 * dipasang atau jaringan gagal, isian disimpan sebagai surat tertunda dan
 * dikirim saat menu ini dibuka lagi.
 */
class MainActivity : AppCompatActivity() {

    private enum class GayaKartu { BIASA, UTAMA, LEMBUT }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)

        // Jadwalkan retry latar: surat tertunda dikirim saat jaringan pulih.
        PengirimWorker.jadwalkan(this)

        Ui.insetAtas(findViewById(R.id.kepala))
        Ui.insetBawah(findViewById(R.id.konten))
        Ui.statusBarIkutGulir(this, findViewById(R.id.gulir), findViewById(R.id.kepala))

        isiMenu()
        tampilkanIdentitas()

        // Identitas kop dan pola nomor diambil dari desktop, bukan diketik di
        // sini. Penarikan hanya jalan bila perangkat sudah dipasang.
        lifecycleScope.launch {
            val hasil = try {
                PengaturanSinkron.tarik(this@MainActivity)
            } catch (e: Exception) {
                // Offline / belum terpasang: pakai salinan lokal.
                null
            }

            // Sesi yang ditolak server tidak bisa dipakai lagi: bersihkan dan
            // minta operator memasang ulang, jangan biarkan aplikasi tampak
            // tersambung padahal setiap kiriman akan gagal.
            if (hasil is PengaturanSinkron.HasilTarik.SesiKedaluwarsa) {
                SupabaseSesi.keluar(this@MainActivity)
                Toast.makeText(
                    this@MainActivity,
                    getString(R.string.sesi_tidak_berlaku),
                    Toast.LENGTH_LONG,
                ).show()
                startActivity(Intent(this@MainActivity, LoginActivity::class.java))
                finish()
                return@launch
            }

            tampilkanIdentitas()
            kirimTertundaSekarang()
        }
    }

    override fun onResume() {
        super.onResume()
        tampilkanIdentitas()
        laporkanAktif()
        tarikBilaLama()
    }

    /**
     * Tarik kop/skema lagi bila sudah lama (>5 menit) — supaya perubahan di
     * desktop terlihat tanpa operator menekan tombol sinkron di Pengaturan.
     * Tarikan pertama tetap di onCreate.
     */
    private fun tarikBilaLama() {
        if (!SupabaseSesi.ambil(this).masuk) return
        if (!PengaturanSinkron.perluTarikUlang(this)) return
        lifecycleScope.launch {
            try {
                PengaturanSinkron.tarik(this@MainActivity)
            } catch (e: Exception) {
                // Offline: pakai salinan lokal; dicoba lagi lain waktu.
            } finally {
                tampilkanIdentitas()
            }
        }
    }

    /**
     * Lapor ke desktop paling sering sekali per menit supaya status bar di
     * komputer menampilkan "Android terhubung". Kegagalan (offline, sesi mati)
     * diabaikan: penarikan pengaturan sudah menangani sesi kedaluwarsa.
     */
    private fun laporkanAktif() {
        if (!SupabaseSesi.ambil(this).masuk) return

        val prefs = getSharedPreferences("lapor_aktif", MODE_PRIVATE)
        val sekarang = System.currentTimeMillis()
        if (sekarang - prefs.getLong("terakhir", 0L) < 60_000L) return
        prefs.edit().putLong("terakhir", sekarang).apply()

        lifecycleScope.launch {
            try {
                SupabaseKlien(SupabaseSesi.ambil(this@MainActivity)).tandaiOnline()
            } catch (e: Exception) {
                // Laporan status bukan hal yang perlu mengganggu operator.
            }
        }
    }

    private fun isiMenu() {
        tambahKartu(
            R.id.wadah_surat,
            R.drawable.ic_surat,
            R.string.menu_buat_surat,
            R.string.ket_buat_surat,
            GayaKartu.UTAMA,
        ) {
            startActivity(
                Intent(this, BuatSuratActivity::class.java)
                    .putExtra(BuatSuratActivity.EXTRA_GROUPS, BuatSuratActivity.GROUP_SURAT)
            )
        }

        tambahKartu(
            R.id.wadah_surat,
            R.drawable.ic_register,
            R.string.menu_register_surat,
            R.string.ket_register_surat,
        ) {
            startActivity(Intent(this, RegisterSuratActivity::class.java))
        }

        tambahKartu(
            R.id.wadah_ntcr,
            R.drawable.ic_ntcr,
            R.string.menu_buat_ntcr,
            R.string.ket_buat_ntcr,
            GayaKartu.LEMBUT,
        ) {
            startActivity(
                Intent(this, BuatSuratActivity::class.java)
                    .putExtra(BuatSuratActivity.EXTRA_GROUPS, BuatSuratActivity.GROUP_NTCR)
            )
        }

        tambahKartu(
            R.id.wadah_lainnya,
            R.drawable.ic_hadir,
            R.string.menu_daftar_hadir,
            R.string.ket_daftar_hadir,
        ) {
            startActivity(Intent(this, DaftarHadirActivity::class.java))
        }

        tambahKartu(
            R.id.wadah_lainnya,
            R.drawable.ic_pengaturan,
            R.string.menu_pengaturan,
            R.string.ket_pengaturan,
        ) {
            startActivity(Intent(this, PengaturanActivity::class.java))
        }

        tambahKartu(
            R.id.wadah_lainnya,
            R.drawable.ic_sinkron,
            R.string.menu_sinkron,
            R.string.ket_sinkron,
        ) {
            startActivity(Intent(this, LoginActivity::class.java))
        }
    }

    private fun tambahKartu(
        wadahId: Int,
        ikon: Int,
        judul: Int,
        keterangan: Int,
        gaya: GayaKartu = GayaKartu.BIASA,
        aksi: () -> Unit,
    ) {
        val wadah = findViewById<LinearLayout>(wadahId)
        val kartu = layoutInflater.inflate(R.layout.item_menu, wadah, false) as MaterialCardView
        val wadahIkon = kartu.findViewById<FrameLayout>(R.id.wadah_ikon)
        val gambar = kartu.findViewById<ImageView>(R.id.ikon)
        val teksJudul = kartu.findViewById<TextView>(R.id.judul)
        val teksKet = kartu.findViewById<TextView>(R.id.ket)
        val panah = kartu.findViewById<ImageView>(R.id.panah)

        gambar.setImageResource(ikon)
        teksJudul.setText(judul)
        teksKet.setText(keterangan)

        when (gaya) {
            GayaKartu.UTAMA -> {
                kartu.setCardBackgroundColor(warna(R.color.accent))
                kartu.strokeWidth = 0
                kartu.rippleColor = ColorStateList.valueOf(Color.argb(46, 255, 255, 255))
                wadahIkon.backgroundTintList = ColorStateList.valueOf(Color.argb(41, 255, 255, 255))
                gambar.imageTintList = ColorStateList.valueOf(Color.WHITE)
                teksJudul.setTextColor(Color.WHITE)
                teksKet.setTextColor(Color.argb(204, 255, 255, 255))
                panah.imageTintList = ColorStateList.valueOf(Color.argb(191, 255, 255, 255))
            }

            GayaKartu.LEMBUT -> {
                kartu.setCardBackgroundColor(warna(R.color.accent_muda))
                kartu.strokeWidth = 0
                wadahIkon.backgroundTintList = warna(R.color.surface)
                gambar.imageTintList = warna(R.color.accent)
                teksJudul.setTextColor(warna(R.color.accent_dark))
                teksKet.setTextColor(warna(R.color.teks_redup))
                panah.imageTintList = warna(R.color.accent_dark)
            }

            GayaKartu.BIASA -> Unit
        }

        kartu.setOnClickListener { aksi() }
        wadah.addView(kartu)
    }

    private fun warna(id: Int): ColorStateList =
        ColorStateList.valueOf(ContextCompat.getColor(this, id))

    /**
     * Surat yang sempat gagal terkirim dicoba lagi setiap menu dibuka, jadi
     * operator tidak perlu mengingat-ingat mengirim ulang.
     */
    private suspend fun kirimTertundaSekarang() {
        if (AntreanSurat.jumlahTertunda(this) == 0) return

        val terkirim = try {
            AntreanSurat.kirimTertunda(this)
        } catch (e: Exception) {
            0
        }

        if (terkirim > 0) {
            Toast.makeText(
                this,
                getString(R.string.antrean_tertunda_terkirim, terkirim),
                Toast.LENGTH_SHORT,
            ).show()
        }
    }

    private fun tampilkanIdentitas() {
        val p = PengaturanSurat.ambil(this)
        val judul = findViewById<TextView>(R.id.nama_desa)
        judul.text = listOf(p.namaDesa, p.kecamatan, p.kabupaten)
            .map { it.trim() }
            .filter { it.isNotEmpty() }
            .joinToString("  \u00B7  ")
            .ifBlank { getString(R.string.identitas_belum_sinkron) }
    }
}
