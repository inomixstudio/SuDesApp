// TemplateSuratWordImporService.cs
// Membaca berkas Word (.docx) lalu menyusunnya menjadi satu atau beberapa
// Template Surat (menu Template Surat).
//
// Berkas dibaca tanpa library tambahan: .docx adalah arsip ZIP berisi
// word/document.xml, sehingga teks, perataan, tebal/miring, dan batas halaman
// (page break / section break) dapat diambil langsung dari XML-nya.
//
// Berkas .doc (format Word 97-2003) dikonversi otomatis lebih dulu menjadi .docx
// memakai Microsoft Word yang terpasang (WordDocConverter), sehingga pengguna tidak
// perlu menyimpan ulang berkasnya secara manual.
//
// Satu halaman/bagian pada berkas dianggap satu bentuk surat. Berkas yang berisi
// beberapa blanko sekaligus tetap bisa disimpan sebagai beberapa template, dan
// bagian yang salah terpisah dapat diabaikan pengguna lewat dialog pemilihan.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using SuDesApp.Data.Models;
using SuDesApp.Utilities;

namespace SuDesApp.Services
{
    public partial class TemplateSuratWordImporService : ITemplateSuratWordImpor
    {
        // =====================================================================
        // Penyusunan definisi template
        // =====================================================================

        private WordSuratKandidat? SusunKandidat(List<WordSuratParagraf> paragraf, int nomor, string namaBerkas)
        {
            string namaDasar = $"{Path.GetFileNameWithoutExtension(namaBerkas)} — bagian {nomor}";
            var (template, judul, jumlahKolom) = SusunTemplate(paragraf, namaDasar);

            if (template.JumlahElemen == 0) return null;

            template.Nama = string.IsNullOrWhiteSpace(judul) ? namaDasar : judul;
            template.Deskripsi =
                $"Dibuat dari berkas Word \"{namaBerkas}\" — bagian {nomor} ({paragraf.Count} baris).";

            return new WordSuratKandidat
            {
                Nomor = nomor,
                Judul = judul,
                JumlahBaris = paragraf.Count,
                JumlahKolom = jumlahKolom,
                Cuplikan = Cuplikan(paragraf),
                Definisi = template
            };
        }

        private static string Cuplikan(List<WordSuratParagraf> paragraf)
        {
            var potongan = paragraf
                .Select(p => p.Teks.Replace('\n', ' ').Replace('\t', ' ').Trim())
                .Where(t => t.Length > 0)
                .Take(3)
                .ToList();

            string gabung = string.Join(" • ", potongan);
            return gabung.Length <= 140 ? gabung : gabung.Substring(0, 140) + "…";
        }

