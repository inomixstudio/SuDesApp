using System.Windows;
using System.Windows.Controls;
using SuDesApp.Wpf.Utilities;

namespace SuDesApp.Wpf.Controls;

/// <summary>
/// Panel keadaan kosong seragam untuk semua halaman data. Susunan dan gaya
/// (chip ikon bulat, judul, pesan, slot aksi) selalu sama; setiap halaman
/// hanya mengisi <see cref="Ikon"/>, <see cref="Judul"/>, <see cref="Pesan"/>,
/// dan opsional <see cref="Aksi"/> (mis. tombol "Tambah" atau "Bersihkan
/// Filter"). Ikon berupa glyph <c>IkonMenu</c> yang tampil lewat AppIconFont.
/// </summary>
public partial class EmptyStateView : UserControl
{
    /// <summary>Glyph ikon (AppIconFont); default <see cref="IkonMenu.Dokumen"/>.</summary>
    public static readonly DependencyProperty IkonProperty =
        DependencyProperty.Register(
            nameof(Ikon), typeof(string), typeof(EmptyStateView),
            new PropertyMetadata(IkonMenu.Dokumen));

    /// <summary>Judul tebal; kosongkan (atau tidak diisi) bila pesan saja cukup.</summary>
    public static readonly DependencyProperty JudulProperty =
        DependencyProperty.Register(
            nameof(Judul), typeof(string), typeof(EmptyStateView),
            new PropertyMetadata(string.Empty));

    /// <summary>Pesan bantuan di bawah judul; boleh kosong.</summary>
    public static readonly DependencyProperty PesanProperty =
        DependencyProperty.Register(
            nameof(Pesan), typeof(string), typeof(EmptyStateView),
            new PropertyMetadata(string.Empty));

    /// <summary>Konten opsional di bawah pesan (tombol aksi).</summary>
    public static readonly DependencyProperty AksiProperty =
        DependencyProperty.Register(
            nameof(Aksi), typeof(object), typeof(EmptyStateView),
            new PropertyMetadata(null));

    public EmptyStateView() => InitializeComponent();

    public string Ikon
    {
        get => (string)GetValue(IkonProperty);
        set => SetValue(IkonProperty, value);
    }

    public string Judul
    {
        get => (string)GetValue(JudulProperty);
        set => SetValue(JudulProperty, value);
    }

    public string Pesan
    {
        get => (string)GetValue(PesanProperty);
        set => SetValue(PesanProperty, value);
    }

    public object Aksi
    {
        get => GetValue(AksiProperty);
        set => SetValue(AksiProperty, value);
    }
}
