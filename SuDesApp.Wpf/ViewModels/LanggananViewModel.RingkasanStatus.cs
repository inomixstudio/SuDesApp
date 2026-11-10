using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using SuDesApp.ControlSurat;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class LanggananViewModel : ObservableObject
    {
        // =======================================================================
        // Ringkasan status untuk kartu status (lencana header + petak KPI).
        // Semua turunan dari status terakhir yang sudah diverifikasi tanda
        // tangannya — tidak ada angka yang dihitung ulang di sisi tampilan.
        // =======================================================================

        /// <summary>
        /// Ringkasan status terakhir yang sudah diverifikasi tanda tangannya —
        /// satu-satunya sumber kalimat banner dan angka petak KPI di kartu Status
        /// Langganan. Penyusunannya ada di Core (<see cref="RingkasanLangganan"/>),
        /// jadi halaman ini tidak menyimpan aturan lisensi sendiri dan tanggal di
        /// banner tidak bisa berbeda dari tanggal di petak.
        /// </summary>
        private RingkasanLangganan _ringkasan = RingkasanLangganan.BelumDiperiksa;

        /// <summary>
        /// Label keadaan untuk lencana di header. Trial server memakai token sah,
        /// tetapi tetap tampil "Trial" (bukan "Full Service") karena desa belum
        /// membayar apa pun — aturannya di <see cref="PenjagaLisensi.KeadaanTampil"/>.
        /// </summary>
        public string KeadaanLabel => PenjagaLisensi.LabelKeadaan(KeadaanTampil);

        /// <summary>Nada lencana keadaan: 1 aktif (hijau), 2 Demo/Trial (kuning), 3 baca-saja (merah).</summary>
        public int KeadaanTone => PenjagaLisensi.NadaKeadaan(KeadaanTampil);

        /// <summary>Keadaan yang ditampilkan (trial server dibaca sebagai Trial).</summary>
        private SuDesApp.Services.KeadaanLisensi KeadaanTampil =>
            PenjagaLisensi.KeadaanTampil(_penjaga.Keadaan, _penjaga.Status?.Paket);

        /// <summary>True bila aplikasi sedang baca-saja karena langganan benar-benar berakhir.</summary>
        public bool BacaSaja => _penjaga.BacaSaja;

        /// <summary>Pesan baca-saja — sama persis dengan yang dipakai gerbang fitur.</summary>
        public string PesanBacaSaja => PenjagaLisensi.PesanAksesDikunci;

        /// <summary>
        /// Nama paket aktif dalam bahasa manusia ("Tahunan", "6 Bulan", "Bulanan",
        /// "Trial 30 hari"). Pemetaan kode → nama ada di Core, tempat kode "trial"
        /// — yang tidak punya kartu karena gratis — tetap terbaca sebagai nama
        /// paket, bukan kode mentah.
        /// </summary>
        public string PaketNama => _ringkasan.PaketNama;

        /// <summary>
        /// Batas SAH efektif dalam waktu lokal (tanggal bayar ATAU tanggal cek
        /// terakhir + masa tenggang), atau "—" bila belum diketahui.
        ///
        /// Diambil dari <see cref="RingkasanLangganan"/> supaya petak ini tidak
        /// bisa memakai tanggal yang berbeda dari kalimat banner di atasnya.
        /// </summary>
        public string BerlakuSampaiTampil => _ringkasan.SahSampaiTampil;

        /// <summary>Sisa hari sebelum perlu menyambung internet lagi, atau "—".</summary>
        public string SisaHariTampil => _ringkasan.SisaHariTampil;

        /// <summary>Waktu pemeriksaan server terakhir (waktu lokal), atau "—".</summary>
        public string TerakhirDiperiksaTampil => _ringkasan.TerakhirDiperiksaTampil;

        /// <summary>
        /// Tombol trial tampil hanya bila langganan TIDAK sedang aktif — yang
        /// sudah Full Service tidak perlu trial, dan server pun akan menolak.
        /// Sebelum ada status terbaca, tombol tampil.
        /// </summary>
        public bool TampilTombolTrial => _ringkasan.TampilTombolTrial && !_penjaga.TrialPernahDimulai;

        /// <summary>
        /// True selama trial berjalan (lokal maupun server). Kartu Status memakai
        /// ini untuk memasang tombol "Bayar / Berlangganan" langsung di samping
        /// Cek Status — supaya desa yang sedang uji coba tidak perlu mencari
        /// tombol bayar di kartu paket di bawahnya. Perintahnya tetap
        /// <see cref="BayarLanggananCommand"/> yang sama, jadi aturan pembayaran
        /// (termasuk penjagaan kode aktivasi) tinggal satu salinan.
        /// </summary>
        public bool SedangTrial => _ringkasan.SelamaTrial;

        private bool _langgananSibuk;

        /// <summary>Penanda sedang cek/membuat tagihan (menonaktifkan tombol).</summary>
        public bool LanggananSibuk
        {
            get => _langgananSibuk;
            private set
            {
                if (SetProperty(ref _langgananSibuk, value))
                {
                    CekLanggananCommand.RaiseCanExecuteChanged();
                    SimpanLanggananCommand.RaiseCanExecuteChanged();
                    TempelKodeCommand.RaiseCanExecuteChanged();
                    HapusKodeCommand.RaiseCanExecuteChanged();
                    BayarLanggananCommand.RaiseCanExecuteChanged();
                    MulaiTrialCommand.RaiseCanExecuteChanged();
                    SimpanServerCommand.RaiseCanExecuteChanged();
                    HubungkanCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public AsyncRelayCommand CekLanggananCommand { get; }
        public AsyncRelayCommand SimpanLanggananCommand { get; }
        public RelayCommand TempelKodeCommand { get; }
        public RelayCommand HapusKodeCommand { get; }
        public AsyncRelayCommand BayarLanggananCommand { get; }
        public AsyncRelayCommand MulaiTrialCommand { get; }
        public AsyncRelayCommand SimpanServerCommand { get; }
        public AsyncRelayCommand HubungkanCommand { get; }
        public RelayCommand BukaKunciServerCommand { get; }


    }
}
