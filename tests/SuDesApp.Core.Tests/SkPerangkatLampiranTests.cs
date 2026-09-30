using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using QuestPDF.Fluent;

using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.GeneratorPdf;
using SuDesApp.Services;
using SuDesApp.Utilities;

using Xunit;

namespace SuDesApp.Core.Tests
{
    /// <summary>
    /// Uji lampiran daftar nama pada SK perangkat: satu SK untuk banyak orang
    /// sekaligus, dengan kolom pekerjaan/agama/golongan darah/status kawin dari
    /// data Warga (lewat NIK) dan pengelompokan per unit — mis. satu blok untuk
    /// setiap Posyandu.
    ///
    /// Uji terakhir membaca teks yang benar-benar tercetak per halaman (QuestPDF
    /// bisa menghasilkan SVG per halaman), sebab isi PDF terkompresi dan tidak
    /// bisa diperiksa langsung dari byte-nya.
    /// </summary>
    public sealed class SkPerangkatLampiranTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly WargaRepository _wargaRepo;

        public SkPerangkatLampiranTests()
        {
            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();
            _connection.ExecuteScript(CoreTestFixture.ReadProjectFile("desa.db.sql"));
            _connection.ExecuteNonQuery("DELETE FROM Warga;");

            _wargaRepo = new WargaRepository(
                _connection,
                new MemoryCacheService(
                    new MemoryCache(new MemoryCacheOptions()),
                    NullLogger<MemoryCacheService>.Instance),
                Konfigurasi(),
                NullLogger<WargaRepository>.Instance);
        }

        public void Dispose() => _connection.Dispose();

        // ---------- pengelompokan unit ----------

        [Fact]
        public void Kelompokkan_MenomoriUlangPerUnit_DanMenjagaUrutanMuncul()
        {
            var blok = SkPerangkatLampiran.Kelompokkan(new[]
            {
                Orang("Siti Aminah", unit: "POSYANDU SAKURA I"),
                Orang("Nurhayati", unit: "POSYANDU SAKURA I"),
                Orang("Yanti", unit: "POSYANDU SAKURA II"),
                Orang("Darwati", unit: "POSYANDU SAKURA I")
            });

            Assert.Equal(2, blok.Count);
            Assert.Equal("POSYANDU SAKURA I", blok[0].Unit);
            Assert.Equal("POSYANDU SAKURA II", blok[1].Unit);

            // Nomor dimulai lagi dari 1 pada setiap unit (setiap Posyandu punya
            // daftar 1..n sendiri, sama seperti contoh SK Posyandu).
            Assert.Equal(new[] { 1, 2, 3 }, blok[0].Baris.Select(b => b.Nomor));
            Assert.Equal(
                new[] { "Siti Aminah", "Nurhayati", "Darwati" },
                blok[0].Baris.Select(b => b.Orang.Nama));
            Assert.Equal(new[] { 1 }, blok[1].Baris.Select(b => b.Nomor));

            // Unit dicocokkan tanpa membedakan huruf besar/kecil, dan spasi tepi
            // tidak membuat blok terpisah.
            var sama = SkPerangkatLampiran.Kelompokkan(new[]
            {
                Orang("A", unit: "Pokjanal"),
                Orang("B", unit: " POKJANAL ")
            });
            Assert.Single(sama);
            Assert.Equal(2, sama[0].Jumlah);
        }

        [Fact]
        public void Kelompokkan_MembuangBarisTanpaNama()
        {
            var blok = SkPerangkatLampiran.Kelompokkan(new[]
            {
                Orang("Siti Aminah", unit: "SAKURA I"),
                Orang("   ", unit: "SAKURA I"),
                Orang("Yanti")
            });

            Assert.Equal(2, SkPerangkatLampiran.HitungBaris(new[]
            {
                Orang("Siti Aminah", unit: "SAKURA I"),
                Orang("   ", unit: "SAKURA I"),
                Orang("Yanti")
            }));

            // Baris tanpa unit tidak bercampur dengan baris berunit.
            Assert.Equal(2, blok.Count);
            Assert.Equal("SAKURA I", blok[0].Unit);
            Assert.Equal(string.Empty, blok[1].Unit);
            Assert.Equal("Yanti", blok[1].Baris[0].Orang.Nama);
        }

