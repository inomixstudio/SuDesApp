using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using SuDesApp.Data.Repositories;
using SuDesApp.Services;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Services;

namespace SuDesApp.Wpf.ViewModels
{
    public partial class ApbdesViewModel : ObservableObject, IDisposable
    {
        // ===================== Muat & simpan =====================

        private void Muat()
        {
            _semua = ApbdesStore.Muat();
            SegarkanDaftar();
            SegarkanPilihanTahun();
        }

        /// <summary>
        /// Segarkan daftar tahun di ComboBox. Dipanggil setiap kali arsip dibaca
        /// atau dimuat ulang, sehingga tahun berjalan menurut jam komputer ikut
        /// ditambahkan walau aplikasi sudah lama terbuka (melewati pergantian tahun).
        /// </summary>
        private void SegarkanPilihanTahun()
        {
            OnPropertyChanged(nameof(TahunPilihan));
        }

        private void MuatUlang()
        {
            SimpanKeModel();
            Muat();
            Pesan = "Daftar dokumen dimuat ulang dari arsip komputer ini.";
            PesanGalat = string.Empty;
        }

        /// <summary>Isi daftar dokumen untuk tahun aktif dari daftar di memori.</summary>
        private void SegarkanDaftar(DokumenItem? pilih = null)
        {
            string? idPilihan = (pilih ?? _terpilih)?.Id;

            Dokumen.Clear();
            foreach (var dok in _semua.Where(d => d.Tahun == Tahun)
                         .OrderBy(d => d.Jenis == "APBDes Perubahan" ? 1 : 0)
                         .ThenBy(d => d.PerubahanKe)
                         .ThenBy(d => d.Jenis))
            {
                Dokumen.Add(new DokumenItem(dok));
            }

            var berikut = idPilihan == null
                ? Dokumen.FirstOrDefault()
                : Dokumen.FirstOrDefault(d => d.Id == idPilihan) ?? Dokumen.FirstOrDefault();

            _terpilih = null;
            DokumenTerpilih = berikut;

            OnPropertyChanged(nameof(TidakAdaDokumen));
            OnPropertyChanged(nameof(TahunPilihan));
            SegarkanRiwayat();
            PerbaruiTombol();
        }

        private void Tambah()
        {
            SimpanKeModel();

            var dok = new ApbdesStore.Dokumen
            {
                Tahun = Tahun,
                Jenis = "APBDes Awal",
                PerubahanKe = 0,
                Judul = "APBDes " + Tahun.ToString(CultureInfo.InvariantCulture),
                Tanggal = _jam().ToString("yyyy-MM-dd"),
                Diubah = _jam().ToString("s"),
            };

            _semua.Add(dok);
            var item = new DokumenItem(dok);
            Dokumen.Add(item);
            DokumenTerpilih = item;

            OnPropertyChanged(nameof(TidakAdaDokumen));
            Pesan = "Dokumen baru ditambahkan. Isi atau impor berkasnya, lalu tekan Simpan.";
            PesanGalat = string.Empty;
            PesanPeringatan = string.Empty;
        }

        private void Simpan()
        {
            if (!SimpanKeModel())
            {
                Pesan = string.Empty;
                return;
            }

            PesanPeringatan = string.Empty;
            PesanGalat = string.Empty;

            if (ApbdesStore.Simpan(_semua))
            {
                Pesan = $"Tersimpan di komputer ini: {_semua.Count} dokumen APBDES di arsip SuDesApp.";
                foreach (var d in Dokumen) d.PerbaruiRingkasan();
            }
            else
            {
                Pesan = string.Empty;
                PesanGalat = "Gagal menyimpan berkas APBDES — periksa disk/hak akses.";
            }
        }

        /// <summary>
        /// Pindahkan suntingan dokumen yang sedang dibuka (termasuk tabel rincian)
        /// ke model di memori. Dianggap berhasil bila ada dokumen terpilih.
        /// </summary>
        private bool SimpanKeModel()
        {
            if (_terpilih == null) return false;
            if (_pindahDokumen) return false;

            _terpilih.TulisKeModel();
            _terpilih.Model.Pos = Pos
                .Where(p => !string.IsNullOrWhiteSpace(p.Kode) || !string.IsNullOrWhiteSpace(p.Uraian)
                            || p.Anggaran != 0 || p.Realisasi != 0)
                .Select(p => new ApbdesStore.Pos
                {
                    Kode = p.Kode?.Trim() ?? string.Empty,
                    Uraian = p.Uraian?.Trim() ?? string.Empty,
                    Anggaran = p.Anggaran,
                    Realisasi = p.Realisasi,
                    Keterangan = p.Keterangan?.Trim() ?? string.Empty,
                })
                .ToList();
            _terpilih.Model.Diubah = _jam().ToString("s");
            _terpilih.PerbaruiRingkasan();
            return true;
        }

        private void IsiPosDariModel()
        {
            foreach (var item in Pos.ToList()) item.PropertyChanged -= PosBerubah;
            Pos.Clear();

            if (_terpilih != null)
            {
                foreach (var p in _terpilih.Model.Pos)
                {
                    Pos.Add(BuatPos(p));
                }
            }

            RingkasanBerubah();
        }

        /// <summary>
        /// Buat baris tabel sekaligus menyambungkannya ke ringkasan: setiap
        /// perubahan angka pada baris harus langsung terlihat di total, tanpa
        /// menunggu disimpan.
        /// </summary>
        private PosItem BuatPos(ApbdesStore.Pos pos)
        {
            var item = new PosItem(pos, HapusPos);
            item.PropertyChanged += PosBerubah;
            return item;
        }

        private void PosBerubah(object? sender, PropertyChangedEventArgs e)
        {
            if (_pindahDokumen) return;
            RingkasanBerubah();
        }

        private void TambahPos()
        {
            if (_terpilih == null) return;
            Pos.Add(BuatPos(new ApbdesStore.Pos { Kode = "5" }));
            Pesan = string.Empty;
        }

        private void HapusPos(PosItem item)
        {
            item.PropertyChanged -= PosBerubah;
            Pos.Remove(item);
        }

        private void HapusDokumen()
        {
            if (_terpilih == null) return;

            var dihapus = _terpilih;
            ApbdesStore.HapusBerkas(dihapus.Model.Berkas);
            _semua.RemoveAll(d => d.Id == dihapus.Model.Id);
            Dokumen.Remove(dihapus);

            _terpilih = null;
            DokumenTerpilih = Dokumen.FirstOrDefault();

            Pesan = "Dokumen \"" + ApbdesStore.LabelJenis(dihapus.Jenis, dihapus.PerubahanKe)
                    + "\" dihapus dari daftar — tekan Simpan supaya penghapusan tetap tersimpan.";
            PesanPeringatan = string.Empty;
        }


    }
}
