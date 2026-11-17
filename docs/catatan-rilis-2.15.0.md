# SuDesApp 2.15.0 — 11 November 2026

Rilis pemeliharaan: migrasi ke .NET 11, pembersihan kode mati hasil audit menyeluruh, token login Google memakai serialisasi bawaan platform, login Google aktif otomatis dari installer, gerbang uji E2E server lisensi diperbaiki, Android menaikkan alat build ke AGP 9 / Gradle 9.8 / compileSdk 37, isian Buat Surat Android dipecah per kartu mengikuti formulir desktop, dan tombol Hubungkan di halaman Langganan dibuat tetap tersambung ke server lisensi lokal.

Tidak ada perubahan format data: `desa.db`, `Templates`, token Google yang sudah tersimpan, dan arsip surat lama dipakai apa adanya.

## Apa yang Baru

- **Login Google aktif otomatis** — kredensial OAuth kini tertanam pada paket rilis, sehingga pengguna tidak lagi mengisi Client ID/Secret manual; halaman isian kredensial manual dan halaman masuk terpisah dihapus, dan halaman Google Drive yang menangani akun.
- **Umpan balik tekan halus** — tombol mengecil sedikit (skala 0,97) saat ditekan dan mendarat lembut saat dilepas; toast notifikasi kritis masuk dari kanan dengan fade singkat. Keduanya hanya menganimasikan `RenderTransform`/`Opacity` di lapisan komposisi dan dilewati bila animasi dimatikan pada Windows.
- **Keterangan Penghasilan (472)** — formulir desktop baru (data diri + pekerjaan/usaha, penghasilan per bulan, jumlah tanggungan), generator PDF sendiri, skema Android, dan impor desktop yang memetakan `penghasilan.*` sehingga surat dari Android masuk register lengkap.
- **Lambang kabupaten tertanam** — satu arsip `LambangKabupaten.zip` (±20 MB, 514 lambang ≤512 px) dipakai bersama; installer tidak lagi menyalin 514 berkas mentah (±214 MB) ke folder instalasi. Berkas pengguna di `%LocalAppData%` tetap dihormati lebih dulu.
- **Formulir pendataan perangkat desa selalu satu halaman** — di A4 maupun F4; bila kop tambah baris (alamat panjang/surel), jarak vertikal dicoba berurutan lebih rapat sampai muat, ukuran huruf tidak berubah.
- **Teks bersama Surat Ijin Tinggal** — alamat dan kalimat keterangan kini dibangun dari satu sumber (`IjinTinggalGenerator`) yang dipakai bersama generator PDF dan form isian, jadi isian selalu persis sama dengan hasil cetak.
- **Laporan Penduduk: tabel Pekerjaan** — statistik pekerjaan seperti tabel lainnya; dapat dicetak ke PDF dan diekspor.
- **IDM: pilihan tahun laporan** — combobox “Tahun laporan” membuat cetak PDF mengikuti tahun yang dipilih, bukan selalu tahun layar.
- **Dokumentasi bisa dicari** — saringan judul/keterangan/jalur per kelompok dengan tombol bersihkan dan keadaan “Tidak ada dokumen yang cocok”.
- **Android: PDF menyamai desktop** — blanko NTCR (N1–N6, N8), Ahli Waris tiga halaman, rincian garapan bertabel, dan penulisan nama bergelar.
- **Android: isian Buat Surat per kartu** — formulir tidak lagi satu daftar panjang; kolom dikelompokkan ke kartu berjudul sama seperti section di desktop, dan judul serta urutannya diturunkan otomatis dari XAML desktop.
- **Sinkron Android bergerbang versi** — perangkat dengan `versionName` berbeda dari desktop menahan sinkron (QR, kode, maupun muat ulang menu) dan meminta aplikasi diperbarui lebih dulu, supaya skema dan pola nomor tidak dipakai silang antar versi.
- **Android: alat build baru** — AGP 9.3 (built-in Kotlin), Gradle 9.8.2, compileSdk 37, okhttp 5.5.1.

## Apa yang Ditingkatkan

