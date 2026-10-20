using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
        private const double SideFeatureInset =
            (SideCollapsedWidth - FeatureButtonWidth) / 2;

        private const double FeatureButtonWidth = 30;
        private const double FeatureButtonTop = 5;
        private const double FeatureGap = 8;
        private const double FeatureDecorationWidth = FeatureGap + FeatureButtonWidth;
        private const double FeaturePanelOpenOffset = 8;
        private const double FeaturePanelStartOffset = -6;

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

        private static readonly SolidColorBrush AccentBrush = FrozenBrush(0x3D, 0x7E, 0xFF);
        private static readonly SolidColorBrush WarningBrush = FrozenBrush(0xFF, 0xB0, 0x20);
        private static readonly SolidColorBrush DangerBrush = FrozenBrush(0xFF, 0x45, 0x3A);
        private static readonly SolidColorBrush BatteryBrush = FrozenBrush(0x4C, 0xD9, 0x64);

        private static readonly Color IdleAccent = Color.FromRgb(0x6C, 0x7B, 0xFF);
        private static readonly Color DropAccent = Color.FromRgb(0x3D, 0x7E, 0xFF);
        private static readonly Color UnknownSourceColor = Color.FromRgb(0x5A, 0x5A, 0x5E);

        // Tepi yang menempel ke layar tetap hitam pekat; sisi luar memakai
        // gradasi violet-biru dengan opacity sedikit lebih rendah.
        private static readonly Color AttachedEdgeColor = Color.FromArgb(0xFF, 0x07, 0x07, 0x08);
        private static readonly Color BaseMidColor = Color.FromArgb(0xFF, 0x08, 0x08, 0x0B);
        private static readonly Color BaseTintColor = Color.FromArgb(0xF2, 0x0B, 0x09, 0x14);
        private static readonly Color BaseOuterColor = Color.FromArgb(0xE6, 0x11, 0x0E, 0x22);

        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
        private readonly DispatcherTimer _collapseTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
        private readonly DispatcherTimer _featureHoverTimer = new() { Interval = TimeSpan.FromMilliseconds(220) };
        private readonly DispatcherTimer _mediaTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        private readonly SmtcService _smtc = new();
        private readonly SystemStatusService _systemStatus = new();
        private AppSettings _settings = new();
        private MediaInfo? _media;
        private bool _isSeeking;
        private bool _isExpanded;
        private bool _isDragging;
        private bool _dragMoved;
        private bool _featurePanelOpen;
        private bool _keepNotchOpenForAux;
        private bool _isFileDragOver;
        private NoteWindow? _noteWindow;
        private SettingsWindow? _settingsWindow;
        private DropShelfWindow? _dropShelf;
        private readonly List<string> _droppedPaths = new();
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

        private bool IsSideAnchor => _anchor is NotchAnchor.LeftCenter or NotchAnchor.RightCenter;
        private bool IsNoteOpen => _noteWindow is { IsVisible: true };
        private bool IsSettingsOpen => _settingsWindow is { IsVisible: true };
        private bool IsShelfOpen => _dropShelf is { IsVisible: true };
        private bool IsAuxOpen => IsNoteOpen || IsSettingsOpen || IsShelfOpen;

        private (double Width, double Height) CollapsedSize => IsSideAnchor
            ? (SideCollapsedWidth, SideCollapsedHeight)
            : (CollapsedWidth, CollapsedHeight);

        private (double Width, double Height) ExpandedSize => IsSideAnchor
            ? (SideExpandedWidth, SideExpandedHeight)
            : (ExpandedWidth, ExpandedHeight);

        public MainWindow()
        {
            InitializeComponent();

            _settings = SettingsStore.Load();

            bool autoStartEnabled = StartupService.IsEnabled();
            if (autoStartEnabled)
            {
                StartupService.RefreshPathIfEnabled();
            }

            if (_settings.AutoStart != autoStartEnabled)
            {
                _settings.AutoStart = autoStartEnabled;
                SettingsStore.Save(_settings);
            }

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
                if (_isDragging || _featurePanelOpen || IsAuxOpen || _isFileDragOver) return;
                _collapseTimer.Stop();
                ExpandNotch();
            };
            PillHost.MouseLeave += (s, e) =>
            {
                if (_isDragging || _featurePanelOpen || IsAuxOpen || _isFileDragOver) return;
                _collapseTimer.Stop();
                _collapseTimer.Start();
            };
            _collapseTimer.Tick += (s, e) =>
            {
                _collapseTimer.Stop();
                if (!_isDragging && !_featurePanelOpen && !IsAuxOpen &&
                    !_isFileDragOver)
                {
                    CollapseNotch();
                }
            };
            PillHost.MouseRightButtonDown += (s, e) => Application.Current.Shutdown();

            // Kapsul fitur: menu terbuka saat kursor hover, tertutup saat kursor pergi
            FeatureButton.MouseEnter += (s, e) =>
            {
                _collapseTimer.Stop();
                _featureHoverTimer.Stop();
                OpenFeaturePanel();
            };
            FeatureButton.MouseLeave += (s, e) =>
            {
                _collapseTimer.Stop();
                _featureHoverTimer.Stop();
                _featureHoverTimer.Start();
            };

            // Beri jeda singkat agar kursor bisa menyeberang dari ikon ke panel
            FeaturePanelCard.MouseEnter += (s, e) => _featureHoverTimer.Stop();
            FeaturePanelCard.MouseLeave += (s, e) =>
            {
                _featureHoverTimer.Stop();
                _featureHoverTimer.Start();
            };
            _featureHoverTimer.Tick += (s, e) =>
            {
                _featureHoverTimer.Stop();
                if (FeatureButton.IsMouseOver || FeaturePanelCard.IsMouseOver) return;

                if (FeaturePopup.IsOpen) FeaturePopup.IsOpen = false;

                if (_isDragging || IsAuxOpen || _isFileDragOver) return;
                _collapseTimer.Stop();
                _collapseTimer.Start();
            };

            FeaturePopup.Opened += OnFeaturePopupOpened;
            FeaturePopup.Closed += OnFeaturePopupClosed;
            FileButton.Click += OnFileButtonClick;
            NoteButton.Click += OnNoteButtonClick;
            SettingsButton.Click += OnSettingsButtonClick;
            ExitButton.Click += (s, e) => Application.Current.Shutdown();

            PillHost.DragEnter += OnPillDragEnter;
            PillHost.DragOver += OnPillDragOver;
            PillHost.DragLeave += OnPillDragLeave;
            PillHost.Drop += OnPillDrop;

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
                UpdateFeatureButtonPosition(e.NewSize.Width, e.NewSize.Height);

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

        private double FeaturePanelDirection =>
            _anchor == NotchAnchor.RightCenter ? -1 : 1;

        private RotateTransform? FeatureIconRotate =>
            FeatureButton.Template?.FindName("IconRotate", FeatureButton) as RotateTransform;

        // Ikon berputar penuh ke kanan saat menu dibuka, dan kembali ke kiri saat ditutup
        private void AnimateFeatureIcon(bool open)
        {
            if (FeatureIconRotate is not RotateTransform rotate) return;

            double from = rotate.Angle;
            double to = open ? 360 : 0;
            if (Math.Abs(to - from) < 0.01) return;

            rotate.BeginAnimation(RotateTransform.AngleProperty, null);

            rotate.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(from, to, new Duration(TimeSpan.FromSeconds(0.18)))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
        }

        private void OpenFeaturePanel()
        {
            if (_featurePanelOpen) return;

            _featurePanelOpen = true;
            _collapseTimer.Stop();

            AnimateFeatureIcon(open: true);

            // Saat notch menempel kanan layar, panel dibuka ke kiri agar tetap terlihat
            FeaturePopup.Placement = _anchor == NotchAnchor.RightCenter
                ? PlacementMode.Left
                : PlacementMode.Right;

            FeaturePopup.BeginAnimation(Popup.HorizontalOffsetProperty, null);
            FeaturePopup.HorizontalOffset = FeaturePanelDirection * FeaturePanelStartOffset;
            FeaturePanelCard.BeginAnimation(UIElement.OpacityProperty, null);
            FeaturePanelCard.Opacity = 0;

            FeaturePopup.IsOpen = true;
        }

        private void OnFeaturePopupOpened(object? sender, EventArgs e)
        {
            var easing = new QuinticEase { EasingMode = EasingMode.EaseOut };
            var duration = new Duration(TimeSpan.FromSeconds(0.2));

            // Slide dari balik ikon + fade-in
            FeaturePopup.BeginAnimation(Popup.HorizontalOffsetProperty,
                new DoubleAnimation(FeaturePanelDirection * FeaturePanelOpenOffset, duration)
                {
                    EasingFunction = easing
                });
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

            AnimateFeatureIcon(open: false);

            FeaturePopup.BeginAnimation(Popup.HorizontalOffsetProperty, null);
            FeaturePopup.HorizontalOffset = FeaturePanelDirection * FeaturePanelOpenOffset;
            FeaturePanelCard.BeginAnimation(UIElement.OpacityProperty, null);
            FeaturePanelCard.Opacity = 1;

            if (_keepNotchOpenForAux)
            {
                _keepNotchOpenForAux = false;
                return;
            }

            if (IsAuxOpen) return;
            if (_isDragging || _isFileDragOver) return;
            if (PillHost.IsMouseOver || FeatureButton.IsMouseOver) return;

            _collapseTimer.Stop();
            CollapseNotch();
        }

        private void OnFileButtonClick(object sender, RoutedEventArgs e)
        {
            _keepNotchOpenForAux = true;
            FeaturePopup.IsOpen = false;
            ShowFileShelf();
        }

        private void OnNoteButtonClick(object sender, RoutedEventArgs e)
        {
            _keepNotchOpenForAux = true;
            FeaturePopup.IsOpen = false;
            ShowNoteWindow();
        }

        private void OnSettingsButtonClick(object sender, RoutedEventArgs e)
        {
            _keepNotchOpenForAux = true;
            FeaturePopup.IsOpen = false;
            ShowSettingsWindow();
        }

        private void ShowNoteWindow()
        {
            if (_noteWindow is { IsVisible: true })
            {
                _noteWindow.Activate();
                return;
            }

            var note = new NoteWindow();
            PositionAuxWindow(note);

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

        private void ShowSettingsWindow()
        {
            if (_settingsWindow is { IsVisible: true })
            {
                _settingsWindow.Activate();
                return;
            }

            var settings = new SettingsWindow(_settings);
            PositionAuxWindow(settings);

            settings.SettingsChanged += (s, e) => ApplySettingsVisibility();

            settings.Closed += (s, e) =>
            {
                _settingsWindow = null;

                if (settings.ClosedByUser && !IsSideAnchor)
                {
                    OpenFeaturePanel();
                    return;
                }

                if (PillHost.IsMouseOver || FeatureButton.IsMouseOver) return;

                _collapseTimer.Stop();
                CollapseNotch();
            };

            _settingsWindow = settings;
            settings.Show();
            settings.Activate();
        }

        private void PositionAuxWindow(Window window)
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
            if (left + window.Width > work.Right - gap)
            {
                left = Left + FeatureButton.Margin.Left - window.Width - gap;
            }

            left = Math.Clamp(left,
                work.Left + gap,
                Math.Max(work.Left + gap, work.Right - window.Width - gap));
            top = Math.Clamp(top,
                work.Top + gap,
                Math.Max(work.Top + gap, work.Bottom - window.Height - gap));

            window.Left = left;
            window.Top = top;
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
                UpdateAccent();
                return;
            }

            _systemStatus.Stop();

            TrackTitle.Text = info.Title;
            TrackArtist.Text = info.Artist;
            SetAlbumArt(info.AlbumArt);

            ApplySourceApp(info.SourceAppId);
            UpdatePlayPauseGlyph(info.IsPlaying);

            UpdateVisualState();
            UpdateAccent();

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

        // Ikon aplikasi sumber: logo platform bila dikenal, fallback ke glyph musik.
        private void ApplySourceApp(string aumid)
        {
            string? logoKey = TryGetSourceLogoKey(aumid);

            if (logoKey != null && TryFindResource(logoKey) is ImageSource logo)
            {
                SourceAppLogo.Source = logo;
                SourceAppLogo.Visibility = Visibility.Visible;
                SourceAppGlyph.Visibility = Visibility.Collapsed;
                SourceAppIcon.Background = Brushes.Transparent;
                return;
            }

            Color color = GetSourceColor(aumid) ?? UnknownSourceColor;

            SourceAppLogo.Source = null;
            SourceAppLogo.Visibility = Visibility.Collapsed;
            SourceAppGlyph.Visibility = Visibility.Visible;
            SourceAppIcon.Background = FrozenBrush(color);
        }

        private static string? TryGetSourceLogoKey(string aumid)
        {
            var id = (aumid ?? string.Empty).ToLowerInvariant();

            if (id.Contains("spotify")) return "SpotifyLogoImage";
            if (id.Contains("chrome")) return "ChromeLogoImage";
            if (id.Contains("youtube")) return "YouTubeLogoImage";

            return null;
        }

        private static Color? GetSourceColor(string aumid)
        {
            var id = (aumid ?? string.Empty).ToLowerInvariant();

            if (id.Contains("spotify")) return Color.FromRgb(0x1D, 0xB9, 0x54);
            if (id.Contains("chrome")) return Color.FromRgb(0x42, 0x85, 0xF4);
            if (id.Contains("msedge") || id.Contains("edge")) return Color.FromRgb(0x00, 0x78, 0xD4);
            if (id.Contains("firefox")) return Color.FromRgb(0xFF, 0x71, 0x39);
            if (id.Contains("youtube")) return Color.FromRgb(0xFF, 0x00, 0x00);
            if (id.Contains("foobar")) return Color.FromRgb(0x2B, 0x2B, 0x2B);

            return null;
        }

        // Warna aksen mengikuti state: drop > media > idle.
        private void UpdateAccent()
        {
            Color accent = _isFileDragOver
                ? DropAccent
                : _media == null
                    ? IdleAccent
                    : GetSourceColor(_media.SourceAppId) ?? IdleAccent;

            NotchAccent.Fill = CreateAccentBrush(accent);
        }

        // Arah gradasi mengikuti sisi yang menempel ke layar.
        private void UpdateSurface()
        {
            NotchShape.Fill = CreateBaseBrush();
            NotchSheen.Fill = CreateSheenBrush();

            Brush attachedEdgeMask = CreateAttachedEdgeMask();
            NotchOuterBorder.OpacityMask = attachedEdgeMask;
            NotchBorder.OpacityMask = attachedEdgeMask;
        }

        private (Point Start, Point End) EdgeGradientPoints => _anchor switch
        {
            NotchAnchor.LeftCenter => (new Point(0, 0.5), new Point(1, 0.5)),
            NotchAnchor.RightCenter => (new Point(1, 0.5), new Point(0, 0.5)),
            _ => (new Point(0, 0), new Point(0, 1))
        };

        private Brush CreateBaseBrush()
        {
            (Point start, Point end) = EdgeGradientPoints;

            var brush = new LinearGradientBrush
            {
                StartPoint = start,
                EndPoint = end
            };

            brush.GradientStops.Add(new GradientStop(AttachedEdgeColor, 0));
            brush.GradientStops.Add(new GradientStop(BaseMidColor, 0.45));
            brush.GradientStops.Add(new GradientStop(BaseTintColor, 0.75));
            brush.GradientStops.Add(new GradientStop(BaseOuterColor, 1));

            brush.Freeze();
            return brush;
        }

        private Brush CreateSheenBrush()
        {
            (Point start, Point end) = EdgeGradientPoints;

            var brush = new LinearGradientBrush
            {
                StartPoint = start,
                EndPoint = end
            };

            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 255, 255, 255), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 255, 255, 255), 0.55));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x06, 255, 255, 255), 1));

            brush.Freeze();
            return brush;
        }

        // Menghilangkan stroke tepat di sisi yang menempel ke layar.
        private Brush CreateAttachedEdgeMask()
        {
            (Point start, Point end) = EdgeGradientPoints;

            var brush = new LinearGradientBrush
            {
                StartPoint = start,
                EndPoint = end
            };

            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 255, 255, 255), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 255, 255, 255), 0.03));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 255, 255, 255), 0.12));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 255, 255, 255), 1));

            brush.Freeze();
            return brush;
        }

        private Brush CreateAccentBrush(Color accent)
        {
            Point center = _anchor switch
            {
                NotchAnchor.LeftCenter => new Point(1, 0.5),
                NotchAnchor.RightCenter => new Point(0, 0.5),
                _ => new Point(0.82, 0.5)
            };

            var brush = new RadialGradientBrush
            {
                Center = center,
                GradientOrigin = center,
                RadiusX = IsSideAnchor ? 0.95 : 0.72,
                RadiusY = 0.95,
                MappingMode = BrushMappingMode.RelativeToBoundingBox
            };

            brush.GradientStops.Add(new GradientStop(
                Color.FromArgb(0x38, accent.R, accent.G, accent.B), 0));
            brush.GradientStops.Add(new GradientStop(
                Color.FromArgb(0x1C, accent.R, accent.G, accent.B), 0.45));
            brush.GradientStops.Add(new GradientStop(
                Color.FromArgb(0x08, accent.R, accent.G, accent.B), 0.75));
            brush.GradientStops.Add(new GradientStop(
                Color.FromArgb(0x00, accent.R, accent.G, accent.B), 1));

            brush.Freeze();
            return brush;
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

                    if (_anchor == NotchAnchor.LeftCenter)
                    {
                        // Menempel kiri: sudut kanan membulat
                        ctx.BeginFigure(new Point(0, 0), isFilled: true, isClosed: true);
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
                        // Menempel kanan: sudut kiri membulat. Mulai dari ujung
                        // lengkung, bukan (0,0), agar tidak ada garis penutup
                        // yang menyeberangi sudut kiri.
                        ctx.BeginFigure(new Point(r, 0), isFilled: true, isClosed: true);
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
            PillHost.VerticalAlignment = VerticalAlignment.Top;

            PillHost.Width = w;
            PillHost.Height = h;

            UpdateContentMargin();
            SetWindowSize(w, h);
            UpdateFeatureButtonPosition(w, h);
            UpdateNotchShape(new Size(w, h));
            UpdateSurface();
            UpdateVisualState();
            UpdateAccent();
        }

        private void UpdateContentMargin()
        {
            ContentRoot.Margin = IsSideAnchor && !_isExpanded
                ? new Thickness(0)
                : new Thickness(16, 0, 16, 0);
        }

        // Kapsul ikon tampil di semua mode (atas & sisi), hanya disembunyikan saat digeser
        private void UpdateFeatureButtonVisibility()
        {
            bool visible = !_isDragging;
            FeatureButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

            if (!visible && FeaturePopup.IsOpen)
            {
                FeaturePopup.IsOpen = false;
            }
        }

        // Mode atas: kapsul di kanan tepi pill.
        // Mode sisi: kapsul di tengah bawah pill.
        private void UpdateFeatureButtonPosition(double pillWidth, double pillHeight)
        {
            if (IsSideAnchor)
            {
                // Mode sisi: ikon menempel ke tepi layar. Untuk sisi kanan gunakan
                // lebar window, bukan lebar pill yang sedang dianimasikan, supaya
                // ikon tidak ikut bergeser saat collapse.
                double windowWidth = Width > 0 && !double.IsNaN(Width)
                    ? Width
                    : ActualWidth;
                double left = _anchor == NotchAnchor.RightCenter
                    ? Math.Max(0, windowWidth - FeatureButtonWidth - SideFeatureInset)
                    : SideFeatureInset;
                double top = pillHeight + FeatureGap;
                FeatureButton.Margin = new Thickness(left, top, 0, 0);
                return;
            }

            double rightLeft = (Width + pillWidth) / 2 + FeatureGap;
            FeatureButton.Margin = new Thickness(rightLeft, FeatureButtonTop, 0, 0);
        }

        // Snap instan ukuran window (dipakai saat expand & setelah collapse selesai).
        // Mode atas-tengah menyisakan ruang di kanan untuk kapsul ikon; pill tetap
        // di tengah layar karena window juga di-center.
        // Mode sisi menyisakan ruang di bawah pill untuk kapsul ikon.
        private void SetWindowSize(double targetWidth, double targetHeight)
        {
            double windowWidth = targetWidth + (IsSideAnchor ? 0 : 2 * FeatureDecorationWidth);
            double windowHeight = targetHeight +
                (IsSideAnchor ? FeatureGap + FeatureButtonWidth : 0);

            Width = windowWidth;
            Height = windowHeight;

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

            UpdateFeatureButtonPosition(targetWidth, targetHeight);
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

        private void OnPillDragEnter(object sender, DragEventArgs e)
        {
            if (!HasFileDrop(e.Data))
            {
                e.Effects = DragDropEffects.None;
                return;
            }

            _isFileDragOver = true;
            _collapseTimer.Stop();
            DropIndicator.Visibility = Visibility.Visible;
            ExpandNotch();
            UpdateAccent();

            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }

        private void OnPillDragOver(object sender, DragEventArgs e)
        {
            if (!HasFileDrop(e.Data))
            {
                e.Effects = DragDropEffects.None;
                return;
            }

            if (!_isFileDragOver)
            {
                _isFileDragOver = true;
                _collapseTimer.Stop();
                DropIndicator.Visibility = Visibility.Visible;
                UpdateAccent();
            }

            ExpandNotch();

            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }

        private void OnPillDragLeave(object sender, DragEventArgs e)
        {
            if (!_isFileDragOver) return;

            _isFileDragOver = false;
            DropIndicator.Visibility = Visibility.Collapsed;
            UpdateAccent();

            if (IsAuxOpen || _isDragging) return;
            if (PillHost.IsMouseOver || FeatureButton.IsMouseOver) return;

            _collapseTimer.Stop();
            _collapseTimer.Start();
        }

        private void OnPillDrop(object sender, DragEventArgs e)
        {
            if (!TryGetDroppedPaths(e.Data, out var paths))
            {
                e.Effects = DragDropEffects.None;
                return;
            }

            _isFileDragOver = false;
            DropIndicator.Visibility = Visibility.Collapsed;
            UpdateAccent();
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;

            ShowDropShelf(paths);
        }

        private static bool HasFileDrop(IDataObject data)
        {
            return data.GetDataPresent(DataFormats.FileDrop);
        }

        private static bool TryGetDroppedPaths(IDataObject data, out string[] paths)
        {
            paths = Array.Empty<string>();

            if (!data.GetDataPresent(DataFormats.FileDrop)) return false;
            if (data.GetData(DataFormats.FileDrop) is not string[] dropped ||
                dropped.Length == 0)
            {
                return false;
            }

            paths = dropped;
            return true;
        }

        private void ShowFileShelf()
        {
            if (_dropShelf is { IsVisible: true })
            {
                _dropShelf.Activate();
                return;
            }

            CreateDropShelf(_droppedPaths);
        }

        private void ShowDropShelf(string[] paths)
        {
            string[] validPaths = paths
                .Where(path => File.Exists(path) || Directory.Exists(path))
                .ToArray();

            if (validPaths.Length == 0) return;

            foreach (string path in validPaths)
            {
                if (_droppedPaths.Count >= DropShelfWindow.MaxItems) break;
                if (_droppedPaths.Any(existing =>
                        string.Equals(existing, path,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                _droppedPaths.Add(path);
            }

            if (_dropShelf is { IsVisible: true })
            {
                _dropShelf.AddPaths(validPaths);
                SyncDroppedPathsFromShelf(_dropShelf);
                PositionAuxWindow(_dropShelf);
                _dropShelf.Activate();
                return;
            }

            CreateDropShelf(_droppedPaths);
        }

        private void CreateDropShelf(IEnumerable<string> paths)
        {
            var shelf = new DropShelfWindow(paths);
            PositionAuxWindow(shelf);

            shelf.ItemsChanged += (s, e) => SyncDroppedPathsFromShelf(shelf);
            shelf.Closed += (s, e) =>
            {
                _dropShelf = null;

                if (PillHost.IsMouseOver || FeatureButton.IsMouseOver) return;

                _collapseTimer.Stop();
                CollapseNotch();
            };

            _dropShelf = shelf;
            shelf.Show();
            shelf.Activate();
            SyncDroppedPathsFromShelf(shelf);
        }

        private void SyncDroppedPathsFromShelf(DropShelfWindow shelf)
        {
            _droppedPaths.Clear();
            _droppedPaths.AddRange(shelf.Paths);
            UpdateFileMenuLabel();
        }

        private void UpdateFileMenuLabel()
        {
            FileButton.IsEnabled = _droppedPaths.Count > 0;
            FileMenuLabel.Text = _droppedPaths.Count > 0
                ? $"File ({_droppedPaths.Count})"
                : "File";
        }

        private void SaveAnchor()
        {
            _settings.Anchor = _anchor.ToString();
            SettingsStore.Save(_settings);
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
            return FrozenBrush(Color.FromRgb(r, g, b));
        }

        private static SolidColorBrush FrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
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
