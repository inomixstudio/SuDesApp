
-- InsertGarapan
INSERT INTO Garapan (ID_Surat, PemilikTanah, Luas, Lokasi, NomorPersil, KeteranganGarapan)
VALUES (@ID_Surat, @PemilikTanah, @Luas, @Lokasi, @NomorPersil, @KeteranganGarapan);

-- DeleteGarapan
DELETE FROM Garapan WHERE ID_Surat = @ID_Surat;

-- LoadGarapan
SELECT 
    g.ID_GarapanItem, 
    g.PemilikTanah, 
    g.Luas, 
    g.Lokasi, 
    g.NomorPersil, 
    g.KeteranganGarapan
FROM Garapan g
WHERE g.ID_Surat = @ID_Surat;

-- GetGarapanBySuratId
SELECT 
    g.ID_GarapanItem, 
    g.PemilikTanah, 
    g.Luas, 
    g.Lokasi, 
    g.NomorPersil, 
    g.KeteranganGarapan
FROM Garapan g
WHERE g.ID_Surat = @idSurat;


-- InsertKematian
INSERT INTO Kematian (
    ID_Surat,
    HariKematian, TanggalKematian, PukulKematian, 
    PenyebabKematian, TempatKematian, HubunganPelapor,
    NIKPelapor, NamaPelapor, AgamaPelapor,
    UmurPelapor, PekerjaanPelapor, AlamatPelapor
)
VALUES (
    @ID_Surat,
    @HariKematian, @TanggalKematian, @PukulKematian,
    @PenyebabKematian, @TempatKematian, @HubunganPelapor,
    @NIKPelapor, @NamaPelapor, @AgamaPelapor,
    @UmurPelapor, @PekerjaanPelapor, @AlamatPelapor
);

-- DeleteKematian
DELETE FROM Kematian WHERE ID_Surat = @ID_Surat;

-- LoadKematian
SELECT 
    HariKematian, TanggalKematian, PukulKematian, 
    PenyebabKematian, TempatKematian, HubunganPelapor,
    NIKPelapor, NamaPelapor, AgamaPelapor,
    UmurPelapor, PekerjaanPelapor, AlamatPelapor
FROM Kematian
WHERE ID_Surat = @ID_Surat;

-- GetKematianBySuratId
SELECT 
    km.HariKematian, km.TanggalKematian, km.PukulKematian, 
    km.PenyebabKematian, km.TempatKematian, km.HubunganPelapor,
    km.NIKPelapor, km.NamaPelapor, km.AgamaPelapor,
    km.UmurPelapor, km.PekerjaanPelapor, km.AlamatPelapor
FROM Kematian km
WHERE km.ID_Surat = @idSurat;


-- InsertInstansi
INSERT INTO Instansi (ID_Surat, NamaInstansi, AlamatInstansi, PimpinanInstansi)
VALUES (@ID_Surat, @NamaInstansi, @AlamatInstansi, @PimpinanInstansi);

-- DeleteInstansi
DELETE FROM Instansi WHERE ID_Surat = @ID_Surat;

-- LoadInstansi
SELECT NamaInstansi, AlamatInstansi, PimpinanInstansi
FROM Instansi
WHERE ID_Surat = @ID_Surat;

-- InsertIzinOrtu
INSERT INTO IZIN (ID_Surat, ID_Warga_Anak, NegaraTujuan, NamaPT)
VALUES (@ID_Surat, @ID_Warga_Anak, @NegaraTujuan, @NamaPT);

-- DeleteIzinOrtu
DELETE FROM IZIN WHERE ID_Surat = @ID_Surat;

-- LoadIzinOrtu
SELECT ID_Surat, ID_Warga_Anak AS ID_Anak, NegaraTujuan, NamaPT
FROM IZIN WHERE ID_Surat = @ID_Surat;

-- GetIzinWithAnakData
SELECT 
    i.ID_Surat, i.ID_Anak, i.NegaraTujuan, i.NamaPT, 
    i.TanggalKeberangkatan, i.TanggalKembali,
    a.NamaAnak, a.TanggalLahir AS TanggalLahirAnak, a.JenisKelamin AS JenisKelaminAnak,
    wa.NIK AS NIKAyah, wa.Nama AS NamaAyah,
    wi.NIK AS NIKIbu, wi.Nama AS NamaIbu
