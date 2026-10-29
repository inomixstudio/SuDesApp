// SkPerangkatGenerator.cs
// Penyusun dokumen Surat Keputusan Kepala Desa (dan lampiran daftar nama)
// memakai API SudesPdf.Layout langsung. Kelas ini sengaja TIDAK mewarisi
// SuratGeneratorBase karena SK Bupati diterbitkan di luar aplikasi — hanya
// SK milik Kepala Desa yang disusun di sini. Tidak ada font yang perlu
// didaftarkan: mesin memakai metrik font bawaan PDF, jadi dokumen pertama
// pada sebuah sesi tercetak sama seperti dokumen berikutnya.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SudesPdf.Core.Layout;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;

namespace SuDesApp.GeneratorPdf
{
    /// <summary>
    /// Menyusun PDF Surat Keputusan dari <see cref="SkPerangkatIsi"/>: kepala
    /// naskah (Gambar Garuda Pancasila + blok KEPALA DESA/KECAMATAN/KABUPATEN +
    /// judul keputusan, sesuai contoh SK desa dan format naskah dinas Perbup
    /// 74/2020), Menimbang/Memperhatikan/Mengingat, diktum (termasuk bentuk
    /// lampiran untuk SK banyak orang), blok Ditetapkan di + tanda tangan +
    /// tembusan, dan halaman lampiran daftar nama yang dikelompokkan per unit
    /// (mis. Posyandu). Nilai isian yang kosong dicetak garis titik-titik
    /// sehingga contoh SK tetap terbaca seperti formulir.
    /// </summary>
    public class SkPerangkatGenerator
    {
        // Ukuran badan 11pt dengan leading 1,05 — tetap nyaman dibaca; ruang
        // tanda tangan dan tembusan tidak disumpal agar muat satu halaman.
        // SK berdiktum panjang (Mengingat banyak butir) wajar menjadi dua
        // halaman asalkan blok diktum/tanda tangan tidak terbelah (ShowEntire).
        private const float UkuranTeks = 11f;
        private const float UkuranJudul = 13f;

        private static readonly string[] NamaBulan =
        {
            "Januari", "Februari", "Maret", "April", "Mei", "Juni",
            "Juli", "Agustus", "September", "Oktober", "November", "Desember"
        };

        private readonly AppConfig _config;