        /// <summary>
        /// Susun definisi template dari paragraf hasil pembacaan: kop, judul, nomor,
        /// tempat &amp; tanggal dikenali lebih dulu, lalu sisanya menjadi blok teks dan
        /// kelompok kolom isian berurutan.
        /// </summary>
        private static (TemplateSuratKustom Template, string Judul, int JumlahKolom) SusunTemplate(
            IReadOnlyList<WordSuratParagraf> paragraf,
            string namaCadangan)
        {
            var template = new TemplateSuratKustom
            {
                Nama = namaCadangan,
                PakaiKop = false,
                PakaiNomor = false,
                PakaiTempatTanggal = false,
                PakaiTandaTangan = false,
                PakaiTeksKaki = false,
                PakaiGrid = false,
                AwalanNomor = "470",
                PolaNomor = TemplateSuratNomor.PolaBawaan
            };

            var teks = paragraf.Select(p => p.Teks).ToList();
            var buang = new HashSet<int>();

            // ---- 1. Judul surat (baris kapital pertama yang bukan baris kop).
            int idxJudul = CariJudul(teks);
            string judul = string.Empty;
            if (idxJudul >= 0)
            {
                judul = teks[idxJudul];

                // Judul yang terpecah dua baris (mis. \"SURAT\" lalu \"KETERANGAN\").
                if (!judul.Contains(' ') && idxJudul + 1 < teks.Count
                    && IsBarisJudul(teks[idxJudul + 1]) && !IsBarisKop(teks[idxJudul + 1])
                    && judul.Length + teks[idxJudul + 1].Length + 1 <= 90)
                {
                    judul = judul + " " + teks[idxJudul + 1];
                    buang.Add(idxJudul + 1);
                }
                buang.Add(idxJudul);
            }

            // ---- 2. Kop surat pada berkas: diganti kop desa aplikasi. Kop hanya dicari
            // di atas judul, supaya baris surat di bawah judul tidak ikut terbuang.
            int batasKop = idxJudul > 0 ? idxJudul : (idxJudul < 0 ? Math.Min(4, teks.Count) : 0);
            var barisKop = new List<int>();
            for (int i = 0; i < batasKop; i++)
            {
                if (IsBarisKop(teks[i])) barisKop.Add(i);
            }

            // Baris kop baru dianggap ada bila berkas memuat penanda kuat (pemerintah,
            // kabupaten, alamat, dan sejenisnya), bukan sekadar kata "desa" pada baris surat.
            bool pakaiKop = barisKop.Count >= 2 && barisKop.Any(i => PolaKopKuat.IsMatch(teks[i]));
            if (pakaiKop)
            {
                foreach (int i in barisKop) buang.Add(i);
            }

            // ---- 3. Baris nomor surat.
            int idxNomor = -1;
            string awalan = "470";
            for (int i = 0; i < teks.Count; i++)
            {
                if (buang.Contains(i)) continue;
                if (!CobaBarisNomor(teks[i], i + 1 < teks.Count ? teks[i + 1] : null,
                        out string awalanBaru, out bool ambilBarisBerikutnya)) continue;

                idxNomor = i;
                awalan = awalanBaru;
                buang.Add(i);
                if (ambilBarisBerikutnya && i + 1 < teks.Count) buang.Add(i + 1);
                break;
            }

            // ---- 4. Baris tempat & tanggal.
            int idxTempatTanggal = -1;
            for (int i = 0; i < teks.Count; i++)
            {
                if (buang.Contains(i)) continue;
                if (!PolaTempatTanggal.IsMatch(teks[i].Trim())) continue;

                idxTempatTanggal = i;
                buang.Add(i);
                break;
            }

            // ---- 5. Blok tanda tangan di bagian bawah surat: dikenali supaya aplikasi
            // mencetak tanda tangan pejabat desa sendiri (jabatan + nama pejabat).
            var tandaTangan = CariTandaTangan(teks, buang);
            foreach (int i in tandaTangan.Baris) buang.Add(i);

            // ---- 6. Badan surat: blok teks dan kelompok kolom isian berurutan.
            var dipakaiKunci = new List<string>();
            var kelompok = new List<(string Label, bool Wajib)>();
            int jumlahKolom = 0;
            int jumlahDataDiri = 0, jumlahKelompokKolom = 0;

            void SiramKelompok()
            {
                if (kelompok.Count == 0) return;

                int identitas = kelompok.Count(k => LabelDataDiri.Contains(TemplateSuratKunci.Slug(k.Label)));
                bool dataDiri = kelompok.Count >= 2 && identitas * 2 >= kelompok.Count;
                string kunciBagian = dataDiri
                    ? $"datadiri{jumlahDataDiri++}"
                    : $"kelompok{jumlahKelompokKolom++}";

                var bagian = new BagianTemplateSurat
                {
                    Tipe = dataDiri ? TipeBagianTemplate.DataDiri : TipeBagianTemplate.Kolom,
                    Kunci = kunciBagian
                };

                foreach (var (label, wajib) in kelompok)
                {
                    string kunci = dataDiri
                        ? $"{kunciBagian}_{TemplateSuratKunci.Slug(label)}"
                        : TemplateSuratKunci.Unik(label, dipakaiKunci);
                    if (dipakaiKunci.Contains(kunci, StringComparer.OrdinalIgnoreCase))
                    {
                        kunci = TemplateSuratKunci.Unik(kunci, dipakaiKunci);
                    }
                    dipakaiKunci.Add(kunci);

                    var kolom = new KolomTemplateSurat
                    {
                        Label = label,
                        Tipe = PetaTipeKolom(label),
                        Wajib = wajib,
                        Kunci = kunci
                    };

                    bagian.Kolom.Add(kolom);
                    template.Kolom.Add(kolom);
                }

                template.Bagian.Add(bagian);
                jumlahKolom += bagian.Kolom.Count;
                kelompok.Clear();
            }

            for (int i = 0; i < paragraf.Count; i++)
            {
                if (buang.Contains(i)) continue;

                var baris = paragraf[i];
                if (baris.Teks.Length == 0) continue;

                if (CobaKolom(baris.Teks, out string label, out bool wajib))
                {
                    kelompok.Add((label, wajib));
                    continue;
                }

                SiramKelompok();
                template.Bagian.Add(new BagianTemplateSurat
                {
                    Tipe = TipeBagianTemplate.Teks,
                    Isi = baris.Teks,
                    Rata = baris.Rata,
                    Tebal = baris.Tebal,
                    Miring = baris.Miring
                });
            }
            SiramKelompok();

            template.Judul = judul;
            template.PakaiKop = pakaiKop;
            template.PakaiNomor = idxNomor >= 0;
            template.AwalanNomor = awalan;
            template.PakaiTandaTangan = tandaTangan.Ketemu;
            template.JabatanPenandatangan = tandaTangan.Sekretaris ? "Sekretaris Desa" : string.Empty;

            // Blok tanda tangan aplikasi sudah mencetak "Desa, tanggal" sendiri, jadi baris
            // tempat & tanggal pada badan surat tidak dipakai lagi bila blok tanda tangan
            // berkas berhasil dikenali (kalau tidak, tanggal akan tercetak dua kali).
            template.PakaiTempatTanggal = idxTempatTanggal >= 0 && !tandaTangan.Ketemu;

            // Surat harus punya minimal satu komponen resmi: bila berkas sama sekali
            // tidak memuat kop/judul/nomor/tempat & tanggal, kop desa dipakai.
            if (!template.PakaiKop && !template.PakaiNomor && !template.PakaiTempatTanggal
                && string.IsNullOrWhiteSpace(template.Judul))
            {
                template.PakaiKop = true;
            }

            return (template, judul, jumlahKolom);
        }

