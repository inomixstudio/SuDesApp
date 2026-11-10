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
        // N8 — SURAT KETERANGAN NUMPANG NIKAH (numpang kawin, diterbitkan desa)
        //
        // Surat desa biasa (bukan blanko Kepdirjen): memakai kop surat desa yang sama
        // dengan surat keterangan lain, lalu menerangkan bahwa warga berdomisili di
        // desa ini akan melangsungkan akad nikah di wilayah lain.
        // =====================================================================
        private void ComposeN8(BadanSurat badan, SuratData suratData, DesaData desa, WargaData warga, NtcrData ntcr, string keteranganTextBox)
        {
            badan.Blok(c => KopSurat.Gambar(c, desa, CariLogoPath()));
            badan.Blok(c => SuratRenderer.JudulDanNomor(c, "SURAT KETERANGAN NUMPANG NIKAH", HitungNomorSurat(suratData)));

            badan.Paragraf("Yang bertandatangan di bawah ini :", jarakBawah: 8);
            TabelBaris(badan, new List<(string, string?)>
            {
                ("Nama", NamaPejabatSurat(suratData, desa)),
                ("Jabatan", Isi(JabatanPejabatSurat(suratData, desa)))
            });

            badan.Paragraf("Dengan ini menerangkan bahwa :", jarakAtas: 10, jarakBawah: 8);
            TabelBaris(badan, new List<(string, string?)>
            {
                ("Nama", NamaFormatter.ToUpperNama(warga.Nama)),
                ("Tempat, Tgl. Lahir", TempatTanggalLahir(warga.TempatLahir, warga.TanggalLahir)),
                ("Jenis Kelamin", Isi(string.IsNullOrWhiteSpace(warga.JenisKelamin) ? "Laki-laki" : warga.JenisKelamin)),
                ("Kewarganegaraan", Isi(warga.Kewarganegaraan ?? "WNI")),
                ("Agama", Isi(warga.Agama)),
                ("Pekerjaan", Isi(warga.Pekerjaan)),
                ("Alamat", AlamatCalon(false, ntcr, warga))
            });

            badan.Paragraf(
                $"Yang bersangkutan adalah benar warga Desa {Isi(desa.NamaDesa)} Kecamatan {Isi(desa.Kecamatan)} " +
                $"Kabupaten {Isi(desa.Kabupaten)} dan maksud yang bersangkutan akan numpang nikah di :",
                rata: Rata.Justify, indentKiri: 20, jarakAtas: 10, jarakBawah: 6);

            TabelBaris(badan, new List<(string, string?)>
            {
                ("Desa/Kelurahan", Isi(ntcr.DesaNumpang)),
                ("Kecamatan", Isi(ntcr.KecamatanNumpang)),
                ("Kabupaten/Kota", Isi(ntcr.KabupatenNumpang))
            });

            badan.Paragraf("Dengan seorang perempuan :", jarakAtas: 10, jarakBawah: 6);
            TabelBaris(badan, new List<(string, string?)>
            {
                ("Nama", NamaFormatter.ToUpperNama(ntcr.NamaIstri)),
                ("Tempat, Tgl. Lahir", TempatTanggalLahir(ntcr.TempatLahirIstri, ntcr.TanggalLahirIstri)),
                ("Alamat", Isi(ntcr.AlamatIstri)),
                ("Kecamatan", Isi(ntcr.KecamatanIstri)),
                ("Kabupaten/Kota", Isi(ntcr.KabupatenIstri))
            });

            AddKeteranganBebas(badan, suratData, keteranganTextBox);

            badan.Paragraf("Demikian Surat Keterangan ini kami buat, agar kepada yang berkepentingan menjadi tahu adanya.",
                rata: Rata.Justify, indentKiri: 20, jarakAtas: 12);

            TandaTanganDesa(badan, suratData, desa);
        }

        // =====================================================================
        // Tabel data blanko
        // =====================================================================


    }
}