        public SkPerangkatGenerator(AppConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
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
        /// Susun dokumen tanpa menulis — dipakai pemanggil yang butuh bentuk lain
        /// (berkas PDF, pratinjau halaman, atau uji isi dokumen).
        /// </summary>
        public IDocument BangunDokumen(DesaData desa, SkPerangkatIsi isi)
        {
            if (isi == null) throw new ArgumentNullException(nameof(isi));

            var blok = SkPerangkatLampiran.Kelompokkan(isi.Lampiran);
            bool berlampiran = isi.AdaLampiran;

            return Document.Create(container =>
            {
                container.Page(halaman =>
                {
                    // Ukuran kertas mengikuti Pengaturan Cetak (A4 bawaan atau
                    // F4) — sama dengan dokumen lain, jadi SK dan cetakan desa
                    // lain sebangun saat dilipat/diarsipkan.
                    var (lebar, tinggi) = PengaturanCetak.Dimensi();
                    halaman.Size(new PageSize(lebar, tinggi, Unit.Point));
                    halaman.MarginTop(36);
                    halaman.MarginRight(36);
                    halaman.MarginBottom(36);
                    halaman.MarginLeft(36);

                    // Naskah dinas memakai Times New Roman (sama dengan generator
                    // surat lain) dengan leading 1,05: rapat namun tetap lega.
                    // Blok diktum + tanda tangan dibungkus ShowEntire agar tidak
                    // terbelah antar halaman saat SK panjang meluber.
                    halaman.DefaultTextStyle(x => x
                        .FontFamily("Times New Roman")
                        .FontSize(UkuranTeks)
                        .LineHeight(1.05f));

                    halaman.Content().Column(kolom =>
                    {
                        SusunBadan(kolom, desa, isi, berlampiran);
                    });
                });

                if (berlampiran)
                {
                    container.Page(halaman =>
                    {
                    var (lebarL, tinggiL) = PengaturanCetak.Dimensi();
                    halaman.Size(new PageSize(lebarL, tinggiL, Unit.Point));
                    halaman.MarginTop(36);
                    halaman.MarginRight(35);
                    halaman.MarginBottom(36);
                    halaman.MarginLeft(35);

                    halaman.DefaultTextStyle(x => x
                        .FontFamily("Times New Roman")
                        .FontSize(UkuranTeks)
                        .LineHeight(1.05f));

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
                ? PenandaTemplate(isi)
                : isi.PenandaDiktum.Trim();

        private static string PenandaTemplate(SkPerangkatIsi isi) =>
            SkPerangkatKatalog.Untuk(isi.Jabatan, isi.Kelompok)?.PenandaDiktum
            ?? SkPerangkatKatalog.PenandaDiktumBawaan;

        /// <summary>
        /// Kalimat keputusan yang dipakai badan SK: versi lampiran (menyebut
        /// Lampiran, bukan satu nama) atau kalimat kelompok/generik.
        /// </summary>
        internal static string KalimatKeputusan(SkPerangkatIsi isi)
        {
            // Template per jabatan didahulukan: SK Sekretaris Desa, Kaur, Kasi,
            // dan Kadus memakai pertimbangan sesuai tugas pokok jabatannya.
            var template = SkPerangkatKatalog.Untuk(isi.Jabatan, isi.Kelompok);

            if (isi.AdaLampiran)
            {
                // SK bersama memakai butir template bila ada (mis. LINMAS dan
                // POSYANDU meniru contoh SK desa), selain itu butir baku katalog.
                if (isi.Jenis == SkJenisPerangkat.Pengangkatan
                    && !string.IsNullOrWhiteSpace(template?.KetentuanPengangkatanLampiran))
                {
                    return template.KetentuanPengangkatanLampiran!;
                }

                if (isi.Jenis == SkJenisPerangkat.Penetapan
                    && !string.IsNullOrWhiteSpace(template?.KetentuanPenetapanLampiran))
                {
                    return template.KetentuanPenetapanLampiran!;
                }

                return SkPerangkatKatalog.KetentuanLampiran(isi.Jenis);
            }

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

            var template = SkPerangkatKatalog.Untuk(isi.Jabatan, isi.Kelompok);

            // Pemberhentian punya pertimbangannya sendiri (mis. konsultasi
            // kepada Camat); template per jabatan merincinya terpisah.
            if (isi.Jenis == SkJenisPerangkat.Pemberhentian
                && template?.MenimbangPemberhentian is { Count: > 0 } khusus)
            {
                return khusus;
            }

            if (template?.Menimbang is { Count: > 0 } milik)
            {
                return milik;
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
                .Replace("{KECAMATAN}", IsiAtauGaris(desa?.Kecamatan))
                .Replace("{KABUPATEN}", IsiAtauGaris(desa?.Kabupaten))
                .Replace("{WILAYAH}", isi.Wilayah?.Trim() ?? string.Empty)
                .Replace("{NOMOR}", isi.Nomor?.Trim() ?? string.Empty)
                .Replace("{MULAI}", TanggalIndo(isi.Mulai))
                .Replace("{SELESAI}", TanggalIndo(isi.Selesai))
                .Replace("{TANGGAL}", TanggalIndo(isi.Tanggal))
                .Replace("{ALASAN}", isi.Alasan?.Trim() ?? string.Empty)
                .Replace("{PENANDA}", Penanda(isi))
                .Replace("{KEPALA_DAERAH}", WilayahLabel.Kepala(desa?.JenisKabupaten))
                .Replace("{SK_KEPALA}", WilayahLabel.SkKepala(desa?.JenisKabupaten))
                .Replace("{PERDA}", WilayahLabel.PeraturanDaerah(desa?.JenisKabupaten, IsiAtauGaris(desa?.Kabupaten)))
                .Replace("{PERKEPALA_TANPA_NAMA}", WilayahLabel.PeraturanKepalaTanpaNama(desa?.JenisKabupaten))
                .Replace("{IDENTITAS_WILAYAH}", WilayahLabel.IdentitasWilayah(desa?.JenisKabupaten, IsiAtauGaris(desa?.Kabupaten)));

            // WILAYAH/ALASAN yang kosong menyisakan spasi ganda — rapikan; tanda
            // baca yang tadinya didahului placeholder kosong juga dirapatkan
            // (mis. "Desa Sukamaju , terhitung" pada SK bersama tanpa wilayah).
            return string.Join(" ",
                    hasil.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Replace(" ,", ",");
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
            var template = SkPerangkatKatalog.Untuk(isi.Jabatan, isi.Kelompok);
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
        /// hanya bila peran antar baris benar-benar berbeda; NIK dan NO.
        /// TELEPON/HP hanya bila diminta (<paramref name="nikDanTelepon"/> —
        /// contoh SK KPM memakai keduanya).
        /// </summary>
        internal static IReadOnlyList<string> JudulKolomLampiran(
            IEnumerable<BarisLampiranSk>? baris, bool nikDanTelepon = false)
        {
            var daftar = (baris ?? Array.Empty<BarisLampiranSk>())
                .Where(b => b != null && !string.IsNullOrWhiteSpace(b.Nama))
                .ToList();

            var kolom = new List<string> { "NO", "NAMA" };
            // Pada contoh SK KPM, NIK berdiri tepat setelah nama.
            if (nikDanTelepon) kolom.Add("NIK");

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
            if (nikDanTelepon) TambahBilaAda(b => b.NomorHP, "NO. TELEPON/HP");

            return kolom;
        }

        // =====================================================================
        // Susunan badan SK
        // =====================================================================

        /// <summary>
        /// Kepala naskah SK sesuai contoh SK desa (LINMAS &amp; Posyandu) sekaligus
        /// format naskah dinas Perbup 74/2020: Gambar Garuda Pancasila rata tengah
        /// di puncak halaman, blok KEPALA DESA/KECAMATAN/KABUPATEN, baris
        /// KEPUTUSAN KEPALA DESA + NOMOR, kata TENTANG beserta judul, lalu baris
        /// penetap. Contoh SK tidak memuat kalimat pembuka — baris penetap
        /// langsung diikuti Menimbang.
        /// </summary>
        private static void SusunKepalaNaskah(
            ColumnDescriptor kolom, DesaData? desa, SkPerangkatIsi isi, string judul)
        {
            // Garuda: ±2,5 × 2,7 cm seperti pada contoh (904875 × 983475 EMU
            // ≈ 71 × 77 pt); rasio gambar 219 × 238 dipertahankan oleh FitArea.
            byte[]? garuda = GambarBawaan.Garuda();
            if (garuda != null)
            {
                kolom.Item().AlignCenter().PaddingBottom(4).Width(71f).Height(77f)
                    .Element(blok => blok.Image(garuda).FitArea());
            }

            // Blok kepala naskah: tiga baris rata tengah di bawah garuda.
            foreach (string baris in new[]
            {
                $"KEPALA DESA {IsiAtauGaris(desa?.NamaDesa)}",
                $"KECAMATAN {IsiAtauGaris(desa?.Kecamatan)}",
                $"{KopSurat.LabelKabupaten(desa)} {IsiAtauGaris(KopSurat.NamaKabupaten(desa))}"
            })
            {
                kolom.Item().AlignCenter().Text(baris.ToUpperInvariant())
                    .FontSize(UkuranTeks).Bold();
            }

            kolom.Item().PaddingTop(8).AlignCenter()
                .Text($"KEPUTUSAN KEPALA DESA {IsiAtauGaris(desa?.NamaDesa)}".ToUpperInvariant())
                .FontSize(UkuranTeks).Bold();
            kolom.Item().AlignCenter().PaddingBottom(4)
                .Text($"NOMOR : {IsiAtauGaris(isi.Nomor)}")
                .FontSize(UkuranTeks);

            kolom.Item().AlignCenter().Text("TENTANG").FontSize(UkuranTeks).Bold();
            kolom.Item().AlignCenter().PaddingTop(2).PaddingBottom(6)
                .Text(judul).FontSize(UkuranJudul).Bold();

            // Baris penetap; pada contoh SK baris ini langsung diikuti Menimbang.
            kolom.Item().AlignCenter()
                .Text($"KEPALA DESA {IsiAtauGaris(desa?.NamaDesa)},".ToUpperInvariant())
                .FontSize(UkuranTeks);
            kolom.Item().PaddingBottom(4);
        }

        private static void SusunBadan(ColumnDescriptor kolom, DesaData? desa, SkPerangkatIsi isi, bool berlampiran)
        {
            string judul = JudulDokumen(isi, desa);

            // Kepala naskah (garuda + kop naskah + judul + baris penetap);
            // contoh SK tidak memuat kalimat pembuka sebelum Menimbang.
            SusunKepalaNaskah(kolom, desa, isi, judul);

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

            // Mengingat (dasar hukum, bernomor) — template jabatan memuat
            // Permendagri 84/2015 (SOTK) dan Perdes SOTK, bukan hanya
            // regulasi perangkat desa yang umum.
            var mengingat = SkPerangkatKatalog.Untuk(isi.Jabatan, isi.Kelompok)?.Mengingat
                ?? Array.Empty<string>();
            if (mengingat.Count > 0)
            {
                ButirBerlabel(kolom, "Mengingat :", mengingat.Select((b, i) =>
                    ($"{i + 1}.", IsiPlaceholder(b, isi, desa))));
            }

            // Blok penentuan + tanda tangan disatukan (ShowEntire): bila tidak
            // muat pada sisa halaman, SELURUH blok pindah utuh ke halaman
            // berikutnya — diktum KESATU/KEDUA/KETIGA dan blok tanda tangan
            // tidak lagi terbelah antar halaman seperti pada cetakan lama.
            kolom.Item().ShowEntire().Column(blok =>
            {
                blok.Item().PaddingTop(6).AlignCenter().Text("MEMUTUSKAN :")
                    .FontSize(UkuranTeks).Bold();

                // Menetapkan memakai judul keputusan — pada contoh SK baris ini diisi
                // judul (mis. "PENGANGKATAN KADER POSYANDU ..."), bukan nomor.
                blok.Item().Row(baris =>
                {
                    baris.ConstantItem(95).Text("MENETAPKAN :").FontSize(UkuranTeks).Bold();
                    baris.RelativeItem().Text(judul).FontSize(UkuranTeks).Bold();
                });

                // Diktum pertama: kalimat keputusan (versi lampiran bila SK banyak orang).
                blok.Item().Row(baris =>
                {
                    baris.ConstantItem(95).Text($"{Penanda(isi)} :").FontSize(UkuranTeks).Bold();
                    baris.RelativeItem()
                        .Text(IsiPlaceholder(KalimatKeputusan(isi), isi, desa))
                        .FontSize(UkuranTeks);
                });

                // Diktum kedua: kewajiban pelaksanaan — template jabatan memakai
                // kalimatnya sendiri (mis. kewajiban melaporkan hasil kerja kepada
                // Kepala Desa), sisanya kalimat baku katalog.
                string kalimatKetiga = !string.IsNullOrWhiteSpace(isi.KetentuanKetiga)
                    ? isi.KetentuanKetiga
                    : SkPerangkatKatalog.Untuk(isi.Jabatan, isi.Kelompok)?.KetentuanKetiga
                      ?? SkPerangkatKatalog.KetentuanKetigaUmum;
                blok.Item().Row(baris =>
                {
                    baris.ConstantItem(95).Text("KEDUA :").FontSize(UkuranTeks).Bold();
                    baris.RelativeItem()
                        .Text(IsiPlaceholder(kalimatKetiga, isi, desa))
                        .FontSize(UkuranTeks);
                });

                // Diktum ketiga: berlaku sejak ditetapkan — pada contoh SK ketentuan
                // ini selalu menjadi diktum terakhir.
                blok.Item().Row(baris =>
                {
                    baris.ConstantItem(95).Text("KETIGA :").FontSize(UkuranTeks).Bold();
                    baris.RelativeItem().Text(
                        "Supaya Surat Keputusan ini mulai berlaku pada tanggal ditetapkan; " +
                        "apabila di kemudian hari terdapat kekeliruan, akan diperbaiki sebagaimana mestinya.")
                        .FontSize(UkuranTeks);
                });

                blok.Item().PaddingTop(10);

                // Blok Ditetapkan di + tanda tangan di kolom kanan (kolom kiri
                // dibiarkan kosong); tembusan TIDAK lagi berdampingan — dicetak
                // lebih bawah, setelah seluruh blok tanda tangan Kepala Desa.
                blok.Item().Table(tabel =>
                {
                    tabel.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(1);
                        c.RelativeColumn(1);
                    });

                    tabel.Cell();

                    tabel.Cell().Column(kanan =>
                    {
                        var tanggal = TanggalIndo(isi.Tanggal ?? DateTime.Today);

                        // Ditetapkan di / Pada tanggal: label berjajar sebelum titik
                        // dua, seperti pada contoh SK (bukan "{desa}, {tanggal}").
                        kanan.Item().Row(baris =>
                        {
                            baris.ConstantItem(90).Text("Ditetapkan di").FontSize(UkuranTeks);
                            baris.RelativeItem().Text($": {IsiAtauGaris(desa?.NamaDesa)};").FontSize(UkuranTeks);
                        });
                        kanan.Item().Row(baris =>
                        {
                            baris.ConstantItem(90).Text("Pada tanggal").FontSize(UkuranTeks);
                            baris.RelativeItem().Text($": {tanggal}").FontSize(UkuranTeks);
                        });
                        // Blok penanda tangan menempel di kolom KANAN halaman;
                        // isi bloknya rata tengah, sesuai bentuk baku surat dinas.
                        kanan.Item().PaddingTop(14).AlignCenter()
                            .Text($"KEPALA DESA {IsiAtauGaris(desa?.NamaDesa)}".ToUpperInvariant())
                            .FontSize(UkuranTeks).Bold();
                        kanan.Item().Height(45);
                        kanan.Item().AlignCenter()
                            .Text(isi.Contoh ? "(............................................)" : string.Empty)
                            .FontSize(UkuranTeks);
                        if (!isi.Contoh)
                        {
                            // Nama penandatangan dicetak tebal bergaris bawah, seperti contoh.
                            kanan.Item().AlignCenter().Text(IsiAtauGaris(desa?.KepalaDesa))
                                .FontSize(UkuranTeks).Bold().Underline();
                        }
                    });
                });
                if (berlampiran)
                {
                    blok.Item().PaddingTop(6).AlignRight().Text("1 (satu) lembar Lampiran")
                        .FontSize(UkuranTeks);
                }

                // Tembusan: SETELAH blok tanda tangan Kepala Desa (bukan sejajar
                // di sebelah kirinya), rata kiri selebar halaman. Ruang tanda
                // tangan dan leading sengaja dipertahankan lega — SK dengan diktum
                // panjang (mis. Perangkat Desa, Posyandu) menjadi dua halaman,
                // bukan disumpal agar muat satu. Urutan cetak SK:
                //   Garuda -> blok KEPALA DESA/KECAMATAN/KABUPATEN -> KEPUTUSAN +
                //   NOMOR -> TENTANG + judul -> baris penetap -> Menimbang/Mengingat
                //   -> MEMUTUSKAN + diktum -> Ditetapkan di + tanda tangan -> tembusan.
                var tembusan = isi.Tembusan ?? SkPerangkatKatalog.TembusanBawaan;
                if (tembusan.Count > 0)
                {
                    blok.Item().PaddingTop(12).Text("Tembusan :")
                        .FontSize(UkuranTeks).Bold();
                    foreach (var (butir, i) in tembusan.Select((b, i) => (b, i)))
                    {
                        blok.Item().Text($"{i + 1}. {IsiPlaceholder(butir, isi, desa)}")
                            .FontSize(UkuranTeks);
                    }
                }
            });
        }

        /// <summary>
        /// Butir Memperhatikan bawaan template jabatan/kelompok, lalu bawaan umum
        /// katalog. SK pemberhentian memakai butirnya sendiri (surat pengunduran
        /// diri + rekomendasi Camat), bukan berita acara penjaringan.
        /// </summary>
        private static IReadOnlyList<string> ButirMemperhatikan(SkPerangkatIsi isi)
        {
            var template = SkPerangkatKatalog.Untuk(isi.Jabatan, isi.Kelompok);
            var milikTemplate = isi.Jenis == SkJenisPerangkat.Pemberhentian
                                && template?.MemperhatikanPemberhentian is { Count: > 0 } khusus
                ? khusus
                : template?.Memperhatikan;

            return milikTemplate is { Count: > 0 } ? milikTemplate : SkPerangkatKatalog.MemperhatikanUmum;
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

            kolom.Item().PaddingBottom(2);
        }

        // =====================================================================
        // Halaman lampiran daftar nama
        // =====================================================================

        /// <summary>Satu baris keterangan lampiran: label tetap lalu nilai setelah titik dua.</summary>
        private static void BarisLabelLampiran(ColumnDescriptor kolom, string label, string nilai)
        {
            kolom.Item().Row(baris =>
            {
                baris.ConstantItem(75).Text(label).FontSize(UkuranTeks);
                baris.RelativeItem().Text($": {nilai}").FontSize(UkuranTeks);
            });
        }

        private static void SusunLampiran(
            ColumnDescriptor kolom, DesaData? desa, SkPerangkatIsi isi,
            IReadOnlyList<SkLampiranBlok> blok)
        {
            string judul = JudulDokumen(isi, desa);
            string judulDaftar = JudulDaftarLampiran(isi, desa, judul);

            // Kepala lampiran sesuai contoh SK: baris keterangan berlabel
            // (Nomor/Tanggal/Tentang) rata kiri, lalu judul daftar rata tengah.
            kolom.Item().Text($"Lampiran Keputusan Kepala Desa {IsiAtauGaris(desa?.NamaDesa)}")
                .FontSize(UkuranTeks);
            BarisLabelLampiran(kolom, "Nomor", IsiAtauGaris(isi.Nomor));
            BarisLabelLampiran(kolom, "Tanggal", TanggalIndo(isi.Tanggal ?? DateTime.Today));
            BarisLabelLampiran(kolom, "Tentang", judul);
            kolom.Item().PaddingTop(4).PaddingBottom(8).AlignCenter()
                .Text(judulDaftar)
                .FontSize(UkuranTeks).Bold();

            // Lampiran tanpa judul unit dicetak satu tabel; bila perlu judul
            // (banyak unit / unit bernama), setiap blok diberi kepala "UNIT …".
            bool perluJudul = SkPerangkatLampiran.PerluJudulUnit(blok);
            var kolomJudul = JudulKolomLampiran(isi.Lampiran,
                // Contoh SK KPM memuat NIK dan nomor telepon; kelompok lain
                // mengikuti contoh SK-nya sendiri (tanpa kedua kolom itu).
                string.Equals(isi.Kelompok, "KADER KPM", StringComparison.Ordinal));

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

        /// <summary>
        /// Judul daftar pada halaman lampiran. Kelompok yang contoh SK-nya punya
        /// judul sendiri memakainya (mis. Posyandu: “DAFTAR NAMA KADER POSYANDU
        /// DAN KADER POKJANAL DESA …”, Linmas: “DAFTAR PENGANGKATAN ANGGOTA
        /// SATUAN PERLINDUNGAN MASYARAKAT (LINMAS) DESA …”), sedangkan kelompok
        /// lain memakai “DAFTAR {judul keputusan}”. SK pemberhentian selalu memakai
        /// judul keputusannya supaya tidak tercetak “DAFTAR PENGANGKATAN”.
        /// </summary>
        private static string JudulDaftarLampiran(SkPerangkatIsi isi, DesaData? desa, string judul)
        {
            if (isi.Jenis == SkJenisPerangkat.Pemberhentian) return $"DAFTAR {judul}";

            string milik = SkPerangkatKatalog.Untuk(isi.Jabatan, isi.Kelompok)?.JudulLampiran
                ?? string.Empty;
            return string.IsNullOrWhiteSpace(milik)
                ? $"DAFTAR {judul}"
                : IsiPlaceholder(milik, isi, desa).ToUpperInvariant();
        }

        /// <summary>Isi sel tabel lampiran sesuai judul kolomnya; kosong menjadi "-".</summary>
        private static string IsiSel(SkLampiranBaris baris, string judulKolom) => judulKolom switch
        {
            "NO" => baris.Nomor.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "NAMA" => IsiAtauGaris(baris.Orang.Nama),
            "NIK" => AtauStrip(baris.Orang.NIK),
            "JABATAN" => PeranAtauStrip(baris.Orang.Peran),
            "TEMPAT & TGL. LAHIR" => AtauStrip(baris.Orang.TempatTanggalLahir),
            "PENDIDIKAN" => AtauStrip(baris.Orang.Pendidikan),
            "PEKERJAAN" => AtauStrip(baris.Orang.Pekerjaan),
            "ALAMAT" => AtauStrip(baris.Orang.Alamat),
            "AGAMA" => AtauStrip(baris.Orang.Agama),
            "GOL. DARAH" => AtauStrip(baris.Orang.GolonganDarah),
            "STATUS KAWIN" => AtauStrip(baris.Orang.StatusPerkawinan),
            "NO. TELEPON/HP" => AtauStrip(baris.Orang.NomorHP),
            _ => "-"
        };

        private static string PeranAtauStrip(string? peran) =>
            string.IsNullOrWhiteSpace(peran) ? "-" : peran.Trim();

        private static string AtauStrip(string? nilai) =>
            string.IsNullOrWhiteSpace(nilai) ? "-" : nilai.Trim();
    }
}
