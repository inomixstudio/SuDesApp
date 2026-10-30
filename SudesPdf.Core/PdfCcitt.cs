using System;

namespace SudesPdf.Core;

/// <summary>
/// Dekoder gambar faks CCITT Group 3 (PD-T4, kode K ≥ 0) dan Group 4 (PD-T6,
/// K &lt; 0) untuk /CCITTFaxDecode — filter yang dipakai mayoritas dokumen hasil
/// pindai (surat, SK, regulasi). Sebelum dekoder ini ada, isi filter diperlakukan
/// sebagai sampel mentah sehingga halaman pindai menjadi hitam garis bising.
///
/// Keluaran sesuai konvensi PDF (ISO 32000-1 §7.4.6): piksel dengan bit 1 =
/// putih dan bit 0 = hitam; bila /BlackIs1 aktif, hasil dibalik sehingga bit 1
/// = hitam.
///
/// Tabel kode ITU-T T.4 dan mesin pengurai kode diambil dari implementasi
/// PDFium (core/fxcodec/fax/faxmodule.cpp — Foxit Software & penulis PDFium,
/// BSD-3-Clause); isinya sendiri adalah data standar ITU yang sama di semua
/// dekoder faks.
/// </summary>
public static class PdfCcitt
{
    /// <summary>Debug saja: tuliskan sebab pulang-dini.</summary>
    internal static bool Cerita = false;

    /// <summary>Batas dimensi gambar pindai; nilai sama yang dipakai JBIG2.</summary>
    public const int MaxDimension = 65535;

    // Tabel instrumen kode run hitam (T.4): kur Киеве (bit ke bit). Byte
    // pertama tiap posisi = hitungan pasang (kode, panjang % 256, panjang / 256),
    // 0xff = akhir tabel.
    private static readonly byte[] KodeRunHitam =
    {
        0, 2, 2, 3, 0, 3, 2, 0, 2, 2, 1, 0, 3, 4, 0, 2,
        2, 6, 0, 3, 5, 0, 1, 3, 7, 0, 2, 4, 9, 0, 5, 8,
        0, 3, 4, 0x0a, 0, 5, 0x0b, 0, 7, 0x0c, 0, 2, 4, 0x0d, 0, 7,
        0x0e, 0, 1, 0x18, 0x0f, 0, 5, 8, 0x12, 0, 0x0f, 0x40, 0, 0x17, 0x10, 0,
        0x18, 0x11, 0, 0x37, 0, 0, 0x0a, 8, 0, 7, 0x0c, 0x40, 7, 0x0d, 0x80, 7,
        0x17, 0x18, 0, 0x18, 0x19, 0, 0x28, 0x17, 0, 0x37, 0x16, 0, 0x67, 0x13, 0, 0x68,
        0x14, 0, 0x6c, 0x15, 0, 0x36, 0x12, 0xc0, 7, 0x13, 0, 8, 0x14, 0x40, 8, 0x15,
        0x80, 8, 0x16, 0xc0, 8, 0x17, 0, 9, 0x1c, 0x40, 9, 0x1d, 0x80, 9, 0x1e, 0xc0,
        9, 0x1f, 0, 0x0a, 0x24, 0x34, 0, 0x27, 0x37, 0, 0x28, 0x38, 0, 0x2b, 0x3b, 0,
        0x2c, 0x3c, 0, 0x33, 0x40, 1, 0x34, 0x80, 1, 0x35, 0xc0, 1, 0x37, 0x35, 0, 0x38,
        0x36, 0, 0x52, 0x32, 0, 0x53, 0x33, 0, 0x54, 0x2c, 0, 0x55, 0x2d, 0, 0x56, 0x2e,
        0, 0x57, 0x2f, 0, 0x58, 0x39, 0, 0x59, 0x3a, 0, 0x5a, 0x3d, 0, 0x5b, 0, 1,
        0x64, 0x30, 0, 0x65, 0x31, 0, 0x66, 0x3e, 0, 0x67, 0x3f, 0, 0x68, 0x1e, 0, 0x69,
        0x1f, 0, 0x6a, 0x20, 0, 0x6b, 0x21, 0, 0x6c, 0x28, 0, 0x6d, 0x29, 0, 0xc8, 0x80,
        0, 0xc9, 0xc0, 0, 0xca, 0x1a, 0, 0xcb, 0x1b, 0, 0xcc, 0x1c, 0, 0xcd, 0x1d, 0,
        0xd2, 0x22, 0, 0xd3, 0x23, 0, 0xd4, 0x24, 0, 0xd5, 0x25, 0, 0xd6, 0x26, 0, 0xd7,
        0x27, 0, 0xda, 0x2a, 0, 0xdb, 0x2b, 0, 0x14, 0x4a, 0x80, 2, 0x4b, 0xc0, 2, 0x4c,
        0, 3, 0x4d, 0x40, 3, 0x52, 0, 5, 0x53, 0x40, 5, 0x54, 0x80, 5, 0x55, 0xc0,
        5, 0x5a, 0, 6, 0x5b, 0x40, 6, 0x64, 0x80, 6, 0x65, 0xc0, 6, 0x6c, 0, 2,
        0x6d, 0x40, 2, 0x72, 0x80, 3, 0x73, 0xc0, 3, 0x74, 0, 4, 0x75, 0x40, 4, 0x76,
        0x80, 4, 0x77, 0xc0, 4, 0xff,
    };