FROM IZIN i
LEFT JOIN Anak a ON i.ID_Anak = a.ID_Anak
LEFT JOIN Warga wa ON a.ID_Ayah = wa.ID_Warga
LEFT JOIN Warga wi ON a.ID_Ibu = wi.ID_Warga
WHERE i.ID_Surat = @ID_Surat;


-- InsertSKU
INSERT INTO SKU (ID_Surat, BidangUsaha, SejakTahun, LokasiUsaha)
VALUES (@ID_Surat, @BidangUsaha, @SejakTahun, @LokasiUsaha);

-- DeleteSKU
DELETE FROM SKU WHERE ID_Surat = @ID_Surat;

-- LoadSKU
SELECT BidangUsaha, SejakTahun, LokasiUsaha
FROM SKU
WHERE ID_Surat = @ID_Surat;

-- GetSKUBySuratId
SELECT BidangUsaha, SejakTahun, LokasiUsaha 
FROM SKU WHERE ID_Surat = @idSurat;


-- InsertSKTM
INSERT INTO SKTM (ID_Surat, KeteranganKemiskinan, PenghasilanPerBulan, JumlahTanggungan)
VALUES (@ID_Surat, @KeteranganKemiskinan, @PenghasilanPerBulan, @JumlahTanggungan);

-- DeleteSKTM
DELETE FROM SKTM WHERE ID_Surat = @ID_Surat;

-- LoadSKTM
SELECT KeteranganKemiskinan, PenghasilanPerBulan, JumlahTanggungan
FROM SKTM
WHERE ID_Surat = @ID_Surat;

-- InsertBedaNama
INSERT INTO BedaNama (
    ID_Surat, ID_Warga, SumberDataKoreksi, SumberDataKeliru, AlasanPerbedaan,
    NIK2, Nama2, TempatLahir2, TanggalLahir2, JenisKelamin2,
    Dusun2, Desa2, Kecamatan2, Kabupaten2
) VALUES (
    @ID_Surat, @ID_Warga, @SumberDataKoreksi, @SumberDataKeliru, @AlasanPerbedaan,
    @NIK2, @Nama2, @TempatLahir2, @TanggalLahir2, @JenisKelamin2,
    @Dusun2, @Desa2, @Kecamatan2, @Kabupaten2
);

-- DeleteBedaNama
DELETE FROM BedaNama WHERE ID_Surat = @ID_Surat;

-- LoadBedaNama
SELECT 
    ID_Warga, 
    SumberDataKoreksi, 
    SumberDataKeliru,
    AlasanPerbedaan,
    NIK2, Nama2, TempatLahir2, TanggalLahir2, JenisKelamin2,
    Dusun2, Desa2, Kecamatan2, Kabupaten2
FROM BedaNama
WHERE ID_Surat = @ID_Surat;


-- InsertKenalLahir
INSERT INTO KenalLahir (
    ID_Surat, ID_Ayah, ID_Ibu, NamaAnak, TanggalLahirAnak,
    TempatLahirAnak, JenisKelaminAnak
) VALUES (
    @ID_Surat, @ID_Ayah, @ID_Ibu, @NamaAnak, @TanggalLahirAnak,
    @TempatLahirAnak, @JenisKelaminAnak
);

-- LoadKenalLahir
SELECT 
    ID_Ayah, ID_Ibu, NamaAnak, TanggalLahirAnak,
    TempatLahirAnak, JenisKelaminAnak
FROM KenalLahir 
WHERE ID_Surat = @ID_Surat;

-- DeleteKenalLahir
DELETE FROM KenalLahir 
WHERE ID_Surat = @ID_Surat;

-- GetKenalLahirWithParents
SELECT 
    kl.NamaAnak, kl.TanggalLahirAnak, kl.TempatLahirAnak, 
    kl.JenisKelaminAnak,
    wa.NIK AS NIKAyah, wa.Nama AS NamaAyah, wa.Pekerjaan AS PekerjaanAyah,
    wi.NIK AS NIKIbu, wi.Nama AS NamaIbu, wi.Pekerjaan AS PekerjaanIbu
FROM KenalLahir kl
LEFT JOIN Warga wa ON kl.ID_Ayah = wa.ID_Warga
LEFT JOIN Warga wi ON kl.ID_Ibu = wi.ID_Warga
WHERE kl.ID_Surat = @ID_Surat;


-- InsertAhliWaris
INSERT INTO AhliWaris (ID_Surat, NamaWaris, NIKWaris, HubunganWaris)
VALUES (@ID_Surat, @NamaWaris, @NIKWaris, @HubunganWaris);

