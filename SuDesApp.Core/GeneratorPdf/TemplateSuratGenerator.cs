// TemplateSuratGenerator.cs
// Generator PDF surat buatan pengguna (menu Template Surat).
//
// Berbeda dengan generator surat bawaan yang bentuknya tetap, generator ini
// menyusun halaman dari definisi TemplateSuratKustom: kop, judul, nomor, blok teks
// bebas, kolom isian (dengan atau tanpa grid), sampai tanda tangan dan teks kaki.
// Susunan yang dipilih pengguna saat wizard itulah yang tercetak.
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SuDesApp.GeneratorPdf
{
    /// <summary>Menyusun PDF surat dari definisi template buatan pengguna.</summary>
    public class TemplateSuratGenerator
    {
        private readonly AppConfig _config;
        private readonly IDesaRepository _desaRepository;
        private readonly ILogger<TemplateSuratGenerator> _logger;

        private static readonly CultureInfo Budaya = new("id-ID");

        /// <summary>Font didaftarkan sekali saja per proses (QuestPDF menyimpannya global).</summary>
        private static bool _fontTerdaftar;
        private static readonly object _fontLock = new();

        static TemplateSuratGenerator()
        {
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public TemplateSuratGenerator(
            AppConfig config,
            IDesaRepository desaRepository,
            ILogger<TemplateSuratGenerator> logger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _desaRepository = desaRepository ?? throw new ArgumentNullException(nameof(desaRepository));
            _logger = logger ?? NullLogger<TemplateSuratGenerator>.Instance;

            DaftarkanFont();
        }

        /// <summary>
        /// Daftarkan font di folder font ke QuestPDF supaya surat template memakai
        /// Times New Roman yang sama dengan surat bawaan. Generator ini bisa dipakai
        /// lebih dulu daripada generator lain, jadi fontnya didaftarkan sendiri.
        /// </summary>
        private void DaftarkanFont()
        {
            if (_fontTerdaftar) return;

            lock (_fontLock)
            {
                if (_fontTerdaftar) return;

                try
                {
                    var folder = _config.FontFolder;
                    if (!Directory.Exists(folder))
                    {
                        _logger.LogWarning("Folder font tidak ditemukan ({Folder}); surat template memakai font bawaan sistem.", folder);
                    }
                    else
                    {
                        foreach (var berkas in Directory.GetFiles(folder, "*.ttf"))
                        {
                            try
                            {
                                using var aliran = new MemoryStream(File.ReadAllBytes(berkas));
                                FontManager.RegisterFontFromStream(aliran);
                            }
                            catch
                            {
                                // Font ganda atau tidak valid diabaikan.
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Font folder tidak dapat dibaca; surat template memakai font bawaan sistem.");
                }

                _fontTerdaftar = true;
            }
        }

        /// <summary>
        /// Buat PDF surat. <paramref name="nilai"/> berisi isian pengguna dengan kunci
        /// kolom (<see cref="KolomTemplateSurat.Kunci"/>). Kolom yang kosong tetapi tidak
        /// wajib dicetak sebagai titik-titik agar bentuk formulirnya tetap terlihat.
        /// </summary>
        public async Task<byte[]> BuatPdfAsync(
            TemplateSuratKustom template,
            IReadOnlyDictionary<string, string>? nilai,
            string? nomorSurat,
            DateTime tanggalSurat,
            string? namaPejabat = null)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));

            var desa = await _desaRepository.GetInfoDesaAsync().ConfigureAwait(false);
            desa = KopSurat.DesaBersih(desa);

            ValidasiDefinisi(template, desa);

            byte[] hasil = Array.Empty<byte>();
            int halaman = 0;

            // Sama seperti surat bawaan: coba tampilan lapang dulu, lalu rapatkan
            // bertahap hanya bila isinya melimpah ke halaman berikutnya.
            foreach (var kerapatan in KerapatanSurat.Bertingkat)
            {
                using var _ = KerapatanSurat.Pakai(kerapatan);
                using var penampung = new MemoryStream();

                Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        SiapkanHalaman(page);

                        page.Content().Column(kolom =>
                        {
                            var badan = new BadanSurat();
                            SusunSurat(badan, template, nilai, nomorSurat, tanggalSurat, namaPejabat ?? string.Empty, desa);
                            foreach (var potongan in badan.Potongan)
                            {
                                var render = potongan;
                                kolom.Item().Element(c => render(c));
                            }
                        });
                    });
                }).GeneratePdf(penampung);

                hasil = penampung.ToArray();
                halaman = HitungJumlahHalaman(hasil);

                if (halaman <= 1)
                {
                    break;
                }

                _logger.LogInformation("Template surat '{Nama}' masih {Halaman} halaman pada kerapatan {Kerapatan}; mencoba yang lebih rapat.",
                    template.NamaTampil, halaman, kerapatan.Nama);
            }

            if (halaman > 1)
            {
                _logger.LogWarning("Template surat '{Nama}' tetap {Halaman} halaman meski sudah dirapatkan.", template.NamaTampil, halaman);
            }

            return hasil;
        }

        /// <summary>Buat PDF lalu tulis ke aliran keluaran.</summary>
        public async Task GeneratePdfAsync(
            Stream outputStream,
            TemplateSuratKustom template,
            IReadOnlyDictionary<string, string>? nilai,
            string? nomorSurat,
            DateTime tanggalSurat,
            string? namaPejabat = null)
        {
            if (outputStream == null || !outputStream.CanWrite)
            {
                throw new InvalidOperationException("Output stream tidak dapat ditulis.");
            }

            var pdf = await BuatPdfAsync(template, nilai, nomorSurat, tanggalSurat, namaPejabat).ConfigureAwait(false);
            await outputStream.WriteAsync(pdf, 0, pdf.Length).ConfigureAwait(false);
        }

        // =====================================================================
        // Tata letak
        // =====================================================================

        /// <summary>Halaman mengikuti Pengaturan Cetak, margin mengikuti kerapatan aktif.</summary>
        private static void SiapkanHalaman(PageDescriptor page)
        {
            var (lebar, tinggi) = PengaturanCetak.Dimensi();
            var kerapatan = KerapatanSurat.Aktif;

            page.Size(new PageSize(lebar, tinggi, Unit.Point));
            page.MarginTop(kerapatan.MarginAtas, Unit.Point);
            page.MarginRight(45, Unit.Point);
            page.MarginBottom(kerapatan.MarginBawah, Unit.Point);
            page.MarginLeft(45, Unit.Point);
            page.DefaultTextStyle(x => x.FontFamily("Times New Roman").FontSize(SuratRenderer.UkuranTeks));
        }

        /// <summary>Susun seluruh elemen surat sesuai pilihan pada definisi template.</summary>
        private void SusunSurat(
            BadanSurat badan,
            TemplateSuratKustom template,
            IReadOnlyDictionary<string, string>? nilai,
            string? nomorSurat,
            DateTime tanggalSurat,
            string namaPejabat,
            DesaData desa)
        {
            // 1. Kop surat desa (opsional).
            if (template.PakaiKop)
            {
                string? logoPath = PengaturanCetak.JalurLogoEfektif(_config.LogoPath);
                badan.Blok(c => SuratRenderer.Kop(c, desa, logoPath!));
            }

            // 2. Judul + nomor (opsional).
            string judul = (template.Judul ?? string.Empty).Trim();
            string subJudul = (template.SubJudul ?? string.Empty).Trim();
            string nomor = (nomorSurat ?? string.Empty).Trim();

            if (judul.Length > 0 || (template.PakaiNomor && nomor.Length > 0))
            {
                string nomorCetak = template.PakaiNomor ? nomor : string.Empty;
                badan.Blok(c => SuratRenderer.JudulDanNomor(c, judul, nomorCetak));
                if (subJudul.Length > 0)
                {
                    badan.Paragraf(subJudul, rata: Rata.Tengah, tebal: true, jarakBawah: 10);
                }
            }

            // 3. Tempat & tanggal (rata kanan).
            if (template.PakaiTempatTanggal)
            {
                string namaDesa = string.IsNullOrWhiteSpace(desa?.NamaDesa) ? string.Empty : desa!.NamaDesa;
                string tanggal = tanggalSurat.ToString("dd MMMM yyyy", Budaya);
                badan.Paragraf(string.IsNullOrWhiteSpace(namaDesa) ? tanggal : $"{namaDesa}, {tanggal}",
                    rata: Rata.Kanan, jarakBawah: 10);
            }

            // 4. Badan surat: blok teks, data diri, dan kolom isian, menurut urutannya.
            foreach (var bagian in template.BagianEfektif)
            {
                SusunBagian(badan, bagian, template, nilai);
            }

            // 5. Tanda tangan pejabat.
            if (template.PakaiTandaTangan)
            {
                (string jabatan, string namaPejabatCetak) = JatuhkanPenandatangan(template, desa, nilai, namaPejabat);
                string tanggal = tanggalSurat.ToString("dd MMMM yyyy", Budaya);
                string namaDesa = string.IsNullOrWhiteSpace(desa?.NamaDesa) ? string.Empty : desa!.NamaDesa;

                badan.Blok(c => SuratRenderer.TandaTangan(
                    c,
                    tampilkanPemohon: false,
                    namaPemohon: string.Empty,
                    namaDesa: namaDesa,
                    tanggalTerformat: tanggal,
                    jabatan: jabatan,
                    namaPejabat: string.IsNullOrWhiteSpace(namaPejabatCetak) ? "_______________________" : NamaFormatter.ToUpperNama(namaPejabatCetak)));
            }

            // 6. Teks kaki tambahan.
            if (template.PakaiTeksKaki && !string.IsNullOrWhiteSpace(template.TeksKaki))
            {
                badan.Paragraf(template.TeksKaki, jarakAtas: 12, miring: false);
            }
        }

        /// <summary>
        /// Render satu bagian terurut dari badan surat: teks bebas, blok data diri,
        /// atau kelompok kolom isian (grid / baris label : nilai).
        /// </summary>
        private void SusunBagian(
            BadanSurat badan,
            BagianTemplateSurat bagian,
            TemplateSuratKustom template,
            IReadOnlyDictionary<string, string>? nilai)
        {
            if (bagian == null) return;

            if (bagian.Tipe == TipeBagianTemplate.Teks)
            {
                if (string.IsNullOrWhiteSpace(bagian.Isi)) return;

                var rata = bagian.Rata switch
                {
                    RataBlokTemplate.Tengah => Rata.Tengah,
                    RataBlokTemplate.Kanan => Rata.Kanan,
                    RataBlokTemplate.Justify => Rata.Justify,
                    _ => Rata.Kiri
                };
                badan.Paragraf(bagian.Isi, tebal: bagian.Tebal, miring: bagian.Miring, rata: rata, jarakBawah: 3);
                return;
            }

            var kolomIsi = (bagian.Kolom ?? new List<KolomTemplateSurat>())
                .Where(k => k != null && !string.IsNullOrWhiteSpace(k.Label))
                .ToList();
            if (kolomIsi.Count == 0) return;

            if (!string.IsNullOrWhiteSpace(bagian.JudulKelompok))
            {
                badan.Paragraf(bagian.JudulKelompok.Trim(), tebal: true, jarakBawah: 3, jarakAtas: 4);
            }

            var baris = kolomIsi
                .Select(k =>
                {
                    string isi = TemplateSuratNilai.NilaiCetak(k, TemplateSuratNilai.Ambil(nilai, k.Kunci));
                    if (isi.Length == 0) isi = k.Wajib ? "...................." : string.Empty;
                    return (Label: k.Label.Trim(), Nilai: (string?)isi);
                })
                .ToList();

            if (bagian.Tipe == TipeBagianTemplate.DataDiri)
            {
                // Blok identitas orang: baris label : nilai, nama ditebalkan seperti surat resmi.
                badan.TabelFormulir(baris, jarakAtas: 3, jarakBawah: 6, tebalkanNama: true, indentKiri: 20);
                return;
            }

            // Kelompok kolom isian biasa (tagar Kolom).
            if (bagian.Grid)
            {
                badan.Blok(c => TabelGrid(c, baris));
            }
            else
            {
                badan.TabelFormulir(baris, jarakAtas: 3, jarakBawah: 6, tebalkanNama: false, indentKiri: 20);
            }
        }

        /// <summary>
        /// Tentukan jabatan dan nama penandatangan. Bila template memilih salah satu
        /// blok Data Diri sebagai penandatangan, nama diambil dari kolom "Nama" dan
        /// jabatan dari kolom "Jabatan" blok tersebut; bila kosong, jatuh ke pejabat desa
        /// menurut jabatan yang dipilih (Sekretaris Desa atau Kepala Desa).
        /// </summary>
        private static (string Jabatan, string Nama) JatuhkanPenandatangan(
            TemplateSuratKustom template,
            DesaData? desa,
            IReadOnlyDictionary<string, string>? nilai,
            string namaPejabat)
        {
            string namaDesa = string.IsNullOrWhiteSpace(desa?.NamaDesa) ? string.Empty : desa!.NamaDesa;

            // Penandatangan dari blok Data Diri yang dipilih pengguna.
            string kunciBlok = (template.KunciPenandatanganDataDiri ?? string.Empty).Trim();
            if (template.PenandatanganDariDataDiri && kunciBlok.Length > 0)
            {
                var blok = template.BagianEfektif.FirstOrDefault(b =>
                    b.Tipe == TipeBagianTemplate.DataDiri && string.Equals(b.Kunci, kunciBlok, StringComparison.OrdinalIgnoreCase));
                if (blok != null)
                {
                    var kolomNama = blok.Kolom.FirstOrDefault(k => string.Equals(k.Label, "Nama", StringComparison.OrdinalIgnoreCase));
                    var nilaiNama = kolomNama == null ? string.Empty : TemplateSuratNilai.NilaiCetak(kolomNama, TemplateSuratNilai.Ambil(nilai, kolomNama.Kunci));
                    if (!string.IsNullOrWhiteSpace(nilaiNama))
                    {
                        var kolomJabatan = blok.Kolom.FirstOrDefault(k => string.Equals(k.Label, "Jabatan", StringComparison.OrdinalIgnoreCase));
                        string jabatanDipilih = kolomJabatan == null
                            ? string.Empty
                            : TemplateSuratNilai.NilaiCetak(kolomJabatan, TemplateSuratNilai.Ambil(nilai, kolomJabatan.Kunci));

                        string jabatan = JabatanCetak(jabatanDipilih, namaDesa);
                        return (jabatan, NamaFormatter.ToUpperNama(nilaiNama));
                    }
                }
            }

            // Nilai default menurut jabatan: Sekretaris Desa memakai nama sekretaris desa,
            // selain itu Kepala Desa.
            string jabatanTemplate = template.JabatanPenandatangan ?? string.Empty;
            string pejabat;
            if (!string.IsNullOrWhiteSpace(namaPejabat))
            {
                pejabat = namaPejabat;
            }
            else if (jabatanTemplate.Contains("Sekretaris", StringComparison.OrdinalIgnoreCase))
            {
                pejabat = string.IsNullOrWhiteSpace(desa?.SekretarisDesa)
                    ? (desa?.KepalaDesa ?? string.Empty)
                    : desa!.SekretarisDesa!;
            }
            else
            {
                pejabat = desa?.KepalaDesa ?? string.Empty;
            }

            return (JabatanCetak(jabatanTemplate, namaDesa), pejabat);
        }

        /// <summary>
        /// Tabel bergaris: setiap kolom menjadi satu baris berlabel. Barisnya diberi
        /// garis tipis supaya isinya terbaca sebagai grid.
        /// </summary>
        private static void TabelGrid(IContainer container, IReadOnlyList<(string Label, string? Nilai)> baris)
        {
            float leading = KerapatanSurat.Aktif.LeadingSelTabel;
            float padding = KerapatanSurat.Aktif.PaddingSelTabel;

            container.PaddingTop(SuratRenderer.JarakBlok(6)).PaddingBottom(SuratRenderer.JarakBlok(6)).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(150);
                    columns.RelativeColumn();
                });

                foreach (var (label, nilai) in baris)
                {
                    table.Cell()
                        .Border(0.5f).BorderColor(Colors.Grey.Medium)
                        .PaddingTop(padding).PaddingBottom(padding).PaddingLeft(4).PaddingRight(4)
                        .Text(label).FontSize(SuratRenderer.UkuranTeks).LineHeight(leading / SuratRenderer.UkuranTeks);

                    table.Cell()
                        .Border(0.5f).BorderColor(Colors.Grey.Medium)
                        .PaddingTop(padding).PaddingBottom(padding).PaddingLeft(4).PaddingRight(4)
                        .Text(string.IsNullOrWhiteSpace(nilai) ? " " : nilai)
                        .FontSize(SuratRenderer.UkuranTeks).LineHeight(leading / SuratRenderer.UkuranTeks);
                }
            });
        }

        /// <summary>
        /// Jabatan penandatangan yang tercetak (kapital, mengikuti template resmi).
        /// <paramref name="dipilih"/> adalah jabatan yang diketik pengguna (bisa kosong
        /// untuk Kepala Desa). Sekretaris Desa otomatis diberi awalan "A/N KEPALA DESA".
        /// </summary>
        private static string JabatanCetak(string? dipilih, string? namaDesa)
        {
            string namaDesaBersih = string.IsNullOrWhiteSpace(namaDesa) ? string.Empty : namaDesa!.Trim();
            string teks = (dipilih ?? string.Empty).Trim();

            if (teks.Length == 0)
            {
                return string.IsNullOrWhiteSpace(namaDesaBersih) ? "KEPALA DESA" : $"KEPALA DESA {namaDesaBersih.ToUpperInvariant()}";
            }

            if (teks.Contains("Sekretaris", StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(namaDesaBersih)
                    ? "SEKRETARIS DESA"
                    : $"A/N KEPALA DESA {namaDesaBersih.ToUpperInvariant()}\nSEKRETARIS DESA";
            }

            return teks.ToUpperInvariant();
        }

        // =====================================================================
        // Pemeriksaan
        // =====================================================================

        /// <summary>
        /// Jumlah halaman dokumen, dibaca dari pohon halaman PDF yang dihasilkan QuestPDF.
        /// </summary>
        private static int HitungJumlahHalaman(byte[] pdf)
        {
            // Latin1: satu byte = satu karakter, sehingga pola dapat dicari langsung.
            string isi = System.Text.Encoding.Latin1.GetString(pdf);
            var cocok = System.Text.RegularExpressions.Regex.Match(isi, @"/Type\s*/Pages[\s\S]{0,256}?/Count\s+(\d+)");
            return cocok.Success && int.TryParse(cocok.Groups[1].Value, out int jumlah) ? jumlah : 1;
        }

        /// <summary>
        /// Definisi harus menghasilkan surat yang bisa dicetak: ada minimal satu
        /// elemen, dan desa terkonfigurasi bila kop atau tanda tangan dipakai.
        /// </summary>
        private static void ValidasiDefinisi(TemplateSuratKustom template, DesaData? desa)
        {
            if (template.JumlahElemen == 0)
            {
                throw new InvalidOperationException("Template belum memiliki elemen apa pun. Tambahkan kop, judul, nomor, teks, kolom, atau tanda tangan terlebih dahulu.");
            }

            if (template.PakaiKop || template.PakaiTandaTangan || template.PakaiTempatTanggal)
            {
                var kurang = new List<string>();
                if (string.IsNullOrWhiteSpace(desa?.NamaDesa)) kurang.Add("Nama Desa");
                if (template.PakaiKop)
                {
                    if (string.IsNullOrWhiteSpace(desa?.Kecamatan)) kurang.Add("Kecamatan");
                    if (string.IsNullOrWhiteSpace(desa?.Kabupaten)) kurang.Add("Kabupaten");
                    if (string.IsNullOrWhiteSpace(desa?.Alamat)) kurang.Add("Alamat Kantor Desa");
                }

                if (kurang.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"Data desa belum lengkap. Isi kolom berikut di Setelan Aplikasi: {string.Join(", ", kurang)}.");
                }
            }
        }
    }
}