    // Tabel kode run putih (T.4). Format sama dengan KodeRunHitam.
    private static readonly byte[] KodeRunPutih =
    {
        0, 0, 0, 6, 7, 2, 0, 8, 3, 0, 0x0b, 4, 0, 0x0c, 5, 0,
        0x0e, 6, 0, 0x0f, 7, 0, 6, 7, 0x0a, 0, 8, 0x0b, 0, 0x12, 0x80, 0,
        0x13, 8, 0, 0x14, 9, 0, 0x1b, 0x40, 0, 9, 3, 0x0d, 0, 7, 1, 0,
        8, 0x0c, 0, 0x17, 0xc0, 0, 0x18, 0x80, 6, 0x2a, 0x10, 0, 0x2b, 0x11, 0, 0x34,
        0x0e, 0, 0x35, 0x0f, 0, 0x0c, 3, 0x16, 0, 4, 0x17, 0, 8, 0x14, 0, 0x0c,
        0x13, 0, 0x13, 0x1a, 0, 0x17, 0x15, 0, 0x18, 0x1c, 0, 0x24, 0x1b, 0, 0x27, 0x12,
        0, 0x28, 0x18, 0, 0x2b, 0x19, 0, 0x37, 0, 1, 0x2a, 2, 0x1d, 0, 3, 0x1e,
        0, 4, 0x2d, 0, 5, 0x2e, 0, 0x0a, 0x2f, 0, 0x0b, 0x30, 0, 0x12, 0x21, 0,
        0x13, 0x22, 0, 0x14, 0x23, 0, 0x15, 0x24, 0, 0x16, 0x25, 0, 0x17, 0x26, 0, 0x1a,
        0x1f, 0, 0x1b, 0x20, 0, 0x24, 0x35, 0, 0x25, 0x36, 0, 0x28, 0x27, 0, 0x29, 0x28,
        0, 0x2a, 0x29, 0, 0x2b, 0x2a, 0, 0x2c, 0x2b, 0, 0x2d, 0x2c, 0, 0x32, 0x3d, 0,
        0x33, 0x3e, 0, 0x34, 0x3f, 0, 0x35, 0, 0, 0x36, 0x40, 1, 0x37, 0x80, 1, 0x4a,
        0x3b, 0, 0x4b, 0x3c, 0, 0x52, 0x31, 0, 0x53, 0x32, 0, 0x54, 0x33, 0, 0x55, 0x34,
        0, 0x58, 0x37, 0, 0x59, 0x38, 0, 0x5a, 0x39, 0, 0x5b, 0x3a, 0, 0x64, 0xc0, 1,
        0x65, 0, 2, 0x67, 0x80, 2, 0x68, 0x40, 2, 0x10, 0x98, 0xc0, 5, 0x99, 0, 6,
        0x9a, 0x40, 6, 0x9b, 0xc0, 6, 0xcc, 0xc0, 2, 0xcd, 0, 3, 0xd2, 0x40, 3, 0xd3,
        0x80, 3, 0xd4, 0xc0, 3, 0xd5, 0, 4, 0xd6, 0x40, 4, 0xd7, 0x80, 4, 0xd8, 0xc0,
        4, 0xd9, 0, 5, 0xda, 0x40, 5, 0xdb, 0x80, 5, 0, 3, 8, 0, 7, 0x0c,
        0x40, 7, 0x0d, 0x80, 7, 0x0a, 0x12, 0xc0, 7, 0x13, 0, 8, 0x14, 0x40, 8, 0x15,
        0x80, 8, 0x16, 0xc0, 8, 0x17, 0, 9, 0x1c, 0x40, 9, 0x1d, 0x80, 9, 0x1e, 0xc0,
        9, 0x1f, 0, 0x0a, 0xff,
    };