        [Fact]
        public void PerluJudulUnit_BenarBilaBanyakUnitAtauUnitBernama()
        {
            Assert.False(SkPerangkatLampiran.PerluJudulUnit(
                SkPerangkatLampiran.Kelompokkan(new[] { Orang("A"), Orang("B") })));

            Assert.True(SkPerangkatLampiran.PerluJudulUnit(
                SkPerangkatLampiran.Kelompokkan(new[] { Orang("A", unit: "POKJANAL") })));

            Assert.True(SkPerangkatLampiran.PerluJudulUnit(
                SkPerangkatLampiran.Kelompokkan(new[]
                {
                    Orang("A", unit: "SAKURA I"),
                    Orang("B", unit: "SAKURA II")
                })));
        }

        // ---------- kolom tabel ----------

        [Fact]
        public void JudulKolomLampiran_SelaluNoDanNama_KolomLainSesuaiIsi()
        {
            // Hanya nama yang ada: kolom pekerjaan/agama/golongan darah/status kawin
            // tidak dicetak, sebab lampiran tidak boleh penuh tanda hubung.
            var kolom = SkPerangkatGenerator.JudulKolomLampiran(new[]
            {
                Orang("Siti Aminah", pekerjaan: "Petani")
            });

            Assert.Equal(new[] { "NO", "NAMA", "PEKERJAAN" }, kolom);
            Assert.DoesNotContain("GOL. DARAH", kolom);
            Assert.DoesNotContain("AGAMA", kolom);
        }

        [Fact]
        public void JudulKolomLampiran_UrutDanMemuatKolomDataWarga()
        {
            var kolom = SkPerangkatGenerator.JudulKolomLampiran(new[]
            {
                Orang("Siti Aminah", pekerjaan: "Petani", agama: "Islam", gol: "O",
                    statusKawin: "Kawin", ttl: "Karawang, 07-08-1977", pendidikan: "SMA",
                    alamat: "Dusun Uji RT 001/RW 002")
            });

            Assert.Equal(
                new[] { "NO", "NAMA", "TEMPAT & TGL. LAHIR", "PENDIDIKAN", "PEKERJAAN", "ALAMAT", "AGAMA", "GOL. DARAH", "STATUS KAWIN" },
                kolom);
        }

        [Fact]
        public void JudulKolomLampiran_JabatanHanyaBilaPeranBerbeda()
        {
            // Anggota Linmas semuanya berperan sama: kolom JABATAN tidak perlu
            // mengulang satu kata di setiap baris.
            var seragam = SkPerangkatGenerator.JudulKolomLampiran(new[]
            {
                Orang("A", peran: "LINMAS"),
                Orang("B", peran: "LINMAS")
            });
            Assert.DoesNotContain("JABATAN", seragam);

            var berbeda = SkPerangkatGenerator.JudulKolomLampiran(new[]
            {
                Orang("A", peran: "Ketua"),
                Orang("B", peran: "Sekretaris")
            });
            Assert.Contains("JABATAN", berbeda);
            Assert.Equal(new[] { "NO", "NAMA", "JABATAN" }, berbeda);
        }

        // ---------- kalimat lampiran ----------

        [Fact]
        public void IsiPlaceholder_PadaModeLampiran_MenggantiSaudaraISeNama()
        {
            var desa = DesaUji();
            var isiLampiran = new SkPerangkatIsi
            {
                Kelompok = "POSYANDU",
                Jabatan = JabatanPerangkat.KaderPosyandu,
                Lampiran = new[] { Orang("Siti Aminah", unit: "SAKURA I") }
            };

            var template = SkPerangkatKatalog.Cari("POSYANDU")!;
            foreach (var butir in template.Menimbang)
            {
                string teks = SkPerangkatGenerator.IsiPlaceholder(butir, isiLampiran, desa);
                Assert.DoesNotContain("saudara/i", teks);
                Assert.DoesNotContain("{", teks);
            }

            Assert.Contains(
                "nama-nama sebagaimana tercantum dalam Lampiran",
                SkPerangkatGenerator.IsiPlaceholder(template.Menimbang[1], isiLampiran, desa));

            // Tanpa lampiran, kalimatnya tetap menyebut orangnya.
            var tanpaLampiran = new SkPerangkatIsi
            {
                Kelompok = "POSYANDU",
                Nama = "Siti Aminah",
                Jabatan = JabatanPerangkat.KaderPosyandu
            };
            Assert.Contains("Siti Aminah",
                SkPerangkatGenerator.IsiPlaceholder(template.Menimbang[1], tanpaLampiran, desa));
        }

