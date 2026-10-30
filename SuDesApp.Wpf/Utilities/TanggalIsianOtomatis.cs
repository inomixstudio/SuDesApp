using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SuDesApp.Wpf.Utilities
{
    /// <summary>
    /// Isian tanggal <c>DD-MM-YYYY</c> yang ramah pengguna awam: hanya angka yang
    /// diterima, dan tanda hubung disisipkan sendiri setelah tanggal dan bulan.
    /// Pengguna cukup mengetik <c>01011990</c> dan kolom menampilkan
    /// <c>01-01-1990</c>; tanda hubung yang diketik manual diabaikan.
    ///
    /// Dipasang per kolom di XAML:
    /// <code>utilitas:TanggalIsianOtomatis.Aktif="True"</code>
    /// </summary>
    public static class TanggalIsianOtomatis
    {
        private const int JumlahDigitTanggal = 8;

        // Penanda agar penyuntingan teks oleh perilaku ini tidak memicu
        // TextChanged sekali lagi (yang akan menggeser posisi kursor).
        private static bool _sedangMemformat;

        public static readonly DependencyProperty AktifProperty = DependencyProperty.RegisterAttached(
            "Aktif",
            typeof(bool),
            typeof(TanggalIsianOtomatis),
            new PropertyMetadata(false, SaatAktifBerubah));

        public static bool GetAktif(DependencyObject kolom) => (bool)kolom.GetValue(AktifProperty);

        public static void SetAktif(DependencyObject kolom, bool nilai) => kolom.SetValue(AktifProperty, nilai);

        private static void SaatAktifBerubah(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBox kolom) return;

            kolom.TextChanged -= SaatTeksBerubah;
            kolom.PreviewTextInput -= SaatMengetik;
            kolom.PreviewKeyDown -= SaatTombolDitekan;

            if (e.NewValue is true)
            {
                // Tanda hubung ikut dihitung, jadi 8 digit + 2 pemisah = 10 karakter.
                if (kolom.MaxLength <= 0 || kolom.MaxLength > 10) kolom.MaxLength = 10;

                kolom.TextChanged += SaatTeksBerubah;
                kolom.PreviewTextInput += SaatMengetik;
                kolom.PreviewKeyDown += SaatTombolDitekan;

                Rapikan(kolom, kolom.Text, JumlahDigitSebelum(kolom.Text, kolom.Text.Length));
            }
        }

        /// <summary>
        /// Hanya angka dan tanda hubung yang boleh masuk; huruf dan spasi ditolak.
        /// Tanda hubung yang diketik sendiri dibiarkan: Rapikan menyusunnya ulang
        /// ke posisi yang benar, jadi ketikan pengguna tidak pernah "hilang".
        /// </summary>
        private static void SaatMengetik(object pengirim, TextCompositionEventArgs e)
        {
            if (e.Text.Length == 0 || !e.Text.All(c => char.IsDigit(c) || c == '-')) e.Handled = true;
        }

        private static void SaatTeksBerubah(object pengirim, TextChangedEventArgs e)
        {
            if (_sedangMemformat || pengirim is not TextBox kolom) return;

            int digitSebelumKursor = JumlahDigitSebelum(kolom.Text, kolom.CaretIndex);
            Rapikan(kolom, kolom.Text, digitSebelumKursor);
        }

        /// <summary>
        /// Backspace/Delete di sekitar tanda hubung menghapus ANGKA, bukan pemisahnya,
        /// supaya satu ketukan benar-benar menghapus satu angka.
        /// </summary>
        private static void SaatTombolDitekan(object pengirim, KeyEventArgs e)
        {
            if (pengirim is not TextBox kolom) return;
            if (kolom.SelectionLength > 0) return;
            if (e.Key != Key.Back && e.Key != Key.Delete) return;

            string digit = AmbilDigit(kolom.Text);
            int sebelumKursor = JumlahDigitSebelum(kolom.Text, kolom.CaretIndex);

            if (e.Key == Key.Back)
            {
                if (sebelumKursor == 0) return;
                e.Handled = true;
                Rapikan(kolom, digit.Remove(sebelumKursor - 1, 1), sebelumKursor - 1);
            }
            else
            {
                if (sebelumKursor >= digit.Length) return;
                e.Handled = true;
                Rapikan(kolom, digit.Remove(sebelumKursor, 1), sebelumKursor);
            }
        }

        /// <summary>
        /// Menyusun ulang isian dari deretan angka: <c>01011990</c> menjadi
        /// <c>01-01-1990</c>. Tanda hubung juga muncul segera setelah dua angka
        /// pertama (dan empat angka), jadi pengguna tidak perlu mengetiknya.
        /// </summary>
        private static void Rapikan(TextBox kolom, string teks, int digitSebelumKursor)
        {
            string digit = AmbilDigit(teks);
            if (digit.Length > JumlahDigitTanggal) digit = digit[..JumlahDigitTanggal];

            string baru = Format(digit);
            if (baru == kolom.Text)
            {
                kolom.CaretIndex = IndeksKursor(kolom.Text, Math.Min(digitSebelumKursor, digit.Length));
                return;
            }

            bool sebelumnya = _sedangMemformat;
            _sedangMemformat = true;
            try
            {
                kolom.Text = baru;
                kolom.CaretIndex = IndeksKursor(baru, Math.Min(digitSebelumKursor, digit.Length));
            }
            finally
            {
                _sedangMemformat = sebelumnya;
            }
        }

        private static string Format(string digit)
        {
            if (digit.Length <= 2) return digit.Length == 2 ? digit + "-" : digit;

            var teks = new StringBuilder(digit.Length + 2);
            teks.Append(digit, 0, 2).Append('-');
            if (digit.Length <= 4)
            {
                teks.Append(digit, 2, digit.Length - 2);
                if (digit.Length == 4) teks.Append('-');
                return teks.ToString();
            }

            teks.Append(digit, 2, 2).Append('-').Append(digit, 4, digit.Length - 4);
            return teks.ToString();
        }

        private static string AmbilDigit(string teks) => new(teks.Where(char.IsDigit).ToArray());

        private static int JumlahDigitSebelum(string teks, int indeks)
        {
            int batas = Math.Min(Math.Max(indeks, 0), teks.Length);
            int jumlah = 0;
            for (int i = 0; i < batas; i++) if (char.IsDigit(teks[i])) jumlah++;
            return jumlah;
        }

        /// <summary>Posisi kursor setelah sejumlah angka, melewati tanda hubung otomatis.</summary>
        private static int IndeksKursor(string teks, int jumlahDigit)
        {
            if (jumlahDigit <= 0) return 0;

            int terlihat = 0;
            for (int i = 0; i < teks.Length; i++)
            {
                if (!char.IsDigit(teks[i])) continue;

                terlihat++;
                if (terlihat != jumlahDigit) continue;

                int posisi = i + 1;
                if (posisi < teks.Length && teks[posisi] == '-') posisi++;
                return posisi;
            }

            return teks.Length;
        }
    }
}