        /// <summary>Bentuk judul: baris pendek, hampir seluruhnya huruf kapital, tanpa titik dua.</summary>
        private static bool IsBarisJudul(string? teks)
        {
            string baris = (teks ?? string.Empty).Trim();
            if (baris.Length < 3 || baris.Length > 90) return false;
            if (baris.Contains(':')) return false;

            int huruf = baris.Count(char.IsLetter);
            if (huruf < 3) return false;

            int kapital = baris.Count(c => char.IsLetter(c) && char.IsUpper(c));
            return kapital * 100 >= huruf * 85;
        }

        private static int CariJudul(IReadOnlyList<string> teks)
        {
            int batas = Math.Min(6, teks.Count);
            for (int i = 0; i < batas; i++)
            {
                if (IsBarisJudul(teks[i]) && !IsBarisKop(teks[i])) return i;
            }
            return -1;
        }

        private static bool IsBarisKop(string? teks) =>
            !string.IsNullOrWhiteSpace(teks) && PolaKop.IsMatch(teks);

        /// <summary>
        /// Kenali blok tanda tangan di bagian bawah surat — mis. "Kepala Desa" atau
        /// "a.n. Kepala Desa / Sekretaris Desa" beserta nama, titik-titik, dan NIP.
        /// Bila dikenali, baris-barisnya dibuang dari badan surat karena aplikasi mencetak
        /// blok tanda tangan sendiri dari jabatan dan nama pejabat desa pada setelan.
        /// </summary>
        private static (bool Ketemu, bool Sekretaris, List<int> Baris) CariTandaTangan(
            IReadOnlyList<string> teks, IReadOnlyCollection<int> sudahDibuang)
        {
            var tidakAda = (Ketemu: false, Sekretaris: false, Baris: new List<int>());

            // Blok tanda tangan selalu berada di bagian paling bawah surat, jadi hanya
            // beberapa baris terakhir yang diperiksa.
            var ekor = new List<int>();
            for (int i = teks.Count - 1; i >= 0 && ekor.Count < 8; i--)
            {
                if (sudahDibuang.Contains(i) || teks[i].Trim().Length == 0) continue;
                ekor.Add(i);
            }
            ekor.Reverse();

            // Cari dari bawah: bila berkas memuat "a.n. Kepala Desa" lalu "Sekretaris Desa",
            // baris jabatan yang paling bawah itulah penandatangannya.
            int idxJabatan = -1;
            bool sekretaris = false;
            for (int n = ekor.Count - 1; n >= 0; n--)
            {
                string? jabatan = BacaJabatanPenandatangan(teks[ekor[n]]);
                if (jabatan == null) continue;

                idxJabatan = ekor[n];
                sekretaris = jabatan == "sekretaris";
                break;
            }
            if (idxJabatan < 0) return tidakAda;

            var baris = new List<int> { idxJabatan };

            // Baris di atas jabatan yang masih bagian blok tanda tangan: tempat & tanggal,
            // titik-titik/"(ttd)", atau baris jabatan lain (mis. "a.n. Kepala Desa").
            int atas = idxJabatan;
            for (int langkah = 0; langkah < 3; langkah++)
            {
                int sebelum = BarisSebelumnya(teks, atas, sudahDibuang);
                if (sebelum < 0) break;

                string teksSebelum = teks[sebelum].Trim();
                if (!PolaTempatTanggal.IsMatch(teksSebelum)
                    && !IsBarisRuangTandaTangan(teksSebelum)
                    && BacaJabatanPenandatangan(teksSebelum) == null) break;

                baris.Add(sebelum);
                atas = sebelum;
            }

            // Nama penandatangan, baris NIP, dan sisa titik-titik di bawah jabatan. Baris
            // pertama yang bukan bagian tanda tangan (mis. "Tembusan :") menghentikan ini.
            int batas = Math.Min(teks.Count, idxJabatan + 5);
            for (int i = idxJabatan + 1; i < batas; i++)
            {
                if (sudahDibuang.Contains(i) || baris.Contains(i)) continue;

                string bawah = teks[i].Trim();
                if (bawah.Length == 0) continue;
                if (!IsBarisPenandatangan(bawah)) break;

                baris.Add(i);
            }

            return (true, sekretaris, baris);
        }

