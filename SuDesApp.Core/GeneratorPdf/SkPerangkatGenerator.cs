// SkPerangkatGenerator.cs
// Penyusun dokumen Surat Keputusan Kepala Desa (dan lampiran daftar nama)
// memakai API QuestPDF langsung. Kelas ini sengaja TIDAK mewarisi
// SuratGeneratorBase karena SK Bupati diterbitkan di luar aplikasi — hanya
// SK milik Kepala Desa yang disusun di sini. Font didaftarkan lewat
// SuratGeneratorBase.DaftarkanFont agar PDF pertama pada sebuah sesi tetap
// tercetak walau generator ini dipakai lebih dulu.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;

namespace SuDesApp.GeneratorPdf
{
    /// <summary>
    /// Menyusun PDF Surat Keputusan dari <see cref="SkPerangkatIsi"/>: blok
    /// TENTANG (judul template kelompok), Menimbang/Memperhatikan/Mengingat,
    /// diktum (termasuk bentuk lampiran untuk SK banyak orang), tembusan, dan
    /// halaman lampiran daftar nama yang dikelompokkan per unit (mis. Posyandu).
    /// Nilai isian yang kosong dicetak garis titik-titik sehingga contoh SK
    /// tetap terbaca seperti formulir.
    /// </summary>
    public class SkPerangkatGenerator
    {
        private const float UkuranTeks = 12f;
        private const float UkuranJudul = 14f;

        private static readonly string[] NamaBulan =
        {
            "Januari", "Februari", "Maret", "April", "Mei", "Juni",
            "Juli", "Agustus", "September", "Oktober", "November", "Desember"
        };

        private readonly AppConfig _config;

        public SkPerangkatGenerator(AppConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));

