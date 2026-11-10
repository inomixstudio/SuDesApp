using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.GeneratorPdf;
using SuDesApp.Services;
using SuDesApp.Utilities;
using SuDesApp.Wilayah;
using SuDesApp.Wpf.Controls;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Utilities;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class IdmViewModel : ObservableObject
    {
        // ===================== Pemuatan =====================

        /// <summary>
        /// Ambil data pusat untuk desa pada Pengaturan Surat. Aman dipanggil
        /// berkali-kali (tombol Muat ulang); tidak pernah melempar.
        /// </summary>
        /// <summary>Batalkan pemuatan yang sedang berjalan (tombol Batal pada dialog).</summary>
        private void BatalkanMuat()
        {
            try { _ctsMuat?.Cancel(); } catch { /* sudah selesai */ }
        }

        public async Task MuatAsync()
        {
            if (SedangMemuat) return;

            _ctsMuat?.Dispose();
            _ctsMuat = new CancellationTokenSource();
            var ct = _ctsMuat.Token;
            SedangMemuat = true;
            PesanGalat = string.Empty;
            PeringatanSumber = string.Empty;
            PesanStatus = "Menghubungi situs pusat…";

            // Dialog progres gaya splash: deret langkah dibangun bertahap,
            // persis pola SplashStartupWindow.Tahap (aktif → tercentang).
            TahapanMemuat.Clear();
            TambahTahapMemuat("Membaca pengaturan desa & mencocokkan wilayah");

            try
            {
                var desa = await _pengaturan.GetSettingsAsync();
                _namaDesa = desa?.NamaDesa?.Trim() ?? string.Empty;
                _kecamatan = desa?.Kecamatan?.Trim() ?? string.Empty;
                _kabupaten = desa?.Kabupaten?.Trim() ?? string.Empty;
                _provinsi = string.Empty;

                if (_namaDesa.Length == 0 || _kecamatan.Length == 0 || _kabupaten.Length == 0)
                {
                    KosongkanKonteks();
                    PesanStatus = string.Empty;
                    PesanGalat = "Data desa belum diisi. Buka Pengaturan Surat dan lengkapi nama desa, "
                                 + "kecamatan, dan kabupaten — setelah itu halaman ini menampilkan data pusat.";
                    return;
                }

                // Pencocokan wilayah (±90 ribu baris) di thread latar; UI thread
                // tidak tersendat seperti di Setelan.
                var pencocokan = await Task.Run(() => WilayahPencocokan.CariRantai(
                    _wilayah, _namaDesa, _kecamatan, _kabupaten));

                if (pencocokan.Status != StatusPencocokanWilayah.Ditemukan || pencocokan.Rantai == null)
                {
                    KosongkanKonteks();
                    PesanStatus = string.Empty;
                    PesanGalat = PesanPencocokan(pencocokan);
                    return;
                }

                _provinsi = pencocokan.Rantai.Provinsi.Nama;
                _kodeDesa = KodeAngka(pencocokan.Rantai.Desa.Kode);
                if (_kodeDesa.Length != 10)
                {
                    KosongkanKonteks();
                    PesanStatus = string.Empty;
                    PesanGalat = "Kode desa \"" + pencocokan.Rantai.Desa.Kode
                                 + "\" pada data wilayah tidak berbentuk 10 digit, jadi tidak bisa dipakai "
                                 + "mencari data IDM.";
                    return;
                }

                _indeks2025 = null;
                _indeks2025BerhasilDiperiksa = false;
                Task? tugasIndeks = null;
                if (_indeks2025Klien != null)
                {
                    // Indeks2025 berjalan paralel dengan loop tahun (bukan serial).
                    tugasIndeks = Task.Run(async () =>
                    {
                        try
                        {
                            _indeks2025 = await _indeks2025Klien.MuatAsync(_kodeDesa);
                            _indeks2025BerhasilDiperiksa = true;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Dataset Indeks Desa 2025 tidak dapat dimuat");
                        }
                        return Task.CompletedTask;
                    });
                }

                var hasil = new List<HasilIdm>();
                bool lewatHttp = false;
                bool jaringanGagal = false;
                int gagalBeruntun = 0;
                int tahunAwal = _jam().Year;

                TambahTahapMemuat("Menghubungi situs IDM Kemendesa");

                // Limal tahun dicoba PARALEL: sebelumnya serial 5x (~50 detik bila
                // lambat). Kini 5 permintaan tahun BERJALAN bersamaan; pencarian
                // tahun unik tetap via IdmSeriTahun di sisi pemasangan.
                var tugasTahun = Enumerable.Range(0, TahunDicoba)
                    .Select(i => _klien.MuatAsync(_kodeDesa, tahunAwal - i, ct))
                    .ToList();
                var semuaMuatan = await Task.WhenAll(tugasTahun);
                await (tugasIndeks ?? Task.CompletedTask);

                foreach (var muatan in semuaMuatan)
                {
                    if (IdmSeriTahun.Dari(hasil).Tahun.Count >= TahunDitampilkan) break;
                    lewatHttp |= muatan.LewatHttp;
                    if (muatan.Status == StatusMuatIdm.Gagal)
                    {
                        jaringanGagal = true;
                        gagalBeruntun++;
                        if (gagalBeruntun >= 2)
                        {
                            PesanGalat = muatan.Pesan;
                            break;
                        }
                        continue;
                    }
                    gagalBeruntun = 0;
                    if (muatan.Status == StatusMuatIdm.Berhasil && muatan.Hasil != null)
                    {
                        hasil.Add(muatan.Hasil);
                    }
                }

                if (hasil.Count == 0)
                {
                    PesanStatus = string.Empty;

                    if (_indeks2025 != null)
                    {
                        KosongkanKoleksi();
                        _tahunDipilih = 0;
                        Segarkan();
                        PesanGalat = string.Empty;
                        PesanCadangan = "Data Indeks Desa 2025 tersedia. Rincian dan tren IDM lama tidak tersedia untuk desa ini.";
                        DataDiperbarui?.Invoke();
                        return;
                    }

                    // Jaringan gagal → tampilkan cadangan (data terakhir yang
                    // berhasil dimuat, lengkap dengan tanggalnya). Jawaban situs
                    // "tidak ada data" tidak dianggap putus-jaringan sehingga
                    // halaman tetap dikosongkan sesuai fakta dari pusat.
                    if (jaringanGagal)
                    {
                        string alasan = PesanGalat.Length > 0 ? PesanGalat : AlasanJaringan;
                        if (TampilkanCadangan(alasan))
                        {
                            _logger.LogInformation(
                                "Situs pusat gagal dihubungi — halaman IDM memakai cadangan untuk desa {Kode}.",
                                _kodeDesa);
                            return;
                        }

                        PesanGalat = alasan;
                        Kosongkan();
                        return;
                    }

                    Kosongkan();
                    if (PesanGalat.Length == 0)
                    {
                        PesanGalat = "Situs pusat tidak memuat data IDM untuk " + _namaDesa
                                     + " (kode " + _kodeDesa + "). Desa ini mungkin belum masuk penilaian IDM — "
                                     + "kelurahan umumnya tidak dinilai — atau datanya belum diterbitkan.";
                    }

                    return;
                }

                TambahTahapMemuat("Menyusun tampilan halaman");
                Tampilkan(hasil, lewatHttp);
                SelesaikanTahapMemuat();
                PesanStatus = string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memuat data IDM dari situs pusat");
                PesanStatus = string.Empty;

                if (!TampilkanCadangan("Gagal memuat data IDM: " + ex.Message))
                {
                    Kosongkan();
                    PesanGalat = "Gagal memuat data IDM: " + ex.Message;
                }
            }
            finally
            {
                SedangMemuat = false;
            }
        }

        /// <summary>
        /// Pasang satu set hasil sebagai data yang tampil: pilih tahun terbaru
        /// sebagai rincian, simpan seluruh daftar (untuk cadangan dan cetak),
        /// lalu isi koleksi indikator/riwayat/tren. Dipakai bersama oleh pemuatan
        /// langsung dan pemakaian cadangan supaya keduanya tidak menyimpang.
        /// </summary>
        private void PasangData(IReadOnlyList<HasilIdm> semua, string kodeDesa, DateTime? waktuMuat, bool lewatHttp)
        {
            _data = new DataIdm(kodeDesa, waktuMuat, lewatHttp, IdmSeriTahun.Dari(semua));
            _tahunDipilih = _data.Seri.Terbaru;

            BangunTren();
            TampilkanPilihan();
        }

        private void Tampilkan(List<HasilIdm> hasil, bool lewatHttp)
        {
            PasangData(hasil, _kodeDesa, _jam(), lewatHttp);
            PesanCadangan = string.Empty;

            PeringatanSumber = lewatHttp
                ? "Data ini diambil tanpa enkripsi karena sertifikat situs pusat bermasalah. Angka berasal dari "
                  + "situs resmi, tetapi perjalanannya tidak terlindungi — muat ulang saat sertifikat pusat sudah "
                  + "beres untuk menggantinya dengan jalur terenkripsi."
                : string.Empty;

            // Cadangan hasil yang baru saja BERHASIL dimuat — dipakai lagi saat
            // internet mati, termasuk setelah aplikasi dibuka ulang.
            try
            {
                _simpanan?.Simpan(new IdmSimpanan
                {
                    KodeDesa = _kodeDesa,
                    WaktuMuat = _data?.WaktuMuat,
                    LewatHttp = lewatHttp,
                    Hasil = _data!.Seri.Tahun.ToList(),
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cadangan IDM gagal ditulis — tampilan tidak terpengaruh");
            }

            // Catatan pemeriksaan portal: memuat halaman ini berarti portal baru
            // saja dijawab, jadi indikator "data terbaru terbit" ikut diperbarui.
            try
            {
                _pemantauanStore?.Simpan(new IdmPemantauan
                {
                    WaktuPeriksa = _data?.WaktuMuat,
                    TahunTerbit = _data!.Seri.Terbaru,
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Catatan pemeriksaan IDM gagal ditulis — tampilan tidak terpengaruh");
            }

            Segarkan();
            DataDiperbarui?.Invoke();
        }

        /// <summary>
        /// Isi ulang chip tahun dari deret yang dimuat. Chip adalah tampilan dari
        /// (<see cref="_data"/>, <see cref="_tahunDipilih"/>), bukan tempat
        /// menyimpan keadaan sendiri: setiap pemilihan tahun membangun ulang daftar
        /// ini sehingga penanda "sedang tampil" tidak bisa menyimpang dari rincian
        /// indikator yang terlihat.
        /// </summary>
        private void BangunRiwayat()
        {
            Riwayat.Clear();
            if (_data is not { } data) return;

            foreach (var h in data.Seri.Tahun)
            {
                int tahun = h.Ringkasan.Tahun;
                Riwayat.Add(new RiwayatTampil(
                    tahun,
                    IdmTeks.Angka(h.Ringkasan.Skor, "0.0000"),
                    IdmTeks.Status(h.Ringkasan.Status),
                    Ditampilkan: tahun == _tahunDipilih,
                    Terbaru: tahun == data.Seri.Terbaru));
            }
        }

        /// <summary>
        /// Deret grafik tren: seluruh tahun yang dimuat, urut menaik (kiri → kanan).
        /// Tidak bergantung pada tahun yang sedang dipilih — grafik menampilkan
        /// seluruh riwayat, sedangkan tabel indikator hanya satu tahun.
        /// </summary>
        private void BangunTren()
        {
            var tahun = _data?.Seri.Tahun ?? Array.Empty<HasilIdm>();

            DeretTren = tahun
                .Select(h => new TitikTren(
                    h.Ringkasan.Tahun,
                    h.Ringkasan.Skor,
                    h.Ringkasan.Iks,
                    h.Ringkasan.Ike,
                    h.Ringkasan.Ikl))
                .OrderBy(t => t.Tahun)
                .ToList();
        }

        /// <summary>
        /// Ganti isi tabel indikator dengan baris milik tahun yang sedang dipilih.
        /// Dipakai saat data baru dipasang maupun saat pengguna memilih tahun lain
        /// pada daftar chip — keduanya harus memperlihatkan indikator tahun yang
        /// sama dengan angka ringkasan di atas tabel.
        /// </summary>
        private void IsiIndikator()
        {
            Indikator.Clear();
            if (Hasil is not { } hasil) return;

            foreach (var baris in hasil.Baris)
            {
                Indikator.Add(BarisTampil.Dari(baris));
            }
        }

        /// <summary>
        /// Tahun terakhir yang datanya benar-benar terbit untuk desa ini — satu
        /// sumber untuk semua keterangan tentang "tahun terbit", supaya kalimat
        /// itu tidak ikut berubah ketika pengguna membuka tahun yang lebih lama.
        /// </summary>
        private int TahunTerbitTerbaru => _data?.Seri.Terbaru ?? 0;

        /// <summary>Kalimat bawaan bila situs pusat tidak terjangkau tanpa pesan rinci.</summary>
        private const string AlasanJaringan =
            "Situs pusat tidak terjangkau. Periksa sambungan internet lalu muat ulang.";

        /// <summary>
        /// Tampilkan cadangan data terakhir yang BERHASIL dimuat saat situs pusat
        /// tidak bisa dihubungi. Sumbernya berurutan: data di memori (masa sesi
        /// berjalan) lalu cadangan berkas — keduanya harus untuk kode desa yang
        /// sama, supaya cadangan desa lain tidak pernah tampil.
        /// Berhasil = halaman terisi + peringatan bertanggal; gagal = tidak ada
        /// cadangan sehingga pemanggil mengosongkan halaman seperti biasa.
        /// </summary>
        private bool TampilkanCadangan(string alasan)
        {
            IdmSimpanan? sumber = null;

            if (_data is { Seri.Tahun.Count: > 0 } memori
                && memori.KodeDesa.Length > 0
                && string.Equals(memori.KodeDesa, _kodeDesa, StringComparison.Ordinal))
            {
                sumber = new IdmSimpanan
                {
                    KodeDesa = memori.KodeDesa,
                    WaktuMuat = memori.WaktuMuat,
                    LewatHttp = memori.LewatHttp,
                    Hasil = memori.Seri.Tahun.ToList(),
                };
            }
            else if (_simpanan != null)
            {
                var berkas = _simpanan.Muat();
                if (berkas != null
                    && berkas.Hasil is { Count: > 0 }
                    && berkas.KodeDesa.Length > 0
                    && string.Equals(berkas.KodeDesa, _kodeDesa, StringComparison.Ordinal))
                {
                    sumber = berkas;
                }
            }

            if (sumber == null) return false;

            PasangData(sumber.Hasil, sumber.KodeDesa, sumber.WaktuMuat, sumber.LewatHttp);

            PeringatanSumber = sumber.LewatHttp
                ? "Data cadangan ini diambil sebelum perbaikan keamanan (sebelum November 2026) dan pernah lewat jalur tidak terenkripsi. Muat ulang saat internet kembali untuk menggantinya."
                : string.Empty;

            PesanGalat = string.Empty;
            var kapan = sumber.WaktuMuat == null
                ? "waktu pengambilannya tidak tercatat"
                : sumber.WaktuMuat.Value.ToString("dd MMMM yyyy pukul HH.mm", new CultureInfo("id-ID"));
            PesanCadangan = alasan.TrimEnd('.')
                            + ". Menampilkan data terakhir yang berhasil dimuat " + kapan
                            + " — muat ulang saat internet sudah kembali.";

            Segarkan();
            return true;
        }

        /// <summary>Apakah data terakhir diambil tanpa enkripsi (dipakai uji/Diagnostik).</summary>
        public bool LewatHttp => _data?.LewatHttp ?? false;

        /// <summary>
        /// Kosongkan data milik desa yang SEDANG dipilih, termasuk penilaian
        /// Indeks Desa 2025. Dipakai saat pengaturan desa tidak lengkap atau
        /// wilayahnya tidak bisa dicocokkan: tanpa ini, kartu Indeks Desa 2025
        /// milik desa sebelumnya tetap tampil padahal halaman sudah menyatakan
        /// desa belum bisa dikenali. Jalur "situs pusat gagal" TIDAK memakai
        /// metode ini karena data 2025 yang baru dimuat tetap sah ditampilkan.
        /// </summary>
        private void KosongkanKonteks()
        {
            _indeks2025 = null;
            _indeks2025BerhasilDiperiksa = false;
            _kodeDesa = string.Empty;
            Kosongkan();
        }

        private void Kosongkan()
        {
            _data = null;
            _tahunDipilih = 0;
            PesanCadangan = string.Empty;
            KosongkanKoleksi();
            Segarkan();
        }

        private void KosongkanKoleksi()
        {
            Indikator.Clear();
            Riwayat.Clear();
            DeretTren = Array.Empty<TitikTren>();
        }

        private void Segarkan()
        {
            // Baca ulang catatan pemeriksaan: pemantau latar bisa saja baru saja
            // menulisnya (mis. menemukan tahun yang lebih baru dari data tampil).
            _pemantauan = _pemantauanStore?.Muat();

            // Catatan sertifikat juga dibaca ulang — pemantau latar memeriksanya
            // tiap interval tanpa membuka halaman ini.
            _sertifikatCatatan = _sertifikatStore?.Muat();

            foreach (var nama in new[]
                     {
                         nameof(AdaData), nameof(NamaDesaTampil), nameof(WilayahTampil),
                         nameof(AdaDataIdm), nameof(AdaIndeksDesa2025), nameof(SkorTotalIndeksDesa2025),
                         nameof(NilaiIndeksDesa2025), nameof(StatusIndeksDesa2025),
                         nameof(JudulSeriLamaTampil), nameof(CatatanTren2025Tampil),
                         nameof(TanggalPembaruanSumberIndeksDesa2025),
                         nameof(DimensiIndeksDesa2025), nameof(RincianSubDimensiIndeksDesa2025),
                         nameof(SumberIndeksDesa2025),
                         nameof(KodeDesaTampil), nameof(TahunTampil), nameof(SkorTampil),
                         nameof(TahunDipilih),
                         nameof(JumlahTahunDimuat), nameof(TahunTerakhirIdmTampil),
                         nameof(RingkasanTahunDimuat), nameof(AdaRiwayatTahun),
                         nameof(StatusTampil), nameof(IksTampil), nameof(IkeTampil),
                         nameof(IklTampil), nameof(TargetTampil), nameof(SkorMinimalTampil),
                         nameof(PesanTarget), nameof(SumberTampil),
                         nameof(TooltipCetak), nameof(PesanSertifikat), nameof(AdaPesanSertifikat),
                         nameof(TampilkanKartuKeadaan), nameof(JudulKeadaan), nameof(IsiKeadaan),
                         nameof(AdaGalatDiToolbar), nameof(PesanMenunggu),
                         nameof(AdaPesanTahun), nameof(PesanTahun), nameof(DeretTren),
                         nameof(TerbitanBaru), nameof(AdaTerbitanBaru),
                         nameof(JudulTerbitanBaru), nameof(KeteranganTerbitanBaru),
                         nameof(IndikatorTerbitTampil), nameof(PemeriksaanTampil),
                         nameof(AdaIndikatorTerbit), nameof(AdaPemeriksaan)
                     })
            {
                OnPropertyChanged(nama);
            }

            // Tombol Cetak hanya aktif saat ada data yang bisa dicetak.
            ((AsyncRelayCommand)CetakCommand).RaiseCanExecuteChanged();
            (BukaTerbitanCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }


    }
}
