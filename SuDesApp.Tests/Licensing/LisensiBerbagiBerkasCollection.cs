using Xunit;

namespace SuDesApp.Tests.Licensing
{
    /// <summary>
    /// Uji lisensi yang memakai penunjuk folder statis (<c>TrialLokal.FolderOverride</c>,
    /// <c>LisensiManager.FolderCacheOverride</c>) dijalankan berurutan, bukan
    /// paralel: keduanya mengubah satu variabel bersama, dan dua uji yang
    /// berjalan bersamaan bisa saling menimpa folder sementara.
    /// </summary>
    [CollectionDefinition("LisensiBerbagiBerkas", DisableParallelization = true)]
    public sealed class LisensiBerbagiBerkasCollection
    {
    }
}
