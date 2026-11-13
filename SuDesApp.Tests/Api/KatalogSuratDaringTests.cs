using System;
using System.Linq;
using SuDesApp.Api;
using SuDesApp.Data.Models;
using Xunit;

namespace SuDesApp.Tests.Api
{
    /// <summary>
    /// Penjaga katalog jenis surat daring (<see cref="KatalogSuratDaring"/>).
    ///
    /// <para>Katalog ini menentukan isi endpoint <c>/jenis-surat</c> di API Desa
    /// dan backend web - daftar yang dilihat sistem luar untuk mengetahui surat
    /// mana yang boleh dikirim. Isinya murni konfigurasi, jadi tidak ada yang
    /// membuatnya gagal build saat berubah: menambahkan NTCR atau menghapus
    /// surat umum bisa lolos tanpa disadari. Uji di bawah mengunci perilaku yang
    /// mengubah apa yang dilihat sistem luar.</para>
    /// </summary>
    public class KatalogSuratDaringTests
    {
        /// <summary>
        /// Setiap nama yang dijanjikan katalog harus benar-benar jenis surat yang
        /// dikenal sistem. Nama yang salah ketik di katalog berarti sistem luar
        /// dijanjikan surat yang tidak akan pernah bisa dibuat.
        /// </summary>
        [Fact]
        public void Semua_nama_jenis_yang_dijanjikan_adalah_jenis_surat_yang_dikenal()
        {
            foreach (var (_, namaJenis) in KatalogSuratDaring.Semua)
            {
                Assert.Contains(namaJenis, SuratConstants.ValidJenisSurat);
            }
        }

        /// <summary>
        /// Jenis yang ditandai "dikecualikan" tidak boleh ikut ditawarkan daring.
        /// Ini penjaga utama: menambahkan entri keliru ke katalog daring membuat
        /// sistem luar mengirim permintaan yang dipastikan ditolak.
        /// </summary>
        [Fact]
        public void Jenis_dikecualikan_tidak_bocor_ke_daftar_daring()
        {
            var daring = KatalogSuratDaring.NamaJenisOnline();

            Assert.NotEmpty(KatalogSuratDaring.Dikecualikan);
            foreach (var nama in KatalogSuratDaring.Dikecualikan)
            {
                Assert.DoesNotContain(nama, daring);
            }
        }

        /// <summary>
        /// NTCR (N1-N6) butuh formulir khusus dan tanda tangan basah, jadi tidak
        /// pernah ditawarkan daring. N7 sudah dihapus; N8 tidak punya formulir
        /// sendiri, jadi keduanya tidak boleh muncul.
        /// </summary>
        [Fact]
        public void NTCR_tidak_pernah_ditawarkan_daring()
        {
            var daring = KatalogSuratDaring.NamaJenisOnline();

            foreach (var ntcr in SuratConstants.NtcrSemua)
            {
                Assert.DoesNotContain(ntcr, daring);
            }
        }

        /// <summary>
        /// Daftar yang dijanjikan harus punya isi dan tidak duplikat - daftar
        /// kosong membuat sistem luar berpikir tidak ada surat yang bisa diminta,
        /// sementara duplikat hanya menambah kebingungan.
        /// </summary>
        [Fact]
        public void Daftar_daring_tidak_kosong_dan_tidak_duplikat()
        {
            var daring = KatalogSuratDaring.NamaJenisOnline();

            Assert.NotEmpty(daring);
            Assert.Equal(
                daring.Count,
                daring.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        /// <summary>
        /// Surat umum andalan (SKTM, domisili, SKU, SKCK, instansi) harus selalu
        /// ada di kanal daring. Menghapus salah satunya adalah keputusan produk,
        /// bukan kecelakaan - uji ini memaksa keputusan itu disadari.
        /// </summary>
        [Fact]
        public void Surat_umum_andalan_selalu_ditawarkan_daring()
        {
            var daring = KatalogSuratDaring.NamaJenisOnline();

            Assert.Contains(SuratConstants.SKTM, daring);
            Assert.Contains(SuratConstants.DOMISILI_WARGA, daring);
            Assert.Contains(SuratConstants.SKU, daring);
            Assert.Contains(SuratConstants.PENGANTAR_SKCK, daring);
            Assert.Contains(SuratConstants.INSTANSI, daring);
        }

        /// <summary>
        /// Tiap entri katalog memuat kata kunci untuk pengenalan teks. Entri tanpa
        /// kata kunci berarti tak bisa dikenali, dan kata kunci kosong akan
        /// menangkap sembarang teks.
        /// </summary>
        [Fact]
        public void Setiap_entri_punya_kata_kunci_yang_berisi()
        {
            foreach (var (kataKunci, namaJenis) in KatalogSuratDaring.Semua)
            {
                Assert.NotNull(kataKunci);
                Assert.NotEmpty(kataKunci);
                Assert.All(kataKunci, k => Assert.False(string.IsNullOrWhiteSpace(k),
                    $"Kata kunci kosong pada entri {namaJenis}."));
            }
        }
    }
}
