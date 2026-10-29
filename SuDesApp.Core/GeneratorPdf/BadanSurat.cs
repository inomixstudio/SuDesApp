// BadanSurat.cs
// Badan surat: daftar potongan siap render yang diisi generator, urut sesuai
// pemanggilan. Setiap potongan menjadi satu item tersendiri sehingga pemenggalan
// halaman tetap sama seperti sebelumnya.
using SudesPdf.Core.Layout;
using SuDesApp.Data.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SuDesApp.GeneratorPdf
{
    /// <summary>
    /// Potongan-potongan badan surat yang siap dirender mesin SudesPdf. Generator mengisi
    /// daftar ini lewat <see cref="Paragraf"/>, <see cref="TabelFormulir"/>, atau
    /// <see cref="Blok"/>; tiap potongan dirender sebagai satu item kolom.
    ///
    /// Kelas ini hanya mengumpulkan urutan — cara mencetaknya ada di
    /// <see cref="SuratRenderer"/>.
    /// </summary>
    public sealed class BadanSurat
    {
        private readonly List<Action<IContainer>> _blok = new();

        /// <summary>Tambahkan potongan bebas (kop, judul, tabel, daftar, gambar) dengan API SudesPdf.Layout.</summary>
        public void Blok(Action<IContainer> blok)
        {
            ArgumentNullException.ThrowIfNull(blok);
            _blok.Add(blok);
        }

        /// <summary>Render potongan dalam urutan saat generator menambahkannya.</summary>
        public void Render(ColumnDescriptor kolom)
        {
            ArgumentNullException.ThrowIfNull(kolom);

            foreach (var blok in _blok)
            {
                var render = blok;
                kolom.Item().Element(container => render(container));
            }
        }

        /// <summary>Tambahkan satu paragraf teks.</summary>
        public void Paragraf(
            string? teks,
            Rata rata = Rata.Justify,
            bool tebal = false,
            bool miring = false,
            float ukuran = SuratRenderer.UkuranTeks,
            float indentKiri = 0,
            float indentBarisPertama = 0,
            float jarakAtas = 0,
            float jarakBawah = 0,
            string? warna = null)
        {
            Blok(container => SuratRenderer.Paragraf(
                container, teks, rata, tebal, miring, ukuran, indentKiri,
                indentBarisPertama, jarakAtas, jarakBawah, warna));
        }

        /// <summary>Paragraf dengan campuran tebal/tidak tebal dalam satu baris.</summary>
        public void ParagrafCampur(
            IEnumerable<(string Teks, bool Tebal)> bagian,
            Rata rata = Rata.Justify,
            float ukuran = SuratRenderer.UkuranTeks,
            float indentKiri = 0,
            float indentBarisPertama = 0,
            float jarakAtas = 0,
            float jarakBawah = 0)
        {
            // Disalin saat dipanggil supaya urutan tidak berubah bila daftar sumber
            // dimodifikasi setelah blok ini ditambahkan.
            var salinan = bagian?.ToArray() ?? Array.Empty<(string, bool)>();
            if (salinan.Length == 0) return;

            Blok(container => SuratRenderer.ParagrafCampur(
                container, salinan, rata, ukuran, indentKiri, indentBarisPertama,
                jarakAtas, jarakBawah));
        }

        /// <summary>
        /// Tabel berlabel ala surat desa: kolom label, titik dua, lalu nilai.
        /// Nilai baris "Nama" dicetak tebal seperti template resmi.
        /// </summary>
        public void TabelFormulir(
            IEnumerable<(string Label, string? Nilai)> baris,
            float indentKiri = 0,
            float jarakAtas = 0,
            float jarakBawah = 0,
            bool tebalkanNama = true,
            IEnumerable<string>? labelTebal = null)
        {
            if (baris == null) return;

            var salinan = baris.ToArray();
            Blok(container => SuratRenderer.TabelFormulir(
                container, salinan, jarakAtas, jarakBawah, tebalkanNama, indentKiri,
                labelTebal?.ToArray() ?? Array.Empty<string>()));
        }

        /// <summary>Daftar bernomor (1., 2., …) dengan nomor di kolom tersendiri.</summary>
        public void DaftarBernomor(
            IEnumerable<string> butir,
            float indentKiri = 0,
            float jarakAtas = 0,
            float jarakBawah = 0,
            float lebarNomor = 20f,
            float spasiAntarButir = 3f)
        {
            if (butir == null) return;

            var salinan = butir.ToArray();
            if (salinan.Length == 0) return;

            Blok(container => SuratRenderer.DaftarBernomor(
                container, salinan, indentKiri, jarakAtas, jarakBawah,
                lebarNomor, spasiAntarButir));
        }
    }
}
