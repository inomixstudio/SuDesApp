using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Services;
using Xunit;

namespace SuDesApp.Core.Tests
{
    /// <summary>
    /// Uji aturan Data Perangkat Desa: penulisan sampai database, validasi
    /// isian, larangan satu jabatan di satu wilayah terisi dua orang, dan rekap
    /// jabatan inti desa. Memakai SQLite in-memory supaya berkas database
    /// pengguna tidak tersentuh.
    /// </summary>
    public sealed class PerangkatDesaServiceTests : IDisposable
    {
        private static readonly DateTime Sekarang = new(2026, 3, 10);

        private readonly SqliteConnection _connection;
        private readonly PerangkatDesaService _svc;

        public PerangkatDesaServiceTests()
        {
            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();
            _connection.ExecuteScript(CoreTestFixture.ReadProjectFile("desa.db.sql"));
            _connection.ExecuteNonQuery("DELETE FROM PerangkatDesa;");

            var repo = new PerangkatDesaRepository(
                _connection, NullLogger<PerangkatDesaRepository>.Instance);

            _svc = new PerangkatDesaService(
                repo, NullLogger<PerangkatDesaService>.Instance, () => Sekarang);
        }

        public void Dispose() => _connection.Dispose();

        private static PerangkatDesa Baru(string nama, string jabatan) => new()
        {
            Nama = nama,
            Jabatan = jabatan,
            Status = StatusPerangkat.Aktif,
            DiperbaruiOleh = "Operator"
        };

        // ---------- tulis & baca ----------

        [Fact]
        public async Task Simpan_DataBaru_MengisiIdDanJejakAudit()
        {
            var hasil = await _svc.SimpanAsync(
                Baru("Asep Sunandar", JabatanPerangkat.KepalaDesa), modeUbah: false);

            Assert.True(hasil.Berhasil);
            Assert.Equal(AlasanSimpanPerangkat.Berhasil, hasil.Alasan);
            Assert.True(hasil.Data!.ID > 0);
            Assert.Equal("Operator", hasil.Data.DibuatOleh);
            Assert.Equal(Sekarang, hasil.Data.CreatedAt);

            var tersimpan = await _svc.AmbilAsync(hasil.Data.ID);
            Assert.NotNull(tersimpan);
            Assert.Equal("Asep Sunandar", tersimpan!.Nama);
            Assert.Equal(JabatanPerangkat.KepalaDesa, tersimpan.JabatanTampil);
        }

        [Fact]
        public async Task Simpan_ModeUbah_TidakMenggantiPembuatDanWaktu()
        {
            var hasil = await _svc.SimpanAsync(
                Baru("Asep Sunandar", JabatanPerangkat.KepalaDesa), modeUbah: false);
            int id = hasil.Data!.ID;

            var ubah = await _svc.AmbilAsync(id);
            ubah!.Nama = "Asep Sunandar, S.Pd.";
            ubah.DiperbaruiOleh = "Kepala Desa";
            var hasilUbah = await _svc.SimpanAsync(ubah, modeUbah: true);

            Assert.True(hasilUbah.Berhasil);
            Assert.Equal(id, hasilUbah.Data!.ID);
            Assert.Equal("Operator", hasilUbah.Data.DibuatOleh);
            Assert.Equal("Kepala Desa", hasilUbah.Data.DiperbaruiOleh);
            Assert.Equal(Sekarang, hasilUbah.Data.CreatedAt);

            var tersimpan = await _svc.AmbilAsync(id);
            Assert.Equal("Asep Sunandar, S.Pd.", tersimpan!.Nama);
        }

        [Fact]
        public async Task Simpan_UbahIdTidakDikenal_DitolakDenganAlasanTidakAda()
        {
            var data = Baru("Warga Baru", JabatanPerangkat.SekretarisDesa);
            data.ID = 9999;

            var hasil = await _svc.SimpanAsync(data, modeUbah: true);

            Assert.False(hasil.Berhasil);
            Assert.Equal(AlasanSimpanPerangkat.TidakAda, hasil.Alasan);
            Assert.Contains(hasil.Kekurangan, k => k.Contains("sudah tidak ada"));
        }

