# Cadangan dan Pemulihan Data Desa

Panduan satu halaman untuk petugas desa. **SuDesApp tidak mengirim data desa ke server mana pun** — seluruh data (warga, surat, arsip) tinggal di komputer desa ini.

## Di mana cadangan disimpan

1. **Cadangan harian lokal** — dibuat otomatis saat aplikasi ditutup, lewat `DatabaseBackupService` (`VACUUM INTO`, anti-rusak WAL). Lihat folder `Output/` / `desa_backup*.db` di folder instalasi, dan folder lokal aplikasi (`%LOCALAPPDATA%\SuDesApp`).
2. **Cadangan Google Drive** — bila Kredensial Google sudah diisi di Pengaturan, cadangan dibuat otomatis saat ditutup ke folder Drive yang dipilih.
3. **Berkas jejak sinkron** — setiap putaran sinkron Android menulis berkas `.xlsx` jejak di `%LOCALAPPDATA%\SuDesApp\Sinkron`.

## Cara memulihkan

1. Tutup SuDesApp sepenuhnya (pastikan ikon di taskbar/system tray sudah hilang).
2. Simpan salinan `desa.db` yang rusak saat ini ke nama lain (mis. `desa_rusak.db`) sebagai bukti — **jangan hapus asli sebelum cadangan baru terverifikasi**.
3. Salin berkas cadangan terbaru menjadi `desa.db` di folder instalasi (lokasi yang sama dengan berkas rusak).
4. Buka SuDesApp sekali. `PemulihanDatabaseOtomatis` akan memvalidasi berkas; bila rusak, ia membuat cadangan pra-restore otomatis sebelum mencoba lagi.
5. Periksa daftar surat & warga. Bila jumlahnya masuk akal, hapus salinan `desa_rusak.db` bila sudah tidak diperlukan.

## Bila komputer rusak / `desa.db` hilang total

- Gunakan cadangan Drive terbaru (unduh & simpan sebagai `desa.db` per langkah di atas).
- Tanpa cadangan apa pun, data harus diketik ulang — **tidak ada cara memulihkan dari server lisensi** (server hanya menyimpan masa aktif langganan, bukan data desa).
- Cadangkan juga folder `Templates/` (template surat yang sudah diedit petugas) — installer tidak menimpanya.

## Catatan batas waktu

Backup & pemasangan pembaruan saat aplikasi ditutup dibatasi ~20 detik. Jangan mematikan komputer (cabut listrik / paksa mati) tepat setelah menutup SuDesApp sampai proses benar-benar hilang — beri jeda beberapa detik.
