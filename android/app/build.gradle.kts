plugins {
    id("com.android.application")
}

/**
 * Waktu commit terakhir yang mengubah skema formulir desktop, dalam milidetik.
 * 0 bila git atau berkasnya tidak tersedia (build bersih/CI tanpa riwayat), dan
 * dalam keadaan itu aplikasi tidak menganggap APK-nya lebih baru dari server.
 */
fun waktuSkemaDibekukan(): Long {
    val berkas = rootProject.projectDir.parentFile
        ?.resolve("SuDesApp.Core/Configuration/SkemaFormulir.json") ?: return 0L
    if (!berkas.exists()) return 0L
    return try {
        val proses = ProcessBuilder(
            "git", "log", "-1", "--format=%ct", "--",
            berkas.absolutePath.replace(File.separatorChar, '/'),
        )
            .directory(rootProject.projectDir)
            .redirectErrorStream(true)
            .start()
        val keluar = proses.inputStream.bufferedReader().readText().trim()
        proses.waitFor()
        keluar.lines().firstOrNull()?.trim()?.toLongOrNull()?.times(1000L) ?: 0L
    } catch (e: Exception) {
        0L
    }
}

android {
    namespace = "dev.sumberjaya.sudes.android"
    compileSdk = 37
    buildToolsVersion = "36.0.0"

    defaultConfig {
        applicationId = "dev.sumberjaya.sudes.android"
        minSdk = 26
        targetSdk = 35
        versionCode = 21501
        versionName = "2.15.1"
    }

    buildTypes {
        release {
            isMinifyEnabled = true
            isShrinkResources = true
            // Tanpa baris ini app/proguard-rules.pro tidak pernah dibaca dan
            // R8 membuang konstruktor no-arg WorkDatabase_Impl — aplikasi
            // crash saat androidx.startup.InitializationProvider dipasang.
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro",
            )
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    buildFeatures {
        viewBinding = true
        buildConfig = true
    }

    defaultConfig {
        // URL + kunci publik Supabase dibaca dari android/supabase.properties
        // (berkas per mesin, tidak di-commit). Kunci publik memang boleh
        // tampil di dalam APK; kata sandi perangkat TIDAK pernah di sini.
        val properti = mutableMapOf<String, String>()
        val berkas = rootProject.file("supabase.properties")
        if (berkas.exists()) {
            berkas.readLines()
                .filter { it.contains('=') && !it.trimStart().startsWith("#") }
                .forEach { baris ->
                    val kunci = baris.substringBefore('=').trim()
                    properti[kunci] = baris.substringAfter('=').trim()
                }
        }
        buildConfigField("String", "SUPABASE_URL", "\"${properti["SUPABASE_URL"] ?: ""}\"")
        buildConfigField("String", "SUPABASE_ANON_KEY", "\"${properti["SUPABASE_ANON_KEY"] ?: ""}\"")

        // Kapan skema desktop ini dibekukan ke APK, sebagai waktu commit terakhir
        // berkas skema. Dipakai untuk memutuskan mana yang lebih baru antara
        // terbitan desktop di server dan salinan di dalam APK: tanpa itu, APK
        // terbaru kalah oleh terbitan desktop lama dan operator melihat formulir
        // versi lama walaupun baru memasang APK baru. Build tanpa git (0) tidak
        // mengambil alih: server tetap yang dipegang.
        val waktuSkema = waktuSkemaDibekukan()
        buildConfigField("long", "SKEMA_SNAPSHOT_MILLIS", "${waktuSkema}L")
    }
}

dependencies {
    testImplementation("junit:junit:4.13.2")
    implementation("androidx.core:core-ktx:1.19.1")
    implementation("androidx.appcompat:appcompat:1.7.0")
    implementation("com.google.android.material:material:1.12.0")
    implementation("androidx.constraintlayout:constraintlayout:2.1.4")
    implementation("androidx.recyclerview:recyclerview:1.4.0")
    implementation("androidx.lifecycle:lifecycle-runtime-ktx:2.8.7")

    // Supabase REST (Auth + PostgREST) — client tipis, tanpa SDK berat.
    implementation("com.squareup.okhttp3:okhttp:5.5.1")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.11.0")

    // Pindai QR pairing.
    implementation("com.journeyapps:zxing-android-embedded:4.3.0")

    // Penyimpanan sesi terenkripsi (kunci di Android Keystore).
    implementation("androidx.security:security-crypto:1.1.0")

    // Retry terjadwal saat jaringan tersedia (WorkManager).
    implementation("androidx.work:work-runtime-ktx:2.9.0")
}