        [Fact]
        public async Task Simpan_UbahTanpaId_Ditolak()
        {
            var hasil = await _svc.SimpanAsync(
                Baru("Warga Baru", JabatanPerangkat.SekretarisDesa), modeUbah: true);

            Assert.False(hasil.Berhasil);
            Assert.Equal(AlasanSimpanPerangkat.WajibDiisi, hasil.Alasan);
            Assert.Contains(hasil.Kekurangan, k => k.Contains("tidak punya ID"));
        }

        [Fact]
        public async Task Hapus_BarisAda_KemudianHilangDariDaftar()
        {
            var hasil = await _svc.SimpanAsync(
                Baru("Rahmat Hidayat", JabatanPerangkat.KetuaBPD), modeUbah: false);

            Assert.True(await _svc.HapusAsync(hasil.Data!.ID));
            Assert.Null(await _svc.AmbilAsync(hasil.Data.ID));
            Assert.False(await _svc.HapusAsync(hasil.Data.ID));
        }

        // ---------- validasi ----------

        [Fact]
        public async Task Simpan_NamaKosong_DitolakDenganIsianYangBermasalah()
        {
            var hasil = await _svc.SimpanAsync(
                Baru("   ", JabatanPerangkat.KepalaDesa), modeUbah: false);

            Assert.False(hasil.Berhasil);
            Assert.Equal(AlasanSimpanPerangkat.WajibDiisi, hasil.Alasan);
            Assert.Contains(hasil.Kekurangan, k => k.Contains("Nama lengkap"));
        }

        [Fact]
        public async Task Simpan_NomorIdentitasSalahPanjang_Ditolak()
        {
            var nik = Baru("Warga NIK", JabatanPerangkat.KetuaRT);
            nik.NIK = "320401010180000"; // 15 digit
            var hasilNik = await _svc.SimpanAsync(nik, modeUbah: false);
            Assert.False(hasilNik.Berhasil);
            Assert.Contains(hasilNik.Kekurangan, k => k.Contains("NIK"));

            var nip = Baru("Warga NIP", JabatanPerangkat.KaurKeuangan);
            nip.NIP = "12345";
            var hasilNip = await _svc.SimpanAsync(nip, modeUbah: false);
            Assert.False(hasilNip.Berhasil);
            Assert.Contains(hasilNip.Kekurangan, k => k.Contains("NIP"));
        }

        [Fact]
        public async Task Simpan_MasaJabatanTerbalik_Ditolak()
        {
            var data = Baru("Pensiunan", JabatanPerangkat.KetuaRW);
            data.MasaJabatanMulai = new DateTime(2025, 1, 1);
            data.MasaJabatanSelesai = new DateTime(2024, 1, 1);

            var hasil = await _svc.SimpanAsync(data, modeUbah: false);

            Assert.False(hasil.Berhasil);
            Assert.Contains(hasil.Kekurangan, k => k.Contains("Masa jabatan selesai"));
        }

        [Fact]
        public async Task Simpan_TanggalLahirDiMasaDepan_Ditolak()
        {
            var data = Baru("Bayi", JabatanPerangkat.Linmas);
            data.TanggalLahir = Sekarang.AddDays(1);

            var hasil = await _svc.SimpanAsync(data, modeUbah: false);

            Assert.False(hasil.Berhasil);
            Assert.Contains(hasil.Kekurangan, k => k.Contains("Tanggal lahir"));
        }

        [Fact]
        public async Task Simpan_MasaJabatanJauhSebelumSK_Ditolak()
        {
            var data = Baru("Bermasalah", JabatanPerangkat.KepalaDesa);
            data.NomorSK = "800/2025";
            data.TanggalSK = new DateTime(2025, 1, 10);
            data.MasaJabatanMulai = new DateTime(2020, 1, 10);

            var hasil = await _svc.SimpanAsync(data, modeUbah: false);

            Assert.False(hasil.Berhasil);
            Assert.Contains(hasil.Kekurangan, k => k.Contains("tanggal SK"));
        }

