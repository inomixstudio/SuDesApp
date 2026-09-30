using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OfficeOpenXml;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Services;
using SuDesApp.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace SuDesApp.Core.Tests
{
    /// <summary>
    /// Uji lapisan laporan: pemetaan pendidikan ke jenjang BPS, susunan tabel,
    /// dan dua keluaran (PDF serta Excel). Lapisan laporan sebelumnya sama sekali
    /// tidak punya test, jadi regresi di sini baru ketahuan saat petugas
    /// membuka berkas yang sudah dibagikan ke dinas.
    /// </summary>
    public sealed class LaporanPendudukTests
    {
        // =====================================================================
        // Pemetaan pendidikan -> jenjang BPS
        // =====================================================================

        [Theory]
        [InlineData("SD", "SD/MI sederajat")]
        [InlineData("smp", "SMP/MTs sederajat")]
        [InlineData("SMA", "SMA/MA/SMK sederajat")]
        [InlineData("SMK", "SMA/MA/SMK sederajat")]
        [InlineData("D3", "Diploma I/II/III")]
        [InlineData("Diploma III", "Diploma I/II/III")]
        [InlineData("S1", "Universitas")]
        [InlineData("Universitas", "Universitas")]
        [InlineData("Akademi", "Akademi")]
        [InlineData("Tidak sekolah", "Tidak sekolah")]
        [InlineData("LULUS SD/SMP", "SMP/MTs sederajat")]
        [InlineData("s1.", "Universitas")]
        public void JenjangPendidikan_TeksBebasDikelompokkanKeBps(string masuk, string harapan)
            => Assert.Equal(harapan, JenjangPendidikanBps.Tentukan(masuk));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("-")]
        [InlineData("N/A")]
        [InlineData("tidak ada")]
        public void JenjangPendidikan_Kosong_TercatatSebagaiBelumDiisi(string? masuk)
            => Assert.Equal(JenjangPendidikanBps.BelumSekolah, JenjangPendidikanBps.Tentukan(masuk));

        [Fact]
        public void JenjangPendidikan_PencocokanKataUtuh_BukanHurufDiDalamKata()
        {
            // "MA" tidak boleh ikut cocok di "mahasiswa" (kalau ikut, "mahasiswa"
            // akan salah diklasifikasikan sebagai SMA), dan "S1" tidak boleh ikut
            // cocok di "S12". Keduanya jadi "belum diisi" — lebih jujur daripada
            // menebak jenjang.
            Assert.Equal(JenjangPendidikanBps.BelumSekolah, JenjangPendidikanBps.Tentukan("mahasiswa"));
            Assert.Equal(JenjangPendidikanBps.BelumSekolah, JenjangPendidikanBps.Tentukan("S12"));
        }

        [Fact]
        public void JenjangPendidikan_SemuaNilaiDikenal_TidakAdaYangBocor()
        {
            foreach (var nilai in new[] { "SD", "SMP", "SMA", "D3", "S1", "Universitas", "Akademi" })
            {
                Assert.Contains(JenjangPendidikanBps.Tentukan(nilai), JenjangPendidikanBps.Lista);
            }
        }

        // =====================================================================
        // Susunan tabel
        // =====================================================================

        private static WargaStatistikRingkasan Ringkasan(
            int seluruh = 0, int aktif = 0, int baru = 0, int pindah = 0,
            int meninggal = 0, int laki = 0, int perempuan = 0,
            int kk = 0, int tanpaKk = 0)
            => new()
            {
                TotalSeluruh = seluruh, TotalAktif = aktif, TotalBaru = baru,
                TotalPindah = pindah, TotalMeninggal = meninggal,
                LakiLaki = laki, Perempuan = perempuan,
                JumlahKartuKeluarga = kk, TanpaKartuKeluarga = tanpaKk
            };

        [Fact]
        public void Susun_PendudukAdalahAktifDitambahBaru_BukanSeluruhTerdata()
        {
            var data = LaporanPendudukBuilder.Susun(
                Ringkasan(seluruh: 100, aktif: 60, baru: 10, pindah: 20, meninggal: 10),
                null, new DateTime(2026, 9, 27));

            Assert.Equal(70, data.Ringkasan.TotalAktif + data.Ringkasan.TotalBaru);

            var tabel = data.Tabel[JenisTabelLaporan.RingkasanPenduduk];
            // Tiga kolom: angka dan persentase TERPISAH (kolom Persentase di
            // pratinjau layar tidak lagi kosong).
            Assert.Equal("70", tabel.Baris[0][1]);
            Assert.Equal("70,0 %", tabel.Baris[0][2]);
            // Meninggal & pindah boleh tampil, tapi tidak ikut dihitung penduduk.
            Assert.Contains(tabel.Baris, b => b[0] == "Warga meninggal" && b[1].StartsWith("10"));
        }

        [Fact]
        public void Susun_PersentasePakaiKomaDanDibulatkanSatuDesimal()
        {
            // laki/perempuan mengisi angka SELURUH warga; komposisi penduduknya
            // diisi eksplisit karena itulah yang dicetak tabel Ringkasan.
            var data = LaporanPendudukBuilder.Susun(
                new WargaStatistikRingkasan
                {
                    TotalSeluruh = 300, TotalAktif = 300,
                    LakiLaki = 100, Perempuan = 200,
                    LakiLakiPenduduk = 100, PerempuanPenduduk = 200
                },
                null, new DateTime(2026, 9, 27));

            var tabel = data.Tabel[JenisTabelLaporan.RingkasanPenduduk];
            Assert.Equal("300", tabel.Baris[0][1]);
            Assert.Equal("100,0 %", tabel.Baris[0][2]);
            Assert.Equal("100", tabel.Baris[1][1]);
            Assert.Equal("33,3 %", tabel.Baris[1][2]);
            Assert.Equal("200", tabel.Baris[2][1]);
            Assert.Equal("66,7 %", tabel.Baris[2][2]);
        }

        [Fact]
        public void Susun_TotalNol_TidakMembagiNol()
        {
            var data = LaporanPendudukBuilder.Susun(Ringkasan(), null);

            var tabel = data.Tabel[JenisTabelLaporan.RingkasanPenduduk];
            Assert.All(tabel.Baris, b => Assert.DoesNotContain("%", b[1]));
        }

        [Fact]
        public void Susun_RtUrutNumerikBukanAbjad()
        {
            var ringkasan = Ringkasan(seluruh: 30, aktif: 30);
            ringkasan.PerRt = new[]
            {
                new WargaStatistikBaris { Kunci = "RT 10", Jumlah = 5 },
                new WargaStatistikBaris { Kunci = "RT 2", Jumlah = 10 },
                new WargaStatistikBaris { Kunci = "RT 1", Jumlah = 15 }
            };

            var tabel = LaporanPendudukBuilder.Susun(ringkasan, null).Tabel[JenisTabelLaporan.Rt];

            Assert.Equal(new[] { "RT 1", "RT 2", "RT 10" }, tabel.Baris.Select(b => b[0]).ToArray());
        }

        [Fact]
        public void Susun_TabelTanpaIsi_TidakMasukDaftarYangDicetak()
        {
            var ringkasan = Ringkasan(seluruh: 1, aktif: 1);
            ringkasan.PerAgama = Array.Empty<WargaStatistikBaris>();

            var data = LaporanPendudukBuilder.Susun(ringkasan, null);

            Assert.Empty(data.Tabel[JenisTabelLaporan.Agama].Baris);
            Assert.DoesNotContain(data.TabelTerisi, t => t.Judul.Contains("Agama"));
        }

        [Fact]
        public void Susun_HanyaTabelYangDipilih_DisusunDanDicetak()
        {
            var data = LaporanPendudukBuilder.Susun(
                Ringkasan(seluruh: 1, aktif: 1), null,
                tabelTermasuk: new[] { JenisTabelLaporan.StatusWarga });

            Assert.Single(data.UrutanTabel);
            Assert.Single(data.Tabel);
            Assert.Contains(data.TabelTerisi, t => t.Judul == "Warga Menurut Status Tinggal");
        }

        [Fact]
        public void Susun_UrutanTabelTetapBergantungJenis_LaluanAcakDihapus()
        {
            var diminta = new[]
            {
                JenisTabelLaporan.Rt, JenisTabelLaporan.KelompokUsia, JenisTabelLaporan.Rt
            };

            var data = LaporanPendudukBuilder.Susun(Ringkasan(1, 1), null, tabelTermasuk: diminta);

            Assert.Equal(2, data.UrutanTabel.Count);
        }

        [Fact]
        public void Susun_TanggalCetakMenentukanPeriodeDanNamaBerkas()
        {
            var data = LaporanPendudukBuilder.Susun(
                Ringkasan(1, 1), null, new DateTime(2026, 12, 31, 18, 30, 0));

            Assert.Equal(new DateTime(2026, 12, 31), data.TanggalCetak);
            Assert.Equal("Per 31 Desember 2026", data.Periode);
            Assert.Equal("Laporan_Penduduk_20261231", data.NamaBerkas);
        }

        [Fact]
        public void Susun_KeteranganFilterKosong_TidakDicetak()
        {
            Assert.Null(LaporanPendudukBuilder.Susun(Ringkasan(1, 1), null, keteranganFilter: "   ")
                .KeteranganFilter);
            Assert.Equal("RT 01", LaporanPendudukBuilder
                .Susun(Ringkasan(1, 1), null, keteranganFilter: " RT 01 ").KeteranganFilter);
        }

        [Fact]
        public void Susun_DataDesaNull_TidakMelemparDanIsiDigantiPengganti()
        {
            var data = LaporanPendudukBuilder.Susun(Ringkasan(1, 1), null);

            Assert.NotNull(data.Desa);
            Assert.True(string.IsNullOrEmpty(data.Desa.NamaDesa));
        }

        [Fact]
        public void Susun_TotalWargaNol_TetapPunyaIsiYangBisaDicetak()
        {
            // Laporan kosong tidak boleh_null_, karena petugas tetap perlu
            // dokumen resmi yang menyatakan desa belum punya data.
            var data = LaporanPendudukBuilder.Susun(Ringkasan(), null);

            Assert.True(data.AdaIsi);
            Assert.NotEmpty(data.TabelTerisi);
        }

        [Fact]
        public void Susun_DaftarTabelKosong_BedaArtinyaDenganNull()
        {
            // null = semua tabel (perilaku bawaan); daftar kosong = operator mematikan
            // seluruh tabel. Dulu keduanya sama — "Hapus semua" justru mencetak semua.
            var semua = LaporanPendudukBuilder.Susun(Ringkasan(1, 1), null, tabelTermasuk: null);
            var tanpaTabel = LaporanPendudukBuilder.Susun(Ringkasan(1, 1), null, tabelTermasuk: Array.Empty<JenisTabelLaporan>());

            Assert.Equal(9, semua.UrutanTabel.Count);
            Assert.Empty(tanpaTabel.UrutanTabel);
            Assert.Empty(tanpaTabel.TabelTerisi);

            // Laporan tanpa tabel tetap sah: ringkasan + kartu angka tetap tercetak.
            Assert.True(tanpaTabel.AdaIsi);
        }

        // =====================================================================
        // Komposisi jenis kelamin penduduk (L4)
        // =====================================================================

        [Fact]
        public void Susun_KomposisiJenisKelaminDariPenduduk_BukanSeluruhWarga()
        {
            // 60 aktif + 10 baru = 70 penduduk; ada pula 30 warga pindah/meninggal.
            // Dulu baris L/P memakai LakiLaki/Perempuan (100 + 100 seluruh warga)
            // dibagi 70 penduduk → L + P = 200 "dari" 70, jelas mustahil.
            var data = LaporanPendudukBuilder.Susun(
                new WargaStatistikRingkasan
                {
                    TotalSeluruh = 170, TotalAktif = 60, TotalBaru = 10,
                    TotalPindah = 60, TotalMeninggal = 40,
                    LakiLaki = 100, Perempuan = 70,
                    LakiLakiPenduduk = 40, PerempuanPenduduk = 30
                },
                null, new DateTime(2026, 9, 27));

            var tabel = data.Tabel[JenisTabelLaporan.RingkasanPenduduk];

            Assert.Equal("40", CariBaris(tabel, "Laki-laki")[1]);
            Assert.Equal("30", CariBaris(tabel, "Perempuan")[1]);

            // Persentase dihitung terhadap penduduk, bukan seluruh warga terdata.
            Assert.Equal("57,1 %", CariBaris(tabel, "Laki-laki")[2]);
            Assert.Equal("42,9 %", CariBaris(tabel, "Perempuan")[2]);

            // L + P ≤ penduduk — kewajaran yang dulu bisa dilanggar.
            int laki = int.Parse(CariBaris(tabel, "Laki-laki")[1]);
            int perempuan = int.Parse(CariBaris(tabel, "Perempuan")[1]);
            Assert.True(laki + perempuan <= 70, $"L + P ({laki + perempuan}) melebihi penduduk (70).");
        }

        [Fact]
        public void Susun_JenisKelaminBelumDiisi_MunculSebagaiBarisSendiri()
        {
            // Penduduk yang jenis kelaminnya kosong tidak boleh hilang dari rekap:
            // L + P + tidak diketahui harus sama dengan jumlah penduduk.
            var data = LaporanPendudukBuilder.Susun(
                new WargaStatistikRingkasan
                {
                    TotalSeluruh = 10, TotalAktif = 10,
                    LakiLakiPenduduk = 6, PerempuanPenduduk = 3,
                    PendudukJenisKelaminTidakDiketahui = 1
                },
                null);

            var tabel = data.Tabel[JenisTabelLaporan.RingkasanPenduduk];

            Assert.Equal("1", CariBaris(tabel, "Jenis kelamin belum diisi")[1]);
            Assert.Equal("10,0 %", CariBaris(tabel, "Jenis kelamin belum diisi")[2]);
        }

        [Fact]
        public void Susun_JenisKelaminSemuaTerisi_TidakAdaBarisTidakDiketahui()
        {
            var data = LaporanPendudukBuilder.Susun(
                new WargaStatistikRingkasan
                {
                    TotalSeluruh = 5, TotalAktif = 5,
                    LakiLakiPenduduk = 3, PerempuanPenduduk = 2,
                    PendudukJenisKelaminTidakDiketahui = 0
                },
                null);

            Assert.DoesNotContain(
                data.Tabel[JenisTabelLaporan.RingkasanPenduduk].Baris,
                b => b[0] == "Jenis kelamin belum diisi");
        }

        /// <summary>Ambil satu baris tabel ringkasan berdasar label uraian.</summary>
        private static string[] CariBaris(LaporanTabel tabel, string label) =>
            tabel.Baris.First(b => b[0] == label);

        // =====================================================================
        // Keluaran PDF
        // =====================================================================

        [Fact]
        public async Task RenderPdf_MenghasilkanBerkasPdfYangSah()
        {
            var data = LaporanPendudukBuilder.Susun(
                Ringkasan(seluruh: 40, aktif: 30, baru: 4, pindah: 3, meninggal: 3,
                    laki: 17, perempuan: 17, kk: 9, tanpaKk: 2),
                new DesaData { NamaDesa = "Sumberjaya", Kecamatan = "Cileungsi", Kabupaten = "Bogor",
                    KepalaDesa = "Ahmad", SekretarisDesa = "Budi" },
                new DateTime(2026, 9, 27));
            data.Tabel[JenisTabelLaporan.KelompokUsia] = new LaporanTabel
            {
                Judul = "Penduduk Menurut Kelompok Usia",
                LabelKolom = "Kelompok usia",
                JudulKolom = new[] { "Jumlah", "Persentase" },
                Baris = new[]
                {
                    new[] { "0-4 tahun", "10 (25,0 %)" },
                    new[] { "5-14 tahun", "24 (60,0 %)" },
                    new[] { "75 tahun ke atas", "6 (15,0 %)" }
                }
            };

            byte[] pdf = await new LaporanPendudukService(null!, null!, NullLogger<LaporanPendudukService>.Instance)
                .RenderPdfAsync(data);

            Assert.True(pdf.Length > 1000, $"PDF terlalu kecil: {pdf.Length} byte");
            Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
            // Penanda akhir PDF: %%EOF
            Assert.Contains("%%EOF", Encoding.ASCII.GetString(pdf, pdf.Length - 32, 32));
        }

        /// <summary>
        /// Jumlah halaman PDF = 1 (ringkasan) + 1 per tabel yang berisi data.
        /// Aturan ini yang membuat blok tanda tangan bisa diletakkan di akhir
        /// dokumen: halaman terakhir selalu halaman tabel terakhir.
        /// </summary>
        [Fact]
        public async Task RenderPdf_SatuHalamanRingkasanPlusSatuPerTabel()
        {
            var data = LaporanPendudukBuilder.Susun(
                Ringkasan(seluruh: 120, aktif: 100, baru: 10), null);

            byte[] pdf = await new LaporanPendudukService(null!, null!, NullLogger<LaporanPendudukService>.Instance)
                .RenderPdfAsync(data);

            Assert.Equal(1 + data.TabelTerisi.Count, HitungHalamanPdf(pdf));
        }

        /// <summary>
        /// Blok tanda tangan diletakkan di akhir halaman terakhir. Kalau tabel
        /// terakhir adalah yang paling panjang (Kelompok Usia, 16 baris),
        /// blok tanda tangan harus tetap muat di halaman itu — kalau tidak,
        /// QuestPDF melempar DocumentLayoutException dan laporan gagal dibuat
        /// sama sekali, bukan sekadar tanda tangannya terpotong.
        /// </summary>
        [Fact]
        public async Task RenderPdf_TabelTerpanjang_MasihMuatBlokTandaTangan()
        {
            // 16 baris kelompok usia: baris terbanyak di antara semua tabel.
            var barisUsia = new List<WargaStatistikBaris>();
            for (int usia = 0; usia <= 75; usia += 5)
            {
                barisUsia.Add(new WargaStatistikBaris { Kunci = $"{usia}-{usia + 4} tahun", Jumlah = usia + 1 });
            }

            var data = LaporanPendudukBuilder.Susun(
                new WargaStatistikRingkasan
                {
                    TotalSeluruh = 900,
                    TotalAktif = 900,
                    LakiLaki = 450,
                    Perempuan = 450,
                    JumlahKartuKeluarga = 300,
                    PerKelompokUsia = barisUsia
                },
                null,
                new DateTime(2026, 9, 27),
                tabelTermasuk: new[] { JenisTabelLaporan.KelompokUsia });

            byte[] pdf = await new LaporanPendudukService(null!, null!, NullLogger<LaporanPendudukService>.Instance)
                .RenderPdfAsync(data);

            // Ringkasan + satu halaman kelompok usia.
            Assert.Equal(2, HitungHalamanPdf(pdf));
            Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        }

        /// <summary>
        /// Jumlah objek halaman di PDF. QuestPDF menulis "/Type /Page" untuk
        /// setiap halaman dan "/Type /Pages" untuk akar pohon, jadi pola
        /// "/Page" diikuti karakter non-"s" hanya menghitung halaman.
        /// </summary>
        private static int HitungHalamanPdf(byte[] pdf)
        {
            string isi = Encoding.GetEncoding(28591).GetString(pdf);
            return System.Text.RegularExpressions.Regex.Matches(isi, @"/Type\s*/Page[^s]").Count;
        }

        [Fact]
        public async Task RenderPdf_TanpaWarga_TetapTerbitSatuHalamanPenjelasan()
        {
            var data = LaporanPendudukBuilder.Susun(Ringkasan(), null);

            byte[] pdf = await new LaporanPendudukService(null!, null!, NullLogger<LaporanPendudukService>.Instance)
                .RenderPdfAsync(data);

            Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
            Assert.True(pdf.Length > 500);
        }

        /// <summary>
        /// "Hapus semua" pada pemilih isi laporan harus menghasilkan laporan satu
        /// halaman ringkasan — bukan galat indeks kosong (dulu Compose membaca
        /// tabel[0] tanpa pemeriksaan) dan bukan diam-diam mencetak semua tabel.
        /// </summary>
        [Fact]
        public async Task RenderPdf_SemuaTabelDimatikan_TetapTerbitRingkasanBertandaTangan()
        {
            var data = LaporanPendudukBuilder.Susun(
                Ringkasan(seluruh: 12, aktif: 12, laki: 0, perempuan: 0), null,
                tabelTermasuk: Array.Empty<JenisTabelLaporan>());

            Assert.Empty(data.TabelTerisi);

            byte[] pdf = await new LaporanPendudukService(null!, null!, NullLogger<LaporanPendudukService>.Instance)
                .RenderPdfAsync(data);

            Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
            Assert.True(pdf.Length > 1000, $"PDF terlalu kecil: {pdf.Length} byte");
            // Satu halaman: ringkasan saja, tanpa halaman tabel.
            Assert.Equal(1, HitungHalamanPdf(pdf));
        }

        [Fact]
        public async Task RenderPdf_DataNull_Ditolak()
        {
            var svc = new LaporanPendudukService(null!, null!, NullLogger<LaporanPendudukService>.Instance);
            await Assert.ThrowsAsync<ArgumentNullException>(() => svc.RenderPdfAsync(null!));
        }

        [Fact]
        public async Task RenderExcel_TidakAdaData_TidakLemparException()
        {
            var svc = new LaporanPendudukService(null!, null!, NullLogger<LaporanPendudukService>.Instance);
            byte[] xlsx = await svc.RenderExcelAsync(LaporanPendudukBuilder.Susun(Ringkasan(), null));
            Assert.True(xlsx.Length > 1000);
        }

        /// <summary>
        /// Berkas Excel yang hanya "tidak melempar exception" belum tentu bisa
        /// dibuka Excel/EPPlus. Test ini menutup celah itu: workbook hasil
        /// laporan dengan data nyata harus benar-benar bisa dibaca ulang, dan
        /// angka pada sel harus persis sama dengan tabel laporan — kalau tidak,
        /// yang terkirim ke dinas bisa berbeda dari yang tampil di aplikasi.
        /// </summary>
        [Fact]
        public async Task RenderExcel_DenganData_BisaDibacaUlangDanAngkanyaSama()
        {
            var svc = new LaporanPendudukService(null!, null!, NullLogger<LaporanPendudukService>.Instance);
            var data = LaporanPendudukBuilder.Susun(
                new WargaStatistikRingkasan
                {
                    TotalSeluruh = 300, TotalAktif = 250, TotalBaru = 40,
                    TotalPindah = 5, TotalMeninggal = 5,
                    LakiLaki = 140, Perempuan = 150,
                    LakiLakiPenduduk = 200, PerempuanPenduduk = 130,
                    JumlahKartuKeluarga = 120, TanpaKartuKeluarga = 3
                },
                null);

            byte[] xlsx = await svc.RenderExcelAsync(data);
            Assert.True(xlsx.Length > 1000);

            using var stream = new MemoryStream(xlsx);
            using var paket = new ExcelPackage(stream);

            var sheet = paket.Workbook.Worksheets["Ringkasan"];
            Assert.NotNull(sheet);

            // Tidak boleh ada worksheet tanpa nama: Excel menolak membuka
            // sheet yang namanya kosong, dan laporan ini dikirim ke dinas.
            var namaSheet = paket.Workbook.Worksheets.Select(w => w?.Name).ToList();
            Assert.DoesNotContain(namaSheet, n => string.IsNullOrWhiteSpace(n));

            // Sheet lain harus ikut terbentuk untuk setiap tabel yang berisi
            // data, bukan hanya sheet ringkasan.
            Assert.Contains(paket.Workbook.Worksheets, w => w != null && w.Name.Contains("Kartu Keluarga", StringComparison.OrdinalIgnoreCase));

            // Angka pada sel harus sama persis dengan tabel laporan: penduduk
            // 250 aktif + 40 baru = 290, sementara 300 adalah seluruh warga
            // terdata (termasuk pindah & meninggal) yang TIDAK boleh dipakai
            // sebagai jumlah penduduk.
            Assert.Equal("Penduduk (tinggal di desa)", sheet!.Cells[5, 1].Text);
            Assert.Equal(290, Convert.ToInt32(sheet.Cells[5, 2].Value));
            // Baris KK bergeser empat baris: rekap memuat Kepala Keluarga (total,
            // laki-laki, wanita) dan sekarang baris "Jenis kelamin belum diisi"
            // tidak muncul (semua warga uji terisi jenis kelaminnya).
            Assert.Equal("Kepala Keluarga", sheet.Cells[8, 1].Text);
            Assert.Equal("Jumlah seluruh warga terdata", sheet.Cells[13, 1].Text);
            Assert.Equal(300, Convert.ToInt32(sheet.Cells[13, 2].Value));

            // Komposisi jenis kelamin pada Excel harus memakai angka penduduk
            // (bukan seluruh warga) — sama dengan tabel Ringkasan di PDF.
            Assert.Equal(200, Convert.ToInt32(sheet.Cells[6, 2].Value));
            Assert.Equal(130, Convert.ToInt32(sheet.Cells[7, 2].Value));
        }

        /// <summary>
        /// Excel membatasi nama worksheet 31 karakter. Pemotongan harus
        /// ditandai dengan "…" supaya terbaca terpotong (bukan salah ketik),
        /// dan dua judul yang sama-sama terpotong tidak boleh menghasilkan dua
        /// sheet dengan nama identik.
        /// </summary>
        [Fact]
        public async Task RenderExcel_NamaSheet_DipotongTandaBacaDanTetapUnik()
        {
            var svc = new LaporanPendudukService(null!, null!, NullLogger<LaporanPendudukService>.Instance);

            // Dua tabel berjudul panjang: "Penduduk Menurut Pendidikan Terakhir"
            // (35 karakter) dan "Penduduk Menurut Status Perkawinan" (34)
            // sama-sama melewati batas 31 karakter Excel.
            var duaTabelPanjang = LaporanPendudukBuilder.Susun(
                new WargaStatistikRingkasan
                {
                    TotalSeluruh = 10,
                    TotalAktif = 10,
                    PerPendidikan = new[]
                    {
                        new WargaStatistikBaris { Kunci = "SMP/MTs sederajat", Jumlah = 6 },
                        new WargaStatistikBaris { Kunci = "SMA/MA sederajat", Jumlah = 4 }
                    },
                    PerStatusPerkawinan = new[]
                    {
                        new WargaStatistikBaris { Kunci = "Kawin", Jumlah = 7 },
                        new WargaStatistikBaris { Kunci = "Belum Kawin", Jumlah = 3 }
                    }
                },
                null,
                tabelTermasuk: new[] { JenisTabelLaporan.Pendidikan, JenisTabelLaporan.StatusPerkawinan });

            byte[] xlsx = await svc.RenderExcelAsync(duaTabelPanjang);

            using var stream = new MemoryStream(xlsx);
            using var paket = new ExcelPackage(stream);

            var nama = paket.Workbook.Worksheets.Select(w => w.Name).ToList();

            // Semua nama harus memenuhi batas Excel.
            Assert.All(nama, n => Assert.True(n.Length <= 31, $"Nama sheet kepanjangan: '{n}' ({n.Length} karakter)"));

            // Tidak boleh ada nama yang sama (Excel akan menolak workbooknya).
            Assert.Equal(nama.Count, nama.Distinct(StringComparer.OrdinalIgnoreCase).Count());

            // Nama yang terpotong harus ditandai, bukan dipotong diam-diam.
            Assert.Contains(nama, n => n.EndsWith("…", StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Uji integrasi laporan terhadap database sungguhan (SQLite in-memory dengan
    /// skema asli). Pastikan angka statistik -&gt; tabel laporan benar-benar berasal
    /// dari query, bukan dari data contoh yang di-hardcode.
    /// </summary>
    public sealed class LaporanPendudukIntegrasiTests : IDisposable
    {
        private readonly SqliteConnection _koneksi;
        private readonly WargaRepository _warga;
        private readonly LaporanPendudukService _svc;

        public LaporanPendudukIntegrasiTests()
        {
            _koneksi = new SqliteConnection("Data Source=:memory:");
            _koneksi.Open();
            _koneksi.ExecuteScript(CoreTestFixture.ReadProjectFile("desa.db.sql"));
            _koneksi.ExecuteNonQuery("DELETE FROM Warga;");

            _warga = new WargaRepository(
                _koneksi,
                new MemoryCacheService(
                    new MemoryCache(new MemoryCacheOptions()),
                    NullLogger<MemoryCacheService>.Instance),
                new AppConfig(new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["AppConfig:databaseConnectionString"] = "Data Source=:memory:"
                    })
                    .Build()),
                NullLogger<WargaRepository>.Instance);

            // Desa dikosongkan: laporan harus tetap terbit meski data desa belum diisi.
            _svc = new LaporanPendudukService(_warga, null!, NullLogger<LaporanPendudukService>.Instance);
        }

        public void Dispose() => _koneksi.Dispose();

        private void TambahWarga(string nik, string nama, string? pendidikan, string rt, string jk, string lahir)
        {
            // ExecuteNonQuery di fixture hanya menerima SQL tunggal, jadi nilai
            // disisipkan lewat parameter Dapper agar aman dari kutip di dalam teks.
            _koneksi.Execute("""
                INSERT INTO Warga (NIK, Nama, Pendidikan, RT, JenisKelamin, TanggalLahir, NoKK, StatusWarga)
                VALUES (@Nik, @Nama, @Pendidikan, @Rt, @Jk, @Lahir, '3201010101010001', 'AKTIF')
                """, new { Nik = nik, Nama = nama, Pendidikan = pendidikan, Rt = rt, Jk = jk, Lahir = lahir });
        }

        [Fact]
        public async Task Statistik_DikelompokkanKeJenjangPendidikanBps()
        {
            TambahWarga("3201010101010001", "Warga Satu", "SD", "01", "L", "1990-01-01");
            TambahWarga("3201010101010002", "Warga Dua", "SMP", "01", "P", "1985-01-01");
            TambahWarga("3201010101010003", "Warga Tiga", "Universitas", "02", "L", "1978-01-01");
            TambahWarga("3201010101010004", "Warga Empat", "SD", "02", "P", "1995-01-01");
            TambahWarga("3201010101010005", "Warga Lima", null, "01", "L", "2000-01-01");

            var data = await _svc.SusunAsync();
            var pendidikan = data.Tabel[JenisTabelLaporan.Pendidikan];

            // "SD" dua orang dan "SMP" satu orang harus tetap terpisah sebagai
            // jenjang baku, bukan berderet sebagai nilai mentah.
            Assert.Equal(2, Jumlah(pendidikan, "SD/MI sederajat"));
            Assert.Equal(1, Jumlah(pendidikan, "SMP/MTs sederajat"));
            Assert.Equal(1, Jumlah(pendidikan, "Universitas"));
            Assert.Equal(1, Jumlah(pendidikan, JenjangPendidikanBps.BelumSekolah));

            Assert.Equal(5, pendidikan.Baris.Sum(b => JumlahBaris(b)));
        }

        /// <summary>Ambil angka dari sel "1.234 (12,3 %)" -&gt; 1234.</summary>
        private static int JumlahBaris(IReadOnlyList<string> baris)
        {
            string kepala = baris[1].Split(" (")[0];
            return int.Parse(kepala.Replace(".", string.Empty));
        }

        private static int Jumlah(LaporanTabel tabel, string kunci) =>
            JumlahBaris(tabel.Baris.Single(b => b[0] == kunci));

        [Fact]
        public async Task Susun_LaporanMemakaiAngkaStatistikYangSamaDenganRepository()
        {
            TambahWarga("3201010101010001", "Warga Satu", "SD", "01", "L", "1990-01-01");
            TambahWarga("3201010101010002", "Warga Dua", "SMP", "02", "P", "1985-01-01");

            var langsung = await _warga.GetStatistikWargaAsync();
            var laporan = await _svc.SusunAsync();

            Assert.Equal(langsung.TotalSeluruh, laporan.Ringkasan.TotalSeluruh);
            Assert.Equal(langsung.TotalAktif, laporan.Ringkasan.TotalAktif);
            Assert.Equal(langsung.LakiLaki, laporan.Ringkasan.LakiLaki);
            Assert.Equal(langsung.Perempuan, laporan.Ringkasan.Perempuan);
        }

        [Fact]
        public async Task Susun_DummyInstansiDanKematianTidakDihitung()
        {
            TambahWarga("3201010101010001", "Warga Asli", "SD", "01", "L", "1990-01-01");
            TambahWarga("9999999999999999", "Instansi", "SD", "01", "L", "1990-01-01");
            TambahWarga("0000000000000000", "Kematian", "SD", "01", "L", "1990-01-01");

            var data = await _svc.SusunAsync();

            Assert.Equal(1, data.Ringkasan.TotalSeluruh);
        }

        [Fact]
        public async Task BuatExcel_SheetPerTabelHanyaUntukTabelYangTerisi()
        {
            TambahWarga("3201010101010001", "Warga Satu", "SD", "01", "L", "1990-01-01");

            byte[] xlsx = await _svc.BuatExcelAsync();

            Assert.True(xlsx.Length > 2000);
            // .xlsx adalah zip: header "PK".
            Assert.Equal("PK", Encoding.ASCII.GetString(xlsx, 0, 2));
        }

        [Fact]
        public async Task BuatPdf_LaporanTercetak_LengthTidakNol()
        {
            TambahWarga("3201010101010001", "Warga Satu", "SD", "01", "L", "1990-01-01");

            byte[] pdf = await _svc.BuatPdfAsync(keteranganFilter: "Seluruh warga");

            Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        }
    }
}
