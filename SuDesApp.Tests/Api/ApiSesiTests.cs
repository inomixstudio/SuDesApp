using SuDesApp.Api;
using System;
using System.Linq;
using Xunit;

namespace SuDesApp.Tests.Api
{
    /// <summary>
    /// Sesi login listener API dipakai aplikasi Android untuk masuk dengan akun
    /// desktop. Tiga sifat yang dijaga: token tidak bisa ditebak, sesi berhenti
    /// berlaku saat waktunya habis, dan keluar benar-benar mematikan token.
    /// </summary>
    public class ApiSesiTests
    {
        [Fact]
        public void Token_baru_tidak_sama_dan_panjang()
        {
            var sesi = new ApiSesi();

            var a = sesi.Buat("budi", "Budi Santoso", "Administrator");
            var b = sesi.Buat("budi", "Budi Santoso", "Administrator");

            Assert.NotEqual(a.Token, b.Token);
            Assert.Equal(64, a.Token.Length);
            Assert.Equal(2, sesi.Jumlah);
        }

        [Fact]
        public void Sesi_bisa_diambil_dengan_token()
        {
            var sesi = new ApiSesi();
            var dibuat = sesi.Buat("siti", "Siti Aminah", "Operator");

            var diambil = sesi.Ambil(dibuat.Token);

            Assert.NotNull(diambil);
            Assert.Equal("siti", diambil!.Username);
            Assert.Equal("Operator", diambil.Peran);
        }

        [Fact]
        public void Token_kosong_atau_asing_ditolak()
        {
            var sesi = new ApiSesi();
            sesi.Buat("budi", "Budi", "Operator");

            Assert.Null(sesi.Ambil(null));
            Assert.Null(sesi.Ambil("  "));
            Assert.Null(sesi.Ambil("token-yang-tidak-pernah-dibuat"));
        }

        [Fact]
        public void Sesi_kedaluwarsa_ditolak_dan_dibuang()
        {
            var sekarang = new DateTime(2026, 10, 31, 9, 0, 0);
            var sesi = new ApiSesi(() => sekarang);
            var dibuat = sesi.Buat("budi", "Budi", "Operator");

            Assert.NotNull(sesi.Ambil(dibuat.Token));

            // Lewat 12 jam: token yang sama tidak boleh diterima lagi.
            sekarang = sekarang.AddHours(12).AddSeconds(1);

            Assert.Null(sesi.Ambil(dibuat.Token));
            Assert.Equal(0, sesi.Jumlah);
        }

        [Fact]
        public void Keluar_menghapus_token_dan_hanya_token_itu()
        {
            var sesi = new ApiSesi();
            var hp = sesi.Buat("budi", "Budi", "Operator");
            var lain = sesi.Buat("siti", "Siti", "Operator");

            Assert.True(sesi.Akhiri(hp.Token));

            Assert.Null(sesi.Ambil(hp.Token));
            Assert.NotNull(sesi.Ambil(lain.Token));
        }

        [Fact]
        public void AkhiriSemua_memutus_semua_perangkat_satu_akun()
        {
            var sesi = new ApiSesi();
            var hp1 = sesi.Buat("budi", "Budi", "Operator");
            var hp2 = sesi.Buat("BUDI", "Budi", "Operator");
            var lain = sesi.Buat("siti", "Siti", "Operator");

            Assert.Equal(2, sesi.AkhiriSemua("budi"));

            Assert.Null(sesi.Ambil(hp1.Token));
            Assert.Null(sesi.Ambil(hp2.Token));
            Assert.NotNull(sesi.Ambil(lain.Token));
        }

        [Fact]
        public void Rute_sesi_dan_pengaturan_terdaftar_dengan_metode_benar()
        {
            Assert.NotNull(ApiRute.Cocok("POST", ApiRute.Prefix + "/sesi").Endpoint);
            Assert.NotNull(ApiRute.Cocok("DELETE", ApiRute.Prefix + "/sesi").Endpoint);
            Assert.NotNull(ApiRute.Cocok("GET", ApiRute.Prefix + "/sesi/siapa").Endpoint);
            Assert.NotNull(ApiRute.Cocok("GET", ApiRute.Prefix + "/pengaturan").Endpoint);

            // Metode yang tidak didaftarkan tetap menjawab 405, bukan 404.
            var salahMetode = ApiRute.Cocok("PUT", ApiRute.Prefix + "/pengaturan");
            Assert.True(salahMetode.MetodeTidakDiizinkan);
            Assert.False(salahMetode.Ditemukan);
        }

        [Fact]
        public void Endpoint_sesi_dan_pengaturan_wajib_sesi_dan_hanya_kunci_penuh()
        {
            var butuhSesi = ApiRute.Semua
                .Where(e => e.Jalur.StartsWith("/sesi", StringComparison.Ordinal) || e.Jalur == "/pengaturan")
                .ToList();

            Assert.Equal(4, butuhSesi.Count);

            foreach (var endpoint in butuhSesi.Where(e => e.Metode != "POST"))
            {
                Assert.True(endpoint.ButuhSesi, endpoint.BarisTampil + " harus butuh sesi");
            }

            var agregat = new ApiKunciEntry { Nilai = "x", Cakupan = ApiCakupan.Agregat };
            foreach (var endpoint in butuhSesi)
            {
                Assert.False(
                    ApiAutentikasi.CakupanCukup(agregat.Cakupan, endpoint),
                    endpoint.BarisTampil + " tidak boleh terbuka oleh kunci agregat");
                Assert.True(ApiAutentikasi.CakupanCukup(ApiCakupan.Penuh, endpoint));
            }
        }
    }
}
