using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SudesExPlus;
using SudesExPlus.Style;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.GeneratorPdf;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Utilities;

namespace SuDesApp.Wpf.ViewModels
{
/// <summary>
    /// Panel Surat Keputusan per jabatan: properti panel, muat isian, pratinjau/arsip berkas SK, dan helper nama jabatan.
    /// Pemecahan god-class tanpa mengubah perilaku — bagian dari kelas parsial
    /// <see cref="PerangkatDesaViewModel"/>.
    /// </summary>
    public partial class PerangkatDesaViewModel
    {
        // ---------- panel SK per jabatan (properti) ----------

        /// <summary>Panel SK tampil sebagai overlay di atas tabel.</summary>
        public bool IsPanelSkTerbuka
        {
            get => _isPanelSkTerbuka;
            private set => SetProperty(ref _isPanelSkTerbuka, value);
        }

        public string JudulPanelSk => string.IsNullOrWhiteSpace(_skJabatan)
            ? "SURAT KEPUTUSAN"
            : $"SURAT KEPUTUSAN — {NamaJabatanTampil(_skJabatan)}";

        /// <summary>Jabatan yang bisa dipilih di panel SK — dibatasi bagian tempat
        /// panel dibuka (lihat <see cref="JabatanBagian"/>), sebab tiap kelompok
        /// punya cara penerbitan SK berbeda. Tanpa daftar ini, panel yang dibuka
        /// dari baris bagian lain menampilkan kotak jabatan kosong.</summary>
        public IReadOnlyList<string> PilihanJabatanSk => _daftarSkJabatan ?? DaftarJabatan;

        private IReadOnlyList<string>? _daftarSkJabatan;

        /// <summary>Jenis SK; nilainya nama <see cref="SkJenisPerangkat"/> supaya mudah di-bind.</summary>
        public IReadOnlyList<FilterPilihan> PilihanJenisSk { get; } = new[]
        {
            new FilterPilihan(nameof(SkJenisPerangkat.Pengangkatan), "Pengangkatan"),
            new FilterPilihan(nameof(SkJenisPerangkat.Pemberhentian), "Pemberhentian"),
            new FilterPilihan(nameof(SkJenisPerangkat.Penetapan), "Penetapan (bila kelompok mendukung)")
        };

        /// <summary>Ganti jabatan dari panel; isian disesuaikan dengan data jabatan itu.</summary>
        public string SkJabatan
        {
            get => _skJabatan;
            set
            {
                if (!SetProperty(ref _skJabatan, value ?? string.Empty)) return;
                MuatIsianSk(value);
            }
        }

        public string SkJenis
        {
            get => _skJenis;
            set => SetProperty(ref _skJenis, value ?? nameof(SkJenisPerangkat.Pengangkatan));
        }

        public string SkNomor
        {
            get => _skNomor;
            set => SetProperty(ref _skNomor, value ?? string.Empty);
        }

        public string SkTanggal
        {
            get => _skTanggal;
            set => SetProperty(ref _skTanggal, value ?? string.Empty);
        }

        public string SkNama
        {
            get => _skNama;
            set => SetProperty(ref _skNama, value ?? string.Empty);
        }

        public string SkNik
        {
            get => _skNik;
            set => SetProperty(ref _skNik, value ?? string.Empty);
        }

        public string SkWilayah
        {
            get => _skWilayah;
            set => SetProperty(ref _skWilayah, value ?? string.Empty);
        }

        public string SkMulai
        {
            get => _skMulai;
            set => SetProperty(ref _skMulai, value ?? string.Empty);
        }

        public string SkSelesai
        {
            get => _skSelesai;
            set => SetProperty(ref _skSelesai, value ?? string.Empty);
        }

        public string SkAlasan
        {
            get => _skAlasan;
            set => SetProperty(ref _skAlasan, value ?? string.Empty);
        }

        /// <summary>Isian panel yang bermasalah, tampil di dalam panel.</summary>
        public string SkGalat
        {
            get => _skGalat;
            private set => SetProperty(ref _skGalat, value ?? string.Empty);
        }