- **Platform .NET 11** — seluruh proyek `net11.0`, paket `Microsoft.Extensions.*`/`Sqlite.Core`/`AspNetCore.OpenApi` 11.0.0, SDK dikunci 11.0.100 stabil, runtime Docker `aspnet:11.0`.
- **Kode mati dibersihkan** — 30 resource XAML, 2 converter, sisa metode mati di Core/Server/Wpf/SudesPdf.Core, 3 skrip alat, paket `System.Security.Cryptography.Xml`, dan 562 baris lanjutan (seluruh pemroses WhatsApp di `FormatPermintaan`, 5 method `FileService`, 2 kelas pengecualian, `PdfObjects` kosong, 5 konstanta ikon).
- **Disiplin peringatan disamakan** — `TreatWarningsAsErrors` berlaku juga di `SudesPdf.Core`, `SudesExPlus`, `SuDesApp.Api`, `UjiTrialServerE2E`, dan `healthcheck`.
- **Token Google** — serialisasi pindah ke `System.Text.Json` dengan konverter setia pada bentuk JSON Google; token lama tetap terbaca dan pengguna tidak perlu login ulang.
- **Gerbang CI** — audit kontras tema WCAG jalan di setiap push; GitHub Actions dinaikkan (checkout 7.0.1, setup-dotnet 6.1.0, setup-java 6.2.0, setup-gradle 6.4.1) dan tetap dikunci commit SHA; `gitleaks-action` dipin ke SHA.
- **Uji** — penjaga katalog surat daring (6 uji) dan keselarasan catatan rilis (3 uji); `MidtransService` menerima `serverKey` eksplisit; `Microsoft.NET.Test.Sdk` 18.12.0.
- **Tampilan** — halaman Pembaruan disusun dua kartu berdampingan; Langganan dipangkas mati; Tentang Aplikasi diperbarui; pratinjau kop dibatasi tinggi/lebar acuan kertas.
- **Dokumentasi** — README, Dockerfile, `.agents/*`, `.vscode/launch.json`, dan catatan rilis diperbaiki agar tidak lagi menyebut .NET 10 / SDK prarilis.

## Apa yang Diperbaiki

- **Skeleton permanen di form Buat Surat Baru** dihapus — placeholder kini hanya muncul saat memuat; header accordion diberi nama UI Automation agar terbaca pembaca layar.
- **Animasi masuk berurutan di Beranda** dihapus — isi halaman tampil langsung penuh, gerakan yang tersisa hanya umpan balik.
- **Batal login Google** tidak lagi membuat tombol Masuk mati permanen; kunci OAuth diberi tenggang 10 menit.
- **Panduan Awal** membuka halaman pengaturan yang asli (instance berisi data), bukan form kosong yang melempar galat render.
- **Riwayat Aktivitas** — “Bersihkan Riwayat” kini benar-benar mengosongkan tabel; antrean latar tidak lagi menulis ulang baris yang sudah dihapus, dan aktivitas baru yang dicatat setelah dibersihkan tidak lagi hilang.
- **Uji tidak lagi flaky** — uji API dan uji yang menimpa preferensi statis lain dijalankan berurutan dalam satu koleksi, sehingga suite penuh lolos konsisten di setiap putaran.
- **Gerbang `check-rilis`** diperbaiki — pembersih contoh kini dicari di seluruh `DatabaseInitializer*.cs`, bukan satu berkas.
- **Nama orang bergelar** tidak lagi ikut kapital penuh (desktop maupun Android).
- **NTCR** — nama pemohon/pejabat/orang tua yang kosong tercetak titik-titik sejajar blok pejabat.
- **Uji flaky Midtrans** — `Notifikasi_lunas_tercatat_sebagai_diterima` tidak lagi gagal 403 saat suite penuh.
- **Tombol Hubungkan di halaman Langganan** — setelah server lisensi lokal dinyalakan, katalog tidak lagi ditanya ke alamat yang berbeda (`localhost` vs `127.0.0.1`) sebelum server siap; gejala "lisensi server tidak terhubung" hilang dan perilkunya dikunci uji kontrak.
- **Sel kosong di PDF Android** — kunci `$kunci` di blok `badan` skema yang salah eja (mis. `$kematian.pukul`, `$ntcr.AyahCalonSuami`) dibetulkan sehingga kolom umur, tanggal, dan bin/binti orang tua NTCR terisi sama seperti cetakan desktop.

