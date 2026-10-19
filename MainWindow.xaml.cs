using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace WindowsNotch
{
    public partial class MainWindow : Window
    {
        private const double CollapsedWidth = 250;
        private const double CollapsedHeight = 40;
        private const double ExpandedWidth = 255;
        private const double ExpandedHeight = 100;

        private const int GwlExStyle = -20;
        private const int WsExNoActivate = 0x08000000;
        private const int WsExToolWindow = 0x00000080;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
        private readonly DispatcherTimer _collapseTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
        private readonly DispatcherTimer _mediaTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        private readonly SmtcService _smtc = new();
        private MediaInfo? _media;
        private bool _isSeeking;
        private bool _isExpanded;

        public MainWindow()
        {
            InitializeComponent();
            SetWindowSize(CollapsedWidth, CollapsedHeight);
            StartClock();

            // Hover dengan debounce: cursor yang sempat keluar-masuk tidak memicu getaran
            PillHost.MouseEnter += (s, e) =>
            {
                _collapseTimer.Stop();
                ExpandNotch();
            };
            PillHost.MouseLeave += (s, e) =>
            {
                _collapseTimer.Stop();
                _collapseTimer.Start();
            };
            _collapseTimer.Tick += (s, e) =>
            {
                _collapseTimer.Stop();
                CollapseNotch();
            };
            PillHost.MouseRightButtonDown += (s, e) => Application.Current.Shutdown();

            // Bentuk pill dibangun ulang setiap ukuran berubah (untuk flare atas)
            PillHost.SizeChanged += (s, e) =>
            {
                UpdateNotchShape(e.NewSize);

                // Setelah animasi collapse selesai: window snap ke ukuran pill
                // agar tidak ada area transparan yang menelan klik
                if (!_isExpanded &&
                    e.NewSize.Width <= CollapsedWidth + 0.5 &&
                    e.NewSize.Height <= CollapsedHeight + 0.5)
                {
                    SetWindowSize(CollapsedWidth, CollapsedHeight);
                }
            };
            UpdateNotchShape(new Size(PillHost.Width, PillHost.Height));

            // Jangan pernah merebut fokus aplikasi lain saat tombol diklik,
            // dan sembunyikan dari Alt+Tab
            SourceInitialized += (s, e) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int exStyle = GetWindowLong(hwnd, GwlExStyle);
                SetWindowLong(hwnd, GwlExStyle, exStyle | WsExNoActivate | WsExToolWindow);
            };

            WireMediaControls();

            // Media: tampilkan info lagu saat ada sesi, fallback ke jam bila tidak ada
            _smtc.MediaChanged += OnMediaChanged;
            _mediaTimer.Tick += (s, e) => UpdateProgress();
            _mediaTimer.Start();
            ApplyMedia(null);
            _ = _smtc.InitializeAsync();
        }

        private void WireMediaControls()
        {
            CollapsedPauseButton.Click += async (s, e) => await _smtc.PlayPauseAsync();
            PlayPauseButton.Click += async (s, e) => await _smtc.PlayPauseAsync();
            PrevButton.Click += async (s, e) => await _smtc.PreviousAsync();
            NextButton.Click += async (s, e) => await _smtc.NextAsync();

            // Seek: drag/klik slider, terapkan saat tombol mouse dilepas
            ProgressSlider.PreviewMouseLeftButtonDown += (s, e) => _isSeeking = true;
            ProgressSlider.PreviewMouseLeftButtonUp += (s, e) => CommitSeek();
            ProgressSlider.LostMouseCapture += (s, e) => CommitSeek();
            ProgressSlider.ValueChanged += (s, e) =>
            {
                if (_isSeeking) UpdateTimeLabels(TimeSpan.FromSeconds(ProgressSlider.Value));
            };
        }

        private void CommitSeek()
        {
            if (!_isSeeking) return;
            _isSeeking = false;
            _ = _smtc.SeekAsync(TimeSpan.FromSeconds(ProgressSlider.Value));
        }

        private void OnMediaChanged(MediaInfo? info)
        {
            // Event SMTC datang dari thread lain
            Dispatcher.BeginInvoke(() => ApplyMedia(info));
        }

        private void ApplyMedia(MediaInfo? info)
        {
            _media = info;

            bool hasMedia = info != null;
            MediaRoot.Visibility = hasMedia ? Visibility.Visible : Visibility.Collapsed;
            ClockRoot.Visibility = hasMedia ? Visibility.Collapsed : Visibility.Visible;

            if (info == null) return;

            TrackTitle.Text = info.Title;
            TrackArtist.Text = info.Artist;

            // Album art (fallback ke placeholder ♪)
            if (info.AlbumArt != null)
            {
                AlbumArt.Source = info.AlbumArt;
                AlbumArt.Visibility = Visibility.Visible;
                AlbumArtPlaceholder.Visibility = Visibility.Collapsed;
            }
            else
            {
                AlbumArt.Source = null;
                AlbumArt.Visibility = Visibility.Collapsed;
                AlbumArtPlaceholder.Visibility = Visibility.Visible;
            }

            ApplySourceApp(info.SourceAppId);
            UpdatePlayPauseGlyph(info.IsPlaying);

            ProgressSlider.Maximum = Math.Max(1, info.Duration.TotalSeconds);
            if (!_isSeeking)
            {
                ProgressSlider.Value = Math.Clamp(info.Position.TotalSeconds, 0, ProgressSlider.Maximum);
                UpdateTimeLabels(info.Position);
            }
        }

        // Ikon aplikasi sumber: warna khas per app + glyph not musik
        private void ApplySourceApp(string aumid)
        {
            var id = (aumid ?? string.Empty).ToLowerInvariant();

            Color color =
                id.Contains("spotify") ? Color.FromRgb(0x1D, 0xB9, 0x54) :
                id.Contains("chrome") ? Color.FromRgb(0x42, 0x85, 0xF4) :
                id.Contains("msedge") || id.Contains("edge") ? Color.FromRgb(0x00, 0x78, 0xD4) :
                id.Contains("firefox") ? Color.FromRgb(0xFF, 0x71, 0x39) :
                id.Contains("youtube") ? Color.FromRgb(0xFF, 0x00, 0x00) :
                id.Contains("foobar") ? Color.FromRgb(0x2B, 0x2B, 0x2B) :
                Color.FromRgb(0x5A, 0x5A, 0x5E);

            SourceAppIcon.Background = new SolidColorBrush(color);
        }

        private void UpdatePlayPauseGlyph(bool isPlaying)
        {
            // Pause = dua bar, play = segitiga
            CollapsedPausePath.Data = Geometry.Parse(isPlaying
                ? "M0,0 h4 v13 h-4 z M6,0 h4 v13 h-4 z"
                : "M0,0 L10,6.5 L0,13 z");

            PlayPausePath.Data = Geometry.Parse(isPlaying
                ? "M0,0 h4 v14 h-4 z M8,0 h4 v14 h-4 z"
                : "M0,0 L12,7 L0,14 z");
        }

        private void UpdateProgress()
        {
            if (_media == null || _isSeeking) return;

            var position = _smtc.GetPosition();
            ProgressSlider.Value = Math.Clamp(position.TotalSeconds, 0, ProgressSlider.Maximum);
            UpdateTimeLabels(position);
        }

        // Label kiri "0:11", kanan sisa waktu "-3:22" (ala mockup)
        private void UpdateTimeLabels(TimeSpan position)
        {
            PositionText.Text = FormatTime(position);

            if (_media == null || _media.Duration <= TimeSpan.Zero)
            {
                DurationText.Text = "--:--";
                return;
            }

            var remaining = _media.Duration - position;
            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
            DurationText.Text = "-" + FormatTime(remaining);
        }

        private static string FormatTime(TimeSpan time)
        {
            if (time < TimeSpan.Zero) time = TimeSpan.Zero;
            return time.TotalHours >= 1
                ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
                : $"{time.Minutes}:{time.Seconds:00}";
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

            // Window langsung snap ke ukuran akhir (transparan, tak terlihat),
            // lalu pill-lah yang dianimasikan -> tanpa resize OS per frame
            SetWindowSize(ExpandedWidth, ExpandedHeight);
            AnimatePillTo(ExpandedWidth, ExpandedHeight);
        }

        // Saat kursor mouse keluar dari area Notch (Mengecil)
        private void CollapseNotch()
        {
            if (!_isExpanded) return;
            _isExpanded = false;

            ApplyExpandedState(false);

            // Window baru menyusul setelah animasi pill selesai (lihat handler SizeChanged)
            AnimatePillTo(CollapsedWidth, CollapsedHeight);
        }

        // Snap instan ukuran window (dipakai saat expand & setelah collapse selesai)
        private void SetWindowSize(double targetWidth, double targetHeight)
        {
            Width = targetWidth;
            Height = targetHeight;
            Left = (SystemParameters.PrimaryScreenWidth - targetWidth) / 2;
            Top = 0;
        }

        // Animasi halus: lebar melebar sedikit (250->255) dan tinggi bertambah.
        // Lebar teks dipatok di XAML agar konten tidak re-layout horizontal (anti getar)
        private void AnimatePillTo(double targetWidth, double targetHeight)
        {
            var easing = new QuinticEase { EasingMode = EasingMode.EaseOut };
            var duration = new Duration(TimeSpan.FromSeconds(0.32));

            var widthAnim = new DoubleAnimation(targetWidth, duration) { EasingFunction = easing };
            var heightAnim = new DoubleAnimation(targetHeight, duration) { EasingFunction = easing };

            // SnapshotAndReplace: hover cepat bolak-balik tetap mulus dari posisi sekarang
            PillHost.BeginAnimation(FrameworkElement.WidthProperty, widthAnim);
            PillHost.BeginAnimation(FrameworkElement.HeightProperty, heightAnim);
        }

        // Menampilkan/menyembunyikan elemen sesuai state
        private void ApplyExpandedState(bool expanded)
        {
            ProgressRow.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            ControlsRow.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            CollapsedPauseButton.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
            SourceAppIcon.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        }

        protected override void OnClosed(EventArgs e)
        {
            _mediaTimer.Stop();
            _smtc.Dispose();
            base.OnClosed(e);
        }
    }
}