        [Fact]
        public void KetentuanLampiran_DanMenimbangPemberhentianLampiran_TanpaSisaPlaceholder()
        {
            var desa = DesaUji();
            var isi = new SkPerangkatIsi
            {
                Kelompok = "LINMAS",
                Nomor = "340/01-Kep/Ds/2026",
                Tanggal = new DateTime(2026, 9, 29),
                Jabatan = JabatanPerangkat.Linmas,
                Wilayah = "Dusun Uji, RT 001/RW 001",
                Mulai = new DateTime(2026, 9, 29),
                Selesai = new DateTime(2029, 9, 28),
                Alasan = "karena masa jabatan telah berakhir",
                Lampiran = new[] { Orang("Wawan", unit: "DUSUN UJI") }
            };

            var semua = new List<string>
            {
                SkPerangkatKatalog.KetentuanLampiranUmum(pengangkatan: true),
                SkPerangkatKatalog.KetentuanLampiranUmum(pengangkatan: false)
            };
            semua.AddRange(SkPerangkatKatalog.MenimbangPemberhentianLampiran);

            foreach (var teks in semua)
            {
                string jadi = SkPerangkatGenerator.IsiPlaceholder(teks, isi, desa);
                Assert.DoesNotContain("{", jadi);
                Assert.DoesNotContain("}", jadi);
                Assert.False(string.IsNullOrWhiteSpace(jadi));
            }

            // Yang merujuk lampiran hanyalah kalimat keputusan dan pertimbangan
            // pertama; butir pertimbangan lain berlaku sama untuk satu orang.
            Assert.Contains("Lampiran",
                SkPerangkatGenerator.IsiPlaceholder(SkPerangkatKatalog.MenimbangPemberhentianLampiran[0], isi, desa));
            Assert.Contains("Lampiran",
                SkPerangkatGenerator.IsiPlaceholder(SkPerangkatKatalog.KetentuanLampiranUmum(pengangkatan: true), isi, desa));
            Assert.Contains("Lampiran",
                SkPerangkatGenerator.IsiPlaceholder(SkPerangkatKatalog.KetentuanLampiranUmum(pengangkatan: false), isi, desa));

            // Kalimat keputusan lampiran menyebut jabatan dan tidak menyebut satu nama.
            string ketentuan = SkPerangkatGenerator.IsiPlaceholder(
                SkPerangkatKatalog.KetentuanLampiranUmum(pengangkatan: true), isi, desa);
            Assert.Contains("sebagai LINMAS", ketentuan);
            Assert.DoesNotContain("Wawan", ketentuan);
        }

        // ---------- gabung data Warga lewat NIK ----------

        [Fact]
        public async Task SusunAsync_KolomPribadiDiambilDariDataWarga()
        {
            await _wargaRepo.AddOrUpdateWargaAsync(Warga(
                "3204010101850002", "Siti Aminah",
                pekerjaan: "Guru", agama: "Islam", gol: "O", statusKawin: "Kawin",
                tempatLahir: "Karawang", tanggalLahir: "07-08-1977", pendidikan: "S1"));

            var layanan = new SkPerangkatLampiranService(
                _wargaRepo, NullLogger<SkPerangkatLampiranService>.Instance);

            var baris = await layanan.SusunAsync(new[]
            {
                new LampiranSumber
                {
                    Orang = Perangkat("Siti Aminah", "3204010101850002", JabatanPerangkat.KaderPosyandu),
                    Unit = "POSYANDU SAKURA I"
                }
            });

            var satu = Assert.Single(baris);
            Assert.Equal("Siti Aminah", satu.Nama);
            Assert.Equal("3204010101850002", satu.NIK);
            Assert.Equal("Guru", satu.Pekerjaan);
            Assert.Equal("Islam", satu.Agama);
            Assert.Equal("O", satu.GolonganDarah);
            Assert.Equal("Kawin", satu.StatusPerkawinan);
            Assert.Equal("Karawang, 07-08-1977", satu.TempatTanggalLahir);
            Assert.Equal("POSYANDU SAKURA I", satu.Unit);

            // Peran kosong jatuh ke jabatan orangnya, supaya kolom JABATAN tidak
            // pernah tercetak kosong hanya karena perannya tidak diisi.
            Assert.Equal(JabatanPerangkat.KaderPosyandu, satu.Peran);
        }

