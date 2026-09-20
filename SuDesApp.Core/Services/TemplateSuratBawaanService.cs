using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;

namespace SuDesApp.Services
{
    /// <summary>
    /// Pemasangan contoh Template Surat bawaan ke daftar template pengguna.
    ///
    /// Hasil pemasangan adalah template biasa: bisa diisi, dicetak, disunting, dan
    /// dihapus seperti template buatan sendiri. Contoh dicocokkan dengan template
    /// pengguna lewat namanya, sehingga memasang dua kali tidak menghasilkan salinan
    /// ganda.
    /// </summary>
    public class TemplateSuratBawaanService
    {
        private readonly ITemplateSuratRepository _repository;
        private readonly ILogger<TemplateSuratBawaanService> _logger;

        public TemplateSuratBawaanService(
            ITemplateSuratRepository repository,
            ILogger<TemplateSuratBawaanService> logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>Contoh yang belum ada pada daftar template pengguna.</summary>
        public static List<ContohTemplateSurat> BelumAda(IEnumerable<TemplateSuratKustom>? daftarPengguna) =>
            TemplateSuratBawaan.BelumAda(daftarPengguna);

        /// <summary>
        /// Pasang contoh bawaan. <paramref name="kode"/> null/kosong = seluruh contoh
        /// yang belum ada; selain itu hanya kode yang disebut. Contoh yang namanya
        /// sudah dipakai template pengguna dilewati.
        /// </summary>
        public async Task<List<TemplateSuratKustom>> PasangAsync(IEnumerable<string>? kode = null)
        {
            var terpasang = new List<TemplateSuratKustom>();

            List<ContohTemplateSurat> pilihan;
            if (kode == null)
            {
                pilihan = TemplateSuratBawaan.BelumAda(await _repository.GetAllAsync().ConfigureAwait(false));
            }
            else
            {
                pilihan = kode
                    .Select(TemplateSuratBawaan.Cari)
                    .Where(c => c != null)
                    .Select(c => c!)
                    .ToList();
            }

            if (pilihan.Count == 0) return terpasang;

            var daftar = await _repository.GetAllAsync().ConfigureAwait(false);
            var namaTerpakai = new HashSet<string>(
                daftar.Select(d => (d.NamaTampil ?? string.Empty).Trim()),
                StringComparer.OrdinalIgnoreCase);

            foreach (var contoh in pilihan)
            {
                if (namaTerpakai.Contains(contoh.Nama))
                {
                    continue;
                }

                var salinan = contoh.Salinan();
                salinan.Dibuat = DateTime.Now;
                await _repository.AddAsync(salinan).ConfigureAwait(false);

                namaTerpakai.Add(contoh.Nama);
                terpasang.Add(salinan);

                _logger.LogInformation("Template bawaan dipasang: {Nama} (kode {Kode})", contoh.Nama, contoh.Kode);
            }

            return terpasang;
        }

        /// <summary>Jumlah template pengguna yang berasal dari sebuah contoh bawaan.</summary>
        public static int JumlahBisaDisegarkan(IEnumerable<TemplateSuratKustom>? daftarPengguna)
        {
            if (daftarPengguna == null) return 0;
            return daftarPengguna.Count(t =>
                t != null && TemplateSuratBawaan.KodeDariNama(t.NamaTampil) != null);
        }

        /// <summary>
        /// Segarkan template pengguna yang berasal dari contoh bawaan: definisinya diganti
        /// definisi contoh terbaru, sedangkan identitasnya (id, nama yang dipakai
        /// pengguna, dan nomor urut terakhir) tetap dipertahankan. Dipakai supaya
        /// perbaikan contoh bawaan ikut sampai ke pengguna yang sudah memasangnya.
        /// </summary>
        public async Task<List<TemplateSuratKustom>> SegarkanAsync(IEnumerable<string>? kode = null)
        {
            var hasil = new List<TemplateSuratKustom>();
            var daftar = await _repository.GetAllAsync().ConfigureAwait(false);
            var diminta = kode == null
                ? null
                : new HashSet<string>(kode, StringComparer.OrdinalIgnoreCase);

            foreach (var template in daftar)
            {
                string? kodeContoh = TemplateSuratBawaan.KodeDariNama(template.NamaTampil);
                if (kodeContoh == null) continue;
                if (diminta != null && !diminta.Contains(kodeContoh)) continue;

                var contoh = TemplateSuratBawaan.Cari(kodeContoh);
                if (contoh == null) continue;

                var segar = contoh.Salinan();
                segar.Id = template.Id;
                segar.Nama = template.Nama;
                segar.Dibuat = template.Dibuat;
                segar.DibuatOleh = template.DibuatOleh;
                segar.NomorTerakhir = template.NomorTerakhir;
                segar.TahunNomor = template.TahunNomor;
                segar.Diubah = DateTime.Now;

                await _repository.UpdateAsync(segar).ConfigureAwait(false);
                hasil.Add(segar);

                _logger.LogInformation("Contoh template bawaan disegarkan: {Nama} (kode {Kode})", segar.Nama, kodeContoh);
            }

            return hasil;
        }

        /// <summary>
        /// Pemasangan pertama kali. Hanya berjalan bila daftar template masih kosong
        /// DAN penanda menunjukkan contoh bawaan belum pernah dipasang — sehingga
        /// contoh yang sudah dihapus pengguna tidak muncul kembali sendiri.
        /// </summary>
        public async Task<List<TemplateSuratKustom>> PasangOtomatisAsync()
        {
            try
            {
                if (TemplateBawaanStore.SudahPernah()) return new List<TemplateSuratKustom>();

                var daftar = await _repository.GetAllAsync().ConfigureAwait(false);

                // Sudah ada template buatan sendiri: cukup tandai, jangan menambah apa pun.
                if (daftar.Count > 0)
                {
                    TemplateBawaanStore.Simpan(TemplateSuratBawaan.SemuaKode);
                    return new List<TemplateSuratKustom>();
                }

                var terpasang = await PasangAsync().ConfigureAwait(false);
                TemplateBawaanStore.Simpan(terpasang.Count > 0
                    ? terpasang.Select(t => TemplateSuratBawaan.KodeDariNama(t.NamaTampil) ?? t.NamaTampil)
                    : TemplateSuratBawaan.SemuaKode);

                if (terpasang.Count > 0)
                {
                    _logger.LogInformation("{Jumlah} contoh template bawaan disiapkan otomatis.", terpasang.Count);
                }

                return terpasang;
            }
            catch (Exception ex)
            {
                // Gagal menyiapkan contoh tidak boleh menghalangi pemakaian halaman.
                _logger.LogWarning(ex, "Gagal menyiapkan contoh template bawaan secara otomatis.");
                return new List<TemplateSuratKustom>();
            }
        }
    }
}
