using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using SuDesApp.Utilities;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;
using SuDesApp.Wpf.Utilities;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class PembaruanViewModel : ObservableObject
{
        // =====================================================================
        // Pemberitahuan inline (pengganti dialog popup)
        // =====================================================================

        /// <summary>
        /// Pemberitahuan yang tampil di dalam halaman, terbaru di atas. Memakai kumpulan
        /// kartu bersama <see cref="KumpulanPesanInline"/> supaya bentuk dan perilakunya
        /// sama dengan halaman lain yang juga meninggalkan dialog popup.
        /// </summary>
        public KumpulanPesanInline Pesan { get; } = new();

        /// <summary>Benar bila ada pemberitahuan untuk ditampilkan.</summary>
        public bool AdaPesan => Pesan.Ada;

        /// <summary>Benar bila ada kartu keputusan yang menunggu jawaban pengguna.</summary>
        public bool MenungguJawaban => Pesan.MenungguJawaban;

        /// <summary>
        /// Tambah pemberitahuan inline. Kartu dengan <paramref name="kunci"/> yang sama
        /// mengganti kartu sebelumnya supaya kabar sejenis tidak menumpuk (mis. beberapa
        /// kali menekan Periksa Pembaruan hanya menyisakan hasil terakhir).
        /// </summary>
        private PesanInline TampilkanPesan(JenisPesanInline jenis, string judul, string isi, string kunci = "")
            => Pesan.Tampilkan(jenis, judul, isi, kunci);

        /// <summary>Tutup satu pemberitahuan (tombol ✕ pada kartunya).</summary>
        public void TutupPesan(PesanInline? pesan) => Pesan.Tutup(pesan);

        /// <summary>
        /// Buang seluruh kabar lama — dipakai saat memulai pemeriksaan baru.
        /// Kartu keputusan yang masih menunggu jawaban tidak ikut dibuang.
        /// </summary>
        private void BersihkanPesan() => Pesan.Bersihkan();

        /// <summary>
        /// Tanyakan keputusan penting di dalam halaman (bukan popup) dan tunggu
        /// jawabannya. Selama menunggu, tombol-tombol lain dinonaktifkan supaya
        /// kartu keputusan tidak hilang begitu saja.
        /// </summary>
        private async Task<bool> TanyaAsync(
            string judul, string rincian, string pertanyaan, string teksYa, string teksTidak, string kunci = "konfirmasi")
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var kartu = TampilkanPesan(JenisPesanInline.Pertanyaan, judul, rincian, kunci);
            kartu.SiapkanPertanyaan(pertanyaan, teksYa, teksTidak, jawaban => tcs.TrySetResult(jawaban));

            try
            {
                return await tcs.Task;
            }
            finally
            {
                TutupPesan(kartu);
            }
        }


    }
}
