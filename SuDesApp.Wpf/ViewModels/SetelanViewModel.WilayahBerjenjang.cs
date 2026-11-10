using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Microsoft.Extensions.Logging;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.GeneratorPdf;
using SuDesApp.Utilities;
using SuDesApp.Wilayah;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class SetelanViewModel : ObservableObject
    {
        // =================================================================
        // Pilihan wilayah berjenjang: provinsi → kabupaten/kota → kecamatan →
        // desa/kelurahan, lalu kode pos saran. Pengguna cukup memilih, dan nama
        // wilayah pada kop surat tidak lagi bergantung pada ketikan bebas.
        // =================================================================

        // Daftar pilihan sengaja berupa daftar tetap yang diganti utuh setiap kali
        // berubah, BUKAN koleksi yang isinya dikosongkan lalu diisi ulang: ComboBox
        // mengikat ItemsSource ke daftar ini, dan mengubah isi koleksi dari dalam
        // peristiwa pilihan ComboBox sendiri membuat WPF menyegarkan tampilannya di
        // tengah perubahan — itu menggagalkan aplikasi (lihat SegarkanDaftarWilayah).
        private IReadOnlyList<WilayahDaerah> _daftarProvinsi = Array.Empty<WilayahDaerah>();
        private IReadOnlyList<WilayahDaerah> _daftarKabupaten = Array.Empty<WilayahDaerah>();
        private IReadOnlyList<WilayahDaerah> _daftarKecamatan = Array.Empty<WilayahDaerah>();
        private IReadOnlyList<WilayahDaerah> _daftarDesa = Array.Empty<WilayahDaerah>();

        /// <summary>Provinsi yang bisa dipilih; Jawa Barat paling atas.</summary>
        public IReadOnlyList<WilayahDaerah> DaftarProvinsi
        {
            get => _daftarProvinsi;
            private set => SetProperty(ref _daftarProvinsi, value);
        }

        /// <summary>Kabupaten/kota pada provinsi terpilih — seluruh Jawa Barat saat halaman dibuka.</summary>
        public IReadOnlyList<WilayahDaerah> DaftarKabupaten
        {
            get => _daftarKabupaten;
            private set => SetProperty(ref _daftarKabupaten, value);
        }

        /// <summary>Kecamatan pada kabupaten/kota terpilih; kosong bila belum ada pilihan.</summary>
        public IReadOnlyList<WilayahDaerah> DaftarKecamatan
        {
            get => _daftarKecamatan;
            private set => SetProperty(ref _daftarKecamatan, value);
        }

        /// <summary>Desa/kelurahan pada kecamatan terpilih; kosong bila belum ada pilihan.</summary>
        public IReadOnlyList<WilayahDaerah> DaftarDesa
        {
            get => _daftarDesa;
            private set => SetProperty(ref _daftarDesa, value);
        }

        /// <summary>Penahan pengulangan: pengikatan yang menulis balik pilihan kosong
        /// tidak boleh memicu penyegaran kedua di tengah penyegaran pertama.</summary>
        private bool _sedangSegarkanWilayah;

        /// <summary>Permintaan penyegaran yang datang saat penyegaran berjalan.</summary>
        private bool _segarkanWilayahDiulang;

        /// <summary>Benar bila berkas data wilayah bawaan aplikasi terbaca.</summary>
        public bool WilayahTersedia => _pemilih.AdaData;

        private bool _modePilihWilayah = true;

        /// <summary>Benar bila wilayah dipilih dari daftar (bukan diketik manual).</summary>
        public bool ModePilihWilayah
        {
            get => _modePilihWilayah;
            private set
            {
                if (SetProperty(ref _modePilihWilayah, value))
                {
                    OnPropertyChanged(nameof(ModeIsiManual));
                }
            }
        }

        /// <summary>
        /// Saklar «isi manual» untuk desa yang wilayahnya tidak ada di daftar, atau
        /// data lama yang tidak dikenali data wilayah. Saat dicentang, isian nama
        /// desa/kecamatan/kabupaten kembali bisa diketik bebas seperti sebelumnya.
        /// </summary>
        public bool ModeIsiManual
        {
            get => !_modePilihWilayah;
            set => UbahModeWilayah(pakaiManual: value);
        }

        public WilayahDaerah? ProvinsiTerpilih
        {
            get => _pemilih.ProvinsiTerpilih;
            set
            {
                if (_pemilih.PilihProvinsi(value))
                {
                    SinkronkanIsianDariPilihan();
                }
            }
        }

        public WilayahDaerah? KabupatenTerpilih
        {
            get => _pemilih.KabupatenTerpilih;
            set
            {
                if (_pemilih.PilihKabupaten(value))
                {
                    SinkronkanIsianDariPilihan();
                }
            }
        }

        public WilayahDaerah? KecamatanTerpilih
        {
            get => _pemilih.KecamatanTerpilih;
            set
            {
                if (_pemilih.PilihKecamatan(value))
                {
                    SinkronkanIsianDariPilihan();
                }
            }
        }

        public WilayahDaerah? DesaTerpilih
        {
            get => _pemilih.DesaTerpilih;
            set
            {
                if (_pemilih.PilihDesa(value))
                {
                    SinkronkanIsianDariPilihan();
                }
            }
        }

        /// <summary>Keterangan asal data wilayah — acuan dan tanggal sumbernya.</summary>
        public string InfoDataWilayah => !_pemilih.AdaData
            ? _dataWilayah.PesanMasalah
            : $"Data wilayah {_dataWilayah.Info.Acuan}: {_dataWilayah.Info.JumlahBaris.ToString("N0", CultureInfo.GetCultureInfo("id-ID"))} wilayah " +
              $"(sumber diperbarui {_dataWilayah.Info.SumberDiperbarui}).";

        private string _catatanPilihanWilayah = string.Empty;

        /// <summary>Keterangan keadaan pilihan: dikenali, ambigu, atau perlu dipilih.</summary>
        public string CatatanPilihanWilayah
        {
            get => _catatanPilihanWilayah;
            private set
            {
                if (SetProperty(ref _catatanPilihanWilayah, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(AdaCatatanPilihanWilayah));
                }
            }
        }

        public bool AdaCatatanPilihanWilayah => _catatanPilihanWilayah.Length > 0;

        /// <summary>Ringkasan rantai wilayah yang sudah lengkap (desa sampai provinsi).</summary>
        public string RingkasanPilihanWilayah
        {
            get
            {
                var rantai = _pemilih.Rantai;
                if (rantai == null)
                {
                    return string.Empty;
                }

                string kodepos = _pemilih.KodeposSaran.Length > 0
                    ? $" Kode pos saran: {_pemilih.KodeposSaran}."
                    : string.Empty;

                return $"{rantai.Desa.NamaLengkap}, {rantai.Kecamatan.NamaLengkap}, " +
                       $"{rantai.Kabupaten.NamaLengkap}, Provinsi {rantai.Provinsi.Nama}.{kodepos}";
            }
        }

        public bool AdaRingkasanPilihanWilayah => _pemilih.Rantai != null;

        /// <summary>Keterangan kolom kode pos: hasil saran data wilayah atau isian bebas.</summary>
        public string InfoKodepos => _pemilih.DesaTerpilih is { Berkodepos: true } desa &&
                                     string.Equals(Kodepos?.Trim(), desa.Kodepos, StringComparison.Ordinal)
            ? $"Kode pos {desa.Kodepos} terisi otomatis dari data wilayah untuk {desa.NamaLengkap} — " +
              "periksa dan ubah bila kantor desa memakai kode pos lain."
            : "5 digit angka — boleh dikosongkan.";

        /// <summary>
        /// Siapkan pilihan wilayah dari data desa yang tersimpan. Dipanggil setiap
        /// kali halaman dibuka: data lama berupa teks bebas dicocokkan dengan data
        /// wilayah bawaan, dan pemuatannya dijalankan di thread latar (± 90 ribu
        /// baris) supaya jendela tidak tersendat.
        /// </summary>
        private async Task SiapkanPilihanWilayahAsync(DesaData snapshot)
        {
            // Penyusunan hasil (membaca ± 90 ribu baris data wilayah) berjalan di
            // utas latar supaya jendela tidak tersendat. Penerapannya ke halaman
            // SELALU di utas halaman — lihat JalankanDiUtasHalamanAsync.
            var hasil = await Task.Run(() =>
            {
                _ = _pemilih.Provinsi;   // memicu pemuatan berkas data wilayah
                return _pemilih.AdaData
                    ? _pemilih.MuatDariNama(snapshot.NamaDesa, snapshot.Kecamatan, snapshot.Kabupaten)
                    : new HasilPencocokanWilayah(StatusPencocokanWilayah.TanpaData, null, 0);
            }).ConfigureAwait(false);

            await JalankanDiUtasHalamanAsync(() => TerapkanHasilPencocokanWilayah(hasil, snapshot));
        }

        /// <summary>
        /// Pasang hasil pencocokan wilayah ke isian halaman: mode pilih/manual,
        /// catatan keadaan, pilihan berjenjang, dan lambang kabupaten. Seluruh
        /// perubahan keadaan yang diikat XAML ada di sini, jadi inilah satu-satunya
        /// tempat yang mengubah tampilan pilihan wilayah.
        /// </summary>
        private void TerapkanHasilPencocokanWilayah(HasilPencocokanWilayah hasil, DesaData snapshot)
        {
            // Bila pencocokan nama ambigu (mis. Kabupaten vs Kota Bandung) tetapi
            // database menyimpan kode Kemendagri kabupaten, gunakan kode tersebut
            // untuk memilih kabupaten yang benar secara deterministik. Cari di data
            // wilayahnya sendiri, BUKAN pada `_pemilih.Kabupaten`: hasil Ambigu
            // tidak pernah memilih apa pun, jadi daftar tingkat bawah _pemilih masih
            // kosong saat halaman pertama dibuka dan pencarian di sana selalu gagal.
            if (hasil.Status == StatusPencocokanWilayah.Ambigu &&
                !string.IsNullOrWhiteSpace(snapshot.KodeKabupaten))
            {
                var kab = _dataWilayah.Ambil(snapshot.KodeKabupaten.Trim());
                if (kab is { Jenis: WilayahJenis.Kabupaten or WilayahJenis.Kota } && _pemilih.PilihKabupaten(kab))
                {
                    // Pilihan kecamatan/desa ikut dicoba lagi dari nama tersimpan.
                    var kec = _pemilih.Kecamatan.FirstOrDefault(k =>
                        WilayahPencocokan.Normalisasi(k.Nama) ==
                        WilayahPencocokan.Normalisasi(snapshot.Kecamatan));
                    if (kec != null) _pemilih.PilihKecamatan(kec);
                    var desa = _pemilih.Desa.FirstOrDefault(d =>
                        WilayahPencocokan.Normalisasi(d.Nama) ==
                        WilayahPencocokan.Normalisasi(snapshot.NamaDesa));
                    if (desa != null) _pemilih.PilihDesa(desa);

                    hasil = new HasilPencocokanWilayah(
                        StatusPencocokanWilayah.Ditemukan, _pemilih.Rantai, 1);
                }
            }

            SegarkanDaftarWilayah();

            switch (hasil.Status)
            {
                case StatusPencocokanWilayah.Ditemukan:
                    ModePilihWilayah = true;

                    // Wilayah yang tersimpan dikenali: jenis kabupaten/kotanya ikut
                    // disiapkan supaya kop surat memakai kata yang benar. Data desa lama
                    // belum menyimpan kolom ini, jadi barulah tersimpan setelah pengguna
                    // menekan Simpan — karena itu keadaannya diberitahukan di sini.
                    bool jenisBaru = SinkronkanJenisKabupatenDariPilihan();
                    CatatanPilihanWilayah = jenisBaru
                        ? "Wilayah desa yang tersimpan dikenali dari data wilayah bawaan — pilihan dan penulisan kop " +
                          "sudah disiapkan. Tekan Simpan agar jenis wilayahnya (Kabupaten/Kota) ikut tersimpan."
                        : "Wilayah desa yang tersimpan dikenali dari data wilayah bawaan — pilihan sudah disiapkan.";
                    break;

                case StatusPencocokanWilayah.Ambigu:
                    // Nama kembar (mis. Kabupaten dan Kota Bandung) tidak boleh dipilih
                    // otomatis: salah pilih di sini ikut tercetak pada semua surat.
                    ModePilihWilayah = true;
                    CatatanPilihanWilayah =
                        $"Ada {hasil.JumlahKandidat} wilayah bernama sama, jadi pilihan belum bisa ditentukan otomatis. " +
                        "Pilih kabupaten/kota dan kecamatan yang benar dari daftar.";
                    break;

                case StatusPencocokanWilayah.TidakDitemukan:
                    bool masihContoh =
                        DesaContoh.NilaiContoh(snapshot.NamaDesa, DesaContoh.NamaDesa, wajib: true) ||
                        DesaContoh.NilaiContoh(snapshot.Kecamatan, DesaContoh.Kecamatan, wajib: true) ||
                        DesaContoh.NilaiContoh(snapshot.Kabupaten, DesaContoh.Kabupaten, wajib: true);

                    if (masihContoh)
                    {
                        ModePilihWilayah = true;
                        CatatanPilihanWilayah =
                            "Pilih kabupaten/kota di Jawa Barat, lalu kecamatan, lalu desa/kelurahan dari daftar — " +
                            "nama wilayah dan kode pos terisi sendiri. Untuk provinsi lain, ubah dulu pilihan Provinsi.";
                    }
                    else
                    {
                        // Data desa nyata tetapi tidak ada di data wilayah (mis. wilayah hasil
                        // pemekaran setelah data dirilis). Isian lama tidak boleh diubah diam-diam.
                        ModeIsiManual = true;
                        CatatanPilihanWilayah =
                            "Data desa yang tersimpan tidak ditemukan di data wilayah bawaan, jadi isian manual dipakai. " +
                            "Hilangkan centang «Isi manual» untuk mencari wilayahnya pada daftar.";
                    }

                    break;

                default:
                    ModeIsiManual = true;
                    break;
            }

            // Aplikasi ini dipakai kantor desa di Jawa Barat: bila belum ada pilihan
            // sama sekali, provinsi Jawa Barat sudah ditetapkan sehingga seluruh
            // kabupaten/kota Jawa Barat langsung tersedia pada kotak Kabupaten/Kota —
            // pengguna tidak perlu memilih provinsi lebih dulu. Provinsi lain tetap
            // bisa dipilih, dan pilihan itu tidak pernah menimpa pilihan yang ada.
            if (ModePilihWilayah && _pemilih.PilihProvinsiDiutamakan())
            {
                SegarkanDaftarWilayah();
            }

            // Sinkronkan kode logo kabupaten dari kode yang tersimpan di database.
            // Bila pengguna sebelumnya memakai lambang kabupaten dan kode tersimpan
            // valid (kabupaten terpilih), pastikan kodeLogoKabupaten preference ikut
            // konsisten — supaya JalurLogoEfektif() menemukan berkas yang benar.
            var kabTerpilih = _pemilih.KabupatenTerpilih;
            if (kabTerpilih != null &&
                !string.IsNullOrWhiteSpace(snapshot.KodeKabupaten))
            {
                PengaturanCetak.SetKodeLogoKabupaten(kabTerpilih.Kode);
            }

            // Daftar lambang kabupaten ikut disiapkan. Pilih-otomatis diaktifkan
            // saat halaman dibuka supaya pengguna di Jawa Barat langsung mendapat
            // lambang kabupatennya tanpa harus memilih manual — kecuali bila sudah
            // punya logo unggahan sendiri (dijaga oleh BolehPilihOtomatisLambang).
            SegarkanLambangKabupaten(pilihOtomatis: true);
        }

        /// <summary>Pindah antara memilih dari daftar dan mengisi wilayah manual.</summary>
        private void UbahModeWilayah(bool pakaiManual)
        {
            if (pakaiManual == ModeIsiManual)
            {
                return;
            }

            ModePilihWilayah = !pakaiManual;

            if (pakaiManual)
            {
                CatatanPilihanWilayah =
                    "Isian manual dipakai: nama desa, kecamatan, dan kabupaten diketik sendiri. " +
                    "Hilangkan centang ini untuk memilih dari daftar wilayah.";
                return;
            }

            if (!_pemilih.AdaData)
            {
                ModeIsiManual = true;
                return;
            }

            // Kembali ke daftar: kenali dulu isian yang sedang ada supaya pengguna
            // tidak kehilangan data hanya karena berpindah mode.
            var hasil = _pemilih.MuatDariNama(NamaDesa, Kecamatan, Kabupaten);

            // Bila isian tidak dikenali, daftar dibuka dari Jawa Barat supaya
            // kabupaten/kota Jawa Barat langsung bisa dipilih tanpa memilih provinsi dulu.
            _pemilih.PilihProvinsiDiutamakan();
            SinkronkanJenisKabupatenDariPilihan();
            // Jenis lembaga juga: wilayah yang dikenali bawaannya jelas
            // kelurahan atau desa — kop dan jabatan penandatangan mengikutinya.
            SinkronkanJenisLembagaDariPilihan();
            SegarkanDaftarWilayah();
            CatatanPilihanWilayah = hasil.Status switch
            {
                StatusPencocokanWilayah.Ditemukan =>
                    "Isian yang ada dikenali dari data wilayah — pilihan sudah disiapkan.",
                StatusPencocokanWilayah.Ambigu =>
                    $"Ada {hasil.JumlahKandidat} wilayah bernama sama, jadi pilihan belum ditentukan otomatis. " +
                    "Pilih kabupaten/kota dan kecamatan yang benar dari daftar.",
                _ =>
                    "Isian yang ada tidak cocok dengan data wilayah; pilih dari daftar untuk menggantinya, " +
                    "atau centang «Isi manual» untuk mempertahankannya."
            };
        }

        /// <summary>
        /// Salin pilihan berjenjang ke isian teks yang dipakai kop surat. Hanya
        /// dipanggil saat pengguna memilih dari daftar: tingkat yang belum dipilih
        /// dikosongkan supaya surat tidak memakai campuran wilayah yang tidak pernah ada.
        /// </summary>
        private void SinkronkanIsianDariPilihan()
        {
            Kabupaten = _pemilih.KabupatenTerpilih?.Nama ?? string.Empty;
            Kecamatan = _pemilih.KecamatanTerpilih?.Nama ?? string.Empty;
            NamaDesa = _pemilih.DesaTerpilih?.Nama ?? string.Empty;

            if (_pemilih.DesaTerpilih is { Berkodepos: true } desa)
            {
                Kodepos = desa.Kodepos;
            }

            // Jenis wilayah mengikuti kabupaten/kota yang dipilih, sehingga kop surat
            // mencetak “PEMERINTAH KOTA …” atau “PEMERINTAH KABUPATEN …” dengan benar.
            SinkronkanJenisKabupatenDariPilihan();
            // Jenis lembaga juga: wilayah yang dikenali bawaannya jelas
            // kelurahan atau desa — kop dan jabatan penandatangan mengikutinya.
            SinkronkanJenisLembagaDariPilihan();

            // Jenis lembaga ikut pilihan desa/kelurahan: Kepala Desa untuk Desa,
            // Lurah + NIP untuk Kelurahan.
            SinkronkanJenisLembagaDariPilihan();

            // Daftar lambang kabupaten ikut diperbarui. Pemilihan otomatisnya hanya
            // terjadi bila berkas lambangnya benar-benar ada — dan tidak pernah
            // mengambil alih logo desa yang sudah diunggah pengguna.
            SegarkanLambangKabupaten(pilihOtomatis: true);

            SegarkanDaftarWilayah();
        }

        /// <summary>
        /// Segarkan daftar pilihan dan seluruh keterangan turunannya. Daftar diganti
        /// sebagai satu kesatuan, bukan dikosongkan lalu diisi ulang.
        /// </summary>
        private void SegarkanDaftarWilayah()
        {
            if (_sedangSegarkanWilayah)
            {
                _segarkanWilayahDiulang = true;
                return;
            }

            _sedangSegarkanWilayah = true;
            try
            {
                // Permintaan yang datang di tengah penyegaran (dari pengikatan yang
                // menulis balik pilihan kosong) dijalankan pada putaran berikutnya,
                // bukan diabaikan: keadaan pilihan sudah berubah dan daftarnya harus
                // ikut menyesuaikan. Batasnya kecil karena keadaan ini mengendap cepat.
                int putaran = 0;
                do
                {
                    _segarkanWilayahDiulang = false;
                    TerapkanDaftarWilayah();
                }
                while (_segarkanWilayahDiulang && ++putaran < 4);
            }
            finally
            {
                _sedangSegarkanWilayah = false;
            }
        }

        /// <summary>Salin daftar pilihan terkini ke halaman beserta keterangannya.</summary>
        private void TerapkanDaftarWilayah()
        {
            DaftarProvinsi = _pemilih.Provinsi;
            DaftarKabupaten = _pemilih.Kabupaten;
            DaftarKecamatan = _pemilih.Kecamatan;
            DaftarDesa = _pemilih.Desa;

            OnPropertyChanged(nameof(WilayahTersedia));
            OnPropertyChanged(nameof(InfoDataWilayah));
            OnPropertyChanged(nameof(ProvinsiTerpilih));
            OnPropertyChanged(nameof(KabupatenTerpilih));
            OnPropertyChanged(nameof(KecamatanTerpilih));
            OnPropertyChanged(nameof(DesaTerpilih));
            OnPropertyChanged(nameof(RingkasanPilihanWilayah));
            OnPropertyChanged(nameof(AdaRingkasanPilihanWilayah));
            OnPropertyChanged(nameof(InfoKodepos));
        }

        /// <summary>
        /// Surel (email) kantor desa — <b>opsional</b>, boleh dikosongkan. Bila diisi,
        /// alamatnya ikut dicetak di baris alamat kop surat dengan warna biru.
        /// </summary>
        public string Email { get => _email; set { if (SetProperty(ref _email, value)) OnFieldChanged(); } }
        public string KepalaDesa { get => _kepalaDesa; set { if (SetProperty(ref _kepalaDesa, value)) OnFieldChanged(); } }
        public string Sekdes { get => _sekdes; set { if (SetProperty(ref _sekdes, value)) OnFieldChanged(); } }
        public string NamaCamat { get => _namaCamat; set { if (SetProperty(ref _namaCamat, value)) OnFieldChanged(); } }
        public string NipCamat { get => _nipCamat; set { if (SetProperty(ref _nipCamat, value)) OnFieldChanged(); } }
        public string GolCamat { get => _golCamat; set { if (SetProperty(ref _golCamat, value)) OnFieldChanged(); } }

        public bool IsLoading
        {
            get => _isLoading;
            private set { if (SetProperty(ref _isLoading, value))
            {
                SaveCommand.RaiseCanExecuteChanged();
                CancelCommand.RaiseCanExecuteChanged();
            } }
        }

        public bool HasChanges
        {
            get => _hasChanges;
            private set { if (SetProperty(ref _hasChanges, value)) SaveCommand.RaiseCanExecuteChanged(); }
        }

        private string _statusPesan = "Siap";

        /// <summary>Pesan singkat untuk statusbar bawah halaman (bukan dialog).</summary>
        public string StatusPesan
        {
            get => _statusPesan;
            private set => SetProperty(ref _statusPesan, value);
        }

        public AsyncRelayCommand SaveCommand { get; }
        public AsyncRelayCommand CancelCommand { get; }

        /// <summary>Nilai form sekarang, siap dipakai untuk pemeriksaan maupun disimpan.</summary>
        private DesaData DariForm() => new()
        {
            NamaDesa = NamaDesa?.Trim(),
            Kecamatan = Kecamatan?.Trim(),
            Kabupaten = Kabupaten?.Trim(),
            JenisKabupaten = _jenisKabupaten,
            KodeKabupaten = _pemilih.KabupatenTerpilih?.Kode,
            JenisLembaga = LembagaKelurahan ? _konstLembagaKelurahan : _konstLembagaDesa,
            Alamat = Alamat?.Trim(),
            Kodepos = Kodepos?.Trim(),
            Email = Email?.Trim(),
            KepalaDesa = KepalaDesa?.Trim(),
            SekretarisDesa = Sekdes?.Trim(),
            NamaCamat = NamaCamat?.Trim(),
            NipCamat = NipCamat?.Trim(),
            GolCamat = GolCamat?.Trim(),
            // NIP pejabat (Lurah); dikosongkan untuk Desa karena Kepala Desa non-ASN.
            NipKepalaDesa = LembagaKelurahan ? NipKepalaDesa?.Trim() : string.Empty
        };


    }
}