        [Fact]
        public async Task Simpan_JabatanTidakDikenal_Ditolak()
        {
            var hasil = await _svc.SimpanAsync(Baru("Orang", "KETUA DUNUH"), modeUbah: false);

            Assert.False(hasil.Berhasil);
            Assert.Equal(AlasanSimpanPerangkat.TidakDikenal, hasil.Alasan);
        }

        // ---------- normalisasi ----------

        [Fact]
        public async Task Simpan_InputBerantakan_DirapikanSebelumDisimpan()
        {
            var data = new PerangkatDesa
            {
                Nama = "  Dedi Mulyana  ",
                Jabatan = "kadus",
                NIK = "3204 0101 0180 0009",
                RT = "rt 1",
                RW = "RW-02",
                NomorHP = " 0812-3456-7890 ",
                JenisKelamin = "Laki-laki",
                Status = "aktif",
                DiperbaruiOleh = "  Operator  "
            };

            var hasil = await _svc.SimpanAsync(data, modeUbah: false);

            Assert.True(hasil.Berhasil);
            Assert.Equal("Dedi Mulyana", hasil.Data!.Nama);
            Assert.Equal(JabatanPerangkat.Kadus, hasil.Data.JabatanTampil);
            Assert.Equal("3204010101800009", hasil.Data.NIK);
            Assert.Equal("01", hasil.Data.RT);
            Assert.Equal("02", hasil.Data.RW);
            Assert.Equal("L", hasil.Data.JenisKelamin);
            Assert.Equal(StatusPerangkat.Aktif, hasil.Data.StatusTampil);
            Assert.Equal("Operator", hasil.Data.DiperbaruiOleh);
        }

        [Fact]
        public async Task Simpan_RtSatuDigit_TidakBisaBerebutDenganDuaDigit()
        {
            var pertama = Baru("A", JabatanPerangkat.KetuaRT);
            pertama.Dusun = "Cibogo";
            pertama.RT = "1";
            Assert.True((await _svc.SimpanAsync(pertama, modeUbah: false)).Berhasil);

            var kedua = Baru("B", JabatanPerangkat.KetuaRT);
            kedua.Dusun = "Cibogo";
            kedua.RT = "01";

            var hasil = await _svc.SimpanAsync(kedua, modeUbah: false);

            Assert.False(hasil.Berhasil);
            Assert.Equal(AlasanSimpanPerangkat.JabatanGanda, hasil.Alasan);
        }

        [Fact]
        public async Task Simpan_UbahTidakBertabrakanDenganDirinyaSendiri()
        {
            var hasil = await _svc.SimpanAsync(
                Baru("Siti Aminah", JabatanPerangkat.KetuaRW), modeUbah: false);

            var ubah = await _svc.AmbilAsync(hasil.Data!.ID);
            ubah!.NomorHP = "081299988777";
            var hasilUbah = await _svc.SimpanAsync(ubah, modeUbah: true);

            Assert.True(hasilUbah.Berhasil);
        }

        // ---------- jabatan ganda ----------

        [Fact]
        public async Task Simpan_JabatanSamaWilayahSama_Ditolak()
        {
            var pertama = Baru("Agus", JabatanPerangkat.Kadus);
            pertama.Dusun = "Cibogo";
            Assert.True((await _svc.SimpanAsync(pertama, modeUbah: false)).Berhasil);

            var kedua = Baru("Budi", JabatanPerangkat.Kadus);
            kedua.Dusun = "Cibogo";

            var hasil = await _svc.SimpanAsync(kedua, modeUbah: false);

            Assert.False(hasil.Berhasil);
            Assert.Equal(AlasanSimpanPerangkat.JabatanGanda, hasil.Alasan);
            Assert.Contains("Cibogo", hasil.Kekurangan.First());
        }

