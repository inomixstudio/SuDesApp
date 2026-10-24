# -*- coding: utf-8 -*-
"""Hasilkan berkas tema WPF SuDesApp dari palet preset desain.

Setiap preset hanya punya tiga warna (surface, accent, text).
Kunci tema WPF (~68 kunci) diturunkan dari ketiganya dengan aturan yang
meniru konvensi tema Green (terang) dan Dark (gelap) yang sudah ada:
latar kartu lebih terang, sidebar lebih gelap, warna status standar
disesuaikan terang/gelap, teks di atas aksen memakai kontras aman.

Pemakaian:  py Tools/gen-tema-preset.py
"""
import os

ROOT = os.path.join(os.path.dirname(__file__), "..", "SuDesApp.Wpf", "Themes")


def hx(s):
    s = s.lstrip("#")
    return tuple(int(s[i:i + 2], 16) for i in (0, 2, 4))


def rgb(t):
    return "#%02X%02X%02X" % tuple(max(0, min(255, round(v))) for v in t)


def mix(a, b, t):
    return tuple(a[i] * (1 - t) + b[i] * t for i in range(3))


def lighten(c, t):
    return mix(c, (255, 255, 255), t)


def darken(c, t):
    return mix(c, (0, 0, 0), t)


def lin(v):
    v /= 255
    return v / 12.92 if v <= 0.03928 else ((v + 0.055) / 1.055) ** 2.4


def lum(c):
    # lin() menormalkan 0–255 → 0–1; jangan dibagi 255 lagi di sini (kesalahan
    # itu membuat semua rasio kontras jauh lebih rendah dari nilai sebenarnya).
    r, g, b = [lin(v) for v in c]
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def kontras(a, b):
    """Rasio kontras WCAG 2.1 antara dua warna."""
    la, lb = lum(a), lum(b)
    hi, lo = max(la, lb), min(la, lb)
    return (hi + 0.05) / (lo + 0.05)


def aman_di_latar(c, latar, target=4.6):
    """Gelapkan warna sampai lolos WCAG AA di SEMUA latar terang yang dipakai.

    Teks sekunder/status tidak boleh hanya "cukup gelap di satu latar": kartu,
    panel, baris zebra, dan chip punya kecerahan berbeda. Warna dicampur hitam
    bertahap sehingga rona aslinya tetap terbaca.
    """
    for i in range(101):
        k = mix(c, (0, 0, 0), i / 100)
        if min(kontras(k, l) for l in latar) >= target:
            return k
    return (0, 0, 0)


def aman_teks_putih(c, target=4.6):
    """Gelapkan warna sampai teks putih di atasnya lolos AA.

    Dipakai untuk Primary tema gelap: warnanya menjadi latar judul header,
    tombol, sekaligus teks di atas tombol putih (HeaderActionButton).
    """
    return aman_di_latar(c, [(255, 255, 255)], target)


def aman_di_gelap(c, latar, target=4.6):
    """Terangkan warna sampai lolos AA di latar gelap (mis. aksen sidebar)."""
    for i in range(101):
        k = mix(c, (255, 255, 255), i / 100)
        if min(kontras(k, l) for l in latar) >= target:
            return k
    return (255, 255, 255)


SUKSES = hx("#15803D")      # mode terang
PERINGATAN = hx("#D97706")
GALAT = hx("#DC2626")
SUKSES_GELAP = hx("#22C55E")
PERINGATAN_GELAP = hx("#F59E0B")
GALAT_GELAP = hx("#EF4444")