## Platform — .NET 11

- **Target framework naik ke `net11.0`** di seluruh proyek (desktop, server lisensi, backend API, alat) dengan paket `Microsoft.Extensions.*`, `Microsoft.Data.Sqlite.Core`, dan `Microsoft.AspNetCore.OpenApi` versi 11.0.0. Referensi paket yang kini menjadi bagian framework dibuang mengikuti *package pruning* NU1510.
- **SDK dikunci ke 11.0.100 stabil** di `global.json` (tanpa prarilis) dan CI menyetel versi yang sama; image runtime Docker server lisensi naik ke `aspnet:11.0`.
- **Analyzer WFO2000 (baru di .NET 11)** diperbaiki pada dua getter yang membuat instance baru setiap akses — keduanya kini menyimpan cache beridentitas stabil tanpa perubahan perilaku.

## Kualitas Kode — Hasil Audit Menyeluruh

- **Kode mati dihapus**: 30 resource XAML tak terpakai (token tema di sebelas berkas tema dihapus seragam, palet mockup, token padding/radius, gaya view), 2 converter beserta resource-nya, sisa metode mati di Core/Server/Wpf/SudesPdf.Core, 3 skrip alat yang tidak terpakai, dan paket `System.Security.Cryptography.Xml` yang tidak pernah dirujuk. Build tetap 0 peringatan, 0 galat.
- **Kode mati lanjutan**: 562 baris lagi dihapus pada putaran ulang — seluruh pemroses pesan WhatsApp di `FormatPermintaan` (kanal itu sudah tidak ada), 5 method `FileService` tanpa pemanggil, `PdfObjects` kosong, 2 kelas pengecualian tanpa rujukan, dan 5 konstanta ikon; katalog surat daring dipindah ke `KatalogSuratDaring` di tempat pemakainya.
- **Disiplin peringatan disamakan**: `TreatWarningsAsErrors` kini berlaku juga di `SudesPdf.Core`, `SudesExPlus`, dan `SuDesApp.Api`.
- **Login Google**: serialisasi token OAuth pindah ke `System.Text.Json` dengan konverter yang setia pada bentuk JSON bawaan Google — token lama tetap terbaca dan pengguna **tidak perlu login ulang**; disertai 5 uji kompatibilitas dua arah (termasuk skenario kembali ke versi lama).

## Uji dan Gerbang Rilis

- **Uji tema baru** `SetiapTemaDiLayanan_PunyaBerkasDanSebaliknya` menjaga katalog tema di `ThemeCatalog` selalu cocok dengan berkas `*Theme.xaml` — tema tanpa berkas atau berkas tanpa katalog kini membuat build gagal, bukan diam-diam jatuh ke tema bawaan.
- **Gerbang uji E2E trial server diperbaiki**: penyimpanan server kini per-putaran sehingga uji bisa dijalankan berulang (idempoten), dan harness mengenal format token `v2.<kid>.<payload>.<sig>` yang sebelumnya membuat pemeriksaan token meleset sejak key id disertakan.
- **Penjaga katalog surat daring** (6 uji) memastikan nama jenis yang dijanjikan dikenal sistem, daftar pengecualian tidak bocor, NTCR tidak pernah ditawarkan daring, dan setiap entri punya kata kunci.
- **Penjaga keselarasan catatan rilis** (3 uji) memastikan kartu teratas di aplikasi cocok dengan `AssemblyVersion` dan setiap dokumen rilis punya kartu.
- **Audit kontras tema kini berjalan di CI**: job *Audit kontras tema (WCAG)* menjalankan `Tools/check-kontras-wcag.ps1` pada setiap push, jadi tema baru yang melanggar paritas kunci atau kontras AA gagal build.
- **Dokumentasi**: `docs/backend-web.md` kini memuat tabel keputusan kapan memakai `ApiListener` (desktop), `SuDesApp.Api` (mobile/browser), atau `SuDesApp.Server` (lisensi) beserta status penerbitannya.