        [Fact]
        public async Task Simpan_JabatanSamaWilayahBeda_BolehBerbedaOrang()
        {
            var dusunSatu = Baru("Agus", JabatanPerangkat.Kadus);
            dusunSatu.Dusun = "Cibogo";
            Assert.True((await _svc.SimpanAsync(dusunSatu, modeUbah: false)).Berhasil);

            var dusunDua = Baru("Budi", JabatanPerangkat.Kadus);
            dusunDua.Dusun = "Warung Kaler";

            Assert.True((await _svc.SimpanAsync(dusunDua, modeUbah: false)).Berhasil);
        }

        [Fact]
        public async Task Simpan_PenggantiKepalaDesaSetelahMendadak_Sah_Dan_StatusnyaDipertahankan()
        {
            var lama = Baru("Asep Sunandar", JabatanPerangkat.KepalaDesa);
            Assert.True((await _svc.SimpanAsync(lama, modeUbah: false)).Berhasil);

            // Sejarah: orang pertama berhenti sebelum masa jabatan selesai.
            var ubahLama = await _svc.AmbilAsync(lama.ID);
            ubahLama!.Status = StatusPerangkat.Berhenti;
            ubahLama.MasaJabatanMulai = new DateTime(2019, 8, 1);
            ubahLama.MasaJabatanSelesai = new DateTime(2024, 7, 31);
            Assert.True((await _svc.SimpanAsync(ubahLama, modeUbah: true)).Berhasil);

            // Pengganti baru boleh dicatat walau jabatan dan wilayah sama.
            var baru = Baru("Dedi Mulyana", JabatanPerangkat.KepalaDesa);
            Assert.True((await _svc.SimpanAsync(baru, modeUbah: false)).Berhasil);

            var semua = await _svc.AmbilSemuaAsync(new PerangkatDesaFilter
            {
                Jabatan = JabatanPerangkat.KepalaDesa
            });
            Assert.Equal(2, semua.Count);
            Assert.Contains(semua, p => p.Nama == "Asep Sunandar" && p.StatusTampil == StatusPerangkat.Berhenti);
            Assert.Contains(semua, p => p.Nama == "Dedi Mulyana" && p.StatusTampil == StatusPerangkat.Aktif);

            // Setelah ada yang memegang, jabatan ganda kembali ditolak.
            var ketiga = Baru("Wawan", JabatanPerangkat.KepalaDesa);
            Assert.Equal(
                AlasanSimpanPerangkat.JabatanGanda,
                (await _svc.SimpanAsync(ketiga, modeUbah: false)).Alasan);
        }

        [Fact]
        public async Task Simpan_PenggantiKetuaRtSetelahMasaJabatanSelesai_Sah()
        {
            var lama = Baru("Budi", JabatanPerangkat.KetuaRT);
            lama.Dusun = "Cibogo";
            lama.RT = "01";
            Assert.True((await _svc.SimpanAsync(lama, modeUbah: false)).Berhasil);

            var ubahLama = await _svc.AmbilAsync(lama.ID);
            ubahLama!.Status = StatusPerangkat.Selesai;
            Assert.True((await _svc.SimpanAsync(ubahLama, modeUbah: true)).Berhasil);

            var baru = Baru("Siti Aminah", JabatanPerangkat.KetuaRT);
            baru.Dusun = "Cibogo";
            baru.RT = "01";
            Assert.True((await _svc.SimpanAsync(baru, modeUbah: false)).Berhasil);
        }

        [Fact]
        public async Task Simpan_MenungguSk_JugaMenghalangiJabatanGanda()
        {
            // Status MENUNGGU SK berarti sudah dilantik, jadi jabatan dianggap terisi.
            var dilantik = Baru("Rina", JabatanPerangkat.KetuaBPD);
            dilantik.Status = StatusPerangkat.MenungguSK;
            Assert.True((await _svc.SimpanAsync(dilantik, modeUbah: false)).Berhasil);

            var lain = Baru("Toni", JabatanPerangkat.KetuaBPD);
            Assert.Equal(
                AlasanSimpanPerangkat.JabatanGanda,
                (await _svc.SimpanAsync(lain, modeUbah: false)).Alasan);
        }

