using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SudesExPlus;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;

namespace SuDesApp.Services
{
    public partial class ImporWargaService : IImporWargaService
    {
        // ------------------------------------------------------------------
        // Tahap 3: periksa tiap baris
        // ------------------------------------------------------------------

        private async Task<HasilImporWarga> PeriksaAsync(
            string namaBerkas,
            List<string[]> tabel,
            bool perbaruiYangSudahAda,
            CancellationToken ct)
        {
            if (tabel.Count == 0) return Gagal(namaBerkas, "Berkas tidak berisi data.");

            var indeksKolom = new Dictionary<string, int>(StringComparer.Ordinal);
            var tidakDikenali = new List<string>();
            var sudahDilog = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string[] judul = tabel[0];
            for (int c = 0; c < judul.Length; c++)
            {
                string kunci = BersihkanJudul(judul[c] ?? string.Empty);
                if (kunci.Length == 0 || KolomDiabaikan.Contains(kunci)) continue;

                if (PetakanKolom.TryGetValue(kunci, out string? field))
                {
                    // Kolom pertama yang dikenali menang; judul kembar diabaikan.
                    indeksKolom.TryAdd(field, c);
                }
                else
                {
                    string asli = (judul[c] ?? string.Empty).Trim();
                    if (asli.Length > 0 && sudahDilog.Add(asli))
                        tidakDikenali.Add(asli);
                }
            }

            if (indeksKolom.Count == 0)
            {
                return new HasilImporWarga
                {
                    NamaBerkas = namaBerkas,
                    Baris = Array.Empty<BarisImporWarga>(),
                    KolomTidakDikenali = tidakDikenali,
                    KesalahanBerkas =
                        "Judul kolom tidak ada yang dikenali. Kolom yang diharapkan: NIK, Nama, " +
                        "No Kartu Keluarga, Jenis Kelamin, Tempat Lahir, Tanggal Lahir, Agama, " +
                        "Golongan Darah, dan seterusnya. Cara termudah: ekspor warga dari aplikasi, " +
                        "ubah isinya, lalu impor kembali."
                };
            }

            if (!indeksKolom.ContainsKey("NIK"))
            {
                return new HasilImporWarga
                {
                    NamaBerkas = namaBerkas,
                    Baris = Array.Empty<BarisImporWarga>(),
                    KolomTidakDikenali = tidakDikenali,
                    KesalahanBerkas = "Kolom NIK wajib ada di berkas impor."
                };
            }

            var nikTersedia = new Dictionary<string, bool>(StringComparer.Ordinal);
            var nikDiBerkas = new Dictionary<string, int>(StringComparer.Ordinal);
            var hasilBaris = new List<BarisImporWarga>(tabel.Count - 1);

            for (int i = 1; i < tabel.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                // Repository ini bekerja sinkron di atas SQLite, jadi antrean
                // yang panjang akan membekukan antarmuka. Melepas kendali
                // sesekali membuat jendela tetap responsif dan progres terlihat.
                if (i % 200 == 0)
                {
                    await Task.Yield();
                    ct.ThrowIfCancellationRequested();
                }

                hasilBaris.Add(await PeriksaBarisAsync(
                    i + 1, tabel[i], indeksKolom, nikDiBerkas, nikTersedia,
                    perbaruiYangSudahAda, ct).ConfigureAwait(false));
            }

            return new HasilImporWarga
            {
                NamaBerkas = namaBerkas,
                Baris = hasilBaris,
                KolomTidakDikenali = tidakDikenali
            };
        }