## Android

- Versi diselaraskan dengan desktop (`versionCode 21500` / `versionName 2.15.0`) tanpa perubahan kode.

## Android — Alat Build

- **AGP 8.7.3 → 9.3.0** dengan built-in Kotlin: plugin `org.jetbrains.kotlin.android` dan blok `kotlinOptions` dihapus (`jvmTarget` mengikuti `compileOptions.targetCompatibility`).
- **Gradle wrapper 8.11.1 → 9.8.2**; `gradlew`, `gradlew.bat`, dan wrapper jar dibangkitkan ulang memakai biner 9.8.2.
- **`compileSdk` 35 → 37** (`targetSdk` tetap 35), plugin Kotlin 2.0.21 → 2.2.10, `okhttp` 4.12.0 → 5.5.1, `kotlinx-coroutines-android` 1.8.1 → 1.11.0, `recyclerview` 1.3.2 → 1.4.0, `security-crypto` 1.1.0-alpha06 → 1.1.0.
- Kenaikan ini tidak bisa sebagian: coroutines 1.11.0 memaksa naikkan plugin Kotlin lebih dulu, dan okhttp 5.5.1 menuntut `compileSdk` 37 yang hanya didukung AGP 9.1+.

## Sinkron Android — Menu Baru dan Informasi Lebih Jelas

- **Tombol "Buat Kode" kembali ke halaman Sinkron Android**: selain Buat QR, operator kini bisa menerbitkan kode 4 angka yang diketik manual di aplikasi Android tanpa memindai QR; ikon tombol "Muat ulang" diganti ke ikon segar-ulang yang benar (dulu identik dengan "Impor ulang arsip gagal").
- **Tombol "Terbitkan ulang skema & kop"** ditambahkan di kartu Pengaturan Android — pemeriksaan ulang penerbitan skema formulir + kop surat ke Supabase tanpa menunggu timer latar 60 detik; status "sedang sinkron" kini menonaktifkan tombolnya dengan benar.
- Fitur Sinkron Android kini tercantum di menu Tentang: dua butir di kelompok "Layanan digital", kalimat pembuka kelompok diperluas, tiga kalimat di deskripsi Detail Lengkap, satu kelompok teknologi baru (Supabase, Npgsql, QRCoder), dan apresiasi open-source yang sesuai.
- **Gerbang versi saat sinkron**: desktop menerbitkan versinya bersama kop dan pola nomor (`pengaturan_desa`), dan Android menolak menarik data selama `BuildConfig.VERSION_NAME` berbeda — pesan muncul di layar masuk (menahan masuk ulang) dan sekali toast di menu utama. Versi desktop yang belum diketahui tidak memblokir, jadi perangkat lama tetap bisa bekerja sampai data pertama tiba; dijaga `PengaturanSinkronTest`.
- Harga sambungan tetap sama: QR/kode memuat URL dan kode saja, surel dan kata sandi perangkat tidak pernah tampil di layar.

## Formulir, Blanko, dan Lambang

- **Keterangan Penghasilan (472)**: `PenghasilanInputViewModel` + `PenghasilanInputView` mengikuti pola SKTM (data pemohon lengkap + keterangan + penandatangan) ditambah kartu Data Penghasilan; rincian disimpan sebagai JSON pada `SuratData.AdditionalData` sehingga isian tetap muncul saat diedit. `PenghasilanGenerator` mencetak bentuk sama dengan SKTM (kop, judul + nomor, tabel pejabat, tabel pemohon, tabel penghasilan, alinea keterangan, tanda tangan pemohon di kolom kiri).
- **Formulir pendataan perangkat desa** (`FormulirPerangkatGenerator`) kini berjamin satu halaman: susunan normal dipakai lebih dulu, bila belum muat diulang dengan jarak vertikal lebih rapat (1,0 → 0,8 → 0,6 → 0,45 → 0,3) sampai `PageCount == 1`; ukuran huruf tetap sama. Dijaga uji pada A4 maupun F4, termasuk kop yang tambah baris.
- **Surat Ijin Tinggal Sementara** memakai satu sumber teks (`IjinTinggalGenerator`) untuk bentuk alamat dan kalimat keterangan, dipakai bersama generator PDF dan ViewModel form — dulu keduanya membangun teks sendiri dengan format berbeda.
- **Lambang kabupaten**: `LogoKabupaten` kini membaca berkas pengguna lebih dulu, lalu arsip `LambangKabupaten.zip` (±20 MB, 514 lambang ≤512 px) yang diekstrak sekali per kode ke folder cache pengguna; penelepon tetap menerima jalur berkas seperti pola lama, jadi kop/pratinjau/UI tidak berubah.