        /// <summary>Benar bila jabatan ini belum punya orang, sehingga SK dicetak sebagai contoh.</summary>
        public bool SkContoh
        {
            get => _skContoh;
            private set
            {
                if (SetProperty(ref _skContoh, value))
                    OnPropertyChanged(nameof(SkCatatanMode));
            }
        }

        public string SkCatatanMode => SkContoh
            ? "Jabatan ini belum ada orangnya. SK dicetak sebagai contoh: nama dan tanggal dibiarkan "
              + "bergaris titik-titik, dan isian boleh diketik untuk dicoba."
            : "Isian di bawah sudah diambil dari data orang pada jabatan ini; ubah bila perlu sebelum dicetak.";

        /// <summary>SK jabatan ini diterbitkan Bupati (Kepala Desa), jadi aplikasi tidak menerbitkan dokumennya.</summary>
        public bool SkDariBupati => SkJabatanAksiKatalog.SumberJabatan(_skJabatan) == SumberSkPerangkat.Bupati;

        /// <summary>Sudah ada berkas SK terarsip untuk orang ini.</summary>
        public bool SkAdaBerkas => !string.IsNullOrWhiteSpace(_skBerkasSk);

        /// <summary>Judul bagian pengarsipan berkas SK, menyesuaikan penerbit SK-nya.</summary>
        public string SkArsipJudul => SkDariBupati ? "Berkas SK Bupati terarsip" : "Berkas SK tersimpan";

        /// <summary>Teks tombol pengarsipan berkas SK.</summary>
        public string SkArsipTombol => SkDariBupati ? "Arsipkan SK Bupati" : "Arsipkan Berkas SK";

        /// <summary>
        /// Keterangan pengarsipan: SK Bupati tidak diterbitkan aplikasi, sedangkan
        /// SK jabatan lain boleh dilekati berkas bertanda tangan yang sudah ada.
        /// </summary>
        public string SkArsipKeterangan => SkDariBupati
            ? $"SK {NamaJabatanTampil(_skJabatan)} diterbitkan Bupati, bukan oleh Kepala Desa, sehingga aplikasi tidak "
              + "menerbitkan dokumennya. Arsipkan berkas PDF SK-nya supaya nomor dan dokumennya tersimpan "
              + "bersama data ini."
            : "SK jabatan ini diterbitkan Kepala Desa dan dibuat otomatis dalam bentuk SK jadi saat data "
              + "orang disimpan; berkasnya tersimpan di sini dan bisa dibuka lewat tombol di bawah. "
              + "Dokumen juga tetap bisa dicetak ulang dari isian panel ini.";

        public string SkNamaBerkas => string.IsNullOrWhiteSpace(_skBerkasSk)
            ? "Belum ada berkas arsip."
            : _skBerkasSk!;

        /// <summary>Folder arsip berkas SK — ditampilkan sebagai tooltip supaya operator tahu lokasinya.</summary>
        public string SkLokasiArsip => BerkasSkPerangkat.LokasiFolder;

