// File: SuDesApp/GeneratorPdf/AhliWarisGenerator.cs
// Dokumen 3 halaman: Surat Keterangan, Surat Pernyataan, dan Surat Kuasa.
// Seluruh tata letak disusun langsung dengan QuestPDF (tanpa lapisan kompat).
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SuDesApp.GeneratorPdf
{
    public class AhliWarisGenerator : SuratGeneratorBase
    {
        protected override string JudulSurat => "SURAT KETERANGAN AHLI WARIS";

        // Tiga halaman memang bentuk dokumennya: keterangan, pernyataan, kuasa.
        protected override int HalamanMaksimal => 3;

        protected override bool UseDefaultHeader => true;
        protected override bool UseDefaultFooter => false;

        private const float UkuranTeks = 12f;
        private const float UkuranJudulHalaman = 14f;
        private const float LeadingBarisTabel = 15f;
        private const float IndentTabel = 20f;

        public AhliWarisGenerator(
            AppConfig config,
            FileService fileService,
            IDesaRepository desaRepository,
            ISuratRepository suratRepository,
            SettingsManager settingsManager,
            ILogger<AhliWarisGenerator> logger,
            ILoggerFactory loggerFactory)
            : base(config, fileService, desaRepository, suratRepository, settingsManager, logger, loggerFactory)
        {
        }

        /// <summary>
        /// Tiga halaman dalam satu dokumen: halaman 1 ber-kop, halaman 2 dan 3
        /// hanya judul halaman (tanpa kop).
        /// </summary>
        protected override void ComposeHalaman(BadanSurat halaman, SuratData suratData, string keteranganTextBox = null)
        {
            var desa = suratData.Desa;
            string logoPath = CariLogoPath();

            halaman.Blok(c => SuratRenderer.Kop(c, desa, logoPath));
            ComposeJudul(halaman, suratData);

            // Halaman 1: Surat Keterangan Ahli Waris
            ComposeContentHalaman1(halaman, suratData, keteranganTextBox);
            ComposeKakiHalaman1(halaman, suratData);

            // Halaman 2: Surat Pernyataan Ahli Waris
            halaman.Blok(c => c.PageBreak());
            ComposePernyataan(halaman, suratData, keteranganTextBox);

            // Halaman 3: Surat Kuasa Ahli Waris (bila ada pasangan dan anak)
            var ahliWaris = suratData.AhliWarisData ?? new AhliWarisData();
            if (ShouldGenerateKuasaPage(ahliWaris))
            {
                halaman.Blok(c => c.PageBreak());
                ComposeKuasa(halaman, suratData, keteranganTextBox);
            }
        }

        /// <summary>
        /// Helper method untuk mendapatkan prefix Almarhum/Almarhumah berdasarkan jenis kelamin
        /// </summary>
        private static string GetAlmarhumPrefix(string? jenisKelamin)
        {
            return string.Equals(jenisKelamin, "Perempuan", StringComparison.OrdinalIgnoreCase)
                ? "Almarhumah"
                : "Almarhum";
        }

        /// <summary>
        /// Helper method untuk mendapatkan nama lengkap dengan prefix Almarhum/Almarhumah
        /// </summary>
        private static string GetAlmarhumFullName(string? nama, string? jenisKelamin)
            => $"{GetAlmarhumPrefix(jenisKelamin)} {nama}";

        private static bool ShouldGenerateKuasaPage(AhliWarisData ahliWaris)
        {
            if (ahliWaris == null) return false;

            return !string.IsNullOrWhiteSpace(ahliWaris.NamaPasangan) &&
                   ahliWaris.JumlahAnak > 0 &&
                   ahliWaris.Anak?.Count > 0;
        }

        private static string FormatAlamatForDisplay(string? alamatFull)
        {
            if (string.IsNullOrWhiteSpace(alamatFull))
                return "[Alamat]";

            // Pisahkan alamat berdasarkan koma
            var parts = alamatFull.Split(',');
            var result = new StringBuilder();

            // Format bagian pertama (biasanya dusun/desa)
            if (parts.Length > 0)
                result.Append(parts[0].Trim());

            // Format bagian kedua (biasanya kecamatan)
            if (parts.Length > 1)
                result.Append("\n").Append(parts[1].Trim());

            // Format bagian ketiga (biasanya kabupaten)
            if (parts.Length > 2)
                result.Append(" ").Append(parts[2].Trim());

            return result.ToString();
        }

        private void ComposeContentHalaman1(BadanSurat halaman, SuratData suratData, string? keteranganTextBox)
        {
            var almarhum = suratData.Warga ?? new WargaData();
            var kematian = suratData.Kematian ?? new KematianData();
            var ahliWaris = suratData.AhliWarisData ?? new AhliWarisData();

            var almarhumFullName = GetAlmarhumFullName(almarhum.Nama, almarhum.JenisKelamin);
            string alamatAlmarhum = $"{almarhum.Dusun} Desa {almarhum.Desa} Kecamatan {almarhum.Kecamatan} Kabupaten {almarhum.Kabupaten}";

            // Paragraf pembuka: nama desa/kecamatan/kabupaten dan nama almarhum dicetak tebal.
            halaman.ParagrafCampur(new List<(string, bool)>
            {
                ("Yang bertanda tangan di bawah ini, Kepala Desa ", false),
                (suratData.Desa?.NamaDesa ?? "Desa", true),
                (" Kecamatan ", false),
                (suratData.Desa?.Kecamatan ?? "Kecamatan", true),
                (" Kabupaten ", false),
                (suratData.Desa?.Kabupaten ?? "Kabupaten", true),
                (", menerangkan berdasarkan Surat Pernyataan Ahli Waris ", false),
                (almarhumFullName, true),
                (" tanggal ", false),
                (suratData.TanggalSurat.ToString("dd MMMM yyyy", new CultureInfo("id-ID")), false),
                (" bahwa mendiang ", false),
                (almarhumFullName, true),
                (" telah meninggal dunia pada hari ", false),
                (kematian.HariKematian?.ToLower() ?? "hari", false),
                (", tanggal ", false),
                (FormatTanggalTerbilang(kematian.TanggalKematian).ToLower(), false),
                (", di kediaman yang terakhir yang beralamat di ", false),
                (alamatAlmarhum, false)
            },
            rata: Rata.Justify,
            jarakAtas: 15);

            // Data Pasangan
            if (!string.IsNullOrWhiteSpace(ahliWaris.NamaPasangan))
            {
                halaman.Paragraf($"Semasa hidupnya, mendiang {almarhumFullName} pernah menikah/berumah tangga dengan seorang {(ahliWaris.JenisKelaminPasangan == "Perempuan" ? "perempuan" : "laki-laki")} yakni:",
                    rata: Rata.Justify,
                    jarakAtas: 10);

                TambahTabelFormulir(halaman, CreateDataPasangan(ahliWaris));
            }

            // Data Anak
            if (ahliWaris.JumlahAnak > 0 && ahliWaris.Anak != null)
            {
                halaman.Paragraf($"Dari pernikahan tersebut, dikaruniai anak/keturunan sebanyak {ahliWaris.JumlahAnak} ({Terbilang(ahliWaris.JumlahAnak)}) orang, yakni:",
                    rata: Rata.Justify,
                    jarakAtas: 10);

                TambahTabelFormulir(halaman, CreateDataAnak(ahliWaris, 1));
            }
            else
            {
                halaman.Paragraf("Tidak ada anak/keturunan dari pernikahan tersebut.", jarakAtas: 10);
            }

            // Tujuan Surat
            halaman.Paragraf($"Surat keterangan ini dibuat untuk keperluan {keteranganTextBox ?? "Pengurusan Klaim Asuransi"} atas nama {almarhumFullName}.",
                rata: Rata.Justify,
                jarakAtas: 10);
            halaman.Paragraf("Demikian Surat Keterangan Ahli Waris ini dibuat dengan sebenarnya.",
                rata: Rata.Justify,
                jarakAtas: 5);
        }

        private void ComposePernyataan(BadanSurat halaman, SuratData suratData, string? keteranganTextBox)
        {
            var almarhum = suratData.Warga ?? new WargaData();
            var kematian = suratData.Kematian ?? new KematianData();
            var ahliWaris = suratData.AhliWarisData ?? new AhliWarisData();

            var almarhumFullName = GetAlmarhumFullName(almarhum.Nama, almarhum.JenisKelamin);
            string alamatAlmarhum = $"{almarhum.Dusun} Desa {almarhum.Desa} Kecamatan {almarhum.Kecamatan} Kabupaten {almarhum.Kabupaten}";

            // Judul halaman
            halaman.Blok(c => SuratRenderer.JudulTengah(c, "SURAT PERNYATAAN AHLI WARIS", UkuranJudulHalaman));

            // Pengantar
            halaman.ParagrafCampur(new List<(string, bool)>
            {
                ("Yang bertanda tangan/Cap Jempol di bawah ini, kami para Ahli Waris dari ", false),
                (almarhumFullName, true),
                (", dan siap diangkat sumpah apabila memberikan keterangan yang tidak benar, bahwa:", false)
            },
            rata: Rata.Justify,
            jarakAtas: 15);

            // Catatan: paragraf “telah meninggal dunia” pada halaman pernyataan tidak
            // pernah dicetak oleh versi sebelumnya, jadi tidak ditambahkan di sini juga.

            // Data Pasangan
            if (!string.IsNullOrWhiteSpace(ahliWaris.NamaPasangan))
            {
                halaman.Paragraf($"Semasa hidupnya, mendiang {almarhumFullName} pernah menikah/berumah tangga dengan seorang {(ahliWaris.JenisKelaminPasangan == "Perempuan" ? "perempuan" : "laki-laki")}, yakni:",
                    rata: Rata.Justify,
                    jarakAtas: 10);

                TambahTabelFormulir(halaman, CreateDataPasangan(ahliWaris));
            }

            // Data Anak
            if (ahliWaris.JumlahAnak > 0 && ahliWaris.Anak != null)
            {
                halaman.Paragraf($"Dari pernikahan tersebut, {almarhumFullName} dikaruniai anak/keturunan sebanyak {ahliWaris.JumlahAnak} ({Terbilang(ahliWaris.JumlahAnak)}) orang, yaitu:",
                    rata: Rata.Justify,
                    jarakAtas: 10);

                TambahTabelFormulir(halaman, CreateDataAnak(ahliWaris, 1));
            }
            else
            {
                halaman.Paragraf("Tidak ada anak/keturunan dari pernikahan tersebut.", jarakAtas: 10);
            }

            // Penutup
            halaman.Paragraf("Demikian Surat Pernyataan Ahli Waris ini dibuat dan ditandatangani dengan sebenarnya, dalam keadaan sehat jasmani dan rohani, tanpa adanya unsur paksaan atau bujukan dari pihak manapun. Kami siap mempertanggungjawabkan kebenaran pernyataan ini apabila diperlukan, dan tidak ada ahli waris lain selain yang tersebut di atas.",
                rata: Rata.Justify,
                jarakAtas: 10);

            // Tanda Tangan
            string tglSurat = suratData.TanggalSurat.ToString("dd MMMM yyyy", new CultureInfo("id-ID"));
            halaman.Paragraf($"{suratData.Desa?.NamaDesa ?? "Desa"}, {tglSurat}", rata: Rata.Tengah, jarakAtas: 30);
            halaman.Paragraf("Para Ahli Waris", tebal: true, rata: Rata.Tengah, jarakAtas: 10);

            // Daftar nama penanda tangan (pasangan + anak)
            List<string> penandatangan = new List<string>();
            if (!string.IsNullOrWhiteSpace(ahliWaris.NamaPasangan))
                penandatangan.Add(ahliWaris.NamaPasangan);
            if (ahliWaris.Anak != null)
            {
                foreach (var anak in ahliWaris.Anak)
                {
                    if (anak != null && !string.IsNullOrWhiteSpace(anak.Nama))
                        penandatangan.Add(anak.Nama);
                }
            }

            foreach (var nama in penandatangan)
            {
                halaman.Paragraf($"  {nama}  ...............................", rata: Rata.Tengah, jarakAtas: 15);
            }
        }

        private void ComposeKuasa(BadanSurat halaman, SuratData suratData, string? keteranganTextBox)
        {
            var almarhum = suratData.Warga ?? new WargaData();
            var ahliWaris = suratData.AhliWarisData ?? new AhliWarisData();
            var anakPenerimaKuasa = ahliWaris.Anak[0]; // Ambil anak pertama sebagai penerima kuasa

            var almarhumFullName = GetAlmarhumFullName(almarhum.Nama, almarhum.JenisKelamin);
            string namaDesa = suratData.Desa?.NamaDesa ?? "Desa";

            // Judul halaman
            halaman.Blok(c => SuratRenderer.JudulTengah(c, "SURAT KUASA AHLI WARIS", UkuranJudulHalaman));

            // Pengantar
            halaman.Paragraf($"Yang bertanda tangan/Cap Jempol di bawah ini, kami para Ahli Waris dari {almarhumFullName}:",
                rata: Rata.Justify,
                jarakAtas: 15);

            // Data Ahli Waris
            if (!string.IsNullOrWhiteSpace(ahliWaris.NamaPasangan))
            {
                TambahTabelFormulir(halaman, CreateDataPasangan(ahliWaris, "1. Nama"));
            }
            if (ahliWaris.JumlahAnak > 0 && ahliWaris.Anak != null)
            {
                TambahTabelFormulir(halaman, CreateDataAnak(ahliWaris, 2));
            }

            // Penerima Kuasa (menggunakan data anak pertama)
            halaman.Paragraf("Dengan ini kami sepakat memberikan KUASA PENUH kepada:", jarakAtas: 10);
            TambahTabelFormulir(halaman, CreateDataPenerimaKuasa(anakPenerimaKuasa));

            // Tujuan Kuasa
            halaman.Paragraf("Untuk bertindak atas nama kami dalam hal:", jarakAtas: 10);
            halaman.Paragraf($"  - {keteranganTextBox ?? "Pengurusan Klaim Asuransi"} atas nama {almarhumFullName}.", jarakAtas: 5);
            halaman.Paragraf("Demikian Surat Kuasa ini kami buat dengan sebenarnya, dalam keadaan sehat jasmani dan rohani, tanpa adanya unsur paksaan atau bujukan dari pihak manapun.",
                rata: Rata.Justify,
                jarakAtas: 5);

            // Tanda tangan pemberi & penerima kuasa
            var pemberiKuasa = new List<string>();
            if (!string.IsNullOrWhiteSpace(ahliWaris.NamaPasangan))
            {
                pemberiKuasa.Add(ahliWaris.NamaPasangan);
            }
            if (ahliWaris.Anak != null)
            {
                foreach (var anak in ahliWaris.Anak)
                {
                    if (anak != null && !string.IsNullOrWhiteSpace(anak.Nama))
                    {
                        pemberiKuasa.Add(anak.Nama);
                    }
                }
            }

            string namaPenerima = anakPenerimaKuasa?.Nama ?? "-";
            halaman.Blok(container => container.PaddingTop(30).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(1);
                    cols.RelativeColumn(1);
                });

                table.Cell().Element(sel => sel.Column(kolom =>
                {
                    kolom.Item().Element(c => SuratRenderer.Teks(c, "Yang Menerima Kuasa", tebal: true, rata: Rata.Tengah));
                    kolom.Item().Height(SuratRenderer.RuangTandaTangan);
                    kolom.Item().Element(c => SuratRenderer.Teks(c, namaPenerima, tebal: true, rata: Rata.Tengah));
                }));

                table.Cell().Element(sel => sel.Column(kolom =>
                {
                    kolom.Item().Element(c => SuratRenderer.Teks(c, "Yang Memberi Kuasa", tebal: true, rata: Rata.Tengah));
                    foreach (var nama in pemberiKuasa)
                    {
                        kolom.Item().PaddingTop(15).Element(c => SuratRenderer.Teks(c, $"{nama}   ...............................", rata: Rata.Tengah));
                    }
                }));
            }));

            // Tanda tangan Kepala Desa dan Camat
            string namaKades = string.IsNullOrWhiteSpace(suratData.Desa?.KepalaDesa)
                ? "A. SOPANDI"
                : NamaFormatter.ToUpperNama(suratData.Desa.KepalaDesa);
            string kecamatan = string.IsNullOrWhiteSpace(suratData.Desa?.Kecamatan) ? "____________________" : suratData.Desa.Kecamatan;
            string namaCamat = string.IsNullOrWhiteSpace(suratData.Desa?.NamaCamat) ? "____________________" : suratData.Desa.NamaCamat;
            string golCamat = string.IsNullOrWhiteSpace(suratData.Desa?.GolCamat) ? " " : suratData.Desa.GolCamat;
            string nipCamat = string.IsNullOrWhiteSpace(suratData.Desa?.NipCamat) ? " " : "NIP. " + suratData.Desa.NipCamat;

            halaman.Blok(container => container.PaddingTop(20).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(1);
                    cols.RelativeColumn(1);
                });

                table.Cell().Element(sel => sel.Column(kolom =>
                {
                    kolom.Item().Element(c => SuratRenderer.Teks(c, "Mengetahui;", rata: Rata.Tengah));
                    kolom.Item().Element(c => SuratRenderer.Teks(c, $"Kepala Desa {namaDesa}", rata: Rata.Tengah));
                    kolom.Item().Height(SuratRenderer.RuangTandaTangan);
                    kolom.Item().Element(c => SuratRenderer.Teks(c, namaKades, tebal: true, rata: Rata.Tengah));
                }));

                table.Cell().Element(sel => sel.Column(kolom =>
                {
                    kolom.Item().Element(c => SuratRenderer.Teks(c, "Menguatkan;", rata: Rata.Tengah));
                    kolom.Item().Element(c => SuratRenderer.Teks(c, "Nomor : ...................................", rata: Rata.Tengah));
                    kolom.Item().Element(c => SuratRenderer.Teks(c, $"Camat {kecamatan}", rata: Rata.Tengah));
                    kolom.Item().Height(SuratRenderer.RuangTandaTangan);
                    kolom.Item().Element(c => SuratRenderer.Teks(c, namaCamat, tebal: true, rata: Rata.Tengah));
                    kolom.Item().Element(c => SuratRenderer.Teks(c, golCamat, ukuran: UkuranTeks - 1, rata: Rata.Tengah));
                    kolom.Item().Element(c => SuratRenderer.Teks(c, nipCamat, ukuran: UkuranTeks - 1, rata: Rata.Tengah));
                }));
            }));
        }

        /// <summary>Kaki halaman 1: Camat di kiri, Kepala Desa di kanan.</summary>
        private void ComposeKakiHalaman1(BadanSurat halaman, SuratData suratData)
        {
            var desa = suratData.Desa ?? new DesaData();
            string defaultText = "____________________";

            string kecamatan = string.IsNullOrWhiteSpace(desa.Kecamatan) ? defaultText : desa.Kecamatan;
            string namaCamat = string.IsNullOrWhiteSpace(desa.NamaCamat) ? defaultText : desa.NamaCamat;
            string golCamat = string.IsNullOrWhiteSpace(desa.GolCamat) ? " " : desa.GolCamat;
            string nipCamat = string.IsNullOrWhiteSpace(desa.NipCamat) ? " " : "NIP. " + desa.NipCamat;
            string namaDesa = string.IsNullOrWhiteSpace(desa.NamaDesa) ? defaultText : desa.NamaDesa;
            string kepalaDesa = string.IsNullOrWhiteSpace(desa.KepalaDesa) ? defaultText : NamaFormatter.ToUpperNama(desa.KepalaDesa);

            halaman.Blok(container => container.PaddingTop(20).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(1);
                    cols.RelativeColumn(1);
                });

                table.Cell().Element(sel => sel.Column(kolom =>
                {
                    kolom.Item().Element(c => SuratRenderer.Teks(c, "Menguatkan;", rata: Rata.Tengah));
                    kolom.Item().Element(c => SuratRenderer.Teks(c, "Nomor : ...................................", rata: Rata.Tengah));
                    kolom.Item().Element(c => SuratRenderer.Teks(c, $"Camat {kecamatan}", rata: Rata.Tengah));
                    kolom.Item().Height(SuratRenderer.RuangTandaTangan);
                    kolom.Item().Element(c => SuratRenderer.Teks(c, namaCamat, tebal: true, rata: Rata.Tengah));
                    kolom.Item().Element(c => SuratRenderer.Teks(c, golCamat, ukuran: UkuranTeks - 1, rata: Rata.Tengah));
                    kolom.Item().Element(c => SuratRenderer.Teks(c, nipCamat, ukuran: UkuranTeks - 1, rata: Rata.Tengah));
                }));

                table.Cell().Element(sel => sel.Column(kolom =>
                {
                    kolom.Item().Element(c => SuratRenderer.Teks(c, $"{namaDesa}, {suratData.TanggalSurat:dd MMMM yyyy}", rata: Rata.Tengah));
                    kolom.Item().Element(c => SuratRenderer.Teks(c, $"Kepala Desa {namaDesa}", rata: Rata.Tengah));
                    kolom.Item().Height(SuratRenderer.RuangTandaTangan);
                    kolom.Item().Element(c => SuratRenderer.Teks(c, kepalaDesa, tebal: true, rata: Rata.Tengah));
                }));
            }));
        }

        private static List<(string Label, string? Value)> CreateDataPasangan(AhliWarisData ahliWaris, string labelNama = "Nama")
        {
            return new List<(string, string?)>
            {
                (labelNama, ahliWaris.NamaPasangan),
                ("Tempat/Tgl. Lahir", $"{ahliWaris.TempatLahirPasangan ?? "-"}, {FormatTanggal(ahliWaris.TanggalLahirPasangan)}"),
                ("Jenis Kelamin", ahliWaris.JenisKelaminPasangan ?? "-"),
                ("Alamat", FormatAlamatForDisplay(ahliWaris.AlamatPasangan))
            };
        }

        private static List<(string Label, string? Value)> CreateDataAnak(AhliWarisData ahliWaris, int startNumber)
        {
            var dataAnak = new List<(string, string?)>();
            if (ahliWaris.Anak == null) return dataAnak;

            for (int i = 0; i < ahliWaris.Anak.Count; i++)
            {
                var anak = ahliWaris.Anak[i];
                dataAnak.Add(($"{startNumber + i}. Nama", anak?.Nama ?? "-"));
                dataAnak.Add(("    Tempat/Tgl. Lahir", $"{anak?.TempatLahir ?? "-"}, {FormatTanggal(anak?.TanggalLahir)}"));
                dataAnak.Add(("    Jenis Kelamin", anak?.JenisKelamin ?? "-"));
                dataAnak.Add(("    Alamat", FormatAlamatForDisplay(anak?.Alamat)));
            }

            return dataAnak;
        }

        private static List<(string Label, string? Value)> CreateDataPenerimaKuasa(AnakAhliWarisData anakPenerimaKuasa)
        {
            return new List<(string, string?)>
            {
                ("Nama", anakPenerimaKuasa?.Nama ?? "-"),
                ("Tempat/Tgl. Lahir", $"{anakPenerimaKuasa?.TempatLahir ?? "-"}, {FormatTanggal(anakPenerimaKuasa?.TanggalLahir)}"),
                ("Jenis Kelamin", anakPenerimaKuasa?.JenisKelamin ?? "-"),
                ("Alamat", FormatAlamatForDisplay(anakPenerimaKuasa?.Alamat))
            };
        }

        /// <summary>
        /// Tabel data ahli waris: label 160 / titik dua 10 / nilai 340, menjorok 20pt.
        /// Baris yang labelnya berakhiran “Nama” dicetak tebal (termasuk “1. Nama”).
        /// </summary>
        private void TambahTabelFormulir(BadanSurat halaman, IEnumerable<(string Label, string? Value)> items)
        {
            var baris = new List<(string Label, string? Value)>(items);

            halaman.Blok(container => container.PaddingLeft(IndentTabel).PaddingTop(3).PaddingBottom(3).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(160);
                    cols.RelativeColumn(10);
                    cols.RelativeColumn(340);
                });

                foreach (var item in baris)
                {
                    string label = item.Label ?? string.Empty;
                    bool isNama = label.Trim().EndsWith("Nama", StringComparison.OrdinalIgnoreCase);

                    SelFormulir(table.Cell(), label, tebal: false);
                    SelFormulir(table.Cell(), ":", tebal: false);
                    SelFormulir(table.Cell(), item.Value ?? "-", tebal: isNama);
                }
            }));
        }

        private static void SelFormulir(IContainer cell, string teks, bool tebal)
        {
            cell = cell.PaddingTop(1).PaddingBottom(1);

            // Alamat bisa berisi beberapa baris (garis baru dari FormatAlamatForDisplay).
            // Baris terakhir WAJIB memakai Span, bukan Line: Line menambah pemutus baris
            // di belakangnya sehingga tinggi setiap sel jadi dua kali lipat.
            var barisTeks = (teks ?? string.Empty).Split('\n');

            cell.Text(text =>
            {
                for (int i = 0; i < barisTeks.Length; i++)
                {
                    bool terakhir = i == barisTeks.Length - 1;
                    var span = terakhir
                        ? text.Span(barisTeks[i])
                        : text.Line(barisTeks[i]);

                    span.FontSize(UkuranTeks).LineHeight(LeadingBarisTabel / UkuranTeks);

                    if (tebal)
                    {
                        span.Bold();
                    }
                }
            });
        }

        private string FormatTanggalTerbilang(string? tanggal)
        {
            if (string.IsNullOrWhiteSpace(tanggal))
            {
                return "-";
            }

            try
            {
                if (DateTime.TryParseExact(tanggal, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
                {
                    var culture = new CultureInfo("id-ID");
                    var day = t.Day;
                    var month = culture.DateTimeFormat.GetMonthName(t.Month);
                    var year = Terbilang(t.Year);

                    return $"{Terbilang(day)} bulan {month.ToLower()} tahun {year}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error formatting tanggal terbilang: {Tanggal}", tanggal);
            }

            return tanggal;
        }

        private static string FormatTanggal(DateTime? tanggal)
        {
            if (!tanggal.HasValue)
            {
                return "-";
            }
            return tanggal.Value.ToString("dd-MM-yyyy");
        }

        private static string Terbilang(int jumlah)
        {
            if (jumlah == 0) return "Nol";

            var satuan = new[] { "", "Satu", "Dua", "Tiga", "Empat", "Lima", "Enam", "Tujuh", "Delapan", "Sembilan" };
            var belasan = new[] { "Sepuluh", "Sebelas", "Dua Belas", "Tiga Belas", "Empat Belas", "Lima Belas", "Enam Belas", "Tujuh Belas", "Delapan Belas", "Sembilan Belas" };
            var puluhan = new[] { "", "", "Dua Puluh", "Tiga Puluh", "Empat Puluh", "Lima Puluh", "Enam Puluh", "Tujuh Puluh", "Delapan Puluh", "Sembilan Puluh" };

            if (jumlah < 10)
                return satuan[jumlah];
            if (jumlah < 20)
                return belasan[jumlah - 10];
            if (jumlah < 100)
                return $"{puluhan[jumlah / 10]} {(jumlah % 10 > 0 ? satuan[jumlah % 10] : "")}".Trim();
            if (jumlah < 200)
                return $"Seratus {(jumlah % 100 > 0 ? Terbilang(jumlah % 100) : "")}".Trim();
            if (jumlah < 1000)
                return $"{satuan[jumlah / 100]} Ratus {(jumlah % 100 > 0 ? Terbilang(jumlah % 100) : "")}".Trim();
            if (jumlah < 2000)
                return $"Seribu {(jumlah % 1000 > 0 ? Terbilang(jumlah % 1000) : "")}".Trim();
            if (jumlah < 10000)
                return $"{satuan[jumlah / 1000]} Ribu {(jumlah % 1000 > 0 ? Terbilang(jumlah % 1000) : "")}".Trim();

            return jumlah.ToString(); // Fallback untuk angka yang lebih besar
        }
    }
}