## Halaman Lain — Hasil Audit

- **Laporan Penduduk** menambah tabel statistik **Pekerjaan** (urutan antara Pendidikan dan Agama): teks bebas dirapikan spasinya, tidak diisi masuk kantong "(tidak diisi)", urutan baris dari jumlah terbanyak; dapat dicetak ke PDF dan diekspor seperti tabel lainnya.
- **IDM**: empat baris pesan (galat/cadangan/peringatan/status) disatukan menjadi satu bilah pesan prioritas; combobox "Tahun laporan" baru membuat cetak PDF mengikuti tahun yang dipilih, bukan selalu tahun layar.
- **Dokumentasi**: pencarian judul/keterangan/jalur per kelompok dengan tombol bersihkan dan keadaan "Tidak ada dokumen yang cocok"; ikon kepala emoji Panduan Awal diganti ikon aplikasi.
- **Tombol pengaturan di Panduan Awal** kini membuka halaman pengaturan yang ASLI (instance yang sudah berisi data) dan menampilkan bagian yang tepat — dulu membuka form kosong dan melempar galat render.
- **Halaman Langganan** dipangkas mati (±350 baris XAML panel yang tak pernah tampil): hanya Status → Aktivasi Manual → Server Lisensi, dengan isian 36px yang seragam dan gaya label bersama.
- **Halaman Pembaruan** disusun sebagai dua kartu berdampingan (status & tindakan di kiri, rincian pembaruan di kanan) dengan gulir per kartu, bukan satu kartu sempit di tengah halaman.
- **Perbaikan batal login Google**: membatalkan di tab browser atau tombol Batal tidak lagi membuat tab Masuk mati permanen; status berubah netral "Login dibatalkan." dan tombol Masuk bisa dipakai lagi. Kunci OAuth diberi tenggang 10 menit sehingga tab yang ditutup tidak menggantung koneksi selamanya.
- **Riwayat Aktivitas**: “Bersihkan Riwayat” kini mengosongkan tabel seketika — penjaga `IsBusy` pada pemuat inti yang membuat muat ulang langsung kembali dilepaskan, dan antrean latar tidak lagi menulis ulang baris yang sudah dihapus.
- **NTCR**: nama pemohon/pejabat/orang tua yang kosong tercetak titik-titik sejajar blok pejabat pada seluruh blanko (N1–N6, N8) — sama seperti SKCK.
- **Tentang Aplikasi**: kartu Saweria dan butir donatur dihapus (tiga saluran komunikasi pengembang), deskripsi detail menyebut fitur terbaru, baris "Perusahaan induk"/"Repositori" disembunyikan, kartu pengembang menunjuk website sumberjaya.freedev.app.
- **Pratinjau kop di Pengaturan Surat** dibatasi tingginya (330 px) dan lebarnya mengikuti acuan kertas (600 px) supaya tidak menggeser formulir ke bawah.

## Android — Isian Dipecah per Kartu Seperti Desktop

