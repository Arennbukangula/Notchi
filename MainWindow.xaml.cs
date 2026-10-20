using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
        private const double ExpandedWidth = 300;
        private const double ExpandedHeight = 100;
        private const double IdleExpandedHorizontalMargin = 8;
        private const double IdleExpandedTopMargin = 8;

        private const double SideCollapsedWidth = 40;
        private const double SideCollapsedHeight = 107;
        private const double SideExpandedWidth = 300;
        private const double SideExpandedHeight = 107;
        private const double SideCornerRadius = 20;

        private const double FeatureButtonWidth = 30;
        private const double FeatureButtonTop = 5;
        private const double FeatureGap = 8;
        private const double FeatureDecorationWidth = FeatureGap + FeatureButtonWidth;
        private const double FeaturePanelOpenOffset = 8;
        private const double FeaturePanelStartOffset = -6;
        private const long FeaturePanelToggleGuardMs = 250;

        private static readonly Duration PillPhaseDuration = new(TimeSpan.FromSeconds(0.16));

        private const int GwlExStyle = -20;
        private const int WsExNoActivate = 0x08000000;
        private const int WsExToolWindow = 0x00000080;

        private const uint MonitorDefaultToNearest = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public NativeRect Monitor;
            public NativeRect Work;
            public uint Flags;
        }

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out NativePoint point);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Notchi", "settings.json");

        private static readonly SolidColorBrush AccentBrush = FrozenBrush(0x3D, 0x7E, 0xFF);
        private static readonly SolidColorBrush WarningBrush = FrozenBrush(0xFF, 0xB0, 0x20);
        private static readonly SolidColorBrush DangerBrush = FrozenBrush(0xFF, 0x45, 0x3A);
        private static readonly SolidColorBrush BatteryBrush = FrozenBrush(0x4C, 0xD9, 0x64);

        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
        private readonly DispatcherTimer _collapseTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
        private readonly DispatcherTimer _mediaTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        private readonly SmtcService _smtc = new();
        private readonly SystemStatusService _systemStatus = new();
        private Settings _settings = new();
        private MediaInfo? _media;
        private bool _isSeeking;
        private bool _isExpanded;
        private bool _isDragging;
        private bool _dragMoved;
        private bool _featurePanelOpen;
        private long _featurePanelClosedAt;
        private bool _keepNotchOpenForNote;
        private NoteWindow? _noteWindow;
        private int _animationVersion;
        private Point _dragStartCursor;
        private Point _dragGrab;
        private NotchAnchor _anchor = NotchAnchor.TopCenter;
        private Rect _monitor;

        private enum NotchAnchor
        {
            TopCenter,
            LeftCenter,
            RightCenter
        }

        private sealed class Settings
        {
            public string? Anchor { get; set; }
            public bool ShowCpu { get; set; } = true;
            public bool ShowRam { get; set; } = true;
            public bool ShowDisk { get; set; } = true;
            public bool ShowBattery { get; set; } = true;
        }

        private bool IsSideAnchor => _anchor is NotchAnchor.LeftCenter or NotchAnchor.RightCenter;
        private bool IsNoteOpen => _noteWindow is { IsVisible: true };

        private (double Width, double Height) CollapsedSize => IsSideAnchor
            ? (SideCollapsedWidth, SideCollapsedHeight)
            : (CollapsedWidth, CollapsedHeight);

        private (double Width, double Height) ExpandedSize => IsSideAnchor
            ? (SideExpandedWidth, SideExpandedHeight)
            : (ExpandedWidth, ExpandedHeight);

        public MainWindow()
        {
            InitializeComponent();

            _settings = LoadSettings();
            _anchor = Enum.TryParse<NotchAnchor>(_settings.Anchor, out var anchor)
                ? anchor
                : NotchAnchor.TopCenter;
            _monitor = SystemParameters.WorkArea;
            SetAnchor(_anchor);

            Loaded += (s, e) =>
            {
                // DPI baru akurat setelah window ter-render
                UpdateMonitorFromWindow();
                var (w, h) = _isExpanded ? ExpandedSize : CollapsedSize;
                SetWindowSize(w, h);
            };
            StartClock();
            ApplySettingsVisibility();

            // Hover dengan debounce: cursor yang sempat keluar-masuk tidak memicu getaran
            PillHost.MouseEnter += (s, e) =>
            {
                if (_isDragging || _featurePanelOpen || IsNoteOpen) return;
                _collapseTimer.Stop();
                ExpandNotch();
            };
            PillHost.MouseLeave += (s, e) =>
            {
                if (_isDragging || _featurePanelOpen || IsNoteOpen) return;
                _collapseTimer.Stop();
                _collapseTimer.Start();
            };
            _collapseTimer.Tick += (s, e) =>
            {
                _collapseTimer.Stop();
                if (!_isDragging && !_featurePanelOpen && !IsNoteOpen) CollapseNotch();
            };
            PillHost.MouseRightButtonDown += (s, e) => Application.Current.Shutdown();

            // Kapsul fitur di sisi kanan notch
            FeatureButton.MouseEnter += (s, e) => _collapseTimer.Stop();
            FeatureButton.MouseLeave += (s, e) =>
            {
                if (_isDragging || _featurePanelOpen || IsNoteOpen) return;
                _collapseTimer.Stop();
                _collapseTimer.Start();
            };
            FeatureButton.Click += OnFeatureButtonClick;
            FeaturePopup.Opened += OnFeaturePopupOpened;
            FeaturePopup.Closed += OnFeaturePopupClosed;
            NoteButton.Click += OnNoteButtonClick;
            ExitButton.Click += (s, e) => Application.Current.Shutdown();

            // Drag & drop: geser notch ke salah satu titik snap
            PillHost.PreviewMouseLeftButtonDown += OnPillMouseDown;
            PillHost.PreviewMouseMove += OnPillMouseMove;
            PillHost.PreviewMouseLeftButtonUp += OnPillMouseUp;
            PillHost.LostMouseCapture += (s, e) =>
            {
                // Jangan biarkan state drag menggantung bila capture hilang
                if (!_isDragging) return;
                _isDragging = false;
                PillHost.Cursor = Cursors.Hand;
                UpdateFeatureButtonVisibility();
                UpdateNotchShape(PillHost.RenderSize);
            };

            // Bentuk pill dibangun ulang setiap ukuran berubah
            PillHost.SizeChanged += (s, e) =>
            {
                UpdateNotchShape(e.NewSize);
                UpdateFeatureButtonPosition(e.NewSize.Width);

                // Setelah animasi collapse selesai: window snap ke ukuran pill
                // agar tidak ada area transparan yang menelan klik
                var (cw, ch) = CollapsedSize;
                if (!_isDragging &&
                    !_isExpanded &&
                    e.NewSize.Width <= cw + 0.5 &&
                    e.NewSize.Height <= ch + 0.5)
                {
                    SetWindowSize(cw, ch);
                    UpdateContentMargin();
                }
            };

            // Jangan pernah merebut fokus aplikasi lain saat tombol diklik,
            // dan sembunyikan dari Alt+Tab
            SourceInitialized += (s, e) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int exStyle = GetWindowLong(hwnd, GwlExStyle);
                SetWindowLong(hwnd, GwlExStyle, exStyle | WsExNoActivate | WsExToolWindow);
            };

            WireMediaControls();

            // Media: tampilkan info lagu saat ada sesi, fallback ke idle dashboard bila tidak ada
            _smtc.MediaChanged += OnMediaChanged;
            _systemStatus.StatusChanged += OnSystemStatusChanged;
            _mediaTimer.Tick += (s, e) => UpdateProgress();
            _mediaTimer.Start();
            ApplyMedia(null);
            _ = _smtc.InitializeAsync();
        }

        private void WireMediaControls()
        {
            CollapsedPauseButton.Click += async (s, e) => await _smtc.PlayPauseAsync();
            SidePauseButton.Click += async (s, e) => await _smtc.PlayPauseAsync();
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

        // ===================== Panel Fitur =====================

        private void OnFeatureButtonClick(object sender, RoutedEventArgs e)
        {
            if (FeaturePopup.IsOpen)
            {
                FeaturePopup.IsOpen = false;
                return;
            }

            // StaysOpen=false menutup popup saat klik di luar, termasuk saat
            // mengklik ikon ini; jangan biarkan klik yang sama membukanya ulang.
            if (Environment.TickCount64 - _featurePanelClosedAt < FeaturePanelToggleGuardMs)
            {
                return;
            }

            OpenFeaturePanel();
        }

        private void OpenFeaturePanel()
        {
            if (IsSideAnchor || _featurePanelOpen) return;

            _featurePanelOpen = true;
            _collapseTimer.Stop();

            FeaturePopup.BeginAnimation(Popup.HorizontalOffsetProperty, null);
            FeaturePopup.HorizontalOffset = FeaturePanelStartOffset;
            FeaturePanelCard.BeginAnimation(UIElement.OpacityProperty, null);
            FeaturePanelCard.Opacity = 0;

            FeaturePopup.IsOpen = true;
        }

        private void OnFeaturePopupOpened(object? sender, EventArgs e)
        {
            var easing = new QuinticEase { EasingMode = EasingMode.EaseOut };
            var duration = new Duration(TimeSpan.FromSeconds(0.2));

            // Slide dari balik ikon ke kanan + fade-in
            FeaturePopup.BeginAnimation(Popup.HorizontalOffsetProperty,
                new DoubleAnimation(FeaturePanelOpenOffset, duration) { EasingFunction = easing });
            FeaturePanelCard.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(1, duration) { EasingFunction = easing });

            // Panel tidak boleh merebut fokus aplikasi lain
            if (PresentationSource.FromVisual(FeaturePanelCard) is HwndSource source)
            {
                int exStyle = GetWindowLong(source.Handle, GwlExStyle);
                SetWindowLong(source.Handle, GwlExStyle, exStyle | WsExNoActivate | WsExToolWindow);
            }
        }

        private void OnFeaturePopupClosed(object? sender, EventArgs e)
        {
            _featurePanelOpen = false;
            _featurePanelClosedAt = Environment.TickCount64;

            FeaturePopup.BeginAnimation(Popup.HorizontalOffsetProperty, null);
            FeaturePopup.HorizontalOffset = FeaturePanelOpenOffset;
            FeaturePanelCard.BeginAnimation(UIElement.OpacityProperty, null);
            FeaturePanelCard.Opacity = 1;

            if (_keepNotchOpenForNote)
            {
                _keepNotchOpenForNote = false;
                return;
            }

            if (IsNoteOpen) return;
            if (_isDragging) return;
            if (PillHost.IsMouseOver || FeatureButton.IsMouseOver) return;

            _collapseTimer.Stop();
            CollapseNotch();
        }

        private void OnNoteButtonClick(object sender, RoutedEventArgs e)
        {
            _keepNotchOpenForNote = true;
            FeaturePopup.IsOpen = false;
            ShowNoteWindow();
        }

        private void ShowNoteWindow()
        {
            if (_noteWindow is { IsVisible: true })
            {
                _noteWindow.Activate();
                return;
            }

            var note = new NoteWindow();
            PositionNoteWindow(note);

            note.Closed += (s, e) =>
            {
                _noteWindow = null;

                // Tombol X / Esc hanya menutup catatan dan kembali ke menu fitur,
                // bukan ikut menutup atau men-collapse notch.
                if (note.ClosedByUser && !IsSideAnchor)
                {
                    OpenFeaturePanel();
                    return;
                }

                if (PillHost.IsMouseOver || FeatureButton.IsMouseOver) return;

                _collapseTimer.Stop();
                CollapseNotch();
            };

            _noteWindow = note;
            note.Show();
            note.Activate();
        }

        private void PositionNoteWindow(Window note)
        {
            const double gap = 8;

            double left = Left + FeatureButton.Margin.Left + FeatureButton.ActualWidth + gap;
            double top = Top + FeatureButton.Margin.Top;

            var work = _monitor;
            if (work.Width < 1 || work.Height < 1)
            {
                work = new Rect(0, 0,
                    SystemParameters.PrimaryScreenWidth,
                    SystemParameters.PrimaryScreenHeight);
            }

            // Kalau ruang di kanan tidak cukup, buka di kiri ikon.
            if (left + note.Width > work.Right - gap)
            {
                left = Left + FeatureButton.Margin.Left - note.Width - gap;
            }

            left = Math.Clamp(left,
                work.Left + gap,
                Math.Max(work.Left + gap, work.Right - note.Width - gap));
            top = Math.Clamp(top,
                work.Top + gap,
                Math.Max(work.Top + gap, work.Bottom - note.Height - gap));

            note.Left = left;
            note.Top = top;
        }

        private void OnMediaChanged(MediaInfo? info)
        {
            // Event SMTC datang dari thread lain
            Dispatcher.BeginInvoke(() => ApplyMedia(info));
        }

        private void OnSystemStatusChanged(SystemStatusSnapshot status)
        {
            // Sampling berjalan di thread pool; update UI di dispatcher
            Dispatcher.BeginInvoke(() => ApplySystemStatus(status));
        }

        private void ApplySystemStatus(SystemStatusSnapshot status)
        {
            if (_media != null) return;

            SetMetric(ExpandedCpuBar, ExpandedCpuText, status.CpuPercent);
            SetMetric(ExpandedRamBar, ExpandedRamText, status.RamPercent);
            SetMetric(ExpandedDiskBar, ExpandedDiskText, status.DiskPercent);

            ExpandedDiskLabel.Text = status.DiskLabel;

            ApplyBattery(status.BatteryPercent, status.IsCharging,
                ExpandedBatteryPanel, ExpandedBatteryFill, ExpandedBatteryText, 16);
        }

        private void ApplyMedia(MediaInfo? info)
        {
            _media = info;

            if (info == null)
            {
                _systemStatus.Start();
                UpdateVisualState();
                return;
            }

            _systemStatus.Stop();

            TrackTitle.Text = info.Title;
            TrackArtist.Text = info.Artist;
            SetAlbumArt(info.AlbumArt);

            ApplySourceApp(info.SourceAppId);
            UpdatePlayPauseGlyph(info.IsPlaying);

            UpdateVisualState();

            ProgressSlider.Maximum = Math.Max(1, info.Duration.TotalSeconds);
            if (!_isSeeking)
            {
                ProgressSlider.Value = Math.Clamp(info.Position.TotalSeconds, 0, ProgressSlider.Maximum);
                UpdateTimeLabels(info.Position);
            }
        }

        private void SetAlbumArt(ImageSource? art)
        {
            if (art != null)
            {
                AlbumArt.Source = art;
                AlbumArt.Visibility = Visibility.Visible;
                AlbumArtPlaceholder.Visibility = Visibility.Collapsed;

                SideAlbumArt.Source = art;
                SideAlbumArt.Visibility = Visibility.Visible;
                SideAlbumArtPlaceholder.Visibility = Visibility.Collapsed;
            }
            else
            {
                AlbumArt.Source = null;
                AlbumArt.Visibility = Visibility.Collapsed;
                AlbumArtPlaceholder.Visibility = Visibility.Visible;

                SideAlbumArt.Source = null;
                SideAlbumArt.Visibility = Visibility.Collapsed;
                SideAlbumArtPlaceholder.Visibility = Visibility.Visible;
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
            string collapsed = isPlaying
                ? "M0,0 h4 v13 h-4 z M6,0 h4 v13 h-4 z"
                : "M0,0 L10,6.5 L0,13 z";
            string expanded = isPlaying
                ? "M0,0 h4 v14 h-4 z M8,0 h4 v14 h-4 z"
                : "M0,0 L12,7 L0,14 z";

            CollapsedPausePath.Data = Geometry.Parse(collapsed);
            SidePausePath.Data = Geometry.Parse(collapsed);
            PlayPausePath.Data = Geometry.Parse(expanded);
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

        // ===================== Posisi & Bentuk =====================

        // Membentuk geometri pill. Mode atas: flare keluar di tepi atas.
        // Mode sisi: sudut membulat di sisi dalam layar, sisi menempel layar tetap rata.
        // Saat digeser: selalu berbentuk kapsul dengan radius penuh agar terlihat rapi.
        private void UpdateNotchShape(Size size)
        {
            double w = size.Width;
            double h = size.Height;
            if (w < 1 || h < 1) return;

            if (_isDragging)
            {
                double radius = Math.Min(w, h) / 2;
                var capsule = new RectangleGeometry(new Rect(0, 0, w, h), radius, radius);
                capsule.Freeze();
                NotchShape.Data = capsule;
                return;
            }

            var geometry = new StreamGeometry();
            using (StreamGeometryContext ctx = geometry.Open())
            {
                if (IsSideAnchor)
                {
                    double r = Math.Min(SideCornerRadius, Math.Min(w, h) / 2);
                    ctx.BeginFigure(new Point(0, 0), isFilled: true, isClosed: true);

                    if (_anchor == NotchAnchor.LeftCenter)
                    {
                        // Menempel kiri: sudut kanan membulat
                        ctx.LineTo(new Point(w - r, 0), true, false);
                        ctx.ArcTo(new Point(w, r), new Size(r, r), 0, false,
                                  SweepDirection.Clockwise, true, false);
                        ctx.LineTo(new Point(w, h - r), true, false);
                        ctx.ArcTo(new Point(w - r, h), new Size(r, r), 0, false,
                                  SweepDirection.Clockwise, true, false);
                        ctx.LineTo(new Point(0, h), true, false);
                    }
                    else
                    {
                        // Menempel kanan: sudut kiri membulat
                        ctx.LineTo(new Point(w, 0), true, false);
                        ctx.LineTo(new Point(w, h), true, false);
                        ctx.LineTo(new Point(r, h), true, false);
                        ctx.ArcTo(new Point(0, h - r), new Size(r, r), 0, false,
                                  SweepDirection.Clockwise, true, false);
                        ctx.LineTo(new Point(0, r), true, false);
                        ctx.ArcTo(new Point(r, 0), new Size(r, r), 0, false,
                                  SweepDirection.Clockwise, true, false);
                    }
                }
                else
                {
                    double flare = 6;                    // radius flare atas (keluar)
                    double bottomRadius = 18;            // radius bawah (masuk ke tengah)
                    bottomRadius = Math.Min(bottomRadius, (w - 2 * flare) / 2);
                    bottomRadius = Math.Min(bottomRadius, h / 2);

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
            }
            geometry.Freeze();
            NotchShape.Data = geometry;
        }

        private void SetAnchor(NotchAnchor anchor)
        {
            _anchor = anchor;

            if (FeaturePopup.IsOpen) FeaturePopup.IsOpen = false;

            // Hentikan animasi yang mungkin masih berjalan sebelum mengubah ukuran dasar
            _animationVersion++;
            PillHost.BeginAnimation(FrameworkElement.WidthProperty, null);
            PillHost.BeginAnimation(FrameworkElement.HeightProperty, null);

            var (w, h) = _isExpanded ? ExpandedSize : CollapsedSize;

            PillHost.HorizontalAlignment = anchor switch
            {
                NotchAnchor.LeftCenter => HorizontalAlignment.Left,
                NotchAnchor.RightCenter => HorizontalAlignment.Right,
                _ => HorizontalAlignment.Center
            };
            PillHost.VerticalAlignment = IsSideAnchor
                ? VerticalAlignment.Center
                : VerticalAlignment.Top;

            PillHost.Width = w;
            PillHost.Height = h;

            UpdateContentMargin();
            SetWindowSize(w, h);
            UpdateFeatureButtonPosition(w);
            UpdateNotchShape(new Size(w, h));
            UpdateVisualState();
        }

        private void UpdateContentMargin()
        {
            ContentRoot.Margin = IsSideAnchor && !_isExpanded
                ? new Thickness(0)
                : new Thickness(16, 0, 16, 0);
        }

        // Kapsul ikon hanya tampil pada mode atas-tengah dan tidak saat digeser
        private void UpdateFeatureButtonVisibility()
        {
            bool visible = !IsSideAnchor && !_isDragging;
            FeatureButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

            if (!visible && FeaturePopup.IsOpen)
            {
                FeaturePopup.IsOpen = false;
            }
        }

        // Tempelkan kapsul tepat di kanan tepi pill (pill selalu di tengah window)
        private void UpdateFeatureButtonPosition(double pillWidth)
        {
            double left = (Width + pillWidth) / 2 + FeatureGap;
            FeatureButton.Margin = new Thickness(left, FeatureButtonTop, 0, 0);
        }

        // Snap instan ukuran window (dipakai saat expand & setelah collapse selesai).
        // Mode atas-tengah menyisakan ruang di kanan untuk kapsul ikon; pill tetap
        // di tengah layar karena window juga di-center.
        private void SetWindowSize(double targetWidth, double targetHeight)
        {
            double windowWidth = targetWidth + (IsSideAnchor ? 0 : 2 * FeatureDecorationWidth);

            Width = windowWidth;
            Height = targetHeight;

            var work = _monitor;
            if (work.Width < 1 || work.Height < 1)
            {
                work = new Rect(0, 0,
                    SystemParameters.PrimaryScreenWidth,
                    SystemParameters.PrimaryScreenHeight);
            }

            switch (_anchor)
            {
                case NotchAnchor.LeftCenter:
                    Left = work.Left;
                    Top = work.Top + (work.Height - targetHeight) / 2;
                    break;
                case NotchAnchor.RightCenter:
                    Left = work.Right - windowWidth;
                    Top = work.Top + (work.Height - targetHeight) / 2;
                    break;
                default:
                    Left = work.Left + (work.Width - windowWidth) / 2;
                    Top = work.Top;
                    break;
            }

            UpdateFeatureButtonPosition(targetWidth);
        }

        // Area kerja monitor (dalam DIP) tempat notch berada
        private void UpdateMonitorFromWindow()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
                UpdateMonitor(monitor);
            }
            catch
            {
                // Pakai monitor utama bila query gagal
            }
        }

        private void UpdateMonitorFromCursor()
        {
            try
            {
                if (GetCursorPos(out var point))
                {
                    UpdateMonitor(MonitorFromPoint(point, MonitorDefaultToNearest));
                }
            }
            catch
            {
                // Pakai monitor utama bila query gagal
            }
        }

        private void UpdateMonitor(IntPtr monitor)
        {
            if (monitor == IntPtr.Zero) return;

            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info)) return;

            // GetMonitorInfo mengembalikan piksel fisik; WPF memakai DIP.
            // DPI diambil dari window; fallback ke VisualTree setelah render.
            double scaleX = 1, scaleY = 1;
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                uint dpi = GetDpiForWindow(hwnd);
                if (dpi > 0)
                {
                    scaleX = dpi / 96.0;
                    scaleY = scaleX;
                }
            }

            if (scaleX <= 0 || scaleX == 1)
            {
                var visual = VisualTreeHelper.GetDpi(this);
                if (visual.DpiScaleX > 0) scaleX = visual.DpiScaleX;
                if (visual.DpiScaleY > 0) scaleY = visual.DpiScaleY;
            }

            if (scaleX <= 0) scaleX = 1;
            if (scaleY <= 0) scaleY = 1;

            _monitor = new Rect(
                info.Work.Left / scaleX,
                info.Work.Top / scaleY,
                (info.Work.Right - info.Work.Left) / scaleX,
                (info.Work.Bottom - info.Work.Top) / scaleY);
        }

        // ===================== Drag & Drop =====================

        private void OnPillMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (IsInteractiveElement(e.OriginalSource as DependencyObject)) return;
            if (!GetCursorPos(out var cursor)) return;

            var dpi = VisualTreeHelper.GetDpi(this);
            double scaleX = dpi.DpiScaleX <= 0 ? 1 : dpi.DpiScaleX;
            double scaleY = dpi.DpiScaleY <= 0 ? 1 : dpi.DpiScaleY;

            _dragStartCursor = new Point(cursor.X, cursor.Y);
            // Offset kursor terhadap tepi kiri pill (bukan window) agar notch tidak
            // melompat saat window menyusut ke ukuran drag tanpa ruang ikon.
            double pillOffsetX = Math.Max(0, (ActualWidth - PillHost.ActualWidth) / 2);
            _dragGrab = new Point(cursor.X / scaleX - Left - pillOffsetX, cursor.Y / scaleY - Top);
            _dragMoved = false;
            _isDragging = true;
            _collapseTimer.Stop();
            PillHost.CaptureMouse();
        }

        private void OnPillMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || e.LeftButton != MouseButtonState.Pressed) return;
            if (!GetCursorPos(out var cursor)) return;

            var dpi = VisualTreeHelper.GetDpi(this);
            double scaleX = dpi.DpiScaleX <= 0 ? 1 : dpi.DpiScaleX;
            double scaleY = dpi.DpiScaleY <= 0 ? 1 : dpi.DpiScaleY;

            double dx = (cursor.X - _dragStartCursor.X) / scaleX;
            double dy = (cursor.Y - _dragStartCursor.Y) / scaleY;

            if (!_dragMoved && Math.Abs(dx) < 4 && Math.Abs(dy) < 4) return;

            if (!_dragMoved)
            {
                _dragMoved = true;
                UpdateMonitorFromCursor();
                BeginDragVisual(cursor, scaleX, scaleY);
            }
            else
            {
                // Ikuti monitor tempat kursor berada (multi-monitor)
                UpdateMonitorFromCursor();
            }

            // Bebas mengikuti kursor; snap ke anchor baru terjadi saat mouse dilepas
            Left = cursor.X / scaleX - _dragGrab.X;
            Top = cursor.Y / scaleY - _dragGrab.Y;
            PillHost.Cursor = Cursors.SizeAll;
        }

        // Saat mulai digeser: ciutkan notch ke ukuran collapsed dan bentuk kapsul rapi
        private void BeginDragVisual(NativePoint cursor, double scaleX, double scaleY)
        {
            if (_isExpanded)
            {
                _isExpanded = false;
                ApplyExpandedState(false);
            }

            _animationVersion++;
            PillHost.BeginAnimation(FrameworkElement.WidthProperty, null);
            PillHost.BeginAnimation(FrameworkElement.HeightProperty, null);

            var (cw, ch) = CollapsedSize;
            double grabX = cursor.X / scaleX - Left;
            double grabY = cursor.Y / scaleY - Top;

            PillHost.Width = cw;
            PillHost.Height = ch;
            Width = cw;
            Height = ch;
            UpdateFeatureButtonVisibility();

            // Jaga posisi notch tetap di bawah kursor setelah ukuran berubah
            grabX = Math.Clamp(grabX, 0, cw);
            grabY = Math.Clamp(grabY, 0, ch);
            _dragGrab = new Point(grabX, grabY);
            Left = cursor.X / scaleX - grabX;
            Top = cursor.Y / scaleY - grabY;

            UpdateNotchShape(new Size(cw, ch));
        }

        private void OnPillMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging) return;

            _isDragging = false;
            PillHost.ReleaseMouseCapture();
            PillHost.Cursor = Cursors.Hand;

            if (!_dragMoved)
            {
                // Klik tanpa geser: kembalikan bentuk sesuai anchor
                UpdateNotchShape(PillHost.RenderSize);
                return;
            }

            // Simpan pusat pill saat ini sebelum ukuran diubah
            double centerX = Left + Width / 2;
            double centerY = Top + Height / 2;

            // Setelah drag, kembali ke ukuran collapsed lalu snap ke titik terdekat
            if (_isExpanded)
            {
                _isExpanded = false;
                ApplyExpandedState(false);
            }

            _animationVersion++;
            PillHost.BeginAnimation(FrameworkElement.WidthProperty, null);
            PillHost.BeginAnimation(FrameworkElement.HeightProperty, null);

            var (cw, ch) = CollapsedSize;
            PillHost.Width = cw;
            PillHost.Height = ch;
            Width = cw;
            Height = ch;

            SnapToNearestAnchor(centerX, centerY);
        }

        private void SnapToNearestAnchor(double centerX, double centerY)
        {
            var work = _monitor;
            if (work.Width < 1 || work.Height < 1)
            {
                work = new Rect(0, 0,
                    SystemParameters.PrimaryScreenWidth,
                    SystemParameters.PrimaryScreenHeight);
            }

            // Hanya tiga anchor: tengah atas, kiri tengah, kanan tengah
            var candidates = new (NotchAnchor Anchor, double Left, double Top, double Width, double Height)[]
            {
                (NotchAnchor.TopCenter, work.Left + (work.Width - CollapsedWidth) / 2, work.Top,
                    CollapsedWidth, CollapsedHeight),
                (NotchAnchor.LeftCenter, work.Left, work.Top + (work.Height - SideCollapsedHeight) / 2,
                    SideCollapsedWidth, SideCollapsedHeight),
                (NotchAnchor.RightCenter, work.Right - SideCollapsedWidth,
                    work.Top + (work.Height - SideCollapsedHeight) / 2,
                    SideCollapsedWidth, SideCollapsedHeight)
            };

            // Bandingkan pusat pill dengan pusat tiap titik snap
            var best = candidates
                .OrderBy(c =>
                {
                    double cx = c.Left + c.Width / 2;
                    double cy = c.Top + c.Height / 2;
                    return (cx - centerX) * (cx - centerX) + (cy - centerY) * (cy - centerY);
                })
                .First();

            SetAnchor(best.Anchor);
            SaveAnchor();
        }

        private static bool IsInteractiveElement(DependencyObject? source)
        {
            while (source != null)
            {
                if (source is ButtonBase || source is Slider || source is Thumb)
                {
                    return true;
                }
                source = VisualTreeHelper.GetParent(source);
            }
            return false;
        }

        private static Settings LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return new Settings();

                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath))
                       ?? new Settings();
            }
            catch
            {
                return new Settings();
            }
        }

        private void SaveAnchor()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                _settings.Anchor = _anchor.ToString();
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(_settings));
            }
            catch
            {
                // Preferensi posisi bersifat opsional
            }
        }

        // ===================== State =====================

        // Menampilkan/menyembunyikan elemen sesuai state (expanded/collapsed)
        private void ApplyExpandedState(bool expanded)
        {
            ProgressRow.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            ControlsRow.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            CollapsedPauseButton.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
            SourceAppIcon.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            UpdateContentMargin();
            UpdateVisualState();
        }

        // Menentukan layout mana yang tampil: idle dashboard, media horizontal, atau mini vertikal di sisi
        private void UpdateVisualState()
        {
            UpdateFeatureButtonVisibility();

            bool hasMedia = _media != null;
            bool sideCollapsed = IsSideAnchor && !_isExpanded;

            SideCollapsedRoot.Visibility = hasMedia && sideCollapsed ? Visibility.Visible : Visibility.Collapsed;
            SideIdleRoot.Visibility = !hasMedia && sideCollapsed ? Visibility.Visible : Visibility.Collapsed;
            MediaRoot.Visibility = hasMedia && !sideCollapsed ? Visibility.Visible : Visibility.Collapsed;
            IdleRoot.Visibility = !hasMedia && !sideCollapsed ? Visibility.Visible : Visibility.Collapsed;

            // Mode sisi yang mekar tetap horizontal, tapi konten ditengahkan vertikal
            MediaRoot.VerticalAlignment = IsSideAnchor ? VerticalAlignment.Center : VerticalAlignment.Top;
            MediaRoot.Margin = IsSideAnchor ? new Thickness(0) : new Thickness(0, 6.5, 0, 0);

            if (!hasMedia && !sideCollapsed)
            {
                IdleCompactRoot.Visibility = _isExpanded ? Visibility.Collapsed : Visibility.Visible;
                IdleExpandedRoot.Visibility = _isExpanded ? Visibility.Visible : Visibility.Collapsed;
                IdleExpandedRoot.Margin = IsSideAnchor
                    ? new Thickness(IdleExpandedHorizontalMargin, 0, IdleExpandedHorizontalMargin, 0)
                    : new Thickness(IdleExpandedHorizontalMargin, IdleExpandedTopMargin,
                        IdleExpandedHorizontalMargin, 0);
            }

            if (!sideCollapsed) return;

            SideAlbumArtBorder.Visibility = hasMedia ? Visibility.Visible : Visibility.Collapsed;
            SidePauseButton.Visibility = hasMedia ? Visibility.Visible : Visibility.Collapsed;
        }

        // Menjalankan jam dan tanggal real-time (untuk state tanpa media)
        private void StartClock()
        {
            UpdateClock();
            _timer.Tick += (s, e) => UpdateClock();
            _timer.Start();
        }

        private void UpdateClock()
        {
            var now = DateTime.Now;
            string time = now.ToString("HH:mm", CultureInfo.CurrentCulture);

            CompactClockText.Text = time;
            ExpandedClockText.Text = time;
            SideHourText.Text = now.ToString("HH", CultureInfo.CurrentCulture);
            SideMinuteText.Text = now.ToString("mm", CultureInfo.CurrentCulture);

            CompactDateText.Text = now.ToString("ddd, d MMM", CultureInfo.CurrentCulture);
            ExpandedDateText.Text = now.ToString("dddd, d MMM yyyy", CultureInfo.CurrentCulture);
            SideDayText.Text = now.ToString("ddd", CultureInfo.CurrentCulture);
            SideDateText.Text = now.ToString("d MMM", CultureInfo.CurrentCulture);
        }

        private static SolidColorBrush FrozenBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        private static void SetMetric(ProgressBar bar, TextBlock text, double? value)
        {
            if (value is not double v)
            {
                bar.Value = 0;
                text.Text = "--%";
                return;
            }

            v = Math.Clamp(v, 0, 100);
            bar.Value = v;
            text.Text = $"{Math.Round(v):0}%";
            bar.Foreground = v >= 90 ? DangerBrush : v >= 75 ? WarningBrush : AccentBrush;
        }

        private void ApplyBattery(
            int? percent,
            bool isCharging,
            FrameworkElement panel,
            Border fill,
            TextBlock text,
            double innerWidth)
        {
            if (!_settings.ShowBattery || percent is not int p)
            {
                panel.Visibility = Visibility.Collapsed;
                return;
            }

            p = Math.Clamp(p, 0, 100);
            panel.Visibility = Visibility.Visible;
            fill.Width = innerWidth * p / 100.0;
            fill.Background = isCharging
                ? AccentBrush
                : p <= 20 ? DangerBrush : p <= 40 ? WarningBrush : BatteryBrush;
            text.Text = isCharging ? $"{p}% AC" : $"{p}%";
        }

        private void ApplySettingsVisibility()
        {
            var visible = Visibility.Visible;
            var collapsed = Visibility.Collapsed;

            ExpandedCpuRow.Visibility = _settings.ShowCpu ? visible : collapsed;
            ExpandedRamRow.Visibility = _settings.ShowRam ? visible : collapsed;
            ExpandedDiskRow.Visibility = _settings.ShowDisk ? visible : collapsed;

            if (!_settings.ShowBattery)
            {
                ExpandedBatteryPanel.Visibility = collapsed;
            }
        }

        // Saat kursor mouse masuk ke area Notch (Mekar)
        private void ExpandNotch()
        {
            if (_isExpanded) return;
            _isExpanded = true;
            _animationVersion++;

            ApplyExpandedState(true);

            // Window langsung snap ke ukuran akhir (transparan, tak terlihat),
            // lalu pill-lah yang dianimasikan -> tanpa resize OS per frame
            var (w, h) = ExpandedSize;
            SetWindowSize(w, h);
            AnimatePillTo(w, h, widthFirst: true);
        }

        // Saat kursor mouse keluar dari area Notch (Mengecil)
        private void CollapseNotch()
        {
            if (!_isExpanded) return;
            _isExpanded = false;
            _animationVersion++;

            ApplyExpandedState(false);

            // Window baru menyusul setelah animasi pill selesai (lihat handler SizeChanged)
            var (w, h) = CollapsedSize;
            AnimatePillTo(w, h, widthFirst: false);
        }

        // Animasi dua tahap agar capsule tidak bergetar:
        // buka = lebar dulu lalu tinggi, tutup = tinggi dulu lalu lebar.
        private void AnimatePillTo(double targetWidth, double targetHeight, bool widthFirst)
        {
            int version = _animationVersion;

            double startWidth = PillHost.Width;
            double startHeight = PillHost.Height;
            if (double.IsNaN(startWidth) || startWidth <= 0) startWidth = PillHost.ActualWidth;
            if (double.IsNaN(startHeight) || startHeight <= 0) startHeight = PillHost.ActualHeight;

            // Bekukan posisi saat ini supaya hover cepat bolak-balik tetap mulus
            PillHost.BeginAnimation(FrameworkElement.WidthProperty, null);
            PillHost.BeginAnimation(FrameworkElement.HeightProperty, null);
            PillHost.Width = startWidth;
            PillHost.Height = startHeight;

            // Tutup: tinggi menyusut lebih dulu, lebar menyusul dengan sedikit
            // overlap. Keduanya dijadwalkan sekaligus agar tidak ada jeda frame
            // di antara tahap (penyebab animasi terasa patah).
            if (!widthFirst)
            {
                var heightAnim = new DoubleAnimation(
                    startHeight, targetHeight, new Duration(TimeSpan.FromSeconds(0.2)))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                var widthAnim = new DoubleAnimation(
                    startWidth, targetWidth, new Duration(TimeSpan.FromSeconds(0.2)))
                {
                    BeginTime = TimeSpan.FromSeconds(0.11),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                };

                PillHost.BeginAnimation(FrameworkElement.HeightProperty, heightAnim);
                PillHost.BeginAnimation(FrameworkElement.WidthProperty, widthAnim);
                return;
            }

            var firstProperty = widthFirst
                ? FrameworkElement.WidthProperty
                : FrameworkElement.HeightProperty;
            double firstStart = widthFirst ? startWidth : startHeight;
            double firstTarget = widthFirst ? targetWidth : targetHeight;

            if (Math.Abs(firstTarget - firstStart) < 0.5)
            {
                AnimatePillSecondPhase(targetWidth, targetHeight, widthFirst, version);
                return;
            }

            var firstAnim = new DoubleAnimation(firstStart, firstTarget, PillPhaseDuration)
            {
                EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut }
            };
            firstAnim.Completed += (s, e) =>
                AnimatePillSecondPhase(targetWidth, targetHeight, widthFirst, version);

            PillHost.BeginAnimation(firstProperty, firstAnim);
        }

        private void AnimatePillSecondPhase(
            double targetWidth, double targetHeight, bool widthFirst, int version)
        {
            if (version != _animationVersion) return;

            var property = widthFirst
                ? FrameworkElement.HeightProperty
                : FrameworkElement.WidthProperty;
            double target = widthFirst ? targetHeight : targetWidth;
            double current = widthFirst ? PillHost.Height : PillHost.Width;

            if (Math.Abs(target - current) < 0.5) return;

            var anim = new DoubleAnimation(current, target, PillPhaseDuration)
            {
                EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut }
            };

            PillHost.BeginAnimation(property, anim);
        }

        protected override void OnClosed(EventArgs e)
        {
            _mediaTimer.Stop();
            _timer.Stop();
            _systemStatus.StatusChanged -= OnSystemStatusChanged;
            _systemStatus.Dispose();
            _smtc.Dispose();
            base.OnClosed(e);
        }
    }
}
