// File: SuDesApp/GeneratorPdf/DomisiliInstansiGenerator.cs
// Badan surat dibuat langsung dengan QuestPDF (BadanSurat), tanpa lapisan kompat.
using Microsoft.Extensions.Logging;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Repositories;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;
using System;
using System.Collections.Generic; // Diperlukan untuk List
using System.Threading.Tasks;

namespace SuDesApp.GeneratorPdf
{
    public class DomisiliInstansiGenerator : SuratGeneratorBase
    {
        public DomisiliInstansiGenerator(AppConfig config, FileService fileService, IDesaRepository desaRepository, ISuratRepository suratRepository, SettingsManager settingsManager, ILogger<DomisiliInstansiGenerator> logger, ILoggerFactory loggerFactory)
            : base(config, fileService, desaRepository, suratRepository, settingsManager, logger, loggerFactory)
        {
            _logger.LogInformation("DomisiliInstansiGenerator initialized.");
        }

        protected override string JudulSurat => "SURAT KETERANGAN DOMISILI";
        protected override bool ShowPemohonInFooter => false;

        protected override void ComposeBody(BadanSurat badan, SuratData suratData, string? keteranganTextBoxValue = null)
        {
            try
            {
                // Perbaikan: Pengecekan data Instansi dengan fallback
                var instansi = suratData.Instansi;
                if (instansi == null || !instansi.IsValid())
                {
                    _logger.LogWarning("Data Instansi tidak lengkap untuk membuat PDF Domisili Instansi. Surat ID: {SuratId}. Menggunakan data default.",
                        suratData.ID_Surat);
                    instansi = new Instansi
                    {
                        NamaInstansi = "[Nama Instansi Tidak Ditemukan]",
                        AlamatInstansi = "[Alamat Instansi Tidak Ditemukan]"
                    };
                    suratData.Instansi = instansi;
                }

                var desa = suratData.Desa;
                if (desa == null || string.IsNullOrWhiteSpace(desa.NamaDesa) ||
                    string.IsNullOrWhiteSpace(desa.Kecamatan) || string.IsNullOrWhiteSpace(desa.Kabupaten))
                {
                    _logger.LogError("Data Desa tidak lengkap untuk membuat PDF Domisili Instansi. Surat ID: {SuratId}", suratData.ID_Surat);
                    throw new InvalidOperationException("Data Desa tidak lengkap.");
                }

                _logger.LogInformation("Rendering content for Domisili Instansi: NamaInstansi={NamaInstansi}, NomorSurat={NomorSurat}",
                    instansi.NamaInstansi, suratData.NomorSurat);

                badan.Paragraf("Yang bertanda tangan di bawah ini :", jarakBawah: 10);

                badan.TabelFormulir(
                [
                    ("Nama", suratData.NamaPejabatPenandatangan ?? "[Nama Pejabat]"),
                    ("Jabatan", $"{suratData.PejabatPenandatangan ?? "Pejabat"} {suratData.Desa?.NamaDesa ?? "[Nama Desa]"}"),
                ]);

                badan.Paragraf("Dengan ini menerangkan bahwa :", jarakBawah: 10);

                // Format Alamat Instansi dengan menyertakan Desa, Kecamatan, Kabupaten
                string alamatLengkapInstansi = $"{instansi.AlamatInstansi} " + // Pemeriksaan null sudah dilakukan di atas
                                             $"Desa {desa.NamaDesa} " +
                                             $"\nKecamatan {desa.Kecamatan} " +
                                             $"Kab. {desa.Kabupaten}" +
                                             $"\nKodepos {desa.Kodepos}";

                // Nama instansi dicetak kapital + tebal, seperti tabel formulir desa.
                badan.TabelFormulir(
                [
                    ("Nama Instansi/Lembaga", instansi.NamaInstansi),
                    ("Alamat Instansi/Lembaga", alamatLengkapInstansi),
                ],
                labelTebal: ["Nama Instansi/Lembaga"]);

                string? keteranganFinal = suratData.Keterangan;

                if (string.IsNullOrWhiteSpace(keteranganFinal))
                {
                    _logger.LogWarning("Keterangan kosong untuk Surat ID: {SuratId}. Menggunakan fallback keterangan internal.", suratData.ID_Surat);
                    keteranganFinal = $"Adalah benar domisili instansi tersebut diatas berada di wilayah administratif Desa {desa.NamaDesa} Kecamatan {desa.Kecamatan} Kabupaten {desa.Kabupaten}.";
                }

                // Indentasi 60pt untuk seluruh baris (indent 30 + padding kiri 30 pada bentuk lama).
                badan.Paragraf(keteranganFinal,
                    rata: Rata.Justify,
                    indentKiri: 60,
                    jarakAtas: 15,
                    jarakBawah: 20);

                _logger.LogInformation("Keterangan ditambahkan ke PDF: {Keterangan}", keteranganFinal);

                badan.Paragraf("Demikian surat keterangan ini dibuat dengan sebenarnya dan untuk dipergunakan sebagaimana keperluannya.",
                    rata: Rata.Justify,
                    jarakBawah: 20);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menambahkan konten PDF untuk Domisili Instansi. Nama Instansi={NamaInstansi}", suratData.Instansi?.NamaInstansi ?? "N/A");
                throw; // Lempar kembali galat agar bisa ditangani oleh pemanggil
            }
        }
    }
}
