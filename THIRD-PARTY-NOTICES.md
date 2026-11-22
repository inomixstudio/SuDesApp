# Pemberitahuan Pihak Ketiga — SuDesApp

SuDesApp sendiri dilisensikan **MIT** (lihat [`LICENSE`](LICENSE)). Aplikasi
memakai pustaka dan alat pihak ketiga yang tetap tunduk pada lisensi
masing-masing. Berkas ini adalah ringkasan, bukan pengganti teks lisensi resmi.

## Desktop dan layanan .NET

| Paket | Lisensi | Tautan |
|---|---|---|
| Dapper | Apache-2.0 | <https://github.com/DapperLib/Dapper> |
| Microsoft.Data.Sqlite.Core | MIT | <https://github.com/dotnet/efcore> |
| SQLitePCLRaw.bundle_e_sqlcipher | Apache-2.0 | <https://github.com/ericsink/SQLitePCL.raw> |
| Npgsql | PostgreSQL License | <https://github.com/npgsql/npgsql> |
| Newtonsoft.Json | MIT | <https://www.newtonsoft.com/json> |
| Google.Apis.Drive.v3 | Apache-2.0 | <https://github.com/google/google-api-dotnet-client> |
| QRCoder | MIT | <https://github.com/codebude/QRCoder> |
| Microsoft.Web.WebView2 | Lisensi Microsoft (lihat `LICENSE.txt` paket) | <https://aka.ms/webview> |
| Microsoft.Extensions.* | MIT | <https://github.com/dotnet/runtime> |
| Microsoft.AspNetCore.OpenApi | MIT | <https://github.com/dotnet/aspnetcore> |
| System.Security.Cryptography.ProtectedData | MIT | <https://github.com/dotnet/runtime> |
| Microsoft.CodeAnalysis.CSharp | MIT | <https://github.com/dotnet/roslyn> |

Paket uji (`Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`) hanya
dipakai saat build dan tidak ikut ke installer atau hasil publish.

## Android

| Pustaka | Lisensi |
|---|---|
| AndroidX (core-ktx, appcompat, constraintlayout, recyclerview, lifecycle, security-crypto, work-runtime) | Apache-2.0 |
| Material Components for Android | Apache-2.0 |
| OkHttp | Apache-2.0 |
| Kotlinx Coroutines | Apache-2.0 |
| ZXing Android Embedded | Apache-2.0 |

## Alat build dan rilis

| Alat | Lisensi |
|---|---|
| .NET SDK dan runtime | MIT |
| Gradle dan Android Gradle Plugin | Apache-2.0 |
| Inno Setup | Inno Setup License |
| GitHub Actions | Ketentuan GitHub |

## Aset dan merek

- Lambang kabupaten/kota berasal dari sumber publik dan dipakai untuk kop surat
  desa; hak atas lambang tetap milik pemerintah daerah masing-masing.
- Times New Roman adalah merek dagang The Monotype Corporation. Berkas font
  disertakan untuk memastikan hasil cetak konsisten di komputer desa.
- Nama, logo, dan identitas Sumberjaya Dev/PT Duta Publik tidak dilisensikan
  untuk dipakai sebagai merek produk turunan.

Bila Anda menemukan pustaka yang belum tercantum atau atribusi yang keliru,
laporkan lewat [issue](https://github.com/inomixstudio/SuDesApp/issues) atau
<sumberjayadev@gmail.com>.