-- DeleteAhliWaris
DELETE FROM AhliWaris WHERE ID_Surat = @ID_Surat;

-- DeleteAhliWarisById
DELETE FROM AhliWaris WHERE ID_AhliWaris = @ID_AhliWaris;

-- LoadAhliWaris
SELECT 
    a.ID_AhliWaris,
    a.ID_Surat,
    a.NamaWaris,
    a.NIKWaris,
    a.HubunganWaris
FROM AhliWaris a
WHERE a.ID_Surat = @ID_Surat;


-- InsertIjinTinggal
INSERT INTO IjinTinggal (
    ID_Surat, ID_Penjamin, AlamatAsal, TanggalMulai, 
    TanggalSelesai, TujuanTinggal
) VALUES (
    @ID_Surat, @ID_Penjamin, @AlamatAsal, @TanggalMulai,
    @TanggalSelesai, @TujuanTinggal
);

-- DeleteIjinTinggal
DELETE FROM IjinTinggal WHERE ID_Surat = @ID_Surat;

-- LoadIjinTinggal
SELECT 
    ID_Penjamin, AlamatAsal, TanggalMulai,
    TanggalSelesai, TujuanTinggal
FROM IjinTinggal
WHERE ID_Surat = @ID_Surat;

-- GetIjinTinggalWithPenjamin
SELECT 
    ijt.AlamatAsal, ijt.TanggalMulai, ijt.TanggalSelesai, ijt.TujuanTinggal,
    w.NIK AS NIKPenjamin, w.Nama AS NamaPenjamin, w.Pekerjaan AS PekerjaanPenjamin
FROM IjinTinggal ijt
LEFT JOIN Warga w ON ijt.ID_Penjamin = w.ID_Warga
WHERE ijt.ID_Surat = @ID_Surat;


-- InsertSurat
INSERT INTO Surat (
    ID_Jenis, NomorSurat, TanggalSurat, Keterangan, 
    Keperluan, ID_Warga, AdditionalData, Status
)
VALUES (
    @ID_Jenis, @NomorSurat, @TanggalSurat, @Keterangan, 
    @Keperluan, @ID_Warga, @AdditionalData, COALESCE(@Status, 'Draft')
);
SELECT last_insert_rowid();

-- InsertSuratSimple
INSERT INTO Surat (ID_Jenis, NomorSurat, TanggalSurat, Keterangan, Keperluan, ID_Warga)
VALUES (@ID_Jenis, @NomorSurat, @TanggalSurat, @Keterangan, @Keperluan, @ID_Warga);
SELECT last_insert_rowid();

-- UpdateSurat
UPDATE Surat
SET ID_Jenis = @ID_Jenis,
    NomorSurat = @NomorSurat,
    TanggalSurat = @TanggalSurat,
    Keterangan = @Keterangan,
    Keperluan = @Keperluan,
    ID_Warga = @ID_Warga,
    AdditionalData = @AdditionalData,
    Status = COALESCE(@Status, Status)
WHERE ID_Surat = @ID_Surat;

-- UpdateSuratStatus
UPDATE Surat 
SET Status = @Status
WHERE ID_Surat = @ID_Surat;

-- DeleteSurat
DELETE FROM Surat WHERE ID_Surat = @ID_Surat;

-- GetSuratById
SELECT 
    s.ID_Surat, s.ID_Jenis, js.NamaJenis, js.KodeJenis,
    s.NomorSurat, s.TanggalSurat, s.Keterangan, s.Keperluan,
    s.ID_Warga, s.AdditionalData, s.Status, s.CreatedAt, s.UpdatedAt,
    w.NIK, w.ID_Warga, w.Nama, w.TempatLahir, w.TanggalLahir, w.JenisKelamin,
    w.Agama, w.StatusPerkawinan, w.Pekerjaan, w.Dusun, w.Desa,
    w.Kecamatan, w.Kabupaten, w.Pendidikan, w.Kewarganegaraan,
    i.NamaInstansi, i.AlamatInstansi, i.PimpinanInstansi
FROM Surat s
INNER JOIN JenisSurat js ON s.ID_Jenis = js.ID_Jenis
LEFT JOIN Warga w ON s.ID_Warga = w.ID_Warga
LEFT JOIN Instansi i ON s.ID_Surat = i.ID_Surat
WHERE s.ID_Surat = @ID_Surat;

