plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

android {
    namespace = "dev.sumberjaya.sudes.android"
    compileSdk = 35
    buildToolsVersion = "36.0.0"

    defaultConfig {
        applicationId = "dev.sumberjaya.sudes.android"
        minSdk = 26
        targetSdk = 35
        versionCode = 21302
        versionName = "2.13.2"
    }

    buildTypes {
        release {
            isMinifyEnabled = true
            isShrinkResources = true
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    kotlinOptions {
        jvmTarget = "17"
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
    }
}

dependencies {
    testImplementation("junit:junit:4.13.2")
    implementation("androidx.core:core-ktx:1.13.1")
    implementation("androidx.appcompat:appcompat:1.7.0")
    implementation("com.google.android.material:material:1.12.0")
    implementation("androidx.constraintlayout:constraintlayout:2.1.4")
    implementation("androidx.recyclerview:recyclerview:1.3.2")
    implementation("androidx.lifecycle:lifecycle-runtime-ktx:2.8.7")

    // Supabase REST (Auth + PostgREST) — client tipis, tanpa SDK berat.
    implementation("com.squareup.okhttp3:okhttp:4.12.0")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.8.1")

    // Pindai QR pairing.
    implementation("com.journeyapps:zxing-android-embedded:4.3.0")

    // Penyimpanan sesi terenkripsi (kunci di Android Keystore).
    implementation("androidx.security:security-crypto:1.1.0-alpha06")

    // Retry terjadwal saat jaringan tersedia (WorkManager).
    implementation("androidx.work:work-runtime-ktx:2.9.0")
}
