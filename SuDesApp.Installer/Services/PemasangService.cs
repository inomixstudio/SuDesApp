using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace SuDesApp.Installer.Services
{
    /// <summary>
    /// Logika pasang/cabut yang mirror installer/SuDesApp.iss:
    /// cek mutex, cadangan desa.db*, hapus runtimes lama, salin payload
    /// (kecuali desa.db, Templates, *.pdb), pintasan Start Menu/Desktop,
    /// registrasi Add/Remove, dan uninstaller berupa salinan diri sendiri.
    /// </summary>
    public sealed class PemasangService
    {
        public string FolderCadanganDatabase =>
            Path.Combine(Path.GetTempPath(), "SuDesApp-database-lama");

        /// <summary>True bila aplikasi sedang berjalan (mutex milik App.OnStartup).</summary>
        public bool AplikasiBerjalan()
        {
            try
            {
                using var m = Mutex.OpenExisting(App.NamaMutex);
                return m is not null;
            }
            catch
            {
                return false;
            }
        }

        public string CariPayloadEkstrak(IProgress<InstallStep> lapor, CancellationToken ct)
        {
            // 1) ZIP tanam di exe (rilis GitHub)
            var asm = Assembly.GetExecutingAssembly();
            var zipTanam = asm.GetManifestResourceStream("Payload.zip");
            if (zipTanam is not null)
            {
                var folder = Path.Combine(Path.GetTempPath(), "SuDesApp-payload-" + App.Versi);
                Directory.CreateDirectory(folder);
                lapor.Report(new InstallStep("Mengurai payload aplikasi…", 5));
                using var fs = File.Open(Path.Combine(folder, "payload.zip"), FileMode.Create, FileAccess.Write);
                zipTanam.CopyTo(fs);
                var ubah = Path.Combine(folder, "ubah");
                Directory.CreateDirectory(ubah);
                ZipFile.ExtractToDirectory(Path.Combine(folder, "payload.zip"), Path.Combine(ubah, "publish"), overwriteFiles: true);
                return Path.Combine(ubah, "publish");
            }
            // 2) Folder Payload/ di samping exe (uji & portable)
            var diri = AppDomain.CurrentDomain.BaseDirectory;
            var payload = Path.Combine(diri, "Payload");
            if (Directory.Exists(payload)) return payload;
            var publish = Path.Combine(diri, "publish");
            if (Directory.Exists(publish)) return publish;
            var zip = Path.Combine(diri, "payload.zip");
            if (File.Exists(zip))
            {
                var folder = Path.Combine(Path.GetTempPath(), "SuDesApp-payload-" + App.Versi);
                Directory.CreateDirectory(folder);
                var ubah = Path.Combine(folder, "ubah");
                Directory.CreateDirectory(ubah);
                ZipFile.ExtractToDirectory(zip, Path.Combine(ubah, "publish"), overwriteFiles: true);
                return Path.Combine(ubah, "publish");
            }
            throw new FileNotFoundException(
                "Payload tidak ditemukan. Letakkan folder Payload/ atau payload.zip di samping installer, atau tanam Payload.zip ke instalasi rilis.");
        }

        public async Task PasangAsync(string sumber, string tujuan, bool buatIkonDesktop,
            IProgress<InstallStep> lapor, CancellationToken ct)
        {
            if (AplikasiBerjalan())
            {
                throw new InvalidOperationException(
                    "SuDesApp masih berjalan. Tutup aplikasi terlebih dulu, lalu jalankan pemasangan lagi.");
            }
            if (string.IsNullOrWhiteSpace(tujuan)) throw new ArgumentException("Lokasi pemasangan kosong.", nameof(tujuan));
            tujuan = tujuan.Trim();

            Directory.CreateDirectory(tujuan);
            Directory.CreateDirectory(FolderCadanganDatabase);

            // Cadangkan database lama (sama CDG Inno: jangan menimpa saat gagal)
            bool adaDb = false;
            foreach (var n in new[] { "desa.db", "desa.db-wal", "desa.db-shm", "desa.db-journal" })
            {
                var s = Path.Combine(tujuan, n);
                if (!File.Exists(s)) continue;
                adaDb = true;
                lapor.Report(new InstallStep("Mencadangkan " + n + "…", 10));
                await Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested();
                    File.Copy(s, Path.Combine(FolderCadanganDatabase, n), overwrite: false);
                }, ct);
            }

            // Hapus native runtimes lama (samakan [InstallDelete])
            foreach (var r in new[] { "runtimes", "x64" })
            {
                var p = Path.Combine(tujuan, r);
                if (Directory.Exists(p))
                {
                    lapor.Report(new InstallStep("Membersihkan runtime lama…", 15));
                    Directory.Delete(p, recursive: true);
                }
            }
            foreach (var pdb in Directory.GetFiles(tujuan, "*.pdb", SearchOption.TopDirectoryOnly))
            {
                File.Delete(pdb);
            }

            // Salin payload (Exclude: desa.db*, *.pdb, Templates\*, Templates\*\*)
            lapor.Report(new InstallStep("Menyalin berkas aplikasi…", 20));
            var files = Directory.GetFiles(sumber, "*", SearchOption.AllDirectories);
            int total = Math.Max(1, files.Length);
            int selesai = 0;
            foreach (var f in files)
            {
                ct.ThrowIfCancellationRequested();
                var rel = Path.GetRelativePath(sumber, f);
                if (HarusDilewati(rel)) continue;
                var dest = Path.Combine(tujuan, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                await Task.Run(() => File.Copy(f, dest, overwrite: true), ct);
                selesai++;
                if (selesai % 25 == 0)
                {
                    lapor.Report(new InstallStep("Menyalin berkas aplikasi…", 20 + (int)(selesai * 60.0 / total)));
                }
            }

            // Templates hanya bila belum ada (onlyifdoesntexist)
            var sumberTemplates = Path.Combine(sumber, "Templates");
            if (Directory.Exists(sumberTemplates))
            {
                lapor.Report(new InstallStep("Menyiapkan template bawaan…", 85));
                foreach (var f in Directory.GetFiles(sumberTemplates, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(sumberTemplates, f);
                    var dest = Path.Combine(tujuan, "Templates", rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    if (!File.Exists(dest))
                    {
                        await Task.Run(() => File.Copy(f, dest, overwrite: false), ct);
                    }
                }
            }

            lapor.Report(new InstallStep("Membuat pintasan & registrasi…", 90));
            await Task.Run(() =>
            {
                BuatPintasan(tujuan, buatIkonDesktop);
                DaftarAddRemove(tujuan);
                SalinUninstaller(tujuan);
            }, ct);

            // Pulihkan cadangan bila uninstaller lama menghapus (samakan PulihkanDatabase)
            foreach (var n in new[] { "desa.db", "desa.db-wal", "desa.db-shm", "desa.db-journal" })
            {
                var s = Path.Combine(FolderCadanganDatabase, n);
                var d = Path.Combine(tujuan, n);
                if (File.Exists(s) && !File.Exists(d))
                {
                    lapor.Report(new InstallStep("Memulihkan " + n + "…", 95));
                    File.Copy(s, d, overwrite: false);
                }
            }
            _ = adaDb;
            lapor.Report(new InstallStep("Selesai.", 100));
        }

        public Task CabutAsync(string tujuan, IProgress<InstallStep> lapor, CancellationToken ct)
        {
            if (AplikasiBerjalan())
            {
                throw new InvalidOperationException("SuDesApp masih berjalan. Tutup aplikasi terlebih dulu sebelum mencopot.");
            }
            lapor.Report(new InstallStep("Membaca daftar berkas…", 20));
            // Uninstaller Inno tidak menghapus desa.db, Templates diedit, dan hasil PDF.
            // Yang dihapus: seluruh folder {app} lalu tulis ulang hanya yang dilindungi.
            var lindungi = new List<string>();
            foreach (var n in new[] { "desa.db", "desa.db-wal", "desa.db-shm", "desa.db-journal" })
            {
                var p = Path.Combine(tujuan, n);
                if (File.Exists(p)) lindungi.Add(p);
            }
            var templates = Path.Combine(tujuan, "Templates");
            lapor.Report(new InstallStep("Mencopot berkas aplikasi…", 50));
            foreach (var f in Directory.GetFiles(tujuan, "*", SearchOption.AllDirectories))
            {
                if (f.StartsWith(templates, StringComparison.OrdinalIgnoreCase)) continue;
                if (lindungi.Contains(f)) continue;
                ct.ThrowIfCancellationRequested();
                File.Delete(f);
            }
            foreach (var d in Directory.GetDirectories(tujuan, "*", SearchOption.TopDirectoryOnly).Reverse())
            {
                if (string.Equals(d, templates, StringComparison.OrdinalIgnoreCase)) continue;
                try { Directory.Delete(d, recursive: true); } catch { /* folder berisi data dilindungi */ }
            }
            lapor.Report(new InstallStep("Menghapus registrasi & pintasan…", 85));
            HapusAddRemove();
            HapusPintasan();
            lapor.Report(new InstallStep("Selesai.", 100));
            return Task.CompletedTask;
        }

        private static bool HarusDilewati(string rel)
        {
            var n = rel.Replace('\\', '/');
            if (n.StartsWith("Templates/", StringComparison.OrdinalIgnoreCase)) return true;
            var nama = Path.GetFileName(n);
            if (nama.Equals("desa.db", StringComparison.OrdinalIgnoreCase)) return true;
            if (nama.StartsWith("desa_backup", StringComparison.OrdinalIgnoreCase)) return true;
            if (nama.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static void BuatPintasan(string tujuan, bool desktop)
        {
            var exe = Path.Combine(tujuan, "SuDesApp.exe");
            if (!File.Exists(exe)) return;
            var wsh = new ShellLinkMaker();
            var startMenu = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                "Programs", "SuDesApp");
            Directory.CreateDirectory(startMenu);
            wsh.Buat(Path.Combine(startMenu, "SuDesApp.lnk"), exe, tujuan, "Sistem Surat Desa");
            if (desktop)
            {
                var desk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "SuDesApp.lnk");
                wsh.Buat(desk, exe, tujuan, "Sistem Surat Desa");
            }
        }

        private static void HapusPintasan()
        {
            try
            {
                var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "SuDesApp", "SuDesApp.lnk");
                if (File.Exists(startMenu)) File.Delete(startMenu);
                var desk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "SuDesApp.lnk");
                if (File.Exists(desk)) File.Delete(desk);
            }
            catch
            {
                /* pintasan yang tidak ada adalah keadaan normal saat mencopot */
            }
        }

        private static void DaftarAddRemove(string tujuan)
        {
            var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\SuDesAppSumberjaya");
            if (k is null) return;
            using (k)
            {
                k.SetValue("DisplayName", "SuDesApp " + App.Versi);
                k.SetValue("DisplayVersion", App.Versi);
                k.SetValue("Publisher", "Sumberjaya Dev.");
                k.SetValue("InstallLocation", tujuan);
                k.SetValue("DisplayIcon", Path.Combine(tujuan, "SuDesApp.exe"));
                k.SetValue("UninstallString", "\"" + Path.Combine(tujuan, "SuDesAppUninstaller.exe") + "\" /uninstall");
                k.SetValue("QuietUninstallString", "\"" + Path.Combine(tujuan, "SuDesAppUninstaller.exe") + "\" /uninstall /SILENT", RegistryValueKind.String);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }

        private static void HapusAddRemove()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\SuDesAppSumberjaya", false);
            }
            catch
            {
                /* entri yang tidak ada adalah keadaan normal saat mencopot */
            }
        }

        private static void SalinUninstaller(string tujuan)
        {
            // Single-file: Assembly.Location kosong — pakai BaseDirectory.
            var bas = AppContext.BaseDirectory;
            var diriExe = Path.Combine(bas, Path.GetFileName(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "SuDesAppSetup.exe"));
            if (!File.Exists(diriExe)) return;
            var copy = Path.Combine(tujuan, "SuDesAppUninstaller.exe");
            try
            {
                File.Copy(diriExe, copy, overwrite: true);
            }
            catch
            {
                /* uninstaller salinan gagal bukan alasan membatalkan pasang */
            }
        }

        /// <summary>Langkah instalasi untuk progress bar.</summary>
        public sealed record InstallStep(string Teks, int Persen);
    }

    /// <summary>Pembuat pintasan .lnk via WScript.Shell.</summary>
    internal sealed class ShellLinkMaker
    {
        public void Buat(string lnk, string target, string kerja, string deskripsi)
        {
            var t = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell tidak tersedia.");
            dynamic shell = Activator.CreateInstance(t!)!;
            dynamic pintasan = shell.CreateShortcut(lnk);
            pintasan.TargetPath = target;
            pintasan.WorkingDirectory = kerja;
            pintasan.Description = deskripsi;
            pintasan.Save();
        }
    }
}
