using System;
using System.Collections.Generic;
using System.Linq;

namespace SuDesApp.Wilayah
{
    /// <summary>
    /// Logika pilihan wilayah berjenjang: provinsi → kabupaten/kota → kecamatan →
    /// desa/kelurahan, dengan kode pos saran mengikuti desa yang dipilih.
    ///
    /// Kelas ini sengaja tidak menyentuh UI supaya aturan yang mudah salah —
    /// pilihan yang lebih dalam harus dikosongkan ketika pilihan di atasnya berubah,
    /// dan memilih tingkat bawah harus sekalian mengisi tingkat atasnya — bisa diuji
    /// langsung tanpa membuka jendela aplikasi.
    /// </summary>
    public sealed class WilayahPemilih
    {
        private static readonly IReadOnlyList<WilayahDaerah> Kosong = Array.Empty<WilayahDaerah>();

        private readonly WilayahDataStore _data;
        private readonly string _kodeProvinsiDiutamakan;
        private IReadOnlyList<WilayahDaerah>? _provinsi;

        public WilayahPemilih(WilayahDataStore data, string kodeProvinsiDiutamakan = WilayahDataStore.KodeJawaBarat)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            _kodeProvinsiDiutamakan = kodeProvinsiDiutamakan;
        }

        /// <summary>
        /// Provinsi pilihan, Jawa Barat lebih dulu (provinsi lain tetap ada di bawahnya).
        /// Daftar ini baru disusun saat dipakai: pemuatan data wilayah (± 90 ribu
        /// baris) terjadi sekali, dan halaman yang membukanya bisa melakukannya di
        /// thread latar supaya jendela tidak tersendat.
        /// </summary>
        public IReadOnlyList<WilayahDaerah> Provinsi => _provinsi ??= _data.DaftarProvinsi(_kodeProvinsiDiutamakan);

        public IReadOnlyList<WilayahDaerah> Kabupaten { get; private set; } = Kosong;

        public IReadOnlyList<WilayahDaerah> Kecamatan { get; private set; } = Kosong;

        public IReadOnlyList<WilayahDaerah> Desa { get; private set; } = Kosong;

        public WilayahDaerah? ProvinsiTerpilih { get; private set; }

        public WilayahDaerah? KabupatenTerpilih { get; private set; }

        public WilayahDaerah? KecamatanTerpilih { get; private set; }

        public WilayahDaerah? DesaTerpilih { get; private set; }

        /// <summary>Benar bila ada data wilayah sehingga jenjang ini bisa dipakai.</summary>
        public bool AdaData => _data.AdaData;

        /// <summary>
        /// Kode pos saran untuk desa/kelurahan yang dipilih. Hanya saran: pemetaan
        /// kode pos bisa saja tidak tepat untuk alamat kantor desa bersangkutan,
        /// jadi pengguna tetap bisa mengoreksinya.
        /// </summary>
        public string KodeposSaran => DesaTerpilih?.Kodepos ?? string.Empty;

        /// <summary>Pilihan lengkap sampai desa, atau null bila belum lengkap.</summary>
        public RantaiWilayah? Rantai => ProvinsiTerpilih != null && KabupatenTerpilih != null &&
                                        KecamatanTerpilih != null && DesaTerpilih != null
            ? new RantaiWilayah(ProvinsiTerpilih, KabupatenTerpilih, KecamatanTerpilih, DesaTerpilih)
            : null;

        /// <summary>
        /// Pilih provinsi yang diutamakan (Jawa Barat) bila belum ada pilihan sama
        /// sekali. Aplikasi ini dipakai kantor desa di Jawa Barat, jadi saat halaman
        /// Pengaturan dibuka daftar kabupaten/kota Jawa Barat sudah langsung tersedia:
        /// pengguna cukup memilih kabupaten → kecamatan → desa/kelurahan tanpa harus
        /// memilih provinsi lebih dulu. Provinsi lain tetap bisa dipilih kapan saja.
        /// </summary>
        /// <returns>True bila pilihan provinsi berubah.</returns>
        public bool PilihProvinsiDiutamakan()
        {
            if (ProvinsiTerpilih != null)
            {
                return false;
            }

            var utama = Provinsi.FirstOrDefault(p => p.Kode == _kodeProvinsiDiutamakan)
                        ?? Provinsi.FirstOrDefault();

            return utama != null && PilihProvinsi(utama);
        }

        /// <summary>Pilih provinsi; pilihan kabupaten sampai desa dikosongkan.</summary>
        public bool PilihProvinsi(WilayahDaerah? provinsi)
        {
            if (!SesuaiTingkat(provinsi, WilayahJenis.Provinsi))
            {
                return false;
            }

            if (provinsi == null)
            {
                ProvinsiTerpilih = null;
                KabupatenTerpilih = null;
                KecamatanTerpilih = null;
                DesaTerpilih = null;
                Kabupaten = Kosong;
                Kecamatan = Kosong;
                Desa = Kosong;
                return true;
            }

            TurunkanKe(_data.Ambil(provinsi.Kode) ?? provinsi);
            return true;
        }

