# IDM: cetak & ekspor laporan PDF

Tombol **Cetak / PDF** di halaman **Indeks Desa & IDM** membuat satu berkas PDF
berisi **ringkasan penilaian + seluruh rincian indikator** seri IDM yang sedang
tampil di layar, lalu membukanya di halaman pratinjau PDF yang sama dengan
dokumen lain — di sana tombol **Cetak** dan **Ekspor** sudah tersedia (Ekspor
menyimpan salinan ke lokasi pilihan, mis. folder lampiran laporan).

Laporan ini khusus seri **IDM lama (IKS/IKE/IKL)**. Penilaian **Indeks Desa 2025**
(enam dimensi, lihat `docs/indeks-desa-2025.md`) tampil di halaman sebagai tahun
utama, tetapi tidak dicetak di laporan IDM karena susunannya berbeda. Untuk desa
yang hanya punya penilaian 2025, tombol Cetak/PDF **nonaktif** dan tooltip-nya
menjelaskan sebabnya (`TooltipCetak` di `IdmViewModel`).

## Yang dicetak

| Bagian | Isi |
|---|---|
| Kepala | Judul, "Desa … | Kec. … | Kab. … | Prov. …", tahun penilaian, kode desa |
| Ringkasan | Skor IDM, status desa, IKS/IKE/IKL, target pusat, skor minimal target, tahun penilaian, kode desa |
| Catatan target | Kalimat jarak ke status berikutnya (angkanya dari pusat: skor minimal / penambahan) |
| Tren tahun | Tahun-tahun yang diterbitkan pusat, terbaru dulu |
| Rincian indikator | Nomor, bidang, indikator, skor, keterangan pusat, kegiatan yang disarankan, pihak — baris subtotal resmi ditebalkan |
| Tanda tangan | Mengetahui Kepala Desa & Sekretaris Desa, bertanggal (nama diambil dari Pengaturan Surat) |
| Kaki halaman | Sumber `idm.kemendesa.go.id` + waktu pengambilan, dan "Hal. x dari y" |

Kertas **A4 mendatar** karena tabel rincian memuat tujuh kolom; pada kertas
tegak kolomnya terpotong atau membungkus menjadi baris yang sangat tinggi.

## Sumber angka

Seluruh angka disalin apa adanya dari data pusat yang sudah dimuat halaman IDM
(termasuk data cadangan saat internet mati). Aplikasi **tidak menghitung ulang**
skor, status, maupun target — jadi angka di kertas, di layar, dan di situs
kementerian selalu sama. Baris sumber mencantumkan waktu pengambilan agar
pembaca tahu angkanya bukan angka hari ini.

Format teks (angka koma, status kapital, kalimat target, baris sumber) dipusatkan
di `SuDesApp.Core/Services/IdmTeks.cs` sehingga layar dan PDF tidak bisa
menyimpang satu sama lain.

## Kabar data resmi pusat (tahun yang belum dimuat portal)

Portal IDM bisa tertinggal dari penetapan resmi. Keadaan yang dijumpai pada
Oktober 2026: portal hanya memuat seri IDM lama sampai **2024**, sedangkan
status desa **2025** sudah ditetapkan Menteri Desa dan PDT lewat **Kepmendesa
PDT No. 343 Tahun 2025** (27 Agustus 2025, JDIH Kemendesa PDT). Penilaian 2025
tidak diambil dari portal IDM, melainkan dari dataset resmi **Indeks Desa 2025**
(Satu Data Kemendesa) — lihat `docs/indeks-desa-2025.md`. Halaman **tidak**
memakai unggahan dataset daerah di data.go.id (mis. Kabupaten Nganjuk) karena
cakupan dan angkanya bukan angka pusat untuk desa pengguna.

Yang tampil sebagai gantinya: kartu **Kabar data resmi pusat** berisi judul
dokumen resmi 2025 + tautan halaman JDIH-nya, dengan penjelasan bahwa skor seri
IDM yang tampil adalah tahun terbit terakhir portal. Daftar dokumen resmi
beserta tautannya dipusatkan di `SuDesApp.Core/Services/IdmTerbitanResmi.cs`;
menambah terbitan tahun berikutnya cukup menambah satu baris di sana (hanya
dokumen pemerintah pusat).



## Pemberitahuan data IDM tahun baru

Halaman IDM mengambil data hanya saat dibuka, jadi pusat yang menerbitkan
tahun baru di tengah sesi tidak akan terlihat sampai halamannya dibuka lagi.
`SuDesApp.Wpf/Services/PemantauIdmBaru.cs` menutup celah itu: **45 detik**
setelah jendela utama tampil, lalu mengulang sesuai pilihan pengguna, layanan
itu membandingkan tahun terbaru pada cadangan IDM lokal (hasil muat terakhir
halaman) dengan jawaban portal — memeriksa dari tahun berjalan mundur sampai
satu tahun di atas tahun tersimpan.

Intervalnya diatur di **Pengaturan Aplikasi → Umum & Preferensi → Pemantauan
Data IDM** (`IntervalPemantauanIdmPrefs`):

| Pilihan | Perilaku |
|---|---|
| **Setiap 6 jam** | Bawaan — pemeriksaan pertama 45 detik setelah aplikasi dibuka, lalu tiap 6 jam selama aplikasi terbuka |
| **Setiap 24 jam** | Sama, tetapi diulang sekali sehari (lebih hemat kuota) |
| **Hanya saat aplikasi dibuka** | Satu pemeriksaan setelah aplikasi dibuka, tanpa pengulangan; halaman IDM tetap bisa memuat data lewat tombol **Muat ulang** |

