using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SuDesApp.Data.Models;
using SuDesApp.Wpf.Mvvm;

namespace SuDesApp.Wpf.ViewModels
{
    /// <summary>Satu pilihan tipe kolom untuk daftar drop-down di wizard.</summary>
    public sealed class TipeKolomOpsi
    {
        public TipeKolomTemplate Nilai { get; init; }
        public string Nama { get; init; } = string.Empty;
        public string Keterangan { get; init; } = string.Empty;
    }

    /// <summary>Satu pilihan perataan blok teks untuk daftar drop-down di wizard.</summary>
    public sealed class RataBlokOpsi
    {
        public RataBlokTemplate Nilai { get; init; }
        public string Nama { get; init; } = string.Empty;
    }

    /// <summary>Satu langkah pada daftar langkah wizard (kiri jendela).</summary>
    public class LangkahItemViewModel : ObservableObject
    {
        private bool _aktif;
        private bool _selesai;

        public LangkahItemViewModel(int nomor, string judul, string keterangan)
        {
            Nomor = nomor;
            Judul = judul;
            Keterangan = keterangan;
        }

        public int Nomor { get; }
        public string Judul { get; }
        public string Keterangan { get; }
        public string NomorTeks => Nomor.ToString(System.Globalization.CultureInfo.InvariantCulture);

        private string _lencana = string.Empty;

        /// <summary>
        /// Lencana ringkasan isian pada navigasi kiri (mis. "3 teks · 2 kolom"),
        /// diperbarui wizard setiap kali isi langkah ini berubah.
        /// </summary>
        public string Lencana
        {
            get => _lencana;
            set => SetProperty(ref _lencana, value ?? string.Empty);
        }

        /// <summary>Langkah yang sedang ditampilkan.</summary>
        public bool Aktif
        {
            get => _aktif;
            set => SetProperty(ref _aktif, value);
        }

        /// <summary>Langkah yang sudah dilalui.</summary>
        public bool Selesai
        {
            get => _selesai;
            set => SetProperty(ref _selesai, value);
        }
    }

    /// <summary>Daftar pilihan bersama (tipe kolom & perataan teks).</summary>
    public static class TemplateSuratOpsi
    {
        /// <summary>Tipe kolom yang bisa dipilih beserta penjelasan singkatnya.</summary>
        public static readonly IReadOnlyList<TipeKolomOpsi> TipeKolom = new List<TipeKolomOpsi>
        {
            new() { Nilai = TipeKolomTemplate.Teks, Nama = "Teks", Keterangan = "Satu baris isian bebas" },
            new() { Nilai = TipeKolomTemplate.Nik, Nama = "NIK", Keterangan = "16 angka, diperiksa otomatis" },
            new() { Nilai = TipeKolomTemplate.Angka, Nama = "Angka", Keterangan = "Hanya menerima angka" },
            new() { Nilai = TipeKolomTemplate.Tanggal, Nama = "Tanggal", Keterangan = "Diisi 17-08-2026, dicetak 17 Agustus 2026" },
            new() { Nilai = TipeKolomTemplate.Paragraf, Nama = "Paragraf", Keterangan = "Isian panjang beberapa baris" },
            new() { Nilai = TipeKolomTemplate.Pilihan, Nama = "Pilihan", Keterangan = "Dipilih dari daftar yang Anda tentukan" }
        };

        /// <summary>Perataan blok teks.</summary>
        public static readonly IReadOnlyList<RataBlokOpsi> Rata = new List<RataBlokOpsi>
        {
            new() { Nilai = RataBlokTemplate.Kiri, Nama = "Kiri" },
            new() { Nilai = RataBlokTemplate.Tengah, Nama = "Tengah" },
            new() { Nilai = RataBlokTemplate.Kanan, Nama = "Kanan" },
            new() { Nilai = RataBlokTemplate.Justify, Nama = "Rata kiri-kanan" }
        };

        /// <summary>Label tipe kolom (dipakai pada ringkasan daftar).</summary>
        public static string Nama(TipeKolomTemplate tipe) =>
            TipeKolom.FirstOrDefault(t => t.Nilai == tipe)?.Nama ?? "Teks";
    }

    /// <summary>
    /// Satu blok teks bebas pada wizard ("tambah teks kosong untuk diketik sendiri").
    /// </summary>
    public class BlokTeksItemViewModel : ObservableObject
    {
        private string _isi = string.Empty;
        private RataBlokTemplate _rata = RataBlokTemplate.Kiri;
        private bool _tebal;
        private bool _miring;

        public BlokTeksItemViewModel() { }

        public BlokTeksItemViewModel(BlokTeksTemplateSurat blok)
        {
            _isi = blok?.Isi ?? string.Empty;
            _rata = blok?.Rata ?? RataBlokTemplate.Kiri;
            _tebal = blok?.Tebal ?? false;
            _miring = blok?.Miring ?? false;
        }

