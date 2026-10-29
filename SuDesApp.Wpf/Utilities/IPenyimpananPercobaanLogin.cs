using System;

namespace SuDesApp.Wpf.Utilities
{
    /// <summary>
    /// Penyimpanan keadaan pembatas percobaan login. Hanya jumlah gagal dan
    /// waktu gagal terakhir yang disimpan — tidak pernah kata sandi.
    /// </summary>
    internal interface IPenyimpananPercobaanLogin
    {
        (int Gagal, DateTime Terakhir)? Baca();

        void Simpan(int gagal, DateTime terakhir);

        void Hapus();
    }
}
