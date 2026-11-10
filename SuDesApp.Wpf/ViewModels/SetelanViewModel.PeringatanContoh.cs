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
        // Peringatan data contoh
        // =================================================================

        private string _pesanPeringatanContoh = string.Empty;

        /// <summary>Benar bila masih ada kolom wajib yang berisi contoh bawaan aplikasi.</summary>
        public bool AdaPeringatanContoh => _pesanPeringatanContoh.Length > 0;

        /// <summary>Pesan yang meminta pengguna mengganti data contoh dengan data desanya.</summary>
        public string PesanPeringatanContoh
        {
            get => _pesanPeringatanContoh;
            private set
            {
                if (SetProperty(ref _pesanPeringatanContoh, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(AdaPeringatanContoh));
                }
            }
        }

        /// <summary>Keterangan Pejabat Kecamatan: sengaja tidak diisi contoh dan boleh dikosongkan.</summary>
        public string InfoPejabatKecamatan =>
            "Kolom Pejabat Kecamatan boleh dikosongkan — tidak semua surat membutuhkannya, dan " +
            "aplikasi tidak mengisinya dengan data contoh. Isi hanya bila kantor desa memakai nama camat " +
            "pada surat (mis. surat yang disetujui/diketahui camat).";

        /// <summary>
        /// Keterangan kolom email: opsional, dan bila diisi alamatnya dicetak biru pada
        /// baris alamat kop surat.
        /// </summary>
        public string InfoEmail =>
            "Opsional — boleh dikosongkan. Bila diisi, email dicetak pada baris alamat kop surat " +
            "dengan warna biru (mis. desa@sumberjaya.id).";

        /// <summary>Benar bila salah satu data Pejabat Kecamatan sudah diisi pengguna.</summary>
        public bool AdaPejabatKecamatan =>
            !string.IsNullOrWhiteSpace(_namaCamat) ||
            !string.IsNullOrWhiteSpace(_nipCamat) ||
            !string.IsNullOrWhiteSpace(_golCamat);

        /// <summary>
        /// Perbarui peringatan data contoh dan penanda tiap bagian pada navigasi.
        /// Dipanggil setiap kali isian berubah supaya pengguna langsung melihat
        /// mana yang sudah beres dan mana yang masih contoh.
        /// </summary>
        private void PerbaruiPeringatanContoh()
        {
            var fieldContoh = DesaContoh.FieldContoh(DariForm());

            PesanPeringatanContoh = fieldContoh.Count == 0
                ? string.Empty
                : "Data desa masih memakai contoh bawaan aplikasi: " + string.Join(", ", fieldContoh) +
                  ". Ganti dengan data desa Anda yang sebenarnya supaya surat yang dicetak tidak memakai " +
                  "data contoh.";

            PerbaruiLencanaBagian(fieldContoh);
            OnPropertyChanged(nameof(AdaPejabatKecamatan));

            // Pratinjau ikut isian terbaru: nama desa, pejabat, dan logo terlihat
            // seketika di layar, belum perlu disimpan dulu.
            PerbaruiPratinjau();
        }

        /// <summary>Ringkasan keadaan tiap bagian pada navigasi kiri.</summary>
        private void PerbaruiLencanaBagian(System.Collections.Generic.List<string> fieldContoh)
        {
            if (Bagian.Count < 4) return;

            var kelompokDesa = new[] { "Nama Desa", "Kecamatan", "Kabupaten", "Alamat Desa" };
            var kelompokPejabat = new[] { "Kepala Desa", "Sekretaris Desa" };

            int desa = fieldContoh.Count(f => kelompokDesa.Contains(f));
            int pejabat = fieldContoh.Count(f => kelompokPejabat.Contains(f));

            Lencana(Bagian[0], desa, "Kolom masih contoh", "Lengkap");
            Lencana(Bagian[1], pejabat, "Kolom masih contoh", "Lengkap");

            Bagian[2].Lencana = AdaPejabatKecamatan ? "Terisi (opsional)" : "Dikosongkan";
            Bagian[2].LencanaPeringatan = false;

            string kertas = _ukuranKertas == UkuranKertasSurat.F4 ? "Kertas F4 / Folio" : "Kertas A4";
            string logo = !AdaLogo
                ? "tanpa logo"
                : string.IsNullOrWhiteSpace(PengaturanCetak.GetJalurLogo()) ? "logo bawaan" : "logo Anda";
            Bagian[3].Lencana = $"{kertas} · {logo}";
            Bagian[3].LencanaPeringatan = false;
        }

        private static void Lencana(
            BagianSetelanItemViewModel bagian, int jumlahContoh, string pesanContoh, string pesanLengkap)
        {
            if (jumlahContoh > 0)
            {
                bagian.Lencana = $"{jumlahContoh} {pesanContoh}";
                bagian.LencanaPeringatan = true;
            }
            else
            {
                bagian.Lencana = pesanLengkap;
                bagian.LencanaPeringatan = false;
            }
        }

        /// <summary>Hitung ulang HasChanges dengan membandingkan nilai form terhadap snapshot tersimpan.</summary>
        private void OnFieldChanged()
        {
            HasChanges = HasDataChanged();
            PerbaruiPeringatanContoh();
        }

        /// <summary>Padanan HasDataChanged WinForms: bandingkan (trim + ignore case) terhadap snapshot.</summary>
        private bool HasDataChanged()
        {
            return !string.Equals(_namaDesa?.Trim(), _snapshot.NamaDesa, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(_kecamatan?.Trim(), _snapshot.Kecamatan, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(_kabupaten?.Trim(), _snapshot.Kabupaten, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(_jenisKabupaten, _snapshot.JenisKabupaten, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(_alamat?.Trim(), _snapshot.Alamat, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(_kodepos?.Trim(), _snapshot.Kodepos, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(_email?.Trim(), _snapshot.Email, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(_kepalaDesa?.Trim(), _snapshot.KepalaDesa, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(_sekdes?.Trim(), _snapshot.SekretarisDesa, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(_namaCamat?.Trim(), _snapshot.NamaCamat, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(_nipCamat?.Trim(), _snapshot.NipCamat, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(_golCamat?.Trim(), _snapshot.GolCamat, StringComparison.OrdinalIgnoreCase) ||
                   // Jenis lembaga dan NIP pejabat ikut mengikatan tombol Simpan.
                   !string.Equals(_jenisLembaga, _snapshot.JenisLembaga, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(_nipKepalaDesa?.Trim(), _snapshot.NipKepalaDesa, StringComparison.OrdinalIgnoreCase);
        }

        private async Task LoadAsync()
        {
            try
            {
                IsLoading = true;

                // Baca database di utas pemanggil (tidak menahan UI lebih dari
                // sekali), lalu SELURUH penerapan ke halaman dijalankan di utas
                // halaman — isian, tombol Simpan, dan pratinjau adalah keadaan
                // milik halaman itu.
                var settings = await _settingsManager.GetSettingsAsync();

                // Nomor surat yang diatur per wilayah mengikuti kode kabupaten/kota
                // yang benar-benar tersimpan pada data desa. Nama yang diketik manual
                // tidak cukup untuk memilih aturan hukum secara determin deterministic.
                SuDesApp.Configuration.PenomoranOverrideStore.GunakanDaerah(settings?.KodeKabupaten);
                var kodeBaru = settings?.KodeKabupaten;
                var kodeLama = _kodeKabupatenTerakhir;
                if (!string.IsNullOrWhiteSpace(kodeBaru) && _jenisSuratConfig != null
                    && !string.Equals(kodeBaru, kodeLama, StringComparison.Ordinal))
                {
                    _kodeKabupatenTerakhir = kodeBaru;
                    await _jenisSuratConfig.RefreshConfigAsync();
                }

                await JalankanDiUtasHalamanAsync(async () =>
                {
                    TerapkanDataDesa(settings ?? new DesaData());

                    // Kenali wilayah desa yang tersimpan dari data wilayah bawaan supaya
                    // kunjungan berikutnya cukup memilih dari daftar.
                    await SiapkanPilihanWilayahAsync(_snapshot);

                    // Bagian yang masih berisi contoh langsung dibuka supaya pengguna
                    // tahu apa yang harus diganti.
                    if (DesaContoh.MasihContoh(_snapshot))
                    {
                        var fieldContoh = DesaContoh.FieldContoh(_snapshot);
                        bool pejabatDulu = fieldContoh.Contains("Kepala Desa") || fieldContoh.Contains("Sekretaris Desa");
                        bool desaMasihKosong = fieldContoh.Exists(f =>
                            f is "Nama Desa" or "Kecamatan" or "Kabupaten" or "Alamat Desa");
                        TampilkanBagian(desaMasihKosong || !pejabatDulu ? 0 : 1);
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal memuat pengaturan desa");
                await _messageService.ShowErrorAsync("Gagal memuat pengaturan: " + ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Pasang data desa tersimpan ke seluruh isian halaman beserta lencana dan
        /// pratinjaunya. Dipanggil hanya dari utas halaman (lihat
        /// <see cref="JalankanDiUtasHalamanAsync(Action)"/>).
        /// </summary>
        private void TerapkanDataDesa(DesaData data)
        {
            _snapshot = data;

            _namaDesa = _snapshot.NamaDesa ?? string.Empty;
            _kecamatan = _snapshot.Kecamatan ?? string.Empty;
            _kabupaten = _snapshot.Kabupaten ?? string.Empty;
            _jenisKabupaten = _snapshot.JenisKabupaten ?? string.Empty;
            _alamat = _snapshot.Alamat ?? string.Empty;
            _kodepos = _snapshot.Kodepos ?? string.Empty;
            _email = _snapshot.Email ?? string.Empty;
            _kepalaDesa = _snapshot.KepalaDesa ?? string.Empty;
            _sekdes = _snapshot.SekretarisDesa ?? string.Empty;
            _namaCamat = _snapshot.NamaCamat ?? string.Empty;
            _nipCamat = _snapshot.NipCamat ?? string.Empty;
            _golCamat = _snapshot.GolCamat ?? string.Empty;
            _nipKepalaDesa = _snapshot.NipKepalaDesa ?? string.Empty;

            OnPropertyChanged(nameof(NamaDesa));
            OnPropertyChanged(nameof(Kecamatan));
            OnPropertyChanged(nameof(Kabupaten));
            SegarkanJenisKabupaten();
            SegarkanJenisLembaga();
            OnPropertyChanged(nameof(NipKepalaDesa));
            OnPropertyChanged(nameof(Alamat));
            OnPropertyChanged(nameof(Kodepos));
            OnPropertyChanged(nameof(Email));
            OnPropertyChanged(nameof(KepalaDesa));
            OnPropertyChanged(nameof(Sekdes));
            OnPropertyChanged(nameof(NipKepalaDesa));
            OnPropertyChanged(nameof(NamaCamat));
            OnPropertyChanged(nameof(NipCamat));
            OnPropertyChanged(nameof(GolCamat));
            MuatPengaturanCetak();
            HasChanges = false;
            PerbaruiPeringatanContoh();
        }

        /// <summary>
        /// Batal: buang seluruh perubahan yang belum disimpan, kembalikan form
        /// ke nilai tersimpan terakhir (snapshot) — tanpa menyentuh database —
        /// lalu tutup halaman kembali ke tampilan default.
        /// </summary>
        private async Task CancelAsync()
        {
            try
            {
                IsLoading = true;

                _namaDesa = _snapshot.NamaDesa ?? string.Empty;
                _kecamatan = _snapshot.Kecamatan ?? string.Empty;
                _kabupaten = _snapshot.Kabupaten ?? string.Empty;
                _jenisKabupaten = _snapshot.JenisKabupaten ?? string.Empty;
                _alamat = _snapshot.Alamat ?? string.Empty;
                _kodepos = _snapshot.Kodepos ?? string.Empty;
                _email = _snapshot.Email ?? string.Empty;
            _kepalaDesa = _snapshot.KepalaDesa ?? string.Empty;
            _sekdes = _snapshot.SekretarisDesa ?? string.Empty;
            _namaCamat = _snapshot.NamaCamat ?? string.Empty;
            _nipCamat = _snapshot.NipCamat ?? string.Empty;
            _golCamat = _snapshot.GolCamat ?? string.Empty;
            _nipKepalaDesa = _snapshot.NipKepalaDesa ?? string.Empty;

            OnPropertyChanged(nameof(NamaDesa));
            OnPropertyChanged(nameof(Kecamatan));
            OnPropertyChanged(nameof(Kabupaten));
            SegarkanJenisKabupaten();
            SegarkanJenisLembaga();
            OnPropertyChanged(nameof(NipKepalaDesa));
                await SiapkanPilihanWilayahAsync(_snapshot);

                // Halaman ditutup dari utas halaman, sama seperti semua perubahan
                // keadaan lain di kelas ini.
                await JalankanDiUtasHalamanAsync(() => _navigation.ShowDefault());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal membatalkan perubahan pengaturan desa");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task SaveAsync()
        {
            if (!ValidateRequiredFields())
            {
                return;
            }

            try
            {
                IsLoading = true;
                var desaData = DariForm();
                await _settingsManager.SaveSettingsAsync(desaData);
                var kodeDaerahSebelum = _snapshot.KodeKabupaten;
                SuDesApp.Configuration.PenomoranOverrideStore.GunakanDaerah(desaData.KodeKabupaten);
                if (!string.Equals(kodeDaerahSebelum, desaData.KodeKabupaten, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(desaData.KodeKabupaten) && _jenisSuratConfig != null)
                    await _jenisSuratConfig.RefreshConfigAsync();

                // Penerapan setelah simpan mengubah lencana, pratinjau, dan statusbar:
                // keadaan halaman, jadi dijalankan di utas halaman.
                await JalankanDiUtasHalamanAsync(() =>
                {
                    _snapshot = desaData;
                    HasChanges = false;
                    PerbaruiPeringatanContoh();
                    SettingsSaved?.Invoke();
                    _logger.LogInformation("Pengaturan desa berhasil disimpan");

                    // Konfirmasi cukup lewat statusbar halaman ini — dialog yang muncul
                    // setiap kali Simpan terlalu mengganggu alur kerja.
                    StatusPesan = $"Pengaturan desa tersimpan — {DateTime.Now:HH:mm:ss}";
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyimpan pengaturan desa");
                await _messageService.ShowErrorAsync("Gagal menyimpan pengaturan: " + ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Padanan ValidateRequiredFields WinForms: field wajib terisi, kodepos 5
        /// digit (opsional), NIP camat minimal 9 digit (opsional). Mengembalikan
        /// true bila valid; bila tidak, menampilkan peringatan berisi daftar kesalahan.
        /// </summary>
        private bool ValidateRequiredFields()
        {
            var errorMessages = new List<string>();

            ValidateField(NamaDesa, "Nama Desa", errorMessages);
            ValidateField(Alamat, "Alamat Desa", errorMessages);
            ValidateField(Kecamatan, "Kecamatan", errorMessages);
            ValidateField(Kabupaten, "Kabupaten", errorMessages);
            ValidateField(KepalaDesa, "Kepala Desa", errorMessages);
            ValidateField(Sekdes, "Sekretaris Desa", errorMessages);

            if (!string.IsNullOrWhiteSpace(Kodepos) && !_kodeposRegex.IsMatch(Kodepos.Trim()))
            {
                errorMessages.Add("Kodepos harus terdiri dari 5 digit angka");
            }

            // Surel opsional: hanya diperiksa bila kolomnya memang diisi, supaya
            // kekeliruan ketik tidak terlanjur tercetak di kop semua surat.
            if (!string.IsNullOrWhiteSpace(Email) && !_emailRegex.IsMatch(Email.Trim()))
            {
                errorMessages.Add("Email desa tidak valid — contoh yang benar: desa@sumberjaya.id");
            }

            if (!string.IsNullOrWhiteSpace(NipCamat) && NipCamat.Trim().Length < 9)
            {
                errorMessages.Add("NIP Camat minimal 9 digit");
            }

            // NIP pejabat desa/kelurahan: wajib cukup panjang HANYA bila diisi, dan
            // bermanfaat saat lembaganya kelurahan (Lurah, ASN) — NIP yang tercetak.
            if (!string.IsNullOrWhiteSpace(NipKepalaDesa) && NipKepalaDesa.Trim().Length < 9)
            {
                errorMessages.Add("NIP pejabat minimal 9 digit");
            }

            if (errorMessages.Count > 0)
            {
                var errorMessage = "Perbaiki data berikut:\n\u2022 " + string.Join("\n\u2022 ", errorMessages);
                _ = _messageService.ShowWarningAsync(errorMessage);
                return false;
            }

            return true;
        }

        private static void ValidateField(string? fieldValue, string fieldName, List<string> errorMessages)
        {
            if (!Validator.ValidateRequired(fieldValue!, fieldName, out var msg))
            {
                errorMessages.Add(msg);
            }
        }

    }
}