TPL = """<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- {nama} Theme — palet diadaptasi dari preset tema desain populer
         (id preset: "{sid}"): latar {surface_c}, aksen {accent_c}, teks {text_c}.
         Seluruh kunci di bawah wajib sama dengan tema lain (dijaga uji
         TemaKonsistenTests); nilai diturunkan dari tiga warna ciri preset
         dengan aturan yang sama seperti Green (terang) / Dark (gelap).
         Warna teks sekunder/status digelapkan otomatis sampai lolos WCAG AA
         di semua permukaan tema (kartu, panel, zebra, chip status). -->

    <Color x:Key="PrimaryColor">{primary}</Color>
    <Color x:Key="SecondaryColor">{secondary}</Color>
    <Color x:Key="AccentColor">{accent}</Color>
    <Color x:Key="BackgroundColor">{background}</Color>
    <Color x:Key="SurfaceColor">{surfacec}</Color>
    <Color x:Key="TextColor">{text}</Color>
    <Color x:Key="TextSecondaryColor">{textsecondary}</Color>
    <Color x:Key="TextSelectedColor">{textselected}</Color>
    <Color x:Key="BorderColor">{border}</Color>
    <Color x:Key="PanelColor">{panel}</Color>
    <Color x:Key="SuccessColor">{success}</Color>
    <Color x:Key="WarningColor">{warning}</Color>
    <Color x:Key="ErrorColor">{error}</Color>
    <Color x:Key="MenuBackgroundColor">{menubg}</Color>
    <Color x:Key="MenuTextColor">{menutext}</Color>
    <Color x:Key="AccentLightColor">{accentlight}</Color>
    <Color x:Key="AccentDarkColor">{accentdark}</Color>
    <Color x:Key="ShadowColor">{shadow}</Color>

    <!-- Subtle badge colors -->
    <Color x:Key="SuccessSubtleColor">{successsubtle}</Color>
    <Color x:Key="WarningSubtleColor">{warningsubtle}</Color>
    <Color x:Key="ErrorSubtleColor">{errorsubtle}</Color>
    <Color x:Key="AccentSubtleColor">{accentsubtle}</Color>
    <Color x:Key="ZebraColor">{zebra}</Color>

    <!-- Brushes -->
    <SolidColorBrush x:Key="PrimaryBrush" Color="{{StaticResource PrimaryColor}}"/>
    <SolidColorBrush x:Key="SecondaryBrush" Color="{{StaticResource SecondaryColor}}"/>
    <SolidColorBrush x:Key="AccentBrush" Color="{{StaticResource AccentColor}}"/>
    <SolidColorBrush x:Key="WindowBackgroundBrush" Color="{{StaticResource BackgroundColor}}"/>
    <SolidColorBrush x:Key="SurfaceBrush" Color="{{StaticResource SurfaceColor}}"/>
    <SolidColorBrush x:Key="TextBrush" Color="{{StaticResource TextColor}}"/>
    <SolidColorBrush x:Key="TextSecondaryBrush" Color="{{StaticResource TextSecondaryColor}}"/>
    <SolidColorBrush x:Key="TextSelectedBrush" Color="{{StaticResource TextSelectedColor}}"/>
    <SolidColorBrush x:Key="BorderBrush" Color="{{StaticResource BorderColor}}"/>
    <SolidColorBrush x:Key="PanelBrush" Color="{{StaticResource PanelColor}}"/>
    <SolidColorBrush x:Key="SuccessBrush" Color="{{StaticResource SuccessColor}}"/>
    <SolidColorBrush x:Key="WarningBrush" Color="{{StaticResource WarningColor}}"/>
    <SolidColorBrush x:Key="ErrorBrush" Color="{{StaticResource ErrorColor}}"/>

    <!-- Subtle Badge & Chip Brushes -->
    <SolidColorBrush x:Key="SuccessSubtleBrush" Color="{{StaticResource SuccessSubtleColor}}"/>
    <SolidColorBrush x:Key="WarningSubtleBrush" Color="{{StaticResource WarningSubtleColor}}"/>
    <SolidColorBrush x:Key="ErrorSubtleBrush" Color="{{StaticResource ErrorSubtleColor}}"/>
    <SolidColorBrush x:Key="AccentSubtleBrush" Color="{{StaticResource AccentSubtleColor}}"/>
    <SolidColorBrush x:Key="ZebraBrush" Color="{{StaticResource ZebraColor}}"/>

    <!-- Warna teks status dipisah dari warna isian agar kontrasnya aman (WCAG AA). -->
    <SolidColorBrush x:Key="SuccessTextBrush" Color="{successtext}"/>
    <SolidColorBrush x:Key="WarningTextBrush" Color="{warningtext}"/>
    <SolidColorBrush x:Key="ErrorTextBrush" Color="{errortext}"/>
    <SolidColorBrush x:Key="AccentTextBrush" Color="{accenttext}"/>

    <!-- Sidebar / StatusBar -->
    <SolidColorBrush x:Key="SidebarBackgroundBrush" Color="{{StaticResource MenuBackgroundColor}}"/>
    <SolidColorBrush x:Key="SidebarTextBrush" Color="{{StaticResource MenuTextColor}}"/>
    <SolidColorBrush x:Key="SidebarAccentBrush" Color="{{StaticResource AccentLightColor}}"/>
    <SolidColorBrush x:Key="SidebarHoverBrush" Color="{hovercolor}"{hoverop}/>
    <SolidColorBrush x:Key="SidebarPressedBrush" Color="{pressedcolor}"{pressedop}/>
    <SolidColorBrush x:Key="StatusBarBackgroundBrush" Color="{{StaticResource MenuBackgroundColor}}"/>
    <SolidColorBrush x:Key="StatusBarTextBrush" Color="{{StaticResource MenuTextColor}}"/>

    <!-- Helper brushes -->
    <SolidColorBrush x:Key="HeaderTextBrush" Color="{headertext}"/>
    <SolidColorBrush x:Key="HeaderSubtitleBrush" Color="{headersubtitle}"{headersubop}/>
    <SolidColorBrush x:Key="LogoCircleBackgroundBrush" Color="{{StaticResource AccentColor}}"/>
    <SolidColorBrush x:Key="ErrorOverlayBrush" Color="{{StaticResource ErrorColor}}" Opacity="{overlayop}"/>

    <SolidColorBrush x:Key="OnPrimaryTextBrush" Color="{onprimary}"/>
    <SolidColorBrush x:Key="OnPrimarySubtleBrush" Color="{onprimary}" Opacity="0.85"/>
    <SolidColorBrush x:Key="OnAccentTextBrush" Color="{onaccent}"/>

    <SolidColorBrush x:Key="OnSuccessTextBrush" Color="{onsuccess}"/>
    <SolidColorBrush x:Key="OnWarningTextBrush" Color="#0F172A"/>
    <SolidColorBrush x:Key="OnErrorTextBrush" Color="{onerror}"/>

    <!-- Font -->
    <FontFamily x:Key="AppFontFamily">pack://application:,,,/SuDesApp;component/Fonts/GoogleSans.ttc#Google Sans, Segoe UI Variable, Segoe UI</FontFamily>

</ResourceDictionary>
"""


