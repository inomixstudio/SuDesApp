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
        // N1 — SURAT PENGANTAR NIKAH (diterbitkan & ditandatangani desa)
        // =====================================================================
        private void ComposeN1(BadanSurat badan, SuratData suratData, DesaData desa, WargaData warga, NtcrData ntcr, string keteranganTextBox)
        {
            bool istriDiterangkan = string.Equals(ntcr.PihakDiterangkanN1, "Istri", StringComparison.OrdinalIgnoreCase);
            var (ayah, ibu) = istriDiterangkan
                ? (ntcr.AyahCalonIstri, ntcr.IbuCalonIstri)
                : (ntcr.AyahCalonSuami, ntcr.IbuCalonSuami);

            ComposeKepalaDesa(badan, desa);
            badan.Blok(c => SuratRenderer.JudulDanNomor(c, "FORMULIR PENGANTAR NIKAH", HitungNomorSurat(suratData)));

            badan.Paragraf("Yang bertanda tangan di bawah ini menjelaskan dengan sesungguhnya bahwa:", jarakBawah: 8);

            var baris = new List<(string Label, string? Nilai)>
            {
                ("1. Nama", NamaFormatter.ToUpperNama(istriDiterangkan ? ntcr.NamaIstri : warga.Nama)),
                ("2. Nomor Induk Kependudukan (NIK)", Isi(istriDiterangkan ? ntcr.NikIstri : warga.NIK)),
                ("3. Jenis Kelamin", Isi(istriDiterangkan ? "Perempuan" : (string.IsNullOrWhiteSpace(warga.JenisKelamin) ? "Laki-laki" : warga.JenisKelamin))),
                ("4. Tempat dan tanggal lahir", TempatTanggalLahir(
                    istriDiterangkan ? ntcr.TempatLahirIstri : warga.TempatLahir,
                    istriDiterangkan ? ntcr.TanggalLahirIstri : warga.TanggalLahir)),
                ("5. Kewarganegaraan", Isi(istriDiterangkan ? ntcr.KewarganegaraanIstri ?? "WNI" : warga.Kewarganegaraan ?? "WNI")),
                ("6. Agama", Isi(istriDiterangkan ? ntcr.AgamaIstri : warga.Agama)),
                ("7. Pekerjaan", Isi(istriDiterangkan ? ntcr.PekerjaanIstri : warga.Pekerjaan)),
                ("8. Alamat", AlamatCalon(istriDiterangkan, ntcr, warga))
            };
            TabelBaris(badan, baris);

            // 9. Status pernikahan — blanko mencetak kedua baris (laki-laki & perempuan);
            // baris yang tidak sesuai diberi tanda "-" agar blanko tetap utuh.
            string statusSuami = StatusSebutan(false, istriDiterangkan ? null : warga.StatusPerkawinan);
            string statusIstri = StatusSebutan(true, istriDiterangkan ? ntcr.StatusPerkawinanIstri : null);
            badan.Paragraf("9. Status pernikahan :", indentKiri: 20, jarakAtas: 6);
            TabelBaris(badan, new List<(string, string?)>
            {
                ("a. Laki-laki (Jejaka/Duda)", statusSuami),
                ("b. Perempuan (Perawan/Janda)", statusIstri)
            }, indentKiri: 40);

            badan.Paragraf("Adalah benar anak dari pernikahan seorang pria :", jarakAtas: 8);
            TabelBaris(badan, BarisOrangTua(ayah!, pakaiBin: false));

            badan.Paragraf("dengan seorang wanita :", jarakAtas: 8);
            TabelBaris(badan, BarisOrangTua(ibu!, pakaiBin: false));

            AddKeteranganBebas(badan, suratData, keteranganTextBox);

            badan.Paragraf("Demikian, Surat pengantar ini dibuat dengan mengingat sumpah jabatan dan untuk dipergunakan sebagaimana mestinya.",
                rata: Rata.Justify, indentKiri: 20, jarakAtas: 12);

            TandaTanganDesa(badan, suratData, desa);
        }

        // =====================================================================
        // N2 — PERMOHONAN KEHENDAK NIKAH (diajukan pemohon kepada KUA/PPN LN)
        // =====================================================================
        private void ComposeN2(BadanSurat badan, SuratData suratData, DesaData desa, WargaData warga, NtcrData ntcr, string keteranganTextBox)
        {
            ComposeJudulPermohonan(badan, suratData, desa, "FORMULIR PERMOHONAN KEHENDAK NIKAH", "Permohonan kehendak nikah");
            ComposeAlamatTujuan(badan, "Kepala KUA Kecamatan /PPN LN", ntcr.TujuanKua);

            badan.Paragraf("Dengan hormat, kami mengajukan permohonan kehendak nikah untuk atas nama", jarakAtas: 10, jarakBawah: 4);

            TabelBaris(badan, new List<(string, string?)>
            {
                ("Calon suami", NamaFormatter.ToUpperNama(warga.Nama)),
                ("Calon istri", NamaFormatter.ToUpperNama(ntcr.NamaIstri)),
                ("Hari/Tanggal/Jam", Isi(ntcr.HariTanggalJamAkad)),
                ("Tempat akad nikah", Isi(ntcr.TempatAkad))
            });

            badan.Paragraf("Bersama ini kami sampaikan surat-surat yang diperlukan untuk diperiksa sebagai berikut:", jarakAtas: 10, jarakBawah: 4);
            badan.DaftarBernomor(NtcrLampiran.UntukN2(), indentKiri: 20);

            AddKeteranganBebas(badan, suratData, keteranganTextBox);

            badan.Paragraf("Demikian permohonan ini kami sampaikan, kiranya dapat diperiksa, dihadiri, dan dicatat sesuai dengan ketentuan peraturan perundang-undangan.",
                rata: Rata.Justify, indentKiri: 20, jarakAtas: 12);

            TandaTanganPemohonKua(badan, suratData, warga, ntcr);
        }


    }
}