        [Fact]
        public async Task Simpan_JabatanBerbedaWilayahSama_TidakBertabrakan()
        {
            var kadus = Baru("Agus", JabatanPerangkat.Kadus);
            kadus.Dusun = "Cibogo";
            Assert.True((await _svc.SimpanAsync(kadus, modeUbah: false)).Berhasil);

            var ketua = Baru("Budi", JabatanPerangkat.KetuaRW);
            ketua.Dusun = "Cibogo";

            Assert.True((await _svc.SimpanAsync(ketua, modeUbah: false)).Berhasil);
        }

        // ---------- pencarian ----------

        [Fact]
        public async Task AmbilSemua_FilterJabatan_Dan_Status_MenyaringBenar()
        {
            var kades = Baru("Asep", JabatanPerangkat.KepalaDesa);
            Assert.True((await _svc.SimpanAsync(kades, modeUbah: false)).Berhasil);

            var rt = Baru("Budi", JabatanPerangkat.KetuaRT);
            rt.Dusun = "Cibogo";
            rt.RT = "01";
            Assert.True((await _svc.SimpanAsync(rt, modeUbah: false)).Berhasil);

            var lemma = Baru("Tini", JabatanPerangkat.KetuaBPD);
            lemma.Status = StatusPerangkat.Berhenti;
            Assert.True((await _svc.SimpanAsync(lemma, modeUbah: false)).Berhasil);

            var perJabatan = await _svc.AmbilSemuaAsync(new PerangkatDesaFilter
            {
                Jabatan = JabatanPerangkat.KetuaRT
            });
            Assert.Single(perJabatan);
            Assert.Equal("Budi", perJabatan[0].Nama);

            var perStatus = await _svc.AmbilSemuaAsync(new PerangkatDesaFilter
            {
                Status = StatusPerangkat.Berhenti
            });
            Assert.Single(perStatus);
            Assert.Equal("Tini", perStatus[0].Nama);

            Assert.Equal(3, (await _svc.AmbilSemuaAsync()).Count);
        }

        [Fact]
        public void KatalogKelompok_MembagiSeluruhJabatanTanpaSisa()
        {
            // Setiap jabatan resmi harus masuk tepat satu kelompok yang dipakai menu
            // sidebar: jabatan yang tidak terkelompok akan hilang dari semua menu.
            var semuaKelompok = JabatanPerangkat.UrutanKelompok
                .SelectMany(k => JabatanPerangkat.DaftarKelompok(k))
                .ToList();

            Assert.Equal(JabatanPerangkat.Semua.Count, semuaKelompok.Count);
            Assert.Equal(
                JabatanPerangkat.Semua.OrderBy(j => j, StringComparer.Ordinal),
                semuaKelompok.OrderBy(j => j, StringComparer.Ordinal));

            // Nama menu yang tampil di sidebar dipaku di sini supaya penambahan
            // kelompok baru tidak diam-diam muncul tanpa nama yang layak dibaca.
            var namaMenu = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["PERANGKAT DESA"] = "Perangkat Desa",
                ["RT/RW"] = "RT/RW",
                ["LINMAS"] = "Linmas",
                ["LPM"] = "LPM",
                ["PKK"] = "PKK",
                ["POSYANDU"] = "Posyandu",
                ["BPD"] = "BPD",
                ["KADER STUNTING"] = "Kader Stunting",
                ["KADER KPM"] = "Kader KPM",
                ["LAINNYA"] = "Lainnya"
            };

            foreach (var kelompok in JabatanPerangkat.UrutanKelompok)
            {
                Assert.True(namaMenu.ContainsKey(kelompok), $"Kelompok baru belum punya nama menu: {kelompok}");
                Assert.Equal(namaMenu[kelompok], JabatanPerangkat.TampilanKelompok(kelompok));
            }

