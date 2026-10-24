using SuDesApp.Data.Models;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace SuDesApp.Data.Repositories
{
    /// <summary>
    /// Kemampuan penyisipan surat mentah — sengaja <b>internal</b>: bukan bagian
    /// kontrak publik <see cref="ISuratRepository"/> agar kode di luar Core tidak
    /// dapat menyisipkan baris surat secara langsung (melewati validasi mode
    /// Aktif/Draft, penomoran, dan pencatatan aktivitas).
    ///
    /// Satu-satunya pemakai yang disengaja adalah
    /// <see cref="SuDesApp.Services.SuratSaveService"/> (pemilik alur simpan).
    /// </summary>
    internal interface ISuratInsertion
    {
        /// <summary>
        /// Sisipkan satu surat (resolve jenis, penomoran otomatis bila kosong,
        /// validasi, data terkait, cache) — dalam transaksi yang diberikan bila ada.
        /// </summary>
        Task<int> InsertSuratAsync(SuratData entity, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
    }
}