        /// <summary>Angka ringkas jabatan terpilih, mis. "3 orang — 2 aktif, 1 menunggu SK".</summary>
        public string SkKeteranganJabatan
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_skJabatan)) return string.Empty;
                var kartu = SkJabatanAksiKatalog
                    .Susun(_semua, new[] { JabatanPerangkat.Normalisasi(_skJabatan) })
                    .FirstOrDefault();
                return kartu?.Ringkas ?? string.Empty;
            }
        }

        /// <summary>
        /// Catatan template SK jabatan terpilih: contoh SK yang dipakai aplikasi
        /// untuk jabatan ini beserta sumbernya, supaya operator tahu SK-nya
        /// memang disusun sesuai jabatannya.
        /// </summary>
        /// <summary>
        /// Kelompok jabatan terpilih punya template SK bawaan sehingga dokumennya
        /// bisa dibuat. Bila false, panel jangan menampilkan daftar dasar hukum
        /// sebab tidak ada template yang dirujuk — diganti penjelasan keterbatasannya.
        /// </summary>
        public bool SkAdaTemplate =>
            !string.IsNullOrWhiteSpace(_skJabatan)
            && SkPerangkatKatalog.AdaUntuk(_skJabatan, JabatanPerangkat.Kelompok(_skJabatan));

        public string SkCatatanTemplate
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_skJabatan)) return string.Empty;

                if (!SkAdaTemplate)
                {
                    string kelompok = JabatanPerangkat.Kelompok(_skJabatan);
                    return $"Kelompok {kelompok} belum punya template SK bawaan, jadi SK "
                         + $"{NamaJabatanTampil(_skJabatan)} belum bisa dicetak dari panel ini. "
                         + "Berkas SK bertanda tangan tetap bisa disimpan lewat bagian Berkas SK.";
                }

                return $"Template SK {NamaJabatanTampil(_skJabatan)} disusun mengikuti format naskah "
                     + "dinas (Peraturan Bupati tentang Tata Naskah Dinas Desa) beserta UU, PP, dan "
                     + "Permendagri pengangkatan/pemberhentian perangkat desa yang berlaku, dirinci "
                     + "menurut tugas pokok jabatannya.";
            }
        }

        /// <summary>Dasar hukum template jabatan terpilih, ditampilkan di panel SK.</summary>
        public string SkDasarHukum
        {
            get
            {
                var template = SkPerangkatKatalog.Untuk(_skJabatan, JabatanPerangkat.Kelompok(_skJabatan));
                return template?.DasarHukumTeks ?? string.Empty;
            }
        }


        // ---------- panel surat keputusan per jabatan ----------

        /// <summary>
        /// Buka panel SK untuk satu baris. Baris kosong pun membuka panel: SK
        /// dicetak sebagai contoh (nama dan tanggal bergaris titik-titik) dengan
        /// jabatan baris itu sudah terpilih, sehingga operator bisa melihat
        /// bentuk dokumennya lebih dulu.
        /// </summary>
        private async Task BukaPanelSkAsync(BarisPerangkatViewModel? baris)
        {
            if (baris is null) return;
            await Task.Yield();

            IsFormTerbuka = false;
            SkGalat = string.Empty;
            // Pilihan jabatan panel dibatasi bagian baris ini, supaya SK yang
            // disusun tetap milik kelompok yang bersangkutan.
            _daftarSkJabatan = JabatanBagian(_bagianAktif);
            OnPropertyChanged(nameof(PilihanJabatanSk));
            MuatIsianSk(baris.JabatanTampil);
            IsPanelSkTerbuka = true;
        }

        private void TutupPanelSk()
        {
            IsPanelSkTerbuka = false;
            SkGalat = string.Empty;
        }

        /// <summary>
        /// Isi panel dari data jabatan terpilih: orang yang masih memegang
        /// jabatan didahulukan, lalu nomor/tanggal SK dan masa jabatannya
        /// dibawa serta supaya tidak perlu diketik ulang. Jabatan tanpa orang
        /// membuka mode contoh.
        /// </summary>
        private void MuatIsianSk(string? jabatan)
        {
            string normal = JabatanPerangkat.Normalisasi(jabatan);
            if (!string.Equals(_skJabatan, normal, StringComparison.Ordinal))
            {
                _skJabatan = normal;
                OnPropertyChanged(nameof(SkJabatan));
            }

            // Panel dibuka/dipindah jabatan: mode SK bersama dimulai ulang supaya
            // daftar nama jabatan lama tidak terbawa ke jabatan yang baru.
            SkBersama = false;
            var orang = _semua
                .Where(p => string.Equals(p.JabatanTampil, normal, StringComparison.Ordinal))
                .OrderByDescending(p => p.MasihMemegangJabatan)
                .ThenBy(p => p.Nama, StringComparer.CurrentCulture)
                .FirstOrDefault();

            _skIdPerangkat = orang?.ID ?? 0;
            _skBerkasSk = orang?.BerkasSK;
            SkContoh = orang is null;
            SkNomor = orang?.NomorSK ?? string.Empty;
            SkTanggal = TanggalKeTeks(orang?.TanggalSK);
            SkMulai = TanggalKeTeks(orang?.MasaJabatanMulai);
            SkSelesai = TanggalKeTeks(orang?.MasaJabatanSelesai);
            SkNama = orang?.Nama ?? string.Empty;
            SkNik = orang?.NIK ?? string.Empty;
            SkWilayah = orang is null || orang.WilayahRingkas == "-" ? string.Empty : orang.WilayahRingkas;

            // Pindah JABATAN memang memulai isian dari orang jabatan itu. Tetapi
            // bila jabatannya SAMA (panel dibuka ulang, atau ComboBox memilih
            // item yang sama), isian operator jangan dibuang: dulu SkAlasan
            // dikosongkan dan SkJenis dipaksa kembali ke Pemberhentian /
            // Pengangkatan, sehingga alasan pemberhentian yang sudah diketik
            // hilang dan pilihan "Penetapan" (KPM) terbalik sendiri tanpa
            // operator menyentuhnya.
            bool pindahJabatan = !string.Equals(_skJabatanSebelumnya, normal, StringComparison.Ordinal);
            _skJabatanSebelumnya = normal;

            if (pindahJabatan)
            {
                SkAlasan = string.Empty;
            }

            SkJenis = pindahJabatan
                ? (orang is { StatusTampil: StatusPerangkat.Berhenti }
                    ? nameof(SkJenisPerangkat.Pemberhentian)
                    : nameof(SkJenisPerangkat.Pengangkatan))
                : SkJenis;

            // Kelompok yang SK-nya selalu bersama (Linmas, Posyandu, Kader KPM)
            // langsung memakai mode bersama — SK per orang tidak pernah dibuat
            // untuk kelompok ini.
            SkBersama = SkWajibBersama;

            SegarkanPanelSk();
        }

        /// <summary>Segarkan seluruh turunan yang berubah bersama isian panel.</summary>
        private void SegarkanPanelSk()
        {
            OnPropertyChanged(nameof(JudulPanelSk));
            OnPropertyChanged(nameof(SkDariBupati));
            OnPropertyChanged(nameof(SkAdaBerkas));
            OnPropertyChanged(nameof(SkNamaBerkas));
            OnPropertyChanged(nameof(SkKeteranganJabatan));
            OnPropertyChanged(nameof(SkAdaTemplate));
            OnPropertyChanged(nameof(SkCatatanTemplate));
            OnPropertyChanged(nameof(SkDasarHukum));
            OnPropertyChanged(nameof(SkArsipJudul));
            OnPropertyChanged(nameof(SkArsipTombol));
            OnPropertyChanged(nameof(SkArsipKeterangan));
            OnPropertyChanged(nameof(SkLokasiArsip));
            OnPropertyChanged(nameof(SkBisaBersama));
            OnPropertyChanged(nameof(SkCatatanBersama));
            OnPropertyChanged(nameof(SkLampiranCatatan));
            OnPropertyChanged(nameof(SkLampiranRingkasan));
            OnPropertyChanged(nameof(SkPesanLampiran));
            OnPropertyChanged(nameof(SkTombolPratinjau));
            OnPropertyChanged(nameof(SkWajibBersama));
            OnPropertyChanged(nameof(SkBersamaDiubah));
            SegarkanPerintah();
        }

        /// <summary>
        /// Calon baris lampiran untuk satu jabatan: orang yang masih memegang
        /// jabatan (AKTIF/MENUNGGU SK). Jabatan yang tidak punya pemegang sama
        /// sekali memakai seluruh orang pada jabatan itu — ditandai
        /// <c>Cadangan</c> supaya panel menjelaskannya — agar operator tidak
        /// buntu saat datanya sudah lama.
        /// </summary>

        private async Task PratinjauSkAsync()
        {
            if (_penjaga is { CetakTerkunci: true })
            {
                await _messageService.ShowInfoAsync(
                    SuDesApp.Services.PenjagaLisensi.PesanCetakTerkunci);
                return;
            }

            try
            {
                IsBusy = true;
                SkGalat = string.Empty;

                string kelompok = JabatanPerangkat.Kelompok(_skJabatan);
                if (!SkPerangkatKatalog.Ada(kelompok))
                {
                    SkGalat = $"Kelompok {kelompok} belum punya template SK bawaan, jadi dokumen belum bisa dibuat.";
                    return;
                }

                // Isian tanggal panel berupa teks; yang tidak sah ditolak di sini
                // supaya dokumen tidak tercetak dengan tanggal salah.
                if (!BacaTanggal(_skTanggal, "Tanggal SK", out DateTime? tanggalSk, out string pesanTanggal)
                    || !BacaTanggal(_skMulai, "Masa Jabatan Mulai", out DateTime? mulai, out pesanTanggal)
                    || !BacaTanggal(_skSelesai, "Masa Jabatan Selesai", out DateTime? selesai, out pesanTanggal))
                {
                    SkGalat = pesanTanggal;
                    return;
                }

                // SK bersama mencetak nama-namanya di halaman lampiran.
                bool bersama = SkBersama;
                var lampiran = bersama
                    ? SkLampiran.Select(b => b.KeBaris()).ToList()
                    : new List<BarisLampiranSk>();
                if (bersama && lampiran.Count == 0 && SkBisaBersama)
                {
                    SkGalat = "Belum ada nama pada lampiran SK bersama, jadi dokumen belum bisa dibuat. "
                            + "Muat ulang daftar namanya terlebih dahulu.";
                    return;
                }

                var desa = await _desaRepo.GetInfoDesaAsync().ConfigureAwait(true);

                var isi = new SkPerangkatIsi
                {
                    Jenis = JenisSkTerpilih(),
                    Kelompok = kelompok,
                    Nomor = SkNomor,
                    Tanggal = tanggalSk ?? DateTime.Today,
                    // Pada SK bersama, yang mencetak nama adalah lampirannya.
                    Nama = bersama ? string.Empty : SkNama,
                    NIK = bersama ? string.Empty : SkNik,
                    Jabatan = _skJabatan,
                    Wilayah = bersama ? string.Empty : SkWilayah,
                    Mulai = mulai,
                    Selesai = selesai,
                    // Pratinjau SK juga wajib punya alasan supaya dokumen yang
                    // dilihat operator sama sahnya dengan yang terbit otomatis.
                    Alasan = JenisSkTerpilih() == SkJenisPerangkat.Pemberhentian
                        ? (string.IsNullOrWhiteSpace(SkAlasan)
                            ? SkPerangkatKatalog.AlasanPemberhentianKosong
                            : "karena " + SkAlasan.Trim().TrimEnd('.'))
                        : SkAlasan,
                    Lampiran = lampiran,
                    PenandaDiktum = SkPerangkatKatalog.PenandaDiktum(kelompok),
                    Memperhatikan = SkPerangkatKatalog.Memperhatikan(kelompok),
                    Contoh = SkContoh
                };

                using var stream = new MemoryStream();
                _skGenerator.GenerateSkPdf(stream, desa, isi);
                byte[] pdf = stream.ToArray();
                if (pdf.Length == 0)
                {
                    SkGalat = "Gagal membuat PDF atau PDF kosong.";
                    return;
                }

                // Pratinjau ditulis ke TempPDF supaya ikut dibersihkan
                // TempPdfCleanup seperti keluaran lain; SudesPdf butuh jalur penuh.
                string awalan = bersama ? "SK_Bersama" : "SK";
                string jalur = await PratinjauPdf
                    .SimpanAsync(_appConfig, pdf, $"{awalan}_{_skJabatan.Replace(' ', '_')}")
                    .ConfigureAwait(true);

                _activityLog.Log("PERANGKAT", $"{_skJabatan}",
                    bersama ? "Pratinjau SK bersama" : "Pratinjau SK", jalur);
                StatusText = bersama
                    ? $"SK bersama {_skJabatan} ({lampiran.Count} nama pada lampiran) dibuka di pratinjau PDF."
                    : $"SK {_skJabatan} dibuka di pratinjau PDF.";
                _navigation.Navigate(_previewFactory(
                    bersama
                        ? $"SK Bersama {NamaJabatanTampil(_skJabatan)}"
                        : $"SK {NamaJabatanTampil(_skJabatan)}", jalur));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membuat SK {Jabatan}", _skJabatan);
                SkGalat = "Gagal membuat SK. Penyebabnya: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private SkJenisPerangkat JenisSkTerpilih() =>
            Enum.TryParse<SkJenisPerangkat>(_skJenis, out var jenis)
                ? jenis
                : SkJenisPerangkat.Pengangkatan;

        /// <summary>
        /// Salin berkas PDF SK dari luar ke folder arsip, lalu simpan namanya di
        /// data orang yang sedang ditampilkan panel: SK Bupati untuk Kepala Desa,
        /// atau SK bertanda tangan Kepala Desa untuk jabatan lain.
        /// </summary>
        private async Task ArsipkanSkAsync()
        {
            var orang = _semua.FirstOrDefault(p => p.ID == _skIdPerangkat);
            if (orang is null)
            {
                SkGalat = "Baris ini belum punya orang, jadi berkas SK belum bisa diarsipkan. "
                        + "Isi dulu datanya lewat form, lalu arsipkan berkas SK-nya.";
                return;
            }

            var ofd = new OpenFileDialog
            {
                Title = $"Pilih berkas PDF SK untuk {orang.JabatanTampil}",
                Filter = "Berkas PDF (*.pdf)|*.pdf",
                CheckFileExists = true,
                Multiselect = false
            };
            if (ofd.ShowDialog() != true) return;

            string? namaBaru = null;
            try
            {
                IsBusy = true;
                SkGalat = string.Empty;

                namaBaru = BerkasSkPerangkat.Arsipkan(orang.ID, ofd.FileName);
                orang.BerkasSK = namaBaru;
                var hasil = await _layanan.SimpanAsync(orang, modeUbah: true).ConfigureAwait(true);
                if (!hasil.Berhasil)
                {
                    BerkasSkPerangkat.Hapus(namaBaru);
                    SkGalat = hasil.Kekurangan.Count > 0
                        ? string.Join(Environment.NewLine, hasil.Kekurangan)
                        : hasil.Pesan ?? "Berkas SK belum bisa disimpan.";
                    return;
                }

                _skBerkasSk = namaBaru;
                _activityLog.Log("PERANGKAT", $"{orang.Nama} ({orang.JabatanTampil})",
                    "Arsip SK", namaBaru);
                await LoadAsync().ConfigureAwait(true);
                SegarkanPanelSk();
                StatusText = $"Berkas SK {orang.JabatanTampil} diarsipkan.";
                await _messageService.ShowInfoAsync($"Berkas SK berhasil diarsipkan.\n\n{namaBaru}");
            }
            catch (Exception ex)
            {
                if (namaBaru != null) BerkasSkPerangkat.Hapus(namaBaru);
                _logger.LogError(ex, "Gagal mengarsipkan berkas SK Bupati");
                SkGalat = "Gagal mengarsipkan berkas SK. Penyebabnya: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Tampilkan berkas SK Bupati yang sudah diarsipkan pada pratinjau SudesPdf.</summary>
        private void BukaBerkasSk()
        {
            string? jalur = BerkasSkPerangkat.JalurLengkap(_skBerkasSk);
            if (jalur is null)
            {
                SkGalat = "Berkas SK tidak ditemukan di folder arsip. Lampirkan ulang berkasnya.";
                return;
            }

            _navigation.Navigate(_previewFactory($"SK {NamaJabatanTampil(_skJabatan)}", jalur));
        }

        /// <summary>"KAUR UMUM DAN TATA USAHA" → "Kaur Umum Dan Tata Usaha" untuk judul panel.</summary>
        private static string NamaJabatanTampil(string jabatan) =>
            string.Join(" ", JabatanPerangkat.Normalisasi(jabatan)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(kata => char.ToUpperInvariant(kata[0]) + kata[1..].ToLowerInvariant()));


    }
}
