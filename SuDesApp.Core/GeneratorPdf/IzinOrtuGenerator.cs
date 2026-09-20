// File: SuDesApp/GeneratorPdf/IzinOrtuGenerator.cs
// Badan surat dibuat langsung dengan QuestPDF (BadanSurat), tanpa lapisan kompat.
using Microsoft.Extensions.Logging;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Repositories;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace SuDesApp.GeneratorPdf
{
    public class IzinOrtuGenerator : SuratGeneratorBase
    {
        protected override string JudulSurat => "SURAT IZIN SUAMI / ORANG TUA";

        protected override bool UseDefaultFooter => true;
        protected override bool ShowPemohonInFooter => false;

        private const float LEADING_NORMAL = 14f; // Sedikit lebih rapat dari 15f di SKD
        private const float MARGIN_PARAGRAF_KECIL = 5f;
        private const float MARGIN_PARAGRAF_BESAR = 15f; // Sebelumnya 20f
        private const float MARGIN_ANTAR_BLOK = 8f;  // Sebelumnya 10f atau lebih
        private const float PADDING_CELL_TABLE_FORM = 1f;

        public IzinOrtuGenerator(AppConfig config, FileService fileService, IDesaRepository desaRepository, ISuratRepository suratRepository, SettingsManager settingsManager, ILogger<IzinOrtuGenerator> logger, ILoggerFactory loggerFactory)
            : base(config, fileService, desaRepository, suratRepository, settingsManager, logger, loggerFactory)
        {
        }

        /// <summary>Judul surat izin dicetak lebih rapat dari surat lain (leading 13 / 10).</summary>
        protected override void ComposeJudul(BadanSurat judul, SuratData suratData)
        {
            string nomor = HitungNomorSurat(suratData);
            judul.Blok(c => SuratRenderer.JudulDanNomor(c, JudulSurat, nomor, leadingJudul: 13f, leadingNomor: 10f));
        }

        protected override void ComposeBody(BadanSurat badan, SuratData suratData, string? keteranganTextBox = null)
        {
            // Validasi jenis surat, lebih ketat menggunakan enum
            if (suratData.Jenis != SuratData.JenisSuratEnum.IzinOrtu)
            {
                _logger.LogError("Jenis surat tidak valid: Jenis={Jenis}, diharapkan IzinOrtu untuk ID_Surat={ID_Surat}",
                    suratData.Jenis, suratData.ID_Surat);
                throw new ArgumentException($"SuratData bukan jenis IzinOrtu. Jenis aktual: {suratData.Jenis}");
            }

            if (suratData.Warga == null || !suratData.Warga.IsValid()) // Validasi data orang tua/wali
            {
                _logger.LogError("Data Warga (Orang Tua/Wali) tidak valid atau null untuk Surat ID: {SuratId}", suratData.ID_Surat);
                throw new InvalidOperationException("Data Orang Tua/Wali tidak valid untuk membuat Surat Izin.");
            }
            // Validasi data anak dari IzinOrtu
            if (suratData.IzinOrtu == null || suratData.IzinOrtu.ID_Warga_Anak <= 0 || string.IsNullOrWhiteSpace(suratData.IzinOrtu.NamaAnak))
            {
                _logger.LogError("Data IzinOrtu (Anak) tidak valid atau null untuk Surat ID: {SuratId}. ID_Warga_Anak: {IdWargaAnak}, Nama Anak: {NamaAnak}",
                    suratData.ID_Surat, suratData.IzinOrtu?.ID_Warga_Anak, suratData.IzinOrtu?.NamaAnak);
                throw new InvalidOperationException("Data Anak tidak valid atau tidak lengkap untuk membuat Surat Izin.");
            }

            if (suratData.Desa == null)
            {
                _logger.LogError("Data Desa tidak ditemukan untuk Surat ID: {SuratId}", suratData.ID_Surat);
                throw new InvalidOperationException("Data Desa tidak ditemukan.");
            }

            var orangTua = suratData.Warga;
            var anak = suratData.IzinOrtu; // Menggunakan suratData.IzinOrtu yang sudah diisi

            badan.Paragraf("Yang bertanda tangan dibawah ini :", jarakBawah: MARGIN_PARAGRAF_KECIL);

            // Tabel Data Pemberi Izin (Orang Tua/Wali)
            TambahTabelData(badan, new List<(string, string?)>
            {
                ("Nama", orangTua.Nama == null ? null : NamaFormatter.ToUpperNama(orangTua.Nama)),
                ("NIK", orangTua.NIK),
                ("Tempat/Tgl. Lahir", $"{orangTua.TempatLahir ?? ""}, {ParseTanggalToUiFormat(orangTua.TanggalLahir)}"),
                ("Jenis Kelamin", orangTua.JenisKelamin),
                ("Agama", orangTua.Agama),
                ("Pekerjaan", orangTua.Pekerjaan),
                ("Alamat", AlamatFormatter.Format(orangTua)),
            });

            badan.Paragraf("Dengan ini memberikan izin kepada :",
                jarakAtas: MARGIN_ANTAR_BLOK,
                jarakBawah: MARGIN_PARAGRAF_KECIL);

            // Tabel Data Yang Diberi Izin (Anak)
            TambahTabelData(badan, new List<(string, string?)>
            {
                ("Nama", anak.NamaAnak == null ? null : NamaFormatter.ToUpperNama(anak.NamaAnak)),
                ("NIK", anak.NIKAnak),
                ("Tempat/Tgl. Lahir", $"{anak.TempatLahirAnak ?? ""}, {ParseTanggalToUiFormat(anak.TanggalLahirAnak)}"),
                ("Jenis Kelamin", anak.JenisKelaminAnak),
                ("Agama", anak.AgamaAnak),
                ("Pekerjaan", anak.PekerjaanAnak),
                ("Alamat", string.IsNullOrWhiteSpace(anak.AlamatAnak) ? "[Alamat Anak]" : anak.AlamatAnak),
            });

            // Menggunakan suratData.Keterangan yang sudah di-generate oleh IzinOrtuInput
            string? narasiIzin = suratData.Keterangan;
            if (string.IsNullOrWhiteSpace(narasiIzin))
            {
                _logger.LogWarning("suratData.Keterangan is null or empty for IzinOrtu ID_Surat={ID_Surat}. Using generic fallback narasi.", suratData.ID_Surat);
                // Fallback bila keterangan tidak ada (seharusnya sudah diisi oleh IzinOrtuInput)
                narasiIzin = $"Untuk bekerja sebagai Tenaga Kerja Indonesia (TKI) di luar negeri dengan negara tujuan {anak.NegaraTujuan?.ToUpper() ?? "[NEGARA TUJUAN]"} melalui PT. {anak.NamaPT?.ToUpper() ?? "[NAMA PT]"} " +
                             $"sesuai dengan kontrak kerja yang berlaku dan saya tidak akan menuntut/menggangu gugat dalam bentuk apapun.";
            }
            else
            {
                _logger.LogInformation("Using narasiIzin from suratData.Keterangan for IzinOrtu ID_Surat={ID_Surat}", suratData.ID_Surat);
            }

            badan.Paragraf(narasiIzin,
                rata: Rata.Justify,
                indentKiri: 30,
                jarakAtas: MARGIN_PARAGRAF_BESAR,
                jarakBawah: MARGIN_PARAGRAF_KECIL);

            badan.Paragraf("Demikian surat izin ini dibuat dengan sebenarnya tanpa ada unsur paksaan dari pihak manapun dan untuk dipergunakan sebagaimana mestinya.",
                rata: Rata.Justify,
                jarakAtas: MARGIN_PARAGRAF_KECIL,
                jarakBawah: MARGIN_PARAGRAF_BESAR);
        }

        /// <summary>
        /// Kaki surat izin: tanggal, blok “Yang diberi Izin,” / “Yang Memberi Izin,”,
        /// lalu blok “Mengetahui;” Kepala Desa di bawahnya.
        /// </summary>
        protected override void ComposeTandaTangan(BadanSurat kaki, SuratData suratData)
        {
            var anak = suratData.IzinOrtu;
            var orangTua = suratData.Warga;

            string namaDesa = suratData.Desa.NamaDesa ?? "[Nama Desa]";
            string tanggalTerformat = suratData.TanggalSurat.ToString("dd MMMM yyyy", new CultureInfo("id-ID"));
            string namaAnak = string.IsNullOrWhiteSpace(anak?.NamaAnak) ? "_______________________" : NamaFormatter.ToUpperNama(anak!.NamaAnak);
            string namaOrangTua = string.IsNullOrWhiteSpace(orangTua?.Nama) ? "_______________________" : NamaFormatter.ToUpperNama(orangTua!.Nama);
            string namaKades = string.IsNullOrWhiteSpace(suratData.Desa.KepalaDesa)
                ? "_______________________"
                : NamaFormatter.ToUpperNama(suratData.Desa.KepalaDesa);

            kaki.Blok(container => container.PaddingTop(MARGIN_PARAGRAF_BESAR).Column(kolom =>
            {
                kolom.Item().Element(c => SuratRenderer.Teks(c, $"{namaDesa}, {tanggalTerformat}", rata: Rata.Tengah));

                // Dua pihak: anak yang diberi izin dan orang tua yang memberi izin.
                kolom.Item().PaddingTop(MARGIN_PARAGRAF_KECIL).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(1);
                        c.RelativeColumn(1);
                    });

                    table.Cell().Element(sel => sel.Column(cell =>
                    {
                        cell.Item().Element(c => SuratRenderer.Teks(c, "Yang diberi Izin,", rata: Rata.Tengah));
                        cell.Item().Height(SuratRenderer.RuangTandaTangan);
                        cell.Item().Element(c => SuratRenderer.Teks(c, namaAnak, tebal: true, rata: Rata.Tengah));
                    }));

                    table.Cell().Element(sel => sel.Column(cell =>
                    {
                        cell.Item().Element(c => SuratRenderer.Teks(c, "Yang Memberi Izin,", rata: Rata.Tengah));
                        cell.Item().Height(SuratRenderer.RuangTandaTangan);
                        cell.Item().Element(c => SuratRenderer.Teks(c, namaOrangTua, tebal: true, rata: Rata.Tengah));
                    }));
                });

                // Blok “Mengetahui;” selebar halaman di bawah kedua blok di atas.
                kolom.Item().PaddingTop(MARGIN_PARAGRAF_BESAR).Column(mengetahui =>
                {
                    mengetahui.Item().Element(c => SuratRenderer.Teks(c, "Mengetahui;", rata: Rata.Tengah));
                    mengetahui.Item().Element(c => SuratRenderer.Teks(c, $"KEPALA DESA {namaDesa.ToUpper()}", rata: Rata.Tengah));
                    mengetahui.Item().Height(SuratRenderer.RuangTandaTangan);
                    mengetahui.Item().Element(c => SuratRenderer.Teks(c, namaKades, tebal: true, rata: Rata.Tengah));
                });
            }));
        }

        /// <summary>
        /// Tabel data surat izin: label 30%, titik dua 5%, nilai 65% — tanpa garis,
        /// padding 1pt dan nilai boleh turun ke baris berikutnya.
        /// </summary>
        private void TambahTabelData(BadanSurat badan, IReadOnlyList<(string Label, string? Nilai)> baris)
        {
            badan.Blok(container => container.PaddingBottom(MARGIN_ANTAR_BLOK).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(30);
                    cols.RelativeColumn(5);
                    cols.RelativeColumn(65);
                });

                foreach (var (label, nilai) in baris)
                {
                    SelTeks(table.Cell(), label, paddingKanan: 5);
                    SelTeks(table.Cell(), ":", paddingKanan: 5);
                    SelTeks(table.Cell(), string.IsNullOrWhiteSpace(nilai) ? "-" : nilai, paddingKanan: 0);
                }
            }));
        }

        private void SelTeks(IContainer cell, string teks, float paddingKanan)
            => cell.PaddingTop(PADDING_CELL_TABLE_FORM)
                .PaddingBottom(PADDING_CELL_TABLE_FORM)
                .PaddingRight(paddingKanan)
                .Text(teks ?? string.Empty)
                .FontSize(DEFAULT_FONT_SIZE)
                .LineHeight(LEADING_NORMAL / DEFAULT_FONT_SIZE);

        private string ParseTanggalToUiFormat(string dbDate)
        {
            if (string.IsNullOrWhiteSpace(dbDate)) return string.Empty;
            if (DateTime.TryParseExact(dbDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDate))
            {
                return parsedDate.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
            }
            _logger.LogWarning("Gagal memformat tanggal dari DB: {DbDate} untuk IzinOrtuGenerator", dbDate);
            return dbDate;
        }
    }
}
