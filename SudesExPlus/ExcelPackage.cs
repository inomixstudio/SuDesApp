using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SudesExPlus;

/// <summary>
/// Satu berkas Excel (.xlsx). Bisa dibuat baru (<c>new ExcelPackage()</c>) atau
/// dibuka dari berkas yang sudah ada; <see cref="SaveAs(Stream)"/> dan
/// <see cref="SaveAsAsync(FileInfo)"/> menulis seluruh workbook sebagai .xlsx
/// standar.
///
/// Kelas ini sengaja memakai nama dan bentuk pemanggilan yang sama dengan
/// pustaka sebelumnya agar kode pemanggil tidak berubah selain namespace-nya.
/// </summary>
public sealed class ExcelPackage : IDisposable
{
    private bool _dibuang;

    /// <summary>Workbook kosong untuk dibuat dari awal.</summary>
    public ExcelPackage()
    {
        Workbook = new ExcelWorkbook();
    }

    /// <summary>Buka berkas .xlsx yang sudah ada.</summary>
    public ExcelPackage(string path) : this(new FileInfo(path))
    {
    }

    /// <summary>Buka berkas .xlsx yang sudah ada.</summary>
    public ExcelPackage(FileInfo file)
    {
        ArgumentNullException.ThrowIfNull(file);
        Workbook = new ExcelWorkbook();

        if (file.Exists)
        {
            XlsxReader.Muat(file.FullName, Workbook);
        }
    }

    public ExcelWorkbook Workbook { get; }

    /// <summary>Tulis workbook ke aliran (mis. MemoryStream) sebagai .xlsx.</summary>
    public void SaveAs(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ObjectDisposedException.ThrowIf(_dibuang, this);
        XlsxWriter.Tulis(this, stream);
    }

    /// <summary>Tulis workbook ke berkas baru (menimpa bila sudah ada).</summary>
    public void SaveAs(FileInfo file)
    {
        ArgumentNullException.ThrowIfNull(file);
        using var aliran = new FileStream(file.FullName, FileMode.Create, FileAccess.Write, FileShare.None);
        SaveAs(aliran);
    }

    /// <summary>Tulis workbook ke berkas baru (menimpa bila sudah ada).</summary>
    public void SaveAs(string path) => SaveAs(new FileInfo(path));

    /// <summary>Tulis workbook ke aliran secara asinkron.</summary>
    public async Task SaveAsAsync(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ObjectDisposedException.ThrowIf(_dibuang, this);

        using var penampung = new MemoryStream();
        XlsxWriter.Tulis(this, penampung);
        penampung.Position = 0;
        await penampung.CopyToAsync(stream).ConfigureAwait(false);
    }

    /// <summary>Tulis workbook ke berkas secara asinkron.</summary>
    public async Task SaveAsAsync(FileInfo file)
    {
        ArgumentNullException.ThrowIfNull(file);

        using var penampung = new MemoryStream();
        XlsxWriter.Tulis(this, penampung);
        await File.WriteAllBytesAsync(file.FullName, penampung.ToArray()).ConfigureAwait(false);
    }

    /// <summary>
    /// Tidak ada sumber daya yang perlu dilepas di luar aliran yang dibuka
    /// pemanggil; metode ini ada supaya pola <c>using var package = …</c> tetap
    /// berlaku dan pemakaian setelah dispose gagal dengan jelas.
    /// </summary>
    public void Dispose() => _dibuang = true;
}

/// <summary>Workbook: kumpulan worksheet.</summary>
public sealed class ExcelWorkbook
{
    internal ExcelWorkbook() => Worksheets = new ExcelWorksheetCollection();

    public ExcelWorksheetCollection Worksheets { get; }
}

/// <summary>Koleksi worksheet berurutan sesuai tab di Excel.</summary>
public sealed class ExcelWorksheetCollection : IEnumerable<ExcelWorksheet>
{
    private readonly List<ExcelWorksheet> _daftar = new();

    /// <summary>Banyaknya worksheet.</summary>
    public int Count => _daftar.Count;

    /// <summary>Worksheet menurut urutan tab (0-based).</summary>
    public ExcelWorksheet this[int indeks] => _daftar[indeks];

    /// <summary>Worksheet menurut nama; null bila tidak ada.</summary>
    public ExcelWorksheet? this[string nama] =>
        _daftar.FirstOrDefault(w => string.Equals(w.Name, nama, StringComparison.OrdinalIgnoreCase));

    /// <summary>Tambah worksheet baru dengan nama unik.</summary>
    public ExcelWorksheet Add(string nama)
    {
        string bersih = NamaAman(nama);
        string unik = bersih;
        int urutan = 2;

        while (_daftar.Any(w => string.Equals(w.Name, unik, StringComparison.OrdinalIgnoreCase)))
        {
            string akhiran = " " + urutan.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string awalan = bersih.Length > 31 - akhiran.Length
                ? bersih[..(31 - akhiran.Length)].TrimEnd()
                : bersih;
            unik = awalan + akhiran;
            urutan++;
        }

        var lembar = new ExcelWorksheet(unik);
        _daftar.Add(lembar);
        return lembar;
    }

    /// <summary>Tambah worksheet yang sudah dibaca dari berkas.</summary>
    internal ExcelWorksheet TambahDariBerkas(string nama)
    {
        var lembar = new ExcelWorksheet(nama);
        _daftar.Add(lembar);
        return lembar;
    }

    public IEnumerator<ExcelWorksheet> GetEnumerator() => _daftar.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Bersihkan nama sheet: maksimal 31 karakter, tanpa karakter terlarang
    /// Excel (<c>: \ / ? * [ ]</c>), dan tidak kosong.
    /// </summary>
    internal static string NamaAman(string? nama)
    {
        string hasil = (nama ?? string.Empty).Trim();
        foreach (char terlarang in new[] { ':', '\\', '/', '?', '*', '[', ']' })
        {
            hasil = hasil.Replace(terlarang, '-');
        }

        hasil = hasil.Trim();
        if (hasil.Length == 0) hasil = "Sheet";
        if (hasil.Length > 31) hasil = hasil[..31].TrimEnd();
        return hasil;
    }
}
