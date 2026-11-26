using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SuDesApp.Wpf.Controls;
using Xunit;

namespace SuDesApp.Tests.UI
{
    /// <summary>
    /// Mengunci jebakan <c>RenderTransform</c> yang pernah membuat animasi kartu dan
    /// tombol mati diam-diam: nilai awal WPF bukan null, melainkan MatrixTransform
    /// identity, sehingga penjaga "jangan timpa transform milik XAML" yang ditulis
    /// <c>!= null</c> selalu batal pasang — tanpa galat, tanpa pesan, hanya tampilan datar.
    /// </summary>
    public sealed class AnimasiKartuTombolTests
    {
        private static void JalankanSTA(Action aksi)
        {
            Exception? galat = null;
            var utas = new Thread(() =>
            {
                try { aksi(); }
                catch (Exception ex) { galat = ex; }
            });
            utas.SetApartmentState(ApartmentState.STA);
            utas.Start();
            utas.Join(TimeSpan.FromSeconds(30));
            Assert.Null(galat?.ToString());
            if (galat is not null) throw galat;
        }

        [Fact]
        public void RenderTransform_awal_bukan_null_jadi_jangan_pakai_cek_null()
        {
            JalankanSTA(() =>
            {
                var kartu = new Border { Width = 240, Height = 96 };

                // Inilah sumber bugnya: cek "sudah punya transform?" dengan != null
                // selalu benar pada elemen baru.
                Assert.NotNull(kartu.RenderTransform);
                Assert.True(TransformAman.TanpaTransformSendiri(kartu));

                kartu.RenderTransform = new TranslateTransform(4, 4);
                Assert.False(TransformAman.TanpaTransformSendiri(kartu));
            });
        }

        [Fact]
        public void KartuHalus_memasang_transformnya_sendiri_saat_kursor_masuk()
        {
            JalankanSTA(() =>
            {
                var kartu = new Border { Width = 240, Height = 96 };
                KartuHalus.SetAktif(kartu, true);
                KartuHalus.SetJarakAngkat(kartu, 3d);

                kartu.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0)
                {
                    RoutedEvent = UIElement.MouseEnterEvent
                });

                var grup = Assert.IsType<TransformGroup>(kartu.RenderTransform);
                Assert.IsType<TranslateTransform>(grup.Children[0]);
                Assert.IsType<ScaleTransform>(grup.Children[1]);
            });
        }

        [Fact]
        public void KartuHalus_tidak_menimpa_transform_milik_halaman()
        {
            JalankanSTA(() =>
            {
                var punya = new TranslateTransform();
                var kartu = new Border { Width = 240, Height = 96, RenderTransform = punya };
                KartuHalus.SetAktif(kartu, true);

                kartu.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0)
                {
                    RoutedEvent = UIElement.MouseEnterEvent
                });

                Assert.Same(punya, kartu.RenderTransform);
            });
        }

        [Fact]
        public void Kurva_pegas_mulai_dan_berhenti_tepat_pada_tujuannya()
        {
            // Ujung kurva harus presisi: dengan FillBehavior.HoldEnd nilai terakhir
            // dipegang selamanya, jadi sisa ayunan di t=1 membuat tombol berhenti
            // dalam keadaan sedikit lebih besar dari ukuran penuh.
            var pegas = new PegasEase { EasingMode = EasingMode.EaseOut };

            Assert.Equal(0d, pegas.Ease(0d), 6);
            Assert.Equal(1d, pegas.Ease(1d), 6);

            double puncak = 0d;
            for (int i = 0; i <= 1000; i++) puncak = Math.Max(puncak, pegas.Ease(i / 1000d));

            // Pantulan harus terlihat (>=1,05) tapi tidak seperti kesalahan layout.
            Assert.InRange(puncak, 1.05d, 1.35d);
        }
    }
}
