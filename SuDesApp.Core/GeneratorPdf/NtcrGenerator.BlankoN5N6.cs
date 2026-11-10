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
        // N5 — SURAT IZIN ORANG TUA (ditandatangani ayah & ibu/wali)
        // =====================================================================
        private void ComposeN5(BadanSurat badan, SuratData suratData, DesaData desa, WargaData warga, NtcrData ntcr, string keteranganTextBox)
        {
            bool anakIstri = string.Equals(ntcr.PihakAnakIzinOrtu, "Istri", StringComparison.OrdinalIgnoreCase);
            var (ayah, ibu) = anakIstri
                ? (ntcr.AyahCalonIstri, ntcr.IbuCalonIstri)
                : (ntcr.AyahCalonSuami, ntcr.IbuCalonSuami);

            // Orang tua boleh belum diisi; pakai objek kosong agar blanko tetap
            // tercetak dengan isian titik-titik.
            ayah ??= new NtcrOrangTua();
            ibu ??= new NtcrOrangTua();

            badan.Blok(c => SuratRenderer.JudulTengah(c, "SURAT IZIN ORANG TUA"));

            badan.Paragraf("Yang bertanda tangan di bawah ini:", jarakAtas: 15, jarakBawah: 4);

            badan.Paragraf("A. Ayah/wali/pengampu:", tebal: true, jarakBawah: 2);
            TabelBaris(badan, BarisOrangTua(ayah, pakaiBin: true));

            badan.Paragraf("B. Ibu/wali/pengampu:", tebal: true, jarakAtas: 10, jarakBawah: 2);
            TabelBaris(badan, BarisOrangTua(ibu, pakaiBin: true));

            badan.Paragraf("adalah ayah dan ibu kandung/wali/pengampu dari:", jarakAtas: 12, jarakBawah: 2);
            TabelBaris(badan, BarisIdentitasCalon(anakIstri, warga, ntcr));

            badan.Paragraf("Memberikan izin kepada anak kami untuk melakukan pernikahan dengan:", jarakAtas: 12, jarakBawah: 2);
            TabelBaris(badan, BarisIdentitasCalon(!anakIstri, warga, ntcr));

            AddKeteranganBebas(badan, suratData, keteranganTextBox);

            badan.Paragraf("Demikian Surat izin ini di buat dengan kesadaran tanpa ada paksaan dari siapapun dan untuk digunakan seperlunya.",
                rata: Rata.Justify, indentKiri: 20, jarakAtas: 12);

            TandaTanganDuaPihak(
                badan,
                desa,
                suratData,
                ("Ayah/wali/pengampu", NamaFormatter.ToUpperNama(ayah.Nama)),
                ("Ibu/wali/pengampu", NamaFormatter.ToUpperNama(ibu.Nama)));
        }

        // =====================================================================
        // N6 — SURAT KETERANGAN KEMATIAN SUAMI/ISTRI (diterbitkan desa)
        // =====================================================================
        private void ComposeN6(BadanSurat badan, SuratData suratData, DesaData desa, WargaData warga, NtcrData ntcr, string keteranganTextBox)
        {
            bool suamiMeninggal = !string.Equals(ntcr.PihakMeninggal, "Istri", StringComparison.OrdinalIgnoreCase);

            ComposeKepalaDesa(badan, desa);
            badan.Blok(c => SuratRenderer.JudulDanNomor(c, "SURAT KETERANGAN KEMATIAN SUAMI/ISTRI", HitungNomorSurat(suratData)));

            badan.Paragraf("Yang bertanda tangan di bawah ini menerangkan dengan sesungguhnya bahwa:", jarakBawah: 8);

            var almarhum = new List<(string Label, string? Nilai)>
            {
                ("1. Nama lengkap dan alias", NamaFormatter.ToUpperNama(suamiMeninggal ? warga.Nama : ntcr.NamaIstri)),
                ("2. Bin/Binti", Isi(NamaFormatter.ToUpperNama(suamiMeninggal ? ntcr.AyahCalonSuami?.Nama : ntcr.AyahCalonIstri?.Nama))),
                ("3. Nomor Induk Kependudukan", Isi(suamiMeninggal ? warga.NIK : ntcr.NikIstri)),
                ("4. Tempat dan tanggal lahir", TempatTanggalLahir(
                    suamiMeninggal ? warga.TempatLahir : ntcr.TempatLahirIstri,
                    suamiMeninggal ? warga.TanggalLahir : ntcr.TanggalLahirIstri)),
                ("5. Kewarganegaraan", Isi(suamiMeninggal ? warga.Kewarganegaraan ?? "WNI" : ntcr.KewarganegaraanIstri ?? "WNI")),
                ("6. Agama", Isi(suamiMeninggal ? warga.Agama : ntcr.AgamaIstri)),
                ("7. Pekerjaan", Isi(suamiMeninggal ? warga.Pekerjaan : ntcr.PekerjaanIstri)),
                ("8. Alamat", AlamatCalon(suamiMeninggal, ntcr, warga))
            };
            TabelBaris(badan, almarhum);

            TabelBaris(badan, new List<(string, string?)>
            {
                ("Telah meninggal dunia pada tanggal", Isi(TanggalIndonesia(ntcr.TanggalMeninggal))),
                ("Di", Isi(ntcr.TempatMeninggal))
            }, jarakAtas: 8);

            badan.Paragraf("Yang bersangkutan adalah suami/istri*) dari:", jarakAtas: 10, jarakBawah: 4);

            var ahli = new List<(string Label, string? Nilai)>
            {
                ("1. Nama lengkap dan alias", NamaFormatter.ToUpperNama(suamiMeninggal ? ntcr.NamaIstri : warga.Nama)),
                ("2. Bin/Binti", Isi(NamaFormatter.ToUpperNama(suamiMeninggal ? ntcr.AyahCalonIstri?.Nama : ntcr.AyahCalonSuami?.Nama))),
                ("3. Nomor Induk Kependudukan", Isi(suamiMeninggal ? ntcr.NikIstri : warga.NIK)),
                ("4. Tempat dan tanggal lahir", TempatTanggalLahir(
                    suamiMeninggal ? ntcr.TempatLahirIstri : warga.TempatLahir,
                    suamiMeninggal ? ntcr.TanggalLahirIstri : warga.TanggalLahir)),
                ("5. Kewarganegaraan", Isi(suamiMeninggal ? ntcr.KewarganegaraanIstri ?? "WNI" : warga.Kewarganegaraan ?? "WNI")),
                ("6. Agama", Isi(suamiMeninggal ? ntcr.AgamaIstri : warga.Agama)),
                ("7. Pekerjaan", Isi(suamiMeninggal ? ntcr.PekerjaanIstri : warga.Pekerjaan)),
                ("8. Alamat", AlamatCalon(!suamiMeninggal, ntcr, warga))
            };
            TabelBaris(badan, ahli);

            AddKeteranganBebas(badan, suratData, keteranganTextBox);

            badan.Paragraf("Demikian surat keterangan ini dibuat dengan mengingat sumpah jabatan dan untuk digunakan seperlunya.",
                rata: Rata.Justify, indentKiri: 20, jarakAtas: 12);

            badan.Paragraf("*) coret yang tidak perlu", ukuran: 9f, jarakAtas: 6);

            TandaTanganDesa(badan, suratData, desa);
        }


    }
}
