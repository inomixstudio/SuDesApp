using System.Collections.Generic;
using System.Linq;
using SuDesApp.Services;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Menyalin keputusan lisensi ke tampilan sidebar: setiap menu vital diberi
    /// tanda terkunci (gembok + tooltip yang menyebut jalan keluarnya), dan
    /// akordeon ikut ditandai bila SELURUH isinya terkunci.
    ///
    /// Aturan "rute mana yang vital" tetap milik <see cref="PenjagaLisensi"/> —
    /// kelas ini hanya menyebarkannya ke pohon menu. Dipisah dari
    /// <see cref="FeatureNavigationViewModel"/> supaya bisa diuji dengan pohon
    /// menu kecil, tanpa membangun seluruh sidebar (yang butuh layanan tema).
    /// </summary>
    public static class KunciMenuSidebar
    {
        /// <summary>
        /// Tandai/lepas tanda pada <paramref name="items"/> dan seluruh anaknya.
        /// <paramref name="pesan"/> adalah kalimat tooltip saat terkunci; null
        /// saat lisensi aktif. Akordeon hanya bergembok bila semua anaknya
        /// terkunci — akordeon campuran (mis. Formulir berisi item non-vital)
        /// dibiarkan tanpa gembok agar tidak menjanjikan kunci yang tidak ada.
        /// </summary>
        public static void Terapkan(IEnumerable<NavItem> items, bool terkunci, string? pesan)
        {
            string alasan = pesan ?? string.Empty;

            foreach (var item in items)
            {
                if (item.Children.Count > 0)
                {
                    Terapkan(item.Children, terkunci, pesan);

                    if (item.IsAccordion)
                    {
                        item.PesanTerkunci = alasan;
                        item.Terkunci = terkunci && item.Children.All(anak => anak.Terkunci);
                    }
                    continue;
                }

                item.PesanTerkunci = alasan;
                item.Terkunci = terkunci && PenjagaLisensi.ApakahMenuVital(item.Route);
            }
        }
    }
}
