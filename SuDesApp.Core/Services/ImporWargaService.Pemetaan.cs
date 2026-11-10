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
    public partial class ImporWargaService : IImporWargaService
    {
        // ------------------------------------------------------------------
        // Tahap 2: petakan judul kolom
        // ------------------------------------------------------------------

        /// <summary>
        /// Judul kolom yang dikenali. Key = judul bersih (huruf kecil, tanpa
        /// spasi dan tanda baca), value = nama field. Mencakup seluruh judul
        /// kolom ekspor warga dan berbagai sinonimnya.
        /// </summary>
        private static readonly Dictionary<string, string> PetakanKolom = new(StringComparer.Ordinal)
        {
            ["nik"] = "NIK",
            ["nama"] = "Nama",
            ["namalengkap"] = "Nama",
            ["namawarga"] = "Nama",
            ["nokartukeluarga"] = "NoKK",
            ["nomorkartukeluarga"] = "NoKK",
            ["nokk"] = "NoKK",
            ["nok"] = "NoKK",
            ["jeniskelamin"] = "JenisKelamin",
            ["jk"] = "JenisKelamin",
            ["gender"] = "JenisKelamin",
            ["tempatlahir"] = "TempatLahir",
            ["tempat"] = "TempatLahir",
            ["tanggallahir"] = "TanggalLahir",
            ["tgllahir"] = "TanggalLahir",
            ["tglahir"] = "TanggalLahir",
            ["agama"] = "Agama",
            ["golongandarah"] = "GolonganDarah",
            ["goldah"] = "GolonganDarah",
            ["statusperkawinan"] = "StatusPerkawinan",
            ["statuskawin"] = "StatusPerkawinan",
            ["pekerjaan"] = "Pekerjaan",
            ["pendidikan"] = "Pendidikan",
            ["namaayah"] = "NamaAyah",
            ["namaibu"] = "NamaIbu",
            ["nomorhp"] = "NomorHP",
            ["notelepon"] = "NomorHP",
            ["telepon"] = "NomorHP",
            ["hp"] = "NomorHP",
            ["rt"] = "RT",
            ["rw"] = "RW",
            ["dusun"] = "Dusun",
            ["alamatdetail"] = "AlamatDetail",
            ["alamat"] = "AlamatDetail",
            ["alamatjalankampung"] = "AlamatDetail",
            ["alamatjalan"] = "AlamatDetail",
            ["desa"] = "Desa",
            ["kecamatan"] = "Kecamatan",
            ["kabupaten"] = "Kabupaten",
            ["kewarganegaraan"] = "Kewarganegaraan",
            ["statuswarga"] = "StatusWarga",
            ["status"] = "StatusWarga",
            ["tanggalstatus"] = "TanggalStatus",
            ["keterangan"] = "KeteranganWarga",
            ["keteranganwarga"] = "KeteranganWarga"
        };

        /// <summary>Kolom hasil ekspor yang memang tidak perlu diimpor.</summary>
        private static readonly HashSet<string> KolomDiabaikan = new(StringComparer.Ordinal)
        {
            "no", "nomor", "nourut", "jumlahsurat", "idad", "iddatabase"
        };

        /// <summary>Buang spasi, tanda baca, dan huruf besar dari judul kolom.</summary>
        internal static string BersihkanJudul(string judul)
        {
            var sb = new StringBuilder(judul.Length);
            foreach (char ch in judul)
            {
                if (char.IsLetterOrDigit(ch))
                    sb.Append(char.ToLowerInvariant(ch));
            }
            return sb.ToString();
        }


    }
}