    /// <summary>
    /// Dekode seluruh gambar faks menjadi bitmap 1 bit per piksel yang dipaket
    /// per baris (panjang baris = (lebar + 7) / 8 byte, MSB dulu). Bit 1 =
    /// putih, bit 0 = hitam; bila blackIs1, tiap baris dibalik. Mengembalikan
    /// null kalau parameter tidak masuk akal — pemanggil boleh membiarkan
    /// gambar tidak digambar daripada merender sampah.
    /// </summary>
    public static byte[]? Decode(
        byte[] source, int lebar, int tinggi, int k,
        bool endOfLine, bool byteAligned, bool blackIs1)
    {
        if (lebar <= 0 || tinggi <= 0
            || lebar > MaxDimension || tinggi > MaxDimension
            || source.Length == 0)
        {
            return null;
        }

        int pitch = (lebar + 7) / 8;
        var output = new byte[pitch * tinggi];
        var decoder = new DekoderFaks(source, lebar, tinggi, k, endOfLine, byteAligned, blackIs1, output, pitch);
        decoder.Run();
        return output;
    }

    private sealed class DekoderFaks
    {
        private readonly byte[] _source;
        private readonly int _bitsize;
        private readonly int _lebar;
        private readonly int _tinggi;
        private readonly int _k;
        private readonly bool _endOfLine;
        private readonly bool _byteAligned;
        private readonly bool _hitam;
        private readonly byte[] _output;
        private readonly byte[] _ref;
        private readonly byte[] _baris;
        private int _bitpos;

        public DekoderFaks(byte[] source, int lebar, int tinggi, int k,
            bool endOfLine, bool byteAligned, bool hitam, byte[] output, int pitch)
        {
            _source = source;
            _bitsize = source.Length * 8;
            _lebar = lebar;
            _tinggi = tinggi;
            _k = k;
            _endOfLine = endOfLine;
            _byteAligned = byteAligned;
            _hitam = hitam;
            _output = output;
            _ref = new byte[pitch];
            _baris = new byte[pitch];
            Array.Fill(_ref, (byte)0xff);
        }

        public void Run()
        {
            int pitch = _baris.Length;
            for (int baris = 0; baris < _tinggi; baris++)
            {
                int mulai = baris * pitch;
                LompatiEol();
                if (_bitpos >= _bitsize) break;

                Array.Fill(_baris, (byte)0xff); // setiap bit 1 berarti putih
                if (_k < 0)
                {
                    BarisG4();
                }
                else if (_k == 0)
                {
                    Baris1D();
                }
                else
                {
                    // T.4 dua dimensi: satu bit mengawakan baris ini (1 = satu
                    // dimensi, 0 = dua dimensi).
                    if (Baca())
                    {
                        Baris1D();
                    }
                    else
                    {
                        BarisG4();
                    }

                    Array.Copy(_baris, _ref, pitch);
                }

                if (_endOfLine) LompatiEol();
                if (_byteAligned && _bitpos < _bitsize)
                {
                    int pos0 = _bitpos;
                    int pos1 = (pos0 + 7) & ~7;
                    bool selaras = true;
                    while (pos0 < pos1)
                    {
                        if ((_source[pos0 / 8] & (1 << (7 - pos0 % 8))) != 0)
                        {
                            // Byte terpotong di ujung data: selaraskan hanya bila
                            // sisa bit sampai batas byte semuanya nol.
                            selaras = false;
                            break;
                        }

                        pos0++;
                    }

                    if (selaras) _bitpos = pos1;
                }

                for (int i = 0; i < pitch; i++)
                {
                    _output[mulai + i] = _hitam ? (byte)~_baris[i] : _baris[i];
                }
            }
        }

