using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;

namespace SuDesApp.Services
{
    /// <summary>Alasan penyimpanan perangkat desa ditolak.</summary>
    public enum AlasanSimpanPerangkat
    {
        Berhasil,
        WajibDiisi,
        TidakDikenal,
        TidakAda,
        JabatanGanda
    }

    /// <summary>Hasil satu percobaan simpan perangkat desa.</summary>
    public class HasilSimpanPerangkat
    {
        public bool Berhasil { get; init; }
        public AlasanSimpanPerangkat Alasan { get; init; } = AlasanSimpanPerangkat.Berhasil;
        public string? Pesan { get; init; }

        /// <summary>Daftar isian yang bermasalah, supaya operator bisa memperbaiki form.</summary>
        public IReadOnlyList<string> Kekurangan { get; init; } = Array.Empty<string>();

        /// <summary>Data yang tersimpan, kalau berhasil.</summary>
        public PerangkatDesa? Data { get; init; }
    }

    public interface IPerangkatDesaService
    {
        /// <summary>Daftar perangkat desa sesuai filter pencarian.</summary>
        Task<List<PerangkatDesa>> AmbilSemuaAsync(PerangkatDesaFilter? filter = null, CancellationToken ct = default);

        /// <summary>Satu orang perangkat desa, atau <c>null</c> kalau ID tidak dikenal.</summary>
        Task<PerangkatDesa?> AmbilAsync(int id, CancellationToken ct = default);

        /// <summary>Simpan data baru atau perbarui yang lama.</summary>
        Task<HasilSimpanPerangkat> SimpanAsync(PerangkatDesa data, bool modeUbah, CancellationToken ct = default);

        /// <summary>Hapus satu baris perangkat desa.</summary>
        Task<bool> HapusAsync(int id, CancellationToken ct = default);

        /// <summary>Angka rekap untuk kartu di atas daftar.</summary>
        Task<PerangkatDesaStatistik> AmbilStatistikAsync(CancellationToken ct = default);

        /// <summary>Nilai dusun, RT, dan RW yang ada, untuk dropdown filter.</summary>
        Task<IReadOnlyList<string>> AmbilNilaiWilayahAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Aturan data perangkat desa: nama dan jabatan wajib, nomor identitas
    /// harus masuk akal, masa jabatan tidak terbalik, dan satu jabatan pada
    /// satu wilayah hanya boleh dipegang satu orang.
    /// </summary>
    public class PerangkatDesaService : IPerangkatDesaService
    {
        private readonly IPerangkatDesaRepository _repo;
        private readonly ILogger<PerangkatDesaService> _logger;
        private readonly Func<DateTime> _sekarang;

        public PerangkatDesaService(
            IPerangkatDesaRepository repo,
            ILogger<PerangkatDesaService>? logger = null,
            Func<DateTime>? sekarang = null)
        {
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
            _logger = logger ?? NullLogger<PerangkatDesaService>.Instance;
            _sekarang = sekarang ?? (() => DateTime.Now);
        }

        public async Task<List<PerangkatDesa>> AmbilSemuaAsync(
            PerangkatDesaFilter? filter = null, CancellationToken ct = default)
        {
            return await _repo.SearchAsync(filter ?? new PerangkatDesaFilter(), ct).ConfigureAwait(false);
        }