-- GetFilteredSuratData
SELECT 
    s.ID_Surat, s.ID_Jenis, 
    UPPER(js.NamaJenis) AS NamaJenis, js.KodeJenis,
    s.NomorSurat, s.TanggalSurat, s.Keterangan, s.Keperluan,
    s.AdditionalData, s.Status, s.CreatedAt,
    w.ID_Warga, w.NIK, w.Nama, w.TempatLahir, w.TanggalLahir,
    w.JenisKelamin, w.Agama, w.StatusPerkawinan, w.Pekerjaan,
    w.Dusun, w.Desa, w.Kecamatan, w.Kabupaten, w.Pendidikan, w.Kewarganegaraan,
    i.NamaInstansi, i.AlamatInstansi, i.PimpinanInstansi
FROM Surat s
INNER JOIN JenisSurat js ON s.ID_Jenis = js.ID_Jenis
LEFT JOIN Warga w ON s.ID_Warga = w.ID_Warga
LEFT JOIN Instansi i ON s.ID_Surat = i.ID_Surat
{WhereClause} ORDER BY {OrderBy} {Direction} LIMIT @Take OFFSET @Skip;

-- GetFilteredSuratCount
SELECT COUNT(*) FROM Surat s
INNER JOIN JenisSurat js ON s.ID_Jenis = js.ID_Jenis
LEFT JOIN Warga w ON s.ID_Warga = w.ID_Warga
LEFT JOIN Instansi i ON s.ID_Surat = i.ID_Surat
{WhereClause};

-- GetAllSuratData
SELECT 
    s.ID_Surat, s.ID_Jenis, s.NomorSurat, s.TanggalSurat, 
    s.Keterangan, s.Keperluan, s.AdditionalData, s.Status, s.CreatedAt,
    w.ID_Warga, w.NIK, w.Nama, w.TempatLahir, w.TanggalLahir, w.JenisKelamin, 
    w.Agama, w.StatusPerkawinan, w.Pekerjaan, w.Dusun, w.Desa, w.Kecamatan, 
    w.Kabupaten, w.Pendidikan, w.Kewarganegaraan,
    i.NamaInstansi, i.AlamatInstansi, i.PimpinanInstansi,
    js.ID_Jenis, js.NamaJenis, js.KodeJenis, js.Deskripsi
FROM Surat s
LEFT JOIN Warga w ON s.ID_Warga = w.ID_Warga
INNER JOIN JenisSurat js ON s.ID_Jenis = js.ID_Jenis
LEFT JOIN Instansi i ON s.ID_Surat = i.ID_Surat
{WhereClause}
ORDER BY {OrderBy} {Direction}
LIMIT @Take OFFSET @Skip;

-- CountAllSurat
SELECT COUNT(*) FROM Surat;

-- CountSuratByStatus
SELECT Status, COUNT(*) as Jumlah
FROM Surat 
GROUP BY Status;

-- CountSuratByJenis
SELECT COUNT(s.ID_Surat)
FROM Surat s
INNER JOIN JenisSurat js ON s.ID_Jenis = js.ID_Jenis
WHERE UPPER(js.NamaJenis) = @NamaJenis;

-- CountSuratByJenisGroup
SELECT COUNT(s.ID_Surat)
FROM Surat s
INNER JOIN JenisSurat js ON s.ID_Jenis = js.ID_Jenis
WHERE js.ID_Jenis IN @ID_Jenis;

-- CountSuratByJenisAndYear
SELECT COUNT(*) 
FROM Surat s
INNER JOIN JenisSurat js ON s.ID_Jenis = js.ID_Jenis
WHERE js.NamaJenis = @NamaJenis
  AND strftime('%Y', s.TanggalSurat) = @Year;

-- GetSuratStatisticsByMonth
SELECT 
    strftime('%Y-%m', s.TanggalSurat) as Bulan,
    js.NamaJenis,
    COUNT(*) as Jumlah
FROM Surat s
INNER JOIN JenisSurat js ON s.ID_Jenis = js.ID_Jenis
WHERE strftime('%Y', s.TanggalSurat) = @Year
GROUP BY strftime('%Y-%m', s.TanggalSurat), js.NamaJenis
ORDER BY Bulan, js.NamaJenis;

-- GetSuratByDateRange
SELECT s.*, js.NamaJenis, w.Nama
FROM Surat s
INNER JOIN JenisSurat js ON s.ID_Jenis = js.ID_Jenis
LEFT JOIN Warga w ON s.ID_Warga = w.ID_Warga
WHERE s.TanggalSurat BETWEEN @TanggalMulai AND @TanggalSelesai
ORDER BY s.TanggalSurat DESC;

