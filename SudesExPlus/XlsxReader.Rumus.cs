using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using SudesExPlus.Style;

namespace SudesExPlus;

internal static partial class XlsxReader
    {
    /// </summary>
    internal static string GeserRumus(string rumus, int deltaBaris, int deltaKolom)
    {
        if (string.IsNullOrEmpty(rumus) || (deltaBaris == 0 && deltaKolom == 0)) return rumus;

        var hasil = new StringBuilder(rumus.Length + 8);
        int i = 0;
        bool dalamTeks = false;

        while (i < rumus.Length)
        {
            char huruf = rumus[i];

            if (huruf == '"')
            {
                dalamTeks = !dalamTeks;
                hasil.Append(huruf);
                i++;
                continue;
            }

            if (dalamTeks)
            {
                hasil.Append(huruf);
                i++;
                continue;
            }

            int panjang = CocokRujukan(rumus, i, out int kolom, out int baris,
                out bool mutlakKolom, out bool mutlakBaris);

            if (panjang > 0)
            {
                int kolomBaru = mutlakKolom ? kolom : kolom + deltaKolom;
                int barisBaru = mutlakBaris ? baris : baris + deltaBaris;

                if (kolomBaru >= 1 && barisBaru >= 1)
                {
                    if (mutlakKolom) hasil.Append('$');
                    hasil.Append(ExcelRujukan.Kolom(kolomBaru));
                    if (mutlakBaris) hasil.Append('$');
                    hasil.Append(barisBaru.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    // Rujukan yang bergeser keluar lembar: biarkan apa adanya.
                    hasil.Append(rumus, i, panjang);
                }

                i += panjang;
                continue;
            }

            hasil.Append(huruf);
            i++;
        }

        return hasil.ToString();
    }

    /// <summary>
    /// Kenali rujukan A1/$A$1 pada posisi <paramref name="mulai"/>; kembalikan
    /// panjangnya. Nama fungsi (LOG10, ATAN2) dan teks biasa tidak dianggap
    /// rujukan karena harus didahului pemisah dan diakhiri digit.
    /// </summary>
    private static int CocokRujukan(
        string teks,
        int mulai,
        out int kolom,
        out int baris,
        out bool mutlakKolom,
        out bool mutlakBaris)
    {
        kolom = 0;
        baris = 0;
        mutlakKolom = false;
        mutlakBaris = false;

        if (mulai > 0)
        {
            char sebelum = teks[mulai - 1];
            if (char.IsLetterOrDigit(sebelum) || sebelum is '_' or '.' or '$') return 0;
        }

        int i = mulai;
        if (i < teks.Length && teks[i] == '$')
        {
            mutlakKolom = true;
            i++;
        }

        int kolomHasil = 0;
        int panjangKolom = 0;
        while (i < teks.Length && char.IsLetter(teks[i]) && panjangKolom < 3)
        {
            kolomHasil = kolomHasil * 26 + (char.ToUpperInvariant(teks[i]) - 'A' + 1);
            i++;
            panjangKolom++;
        }

        if (panjangKolom == 0) return 0;

        // Huruf keempat menandakan nama fungsi/nama lain, bukan rujukan.
        if (i < teks.Length && char.IsLetter(teks[i])) return 0;

        if (i < teks.Length && teks[i] == '$')
        {
            mutlakBaris = true;
            i++;
        }

        int awalAngka = i;
        int barisHasil = 0;
        while (i < teks.Length && char.IsDigit(teks[i]))
        {
            barisHasil = barisHasil * 10 + (teks[i] - '0');
            i++;
        }

        if (i == awalAngka || barisHasil < 1) return 0;

        // Sesudah angka tidak boleh huruf/garis bawah (bagian dari nama).
        if (i < teks.Length && (char.IsLetterOrDigit(teks[i]) || teks[i] == '_')) return 0;

        kolom = kolomHasil;
        baris = barisHasil;
        return i - mulai;
    }

    }
