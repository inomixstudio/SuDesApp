using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SudesExPlus;
using SudesExPlus.Style;
using SuDesApp.Configuration;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Views;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class RegisterSuratViewModel : ObservableObject
    {
        private string GetNamaDisplay(SuratData surat)
        {
            if (surat.Instansi != null && !string.IsNullOrEmpty(surat.Instansi.NamaInstansi))
                return surat.Instansi.NamaInstansi;
            // Surat dari Template Surat tidak memakai data kependudukan: nama penerimanya
            // dibaca dari isian surat itu sendiri (mis. kolom "Nama" atau "Ditujukan Kepada").
            if (TemplateSuratTercatat.DariTemplateSurat(surat))
            {
                string namaTemplate = TemplateSuratTercatat.NamaPenerimaTampil(surat);
                return namaTemplate.Length > 0 ? namaTemplate : "-";
            }

            if (surat.Warga != null && !string.IsNullOrEmpty(surat.Warga.Nama))
                return surat.Warga.Nama;
            return "-";
        }

        private string GetTTLDisplay(SuratData surat)
        {
            // Surat dari Template Surat: TTL datang dari kolom isian templatenya.
            if (TemplateSuratTercatat.DariTemplateSurat(surat))
            {
                string ttlTemplate = TemplateSuratTercatat.TempatTanggalLahirTampil(surat);
                return ttlTemplate.Length > 0 ? ttlTemplate : "-";
            }

            if (surat.Warga == null) return "-";
            return $"{surat.Warga.TempatLahir ?? "-"}, {surat.Warga.TanggalLahir ?? "-"}";
        }

        private string GetJenisKelaminDisplay(SuratData surat)
        {
            if (TemplateSuratTercatat.DariTemplateSurat(surat))
            {
                string jkTemplate = TemplateSuratTercatat.JenisKelaminTampil(surat);
                return jkTemplate.Length > 0 ? jkTemplate : "-";
            }

            if (surat.Warga == null || string.IsNullOrEmpty(surat.Warga.JenisKelamin)) return "-";
            return surat.Warga.JenisKelamin.Substring(0, 1).ToUpper();
        }

        private string GetAlamatDisplay(SuratData surat)
        {
            // Surat dari Template Surat menyimpan alamat bebas (bukan dusun/desa/
            // kecamatan) di dalam isian suratnya.
            if (TemplateSuratTercatat.DariTemplateSurat(surat))
            {
                string alamatTemplate = TemplateSuratTercatat.AlamatPenerimaTampil(surat);
                return alamatTemplate.Length > 0 ? alamatTemplate : "-";
            }

            if (surat.Warga == null)
                return surat.Instansi?.AlamatInstansi ?? "-";

            var parts = new[]
            {
                surat.Warga.Dusun,
                surat.Warga.Desa,
                surat.Warga.Kecamatan,
                surat.Warga.Kabupaten
            }.Where(p => !string.IsNullOrWhiteSpace(p));

            return parts.Any() ? string.Join(", ", parts) : "-";
        }

        private string GetJenisSuratLabel(SuratData surat)
        {
            return surat.NamaJenis?.ToUpperInvariant() switch
            {
                "NTCR_N1" => "N1 - Surat Pengantar Nikah",
                "NTCR_N2" => "N2 - Permohonan Kehendak Nikah",
                "NTCR_N3" => "N3 - Permohonan Pencatatan Isbat",
                "NTCR_N4" => "N4 - Persetujuan Calon Pengantin",
                "NTCR_N5" => "N5 - Surat Izin Orang Tua",
                "NTCR_N6" => "N6 - Ket. Kematian Suami/Istri",
                "NTCR_N8" => "N8 - Ket. Numpang Nikah",
                "REKENING_KORAN" => "Permohonan Rekening Koran",
                "TEMPLATE_SURAT" => GetLabelTemplateSurat(surat),
                "BEDANAMA" => "Surat Ket. Beda Data",
                "SKD_UMUM" => "SKD Belum Menikah (KUA)",
                "DOMISILI_WARGA" => "Domisili Warga",
                "INSTANSI" => "Domisili Instansi/Lembaga",
                "SKU" => "SKU (Surat Ket. Usaha)",
                "PENGANTAR_SKCK" => "Pengantar SKCK",
                "IZIN_ORTU" => "Izin Suami/Orang Tua",
                "GARAPAN_SAWAH" => "Ket. Garapan Sawah",
                "KEMATIAN" => "Surat Kematian",
                "SKTM" => "SKTM (Surat Ket. Tidak Mampu)",
                "SK_BELUM_TERBIT" => "Surat Ket. Belum Terbit / Sudah Terbit",
                // Tiga jenis di bawah pernah tercetak MENTAH di register
                // ("KENAL LAHIR", "AHLI WARIS", "TINGGAL SEMENTARA") karena
                // masuk cabang bawaan NamaJenis.Replace("_", " "). Register dan
                // arsip adalah dokumen resmi, jadi nama jenisnya harus sama
                // seperti yang tampil di menu Buat Surat Baru.
                "KENAL_LAHIR" => "Ket. Kenal Lahir",
                "AHLI_WARIS" => "Surat Ket. Ahli Waris",
                "IJIN_TINGGAL" => "Ket. Tinggal Sementara",
                _ => surat.NamaJenis?.Replace("_", " ") ?? "-"
            };
        }

        /// <summary>
        /// Label jenis surat untuk surat dari Template Surat: menyebut nama templatenya
        /// (dibaca dari payload surat) supaya daftar register tetap informatif.
        /// </summary>
        private static string GetLabelTemplateSurat(SuratData surat)
        {
            var payload = TemplateSuratTercatat.FromJson(surat?.AdditionalData);
            string nama = payload?.NamaTemplate?.Trim() ?? string.Empty;

            return nama.Length == 0 ? "Template Surat" : $"Template: {nama}";
        }

        private void UpdateCommandStates()
        {
            OnPropertyChanged(nameof(CanPreviousPage));
            OnPropertyChanged(nameof(CanNextPage));
            OnPropertyChanged(nameof(HasData));
            (PreviousPageCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (NextPageCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (ExportCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (PrintRegisterCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
    }
}
