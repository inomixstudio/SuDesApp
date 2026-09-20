// KerapatanSurat.cs
// Tingkat kerapatan tata letak surat.
//
// Dokumen SELALU dicoba dulu pada kerapatan Normal supaya tampilannya lapang dan
// sama seperti sebelumnya. Hanya bila isinya melebihi jumlah halaman yang
// diizinkan (mis. NTCR di kertas A4 yang ±94pt lebih pendek dari F4), dokumen
// dirender ulang dengan kerapatan yang lebih rapat sampai muat. Dengan begitu
// surat yang sudah muat tidak berubah sama sekali.
using System;
using System.Threading;

namespace SuDesApp.GeneratorPdf
{
    /// <summary>Satu tingkat kerapatan: jarak baris, jarak paragraf, dan ruang tanda tangan.</summary>
    public sealed class KerapatanSurat
    {
        public string Nama { get; }

        /// <summary>Faktor tinggi baris teks paragraf (Normal = 1,20× ukuran font).</summary>
        public float FaktorLeadingTeks { get; }

        /// <summary>Jarak baris di dalam sel tabel (point absolut).</summary>
        public float LeadingSelTabel { get; }

        /// <summary>Padding atas/bawah setiap sel tabel (point).</summary>
        public float PaddingSelTabel { get; }

        /// <summary>Jumlah baris ruang kosong untuk tanda tangan.</summary>
        public float BarisRuangTandaTangan { get; }

        /// <summary>Pengali jarak antar paragraf/blok (jarakAtas & jarakBawah).</summary>
        public float FaktorJarakBlok { get; }

        /// <summary>
        /// Ukuran font blok kepala.
        ///
        /// Blok lampiran ("LAMPIRAN IV / KEPUTUSAN DIREKTUR JENDERAL …") pada blanko
        /// NTCR tingginya memakan 5 baris. Bila isi blanko menjadi terlalu panjang,
        /// ukuran font blok ini ikut mengecil bersama kerapatan surat, sehingga blanko
        /// tetap muat dalam satu halaman tanpa mengorbankan bagian isi.
        /// </summary>
        public float UkuranFontLampiran { get; }

        /// <summary>Margin atas halaman (point) — menipis bersama kerapatan.</summary>
        public float MarginAtas { get; }

        /// <summary>Margin bawah halaman (point) — menipis bersama kerapatan.</summary>
        public float MarginBawah { get; }

        private KerapatanSurat(
            string nama,
            float faktorLeadingTeks,
            float leadingSelTabel,
            float paddingSelTabel,
            float barisRuangTandaTangan,
            float faktorJarakBlok,
            float ukuranFontLampiran,
            float marginAtas,
            float marginBawah)
        {
            Nama = nama;
            FaktorLeadingTeks = faktorLeadingTeks;
            LeadingSelTabel = leadingSelTabel;
            PaddingSelTabel = paddingSelTabel;
            BarisRuangTandaTangan = barisRuangTandaTangan;
            FaktorJarakBlok = faktorJarakBlok;
            UkuranFontLampiran = ukuranFontLampiran;
            MarginAtas = marginAtas;
            MarginBawah = marginBawah;
        }

        /// <summary>Tampilan bawaan: seperti sebelum ada penyesuaian otomatis.</summary>
        public static readonly KerapatanSurat Normal =
            new("Normal", 1.20f, 15f, 1f, 4f, 1.00f, 10f, 36f, 30f);

        /// <summary>Dipilih bila surat masih melimpah ke halaman berikutnya.</summary>
        public static readonly KerapatanSurat Rapat =
            new("Rapat", 1.12f, 13.8f, 0.5f, 3f, 0.70f, 8.5f, 32f, 24f);

        /// <summary>Rapat tetapi masih terbaca nyaman.</summary>
        public static readonly KerapatanSurat SangatRapat =
            new("Sangat rapat", 1.07f, 13.2f, 0.35f, 2.75f, 0.55f, 7.5f, 28f, 20f);

        /// <summary>
        /// Tingkat terakhir: jarak dan ukuran font blok kepala paling kecil, dipakai
        /// hanya bila blanko yang isinya panjang (mis. N5) belum muat juga.
        /// </summary>
        public static readonly KerapatanSurat PalingRapat =
            new("Paling rapat", 1.03f, 12.6f, 0.2f, 2.5f, 0.40f, 6.5f, 24f, 18f);

        /// <summary>Urutan percobaan: dari lapang ke rapat.</summary>
        public static readonly KerapatanSurat[] Bertingkat = { Normal, Rapat, SangatRapat, PalingRapat };

        // ------------------------------------------------------------------
        // Kerapatan yang sedang dipakai saat merender satu dokumen.
        // Disimpan per konteks async (AsyncLocal) supaya dua dokumen yang
        // dibuat bersamaan — mis. dari UI dan dari layanan WhatsApp — tidak
        // saling menimpa pengaturannya.
        // ------------------------------------------------------------------
        private static readonly AsyncLocal<KerapatanSurat> _aktif = new();

        /// <summary>Kerapatan yang sedang berlaku untuk rendering saat ini.</summary>
        public static KerapatanSurat Aktif => _aktif.Value ?? Normal;

        /// <summary>Pakai <paramref name="kerapatan"/> selama blok rendering berlangsung.</summary>
        public static IDisposable Pakai(KerapatanSurat kerapatan)
        {
            var sebelumnya = _aktif.Value;
            _aktif.Value = kerapatan ?? Normal;
            return new Pemulih(sebelumnya);
        }

        private sealed class Pemulih : IDisposable
        {
            private readonly KerapatanSurat? _sebelumnya;

            public Pemulih(KerapatanSurat? sebelumnya) => _sebelumnya = sebelumnya;

            public void Dispose() => _aktif.Value = _sebelumnya!;
        }
    }
}
