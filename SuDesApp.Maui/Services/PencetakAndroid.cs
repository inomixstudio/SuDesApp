#if ANDROID
using Android.Content;
using Android.OS;
using Android.Print;
#endif

namespace SuDesApp.Maui.Services;

/// <summary>
/// Mengirim PDF ke layar cetak Android: pengguna memilih pencetak (printer Wi-Fi,
/// Save as PDF, atau layanan cetak lain). Berkas ditulis ke folder cache dulu
/// karena kerangka cetak Android meminta aliran berkas, bukan memori.
/// </summary>
public static class PencetakAndroid
{
    public static Task CetakAsync(string namaBerkas, byte[] pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);

        if (pdf.Length == 0) throw new InvalidOperationException("PDF kosong, tidak ada yang bisa dicetak.");

#if ANDROID
        // PrintManager hanya mau menerima konteks Activity; konteks Application
        // ditolak dengan pesan "Can print only from an activity".
        var aktivitas = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity
            ?? throw new InvalidOperationException("Layar aplikasi tidak aktif, tidak bisa membuka cetak.");

        var folder = new Java.IO.File(aktivitas.CacheDir, "cetak");
        folder.Mkdirs();

        string namaAman = string.IsNullOrWhiteSpace(namaBerkas) ? "surat" : namaBerkas;
        var berkas = new Java.IO.File(folder, namaAman + ".pdf");

        using (var aliran = new Java.IO.FileOutputStream(berkas))
        {
            aliran.Write(pdf);
            aliran.Flush();
        }

        var adapter = new PdfKeCetakAdapter(berkas, namaAman);
        var manajer = (PrintManager?)aktivitas.GetSystemService(Context.PrintService)
            ?? throw new InvalidOperationException("Layanan cetak Android tidak tersedia.");

        manajer.Print(namaAman, adapter, null);
        return Task.CompletedTask;
#else
        throw new PlatformNotSupportedException("Pencetakan PDF tersedia di aplikasi Android.");
#endif
    }
}

#if ANDROID
/// <summary>Adapter cetak yang menyalin berkas PDF ke tujuan yang dipilih Android.</summary>
internal sealed class PdfKeCetakAdapter : PrintDocumentAdapter
{
    private readonly Java.IO.File _berkas;
    private readonly string _nama;

    public PdfKeCetakAdapter(Java.IO.File berkas, string nama)
    {
        _berkas = berkas;
        _nama = nama;
    }

    public override void OnLayout(
        PrintAttributes? oldAttributes,
        PrintAttributes? newAttributes,
        CancellationSignal? cancellationSignal,
        LayoutResultCallback? callback,
        Bundle? extras)
    {
        if (callback == null) return;

        if (cancellationSignal?.IsCanceled == true)
        {
            callback.OnLayoutCancelled();
            return;
        }

        var info = new PrintDocumentInfo.Builder(_nama)
            .SetContentType(PrintContentType.Document)
            .SetPageCount(1)
            .Build();

        callback.OnLayoutFinished(info, true);
    }

    public override void OnWrite(
        PageRange[]? pages,
        ParcelFileDescriptor? destination,
        CancellationSignal? cancellationSignal,
        WriteResultCallback? callback)
    {
        if (destination?.FileDescriptor == null || callback == null) return;

        try
        {
            var keluaran = new Java.IO.FileOutputStream(destination.FileDescriptor);

            using (var masukan = new Java.IO.FileInputStream(_berkas))
            {
                var penyangga = new byte[8192];
                int dibaca;
                while ((dibaca = masukan.Read(penyangga)) > 0)
                {
                    keluaran.Write(penyangga, 0, dibaca);
                }
            }

            keluaran.Flush();
            callback.OnWriteFinished(new[] { PageRange.AllPages });
        }
        catch (Java.IO.IOException ex)
        {
            callback.OnWriteFailed(ex.Message);
        }
        catch (Exception ex)
        {
            callback.OnWriteFailed(ex.Message);
        }
        finally
        {
            destination.Close();
        }
    }
}
#endif
