using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SudesExPlus;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;

namespace SuDesApp.Services
{
    /// <summary>Yang akan dilakukan terhadap satu baris berkas impor.</summary>
    public enum TindakanImpor
    {
        /// <summary>Baris bermasalah atau NIK sudah ada — tidak akan disimpan.</summary>
        Lewati,

        /// <summary>Warga baru, akan ditambahkan.</summary>
        Simpan,

        /// <summary>NIK sudah ada di database dan mode perbaruan aktif.</summary>
        Perbarui
    }

    /// <summary>
    /// Satu baris berkas impor beserta hasil pemeriksaannya. Dipakai juga untuk
    /// pratinjau di layar: <see cref="Kesalahan"/> yang terisi berarti baris
    /// tersebut tidak akan tersimpan.
    /// </summary>
    public class BarisImporWarga
    {
        /// <summary>Nomor baris di berkas; baris 1 adalah judul kolom.</summary>
        public int NomorBaris { get; init; }

        public string? NIK { get; init; }
        public string? Nama { get; init; }

        public TindakanImpor Tindakan { get; set; } = TindakanImpor.Lewati;

        /// <summary>Alasan baris ditolak. Kosong berarti baris layak simpan.</summary>
        public List<string> Kesalahan { get; } = new();

        public bool AdaKesalahan => Kesalahan.Count > 0;

        /// <summary>
        /// Alasan yang siap ditampilkan di kolom "Alasan ditolak". Setiap
        /// masalah ditulis di baris sendiri supaya mudah dibaca di kolom sempit,
        /// dan baris yang tidak bermasalah cukup menampilkan "-".
        /// </summary>
        public string AlasanTampil => Kesalahan.Count == 0
            ? "-"
            : string.Join(Environment.NewLine, Kesalahan);

        /// <summary>Data siap simpan; null bila baris ditolak.</summary>
        public WargaData? Data { get; set; }

        public string KeteranganTindakan => Tindakan switch
        {
            TindakanImpor.Simpan => "Akan ditambahkan",
            TindakanImpor.Perbarui => "Akan diperbarui",
            _ => "Dilewati"
        };
    }

    /// <summary>Ringkasan hasil pemeriksaan satu berkas impor.</summary>
    public class HasilImporWarga
    {
        public required string NamaBerkas { get; init; }
        public required IReadOnlyList<BarisImporWarga> Baris { get; init; }

        /// <summary>Judul kolom yang tidak dikenali; diabaikan, bukan error.</summary>
        public required IReadOnlyList<string> KolomTidakDikenali { get; init; }

        /// <summary>Masalah pada berkasnya sendiri, bukan pada baris.</summary>
        public string? KesalahanBerkas { get; init; }

        public bool AdaKesalahanBerkas => !string.IsNullOrWhiteSpace(KesalahanBerkas);

        public int JumlahBaris => Baris.Count;
        public int JumlahGagal => Baris.Count(b => b.AdaKesalahan);
        public int JumlahDuplikatDiBerkas => Baris.Count(b =>
            b.Kesalahan.Any(k => k.Contains("duplikat di berkas", StringComparison.OrdinalIgnoreCase)));
        public int JumlahDuplikatDiDatabase => Baris.Count(b =>
            b.Kesalahan.Any(k => k.Contains("sudah terdaftar", StringComparison.OrdinalIgnoreCase)));
        public int JumlahAkanDisimpan => Baris.Count(b => b.Tindakan == TindakanImpor.Simpan);
        public int JumlahAkanDiperbarui => Baris.Count(b => b.Tindakan == TindakanImpor.Perbarui);
        public int JumlahBisaDisimpan => JumlahAkanDisimpan + JumlahAkanDiperbarui;

        public string Ringkasan =>
            $"{JumlahBaris} baris terbaca: {JumlahBisaDisimpan} bisa disimpan " +
            $"({JumlahAkanDisimpan} baru, {JumlahAkanDiperbarui} pembaruan), {JumlahGagal} ditolak";
    }

    public interface IImporWargaService
    {
        /// <summary>
        /// Baca dan periksa berkas tanpa menyimpan apa pun. Bisa ditampilkan ke
        /// pengguna lebih dulu, lengkap dengan alasan setiap baris ditolak.
        /// </summary>
        Task<HasilImporWarga> ValidasiAsync(
            string pathBerkas,
            bool perbaruiYangSudahAda = false,
            CancellationToken ct = default);

        /// <summary>Simpan baris yang lolos pemeriksaan.</summary>
        Task<int> JalankanAsync(
            HasilImporWarga hasil,
            IProgress<int>? progres = null,
            CancellationToken ct = default);
    }

    /// <summary>
    /// Impor warga massal dari .xlsx atau .csv.
    ///
    /// Berkas hasil ekspor warga bisa langsung diimpor ulang: judul kolom ekspor
    /// dikenali, urutan kolom bebas, dan kolom tambahan diabaikan. Baris
    /// bermasalah tidak pernah disimpan — pengguna mendapat daftar alasan per
    /// baris supaya berkas diperbaiki, bukan hasil impor ditebak-tebak.
    /// </summary>
    public partial class ImporWargaService : IImporWargaService
    {
        private readonly IWargaRepository _warga;
        private readonly ILogger _logger;

        /// <summary>
        /// NIK yang dipakai baris internal (instansi dan kematian), bukan warga
        /// sungguhan. Semua import memakai NIK lain, sehingga dua nilai ini
        /// harus ditolak agar tidak menimpa baris yang sudah ada.
        /// </summary>
        private static readonly string[] NikCadangan =
        {
            "9999999999999999", "0000000000000000"
        };

        public ImporWargaService(IWargaRepository warga, ILogger<ImporWargaService>? logger = null)
        {
            _warga = warga ?? throw new ArgumentNullException(nameof(warga));
            _logger = logger ?? (ILogger)NullLogger<ImporWargaService>.Instance;
        }

        // ------------------------------------------------------------------
        // Tahap 1: baca berkas
        // ------------------------------------------------------------------

        public async Task<HasilImporWarga> ValidasiAsync(
            string pathBerkas,
            bool perbaruiYangSudahAda = false,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(pathBerkas))
                throw new ArgumentException("Path berkas tidak boleh kosong.", nameof(pathBerkas));

            if (!File.Exists(pathBerkas))
                throw new FileNotFoundException("Berkas impor tidak ditemukan.", pathBerkas);

            string nama = Path.GetFileName(pathBerkas);
            string ekstensi = Path.GetExtension(pathBerkas).ToLowerInvariant();

            List<string[]> tabel;
            try
            {
                tabel = ekstensi switch
                {
                    ".xlsx" => BacaXlsx(pathBerkas),
                    ".csv" => BacaCsv(pathBerkas),
                    ".xls" => throw new NotSupportedException(
                        "Format .xls lama belum didukung. Buka di Excel lalu simpan ulang sebagai .xlsx."),
                    _ => throw new NotSupportedException(
                        $"Format berkas '{ekstensi}' belum didukung. Gunakan .xlsx atau .csv.")
                };
            }
            // Batas file: semua kesalahan di sini adalah berkas rusak atau
            // format yang tidak didukung, bukan bug aplikasi. Cancellation
            // tetap diteruskan agar tombol Batal bekerja.
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Gagal membaca berkas impor {Berkas}", nama);
                return Gagal(nama, ex.Message);
            }

            await Task.Yield();
            ct.ThrowIfCancellationRequested();

            return await PeriksaAsync(nama, tabel, perbaruiYangSudahAda, ct).ConfigureAwait(false);
        }


    }
}