Pilihan tersimpan langsung di preferensi aplikasi dan langsung diterapkan ke
timer pemantau — tanpa menutup aplikasi. Pemasangan lama yang belum pernah
memilih tetap memakai 6 jam, jadi pembaruan aplikasi tidak mengubah perilaku.

Kalau ada tahun yang lebih baru, notifikasi lonceng muncul:

> **Data IDM tahun 2026 sudah terbit** — Portal pusat kini memuat penilaian IDM
tahun 2026 — yang tersimpan di perangkat masih tahun 2024. Buka halaman IDM
untuk memuat angka terbaru.

Notifikasi itu bisa diklik (`TujuanMenu = "idm"`) dan langsung membuka halaman
IDM, yang memuat ulang seluruh tahun dari pusat (termasuk riwayat tren).

Setiap pemeriksaan yang diselesaikan portal — oleh halaman maupun oleh pemantau
— dicatat di `%LocalAppData%\SuDesApp\idm-pemantauan.json`
(`SuDesApp.Core/Services/IdmPemantauanStore.cs`): waktu periksa dan tahun
terbit terbaru. Halaman memakai catatan itu untuk **indikator data terbaru**:

> **Data terbaru yang terbit: 2024** · Diperiksa: 18/10/2026 07.12

Bila yang tampil adalah data cadangan dan catatan menunjukkan tahun yang lebih
baru, kalimatnya berubah menjadi ajakan muat ulang (mis. "halaman ini masih
memuat tahun 2024 (data cadangan), muat ulang untuk memperbarui"). Memuat
cadangan tidak pernah menimpa catatan pemeriksaan — hanya jawaban portal yang
boleh memperbaruinya.

Batas yang disengaja:

- pemantauan hanya berjalan bila sudah ada cadangan untuk desa yang **sedang
dipilih** — tanpa tahun pembanding, "tahun baru" tidak bisa ditentukan;
  kode desa aktif diselesaikan lewat `SuDesApp.Wpf/Utilities/IdmDesaAktif.cs`,
  sumber yang sama dengan status bar IDM;
- pemantau **tidak** menulis cadangan dan tidak mengubah halaman — halaman yang
  memuat ulang tetap satu-satunya jalur data segar ke layar;
- portal yang tidak terjangkau bukan galat: putaran itu diam dan pemeriksaan
  berikutnya mencoba lagi;
- satu notifikasi per desa+tahun per sesi, walau pemeriksaan berkala berulang.

Uji: `SuDesApp.Tests/PemantauIdmBaruTests.cs` (tahun berjalan langsung ketemu,
hanya tahun di antara yang terbit, belum ada tahun baru, portal tidak
terjangkau, tanpa cadangan, cadangan desa lain, tidak mengulang notifikasi,
serta catatan pemeriksaannya) dan uji indikator di
`SuDesApp.Tests/IdmPusatTests.cs` (catatan ditulis saat memuat portal, dipakai
tanpa ditimpa saat cadangan, tersembunyi saat tidak ada data, dan bertahan di
berkas).

## Grafik tren di layar

Halaman IDM memuat kartu **TREN PENILAIAN**: garis skor IDM (tebal, berlabel
angka) plus IKS/IKE/IKL untuk tahun-tahun yang diterbitkan pusat — daftar tahun
yang SAMA dengan chip "Tahun lain", jadi grafik tidak pernah menampilkan angka
berbeda dari baris ringkasan. Grafik hanya menggambar, tidak menghitung.

Penggambaran dilakukan kontrol `SuDesApp.Wpf/Controls/GrafikTrenIdm.cs`
(tanpa pustaka grafik): warna garis diambil lewat kuas bertema
(DynamicResource) sehingga ikut berganti saat pengguna menukar tema, dan label
angka memakai keping latar kartu supaya tetap terbaca saat melewati garis.
IKE bergaris putus-putus dan IKL titik-titik untuk membedakan seri walau dua
tema memberi warna senada. Grafik ini tampil di LAYAR saja — PDF tetap
mencetak angka tahun dalam bentuk baris teks seperti tabel di atas.

Uji: `SuDesApp.Tests/GrafikTrenIdmTests.cs` (deret dari riwayat, render tanpa
galat pada data kosong/satu tahun/seri kosong, warna ikut tema, tampilan
memakai kontrol).

## Berkas & masa hidupnya

PDF ditulis ke folder **TempPDF** yang dikonfigurasi aplikasi
(`AppConfig.TempPdfFolder`), dengan nama
`Laporan_IDM_<Desa>_<Tahun>_<yyyyMMdd>.pdf`; berkas di folder itu dibersihkan
`TempPdfCleanup` seperti berkas surat. Menyimpan permanen lewat **Ekspor** di
halaman pratinjau.

Tombol nonaktif selama belum ada data yang dimuat (data desa belum diisi, desa
belum dikenali dari data wilayah, atau situs pusat tidak terjangkau tanpa
cadangan). Tanpa data pun laporan tetap bisa dibuat sebagai satu halaman
keterangan — berguna saat menjelaskan ke pembaca mengapa tidak ada angkanya.

## Uji

`SuDesApp.Tests/IdmCetakPdfTests.cs` menjaga:

- angka pusat disalin apa adanya (format koma, status kapital, penambahan dari pusat),
- seluruh baris indikator ikut tercetak, termasuk baris subtotal,
- PDF terbentuk (`%PDF`) dan 60 indikator terbagi ke beberapa halaman tanpa galat tata letak,
- tombol Cetak nonaktif sebelum ada data, lalu membuka pratinjau dengan berkas
  di folder TempPDF aplikasi.
