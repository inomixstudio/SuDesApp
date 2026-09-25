using System.Collections.Generic;
using SuDesApp.Wpf.Mvvm;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>
    /// Satu entri navigasi bagian pada halaman Panduan WhatsApp.
    /// </summary>
    public class PanduanWaBagianViewModel : ObservableObject
    {
        private bool _isTerpilih;

        public PanduanWaBagianViewModel(string kunci, string ikon, string label, string deskripsiStatus)
        {
            Kunci = kunci;
            Ikon = ikon;
            Label = label;
            DeskripsiStatus = deskripsiStatus;
        }

        /// <summary>Kunci bagian — menentukan template konten mana yang dirender.</summary>
        public string Kunci { get; }

        public string Ikon { get; }

        public string Label { get; }

        /// <summary>Teks status bar saat bagian ini aktif.</summary>
        public string DeskripsiStatus { get; }

        public bool IsTerpilih
        {
            get => _isTerpilih;
            set => SetProperty(ref _isTerpilih, value);
        }
    }

    /// <summary>
    /// ViewModel halaman Panduan WhatsApp — konten bersifat statis (panduan
    /// setup WhatsApp Cloud API + webhook), tidak butuh dependensi.
    /// Halaman dibagi menjadi beberapa bagian; hanya bagian aktif yang
    /// dirender agar membuka halaman terasa ringan (XAML kompleks tidak
    /// dibangun sekaligus).
    /// </summary>
    public class PanduanWaViewModel : ObservableObject
    {
        private PanduanWaBagianViewModel _bagianAktif;

        public PanduanWaViewModel()
        {
            // Catatan penting TIDAK menjadi bagian navigasi — kartunya tampil tetap
            // di bawah konten tanpa perlu diklik (lihat PanduanWaView).
            Bagian = new List<PanduanWaBagianViewModel>
            {
                new("Gambaran", "\U0001F5FA\uFE0F", "Gambaran besar",
                    "Panduan WhatsApp — gambaran besar layanan surat online otomatis."),
                new("A", "\U0001F4DD", "A \u2022 Daftar Meta",
                    "Panduan WhatsApp — Bagian A: daftar Meta & aktifkan WhatsApp (\u2248 30 menit, sekali saja)."),
                new("B", "\u2699\uFE0F", "B \u2022 Pasang token",
                    "Panduan WhatsApp — Bagian B: pasang token di aplikasi (\u2248 1 menit)."),
                new("C", "\U0001F310", "C \u2022 Webhook",
                    "Panduan WhatsApp — Bagian C: webhook untuk pesan masuk warga (sekali setup)."),
                new("D", "\U0001F4CB", "D \u2022 Form & Sheet",
                    "Panduan WhatsApp — Bagian D: Google Formulir & Sheet jawaban (\u2248 2 menit, otomatis)."),
                new("E", "\u2705", "E \u2022 Uji alur",
                    "Panduan WhatsApp — Bagian E: uji alur penuh dari sisi warga."),
            };

            _bagianAktif = Bagian[0];
            Bagian[0].IsTerpilih = true;
        }

        public IReadOnlyList<PanduanWaBagianViewModel> Bagian { get; }

        /// <summary>Bagian yang sedang ditampilkan — menentukan template konten.</summary>
        public PanduanWaBagianViewModel BagianAktif
        {
            get => _bagianAktif;
            private set => SetProperty(ref _bagianAktif, value);
        }

        /// <summary>Dipanggil chip navigasi (code-behind) saat bagian dipilih.</summary>
        public void PilihBagian(PanduanWaBagianViewModel bagian)
        {
            if (ReferenceEquals(bagian, BagianAktif))
            {
                return;
            }

            foreach (var item in Bagian)
            {
                item.IsTerpilih = ReferenceEquals(item, bagian);
            }

            BagianAktif = bagian;
        }
    }
}