-- GetSuratLengkapById
SELECT * FROM v_SuratLengkap WHERE ID_Surat = @ID_Surat;

-- GetAllSuratLengkap
SELECT * FROM v_SuratLengkap 
ORDER BY TanggalSurat DESC 
LIMIT @Take OFFSET @Skip;

-- GetWargaAktif
SELECT * FROM v_WargaAktif 
WHERE JumlahSurat > 0
ORDER BY JumlahSurat DESC;

-- GetKenalLahirLengkap
SELECT * FROM v_KenalLahirLengkap 
ORDER BY TanggalSurat DESC 
LIMIT @Take OFFSET @Skip;

-- GetKenalLahirLengkapById
SELECT * FROM v_KenalLahirLengkap WHERE ID_Surat = @ID_Surat;

-- CheckNomorSuratExists
SELECT COUNT(*) FROM Surat WHERE NomorSurat = @NomorSurat AND ID_Surat != COALESCE(@ID_Surat, 0);

-- CheckNIKExists
SELECT COUNT(*) FROM Warga WHERE NIK = @NIK AND ID_Warga != COALESCE(@ID_Warga, 0);

-- GetLastNomorSuratByJenis
SELECT NomorSurat 
FROM Surat s
INNER JOIN JenisSurat js ON s.ID_Jenis = js.ID_Jenis
WHERE js.KodeJenis = @KodeJenis
  AND strftime('%Y', s.TanggalSurat) = @Year
ORDER BY s.TanggalSurat DESC, s.ID_Surat DESC
LIMIT 1;

-- GetAllDataForBackup
SELECT 'Surat' as TableName, 
       json_object(
           'ID_Surat', ID_Surat,
           'ID_Jenis', ID_Jenis,
           'NomorSurat', NomorSurat,
           'TanggalSurat', TanggalSurat,
           'Keterangan', Keterangan,
           'Keperluan', Keperluan,
           'ID_Warga', ID_Warga,
           'AdditionalData', AdditionalData,
           'Status', Status
       ) as Data
FROM Surat
UNION ALL
SELECT 'Warga' as TableName,
       json_object(
           'ID_Warga', ID_Warga,
           'NIK', NIK,
           'Nama', Nama,
           'TempatLahir', TempatLahir,
           'TanggalLahir', TanggalLahir,
           'JenisKelamin', JenisKelamin,
           'Agama', Agama,
           'StatusPerkawinan', StatusPerkawinan,
           'Pekerjaan', Pekerjaan,
           'Dusun', Dusun,
           'Desa', Desa,
           'Kecamatan', Kecamatan,
           'Kabupaten', Kabupaten,
           'Pendidikan', Pendidikan,
           'Kewarganegaraan', Kewarganegaraan
       ) as Data
FROM Warga
UNION ALL
SELECT 'JenisSurat' as TableName,
       json_object(
           'ID_Jenis', ID_Jenis,
           'NamaJenis', NamaJenis,
           'KodeJenis', KodeJenis,
           'Deskripsi', Deskripsi,
           'IsActive', IsActive
       ) as Data
FROM JenisSurat;

-- GetAllJenisSurat
SELECT * FROM JenisSurat WHERE IsActive = 1 ORDER BY NamaJenis;

-- GetWargaByNIK
SELECT * FROM Warga WHERE NIK = @NIK;

-- GetWargaByName
SELECT * FROM Warga WHERE Nama LIKE '%' || @Nama || '%' ORDER BY Nama;

-- GetAnakByParent
SELECT 
    a.*,
    wa.Nama AS NamaAyah,
    wi.Nama AS NamaIbu
FROM Anak a
LEFT JOIN Warga wa ON a.ID_Ayah = wa.ID_Warga
LEFT JOIN Warga wi ON a.ID_Ibu = wi.ID_Warga
WHERE a.ID_Ayah = @ID_Warga OR a.ID_Ibu = @ID_Warga
ORDER BY a.TanggalLahir DESC;

-- GetSuratByWarga
SELECT 
    s.*,
    js.NamaJenis,
    js.KodeJenis
FROM Surat s
INNER JOIN JenisSurat js ON s.ID_Jenis = js.ID_Jenis
WHERE s.ID_Warga = @ID_Warga
ORDER BY s.TanggalSurat DESC;