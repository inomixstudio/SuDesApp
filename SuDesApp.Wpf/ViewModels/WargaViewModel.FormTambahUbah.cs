using System;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SudesExPlus;
using SudesExPlus.Style;
using System.Drawing;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.GeneratorPdf;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class WargaViewModel : ObservableObject
    {
        // ----- Form tambah / ubah -----
        private int _formIdWarga;
        private string? _alamatDetailFormTersimpan;

        public bool IsFormTerbuka
        {
            get => _isFormTerbuka;
            private set => SetProperty(ref _isFormTerbuka, value);
        }
        private bool _isFormTerbuka;
        private bool _isModeUbah;

        public bool IsModeUbah
        {
            get => _isModeUbah;
            private set => SetProperty(ref _isModeUbah, value);
        }

        public string JudulForm => IsModeUbah ? "Ubah Data Warga" : "Tambah Data Warga";

        /// <summary>Nomor HP kepala keluarga: dipakai form dan kartu keluarga.</summary>
        private string _formNoKK = string.Empty;
        public string FormNoKK
        {
            get => _formNoKK;
            set => SetProperty(ref _formNoKK, value ?? string.Empty);
        }

        private string _formNik = string.Empty;
        public string FormNik
        {
            get => _formNik;
            set { if (SetProperty(ref _formNik, value ?? string.Empty)) SegarkanPerintah(); }
        }

        private string _formNama = string.Empty;
        public string FormNama
        {
            get => _formNama;
            set => SetProperty(ref _formNama, value ?? string.Empty);
        }

        private string _formTempatLahir = string.Empty;
        public string FormTempatLahir
        {
            get => _formTempatLahir;
            set => SetProperty(ref _formTempatLahir, value ?? string.Empty);
        }

        private string _formTanggalLahir = string.Empty;
        public string FormTanggalLahir
        {
            get => _formTanggalLahir;
            set => SetProperty(ref _formTanggalLahir, value ?? string.Empty);
        }

        private string _formJenisKelamin = string.Empty;
        public string FormJenisKelamin
        {
            get => _formJenisKelamin;
            set => SetProperty(ref _formJenisKelamin, value ?? string.Empty);
        }

        private string _formAgama = string.Empty;
        public string FormAgama
        {
            get => _formAgama;
            set => SetProperty(ref _formAgama, value ?? string.Empty);
        }

        private string _formStatusPerkawinan = string.Empty;
        public string FormStatusPerkawinan
        {
            get => _formStatusPerkawinan;
            set => SetProperty(ref _formStatusPerkawinan, value ?? string.Empty);
        }

        /// <summary>Kedudukan dalam Kartu Keluarga: Kepala Keluarga/Istri/Anak/Cucu/Family Lain.</summary>
        private string _formStatusKeluarga = string.Empty;
        public string FormStatusKeluarga
        {
            get => _formStatusKeluarga;
            set => SetProperty(ref _formStatusKeluarga, value ?? string.Empty);
        }

        private string _formGolonganDarah = string.Empty;
        public string FormGolonganDarah
        {
            get => _formGolonganDarah;
            set => SetProperty(ref _formGolonganDarah, value ?? string.Empty);
        }

        private string _formPekerjaan = string.Empty;
        public string FormPekerjaan
        {
            get => _formPekerjaan;
            set => SetProperty(ref _formPekerjaan, value ?? string.Empty);
        }

        private string _formPendidikan = string.Empty;
        public string FormPendidikan
        {
            get => _formPendidikan;
            set => SetProperty(ref _formPendidikan, value ?? string.Empty);
        }

        private string _formNamaAyah = string.Empty;
        public string FormNamaAyah
        {
            get => _formNamaAyah;
            set => SetProperty(ref _formNamaAyah, value ?? string.Empty);
        }

        private string _formNamaIbu = string.Empty;
        public string FormNamaIbu
        {
            get => _formNamaIbu;
            set => SetProperty(ref _formNamaIbu, value ?? string.Empty);
        }

        private string _formNomorHP = string.Empty;
        public string FormNomorHP
        {
            get => _formNomorHP;
            set => SetProperty(ref _formNomorHP, value ?? string.Empty);
        }

        private string _formDusun = string.Empty;
        public string FormDusun
        {
            get => _formDusun;
            set => SetProperty(ref _formDusun, value ?? string.Empty);
        }

        private string _formRT = string.Empty;
        public string FormRT
        {
            get => _formRT;
            set => SetProperty(ref _formRT, value ?? string.Empty);
        }

        private string _formRW = string.Empty;
        public string FormRW
        {
            get => _formRW;
            set => SetProperty(ref _formRW, value ?? string.Empty);
        }

        private string _formStatusWarga = StatusWargaTipe.Aktif;
        public string FormStatusWarga
        {
            get => _formStatusWarga;
            set => SetProperty(ref _formStatusWarga, value ?? string.Empty);
        }

        private string _formTanggalStatus = string.Empty;
        public string FormTanggalStatus
        {
            get => _formTanggalStatus;
            set => SetProperty(ref _formTanggalStatus, value ?? string.Empty);
        }

        private string _formKeterangan = string.Empty;
        public string FormKeterangan
        {
            get => _formKeterangan;
            set => SetProperty(ref _formKeterangan, value ?? string.Empty);
        }

        private string _formGalat = string.Empty;
        public string FormGalat
        {
            get => _formGalat;
            private set => SetProperty(ref _formGalat, value);
        }


    }
}
