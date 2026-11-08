using System;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Bagian terpisah PerangkatDesaViewModel: helper ekspor & SK cukup
    /// dipanggil dari kelas utama. File ini memecah god-class 2900+ baris
    /// tanpa mengubah perilaku — hanya komentar pemisah domain.
    /// </summary>
    public partial class PerangkatDesaViewModel
    {
        /// <summary>
        /// Peta bagian: tombol bagian → koleksi tabel. Dipisah dari kelas utama
        /// supaya navigasi antar-bagian (PilihBagian/SusunBarisBagianAktif)
        /// mudah ditemukan tanpa menggeser seluruh berkas 2900 baris.
        /// </summary>
        private System.Collections.ObjectModel.ObservableCollection<BarisPerangkatViewModel> TabelBagianAktif(string bagian)
        {
            return bagian switch
            {
                nameof(BagianPerangkat.RtRw) => ItemsRtRw,
                nameof(BagianPerangkat.Bpd) => ItemsBpd,
                nameof(BagianPerangkat.Linmas) => ItemsLinmas,
                nameof(BagianPerangkat.Posyandu) => ItemsPosyandu,
                nameof(BagianPerangkat.KaderKpm) => ItemsKaderKpm,
                nameof(BagianPerangkat.KaderStunting) => ItemsKaderStunting,
                nameof(BagianPerangkat.StafDesa) => ItemsStafDesa,
                nameof(BagianPerangkat.OperatorDesa) => ItemsOperatorDesa,
                nameof(BagianPerangkat.Pkk) => ItemsPkk,
                nameof(BagianPerangkat.Lpm) => ItemsLpm,
                _ => Items,
            };
        }
    }
}
