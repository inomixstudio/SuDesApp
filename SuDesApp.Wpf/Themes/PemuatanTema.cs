using System;
using System.Collections.Generic;
using System.Windows;

namespace SuDesApp.Wpf.Themes
{
    /// <summary>
    /// SATU daftar urutan ResourceDictionary dasar aplikasi.
    ///
    /// App.xaml memuat kamus yang sama; uji tampilan dan alat render memakai
    /// daftar ini supaya memuat persis seperti aplikasi berjalan. Sebelumnya
    /// daftar itu ditulis ulang di tiap harness uji — dan ketika palet warna
    /// ditambahkan ke App.xaml, harness tetap memuat daftar lamanya. Akibatnya
    /// uji memuat tema "setengah jadi": sebagian kunci warna tidak ada, dan
    /// bagian yang seharusnya ikut tema tampil dengan warna bawaan WPF.
    ///
    /// Urutan penting:
    ///   1. PaletTetap — warna yang sengaja tidak mengikuti tema;
    ///   2. ThemeStyles — gaya; memakai StaticResource ke palet;
    ///   3. tema bawaan — warna yang mengikuti tema;
    ///   4. kamus gaya bersama (notifikasi, navigasi, catatan rilis).
    ///
    /// Uji TemaKonsistenTests menjaga daftar ini tetap sama dengan App.xaml,
    /// jadi menambah kamus di satu tempat tanpa yang lain akan gagal.
    /// </summary>
    public static class PemuatanTema
    {
        public const string Palet = "Themes/PaletTetap.xaml";
        public const string Styles = "Themes/ThemeStyles.xaml";
        public const string TemaBawaan = "Themes/GreenTheme.xaml";
        public const string NotifikasiInline = "Themes/NotifikasiInline.xaml";
        public const string NavKiri = "Themes/NavKiri.xaml";
        public const string KartuCatatanRilis = "Themes/KartuCatatanRilis.xaml";

        /// <summary>Kamus dasar, dalam urutan pemuatan yang benar.</summary>
        public static IReadOnlyList<string> BerkasDasar { get; } = new[]
        {
            Palet, Styles, TemaBawaan, NotifikasiInline, NavKiri, KartuCatatanRilis,
        };

        /// <summary>Alamat pack absolut — sah dari aplikasi maupun dari proses uji.</summary>
        public static Uri Alamat(string sumber) =>
            new("pack://application:,,,/SuDesApp;component/" + sumber, UriKind.Absolute);

        public static ResourceDictionary Muat(string sumber) => new() { Source = Alamat(sumber) };

        /// <summary>Muat seluruh kamus dasar ke dalam koleksi milik pemanggil.</summary>
        public static void MuatDasar(ResourceDictionary tujuan)
        {
            ArgumentNullException.ThrowIfNull(tujuan);
            foreach (string sumber in BerkasDasar)
            {
                tujuan.MergedDictionaries.Add(Muat(sumber));
            }
        }
    }
}
