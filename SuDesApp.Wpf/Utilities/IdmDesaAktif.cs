using System.Linq;
using System.Threading.Tasks;
using SuDesApp.ControlSurat;
using SuDesApp.Wilayah;

namespace SuDesApp.Wpf.Utilities
{
    /// <summary>
    /// Kode desa (hanya digit) untuk desa pada Pengaturan Surat — sumber tunggal
    /// bagi status bar IDM dan pemantau data IDM tahun baru. Keduanya harus
    /// memakai desa yang SAMA dengan halaman IDM; kalau tidak, cadangan desa
    /// lama bisa dinilai sebagai milik desa yang sedang dipilih.
    ///
    /// Mengembalikan string kosong bila data desa belum lengkap atau wilayahnya
    /// tidak ditemukan — pemanggil memperlakukan itu sebagai "belum ada data".
    /// </summary>
    public static class IdmDesaAktif
    {
        public static async Task<string> KodeAsync(SettingsManager pengaturan, WilayahDataStore wilayah)
        {
            var desa = await pengaturan.GetSettingsAsync();
            string nama = desa?.NamaDesa?.Trim() ?? string.Empty;
            string kecamatan = desa?.Kecamatan?.Trim() ?? string.Empty;
            string kabupaten = desa?.Kabupaten?.Trim() ?? string.Empty;
            if (nama.Length == 0 || kecamatan.Length == 0 || kabupaten.Length == 0)
            {
                return string.Empty;
            }

            var pencocokan = WilayahPencocokan.CariRantai(wilayah, nama, kecamatan, kabupaten);
            if (pencocokan.Status != StatusPencocokanWilayah.Ditemukan || pencocokan.Rantai == null)
            {
                return string.Empty;
            }

            return new string(pencocokan.Rantai.Desa.Kode.Where(char.IsDigit).ToArray());
        }
    }
}
