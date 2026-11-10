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
        // ===================== Baris tabel =====================

        /// <summary>Satu dokumen pada daftar arsip.</summary>
        public sealed class DokumenItem : ObservableObject
        {
            private string _jenis = "APBDes Awal";
            private int _perubahanKe;
            private string _judul = string.Empty;
            private string _nomor = string.Empty;
            private DateTime? _tanggal;
            private int _tahun;
            private string _keterangan = string.Empty;

            public DokumenItem(ApbdesStore.Dokumen model)
            {
                Model = model;
                _tahun = model.Tahun;
                _jenis = model.Jenis;
                _perubahanKe = model.PerubahanKe;
                _judul = model.Judul;
                _nomor = model.Nomor;
                _keterangan = model.Keterangan;
                _tanggal = DateTime.TryParseExact(model.Tanggal, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var t)
                    ? t
                    : null;
            }

            public ApbdesStore.Dokumen Model { get; }

            public string Id => Model.Id;

            public int Tahun
            {
                get => _tahun;
                set
                {
                    if (SetProperty(ref _tahun, value)) OnPropertyChanged(nameof(Label));
                }
            }

            public string Jenis
            {
                get => _jenis;
                set
                {
                    if (SetProperty(ref _jenis, value)) OnPropertyChanged(nameof(Label));
                }
            }

            public int PerubahanKe
            {
                get => _perubahanKe;
                set
                {
                    if (SetProperty(ref _perubahanKe, value)) OnPropertyChanged(nameof(Label));
                }
            }

            public string Judul
            {
                get => _judul;
                set => SetProperty(ref _judul, value);
            }

            public string Nomor
            {
                get => _nomor;
                set => SetProperty(ref _nomor, value);
            }

            public DateTime? Tanggal
            {
                get => _tanggal;
                set
                {
                    if (SetProperty(ref _tanggal, value))
                    {
                        OnPropertyChanged(nameof(Label));
                        OnPropertyChanged(nameof(TanggalTampil));
                    }
                }
            }

            public string Keterangan
            {
                get => _keterangan;
                set => SetProperty(ref _keterangan, value);
            }

            public string Label => ApbdesStore.LabelJenis(_jenis, _perubahanKe) +
                                   (TanggalTampil.Length > 0 ? " · " + TanggalTampil : "");

            public string TanggalTampil => _tanggal?.ToString("dd MMM yyyy", new CultureInfo("id-ID")) ?? "";

            public string Teks => Model.Teks;

            public bool AdaBerkas => !string.IsNullOrWhiteSpace(Model.Berkas);

            public string Berkas => Model.Berkas;

            public string BerkasAsli => Model.BerkasAsli;

            /// <summary>Jumlah baris rincian pada dokumen ini (ditampilkan di daftar).</summary>
            public string PosTampil => Model.Pos.Count == 0 ? "belum ada rincian" : Model.Pos.Count + " baris";

            public string TotalTampil => Model.Pos.Count == 0
                ? "—"
                : Rupiah(Model.Pos.Sum(p => p.Anggaran));

            /// <summary>Pindahkan suntingan yang terlihat di halaman ke model memori.</summary>
            public void TulisKeModel()
            {
                Model.Tahun = _tahun;
                Model.Jenis = _jenis;
                Model.PerubahanKe = _perubahanKe;
                Model.Judul = _judul.Trim();
                Model.Nomor = _nomor.Trim();
                Model.Keterangan = _keterangan.Trim();
                Model.Tanggal = _tanggal?.ToString("yyyy-MM-dd") ?? string.Empty;
            }

            public void PerbaruiRingkasan()
            {
                OnPropertyChanged(nameof(PosTampil));
                OnPropertyChanged(nameof(TotalTampil));
            }

            public void PerbaruiBerkas()
            {
                OnPropertyChanged(nameof(AdaBerkas));
                OnPropertyChanged(nameof(Berkas));
                OnPropertyChanged(nameof(BerkasAsli));
                OnPropertyChanged(nameof(Teks));
            }
        }

        /// <summary>Satu baris rincian anggaran yang bisa disunting di halaman.</summary>
        public sealed class PosItem : ObservableObject
        {
            private string _kode;
            private string _uraian;
            private long _anggaran;
            private long _realisasi;
            private string _keterangan;

            public PosItem(ApbdesStore.Pos pos, Action<PosItem>? hapus = null)
            {
                _kode = pos.Kode;
                _uraian = pos.Uraian;
                _anggaran = pos.Anggaran;
                _realisasi = pos.Realisasi;
                _keterangan = pos.Keterangan;
                HapusCommand = new RelayCommand(() => hapus?.Invoke(this));
            }

            public string Kode
            {
                get => _kode;
                set => SetProperty(ref _kode, value);
            }

            public string Uraian
            {
                get => _uraian;
                set => SetProperty(ref _uraian, value);
            }

            public long Anggaran
            {
                get => _anggaran;
                set
                {
                    // Lebih/(Kurang), %, dan total halaman ikut berubah begitu angka diisi.
                    if (SetProperty(ref _anggaran, value)) PerbaruiHitung();
                }
            }

            public long Realisasi
            {
                get => _realisasi;
                set
                {
                    if (SetProperty(ref _realisasi, value)) PerbaruiHitung();
                }
            }

            public string Keterangan
            {
                get => _keterangan;
                set => SetProperty(ref _keterangan, value);
            }

            /// <summary>Lebih/(Kurang): selisih anggaran dengan realisasi, seperti kolom laporan resmi.</summary>
            public string LebihKurangTampil => Rupiah(_anggaran - _realisasi);

            public string PersenTampil => _anggaran > 0
                ? Math.Round(_realisasi * 100.0 / _anggaran) + "%"
                : "—";

            public ICommand HapusCommand { get; }

            private void PerbaruiHitung()
            {
                OnPropertyChanged(nameof(LebihKurangTampil));
                OnPropertyChanged(nameof(PersenTampil));
            }
        }

    }
}
