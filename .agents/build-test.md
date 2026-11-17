# Build & Uji Guidelines

## Build
- Jangan menambahkan `-f net10.0` pada `dotnet test`: proyek uji hanya punya target `net11.0-windows`, hasilnya galat NETSDK1005 yang menyesalkan (seolah restore belum jalan).
- Analyzer SUDES0001 (dipasang lewat `Directory.Build.props`) menggagalkan build bila ada blok `catch` kosong tanpa komentar alasan di dalamnya.

## Android
- Build Android **lokal**, bukan lewat GitHub Actions: `cd android && ./gradlew assembleDebug --console=plain` lalu `./gradlew testDebugUnitTest --console=plain`.
- Butuh `JAVA_HOME` ke **JDK 21** (sama dengan CI). JBR bawaan Android Studio (`C:\Program Files\Android\Android Studio\jbr`) melaporkan versi `25.0.4` yang **tidak bisa di-parse** Kotlin DSL Gradle — build gagal dengan `java.lang.IllegalArgumentException: 25.0.4` tanpa penjelasan lain. JDK 21 lokal: `C:\Program Files\Eclipse Adoptium\jdk-21.0.13.9-hotspot` (pasang dengan `winget install --id EclipseAdoptium.Temurin.21.JDK`).
- `android/local.properties` sudah berisi `sdk.dir` ke SDK lokal, jadi `ANDROID_HOME` tidak perlu disetel.
- Build pertama mengunduh dependensi; beri waktu beberapa menit sebelum menyimpulkan gagal.
- **AGP 9 + built-in Kotlin**: sejak AGP 9.0, dukungan Kotlin sudah terpasang di AGP — jangan.apply `org.jetbrains.kotlin.android` lagi (plugin itu menyebabkan error "no longer required for Kotlin support"). Blok `android { kotlinOptions { ... } }` juga ditolak; `jvmTarget` kini otomatis mengikuti `compileOptions.targetCompatibility`, jadi blok itu cukup dihapus.
- **Rantai upgrade Android saling terkait**: `kotlinx-coroutines-android` 1.11.0 dibangun dengan Kotlin lebih baru sehingga menaikkannya memaksa naikkan plugin Kotlin (2.0.21 → 2.2.10) lebih dulu; `okhttp` 5.5.1 menuntut `compileSdk` 37 yang hanya didukung AGP 9.1+. Naikkan bertahap dan verifikasi tiap langkah, jangan sekaligus.

## Uji
- Perintah `dotnet test ... -v q` ala README menyembunyikan keluaran `ITestOutputHelper`; tambahkan `--logger "console;verbosity=detailed"` lalu saring dengan grep bila butuh angka dari tes.
- Uji xunit berjalan PARALEL dalam satu proses, jadi `Environment.SetEnvironmentVariable` di satu uji mengganggu uji lain yang membaca env yang sama — jangan pakai env global untuk kredensial dalam tes; teruskan nilai lewat konstruktor/parameter seperti yang dilakukan `UjiSertifikatTestServer` dan `AdminOperatorRegistry` (builder in-memory).
- Gateway pembayaran (Midtrans) sudah dihapus dan dijaga tetap pergi oleh `TanpaGatewayPembayaranTests` (berkas, endpoint, kunci config, medan JSON `token`/`link_pembayaran`/`mode`). Jangan menambahkan jalur pembayaran otomatis baru tanpa mengubah uji itu lebih dulu; jalan satu-satunya memberi hak akses adalah `POST /admin/pelanggan/{kode}/tandai-lunas` (dijaga `KontrakHubunganServerLisensiTests` lewat `AdminEndpointCatalog`).
