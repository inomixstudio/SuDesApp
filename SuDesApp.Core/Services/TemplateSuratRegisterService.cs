using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.Data.Models;

namespace SuDesApp.Services
{
    /// <summary>Hasil pencatatan surat dari Template Surat ke Register Surat.</summary>
    public sealed record HasilCatatTemplateSurat(
        int IdSurat,
        bool Berhasil,
        bool NomorSudahDipakai,
        string Pesan)
    {
        public static HasilCatatTemplateSurat Gagal(string pesan) => new(0, false, false, pesan);
    }

    /// <summary>
    /// Mencatat surat yang dibuat dari menu Template Surat ke Register Surat.
    ///
    /// Satu baris register menyimpan: jenis surat <c>TEMPLATE_SURAT</c>, nomor &amp;
    /// tanggal surat, keterangan ringkas, serta payload <see cref="TemplateSuratTercatat"/>
    /// pada kolom AdditionalData. Dari baris itulah surat dapat dicari, dibuka lagi,
    /// dicetak ulang, dan diedit — tanpa bergantung pada template aslinya.
    /// </summary>
    public class TemplateSuratRegisterService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<TemplateSuratRegisterService> _logger;

        public TemplateSuratRegisterService(
            IUnitOfWork unitOfWork,
            ILogger<TemplateSuratRegisterService> logger)
        {
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Catat surat baru, atau perbarui baris yang sudah ada bila
        /// <paramref name="idSuratTercatat"/> lebih dari nol (mode edit).
        ///
        /// Nomor surat harus belum dipakai surat lain di register: nomor template
        /// (mis. 470/001/Ds/2026) bisa saja sama dengan nomor surat desa lain, dan
        /// register tidak boleh memuat dua surat bernomor sama.
        /// </summary>
        public async Task<HasilCatatTemplateSurat> CatatAsync(
            TemplateSuratTercatat payload,
            int idSuratTercatat = 0)
        {
            if (payload == null)
            {
                return HasilCatatTemplateSurat.Gagal("Isi surat tidak tersedia untuk dicatat.");
            }

            string nomor = (payload.NomorSurat ?? string.Empty).Trim();
            if (nomor.Length == 0)
            {
                return HasilCatatTemplateSurat.Gagal("Nomor surat belum diisi sehingga surat belum bisa dicatat.");
            }

            try
            {
                if (_unitOfWork.SuratRepository == null)
                {
                    return HasilCatatTemplateSurat.Gagal("Penyimpanan surat tidak tersedia.");
                }

                // Nomor yang sama sudah terpakai surat lain (mis. nomor SKD desa):
                // jangan diubah diam-diam, beri tahu pengguna agar nomornya disesuaikan.
                if (idSuratTercatat <= 0)
                {
                    bool sudahDipakai = await _unitOfWork.JenisSuratRepository.IsNomorSuratExistsAsync(nomor);
                    if (sudahDipakai)
                    {
                        _logger.LogWarning("Nomor surat template {Nomor} sudah dipakai surat lain.", nomor);
                        return new HasilCatatTemplateSurat(0, false, true,
                            $"Nomor surat {nomor} sudah dipakai surat lain di register. " +
                            "Ubah awalan nomor pada template ini (Template Surat → Edit) lalu cetak ulang.");
                    }
                }

                var surat = BuatSurat(payload, idSuratTercatat);

                if (idSuratTercatat > 0)
                {
                    bool diperbarui = await _unitOfWork.SuratRepository.UpdateAsync(surat);
                    if (!diperbarui)
                    {
                        _logger.LogWarning("Update surat template #{Id} tidak mengubah baris.", idSuratTercatat);
                        return HasilCatatTemplateSurat.Gagal(
                            $"Surat #{idSuratTercatat} tidak ditemukan di register sehingga perubahan tidak tersimpan.");
                    }

                    _logger.LogInformation("Surat template #{Id} diperbarui di register ({Nomor}).",
                        idSuratTercatat, nomor);
                    return new HasilCatatTemplateSurat(idSuratTercatat, true, false,
                        $"Surat {nomor} diperbarui di register.");
                }

                int id = await _unitOfWork.SuratRepository.AddSuratAsync(surat);
                if (id <= 0)
                {
                    return HasilCatatTemplateSurat.Gagal("Surat gagal disimpan ke register.");
                }

                _logger.LogInformation("Surat template tercatat di register: #{Id} {Nomor} ({Nama}).",
                    id, nomor, payload.NamaTemplate);
                return new HasilCatatTemplateSurat(id, true, false, $"Surat {nomor} tercatat di register.");
            }
            catch (Exception ex)
            {
                // Pesan "Nomor surat ... already used" berasal dari pemeriksaan nomor
                // di repositori; diterjemahkan agar pengguna tahu langkah perbaikannya.
                if (ex is ValidationException || ex.Message.Contains("already used", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(ex, "Nomor surat template {Nomor} ditolak register.", nomor);
                    return new HasilCatatTemplateSurat(0, false, true,
                        $"Nomor surat {nomor} sudah dipakai surat lain di register. " +
                        "Ubah awalan nomor pada template ini lalu cetak ulang.");
                }

                _logger.LogError(ex, "Gagal mencatat surat template {Nomor} ke register.", nomor);
                return HasilCatatTemplateSurat.Gagal("Surat gagal dicatat ke register: " + ex.Message);
            }
        }

        /// <summary>
        /// Baris register untuk satu surat template.
        ///
        /// Kolom warga sengaja TIDAK diisi (repositori memakai baris warga penanda
        /// milik aplikasi). Alasannya: isian surat template bebas bentuknya — bisa
        /// perorangan, bisa undangan/pengumuman — sehingga tidak dapat dipaksa menjadi
        /// data kependudukan. Identitas penerima (nama, alamat, TTL, jenis kelamin)
        /// dibaca dari payload surat ini saat register ditampilkan atau dicetak, dan
        /// isi payload ikut dicari oleh kotak pencarian Register Surat.
        /// </summary>
        private static SuratData BuatSurat(TemplateSuratTercatat payload, int idSuratTercatat) => new()
        {
            ID_Surat = idSuratTercatat,
            NamaJenis = SuratConstants.TEMPLATE_SURAT,
            NomorSurat = payload.NomorSurat.Trim(),
            TanggalSurat = payload.TanggalSurat == default ? DateTime.Now : payload.TanggalSurat,
            Status = "Active",
            Keperluan = payload.KeperluanTampil,
            Keterangan = payload.RingkasanIsian,
            AdditionalData = payload.ToJson(),

            // Warga = null membuat register memakai baris warga penanda milik aplikasi
            // (sama seperti surat instansi), bukan memaksa isian surat template menjadi
            // data kependudukan yang bisa mengotori daftar warga desa.
            Warga = null
        };
    }
}