        /// <summary>Teks yang akan tercetak pada surat.</summary>
        public string Isi
        {
            get => _isi;
            set => SetProperty(ref _isi, value);
        }

        public RataBlokTemplate Rata
        {
            get => _rata;
            set => SetProperty(ref _rata, value);
        }

        public bool Tebal
        {
            get => _tebal;
            set => SetProperty(ref _tebal, value);
        }

        public bool Miring
        {
            get => _miring;
            set => SetProperty(ref _miring, value);
        }

        public IReadOnlyList<RataBlokOpsi> DaftarRata => TemplateSuratOpsi.Rata;

        /// <summary>Ringkasan singkat untuk daftar blok di layar.</summary>
        public string Ringkasan
        {
            get
            {
                string isi = (_isi ?? string.Empty).Trim();
                if (isi.Length == 0) return "(blok kosong)";
                var satuBaris = isi.Replace("\r", " ").Replace("\n", " ");
                return satuBaris.Length > 90 ? satuBaris.Substring(0, 90) + "…" : satuBaris;
            }
        }

        public BlokTeksTemplateSurat KeModel() => new()
        {
            Isi = (_isi ?? string.Empty).Trim(),
            Rata = _rata,
            Tebal = _tebal,
            Miring = _miring
        };
    }

    /// <summary>Satu kolom isian pada wizard ("tambah kolom untuk NIK, nama, dan lainnya").</summary>
    public class KolomItemViewModel : ObservableObject
    {
        private string _label = string.Empty;
        private TipeKolomTemplate _tipe = TipeKolomTemplate.Teks;
        private bool _wajib;
        private string _pilihanTeks = string.Empty;
        private string _nilaiBawaan = string.Empty;

        public KolomItemViewModel() { }

        public KolomItemViewModel(KolomTemplateSurat kolom)
        {
            _label = kolom?.Label ?? string.Empty;
            _tipe = kolom?.Tipe ?? TipeKolomTemplate.Teks;
            _wajib = kolom?.Wajib ?? false;
            _pilihanTeks = kolom?.Pilihan == null ? string.Empty : string.Join(", ", kolom.Pilihan);
            _nilaiBawaan = kolom?.NilaiBawaan ?? string.Empty;
            Kunci = kolom?.Kunci ?? string.Empty;
        }

        /// <summary>Kunci teknis; kosong untuk kolom baru (dibuat dari label saat disimpan).</summary>
        public string Kunci { get; private set; } = string.Empty;

        /// <summary>Label yang tercetak di surat, mis. "NIK".</summary>
        public string Label
        {
            get => _label;
            set
            {
                if (SetProperty(ref _label, value))
                {
                    OnPropertyChanged(nameof(JudulBaris));
                }
            }
        }

        public TipeKolomTemplate Tipe
        {
            get => _tipe;
            set
            {
                if (SetProperty(ref _tipe, value))
                {
                    OnPropertyChanged(nameof(PakaiPilihan));
                    OnPropertyChanged(nameof(BantuanTipe));
                }
            }
        }

        public bool Wajib
        {
            get => _wajib;
            set => SetProperty(ref _wajib, value);
        }

        /// <summary>Daftar pilihan (hanya untuk tipe Pilihan), dipisah koma.</summary>
        public string PilihanTeks
        {
            get => _pilihanTeks;
            set => SetProperty(ref _pilihanTeks, value);
        }

        /// <summary>Nilai yang sudah terisi saat formulir pengisian dibuka.</summary>
        public string NilaiBawaan
        {
            get => _nilaiBawaan;
            set => SetProperty(ref _nilaiBawaan, value);
        }

        public bool PakaiPilihan => _tipe == TipeKolomTemplate.Pilihan;

        public IReadOnlyList<TipeKolomOpsi> DaftarTipe => TemplateSuratOpsi.TipeKolom;

        /// <summary>Judul baris pada daftar kolom: label atau penanda belum diberi nama.</summary>
        public string JudulBaris => string.IsNullOrWhiteSpace(_label) ? "(kolom tanpa label)" : _label.Trim();

        /// <summary>Panduan singkat sesuai tipe yang dipilih.</summary>
        public string BantuanTipe => TemplateSuratOpsi.TipeKolom
            .FirstOrDefault(t => t.Nilai == _tipe)?.Keterangan ?? string.Empty;

