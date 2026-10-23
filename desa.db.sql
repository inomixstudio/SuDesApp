PRAGMA journal_mode = WAL;
BEGIN TRANSACTION;
CREATE TABLE IF NOT EXISTS "Garapan" (
    "ID_GarapanItem"    INTEGER PRIMARY KEY AUTOINCREMENT,
    "ID_Surat"          INTEGER NOT NULL,                  
    "Luas"              REAL,                            
    "Lokasi"            TEXT,
    "PemilikTanah"      TEXT,                            
    "NomorPersil"       TEXT,
    "KeteranganGarapan" TEXT,                            
    FOREIGN KEY("ID_Surat") REFERENCES "Surat"("ID_Surat") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "IZIN" (
    "ID_Surat" INTEGER PRIMARY KEY,
    "ID_Warga_Anak" INTEGER NOT NULL,  -- Lebih baik gunakan ID_Warga sebagai FK
    "NegaraTujuan" TEXT NOT NULL,
    "NamaPT" TEXT,
    FOREIGN KEY("ID_Surat") REFERENCES "Surat"("ID_Surat") ON DELETE CASCADE,
    FOREIGN KEY("ID_Warga_Anak") REFERENCES "Warga"("ID_Warga") ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS "InfoDesa" (
    "NamaDesa" TEXT NOT NULL,
    "Kecamatan" TEXT NOT NULL,
    "Kabupaten" TEXT NOT NULL,
    "Alamat" TEXT NOT NULL,
    "Kodepos" TEXT NOT NULL,
    "KepalaDesa" TEXT,
    "SekretarisDesa" TEXT,
    "Email" TEXT,
    "NamaCamat" TEXT,
    "NipCamat" TEXT,
    "GolCamat" TEXT,
    -- Jenis wilayah tingkat kabupaten/kota ("Kabupaten"/"Kota") dari pilihan
    -- berjenjang di Pengaturan Surat. Kosong/NULL untuk data desa lama.
    "JenisKabupaten" TEXT,
    -- Kode Kemendagri tingkat kabupaten/kota (mis. "32.15"). Dipakai untuk
    -- mencari lambang kabupaten secara deterministik tanpa pencocokan nama.
    -- Kosong/NULL untuk data desa lama yang belum memilih dari daftar wilayah.
    "KodeKabupaten" TEXT,
    UNIQUE("NamaDesa","Kecamatan","Kabupaten")
);

CREATE TABLE IF NOT EXISTS "JenisSurat" (
	"ID_Jenis"	INTEGER,
	"NamaJenis"	TEXT NOT NULL UNIQUE,
	"KodeJenis"	TEXT,
	PRIMARY KEY("ID_Jenis" AUTOINCREMENT)
);
CREATE TABLE IF NOT EXISTS "Kematian" (
	"ID_Surat"	INTEGER,
	"HariKematian"	TEXT,
	"TanggalKematian"	TEXT,
	"PukulKematian"	TEXT,
	"PenyebabKematian"	TEXT,
	"TempatKematian"	TEXT,
	"NIKPelapor"	TEXT,
	"NamaPelapor"	TEXT,
	"AgamaPelapor"	TEXT CHECK("AgamaPelapor" IN ('Islam', 'Kristen', 'Katolik', 'Hindu', 'Buddha', 'Konghucu')),
	"UmurPelapor"	TEXT,
	"PekerjaanPelapor"	TEXT,
	"AlamatPelapor"	TEXT,
	"HubunganPelapor"	TEXT,
	PRIMARY KEY("ID_Surat"),
	FOREIGN KEY("ID_Surat") REFERENCES "Surat"("ID_Surat") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "SKU" (
	"ID_Surat"	INTEGER,
	"BidangUsaha"	TEXT,
	"SejakTahun"	TEXT,
	PRIMARY KEY("ID_Surat"),
	FOREIGN KEY("ID_Surat") REFERENCES "Surat"("ID_Surat") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "SKTM" (
	"ID_Surat"	INTEGER,
	"KeteranganKemiskinan"	TEXT,
	"PenghasilanPerBulan"	REAL CHECK("PenghasilanPerBulan" >= 0),
	"JumlahTanggungan"	INTEGER CHECK("JumlahTanggungan" >= 0),
	PRIMARY KEY("ID_Surat"),
	FOREIGN KEY("ID_Surat") REFERENCES "Surat"("ID_Surat") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "Instansi" (
    "ID_Surat" INTEGER PRIMARY KEY,
    "NamaInstansi" TEXT NOT NULL,
    "AlamatInstansi" TEXT,
    "PimpinanInstansi" TEXT,
    "NomorSkIzinUsaha" TEXT,
    "NikPimpinan" TEXT,
    "TeleponInstansi" TEXT,
    FOREIGN KEY("ID_Surat") REFERENCES "Surat"("ID_Surat") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "Surat" (
    "ID_Surat" INTEGER PRIMARY KEY AUTOINCREMENT,
    "ID_Jenis" INTEGER NOT NULL,
    "ID_Warga" INTEGER,
    "NomorSurat" TEXT NOT NULL UNIQUE,
    "TanggalSurat" DATE NOT NULL,
    "Keterangan" TEXT,
	"Keperluan" TEXT,
	-- Alur persetujuan opsional + keaslian surat (semuanya NULL sampai dipakai).
	"StatusPersetujuan" TEXT,
	"VerifikasiOleh" TEXT,
	"VerifikasiPada" TEXT,
	"DitandatanganiOleh" TEXT,
	"DitandatanganiPada" TEXT,
	"CatatanPersetujuan" TEXT,
	"FileScanSurat" TEXT,
	"JumlahCetak" INTEGER NOT NULL DEFAULT 0,
	"KodeVerifikasi" TEXT,
	"HashVerifikasi" TEXT,
    FOREIGN KEY("ID_Jenis") REFERENCES "JenisSurat"("ID_Jenis"),
    FOREIGN KEY("ID_Warga") REFERENCES "Warga"("ID_Warga") ON DELETE SET NULL
);
CREATE INDEX IF NOT EXISTS "idx_surat_kodeverifikasi" ON "Surat"("KodeVerifikasi");

CREATE TABLE IF NOT EXISTS "Warga" (
	"ID_Warga"	INTEGER,
	"NIK"	TEXT NOT NULL UNIQUE,
	"Nama"	TEXT NOT NULL,
	"TempatLahir"	TEXT,
	"TanggalLahir"	TEXT,
	"JenisKelamin"	TEXT,
	"Agama"	TEXT,
	"StatusPerkawinan"	TEXT,
	"Pekerjaan"	TEXT,
	"Alamat"	TEXT,
	"Pendidikan"	TEXT,
	"Kewarganegaraan"	TEXT,
	-- Kolom wilayah. Repository (WargaRepository) menulis kolom ini pada
	-- INSERT/UPDATE, jadi harus ada sejak database pertama dibuat — bukan
	-- hanya lewat migrasi. Database lama tetap dilayani oleh
	-- WargaRepository.EnsureWargaSchemaAsync.
	"Dusun"	TEXT,
	"RT"	TEXT,
	"RW"	TEXT,
	"Desa"	TEXT,
	"Kecamatan"	TEXT,
	"Kabupaten"	TEXT,
	-- Dipakai rekap demografi dan kolom isi lampiran SK.
	"GolonganDarah"	TEXT,
	"NomorHP"	TEXT,
	"StatusWarga"	TEXT,
	-- Data lengkap warga: orang tua, KK, detail alamat, catatan status.
	"NamaAyah"	TEXT,
	"NamaIbu"	TEXT,
	"NoKK"	TEXT,
	"AlamatDetail"	TEXT,
	"TanggalStatus"	TEXT,
	"KeteranganWarga"	TEXT,
	-- Kedudukan dalam keluarga (Kepala Keluarga/Istri/Anak) — ditulis
	-- WargaRepository pada INSERT/UPDATE, jadi harus ada sejak awal.
	"StatusKeluarga"	TEXT,
	"CreatedAt"	DATETIME DEFAULT CURRENT_TIMESTAMP,
	"UpdatedAt"	DATETIME DEFAULT CURRENT_TIMESTAMP,
	PRIMARY KEY("ID_Warga" AUTOINCREMENT)
);
CREATE INDEX IF NOT EXISTS "idx_warga_nik" ON "Warga"("NIK");
CREATE TRIGGER IF NOT EXISTS "tr_warga_updated" AFTER UPDATE ON "Warga"
FOR EACH ROW BEGIN
	UPDATE "Warga" SET "UpdatedAt" = CURRENT_TIMESTAMP WHERE "ID_Warga" = NEW."ID_Warga";
END;
-- Data contoh warga TIDAK disuntik lagi: instalasi baru mulai dengan data warga kosong.
-- Baris contoh lama ('NAMA ORANG TUA' / NIK 1234567890123456) pada database lama
-- dibersihkan DatabaseInitializer.BersihkanWargaContohAsync saat aplikasi dibuka.

-- Gunakan INSERT OR IGNORE untuk menghindari error duplikasi
-- Gunakan daftar kolom eksplisit agar tetap valid walau tabel JenisSurat
-- sudah memiliki kolom tambahan (mis. Deskripsi) hasil migrasi.
INSERT OR IGNORE INTO "JenisSurat" (ID_Jenis, NamaJenis, KodeJenis) VALUES (1,'SKD_UMUM','SKD');
INSERT OR IGNORE INTO "JenisSurat" (ID_Jenis, NamaJenis, KodeJenis) VALUES (2,'DOMISILI_WARGA','DOM_WRG');
INSERT OR IGNORE INTO "JenisSurat" (ID_Jenis, NamaJenis, KodeJenis) VALUES (3,'INSTANSI','DOM_INS');
INSERT OR IGNORE INTO "JenisSurat" (ID_Jenis, NamaJenis, KodeJenis) VALUES (4,'SKU','SKU');
INSERT OR IGNORE INTO "JenisSurat" (ID_Jenis, NamaJenis, KodeJenis) VALUES (5,'PENGANTAR_SKCK','SKCK');
INSERT OR IGNORE INTO "JenisSurat" (ID_Jenis, NamaJenis, KodeJenis) VALUES (6,'IZIN_ORTU','IZIN');
INSERT OR IGNORE INTO "JenisSurat" (ID_Jenis, NamaJenis, KodeJenis) VALUES (7,'GARAPAN_SAWAH','GRP_SAW');
INSERT OR IGNORE INTO "JenisSurat" (ID_Jenis, NamaJenis, KodeJenis) VALUES (8,'KEMATIAN','KEM');
INSERT OR IGNORE INTO "JenisSurat" (ID_Jenis, NamaJenis, KodeJenis) VALUES (9,'SKTM','SKTM');
INSERT OR IGNORE INTO "JenisSurat" (ID_Jenis, NamaJenis, KodeJenis) VALUES (10,'BEDANAMA','BEDANAMA');
INSERT OR IGNORE INTO "JenisSurat" (ID_Jenis, NamaJenis, KodeJenis) VALUES (22,'PENGANTAR_ADMINDUK','PAM');
INSERT OR IGNORE INTO "JenisSurat" (ID_Jenis, NamaJenis, KodeJenis) VALUES (23,'SK_PINDAH','PND');
INSERT OR IGNORE INTO "JenisSurat" (ID_Jenis, NamaJenis, KodeJenis) VALUES (24,'SK_TIDAK_PUNYA_RUMAH','TPR');
INSERT OR IGNORE INTO "JenisSurat" (ID_Jenis, NamaJenis, KodeJenis) VALUES (25,'IZIN_KERAMAIAN','IKR');

CREATE TABLE IF NOT EXISTS "AhliWaris" (
    "ID_AhliWaris" INTEGER PRIMARY KEY AUTOINCREMENT,
    "ID_Surat" INTEGER NOT NULL,
    "NamaWaris" TEXT,
    "NIKWaris" TEXT,
    "HubunganWaris" TEXT,
    FOREIGN KEY("ID_Surat") REFERENCES "Surat"("ID_Surat") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "KenalLahir" (
    "ID_Surat" INTEGER PRIMARY KEY,
    "ID_Ayah" INTEGER,
    "ID_Ibu" INTEGER,
    "NamaAnak" TEXT,
    "TanggalLahirAnak" TEXT,
    "TempatLahirAnak" TEXT,
    "JenisKelaminAnak" TEXT,
    FOREIGN KEY("ID_Surat") REFERENCES "Surat"("ID_Surat") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "IjinTinggal" (
    "ID_Surat" INTEGER PRIMARY KEY,
    "ID_Penjamin" INTEGER,
    "AlamatAsal" TEXT,
    "TanggalMulai" TEXT,
    "TanggalSelesai" TEXT,
    "TujuanTinggal" TEXT,
    "DusunTujuan" TEXT,
    "DesaTujuan" TEXT,
    "KecamatanTujuan" TEXT,
    "KabupatenTujuan" TEXT,
    FOREIGN KEY("ID_Surat") REFERENCES "Surat"("ID_Surat") ON DELETE CASCADE
);
-- Surat Keterangan Pindah: alamat tujuan + alasan pindah (data warganya tetap
-- pada tabel Warga lewat Surat.ID_Warga).
CREATE TABLE IF NOT EXISTS "Pindah" (
    "ID_Surat" INTEGER PRIMARY KEY,
    "AlamatTujuanPindah" TEXT,
    "AlasanPindah" TEXT,
    FOREIGN KEY("ID_Surat") REFERENCES "Surat"("ID_Surat") ON DELETE CASCADE
);
-- Izin Keramaian: kegiatan yang diajukan warga (penanggung jawab = pemohon).
CREATE TABLE IF NOT EXISTS "Keramaian" (
    "ID_Surat" INTEGER PRIMARY KEY,
    "JenisKegiatan" TEXT,
    "TanggalKegiatan" TEXT,
    "LokasiKegiatan" TEXT,
    FOREIGN KEY("ID_Surat") REFERENCES "Surat"("ID_Surat") ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS "BedaNama" (
    "ID_Surat" INTEGER PRIMARY KEY,
    "ID_Warga" INTEGER,
    "SumberDataKoreksi" TEXT,
    "SumberDataKeliru" TEXT,
    "AlasanPerbedaan" TEXT,
    "NIK2" TEXT,
    "Nama2" TEXT,
    "TempatLahir2" TEXT,
    "TanggalLahir2" TEXT,
    "JenisKelamin2" TEXT,
    "Dusun2" TEXT,
    "Desa2" TEXT,
    "Kecamatan2" TEXT,
    "Kabupaten2" TEXT,
    FOREIGN KEY("ID_Surat") REFERENCES "Surat"("ID_Surat") ON DELETE CASCADE,
    FOREIGN KEY("ID_Warga") REFERENCES "Warga"("ID_Warga") ON DELETE RESTRICT
);
CREATE TABLE IF NOT EXISTS "PermintaanWa" (
    "ID_Permintaan"    INTEGER PRIMARY KEY AUTOINCREMENT,
    "KodePermintaan"   TEXT NOT NULL,
    "NomorWA"          TEXT NOT NULL,
    "NamaWarga"        TEXT,
    "NIK"              TEXT,
    "NamaJenis"        TEXT NOT NULL,
    "PesanMentah"      TEXT,
    "DataJson"         TEXT,
    "Status"           TEXT NOT NULL DEFAULT 'BARU',
    "IdSurat"          INTEGER,
    "IsRead"           INTEGER NOT NULL DEFAULT 0,
    "TanggalPermintaan" TEXT NOT NULL,
    "TanggalDiproses"  TEXT,
    "Catatan"          TEXT,
    "PesanBalasan"     TEXT,
    "Sumber"           TEXT NOT NULL DEFAULT 'WA',
    "SheetToken"       TEXT,
    "SheetRowId"       INTEGER,
    "Referensi"        TEXT
);
CREATE INDEX IF NOT EXISTS "idx_permintaanwa_status" ON "PermintaanWa"("Status");
CREATE INDEX IF NOT EXISTS "idx_permintaanwa_wa" ON "PermintaanWa"("NomorWA");
CREATE TABLE IF NOT EXISTS "NTCR" (
    "ID_Surat" INTEGER PRIMARY KEY,
    "ID_CalonIstri" INTEGER,
    "NikIstri" TEXT,
    "NamaIstri" TEXT,
    "TempatLahirIstri" TEXT,
    "TanggalLahirIstri" TEXT,
    "AgamaIstri" TEXT,
    "PekerjaanIstri" TEXT,
    "AlamatIstri" TEXT,
    "NamaAyahCalonSuami" TEXT,
    "NamaIbuCalonSuami" TEXT,
    "NamaAyahCalonIstri" TEXT,
    "NamaIbuCalonIstri" TEXT,
    "StatusPerkawinanIstri" TEXT,
    "KeteranganTemuan" TEXT,
    "TujuanSurat" TEXT,
    -- Kolom khusus tiap blanko NTCR (Model N1-N6) + identitas orang tua,
    -- disimpan sebagai JSON. Database lama ditambal otomatis oleh
    -- NtcrDataHandler.EnsureNtcrColumnsAsync saat pertama kali dipakai.
    "DetailJson" TEXT,
    FOREIGN KEY("ID_Surat") REFERENCES "Surat"("ID_Surat") ON DELETE CASCADE
);

-- Perangkat desa: siapa yang memegang jabatan di desa. Daftar jabatan
-- beserta kelompoknya ada di kode (JabatanPerangkat), bukan di tabel.
-- BerkasSK menyimpan nama berkas PDF SK Bupati yang diarsipkan (tanpa jalur).
CREATE TABLE IF NOT EXISTS "PerangkatDesa" (
    "ID" INTEGER PRIMARY KEY AUTOINCREMENT,
    "Nama" TEXT NOT NULL,
    "Jabatan" TEXT NOT NULL,
    "NIP" TEXT,
    "NIK" TEXT,
    "JenisKelamin" TEXT,
    "TempatLahir" TEXT,
    "TanggalLahir" DATETIME,
    "Pendidikan" TEXT,
    "Alamat" TEXT,
    "Dusun" TEXT,
    "RT" TEXT,
    "RW" TEXT,
    "NomorHP" TEXT,
    "WhatsApp" TEXT,
    "Unit" TEXT,
    "NomorSK" TEXT,
    "TanggalSK" DATETIME,
    "BerkasSK" TEXT,
    "MasaJabatanMulai" DATETIME,
    "MasaJabatanSelesai" DATETIME,
    "Status" TEXT NOT NULL DEFAULT 'AKTIF',
    "Catatan" TEXT,
    "DibuatOleh" TEXT,
    "DiperbaruiOleh" TEXT,
    "CreatedAt" DATETIME DEFAULT CURRENT_TIMESTAMP,
    "UpdatedAt" DATETIME DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS "IX_PerangkatDesa_Jabatan" ON "PerangkatDesa"("Jabatan");
CREATE INDEX IF NOT EXISTS "IX_PerangkatDesa_Status" ON "PerangkatDesa"("Status");
CREATE INDEX IF NOT EXISTS "IX_PerangkatDesa_Wilayah" ON "PerangkatDesa"("Dusun", "RT", "RW");
CREATE TABLE IF NOT EXISTS "TutupBukuTahun" (
    "ID" INTEGER PRIMARY KEY AUTOINCREMENT,
    "Tahun" INTEGER NOT NULL UNIQUE,
    "Status" TEXT NOT NULL DEFAULT 'TERBUKA'
        CHECK ("Status" IN ('TERBUKA', 'TERTUTUP')),
    "TanggalTutup" TEXT,
    "JumlahSurat" INTEGER NOT NULL DEFAULT 0,
    "JumlahDeret" INTEGER NOT NULL DEFAULT 0,
    "Snapshot" TEXT,
    "Catatan" TEXT,
    "DitutupOleh" TEXT,
    "DitutupPada" TEXT,
    "DibukaOleh" TEXT,
    "DibukaPada" TEXT,
    "CreatedAt" DATETIME DEFAULT CURRENT_TIMESTAMP,
    "UpdatedAt" DATETIME DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS "IX_TutupBukuTahun_Status" ON "TutupBukuTahun"("Status");
CREATE TABLE IF NOT EXISTS "LisensiAktivasi" (
    -- Bukti langganan desa: kode aktivasi + token bertanda tangan dari server.
    -- Kolom Token adalah satu-satunya sumber kebenaran (selalu diverifikasi
    -- ulang); kolom lain hanya untuk tampilan. Lihat LisensiAktivasiRepository.
    "KodeAktivasi" TEXT PRIMARY KEY COLLATE NOCASE,
    "NamaDesa"     TEXT,
    "Paket"        TEXT,
    "Token"        TEXT,
    "IdPerangkat"  TEXT,
    "CekTerakhir"  TEXT,
    "CreatedAt"    DATETIME DEFAULT CURRENT_TIMESTAMP,
    "UpdatedAt"    DATETIME DEFAULT CURRENT_TIMESTAMP
);
COMMIT;