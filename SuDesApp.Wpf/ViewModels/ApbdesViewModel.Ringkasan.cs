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
        // ===================== Ringkasan =====================

        private void RingkasanBerubah()
        {
            foreach (var nama in new[]
                     {
                         nameof(TotalAnggaranTampil), nameof(TotalRealisasiTampil), nameof(SisaTampil),
                         nameof(PendapatanTampil), nameof(BelanjaTampil), nameof(PembiayaanTampil),
                         nameof(TidakAdaPos)
                     })
            {
                OnPropertyChanged(nama);
            }

            if (_terpilih != null)
            {
                _terpilih.PerbaruiRingkasan();
            }
        }

        /// <summary>Belum ada baris rincian — tabel menampilkan keterangan, bukan ruang kosong.</summary>
        public bool TidakAdaPos => Pos.Count == 0;

        public string TotalAnggaranTampil => Pos.Count == 0 ? "—" : Rupiah(Pos.Sum(p => p.Anggaran));

        public string TotalRealisasiTampil => Pos.Count == 0 ? "—" : Rupiah(Pos.Sum(p => p.Realisasi));

        public string SisaTampil
        {
            get
            {
                if (Pos.Count == 0) return "—";
                long sisa = Pos.Sum(p => p.Anggaran) - Pos.Sum(p => p.Realisasi);
                return Rupiah(sisa);
            }
        }

        public string PendapatanTampil => RingkasanKode("4");
        public string BelanjaTampil => RingkasanKode("5");
        public string PembiayaanTampil => RingkasanKode("6");

        /// <summary>Jumlah per kelompok kode rekening pertama (4 Pendapatan, 5 Belanja, 6 Pembiayaan).</summary>
        private string RingkasanKode(string awalan)
        {
            var nyala = Pos.Where(p => (p.Kode ?? string.Empty).StartsWith(awalan, StringComparison.Ordinal)).ToList();
            if (nyala.Count == 0) return "—";

            long anggaran = nyala.Sum(p => p.Anggaran);
            long realisasi = nyala.Sum(p => p.Realisasi);
            string persen = anggaran > 0 ? $" ({Math.Round(realisasi * 100.0 / anggaran)}%)" : "";
            return $"{Rupiah(anggaran)} — terpakai {Rupiah(realisasi)}{persen}";
        }

        /// <summary>Rupiah format ribuan Indonesia ("Rp1.234.567").</summary>
        internal static string Rupiah(long nominal) =>
            "Rp" + nominal.ToString("N0", new CultureInfo("id-ID"));


    }
}
