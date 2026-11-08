package dev.sumberjaya.sudes.android

import android.content.Context
import androidx.work.Constraints
import androidx.work.CoroutineWorker
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.NetworkType
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkManager
import androidx.work.WorkerParameters
import java.util.concurrent.TimeUnit

/**
 * Worker retry terjadwal: tarik kop/skema agar selalu terbaru, lalu kirim
 * surat tertunda saat jaringan tersedia. Antrean yang gagal tidak lagi hanya
 * dicoba saat menu dibuka — WorkManager menjadwalkan ulang sampai terkirim
 * atau dihapus.
 */
class PengirimWorker(ctx: Context, params: WorkerParameters) :
    CoroutineWorker(ctx, params) {
    override suspend fun doWork(): Result {
        return try {
            // Tarik dulu supaya kop/skema di perangkat mengikuti desktop
            // tanpa operator membuka aplikasi.
            try { PengaturanSinkron.tarik(applicationContext) } catch (e: Exception) {
                // Gagal tarik (offline) tidak membatalkan kirim.
            }
            val terkirim = AntreanSurat.kirimTertunda(applicationContext)
            if (terkirim == 0 && AntreanSurat.jumlahTertunda(applicationContext) > 0) {
                Result.retry()
            } else {
                Result.success()
            }
        } catch (e: Exception) {
            Result.retry()
        }
    }

    companion object {
        private const val NAMA = "sudes-pengirim-tertunda"

        fun jadwalkan(ctx: Context) {
            val kendala = Constraints.Builder()
                .setRequiredNetworkType(NetworkType.CONNECTED)
                .build()
            val permintaan = PeriodicWorkRequestBuilder<PengirimWorker>(15, TimeUnit.MINUTES)
                .setConstraints(kendala)
                .addTag(NAMA)
                .build()
            WorkManager.getInstance(ctx).enqueueUniquePeriodicWork(
                NAMA, ExistingPeriodicWorkPolicy.KEEP, permintaan)
        }

        fun sekali(ctx: Context) {
            val kendala = Constraints.Builder()
                .setRequiredNetworkType(NetworkType.CONNECTED)
                .build()
            val permintaan = androidx.work.OneTimeWorkRequestBuilder<PengirimWorker>()
                .setConstraints(kendala)
                .addTag(NAMA)
                .build()
            // Unik: surat baru menimpa pengiriman sekali yang masih menunggu,
            // tidak menumpuk worker untuk tiap surat.
            WorkManager.getInstance(ctx).enqueueUniqueWork(
                "$NAMA:sekali",
                androidx.work.ExistingWorkPolicy.REPLACE,
                permintaan,
            )
        }
    }
}
