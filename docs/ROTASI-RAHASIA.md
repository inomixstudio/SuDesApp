# Rotasi Rahasia Produksi

Lakukan segera bila kredensial pernah tampil di terminal, commit, artefak, atau dibagikan ke pihak lain.

1. Ganti password role aplikasi dan role migrasi Supabase; perbarui hanya melalui secret hosting atau `appsettings.lokal.json` yang di-ignore.
2. Buat ulang token setiap operator admin (`SUDES_ADMIN_OPERATORS__*__TOKEN`) dan cabut token lama.
3. Rotasi Midtrans Server Key di dashboard Midtrans dan perbarui secret hosting.
4. Buat pasangan kunci lisensi ES256 baru hanya bila kebocoran kunci privat dipastikan. Rilis desktop baru dengan kunci publik pengganti sebelum mematikan kunci lama.
5. Periksa deployment, Docker image, GitHub Actions log, dan artefak rilis agar tidak menyimpan file `appsettings.lokal.json` atau `keys/`.

Jangan menyimpan rahasia di issue, chat, commit, atau screenshot.