        [Fact]
        public async Task SusunAsync_NikTidakAdaTetapDicetakDenganKolomKosong()
        {
            var layanan = new SkPerangkatLampiranService(
                _wargaRepo, NullLogger<SkPerangkatLampiranService>.Instance);

            var baris = await layanan.SusunAsync(new[]
            {
                new LampiranSumber { Orang = Perangkat("Tanpa Data", "3204010199999999") },
                new LampiranSumber { Orang = Perangkat("Tanpa Nik", null) }
            });

            Assert.Equal(2, baris.Count);
            Assert.All(baris, b =>
            {
                Assert.False(string.IsNullOrWhiteSpace(b.Nama));
                Assert.Equal(string.Empty, b.Pekerjaan);
                Assert.Equal(string.Empty, b.Agama);
                Assert.Equal(string.Empty, b.GolonganDarah);
                Assert.Equal(string.Empty, b.StatusPerkawinan);
            });
        }

        [Fact]
        public async Task SusunSatuAsync_NikKosongAtauTidakDikenal_MengembalikanNull()
        {
            var layanan = new SkPerangkatLampiranService(
                _wargaRepo, NullLogger<SkPerangkatLampiranService>.Instance);

            Assert.Null(await layanan.SusunSatuAsync(null));
            Assert.Null(await layanan.SusunSatuAsync("   "));
            Assert.Null(await layanan.SusunSatuAsync("3204010199999999"));
        }

        [Fact]
        public async Task SusunSatuAsync_WargaAda_MengambilNamaDanDataPribadinya()
        {
            await _wargaRepo.AddOrUpdateWargaAsync(Warga(
                "3204010101900003", "Nurhayati",
                pekerjaan: "Ibu Rumah Tangga", agama: "Islam", gol: "A", statusKawin: "Kawin",
                tempatLahir: "Karawang", tanggalLahir: "01-02-1980"));

            var layanan = new SkPerangkatLampiranService(
                _wargaRepo, NullLogger<SkPerangkatLampiranService>.Instance);

            var baris = await layanan.SusunSatuAsync("3204010101900003", peran: "Sekretaris", unit: "SAKURA I");

            Assert.NotNull(baris);
            Assert.Equal("Nurhayati", baris!.Nama);
            Assert.Equal("Sekretaris", baris.Peran);
            Assert.Equal("SAKURA I", baris.Unit);
            Assert.Equal("Ibu Rumah Tangga", baris.Pekerjaan);
            Assert.Equal("A", baris.GolonganDarah);
        }

        // ---------- teks yang benar-benar tercetak ----------

        [Fact]
        public async Task Generator_SkBerlampiran_MencetakHalamanLampiranPerUnit()
        {
            await DuaWarga();

            var halaman = HalamanTercetak(SkIsi(adaLampiran: true));

            // Halaman badan menyebut lampiran, bukan satu nama.
            Assert.Contains("nama-nama sebagaimana tercantum dalam Lampiran", halaman[0]);

            // Halaman lampiran adalah halaman terakhir dan memuat judul, unit, nama,
            // serta kolom data warga.
            string lampiran = halaman[^1];
            Assert.Contains("Lampiran Keputusan Kepala Desa", lampiran);
            Assert.Contains("DAFTAR PENGANGKATAN KADER POSYANDU DESA SUMBERJAYA", lampiran);
            Assert.Contains("UNIT POSYANDU SAKURA I", lampiran);
            Assert.Contains("UNIT POSYANDU SAKURA II", lampiran);
            Assert.Contains("Siti Aminah", lampiran);
            Assert.Contains("Yanti", lampiran);
            // Judul kolom panjang terbungkus dua baris, dan QuestPDF menggambar
            // potongan teks per baris (bukan per sel), jadi setiap kata judul
            // diperiksa sendiri — "GOL. DARAH" bisa terbelah oleh kolom sebelahnya.
            foreach (var kata in new[]
                     {
                         "NO", "NAMA", "JABATAN", "TEMPAT", "LAHIR", "PEKERJAAN",
                         "ALAMAT", "AGAMA", "GOL.", "DARAH", "STATUS", "KAWIN"
                     })
            {
                Assert.Contains(kata, lampiran);
            }

            // Data warga ikut tercetak pada baris namanya, bukan hanya judul kolom.
            Assert.Contains("Guru", lampiran);
            Assert.Contains("Islam", lampiran);
            Assert.Contains("Kawin", lampiran);
            Assert.Contains("Dusun Uji RT 001/RW 002", lampiran);

            // Halaman lampiran tidak memakai kop surat: tidak ada baris PEMERINTAH.
            Assert.DoesNotContain("PEMERINTAH KABUPATEN", lampiran);
        }

