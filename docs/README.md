# Indeks Dokumentasi SuDesApp

Dokumen di folder ini menjelaskan kebijakan, arsitektur, API, dan riwayat rilis
SuDesApp. Kembali ke [README utama](../README.md).

## Produk dan alur kerja

| Dokumen | Isi |
|---|---|
| [alur-persetujuan-surat.md](alur-persetujuan-surat.md) | Alur opsional ajukan → verifikasi → tanda tangan per surat |
| [perangkat-desa-dan-sk.md](perangkat-desa-dan-sk.md) | Struktur data perangkat desa, SK, ekspor, dan cetak |
| [apbdes-arsip-dokumen.md](apbdes-arsip-dokumen.md) | Arsip dokumen APBDes per tahun dan tahapan |
| [idm-laporan-pdf.md](idm-laporan-pdf.md) | Cetak dan ekspor laporan IDM |
| [idm-tata-letak.md](idm-tata-letak.md) | Tata letak halaman IDM |
| [indeks-desa-2025.md](indeks-desa-2025.md) | Sumber resmi Indeks Desa 2025 |
| [sinkronisasi-android.md](sinkronisasi-android.md) | Alur sinkronisasi Android → desktop |
| [pengumuman-aplikasi.md](pengumuman-aplikasi.md) | Pengumuman broadcast ke seluruh instalasi desa |
| [sumber-formulir-daerah.md](sumber-formulir-daerah.md) | Sumber formulir administrasi kependudukan per kabupaten |
| [referensi-penomoran-surat.md](referensi-penomoran-surat.md) | Penomoran surat Perbup Karawang |
| [referensi-penomoran-surat-bekasi.md](referensi-penomoran-surat-bekasi.md) | Penomoran surat Kabupaten Bekasi |

## Teknis dan arsitektur

| Dokumen | Isi |
|---|---|
| [api-desa.md](api-desa.md) | API desa lokal: endpoint, kunci API, dan verifikasi surat |
| [backend-web.md](backend-web.md) | Backend `SuDesApp.Api` untuk klien mobile/browser |
| [arsitektur-pdf-surat.md](arsitektur-pdf-surat.md) | Lapisan pembuatan PDF surat: kop, renderer, generator |
| [tema-dan-warna.md](tema-dan-warna.md) | Token warna, tema, dan aturan kontras WCAG |
| [PEMBARUAN-TAMBALAN.md](PEMBARUAN-TAMBALAN.md) | Mekanisme pembaruan kecil (tambalan) vs installer penuh |
| [TANDA-TANGAN-RILIS.md](TANDA-TANGAN-RILIS.md) | Tanda tangan Authenticode untuk aset rilis |
| [audit-mutu-2026-10-28.md](audit-mutu-2026-10-28.md) | Hasil audit mutu aplikasi |
| [audit-perubahan-github.md](audit-perubahan-github.md) | Periksa perubahan lokal sebelum unggah ke GitHub |

## Lisensi, server, dan operasi

| Dokumen | Isi |
|---|---|
| [langganan-dan-lisensi.md](langganan-dan-lisensi.md) | Model langganan, token ECDSA, dan masa tenggang |
| [SETUP-LANGGANAN-DAN-ADMIN.md](SETUP-LANGGANAN-DAN-ADMIN.md) | Menyiapkan menu Langganan dan Pusat Administrasi dengan Supabase |
| [deploy-server.md](deploy-server.md) | Deploy server lisensi ke Railway/Render/VPS |
| [ROTASI-RAHASIA.md](ROTASI-RAHASIA.md) | Prosedur rotasi rahasia produksi |
| [cadangan-dan-pemulihan.md](cadangan-dan-pemulihan.md) | Pencadangan Google Drive dan pemulihan database |

## Kebijakan dan privasi

| Dokumen | Isi |
|---|---|
| [kebijakan-privasi.md](kebijakan-privasi.md) | Data apa yang lokal, apa yang dikirim ke server, dan hak subjek data |
| [syarat-ketentuan.md](syarat-ketentuan.md) | Syarat layanan, lisensi, pembayaran, dan tanggung jawab |
| [policy-peran-pengguna.md](policy-peran-pengguna.md) | Peran, izin, dan pengelolaan akun |
| [policy-tutup-buku-dan-retensi.md](policy-tutup-buku-dan-retensi.md) | Tutup buku tahunan dan retensi arsip |

## Catatan rilis

| Versi | Dokumen |
|---|---|
| 2.15.1 | [catatan-rilis-2.15.1.md](catatan-rilis-2.15.1.md) |
| 2.15.0 | [catatan-rilis-2.15.0.md](catatan-rilis-2.15.0.md) |
| 2.14.0 | [catatan-rilis-2.14.0.md](catatan-rilis-2.14.0.md) |
| 2.13.2 | [catatan-rilis-2.13.2.md](catatan-rilis-2.13.2.md) |
| 2.13.1 | [catatan-rilis-2.13.1.md](catatan-rilis-2.13.1.md) |
| 2.13.0 | [catatan-rilis-2.13.0.md](catatan-rilis-2.13.0.md) |
| 2.12.1 | [catatan-rilis-2.12.1.md](catatan-rilis-2.12.1.md) |
| 2.12.0 | [catatan-rilis-2.12.0.md](catatan-rilis-2.12.0.md) |
| 2.11.0 | [catatan-rilis-2.11.0.md](catatan-rilis-2.11.0.md) |
| 2.10.5 | [catatan-rilis-2.10.5.md](catatan-rilis-2.10.5.md) |
| 2.10.4 | [catatan-rilis-2.10.4.md](catatan-rilis-2.10.4.md) |
| 2.10.3 | [catatan-rilis-2.10.3.md](catatan-rilis-2.10.3.md) |

Catatan rilis terbaru juga tersedia di halaman
[Releases](https://github.com/inomixstudio/SuDesApp/releases).

## Dokumen di akar repositori

| Dokumen | Isi |
|---|---|
| [README.md](../README.md) | Ringkasan produk, unduhan, build, dan lisensi |
| [SECURITY.md](../SECURITY.md) | Cara melaporkan kerentanan dan desain keamanan |
| [CONTRIBUTING.md](../CONTRIBUTING.md) | Alur kontribusi, build, dan daftar periksa PR |
| [CODE_OF_CONDUCT.md](../CODE_OF_CONDUCT.md) | Aturan komunitas |
| [AGENTS.md](../AGENTS.md) | Aturan teknis internal dan area yang dijaga uji |
| [PANDUAN-GITHUB-ACTIONS.md](../PANDUAN-GITHUB-ACTIONS.md) | Workflow CI dan rilis |
