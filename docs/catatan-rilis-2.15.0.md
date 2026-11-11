# SuDesApp 2.15.0 — 11 November 2026

Rilis pemeliharaan: migrasi ke .NET 11, pembersihan kode mati hasil audit menyeluruh, token login Google memakai serialisasi bawaan platform, dan gerbang uji E2E server lisensi diperbaiki.

Tidak ada perubahan format data: `desa.db`, `Templates`, token Google yang sudah tersimpan, dan arsip surat lama dipakai apa adanya.

## Platform — .NET 11

- **Target framework naik ke `net11.0`** di seluruh proyek (desktop, server lisensi, backend API, alat) dengan paket `Microsoft.Extensions.*`, `Microsoft.Data.Sqlite.Core`, dan `Microsoft.AspNetCore.OpenApi` versi 11.0.0. Referensi paket yang kini menjadi bagian framework dibuang mengikuti *package pruning* NU1510.
- **SDK dikunci ke 11.0.100 stabil** di `global.json` (tanpa prarilis) dan CI menyetel versi yang sama; image runtime Docker server lisensi naik ke `aspnet:11.0`.
- **Analyzer WFO2000 (baru di .NET 11)** diperbaiki pada dua getter yang membuat instance baru setiap akses — keduanya kini menyimpan cache beridentitas stabil tanpa perubahan perilaku.

## Kualitas Kode — Hasil Audit Menyeluruh

- **Kode mati dihapus**: 30 resource XAML tak terpakai (token tema di sebelas berkas tema dihapus seragam, palet mockup, token padding/radius, gaya view), 2 converter beserta resource-nya, sisa metode mati di Core/Server/Wpf/SudesPdf.Core, 3 skrip alat yang tidak terpakai, dan paket `System.Security.Cryptography.Xml` yang tidak pernah dirujuk. Build tetap 0 peringatan, 0 galat.
- **Disiplin peringatan disamakan**: `TreatWarningsAsErrors` kini berlaku juga di `SudesPdf.Core`, `SudesExPlus`, dan `SuDesApp.Api`.
- **Login Google**: serialisasi token OAuth pindah ke `System.Text.Json` dengan konverter yang setia pada bentuk JSON bawaan Google — token lama tetap terbaca dan pengguna **tidak perlu login ulang**; disertai 5 uji kompatibilitas dua arah (termasuk skenario kembali ke versi lama).

## Uji dan Gerbang Rilis

- **Uji tema baru** `SetiapTemaDiLayanan_PunyaBerkasDanSebaliknya` menjaga katalog tema di `ThemeCatalog` selalu cocok dengan berkas `*Theme.xaml` — tema tanpa berkas atau berkas tanpa katalog kini membuat build gagal, bukan diam-diam jatuh ke tema bawaan.
- **Gerbang uji E2E trial server diperbaiki**: penyimpanan server kini per-putaran sehingga uji bisa dijalankan berulang (idempoten), dan harness mengenal format token `v2.<kid>.<payload>.<sig>` yang sebelumnya membuat pemeriksaan token meleset sejak key id disertakan.
- **Dokumentasi**: `docs/backend-web.md` kini memuat tabel keputusan kapan memakai `ApiListener` (desktop), `SuDesApp.Api` (mobile/browser), atau `SuDesApp.Server` (lisensi) beserta status penerbitannya.

## Android

- Versi diselaraskan dengan desktop (`versionCode 21500` / `versionName 2.15.0`) tanpa perubahan kode.

## Verifikasi

- `dotnet build SuDesApp.slnx -c Release` — 0 galat, 0 peringatan.
- `dotnet test SuDesApp.Tests` — **449/449 lolos** (termasuk 6 uji baru).
- `Tools/uji-e2e-trial-server.sh` — SEMUA LULUS, terverifikasi pada dua putaran berturut.
- `Tools/check-kontras-wcag.ps1` — semua pemeriksaan lolos (sebelas tema × 60 kunci konsisten).