        private async Task<BarisImporWarga> PeriksaBarisAsync(
            int nomorBaris,
            string[] baris,
            Dictionary<string, int> indeksKolom,
            Dictionary<string, int> nikDiBerkas,
            Dictionary<string, bool> nikTersedia,
            bool perbaruiYangSudahAda,
            CancellationToken ct)
        {
            string Ambil(string field) =>
                indeksKolom.TryGetValue(field, out int idx) && idx < baris.Length
                    ? (baris[idx] ?? string.Empty).Trim()
                    : string.Empty;

            var hasil = new BarisImporWarga
            {
                NomorBaris = nomorBaris,
                NIK = Ambil("NIK"),
                Nama = Ambil("Nama")
            };

            // --- NIK: wajib 16 digit angka, bukan nilai internal -------------
            string nik = hasil.NIK ?? string.Empty;
            bool nikBisaDiperiksa;

            if (nik.Length == 0)
            {
                hasil.Kesalahan.Add("NIK kosong.");
                nikBisaDiperiksa = false;
            }
            else if (!nik.All(char.IsDigit))
            {
                hasil.Kesalahan.Add("NIK hanya boleh berisi angka.");
                nikBisaDiperiksa = false;
            }
            else if (nik.Length != 16)
            {
                hasil.Kesalahan.Add($"NIK harus 16 digit, terbaca {nik.Length} digit.");
                nikBisaDiperiksa = false;
            }
            else if (NikCadangan.Contains(nik))
            {
                hasil.Kesalahan.Add("NIK ini dipakai untuk instansi atau kematian, bukan warga.");
                nikBisaDiperiksa = false;
            }
            else if (nikDiBerkas.TryGetValue(nik, out int barisPertama))
            {
                hasil.Kesalahan.Add($"NIK duplikat di berkas, sudah muncul di baris {barisPertama}.");
                nikBisaDiperiksa = false;
            }
            else
            {
                nikDiBerkas[nik] = nomorBaris;
                nikBisaDiperiksa = true;
            }

            // --- Tanggal lahir ------------------------------------------------
            string teksTanggal = Ambil("TanggalLahir");
            DateTime? tanggalLahir = null;
            if (teksTanggal.Length > 0 && !CobaParseTanggal(teksTanggal, out tanggalLahir))
                hasil.Kesalahan.Add($"Tanggal lahir '{teksTanggal}' tidak dikenali, contoh yang benar 17/08/1990.");

            // --- Jenis kelamin -------------------------------------------------
            string jenisKelamin = Ambil("JenisKelamin");
            string? jkNormal = NormalisasiJenisKelamin(jenisKelamin);
            if (jenisKelamin.Length > 0 && jkNormal == null)
                hasil.Kesalahan.Add($"Jenis kelamin '{jenisKelamin}' tidak dikenali, isi L, P, Laki-laki, atau Perempuan.");

            // --- Golongan darah -------------------------------------------------
            string golonganDarah = Ambil("GolonganDarah");
            string? goldahNormal = NormalisasiGolonganDarah(golonganDarah);
            if (golonganDarah.Length > 0 && goldahNormal == null)
                hasil.Kesalahan.Add($"Golongan darah '{golonganDarah}' tidak dikenali, isi A, B, AB, atau O.");

            // --- Status warga ----------------------------------------------------
            string status = Ambil("StatusWarga");
            if (status.Length > 0 && !StatusWargaTipe.Valid(status))
                hasil.Kesalahan.Add(
                    $"Status warga '{status}' tidak dikenali, isi {string.Join(", ", StatusWargaTipe.Semua)}.");

            if (hasil.AdaKesalahan) return hasil;

            // --- Batas panjang kolom (dari atribut model) -----------------------
            foreach (var (kolom, nilai, batas) in new (string, string?, int)[]
            {
                ("Nama", hasil.Nama, 100),
                ("Tempat Lahir", Ambil("TempatLahir"), 100),
                ("Pekerjaan", Ambil("Pekerjaan"), 100),
                ("Pendidikan", Ambil("Pendidikan"), 100),
                ("Dusun", Ambil("Dusun"), 100),
                ("Desa", Ambil("Desa"), 100),
                ("Kecamatan", Ambil("Kecamatan"), 100),
                ("Kabupaten", Ambil("Kabupaten"), 100),
                ("Nama Ayah", Ambil("NamaAyah"), 100),
                ("Nama Ibu", Ambil("NamaIbu"), 100),
                ("No Kartu Keluarga", Ambil("NoKK"), 16),
                ("Nomor HP", Ambil("NomorHP"), 20),
                ("RT", Ambil("RT"), 10),
                ("RW", Ambil("RW"), 10),
                ("Alamat (jalan/kampung)", Ambil("AlamatDetail"), 200),
                ("Keterangan", Ambil("KeteranganWarga"), 300)
            })
            {
                if (nilai is { Length: > 0 } && nilai.Length > batas)
                    hasil.Kesalahan.Add($"Kolom '{kolom}' melebihi {batas} karakter ({nilai.Length}).");
            }

            if (hasil.AdaKesalahan) return hasil;

            hasil.Data = new WargaData
            {
                NIK = nik,
                Nama = hasil.Nama!.Trim(),
                TempatLahir = Kosongkan(Ambil("TempatLahir")),
                TanggalLahir = tanggalLahir?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                JenisKelamin = jkNormal,
                Agama = Kosongkan(Ambil("Agama")),
                GolonganDarah = goldahNormal,
                StatusPerkawinan = Kosongkan(Ambil("StatusPerkawinan")),
                Pekerjaan = Kosongkan(Ambil("Pekerjaan")),
                Pendidikan = Kosongkan(Ambil("Pendidikan")),
                NamaAyah = Kosongkan(Ambil("NamaAyah")),
                NamaIbu = Kosongkan(Ambil("NamaIbu")),
                NomorHP = Kosongkan(Ambil("NomorHP")),
                NoKK = Kosongkan(Ambil("NoKK")),
                RT = Kosongkan(Ambil("RT")),
                RW = Kosongkan(Ambil("RW")),
                Dusun = Kosongkan(Ambil("Dusun")),
                AlamatDetail = Kosongkan(Ambil("AlamatDetail")),
                Desa = Kosongkan(Ambil("Desa")),
                Kecamatan = Kosongkan(Ambil("Kecamatan")),
                Kabupaten = Kosongkan(Ambil("Kabupaten")),
                Kewarganegaraan = Kosongkan(Ambil("Kewarganegaraan")) ?? "WNI",
                StatusWarga = status.Length > 0 ? StatusWargaTipe.Normalisasi(status) : StatusWargaTipe.Aktif,
                TanggalStatus = Kosongkan(Ambil("TanggalStatus")),
                KeteranganWarga = Kosongkan(Ambil("KeteranganWarga"))
            };

            // --- Aturan kolom wajib ikut model, bukan ditiru di sini ----------
            // Kalau aturan ini diduplikasi, dua tempat bisa berbeda dan berkas
            // yang lolos pemeriksaan gagal diam-diam saat disimpan. Hanya pesan
            // NIK yang dilewati, karena importer sudah memberi yang lebih
            // spesifik (panjang, karakter, duplikat di berkas, nilai internal).
            if (!hasil.Data.IsValid(out List<string> galatModel))
            {
                foreach (string pesan in galatModel)
                {
                    if (!pesan.StartsWith("NIK", StringComparison.Ordinal))
                        hasil.Kesalahan.Add(pesan);
                }

                if (hasil.AdaKesalahan) return hasil;
            }

            // --- Duplikat di database -------------------------------------------
            if (nikBisaDiperiksa)
            {
                ct.ThrowIfCancellationRequested();
                if (!nikTersedia.TryGetValue(nik, out bool sudahAda))
                {
                    sudahAda = await AdaDiDatabaseAsync(nik, ct).ConfigureAwait(false);
                    nikTersedia[nik] = sudahAda;
                }

                if (sudahAda)
                {
                    if (perbaruiYangSudahAda)
                    {
                        hasil.Tindakan = TindakanImpor.Perbarui;
                    }
                    else
                    {
                        hasil.Kesalahan.Add(
                            "NIK sudah terdaftar di database. Aktifkan perbarui bila memang ingin menimpa.");
                    }
                }
                else
                {
                    hasil.Tindakan = TindakanImpor.Simpan;
                }
            }

            return hasil;
        }

        private async Task<bool> AdaDiDatabaseAsync(string nik, CancellationToken ct)
        {
            try
            {
                var warga = await _warga.GetWargaByNikAsync(nik).ConfigureAwait(false);
                return warga != null;
            }
            catch (Exception ex)
            {
                // Gagal membaca harus terdengar, bukan diam-diam membuat semua
                // baris terlihat baru lalu menimpa data yang sudah ada.
                _logger.LogError(ex, "Gagal memeriksa NIK di database saat impor.");
                throw new InvalidOperationException(
                    "Gagal memeriksa NIK di database. Periksa koneksi lalu coba lagi.", ex);
            }
        }


    }
}
