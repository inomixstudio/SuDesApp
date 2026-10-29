using System;
using System.IO;
using Microsoft.Extensions.Logging;
using SuDesApp.Wpf.Services;
using Xunit;

namespace SuDesApp.Tests.UI;

public class BerkasLogTests
{
    [Fact]
    public void HanyaLevelMinimumKeAtasYangDitulis()
    {
        string folder = FolderSementara();
        string jalur = Path.Combine(folder, "app.log");
        try
        {
            using var penyedia = new BerkasLogProvider(
                jalur, LogLevel.Warning, batasByte: 1024 * 1024, jumlahArsip: 2);
            var log = penyedia.CreateLogger("Uji");

            log.LogInformation("pesan informasi");
            log.LogWarning("pesan peringatan");
            log.LogError("pesan galat");

            string isi = File.ReadAllText(jalur);
            Assert.DoesNotContain("pesan informasi", isi);
            Assert.Contains("[Warning] Uji: pesan peringatan", isi);
            Assert.Contains("[Error] Uji: pesan galat", isi);
        }
        finally
        {
            Hapus(folder);
        }
    }

    [Fact]
    public void RotasiMembatasiJumlahBerkas()
    {
        string folder = FolderSementara();
        string jalur = Path.Combine(folder, "app.log");
        try
        {
            using var penyedia = new BerkasLogProvider(
                jalur, LogLevel.Warning, batasByte: 120, jumlahArsip: 2);
            var log = penyedia.CreateLogger("Uji");

            for (int i = 0; i < 8; i++)
            {
                log.LogWarning("baris panjang {Nomor} {Isi}", i, new string('x', 80));
            }

            Assert.True(File.Exists(jalur));
            Assert.True(File.Exists(jalur + ".1"));
            Assert.True(File.Exists(jalur + ".2"));
            Assert.False(File.Exists(jalur + ".3"));
        }
        finally
        {
            Hapus(folder);
        }
    }

    private static string FolderSementara()
    {
        string folder = Path.Combine(
            Path.GetTempPath(), "sudesapp-log-uji", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void Hapus(string folder)
    {
        try { Directory.Delete(folder, recursive: true); }
        catch (IOException) { /* folder uji sementara masih dipakai proses lain */ }
        catch (UnauthorizedAccessException) { /* izin folder uji sementara berubah */ }
    }
}
