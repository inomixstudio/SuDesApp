# Keep classes accessed via reflection / JSON parsing.
-keep class org.json.** { *; }
-keep class okhttp3.** { *; }
-keep class com.journeyapps.barcodescanner.** { *; }
-keep class androidx.security.crypto.** { *; }
-keep class androidx.work.** { *; }

# AGP 9 menjalankan R8 dalam full mode: aturan "-keep class" polos tidak
# lagi melindungi konstruktor no-arg, sehingga WorkDatabase_Impl dibuang dan
# WorkManager crash saat androidx.startup.InitializationProvider dipasang —
# aplikasi mati sebelum layar pertama. Nama konstruktor harus disebut eksplisit.
-keep class * extends androidx.room.RoomDatabase { void <init>(); }
-keepclassmembers class * extends androidx.room.RoomDatabase { void <init>(); }
-dontwarn androidx.room.**

# Keep data classes / enums referenced by name from resources or reflection.
-keepclassmembers class * {
    public <init>(...);
}

# Suppress warnings for optional dependencies.
-dontwarn okhttp3.**
-dontwarn okio.**
-dontwarn com.google.zxing.**
