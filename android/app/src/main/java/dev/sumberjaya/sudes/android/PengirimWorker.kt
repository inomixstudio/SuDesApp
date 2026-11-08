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
 * Worker retry terjadwal: kirim surat tertunda saat jaringan tersedia.
 * Antrean yang gagal tidak lagi hanya dicoba saat menu dibuka — WorkManager
 * menjadwalkan ulang dengan backoff sampai terkirim atau dihapus.
 */
class PengirimWorker(ctx: Context, params: WorkerParameters) :
    CoroutineWorker(ctx, params) {
    override suspend fun doWork(): Result {
        return try {
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
            WorkManager.getInstance(ctx).enqueue(permintaan)
        }
    }
}