        /// <summary>Lewati EOL (000000000001) agar bit berikutnya siap dibaca.</summary>
        private void LompatiEol()
        {
            int startbit = _bitpos;
            while (_bitpos < _bitsize)
            {
                if (!Baca()) continue;
                if (_bitpos - startbit <= 11) _bitpos = startbit;
                return;
            }
        }

        private bool Baca()
        {
            int pos = _bitpos++;
            return (_source[pos / 8] & (1 << (7 - pos % 8))) != 0;
        }

        private int BacaJikaAda()
        {
            if (_bitpos >= _bitsize) return -1;
            return Baca() ? 1 : 0;
        }

        /// <summary>Satu baris kode satu dimensi (Modified Huffman).</summary>
        private void Baris1D()
        {
            bool putih = true;
            int posisi = 0;
            while (true)
            {
                if (_bitpos >= _bitsize) return;
                int panjang = 0;
                while (true)
                {
                    int run = AmbilRun(putih ? KodeRunPutih : KodeRunHitam);
                    if (run < 0)
                    {
                        while (_bitpos < _bitsize)
                        {
                            if (Baca()) return;
                        }

                        return;
                    }

                    panjang += run;
                    if (run < 64) break;
                }

                if (!putih) IsiBit(_baris, _lebar, posisi, posisi + panjang);
                posisi += panjang;
                if (posisi >= _lebar) return;
                putih = !putih;
            }
        }

        /// <summary>Satu baris kode dua dimensi (T.6/T.4 2D).</summary>
        private void BarisG4()
        {
            int a0 = -1;
            bool warnaA0 = true;
            while (true)
            {
                if (_bitpos >= _bitsize) return;

                CariB1B2(_ref, _lebar, a0, warnaA0, out int b1, out int b2);
                int vDelta = 0;
                int cek = BacaJikaAda();
                if (cek == -1) return;
                if (cek == 0)
                {
                    cek = BacaJikaAda();
                    if (cek == -1) return;
                    int cek2 = BacaJikaAda();
                    if (cek2 == -1) return;
                    if (cek == 1)
                    {
                        // Vertikal ±1.
                        vDelta = cek2 == 1 ? 1 : -1;
                    }
                    else if (cek2 == 1)
                    {
                        // Horisontal: dua kode run MH berturut.
                        int run1 = 0;
                        while (true)
                        {
                            int run = AmbilRun(warnaA0 ? KodeRunPutih : KodeRunHitam);
                            if (run < 0) { if (Cerita) Console.Error.WriteLine($"CH: run1 gagal a0={a0}"); return; }
                            run1 += run;
                            if (run < 64) break;
                        }

                        if (a0 < 0) run1++;
                        if (run1 < 0) { if (Cerita) Console.Error.WriteLine($"CH: run1 negatif"); return; }
                        int a1 = a0 + run1;
                        if (!warnaA0) IsiBit(_baris, _lebar, a0, a1);

                        int run2 = 0;
                        while (true)
                        {
                            int run = AmbilRun(warnaA0 ? KodeRunHitam : KodeRunPutih);
                            if (run < 0) { if (Cerita) Console.Error.WriteLine($"CH: run2 gagal a1={a1}"); return; }
                            run2 += run;
                            if (run < 64) break;
                        }

                        if (run2 < 0) return;
                        int a2 = a1 + run2;
                        if (warnaA0) IsiBit(_baris, _lebar, a1, a2);
                        a0 = a2;
                        if (a0 < _lebar) continue;
                        if (Cerita) Console.Error.WriteLine($"CH: aki {a2} >= lebar (baru saja)");
                        return;
                    }
                    else
                    {
                        cek = BacaJikaAda();
                        if (cek == -1) return;
                        int cek3 = BacaJikaAda();
                        if (cek3 == -1) return;
                        if (cek == 1)
                        {
                            // Vertikal ±2.
                            vDelta = cek3 == 1 ? 2 : -2;
                        }
                        else if (cek3 == 1)
                        {
                            cek = BacaJikaAda();
                            if (cek == -1) return;
                            // Vertikal ±3.
                            vDelta = cek == 1 ? 3 : -3;
                        }
                        else
                        {
                            cek = BacaJikaAda();
                            if (cek == -1) return;
                            if (cek == 1)
                            {
                                // Kode ekstensi: lewati, ulangi pada a0 yang sama.
                                _bitpos += 3;
                                continue;
                            }

                            _bitpos += 5;
                            return;
                        }
                    }
                }

                int a1v = b1 + vDelta;
                if (!warnaA0) IsiBit(_baris, _lebar, a0, a1v);
                if (a1v >= _lebar) { if (Cerita) Console.Error.WriteLine($"CH: a1 {a1v} >= lebar (vertikal)"); return; }
                // Posisi elemen harus monotonik naik; mundur artinya data rusak.
                if (a0 >= a1v) { if (Cerita) Console.Error.WriteLine($"CH: mundur a0 {a0} >= a1 {a1v}"); return; }
                a0 = a1v;
                warnaA0 = !warnaA0;
            }
        }

