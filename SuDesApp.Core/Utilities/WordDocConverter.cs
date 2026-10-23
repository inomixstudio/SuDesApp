// WordDocConverter.cs
// Konversi berkas Word lama (.doc, format biner 97-2003) menjadi .docx sementara.
//
// Berkas .doc tidak dapat dibaca langsung seperti .docx (arsip ZIP berisi XML),
// dan format binernya tidak diurai sendiri oleh aplikasi. Bila Microsoft Word
// terpasang di komputer pengguna, berkas dibuka Word secara senyap (makro
// dimatikan) lalu disimpan sebagai .docx sementara, sehingga susunan surat,
// tabel, dan batas halamannya tetap terbawa. Bila Word tidak ada, pemanggil
// diberi tahu lewat Tersedia=false supaya bisa menampilkan petunjuk yang jelas
// (tanpa konversi manual, aplikasi tidak bisa membaca .doc).
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SuDesApp.Utilities
{
    /// <summary>Pengubah berkas Word lama (.doc) menjadi .docx sementara.</summary>
    public interface IWordDocConverter
    {
        /// <summary>Microsoft Word (atau pengolah kata yang mendaftarkan Word.Application) terpasang.</summary>
        bool Tersedia { get; }

        /// <summary>
        /// Konversi <paramref name="berkasDoc"/> menjadi .docx sementara dan kembalikan
        /// jalurnya. Pemanggil wajib menghapus berkas hasilnya setelah selesai dipakai.
        /// </summary>
        Task<string> KeDocxAsync(string berkasDoc, CancellationToken ct = default);
    }

    /// <summary>
    /// Konversi .doc → .docx memakai Microsoft Word lewat COM automation, dijalankan
    /// pada thread STA tersendiri supaya antarmuka aplikasi tidak membeku dan Word
    /// tidak menampilkan dialog apa pun (Visible=false, DisplayAlerts=0, makro mati).
    /// </summary>
    public class WordDocConverter : IWordDocConverter
    {
        /// <summary>Berkas .docx biasa (wdFormatXMLDocument).</summary>
        private const int FormatDocx = 16;

        /// <summary>Segala makro di dalam berkas yang dikonversi dimatikan (msoAutomationSecurityForceDisable).</summary>
        private const int MakroMati = 3;

        /// <summary>Petunjuk yang ditampilkan bila Microsoft Word tidak terpasang.</summary>
        public const string PesanTanpaWord =
            "Berkas .doc (format Word lama) perlu dikonversi otomatis, tetapi Microsoft Word tidak terpasang " +
            "di komputer ini. Pasang Microsoft Word, atau buka berkasnya di aplikasi pengolah kata lain lalu " +
            "pilih Simpan Sebagai → Dokumen Word (*.docx), kemudian ulangi proses ini.";

        /// <summary>Batas waktu konversi; berkas yang rusak/terkunci tidak boleh menggantungkan aplikasi.</summary>
        public static readonly TimeSpan BatasWaktu = TimeSpan.FromSeconds(90);

        private readonly ILogger<WordDocConverter> _logger;

        public WordDocConverter(ILogger<WordDocConverter> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public bool Tersedia => WordTerpasang();

        /// <summary>Microsoft Word tersedia untuk dipakai mengonversi berkas lama.</summary>
        public static bool WordTerpasang()
        {
            try
            {
                var tipe = Type.GetTypeFromProgID("Word.Application");
                return tipe != null && tipe != typeof(DBNull);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Folder berkas sementara hasil konversi (dibersihkan pemanggil).</summary>
        public static string FolderSementara =>
            Path.Combine(Path.GetTempPath(), "SuDesApp", "WordImpor");

        public async Task<string> KeDocxAsync(string berkasDoc, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(berkasDoc))
                throw new ArgumentException("Berkas .doc belum dipilih.", nameof(berkasDoc));
            if (!File.Exists(berkasDoc))
                throw new FileNotFoundException("Berkas .doc tidak ditemukan.", berkasDoc);

            if (!Tersedia)
            {
                throw new NotSupportedException(PesanTanpaWord);
            }

            Directory.CreateDirectory(FolderSementara);
            string tujuan = Path.Combine(FolderSementara,
                $"{Path.GetFileNameWithoutExtension(berkasDoc)}-{Guid.NewGuid():N}.docx");

            // COM Word memerlukan STA; pekerjaan dijalankan di thread tersendiri supaya
            // thread pemanggil (UI) tetap responsif selama Word membuka berkas.
            var pekerjaan = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try
                {
                    pekerjaan.SetResult(KonversiDiThread(berkasDoc, tujuan));
                }
                catch (Exception ex)
                {
                    pekerjaan.SetException(ex);
                }
            })
            {
                IsBackground = true,
                Name = "KonversiWordDoc"
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            // Timer batas waktu dihentikan begitu pekerjaan selesai agar tidak menahan
            // timer sia-sia (pembatalan hanya menyentuh timer, bukan pekerjaan Word).
            using var pembatalan = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var selesai = await Task.WhenAny(
                pekerjaan.Task, Task.Delay(BatasWaktu, pembatalan.Token)).ConfigureAwait(false);
            pembatalan.Cancel();

            if (selesai != pekerjaan.Task)
            {
                ct.ThrowIfCancellationRequested();
                throw new TimeoutException(
                    $"Konversi berkas .doc memakan waktu lebih dari {BatasWaktu.TotalSeconds:0} detik. " +
                    "Pastikan berkasnya tidak rusak dan tidak sedang dibuka di aplikasi lain.");
            }

            return await pekerjaan.Task.ConfigureAwait(false);
        }

        /// <summary>Bagian sinkron: jalankan Word, buka berkas, simpan sebagai .docx.</summary>
        private string KonversiDiThread(string berkasDoc, string tujuan)
        {
            var tipe = Type.GetTypeFromProgID("Word.Application");
            if (tipe == null || tipe == typeof(DBNull))
            {
                throw new NotSupportedException(PesanTanpaWord);
            }

            object? aplikasi = null;
            object? dokumen = null;
            try
            {
                aplikasi = Activator.CreateInstance(tipe)
                    ?? throw new InvalidOperationException("Microsoft Word tidak dapat dijalankan.");

                SetProperti(aplikasi, tipe, "Visible", false);
                SetProperti(aplikasi, tipe, "ScreenUpdating", false);
                SetProperti(aplikasi, tipe, "DisplayAlerts", 0);
                SetProperti(aplikasi, tipe, "AutomationSecurity", MakroMati);

                var documents = tipe.InvokeMember("Documents", AmbilProperti, null, aplikasi, null);
                if (documents == null)
                {
                    throw new InvalidOperationException("Microsoft Word tidak dapat membuka daftar dokumen.");
                }

                // Hanya parameter awal yang diisi (FileName, ConfirmConversions, ReadOnly,
                // AddToRecentFiles): parameter lanjutan Word berubah antar versi, dan
                // memberi nilai pada posisi yang salah membuat Word menolak membuka.
                // DisplayAlerts=0 membuat berkas bermasalah gagal cepat, bukan menunggu
                // pengguna menutup dialog.
                dokumen = documents.GetType().InvokeMember("Open", PanggilMetode, null, documents,
                    new object[]
                    {
                        berkasDoc,  // FileName
                        false,      // ConfirmConversions
                        true,       // ReadOnly
                        false       // AddToRecentFiles
                    });
                if (dokumen == null)
                {
                    throw new InvalidOperationException(
                        $"Berkas \"{Path.GetFileName(berkasDoc)}\" tidak dapat dibuka oleh Microsoft Word.");
                }

                SimpanSebagaiDocx(dokumen, tujuan);

                if (!File.Exists(tujuan))
                {
                    throw new InvalidOperationException("Microsoft Word tidak menghasilkan berkas .docx.");
                }

                _logger.LogInformation("Berkas .doc dikonversi ke .docx: {Berkas}", Path.GetFileName(berkasDoc));
                return tujuan;
            }
            finally
            {
                TutupWord(aplikasi, tipe, dokumen);
            }
        }

        /// <summary>
        /// Simpan dokumen sebagai .docx. SaveAs2 dipakai lebih dulu (Word 2010+);
        /// SaveAs jadi cadangan untuk versi Word yang belum memilikinya.
        /// </summary>
        private void SimpanSebagaiDocx(object dokumen, string tujuan)
        {
            var tipeDokumen = dokumen.GetType();
            try
            {
                tipeDokumen.InvokeMember("SaveAs2", PanggilMetode, null, dokumen,
                    new object[] { tujuan, FormatDocx });
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "SaveAs2 tidak tersedia pada Word ini; mencoba SaveAs.");
                tipeDokumen.InvokeMember("SaveAs", PanggilMetode, null, dokumen,
                    new object[] { tujuan, FormatDocx });
            }
        }

        /// <summary>Tutup dokumen & keluar dari Word — selalu tanpa menyimpan perubahan.</summary>
        private static void TutupWord(object? aplikasi, Type tipe, object? dokumen)
        {
            if (dokumen != null)
            {
                try { dokumen.GetType().InvokeMember("Close", PanggilMetode, null, dokumen, new object[] { false }); }
                catch { /* Word mungkin sudah menutupnya sendiri */ }
            }

            if (aplikasi == null) return;

            try { tipe.InvokeMember("Quit", PanggilMetode, null, aplikasi, new object[] { false }); }
            catch { /* proses Word berakhir sendiri */ }
            finally
            {
                try { System.Runtime.InteropServices.Marshal.ReleaseComObject(aplikasi); }
                catch { /* bukan objek COM lagi */ }
            }
        }

        private static void SetProperti(object sasaran, Type tipe, string nama, object nilai)
        {
            try
            {
                tipe.InvokeMember(nama, SetPropertiFlag, null, sasaran, new[] { nilai });
            }
            catch
            {
                // Properti opsional (mis. AutomationSecurity pada Word sangat lama).
            }
        }

        private const System.Reflection.BindingFlags AmbilProperti =
            System.Reflection.BindingFlags.GetProperty | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;

        private const System.Reflection.BindingFlags SetPropertiFlag =
            System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;

        private const System.Reflection.BindingFlags PanggilMetode =
            System.Reflection.BindingFlags.InvokeMethod | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
    }
}
