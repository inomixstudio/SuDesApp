using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SuDesApp.Data.Models
{
    /// <summary>
    /// Satu contoh Template Surat siap pakai yang disediakan aplikasi.
    ///
    /// Contoh ini bisa dipakai langsung (dipasang ke daftar Template Surat pengguna)
    /// atau dijadikan dasar saat membuat template baru lewat wizard.
    /// </summary>
    public sealed class ContohTemplateSurat
    {
        public ContohTemplateSurat(string kode, string nama, string kategori, string keterangan, TemplateSuratKustom template)
        {
            Kode = kode ?? string.Empty;
            Nama = nama ?? string.Empty;
            Kategori = kategori ?? string.Empty;
            Keterangan = keterangan ?? string.Empty;
            Template = template ?? throw new ArgumentNullException(nameof(template));

            Template.Nama = Nama;
            Template.Deskripsi = Keterangan;
            Template.FormatContoh = true;
        }

        /// <summary>Kode tetap contoh, mis. "pengantar-rt-rw" (dipakai penyimpanan penanda).</summary>
        public string Kode { get; }

        public string Nama { get; }

        /// <summary>Golongan surat untuk pengelompokan, mis. "Pengantar", "Izin".</summary>
        public string Kategori { get; }

        /// <summary>Penjelasan singkat kapan surat ini dipakai.</summary>
        public string Keterangan { get; }

        /// <summary>Definisi surat lengkap (kop, judul, nomor, teks, kolom, kaki).</summary>
        public TemplateSuratKustom Template { get; }

        public string AwalanNomor => Template.AwalanNomor;

        /// <summary>Contoh nomor surat pertama yang akan tercetak dari template ini.</summary>
        public string ContohNomor => Template.NomorBerikutnyaTampil;

        public int JumlahKolom => Template.JumlahKolom;

        public IReadOnlyList<KolomTemplateSurat> Kolom => Template.Kolom;

        public string RingkasanSusunan => Template.RingkasanSusunan;

        public string LabelKop => Template.PakaiKop ? "Kop desa" : "Tanpa kop";

        public string LabelTataLetak => Template.PakaiGrid ? "Grid bergaris" : "Baris label : isi";

        public string LabelPenandaTangan => Template.PakaiTandaTangan ? Template.JabatanTampil : "Tanpa tanda tangan";

        /// <summary>Salinan definisi tanpa identitas database — siap disimpan sebagai template pengguna.</summary>
        public TemplateSuratKustom Salinan() => Template.Clone();
    }

    /// <summary>
    /// Satu berkas contoh Template Surat pada folder <c>Bawaan/TemplateSurat</c> —
    /// bentuk simpanan contoh yang ikut terkirim aplikasi. Isinya identik dengan
    /// <see cref="ContohTemplateSurat"/>: kode/kategori/keterangan di luar,
    /// definisi surat lengkap di dalam <see cref="Template"/>.
    /// </summary>
    public sealed class BerkasContohTemplate
    {
        public string Kode { get; set; } = string.Empty;

        public string Kategori { get; set; } = string.Empty;

        public string Keterangan { get; set; } = string.Empty;

        public TemplateSuratKustom? Template { get; set; }
    }

    /// <summary>
    /// Koleksi contoh Template Surat siap pakai.
    ///
    /// Sumber utama: berkas JSON di folder <c>Bawaan/TemplateSurat</c> milik
    /// aplikasi — dikirim installer dan dapat ditambah/diubah lewat tambalan tanpa
    /// mengganti DLL. Folder tidak ada atau tidak terbaca = memakai definisi
    /// tertanam dalam kode (<see cref="KatalogTertanam"/>). Apa pun sumbernya,
    /// contoh disemai sekali ke database dengan penanda, jadi hapusan pengguna
    /// menempel dan contoh baru dari pembaruan tetap disiapkan otomatis.
    /// </summary>
    public static class TemplateSuratBawaan
    {
        /// <summary>Nama folder contoh milik aplikasi di dalam folder aplikasi.</summary>
        public const string NamaFolderBawaan = "Bawaan";

        /// <summary>Subfolder contoh Template Surat di dalam folder Bawaan.</summary>
        public const string NamaSubFolderTemplate = "TemplateSurat";

        private static readonly object _kunci = new();
        private static string? _folderContoh;
        private static IReadOnlyList<ContohTemplateSurat>? _daftar;
        private static IReadOnlyList<ContohTemplateSurat>? _tertanam;
        private static IReadOnlyList<string> _galatPemuatan = Array.Empty<string>();

        private static readonly JsonSerializerOptions OpsiJson = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        /// <summary>
        /// Seluruh contoh yang tersedia — dibaca dari folder Bawaan bila ada
        /// (urutan nama berkas), selain itu dari definisi tertanam. Dimuat sekali
        /// pada akses pertama.
        /// </summary>
        public static IReadOnlyList<ContohTemplateSurat> Daftar
        {
            get
            {
                if (_daftar != null) return _daftar;
                lock (_kunci)
                {
                    if (_daftar == null) MuatSekali();
                    return _daftar!;
                }
            }
        }

        /// <summary>Berkas contoh pada folder Bawaan yang gagal dibaca (dilaporkan layanan penyemai).</summary>
        public static IReadOnlyList<string> GalatPemuatan => _galatPemuatan;

        /// <summary>
        /// Definisi contoh tertanam dalam kode — cadangan bila folder Bawaan tidak
        /// tersedia, sekaligus acuan pembuat berkas JSON folder Bawaan.
        /// </summary>
        public static IReadOnlyList<ContohTemplateSurat> KatalogTertanam
        {
            get
            {
                if (_tertanam == null) _tertanam = Bangun();
                return _tertanam;
            }
        }

        /// <summary>
        /// Folder contoh terpasang: <c>{folder aplikasi}\Bawaan\TemplateSurat</c>,
        /// dicari naik dari <c>BaseDirectory</c> (aplikasi terpasang maupun folder
        /// bin saat pengembangan) — pola yang sama dengan folder <c>docs</c>.
        /// Null bila tidak ditemukan. Dapat diarahkan ke folder lain untuk
        /// pengujian; atur sebelum <see cref="Daftar"/> pertama dibaca.
        /// </summary>
        public static string? FolderContoh
        {
            get
            {
                if (_folderContoh == null) _folderContoh = CariFolderContoh();
                return _folderContoh;
            }
            set => _folderContoh = value;
        }

        /// <summary>Seluruh kode contoh.</summary>
        public static IReadOnlyList<string> SemuaKode => Daftar.Select(c => c.Kode).ToList();

        /// <summary>Cari contoh menurut kodenya; null bila tidak ada.</summary>
        public static ContohTemplateSurat? Cari(string? kode)
        {
            if (string.IsNullOrWhiteSpace(kode)) return null;
            string dicari = kode.Trim();
            return Daftar.FirstOrDefault(c => string.Equals(c.Kode, dicari, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Kode contoh yang cocok dengan nama template pengguna. Dipakai supaya contoh
        /// yang sudah dipasang (atau sudah disalin pengguna) dapat dikenali kembali.
        /// </summary>
        public static string? KodeDariNama(string? nama)
        {
            if (string.IsNullOrWhiteSpace(nama)) return null;
            string dicari = nama.Trim();

            foreach (var contoh in Daftar)
            {
                if (string.Equals(contoh.Nama, dicari, StringComparison.OrdinalIgnoreCase))
                {
                    return contoh.Kode;
                }
            }

            return null;
        }

        /// <summary>Contoh yang belum ada pada daftar template pengguna (dicocokkan lewat nama).</summary>
        public static List<ContohTemplateSurat> BelumAda(IEnumerable<TemplateSuratKustom>? daftarPengguna)
        {
            var namaTerpakai = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (daftarPengguna != null)
            {
                foreach (var template in daftarPengguna)
                {
                    if (template == null) continue;
                    string nama = (template.NamaTampil ?? string.Empty).Trim();
                    if (nama.Length > 0) namaTerpakai.Add(nama);
                }
            }

            return Daftar.Where(c => !namaTerpakai.Contains(c.Nama)).ToList();
        }

        /// <summary>
        /// Baca satu berkas contoh dari folder Bawaan. Melempar
        /// <see cref="InvalidDataException"/> bila isinya tidak sah (kode atau
        /// definisi kosong). Satu-satunya jalur pembacaan — dipakai aplikasi saat
        /// memuat dan pemeriksa kesegaran saat build, sehingga keduanya tidak
        /// pernah menyimpang.
        /// </summary>
        public static ContohTemplateSurat BacaBerkas(string path)
        {
            var dto = JsonSerializer.Deserialize<BerkasContohTemplate>(
                File.ReadAllText(path), OpsiJson);
            if (dto == null) throw new InvalidDataException("isi berkas kosong");
            if (string.IsNullOrWhiteSpace(dto.Kode)) throw new InvalidDataException("kode contoh kosong");
            if (dto.Template == null) throw new InvalidDataException("definisi template kosong");
            if (string.IsNullOrWhiteSpace(dto.Template.Nama)) throw new InvalidDataException("nama template kosong");

            return new ContohTemplateSurat(
                dto.Kode.Trim(),
                dto.Template.Nama,
                dto.Kategori ?? string.Empty,
                dto.Keterangan ?? string.Empty,
                dto.Template);
        }

        /// <summary>
        /// Susunan JSON satu contoh dalam format berkas folder Bawaan. Deterministik,
        /// sehingga dapat dibandingkan byte demi byte oleh pemeriksa kesegaran.
        /// </summary>
        public static string Serialisasi(ContohTemplateSurat contoh)
        {
            if (contoh == null) throw new ArgumentNullException(nameof(contoh));

            return JsonSerializer.Serialize(new BerkasContohTemplate
            {
                Kode = contoh.Kode,
                Kategori = contoh.Kategori,
                Keterangan = contoh.Keterangan,
                Template = contoh.Template
            }, OpsiJson);
        }

        /// <summary>Muat seluruh berkas contoh sekali; berkas rusak dicatat sebagai galat, bukan kegagalan.</summary>
        private static void MuatSekali()
        {
            var hasil = new List<ContohTemplateSurat>();
            var galat = new List<string>();

            try
            {
                var folder = FolderContoh;
                if (folder != null && Directory.Exists(folder))
                {
                    foreach (var berkas in Directory.GetFiles(folder, "*.json")
                        .OrderBy(b => b, StringComparer.OrdinalIgnoreCase))
                    {
                        try
                        {
                            hasil.Add(BacaBerkas(berkas));
                        }
                        catch (Exception ex)
                        {
                            galat.Add($"{Path.GetFileName(berkas)} — {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                galat.Add($"folder {NamaFolderBawaan}/{NamaSubFolderTemplate} — {ex.Message}");
            }

            // Folder kosong/tidak ada = definisi tertanam yang dipakai.
            if (hasil.Count == 0) hasil.AddRange(Bangun());

            _galatPemuatan = galat;
            _daftar = hasil;
        }

        private static string? CariFolderContoh()
        {
            string? folder = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(folder))
            {
                var kandidat = Path.Combine(folder, NamaFolderBawaan, NamaSubFolderTemplate);
                if (Directory.Exists(kandidat)) return kandidat;
                folder = Path.GetDirectoryName(folder.TrimEnd(Path.DirectorySeparatorChar));
            }

            return null;
        }

        private static IReadOnlyList<ContohTemplateSurat> Bangun() => new List<ContohTemplateSurat>
        {
            PengantarRtRw(),
            KeteranganDomisiliUsaha(),
            IzinKeramaian(),
            KeteranganPenghasilan(),
            KeteranganBelumMenikah(),
            UndanganRapat(),
            SuratKuasa(),
            SuratTugas(),
            PengumumanWarga()
        };

        // =====================================================================
        // Definisi tiap contoh
        // =====================================================================

        /// <summary>Field data diri ringkas supaya penulisan contoh tetap terbaca.</summary>
        private static TemplateSuratPreset.FieldDataDiri F(
            string label,
            TipeKolomTemplate tipe = TipeKolomTemplate.Teks,
            bool wajib = false,
            string[]? pilihan = null) => new(label, tipe, wajib, pilihan);

        private static ContohTemplateSurat PengantarRtRw() => new(
            "pengantar-rt-rw",
            "Surat Pengantar RT/RW",
            "Pengantar",
            "Pengantar resmi desa bagi warga yang mengurus dokumen atau surat menyurat di instansi lain.",
            new PenyusunContoh("Surat Pengantar RT/RW", "SURAT PENGANTAR", "475")
                .SusunanBagian()
                .Teks("Yang bertanda tangan di bawah ini menerangkan dengan sesungguhnya bahwa:", RataBlokTemplate.Kiri)
                .BagianDataDiri("Pemohon",
                    F("Nama", wajib: true),
                    F("NIK", TipeKolomTemplate.Nik, true),
                    F("Tempat Lahir"),
                    F("Tanggal Lahir", TipeKolomTemplate.Tanggal),
                    F("Jenis Kelamin", TipeKolomTemplate.Pilihan, pilihan: new[] { "Laki-laki", "Perempuan" }),
                    F("Pekerjaan"),
                    F("Alamat", TipeKolomTemplate.Paragraf, true))
                .Teks("Adalah benar warga Desa tersebut di atas, berkelakuan baik, dan tidak pernah " +
                      "terlibat dalam tindak pidana maupun pelanggaran ketertiban umum di wilayah kami.")
                .KelompokKolom("Keperluan")
                .Kolom("Keperluan", TipeKolomTemplate.Paragraf, wajib: true,
                       bawaan: "Melengkapi persyaratan pengurusan dokumen di instansi terkait")
                .Teks("Surat pengantar ini juga disampaikan kepada Ketua RT setempat dan disimpan sebagai arsip desa.")
                .Teks("Demikian surat pengantar ini dibuat dengan sebenarnya untuk dipergunakan sebagaimana mestinya.")
                .Jadi());

        private static ContohTemplateSurat KeteranganDomisiliUsaha() => new(
            "keterangan-domisili-usaha",
            "Surat Keterangan Domisili Usaha",
            "Keterangan",
            "Contoh keterangan lokasi usaha. Persyaratan, dasar pemeriksaan, dan masa berlaku perlu disesuaikan dengan aturan desa/kabupaten serta instansi tujuan.",
            new PenyusunContoh("Surat Keterangan Domisili Usaha", "SURAT KETERANGAN DOMISILI USAHA", "471")
                .SusunanBagian()
                .Teks("Yang bertanda tangan di bawah ini menerangkan dengan sesungguhnya bahwa:", RataBlokTemplate.Kiri)
                .BagianDataDiri("Pemohon",
                    F("Nama", wajib: true),
                    F("NIK", TipeKolomTemplate.Nik, true),
                    F("Pekerjaan"),
                    F("Alamat", TipeKolomTemplate.Paragraf, true))
                .Teks("Adalah benar memiliki usaha dan berdomisili di wilayah Desa tersebut di atas, dengan keterangan " +
                      "sebagai berikut:")
                .KelompokKolom("Data Usaha")
                .Kolom("Nama Usaha", TipeKolomTemplate.Teks, wajib: true)
                .Kolom("Bentuk Usaha", TipeKolomTemplate.Pilihan, pilihan: "Perorangan|Badan usaha", wajib: true)
                .Kolom("Jenis Usaha", TipeKolomTemplate.Teks, wajib: true, bawaan: "Warung kelontong")
                .Kolom("Alamat Usaha", TipeKolomTemplate.Paragraf, wajib: true)
                .Teks("Keterangan mengenai kondisi usaha diisi berdasarkan pemeriksaan yang benar-benar dilakukan dan dicatat oleh petugas.")
                .Teks("Masa berlaku dan penggunaan surat, bila dipersyaratkan, mengikuti ketentuan instansi tujuan dan aturan lokal yang berlaku.")
                .Teks("Demikian surat keterangan ini dibuat dengan sebenarnya untuk dipergunakan sebagaimana mestinya.")
                .Jadi());

        private static ContohTemplateSurat IzinKeramaian() => new(
            "izin-keramaian",
            "Surat Pengantar Permohonan Keramaian",
            "Pengantar",
            "Contoh surat pengantar/pendataan kegiatan. Surat ini bukan izin final; kewenangan dan persyaratan kegiatan harus dikonfirmasi ke instansi berwenang setempat.",
            new PenyusunContoh("Surat Pengantar Permohonan Keramaian", "SURAT PENGANTAR PERMOHONAN KEGIATAN/KERAMAIAN", "476")
                .SusunanBagian()
                .Teks("Pemerintah Desa menerima permohonan pencatatan/pengantar kegiatan dari warga dengan data sebagai berikut:", RataBlokTemplate.Kiri)
                .BagianDataDiri("Pemohon",
                    F("Nama", wajib: true),
                    F("NIK", TipeKolomTemplate.Nik, true),
                    F("Tempat Lahir"),
                    F("Tanggal Lahir", TipeKolomTemplate.Tanggal),
                    F("Pekerjaan"),
                    F("Agama", TipeKolomTemplate.Pilihan,
                        pilihan: new[] { "Islam", "Kristen", "Katolik", "Hindu", "Buddha", "Konghucu" }),
                    F("Alamat", TipeKolomTemplate.Paragraf, true))
                .Teks("Maksud mengadakan keramaian:", RataBlokTemplate.Kiri)
                .KelompokKolom("Rincian Keramaian")
                .Kolom("Hari/Tanggal Acara", TipeKolomTemplate.Tanggal, wajib: true)
                .Kolom("Acara/Hiburan", TipeKolomTemplate.Pilihan, wajib: true,
                       pilihan: "Organ Tunggal|Wayang Kulit|Dangdut|Musik Keyboard|Panggung Hiburan|Lainnya",
                       bawaan: "Organ Tunggal")
                .Kolom("Perayaan/Acara", TipeKolomTemplate.Pilihan, wajib: true,
                       pilihan: "Resepsi Pernikahan|Khitanan/Sunatan|Tasyakuran|Ulang Tahun|Peringatan Hari Besar|Lainnya",
                       bawaan: "Resepsi Pernikahan")
                .Kolom("Tempat Acara", TipeKolomTemplate.Teks, wajib: true,
                       bawaan: "Kediaman pemohon, alamat tersebut di atas")
                .Kolom("Jumlah Undangan", TipeKolomTemplate.Angka, bawaan: "150")
                .Kolom("Penanggung Jawab", TipeKolomTemplate.Teks, wajib: true,
                       bawaan: "Pemohon (nama tersebut di atas)")
                .Teks("Catatan pelaksanaan berikut merupakan pengingat umum dan perlu disesuaikan dengan ketentuan wilayah serta arahan instansi berwenang:", RataBlokTemplate.Kiri)
                .Teks("1. Pada waktu dilaksanakan rame-rame, ketentraman dan ketertiban lingkungan wajib dijaga, baik " +
                      "terhadap tetangga, menghargai waktu ibadah untuk menciptakan kerukunan umat beragama, maupun " +
                      "kebersihan lingkungan setelah rame-rame selesai.")
                .Teks("2. Pada waktu dilaksanakan rame-rame, tidak dibenarkan melakukan hal-hal yang bertentangan dengan " +
                      "ketentuan yang berlaku dan adat istiadat setempat.")
                .Teks("3. Pemohon mengurus izin/persetujuan lain yang diwajibkan sebelum kegiatan dilaksanakan.")
                .KelompokKolom("Catatan Instansi Terkait")
                .Kolom("Nama Instansi / Pejabat")
                .Kolom("Nomor surat atau persetujuan")
                .Kolom("Catatan", TipeKolomTemplate.Paragraf)
                .Teks("Surat ini hanya berfungsi sebagai pengantar/pendataan dari desa dan tidak menggantikan izin atau persetujuan instansi yang berwenang.")
                .Teks("Demikian surat pengantar permohonan kegiatan ini dibuat untuk " +
                      "dipergunakan sebagaimana mestinya.")
                .Jadi());

        private static ContohTemplateSurat KeteranganPenghasilan() => new(
            "keterangan-penghasilan",
            "Surat Keterangan Penghasilan",
            "Keterangan",
            "Contoh pencatatan keterangan penghasilan. Nilai dan kalimat kelayakan harus didasarkan pada data/pemeriksaan dan persyaratan program setempat.",
            new PenyusunContoh("Surat Keterangan Penghasilan", "SURAT KETERANGAN PENGHASILAN", "472")
                .SusunanBagian()
                .Teks("Yang bertanda tangan di bawah ini menerangkan dengan sesungguhnya bahwa:", RataBlokTemplate.Kiri)
                .BagianDataDiri("Pemohon",
                    F("Nama", wajib: true),
                    F("NIK", TipeKolomTemplate.Nik, true),
                    F("Alamat", TipeKolomTemplate.Paragraf, true))
                .KelompokKolom("Data Penghasilan")
                .Kolom("Pekerjaan/Usaha", TipeKolomTemplate.Teks, wajib: true,
                       bawaan: "")
                .Kolom("Penghasilan per Bulan", TipeKolomTemplate.Angka, wajib: true)
                .Kolom("Jumlah Tanggungan", TipeKolomTemplate.Angka)
                .Teks("Keterangan penghasilan di atas dicatat berdasarkan keterangan pemohon dan/atau pemeriksaan yang benar-benar dilakukan.")
                .Teks("Surat ini menerangkan data penghasilan; surat ini bukan penetapan kelayakan bantuan atau program tertentu.")
                .Teks("Demikian surat keterangan ini dibuat dengan sebenarnya untuk keperluan pengajuan " +
                      "beasiswa/bantuan sosial dan dipergunakan sebagaimana mestinya.")
                .Jadi());

        private static ContohTemplateSurat KeteranganBelumMenikah() => new(
            "keterangan-belum-menikah",
            "Surat Keterangan Belum Menikah",
            "Keterangan",
            "Keterangan status belum menikah warga, umumnya untuk syarat administrasi perkawinan.",
            new PenyusunContoh("Surat Keterangan Belum Menikah", "SURAT KETERANGAN BELUM MENIKAH", "473")
                .SusunanBagian()
                .Teks("Yang bertanda tangan di bawah ini menerangkan dengan sesungguhnya bahwa:", RataBlokTemplate.Kiri)
                .BagianDataDiri("Pemohon",
                    F("Nama", wajib: true),
                    F("NIK", TipeKolomTemplate.Nik, true),
                    F("Tempat Lahir"),
                    F("Tanggal Lahir", TipeKolomTemplate.Tanggal),
                    F("Pekerjaan"),
                    F("Alamat", TipeKolomTemplate.Paragraf, true))
                .Teks("Sampai dengan diterbitkannya surat keterangan ini, yang bersangkutan berstatus BELUM PERNAH KAWIN, " +
                      "tidak pernah menikah secara resmi maupun tidak resmi, dan tercatat sebagai warga yang berdomisili " +
                      "di wilayah kami.")
                .KelompokKolom("Keperluan")
                .Kolom("Keperluan", TipeKolomTemplate.Paragraf, wajib: true,
                       bawaan: "Melengkapi persyaratan administrasi perkawinan di KUA")
                .Teks("Apabila di kemudian hari terdapat ketidaksesuaian data, yang bersangkutan bersedia bertanggung " +
                      "jawab menurut peraturan perundang-undangan yang berlaku.")
                .Teks("Demikian surat keterangan ini dibuat dengan sebenarnya agar dapat dipergunakan sebagaimana mestinya.")
                .Jadi());

        private static ContohTemplateSurat UndanganRapat() => new(
            "undangan-rapat",
            "Surat Undangan Rapat",
            "Undangan",
            "Undangan kegiatan atau rapat desa untuk warga, perangkat, maupun lembaga desa.",
            new PenyusunContoh("Surat Undangan Rapat", "SURAT UNDANGAN", "503")
                .SusunanBagian()
                .Teks("Dengan hormat,", RataBlokTemplate.Kiri)
                .Teks("Sehubungan dengan akan dilaksanakannya kegiatan desa, dengan ini kami mengharap kehadiran " +
                      "Bapak/Ibu/Saudara pada:")
                .KelompokKolom("Rincian Kegiatan")
                .Kolom("Acara", TipeKolomTemplate.Teks, wajib: true,
                       bawaan: "Rapat koordinasi dengan perangkat desa")
                .Kolom("Hari/Tanggal", TipeKolomTemplate.Tanggal, wajib: true)
                .Kolom("Waktu", TipeKolomTemplate.Teks, wajib: true, bawaan: "09.00 WIB s.d. selesai")
                .Kolom("Tempat", TipeKolomTemplate.Teks, wajib: true, bawaan: "Balai Desa")
                .Kolom("Agenda", TipeKolomTemplate.Paragraf, wajib: true,
                       bawaan: "1. Penyampaian program kerja; 2. Evaluasi kegiatan berjalan; 3. Persiapan kegiatan mendatang.")
                .Kolom("Pakaian", TipeKolomTemplate.Pilihan, pilihan: "Kemeja/ Batik|Bebas rapi|Seragam lapangan", bawaan: "Kemeja/ Batik")
                .Teks("Mengingat pentingnya acara dimaksud, kami mengharapkan kehadiran Bapak/Ibu/Saudara tepat waktu " +
                      "dan tanpa diwakilkan.")
                .Teks("Demikian undangan ini disampaikan untuk dilaksanakan sebagaimana mestinya. Atas perhatian dan " +
                      "kehadirannya kami ucapkan terima kasih.")
                .Jadi());

        private static ContohTemplateSurat SuratKuasa()
        {
            // Surat kuasa lazimnya tidak memakai kop desa dan ditandatangani oleh
            // pemberi kuasa di atas materai (bukan oleh Kepala Desa).
            var template = new PenyusunContoh("Surat Kuasa", "SURAT KUASA", "591")
                .SusunanBagian()
                .Teks("Yang bertanda tangan di bawah ini:", RataBlokTemplate.Kiri)
                .BagianDataDiri("Pemberi Kuasa",
                    F("Nama", wajib: true),
                    F("NIK", TipeKolomTemplate.Nik, true),
                    F("Tempat Lahir"),
                    F("Tanggal Lahir", TipeKolomTemplate.Tanggal),
                    F("Jenis Kelamin", TipeKolomTemplate.Pilihan,
                        pilihan: new[] { "Laki-laki", "Perempuan" }),
                    F("Pekerjaan"),
                    F("Jabatan", wajib: true),
                    F("Alamat", TipeKolomTemplate.Paragraf, true))
                .Teks("Dalam hal ini bertindak untuk dan atas nama diri sendiri, selanjutnya disebut sebagai " +
                      "PEMBERI KUASA, dengan ini memberikan kuasa kepada:", RataBlokTemplate.Kiri)
                .BagianDataDiri("Penerima Kuasa",
                    F("Nama", wajib: true),
                    F("NIK", TipeKolomTemplate.Nik, true),
                    F("Pekerjaan"),
                    F("Alamat", TipeKolomTemplate.Paragraf, true))
                .Teks("Untuk selanjutnya disebut sebagai PENERIMA KUASA, khusus untuk:", RataBlokTemplate.Kiri)
                .KelompokKolom("Isi Kuasa")
                .Kolom("Hal yang Dikuasakan", TipeKolomTemplate.Paragraf, wajib: true,
                       bawaan: "Mewakili pemberi kuasa mengurus penerimaan dan/atau penandatanganan dokumen di instansi terkait")
                .Kolom("Masa Berlaku", TipeKolomTemplate.Teks,
                       bawaan: "6 (enam) bulan sejak surat kuasa ini diterbitkan")
                .Teks("Penerima kuasa tidak berhak melimpahkan (menguasakan kembali) kuasa ini kepada pihak lain, " +
                      "kecuali dengan persetujuan tertulis lebih dahulu dari pemberi kuasa.")
                .Teks("Pemberi kuasa dapat mencabut surat kuasa ini sewaktu-waktu selama kepentingan yang " +
                      "dikuasakan belum selesai dilaksanakan.")
                .Teks("Demikian surat kuasa ini dibuat dengan sebenarnya di atas materai secukupnya sesuai " +
                      "ketentuan peraturan perundang-undangan yang berlaku, untuk dipergunakan sebagaimana mestinya.")
                .Jadi();

            template.PakaiKop = false;
            template.PenandatanganDariDataDiri = true;
            template.KunciPenandatanganDataDiri = "datadiri0";
            template.JabatanPenandatangan = "Pemberi Kuasa";

            return new ContohTemplateSurat(
                "surat-kuasa",
                "Surat Kuasa",
                "Kuasa",
                "Surat pemberian kuasa dari warga (pemberi kuasa) kepada orang lain (penerima kuasa) " +
                "untuk mengurus atau mewakili suatu kepentingan tertentu.",
                template);
        }

        private static ContohTemplateSurat SuratTugas() => new(
            "surat-tugas",
            "Surat Tugas",
            "Tugas",
            "Penugasan resmi kepada perangkat desa atau warga untuk melaksanakan suatu tugas, " +
            "menghadiri kegiatan, atau mewakili desa pada acara tertentu.",
            new PenyusunContoh("Surat Tugas", "SURAT TUGAS", "592")
                .SusunanBagian()
                .Teks("Yang bertanda tangan di bawah ini menerangkan bahwa berdasarkan keperluan dinas/kegiatan, " +
                      "dengan ini menugaskan:", RataBlokTemplate.Kiri)
                .BagianDataDiri("Yang Ditugaskan",
                    F("Nama", wajib: true),
                    F("NIK", TipeKolomTemplate.Nik, true),
                    F("Tempat Lahir"),
                    F("Tanggal Lahir", TipeKolomTemplate.Tanggal),
                    F("Pekerjaan"),
                    F("Alamat", TipeKolomTemplate.Paragraf, true))
                .Teks("Untuk melaksanakan tugas sebagai berikut:", RataBlokTemplate.Kiri)
                .KelompokKolom("Tugas yang Dilaksanakan")
                .Kolom("Uraian Tugas", TipeKolomTemplate.Paragraf, wajib: true,
                       bawaan: "Menghadiri rapat koordinasi perangkat desa se-kecamatan")
                .Kolom("Hari/Tanggal Pelaksanaan", TipeKolomTemplate.Tanggal, wajib: true)
                .Kolom("Waktu", TipeKolomTemplate.Teks, bawaan: "08.00 WIB s.d. selesai")
                .Kolom("Tempat", TipeKolomTemplate.Teks, wajib: true, bawaan: "Kantor Kecamatan")
                .Teks("Selesai melaksanakan tugas tersebut, yang bersangkutan wajib menyampaikan hasil dan laporan " +
                      "kegiatannya kepada pejabat yang menugaskan.")
                .Teks("Demikian surat tugas ini diberikan untuk dilaksanakan dengan sebaik-baiknya dan penuh " +
                      "tanggung jawab.")
                .Jadi());

        private static ContohTemplateSurat PengumumanWarga() => new(
            "pengumuman-warga",
            "Surat Pengumuman Warga",
            "Pengumuman",
            "Pengumuman resmi desa kepada warga, mis. jadwal kegiatan, kerja bakti, atau pemberitahuan penting.",
            new PenyusunContoh("Surat Pengumuman Warga", "PENGUMUMAN", "504")
                .SusunanBagian()
                .Teks("Dengan ini disampaikan kepada seluruh warga Desa bahwa:", RataBlokTemplate.Kiri)
                .KelompokKolom("Isi Pengumuman")
                .Kolom("Perihal", TipeKolomTemplate.Teks, wajib: true,
                       bawaan: "Kerja bakti pembersihan lingkungan")
                .Kolom("Hari/Tanggal Pelaksanaan", TipeKolomTemplate.Tanggal, wajib: true)
                .Kolom("Waktu Pelaksanaan", TipeKolomTemplate.Teks, bawaan: "07.00 WIB s.d. selesai")
                .Kolom("Tempat", TipeKolomTemplate.Teks, bawaan: "Seluruh jalan lingkungan desa")
                .Kolom("Isi Pengumuman", TipeKolomTemplate.Paragraf, wajib: true,
                       bawaan: "Seluruh warga dimohon hadir membawa peralatan kebersihan masing-masing dan mengikuti arahan " +
                               "koordinator lingkungan. Kegiatan berlangsung meskipun hujan ringan.")
                .Teks("Demikian pengumuman ini disampaikan untuk diketahui dan dilaksanakan dengan penuh tanggung jawab. " +
                      "Atas perhatian dan kerja samanya, kami ucapkan terima kasih.")
                .Jadi());

        // =====================================================================
        // Penyusun definisi (mempermudah penulisan contoh di atas)
        // =====================================================================

        /// <summary>Penyusun satu definisi contoh supaya penulisannya ringkas dan seragam.</summary>
        private sealed class PenyusunContoh
        {
            private readonly TemplateSuratKustom _template;
            private readonly List<string> _kunci = new();

            /// <summary>Susunan bagian dipakai (bukan lagi daftar blok datar).</summary>
            private bool _pakaiBagian;

            /// <summary>Kelompok kolom yang sedang dibuka; kolom berikutnya masuk ke sini.</summary>
            private BagianTemplateSurat? _kelompokAktif;

            public PenyusunContoh(string nama, string judul, string awalan, bool grid = false)
            {
                _template = new TemplateSuratKustom
                {
                    Nama = nama,
                    Judul = judul,
                    AwalanNomor = awalan,
                    PolaNomor = TemplateSuratNomor.PolaBawaan,
                    PakaiKop = true,
                    PakaiNomor = true,
                    PakaiGrid = grid,
                    PakaiTandaTangan = true
                };
            }

            /// <summary>Baris tambahan di bawah judul.</summary>
            public PenyusunContoh Sub(string teks)
            {
                _template.SubJudul = teks;
                return this;
            }

            /// <summary>Cetak baris "Desa, 12 Januari 2026" di atas badan surat.</summary>
            public PenyusunContoh Tanggal(bool pakai = true)
            {
                _template.PakaiTempatTanggal = pakai;
                return this;
            }

            /// <summary>Jabatan penandatangan (kosong = Kepala Desa).</summary>
            public PenyusunContoh Jabatan(string jabatan)
            {
                _template.JabatanPenandatangan = jabatan;
                return this;
            }

            /// <summary>Teks penutup di bawah tanda tangan.</summary>
            public PenyusunContoh Kaki(string teks)
            {
                _template.PakaiTeksKaki = true;
                _template.TeksKaki = teks;
                return this;
            }

            /// <summary>Satu blok teks bebas.</summary>
            public PenyusunContoh Teks(
                string isi,
                RataBlokTemplate rata = RataBlokTemplate.Justify,
                bool tebal = false,
                bool miring = false)
            {
                _template.Blok.Add(new BlokTeksTemplateSurat
                {
                    Isi = isi,
                    Rata = rata,
                    Tebal = tebal,
                    Miring = miring
                });

                // Pada susunan bagian, teks ikut dicatat sebagai bagian supaya urutannya
                // tetap tepat di antara data diri dan kelompok kolom.
                if (_pakaiBagian)
                {
                    _template.Bagian.Add(new BagianTemplateSurat
                    {
                        Tipe = TipeBagianTemplate.Teks,
                        Isi = isi,
                        Rata = rata,
                        Tebal = tebal,
                        Miring = miring
                    });
                }

                _kelompokAktif = null;
                return this;
            }

            /// <summary>
            /// Beralih dari daftar blok datar ke susunan bagian (urutan Teks / Data Diri /
            /// Kolom). Blok teks yang sudah ditulis dipindahkan ke bagian lebih dulu,
            /// sehingga tidak ada kalimat yang hilang dari surat.
            /// </summary>
            public PenyusunContoh SusunanBagian()
            {
                if (_pakaiBagian) return this;
                _pakaiBagian = true;

                foreach (var blok in _template.Blok)
                {
                    _template.Bagian.Add(new BagianTemplateSurat
                    {
                        Tipe = TipeBagianTemplate.Teks,
                        Isi = blok.Isi,
                        Rata = blok.Rata,
                        Tebal = blok.Tebal,
                        Miring = blok.Miring
                    });
                }

                return this;
            }

            /// <summary>
            /// Blok data diri satu orang (NIK, nama, TTL, alamat, …) dengan judul kelompok
            /// seperti "Pemohon" atau "Penanggung Jawab".
            /// </summary>
            public PenyusunContoh BagianDataDiri(string judul, params TemplateSuratPreset.FieldDataDiri[] field)
            {
                SusunanBagian();

                int urutan = _template.Bagian.Count(b => b.Tipe == TipeBagianTemplate.DataDiri);
                string kunci = $"datadiri{urutan}";

                var bagian = new BagianTemplateSurat
                {
                    Tipe = TipeBagianTemplate.DataDiri,
                    JudulKelompok = judul,
                    Kunci = kunci
                };

                foreach (var f in field)
                {
                    var kolom = new KolomTemplateSurat
                    {
                        Label = f.Label,
                        Tipe = f.Tipe,
                        Wajib = f.Wajib,
                        Pilihan = f.Pilihan == null ? new List<string>() : new List<string>(f.Pilihan),
                        Kunci = $"{kunci}_{TemplateSuratKunci.Slug(f.Label)}",
                        Kelompok = judul
                    };

                    bagian.Kolom.Add(kolom);
                    _template.Kolom.Add(kolom);
                    _kunci.Add(kolom.Kunci);
                }

                _template.Bagian.Add(bagian);
                _kelompokAktif = null;
                return this;
            }

            /// <summary>
            /// Buka kelompok kolom isian berikutnya (mis. "Rincian Keramaian"). Semua
            /// <see cref="Kolom"/> sesudahnya masuk ke kelompok ini sampai kelompok baru
            /// dibuka.
            /// </summary>
            public PenyusunContoh KelompokKolom(string judul, bool grid = false)
            {
                SusunanBagian();

                int urutan = _template.Bagian.Count(b => b.Tipe == TipeBagianTemplate.Kolom);
                _kelompokAktif = new BagianTemplateSurat
                {
                    Tipe = TipeBagianTemplate.Kolom,
                    JudulKelompok = judul,
                    Grid = grid,
                    Kunci = $"kelompok{urutan}"
                };
                _template.Bagian.Add(_kelompokAktif);
                return this;
            }

            /// <summary>Satu kolom isian; kuncinya dibuat otomatis dan dijamin unik.</summary>
            public PenyusunContoh Kolom(
                string label,
                TipeKolomTemplate tipe = TipeKolomTemplate.Teks,
                bool wajib = false,
                string? pilihan = null,
                string? bawaan = null)
            {
                var kolom = new KolomTemplateSurat
                {
                    Label = label,
                    Tipe = tipe,
                    Wajib = wajib,
                    NilaiBawaan = bawaan ?? string.Empty
                };

                if (!string.IsNullOrWhiteSpace(pilihan))
                {
                    kolom.Pilihan = pilihan!
                        .Split('|', StringSplitOptions.RemoveEmptyEntries)
                        .Select(p => p.Trim())
                        .Where(p => p.Length > 0)
                        .ToList();
                }

                kolom.Kunci = TemplateSuratKunci.Unik(label, _kunci);
                _kunci.Add(kolom.Kunci);
                _template.Kolom.Add(kolom);

                // Bila sedang ada kelompok terbuka (susunan bagian), kolom ini ikut
                // tercetak di dalam kelompok tersebut.
                if (_pakaiBagian && _kelompokAktif != null)
                {
                    kolom.Kelompok = _kelompokAktif.JudulKelompok;
                    _kelompokAktif.Kolom.Add(kolom);
                }

                return this;
            }

            /// <summary>Kolom identitas warga yang paling sering dipakai surat desa.</summary>
            public PenyusunContoh IdentitasWarga() => Kolom("Nama", wajib: true)
                .Kolom("NIK", TipeKolomTemplate.Nik, wajib: true)
                .Kolom("Tempat Lahir")
                .Kolom("Tanggal Lahir", TipeKolomTemplate.Tanggal)
                .Kolom("Jenis Kelamin", TipeKolomTemplate.Pilihan, pilihan: "Laki-laki|Perempuan")
                .Kolom("Pekerjaan")
                .Kolom("Alamat", TipeKolomTemplate.Paragraf, wajib: true);

            public TemplateSuratKustom Jadi() => _template;
        }
    }
}