            // Kelompok yang tidak dikenal tidak menyaring apa pun (daftarnya kosong).
            Assert.Empty(JabatanPerangkat.DaftarKelompok("KELOMPOK TIDAK ADA"));
            Assert.Empty(JabatanPerangkat.DaftarKelompok(string.Empty));
        }

        [Fact]
        public async Task AmbilSemua_FilterKelompok_MenyaringHanyaKelompokItu()
        {
            var kades = Baru("Asep", JabatanPerangkat.KepalaDesa);
            Assert.True((await _svc.SimpanAsync(kades, modeUbah: false)).Berhasil);

            var kaur = Baru("Rina", JabatanPerangkat.KaurKeuangan);
            Assert.True((await _svc.SimpanAsync(kaur, modeUbah: false)).Berhasil);

            var rt = Baru("Budi", JabatanPerangkat.KetuaRT);
            rt.Dusun = "Cibogo";
            rt.RT = "01";
            Assert.True((await _svc.SimpanAsync(rt, modeUbah: false)).Berhasil);

            var linmas = Baru("Wawan", JabatanPerangkat.Linmas);
            Assert.True((await _svc.SimpanAsync(linmas, modeUbah: false)).Berhasil);

            // Kelompok besar memuat beberapa jabatan sekaligus.
            var perangkat = await _svc.AmbilSemuaAsync(new PerangkatDesaFilter
            {
                Kelompok = "PERANGKAT DESA"
            });
            Assert.Equal(2, perangkat.Count);
            Assert.Contains(perangkat, p => p.Nama == "Asep");
            Assert.Contains(perangkat, p => p.Nama == "Rina");

            var rtRw = await _svc.AmbilSemuaAsync(new PerangkatDesaFilter
            {
                Kelompok = "RT/RW"
            });
            Assert.Single(rtRw);
            Assert.Equal("Budi", rtRw[0].Nama);

            // Nama kelompok yang tidak dikenal menghasilkan daftar kosong, bukan
            // seluruh baris (filter yang bocor akan menyesatkan operator).
            Assert.Empty(await _svc.AmbilSemuaAsync(new PerangkatDesaFilter
            {
                Kelompok = "KELOMPOK TIDAK ADA"
            }));

            // Tanpa kelompok, seluruh perangkat kembali tampil.
            Assert.Equal(4, (await _svc.AmbilSemuaAsync()).Count);
        }

        [Fact]
        public async Task AmbilSemua_FilterKelompok_Dan_Jabatan_BekerjaBersama()
        {
            var kades = Baru("Asep", JabatanPerangkat.KepalaDesa);
            Assert.True((await _svc.SimpanAsync(kades, modeUbah: false)).Berhasil);

            var kaur = Baru("Rina", JabatanPerangkat.KaurKeuangan);
            Assert.True((await _svc.SimpanAsync(kaur, modeUbah: false)).Berhasil);

            // Kelompok "PERANGKAT DESA" + jabatan Kaur Keuangan → hanya Rina, sebab
            // syarat kelompok dan jabatan digabung, bukan saling menimpa.
            var hasil = await _svc.AmbilSemuaAsync(new PerangkatDesaFilter
            {
                Kelompok = "PERANGKAT DESA",
                Jabatan = JabatanPerangkat.KaurKeuangan
            });
            Assert.Single(hasil);
            Assert.Equal("Rina", hasil[0].Nama);

            // Kelompok benar, jabatan di luar kelompok itu → kosong.
            Assert.Empty(await _svc.AmbilSemuaAsync(new PerangkatDesaFilter
            {
                Kelompok = "RT/RW",
                Jabatan = JabatanPerangkat.KaurKeuangan
            }));
        }