        /// <summary>Daftar pilihan yang sudah dirapikan (dipisah koma/baris baru).</summary>
        public List<string> PilihanBersih() => (_pilihanTeks ?? string.Empty)
            .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        /// <summary>
        /// Ubah menjadi model kolom. Kunci dipertahankan bila sudah ada supaya update
        /// template tidak mengubah arti data yang sudah tercetak sebelumnya.
        /// </summary>
        public KolomTemplateSurat KeModel(IEnumerable<string> kunciTerpakai)
        {
            var model = new KolomTemplateSurat
            {
                Label = (_label ?? string.Empty).Trim(),
                Tipe = _tipe,
                Wajib = _wajib,
                Pilihan = PilihanBersih(),
                NilaiBawaan = (_nilaiBawaan ?? string.Empty).Trim()
            };

            model.Kunci = string.IsNullOrWhiteSpace(Kunci)
                ? TemplateSuratKunci.Unik(model.Label, kunciTerpakai)
                : Kunci;
            return model;
        }
    }

    /// <summary>
    /// Satu bagian (blok) terurut dari badan surat pada wizard. Urutan daftar
    /// <see cref="TemplateSuratWizardViewModel.Bagian"/> menentukan tata letak dari
    /// atas ke bawah: teks bebas, data diri seseorang, dan kelompok kolom isian.
    /// </summary>
    public abstract class BagianWizardItemViewModel
    {
        public abstract TipeBagianTemplate Tipe { get; }

        /// <summary>Label jenis bagian untuk kepala kartu di wizard.</summary>
        public string JudulTipe => Tipe switch
        {
            TipeBagianTemplate.DataDiri => "Data Diri",
            TipeBagianTemplate.Kolom => "Kolom Isian",
            _ => "Teks"
        };
    }

    /// <summary>Bagian teks bebas pada susunan badan surat.</summary>
    public sealed class BagianTeksWizardItemViewModel : BagianWizardItemViewModel
    {
        public BagianTeksWizardItemViewModel(BlokTeksItemViewModel teks)
        {
            Teks = teks ?? new BlokTeksItemViewModel();
        }

        public override TipeBagianTemplate Tipe => TipeBagianTemplate.Teks;

        public BlokTeksItemViewModel Teks { get; }

        public string Ringkasan => Teks.Ringkasan;
    }

    /// <summary>
    /// Bagian data diri (satu orang). Field yang tampil adalah kolom isian berawalan
    /// <see cref="Kunci"/> supaya boleh dipakai untuk beberapa orang sekaligus.
    /// </summary>
    public sealed class BagianDataDiriWizardItemViewModel : BagianWizardItemViewModel
    {
        public BagianDataDiriWizardItemViewModel(string judul, string kunci)
        {
            Judul = judul;
            Kunci = kunci;
        }

        public override TipeBagianTemplate Tipe => TipeBagianTemplate.DataDiri;

        /// <summary>Judul kelompok yang tercetak (mis. "Pemohon", "Saksi").</summary>
        public string Judul { get; set; } = string.Empty;

        /// <summary>Awalan kunci kolom (mis. "datadiri0"); kolom berawalan <c>[kunci]_…</c>.</summary>
        public string Kunci { get; }

        /// <summary>Field yang tampil (NIK, Nama, Jabatan, TTL, Alamat, dan lainnya).</summary>
        public ObservableCollection<KolomItemViewModel> Kolom { get; } = new();
    }

    /// <summary>Bagian kelompok kolom isian (diisi saat membuat surat).</summary>
    public sealed class BagianKolomWizardItemViewModel : BagianWizardItemViewModel
    {
        public BagianKolomWizardItemViewModel(string judul, string kunci)
        {
            Judul = judul;
            Kunci = kunci;
        }

        public override TipeBagianTemplate Tipe => TipeBagianTemplate.Kolom;

        /// <summary>Judul kelompok (opsional), mis. "Isi Keperluan".</summary>
        public string Judul { get; set; } = string.Empty;

        /// <summary>Awalan kunci kolom (mis. "kolom0").</summary>
        public string Kunci { get; }

        public bool Grid { get; set; }

        public ObservableCollection<KolomItemViewModel> Kolom { get; } = new();

        /// <summary>Isi kelompok dengan daftar kolom yang sudah ada (untuk pemuatan template lama).</summary>
        public BagianKolomWizardItemViewModel DenganKolom(IEnumerable<KolomItemViewModel> kolom)
        {
            Kolom.Clear();
            foreach (var item in kolom)
            {
                Kolom.Add(item);
            }
            return this;
        }
    }

    /// <summary>Pilihan blok Data Diri yang bisa menjadi penandatangan surat.</summary>
    public sealed class PenandatanganDataDiriOpsi
    {
        public PenandatanganDataDiriOpsi(string kunci, string judul)
        {
            Kunci = kunci;
            Judul = judul;
        }

        /// <summary>Kunci bagian (mis. "datadiri0").</summary>
        public string Kunci { get; }

        /// <summary>Judul yang tampil pada pilihan (judul bagian atau penanda nomor).</summary>
        public string Judul { get; }
    }
}
