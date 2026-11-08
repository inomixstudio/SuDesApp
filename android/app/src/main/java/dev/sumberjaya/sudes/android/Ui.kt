package dev.sumberjaya.sudes.android

import android.app.Activity
import android.content.Context
import android.content.res.Configuration
import android.view.View
import androidx.core.view.ViewCompat
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat

/**
 * Pembantu tampilan yang dipakai lintas layar.
 *
 * Sengaja kecil dan tanpa library tambahan: hanya konversi dp dan penanganan
 * inset sistem (status bar / navigation bar) supaya isi tidak tertutup saat
 * aplikasi berjalan edge-to-edge di Android 15.
 */
object Ui {

    fun dp(ctx: Context, nilai: Int): Int =
        (nilai * ctx.resources.displayMetrics.density + 0.5f).toInt()

    /** Tambahkan tinggi status bar ke padding atas view (aman dipanggil ulang). */
    fun insetAtas(view: View) = pasangInset(view, R.id.tag_inset_atas, atas = true)

    /** Tambahkan tinggi navigation bar ke padding bawah view. */
    fun insetBawah(view: View) = pasangInset(view, R.id.tag_inset_bawah, atas = false)

    /**
     * Kepala halaman bergradien gelap, jadi ikon status bar dibuat terang.
     * Begitu kepala tergulir habis, latar di bawah status bar menjadi terang
     * (tema siang) dan ikon perlu dibalik jadi gelap. Di tema malam latar tetap
     * gelap, jadi tidak perlu diubah.
     */
    fun statusBarIkutGulir(activity: Activity, gulir: View, kepala: View) {
        val malam = (activity.resources.configuration.uiMode and
            Configuration.UI_MODE_NIGHT_MASK) == Configuration.UI_MODE_NIGHT_YES
        if (malam) return

        val kontrol = WindowCompat.getInsetsController(activity.window, gulir)
        var terang = false
        gulir.setOnScrollChangeListener { _, _, y, _, _ ->
            val baru = y >= kepala.height
            if (baru != terang) {
                terang = baru
                kontrol.isAppearanceLightStatusBars = baru
            }
        }
    }

    private fun pasangInset(view: View, tag: Int, atas: Boolean) {
        val awal = view.getTag(tag) as? Int ?: if (atas) view.paddingTop else view.paddingBottom
        view.setTag(tag, awal)

        ViewCompat.setOnApplyWindowInsetsListener(view) { v, insets ->
            val bars = insets.getInsets(WindowInsetsCompat.Type.systemBars())
            if (atas) {
                v.setPadding(
                    v.paddingLeft,
                    awal + bars.top,
                    v.paddingRight,
                    v.paddingBottom,
                )
            } else {
                v.setPadding(
                    v.paddingLeft,
                    v.paddingTop,
                    v.paddingRight,
                    awal + bars.bottom,
                )
            }
            insets
        }
        ViewCompat.requestApplyInsets(view)
    }
}
