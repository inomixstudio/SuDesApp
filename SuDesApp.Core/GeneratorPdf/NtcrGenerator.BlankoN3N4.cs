using Microsoft.Extensions.Logging;
using SuDesApp.Configuration;
using SuDesApp.ControlSurat;
using SuDesApp.Data.Models;
using SuDesApp.Data.Repositories;
using SuDesApp.Utilities;
using SudesPdf.Core.Layout;
using System.Globalization;

namespace SuDesApp.GeneratorPdf
{
    public partial class NtcrGenerator : SuratGeneratorBase
    {
        // =====================================================================
        // N3 — PERMOHONAN PENCATATAN ISBAT
        // =====================================================================
        private void ComposeN3(BadanSurat badan, SuratData suratData, DesaData desa, WargaData warga, NtcrData ntcr, string keteranganTextBox)
        {
            ComposeJudulPermohonan(badan, suratData, desa, "FORMULIR PERMOHONAN PENCATATAN ISBAT", "Permohonan pencatatan isbat");
            ComposeAlamatTujuan(badan, "Kepala KUA Kecamatan /PPN LN", ntcr.TujuanKua);

            badan.Paragraf("Dengan hormat, kami mengajukan permohonan pencatatan isbat untuk atas nama", jarakAtas: 10, jarakBawah: 4);

            // N3 boleh dikosongkan: nilai kosong dicetak sebagai titik-titik seperti
            // template, lalu diisi tangan saat surat dibawa ke KUA/pengadilan.
            TabelBaris(badan, new List<(string, string?)>
            {
                ("Suami", NamaAtauTitik(warga?.Nama)),
                ("Istri", NamaAtauTitik(ntcr.NamaIstri)),
                ("Tanggal penetapan", Isi(TanggalIndonesia(ntcr.TanggalPenetapanIsbat))),
                ("Pengadilan Agama", Isi(ntcr.PengadilanAgama))
            });

            badan.Paragraf("Bersama ini kami sampaikan surat-surat yang diperlukan untuk diperiksa sebagai berikut:", jarakAtas: 10, jarakBawah: 4);
            badan.DaftarBernomor(NtcrLampiran.UntukN3(), indentKiri: 20);

            AddKeteranganBebas(badan, suratData, keteranganTextBox);

            badan.Paragraf("Demikian permohonan ini kami sampaikan, kiranya dapat diperiksa, dihadiri, dan dicatat sesuai dengan ketentuan peraturan perundang-undangan.",
                rata: Rata.Justify, indentKiri: 20, jarakAtas: 12);

            TandaTanganPemohonKua(badan, suratData, warga!, ntcr);
        }

        // =====================================================================
        // N4 — PERSETUJUAN CALON PENGANTIN (ditandatangani kedua calon)
        // =====================================================================
        private void ComposeN4(BadanSurat badan, SuratData suratData, DesaData desa, WargaData warga, NtcrData ntcr, string keteranganTextBox)
        {
            badan.Blok(c => SuratRenderer.JudulTengah(c, "SURAT PERSETUJUAN PENGANTIN"));

            badan.Paragraf("Yang bertanda tangan di bawah ini:", jarakAtas: 15, jarakBawah: 4);

            badan.Paragraf("A. Calon suami:", tebal: true, jarakBawah: 2);
            TabelBaris(badan, BarisIdentitasCalon(false, warga, ntcr));

            badan.Paragraf("B. Calon Istri:", tebal: true, jarakAtas: 10, jarakBawah: 2);
            TabelBaris(badan, BarisIdentitasCalon(true, warga, ntcr));

            AddKeteranganBebas(badan, suratData, keteranganTextBox);

            badan.Paragraf("Menyatakan dengan sesungguhnya bahwa atas dasar suka rela, dengan kesadaran sendiri, tanpa ada paksaan dari siapapun juga, setuju untuk melangsungkan pernikahan.",
                rata: Rata.Justify, indentKiri: 20, jarakAtas: 12);

            badan.Paragraf("Demikian Surat persetujuan ini di buat untuk digunakan seperlunya.",
                rata: Rata.Justify, indentKiri: 20, jarakAtas: 8);

            TandaTanganDuaPihak(
                badan,
                desa,
                suratData,
                ("Calon Suami", NamaFormatter.ToUpperNama(warga.Nama)),
                ("Calon Istri", NamaFormatter.ToUpperNama(ntcr.NamaIstri)));
        }


    }
}