        [Fact]
        public void Generator_SkTanpaLampiran_TidakMencetakHalamanLampiran()
        {
            var halaman = HalamanTercetak(SkIsi(adaLampiran: false));

            Assert.All(halaman, h => Assert.DoesNotContain("Lampiran Keputusan", h));
            Assert.Contains("Siti Aminah", string.Join(" ", halaman));
        }

        [Fact]
        public void Generator_SkPemberhentianBerlampiran_MemakaiMenimbangLampiran()
        {
            var asal = SkIsi(adaLampiran: true);
            var isi = new SkPerangkatIsi
            {
                Jenis = SkJenisPerangkat.Pemberhentian,
                Kelompok = "POSYANDU",
                Nomor = asal.Nomor,
                Tanggal = asal.Tanggal,
                Jabatan = asal.Jabatan,
                Alasan = "karena masa jabatan telah berakhir",
                Lampiran = asal.Lampiran
            };

            string badan = string.Join(" ", HalamanTercetak(isi)).Replace("\n", " ");

            Assert.Contains("Bahwa nama-nama sebagaimana tercantum dalam Lampiran saat ini menjabat", badan);
            Assert.Contains("Memberhentikan nama-nama sebagaimana tercantum dalam Lampiran", badan);
            Assert.Contains("karena masa jabatan telah berakhir", badan);
        }

        // ---------- pembantu ----------

