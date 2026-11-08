using System;
using System.Reflection;
using System.Threading.Tasks;
using SuDesApp.Wpf.ViewModels;
using Xunit;

namespace SuDesApp.Tests.Idm;

/// <summary>
/// Kartu keadaan IDM: halaman tidak boleh berakhir sebagai layar kosong —
/// saat gagal/offline selalu ada kartu (JudulKeadaan + IsiKeadaan).
/// Uji ini memeriksa ViewModel state tanpa perlu Muatan jaringan.
/// </summary>
public sealed class IdmKartuKeadaanTests
{
    [Fact]
    public void Awal_tampilkanKartuKeadaan_denganJudulBelumDimuat()
    {
        var vm = BuatVm();
        Assert.True(vm.TampilkanKartuKeadaan);
        Assert.Equal("Data IDM belum dimuat", vm.JudulKeadaan);
        Assert.NotNull(vm.IsiKeadaan);
    }

    [Fact]
    public void AdaGalat_judulKartuMenjadiBelumBisaDimuat()
    {
        var vm = BuatVm();
        // PesanGalat adalah private-set; set lewat refleksi agar test tetap
        // memvalidasi kontrak tampak tanpa perlu memanggil MuatAsync (jaringan).
        var prop = typeof(IdmViewModel).GetProperty(nameof(IdmViewModel.PesanGalat));
        prop!.SetMethod!.Invoke(vm, new object[] { "Situs pusat tidak terjangkau." });
        Assert.True(vm.AdaGalat);
        Assert.Equal("Data pusat belum bisa dimuat", vm.JudulKeadaan);
        Assert.Contains("tidak terjangkau", vm.IsiKeadaan);
    }

    private static IdmViewModel BuatVm()
    {
        // Dependensi SettingsManager/WilayahDataStore/klien tidak dipakai untuk
        // state awal kartu (PesanGalat kosong, SedangMemuat false). Stub dengan
        // DispatchProxy agar konstruktor lolos tanpa jaringan/DB.
        var pengaturan = SettingsTunggu.Barua();
        return new IdmViewModel(
            pengaturan: pengaturan,
            wilayah: WilayahTunggu.Barua(),
            klien: KlienTunggu.Barua(),
            logger: null,
            jam: null,
            simpanan: null,
            navigasi: null,
            previewFactory: null,
            appConfig: null,
            pesan: null,
            pemantauan: null,
            sertifikat: null,
            indeksDesa2025: null,
            dokumenFactory: null);
    }

    // SettingsManager dan WilayahDataStore adalah class — buat instance tanpa
    // menjalankan konstruktor agar test tidak memicu IO.
#pragma warning disable SYSLIB0050 // Formatter-based serialization obsolete, dipakai khusus untuk uninitialized object di test
    private static object BuatTanpaKonstruktor(Type tipe) =>
        System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(tipe);
#pragma warning restore SYSLIB0050

    private static class SettingsTunggu
    {
        public static SuDesApp.ControlSurat.SettingsManager Barua() =>
            (SuDesApp.ControlSurat.SettingsManager)BuatTanpaKonstruktor(typeof(SuDesApp.ControlSurat.SettingsManager));
    }

    private static class WilayahTunggu
    {
        public static SuDesApp.Wilayah.WilayahDataStore Barua() =>
            (SuDesApp.Wilayah.WilayahDataStore)BuatTanpaKonstruktor(typeof(SuDesApp.Wilayah.WilayahDataStore));
    }

    private static class KlienTunggu
    {
        private class Proxy : DispatchProxy
        {
            protected override object? Invoke(MethodInfo? m, object?[]? a) =>
                m!.ReturnType == typeof(Task<SuDesApp.Services.MuatanIdm>)
                    ? Task.FromResult(SuDesApp.Services.MuatanIdm.Galat("uji"))
                    : null;
        }

        public static SuDesApp.Services.IIdmKemendesaKlien Barua() =>
            DispatchProxy.Create<SuDesApp.Services.IIdmKemendesaKlien, Proxy>();
    }
}