def buat(sid, nama, surface, accent, text, gelap):
    S, A, T = hx(surface), hx(accent), hx(text)
    if gelap:
        d = dict(
            nama=nama, sid=sid, surface_c=surface, accent_c=accent, text_c=text,
            # Primary jadi latar judul header DAN teks di atas tombol putih,
            # jadi pastikan putih di atasnya (dan warna ini di atas putih) AA.
            primary=rgb(aman_teks_putih(darken(A, 0.35))),
            secondary=rgb(darken(A, 0.50)),
            accent=rgb(A),
            background=rgb(S),
            surfacec=rgb(lighten(S, 0.06)),
            text=rgb(T),
            textsecondary=rgb(mix(T, S, 0.28)),
            textselected=rgb(lighten(T, 0.20)),
            border=rgb(mix(darken(A, 0.2), S, 0.75)),
            panel=rgb(lighten(S, 0.10)),
            success=rgb(SUKSES_GELAP), warning=rgb(PERINGATAN_GELAP), error=rgb(GALAT_GELAP),
            menubg=rgb(mix(darken(S, 0.35), darken(A, 0.70), 0.18)),
            menutext=rgb(lighten(T, 0.15)),
            accentlight=rgb(lighten(A, 0.25)),
            accentdark=rgb(darken(A, 0.35)),
            shadow="#50000000",
            successsubtle=rgb(mix(SUKSES_GELAP, S, 0.82)),
            warningsubtle=rgb(mix(PERINGATAN_GELAP, S, 0.86)),
            errorsubtle=rgb(mix(GALAT_GELAP, S, 0.85)),
            accentsubtle=rgb(mix(A, S, 0.86)),
            zebra=rgb(lighten(S, 0.045)),
            successtext="#4ADE80", warningtext="#FCD34D", errortext="#FCA5A5",
            accenttext=rgb(lighten(A, 0.15)),
            statusbartextcolor=rgb(mix(T, S, 0.28)),
            hovercolor=rgb(A), hoverop=' Opacity="0.15"',
            pressedcolor=rgb(A), pressedop=' Opacity="0.25"',
            headertext="#FAFAFA",
            # Subjudul harus putih PENUH: di atas Primary yang cukup gelap untuk
            # teks putih, opasitas 0,85 menurunkan kontras di bawah 4,5:1.
            headersubtitle="#FFFFFF", headersubop="",
            overlayop="0.18",
            onprimary="#FFFFFF", onaccent="#0F172A",
            onsuccess="#0F172A", onerror="#0F172A",
        )
    else:
        # Permukaan tempat teks tema benar-benar diletakkan (lihat daftar pasangan
        # di Tools/check-kontras-wcag.ps1). Warna teks sekunder & status dicari
        # otomatis yang lolos AA di SEMUA permukaan ini — bukan hanya di kartu.
        # Tanpa ini teks sekunder preset terang (mis. Blossom #8B7C7F) hanya
        # mencapai 3,3:1 di panel dan tampak pudar/hampir tak terbaca.
        surfacec = lighten(S, 0.60)
        panel = mix(lighten(S, 0.30), A, 0.07)
        zebra = darken(S, 0.035)
        accentsubtle = mix(A, S, 0.86)
        successsubtle = mix(SUKSES, S, 0.85)
        warningsubtle = mix(PERINGATAN, S, 0.88)
        errorsubtle = mix(GALAT, S, 0.87)
        latar = [surfacec, S, panel, zebra, accentsubtle,
                 successsubtle, warningsubtle, errorsubtle]
        menubg = mix(darken(A, 0.50), T, 0.28)
        d = dict(
            nama=nama, sid=sid, surface_c=surface, accent_c=accent, text_c=text,
            primary=rgb(darken(A, 0.32)),
            secondary=rgb(darken(A, 0.45)),
            accent=rgb(A),
            background=rgb(S),
            surfacec=rgb(surfacec),
            text=rgb(T),
            textsecondary=rgb(aman_di_latar(mix(T, S, 0.42), latar)),
            textselected=rgb(darken(A, 0.20)),
            border=rgb(mix(A, S, 0.72)),
            panel=rgb(panel),
            success=rgb(SUKSES), warning=rgb(PERINGATAN), error=rgb(GALAT),
            menubg=rgb(menubg),
            menutext="#FFFFFF",
            accentlight=rgb(aman_di_gelap(lighten(A, 0.22), [menubg])),
            accentdark=rgb(darken(A, 0.32)),
            shadow="#33000000",
            successsubtle=rgb(successsubtle),
            warningsubtle=rgb(warningsubtle),
            errorsubtle=rgb(errorsubtle),
            accentsubtle=rgb(accentsubtle),
            zebra=rgb(zebra),
            successtext=rgb(aman_di_latar(hx("#117F3A"), latar)),
            warningtext=rgb(aman_di_latar(hx("#A15904"), latar)),
            errortext=rgb(aman_di_latar(hx("#C92020"), latar)),
            accenttext=rgb(aman_di_latar(darken(A, 0.32), latar)),
            statusbartextcolor="#FFFFFF",
            hovercolor="#FFFFFF", hoverop=' Opacity="0.12"',
            pressedcolor="#FFFFFF", pressedop=' Opacity="0.2"',
            headertext="#FFFFFF", headersubtitle="#FFFFFF", headersubop=' Opacity="0.85"',
            overlayop="0.12",
            onprimary="#FFFFFF", onaccent="#FFFFFF",
            onsuccess="#FFFFFF", onerror="#FFFFFF",
        )
    path = os.path.join(ROOT, nama + "Theme.xaml")
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(TPL.format(**d))
    print("tulis", path)


buat("orbit", "Orbit", "#171624", "#C2AFF7", "#E9E5F7", gelap=True)
buat("ember", "Ember", "#1D1310", "#F0A066", "#F3E5DA", gelap=True)
buat("kanagawa", "Kanagawa", "#F2EBD9", "#2F5D8C", "#1F2A38", gelap=False)
buat("blossom", "Blossom", "#F6EEEE", "#B4566F", "#3D292F", gelap=False)