        /// <summary>Indeks baris berisi teks sebelum <paramref name="mulai"/>; -1 bila tidak ada.</summary>
        private static int BarisSebelumnya(
            IReadOnlyList<string> teks, int mulai, IReadOnlyCollection<int> sudahDibuang)
        {
            for (int i = mulai - 1; i >= 0; i--)
            {
                if (sudahDibuang.Contains(i)) continue;
                if (teks[i].Trim().Length == 0) continue;
                return i;
            }
            return -1;
        }

        /// <summary>
        /// Kenali baris jabatan penandatangan: "kepala desa" atau "sekretaris" (surat yang
        /// ditandatangani a.n. Kepala Desa disamakan dengan Sekretaris Desa, sesuai bentuk
        /// baku blok tanda tangan aplikasi). Null bila barisnya bukan jabatan.
        /// </summary>
        private static string? BacaJabatanPenandatangan(string? teks)
        {
            string padat = RapikanBaris(teks);
            if (padat.Length < 4 || padat.Length > 70) return null;
            if (padat.Any(char.IsDigit)) return null;

            // Kata sambung/verba surat tidak pernah muncul pada baris jabatan yang berdiri
            // sendiri, jadi baris kalimat seperti "KEPALA DESA MENERANGKAN BAHWA ..." ditolak.
            foreach (string kata in padat.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (KataBukanLabel.Contains(kata)) return null;
            }

            if (PolaJabatanSekdes.IsMatch(padat)) return "sekretaris";
            if (PolaJabatanKades.IsMatch(padat))
            {
                return PolaDelegasiJabatan.IsMatch(padat) ? "sekretaris" : "kepala desa";
            }
            return null;
        }

        /// <summary>Rapikan baris untuk pencocokan pola: kapital, spasi tunggal, tanpa tanda baca akhir.</summary>
        private static string RapikanBaris(string? teks)
        {
            string baris = Regex.Replace((teks ?? string.Empty).Trim(), @"\s+", " ").ToUpperInvariant();
            return baris.TrimEnd('.', ',', ':', ';').Trim();
        }

        /// <summary>Baris ruang tanda tangan: titik-titik/garis atau penanda "(ttd)".</summary>
        private static bool IsBarisRuangTandaTangan(string? teks)
        {
            string baris = (teks ?? string.Empty).Trim();
            if (baris.Length < 2 || baris.Length > 60) return false;

            string rapat = baris.Replace(" ", string.Empty);
            if (rapat.Length >= 3 && rapat.All(c => c is '.' or '_' or '-' or '–' or '—' or '…')) return true;

            string saja = baris.Trim('(', ')', '.', ':', ',', ' ').ToUpperInvariant();
            return saja is "TTD" or "TANDA TANGAN";
        }

        /// <summary>Nama penandatangan, baris NIP, atau "Nama : ..." di dalam blok tanda tangan.</summary>
        private static bool IsBarisPenandatangan(string teks)
        {
            string baris = teks.Trim();
            if (IsBarisRuangTandaTangan(baris)) return true;
            if (baris.Length < 3 || baris.Length > 60) return false;

            // "Nama : H. SURYANA" dan "NIP : 1968..." masih bagian blok tanda tangan.
            var kolom = PolaKolom.Match(baris);
            if (kolom.Success)
            {
                string label = TemplateSuratKunci.Slug(kolom.Groups["label"].Value);
                return label is "nama" or "nama_lengkap" or "nip" or "nip_nip";
            }

            if (baris.Contains(':') || baris.Contains(';')) return false;

            string besar = baris.ToUpperInvariant();
            if (KataBukanNamaPenandatangan.Any(k => besar.StartsWith(k, StringComparison.Ordinal))) return false;

            if (besar.StartsWith("NIP", StringComparison.Ordinal)) return true;
            if (baris.Any(char.IsDigit) || !baris.Any(char.IsLetter)) return false;

            int kata = baris.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            return kata <= 6;
        }

