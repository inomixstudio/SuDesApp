using System;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace SuDesApp.Wpf.Services
{
    /// <summary>
    /// Menyimpan log level tertentu ke satu berkas teks dengan rotasi berdasarkan
    /// ukuran. Tanpa ini, logger WPF hanya menulis ke konsol/Debug — tidak terlihat
    /// pada instalasi yang dibuka lewat menu Mulai — sehingga galat penting hilang.
    ///
    /// Berkas aktif dibatasi <c>batasByte</c>; bila terlampaui, berkas itu digeser
    /// menjadi <c>.1</c>, arsip lama bergeser, dan arsip di luar <c>jumlahArsip</c>
    /// dihapus. Total pemakaian karenanya tetap terbatas dan tidak tumbuh selamanya.
    /// </summary>
    internal sealed class BerkasLogProvider : ILoggerProvider
    {
        private readonly object _kunci = new();
        private readonly string _jalur;
        private readonly long _batasByte;
        private readonly int _jumlahArsip;
        private readonly LogLevel _minimum;

        internal BerkasLogProvider(string jalur, LogLevel minimum, long batasByte, int jumlahArsip)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(jalur);

            _jalur = Path.GetFullPath(jalur);
            _minimum = minimum;
            _batasByte = Math.Max(1, batasByte);
            _jumlahArsip = Math.Max(1, jumlahArsip);
        }

        internal string Jalur => _jalur;

        public ILogger CreateLogger(string categoryName) => new BerkasLogLogger(this, categoryName);

        public void Dispose()
        {
            // Tidak ada aliran yang ditahan: setiap baris ditulis lalu ditutup,
            // jadi berkas boleh dibuka operator saat aplikasi berjalan.
        }

        private bool IsEnabled(LogLevel level) =>
            level != LogLevel.None && level >= _minimum;

        private void Tulis(string kategori, LogLevel level, string pesan, Exception? galat)
        {
            lock (_kunci)
            {
                try
                {
                    string? folder = Path.GetDirectoryName(_jalur);
                    if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

                    try { PutarBilaBesar(); }
                    catch
                    {
                        // Arsip terkunci/penuh: tetap tulis ke berkas aktif agar
                        // baris peringatan/galat tidak hilang.
                    }

                    var baris = new StringBuilder()
                        .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
                        .Append(" [").Append(level).Append("] ")
                        .Append(kategori).Append(": ").Append(pesan)
                        .AppendLine();

                    if (galat is not null) baris.AppendLine(galat.ToString());

                    File.AppendAllText(_jalur, baris.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                }
                catch
                {
                    // Log tidak boleh menjatuhkan aplikasi. Kegagalan menulis
                    // (disk penuh, folder tidak bisa ditulis) dibiarkan lewat;
                    // konsol/Debug dan dialog pengguna tetap menjadi jalur lain.
                }
            }
        }

        private void PutarBilaBesar()
        {
            if (!File.Exists(_jalur)) return;
            if (new FileInfo(_jalur).Length < _batasByte) return;

            string arsipTerakhir = $"{_jalur}.{_jumlahArsip}";
            if (File.Exists(arsipTerakhir)) File.Delete(arsipTerakhir);

            for (int i = _jumlahArsip - 1; i >= 1; i--)
            {
                string sumber = $"{_jalur}.{i}";
                if (File.Exists(sumber)) File.Move(sumber, $"{_jalur}.{i + 1}", overwrite: true);
            }

            File.Move(_jalur, $"{_jalur}.1", overwrite: true);
        }

        private sealed class BerkasLogLogger : ILogger
        {
            private readonly BerkasLogProvider _induk;
            private readonly string _kategori;

            internal BerkasLogLogger(BerkasLogProvider induk, string kategori)
            {
                _induk = induk;
                _kategori = kategori;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => _induk.IsEnabled(logLevel);

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel)) return;

                string pesan;
                try
                {
                    pesan = formatter(state, exception) ?? string.Empty;
                }
                catch
                {
                    // Formatter pihak lain tidak boleh membuat logger melempar.
                    pesan = state?.ToString() ?? string.Empty;
                }

                _induk.Tulis(_kategori, logLevel, pesan, exception);
            }
        }
    }
}