        public async Task<PerangkatDesa?> AmbilAsync(int id, CancellationToken ct = default)
        {
            return await _repo.GetAsync(id, ct).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<string>> AmbilNilaiWilayahAsync(CancellationToken ct = default)
        {
            return await _repo.GetDaftarNilaiWilayahAsync(ct).ConfigureAwait(false);
        }

        public async Task<PerangkatDesaStatistik> AmbilStatistikAsync(CancellationToken ct = default)
        {
            var semua = await _repo.GetAllAsync(ct).ConfigureAwait(false);
            var stat = new PerangkatDesaStatistik { Total = semua.Count };

            foreach (var p in semua)
            {
                switch (p.StatusTampil)
                {
                    case StatusPerangkat.Aktif: stat.Aktif++; break;
                    case StatusPerangkat.MenungguSK: stat.MenungguSK++; break;
                    case StatusPerangkat.Selesai: stat.Selesai++; break;
                    case StatusPerangkat.Berhenti: stat.Berhenti++; break;
                }
            }

            // Jabatan inti dianggap terisi bila ada orang yang masih memegangnya,
            // bukan sekadar baris history Jabatan=SELESAI.
            var jabatanTerisi = semua
                .Where(p => p.MasihMemegangJabatan)
                .Select(p => p.JabatanTampil)
                .ToHashSet(StringComparer.Ordinal);

            stat.JabatanIntiKosong = JabatanPerangkat.Inti
                .Where(j => !jabatanTerisi.Contains(j))
                .ToList();

            return stat;
        }

        public async Task<bool> HapusAsync(int id, CancellationToken ct = default)
        {
            bool terhapus = await _repo.DeleteAsync(id, ct).ConfigureAwait(false);
            if (terhapus) _logger.LogInformation("Data perangkat desa #{ID} dihapus", id);
            return terhapus;
        }

        public async Task<HasilSimpanPerangkat> SimpanAsync(
            PerangkatDesa data, bool modeUbah, CancellationToken ct = default)
        {
            if (data is null)
            {
                return Gagal(AlasanSimpanPerangkat.TidakAda, "Data perangkat desa tidak terbaca.");
            }

            Rapikan(data);

            var kekurangan = new List<string>();
            Periksa(data, kekurangan);
            if (kekurangan.Count > 0)
            {
                return new HasilSimpanPerangkat
                {
                    Alasan = AlasanSimpanPerangkat.WajibDiisi,
                    Pesan = "Ada isian yang belum benar.",
                    Kekurangan = kekurangan
                };
            }

            if (!JabatanPerangkat.Valid(data.Jabatan))
            {
                return new HasilSimpanPerangkat
                {
                    Alasan = AlasanSimpanPerangkat.TidakDikenal,
                    Pesan = $"Jabatan \"{data.Jabatan}\" tidak dikenal.",
                    Kekurangan = new[] { $"Jabatan \"{data.Jabatan}\" tidak ada dalam daftar jabatan perangkat desa." }
                };
            }

            bool sudahAda = await _repo.JabatanSudahDipakaiAsync(
                data.Jabatan, data.Dusun, data.RT, data.RW,
                modeUbah ? data.ID : null, ct).ConfigureAwait(false);

            if (sudahAda)
            {
                return new HasilSimpanPerangkat
                {
                    Alasan = AlasanSimpanPerangkat.JabatanGanda,
                    Pesan = "Jabatan pada wilayah yang sama sudah terisi.",
                    Kekurangan = new[]
                    {
                        $"{data.Jabatan} untuk wilayah {Wilayah(data)} sudah ada orang lain. " +
                        "Satu jabatan pada satu wilayah hanya boleh satu orang."
                    }
                };
            }

            try
            {
                DateTime sekarang = _sekarang();
                data.UpdatedAt = sekarang;

                if (modeUbah)
                {
                    if (data.ID <= 0)
                    {
                        return Gagal(AlasanSimpanPerangkat.WajibDiisi, "Data yang akan diubah tidak punya ID.");
                    }

                    var lama = await _repo.GetAsync(data.ID, ct).ConfigureAwait(false);
                    if (lama is null)
                    {
                        return Gagal(AlasanSimpanPerangkat.TidakAda, "Data perangkat desa ini sudah tidak ada.");
                    }

                    data.CreatedAt = lama.CreatedAt;
                    data.DibuatOleh = lama.DibuatOleh;
                    await _repo.UpdateAsync(data, ct).ConfigureAwait(false);
                    _logger.LogInformation("Perangkat desa #{ID} ({Nama}) diperbarui", data.ID, data.Nama);
                }
                else
                {
                    data.ID = 0;
                    data.CreatedAt = sekarang;
                    data.DibuatOleh = data.DiperbaruiOleh;
                    data.ID = await _repo.InsertAsync(data, ct).ConfigureAwait(false);
                    _logger.LogInformation("Perangkat desa #{ID} ({Nama}) ditambahkan", data.ID, data.Nama);
                }

                return new HasilSimpanPerangkat { Berhasil = true, Data = data };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menyimpan perangkat desa {Nama}", data.Nama);
                throw;
            }
        }

        // ---------- validasi ----------

        /// <summary>
        /// Bersihkan input operator: potong spasi, rapatkan jabatan jadi huruf
        /// kapital, dan sisakan angka saja untuk NIK, NIP, RT, serta RW.
        /// </summary>
        internal static void Rapikan(PerangkatDesa data)
        {
            data.Nama = Potong(data.Nama);
            data.Jabatan = JabatanPerangkat.Normalisasi(data.Jabatan);
            data.Status = StatusPerangkat.Normalisasi(data.Status);
            data.NIK = AngkaSaja(data.NIK);
            data.NIP = AngkaSaja(data.NIP);
            data.RT = NomorWilayah(data.RT);
            data.RW = NomorWilayah(data.RW);
            data.Dusun = Potong(data.Dusun);
            data.JenisKelamin = NormalisasiJenisKelamin(data.JenisKelamin);
            data.TempatLahir = Potong(data.TempatLahir);
            data.Pendidikan = Potong(data.Pendidikan);
            data.Alamat = Potong(data.Alamat);
            data.NomorHP = NomorKontak(data.NomorHP);
            data.WhatsApp = NomorKontak(data.WhatsApp);
            data.NomorSK = Potong(data.NomorSK);
            data.Catatan = Potong(data.Catatan);
            data.DiperbaruiOleh = Potong(data.DiperbaruiOleh);
        }

        private void Periksa(PerangkatDesa data, List<string> kekurangan)
        {
            DateTime hariIni = _sekarang();

            if (string.IsNullOrWhiteSpace(data.Nama))
                kekurangan.Add("Nama lengkap wajib diisi.");
            else if (data.Nama.Length > 100)
                kekurangan.Add("Nama lengkap maksimal 100 huruf.");

            if (string.IsNullOrWhiteSpace(data.Jabatan))
                kekurangan.Add("Jabatan wajib diisi.");

            if (data.NIK is not null && data.NIK.Length != 16)
                kekurangan.Add("NIK harus 16 digit angka (boleh dikosongkan).");

            if (data.NIP is not null && data.NIP.Length != 18)
                kekurangan.Add("NIP harus 18 digit angka (boleh dikosongkan).");

            if (data.TanggalLahir.HasValue)
            {
                if (data.TanggalLahir.Value.Date > hariIni.Date)
                    kekurangan.Add("Tanggal lahir tidak boleh di masa depan.");
                else if (data.TanggalLahir.Value.Date < hariIni.AddYears(-100).Date)
                    kekurangan.Add("Tanggal lahir lebih dari 100 tahun yang lalu, periksa kembali.");
            }

            if (data.MasaJabatanMulai.HasValue && data.MasaJabatanSelesai.HasValue
                && data.MasaJabatanSelesai.Value.Date < data.MasaJabatanMulai.Value.Date)
            {
                kekurangan.Add("Masa jabatan selesai tidak boleh lebih awal dari masa jabatan mulai.");
            }

            if (data.TanggalSK.HasValue && data.MasaJabatanMulai.HasValue
                && data.MasaJabatanMulai.Value.Date < data.TanggalSK.Value.Date)
            {
                // Lazim SK pelantikan dan masa jabatan mulai pada tanggal yang sama;
                // selisih beberapa hari masih wajar karena SK bisa turun belakangan.
                if ((data.TanggalSK.Value.Date - data.MasaJabatanMulai.Value.Date).TotalDays > 365)
                    kekurangan.Add("Masa jabatan mulai jauh lebih awal dari tanggal SK. Periksa kembali.");
            }
        }

        // ---------- pembantu ----------

        private static HasilSimpanPerangkat Gagal(AlasanSimpanPerangkat alasan, string pesan) => new()
        {
            Alasan = alasan,
            Pesan = pesan,
            Kekurangan = new[] { pesan }
        };

        private static string Wilayah(PerangkatDesa data)
        {
            var bagian = new List<string>();
            if (!string.IsNullOrWhiteSpace(data.Dusun)) bagian.Add(data.Dusun);
            if (!string.IsNullOrWhiteSpace(data.RT)) bagian.Add("RT " + data.RT);
            if (!string.IsNullOrWhiteSpace(data.RW)) bagian.Add("RW " + data.RW);
            return bagian.Count == 0 ? "seluruh desa" : string.Join(" ", bagian);
        }

        internal static string Potong(string? nilai) =>
            string.IsNullOrWhiteSpace(nilai) ? string.Empty : nilai!.Trim();

        internal static string? AngkaSaja(string? nilai)
        {
            if (string.IsNullOrWhiteSpace(nilai)) return null;
            string digits = new string(nilai.Where(char.IsDigit).ToArray());
            return digits.Length == 0 ? null : digits;
        }

        /// <summary>RT/RW disimpan dua digit, jadi "rt 1" dan "01" dianggap sama.</summary>
        internal static string? NomorWilayah(string? nilai)
        {
            string? digits = AngkaSaja(nilai);
            return digits is null ? null : digits.PadLeft(2, '0');
        }

        internal static string? NomorKontak(string? nilai)
        {
            string dipangkas = Potong(nilai);
            return dipangkas.Length == 0 ? null : dipangkas;
        }

        internal static string? NormalisasiJenisKelamin(string? nilai) => Potong(nilai).ToUpperInvariant() switch
        {
            "" or "-" => null,
            "L" or "LAKI-LAKI" or "PRIA" => "L",
            "P" or "PEREMPUAN" or "WANITA" => "P",
            var lain => lain
        };
    }
}
