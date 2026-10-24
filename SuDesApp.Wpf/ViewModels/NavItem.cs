using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using SuDesApp.Wpf.Mvvm;
using SuDesApp.Wpf.Utilities;

namespace SuDesApp.Wpf.ViewModels
{
    public class NavItem : ObservableObject
    {
        private string _title = string.Empty;
        private string? _accessibilityName;
        private string _statusText = string.Empty;
        private bool _isExpanded;
        private bool _terkunci;
        private string _pesanTerkunci = string.Empty;

        public NavItem()
        {
            // Accordion eksklusif: saat item ini di-expand, semua akordeon lain
            // otomatis ditutup — hanya satu terbuka pada satu waktu. Menutup
            // sibling tidak memicu rekursi karena handler hanya bereaksi pada nilai true.
            PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(IsExpanded) && _isExpanded)
                {
                    foreach (var sibling in _allAccordions)
                    {
                        if (!ReferenceEquals(sibling, this) && sibling.IsExpanded)
                        {
                            sibling.IsExpanded = false;
                        }
                    }
                }
            };
        }

        public string Title
        {
            get => _title;
            set
            {
                if (SetProperty(ref _title, value)) OnPropertyChanged(nameof(TooltipCiut));
            }
        }

        /// <summary>Nama UI Automation; item biasa tetap memakai judulnya.</summary>
        public string AccessibilityName
        {
            get => _accessibilityName ?? Title;
            set => SetProperty(ref _accessibilityName, value);
        }
        /// <summary>Glyph ikon item (font ikon Segoe Fluent/MDL2). Nilai bawaan dari
        /// <see cref="IkonMenu.Halaman"/> — jangan isi glyph literal teks di sini.</summary>
        public string Icon { get; set; } = IkonMenu.Halaman;

        /// <summary>Glyph chip pada header accordion (bawaan: <see cref="IkonMenu.Folder"/>). Memakai font ikon
        /// monokrom agar warnanya mengikuti tema, bukan emoji berwarna tetap.</summary>
        public string GroupIcon { get; set; } = IkonMenu.Folder;

        public string Description { get; set; } = string.Empty;

        /// <summary>Ringkasan dinamis yang tampil di bawah judul item sidebar bila tersedia.</summary>
        public string StatusText
        {
            get => _statusText;
            set
            {
                if (SetProperty(ref _statusText, value)) OnPropertyChanged(nameof(TooltipCiut));
            }
        }

        /// <summary>
        /// Tooltip saat sidebar ciut (hanya ikon yang tampak): judul, ditambah
        /// baris status bila ada — mis. server yang dituju menu Pusat
        /// Administrasi tetap terbaca walau labelnya sedang disembunyikan —
        /// dan alasan terkunci bila menu vital sedang dikunci.
        /// </summary>
        public string TooltipCiut
        {
            get
            {
                var bagian = new List<string> { Title };
                if (!string.IsNullOrWhiteSpace(StatusText)) bagian.Add(StatusText);
                if (Terkunci && !string.IsNullOrWhiteSpace(PesanTerkunci)) bagian.Add(PesanTerkunci);
                return string.Join(" — ", bagian);
            }
        }

        /// <summary>
        /// Rute menu yang dijalankan item ini ("warga", "surat:SKTM", …).
        /// Kosong untuk label seksi, akordeon, dan pilihan tema. Dipakai gerbang
        /// lisensi untuk menandai menu vital TANPA mencocokkan judulnya.
        /// </summary>
        public string Route { get; set; } = string.Empty;

        /// <summary>
        /// True bila item ini menu vital yang sedang dikunci karena lisensi belum
        /// aktif. Tampil sebagai gembok + judul redup di sidebar, dan tooltip-nya
        /// menyebut jalan keluar (mulai trial atau bayar).
        /// </summary>
        public bool Terkunci
        {
            get => _terkunci;
            set
            {
                if (SetProperty(ref _terkunci, value))
                {
                    OnPropertyChanged(nameof(TooltipCiut));
                    OnPropertyChanged(nameof(TooltipTampil));
                }
            }
        }

        /// <summary>Kalimat tooltip saat terkunci — dibedakan oleh keadaan lisensi (ajakan trial vs wajib bayar).</summary>
        public string PesanTerkunci
        {
            get => _pesanTerkunci;
            set
            {
                if (SetProperty(ref _pesanTerkunci, value))
                {
                    OnPropertyChanged(nameof(TooltipCiut));
                    OnPropertyChanged(nameof(TooltipTampil));
                }
            }
        }

        /// <summary>
        /// Tooltip yang benar-benar dipasang di sidebar: penjelasan biasa, atau
        /// alasan terkunci bila menu ini sedang dikunci. Kosong = tanpa tooltip.
        /// </summary>
        public string TooltipTampil => Terkunci
            ? Title + (string.IsNullOrWhiteSpace(PesanTerkunci) ? " — terkunci" : " — " + PesanTerkunci)
            : Description;

        /// <summary>Titik warna (swatch) untuk item — dipakai pemilih tema visual:
