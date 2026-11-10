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
    /// Form data diri perangkat: isian, pencarian warga, aturan SK pada form (wajib Bupati / otomatis), buka-tutup form, dan SimpanAsync.
    /// Pemecahan god-class tanpa mengubah perilaku — bagian dari kelas parsial
    /// <see cref="PerangkatDesaViewModel"/>.
    /// </summary>
    public partial class PerangkatDesaViewModel
    {
        // ---------- form ----------

        public bool IsFormTerbuka
        {
            get => _isFormTerbuka;
            private set => SetProperty(ref _isFormTerbuka, value);
        }

        public string JudulForm => IsModeUbah
            ? $"Ubah Data {NamaBagianTampil}"
            : $"Tambah {NamaBagianTampil}";

        public bool IsModeUbah
        {
            get => _isModeUbah;
            private set
            {
                if (SetProperty(ref _isModeUbah, value))
                    OnPropertyChanged(nameof(JudulForm));
            }
        }

        /// <summary>Isian form yang bermasalah, tampil di bawah form.</summary>
        public string FormGalat
        {
            get => _formGalat;
            private set => SetProperty(ref _formGalat, value);
        }

        public string FormNama
        {
            get => _formNama;
            set
            {
                if (SetProperty(ref _formNama, value))
                {
                    BersihkanGalat();
                    SimpanCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string CariWarga
        {
            get => _cariWarga;
            set
            {
                if (SetProperty(ref _cariWarga, value ?? string.Empty))
                    _ = CariWargaAsync(_cariWarga);
            }
        }

        public ObservableCollection<WargaData> HasilWarga
        {
            get => _hasilWarga;
            private set => SetProperty(ref _hasilWarga, value);
        }

        private async Task CariWargaAsync(string kata)
        {
            if (kata.Trim().Length < 2)
            {
                HasilWarga.Clear();
                return;
            }

            string pencarian = kata.Trim();
            try
            {
                var wargaDitemukan = await _wargaRepository.CariWargaUntukPerangkatAsync(pencarian, 20)
                    .ConfigureAwait(true);
                if (!string.Equals(pencarian, _cariWarga.Trim(), StringComparison.Ordinal)) return;
                HasilWarga = new ObservableCollection<WargaData>(wargaDitemukan);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Pencarian warga untuk perangkat desa gagal");
                HasilWarga.Clear();
            }
        }

        public void PilihWarga(WargaData? warga)
        {
            if (warga is null) return;
            FormNama = warga.Nama ?? string.Empty;
            FormNik = warga.NIK ?? string.Empty;
            FormJenisKelamin = PerangkatDesa.JenisKelaminRingkas(warga.JenisKelamin);
            FormTempatLahir = warga.TempatLahir ?? string.Empty;
            FormTanggalLahir = TanggalIsian.KeTeks(DateTime.TryParse(warga.TanggalLahir, out var tgl) ? tgl : null);
            FormAgama = warga.Agama ?? string.Empty;
            FormPendidikan = warga.Pendidikan ?? string.Empty;
            FormAlamat = warga.AlamatLengkap ?? warga.Alamat ?? string.Empty;
            FormDusun = warga.Dusun ?? string.Empty;
            FormRt = warga.RT ?? string.Empty;
            FormRw = warga.RW ?? string.Empty;
            FormNomorHp = warga.NomorHP ?? string.Empty;
            CariWarga = string.Empty;
            HasilWarga.Clear();
        }

        public string FormJabatan
        {
            get => _formJabatan;
            set
            {
                if (SetProperty(ref _formJabatan, value))
                {
                    BersihkanGalat();
                    SegarkanFormSk();
                    SimpanCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// Hilangkan pesan galat begitu operator mulai memperbaiki isian, supaya
        /// tidak terbawa dari percobaan sebelumnya.
        /// </summary>
        private void BersihkanGalat()
        {
            if (!string.IsNullOrEmpty(_formGalat)) FormGalat = string.Empty;
        }

        public string FormNip
        {
            get => _formNip;
            set => SetProperty(ref _formNip, value);
        }

        public string FormNik
        {
            get => _formNik;
            set => SetProperty(ref _formNik, value);
        }

        public string FormJenisKelamin
        {
            get => _formJenisKelamin;
            set => SetProperty(ref _formJenisKelamin, value);
        }

        public string FormTempatLahir
        {
            get => _formTempatLahir;
            set => SetProperty(ref _formTempatLahir, value);
        }

        /// <summary>
        /// Tanggal lahir sebagai teks bebas (isian TextBox biasa, bukan pemilih
        /// tanggal): diisi dan dibaca dengan pola dd-MM-yyyy. Teks selain pola
        /// itu ditolak saat disimpan dengan pesan yang jelas, sebab hanya tanggal
        /// yang sah boleh masuk ke kolom tanggal database.
        /// </summary>
        public string FormTanggalLahir
        {
            get => _formTanggalLahir;
            set => SetProperty(ref _formTanggalLahir, value ?? string.Empty);
        }

        public string FormPendidikan
        {
            get => _formPendidikan;
            set => SetProperty(ref _formPendidikan, value);
        }

        /// <summary>Agama (kolom AGAMA pada berkas data perangkat contoh).</summary>
        public string FormAgama
        {
            get => _formAgama;
            set => SetProperty(ref _formAgama, value ?? string.Empty);
        }

        /// <summary>Tahun lulus pendidikan terakhir (teks, tanda "-" aman).</summary>
        public string FormTahunLulus
        {
            get => _formTahunLulus;
            set => SetProperty(ref _formTahunLulus, value ?? string.Empty);
        }

        /// <summary>Nomor ijazah pendidikan terakhir.</summary>
        public string FormNoIjasah
        {
            get => _formNoIjasah;
            set => SetProperty(ref _formNoIjasah, value ?? string.Empty);
        }

        /// <summary>
        /// Alamat lama tetap dibawa saat data diubah supaya baris lama tidak
        /// kehilangan alamatnya, tetapi form sudah tidak menyediakan isiannya —
        /// keterangan wilayah cukup diisi lewat Dusun/RT/RW.
        /// </summary>
        public string FormAlamat
        {
            get => _formAlamat;
            set => SetProperty(ref _formAlamat, value);
        }

        public string FormDusun
        {
            get => _formDusun;
            set => SetProperty(ref _formDusun, value);
        }

        public string FormRt
        {
            get => _formRt;
            set => SetProperty(ref _formRt, value);
        }

        public string FormRw
        {
            get => _formRw;
            set => SetProperty(ref _formRw, value);
        }

        /// <summary>Unit kerja baris (mis. dusun untuk KADUS), tersimpan pada data
        /// walau halaman ini tidak lagi memakainya untuk pengelompokan SK.</summary>
        public string FormUnit
        {
            get => _formUnit;
            set => SetProperty(ref _formUnit, value ?? string.Empty);
        }

        public string FormNomorHp
        {
            get => _formNomorHp;
            set => SetProperty(ref _formNomorHp, value);
        }

        public string FormWhatsApp
        {
            get => _formWhatsApp;
            set => SetProperty(ref _formWhatsApp, value);
        }

        /// <summary>Nomor rekening bank perangkat desa (opsional).</summary>
        public string FormNomorRekening
        {
            get => _formNomorRekening;
            set => SetProperty(ref _formNomorRekening, value ?? string.Empty);
        }

        /// <summary>Nomor kepesertaan BPJS perangkat desa (opsional).</summary>
        public string FormNomorBpjs
        {
            get => _formNomorBpjs;
            set => SetProperty(ref _formNomorBpjs, value ?? string.Empty);
        }

        public string FormNomorSk
        {
            get => _formNomorSk;
            set => SetProperty(ref _formNomorSk, value);
        }

        public string FormTanggalSk
        {
            get => _formTanggalSk;
            set => SetProperty(ref _formTanggalSk, value ?? string.Empty);
        }

        /// <summary>Nomor rekomendasi pengangkatan (rekomendasi Kecamatan).</summary>
        public string FormNomorRekomendasi
        {
            get => _formNomorRekomendasi;
            set => SetProperty(ref _formNomorRekomendasi, value ?? string.Empty);
        }

        /// <summary>Tanggal rekomendasi pengangkatan (teks dd-MM-yyyy).</summary>
        public string FormTanggalRekomendasi
        {
            get => _formTanggalRekomendasi;
            set => SetProperty(ref _formTanggalRekomendasi, value ?? string.Empty);
        }

        public string FormMasaJabatanMulai
        {
            get => _formMasaJabatanMulai;
            set => SetProperty(ref _formMasaJabatanMulai, value ?? string.Empty);
        }

        public string FormMasaJabatanSelesai
        {
            get => _formMasaJabatanSelesai;
            set => SetProperty(ref _formMasaJabatanSelesai, value ?? string.Empty);
        }

        public string FormStatus
        {
            get => _formStatus;
            set => SetProperty(ref _formStatus, value);
        }

        public string FormCatatan
        {
            get => _formCatatan;
            set => SetProperty(ref _formCatatan, value);
        }

        // ---------- SK pada form ----------

        /// <summary>
        /// Benar bila jabatan terpilih mempunyai SK yang diterbitkan Bupati
        /// (Kepala Desa dan anggota BPD): berkas PDF-nya wajib dilampirkan saat
        /// menyimpan, sebab aplikasi tidak menerbitkan SK itu.
        /// </summary>
        public bool FormSkWajib =>
            SkJabatanAksiKatalog.SumberJabatan(FormJabatan) == SumberSkPerangkat.Bupati;

        /// <summary>
        /// Benar bila jabatan terpilih mempunyai SK yang diterbitkan Kepala Desa:
        /// SK jadi dibuat otomatis dari data form saat menyimpan, jadi form tidak
        /// menampilkan pilihan berkas lampiran sama sekali.
        /// </summary>
        public bool FormSkOtomatis =>
            !string.IsNullOrWhiteSpace(FormJabatan) && !FormSkWajib;

        /// <summary>Penjelasan pembuatan SK otomatis (info pada form).</summary>
        public string FormSkPesanOtomatis
        {
            get
            {
                string jabatan = FormJabatan;
                string kelompok = JabatanPerangkat.Kelompok(jabatan);
                if (!SkPerangkatKatalog.AdaUntuk(jabatan, kelompok))
                {
                    return $"Kelompok jabatan {NamaJabatanTampil(jabatan)} belum punya template SK, "
                         + "jadi SK tidak bisa dibuat otomatis saat menyimpan.";
                }

                string jabatanTampil = NamaJabatanTampil(jabatan);
                if (SkPerangkatKatalog.LazimBersama(kelompok))
                {
                    string kelompokTeks;
                    if (string.Equals(kelompok, "POSYANDU", StringComparison.Ordinal))
                    {
                        kelompokTeks = " Anggota dikelompokkan per Posyandu mengikuti dusunnya — "
                            + "jumlah posyandu mengikuti jumlah Kepala Dusun.";
                    }
                    else if (SkPerangkatKatalog.Untuk(jabatan, kelompok)?.BisaPenetapan == true)
                    {
                        kelompokTeks = " SK kelompok ini berbentuk Penetapan, sesuai contoh SK desanya.";
                    }
                    else
                    {
                        kelompokTeks = string.Empty;
                    }

                    return $"SK {jabatanTampil} tidak perlu dilampirkan: satu SK bersama untuk seluruh "
                         + "anggota dengan lampiran daftar nama dibuat otomatis dari data form ini "
                         + "(nomor dan tanggal SK) tepat saat data disimpan, lalu diarsipkan."
                         + kelompokTeks;
                }

                return $"SK {jabatanTampil} tidak perlu dilampirkan: aplikasi membuatnya "
                     + "otomatis dalam bentuk SK jadi dari data form ini (nomor dan tanggal SK, nama, "
                     + "NIK, wilayah, serta masa jabatan) tepat saat data disimpan, lalu mengarsipkannya.";
            }
        }

        /// <summary>Nama berkas SK pada form: pilihan baru, arsip lama, atau belum ada.</summary>
        public string FormSkNamaBerkas
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_formBerkasSkSumber))
                    return Path.GetFileName(_formBerkasSkSumber) + "   (belum diarsipkan)";
                if (BerkasSkPerangkat.JalurLengkap(_formBerkasSk) is not null)
                    return _formBerkasSk!;
                return "Belum ada berkas — wajib dilampirkan sebelum data disimpan.";
            }
        }

        /// <summary>Benar bila berkas SK bisa dibuka dari form (pilihan baru atau arsip lama).</summary>
        public bool FormSkAdaBerkas =>
            (!string.IsNullOrWhiteSpace(_formBerkasSkSumber) && File.Exists(_formBerkasSkSumber))
            || BerkasSkPerangkat.JalurLengkap(_formBerkasSk) is not null;


        // ---------- form tambah/ubah ----------

        private Task BukaFormTambahAsync() => BukaFormTambah(JabatanPerangkat.KepalaDesa);

        /// <summary>
        /// Buka form data diri mode tambah pada bagian yang sedang tampil,
        /// dengan jabatan awal bagian itu. Daftar jabatan form dibatasi
        /// bagiannya, jadi tambah orang tidak pernah menyatu dengan
        /// jabatan bagian lain.
        /// </summary>
        private async Task BukaFormTambah(string jabatanAwal)
        {
            await Task.Yield();

            // Form data diri dan panel SK sama-sama overlay; jangan bertumpuk.
            IsPanelSkTerbuka = false;
            KosongkanForm();
            IsModeUbah = false;

            var daftar = JabatanBagian(_bagianAktif);
            AturDaftarJabatanForm(daftar);
            // Jabatan awal bisa berasal dari pemanggil lain (mis. tombol Tambah
            // Perangkat pada bagian lain lewat panel kosong); bila tidak termasuk
            // bagian ini, pakai jabatan pertama bagian itu.
            FormJabatan = daftar.Contains(jabatanAwal, StringComparer.Ordinal)
                ? jabatanAwal
                : daftar.FirstOrDefault() ?? jabatanAwal;
            FormStatus = StatusPerangkat.Aktif;
            FormGalat = string.Empty;
            IsFormTerbuka = true;
        }

        /// <summary>
        /// Buka form dari satu baris: baris kosong berarti tambah baru dengan
        /// jabatannya sudah terpilih, baris berisi orang berarti ubah data
        /// orang itu.
        /// </summary>
        private async Task BukaFormDariBarisAsync(BarisPerangkatViewModel? baris)
        {
            if (baris is null) return;
            await Task.Yield();

            IsPanelSkTerbuka = false;
            // Pilihan jabatan dibatasi bagian baris ini, baik untuk menambah
            // (baris kosong) maupun mengubah orang yang sudah ada.
            AturDaftarJabatanForm(JabatanBagianJabatan(baris.JabatanTampil));

            if (baris.Sumber is null)
            {
                KosongkanForm();
                IsModeUbah = false;
                FormJabatan = baris.JabatanTampil;
                FormStatus = StatusPerangkat.Aktif;
                FormGalat = string.Empty;
                IsFormTerbuka = true;
                return;
            }

            BukaFormUbah(baris.Sumber);
        }

        private void BukaFormUbah(PerangkatDesa data)
        {
            _formId = data.ID;
            _formBerkasSk = data.BerkasSK;
            _formBerkasSkSumber = null;
            IsModeUbah = true;
            FormNama = data.Nama;
            FormJabatan = data.JabatanTampil;
            FormNip = data.NIP ?? string.Empty;
            FormNik = data.NIK ?? string.Empty;
            FormJenisKelamin = PerangkatDesa.JenisKelaminRingkas(data.JenisKelamin);
            FormTempatLahir = data.TempatLahir ?? string.Empty;
            FormTanggalLahir = TanggalKeTeks(data.TanggalLahir);
            FormPendidikan = data.Pendidikan ?? string.Empty;
            FormTahunLulus = data.TahunLulus ?? string.Empty;
            FormNoIjasah = data.NoIjasah ?? string.Empty;
            FormAgama = data.Agama ?? string.Empty;
            FormAlamat = data.Alamat ?? string.Empty;
            FormDusun = data.Dusun ?? string.Empty;
            FormRt = data.RT ?? string.Empty;
            FormRw = data.RW ?? string.Empty;
            FormUnit = data.Unit ?? string.Empty;
            FormNomorHp = data.NomorHP ?? string.Empty;
            FormWhatsApp = data.WhatsApp ?? string.Empty;
            FormNomorRekening = data.NomorRekening ?? string.Empty;
            FormNomorBpjs = data.NomorBPJS ?? string.Empty;
            FormNomorSk = data.NomorSK ?? string.Empty;
            FormTanggalSk = TanggalKeTeks(data.TanggalSK);
            FormNomorRekomendasi = data.NomorRekomendasi ?? string.Empty;
            FormTanggalRekomendasi = TanggalKeTeks(data.TanggalRekomendasi);
            FormMasaJabatanMulai = TanggalKeTeks(data.MasaJabatanMulai);
            FormMasaJabatanSelesai = TanggalKeTeks(data.MasaJabatanSelesai);
            FormStatus = data.StatusTampil;
            FormCatatan = data.Catatan ?? string.Empty;
            FormGalat = string.Empty;
            SegarkanFormSk();

            IsFormTerbuka = true;
        }

        private void TutupForm()
        {
            IsFormTerbuka = false;
            FormGalat = string.Empty;
            KosongkanForm();
        }

        private void KosongkanForm()
        {
            _formId = 0;
            _formBerkasSk = null;
            _formBerkasSkSumber = null;
            FormNama = string.Empty;
            CariWarga = string.Empty;
            HasilWarga.Clear();
            FormJabatan = string.Empty;
            FormNip = string.Empty;
            FormNik = string.Empty;
            FormJenisKelamin = string.Empty;
            FormTempatLahir = string.Empty;
            FormTanggalLahir = string.Empty;
            FormPendidikan = string.Empty;
            FormTahunLulus = string.Empty;
            FormNoIjasah = string.Empty;
            FormAgama = string.Empty;
            FormAlamat = string.Empty;
            FormDusun = string.Empty;
            FormRt = string.Empty;
            FormRw = string.Empty;
            FormUnit = string.Empty;
            FormNomorHp = string.Empty;
            FormWhatsApp = string.Empty;
            FormNomorRekening = string.Empty;
            FormNomorBpjs = string.Empty;
            FormNomorSk = string.Empty;
            FormTanggalSk = string.Empty;
            FormNomorRekomendasi = string.Empty;
            FormTanggalRekomendasi = string.Empty;
            FormMasaJabatanMulai = string.Empty;
            FormMasaJabatanSelesai = string.Empty;
            FormStatus = StatusPerangkat.Aktif;
            FormCatatan = string.Empty;
            SegarkanFormSk();
        }

        private async Task SimpanAsync()
        {
            // SK Bupati (Kepala Desa & anggota BPD) harus sudah dilampirkan;
            // SK jabatan lain dibuat aplikasi dari data form saat disimpan.
            bool skDariBupati = FormSkWajib;
            bool arsipLamaAda = BerkasSkPerangkat.JalurLengkap(_formBerkasSk) is not null;

            if (skDariBupati)
            {
                if (_formBerkasSkSumber is null && !arsipLamaAda)
                {
                    FormGalat = "Jabatan ini mempunyai SK yang diterbitkan Bupati, jadi berkas PDF SK-nya "
                        + "wajib dilampirkan sebelum data disimpan. Tekan “Lampirkan SK” lalu pilih berkasnya.";
                    return;
                }
                if (_formBerkasSkSumber is not null && !File.Exists(_formBerkasSkSumber))
                {
                    FormGalat = "Berkas SK yang dipilih tidak ditemukan lagi. Pilih ulang berkasnya.";
                    return;
                }
            }

            // Seluruh isian tanggal form berupa teks; dibaca sekaligus di sini
            // supaya semua kesalahan format tampil bersamaan sebelum data disusun.
            if (!BacaTanggalForm(out DateTime? tanggalLahir, out DateTime? tanggalSk,
                    out DateTime? tanggalRekomendasi, out DateTime? masaJabatanMulai,
                    out DateTime? masaJabatanSelesai, out string pesanTanggal))
            {
                FormGalat = pesanTanggal;
                return;
            }

            var data = new PerangkatDesa
            {
                ID = _formId,
                Nama = FormNama,
                Jabatan = FormJabatan,
                NIP = FormNip,
                NIK = FormNik,
                JenisKelamin = FormJenisKelamin,
                TempatLahir = FormTempatLahir,
                TanggalLahir = tanggalLahir,
                Agama = FormAgama,
                Pendidikan = FormPendidikan,
                TahunLulus = FormTahunLulus,
                NoIjasah = FormNoIjasah,
                Alamat = FormAlamat,
                Dusun = FormDusun,
                RT = FormRt,
                RW = FormRw,
                Unit = FormUnit,
                NomorHP = FormNomorHp,
                WhatsApp = FormWhatsApp,
                NomorSK = FormNomorSk,
                TanggalSK = tanggalSk,
                NomorRekomendasi = FormNomorRekomendasi,
                TanggalRekomendasi = tanggalRekomendasi,
                NomorRekening = FormNomorRekening,
                NomorBPJS = FormNomorBpjs,
                // Form tidak memuat berkas, jadi nama berkas arsip dibawa dari
                // baris yang dibuka. Tanpa ini, setiap penyimpanan form akan
                // mengosongkan BerkasSK dan memutus tautan ke PDF SK tersimpan.
                BerkasSK = _formBerkasSk,
                MasaJabatanMulai = masaJabatanMulai,
                MasaJabatanSelesai = masaJabatanSelesai,
                Status = FormStatus,
                Catatan = FormCatatan,
                DiperbaruiOleh = SessionContext.Display
            };

            bool modeUbah = IsModeUbah;
            try
            {
                IsBusy = true;
                FormGalat = string.Empty;

                var hasil = await _layanan.SimpanAsync(data, modeUbah).ConfigureAwait(true);
                if (!hasil.Berhasil)
                {
                    FormGalat = hasil.Kekurangan.Count > 0
                        ? string.Join(Environment.NewLine, hasil.Kekurangan)
                        : hasil.Pesan ?? "Data belum bisa disimpan.";
                    return;
                }

                _activityLog.Log("PERANGKAT", $"{hasil.Data!.Nama} ({hasil.Data.JabatanTampil})",
                    modeUbah ? "Ubah" : "Tambah",
                    modeUbah ? $"ID {hasil.Data.ID} diperbarui" : $"ID {hasil.Data.ID} ditambahkan");

                bool skDibuat = false;

                // SK Bupati (Kepala Desa & anggota BPD): salin berkas terpilih
                // ke folder arsip lalu tulis namanya pada baris yang tersimpan.
                if (skDariBupati && _formBerkasSkSumber is not null)
                {
                    string? namaArsip = null;
                    try
                    {
                        namaArsip = BerkasSkPerangkat.Arsipkan(data.ID, _formBerkasSkSumber);
                        data.BerkasSK = namaArsip;
                        var ulang = await _layanan.SimpanAsync(data, modeUbah: true).ConfigureAwait(true);
                        if (!ulang.Berhasil)
                        {
                            throw new InvalidOperationException(ulang.Kekurangan.Count > 0
                                ? string.Join(Environment.NewLine, ulang.Kekurangan)
                                : ulang.Pesan ?? "nama berkas SK belum tertulis pada data.");
                        }

                        _formBerkasSk = namaArsip;
                        _formBerkasSkSumber = null;
                        _activityLog.Log("PERANGKAT", $"{data.Nama} ({data.JabatanTampil})",
                            "Lampirkan SK Bupati", namaArsip);
                    }
                    catch (Exception ex)
                    {
                        if (namaArsip is not null)
                        {
                            BerkasSkPerangkat.Hapus(namaArsip);
                            data.BerkasSK = _formBerkasSk;
                        }

                        _logger.LogError(ex, "Gagal mengarsipkan berkas SK Bupati {Nama}", data.Nama);
                        // Datanya sudah tersimpan; lanjutkan sebagai mode ubah supaya
                        // tekan Simpan berikutnya memperbarui baris ini, bukan ganda.
                        _formId = data.ID;
                        IsModeUbah = true;
                        await LoadAsync().ConfigureAwait(true);
                        FormGalat = "Data tersimpan, tetapi berkas SK Bupati belum terarsipkan: " + ex.Message
                            + " Tekan Simpan sekali lagi untuk mengarsipkan berkasnya.";
                        return;
                    }
                }
                // SK jabatan lain: buat SK jadi dari data form (bukan contoh)
                // lalu simpan PDF-nya sebagai berkas arsip baris ini.
                else if (!skDariBupati)
                {
                    try
                    {
                        skDibuat = await BuatSkJadiAsync(data).ConfigureAwait(true);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Gagal membuat SK jadi {Nama} ({Jabatan})",
                            data.Nama, data.JabatanTampil);
                        _formId = data.ID;
                        IsModeUbah = true;
                        await LoadAsync().ConfigureAwait(true);
                        FormGalat = "Data tersimpan, tetapi SK jadi belum berhasil dibuat: " + ex.Message
                            + " Bila berkas SK sedang dibuka di pratinjau, tutup dulu lalu tekan Simpan sekali lagi.";
                        return;
                    }
                }

                TutupForm();
                await LoadAsync().ConfigureAwait(true);
                StatusText = modeUbah
                    ? $"Data {hasil.Data.Nama} diperbarui."
                    : $"{hasil.Data.Nama} ditambahkan sebagai {hasil.Data.JabatanTampil}.";
                if (skDibuat)
                {
                    StatusText += " SK jadi dibuat dan diarsipkan.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyimpan perangkat desa {Nama}", data.Nama);
                FormGalat = "Data belum tersimpan. Penyebabnya: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        // ---------- SK otomatis & lampiran SK Bupati pada form ----------

        /// <summary>
        /// Pilih berkas PDF SK Bupati pada form. Berkasnya belum disalin ke
        /// folder arsip sebab baris baru belum punya ID — penyalinan dilakukan
        /// oleh <see cref="SimpanAsync"/> tepat setelah data tersimpan.
        /// </summary>
        private async Task LampirkanSkFormAsync()
        {
            await Task.Yield();

            var ofd = new OpenFileDialog
            {
                Title = "Pilih berkas PDF SK Bupati",
                Filter = "Berkas PDF (*.pdf)|*.pdf",
                CheckFileExists = true,
                Multiselect = false
            };
            if (ofd.ShowDialog() != true) return;

            _formBerkasSkSumber = ofd.FileName;
            BersihkanGalat();
            SegarkanFormSk();
        }

        /// <summary>Buka berkas SK pada form (pilihan baru atau arsip lama) di pratinjau SudesPdf.</summary>
        private void BukaSkForm()
        {
            string? jalur = !string.IsNullOrWhiteSpace(_formBerkasSkSumber) && File.Exists(_formBerkasSkSumber)
                ? _formBerkasSkSumber
                : BerkasSkPerangkat.JalurLengkap(_formBerkasSk);

            if (jalur is null)
            {
                FormGalat = "Berkas SK tidak ditemukan. Pilih ulang berkasnya.";
                return;
            }

            _navigation.Navigate(_previewFactory($"SK {NamaJabatanTampil(FormJabatan)}", jalur));
        }

        /// <summary>Segarkan turunan form yang bergantung pada pilihan berkas SK.</summary>
        private void SegarkanFormSk()
        {
            OnPropertyChanged(nameof(FormSkWajib));
            OnPropertyChanged(nameof(FormSkOtomatis));
            OnPropertyChanged(nameof(FormSkPesanOtomatis));
            OnPropertyChanged(nameof(FormSkNamaBerkas));
            OnPropertyChanged(nameof(FormSkAdaBerkas));
            BukaSkFormCommand.RaiseCanExecuteChanged();
        }


    }
}
