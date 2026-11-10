# Keep classes accessed via reflection / JSON parsing.
-keep class org.json.** { *; }
-keep class okhttp3.** { *; }
-keep class com.journeyapps.barcodescanner.** { *; }
-keep class androidx.security.crypto.** { *; }
-keep class androidx.work.** { *; }

# Keep data classes / enums referenced by name from resources or reflection.
-keepclassmembers class * {
    public <init>(...);
}

# Suppress warnings for optional dependencies.
-dontwarn okhttp3.**
-dontwarn okio.**
-dontwarn com.google.zxing.**