- **Satu kartu per section, bukan satu daftar panjang**: halaman Buat Surat kini merender kartu berjudul (mis. KEMATIAN → DATA JENAZAH (PEMOHON), DATA PELAPOR, DATA KEMATIAN, KETERANGAN SURAT, KEPERLUAN SURAT, PEJABAT PENANDATANGAN; KENAL_LAHIR → DATA AYAH, DATA IBU, DATA ANAK; NTCR N1 → CALON MEMPELAI PRIA/WANITA, KOLOM KHUSUS BLANKO TERPILIH, DATA ORANG TUA / WALI), dengan kepala kartu bergaya sama seperti desktop (bar aksen hijau + judul kapital).
- **Judul dan urutan kartu tidak diketik dua kali**: `Tools/turunkan-grup.py` membaca section XAML desktop (`SuDesApp.Wpf/Views/*InputView.xaml` + `FormSectionTitle`) lalu menulisnya ke skema formulir sebagai `grupUrutan` per jenis dan `grup` per bidang; karena skema ikut diterbitkan ke Supabase, perubahan kartu di desktop tidak menuntut APK baru. Binding di dalam `<DataGrid>` diperhitungkan terhadap objek baris, sehingga kolom daftar anak tidak lagi merebut kolom tingkat atas.
- **Skema lama tetap aman**: cache `skema-cache.json` yang belum memuat `grup` dirender datar seperti sebelumnya (`SkemaJenis.grupkan()`), jadi perangkat yang belum tersinkron tidak kehilangan kolom.
- **Penjaga**: `SkemaGrupTest` (Android, urutan grup, grup asing, skema lama) dan uji paritas `Skema_MengandungGrupKartu_SepertiSectionDesktop` serta `Android_MembacaDanMerenderGrupSkema` (desktop) — keduanya menolak bila pengelompokan hilang atau berhenti dipakai.

## Langganan — Hubungan Server Lisensi

- **Tombol Hubungkan menunggu server siap**: `HubungkanAsync` menyalakan server lisensi lokal lebih dulu (sinkron), baru menanyakan katalog; sebelumnya permintaan katalog bisa berangkat mendahului server yang masih naik sehingga tombol tampak gagal padahal server sehat.

- **Satu bentuk alamat loopback**: server lokal dan `Lisensi:UrlServer` memakai `http://127.0.0.1:5110`. `PenentuAlamatLisensi` hanya mengunci alamat untuk HTTPS non-loopback, jadi konfigurasi loopback tidak bisa mengunci aplikasi ke alamat yang salah.
- **Dikunci supaya tidak rusak lagi**: `SuDesApp.Tests/Licensing/KontrakHubunganServerLisensiTests.cs` mengikat perilkaku (siapa menang antara konfigurasi, environment, dan server lokal), Berkas kritis diberi penanda "TERKUNCI" + read-only + `git update-index --assume-unchanged`, dan aturannya dicatat di `AGENTS.md`.
- **Server lisensi lokal wajib nyala kalau alamatnya loopback**: `App.Startup.MulaiServerLisensiLokal` sebelumnya hanya menurut preferensi `serverLisensiLokal`. Berkas preferensi (`%LOCALAPPDATA%\SuDesApp\login_prefs.json`) bisa hilang — profil baru, bersih-bersih, instalasi ulang — dan setelah itu aplikasi berhenti menyalakan servernya sendiri tanpa satu pun pesan: yang tersisa hanya chip "Belum terjangkau" dan tombol **Ubah alamat** yang menghilang. Keputusan kini diambil `PenentuAlamatLisensi.ServerLokalDibutuhkan(alamat, terkunci)`: alamat loopback yang tidak dikunci konfigurasi produksi selalu menyalakan server lokal, dan preferensinya dipulihkan otomatis.
- **Alasan kegagalan server terbaca di layar**: `ServerLisensiLokal` menyimpan beberapa baris keluaran server terakhir dan menempelkannya ke `GalatTerakhir`. Penolakan start seperti "ServerKey Midtrans belum diatur dan aplikasi tidak berjalan dalam mode Development" sekarang muncul di halaman Langganan, bukan terkubur di `server-lisensi\server.log` dengan pesan kosong "berhenti sendiri (kode 0)".
- **"Tombol Ubah hilang = server tidak menjawab" kini resmi**: tombol itu memang hanya tampil selama alamat terkunci oleh koneksi yang terbukti. Hubungan dua arah ini tidak lagi cuma kebetulan — dijaga `TombolUbahAlamatMunculSesudahServerMenjawab_DanHilangSesudahDibukaKunci`.