/// bulatan warna aksen tiap tema di samping namanya. Null =
        /// tanpa swatch (item biasa; ikon glyph tetap ditampilkan).</summary>
        public System.Windows.Media.Brush? SwatchBrush { get; set; }

        /// <summary>Id tema (mis. "Green") bila item adalah pilihan tema —
        /// dipakai untuk menandai tema aktif setelah klik.</summary>
        public string? TemaId { get; set; }

        private bool _isTemaAktif;

        /// <summary>True bila item tema mewakili tema yang sedang aktif —
        /// menampilkan tanda centang di ujung kanan baris menu.</summary>
        public bool IsTemaAktif
        {
            get => _isTemaAktif;
            set => SetProperty(ref _isTemaAktif, value);
        }
        public ObservableCollection<NavItem> Children { get; } = new();

        /// <summary>Ikon emoji/teks yang ditampilkan di samping judul pada template tombol.</summary>
        public string IconGlyph
        {
            get => Icon;
            set => Icon = value;
        }

        /// <summary>True jika item adalah label section (header, bukan tombol/accordion).</summary>
        public bool IsSectionHeader { get; set; }

        /// <summary>True jika item adalah accordion yang dapat di-expand (memiliki Children).</summary>
        public bool IsAccordion { get; set; }

        public bool HasChildren => Children.Count > 0;

        /// <summary>
        /// Daftar bersama seluruh akordeon sidebar. Dihubungkan sekali di BuildSidebar
        /// melalui RegisterExclusiveAccordions agar perilaku eksklusif dapat dibagi
        /// antar item tanpa membuat dependensi antar-instance yang tersembunyi.
        /// </summary>
        private static readonly List<NavItem> _allAccordions = new();

        /// <summary>
        /// Aktifkan mode akordeon eksklusif: membuka satu akordeon otomatis menutup
        /// semua akordeon lain (hanya satu terbuka pada satu waktu).
        /// </summary>
        public static void RegisterExclusiveAccordions(IEnumerable<NavItem> accordions)
        {
            _allAccordions.Clear();
            _allAccordions.AddRange(accordions);
        }

        public ICommand? Action { get; set; }

        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        private bool _isActive;

        /// <summary>True bila halaman item ini sedang ditampilkan (disorot di sidebar).</summary>
        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value);
        }
    }

    /// <summary>
    /// Pembungkus perintah tombol sidebar: menandai halaman sebagai aktif lebih dulu,
    /// lalu meneruskan eksekusi dan status enabled ke perintah aslinya.
    /// </summary>
    internal sealed class PenandaAktifCommand : ICommand
    {
        private readonly ICommand _asli;
        private readonly Action _saatDipilih;

        public PenandaAktifCommand(ICommand asli, Action saatDipilih)
        {
            _asli = asli ?? throw new ArgumentNullException(nameof(asli));
            _saatDipilih = saatDipilih ?? throw new ArgumentNullException(nameof(saatDipilih));
            _asli.CanExecuteChanged += (_, e) => CanExecuteChanged?.Invoke(this, e);
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => _asli.CanExecute(parameter);

        public void Execute(object? parameter)
        {
            _saatDipilih();
            _asli.Execute(parameter);
        }
    }

}
