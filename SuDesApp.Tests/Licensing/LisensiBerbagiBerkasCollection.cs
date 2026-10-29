using Xunit;

namespace SuDesApp.Tests.Licensing
{
    /// <summary>
    /// Uji yang memakai penunjuk folder statis (<c>TrialLokal.FolderOverride</c>,
    /// <c>LisensiManager.FolderCacheOverride</c>, <c>AppPreferenceStore.LokasiOverride</c>,
    /// <c>BerkasLogin.LokasiOverride</c>) dijalankan berurutan, bukan paralel:
    /// semuanya mengubah variabel bersama, dan dua uji yang berjalan bersamaan
    /// bisa saling menimpa folder sementara.
    /// </summary>
    [CollectionDefinition("LisensiBerbagiBerkas", DisableParallelization = true)]
    public sealed class LisensiBerbagiBerkasCollection
    {
    }
}