        /// <summary>
        /// Kenali baris nomor surat beserta awalannya. Nomor pada berkas dipakai sebagai
        /// awalan supaya penomoran otomatis aplikasi tetap sejenis dengan berkas aslinya.
        /// </summary>
        private static bool CobaBarisNomor(string? teks, string? barisBerikutnya,
            out string awalan, out bool ambilBarisBerikutnya)
        {
            awalan = "470";
            ambilBarisBerikutnya = false;

            string baris = (teks ?? string.Empty).Trim();
            if (baris.Length == 0 || baris.Length > 80) return false;

            var cocok = PolaNomor.Match(baris);
            if (!cocok.Success) return false;

            string nilai = cocok.Groups["nilai"].Value.Trim();
            if (nilai.Length == 0 && !string.IsNullOrWhiteSpace(barisBerikutnya)
                && PolaNilaiNomor.IsMatch(barisBerikutnya.Trim())
                && barisBerikutnya.Any(char.IsDigit))
            {
                nilai = barisBerikutnya.Trim();
                ambilBarisBerikutnya = true;
            }

            if (nilai.Length < 4 || !nilai.Any(char.IsDigit)) return false;
            if (!PolaNilaiNomor.IsMatch(nilai)) return false;
            if (nilai.Count(c => c == ' ') > 3) return false;

            awalan = AwalanDariNomor(nilai);
            return true;
        }

        /// <summary>Awalan (kode klasifikasi) dari contoh nomor pada berkas.</summary>
        private static string AwalanDariNomor(string nomor)
        {
            string depan = nomor.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            depan = depan.Trim().Trim('.', '-');

            if (depan.Length is >= 1 and <= 16
                && depan.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_'))
            {
                return depan;
            }
            return "470";
        }

        /// <summary>Kenali baris isian \"Label : ......\" sebagai kolom surat.</summary>
        public static bool CobaKolom(string? teks, out string label, out bool wajib)
        {
            label = string.Empty;
            wajib = false;

            string baris = (teks ?? string.Empty).Trim();
            if (baris.Length == 0 || baris.Length > 90) return false;

            var cocok = PolaKolom.Match(baris);
            if (!cocok.Success) return false;

            string kandidatLabel = cocok.Groups["label"].Value.Trim();
            string nilai = cocok.Groups["nilai"].Value.Trim();

            if (kandidatLabel.Length < 2 || kandidatLabel.Length > 45) return false;
            if (!PolaLabelSah.IsMatch(kandidatLabel)) return false;
            if (kandidatLabel.Contains(',')) return false;

            // Label tidak boleh berupa potongan kalimat (\"Dengan ini menerangkan bahwa :\").
            var kata = kandidatLabel
                .Split(new[] { ' ', '\t', '/', '&', '.', '(', ')', '\'' }, StringSplitOptions.RemoveEmptyEntries);
            if (kata.Length == 0 || kata.Length > 4) return false;
            if (kata.Any(k => KataBukanLabel.Contains(k))) return false;

            int titik = nilai.Count(c => c == '.' || c == '_' || c == '…' || c == '–' || c == '—');
            if (nilai.Length > 0 && titik < 2) return false;   // sudah ada isi asli → bukan kolom isian

            label = RapikanTeks(kandidatLabel);
            wajib = titik >= 3;
            return true;
        }

        private static TipeKolomTemplate PetaTipeKolom(string label)
        {
            string slug = TemplateSuratKunci.Slug(label);

            if (slug == "nik" || slug.StartsWith("nik") || slug == "no_ktp" || slug == "no_kk") return TipeKolomTemplate.Nik;
            if (slug.StartsWith("tanggal")) return TipeKolomTemplate.Tanggal;
            if (slug.StartsWith("alamat") || slug is "keperluan" or "keterangan" or "maksud" or "tujuan"
                or "uraian" or "catatan" or "keterangan_lain")
            {
                return TipeKolomTemplate.Paragraf;
            }
            if (slug is "jumlah" or "umur" or "usia" or "luas" or "nomor" or "no") return TipeKolomTemplate.Angka;
            return TipeKolomTemplate.Teks;
        }


    }
}
