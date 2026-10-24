using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace SuDesApp.Wpf.Views;

/// <summary>Halaman administrasi server lisensi yang ditampilkan di dalam aplikasi.</summary>
public partial class PusatAdministrasiView : UserControl
{
    private Uri? _alamat;
    private bool _siap;
    private bool _sedangMemuat;
    private string? _mintaMuatBerikutnya;
    private bool _sedangMembersihkan;
    private readonly string _folderProfil;

    public PusatAdministrasiView()
    {
        InitializeComponent();

        // Profil terpisah dan privat: cookies, storage, serta cache tidak
        // bercampur dengan browser lain atau halaman WebView lain aplikasi.
        _folderProfil = Path.Combine(Path.GetTempPath(), "SuDesApp", "AdminWebView", Guid.NewGuid().ToString("N"));
        AdminBrowser.CreationProperties = new CoreWebView2CreationProperties
        {
            UserDataFolder = _folderProfil,
            IsInPrivateModeEnabled = true,
            AreBrowserExtensionsEnabled = false
        };
        Unloaded += SaatHalamanDitutup;
    }

    public async Task MuatAsync(string alamat)
    {
        if (!Uri.TryCreate(alamat, UriKind.Absolute, out var tujuan)
            || (tujuan.Scheme != Uri.UriSchemeHttps && tujuan.Scheme != Uri.UriSchemeHttp))
        {
            _alamat = null;
            TampilkanGalat("Alamat server lisensi tidak valid. Periksa alamat pada halaman Langganan.");
            return;
        }

        _alamat = tujuan;

        // Permintaan yang datang saat pemuatan lain berjalan TIDAK dibuang:
        // operator bisa mengklik menu dua kali, atau membuka alamat lain
        // sebelum halaman pertama selesai. Tanpa antrean ini hasil akhirnya
        // ditentukan oleh siapa yang selesai terakhir — bisa halaman lama,
        // dengan alamat di bilah status yang tidak sesuai isi layar.
        if (_sedangMemuat)
        {
            _mintaMuatBerikutnya = tujuan.AbsoluteUri;
            return;
        }

        _sedangMemuat = true;
        RefreshButton.IsEnabled = false;
        ErrorPanel.Visibility = Visibility.Collapsed;
        AdminBrowser.Visibility = Visibility.Collapsed;
        StatusText.Text = "Menghubungkan ke " + _alamat.GetLeftPart(UriPartial.Authority) + "…";

        try
        {
            if (!_siap)
            {
                await AdminBrowser.EnsureCoreWebView2Async();
                AdminBrowser.CoreWebView2.Settings.AreDevToolsEnabled = false;
                AdminBrowser.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
                AdminBrowser.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
                AdminBrowser.CoreWebView2.NewWindowRequested += TanganiJendelaBaru;
                AdminBrowser.CoreWebView2.NavigationCompleted += NavigasiSelesai;
                _siap = true;
            }

            var adminTertanam = new UriBuilder(_alamat);
            string queryLama = adminTertanam.Query.TrimStart('?');
            adminTertanam.Query = string.IsNullOrEmpty(queryLama)
                ? "sudesEmbedded=1"
                : queryLama + "&sudesEmbedded=1";
            AdminBrowser.Source = adminTertanam.Uri;
        }
        catch (Exception ex)
        {
            TampilkanGalat("WebView2 gagal dimulai atau server tidak dapat dijangkau. "
                + "Pastikan Microsoft Edge WebView2 Runtime terpasang dan alamat server di halaman Langganan benar. "
                + "Alamat yang dicoba: " + _alamat.GetLeftPart(UriPartial.Path) + ". "
                + "Bila WebView2 tidak tersedia, gunakan tombol Buka di Browser. "
                + ex.Message);
        }
        finally
        {
            _sedangMemuat = false;
            RefreshButton.IsEnabled = true;

            if (_mintaMuatBerikutnya is { } berikutnya)
            {
                _mintaMuatBerikutnya = null;
                await MuatAsync(berikutnya);
            }
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (_alamat is null)
        {
            TampilkanGalat("Alamat server lisensi belum tersedia. Isi alamat server di halaman "
                + "Langganan, lalu buka Pusat Administrasi lagi.");
            return;
        }

        await MuatAsync(_alamat.AbsoluteUri);
    }

    /// <summary>
    /// Jalur cadangan bila WebView2 Runtime tidak terpasang atau halaman tidak
    /// bisa dimuat di dalam aplikasi. Alamat yang dibuka tetap alamat server
    /// yang sama (tanpa penanda <c>sudesEmbedded</c>), jadi operator bisa
    /// menyelesaikan pekerjaannya di peramban biasa.
    /// </summary>
    private void OpenBrowserButton_Click(object sender, RoutedEventArgs e)
    {
        if (_alamat is null)
        {
            TampilkanGalat("Alamat server lisensi belum tersedia, jadi browser tidak bisa dibuka.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(_alamat.AbsoluteUri)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            TampilkanGalat("Halaman tidak bisa dibuka di browser. Salin alamat berikut secara manual: "
                + _alamat.AbsoluteUri + " (" + ex.Message + ")");
        }
    }

    private async void SaatHalamanDitutup(object sender, RoutedEventArgs e)
    {
        await BersihkanSesiAsync();
    }

    /// <summary>
    /// Hapus kredensial dari state JavaScript dan seluruh data situs sebelum
    /// menutup controller WebView2. InPrivate juga membuat data sesi hilang
    /// ketika proses aplikasi berakhir mendadak.
    /// </summary>
    private async Task BersihkanSesiAsync()
    {
        if (_sedangMembersihkan) return;
        _sedangMembersihkan = true;

        try
        {
            if (AdminBrowser.CoreWebView2 is { } core)
            {
                try
                {
                    await core.ExecuteScriptAsync(
                        "if (typeof window.keluar === 'function') window.keluar();");
                }
                catch (Exception) { /* Dokumen mungkin sudah berpindah/ditutup. */ }

                try
                {
                    await core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllSite);
                }
                catch (Exception) { /* Profil InPrivate tetap dibuang saat controller ditutup. */ }
            }
        }
        finally
        {
            try
            {
                if (AdminBrowser.CoreWebView2 is { } core)
                {
                    core.NewWindowRequested -= TanganiJendelaBaru;
                    core.NavigationCompleted -= NavigasiSelesai;
                }
                AdminBrowser.Dispose();
            }
            catch (Exception) { /* Pembersihan best-effort saat aplikasi ditutup. */ }

            try
            {
                string folderAkar = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "SuDesApp", "AdminWebView"));
                string folderProfil = Path.GetFullPath(_folderProfil);
                if (Directory.Exists(folderProfil)
                    && string.Equals(Path.GetDirectoryName(folderProfil), folderAkar, StringComparison.OrdinalIgnoreCase)
                    && Guid.TryParseExact(Path.GetFileName(folderProfil), "N", out _))
                {
                    Directory.Delete(folderProfil, recursive: true);
                }
            }
            catch (Exception) { /* Browser process mungkin masih menutup file sementara. */ }
        }
    }

    private void NavigasiSelesai(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            TampilkanGalat("Server lisensi tidak merespons halaman administrasi (" + e.WebErrorStatus
                + "). Periksa koneksi dan alamat server pada halaman Langganan.");
            return;
        }

        // WebView2 melaporkan halaman 404/500 sebagai navigasi BERHASIL. Tanpa
        // pemeriksaan ini operator membaca "Terhubung" padahal server justru
        // menjawab galat — mis. rute /admin belum ada di server versi lama,
        // atau alamat menunjuk aplikasi lain di host yang sama.
        int kode = e.HttpStatusCode;
        if (kode >= 400)
        {
            TampilkanGalat($"Server lisensi menjawab HTTP {kode} untuk halaman administrasi. "
                + "Pastikan alamat server di halaman Langganan menunjuk server lisensi yang benar "
                + "dan rute /admin tersedia (server versi lama mungkin belum memilikinya).");
            return;
        }

        ErrorPanel.Visibility = Visibility.Collapsed;
        AdminBrowser.Visibility = Visibility.Visible;
        StatusText.Text = "Terhubung ke " + (_alamat?.GetLeftPart(UriPartial.Authority) ?? "server lisensi")
            + ". Masuk dengan ID dan token operator yang memiliki izin.";
    }

    private void TanganiJendelaBaru(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        // Tautan yang meminta tab/jendela baru tetap dibuka di WebView yang sama,
        // sehingga alur Administrasi Pusat tidak pernah meluncurkan browser luar.
        e.Handled = true;
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var tujuan)
            && (tujuan.Scheme == Uri.UriSchemeHttps || tujuan.Scheme == Uri.UriSchemeHttp))
        {
            AdminBrowser.CoreWebView2.Navigate(tujuan.AbsoluteUri);
        }
    }

    private void TampilkanGalat(string pesan)
    {
        StatusText.Text = "Belum terhubung ke server lisensi.";
        ErrorText.Text = pesan;
        ErrorPanel.Visibility = Visibility.Visible;
        AdminBrowser.Visibility = Visibility.Collapsed;
    }
}
