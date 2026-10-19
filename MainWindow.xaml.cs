using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace WindowsNotch
{
    public partial class MainWindow : Window
    {
        private const double CollapsedWidth = 250;
        private const double CollapsedHeight = 40;
        private const double ExpandedWidth = 255;
        private const double ExpandedHeight = 100;

        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
        private bool _isExpanded;

        public MainWindow()
        {
            InitializeComponent();
            PositionAtTopCenter();
            StartClock();

            // Event hover/klik kanan menempel di host (menangkap seluruh area, termasuk konten)
            NotchHost.MouseEnter += (s, e) => ExpandNotch();
            NotchHost.MouseLeave += (s, e) => CollapseNotch();
            NotchHost.MouseRightButtonDown += (s, e) => Application.Current.Shutdown();

            // Bentuk pill dibangun ulang setiap ukuran berubah (untuk flare atas)
            NotchHost.SizeChanged += (s, e) => UpdateNotchShape(e.NewSize);
            UpdateNotchShape(new Size(Width, Height));
        }

        // Membentuk geometri pill: tepi atas selebar pill dengan flare melengkung
        // turun ke badan (arah keluar kiri-kanan), bawah radius normal (masuk ke tengah)
        private void UpdateNotchShape(Size size)
        {
            double w = size.Width;
            double h = size.Height;
            if (w < 1 || h < 1) return;

            double flare = 6;                    // radius flare atas (keluar)
            double bottomRadius = 18;            // radius bawah (masuk ke tengah)
            bottomRadius = Math.Min(bottomRadius, (w - 2 * flare) / 2);
            bottomRadius = Math.Min(bottomRadius, h / 2);

            var geometry = new StreamGeometry();
            using (StreamGeometryContext ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(0, 0), isFilled: true, isClosed: true);
                ctx.LineTo(new Point(w, 0), true, false);
                // Flare kanan: dari ujung atas kanan -> badan (tangen mulus di titik ujung)
                ctx.ArcTo(new Point(w - flare, flare), new Size(flare, flare), 0, false,
                          SweepDirection.Counterclockwise, true, false);
                ctx.LineTo(new Point(w - flare, h - bottomRadius), true, false);
                // Sudut bawah kanan (masuk ke tengah)
                ctx.ArcTo(new Point(w - flare - bottomRadius, h), new Size(bottomRadius, bottomRadius), 0, false,
                          SweepDirection.Clockwise, true, false);
                ctx.LineTo(new Point(flare + bottomRadius, h), true, false);
                // Sudut bawah kiri (masuk ke tengah)
                ctx.ArcTo(new Point(flare, h - bottomRadius), new Size(bottomRadius, bottomRadius), 0, false,
                          SweepDirection.Clockwise, true, false);
                ctx.LineTo(new Point(flare, flare), true, false);
                // Flare kiri: dari badan -> ujung atas kiri (tangen mulus)
                ctx.ArcTo(new Point(0, 0), new Size(flare, flare), 0, false,
                          SweepDirection.Counterclockwise, true, false);
            }
            geometry.Freeze();
            NotchShape.Data = geometry;
        }

        // Memosisikan jendela tepat di tengah atas layar
        private void PositionAtTopCenter()
        {
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            this.Left = (screenWidth / 2) - (this.Width / 2);
            this.Top = 0;
        }

        // Menjalankan jam real-time (untuk state tanpa media)
        private void StartClock()
        {
            _timer.Tick += (s, e) =>
            {
                ClockText.Text = DateTime.Now.ToString("HH:mm");
            };
            _timer.Start();
        }

        // Saat kursor mouse masuk ke area Notch (Mekar)
        private void ExpandNotch()
        {
            if (_isExpanded) return;
            _isExpanded = true;

            ApplyExpandedState(true);

            this.Width = ExpandedWidth;
            this.Height = ExpandedHeight;
            PositionAtTopCenter();
        }

        // Saat kursor mouse keluar dari area Notch (Mengecil)
        private void CollapseNotch()
        {
            if (!_isExpanded) return;
            _isExpanded = false;

            ApplyExpandedState(false);

            this.Width = CollapsedWidth;
            this.Height = CollapsedHeight;
            PositionAtTopCenter();
        }

        // Menampilkan/menyembunyikan elemen sesuai state
        private void ApplyExpandedState(bool expanded)
        {
            ProgressRow.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            ControlsRow.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            CollapsedPauseButton.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
            SourceAppIcon.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