        [Fact]
        public async Task AmbilSemua_Cari_LihatNamaNomorIdentitasDanWilayah()
        {
            var kades = Baru("Asep Sunandar", JabatanPerangkat.KepalaDesa);
            kades.NIP = "198504122010011001";
            kades.NomorSK = "800/2025";
            Assert.True((await _svc.SimpanAsync(kades, modeUbah: false)).Berhasil);

            var rt = Baru("Budi", JabatanPerangkat.KetuaRT);
            rt.Dusun = "Cibogo";
            Assert.True((await _svc.SimpanAsync(rt, modeUbah: false)).Berhasil);

            Assert.Single(await _svc.AmbilSemuaAsync(new PerangkatDesaFilter { Cari = "Asep" }));
            Assert.Single(await _svc.AmbilSemuaAsync(new PerangkatDesaFilter { Cari = "2010011" }));
            Assert.Single(await _svc.AmbilSemuaAsync(new PerangkatDesaFilter { Cari = "800/2025" }));
            Assert.Single(await _svc.AmbilSemuaAsync(new PerangkatDesaFilter { Cari = "Cibogo" }));
            Assert.Empty(await _svc.AmbilSemuaAsync(new PerangkatDesaFilter { Cari = "Zainal" }));
        }

        [Fact]
        public async Task AmbilNilaiWilayah_MengumpulkanDusunRtRwTanpaDuplikat()
        {
            foreach (var dusun in new[] { "Cibogo", "Warung Kaler" })
            {
                var kades = Baru("Kades " + dusun, JabatanPerangkat.KepalaDesa);
                kades.Dusun = dusun;
                Assert.True((await _svc.SimpanAsync(kades, modeUbah: false)).Berhasil);
            }

            var rt = Baru("Budi", JabatanPerangkat.KetuaRT);
            rt.Dusun = "Cibogo";
            rt.RT = "01";
            rt.RW = "02";
            Assert.True((await _svc.SimpanAsync(rt, modeUbah: false)).Berhasil);

            var nilai = await _svc.AmbilNilaiWilayahAsync();

            Assert.Equal(nilai.Distinct(StringComparer.OrdinalIgnoreCase).Count(), nilai.Count);
            Assert.Contains("Cibogo", nilai);
            Assert.Contains("Warung Kaler", nilai);
            Assert.Contains("01", nilai);
            Assert.Contains("02", nilai);
        }

        // ---------- rekap ----------

        [Fact]
        public async Task AmbilStatistik_HanyaSekretaris_JabatanIntiKosongDuaBuah()
        {
            var sekretaris = Baru("Sari", JabatanPerangkat.SekretarisDesa);
            Assert.True((await _svc.SimpanAsync(sekretaris, modeUbah: false)).Berhasil);

            var stat = await _svc.AmbilStatistikAsync();

            Assert.Equal(1, stat.Total);
            Assert.Equal(1, stat.Aktif);
            Assert.DoesNotContain(JabatanPerangkat.SekretarisDesa, stat.JabatanIntiKosong);
            Assert.Contains(JabatanPerangkat.KepalaDesa, stat.JabatanIntiKosong);
            Assert.Contains(JabatanPerangkat.KetuaBPD, stat.JabatanIntiKosong);
        }

        [Fact]
        public async Task AmbilStatistik_StrukturIntiLengkap_TidakAdaJabatanKosong()
        {
            foreach (var jabatan in JabatanPerangkat.Inti)
            {
                bool ok = (await _svc.SimpanAsync(
                    Baru("Pemangku " + jabatan, jabatan), modeUbah: false)).Berhasil;
                Assert.True(ok);
            }

            var stat = await _svc.AmbilStatistikAsync();

            Assert.Equal(JabatanPerangkat.Inti.Count, stat.Total);
            Assert.Empty(stat.JabatanIntiKosong);
        }

