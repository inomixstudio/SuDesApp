using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SuDesApp.Data.Models;
using SuDesApp.GeneratorPdf;
using SuDesApp.Services;
using SuDesApp.Utilities;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class PerangkatDesaViewModel
    {
/// <summary>
        /// Buat SK jadi (bukan contoh) untuk baris yang baru tersimpan: isian
        /// memakai data orang asli dari form — nomor &amp; tanggal SK, nama, NIK,
        /// wilayah, dan masa jabatan — sedangkan tanda tangannya memakai nama
        /// Kepala Desa dari data desa (Contoh = false). PDF-nya ditulis ke folder
        /// arsip dengan nama tetap per baris lalu namanya ditulis pada kolom
        /// BerkasSK. Mengembalikan true bila SK memang dibuat.
        /// </summary>
        private async Task<bool> BuatSkJadiAsync(PerangkatDesa data)
        {
            string jabatan = data.JabatanTampil;
            string kelompok = JabatanPerangkat.Kelompok(jabatan);

            // Kelompok tanpa template (mis. LAINNYA) tidak bisa dibuatkan SK;
            // datanya tetap tersimpan tanpa berkas SK.
            if (!SkPerangkatKatalog.AdaUntuk(jabatan, kelompok)) return false;

            // SK bersama (LINMAS & POSYANDU): satu SK untuk seluruh anggota
            // jabatan ini dengan lampiran daftar nama — bukan SK per orang,
            // sama seperti contoh SK Linmas dan SK Posyandu desa.
            bool bersama = SkPerangkatKatalog.LazimBersama(kelompok);
            var peserta = bersama
                ? PesertaSkBersama(data, jabatan)
                : new List<PerangkatDesa> { data };
            var lampiran = bersama
                ? await SusunLampiranSkBersamaAsync(kelompok, peserta).ConfigureAwait(true)
                : (IReadOnlyList<BarisLampiranSk>)Array.Empty<BarisLampiranSk>();

            var desa = await _desaRepo.GetInfoDesaAsync().ConfigureAwait(true);
            var template = SkPerangkatKatalog.Untuk(jabatan, kelompok);
            var isi = new SkPerangkatIsi
            {
                // Pemberhentian mengikuti status baris; kelompok yang pada
                // contoh SK-nya berbentuk penetapan (KPM) memakai Penetapan
                // — bukan pengangkatan.
                Jenis = data.StatusTampil == StatusPerangkat.Berhenti
                    ? SkJenisPerangkat.Pemberhentian
                    : template?.BisaPenetapan == true
                        ? SkJenisPerangkat.Penetapan
                        : SkJenisPerangkat.Pengangkatan,
                Kelompok = kelompok,
                Nomor = data.NomorSK ?? string.Empty,
                Tanggal = data.TanggalSK ?? DateTime.Today,
                // Pada SK bersama nama & NIK dicetak dari halaman lampiran.
                Nama = bersama ? string.Empty : data.Nama,
                NIK = bersama ? string.Empty : data.NIK ?? string.Empty,
                Jabatan = jabatan,
                Wilayah = data.WilayahRingkas == "-" ? string.Empty : data.WilayahRingkas,
                Mulai = data.MasaJabatanMulai,
                Selesai = data.MasaJabatanSelesai,

                // Alasan pemberhentian SELALU terisi. Jalur ini (simpan form)
                // tidak melewati panel SK sama sekali, jadi tanpa ini setiap SK
                // pemberhentian otomatis tercetak tanpa dasar — "… dari jabatan
                // KADUS di Desa XYZ, terhitung sejak tanggal …" — yang tidak sah.
                Alasan = data.StatusTampil == StatusPerangkat.Berhenti
                    ? SkPerangkatKatalog.KalimatAlasanPemberhentian(data.Catatan)
                    : string.Empty,

                Lampiran = lampiran,
                PenandaDiktum = SkPerangkatKatalog.PenandaDiktum(kelompok),
                Memperhatikan = SkPerangkatKatalog.Memperhatikan(kelompok),
                Contoh = false
            };

            // Generate PDF di thread_pool agar UI tetap responsif.
            byte[] pdf = await Task.Run(() =>
            {
                using var stream = new MemoryStream();
                _skGenerator.GenerateSkPdf(stream, desa, isi);
                return stream.ToArray();
            }, CancellationToken.None).ConfigureAwait(true);
            if (pdf.Length == 0)
            {
                throw new InvalidOperationException(
                    "PDF SK kosong. Periksa data desa pada Pengaturan, lalu coba lagi.");
            }

            // Satu berkas dipakai bersama: SK bersama ditautkan ke seluruh
            // anggota, SK biasa hanya ke baris ini.
            string namaBaru = BerkasSkPerangkat.SimpanHasilOtomatis(data.ID, pdf);
            await TulisBerkasSkAsync(peserta, namaBaru).ConfigureAwait(true);

            _activityLog.Log("PERANGKAT",
                bersama ? $"{jabatan} — {peserta.Count} anggota" : $"{data.Nama} ({jabatan})",
                "SK jadi dibuat otomatis", namaBaru);
            return true;
        }

        /// <summary>
        /// Seluruh anggota satu jabatan untuk SK bersama: orang yang masih
        /// memegang jabatan didahulukan (bila tidak ada, seluruh orangnya dipakai
        /// — sama dengan aturan lampiran panel), dan baris yang baru disimpan
        /// selalu ikut memakai data terbarunya.
        /// </summary>
        private List<PerangkatDesa> PesertaSkBersama(PerangkatDesa data, string jabatan)
        {
            var (orang, _) = CalonLampiran(jabatan);
            int indeks = orang.FindIndex(p => p.ID == data.ID);
            if (indeks >= 0)
            {
                orang[indeks] = data;
            }
            else
            {
                orang.Add(data);
            }
            return orang;
        }

        /// <summary>
        /// Susun baris lampiran SK bersama, lengkap dengan data warga lewat NIK.
        /// Pengelompokan unit: LINMAS satu daftar tanpa judul (atau mengikuti
        /// isian Unit operator); POSYANDU dikelompokkan per posyandu — isian Unit
        /// didahulukan (mis. "POSYANDU SAKURA I"), dan bila kosong diturunkan
        /// dari dusun orangnya sebab jumlah posyandu mengikuti jumlah Kepala Dusun.
        /// </summary>
        private async Task<IReadOnlyList<BarisLampiranSk>> SusunLampiranSkBersamaAsync(
            string kelompok, List<PerangkatDesa> peserta)
        {
            var sumber = peserta
                .Select(p => new LampiranSumber
                {
                    Orang = p,
                    Peran = PeranLampiran(kelompok),
                    Unit = UnitLampiran(kelompok, p)
                })
                // Urutan sumber menentukan urutan blok unit pada lampiran:
                // urut unit (tanpa unit dulu), lalu yang masih menjabat, lalu nama.
                .OrderBy(s => s.Unit, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(s => s.Orang.MasihMemegangJabatan)
                .ThenBy(s => s.Orang.Nama, StringComparer.CurrentCulture)
                .ToList();

            return await _lampiranService.SusunAsync(sumber).ConfigureAwait(true);
        }

        /// <summary>
        /// Peran awal baris lampiran: Posyandu memakai "Anggota" sebab contoh
        /// SK-nya memuat kolom Jabatan berisi Ketua/Bendahara/Anggota — operator
        /// mengganti baris yang memegang peran. Kelompok lain kosong supaya kolom
        /// Jabatan tidak muncul saat seluruh barisnya seragam.
        /// </summary>
        private static string PeranLampiran(string kelompok) =>
            string.Equals(kelompok, "POSYANDU", StringComparison.Ordinal) ? "Anggota" : string.Empty;

        /// <summary>Unit lampiran satu orang pada SK bersama.</summary>
        private static string UnitLampiran(string kelompok, PerangkatDesa orang)
        {
            string unit = (orang.Unit ?? string.Empty).Trim();

            if (string.Equals(kelompok, "POSYANDU", StringComparison.Ordinal))
            {
                if (unit.Length > 0) return unit;

                // Posyandu mengikuti dusun: jumlah posyandu = jumlah Kepala Dusun,
                // jadi tanpa isian Unit, bloknya diturunkan dari dusun orangnya
                // (mis. POSYANDU KALENJAYA).
                string dusun = (orang.Dusun ?? string.Empty).Trim();
                return dusun.Length > 0 ? "POSYANDU " + dusun : string.Empty;
            }

            // LINMAS (dan KPM): satu daftar tanpa judul blok, sama seperti contoh
            // SK-nya; kelompok lain tetap memakai isian unitnya.
            if (string.Equals(kelompok, "LINMAS", StringComparison.Ordinal)
                || string.Equals(kelompok, "KADER KPM", StringComparison.Ordinal))
            {
                return string.Empty;
            }

            return unit;
        }

        /// <summary>
        /// Tulis nama berkas SK pada kolom BerkasSK seluruh peserta SK. Berkas
        /// otomatis lama dibuang setelah semua baris berpindah supaya tidak ada
        /// baris yang menunjuk berkas terhapus; berkas unggahan operator tidak
        /// pernah dihapus (bisa jadi SK bertanda tangan asli) — hanya tautannya
        /// yang berganti.
        /// </summary>
        private async Task TulisBerkasSkAsync(List<PerangkatDesa> peserta, string namaBaru)
        {
            var lama = peserta
                .Select(p => p.BerkasSK)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var orang in peserta)
            {
                if (string.Equals(orang.BerkasSK ?? string.Empty, namaBaru,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                orang.BerkasSK = namaBaru;
                orang.DiperbaruiOleh = SessionContext.Display;
                var ulang = await _layanan.SimpanAsync(orang, modeUbah: true).ConfigureAwait(true);
                if (!ulang.Berhasil)
                {
                    throw new InvalidOperationException(ulang.Kekurangan.Count > 0
                        ? string.Join(Environment.NewLine, ulang.Kekurangan)
                        : ulang.Pesan ?? "nama berkas SK belum tertulis pada data.");
                }
            }

            foreach (var nama in lama)
            {
                if (!string.Equals(nama, namaBaru, StringComparison.OrdinalIgnoreCase)
                    && BerkasSkPerangkat.AdalahHasilOtomatis(nama))
                {
                    BerkasSkPerangkat.Hapus(nama);
                }
            }
        }
    }
}