## Android — Surat Kembali Sama dengan Desktop

- **Menu Buat Surat Android kini memuat semua jenis 2.15.0, termasuk Keterangan Penghasilan (472)**: skema desktop diperluas dengan jenis PENGHASILAN (form data diri + pekerjaan/usaha, penghasilan per bulan, jumlah tanggungan), dan impor desktop memetakan `penghasilan.*` sehingga surat dari Android masuk register lengkap.
- **Generator PDF Android menyamai generator desktop**: blok-blok bespoke yang dulu hanya dicetak sebagai catatan kini dirender sungguhan — kepala blanko NTCR (LAMPIRAN IV/VI/VII/VIII/IX/X + Model N), blok KANTOR DESA/KELURAHAN, judul + Nomor/Perihal permohonan (N2/N3), blok Diterima KUA ↔ Wassalam/Pemohon, tanda tangan dua pihak (N4/N5), tanda tangan desa (N1/N6/N8), kotak "Kode : F-2.17" kematian, tabel rincian garapan 6 kolom berbingkai + baris Jumlah, serta Nomor/Perihal ↔ tujuan Bank/KCP Rekening Koran. Angka kerapatan menyusul `KerapatanSurat` termasuk ukuran font lampiran.
- **Nama orang bergelar**: gelar (Dr., Hj., S.H., …) tidak lagi ikut tercetak kapital — Android kini memakai perilaku `NamaFormatter.ToUpperNama` yang sama dengan desktop, di halaman surat maupun Daftar Hadir; disertai 4 uji unit.
- **Footer Daftar Hadir** memakai tanggal panjang bahasa Indonesia ("17 September 2026") seperti desktop.
- **Surat Keterangan Ahli Waris kini tiga halaman berurutan di Android** seperti desktop: Surat Keterangan (berkop, kaki Camat ↔ Kepala Desa), Surat Pernyataan Ahli Waris (daftar penanda tangan pasangan + tiap anak), dan Surat Kuasa (penerima kuasa = anak pertama, blok kaki Kepala Desa ↔ Camat). Page break antar halaman ikut salinan: halaman kuasa hanya muncul bila ada pasangan DAN anak, dan blok pasangan/anak hanya tercetak bila terisi (cabang `opsional`/`hanyaKosong` skema).
- **Rincian garapan bidang ke-2 dst. kini ditabulasi**: skema `rincianTambahan` menjadi daftar (letak, nomor persil, luas, keterangan), tiap baris masuk tabel PDF Android, jumlah luas otomatis menghitung semua bidang, dua baris kosong tetap ada seperti desktop.
- **Isian turunan dihitung Android, tidak digantungkan ke skema**: umur (`"<n> Tahun"`), tanggal cetak `dd MMMM yyyy` locale id-ID, dan pemetaan orang tua NTCR (`binBinti`, ayah/ibu calon suami-istri) dibangun `BuatSuratActivity` persis seperti generator C# desktop.
- **Impor desktop menahan bentuk data Android**: `SkemaFormulir` membuat objek antara yang null (mis. `wargaKK`) saat memasang target bertingkat, melepas tanda kutip JSON pada nilai string, dan menormalkan tanggal ke ISO `yyyy-MM-dd` — berkas properti `string` tanggal di database tetap satu bentuk untuk semua generator PDF.
- **Kolom yang tidak ada di desktop dihapus dari Garapan Sawah**: skema memuat `statusPerkawinan` padahal `GarapanInputView` tidak punya barisnya dan `GarapanGenerator` tidak mencetaknya — operator Android melihat satu dropdown yang desktop tidak pernah minta.
- **Tanggal lahir dicetak seperti desktop**: `GarapanGenerator` dan `IzinOrtuGenerator` memakai `dd-MM-yyyy` (bukan `dd MMMM yyyy` seperti surat lain), sedangkan pemilih tanggal Android menghasilkan `dd/MM/yyyy`. `BuatSuratActivity` kini menormalkan semua kunci tanggal lahir dengan `TanggalIndonesia.polaTanggalLahir(idJenis)`, jadi baris "Tempat Tgl lahir" Garapan dan Izin Orang Tua — dan baris tempat/tanggal surat lain — tidak lagi berbentuk garis miring.
- **Penjaga baru**: `Tools/turunkan-grup.py` ikut menerbitkan `Configuration/BidangFormDesktop.json` (kolom yang benar-benar ber-binding di XAML desktop) dan mode `--audit`; uji `SkemaTidakMenambahKolomDiLuarFormDesktop` menolak kolom asing, `PolaTanggalLahirAndroidSamaDenganGeneratorDesktop` mengikat pola tanggal ke generator aslinya, dan `TanggalLahirPolaTest` mengujinya di Android. Penjaga lama `Skema_jenis_ber_NIK_memuat_field_warga_lengkap` dilepas dari `statusPerkawinan` karena itulah yang memaksa kolom asing masuk skema.
- Uji unit Android diperluas menjadi 52 kasus.