        [Fact]
        public async Task AmbilStatistik_JabatanIntiDipegangYangBerhenti_TetapKosong()
        {
            var kades = Baru("Asep", JabatanPerangkat.KepalaDesa);
            kades.Status = StatusPerangkat.Berhenti;
            Assert.True((await _svc.SimpanAsync(kades, modeUbah: false)).Berhasil);

            var stat = await _svc.AmbilStatistikAsync();

            Assert.Equal(1, stat.Total);
            Assert.Equal(1, stat.Berhenti);
            Assert.Contains(JabatanPerangkat.KepalaDesa, stat.JabatanIntiKosong);
        }

        [Fact]
        public async Task AmbilStatistik_MenghitungSetiapStatus()
        {
            var daftar = new (string Nama, string Jabatan, string Status)[]
            {
                ("Asep", JabatanPerangkat.KepalaDesa, StatusPerangkat.Aktif),
                ("Sari", JabatanPerangkat.SekretarisDesa, StatusPerangkat.MenungguSK),
                ("Tini", JabatanPerangkat.KetuaBPD, StatusPerangkat.Selesai),
                ("Agus", JabatanPerangkat.KetuaRW, StatusPerangkat.Berhenti)
            };

            foreach (var item in daftar)
            {
                var data = Baru(item.Nama, item.Jabatan);
                data.Status = item.Status;
                Assert.True((await _svc.SimpanAsync(data, modeUbah: false)).Berhasil);
            }

            var stat = await _svc.AmbilStatistikAsync();

            Assert.Equal(4, stat.Total);
            Assert.Equal(1, stat.Aktif);
            Assert.Equal(1, stat.MenungguSK);
            Assert.Equal(1, stat.Selesai);
            Assert.Equal(1, stat.Berhenti);

            // Ketua BPD-nya sudah selesai masa jabatannya, jadi desa ini belum
            // punya Ketua BPD yang sedang memegang jabatan.
            Assert.Contains(JabatanPerangkat.KetuaBPD, stat.JabatanIntiKosong);
            Assert.DoesNotContain(JabatanPerangkat.KepalaDesa, stat.JabatanIntiKosong);
            Assert.DoesNotContain(JabatanPerangkat.SekretarisDesa, stat.JabatanIntiKosong);
        }

        // ---------- daftar jabatan ----------

        [Fact]
        public void JabatanPerangkat_KelompokDanValidasi_SesuaiDaftar()
        {
            Assert.Equal("PERANGKAT DESA", JabatanPerangkat.Kelompok(JabatanPerangkat.KepalaDesa));
            Assert.Equal("RT/RW", JabatanPerangkat.Kelompok(JabatanPerangkat.KetuaRT));
            Assert.Equal("BPD", JabatanPerangkat.Kelompok(JabatanPerangkat.KetuaBPD));

            Assert.True(JabatanPerangkat.Valid("kepala desa"));
            Assert.False(JabatanPerangkat.Valid("ketua dusun"));
            Assert.Equal(JabatanPerangkat.KetuaRT, JabatanPerangkat.Normalisasi("ketua rt"));
        }

        [Fact]
        public void PerangkatDesa_WilayahDanMasaJabatan_TampilRingkas()
        {
            var rt = new PerangkatDesa { RT = "01", RW = "02" };
            Assert.Equal("RT 01/RW 02", rt.WilayahRingkas);
            Assert.Equal("-", new PerangkatDesa().WilayahRingkas);

            var dusun = new PerangkatDesa { Dusun = "Cibogo", RT = "01" };
            Assert.Equal("Cibogo, RT 01/RW -", dusun.WilayahRingkas);

            var masa = new PerangkatDesa
            {
                MasaJabatanMulai = new DateTime(2025, 1, 1),
                MasaJabatanSelesai = new DateTime(2029, 1, 1)
            };
            Assert.Equal("01-01-2025 s/d 01-01-2029", masa.MasaJabatanTampil);

            var tanpaSelesai = new PerangkatDesa
            {
                MasaJabatanMulai = new DateTime(2025, 1, 1)
            };
            Assert.Equal("01-01-2025", tanpaSelesai.MasaJabatanTampil);
            Assert.Equal("-", new PerangkatDesa().MasaJabatanTampil);
        }
    }
}