            // Sekali per proses; berlaku juga untuk generator surat lain karena
            // status pendaftaran font dipusatkan di SuratGeneratorBase.
            SuratGeneratorBase.DaftarkanFont();
        }

        // =====================================================================
        // API publik
        // =====================================================================

        /// <summary>Tulis PDF SK lengkap ke aliran keluaran.</summary>
        public void GenerateSkPdf(Stream outputStream, DesaData desa, SkPerangkatIsi isi)
        {
            BangunDokumen(desa, isi).GeneratePdf(outputStream);
        }

        /// <summary>
        /// Susun dokumen QuestPDF tanpa menulis — dipakai pemanggil yang butuh
        /// bentuk lain (PDF, pratinjau, atau SVG per halaman untuk pengujian).
        /// </summary>
        public Document BangunDokumen(DesaData desa, SkPerangkatIsi isi)
        {
            if (isi == null) throw new ArgumentNullException(nameof(isi));

            var blok = SkPerangkatLampiran.Kelompokkan(isi.Lampiran);
            bool berlampiran = isi.AdaLampiran;

            return Document.Create(container =>
            {
                container.Page(halaman =>
                {
                    halaman.Size(PageSizes.A4);
                    halaman.MarginTop(45);
                    halaman.MarginRight(45);
                    halaman.MarginBottom(45);
                    halaman.MarginLeft(45);

                    halaman.Content().Column(kolom =>
                    {
                        SusunBadan(kolom, desa, isi, berlampiran);
                    });
                });

                if (berlampiran)
                {
                    container.Page(halaman =>
                    {
                        halaman.Size(PageSizes.A4);
                        halaman.MarginTop(45);
                        halaman.MarginRight(35);
                        halaman.MarginBottom(45);
                        halaman.MarginLeft(35);

                        halaman.Content().Column(kolom => SusunLampiran(kolom, desa, isi, blok));
                    });
                }
            });
        }

        // =====================================================================
        // Helper teks (dipakai langsung oleh uji unit)
        // =====================================================================

        /// <summary>Isi satu tanggal siap cetak; kosong menjadi garis titik-titik.</summary>
        private static string TanggalIndo(DateTime? tanggal) => tanggal.HasValue
            ? $"{tanggal.Value.Day} {NamaBulan[tanggal.Value.Month - 1]} {tanggal.Value.Year}"
            : "....................";

        /// <summary>Nilai isian; kosong menjadi garis titik-titik (gaya formulir).</summary>
        private static string IsiAtauGaris(string? nilai) =>
            string.IsNullOrWhiteSpace(nilai) ? "...................." : nilai.Trim();

        /// <summary>Penanda diktum pertama untuk isian ini (PERTAMA untuk Linmas, selainnya KESATU).</summary>
        private static string Penanda(SkPerangkatIsi isi) =>
            string.IsNullOrWhiteSpace(isi.PenandaDiktum)
                ? PenandaTemplate(isi.Kelompok)
                : isi.PenandaDiktum.Trim();

        private static string PenandaTemplate(string kelompok) =>
            SkPerangkatKatalog.Cari(kelompok)?.PenandaDiktum
            ?? SkPerangkatKatalog.PenandaDiktumBawaan;

        /// <summary>
        /// Kalimat keputusan yang dipakai badan SK: versi lampiran (menyebut
        /// Lampiran, bukan satu nama) atau kalimat kelompok/generik.
        /// </summary>
        internal static string KalimatKeputusan(SkPerangkatIsi isi)
        {
            if (isi.AdaLampiran)
            {
                return SkPerangkatKatalog.KetentuanLampiran(isi.Jenis);
            }

            var template = SkPerangkatKatalog.Cari(isi.Kelompok);
            return isi.Jenis switch
            {
                SkJenisPerangkat.Pemberhentian =>
                    template?.KetentuanPemberhentian ?? SkPerangkatKatalog.KetentuanPemberhentianUmum,
                SkJenisPerangkat.Penetapan =>
                    template?.KetentuanPenetapan ?? SkPerangkatKatalog.KetentuanPenetapanUmum,
                _ => template?.KetentuanPengangkatan ?? SkPerangkatKatalog.KetentuanPengangkatanUmum
            };
        }

        /// <summary>
        /// Butir Menimbang: SK pemberhentian berlampiran memakai butir yang
        /// menyebut Lampiran (menyebut "saudara/i {NAMA}" untuk sepuluh orang
        /// tidak masuk akal); kelompok lain memakai butirnya sendiri.
        /// </summary>
        internal static IReadOnlyList<string> ButirMenimbang(SkPerangkatIsi isi)
        {
            if (isi.Jenis == SkJenisPerangkat.Pemberhentian && isi.AdaLampiran)
            {
                return SkPerangkatKatalog.MenimbangPemberhentianLampiran;
            }

            var milikKelompok = SkPerangkatKatalog.Cari(isi.Kelompok)?.Menimbang;
            if (milikKelompok is { Count: > 0 })
            {
                return milikKelompok;
            }

            return isi.Jenis == SkJenisPerangkat.Pemberhentian
                ? SkPerangkatKatalog.MenimbangPemberhentian
                : Array.Empty<string>();
        }

        /// <summary>
        /// Ganti seluruh placeholder yang dikenali generator dengan isian.
        /// Pada mode lampiran, frasa "saudara/i {NAMA}" (dan {NAMA} apa pun)
        /// menjadi rujukan ke Lampiran — SK banyak orang tidak menyebut satu nama.
        /// </summary>
        internal static string IsiPlaceholder(string teks, SkPerangkatIsi isi, DesaData? desa)
        {
            if (string.IsNullOrWhiteSpace(teks)) return string.Empty;

            const string rujukanLampiran = "nama-nama sebagaimana tercantum dalam Lampiran";
            bool lampiran = isi.AdaLampiran;

            string hasil = teks;

            if (lampiran)
            {
                hasil = hasil
                    .Replace("saudara/i {NAMA}", "{NAMA_LAMPIRAN}")
                    .Replace("saudara/i{NAMA}", "{NAMA_LAMPIRAN}")
                    .Replace("{NAMA}", "{NAMA_LAMPIRAN}")
                    .Replace("saudara/i", string.Empty);
            }

            hasil = hasil
                .Replace("{JABATAN}", IsiAtauGaris(isi.Jabatan))
                .Replace(lampiran ? "{NAMA_LAMPIRAN}" : "{NAMA}",
                    lampiran ? rujukanLampiran : IsiAtauGaris(isi.Nama))
                .Replace("{NIK}", isi.NIK?.Trim() ?? string.Empty)
                .Replace("{DESA}", IsiAtauGaris(desa?.NamaDesa))
                .Replace("{WILAYAH}", isi.Wilayah?.Trim() ?? string.Empty)
                .Replace("{NOMOR}", isi.Nomor?.Trim() ?? string.Empty)
                .Replace("{MULAI}", TanggalIndo(isi.Mulai))
                .Replace("{SELESAI}", TanggalIndo(isi.Selesai))
                .Replace("{TANGGAL}", TanggalIndo(isi.Tanggal))
                .Replace("{ALASAN}", isi.Alasan?.Trim() ?? string.Empty)
                .Replace("{PENANDA}", Penanda(isi));

            // WILAYAH/ALASAN yang kosong menyisakan spasi ganda — rapikan.
            return string.Join(" ",
                hasil.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        /// <summary>
        /// Judul dokumen (blok TENTANG) dari template kelompok; huruf kapital
        /// sesuai bentuk SK resmi.
        /// </summary>
        internal static string JudulTampil(
            SkKelompokTemplate template, bool pengangkatan, SkPerangkatIsi isi, DesaData? desa)
        {
            string pola = pengangkatan
                ? (string.IsNullOrWhiteSpace(template.JudulPengangkatan)
                    ? SkPerangkatKatalog.JudulPengangkatanUmum
                    : template.JudulPengangkatan)
                : (string.IsNullOrWhiteSpace(template.JudulPemberhentian)
                    ? SkPerangkatKatalog.JudulPemberhentianUmum
                    : template.JudulPemberhentian);

            return IsiPlaceholder(pola, isi, desa).ToUpperInvariant();
        }

        /// <summary>Judul dokumen untuk jenis SK apa pun (termasuk Penetapan).</summary>
        private static string JudulDokumen(SkPerangkatIsi isi, DesaData? desa)
        {
            var template = SkPerangkatKatalog.Cari(isi.Kelompok);
            if (template == null)
            {
                var isiGenerik = isi.Jenis == SkJenisPerangkat.Pemberhentian
                    ? SkPerangkatKatalog.JudulPemberhentianUmum
                    : SkPerangkatKatalog.JudulPengangkatanUmum;
                return IsiPlaceholder(isiGenerik, isi, desa).ToUpperInvariant();
            }

            return isi.Jenis switch
            {
                SkJenisPerangkat.Pemberhentian =>
                    IsiPlaceholder(template.JudulPemberhentian, isi, desa).ToUpperInvariant(),
                SkJenisPerangkat.Penetapan when template.BisaPenetapan =>
                    IsiPlaceholder(template.JudulPenetapan, isi, desa).ToUpperInvariant(),
                _ => JudulTampil(template, isi.Jenis != SkJenisPerangkat.Pemberhentian, isi, desa)
            };
        }

        /// <summary>
        /// Kolom tabel lampiran yang benar-benar tercetak: NO dan NAMA selalu
        /// ada; kolom data warga hanya bila ada baris yang mengisinya; JABATAN
        /// hanya bila peran antar baris benar-benar berbeda.
        /// </summary>
        internal static IReadOnlyList<string> JudulKolomLampiran(IEnumerable<BarisLampiranSk>? baris)
        {
            var daftar = (baris ?? Array.Empty<BarisLampiranSk>())
                .Where(b => b != null && !string.IsNullOrWhiteSpace(b.Nama))
                .ToList();

            var kolom = new List<string> { "NO", "NAMA" };

            int peranBeda = daftar
                .Select(b => (b.Peran ?? string.Empty).Trim())
                .Where(p => p.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            if (peranBeda >= 2) kolom.Add("JABATAN");

            void TambahBilaAda(Func<BarisLampiranSk, string> ambil, string judul)
            {
                if (daftar.Any(b => !string.IsNullOrWhiteSpace(ambil(b))))
                {
                    kolom.Add(judul);
                }
            }

            TambahBilaAda(b => b.TempatTanggalLahir, "TEMPAT & TGL. LAHIR");
            TambahBilaAda(b => b.Pendidikan, "PENDIDIKAN");
            TambahBilaAda(b => b.Pekerjaan, "PEKERJAAN");
            TambahBilaAda(b => b.Alamat, "ALAMAT");
            TambahBilaAda(b => b.Agama, "AGAMA");
            TambahBilaAda(b => b.GolonganDarah, "GOL. DARAH");
            TambahBilaAda(b => b.StatusPerkawinan, "STATUS KAWIN");

            return kolom;
        }

        // =====================================================================
        // Susunan badan SK
        // =====================================================================

        private static void SusunBadan(ColumnDescriptor kolom, DesaData? desa, SkPerangkatIsi isi, bool berlampiran)
        {
            string judul = JudulDokumen(isi, desa);

            // TENTANG: judul keputusan dari template kelompok + nomor SK.
            kolom.Item().AlignCenter().Text(judul)
                .FontSize(UkuranJudul).Bold().Underline();
            kolom.Item().AlignCenter().PaddingTop(2).PaddingBottom(10)
                .Text($"NOMOR : {IsiAtauGaris(isi.Nomor)}")
                .FontSize(UkuranTeks);

            // Pembuka.
            Teks(kolom,
                $"Kepala Desa {IsiAtauGaris(desa?.NamaDesa)}, Kecamatan {IsiAtauGaris(desa?.Kecamatan)}, " +
                $"Kabupaten {IsiAtauGaris(desa?.Kabupaten)}, dengan ini menetapkan Surat Keputusan tentang " +
                $"{judul} sebagai berikut:");
            kolom.Item().PaddingBottom(4);

            // Menimbang (huruf a, b, c).
            var menimbang = ButirMenimbang(isi);
            if (menimbang.Count > 0)
            {
                ButirBerlabel(kolom, "Menimbang :", menimbang.Select((b, i) =>
                    ($"{(char)('a' + i)}.", IsiPlaceholder(b, isi, desa))));
            }

            // Memperhatikan (bila ada butirnya).
            var memperhatikan = isi.Memperhatikan is { Count: > 0 }
                ? isi.Memperhatikan
                : ButirMemperhatikan(isi);
            if (memperhatikan.Count > 0)
            {
                ButirBerlabel(kolom, "Memperhatikan :", memperhatikan.Select((b, i) =>
                    ($"{i + 1}.", IsiPlaceholder(b, isi, desa))));
            }

            // Mengingat (dasar hukum, bernomor).
            var mengingat = SkPerangkatKatalog.Cari(isi.Kelompok)?.Mengingat
                ?? Array.Empty<string>();
            if (mengingat.Count > 0)
            {
                ButirBerlabel(kolom, "Mengingat :", mengingat.Select((b, i) =>
                    ($"{i + 1}.", IsiPlaceholder(b, isi, desa))));
            }

            kolom.Item().PaddingTop(8).AlignCenter().Text("MEMUTUSKAN")
                .FontSize(UkuranTeks).Bold().Underline();
            kolom.Item().AlignCenter().Text($"Surat Keputusan Kepala Desa {IsiAtauGaris(desa?.NamaDesa)}")
                .FontSize(UkuranTeks);
            kolom.Item().AlignCenter().PaddingBottom(6)
                .Text($"Nomor : {IsiAtauGaris(isi.Nomor)}")
                .FontSize(UkuranTeks);

            // Diktum pertama: kalimat keputusan (versi lampiran bila SK banyak orang).
            kolom.Item().Row(baris =>
            {
                baris.ConstantItem(95).Text("MENETAPKAN :").FontSize(UkuranTeks).Bold();
                baris.RelativeItem().Text($"{Penanda(isi)}   " +
                        IsiPlaceholder(KalimatKeputusan(isi), isi, desa))
                    .FontSize(UkuranTeks);
            });

            // Diktum kedua: berlaku sejak ditetapkan.
            kolom.Item().Row(baris =>
            {
                baris.ConstantItem(95).Text("KEDUA :").FontSize(UkuranTeks).Bold();
                baris.RelativeItem().Text(
                    "Supaya Surat Keputusan ini mulai berlaku pada tanggal ditetapkan; " +
                    "apabila di kemudian hari terdapat kekeliruan, akan diperbaiki sebagaimana mestinya.")
                    .FontSize(UkuranTeks);
            });

            // Diktum ketiga: kewajiban pelaksanaan.
            kolom.Item().Row(baris =>
            {
                baris.ConstantItem(95).Text("KETIGA :").FontSize(UkuranTeks).Bold();
                baris.RelativeItem()
                    .Text(IsiPlaceholder(SkPerangkatKatalog.KetentuanKetigaUmum, isi, desa))
                    .FontSize(UkuranTeks);
            });

            kolom.Item().PaddingTop(14);

            // Tanda tangan di kanan; tembusan di kiri bawah.
            kolom.Item().Table(tabel =>
            {
                tabel.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(1);
                    c.RelativeColumn(1);
                });

                tabel.Cell().Column(kiri =>
                {
                    var tembusan = isi.Tembusan ?? SkPerangkatKatalog.TembusanBawaan;
                    if (tembusan.Count == 0) return;

                    kiri.Item().PaddingTop(10).Text("Tembusan :").FontSize(UkuranTeks).Bold();
                    foreach (var (butir, i) in tembusan.Select((b, i) => (b, i)))
                    {
                        kiri.Item().Text($"{i + 1}. {IsiPlaceholder(butir, isi, desa)}")
                            .FontSize(UkuranTeks);
                    }
                });

                tabel.Cell().Column(kanan =>
                {
                    var tanggal = TanggalIndo(isi.Tanggal ?? DateTime.Today);
                    kanan.Item().Text($"{IsiAtauGaris(desa?.NamaDesa)}, {tanggal}")
                        .FontSize(UkuranTeks);
                    kanan.Item().Text($"Kepala Desa {IsiAtauGaris(desa?.NamaDesa)}")
                        .FontSize(UkuranTeks);
                    kanan.Item().Height(55);
                    kanan.Item().Text(isi.Contoh ? "(............................................)" : string.Empty)
                        .FontSize(UkuranTeks).Bold();
                    if (!isi.Contoh)
                    {
                        kanan.Item().Text(IsiAtauGaris(desa?.KepalaDesa)).FontSize(UkuranTeks).Bold();
                    }
                });
            });

            if (berlampiran)
            {
                kolom.Item().PaddingTop(8).AlignRight().Text("1 (satu) lembar Lampiran")
                    .FontSize(UkuranTeks);
            }
        }

        /// <summary>Butir Memperhatikan bawaan kelompok, lalu bawaan umum katalog.</summary>
        private static IReadOnlyList<string> ButirMemperhatikan(SkPerangkatIsi isi)
        {
            var milikKelompok = SkPerangkatKatalog.Cari(isi.Kelompok)?.Memperhatikan;
            return milikKelompok is { Count: > 0 } ? milikKelompok : SkPerangkatKatalog.MemperhatikanUmum;
        }

        private static void ButirBerlabel(
            ColumnDescriptor kolom, string label, IEnumerable<(string Nomor, string Teks)> butir)
        {
            kolom.Item().Text(label).FontSize(UkuranTeks).Bold();
            foreach (var (nomor, teks) in butir)
            {
                kolom.Item().Row(baris =>
                {
                    baris.ConstantItem(28).Text(nomor).FontSize(UkuranTeks);
                    baris.RelativeItem().Text(teks).FontSize(UkuranTeks);
                });
            }

            kolom.Item().PaddingBottom(4);
        }

        // =====================================================================
        // Halaman lampiran daftar nama
        // =====================================================================

        private static void SusunLampiran(
            ColumnDescriptor kolom, DesaData? desa, SkPerangkatIsi isi,
            IReadOnlyList<SkLampiranBlok> blok)
        {
            string judul = JudulDokumen(isi, desa);

            kolom.Item().AlignCenter().Text($"Lampiran Keputusan Kepala Desa {IsiAtauGaris(desa?.NamaDesa)}")
                .FontSize(UkuranTeks).Bold();
            kolom.Item().AlignCenter().Text($"Nomor : {IsiAtauGaris(isi.Nomor)}")
                .FontSize(UkuranTeks);
            kolom.Item().PaddingTop(4).PaddingBottom(8).AlignCenter()
                .Text($"DAFTAR {judul}")
                .FontSize(UkuranTeks).Bold().Underline();

            // Lampiran tanpa judul unit dicetak satu tabel; bila perlu judul
            // (banyak unit / unit bernama), setiap blok diberi kepala "UNIT …".
            bool perluJudul = SkPerangkatLampiran.PerluJudulUnit(blok);
            var kolomJudul = JudulKolomLampiran(isi.Lampiran);

            foreach (var satu in blok)
            {
                if (perluJudul && !string.IsNullOrWhiteSpace(satu.Unit))
                {
                    kolom.Item().PaddingTop(6).Text($"UNIT {satu.Unit.ToUpperInvariant()}")
                        .FontSize(UkuranTeks).Bold();
                }

                kolom.Item().Table(tabel =>
                {
                    tabel.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(28);
                        foreach (var _ in kolomJudul.Skip(1)) c.RelativeColumn();
                    });

                    // Baris judul kolom.
                    foreach (var (judulKolom, i) in kolomJudul.Select((j, i) => (j, i)))
                    {
                        tabel.Cell().BorderBottom(1).PaddingVertical(2)
                            .Text(judulKolom).FontSize(10f).Bold();
                    }

                    foreach (var baris in satu.Baris)
                    {
                        foreach (var (judulKolom, i) in kolomJudul.Select((j, i) => (j, i)))
                        {
                            tabel.Cell().BorderBottom(0.5f).PaddingVertical(2)
                                .Text(IsiSel(baris, judulKolom)).FontSize(10f);
                        }
                    }
                });
            }
        }

        /// <summary>Isi sel tabel lampiran sesuai judul kolomnya; kosong menjadi "-".</summary>
        private static string IsiSel(SkLampiranBaris baris, string judulKolom) => judulKolom switch
        {
            "NO" => baris.Nomor.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "NAMA" => IsiAtauGaris(baris.Orang.Nama),
            "JABATAN" => PeranAtauStrip(baris.Orang.Peran),
            "TEMPAT & TGL. LAHIR" => AtauStrip(baris.Orang.TempatTanggalLahir),
            "PENDIDIKAN" => AtauStrip(baris.Orang.Pendidikan),
            "PEKERJAAN" => AtauStrip(baris.Orang.Pekerjaan),
            "ALAMAT" => AtauStrip(baris.Orang.Alamat),
            "AGAMA" => AtauStrip(baris.Orang.Agama),
            "GOL. DARAH" => AtauStrip(baris.Orang.GolonganDarah),
            "STATUS KAWIN" => AtauStrip(baris.Orang.StatusPerkawinan),
            _ => "-"
        };

        private static string PeranAtauStrip(string? peran) =>
            string.IsNullOrWhiteSpace(peran) ? "-" : peran.Trim();

        private static string AtauStrip(string? nilai) =>
            string.IsNullOrWhiteSpace(nilai) ? "-" : nilai.Trim();

        /// <summary>Satu baris teks badan dengan gaya baku.</summary>
        private static void Teks(ColumnDescriptor kolom, string isi)
        {
            kolom.Item().Text(isi).FontSize(UkuranTeks);
        }
    }
}