## Instalasi Baru — Skema Database Tidak Ikut Terpasang

`desa.db.sql` adalah satu-satunya sumber skema yang dibaca `DatabaseInitializer` untuk membuat seluruh tabel. Pengecualian Inno `desa.db*` — yang maksudnya menahan basis data pengguna agar tidak tertimpa isi paket — juga menelan berkas skema itu, karena pola tersebut cocok dengan semua nama yang diawali `desa.db`.

Akibatnya pada setiap instalasi dari paket: `DatabaseInitializer` mencatat *"File schema SQL tidak ditemukan"*, database baru hanya berisi 8 tabel buatan kode (`Warga`, `JenisSurat`, `InfoDesa`, `Pengguna`, …), dan startup melapor `no such table: main.Surat` serta rentetan galat `no such table: IjinTinggal` dari `EnsureRequiredTableColumnsAsync`.

Pola kini dipecah menjadi `desa*.db` dan `desa*.db-*` (mencakup berkas samping `-wal`/`-shm`/`-journal` dan `desa_backup*.db`), jadi data desa tetap tidak pernah tertimpa sementara skema kembali ikut terpasang. Diperbaiki dengan `installer/SuDesApp.iss` + uji baru `SuDesApp.Tests/Configuration/InstallerSkemaDatabaseTests.cs` (8 uji) yang mengunci dua arah: menolak bila `desa.db.sql` tersaring, menolak bila `desa.db`/`desa.db-wal`/`desa_backup*.db` justru terpasang, memastikan csproj menyalin skema ke output, dan memeriksa `desa.db.sql` memuat seluruh tabel surat. Merah–hijau diverifikasi: dengan pola lama satu uji gagal, dengan pola baru semuanya lolos.

## Verifikasi

- `dotnet build SuDesApp.slnx -c Release` — 0 galat, 0 peringatan.
- `dotnet test SuDesApp.Tests` — **520 uji: 518 lolos**; 2 uji pemulihan basis data (`PemulihanDatabaseOtomatisTests`) bergantung pada lingkungan dan lolos begitu `desa.db` terpakai di folder instalasi disingkirkan sementara. Termasuk 6 uji gerbang rilis, 3 uji keselarasan catatan rilis, 152 uji berfilter `Licensing`, dan 9 uji paritas Buat Surat Android ↔ desktop.
- `gradlew testDebugUnitTest` (Android) — **52/52 lolos**.
- `Tools/check-rilis.ps1 -TanpaGitHub` — 9 lulus, 0 gagal, 4 dilewati.
- `Tools/check-resources.ps1 -FailOnOrphan` — 0 galat, 0 peringatan.
- `Tools/check-kontras-wcag.ps1` — semua pemeriksaan lolos (sebelas tema × 60 kunci konsisten).
- `Tools/uji-e2e-trial-server.sh` — SEMUA LULUS, terverifikasi pada dua putaran berturut.
- Skema Android (aset APK) identik dengan `Configuration/SkemaFormulir.json` yang diterbitkan desktop; jenis baru terbawa tanpa APK baru lewat penerbitan skema.
