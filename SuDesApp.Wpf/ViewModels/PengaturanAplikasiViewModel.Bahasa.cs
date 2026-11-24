using System;
using System.Collections.ObjectModel;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Utilities;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class PengaturanAplikasiViewModel : ObservableObject
    {
        // =====================================================================
        // Bahasa tampilan.
        //
        // Pilihan bahasa disimpan di preferensi pengguna (login_prefs.json) lalu
        // dibaca Lokalisasi saat halaman dibuka. Barisnya dipetakan lewat kunci,
        // jadi judul dan penyesuaian teks lain ikut berpindah bila pengguna
        // mengganti bahasa — tanpa menambah berkas sumber daya eksternal.
        // =====================================================================

        /// <summary>Baris pilihan yang nilainya berupa daftar teks (saat ini: bahasa).</summary>
        public ObservableCollection<SettingSelectionRowVM> RowsPilihan { get; } = new();

        private string _selectedBahasa = AppPreferenceStore.GetBahasa();

        /// <summary>Bahasa tampilan yang sedang dipakai ("id" atau "en").</summary>
        public string SelectedBahasa
        {
            get => _selectedBahasa;
            set
            {
                if (SetProperty(ref _selectedBahasa, value))
                {
                    Lokalisasi.SetCurrent(value);
                    try
                    {
                        AppPreferenceStore.SetBahasa(value);
                    }
                    catch
                    {
                        // Preferensi non-kritis — kegagalan menyimpan diabaikan.
                    }
                }
            }
        }

        /// <summary>Bangun baris pilihan bahasa dan sambungkan penyimpanannya.</summary>
        private void SiapkanBahasa()
        {
            var baris = new SettingSelectionRowVM(
                "Settings.Bahasa.Judul", "Settings.Bahasa.Keterangan", SelectedBahasa)
            {
                Diubah = nilai => SelectedBahasa = nilai
            };

            RowsPilihan.Add(baris);
        }

        /// <summary>Identitas versi yang dipakai baris "Informasi" halaman ini.</summary>
        public string VersiAplikasi =>
            $"{Lokalisasi.Get("Settings.Versi.Judul")}: {IdentitasAplikasi.VersiSingkat}";
    }
}
