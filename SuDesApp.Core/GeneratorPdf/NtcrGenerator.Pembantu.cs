using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;
using SudesPdf.Core.Layout;
using System.Globalization;

namespace SuDesApp.GeneratorPdf
{
    public partial class NtcrGenerator : SuratGeneratorBase
    {
        /// <summary>
        /// Tabel "label : nilai" rapat khas blanko (lebar label lebih lega daripada
        /// TabelFormulir surat desa, tanpa jarak antar baris) agar seluruh blanko
        /// NTCR muat di halaman yang seharusnya.
        /// </summary>
        private static void TabelBaris(
            BadanSurat badan,
            IEnumerable<(string Label, string? Nilai)>? baris,
            float indentKiri = 20f,
            float jarakAtas = 4f,
            float jarakBawah = 0f)
        {
            var daftar = baris?.ToList() ?? new List<(string Label, string? Nilai)>();
            if (daftar.Count == 0) return;

            badan.Blok(container =>
            {
                var area = container;
                if (jarakAtas > 0f) area = area.PaddingTop(SuratRenderer.JarakBlok(jarakAtas));
                if (jarakBawah > 0f) area = area.PaddingBottom(SuratRenderer.JarakBlok(jarakBawah));
                if (indentKiri > 0f) area = area.PaddingLeft(indentKiri);

                area.Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn(LebarLabelSel);
                        cols.RelativeColumn(3);
                        cols.RelativeColumn(100f - LebarLabelSel - 3f);
                    });

                    foreach (var (label, nilai) in daftar)
                    {
                        SelData(table.Cell(), label ?? string.Empty);
                        SelData(table.Cell(), ":");
                        SelData(table.Cell(), Isi(nilai));
                    }
                });
            });
        }

        private static void SelData(IContainer cell, string teks)
        {
            float leading = LeadingData;
            float padding = KerapatanSurat.Aktif.PaddingSelTabel;

            cell.PaddingTop(padding).PaddingBottom(padding)
                .Text(teks ?? string.Empty)
                .FontSize(DEFAULT_FONT_SIZE)
                .LineHeight(leading / DEFAULT_FONT_SIZE);
        }

        // =====================================================================
        // Blok kepala surat
        // =====================================================================

        /// <summary>Kepala blanko desa: "KANTOR DESA/KELURAHAN : …" (N1 &amp; N6).</summary>
        private static void ComposeKepalaDesa(BadanSurat badan, DesaData desa)
        {
            badan.Blok(c => c.PaddingBottom(12f).Column(kolom =>
            {
                BarisLabel(kolom, "KANTOR DESA/KELURAHAN", desa.NamaDesa?.ToUpperInvariant() ?? "[NAMA DESA]", 6f);
                BarisLabel(kolom, "KECAMATAN", desa.Kecamatan?.ToUpperInvariant() ?? "[KECAMATAN]", 6f);
                BarisLabel(kolom, "KABUPATEN/KOTA", desa.Kabupaten?.ToUpperInvariant() ?? "[KABUPATEN]", 0f);
            }));
        }

        /// <summary>Judul + perihal + tanggal pada blanko permohonan kepada KUA (N2 &amp; N3).</summary>
        private static void ComposeJudulPermohonan(BadanSurat badan, SuratData suratData, DesaData desa, string judul, string perihal)
        {
            badan.Paragraf(judul, tebal: true, rata: Rata.Tengah, ukuran: TITLE_FONT_SIZE, jarakAtas: 12);

            string tempatTanggal = $"{desa.NamaDesa}, {suratData.TanggalSurat.ToString("dd MMMM yyyy", Budaya)}";

            badan.Blok(c => c.PaddingTop(14).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(60);
                    cols.RelativeColumn(40);
                });

                table.Cell().Column(kolom =>
                {
                    kolom.Item().Element(c2 => SuratRenderer.Teks(c2, $"Nomor : {HitungNomorTampil(suratData)}", ukuran: DEFAULT_FONT_SIZE));
                    kolom.Item().Element(c2 => SuratRenderer.Teks(c2, $"Perihal : {perihal}", ukuran: DEFAULT_FONT_SIZE));
                });

                table.Cell().Element(c2 => SuratRenderer.Teks(c2, tempatTanggal, ukuran: DEFAULT_FONT_SIZE, rata: Rata.Kanan));
            }));
        }

        /// <summary>Blok "Kepada yth, … di …" pada blanko permohonan (N2 &amp; N3).</summary>
        private static void ComposeAlamatTujuan(BadanSurat badan, string pejabat, string? tujuan)
        {
            badan.Paragraf("Kepada yth,", jarakAtas: 15);
            badan.Paragraf(pejabat, indentKiri: 20);
            badan.Paragraf($"di {TujuanKua(tujuan)}", indentKiri: 20);
        }

        /// <summary>Nama KUA tujuan: dilengkapi "Kecamatan" bila pengguna belum menulisnya.</summary>
        private static string TujuanKua(string? tujuan)
        {
            if (string.IsNullOrWhiteSpace(tujuan)) return Titik;

            string bersih = tujuan.Trim();
            return bersih.Contains("kecamatan", StringComparison.OrdinalIgnoreCase) ||
                   bersih.StartsWith("KUA", StringComparison.OrdinalIgnoreCase) ||
                   bersih.Contains("PPN", StringComparison.OrdinalIgnoreCase)
                ? bersih
                : $"Kecamatan {bersih}";
        }

        private static void BarisLabel(ColumnDescriptor kolom, string label, string nilai, float jarakBawah)
        {
            kolom.Item().Row(row =>
            {
                row.ConstantItem(LebarLabelKepala).Text($"{label} :");
                row.RelativeItem().Text(nilai);
            });

            if (jarakBawah > 0f)
            {
                kolom.Item().Height(jarakBawah);
            }
        }

        // =====================================================================
        // Blok tanda tangan
        // =====================================================================

        /// <summary>Blok tanda tangan desa (satu kolom, rata kanan) untuk N1, N6, &amp; N8.</summary>
        private void TandaTanganDesa(BadanSurat badan, SuratData suratData, DesaData desa)
        {
            bool sekdes = ApakahSekdes(suratData);
            string jabatan = sekdes
                ? $"A/N Kepala Desa {desa.NamaDesa}\nSekretaris Desa"
                // Pejabat mengikuti lembaga: “Kepala Desa” (desa) atau “Lurah” (kelurahan).
                : KopSurat.KataPejabat(desa);

            string nama = NamaPejabatSurat(suratData, desa);
            string tempatTanggal = $"{desa.NamaDesa}, {suratData.TanggalSurat.ToString("dd MMMM yyyy", Budaya)}";

            // NIP hanya untuk pejabat ASN kelurahan (Lurah); Sekretaris Desa
            // menandatangani a.n. Kepala Desa tanpa NIP sendiri di sini.
            string? nip = !sekdes && KopSurat.IsKelurahan(desa)
                ? (desa.NipKepalaDesa ?? string.Empty).Trim()
                : null;

            TandaTanganKanan(badan, suratData, desa, jabatan.Split('\n'), nama: nama, barisAtas: tempatTanggal, nip: nip);
        }

        /// <summary>Apakah surat ditandatangani Sekretaris Desa (a.n. Kepala Desa).</summary>
        private static bool ApakahSekdes(SuratData suratData) =>
            string.Equals(suratData.PejabatPenandatangan, "Sekretaris Desa", StringComparison.OrdinalIgnoreCase);

        /// <summary>Nama pejabat penandatangan: dari form, atau dari data desa.</summary>
        private static string NamaPejabatSurat(SuratData suratData, DesaData desa)
        {
            bool sekdes = ApakahSekdes(suratData);
            return NamaFormatter.ToUpperNama(!string.IsNullOrWhiteSpace(suratData.NamaPejabatPenandatangan)
                ? suratData.NamaPejabatPenandatangan
                : (sekdes ? desa.SekretarisDesa : desa.KepalaDesa));
        }

        /// <summary>Jabatan pejabat penandatangan yang tercetak di badan surat (N8).</summary>
        private static string JabatanPejabatSurat(SuratData suratData, DesaData desa) =>
            ApakahSekdes(suratData)
                ? $"Sekretaris Desa {desa.NamaDesa}"
                : $"{KopSurat.KataPejabat(desa)} {desa.NamaDesa}";

        /// <summary>
        /// Blok tanda tangan pada kolom KANAN halaman dengan ruang tanda tangan;
        /// isi bloknya rata tengah, sesuai bentuk baku tanda tangan surat dinas.
        /// NIP dicetak di bawah nama bila diisi (Lurah pada kelurahan).
        /// </summary>
        private static void TandaTanganKanan(BadanSurat badan, SuratData suratData, DesaData desa, string[] jabatan, string? nama, string? barisAtas = null, string? nip = null)
        {
            string namaFinal = string.IsNullOrWhiteSpace(nama)
                ? "..................................."
                : nama;
            string[] baris = barisAtas != null
                ? new[] { barisAtas }.Concat(jabatan).ToArray()
                : jabatan;

            badan.Blok(c => c.PaddingTop(20).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(50);
                    cols.RelativeColumn(50);
                });

                table.Cell().Text(string.Empty);
                table.Cell().Element(sel => sel.Column(kolom =>
                {
                    foreach (var b in baris)
                    {
                        kolom.Item().Element(c2 => SuratRenderer.Teks(c2, b, rata: Rata.Tengah));
                    }

                    kolom.Item().Height(SuratRenderer.RuangTandaTangan);
                    kolom.Item().Element(c2 => SuratRenderer.Teks(c2, namaFinal, tebal: true, rata: Rata.Tengah));

                    if (!string.IsNullOrWhiteSpace(nip))
                    {
                        kolom.Item().Element(c2 => SuratRenderer.Teks(c2, $"NIP {nip}", rata: Rata.Tengah));
                    }
                }));
            }));
        }

        /// <summary>Blok tanda tangan dua kolom setara (N4: calon suami &amp; istri; N5: ayah &amp; ibu).</summary>
        private static void TandaTanganDuaPihak(
            BadanSurat badan,
            DesaData desa,
            SuratData suratData,
            (string Jabatan, string Nama) kiri,
            (string Jabatan, string Nama) kanan)
        {
            string tempatTanggal = $"{desa.NamaDesa}, {suratData.TanggalSurat.ToString("dd MMMM yyyy", Budaya)}";

            badan.Blok(c => c.PaddingTop(10).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(1);
                    cols.RelativeColumn(1);
                });

                // Kiri: jabatan di atas, lalu nama setelah ruang tanda tangan.
                table.Cell().Element(sel => sel.Column(kolom =>
                {
                    kolom.Item().Height(SuratRenderer.TinggiBaris * 2);
                    kolom.Item().Element(c2 => SuratRenderer.Teks(c2, kiri.Jabatan, rata: Rata.Tengah));
                    kolom.Item().Height(SuratRenderer.RuangTandaTangan);
                    kolom.Item().Element(c2 => SuratRenderer.Teks(c2, NamaFormatter.ToUpperNama(kiri.Nama), tebal: true, rata: Rata.Tengah));
                }));

                // Kanan: tempat & tanggal, jabatan, nama.
                table.Cell().Element(sel => sel.Column(kolom =>
                {
                    kolom.Item().Element(c2 => SuratRenderer.Teks(c2, tempatTanggal, rata: Rata.Tengah));
                    kolom.Item().Element(c2 => SuratRenderer.Teks(c2, kanan.Jabatan, rata: Rata.Tengah));
                    kolom.Item().Height(SuratRenderer.RuangTandaTangan);
                    kolom.Item().Element(c2 => SuratRenderer.Teks(c2, NamaFormatter.ToUpperNama(kanan.Nama), tebal: true, rata: Rata.Tengah));
                }));
            }));
        }

        /// <summary>Blok "Diterima Tanggal … / Yang menerima, Kepala KUA" + "Wassalam, Pemohon" (N2 &amp; N3).</summary>
        private void TandaTanganPemohonKua(BadanSurat badan, SuratData suratData, WargaData warga, NtcrData ntcr)
        {
            string diterima = string.IsNullOrWhiteSpace(ntcr.TanggalDiterima)
                ? "Diterima Tanggal ..............."
                : $"Diterima Tanggal {TanggalIndonesia(ntcr.TanggalDiterima)}";

            badan.Blok(c => c.PaddingTop(20).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(1);
                    cols.RelativeColumn(1);
                });

                table.Cell().Element(sel => sel.Column(kolom =>
                {
                    kolom.Item().Element(c2 => SuratRenderer.Teks(c2, diterima, rata: Rata.Tengah));
                    kolom.Item().Element(c2 => SuratRenderer.Teks(c2, "Yang menerima,", rata: Rata.Tengah));
                    kolom.Item().Element(c2 => SuratRenderer.Teks(c2, "Kepala KUA/PPN Luar Negeri", rata: Rata.Tengah));
                    kolom.Item().Height(SuratRenderer.RuangTandaTangan);
                    kolom.Item().Element(c2 => SuratRenderer.Teks(c2, "...................................", rata: Rata.Tengah));
                }));

                table.Cell().Element(sel => sel.Column(kolom =>
                {
                    kolom.Item().Height(SuratRenderer.TinggiBaris * 3);
                    kolom.Item().Element(c2 => SuratRenderer.Teks(c2, "Wassalam,", rata: Rata.Tengah));
                    kolom.Item().Element(c2 => SuratRenderer.Teks(c2, "Pemohon", rata: Rata.Tengah));
                    kolom.Item().Height(SuratRenderer.RuangTandaTangan);
                    kolom.Item().Element(c2 => SuratRenderer.Teks(c2, NamaFormatter.ToUpperNama(warga.Nama), tebal: true, rata: Rata.Tengah));
                }));
            }));
        }

        // =====================================================================
        // Baris identitas
        // =====================================================================

        /// <summary>Identitas calon mempelai ala blanko N4/N5 (nama, bin/binti, NIK, TTL, …).</summary>
        private List<(string Label, string? Nilai)> BarisIdentitasCalon(bool istri, WargaData warga, NtcrData ntcr)
        {
            var orangTuaAyah = istri ? ntcr.AyahCalonIstri : ntcr.AyahCalonSuami;

            return new List<(string, string?)>
            {
                ("1. Nama lengkap dan alias", NamaFormatter.ToUpperNama(istri ? ntcr.NamaIstri : warga.Nama)),
                ($"2. {(istri ? "Binti" : "Bin")}", Isi(NamaFormatter.ToUpperNama(orangTuaAyah?.Nama))),
                ("3. Nomor Induk Kependudukan", Isi(istri ? ntcr.NikIstri : warga.NIK)),
                ("4. Tempat dan tanggal lahir", TempatTanggalLahir(istri ? ntcr.TempatLahirIstri : warga.TempatLahir,
                    istri ? ntcr.TanggalLahirIstri : warga.TanggalLahir)),
                ("5. Kewarganegaraan", Isi(istri ? ntcr.KewarganegaraanIstri ?? "WNI" : warga.Kewarganegaraan ?? "WNI")),
                ("6. Agama", Isi(istri ? ntcr.AgamaIstri : warga.Agama)),
                ("7. Pekerjaan", Isi(istri ? ntcr.PekerjaanIstri : warga.Pekerjaan)),
                ("8. Alamat", AlamatCalon(istri, ntcr, warga))
            };
        }

        /// <summary>Identitas orang tua/wali ala blanko N1 &amp; N5.</summary>
        private List<(string Label, string? Nilai)> BarisOrangTua(NtcrOrangTua orangTua, bool pakaiBin)
        {
            orangTua ??= new NtcrOrangTua();

            var baris = new List<(string, string?)>();
            if (pakaiBin)
            {
                baris.Add(("1. Nama lengkap dan alias", NamaFormatter.ToUpperNama(orangTua.Nama)));
                baris.Add(("2. Bin/Binti", Isi(NamaFormatter.ToUpperNama(orangTua.BinBinti))));
                baris.Add(("3. Nomor Induk Kependudukan", Isi(orangTua.Nik)));
                baris.Add(("4. Tempat dan tanggal lahir", TempatTanggalLahir(orangTua.TempatLahir, orangTua.TanggalLahir)));
                baris.Add(("5. Kewarganegaraan", Isi(orangTua.Kewarganegaraan)));
                baris.Add(("6. Agama", Isi(orangTua.Agama)));
                baris.Add(("7. Pekerjaan", Isi(orangTua.Pekerjaan)));
                baris.Add(("8. Alamat", Isi(orangTua.Alamat)));
            }
            else
            {
                baris.Add(("Nama Lengkap dan alias", NamaFormatter.ToUpperNama(orangTua.Nama)));
                baris.Add(("Nomor Induk Kependudukan (NIK)", Isi(orangTua.Nik)));
                baris.Add(("Tempat dan tanggal lahir", TempatTanggalLahir(orangTua.TempatLahir, orangTua.TanggalLahir)));
                baris.Add(("Kewarganegaraan", Isi(orangTua.Kewarganegaraan)));
                baris.Add(("Agama", Isi(orangTua.Agama)));
                baris.Add(("Pekerjaan", Isi(orangTua.Pekerjaan)));
                baris.Add(("Alamat", Isi(orangTua.Alamat)));
            }

            return baris;
        }


        /// <summary>Keterangan bebas dari form (jika diisi) — dicetak sebagai paragraf tersendiri.</summary>
        private static void AddKeteranganBebas(BadanSurat badan, SuratData suratData, string keteranganTextBox)
        {
            if (!string.IsNullOrWhiteSpace(keteranganTextBox) || !string.IsNullOrWhiteSpace(suratData.Keterangan))
            {
                string keterangan = !string.IsNullOrWhiteSpace(keteranganTextBox)
                    ? keteranganTextBox
                    : suratData.Keterangan ?? string.Empty;

                badan.Paragraf(keterangan, rata: Rata.Justify, indentKiri: 20, jarakAtas: 12);
            }
        }

        // =====================================================================
        // Pembantu umum
        // =====================================================================

        private static string Isi(string? nilai) => string.IsNullOrWhiteSpace(nilai) ? Titik : nilai.Trim();

        /// <summary>Nama untuk blanko yang boleh kosong: titik-titik bila belum diisi.</summary>
        private static string NamaAtauTitik(string? nama) =>
            string.IsNullOrWhiteSpace(nama) ? Titik : NamaFormatter.ToUpperNama(nama);

        /// <summary>
        /// Sebutan status pernikahan menurut blanko N1: laki-laki memakai
        /// Jejaka/Duda, perempuan memakai Perawan/Janda.
        /// </summary>
        private static string StatusSebutan(bool perempuan, string? status)
        {
            if (string.IsNullOrWhiteSpace(status)) return null!;

            string bersih = status.Trim();
            if (bersih.Contains("Belum", StringComparison.OrdinalIgnoreCase)) return perempuan ? "Perawan" : "Jejaka";
            if (bersih.Contains("Cerai", StringComparison.OrdinalIgnoreCase) ||
                bersih.Contains("Janda", StringComparison.OrdinalIgnoreCase) ||
                bersih.Contains("Duda", StringComparison.OrdinalIgnoreCase))
            {
                return perempuan ? "Janda" : "Duda";
            }

            return bersih;
        }

        private static string HitungNomorTampil(SuratData suratData) =>
            string.IsNullOrWhiteSpace(suratData.NomorSurat) ? Titik : suratData.NomorSurat;

        /// <summary>Alamat calon mempelai (pemohon memakai komponen alamat, calon istri teks bebas).</summary>
        private static string AlamatCalon(bool istri, NtcrData ntcr, WargaData warga)
        {
            if (istri)
            {
                return Isi(ntcr.AlamatIstri);
            }

            var rtRw = AlamatFormatter.FormatRtRw(warga.RT, warga.RW);
            var bagian = new[]
            {
                warga.Dusun,
                rtRw.Length > 0 ? rtRw : null,
                string.IsNullOrWhiteSpace(warga.Desa) ? null : $"Desa {warga.Desa}",
                string.IsNullOrWhiteSpace(warga.Kecamatan) ? null : $"Kecamatan {warga.Kecamatan}",
                string.IsNullOrWhiteSpace(warga.Kabupaten) ? null : $"Kabupaten {warga.Kabupaten}"
            }.Where(b => !string.IsNullOrWhiteSpace(b)).Select(b => b!.Trim());

            string alamat = string.Join(", ", bagian);
            return Isi(string.IsNullOrWhiteSpace(alamat) ? null : alamat);
        }

        /// <summary>"Tempat, dd MMMM yyyy" — titik-titik bila belum lengkap.</summary>
        private string TempatTanggalLahir(string? tempat, string? tanggalDb)
        {
            string? tempatBersih = tempat?.Trim();
            string tanggal = FormatTanggalLahir(tanggalDb);

            if (string.IsNullOrWhiteSpace(tempatBersih) && tanggal == null) return Titik;
            if (string.IsNullOrWhiteSpace(tempatBersih)) return Isi(tanggal);
            if (tanggal == null) return Isi(tempatBersih);
            return $"{tempatBersih}, {tanggal}";
        }

        /// <summary>Tanggal DB/UI menjadi "dd MMMM yyyy"; null bila kosong/tidak terbaca.</summary>
        private static string FormatTanggalLahir(string? tanggal)
        {
            if (string.IsNullOrWhiteSpace(tanggal)) return null!;

            if (DateTime.TryParseExact(tanggal.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var db))
                return db.ToString("dd MMMM yyyy", Budaya);

            if (DateTime.TryParseExact(tanggal.Trim(), "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var ui))
                return ui.ToString("dd MMMM yyyy", Budaya);

            return tanggal.Trim();
        }

        private static string TanggalIndonesia(string? tanggal)
        {
            if (string.IsNullOrWhiteSpace(tanggal)) return null!;
            return FormatTanggalLahir(tanggal);
        }

    }
}