        /// <summary>Pilih kabupaten/kota; pilihan kecamatan dan desa dikosongkan.</summary>
        public bool PilihKabupaten(WilayahDaerah? kabupaten)
        {
            if (kabupaten == null)
            {
                KabupatenTerpilih = null;
                KecamatanTerpilih = null;
                DesaTerpilih = null;
                Kecamatan = Kosong;
                Desa = Kosong;
                return true;
            }

            if (kabupaten.Jenis is not (WilayahJenis.Kabupaten or WilayahJenis.Kota))
            {
                return false;
            }

            TurunkanKe(_data.Ambil(kabupaten.Kode) ?? kabupaten);
            return true;
        }

        /// <summary>Pilih kecamatan; pilihan desa dikosongkan.</summary>
        public bool PilihKecamatan(WilayahDaerah? kecamatan)
        {
            if (kecamatan == null)
            {
                KecamatanTerpilih = null;
                DesaTerpilih = null;
                Desa = Kosong;
                return true;
            }

            if (kecamatan.Jenis != WilayahJenis.Kecamatan)
            {
                return false;
            }

            TurunkanKe(_data.Ambil(kecamatan.Kode) ?? kecamatan);
            return true;
        }

        /// <summary>Pilih desa/kelurahan; kode pos saran ikut berubah.</summary>
        public bool PilihDesa(WilayahDaerah? desa)
        {
            if (desa == null)
            {
                DesaTerpilih = null;
                return true;
            }

            if (!desa.DesaAtauKelurahan)
            {
                return false;
            }

            TurunkanKe(_data.Ambil(desa.Kode) ?? desa);
            return true;
        }

        /// <summary>
        /// Pasang pilihan dari rantai wilayah yang sudah pasti (mis. hasil pencocokan
        /// data desa yang tersimpan sebelumnya).
        /// </summary>
        public void MuatDariRantai(RantaiWilayah rantai)
        {
            TurunkanKe(_data.Ambil(rantai.Desa.Kode) ?? rantai.Desa);
        }

        /// <summary>
        /// Cocokkan nama desa/kecamatan/kabupaten yang tersimpan dengan data wilayah,
        /// lalu pasang pilihannya bila hasilnya tunggal.
        /// </summary>
        public HasilPencocokanWilayah MuatDariNama(string? namaDesa, string? namaKecamatan, string? namaKabupaten)
        {
            var hasil = WilayahPencocokan.CariRantai(_data, namaDesa, namaKecamatan, namaKabupaten);
            if (hasil.Status == StatusPencocokanWilayah.Ditemukan && hasil.Rantai != null)
            {
                MuatDariRantai(hasil.Rantai);
            }

            return hasil;
        }

        /// <summary>Kosongkan seluruh pilihan.</summary>
        public void Kosongkan()
        {
            ProvinsiTerpilih = null;
            KabupatenTerpilih = null;
            KecamatanTerpilih = null;
            DesaTerpilih = null;
            Kabupaten = Kosong;
            Kecamatan = Kosong;
            Desa = Kosong;
        }

        /// <summary>
        /// Tetapkan pilihan beserta seluruh tingkat di atasnya. Rantai induk dibaca
        /// dari datanya sendiri, sehingga memilih kecamatan tanpa lebih dulu memilih
        /// kabupaten tetap menghasilkan kombinasi yang sah.
        /// </summary>
        private void TurunkanKe(WilayahDaerah wilayah)
        {
            var rantai = new List<WilayahDaerah>(4);
            WilayahDaerah? sekarang = wilayah;
            while (sekarang != null && rantai.Count < 8)
            {
                rantai.Insert(0, sekarang);
                sekarang = sekarang.Induk.Length == 0 ? null : _data.Ambil(sekarang.Induk);
            }

            ProvinsiTerpilih = rantai.Count > 0 ? rantai[0] : null;
            Kabupaten = ProvinsiTerpilih == null ? Kosong : _data.DaftarAnak(ProvinsiTerpilih.Kode);
            KabupatenTerpilih = rantai.Count > 1 ? rantai[1] : null;
            Kecamatan = KabupatenTerpilih == null ? Kosong : _data.DaftarAnak(KabupatenTerpilih.Kode);
            KecamatanTerpilih = rantai.Count > 2 ? rantai[2] : null;
            Desa = KecamatanTerpilih == null ? Kosong : _data.DaftarAnak(KecamatanTerpilih.Kode);
            DesaTerpilih = rantai.Count > 3 ? rantai[3] : null;
        }

        private static bool SesuaiTingkat(WilayahDaerah? wilayah, WilayahJenis tingkat) =>
            wilayah == null || wilayah.Jenis == tingkat;
    }
}