        private static AppConfig Konfigurasi() => new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AppConfig:databaseConnectionString"] = "Data Source=:memory:"
            })
            .Build());

        private static BarisLampiranSk Orang(
            string nama,
            string unit = "",
            string peran = "",
            string? pekerjaan = null,
            string? agama = null,
            string? gol = null,
            string? statusKawin = null,
            string? ttl = null,
            string? pendidikan = null,
            string? alamat = null) => new()
        {
            Nama = nama,
            Peran = peran,
            Unit = unit,
            Pekerjaan = pekerjaan ?? string.Empty,
            Agama = agama ?? string.Empty,
            GolonganDarah = gol ?? string.Empty,
            StatusPerkawinan = statusKawin ?? string.Empty,
            TempatTanggalLahir = ttl ?? string.Empty,
            Pendidikan = pendidikan ?? string.Empty,
            Alamat = alamat ?? string.Empty
        };

        private static PerangkatDesa Perangkat(string nama, string? nik, string jabatan = JabatanPerangkat.KaderPosyandu) => new()
        {
            Nama = nama,
            NIK = nik,
            Jabatan = jabatan,
            Status = StatusPerangkat.Aktif
        };

        private static WargaData Warga(
            string nik, string nama,
            string? pekerjaan = null, string? agama = null, string? gol = null,
            string? statusKawin = null, string? tempatLahir = null,
            string? tanggalLahir = null, string? pendidikan = null) => new()
        {
            NIK = nik,
            Nama = nama,
            TempatLahir = tempatLahir,
            TanggalLahir = tanggalLahir,
            JenisKelamin = "P",
            Agama = agama,
            StatusPerkawinan = statusKawin,
            Pekerjaan = pekerjaan,
            Pendidikan = pendidikan,
            GolonganDarah = gol,
            Dusun = "Dusun Uji",
            RT = "001",
            RW = "002",
            Desa = "Desa Uji",
            Kecamatan = "Kec. Uji",
            Kabupaten = "Kab. Uji",
            StatusWarga = StatusWargaTipe.Aktif
        };

        private async Task DuaWarga()
        {
            await _wargaRepo.AddOrUpdateWargaAsync(Warga(
                "3204010101850002", "Siti Aminah", "Guru", "Islam", "O", "Kawin",
                "Karawang", "07-08-1977", "S1"));
            await _wargaRepo.AddOrUpdateWargaAsync(Warga(
                "3204010101920004", "Yanti", "Petani", "Islam", "B", "Kawin",
                "Karawang", "03-04-1982", "SMP"));
        }

        private static DesaData DesaUji() => new()
        {
            NamaDesa = "Sumberjaya",
            Kecamatan = "Tempuran",
            Kabupaten = "Karawang",
            Alamat = "Jl. Belendung 02",
            KepalaDesa = "A. Sopandi"
        };

        /// <summary>Isi SK Posyandu untuk satu orang atau berlampiran dua unit.</summary>
        private static SkPerangkatIsi SkIsi(bool adaLampiran)
        {
            var isi = new SkPerangkatIsi
            {
                Jenis = SkJenisPerangkat.Pengangkatan,
                Kelompok = "POSYANDU",
                Nomor = "340/02-Kep/Ds/2026",
                Tanggal = new DateTime(2026, 9, 29),
                Jabatan = JabatanPerangkat.KaderPosyandu,
                Mulai = new DateTime(2026, 9, 29),
                Selesai = new DateTime(2029, 9, 28),
                Nama = "Siti Aminah",
                NIK = "3204010101850002",
                Lampiran = adaLampiran
                    ? new[]
                    {
                        new BarisLampiranSk
                        {
                            Nama = "Siti Aminah", Peran = "Ketua",
                            TempatTanggalLahir = "Karawang, 07-08-1977",
                            Pekerjaan = "Guru", Agama = "Islam", GolonganDarah = "O",
                            StatusPerkawinan = "Kawin", Alamat = "Dusun Uji RT 001/RW 002",
                            Unit = "POSYANDU SAKURA I"
                        },
                        new BarisLampiranSk
                        {
                            Nama = "Nurhayati", Peran = "Sekretaris",
                            TempatTanggalLahir = "Karawang, 01-02-1980",
                            Pekerjaan = "Ibu Rumah Tangga", Agama = "Islam", GolonganDarah = "A",
                            StatusPerkawinan = "Kawin", Alamat = "Dusun Uji RT 003/RW 002",
                            Unit = "POSYANDU SAKURA I"
                        },
                        new BarisLampiranSk
                        {
                            Nama = "Yanti", Peran = "Anggota",
                            TempatTanggalLahir = "Karawang, 03-04-1982",
                            Pekerjaan = "Petani", Agama = "Islam", GolonganDarah = "B",
                            StatusPerkawinan = "Kawin", Alamat = "Dusun Uji RT 002/RW 001",
                            Unit = "POSYANDU SAKURA II"
                        }
                    }
                    : Array.Empty<BarisLampiranSk>()
            };

            return isi;
        }

        /// <summary>
        /// Teks tiap halaman dokumen SK. QuestPDF menghasilkan SVG per halaman,
        /// jadi isi yang benar-benar tercetak bisa diperiksa tanpa membuka PDF
        /// (aliran teksnya terkompresi). Potongan teks digabung dengan spasi;
        /// perhatikan bahwa sel yang terbungkus dua baris bisa terbelah oleh
        /// potongan dari sel sebelahnya, sehingga pemeriksaan sebaiknya per kata.
        /// </summary>
        private static IReadOnlyList<string> HalamanTercetak(SkPerangkatIsi isi)
        {
            var dokumen = new SkPerangkatGenerator(Konfigurasi()).BangunDokumen(DesaUji(), isi);

            return dokumen.GenerateSvg()
                .Select(svg => WebUtility.HtmlDecode(string.Join(" ", Regex
                    .Matches(svg ?? string.Empty, @">([^<]*)</text>", RegexOptions.Singleline)
                    .Select(m => Regex.Replace(m.Groups[1].Value, @"\s+", " ").Trim())
                    .Where(teks => teks.Length > 0))))
                .ToList();
        }
    }
}