        private void CariB1B2(byte[] refBuf, int kolom, int a0, bool warnaA0, out int b1, out int b2)
        {
            bool firstBit = a0 < 0 || (refBuf[a0 / 8] & (1 << (7 - a0 % 8))) != 0;
            b1 = CariBit(refBuf, kolom, a0 + 1, !firstBit);
            if (b1 >= kolom)
            {
                b1 = b2 = kolom;
                return;
            }

            if (firstBit == !warnaA0)
            {
                b1 = CariBit(refBuf, kolom, b1 + 1, firstBit);
                firstBit = !firstBit;
            }

            if (b1 >= kolom)
            {
                b1 = b2 = kolom;
                return;
            }

            b2 = CariBit(refBuf, kolom, b1 + 1, firstBit);
        }

        /// <summary>Posisi bit pertama dengan nilai tertentu, mulai posisi yang diisi.</summary>
        private static int CariBit(byte[] data, int maks, int mulai, bool cariSatu)
        {
            byte xor = cariSatu ? (byte)0 : (byte)0xff;
            for (int posisi = mulai; posisi < maks; posisi++)
            {
                if (((data[posisi / 8] ^ xor) & (1 << (7 - posisi % 8))) != 0) return posisi;
            }

            return maks;
        }

        /// <summary>
        /// Baca kode run dari tabel instrumen (trie satu bit per langkah).
        /// Mengembalikan panjang run, atau -1 bila data/langkah gugur.
        /// </summary>
        private int AmbilRun(byte[] table)
        {
            int kode = 0;
            int offset = 0;
            while (true)
            {
                byte langkah = table[offset++];
                if (langkah == 0xff) return -1;
                if (_bitpos >= _bitsize) return -1;

                kode <<= 1;
                if (Baca()) kode |= 1;
                int nextOffset = offset + langkah * 3;
                for (; offset < nextOffset; offset += 3)
                {
                    if (table[offset] == kode)
                    {
                        return table[offset + 1] + table[offset + 2] * 256;
                    }
                }
            }
        }

        /// <summary>Nyalakan bit 0 (hitam) pada selang [mulai, akhir).</summary>
        private static void IsiBit(byte[] dest, int kolom, int mulai, int akhir)
        {
            mulai = Math.Max(mulai, 0);
            akhir = Math.Clamp(akhir, 0, kolom);
            if (mulai >= akhir) return;

            int bytePertama = mulai / 8;
            int byteAkhir = (akhir - 1) / 8;
            if (bytePertama == byteAkhir)
            {
                for (int i = mulai % 8; i <= (akhir - 1) % 8; i++)
                {
                    dest[bytePertama] -= (byte)(1 << (7 - i));
                }

                return;
            }

            for (int i = mulai % 8; i < 8; i++)
            {
                dest[bytePertama] -= (byte)(1 << (7 - i));
            }

            for (int i = 0; i <= (akhir - 1) % 8; i++)
            {
                dest[byteAkhir] -= (byte)(1 << (7 - i));
            }

            if (byteAkhir > bytePertama + 1)
            {
                Array.Fill(dest, (byte)0, bytePertama + 1, byteAkhir - bytePertama - 1);
            }
        }
    }
}
